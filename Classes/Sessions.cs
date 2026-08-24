using Mocha2023.Classes.DBs;
using Mocha2023.Classes.DBs.DBClasses;
using static Mocha2023.Classes.DBs.DBClasses.PlayerDBClasses;
using static Mocha2023.Classes.DBs.DBClasses.RoomDBClasses;

namespace Mocha2023.Classes
{
    public class Sessions
    {
        private static readonly Random _rng = new();
        private static readonly object _matchmakingLock = new();
        private const long ActiveHeartbeatWindowSeconds = 120;
        private static readonly Dictionary<long, HashSet<long>>
            _confirmedParticipantsByInstance = new();
        private static readonly Dictionary<long, long>
            _instanceOwners = new();
        private static readonly Dictionary<long, DateTime>
            _guestDormEnteredAt = new();
        private static readonly TimeSpan GuestDormHoldWindow =
            TimeSpan.FromSeconds(15);

        public static Heartbeat? CreateRoom(
            long playerId,
            long roomId,
            string sceneName = "",
            bool isPrivate = false)
        {
            return CreateRoomInternal(
                playerId,
                roomId,
                sceneName,
                exactSubRoomId: null,
                isPrivate: isPrivate);
        }

        public static Heartbeat? CreateRoom(
            long playerId,
            long roomId,
            long subRoomId,
            bool isPrivate = false)
        {
            return CreateRoomInternal(
                playerId,
                roomId,
                sceneName: null,
                exactSubRoomId: subRoomId,
                isPrivate: isPrivate);
        }

