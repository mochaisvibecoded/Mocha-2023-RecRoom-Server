using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiteDB;

namespace Mocha2023.Classes.DBs
{

    public static class CreatorFeatureDB
    {
        private static readonly object Sync = new();
        private static readonly LiteDatabase Database = OpenDatabase();

        private static readonly ILiteCollection<InventionRecord> Inventions =
            Database.GetCollection<InventionRecord>("Inventions");

        private const long MaxClientInventionId = 1_000_000_000;

        private static readonly ILiteCollection<InventionCheerRecord> InventionCheers =
            Database.GetCollection<InventionCheerRecord>("InventionCheers");
        private static readonly ILiteCollection<InventionAliasRecord> InventionAliases =
            Database.GetCollection<InventionAliasRecord>("InventionAliases");
        private static readonly ILiteCollection<InventionPurchaseRecord> InventionPurchases =
            Database.GetCollection<InventionPurchaseRecord>("InventionPurchases");
        private static readonly ILiteCollection<MeetupCodeRecord> MeetupCodes =
            Database.GetCollection<MeetupCodeRecord>("MeetupCodes");
        private static readonly ILiteCollection<CloudVariableRecord> CloudVariables =
            Database.GetCollection<CloudVariableRecord>("CloudVariables");
        private static readonly ILiteCollection<RoomPlayerDataRecord> RoomPlayerData =
            Database.GetCollection<RoomPlayerDataRecord>("RoomPlayerData");

        private static LiteDatabase OpenDatabase()
        {
            string directory = Path.Combine(Program.dataDir, "DBs");
            Directory.CreateDirectory(directory);
            return new LiteDatabase(Path.Combine(directory, "CreatorFeatures.db"));
        }

        static CreatorFeatureDB()
        {
            Directory.CreateDirectory(Path.Combine(Program.dataDir, "DBs"));
            LiteDbMaintenance.StartPeriodicCheckpoint("CreatorFeatures.db", Database);

            Inventions.EnsureIndex(value => value.CreatorAccountId);
            Inventions.EnsureIndex(value => value.RoomId);
            Inventions.EnsureIndex(value => value.UpdatedAtUtc);
            InventionCheers.EnsureIndex(value => value.InventionId);
            InventionCheers.EnsureIndex(value => value.AccountId);
            InventionAliases.EnsureIndex(value => value.CurrentInventionId);
            InventionPurchases.EnsureIndex(value => value.InventionId);
            InventionPurchases.EnsureIndex(value => value.AccountId);
            MeetupCodes.EnsureIndex(value => value.CreatorAccountId);
            MeetupCodes.EnsureIndex(value => value.RoomInstanceId);
            MeetupCodes.EnsureIndex(value => value.ExpiresAtUtc);
            CloudVariables.EnsureIndex(value => value.RoomId);
            CloudVariables.EnsureIndex(value => value.AccountId);
            CloudVariables.EnsureIndex(value => value.UpdatedAtUtc);
            RoomPlayerData.EnsureIndex(value => value.RoomId);
            RoomPlayerData.EnsureIndex(value => value.AccountId);
            RoomPlayerData.EnsureIndex(value => value.UpdatedAtUtc);

            MigrateLegacyInventionFiles();
            RepairOversizedInventionIds();
            BackfillInventionSaveMetadata();
            RepairStoredInventionBlobs();
        }

        public sealed class InventionRecord
        {
            [BsonId]
            public long InventionId { get; set; }
            public long CreatorAccountId { get; set; }
            public long RoomId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string ImageName { get; set; } = string.Empty;
            public string DataBlob { get; set; } = string.Empty;
            public string DataBlobHash { get; set; } = string.Empty;
            public long DataBlobSize { get; set; }

            public int InstantiationCost { get; set; }
            public int LightsCost { get; set; }
            public int ChipsCost { get; set; }
            public int CloudVariablesCost { get; set; }
            public int AiCost { get; set; }
            public List<long> ReferencedInventionIds { get; set; } = new();
            public int CreatorAccountRole { get; set; }
            public List<string> Tags { get; set; } = new();
            public int Version { get; set; } = 1;
            public bool IsPublished { get; set; } = true;
            public int Uses { get; set; }
            public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
            public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
            public string RawPayloadJson { get; set; } = "{}";
        }

        public sealed class InventionAliasRecord
        {
            [BsonId]
            public long LegacyInventionId { get; set; }
            public long CurrentInventionId { get; set; }
            public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        }

        public sealed class InventionPurchaseRecord
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;
            public long InventionId { get; set; }
            public long AccountId { get; set; }
            public int PricePaid { get; set; }
            public DateTime PurchasedAtUtc { get; set; } = DateTime.UtcNow;
        }

