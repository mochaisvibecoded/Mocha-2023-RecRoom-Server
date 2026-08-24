using Mocha2023.Classes.DBs.DBClasses;
using LiteDB;
using static Mocha2023.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Mocha2023.Classes.DBs
{

    public static class RelationshipDB
    {
        private static readonly object Sync = new();
        private static readonly LiteDatabase Database =
            new(Path.Combine(Program.dataDir, "DBs", "Relationships.db"));

        private static readonly ILiteCollection<RelationshipRecord> Records =
            Database.GetCollection<RelationshipRecord>("Relationships");

        static RelationshipDB()
        {

            var rawRecords = Database.GetCollection("Relationships");
            foreach (BsonDocument document in rawRecords.FindAll().ToList())
            {
                BsonValue id = document["_id"];
                if (!id.IsString)
                    rawRecords.Delete(id);
            }

            Records.EnsureIndex(value => value.SourcePlayerId);
            Records.EnsureIndex(value => value.TargetPlayerId);
            Records.EnsureIndex(value => value.Kind);
        }

        public enum RelationshipKind
        {
            Friendship,
            Subscription
        }

        public sealed class RelationshipRecord
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;
            public RelationshipKind Kind { get; set; }
            public long SourcePlayerId { get; set; }
            public long TargetPlayerId { get; set; }
            public RelationshipType RelationshipType { get; set; }
            public List<long> FavoritedBy { get; set; } = new();
            public List<long> MutedBy { get; set; } = new();
            public List<long> IgnoredBy { get; set; } = new();
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        public static int MigrateLegacyData(IEnumerable<FullPlayer> players)
        {
            lock (Sync)
            {
                int imported = 0;
                var allPlayers = players.Where(value => value.Player != null).ToList();
                var validIds = allPlayers.Select(value => value.PlayerId).ToHashSet();

                foreach (FullPlayer account in allPlayers)
                {
                    foreach (PlayerRelationship legacy in account.Player!.Relationships ?? new())
                    {
                        if (legacy.PlayerID == account.PlayerId || !validIds.Contains(legacy.PlayerID))
                            continue;

                        string id = FriendshipId(account.PlayerId, legacy.PlayerID);
                        RelationshipRecord? record = Records.FindById(id);
                        bool isNew = record == null;
                        record ??= NewFriendship(account.PlayerId, legacy.PlayerID);
                        record.Id = id;

                        if (legacy.RelationshipType == RelationshipType.OutgoingFriendRequest)
                        {
                            record.SourcePlayerId = account.PlayerId;
                            record.TargetPlayerId = legacy.PlayerID;
                            record.RelationshipType = RelationshipType.OutgoingFriendRequest;
                        }
                        else if (legacy.RelationshipType == RelationshipType.IncomingFriendRequest)
                        {
                            record.SourcePlayerId = legacy.PlayerID;
                            record.TargetPlayerId = account.PlayerId;
                            record.RelationshipType = RelationshipType.OutgoingFriendRequest;
                        }
                        else if (legacy.RelationshipType == RelationshipType.Friend)
                        {
                            record.RelationshipType = RelationshipType.Friend;
                        }
                        else if (isNew)
                        {

                            record.RelationshipType = RelationshipType.None;
                        }

                        SetFlag(record.FavoritedBy, account.PlayerId, legacy.Favorited);
                        SetFlag(record.MutedBy, account.PlayerId, legacy.Muted);
                        SetFlag(record.IgnoredBy, account.PlayerId, legacy.Ignored);
                        record.UpdatedAt = DateTime.UtcNow;
                        Records.Upsert(record);
                        if (isNew)
                            imported++;
                    }

                    foreach (long targetId in account.Player.SubscribedAccountIds ?? new())
                    {
                        if (targetId == account.PlayerId || !validIds.Contains(targetId))
                            continue;

                        string id = SubscriptionId(account.PlayerId, targetId);
                        if (Records.Exists(value => value.Id == id))
                            continue;

                        Records.Insert(new RelationshipRecord
                        {
                            Id = id,
                            Kind = RelationshipKind.Subscription,
                            SourcePlayerId = account.PlayerId,
                            TargetPlayerId = targetId,
                            RelationshipType = RelationshipType.None
                        });
                        imported++;
                    }
                }

                return imported;
            }
        }

        public static List<PlayerRelationship> GetRelationships(long playerId)
        {
            lock (Sync)
            {
                return Records.Find(value =>
                        value.Kind == RelationshipKind.Friendship &&
                        (value.SourcePlayerId == playerId || value.TargetPlayerId == playerId))
                    .Select(value => ToClientRelationship(value, playerId))
                    .OrderByDescending(value => value.RelationshipType == RelationshipType.Friend)
                    .ThenByDescending(value => value.RelationshipType == RelationshipType.IncomingFriendRequest)
                    .ThenBy(value => value.PlayerID)
                    .ToList();
            }
        }

        public static PlayerRelationship? GetRelationship(long playerId, long otherPlayerId)
        {
            lock (Sync)
            {
                RelationshipRecord? record = Records.FindById(
                    FriendshipId(playerId, otherPlayerId));
                return record == null ? null : ToClientRelationship(record, playerId);
            }
        }

        public static List<ClientRelationshipDTO> GetClientRelationships(long playerId)
        {
            lock (Sync)
            {
                return Records.Find(value =>
                        value.Kind == RelationshipKind.Friendship &&
                        (value.SourcePlayerId == playerId || value.TargetPlayerId == playerId))
                    .Select(value => ToClientRelationshipDto(value, playerId))
                    .OrderByDescending(value => value.RelationshipType == (int)RelationshipType.Friend)
                    .ThenByDescending(value => value.RelationshipType == (int)RelationshipType.IncomingFriendRequest)
                    .ThenBy(value => value.PlayerID)
                    .ToList();
            }
        }

        public static ClientRelationshipDTO? GetClientRelationship(
            long playerId,
            long otherPlayerId)
        {
            lock (Sync)
            {
                RelationshipRecord? record = Records.FindById(
                    FriendshipId(playerId, otherPlayerId));
                return record == null ? null : ToClientRelationshipDto(record, playerId);
            }
        }

        public static PlayerRelationship SendFriendRequest(
            long requesterPlayerId,
            long receiverPlayerId,
            out bool acceptedExistingRequest)
        {
            lock (Sync)
            {
                acceptedExistingRequest = false;
                string id = FriendshipId(requesterPlayerId, receiverPlayerId);
                RelationshipRecord? existing = Records.FindById(id);

                if (existing?.RelationshipType == RelationshipType.Friend)
                    return ToClientRelationship(existing, requesterPlayerId);

                if (existing != null &&
                    existing.RelationshipType != RelationshipType.Friend &&
                    existing.SourcePlayerId == receiverPlayerId &&
                    existing.TargetPlayerId == requesterPlayerId)
                {
                    existing.RelationshipType = RelationshipType.Friend;
                    existing.UpdatedAt = DateTime.UtcNow;
                    Records.Upsert(existing);
                    acceptedExistingRequest = true;
                    return ToClientRelationship(existing, requesterPlayerId);
                }

                RelationshipRecord record = existing ?? new RelationshipRecord
                {
                    Id = id,
                    Kind = RelationshipKind.Friendship,
                    CreatedAt = DateTime.UtcNow
                };

                record.SourcePlayerId = requesterPlayerId;
                record.TargetPlayerId = receiverPlayerId;

                record.RelationshipType = RelationshipType.OutgoingFriendRequest;
                record.UpdatedAt = DateTime.UtcNow;
                Records.Upsert(record);

                return ToClientRelationship(record, requesterPlayerId);
            }
        }

        public static PlayerRelationship? AcceptFriendRequest(
            long receiverPlayerId,
            long requesterPlayerId)
        {
            lock (Sync)
            {
                string id = FriendshipId(receiverPlayerId, requesterPlayerId);
                RelationshipRecord? record = Records.FindById(id);
                if (record == null)
                    return null;

                if (record.RelationshipType == RelationshipType.Friend)
                    return ToClientRelationship(record, receiverPlayerId);

                if (record.SourcePlayerId != requesterPlayerId ||
                    record.TargetPlayerId != receiverPlayerId)
                {
                    return null;
                }

                record.RelationshipType = RelationshipType.Friend;
                record.UpdatedAt = DateTime.UtcNow;
                Records.Upsert(record);
                return ToClientRelationship(record, receiverPlayerId);
            }
        }

        public static PlayerRelationship AddFriend(long firstPlayerId, long secondPlayerId)
        {
            lock (Sync)
            {
                string id = FriendshipId(firstPlayerId, secondPlayerId);
                RelationshipRecord record = Records.FindById(id) ??
                    NewFriendship(firstPlayerId, secondPlayerId);
                record.Id = id;
                record.RelationshipType = RelationshipType.Friend;
                record.UpdatedAt = DateTime.UtcNow;
                Records.Upsert(record);
                return ToClientRelationship(record, firstPlayerId);
            }
        }

        public static PlayerRelationship SetFlags(
            long viewerPlayerId,
            long otherPlayerId,
            bool? ignored = null,
            bool? muted = null,
            bool? favorited = null)
        {
            lock (Sync)
            {
                string id = FriendshipId(viewerPlayerId, otherPlayerId);
                RelationshipRecord record = Records.FindById(id) ?? new RelationshipRecord
                {
                    Id = id,
                    Kind = RelationshipKind.Friendship,
                    SourcePlayerId = viewerPlayerId,
                    TargetPlayerId = otherPlayerId,
                    RelationshipType = RelationshipType.None,
                    CreatedAt = DateTime.UtcNow
                };

                if (ignored.HasValue)
                    SetFlag(record.IgnoredBy, viewerPlayerId, ignored.Value);
                if (muted.HasValue)
                    SetFlag(record.MutedBy, viewerPlayerId, muted.Value);
                if (favorited.HasValue)
                    SetFlag(record.FavoritedBy, viewerPlayerId, favorited.Value);

                record.UpdatedAt = DateTime.UtcNow;
                Records.Upsert(record);
                return ToClientRelationship(record, viewerPlayerId);
            }
        }

        public static bool RemoveFriend(long firstPlayerId, long secondPlayerId)
        {
            lock (Sync)
                return Records.Delete(FriendshipId(firstPlayerId, secondPlayerId));
        }

        public static bool SetSubscription(long subscriberId, long targetId, bool subscribe)
        {
            lock (Sync)
            {
                string id = SubscriptionId(subscriberId, targetId);
                if (!subscribe)
                {
                    Records.Delete(id);
                    return true;
                }

                Records.Upsert(new RelationshipRecord
                {
                    Id = id,
                    Kind = RelationshipKind.Subscription,
                    SourcePlayerId = subscriberId,
                    TargetPlayerId = targetId,
                    RelationshipType = RelationshipType.None,
                    UpdatedAt = DateTime.UtcNow
                });
                return true;
            }
        }

        public static bool IsSubscribed(long subscriberId, long targetId)
        {
            lock (Sync)
                return Records.Exists(value => value.Id == SubscriptionId(subscriberId, targetId));
        }

        public static int GetSubscriberCount(long targetId)
        {
            lock (Sync)
                return Records.Count(value =>
                    value.Kind == RelationshipKind.Subscription &&
                    value.TargetPlayerId == targetId);
        }

        public static int GetSubscribedCount(long subscriberId)
        {
            lock (Sync)
                return Records.Count(value =>
                    value.Kind == RelationshipKind.Subscription &&
                    value.SourcePlayerId == subscriberId);
        }

        public static long[] GetPersistentLiveSubjectIds(long viewerPlayerId)
        {
            if (viewerPlayerId <= 0)
                return Array.Empty<long>();

            lock (Sync)
            {
                return Records.FindAll()
                    .Where(record =>
                        (record.Kind == RelationshipKind.Friendship &&
                         (record.SourcePlayerId == viewerPlayerId ||
                          record.TargetPlayerId == viewerPlayerId)) ||
                        (record.Kind == RelationshipKind.Subscription &&
                         record.SourcePlayerId == viewerPlayerId))
                    .Select(record => record.Kind == RelationshipKind.Subscription
                        ? record.TargetPlayerId
                        : record.SourcePlayerId == viewerPlayerId
                            ? record.TargetPlayerId
                            : record.SourcePlayerId)
                    .Where(playerId =>
                        playerId > 0 &&
                        playerId != viewerPlayerId)
                    .Distinct()
                    .ToArray();
            }
        }

        public static long[] GetPersistentLiveWatcherIds(long subjectPlayerId)
        {
            if (subjectPlayerId <= 0)
                return Array.Empty<long>();

            lock (Sync)
            {
                return Records.FindAll()
                    .Where(record =>
                        (record.Kind == RelationshipKind.Friendship &&
                         (record.SourcePlayerId == subjectPlayerId ||
                          record.TargetPlayerId == subjectPlayerId)) ||
                        (record.Kind == RelationshipKind.Subscription &&
                         record.TargetPlayerId == subjectPlayerId))
                    .Select(record => record.Kind == RelationshipKind.Subscription
                        ? record.SourcePlayerId
                        : record.SourcePlayerId == subjectPlayerId
                            ? record.TargetPlayerId
                            : record.SourcePlayerId)
                    .Where(playerId =>
                        playerId > 0 &&
                        playerId != subjectPlayerId)
                    .Distinct()
                    .ToArray();
            }
        }

        public static long GetStableNumericId(long firstPlayerId, long secondPlayerId)
        {

            unchecked
            {
                uint hash = 2166136261;
                long lower = Math.Min(firstPlayerId, secondPlayerId);
                long upper = Math.Max(firstPlayerId, secondPlayerId);

                foreach (long value in new[] { lower, upper })
                {
                    ulong bytes = (ulong)value;
                    for (int index = 0; index < sizeof(long); index++)
                    {
                        hash ^= (byte)(bytes >> (index * 8));
                        hash *= 16777619;
                    }
                }

                int id = (int)(hash & 0x7FFFFFFF);
                return id == 0 ? 1 : id;
            }
        }

        private static ClientRelationshipDTO ToClientRelationshipDto(
            RelationshipRecord record,
            long viewerPlayerId)
        {
            long otherPlayerId = record.SourcePlayerId == viewerPlayerId
                ? record.TargetPlayerId
                : record.SourcePlayerId;

            RelationshipType clientType = record.RelationshipType;
            if (record.RelationshipType == RelationshipType.OutgoingFriendRequest)
            {
                clientType = record.SourcePlayerId == viewerPlayerId
                    ? RelationshipType.OutgoingFriendRequest
                    : RelationshipType.IncomingFriendRequest;
            }

            return new ClientRelationshipDTO
            {
                PlayerID = CheckedPlayerId(otherPlayerId),
                RelationshipType = (int)clientType,
                Favorited = GetPerspectiveStatus(record.FavoritedBy, viewerPlayerId, otherPlayerId),
                Muted = GetPerspectiveStatus(record.MutedBy, viewerPlayerId, otherPlayerId),
                Ignored = GetPerspectiveStatus(record.IgnoredBy, viewerPlayerId, otherPlayerId)
            };
        }

        private static int GetPerspectiveStatus(
            IReadOnlyCollection<long> values,
            long viewerPlayerId,
            long otherPlayerId)
        {
            bool local = values.Contains(viewerPlayerId);
            bool remote = values.Contains(otherPlayerId);

            if (local && remote)
                return 3;
            if (local)
                return 1;
            if (remote)
                return 2;
            return 0;
        }

        private static int CheckedPlayerId(long playerId)
        {
            if (playerId <= 0 || playerId > int.MaxValue)
                throw new InvalidOperationException(
                    $"Player id {playerId} cannot be represented by the April 2023 client.");

            return (int)playerId;
        }

        private static PlayerRelationship ToClientRelationship(
            RelationshipRecord record,
            long viewerPlayerId)
        {
            long otherPlayerId = record.SourcePlayerId == viewerPlayerId
                ? record.TargetPlayerId
                : record.SourcePlayerId;

            RelationshipType clientType = record.RelationshipType;
            if (record.RelationshipType == RelationshipType.OutgoingFriendRequest)
            {
                clientType = record.SourcePlayerId == viewerPlayerId
                    ? RelationshipType.OutgoingFriendRequest
                    : RelationshipType.IncomingFriendRequest;
            }

            return new PlayerRelationship
            {
                Id = GetStableNumericId(record.SourcePlayerId, record.TargetPlayerId),
                PlayerID = otherPlayerId,
                RelationshipType = clientType,
                Favorited = record.FavoritedBy.Contains(viewerPlayerId),
                Muted = record.MutedBy.Contains(viewerPlayerId),
                Ignored = record.IgnoredBy.Contains(viewerPlayerId)
            };
        }

        private static RelationshipRecord NewFriendship(long firstPlayerId, long secondPlayerId)
        {
            long source = Math.Min(firstPlayerId, secondPlayerId);
            long target = Math.Max(firstPlayerId, secondPlayerId);
            return new RelationshipRecord
            {
                Id = FriendshipId(firstPlayerId, secondPlayerId),
                Kind = RelationshipKind.Friendship,
                SourcePlayerId = source,
                TargetPlayerId = target,
                RelationshipType = RelationshipType.Friend
            };
        }

        private static string FriendshipId(long firstPlayerId, long secondPlayerId) =>
            $"friend:{Math.Min(firstPlayerId, secondPlayerId)}:{Math.Max(firstPlayerId, secondPlayerId)}";

        private static string SubscriptionId(long subscriberId, long targetId) =>
            $"subscription:{subscriberId}:{targetId}";

        private static void SetFlag(List<long> values, long playerId, bool enabled)
        {
            values.RemoveAll(value => value == playerId);
            if (enabled)
                values.Add(playerId);
        }
    }
}