        private static Heartbeat? CreateRoomInternal(
            long playerId,
            long roomId,
            string? sceneName,
            long? exactSubRoomId,
            bool isPrivate)
        {
            var room = RoomDB.GetRoom(roomId);

            if (room?.SubRooms == null || room.SubRooms.Count == 0)
                return null;

            SubRooms? subRoom = null;

            if (exactSubRoomId.HasValue)
            {
                subRoom = room.SubRooms.FirstOrDefault(sub =>
                    sub.SubRoomId == exactSubRoomId.Value);

                if (subRoom == null)
                    return null;
            }
            else if (!string.IsNullOrWhiteSpace(sceneName))
            {
                subRoom = room.SubRooms.FirstOrDefault(sub =>
                    string.Equals(
                        sub.Name,
                        sceneName,
                        StringComparison.OrdinalIgnoreCase));
            }

            if (!exactSubRoomId.HasValue)
            {

                subRoom ??= room.SubRooms
                    .Where(sub => sub.Accessibility != RoomAccessibility.Private)
                    .OrderByDescending(sub => string.Equals(
                        sub.Name, "Home", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                subRoom ??= room.SubRooms.FirstOrDefault(sub =>
                    string.Equals(
                        sub.Name,
                        "Home",
                        StringComparison.OrdinalIgnoreCase));

                subRoom ??= room.SubRooms.FirstOrDefault();
            }

            if (subRoom == null)
                return null;

            lock (_matchmakingLock)
            {
                if (!RoomDB.CanPlayerAccessSubRoom(
                        room,
                        subRoom,
                        playerId) ||
                    RoomDB.IsPlayerBannedFromRoom(room.RoomId, playerId))
                {
                    Console.WriteLine(
                        $"[ROOM ACCESS DENIED] player={playerId} " +
                        $"room={room.RoomId} subroom={subRoom.SubRoomId} " +
                        $"isDorm={room.IsDorm} accessibility={room.Accessibility} " +
                        $"subroomAccessibility={subRoom.Accessibility}");
                    return null;
                }

                bool effectivePrivate =
                    isPrivate ||
                    room.IsDorm ||
                    room.Accessibility == RoomAccessibility.Private ||
                    subRoom.Accessibility == RoomAccessibility.Private;

                if (!effectivePrivate)
                {
                    var existing = FindBestPublicInstance(
                        playerId,
                        room.RoomId,
                        subRoom.SubRoomId,
                        subRoom.MaxPlayers);

                    if (existing != null)
                    {
                        var joinedSession = CloneInstance(existing.Value.Instance);
                        joinedSession.isFull =
                            existing.Value.PlayerCount + 1 >= joinedSession.maxCapacity;
                        RemovePlayerFromConfirmedInstancesNoLock(playerId);
                        TrackParticipantNoLock(
                            playerId,
                            joinedSession.roomInstanceId);

                        Console.WriteLine(
                            $"[MATCHMAKING] player={playerId} joined existing " +
                            $"room={room.RoomId} instance={joinedSession.roomInstanceId} " +
                            $"players={existing.Value.PlayerCount + 1}/{joinedSession.maxCapacity}");

                        return PlayerDB.UpdatePlayerHeartbeat(playerId, joinedSession);
                    }
                }

                bool hasPersistencePair = RoomDB.TryGetPersistencePair(
                    room,
                    subRoom,
                    out var dataSave,
                    out string roomDataBlob,
                    out _);
                long instanceId = _rng.NextInt64(1_000_000L, 1_000_000_000_000L);
                string roomName = room.Name ?? "UnknownRoom";

                var session = new RoomInstance
                {
                    Name = $"^{roomName}",

                    dataBlob = hasPersistencePair ? roomDataBlob : string.Empty,
                    isPrivate = effectivePrivate,
                    location = subRoom.UnitySceneId,
                    maxCapacity = Math.Max(1, subRoom.MaxPlayers),
                    photonRegion = ServerConfig.PhotonRegion,
                    photonRegionId = ServerConfig.PhotonRegion,
                    photonRoomId = effectivePrivate
                        ? $"MochaRoom-{roomName}-private-{instanceId}-{Guid.NewGuid()}"
                        : $"MochaRoom-{roomName}-{instanceId}",
                    roomId = room.RoomId,
                    roomInstanceId = instanceId,
                    roomInstanceType = effectivePrivate
                        ? RoomInstanceType.Private
                        : RoomInstanceType.Public,
                    subRoomId = subRoom.SubRoomId,
                    subRoomDataSaveId = dataSave?.SubRoomDataSaveId ?? 0,
                    createdAt = DateTime.UtcNow
                };
                RemovePlayerFromConfirmedInstancesNoLock(playerId);
                TrackParticipantNoLock(playerId, session.roomInstanceId);
                if (IsRestrictedInstance(session))
                    _instanceOwners[session.roomInstanceId] = playerId;

                Console.WriteLine(
                    $"[ROOM LOAD] room={room.RoomId} " +
                    $"subroom={subRoom.SubRoomId} " +
                    $"name={subRoom.Name} " +
                    $"instance={session.roomInstanceId} new=true " +
                    $"superRoomBlob={room.DataBlob ?? "null"} " +
                    $"subRoomBlob={subRoom.DataBlob ?? "null"} " +
                    $"subRoomDataSaveId={session.subRoomDataSaveId}");

                return PlayerDB.UpdatePlayerHeartbeat(playerId, session);
            }
        }

        private static (RoomInstance Instance, int PlayerCount)? FindBestPublicInstance(
            long playerId,
            long roomId,
            long subRoomId,
            int configuredCapacity)
        {
            long activeAfter = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                - ActiveHeartbeatWindowSeconds;

            return PlayerDB.Players.FindAll()
                .Where(player => player.PlayerId != playerId)
                .Select(player => player.Player?.PlayerExtra?.Heartbeat)
                .Where(heartbeat =>
                    heartbeat != null &&
                    heartbeat.isOnline &&
                    heartbeat.lastHeartbeatUnixTime >= activeAfter &&
                    heartbeat.roomInstance != null &&

                    !heartbeat.roomInstance.isPrivate &&
                    heartbeat.roomInstance.roomId == roomId &&
                    heartbeat.roomInstance.subRoomId == subRoomId &&
                    !string.IsNullOrWhiteSpace(heartbeat.roomInstance.photonRoomId))
                .GroupBy(heartbeat => heartbeat!.roomInstance!.roomInstanceId)
                .Select(group =>
                {
                    var instance = group.First()!.roomInstance!;
                    int playerCount = group
                        .Select(heartbeat => heartbeat!.playerId)
                        .Distinct()
                        .Count();
                    int capacity = Math.Max(
                        1,
                        instance.maxCapacity > 0
                            ? instance.maxCapacity
                            : configuredCapacity);

                    return new
                    {
                        Instance = instance,
                        PlayerCount = playerCount,
                        Capacity = capacity
                    };
                })
                .Where(candidate => candidate.PlayerCount < candidate.Capacity)
                .OrderByDescending(candidate => candidate.PlayerCount)
                .ThenBy(candidate => candidate.Instance.roomInstanceId)
                .Select(candidate => ((RoomInstance Instance, int PlayerCount)?)
                    (candidate.Instance, candidate.PlayerCount))
                .FirstOrDefault();
        }

        private static RoomInstance CloneInstance(RoomInstance source)
        {
            return new RoomInstance
            {
                encryptVoiceChat = source.encryptVoiceChat,
                clubId = source.clubId,
                dataBlob = source.dataBlob,
                eventId = source.eventId,
                isFull = source.isFull,
                isInProgress = source.isInProgress,
                isPrivate = source.isPrivate,
                location = source.location,
                maxCapacity = source.maxCapacity,
                Name = source.Name,
                photonRegion = source.photonRegion,
                photonRegionId = source.photonRegionId,
                photonRoomId = source.photonRoomId,
                roomCode = source.roomCode,
                roomId = source.roomId,
                roomInstanceId = source.roomInstanceId,
                roomInstanceType = source.roomInstanceType,
                subRoomId = source.subRoomId,
                subRoomDataSaveId = source.subRoomDataSaveId,
                createdAt = source.createdAt
            };
        }

        public static bool IsRestrictedInstance(RoomInstance? instance) =>
            instance != null &&
            (instance.isPrivate ||
             instance.roomInstanceType is
                 RoomInstanceType.Private or RoomInstanceType.Dormroom);

        public static Heartbeat? JoinRoomInstance(
            long playerId,
            long roomInstanceId)
        {
            return JoinRoomInstanceInternal(
                playerId,
                roomInstanceId,
                invitedByPlayerId: null,
                expectedRoomId: null);
        }

        public static Heartbeat? JoinInvitedRoomInstance(
            long playerId,
            long roomInstanceId,
            long invitedByPlayerId,
            long expectedRoomId)
        {
            if (invitedByPlayerId <= 0 || expectedRoomId <= 0)
                return null;

            return JoinRoomInstanceInternal(
                playerId,
                roomInstanceId,
                invitedByPlayerId,
                expectedRoomId);
        }

        private static Heartbeat? JoinRoomInstanceInternal(
            long playerId,
            long roomInstanceId,
            long? invitedByPlayerId,
            long? expectedRoomId)
        {
            if (playerId <= 0 || roomInstanceId <= 0)
                return null;

            lock (_matchmakingLock)
            {
                long activeAfter = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    - ActiveHeartbeatWindowSeconds;

                var occupants = PlayerDB.Players.FindAll()
                    .Select(player => player.Player?.PlayerExtra?.Heartbeat)
                    .Where(heartbeat =>
                        heartbeat != null &&
                        heartbeat.isOnline &&
                        heartbeat.lastHeartbeatUnixTime >= activeAfter &&
                        heartbeat.roomInstance != null &&
                        heartbeat.roomInstance.roomInstanceId == roomInstanceId &&

                        !string.IsNullOrWhiteSpace(
                            heartbeat.roomInstance.photonRoomId))
                    .ToList();

                RoomInstance? source = occupants
                    .OrderByDescending(heartbeat =>
                        heartbeat!.lastHeartbeatUnixTime)
                    .Select(heartbeat => heartbeat!.roomInstance)
                    .FirstOrDefault();
                if (source == null)
                    return null;

                if (expectedRoomId.HasValue &&
                    source.roomId != expectedRoomId.Value)
                {
                    Console.WriteLine(
                        $"[PRIVATE JOIN DENIED] player={playerId} " +
                        $"instance={roomInstanceId} reason=invite-room-mismatch");
                    return null;
                }

                bool alreadyConfirmed = occupants.Any(heartbeat =>
                    heartbeat!.playerId == playerId);
                bool isOwner = _instanceOwners.TryGetValue(
                    roomInstanceId,
                    out long ownerPlayerId) &&
                    ownerPlayerId == playerId;
                bool inviterIsPresent = invitedByPlayerId.HasValue &&
                    invitedByPlayerId.Value != playerId &&
                    occupants.Any(heartbeat =>
                        heartbeat!.playerId == invitedByPlayerId.Value);

                bool hasVerifiedInviteGrant =
                    invitedByPlayerId.HasValue &&
                    invitedByPlayerId.Value != playerId &&
                    NotificationDB.HasActiveRoomInvite(
                        playerId,
                        invitedByPlayerId.Value,
                        roomInstanceId,
                        source.roomId);

                var sourceRoom = RoomDB.GetRoom(source.roomId);
                var sourceSubRoom = sourceRoom?.SubRooms?.FirstOrDefault(
                    subRoom => subRoom.SubRoomId == source.subRoomId);
                bool persistentRoomIsRestricted =
                    sourceRoom?.IsDorm == true ||
                    sourceRoom?.Accessibility == RoomAccessibility.Private ||
                    sourceSubRoom?.Accessibility == RoomAccessibility.Private;
                bool hasPersistentRoomAccess =
                    persistentRoomIsRestricted &&
                    RoomDB.CanPlayerAccessSubRoom(
                        sourceRoom,
                        sourceSubRoom,
                        playerId);

                if (sourceRoom != null &&
                    RoomDB.IsPlayerBannedFromRoom(sourceRoom.RoomId, playerId))
                {
                    Console.WriteLine(
                        $"[ROOM ACCESS DENIED] player={playerId} " +
                        $"room={sourceRoom.RoomId} instance={roomInstanceId} " +
                        "reason=room-ban");
                    return null;
                }

                if ((IsRestrictedInstance(source) ||
                     persistentRoomIsRestricted) &&
                    !alreadyConfirmed &&
                    !isOwner &&
                    !hasVerifiedInviteGrant &&
                    !hasPersistentRoomAccess)
                {
                    Console.WriteLine(
                        $"[PRIVATE JOIN DENIED] player={playerId} " +
                        $"instance={roomInstanceId} room={source.roomId} " +
                        "reason=invite-required");
                    return null;
                }

                int playerCount = occupants
                    .Select(heartbeat => heartbeat!.playerId)
                    .Where(id => id > 0)
                    .Distinct()
                    .Count();
                int capacity = Math.Max(1, source.maxCapacity);
                if (!alreadyConfirmed && playerCount >= capacity)
                    return null;

                RoomInstance joinedSession = CloneInstance(source);
                int joinedPlayerCount = alreadyConfirmed
                    ? playerCount
                    : playerCount + 1;
                joinedSession.isFull = joinedPlayerCount >= capacity;
                RemovePlayerFromConfirmedInstancesNoLock(playerId);
                TrackParticipantNoLock(
                    playerId,
                    joinedSession.roomInstanceId);

                if (hasVerifiedInviteGrant &&
                    joinedSession.roomInstanceType == RoomInstanceType.Dormroom)
                {
                    _guestDormEnteredAt[playerId] = DateTime.UtcNow;
                }

                Console.WriteLine(
                    $"[MATCHMAKING INSTANCE] player={playerId} " +
                    $"joined room={joinedSession.roomId} " +
                    $"instance={joinedSession.roomInstanceId} " +
                    $"players={joinedPlayerCount}/{capacity} " +
                    $"viaInvite={hasVerifiedInviteGrant.ToString().ToLowerInvariant()} " +
                    $"inviterPresent={inviterIsPresent.ToString().ToLowerInvariant()}");

                return PlayerDB.UpdatePlayerHeartbeat(
                    playerId,
                    joinedSession);
            }
        }

        public static void MarkGuestDormEntry(long playerId)
        {
            lock (_matchmakingLock)
                _guestDormEnteredAt[playerId] = DateTime.UtcNow;
        }

        public static bool ShouldHoldInGuestDorm(long playerId, Heartbeat heartbeat)
        {
            if (!heartbeat.isOnline ||
                heartbeat.roomInstance?.roomInstanceType != RoomInstanceType.Dormroom)
                return false;

            lock (_matchmakingLock)
            {
                return _guestDormEnteredAt.TryGetValue(
                           playerId,
                           out DateTime enteredAt) &&
                       DateTime.UtcNow - enteredAt < GuestDormHoldWindow;
            }
        }

        public static Heartbeat CreateDorm(long playerId, string name)
        {
            var dorm = RoomDB.GetOrCreatePlayerDorm(playerId)
                ?? RoomDB.GetRoom(1);
            var subRoom =
                dorm?.SubRooms?.FirstOrDefault(sub =>
                    string.Equals(
                        sub.Name,
                        "Home",
                        StringComparison.OrdinalIgnoreCase))
                ?? dorm?.SubRooms?.FirstOrDefault();

            long dormId = dorm?.RoomId ?? 1;

            long instanceId = _rng.NextInt64(1_000_000L, 1_000_000_000_000L);
            SubRoomDataSave? dataSave = null;
            string roomDataBlob = string.Empty;
            bool hasPersistencePair = false;
            if (dorm != null && subRoom != null)
            {
                hasPersistencePair = RoomDB.TryGetPersistencePair(
                    dorm,
                    subRoom,
                    out dataSave,
                    out roomDataBlob,
                    out _);
            }

            var session = new RoomInstance
            {
                isPrivate = true,

                dataBlob = hasPersistencePair ? roomDataBlob : string.Empty,

                location = subRoom?.UnitySceneId
                    ?? "76d98498-60a1-430c-ab76-b54a29b7a163",

                maxCapacity = subRoom?.MaxPlayers ?? 4,
                Name = $"@{name}'s Dorm",
                photonRegion = ServerConfig.PhotonRegion,
                photonRegionId = ServerConfig.PhotonRegion,
                photonRoomId = $"MochaDorm-{instanceId}-room",
                roomId = dormId,
                roomInstanceId = instanceId,
                subRoomId = subRoom?.SubRoomId ?? 0,
                subRoomDataSaveId = dataSave?.SubRoomDataSaveId ?? 0,
                roomInstanceType = RoomInstanceType.Dormroom,
                createdAt = DateTime.UtcNow
            };
            lock (_matchmakingLock)
            {
                RemovePlayerFromConfirmedInstancesNoLock(playerId);
                TrackParticipantNoLock(playerId, session.roomInstanceId);
                _instanceOwners[session.roomInstanceId] = playerId;
                _guestDormEnteredAt.Remove(playerId);
            }

            Console.WriteLine(
                $"[DORM LOAD] account={playerId} room={session.roomId} " +
                $"subroom={session.subRoomId} " +
                $"superRoomBlob={(hasPersistencePair ? session.dataBlob : "null")} " +
                $"subRoomBlob={(hasPersistencePair ? dataSave?.DataBlob : "null")} " +
                $"subRoomDataSaveId={session.subRoomDataSaveId} " +
                $"cv2Pair={hasPersistencePair}");

            return PlayerDB.UpdatePlayerHeartbeat(playerId, session)
                ?? new Heartbeat
                {
                    playerId = playerId,
                    isOnline = true,
                    roomInstance = session
                };
        }

        public static bool ReportJoinResult(
            long playerId,
            long roomInstanceId,
            bool succeeded)
        {
            if (playerId <= 0 || roomInstanceId <= 0)
                return false;

            Heartbeat heartbeat = PlayerDB.GetPlayerHeartbeat(playerId);
            if (heartbeat.roomInstance?.roomInstanceId != roomInstanceId)
            {
                Console.WriteLine(
                    $"[JOIN RESULT] player={playerId} instance={roomInstanceId} " +
                    "ignored=true reason=heartbeat-mismatch");
                return false;
            }

            lock (_matchmakingLock)
            {
                RemovePlayerFromConfirmedInstancesNoLock(playerId);
                if (succeeded)
                    TrackParticipantNoLock(playerId, roomInstanceId);
            }

            if (!succeeded)
                PlayerDB.LeaveCurrentRoom(playerId);

            Console.WriteLine(
                $"[JOIN RESULT] player={playerId} instance={roomInstanceId} " +
                $"success={succeeded.ToString().ToLowerInvariant()}");
            return true;
        }

        public static void MarkPlayerLeft(long playerId)
        {
            if (playerId <= 0)
                return;

            lock (_matchmakingLock)
            {
                RemovePlayerFromConfirmedInstancesNoLock(playerId);
                _guestDormEnteredAt.Remove(playerId);
            }
        }

        public static bool IsConfirmedParticipant(
            long playerId,
            long roomInstanceId)
        {
            lock (_matchmakingLock)
            {
                if (IsConfirmedParticipantNoLock(playerId, roomInstanceId))
                    return true;
            }

            Heartbeat heartbeat = PlayerDB.GetPlayerHeartbeat(playerId);
            return heartbeat.isOnline &&
                   heartbeat.roomInstance?.roomInstanceId == roomInstanceId;
        }

        private static void TrackParticipantNoLock(
            long playerId,
            long roomInstanceId)
        {
            if (playerId <= 0 || roomInstanceId <= 0)
                return;

            if (!_confirmedParticipantsByInstance.TryGetValue(
                    roomInstanceId,
                    out HashSet<long>? participants))
            {
                participants = new HashSet<long>();
                _confirmedParticipantsByInstance[roomInstanceId] = participants;
            }

            participants.Add(playerId);
        }

        private static bool IsConfirmedParticipantNoLock(
            long playerId,
            long roomInstanceId)
        {
            return _confirmedParticipantsByInstance.TryGetValue(
                       roomInstanceId,
                       out HashSet<long>? participants) &&
                   participants.Contains(playerId);
        }

        private static void RemovePlayerFromConfirmedInstancesNoLock(
            long playerId)
        {
            foreach (long instanceId in _confirmedParticipantsByInstance
                         .Where(pair => pair.Value.Remove(playerId) &&
                                        pair.Value.Count == 0)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _confirmedParticipantsByInstance.Remove(instanceId);
                _instanceOwners.Remove(instanceId);
            }
        }
    }
}