        public sealed class InventionCheerRecord
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;
            public long InventionId { get; set; }
            public long AccountId { get; set; }
            public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        }

        public sealed class MeetupCodeRecord
        {
            [BsonId]
            public string Code { get; set; } = string.Empty;
            public long CreatorAccountId { get; set; }
            public long RoomId { get; set; }
            public long SubRoomId { get; set; }
            public long RoomInstanceId { get; set; }
            public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
            public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(6);
            public int UseCount { get; set; }
            public int MaxUses { get; set; } = 100;
            public bool IsActive { get; set; } = true;
        }

        public sealed class CloudVariableRecord
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;
            public long RoomId { get; set; }
            public long AccountId { get; set; }
            public string Key { get; set; } = string.Empty;
            public string ValueJson { get; set; } = "null";
            public int Version { get; set; } = 1;
            public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
            public long UpdatedByAccountId { get; set; }
        }

        public sealed class RoomPlayerDataRecord
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;
            public long RoomId { get; set; }
            public long AccountId { get; set; }
            public string DataJson { get; set; } = "{}";
            public int Version { get; set; } = 1;
            public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
            public long UpdatedByAccountId { get; set; }
        }

        public static InventionRecord SaveInvention(
            long accountId,
            JsonElement payload,
            long? forcedInventionId = null)
        {
            if (accountId <= 0)
                throw new ArgumentOutOfRangeException(nameof(accountId));

            lock (Sync)
            {
                bool isForcedImport = forcedInventionId.GetValueOrDefault() > 0;
                long requestedId = isForcedImport
                    ? forcedInventionId!.Value
                    : FindLong(payload,
                        "InventionId", "inventionId", "Id", "id") ?? 0;

                InventionRecord? existing = requestedId > 0
                    ? FindInventionNoLock(requestedId)
                    : null;

                if (existing != null && existing.CreatorAccountId != accountId)
                    throw new UnauthorizedAccessException(
                        "An invention can only be edited by its creator.");

                bool canPreserveImportedId = isForcedImport &&
                    requestedId > 0 &&
                    requestedId <= MaxClientInventionId &&
                    Inventions.FindById(requestedId) == null;
                long inventionId = existing?.InventionId ??
                    (canPreserveImportedId ? requestedId : NextInventionIdNoLock());
                DateTime now = DateTime.UtcNow;
                List<string> tags = ReadStringList(payload,
                    "Tags", "tags", "TagNames", "tagNames");

                var record = existing ?? new InventionRecord
                {
                    InventionId = inventionId,
                    CreatorAccountId = accountId,
                    CreatedAtUtc = now
                };

                record.RoomId = FindLong(payload,
                    "CreationRoomId", "creationRoomId",
                    "RoomId", "roomId", "SourceRoomId", "sourceRoomId")
                    ?? record.RoomId;
                record.InstantiationCost = ReadNonNegativeInt(
                    payload,
                    record.InstantiationCost,
                    "InstantiationCost", "instantiationCost", "InstantiateCost", "instantiateCost");
                record.LightsCost = ReadNonNegativeInt(
                    payload,
                    record.LightsCost,
                    "LightsCost", "lightsCost");
                record.ChipsCost = ReadNonNegativeInt(
                    payload,
                    record.ChipsCost,
                    "ChipsCost", "chipsCost");
                record.CloudVariablesCost = ReadNonNegativeInt(
                    payload,
                    record.CloudVariablesCost,
                    "CloudVariablesCost", "cloudVariablesCost", "CloudVariableCost", "cloudVariableCost");
                record.AiCost = ReadNonNegativeInt(
                    payload,
                    record.AiCost,
                    "AiCost", "aiCost", "AICost");
                record.CreatorAccountRole = ReadNonNegativeInt(
                    payload,
                    record.CreatorAccountRole,
                    "CreatorAccountRole", "creatorAccountRole");
                List<long> referencedInventions = ReadLongList(
                    payload,
                    "ReferencedInventions", "referencedInventions",
                    "ReferencedInventionIds", "referencedInventionIds");
                if (referencedInventions.Count > 0)
                    record.ReferencedInventionIds = referencedInventions.Take(512).ToList();
                record.Name = Limit(
                    FindString(payload,
                        "Name", "name", "Title", "title",
                        "InventionName", "inventionName")
                        ?? record.Name,
                    128);
                if (string.IsNullOrWhiteSpace(record.Name))
                    record.Name = $"Invention {inventionId}";

                record.Description = Limit(
                    FindString(payload, "Description", "description")
                        ?? record.Description,
                    1_000);
                record.ImageName = Limit(
                    FindString(payload,
                        "ImageName", "imageName", "Image", "image")
                        ?? record.ImageName,
                    512);
                record.DataBlob = Limit(
                    FindString(payload,
                        "ObjectDataFilename", "objectDataFilename",
                        "ObjectDataBlob", "objectDataBlob",
                        "SubRoomDataFilename", "subRoomDataFilename",
                        "DataBlob", "dataBlob", "DataBlobPath", "dataBlobPath",
                        "Filename", "filename", "FileName", "fileName",
                        "DataBlobName", "dataBlobName", "BlobName", "blobName")
                        ?? record.DataBlob,
                    512);

                string? requestedBlobHash = FindString(payload,
                    "DataBlobHash", "dataBlobHash",
                    "ObjectDataBlobHash", "objectDataBlobHash",
                    "ObjectDataHash", "objectDataHash", "Hash", "hash");
                if (!string.IsNullOrWhiteSpace(requestedBlobHash))
                    record.DataBlobHash = Limit(requestedBlobHash, 256);

                long? requestedBlobSize = FindLong(payload,
                    "DataBlobSize", "dataBlobSize", "ObjectDataSize", "objectDataSize",
                    "ContentLength", "contentLength", "Length", "length");
                if (requestedBlobSize is >= 0)
                    record.DataBlobSize = requestedBlobSize.Value;

                if (tags.Count > 0)
                    record.Tags = tags.Take(30).ToList();

                bool? requestedPublished = FindBool(payload,
                    "IsPublished", "isPublished", "Published", "published");
                if (requestedPublished.HasValue)
                    record.IsPublished = requestedPublished.Value;

                int requestedVersion = (int)Math.Clamp(
                    FindLong(payload, "Version", "version") ?? 0,
                    0,
                    int.MaxValue);
                record.Version = Math.Max(
                    Math.Max(1, record.Version + (existing == null ? 0 : 1)),
                    requestedVersion);
                record.UpdatedAtUtc = now;
                record.RawPayloadJson = payload.GetRawText();
                RepairInventionBlobMetadataNoLock(record);

                Inventions.Upsert(record);

                if (existing == null && requestedId > 0 && requestedId != inventionId)
                {
                    InventionAliases.Upsert(new InventionAliasRecord
                    {
                        LegacyInventionId = requestedId,
                        CurrentInventionId = inventionId,
                        CreatedAtUtc = now
                    });
                }

                return record;
            }
        }

        public static InventionRecord? GetInvention(long inventionId)
        {
            if (inventionId <= 0)
                return null;

            lock (Sync)
                return FindInventionNoLock(inventionId);
        }

        public static long ResolveInventionId(long inventionId)
        {
            InventionRecord? record = GetInvention(inventionId);
            return record?.InventionId ?? inventionId;
        }

        public static bool RecordInventionPurchase(
            long accountId,
            long inventionId,
            int pricePaid = 0)
        {
            if (accountId <= 0 || inventionId <= 0 || pricePaid < 0)
                return false;

            lock (Sync)
            {
                InventionRecord? invention = FindInventionNoLock(inventionId);
                if (invention == null ||
                    (!invention.IsPublished && invention.CreatorAccountId != accountId) ||
                    !HasValidInventionBlob(invention))
                {
                    return false;
                }

                InventionPurchases.Upsert(new InventionPurchaseRecord
                {
                    Id = InventionPurchaseId(invention.InventionId, accountId),
                    InventionId = invention.InventionId,
                    AccountId = accountId,
                    PricePaid = pricePaid,
                    PurchasedAtUtc = DateTime.UtcNow
                });
                return true;
            }
        }

        public static bool HasInventionAccess(long accountId, long inventionId)
        {
            if (accountId <= 0 || inventionId <= 0)
                return false;

            lock (Sync)
            {
                InventionRecord? invention = FindInventionNoLock(inventionId);
                if (invention == null)
                    return false;
                return invention.CreatorAccountId == accountId ||
                       InventionPurchases.Exists(value =>
                           value.Id == InventionPurchaseId(invention.InventionId, accountId));
            }
        }

        public static InventionRecord? SetInventionTags(
            long inventionId,
            long accountId,
            IEnumerable<string> tags)
        {
            if (inventionId <= 0)
                return null;

            lock (Sync)
            {
                InventionRecord? record = Inventions.FindById(inventionId);
                if (record == null)
                    return null;
                if (record.CreatorAccountId != accountId)
                {
                    throw new UnauthorizedAccessException(
                        "An invention can only be edited by its creator.");
                }

                record.Tags = tags
                    .Select(value => Limit(value.Trim(), 32))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(30)
                    .ToList();
                record.Version = Math.Max(1, record.Version + 1);
                record.UpdatedAtUtc = DateTime.UtcNow;
                Inventions.Update(record);
                return record;
            }
        }

        public static InventionRecord? IncrementInventionUse(long inventionId)
        {
            if (inventionId <= 0)
                return null;

            lock (Sync)
            {
                InventionRecord? record = Inventions.FindById(inventionId);
                if (record == null)
                    return null;
                record.Uses = Math.Max(0, record.Uses) + 1;
                record.UpdatedAtUtc = DateTime.UtcNow;
                Inventions.Update(record);
                return record;
            }
        }

        public static bool DeleteInvention(long inventionId, long accountId)
        {
            lock (Sync)
            {
                InventionRecord? record = Inventions.FindById(inventionId);
                if (record == null || record.CreatorAccountId != accountId ||
                    !Inventions.Delete(inventionId))
                {
                    return false;
                }

                InventionCheers.DeleteMany(value => value.InventionId == inventionId);
                return true;
            }
        }

        public static bool SetInventionCheer(
            long inventionId,
            long accountId,
            bool cheered)
        {
            if (inventionId <= 0 || accountId <= 0)
                return false;

            lock (Sync)
            {
                if (Inventions.FindById(inventionId) == null)
                    return false;

                string id = InventionCheerId(inventionId, accountId);
                if (!cheered)
                {
                    InventionCheers.Delete(id);
                    return true;
                }

                InventionCheers.Upsert(new InventionCheerRecord
                {
                    Id = id,
                    InventionId = inventionId,
                    AccountId = accountId,
                    CreatedAtUtc = DateTime.UtcNow
                });
                return true;
            }
        }

        public static bool IsInventionCheered(long inventionId, long accountId)
        {
            if (inventionId <= 0 || accountId <= 0)
                return false;
            return InventionCheers.Exists(value =>
                value.Id == InventionCheerId(inventionId, accountId));
        }

        public static int GetInventionCheerCount(long inventionId) =>
            inventionId <= 0
                ? 0
                : InventionCheers.Count(value => value.InventionId == inventionId);

        private static string InventionCheerId(long inventionId, long accountId) =>
            $"{accountId}:{inventionId}";

        public static List<InventionRecord> SearchInventions(
            string? query = null,
            long? creatorAccountId = null,
            long? roomId = null,
            string? tag = null,
            bool includeUnpublished = false,
            int skip = 0,
            int take = 100)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 100);
            string q = query?.Trim() ?? string.Empty;
            string wantedTag = tag?.Trim() ?? string.Empty;

            IEnumerable<InventionRecord> rows = Inventions.FindAll();

            if (!includeUnpublished)
                rows = rows.Where(HasValidInventionBlob);
            if (!includeUnpublished)
                rows = rows.Where(value => value.IsPublished);
            if (creatorAccountId.HasValue && creatorAccountId.Value > 0)
                rows = rows.Where(value => value.CreatorAccountId == creatorAccountId.Value);
            if (roomId.HasValue && roomId.Value > 0)
                rows = rows.Where(value => value.RoomId == roomId.Value);
            if (!string.IsNullOrWhiteSpace(wantedTag))
            {
                rows = rows.Where(value => value.Tags?.Any(item =>
                    string.Equals(item, wantedTag, StringComparison.OrdinalIgnoreCase)) == true);
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                rows = rows.Where(value =>
                    value.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    value.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    value.Tags.Any(item =>
                        item.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }

            return rows
                .OrderByDescending(value => value.Uses)
                .ThenByDescending(value => value.UpdatedAtUtc)
                .Skip(skip)
                .Take(take)
                .ToList();
        }

        public static int CountInventions(
            string? query = null,
            long? creatorAccountId = null,
            long? roomId = null,
            string? tag = null,
            bool includeUnpublished = false)
        {
            string q = query?.Trim() ?? string.Empty;
            string wantedTag = tag?.Trim() ?? string.Empty;
            IEnumerable<InventionRecord> rows = Inventions.FindAll();
            if (!includeUnpublished)
                rows = rows.Where(HasValidInventionBlob);
            if (!includeUnpublished)
                rows = rows.Where(value => value.IsPublished);
            if (creatorAccountId.HasValue && creatorAccountId.Value > 0)
                rows = rows.Where(value => value.CreatorAccountId == creatorAccountId.Value);
            if (roomId.HasValue && roomId.Value > 0)
                rows = rows.Where(value => value.RoomId == roomId.Value);
            if (!string.IsNullOrWhiteSpace(wantedTag))
            {
                rows = rows.Where(value => value.Tags?.Any(item =>
                    string.Equals(item, wantedTag, StringComparison.OrdinalIgnoreCase)) == true);
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                rows = rows.Where(value =>
                    value.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    value.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    value.Tags.Any(item => item.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }
            return rows.Count();
        }

        public static object? ToClientInventionVersion(InventionRecord value)
        {
            if (!TryGetInventionBlob(
                    value,
                    out _,
                    out string dataBlobPath,
                    out string dataBlobName,
                    out string dataBlobHash,
                    out long dataBlobSize))
            {
                return null;
            }

            int versionNumber = Math.Max(1, value.Version);
            return new
            {
                InventionId = value.InventionId,
                Id = value.InventionId,
                ReplicationId = StableReplicationId(
                    "invention-version",
                    value.InventionId,
                    versionNumber),
                VersionNumber = versionNumber,
                Version = versionNumber,
                InventionVersionId = value.InventionId,
                VersionId = value.InventionId,
                BlobName = dataBlobName,
                DataBlob = dataBlobName,
                DataBlobName = dataBlobName,
                DataBlobPath = dataBlobPath,
                ObjectDataFilename = dataBlobName,
                BlobHash = dataBlobHash,
                DataBlobHash = dataBlobHash,
                BlobSize = dataBlobSize,
                DataBlobSize = dataBlobSize,
                BlobUrl = "/cdn/" + dataBlobPath.TrimStart('/'),
                DownloadUrl = "/cdn/" + dataBlobPath.TrimStart('/'),
                InstantiationCost = Math.Max(0, value.InstantiationCost),
                LightsCost = Math.Max(0, value.LightsCost),
                ChipsCost = Math.Max(0, value.ChipsCost),
                CloudVariablesCost = Math.Max(0, value.CloudVariablesCost),
                AiCost = Math.Max(0, value.AiCost),
                CanSpawn = true
            };
        }

        public static object ToClientInvention(InventionRecord value)
        {
            bool hasBlob = TryGetInventionBlob(
                value,
                out _,
                out string dataBlobPath,
                out string dataBlobName,
                out string dataBlobHash,
                out long dataBlobSize);

            if (hasBlob &&
                (!string.Equals(value.DataBlob, dataBlobPath, StringComparison.Ordinal) ||
                 !string.Equals(value.DataBlobHash, dataBlobHash, StringComparison.Ordinal) ||
                 value.DataBlobSize != dataBlobSize))
            {
                lock (Sync)
                {
                    value.DataBlob = dataBlobPath;
                    value.DataBlobHash = dataBlobHash;
                    value.DataBlobSize = dataBlobSize;
                    Inventions.Upsert(value);
                }
            }

            int currentVersionNumber = Math.Max(1, value.Version);
            string inventionReplicationId = StableReplicationId(
                "invention",
                value.InventionId,
                0);
            object? currentVersion = hasBlob
                ? ToClientInventionVersion(value)
                : null;

            return new
            {
                InventionId = value.InventionId,
                Id = value.InventionId,
                ReplicationId = inventionReplicationId,
                Name = value.Name,
                InventionName = value.Name,
                Description = value.Description,
                CreatorAccountId = value.CreatorAccountId,
                CreatorPlayerId = value.CreatorAccountId,
                CreatorId = value.CreatorAccountId,
                RoomId = value.RoomId,
                ImageName = value.ImageName,
                Image = value.ImageName,
                PreviewImageName = value.ImageName,
                DataBlob = hasBlob ? dataBlobName : string.Empty,
                DataBlobPath = hasBlob ? dataBlobPath : string.Empty,
                DataBlobName = hasBlob ? dataBlobName : string.Empty,
                Filename = hasBlob ? dataBlobName : string.Empty,
                ObjectDataFilename = hasBlob ? dataBlobName : string.Empty,
                ObjectDataBlob = hasBlob ? dataBlobName : string.Empty,
                DataBlobHash = hasBlob ? dataBlobHash : string.Empty,
                ObjectDataBlobHash = hasBlob ? dataBlobHash : string.Empty,
                DataBlobSize = hasBlob ? dataBlobSize : 0,
                InstantiationCost = Math.Max(0, value.InstantiationCost),
                LightsCost = Math.Max(0, value.LightsCost),
                ChipsCost = Math.Max(0, value.ChipsCost),
                CloudVariablesCost = Math.Max(0, value.CloudVariablesCost),
                AiCost = Math.Max(0, value.AiCost),
                ReferencedInventions = value.ReferencedInventionIds ?? new List<long>(),
                ReferencedInventionIds = value.ReferencedInventionIds ?? new List<long>(),
                DataBlobUrl = hasBlob
                    ? "/cdn/" + dataBlobPath.TrimStart('/')
                    : string.Empty,
                HasValidObjects = hasBlob,
                CanSpawn = hasBlob,
                Tags = value.Tags ?? new List<string>(),
                TagNames = value.Tags ?? new List<string>(),
                CurrentVersionNumber = currentVersionNumber,
                CurrentVersion = currentVersion,
                InventionVersion = currentVersion,
                LatestVersion = currentVersion,
                Version = currentVersionNumber,
                Accessibility = 1,
                CreationRoomId = value.RoomId,
                CreatorPermission = 100,

                GeneralPermission = 20,
                IsFeatured = false,
                IsAGInvention = false,
                IsCertifiedInvention = false,

                Price = 0,
                AllowTrial = true,
                HideFromPlayer = false,
                IsPublished = value.IsPublished,
                Published = value.IsPublished,
                Uses = value.Uses,
                NumDownloads = value.Uses,
                NumPlayersHaveUsedInRoom = value.Uses,
                CheerCount = GetInventionCheerCount(value.InventionId),
                CreatedAt = value.CreatedAtUtc,
                FirstPublishedAt = value.CreatedAtUtc,
                CreationDate = value.CreatedAtUtc,
                ModifiedAt = value.UpdatedAtUtc,
                UpdatedAt = value.UpdatedAtUtc,
                LastUpdated = value.UpdatedAtUtc
            };
        }

        public static bool HasValidInventionBlob(InventionRecord value) =>
            TryGetInventionBlob(
                value,
                out _,
                out _,
                out _,
                out _,
                out _);

        public static bool TryGetInventionBlob(
            InventionRecord value,
            out string fullPath,
            out string relativePath,
            out string filename,
            out string hashBase64,
            out long size)
        {
            fullPath = string.Empty;
            relativePath = string.Empty;
            filename = string.Empty;
            hashBase64 = string.Empty;
            size = 0;

            var candidates = new List<(string Source, string Value)>();
            if (!string.IsNullOrWhiteSpace(value.DataBlob))
                candidates.Add(("storedDataBlob", value.DataBlob));

            if (!string.IsNullOrWhiteSpace(value.RawPayloadJson))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(value.RawPayloadJson);
                    CollectInventionBlobCandidates(document.RootElement, candidates);
                }
                catch (JsonException)
                {
                }
            }

            string cdnRoot = Path.GetFullPath(Path.Combine(Program.dataDir, "CDN"));
            string inventionDirectory = Path.Combine(cdnRoot, "invention");
            string roomDirectory = Path.Combine(cdnRoot, "room");
            Directory.CreateDirectory(inventionDirectory);

            (string FullPath, string Name, long Size, int Score)? best = null;
            foreach ((string source, string rawValue) in candidates)
            {
                string normalized = NormalizeCandidateBlobValue(rawValue);
                if (string.IsNullOrWhiteSpace(normalized))
                    continue;

                string safeName = Path.GetFileName(normalized);
                if (safeName.Length == 0 || safeName.Length > 180 ||
                    safeName.Any(character =>
                        !char.IsLetterOrDigit(character) &&
                        character is not '-' and not '_' and not '.'))
                {
                    continue;
                }

                var blobNames = new List<string> { safeName };
                if (safeName.EndsWith(".inv", StringComparison.OrdinalIgnoreCase))
                {
                    string extensionless = safeName[..^4];
                    if (!string.IsNullOrWhiteSpace(extensionless))
                        blobNames.Add(extensionless);
                }
                else
                {
                    string baseName = Path.GetFileNameWithoutExtension(safeName);
                    if (string.IsNullOrWhiteSpace(baseName))
                        baseName = safeName;
                    blobNames.Add(baseName + ".inv");
                }

                var paths = new List<string>();
                if (normalized.Contains('/'))
                    paths.Add(Path.Combine(cdnRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
                foreach (string blobName in blobNames.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(Path.Combine(inventionDirectory, blobName));
                    paths.Add(Path.Combine(roomDirectory, blobName));
                    paths.Add(Path.Combine(cdnRoot, blobName));
                }

                foreach (string candidatePath in paths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string resolved;
                    try
                    {
                        resolved = Path.GetFullPath(candidatePath);
                    }
                    catch (Exception exception) when (
                        exception is ArgumentException or NotSupportedException or PathTooLongException)
                    {
                        continue;
                    }

                    if (!resolved.StartsWith(cdnRoot + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase) ||
                        !File.Exists(resolved))
                    {
                        continue;
                    }

                    var info = new FileInfo(resolved);
                    if (info.Length <= 0 || LooksLikeImageFile(resolved))
                        continue;

                    int score = ScoreInventionBlobCandidate(source, resolved, info.Length);
                    if (best == null || score > best.Value.Score)
                        best = (resolved, info.Name, info.Length, score);
                }
            }

            if (best == null)
                return false;

            string canonicalBaseName = Path.GetFileNameWithoutExtension(best.Value.Name);
            if (string.IsNullOrWhiteSpace(canonicalBaseName))
                canonicalBaseName = best.Value.Name;
            string canonicalName = best.Value.Name.EndsWith(
                    ".inv",
                    StringComparison.OrdinalIgnoreCase)
                ? best.Value.Name
                : canonicalBaseName + ".inv";
            string canonicalPath = Path.Combine(inventionDirectory, canonicalName);
            if (!string.Equals(best.Value.FullPath, canonicalPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (!File.Exists(canonicalPath))
                        File.Copy(best.Value.FullPath, canonicalPath, overwrite: false);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            fullPath = File.Exists(canonicalPath) ? canonicalPath : best.Value.FullPath;
            var finalInfo = new FileInfo(fullPath);
            if (!finalInfo.Exists || finalInfo.Length <= 0 || LooksLikeImageFile(fullPath))
                return false;

            filename = finalInfo.Name;
            relativePath = string.Equals(
                    Path.GetDirectoryName(fullPath),
                    inventionDirectory,
                    StringComparison.OrdinalIgnoreCase)
                ? "invention/" + filename
                : "room/" + filename;
            size = finalInfo.Length;
            if (!string.IsNullOrWhiteSpace(value.DataBlobHash) &&
                value.DataBlobSize == size)
            {
                hashBase64 = value.DataBlobHash;
            }
            else
            {
                using FileStream stream = File.OpenRead(fullPath);
                hashBase64 = Convert.ToBase64String(SHA256.HashData(stream));
            }
            return true;
        }

        private static void RepairInventionBlobMetadataNoLock(InventionRecord value)
        {
            if (!TryGetInventionBlob(
                    value,
                    out _,
                    out string relativePath,
                    out _,
                    out string hashBase64,
                    out long size))
            {
                return;
            }

            value.DataBlob = relativePath;
            value.DataBlobHash = hashBase64;
            value.DataBlobSize = size;
        }

        private static void BackfillInventionSaveMetadata()
        {
            lock (Sync)
            {
                int repaired = 0;
                foreach (InventionRecord record in Inventions.FindAll().ToList())
                {
                    if (string.IsNullOrWhiteSpace(record.RawPayloadJson))
                        continue;

                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(record.RawPayloadJson);
                        JsonElement payload = document.RootElement;
                        bool changed = false;

                        long roomId = FindLong(payload,
                            "CreationRoomId", "creationRoomId",
                            "RoomId", "roomId", "SourceRoomId", "sourceRoomId") ?? 0;
                        if (record.RoomId <= 0 && roomId > 0)
                        {
                            record.RoomId = roomId;
                            changed = true;
                        }

                        changed |= TryBackfillNonNegativeInt(
                            payload,
                            record.InstantiationCost,
                            value => record.InstantiationCost = value,
                            "InstantiationCost", "instantiationCost", "InstantiateCost", "instantiateCost");
                        changed |= TryBackfillNonNegativeInt(
                            payload,
                            record.LightsCost,
                            value => record.LightsCost = value,
                            "LightsCost", "lightsCost");
                        changed |= TryBackfillNonNegativeInt(
                            payload,
                            record.ChipsCost,
                            value => record.ChipsCost = value,
                            "ChipsCost", "chipsCost");
                        changed |= TryBackfillNonNegativeInt(
                            payload,
                            record.CloudVariablesCost,
                            value => record.CloudVariablesCost = value,
                            "CloudVariablesCost", "cloudVariablesCost", "CloudVariableCost", "cloudVariableCost");
                        changed |= TryBackfillNonNegativeInt(
                            payload,
                            record.AiCost,
                            value => record.AiCost = value,
                            "AiCost", "aiCost", "AICost");
                        changed |= TryBackfillNonNegativeInt(
                            payload,
                            record.CreatorAccountRole,
                            value => record.CreatorAccountRole = value,
                            "CreatorAccountRole", "creatorAccountRole");

                        List<long> references = ReadLongList(payload,
                            "ReferencedInventions", "referencedInventions",
                            "ReferencedInventionIds", "referencedInventionIds");
                        if ((record.ReferencedInventionIds == null || record.ReferencedInventionIds.Count == 0) &&
                            references.Count > 0)
                        {
                            record.ReferencedInventionIds = references.Take(512).ToList();
                            changed = true;
                        }

                        if (!changed)
                            continue;

                        Inventions.Update(record);
                        repaired++;
                    }
                    catch (JsonException)
                    {
                    }
                }

                if (repaired > 0)
                    Console.WriteLine($"[INVENTION METADATA REPAIR] repaired={repaired}");
            }
        }

        private static bool TryBackfillNonNegativeInt(
            JsonElement payload,
            int currentValue,
            Action<int> setter,
            params string[] names)
        {
            long? parsed = FindLong(payload, names);
            if (!parsed.HasValue)
                return false;

            int value = (int)Math.Clamp(parsed.Value, 0, int.MaxValue);
            if (currentValue == value)
                return false;

            setter(value);
            return true;
        }

        private static int ReadNonNegativeInt(
            JsonElement payload,
            int fallback,
            params string[] names)
        {
            long? parsed = FindLong(payload, names);
            return parsed.HasValue
                ? (int)Math.Clamp(parsed.Value, 0, int.MaxValue)
                : Math.Max(0, fallback);
        }

        private static void RepairStoredInventionBlobs()
        {
            lock (Sync)
            {
                int repaired = 0;
                foreach (InventionRecord record in Inventions.FindAll().ToList())
                {
                    string oldBlob = record.DataBlob;
                    string oldHash = record.DataBlobHash;
                    long oldSize = record.DataBlobSize;
                    RepairInventionBlobMetadataNoLock(record);
                    if (!string.Equals(oldBlob, record.DataBlob, StringComparison.Ordinal) ||
                        !string.Equals(oldHash, record.DataBlobHash, StringComparison.Ordinal) ||
                        oldSize != record.DataBlobSize)
                    {
                        Inventions.Update(record);
                        repaired++;
                    }
                }

                if (repaired > 0)
                    Console.WriteLine($"[INVENTION BLOB REPAIR] repaired={repaired}");
            }
        }

        private static void CollectInventionBlobCandidates(
            JsonElement element,
            ICollection<(string Source, string Value)> output)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string lowerName = property.Name.ToLowerInvariant();
                    bool looksLikeBlobField =
                        lowerName.Contains("blob", StringComparison.Ordinal) ||
                        lowerName.Contains("filename", StringComparison.Ordinal) ||
                        lowerName is "file" or "datafile" or "objectdata";
                    bool looksLikePreview =
                        lowerName.Contains("image", StringComparison.Ordinal) ||
                        lowerName.Contains("preview", StringComparison.Ordinal) ||
                        lowerName.Contains("thumbnail", StringComparison.Ordinal) ||
                        lowerName.Contains("icon", StringComparison.Ordinal);

                    if (looksLikeBlobField && !looksLikePreview)
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            string? text = property.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                                output.Add((property.Name, text));
                        }
                        else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            CollectInventionBlobCandidates(property.Value, output);
                        }
                    }
                    else
                    {
                        CollectInventionBlobCandidates(property.Value, output);
                    }

                    if (TryParseEmbeddedJson(property.Value, out JsonElement embedded))
                        CollectInventionBlobCandidates(embedded, output);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                    CollectInventionBlobCandidates(item, output);
            }
            else if (TryParseEmbeddedJson(element, out JsonElement embedded))
            {
                CollectInventionBlobCandidates(embedded, output);
            }
        }

        private static string NormalizeCandidateBlobValue(string? value)
        {
            string normalized = (value ?? string.Empty).Replace('\\', '/').Trim();
            if (normalized.StartsWith("/cdn/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[5..];
            normalized = normalized.TrimStart('/');
            if (normalized.Contains("..", StringComparison.Ordinal))
                return string.Empty;

            if (normalized.StartsWith("room/invention/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[5..];
            if (normalized.StartsWith("invention/room/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[10..];
            return normalized;
        }

        private static int ScoreInventionBlobCandidate(
            string source,
            string fullPath,
            long length)
        {
            string lower = source.ToLowerInvariant();
            int score = 0;
            if (lower.Contains("object", StringComparison.Ordinal)) score += 500;
            if (lower.Contains("data", StringComparison.Ordinal)) score += 300;
            if (lower.Contains("blob", StringComparison.Ordinal)) score += 200;
            if (lower.Contains("filename", StringComparison.Ordinal)) score += 100;
            if (fullPath.Contains(
                    Path.DirectorySeparatorChar + "invention" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)) score += 50;
            score += (int)Math.Min(length / 1024, 100);
            return score;
        }

        private static bool LooksLikeImageFile(string path)
        {
            try
            {
                Span<byte> header = stackalloc byte[12];
                using FileStream stream = File.OpenRead(path);
                int read = stream.Read(header);
                if (read >= 8 &&
                    header[0] == 0x89 && header[1] == 0x50 &&
                    header[2] == 0x4E && header[3] == 0x47)
                    return true;
                if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                    return true;
                if (read >= 6 &&
                    header[0] == (byte)'G' && header[1] == (byte)'I' &&
                    header[2] == (byte)'F')
                    return true;
                if (read >= 12 &&
                    header[0] == (byte)'R' && header[1] == (byte)'I' &&
                    header[2] == (byte)'F' && header[3] == (byte)'F' &&
                    header[8] == (byte)'W' && header[9] == (byte)'E' &&
                    header[10] == (byte)'B' && header[11] == (byte)'P')
                    return true;
                return read >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M';
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static MeetupCodeRecord CreateMeetupCode(
            long creatorAccountId,
            long roomId,
            long subRoomId,
            long roomInstanceId,
            int maxUses = 100,
            int lifetimeMinutes = 360)
        {
            if (creatorAccountId <= 0 || roomId <= 0 || roomInstanceId <= 0)
                throw new ArgumentOutOfRangeException(nameof(roomInstanceId));

            lock (Sync)
            {
                DeleteExpiredMeetupCodesNoLock();

                MeetupCodeRecord? existing = MeetupCodes.Find(value =>
                        value.CreatorAccountId == creatorAccountId &&
                        value.RoomInstanceId == roomInstanceId &&
                        value.IsActive)
                    .OrderByDescending(value => value.CreatedAtUtc)
                    .FirstOrDefault(value => value.ExpiresAtUtc > DateTime.UtcNow);
                if (existing != null)
                    return existing;

                string code;
                do
                {
                    code = GenerateMeetupCode();
                }
                while (MeetupCodes.Exists(value => value.Code == code));

                DateTime now = DateTime.UtcNow;
                var record = new MeetupCodeRecord
                {
                    Code = code,
                    CreatorAccountId = creatorAccountId,
                    RoomId = roomId,
                    SubRoomId = subRoomId,
                    RoomInstanceId = roomInstanceId,
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddMinutes(Math.Clamp(lifetimeMinutes, 5, 1_440)),
                    MaxUses = Math.Clamp(maxUses, 1, 1_000),
                    IsActive = true
                };
                MeetupCodes.Insert(record);
                return record;
            }
        }

        public static MeetupCodeRecord? GetMeetupCode(string? code)
        {
            string normalized = NormalizeMeetupCode(code);
            if (normalized.Length == 0)
                return null;

            lock (Sync)
            {
                DeleteExpiredMeetupCodesNoLock();
                MeetupCodeRecord? record = MeetupCodes.FindById(normalized);
                return record != null && record.IsActive &&
                       record.ExpiresAtUtc > DateTime.UtcNow &&
                       record.UseCount < record.MaxUses
                    ? record
                    : null;
            }
        }

        public static void RecordMeetupCodeUse(string code)
        {
            lock (Sync)
            {
                MeetupCodeRecord? record = MeetupCodes.FindById(NormalizeMeetupCode(code));
                if (record == null)
                    return;
                record.UseCount++;
                if (record.UseCount >= record.MaxUses)
                    record.IsActive = false;
                MeetupCodes.Update(record);
            }
        }

        public static bool RevokeMeetupCode(string code, long callerAccountId)
        {
            lock (Sync)
            {
                MeetupCodeRecord? record = MeetupCodes.FindById(NormalizeMeetupCode(code));
                if (record == null || record.CreatorAccountId != callerAccountId)
                    return false;
                record.IsActive = false;
                return MeetupCodes.Update(record);
            }
        }

        public static object ToClientMeetupCode(MeetupCodeRecord value) => new
        {
            Code = value.Code,
            MeetupCode = value.Code,
            CreatorAccountId = value.CreatorAccountId,
            RoomId = value.RoomId,
            SubRoomId = value.SubRoomId,
            RoomInstanceId = value.RoomInstanceId,
            CreatedAt = value.CreatedAtUtc,
            ExpiresAt = value.ExpiresAtUtc,
            UseCount = value.UseCount,
            MaxUses = value.MaxUses,
            IsActive = value.IsActive
        };

        public static CloudVariableRecord SetCloudVariable(
            long roomId,
            long accountId,
            string key,
            string valueJson,
            long updatedByAccountId)
        {
            if (roomId <= 0 || accountId < 0 || updatedByAccountId <= 0)
                throw new ArgumentOutOfRangeException(nameof(roomId));

            string normalizedKey = NormalizeCloudKey(key);
            if (normalizedKey.Length == 0)
                throw new ArgumentException("A cloud variable key is required.", nameof(key));
            if (Encoding.UTF8.GetByteCount(valueJson) > 16 * 1024)
                throw new ArgumentException("Cloud variable values are limited to 16 KiB.", nameof(valueJson));

            lock (Sync)
            {
                string id = BuildCloudVariableId(roomId, accountId, normalizedKey);
                CloudVariableRecord? existing = CloudVariables.FindById(id);

                if (existing == null)
                {
                    int count = CloudVariables.Count(value =>
                        value.RoomId == roomId && value.AccountId == accountId);
                    if (count >= 512)
                        throw new InvalidOperationException(
                            "This room/player scope already has 512 cloud variables.");
                }

                var record = existing ?? new CloudVariableRecord
                {
                    Id = id,
                    RoomId = roomId,
                    AccountId = accountId,
                    Key = normalizedKey,
                    Version = 0
                };

                record.ValueJson = string.IsNullOrWhiteSpace(valueJson) ? "null" : valueJson;
                record.Version = Math.Max(1, record.Version + 1);
                record.UpdatedAtUtc = DateTime.UtcNow;
                record.UpdatedByAccountId = updatedByAccountId;
                CloudVariables.Upsert(record);
                return record;
            }
        }

        public static CloudVariableRecord? GetCloudVariable(
            long roomId,
            long accountId,
            string key)
        {
            if (roomId <= 0 || accountId < 0)
                return null;
            return CloudVariables.FindById(
                BuildCloudVariableId(roomId, accountId, NormalizeCloudKey(key)));
        }

        public static List<CloudVariableRecord> GetCloudVariables(
            long roomId,
            long accountId,
            bool includeShared = true)
        {
            if (roomId <= 0 || accountId < 0)
                return new List<CloudVariableRecord>();

            return CloudVariables.Find(value =>
                    value.RoomId == roomId &&
                    (value.AccountId == accountId ||
                     (includeShared && value.AccountId == 0)))
                .OrderBy(value => value.Key)
                .ThenBy(value => value.AccountId)
                .ToList();
        }

        public static bool DeleteCloudVariable(
            long roomId,
            long accountId,
            string key) =>
            CloudVariables.Delete(
                BuildCloudVariableId(roomId, accountId, NormalizeCloudKey(key)));

        public static RoomPlayerDataRecord SaveRoomPlayerData(
            long roomId,
            long accountId,
            string dataJson,
            long updatedByAccountId)
        {
            if (roomId <= 0 || accountId <= 0 || updatedByAccountId <= 0)
                throw new ArgumentOutOfRangeException(nameof(roomId));

            string normalizedJson = string.IsNullOrWhiteSpace(dataJson)
                ? "{}"
                : dataJson;
            if (Encoding.UTF8.GetByteCount(normalizedJson) > 256 * 1024)
            {
                throw new ArgumentException(
                    "Room player data is limited to 256 KiB.",
                    nameof(dataJson));
            }

            using (JsonDocument.Parse(normalizedJson))
            {
            }

            lock (Sync)
            {
                string id = BuildRoomPlayerDataId(roomId, accountId);
                RoomPlayerDataRecord? existing = RoomPlayerData.FindById(id);
                var record = existing ?? new RoomPlayerDataRecord
                {
                    Id = id,
                    RoomId = roomId,
                    AccountId = accountId,
                    Version = 0
                };

                record.DataJson = normalizedJson;
                record.Version = Math.Max(1, record.Version + 1);
                record.UpdatedAtUtc = DateTime.UtcNow;
                record.UpdatedByAccountId = updatedByAccountId;
                RoomPlayerData.Upsert(record);
                return record;
            }
        }

        public static RoomPlayerDataRecord? GetRoomPlayerData(
            long roomId,
            long accountId)
        {
            if (roomId <= 0 || accountId <= 0)
                return null;
            return RoomPlayerData.FindById(BuildRoomPlayerDataId(roomId, accountId));
        }

        public static bool DeleteRoomPlayerData(long roomId, long accountId)
        {
            if (roomId <= 0 || accountId <= 0)
                return false;
            return RoomPlayerData.Delete(BuildRoomPlayerDataId(roomId, accountId));
        }

        public static object ToClientCloudVariable(CloudVariableRecord value)
        {
            object? parsedValue;
            try
            {
                using JsonDocument document = JsonDocument.Parse(value.ValueJson);
                parsedValue = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                parsedValue = value.ValueJson;
            }

            return new
            {
                Key = value.Key,
                Name = value.Key,
                VariableName = value.Key,
                CloudVariableName = value.Key,
                Value = parsedValue,
                VariableValue = parsedValue,
                CloudVariableValue = parsedValue,
                ValueJson = value.ValueJson,
                RoomId = value.RoomId,
                AccountId = value.AccountId,
                PlayerId = value.AccountId,
                IsShared = value.AccountId == 0,
                IsPlayerVariable = value.AccountId != 0,
                Scope = value.AccountId == 0 ? "room" : "player",
                Version = value.Version,
                UpdatedAt = value.UpdatedAtUtc,
                UpdatedByAccountId = value.UpdatedByAccountId
            };
        }

        private static void MigrateLegacyInventionFiles()
        {
            string root = Path.Combine(Program.dataDir, "Inventions");
            if (!Directory.Exists(root))
                return;

            int imported = 0;
            foreach (string accountDirectory in Directory.EnumerateDirectories(root))
            {
                if (!long.TryParse(Path.GetFileName(accountDirectory), out long accountId) ||
                    accountId <= 0)
                    continue;

                foreach (string file in Directory.EnumerateFiles(
                             accountDirectory,
                             "*.json",
                             SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(
                            File.ReadAllText(file));
                        long fileId = long.TryParse(
                            Path.GetFileNameWithoutExtension(file),
                            out long parsedId)
                            ? parsedId
                            : 0;
                        if (fileId > 0 && Inventions.FindById(fileId) != null)
                            continue;

                        JsonElement payload = document.RootElement.Clone();
                        SaveInvention(
                            accountId,
                            payload,
                            fileId > 0 ? fileId : null);
                        imported++;
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine(
                            $"[INVENTION MIGRATION] skipped={file} error={exception.Message}");
                    }
                }
            }

            if (imported > 0)
                Console.WriteLine($"[INVENTION MIGRATION] imported={imported}");
        }

        private static InventionRecord? FindInventionNoLock(long inventionId)
        {
            InventionRecord? direct = Inventions.FindById(inventionId);
            if (direct != null)
                return direct;

            InventionAliasRecord? alias = InventionAliases.FindById(inventionId);
            return alias == null
                ? null
                : Inventions.FindById(alias.CurrentInventionId);
        }

        private static string InventionPurchaseId(long inventionId, long accountId) =>
            $"{accountId}:{inventionId}";

        private static long NextInventionIdNoLock()
        {
            long max = Inventions.FindAll()
                .Where(value => value.InventionId > 0 &&
                                value.InventionId <= MaxClientInventionId)
                .Select(value => value.InventionId)
                .DefaultIfEmpty(0)
                .Max();

            long candidate = Math.Max(1, max + 1);
            while (candidate <= MaxClientInventionId &&
                   Inventions.FindById(candidate) != null)
            {
                candidate++;
            }

            if (candidate > MaxClientInventionId)
                throw new InvalidOperationException("No client-compatible invention IDs remain.");
            return candidate;
        }

        private static void RepairOversizedInventionIds()
        {
            lock (Sync)
            {
                List<InventionRecord> oversized = Inventions.FindAll()
                    .Where(value => value.InventionId > MaxClientInventionId)
                    .OrderBy(value => value.CreatedAtUtc)
                    .ThenBy(value => value.InventionId)
                    .ToList();
                if (oversized.Count == 0)
                    return;

                int repaired = 0;
                foreach (InventionRecord record in oversized)
                {
                    long oldId = record.InventionId;
                    long newId = NextInventionIdNoLock();

                    record.InventionId = newId;
                    Inventions.Insert(record);
                    Inventions.Delete(oldId);

                    foreach (InventionCheerRecord cheer in InventionCheers
                                 .Find(value => value.InventionId == oldId)
                                 .ToList())
                    {
                        string oldCheerId = cheer.Id;
                        cheer.InventionId = newId;
                        cheer.Id = InventionCheerId(newId, cheer.AccountId);
                        InventionCheers.Upsert(cheer);
                        if (!string.Equals(oldCheerId, cheer.Id, StringComparison.Ordinal))
                            InventionCheers.Delete(oldCheerId);
                    }

                    foreach (InventionPurchaseRecord purchase in InventionPurchases
                                 .Find(value => value.InventionId == oldId)
                                 .ToList())
                    {
                        string oldPurchaseId = purchase.Id;
                        purchase.InventionId = newId;
                        purchase.Id = InventionPurchaseId(newId, purchase.AccountId);
                        InventionPurchases.Upsert(purchase);
                        if (!string.Equals(oldPurchaseId, purchase.Id, StringComparison.Ordinal))
                            InventionPurchases.Delete(oldPurchaseId);
                    }

                    foreach (InventionAliasRecord existingAlias in InventionAliases
                                 .Find(value => value.CurrentInventionId == oldId)
                                 .ToList())
                    {
                        existingAlias.CurrentInventionId = newId;
                        InventionAliases.Update(existingAlias);
                    }

                    InventionAliases.Upsert(new InventionAliasRecord
                    {
                        LegacyInventionId = oldId,
                        CurrentInventionId = newId,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    repaired++;
                    Console.WriteLine(
                        $"[INVENTION ID REPAIR] old={oldId} new={newId} name={record.Name}");
                }

                Console.WriteLine($"[INVENTION ID REPAIR] repaired={repaired}");
            }
        }

        private static string GenerateMeetupCode()
        {
            const string alphabet = "23456789abcdefghjkmnpqrstuvwxyz";
            byte[] bytes = RandomNumberGenerator.GetBytes(8);
            var chars = new char[8];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = alphabet[bytes[i] % alphabet.Length];
            return new string(chars);
        }

        private static string NormalizeMeetupCode(string? code) =>
            new((code ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .Take(16)
                .ToArray());

        private static void DeleteExpiredMeetupCodesNoLock()
        {
            DateTime now = DateTime.UtcNow;
            MeetupCodes.DeleteMany(value =>
                !value.IsActive ||
                value.ExpiresAtUtc <= now ||
                value.UseCount >= value.MaxUses);
        }

        private static string NormalizeCloudKey(string? key)
        {
            string value = (key ?? string.Empty).Trim();
            if (value.Length > 128)
                value = value[..128];
            return value.Replace('\0', '_');
        }

        public static void MigrateRoomId(long oldRoomId, long newRoomId)
        {
            lock (Sync)
            {
                foreach (InventionRecord invention in Inventions.Find(value => value.RoomId == oldRoomId).ToList())
                {
                    invention.RoomId = newRoomId;
                    Inventions.Update(invention);
                }

                foreach (CloudVariableRecord variable in CloudVariables.Find(value => value.RoomId == oldRoomId).ToList())
                {
                    CloudVariables.Delete(variable.Id);
                    variable.Id = BuildCloudVariableId(newRoomId, variable.AccountId, variable.Key);
                    variable.RoomId = newRoomId;
                    CloudVariables.Upsert(variable);
                }

                foreach (RoomPlayerDataRecord data in RoomPlayerData.Find(value => value.RoomId == oldRoomId).ToList())
                {
                    RoomPlayerData.Delete(data.Id);
                    data.Id = BuildRoomPlayerDataId(newRoomId, data.AccountId);
                    data.RoomId = newRoomId;
                    RoomPlayerData.Upsert(data);
                }
            }
        }

        private static string BuildCloudVariableId(
            long roomId,
            long accountId,
            string key)
        {
            byte[] digest = SHA256.HashData(
                Encoding.UTF8.GetBytes(key.ToLowerInvariant()));
            return $"{roomId}:{accountId}:{Convert.ToHexString(digest)}";
        }

        private static string BuildRoomPlayerDataId(long roomId, long accountId) =>
            $"{roomId}:{accountId}";

        private static string Limit(string? value, int maxLength)
        {
            string result = value?.Trim() ?? string.Empty;
            return result.Length <= maxLength ? result : result[..maxLength];
        }

        private static string StableReplicationId(
            string scope,
            long inventionId,
            int version)
        {
            byte[] source = Encoding.UTF8.GetBytes(
                $"{scope}:{inventionId}:{version}");
            byte[] hash = SHA256.HashData(source);
            var guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, guidBytes.Length);
            return new Guid(guidBytes).ToString();
        }

        private static long? FindLong(JsonElement element, params string[] names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (names.Any(name => string.Equals(
                            name,
                            property.Name,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        if (property.Value.ValueKind == JsonValueKind.Number &&
                            property.Value.TryGetInt64(out long number))
                            return number;
                        if (long.TryParse(property.Value.ToString(), out number))
                            return number;
                    }

                    long? nested = FindLong(property.Value, names);
                    if (nested.HasValue)
                        return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    long? nested = FindLong(item, names);
                    if (nested.HasValue)
                        return nested;
                }
            }
            else if (TryParseEmbeddedJson(element, out JsonElement embedded))
            {
                return FindLong(embedded, names);
            }
            return null;
        }

        private static string? FindString(JsonElement element, params string[] names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (names.Any(name => string.Equals(
                            name,
                            property.Name,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                            return property.Value.GetString();
                        if (property.Value.ValueKind is JsonValueKind.Number or
                            JsonValueKind.True or JsonValueKind.False)
                            return property.Value.ToString();
                    }

                    string? nested = FindString(property.Value, names);
                    if (nested != null)
                        return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string? nested = FindString(item, names);
                    if (nested != null)
                        return nested;
                }
            }
            else if (TryParseEmbeddedJson(element, out JsonElement embedded))
            {
                return FindString(embedded, names);
            }
            return null;
        }

        private static bool? FindBool(JsonElement element, params string[] names)
        {
            string? raw = FindString(element, names);
            if (bool.TryParse(raw, out bool boolean))
                return boolean;
            if (int.TryParse(raw, out int number))
                return number != 0;

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Any(name => string.Equals(
                            name,
                            property.Name,
                            StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (property.Value.ValueKind == JsonValueKind.True)
                        return true;
                    if (property.Value.ValueKind == JsonValueKind.False)
                        return false;
                }
            }
            return null;
        }

        private static bool TryParseEmbeddedJson(
            JsonElement element,
            out JsonElement parsed)
        {
            parsed = default;
            if (element.ValueKind != JsonValueKind.String)
                return false;

            string? raw = element.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(raw) ||
                (raw[0] != '{' && raw[0] != '['))
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(raw);
                parsed = document.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static List<long> ReadLongList(
            JsonElement element,
            params string[] names)
        {
            var output = new List<long>();
            if (TryParseEmbeddedJson(element, out JsonElement embeddedRoot))
                return ReadLongList(embeddedRoot, names);

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out long number))
                    {
                        if (number > 0)
                            output.Add(number);
                    }
                    else if (long.TryParse(item.ToString(), out number) && number > 0)
                    {
                        output.Add(number);
                    }
                    else if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        output.AddRange(ReadLongList(item, names));
                    }
                }
                return output.Distinct().ToList();
            }

            if (element.ValueKind != JsonValueKind.Object)
                return output;

            foreach (JsonProperty property in element.EnumerateObject())
            {
                bool requested = names.Any(name => string.Equals(
                    name,
                    property.Name,
                    StringComparison.OrdinalIgnoreCase));
                if (requested)
                {
                    JsonElement value = property.Value;
                    if (TryParseEmbeddedJson(value, out JsonElement embedded))
                        value = embedded;

                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        output.AddRange(ReadLongList(value, names));
                    }
                    else if (value.ValueKind == JsonValueKind.Number &&
                             value.TryGetInt64(out long number) && number > 0)
                    {
                        output.Add(number);
                    }
                    else
                    {
                        foreach (string part in value.ToString().Split(
                                     new[] { ',', ';' },
                                     StringSplitOptions.RemoveEmptyEntries |
                                     StringSplitOptions.TrimEntries))
                        {
                            if (long.TryParse(part, out number) && number > 0)
                                output.Add(number);
                        }
                    }
                }
                else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    output.AddRange(ReadLongList(property.Value, names));
                }
                else if (TryParseEmbeddedJson(property.Value, out JsonElement embedded))
                {
                    output.AddRange(ReadLongList(embedded, names));
                }
            }

            return output.Distinct().ToList();
        }

        private static List<string> ReadStringList(
            JsonElement element,
            params string[] names)
        {
            var output = new List<string>();
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                    output.AddRange(ReadStringList(item, names));
                return output
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (TryParseEmbeddedJson(element, out JsonElement embeddedRoot))
                return ReadStringList(embeddedRoot, names);

            if (element.ValueKind != JsonValueKind.Object)
                return output;

            foreach (JsonProperty property in element.EnumerateObject())
            {
                bool isRequestedProperty = names.Any(name => string.Equals(
                    name,
                    property.Name,
                    StringComparison.OrdinalIgnoreCase));
                if (!isRequestedProperty)
                {
                    if (property.Value.ValueKind is JsonValueKind.Object or
                        JsonValueKind.Array)
                    {
                        output.AddRange(ReadStringList(property.Value, names));
                    }
                    else if (TryParseEmbeddedJson(
                                 property.Value,
                                 out JsonElement embedded))
                    {
                        output.AddRange(ReadStringList(embedded, names));
                    }
                    continue;
                }

                JsonElement tagValue = property.Value;
                if (TryParseEmbeddedJson(tagValue, out JsonElement parsedTags))
                    tagValue = parsedTags;

                if (tagValue.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in tagValue.EnumerateArray())
                    {
                        string? tag = item.ValueKind == JsonValueKind.String
                            ? item.GetString()
                            : item.ValueKind == JsonValueKind.Object
                                ? FindString(item, "Tag", "tag", "Name", "name")
                                : item.ToString();
                        if (!string.IsNullOrWhiteSpace(tag))
                            output.Add(Limit(tag, 32));
                    }
                }
                else
                {
                    foreach (string item in tagValue.ToString().Split(
                                 new[] { ',', ';' },
                                 StringSplitOptions.RemoveEmptyEntries |
                                 StringSplitOptions.TrimEntries))
                    {
                        output.Add(Limit(item, 32));
                    }
                }
            }

            return output
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
