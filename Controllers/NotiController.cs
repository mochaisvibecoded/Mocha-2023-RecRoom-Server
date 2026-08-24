using Mocha2023.Auth;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs;
using Mocha2023.Classes.DBs.DBClasses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using SystemJsonSerializer = System.Text.Json.JsonSerializer;
using static Mocha2023.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Mocha2023.Controllers
{

    public sealed class NotificationHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            HttpRequest? request = Context.GetHttpContext()?.Request;
            long? playerId = request == null
                ? null
                : AuthStuff.GetPlayerId(request);

            if (!playerId.HasValue)
            {
                Console.WriteLine(
                    $"[SignalR] Rejected unauthenticated connection {Context.ConnectionId}");
                Context.Abort();
                return;
            }

            bool becameOnline = NotiController.RegisterConnection(
                Context.ConnectionId,
                playerId.Value);

            NotiController.RegisterHubContext(Context.ConnectionId, Context);

            NotiController.EnsurePersistentSubscriptions(
                Context.ConnectionId,
                playerId.Value);

            Console.WriteLine(
                $"[SignalR] Player {playerId.Value} connected as {Context.ConnectionId}");

            await base.OnConnectedAsync();

            await Clients.Caller.SendAsync("OnConnect");
            Console.WriteLine(
                $"[SignalR] Player {playerId.Value} OnConnect sent");

            await NotiController.DeliverPendingNotificationsAsync(playerId.Value);
            await NotiController.NotifyAnnouncementsUpdatedAsync(playerId.Value);
            await NotiController.PrimeConnectionAsync(
                Context.ConnectionId,
                playerId.Value);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            bool found = NotiController.UnregisterConnection(
                Context.ConnectionId,
                out long playerId,
                out bool becameOffline);

            NotiController.UnregisterHubContext(Context.ConnectionId);

            await base.OnDisconnectedAsync(exception);

            if (!found)
                return;

            Console.WriteLine(
                $"[SignalR] Player {playerId} disconnected from {Context.ConnectionId}" +
                (exception == null ? string.Empty : $": {exception.Message}"));

        }

        public async Task<string> SubscribeToPlayers(
            PlayerSubscriptionListDTO? subscriptionList)
        {
            List<int> requestedPlayerIds =
                subscriptionList?.PlayerIds ?? new List<int>();

            List<long> newlySubscribedPlayerIds = NotiController.ReplacePlayerSubscriptions(
                Context.ConnectionId,
                requestedPlayerIds);

            HttpRequest? request = Context.GetHttpContext()?.Request;
            long? ownerPlayerId = request == null
                ? null
                : AuthStuff.GetPlayerId(request);

            if (ownerPlayerId.HasValue && newlySubscribedPlayerIds.Count > 0)
            {
                await NotiController.PrimePlayerSubscriptionsAsync(
                    Context.ConnectionId,
                    ownerPlayerId.Value,
                    newlySubscribedPlayerIds.Select(id => (int)id));
            }

            return "200 OK";
        }

        public Task<string> SubscribeToPlayer(
            PlayerSubscriptionListDTO? subscriptionList) =>
            SubscribeToPlayers(subscriptionList);

        public Task<string> UnsubscribeFromPlayers(
            PlayerSubscriptionListDTO? subscriptionList)
        {
            NotiController.RemovePlayerSubscriptions(
                Context.ConnectionId,
                subscriptionList?.PlayerIds ?? new List<int>());

            return Task.FromResult("200 OK");
        }

        public Task<string> UnsubscribeFromPlayer(
            PlayerSubscriptionListDTO? subscriptionList) =>
            UnsubscribeFromPlayers(subscriptionList);

        public Task<string> SubscribeToChannels(
            ChannelSubscriptionListDTO? subscriptionList)
        {
            NotiController.ReplaceChannelSubscriptions(
                Context.ConnectionId,
                subscriptionList?.Channels ?? new List<string>());

            return Task.FromResult("200 OK");
        }

        public Task<string> UnsubscribeFromChannels(
            ChannelSubscriptionListDTO? subscriptionList)
        {
            NotiController.RemoveChannelSubscriptions(
                Context.ConnectionId,
                subscriptionList?.Channels ?? new List<string>());

            return Task.FromResult("200 OK");
        }

        public Task<string> SubscribeTo(
            ChannelSubscriptionListDTO? subscriptionList) =>
            SubscribeToChannels(subscriptionList);

        public Task<string> UnsubscribeFrom(
            ChannelSubscriptionListDTO? subscriptionList) =>
            UnsubscribeFromChannels(subscriptionList);
    }

    public sealed class PlayerSubscriptionListDTO
    {
        public List<int> PlayerIds { get; set; } = new();
    }

    public sealed class ChannelSubscriptionListDTO
    {
        public List<string> Channels { get; set; } = new();
    }

    public static class NotiController
    {
        public sealed class GiftLiveDelivery
        {
            public long ReceiverPlayerId { get; }
            public object? Gift { get; }

            public GiftLiveDelivery(
                long receiverPlayerId,
                object? gift)
            {
                ReceiverPlayerId = receiverPlayerId;
                Gift = gift;
            }
        }

        public sealed class GiftLiveDeliveryResult
        {
            public int TargetPlayers { get; set; }
            public int LivePlayers { get; set; }
            public int LiveSockets { get; set; }
            public int OfflinePlayers { get; set; }
        }

        private sealed class PreparedGiftLiveDelivery
        {
            public long SenderPlayerId { get; set; }
            public long ReceiverPlayerId { get; set; }
            public object Payload { get; set; } = new object();
            public long GiftPackageId { get; set; }
            public HashSet<string> PrimedConnectionIds { get; } =
                new(StringComparer.Ordinal);
        }

        private const int MaxPlayerSubscriptionsPerConnection = 250;

        private const string ClientNotificationHubTarget = "Notification";
        private const string PresenceUpdateNotificationId = "PresenceUpdate";
        private const string RoomUpdateNotificationId = "RoomUpdate";
        private const string AccountUpdateNotificationId = "AccountUpdate";
        private const string SelfAccountUpdateNotificationId = "SelfAccountUpdate";

        private const string RelationshipChangedTarget = "1";
        private const string MessageReceivedTarget = "2";
        private const string SubscriptionUpdatePresenceTarget = "12";
        private const string ServerMaintenanceTarget = "25";
        private const string GiftPackageReceivedTarget = "30";
        private const string GiftPackageReceivedImmediateTarget = "31";
        private const string ConsumableMappingRemovedTarget = "71";
        private const string GiftConsumedTarget = "GiftConsumed";
        private const string RelationshipsInvalidTarget = "50";
        private const string ChatMessageReceivedTarget = "ChatMessageReceived";
        private const string CommunityBoardUpdateTarget = "CommunityBoardUpdate";
        private const string CommunityBoardAnnouncementUpdateTarget = "96";
        private const string FreeGiftButtonItemsAddedTarget = "110";

        private static readonly IReadOnlyDictionary<string, string> PushTargetNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RelationshipChangedTarget] = "RelationshipChanged",
                [MessageReceivedTarget] = "MessageReceived",
                [SubscriptionUpdatePresenceTarget] = "SubscriptionUpdatePresence",
                [ServerMaintenanceTarget] = "ServerMaintenance",
                [GiftPackageReceivedTarget] = "GiftPackageReceived",
                [GiftPackageReceivedImmediateTarget] = "GiftPackageReceivedImmediate",
                [ConsumableMappingRemovedTarget] = "ConsumableMappingRemoved",
                [GiftConsumedTarget] = "GiftConsumed",
                [RelationshipsInvalidTarget] = "RelationshipsInvalid",
                [ChatMessageReceivedTarget] = "ChatMessageReceived",
                [CommunityBoardUpdateTarget] = "CommunityBoardUpdate",
                [CommunityBoardAnnouncementUpdateTarget] = "CommunityBoardAnnouncementUpdate",
                [FreeGiftButtonItemsAddedTarget] = "FreeGiftButtonItemsAdded"
            };

        private static IHubContext<NotificationHub>? _hubContext;
        private static FileSystemWatcher? _announcementWatcher;
        private static int _announcementRefreshPending;

        private static readonly ConcurrentDictionary<string, long>
            ConnectionPlayers = new(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<long, ConcurrentDictionary<string, byte>>
            PlayerConnections = new();

        private static readonly ConcurrentDictionary<string, HubCallerContext>
            ActiveHubContexts = new(StringComparer.Ordinal);

        internal static void RegisterHubContext(string connectionId, HubCallerContext context) =>
            ActiveHubContexts[connectionId] = context;

        internal static void UnregisterHubContext(string connectionId) =>
            ActiveHubContexts.TryRemove(connectionId, out _);

        internal static int ForceDisconnectPlayer(long playerId)
        {
            if (!PlayerConnections.TryGetValue(playerId, out var sockets))
                return 0;

            int aborted = 0;
            foreach (string connectionId in sockets.Keys.ToList())
            {
                if (ActiveHubContexts.TryGetValue(connectionId, out HubCallerContext? context))
                {
                    try
                    {
                        context.Abort();
                        aborted++;
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine(
                            $"[SignalR] Failed aborting {connectionId} for player {playerId}: {exception.Message}");
                    }
                }
            }

            return aborted;
        }

        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<long, byte>>
            ConnectionPlayerSubscriptions = new(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<long, ConcurrentDictionary<string, byte>>
            PlayerSubscribers = new();

        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>
            ConnectionChannelSubscriptions = new(StringComparer.Ordinal);

        public static void Initialize(IHubContext<NotificationHub> hubContext)
        {
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            InitializeAnnouncementWatcher();
            Console.WriteLine("[SignalR] Notification hub broadcaster initialized");
        }

        internal static int ConnectedSocketCount => ConnectionPlayers.Count;

        internal static int GetPlayerSocketCount(long playerId) =>
            PlayerConnections.TryGetValue(playerId, out var connections)
                ? connections.Count
                : 0;

        private static void InitializeAnnouncementWatcher()
        {
            try
            {
                Directory.CreateDirectory(Program.dataDir);

                _announcementWatcher?.Dispose();
                _announcementWatcher = new FileSystemWatcher(
                    Program.dataDir,
                    "announcements.json")
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.CreationTime |
                                   NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                _announcementWatcher.Changed += (_, _) =>
                    QueueAnnouncementBroadcast();
                _announcementWatcher.Created += (_, _) =>
                    QueueAnnouncementBroadcast();
                _announcementWatcher.Deleted += (_, _) =>
                    QueueAnnouncementBroadcast();
                _announcementWatcher.Renamed += (_, _) =>
                    QueueAnnouncementBroadcast();

                Console.WriteLine(
                    $"[Announcements] Watching {Path.Combine(Program.dataDir, "announcements.json")}");
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[Announcements] File watcher disabled: {exception.Message}");
            }
        }

        private static void QueueAnnouncementBroadcast()
        {
            if (Interlocked.Exchange(ref _announcementRefreshPending, 1) != 0)
                return;

            _ = Task.Run(async () =>
            {
                try
                {

                    await Task.Delay(300);
                    await BroadcastAnnouncementsUpdatedAsync();
                }
                catch (Exception exception)
                {
                    Console.WriteLine(
                        $"[Announcements] Live refresh failed: {exception.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _announcementRefreshPending, 0);
                }
            });
        }

        internal static bool RegisterConnection(string connectionId, long playerId)
        {

            if (ConnectionPlayers.TryGetValue(connectionId, out long oldPlayerId))
            {
                UnregisterConnection(
                    connectionId,
                    out _,
                    out _);

                Console.WriteLine(
                    $"[SignalR] Replaced reused connection {connectionId} " +
                    $"from player {oldPlayerId} with player {playerId}");
            }

            ConnectionPlayers[connectionId] = playerId;

            ConcurrentDictionary<string, byte> sockets = PlayerConnections
                .GetOrAdd(playerId, _ => new ConcurrentDictionary<string, byte>(
                    StringComparer.Ordinal));

            bool wasOffline = sockets.IsEmpty;
            sockets[connectionId] = 0;

            ConnectionPlayerSubscriptions.TryAdd(
                connectionId,
                new ConcurrentDictionary<long, byte>());

            ConnectionChannelSubscriptions.TryAdd(
                connectionId,
                new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));

            return wasOffline;
        }

        internal static bool UnregisterConnection(
            string connectionId,
            out long playerId,
            out bool becameOffline)
        {
            becameOffline = false;

            if (!ConnectionPlayers.TryRemove(connectionId, out playerId))
                return false;

            if (PlayerConnections.TryGetValue(playerId, out var sockets))
            {
                sockets.TryRemove(connectionId, out _);
                if (sockets.IsEmpty)
                {
                    PlayerConnections.TryRemove(playerId, out _);
                    becameOffline = true;
                }
            }

            if (ConnectionPlayerSubscriptions.TryRemove(
                    connectionId,
                    out var subscribedPlayers))
            {
                foreach (long subscribedPlayerId in subscribedPlayers.Keys)
                    RemoveSubscriberIndex(connectionId, subscribedPlayerId);
            }

            ConnectionChannelSubscriptions.TryRemove(connectionId, out _);
            return true;
        }

        // Returns only the subject IDs that were newly added by this call -
        // the client re-sends its full subscription list on every call
        // (observed multiple times per second per connection), so priming
        // the whole requested list unconditionally on every call turned
        // every one of those into a full profile+presence re-broadcast for
        // everyone already subscribed, flooding the hub with redundant
        // traffic instead of just the players that are actually new.
        internal static List<long> ReplacePlayerSubscriptions(
            string connectionId,
            IEnumerable<int> requestedPlayerIds)
        {
            if (!ConnectionPlayers.TryGetValue(
                    connectionId,
                    out long ownerPlayerId))
            {
                return new List<long>();
            }

            ConcurrentDictionary<long, byte> current =
                ConnectionPlayerSubscriptions.GetOrAdd(
                    connectionId,
                    _ => new ConcurrentDictionary<long, byte>());

            long[] requested = requestedPlayerIds
                .Where(id => id > 0)
                .Select(id => (long)id)
                .Concat(RelationshipDB.GetPersistentLiveSubjectIds(ownerPlayerId))
                .Where(id => id != ownerPlayerId)
                .Distinct()
                .Take(MaxPlayerSubscriptionsPerConnection)
                .ToArray();

            var newlyAdded = new List<long>();
            foreach (long addedPlayerId in requested)
            {
                if (!current.TryAdd(addedPlayerId, 0))
                    continue;

                newlyAdded.Add(addedPlayerId);
                PlayerSubscribers
                    .GetOrAdd(
                        addedPlayerId,
                        _ => new ConcurrentDictionary<string, byte>(
                            StringComparer.Ordinal))
                    [connectionId] = 0;
            }

            if (newlyAdded.Count > 0)
            {
                Console.WriteLine(
                    $"[SignalR] Player {ownerPlayerId} subscriptions=" +
                    string.Join(',', current.Keys.OrderBy(id => id)));
            }

            return newlyAdded;
        }

        internal static void RemovePlayerSubscriptions(
            string connectionId,
            IEnumerable<int> playerIds)
        {
            if (!ConnectionPlayerSubscriptions.TryGetValue(
                    connectionId,
                    out var current))
            {
                return;
            }

            foreach (long playerId in playerIds
                         .Where(id => id > 0)
                         .Select(id => (long)id)
                         .Distinct())
            {
                current.TryRemove(playerId, out _);
                RemoveSubscriberIndex(connectionId, playerId);
            }
        }

        internal static void ReplaceChannelSubscriptions(
            string connectionId,
            IEnumerable<string> channels)
        {
            ConcurrentDictionary<string, byte> current =
                ConnectionChannelSubscriptions.GetOrAdd(
                    connectionId,
                    _ => new ConcurrentDictionary<string, byte>(
                        StringComparer.Ordinal));

            HashSet<string> desired = channels
                .Where(channel => !string.IsNullOrWhiteSpace(channel))
                .Select(channel => channel.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(250)
                .ToHashSet(StringComparer.Ordinal);

            foreach (string removed in current.Keys.Except(
                         desired,
                         StringComparer.Ordinal))
            {
                current.TryRemove(removed, out _);
            }

            foreach (string added in desired.Except(
                         current.Keys,
                         StringComparer.Ordinal))
            {
                current[added] = 0;
            }
        }

        internal static void RemoveChannelSubscriptions(
            string connectionId,
            IEnumerable<string> channels)
        {
            if (!ConnectionChannelSubscriptions.TryGetValue(
                    connectionId,
                    out var current))
            {
                return;
            }

            foreach (string channel in channels
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Select(value => value.Trim())
                         .Distinct(StringComparer.Ordinal))
            {
                current.TryRemove(channel, out _);
            }
        }

        private static void RemoveSubscriberIndex(
            string connectionId,
            long subscribedPlayerId)
        {
            if (!PlayerSubscribers.TryGetValue(
                    subscribedPlayerId,
                    out var subscribers))
            {
                return;
            }

            subscribers.TryRemove(connectionId, out _);
            if (subscribers.IsEmpty)
                PlayerSubscribers.TryRemove(subscribedPlayerId, out _);
        }

        internal static void EnsurePersistentSubscriptions(
            string connectionId,
            long ownerPlayerId)
        {
            if (ownerPlayerId <= 0 ||
                !ConnectionPlayers.ContainsKey(connectionId))
            {
                return;
            }

            ConcurrentDictionary<long, byte> current =
                ConnectionPlayerSubscriptions.GetOrAdd(
                    connectionId,
                    _ => new ConcurrentDictionary<long, byte>());

            foreach (long subjectPlayerId in
                     RelationshipDB.GetPersistentLiveSubjectIds(ownerPlayerId)
                         .Take(MaxPlayerSubscriptionsPerConnection))
            {
                if (subjectPlayerId <= 0 ||
                    subjectPlayerId == ownerPlayerId ||
                    !current.TryAdd(subjectPlayerId, 0))
                {
                    continue;
                }

                PlayerSubscribers
                    .GetOrAdd(
                        subjectPlayerId,
                        _ => new ConcurrentDictionary<string, byte>(
                            StringComparer.Ordinal))
                    [connectionId] = 0;
            }
        }

        private static void RefreshPersistentSubscriptionsForPlayer(
            long playerId)
        {
            if (!PlayerConnections.TryGetValue(playerId, out var connections))
                return;

            foreach (string connectionId in connections.Keys)
                EnsurePersistentSubscriptions(connectionId, playerId);
        }

        internal static async Task PrimeConnectionAsync(
            string connectionId,
            long ownerPlayerId)
        {
            if (!ConnectionPlayers.ContainsKey(connectionId))
                return;

            EnsurePersistentSubscriptions(connectionId, ownerPlayerId);

            long[] subjectIds = ConnectionPlayerSubscriptions.TryGetValue(
                    connectionId,
                    out var subscriptions)
                ? subscriptions.Keys
                    .Where(id => id > 0)
                    .Distinct()
                    .Take(MaxPlayerSubscriptionsPerConnection)
                    .ToArray()
                : Array.Empty<long>();

            long[] profileIds = subjectIds
                .Append(ownerPlayerId)
                .Distinct()
                .ToArray();

            foreach (PlayerDTOBase account in PlayerDB.GetAccountsBulk(
                         profileIds.ToList(),
                         callerId: ownerPlayerId))
            {
                string eventId = account.accountId == ownerPlayerId
                    ? SelfAccountUpdateNotificationId
                    : AccountUpdateNotificationId;

                await SendProfilePushToConnectionIdsAsync(
                    new[] { connectionId },
                    account,
                    eventId);
            }

            foreach (ClientRelationshipDTO relationship in
                     RelationshipDB.GetClientRelationships(ownerPlayerId))
            {
                await SendPushEventToConnectionIdsAsync(
                    new[] { connectionId },
                    RelationshipChangedTarget,
                    relationship);
            }

            foreach (long subjectPlayerId in subjectIds)
            {
                Heartbeat heartbeat = PlayerDB.GetPlayerHeartbeat(
                    subjectPlayerId,
                    ownerPlayerId);

                await SendPresencePushToConnectionIdsAsync(
                    new[] { connectionId },
                    heartbeat);
            }

            Console.WriteLine(
                $"[LIVE PRIME] player={ownerPlayerId} " +
                $"profiles={profileIds.Length} " +
                $"relationships={RelationshipDB.GetClientRelationships(ownerPlayerId).Count} " +
                $"presence={subjectIds.Length}");
        }

        internal static async Task PrimePlayerSubscriptionsAsync(
            string connectionId,
            long ownerPlayerId,
            IEnumerable<int> requestedPlayerIds)
        {
            if (!ConnectionPlayers.ContainsKey(connectionId))
                return;

            long[] subjectIds = requestedPlayerIds
                .Where(id => id > 0)
                .Select(id => (long)id)
                .Where(id => id != ownerPlayerId)
                .Distinct()
                .Take(MaxPlayerSubscriptionsPerConnection)
                .ToArray();

            if (subjectIds.Length == 0)
                return;

            foreach (PlayerDTOBase account in PlayerDB.GetAccountsBulk(
                         subjectIds.ToList(),
                         callerId: ownerPlayerId))
            {
                await SendProfilePushToConnectionIdsAsync(
                    new[] { connectionId },
                    account);
            }

            foreach (long subjectPlayerId in subjectIds)
            {
                Heartbeat heartbeat = PlayerDB.GetPlayerHeartbeat(
                    subjectPlayerId,
                    ownerPlayerId);

                await SendPresencePushToConnectionIdsAsync(
                    new[] { connectionId },
                    heartbeat);
            }

            Console.WriteLine(
                $"[LIVE SUBSCRIBE PRIME] player={ownerPlayerId} " +
                $"subjects={subjectIds.Length} socket={connectionId}");
        }

        private static void AddPlayerConnections(
            ISet<string> destination,
            long playerId)
        {
            if (playerId <= 0 ||
                !PlayerConnections.TryGetValue(playerId, out var connections))
            {
                return;
            }

            foreach (string connectionId in connections.Keys)
                destination.Add(connectionId);
        }

        private static string[] GetProfileRecipientConnectionIds(
            long subjectPlayerId)
        {
            var recipients = new HashSet<string>(StringComparer.Ordinal);

            AddPlayerConnections(recipients, subjectPlayerId);

            if (PlayerSubscribers.TryGetValue(
                    subjectPlayerId,
                    out var explicitSubscribers))
            {
                recipients.UnionWith(explicitSubscribers.Keys);
            }

            foreach (long watcherPlayerId in
                     RelationshipDB.GetPersistentLiveWatcherIds(subjectPlayerId))
            {
                AddPlayerConnections(recipients, watcherPlayerId);
            }

            foreach (long roomPeerPlayerId in
                     PlayerDB.GetActiveSameInstancePlayerIds(subjectPlayerId))
            {
                AddPlayerConnections(recipients, roomPeerPlayerId);
            }

            return recipients.ToArray();
        }

        private static string[] GetPresenceRecipientConnectionIds(
            long subjectPlayerId)
        {
            var recipients = new HashSet<string>(StringComparer.Ordinal);

            if (PlayerSubscribers.TryGetValue(
                    subjectPlayerId,
                    out var explicitSubscribers))
            {
                recipients.UnionWith(explicitSubscribers.Keys);
            }

            foreach (long watcherPlayerId in
                     RelationshipDB.GetPersistentLiveWatcherIds(subjectPlayerId))
            {
                AddPlayerConnections(recipients, watcherPlayerId);
            }

            if (PlayerConnections.TryGetValue(subjectPlayerId, out var own))
                recipients.ExceptWith(own.Keys);

            return recipients.ToArray();
        }

        public static bool IsPlayerConnected(long playerId) =>
            PlayerConnections.TryGetValue(playerId, out var connections) &&
            !connections.IsEmpty;

        public static object GetDebugStatus(long playerId)
        {
            string[] connectionIds = PlayerConnections.TryGetValue(
                playerId,
                out var own)
                ? own.Keys.ToArray()
                : Array.Empty<string>();

            return new
            {
                playerId,
                connected = connectionIds.Length > 0,
                connectionCount = connectionIds.Length,
                connections = connectionIds.Select(connectionId => new
                {
                    connectionId,
                    playerIds = ConnectionPlayerSubscriptions.TryGetValue(
                        connectionId,
                        out var subscriptions)
                        ? subscriptions.Keys.OrderBy(id => id).ToArray()
                        : Array.Empty<long>()
                }).ToArray()
            };
        }

        public static Task SendNotificationToPlayer(long playerId, object message)
        {
            JsonElement root = SystemJsonSerializer.SerializeToElement(message);
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetPropertyIgnoreCase(root, "target", out JsonElement targetElement))
            {
                return Task.CompletedTask;
            }

            string? target = targetElement.GetString();
            if (string.IsNullOrWhiteSpace(target))
                return Task.CompletedTask;

            object?[] arguments = Array.Empty<object?>();
            if (TryGetPropertyIgnoreCase(root, "arguments", out JsonElement argumentsElement) &&
                argumentsElement.ValueKind == JsonValueKind.Array)
            {
                arguments = argumentsElement
                    .EnumerateArray()
                    .Select(argument => (object?)argument.Clone())
                    .ToArray();
            }

            return SendHubEventToPlayerAsync(playerId, target, arguments);
        }

        public static Task SendHubEventToPlayerAsync(
            long playerId,
            string target,
            params object?[] arguments)
        {
            if (!PlayerConnections.TryGetValue(playerId, out var connections))
                return Task.CompletedTask;

            return SendTargetToConnectionsAsync(
                connections.Keys,
                target,
                arguments);
        }

        public static Task SendHubEventToSubscribersAsync(
            long subjectPlayerId,
            string target,
            params object?[] arguments)
        {
            if (!PlayerSubscribers.TryGetValue(
                    subjectPlayerId,
                    out var subscribers))
            {
                return Task.CompletedTask;
            }

            return SendTargetToConnectionsAsync(
                subscribers.Keys,
                target,
                arguments);
        }

        public static async Task DeliverPendingNotificationsAsync(long playerId)
        {
            List<NotificationDB.ClientNotification> pending =
                NotificationDB.GetMessages(playerId)
                    .OrderBy(message => message.CreatedAt)
                    .Take(100)
                    .ToList();

            if (pending.Count == 0)
                return;

            Console.WriteLine(
                $"[NOTIFICATION REPLAY] player={playerId} count={pending.Count}");

            foreach (NotificationDB.ClientNotification message in pending)
            {
                await SendPushEventToPlayerAsync(
                    playerId,
                    MessageReceivedTarget,
                    message);
            }
        }

        public static async Task NotifyAnnouncementsUpdatedAsync(long playerId)
        {
            await Task.WhenAll(
                SendPushSignalToPlayerAsync(
                    playerId,
                    CommunityBoardAnnouncementUpdateTarget),
                SendPushSignalToPlayerAsync(
                    playerId,
                    CommunityBoardUpdateTarget));
        }

        public static async Task BroadcastAnnouncementsUpdatedAsync()
        {
            long[] connectedPlayers = PlayerConnections.Keys
                .Where(playerId => playerId > 0)
                .Distinct()
                .ToArray();

            if (connectedPlayers.Length == 0)
                return;

            Console.WriteLine(
                $"[Announcements] Broadcasting live refresh to " +
                $"{connectedPlayers.Length} connected players");

            await Task.WhenAll(connectedPlayers.Select(
                NotifyAnnouncementsUpdatedAsync));
        }

        public static async Task BroadcastServerMaintenanceAsync(int startsInMinutes)
        {
            if (startsInMinutes < 0)
                throw new ArgumentOutOfRangeException(nameof(startsInMinutes));

            long[] connectedPlayers = PlayerConnections.Keys
                .Where(playerId => playerId > 0)
                .Distinct()
                .ToArray();

            await Task.WhenAll(connectedPlayers.Select(playerId =>
                SendPushSignalToPlayerAsync(
                    playerId,
                    ServerMaintenanceTarget)));

            Console.WriteLine(
                $"[Maintenance] Sent {startsInMinutes}-minute countdown to " +
                $"{connectedPlayers.Length} connected player(s)");
        }

        public static async Task NotifyFriendRequestAsync(
            long senderPlayerId,
            long receiverPlayerId)
        {
            NotificationDB.ClientNotification message =
                NotificationDB.CreateFriendInvite(
                    senderPlayerId,
                    receiverPlayerId);

            await SendPushEventToPlayerAsync(
                receiverPlayerId,
                MessageReceivedTarget,
                message);

            await NotifyRelationshipChangedAsync(
                senderPlayerId,
                receiverPlayerId);
        }

        public static async Task NotifyFriendAcceptedAsync(
            long acceptingPlayerId,
            long requesterPlayerId)
        {
            NotificationDB.ClientNotification message =
                NotificationDB.CreateFriendAccepted(
                    acceptingPlayerId,
                    requesterPlayerId);

            await SendPushEventToPlayerAsync(
                requesterPlayerId,
                MessageReceivedTarget,
                message);

            await NotifyRelationshipChangedAsync(
                acceptingPlayerId,
                requesterPlayerId);
        }

        public static async Task NotifyFriendIntroductionAsync(
            long senderPlayerId,
            long receiverPlayerId,
            long aboutPlayerId)
        {
            NotificationDB.ClientNotification message =
                NotificationDB.CreateFriendIntroduction(
                    senderPlayerId,
                    receiverPlayerId,
                    aboutPlayerId);

            await SendPushEventToPlayerAsync(
                receiverPlayerId,
                MessageReceivedTarget,
                message);
        }

        public static Task NotifyFriendRemovedAsync(
            long firstPlayerId,
            long secondPlayerId) =>
            NotifyRelationshipChangedAsync(firstPlayerId, secondPlayerId);

        public static async Task NotifyChatMessageReceivedAsync(
            long receiverPlayerId,
            object chatMessageDto)
        {
            int socketCount = PlayerConnections.TryGetValue(
                receiverPlayerId,
                out var sockets)
                ? sockets.Count
                : 0;

            Console.WriteLine(
                $"[CHAT PUSH] receiver={receiverPlayerId} " +
                $"eventId={ChatMessageReceivedTarget} sockets={socketCount}");

            await SendPushEventToPlayerAsync(
                receiverPlayerId,
                ChatMessageReceivedTarget,
                chatMessageDto);
        }

        public static Task NotifyRelationshipChangedAsync(
            long firstPlayerId,
            long secondPlayerId) =>
            NotifyRelationshipFlagsChangedAsync(
                firstPlayerId,
                secondPlayerId,
                "relationship-change");

        public static async Task NotifyRelationshipFlagsChangedAsync(
            long firstPlayerId,
            long secondPlayerId,
            string reason)
        {

            RefreshPersistentSubscriptionsForPlayer(firstPlayerId);
            RefreshPersistentSubscriptionsForPlayer(secondPlayerId);

            ClientRelationshipDTO firstView =
                RelationshipDB.GetClientRelationship(
                    firstPlayerId,
                    secondPlayerId) ??
                CreateNoClientRelationship(secondPlayerId);

            ClientRelationshipDTO secondView =
                RelationshipDB.GetClientRelationship(
                    secondPlayerId,
                    firstPlayerId) ??
                CreateNoClientRelationship(firstPlayerId);

            Console.WriteLine(
                $"[RELATIONSHIP LIVE] reason={reason} " +
                $"first={firstPlayerId}->{secondPlayerId} " +
                $"type={firstView.RelationshipType} " +
                $"ignored={firstView.Ignored} muted={firstView.Muted} " +
                $"sockets={GetPlayerConnectionCount(firstPlayerId)}");
            Console.WriteLine(
                $"[RELATIONSHIP LIVE] reason={reason} " +
                $"second={secondPlayerId}->{firstPlayerId} " +
                $"type={secondView.RelationshipType} " +
                $"ignored={secondView.Ignored} muted={secondView.Muted} " +
                $"sockets={GetPlayerConnectionCount(secondPlayerId)}");

            await Task.WhenAll(
                SendPushSignalToPlayerAsync(
                    firstPlayerId,
                    RelationshipsInvalidTarget),
                SendPushSignalToPlayerAsync(
                    secondPlayerId,
                    RelationshipsInvalidTarget));

            await Task.Delay(50);

            await Task.WhenAll(
                SendPushEventToPlayerAsync(
                    firstPlayerId,
                    RelationshipChangedTarget,
                    firstView),
                SendPushEventToPlayerAsync(
                    secondPlayerId,
                    RelationshipChangedTarget,
                    secondView));

            Console.WriteLine(
                $"[RELATIONSHIP LIVE] complete reason={reason} " +
                $"players={firstPlayerId},{secondPlayerId}");
        }

        private static int GetPlayerConnectionCount(long playerId) =>
            PlayerConnections.TryGetValue(playerId, out var connections)
                ? connections.Count
                : 0;

        public static async Task NotifyPlayerProfileUpdatedAsync(long playerId)
        {
            PlayerDTOBase? account = PlayerDB.GetAccountsBulk(
                    new List<long> { playerId },
                    callerId: null)
                .FirstOrDefault();

            if (account == null)
                return;

            string[] ownConnections = PlayerConnections.TryGetValue(
                    playerId,
                    out var ownSockets)
                ? ownSockets.Keys
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
                : Array.Empty<string>();

            var ownConnectionSet = ownConnections.ToHashSet(StringComparer.Ordinal);
            string[] viewerConnections = GetProfileRecipientConnectionIds(playerId)
                .Where(connectionId => !ownConnectionSet.Contains(connectionId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Task selfPush = ownConnections.Length == 0
                ? Task.CompletedTask
                : SendProfilePushToConnectionIdsAsync(
                    ownConnections,
                    account,
                    SelfAccountUpdateNotificationId);

            Task viewerPush = viewerConnections.Length == 0
                ? Task.CompletedTask
                : SendProfilePushToConnectionIdsAsync(
                    viewerConnections,
                    account,
                    AccountUpdateNotificationId);

            await Task.WhenAll(selfPush, viewerPush);

            Console.WriteLine(
                $"[PROFILE PUSH] player={playerId} " +
                $"selfSockets={ownConnections.Length} viewerSockets={viewerConnections.Length} " +
                $"displayName={account.displayName} emoji={account.displayEmoji} " +
                $"profileImage={account.profileImage}");
        }

        public static async Task NotifyPlayerPresenceUpdatedAsync(long playerId)
        {
            string[] recipientConnections =
                GetPresenceRecipientConnectionIds(playerId);

            if (recipientConnections.Length == 0)
                return;

            var connectionsByViewer = recipientConnections
                .Where(ConnectionPlayers.ContainsKey)
                .GroupBy(connectionId => ConnectionPlayers[connectionId])
                .ToArray();

            foreach (IGrouping<long, string> viewerConnections in
                     connectionsByViewer)
            {
                Heartbeat heartbeat = PlayerDB.GetPlayerHeartbeat(
                    playerId,
                    viewerConnections.Key);

                await SendPresencePushToConnectionIdsAsync(
                    viewerConnections,
                    heartbeat);
            }

            Heartbeat publicHeartbeat = PlayerDB.GetPlayerHeartbeat(playerId);
            Console.WriteLine(
                $"[PRESENCE PUSH] player={playerId} " +
                $"online={publicHeartbeat.isOnline} " +
                $"room={publicHeartbeat.roomInstance?.roomId.ToString() ?? "none"} " +
                $"instance={publicHeartbeat.roomInstance?.roomInstanceId.ToString() ?? "none"} " +
                $"viewers={connectionsByViewer.Length} " +
                $"sockets={recipientConnections.Length}");
        }

        public static async Task NotifyProgressionAsync(
            long playerId,
            int oldLevel,
            int newLevel,
            int xp)
        {
            int clientPlayerId = checked((int)playerId);

            object CreateClientProgression(int level) => new
            {
                PlayerId = clientPlayerId,
                Level = level,
                XP = xp
            };

            if (newLevel > oldLevel)
            {
                await SendPushEventToPlayerAsync(
                    playerId,
                    "PlayerProgressionLevelUpdate",
                    CreateClientProgression(oldLevel));

                await Task.Delay(40);
            }

            await SendPushEventToPlayerAsync(
                playerId,
                "PlayerProgressionLevelUpdate",
                CreateClientProgression(newLevel));

            if (newLevel > oldLevel)
            {
                Console.WriteLine(
                    $"[LEVEL EFFECT] player={playerId} " +
                    $"staged={oldLevel}->{newLevel}");
            }

            await SendHubEventToPlayerAsync(
                playerId,
                "PlayerProgressionUpdated",
                clientPlayerId,
                newLevel,
                xp);

            await NotifyPlayerProfileUpdatedAsync(playerId);

            if (newLevel > oldLevel)
            {
                await SendHubEventToPlayerAsync(
                    playerId,
                    "LevelUpNotified",
                    newLevel);
            }
        }

        public static async Task NotifyCheerAsync(
            long receiverPlayerId,
            NotificationDB.ClientNotification notification,
            int category)
        {

            await SendPushEventToPlayerAsync(
                receiverPlayerId,
                MessageReceivedTarget,
                notification);

            await SendHubEventToPlayerAsync(
                receiverPlayerId,
                "PlayerCheerReceived",
                notification.FromPlayerId,
                category);
        }

        public static async Task NotifyConsumableRemovedAsync(
            long playerId,
            long mappingId,
            string consumableItemDesc,
            string createdAt,
            int previousCount,
            int remainingCount)
        {
            var payload = new
            {
                Id = mappingId,
                ConsumableItemDesc = consumableItemDesc ?? string.Empty,
                CreatedAt = createdAt,
                Count = Math.Max(0, remainingCount),
                InitialCount = Math.Max(0, previousCount),
                IsActive = false,
                ActiveDurationMinutes = 0,
                IsTransferable = false
            };

            await SendPushEventToPlayerAsync(
                playerId,
                ConsumableMappingRemovedTarget,
                payload);

            Console.WriteLine(
                $"[CONSUMABLE PUSH] player={playerId} id={mappingId} " +
                $"count={remainingCount} initial={previousCount} " +
                $"eventId={ConsumableMappingRemovedTarget}");
        }

        public static async Task<NotificationDB.ClientNotification>
            NotifyRoomInviteAsync(
            long inviterPlayerId,
            long receiverPlayerId,
            long roomId,
            long roomInstanceId,
            string? photonRoomId)
        {

            Heartbeat inviterHeartbeat =
                PlayerDB.GetPlayerHeartbeat(inviterPlayerId);
            RoomInstance? inviterInstance = inviterHeartbeat.roomInstance;
            bool isExactInviterInstance =
                inviterInstance != null &&
                inviterInstance.roomInstanceId == roomInstanceId &&
                inviterInstance.roomId == roomId;

            string roomName =
                isExactInviterInstance &&
                !string.IsNullOrWhiteSpace(inviterInstance!.Name)
                    ? inviterInstance.Name
                    : RoomDB.GetRoom(roomId)?.Name ?? "Room";

            bool isPrivateInstance =
                isExactInviterInstance &&
                Sessions.IsRestrictedInstance(inviterInstance);
            string resolvedPhotonRoomId = isExactInviterInstance
                ? inviterInstance!.photonRoomId
                : photonRoomId ?? string.Empty;

            NotificationDB.ClientNotification message =
                NotificationDB.CreateRoomInvite(
                    inviterPlayerId,
                    receiverPlayerId,
                    roomId,
                    roomInstanceId,
                    roomName,
                    inviteMode: isPrivateInstance ? 1 : 0,
                    photonRoomId: resolvedPhotonRoomId);

            await SendPushEventToPlayerAsync(
                receiverPlayerId,
                MessageReceivedTarget,
                message);

            Console.WriteLine(
                $"[ROOM INVITE PUSH] from={inviterPlayerId} " +
                $"to={receiverPlayerId} messageId={message.Id} " +
                $"messageType={message.Type} data={message.Message} " +
                $"roomInstanceId={roomInstanceId} room={roomId} " +
                $"private={isPrivateInstance.ToString().ToLowerInvariant()} " +
                $"name={roomName} eventId={MessageReceivedTarget} " +
                $"idJsonType=string " +
                $"wireContract=game-invite-v1 " +
                $"sockets={GetPlayerConnectionCount(receiverPlayerId)}");

            return message;
        }

        public static async Task NotifyMessageAsync(
            long receiverPlayerId,
            NotificationDB.ClientNotification message)
        {
            await SendPushEventToPlayerAsync(
                receiverPlayerId,
                MessageReceivedTarget,
                message);
        }

        public static async Task NotifyPartyInviteAsync(
            long inviterPlayerId,
            long receiverPlayerId,
            long partyId)
        {
            NotificationDB.ClientNotification message =
                NotificationDB.CreatePartyInvite(
                    inviterPlayerId,
                    receiverPlayerId,
                    partyId);

            await SendPushEventToPlayerAsync(
                receiverPlayerId,
                MessageReceivedTarget,
                message);

            await SendHubEventToPlayerAsync(
                receiverPlayerId,
                "PartyInviteReceived",
                message);
        }

        public static async Task NotifyPartyUpdatedAsync(
            IEnumerable<long> memberPlayerIds,
            object party)
        {
            await Task.WhenAll(memberPlayerIds
                .Where(id => id > 0)
                .Distinct()
                .Select(playerId => SendHubEventToPlayerAsync(
                    playerId,
                    "PartyUpdated",
                    party)));
        }

        public static async Task NotifyGiftAsync(
            long senderPlayerId,
            long receiverPlayerId,
            object? gift = null,
            bool immediate = true)
        {
            await NotifyGiftsAsync(
                senderPlayerId,
                new[]
                {
                    new GiftLiveDelivery(receiverPlayerId, gift)
                },
                immediate);
        }

        public static async Task<GiftLiveDeliveryResult> NotifyGiftsAsync(
            long senderPlayerId,
            IEnumerable<GiftLiveDelivery> deliveries,
            bool immediate = true)
        {
            GiftLiveDelivery[] requested = deliveries?
                .Where(delivery =>
                    delivery != null &&
                    delivery.ReceiverPlayerId > 0)
                .ToArray() ?? Array.Empty<GiftLiveDelivery>();

            var result = new GiftLiveDeliveryResult
            {
                TargetPlayers = requested
                    .Select(delivery => delivery.ReceiverPlayerId)
                    .Distinct()
                    .Count()
            };

            if (requested.Length == 0)
                return result;

            PreparedGiftLiveDelivery[] prepared = requested
                .Select(delivery =>
                {
                    object payload = delivery.Gift is GiftPackage package
                        ? CreateClientGiftPackagePayload(
                            package,
                            senderPlayerId,
                            delivery.ReceiverPlayerId)
                        : delivery.Gift ?? CreateFallbackGiftPackagePayload(
                            senderPlayerId,
                            delivery.ReceiverPlayerId);

                    int effectiveSenderPlayerId =
                        delivery.Gift is GiftPackage sentGift
                            ? NormalizeGiftSenderId(
                                sentGift.FromPlayerId,
                                senderPlayerId,
                                delivery.ReceiverPlayerId)
                            : NormalizeGiftSenderId(
                                0,
                                senderPlayerId,
                                delivery.ReceiverPlayerId);

                    return new PreparedGiftLiveDelivery
                    {
                        SenderPlayerId = effectiveSenderPlayerId,
                        ReceiverPlayerId = delivery.ReceiverPlayerId,
                        Payload = payload,
                        GiftPackageId = delivery.Gift is GiftPackage sentPackage
                            ? sentPackage.GiftPackageId
                            : 0
                    };
                })
                .ToArray();

            bool primedAnyProfile = false;
            foreach (IGrouping<long, PreparedGiftLiveDelivery> senderGroup in
                     prepared.GroupBy(delivery => delivery.SenderPlayerId))
            {
                var connectionOwners = new Dictionary<string, PreparedGiftLiveDelivery>(
                    StringComparer.Ordinal);
                foreach (PreparedGiftLiveDelivery delivery in senderGroup)
                {
                    foreach (string connectionId in
                             GetPlayerConnectionIds(delivery.ReceiverPlayerId))
                    {
                        connectionOwners[connectionId] = delivery;
                    }
                }

                if (connectionOwners.Count == 0)
                    continue;

                PlayerDTOBase? senderAccount = ResolveGiftSenderAccount(
                    senderGroup.Key);
                if (senderAccount == null)
                {
                    Console.WriteLine(
                        $"[GIFT SENDER CONTEXT] sender={senderGroup.Key} " +
                        $"profileMissing=true sockets={connectionOwners.Count}");
                    continue;
                }

                int primedSockets =
                    await TrySendProfilePushToConnectionIdsAsync(
                        connectionOwners.Keys,
                        senderAccount,
                        AccountUpdateNotificationId);
                if (primedSockets > 0)
                {
                    foreach (KeyValuePair<string, PreparedGiftLiveDelivery> entry in
                             connectionOwners)
                    {
                        entry.Value.PrimedConnectionIds.Add(entry.Key);
                    }
                    primedAnyProfile = true;
                }

                Console.WriteLine(
                    $"[GIFT SENDER CONTEXT] sender={senderGroup.Key} " +
                    $"receivers={connectionOwners.Values.Select(value => value.ReceiverPlayerId).Distinct().Count()} " +
                    $"profileEvent={AccountUpdateNotificationId} " +
                    $"sockets={primedSockets} batch=true");
            }

            if (primedAnyProfile)
                await Task.Delay(75);

            string eventId = immediate
                ? GiftPackageReceivedImmediateTarget
                : GiftPackageReceivedTarget;

            var livePlayerIds = new HashSet<long>();
            foreach (PreparedGiftLiveDelivery delivery in prepared)
            {
                string[] connectionIds =
                    GetPlayerConnectionIds(delivery.ReceiverPlayerId);
                if (connectionIds.Length == 0)
                {
                    Console.WriteLine(
                        $"[GIFT PUSH] from={senderPlayerId} " +
                        $"to={delivery.ReceiverPlayerId} " +
                        $"effectiveFrom={delivery.SenderPlayerId} " +
                        $"package={delivery.GiftPackageId} eventId={eventId} " +
                        "idJsonType=string senderProfilePrimed=false sockets=0 " +
                        "live=false pendingFallback=true");
                    continue;
                }

                string[] unprimedConnectionIds = connectionIds
                    .Where(connectionId =>
                        !delivery.PrimedConnectionIds.Contains(connectionId))
                    .ToArray();
                if (unprimedConnectionIds.Length > 0)
                {
                    PlayerDTOBase? senderAccount = ResolveGiftSenderAccount(
                        delivery.SenderPlayerId);
                    if (senderAccount != null)
                    {
                        int primedSockets =
                            await TrySendProfilePushToConnectionIdsAsync(
                                unprimedConnectionIds,
                                senderAccount,
                                AccountUpdateNotificationId);
                        if (primedSockets > 0)
                        {
                            foreach (string connectionId in
                                     unprimedConnectionIds)
                            {
                                delivery.PrimedConnectionIds.Add(connectionId);
                            }
                            await Task.Delay(50);
                        }
                    }
                }

                string jsonArgument = CreateClientNotificationJson(
                    eventId,
                    delivery.Payload);
                int deliveredSockets =
                    await TrySendPushJsonToConnectionIdsAsync(
                        connectionIds,
                        eventId,
                        jsonArgument);

                bool deliveredLive = deliveredSockets > 0;
                if (deliveredLive)
                {
                    livePlayerIds.Add(delivery.ReceiverPlayerId);
                    result.LiveSockets += deliveredSockets;
                }

                bool senderProfilePrimed = connectionIds.Any(connectionId =>
                    delivery.PrimedConnectionIds.Contains(connectionId));
                Console.WriteLine(
                    $"[GIFT PUSH] from={senderPlayerId} " +
                    $"to={delivery.ReceiverPlayerId} " +
                    $"effectiveFrom={delivery.SenderPlayerId} " +
                    $"package={delivery.GiftPackageId} eventId={eventId} " +
                    $"idJsonType=string senderProfilePrimed={senderProfilePrimed} " +
                    $"sockets={deliveredSockets} live={deliveredLive.ToString().ToLowerInvariant()} " +
                    $"pendingFallback={!deliveredLive}");
            }

            result.LivePlayers = livePlayerIds.Count;
            result.OfflinePlayers = Math.Max(
                0,
                result.TargetPlayers - result.LivePlayers);
            Console.WriteLine(
                $"[GIFT LIVE BATCH] sender={senderPlayerId} " +
                $"targets={result.TargetPlayers} livePlayers={result.LivePlayers} " +
                $"liveSockets={result.LiveSockets} offline={result.OfflinePlayers} " +
                $"eventId={eventId}");

            return result;
        }

        private static string[] GetPlayerConnectionIds(long playerId)
        {
            if (!PlayerConnections.TryGetValue(
                    playerId,
                    out var receiverConnections))
            {
                return Array.Empty<string>();
            }

            return receiverConnections.Keys
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Where(id =>
                    ConnectionPlayers.TryGetValue(id, out long ownerId) &&
                    ownerId == playerId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static PlayerDTOBase? ResolveGiftSenderAccount(
            long senderPlayerId)
        {

            return PlayerDB.GetAccountsBulk(
                    new List<long> { senderPlayerId },
                    callerId: null)
                .FirstOrDefault();
        }

        public static async Task NotifyGiftConsumedAsync(
            long receiverPlayerId,
            long giftId,
            IEnumerable<GiftPackage>? remainingGifts = null)
        {
            object[] remainingPayloads = remainingGifts?
                .Select(giftPackage => CreateClientGiftPackagePayload(
                    giftPackage,
                    giftPackage.FromPlayerId,
                    receiverPlayerId))
                .ToArray() ?? Array.Empty<object>();

            var payload = new
            {
                GiftId = giftId,
                RemainingGifts = remainingPayloads
            };

            await SendPushEventToPlayerAsync(
                receiverPlayerId,
                GiftConsumedTarget,
                payload);

            Console.WriteLine(
                $"[GIFT CONSUMED PUSH] player={receiverPlayerId} " +
                $"gift={giftId} remaining={remainingPayloads.Length} " +
                $"eventId={GiftConsumedTarget}");
        }

        private static object CreateClientGiftPackagePayload(
            GiftPackage gift,
            long senderPlayerId,
            long receiverPlayerId)
        {
            int fromPlayerId = NormalizeGiftSenderId(
                gift.FromPlayerId,
                senderPlayerId,
                receiverPlayerId);
            int giftContext = NormalizeGiftContext(gift.GiftContext);

            return new
            {
                AvatarItemDesc = EmptyIfWhiteSpace(gift.AvatarItemDesc),
                AvatarItemType = 0,
                BalanceType = gift.BalanceType ??
                    (int)BalanceType.NonPurchasedNotUsableInP2P,
                ConsumableItemDesc = EmptyIfWhiteSpace(gift.ConsumableItemDesc),
                Currency = gift.Currency,
                CurrencyType = gift.CurrencyType,
                EquipmentModificationGuid =
                    EmptyIfWhiteSpace(gift.EquipmentModificationGuid),
                EquipmentPrefabName =
                    EmptyIfWhiteSpace(gift.EquipmentPrefabName),
                FromGiftDropId = 0,
                FromPlayerId = fromPlayerId,
                GiftContext = giftContext,
                GiftRarity = gift.Rarity,
                Id = gift.GiftPackageId,
                Level = 0,
                Message = gift.Message ?? string.Empty,
                Platform = gift.Platform,
                PlatformsToSpawnOn = gift.PlatformMask,
                Xp = gift.XP
            };
        }

        private static object CreateFallbackGiftPackagePayload(
            long senderPlayerId,
            long receiverPlayerId)
        {
            int fromPlayerId = NormalizeGiftSenderId(
                0,
                senderPlayerId,
                receiverPlayerId);

            return new
            {
                AvatarItemDesc = string.Empty,
                AvatarItemType = 0,
                BalanceType =
                    (int)BalanceType.NonPurchasedNotUsableInP2P,
                ConsumableItemDesc = string.Empty,
                Currency = 0,
                CurrencyType = 2,
                EquipmentModificationGuid = string.Empty,
                EquipmentPrefabName = string.Empty,
                FromGiftDropId = 0,
                FromPlayerId = fromPlayerId,
                GiftContext = 2,
                GiftRarity = 0,
                Id = 0L,
                Level = 0,
                Message = string.Empty,
                Platform = -1,
                PlatformsToSpawnOn = -1,
                Xp = 0
            };
        }

        private static int NormalizeGiftSenderId(
            int storedSenderPlayerId,
            long requestedSenderPlayerId,
            long receiverPlayerId)
        {
            long senderPlayerId = storedSenderPlayerId > 0
                ? storedSenderPlayerId
                : requestedSenderPlayerId;

            if (senderPlayerId <= 0)
                return 0;
            if (senderPlayerId > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Gift sender id {senderPlayerId} cannot be represented by the April 2023 client.");
            }

            if (senderPlayerId == receiverPlayerId)
                return receiverPlayerId == 1 ? 2 : 1;

            return checked((int)senderPlayerId);
        }

        private static int NormalizeGiftContext(int giftContext) =>
            giftContext is (int)PlayerDBClasses.GiftContext.Default
                or (int)PlayerDBClasses.GiftContext.Store_RecCenter
                ? (int)PlayerDBClasses.GiftContext.Game_Drop
                : giftContext;

        private static string EmptyIfWhiteSpace(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value;

        private static ClientRelationshipDTO CreateNoClientRelationship(
            long otherPlayerId)
        {
            if (otherPlayerId <= 0 || otherPlayerId > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Player id {otherPlayerId} cannot be represented by the April 2023 client.");
            }

            return new ClientRelationshipDTO
            {
                PlayerID = checked((int)otherPlayerId),
                RelationshipType = (int)RelationshipType.None,
                Favorited = 0,
                Muted = 0,
                Ignored = 0
            };
        }

        private static Task SendPushSignalToPlayerAsync(
            long playerId,
            string eventId) =>
            SendPushJsonToPlayerAsync(
                playerId,
                eventId,
                CreateClientNotificationJson(eventId, null));

        private static Task SendPushEventToPlayerAsync(
            long playerId,
            string eventId,
            object payload) =>
            SendPushJsonToPlayerAsync(
                playerId,
                eventId,
                CreateClientNotificationJson(eventId, payload));

        private static Task SendPushEventToConnectionIdsAsync(
            IEnumerable<string> connectionIds,
            string eventId,
            object payload) =>
            SendPushJsonToConnectionIdsAsync(
                connectionIds,
                eventId,
                CreateClientNotificationJson(eventId, payload));

        private static Task SendProfilePushToConnectionIdsAsync(
            IEnumerable<string> connectionIds,
            PlayerDTOBase account) =>
            SendProfilePushToConnectionIdsAsync(
                connectionIds,
                account,
                AccountUpdateNotificationId);

        private static async Task SendProfilePushToConnectionIdsAsync(
            IEnumerable<string> connectionIds,
            PlayerDTOBase account,
            string eventId)
        {
            await TrySendProfilePushToConnectionIdsAsync(
                connectionIds,
                account,
                eventId);
        }

        private static async Task<int> TrySendProfilePushToConnectionIdsAsync(
            IEnumerable<string> connectionIds,
            PlayerDTOBase account,
            string eventId)
        {
            string[] ids = connectionIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (ids.Length == 0)
                return 0;

            object profile = CreateClientProfilePayload(account);
            string jsonArgument = CreateClientNotificationJson(
                eventId,
                profile);

            int deliveredSockets = await TrySendTargetToConnectionsAsync(
                    ids,
                    ClientNotificationHubTarget,
                    new object?[] { jsonArgument });

            Console.WriteLine(
                $"[PROFILE TRANSPORT] player={account.accountId} " +
                $"hubTarget={ClientNotificationHubTarget} " +
                $"eventId={eventId} " +
                $"sockets={deliveredSockets} bytes={jsonArgument.Length}");
            return deliveredSockets;
        }

        private static object CreateClientProfilePayload(PlayerDTOBase account)
        {
            FullPlayer? fullPlayer = PlayerDB.Players.FindById(account.accountId);
            string? bannerImage = fullPlayer?.Player?.BannerImage;
            if (string.IsNullOrWhiteSpace(bannerImage))
                bannerImage = null;

            return new
            {
                accountId = checked((int)account.accountId),
                username = account.username ?? string.Empty,
                displayName = account.displayName ?? string.Empty,
                profileImage = account.profileImage ?? string.Empty,
                bannerImage,
                displayEmoji = account.displayEmoji ?? string.Empty,
                isJunior = account.isJunior ?? false,
                platforms = account.platforms,
                personalPronouns = account.personalPronouns,
                identityFlags = account.identityFlags,
                createdAt = account.createdAt
            };
        }

        private static async Task SendPresencePushToConnectionIdsAsync(
            IEnumerable<string> connectionIds,
            Heartbeat heartbeat)
        {
            string[] ids = connectionIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (ids.Length == 0)
                return;

            string jsonArgument = CreateClientNotificationJson(
                PresenceUpdateNotificationId,
                heartbeat);

            await SendTargetToConnectionsAsync(
                ids,
                ClientNotificationHubTarget,
                new object?[] { jsonArgument });

            Console.WriteLine(
                $"[PRESENCE TRANSPORT] player={heartbeat.playerId} " +
                $"hubTarget={ClientNotificationHubTarget} " +
                $"eventId={PresenceUpdateNotificationId} " +
                $"sockets={ids.Length} bytes={jsonArgument.Length}");
        }

        private static Task SendPushJsonToPlayerAsync(
            long playerId,
            string eventId,
            string jsonArgument)
        {
            if (!PlayerConnections.TryGetValue(playerId, out var connections))
            {
                Console.WriteLine(
                    $"[Push] queued/persisted event={eventId}, " +
                    $"player={playerId}, online=false");
                return Task.CompletedTask;
            }

            return SendPushJsonToConnectionIdsAsync(
                connections.Keys,
                eventId,
                jsonArgument);
        }

        private static async Task SendPushJsonToConnectionIdsAsync(
            IEnumerable<string> connectionIds,
            string eventId,
            string jsonArgument)
        {
            await TrySendPushJsonToConnectionIdsAsync(
                connectionIds,
                eventId,
                jsonArgument);
        }

        private static async Task<int> TrySendPushJsonToConnectionIdsAsync(
            IEnumerable<string> connectionIds,
            string eventId,
            string jsonArgument)
        {
            string[] ids = connectionIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (ids.Length == 0)
                return 0;

            int deliveredSockets = await TrySendTargetToConnectionsAsync(
                    ids,
                    ClientNotificationHubTarget,
                    new object?[] { jsonArgument });

            string readableEvent = PushTargetNames.TryGetValue(
                eventId,
                out string? eventName)
                ? $"{eventId} ({eventName})"
                : eventId;
            Console.WriteLine(
                $"[Push] hubTarget={ClientNotificationHubTarget}, " +
                $"event={readableEvent}, sockets={deliveredSockets}, " +
                $"idJsonType=string, bytes={jsonArgument.Length}");
            return deliveredSockets;
        }

        private static string NormalizeClientNotificationId(string eventId)
        {

            if (string.Equals(
                    eventId,
                    "11",
                    StringComparison.Ordinal) ||
                string.Equals(
                    eventId,
                    "SubscriptionUpdateProfile",
                    StringComparison.Ordinal))
            {
                return AccountUpdateNotificationId;
            }

            if (string.Equals(
                    eventId,
                    SubscriptionUpdatePresenceTarget,
                    StringComparison.Ordinal) ||
                string.Equals(
                    eventId,
                    "SubscriptionUpdatePresence",
                    StringComparison.Ordinal))
            {
                return PresenceUpdateNotificationId;
            }

            foreach (KeyValuePair<string, string> entry in PushTargetNames)
            {
                if (string.Equals(
                        entry.Value,
                        eventId,
                        StringComparison.Ordinal))
                {
                    return entry.Key;
                }
            }

            return eventId;
        }

        private static string CreateClientNotificationJson(
            string eventId,
            object? message)
        {

            string normalizedEventId = NormalizeClientNotificationId(eventId);

            return message == null
                ? SystemJsonSerializer.Serialize(new
                {
                    Id = normalizedEventId
                })
                : SystemJsonSerializer.Serialize(new
                {
                    Id = normalizedEventId,
                    Msg = message
                });
        }

        public static Task NotifyRoomUpdatedAsync(
            long recipientPlayerId,
            object roomUpdate) =>
            SendPushEventToPlayerAsync(
                recipientPlayerId,
                RoomUpdateNotificationId,
                roomUpdate);

        private static async Task SendTargetToConnectionsAsync(
            IEnumerable<string> connectionIds,
            string target,
            object?[] arguments)
        {
            await TrySendTargetToConnectionsAsync(
                connectionIds,
                target,
                arguments);
        }

        private static async Task<int> TrySendTargetToConnectionsAsync(
            IEnumerable<string> connectionIds,
            string target,
            object?[] arguments)
        {
            IHubContext<NotificationHub>? hubContext = _hubContext;
            if (hubContext == null)
            {
                Console.WriteLine(
                    $"[SignalR] Tried to send {target} before broadcaster initialization");
                return 0;
            }

            string[] ids = connectionIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Where(ConnectionPlayers.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (ids.Length == 0)
                return 0;

            try
            {
                await hubContext.Clients
                    .Clients(ids)
                    .SendCoreAsync(
                        target,
                        arguments,
                        CancellationToken.None);
                return ids.Length;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[SignalR] Send target={target} failed: {ex.Message}");
                return 0;
            }
        }

        private static bool TryGetPropertyIgnoreCase(
            JsonElement element,
            string propertyName,
            out JsonElement value)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }

    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    [Route("/noti/debug/v1")]
    public sealed class NotiDebugController : ControllerBase
    {
        [HttpGet("status")]
        public IActionResult Status()
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            return Ok(NotiController.GetDebugStatus(playerId.Value));
        }
    }
}
