using LiteDB;
using System.Text.Json;
using System.Text.Json.Serialization;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Mocha2023.Classes.DBs
{

    public static class NotificationDB
    {
        private static readonly object Sync = new();
        private static readonly TimeSpan AcknowledgedInviteGrace =
            TimeSpan.FromMinutes(5);
        private static readonly LiteDatabase Database =
            new(Path.Combine(Program.dataDir, "DBs", "Notifications.db"));
        private static readonly ILiteCollection<StoredNotification> Messages =
            Database.GetCollection<StoredNotification>("Messages", BsonAutoId.Int64);
        private static readonly ILiteCollection<StoredRoomInviteGrant> RoomInviteGrants =
            Database.GetCollection<StoredRoomInviteGrant>("RoomInviteGrants");

        static NotificationDB()
        {
            Messages.EnsureIndex(value => value.RecipientPlayerId);
            Messages.EnsureIndex(value => value.CreatedAt);
            RoomInviteGrants.EnsureIndex(value => value.RecipientPlayerId);
            RoomInviteGrants.EnsureIndex(value => value.RoomInstanceId);
            RoomInviteGrants.EnsureIndex(value => value.ExpirationTime);

            lock (Sync)
            {
                DeleteExpiredNoLock();
                foreach (StoredNotification message in Messages
                             .FindAll()
                             .Where(value =>
                                 IsRoomInviteMessageType(value.Type))
                             .ToList())
                {
                    if (message.FromPlayerId <= 0 ||
                        message.RecipientPlayerId <= 0 ||
                        (message.RoomId ?? 0) <= 0 ||
                        !TryReadRoomInviteId(message, out long roomInstanceId))
                    {
                        continue;
                    }

                    string wireData = roomInstanceId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (message.Type != (int)MessageType.GameInvite ||
                        !string.Equals(
                            message.Message,
                            wireData,
                            StringComparison.Ordinal))
                    {
                        message.Type = (int)MessageType.GameInvite;
                        message.Message = wireData;
                        Messages.Update(message);
                    }

                    RoomInviteGrants.Upsert(new StoredRoomInviteGrant
                    {
                        MessageId = message.Id,
                        RecipientPlayerId = message.RecipientPlayerId,
                        InviterPlayerId = message.FromPlayerId,
                        RoomId = message.RoomId!.Value,
                        RoomInstanceId = roomInstanceId,
                        CreatedAt = message.CreatedAt,
                        ExpirationTime = message.ExpirationTime
                    });
                }
            }
        }

        public enum MessageType
        {
            GameInvite = 0,
            GameInviteDeclined = 1,
            GameJoinFailed = 2,
            PartyActivitySwitch = 3,
            FriendInvite = 4,
            VoteToKick = 5,
            GameInviteV2 = 6,
            PartyActivitySwitchV2 = 7,
            RequestGameInvite = 10,
            RequestGameInviteDeclined = 11,
            FriendStatusOnline = 20,
            TextMessage = 30,
            FriendRequestAccepted = 40,
            PlayerCheer = 50,
            PlayerCheerAnonymous = 51,
            PartyUpRequest = 120,
            FriendIntroduction = 130
        }

        public sealed class StoredNotification
        {
            [BsonId]
            public long Id { get; set; }
            public long RecipientPlayerId { get; set; }
            public int FromPlayerId { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public int Type { get; set; }
            public string Message { get; set; } = "{}";
            public long? RoomId { get; set; }
            public long? RoomInstanceId { get; set; }
            public long? PlayerEventId { get; set; }
            public long? ClubId { get; set; }
            public DateTime ExpirationTime { get; set; } = DateTime.UtcNow.AddDays(7);
        }

        public sealed class StoredRoomInviteGrant
        {
            [BsonId]
            public long MessageId { get; set; }
            public long RecipientPlayerId { get; set; }
            public long InviterPlayerId { get; set; }
            public long RoomId { get; set; }
            public long RoomInstanceId { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime ExpirationTime { get; set; } = DateTime.UtcNow.AddHours(2);
        }

        public sealed class ClientNotification
        {
            public long Id { get; set; }
            public int FromPlayerId { get; set; }
            public long ToPlayerId { get; set; }

            [JsonPropertyName("SentTime")]
            public DateTime CreatedAt { get; set; }

            public int Type { get; set; }

            [JsonPropertyName("Data")]
            public string Message { get; set; } = "{}";

            [JsonIgnore]
            public long? FromAccountId { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public long? RoomId { get; set; }

            [JsonIgnore]
            public long? RoomInstanceId { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public long? PlayerEventId { get; set; }

            [JsonIgnore]
            public long? ClubId { get; set; }

            [JsonIgnore]
            public DateTime ExpirationTime { get; set; }
        }

        public static ClientNotification CreateRoomInvite(
            long inviterPlayerId,
            long receiverPlayerId,
            long roomId,
            long roomInstanceId,
            string? photonRoomId)
        {

            var inviterInstance = PlayerDB
                .GetPlayerHeartbeat(inviterPlayerId)
                .roomInstance;
            bool isExactInviterInstance =
                inviterInstance != null &&
                inviterInstance.roomId == roomId &&
                inviterInstance.roomInstanceId == roomInstanceId;
            string roomName = RoomDB.GetRoom(roomId)?.Name ?? "Room";
            return CreateRoomInvite(
                inviterPlayerId,
                receiverPlayerId,
                roomId,
                roomInstanceId,
                roomName,
                inviteMode:
                    isExactInviterInstance &&
                    Mocha2023.Classes.Sessions.IsRestrictedInstance(
                        inviterInstance)
                        ? 1
                        : 0,
                photonRoomId: isExactInviterInstance
                    ? inviterInstance!.photonRoomId
                    : photonRoomId);
        }

        public static ClientNotification CreateRoomInvite(
            long inviterPlayerId,
            long receiverPlayerId,
            long roomId,
            long roomInstanceId,
            string? roomName,
            int inviteMode,
            string? photonRoomId = null)
        {
            if (inviterPlayerId is <= 0 or > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(inviterPlayerId));
            if (receiverPlayerId <= 0)
                throw new ArgumentOutOfRangeException(nameof(receiverPlayerId));
            if (roomId <= 0)
                throw new ArgumentOutOfRangeException(nameof(roomId));
            if (roomInstanceId <= 0)
                throw new ArgumentOutOfRangeException(nameof(roomInstanceId));

            var stored = new StoredNotification
            {
                RecipientPlayerId = receiverPlayerId,
                FromPlayerId = checked((int)inviterPlayerId),
                CreatedAt = DateTime.UtcNow,
                Type = (int)MessageType.GameInvite,
                Message = roomInstanceId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                RoomId = roomId,
                RoomInstanceId = roomInstanceId,
                PlayerEventId = null,
                ExpirationTime = DateTime.UtcNow.AddHours(2)
            };

            lock (Sync)
            {
                DeleteExpiredNoLock();

                List<StoredRoomInviteGrant> replacedGrants = RoomInviteGrants
                    .Find(value =>
                        value.RecipientPlayerId == receiverPlayerId &&
                        value.InviterPlayerId == inviterPlayerId &&
                        value.RoomInstanceId == roomInstanceId)
                    .ToList();
                foreach (StoredRoomInviteGrant grant in replacedGrants)
                {
                    Messages.Delete(grant.MessageId);
                    RoomInviteGrants.Delete(grant.MessageId);
                }

                Messages.Insert(stored);

                RoomInviteGrants.Upsert(new StoredRoomInviteGrant
                {
                    MessageId = stored.Id,
                    RecipientPlayerId = receiverPlayerId,
                    InviterPlayerId = inviterPlayerId,
                    RoomId = roomId,
                    RoomInstanceId = roomInstanceId,
                    CreatedAt = stored.CreatedAt,
                    ExpirationTime = stored.ExpirationTime
                });

                Console.WriteLine(
                    $"[ROOM INVITE GRANT] issued message={stored.Id} " +
                    $"from={inviterPlayerId} to={receiverPlayerId} " +
                    $"room={roomId} instance={roomInstanceId} " +
                    $"replaced={replacedGrants.Count}");

                return ToClient(stored);
            }
        }

        public static ClientNotification CreatePartyInvite(
            long inviterPlayerId,
            long receiverPlayerId,
            long partyId)
        {
            var messageData = new
            {
                FromAccountId = checked((int)inviterPlayerId),
                InviterPlayerId = inviterPlayerId,
                PartyId = partyId
            };

            return Insert(
                inviterPlayerId,
                receiverPlayerId,
                MessageType.PartyUpRequest,
                messageData);
        }

        public static ClientNotification CreatePlayerMessage(
            long senderPlayerId,
            long receiverPlayerId,
            MessageType type,
            string? message,
            long? roomId = null)
        {
            return InsertMessage(
                senderPlayerId,
                receiverPlayerId,
                type,
                message ?? string.Empty,
                roomId);
        }

        public static ClientNotification CreateFriendInvite(
            long senderPlayerId,
            long receiverPlayerId)
        {
            return Insert(
                senderPlayerId,
                receiverPlayerId,
                MessageType.FriendInvite,
                new
                {
                    FromAccountId = checked((int)senderPlayerId),
                    FromPlayerId = checked((int)senderPlayerId),
                    FriendPlayerId = checked((int)senderPlayerId)
                });
        }

        public static ClientNotification CreateFriendAccepted(
            long acceptingPlayerId,
            long receiverPlayerId)
        {
            return Insert(
                acceptingPlayerId,
                receiverPlayerId,
                MessageType.FriendRequestAccepted,
                new
                {
                    FromAccountId = checked((int)acceptingPlayerId),
                    FromPlayerId = checked((int)acceptingPlayerId),
                    FriendPlayerId = checked((int)acceptingPlayerId)
                });
        }

        public static ClientNotification CreateFriendIntroduction(
            long senderPlayerId,
            long receiverPlayerId,
            long aboutPlayerId)
        {
            if (aboutPlayerId is <= 0 or > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(aboutPlayerId));

            int aboutId = checked((int)aboutPlayerId);
            return Insert(
                senderPlayerId,
                receiverPlayerId,
                MessageType.FriendIntroduction,
                new
                {
                    FromAccountId = checked((int)senderPlayerId),
                    FromPlayerId = checked((int)senderPlayerId),
                    AboutAccountId = aboutId,
                    AboutPlayerId = aboutId,
                    IntroducedPlayerId = aboutId
                });
        }

        public static ClientNotification CreatePlayerCheer(
            long senderPlayerId,
            long receiverPlayerId,
            int category,
            bool anonymous)
        {
            return InsertMessage(
                senderPlayerId,
                receiverPlayerId,
                anonymous
                    ? MessageType.PlayerCheerAnonymous
                    : MessageType.PlayerCheer,
                category.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hideSender: anonymous);
        }

        public static List<ClientNotification> GetMessages(long recipientPlayerId)
        {
            lock (Sync)
            {
                DeleteExpiredNoLock();
                return Messages.Find(value => value.RecipientPlayerId == recipientPlayerId)
                    .OrderByDescending(value => value.CreatedAt)
                    .Select(ToClient)
                    .ToList();
            }
        }

        public static int DeleteMessages(
            long recipientPlayerId,
            IEnumerable<long> messageIds)
        {
            HashSet<long> ids = messageIds
                .Where(value => value > 0)
                .ToHashSet();
            if (ids.Count == 0)
                return 0;

            lock (Sync)
            {
                DeleteExpiredNoLock();
                DateTime graceExpiration =
                    DateTime.UtcNow.Add(AcknowledgedInviteGrace);
                int deleted = 0;
                var acknowledgedGrantIds = new HashSet<long>();
                foreach (StoredNotification message in Messages
                             .Find(value =>
                                 value.RecipientPlayerId == recipientPlayerId)
                             .ToList())
                {
                    bool matchesMessageId = ids.Contains(message.Id);
                    bool matchesInviteId =
                        IsRoomInviteMessageType(message.Type) &&
                        TryReadRoomInviteId(message, out long roomInviteId) &&
                        ids.Contains(roomInviteId);

                    if ((matchesMessageId || matchesInviteId) &&
                        Messages.Delete(message.Id))
                    {
                        deleted++;

                        if (IsRoomInviteMessageType(message.Type))
                            acknowledgedGrantIds.Add(message.Id);
                    }
                }

                foreach (StoredRoomInviteGrant grant in RoomInviteGrants
                             .Find(value =>
                                 value.RecipientPlayerId == recipientPlayerId)
                             .Where(value =>
                                 acknowledgedGrantIds.Contains(value.MessageId) ||
                                 ids.Contains(value.MessageId) ||
                                 ids.Contains(value.RoomInstanceId))
                             .ToList())
                {
                    if (grant.ExpirationTime > graceExpiration)
                    {
                        grant.ExpirationTime = graceExpiration;
                        RoomInviteGrants.Update(grant);
                    }

                    acknowledgedGrantIds.Add(grant.MessageId);
                }

                if (acknowledgedGrantIds.Count > 0)
                {
                    Console.WriteLine(
                        $"[ROOM INVITE ACK] recipient={recipientPlayerId} " +
                        $"grants={acknowledgedGrantIds.Count} " +
                        $"graceSeconds={(int)AcknowledgedInviteGrace.TotalSeconds}");
                }

                return deleted;
            }
        }

        public static int ConsumeRoomInvite(
            long recipientPlayerId,
            long inviteId)
        {
            if (recipientPlayerId <= 0 || inviteId <= 0)
                return 0;

            lock (Sync)
            {
                DeleteExpiredNoLock();

                List<StoredRoomInviteGrant> grants = RoomInviteGrants
                    .Find(value =>
                        value.RecipientPlayerId == recipientPlayerId)
                    .Where(value =>
                        value.MessageId == inviteId ||
                        value.RoomInstanceId == inviteId)
                    .ToList();

                HashSet<long> messageIds = grants
                    .Select(value => value.MessageId)
                    .ToHashSet();

                foreach (StoredNotification message in Messages
                             .Find(value =>
                                 value.RecipientPlayerId == recipientPlayerId)
                             .Where(value =>
                                 IsRoomInviteMessageType(value.Type))
                             .ToList())
                {
                    if (message.Id == inviteId ||
                        (TryReadRoomInviteId(message, out long roomInviteId) &&
                         roomInviteId == inviteId))
                    {
                        messageIds.Add(message.Id);
                    }
                }

                int removed = 0;
                foreach (long messageId in messageIds)
                {
                    if (Messages.Delete(messageId))
                        removed++;
                    RoomInviteGrants.Delete(messageId);
                }

                foreach (StoredRoomInviteGrant grant in grants)
                    RoomInviteGrants.Delete(grant.MessageId);

                return removed + grants.Count;
            }
        }

        public static bool TryGetRoomInvite(
            long recipientPlayerId,
            long inviteId,
            out long inviterPlayerId,
            out long roomId,
            out long roomInstanceId)
        {
            inviterPlayerId = 0;
            roomId = 0;
            roomInstanceId = 0;

            if (recipientPlayerId <= 0 || inviteId <= 0)
                return false;

            lock (Sync)
            {
                DeleteExpiredNoLock();

                StoredNotification? message = Messages
                    .Find(value =>
                        value.RecipientPlayerId == recipientPlayerId)
                    .Where(value =>
                        IsRoomInviteMessageType(value.Type))
                    .OrderByDescending(value => value.CreatedAt)
                    .FirstOrDefault(value =>
                        value.Id == inviteId ||
                        (TryReadRoomInviteId(value, out long candidateInviteId) &&
                         candidateInviteId == inviteId));

                if (message != null)
                {
                    inviterPlayerId = message.FromPlayerId;
                    roomId = message.RoomId ?? 0;

                    if (!TryReadRoomInviteId(message, out roomInstanceId))
                        return false;

                    RoomInviteGrants.Upsert(new StoredRoomInviteGrant
                    {
                        MessageId = message.Id,
                        RecipientPlayerId = message.RecipientPlayerId,
                        InviterPlayerId = message.FromPlayerId,
                        RoomId = roomId,
                        RoomInstanceId = roomInstanceId,
                        CreatedAt = message.CreatedAt,
                        ExpirationTime = message.ExpirationTime
                    });
                }
                else
                {

                    StoredRoomInviteGrant? grant = RoomInviteGrants
                        .Find(value =>
                            value.RecipientPlayerId == recipientPlayerId)
                        .OrderByDescending(value => value.CreatedAt)
                        .FirstOrDefault(value =>
                            value.MessageId == inviteId ||
                            value.RoomInstanceId == inviteId);

                    if (grant == null)
                        return false;

                    inviterPlayerId = grant.InviterPlayerId;
                    roomId = grant.RoomId;
                    roomInstanceId = grant.RoomInstanceId;
                }

                return inviterPlayerId > 0 &&
                       roomId > 0 &&
                       roomInstanceId > 0;
            }
        }

        public static bool TryGetLatestRoomInviteForRoom(
            long recipientPlayerId,
            long roomId,
            out long inviteId,
            out long inviterPlayerId,
            out long roomInstanceId)
        {
            inviteId = 0;
            inviterPlayerId = 0;
            roomInstanceId = 0;

            if (recipientPlayerId <= 0 || roomId <= 0)
                return false;

            lock (Sync)
            {
                DeleteExpiredNoLock();

                StoredRoomInviteGrant? grant = RoomInviteGrants
                    .Find(value =>
                        value.RecipientPlayerId == recipientPlayerId &&
                        value.RoomId == roomId)
                    .OrderByDescending(value => value.CreatedAt)
                    .FirstOrDefault();

                if (grant == null)
                    return false;

                inviteId = grant.MessageId;
                inviterPlayerId = grant.InviterPlayerId;
                roomInstanceId = grant.RoomInstanceId;
                return inviteId > 0 &&
                       inviterPlayerId > 0 &&
                       roomInstanceId > 0;
            }
        }

        public static bool HasActiveRoomInvite(
            long recipientPlayerId,
            long inviterPlayerId,
            long roomInstanceId,
            long? expectedRoomId = null)
        {
            if (recipientPlayerId <= 0 ||
                inviterPlayerId <= 0 ||
                roomInstanceId <= 0)
            {
                return false;
            }

            return TryGetRoomInvite(
                       recipientPlayerId,
                       roomInstanceId,
                       out long resolvedInviterPlayerId,
                       out long resolvedRoomId,
                       out long resolvedRoomInstanceId) &&
                   resolvedInviterPlayerId == inviterPlayerId &&
                   resolvedRoomInstanceId == roomInstanceId &&
                   (!expectedRoomId.HasValue ||
                    resolvedRoomId == expectedRoomId.Value);
        }

        private static bool TryReadRoomInviteId(
            StoredNotification message,
            out long roomInstanceId)
        {
            roomInstanceId = message.RoomInstanceId ?? 0;
            if (roomInstanceId > 0)
                return true;

            if (string.IsNullOrWhiteSpace(message.Message))
                return false;

            if (long.TryParse(
                    message.Message,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out roomInstanceId) &&
                roomInstanceId > 0)
            {
                return true;
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(message.Message);
                JsonElement root = document.RootElement;

                foreach (string propertyName in new[]
                         {
                             "InviteId",
                             "inviteId",
                             "RoomInstanceId",
                             "roomInstanceId",
                             "TargetRoomInstanceId",
                             "targetRoomInstanceId"
                         })
                {
                    if (!root.TryGetProperty(
                            propertyName,
                            out JsonElement value))
                    {
                        continue;
                    }

                    if (value.ValueKind == JsonValueKind.Number &&
                        value.TryGetInt64(out long numericValue) &&
                        numericValue > 0)
                    {
                        roomInstanceId = numericValue;
                        return true;
                    }

                    if (value.ValueKind == JsonValueKind.String &&
                        long.TryParse(value.GetString(), out long stringValue) &&
                        stringValue > 0)
                    {
                        roomInstanceId = stringValue;
                        return true;
                    }
                }
            }
            catch (JsonException)
            {
                return false;
            }

            return false;
        }

        public static int DeletePlayerCheerMessages(long recipientPlayerId)
        {
            if (recipientPlayerId <= 0)
                return 0;

            lock (Sync)
            {
                int deleted = 0;
                foreach (StoredNotification message in Messages
                             .Find(value =>
                                 value.RecipientPlayerId == recipientPlayerId)
                             .Where(value =>
                                 value.Type == (int)MessageType.PlayerCheer ||
                                 value.Type ==
                                 (int)MessageType.PlayerCheerAnonymous)
                             .ToList())
                {
                    if (Messages.Delete(message.Id))
                        deleted++;
                }

                return deleted;
            }
        }

        private static ClientNotification Insert(
            long fromPlayerId,
            long recipientPlayerId,
            MessageType type,
            object messageData,
            long? roomId = null,
            long? playerEventId = null,
            long? clubId = null)
        {
            return InsertMessage(
                fromPlayerId,
                recipientPlayerId,
                type,
                JsonSerializer.Serialize(messageData),
                roomId,
                playerEventId,
                clubId);
        }

        private static ClientNotification InsertMessage(
            long fromPlayerId,
            long recipientPlayerId,
            MessageType type,
            string message,
            long? roomId = null,
            long? playerEventId = null,
            long? clubId = null,
            bool hideSender = false)
        {
            if (fromPlayerId is <= 0 or > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(fromPlayerId));
            if (recipientPlayerId <= 0)
                throw new ArgumentOutOfRangeException(nameof(recipientPlayerId));

            var stored = new StoredNotification
            {
                RecipientPlayerId = recipientPlayerId,
                FromPlayerId = hideSender
                    ? 0
                    : checked((int)fromPlayerId),
                CreatedAt = DateTime.UtcNow,
                Type = (int)type,
                Message = message,
                RoomId = roomId,
                PlayerEventId = playerEventId,
                ClubId = clubId,
                ExpirationTime = DateTime.UtcNow.AddDays(7)
            };

            lock (Sync)
            {
                DeleteExpiredNoLock();
                Messages.Insert(stored);
                return ToClient(stored);
            }
        }

        private static ClientNotification ToClient(StoredNotification value) => new()
        {
            Id = value.Id,
            FromPlayerId = value.FromPlayerId,
            ToPlayerId = value.RecipientPlayerId,
            CreatedAt = value.CreatedAt,
            Type = value.Type,
            Message = value.Message,
            FromAccountId = value.FromPlayerId,
            RoomId = value.RoomId,
            RoomInstanceId = value.RoomInstanceId,
            PlayerEventId = value.PlayerEventId,
            ClubId = value.ClubId,
            ExpirationTime = value.ExpirationTime
        };

        public static bool IsRoomInviteMessageType(int type) =>
            type is
                (int)MessageType.GameInvite or
                (int)MessageType.GameInviteV2;

        private static void DeleteExpiredNoLock()
        {
            DateTime now = DateTime.UtcNow;
            foreach (StoredNotification message in Messages.Find(value =>
                         value.ExpirationTime < now).ToList())
            {
                Messages.Delete(message.Id);
            }

            foreach (StoredRoomInviteGrant grant in RoomInviteGrants.Find(value =>
                         value.ExpirationTime < now).ToList())
            {
                RoomInviteGrants.Delete(grant.MessageId);
            }
        }
    }
}
