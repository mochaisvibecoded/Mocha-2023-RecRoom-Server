using LiteDB;
using System.Net;
using System.Text.Json;
using Mocha2023.Classes;

namespace Mocha2023.Classes.DBs
{
    public static class RecNetDB
    {
        private const int ChallengeDefinitionVersion = 2;
        private static readonly LiteDatabase Database =
            new(Path.Combine(Program.dataDir, "DBs", "RecNet.db"));

        public static readonly ILiteCollection<PhotoCheer> PhotoCheers =
            Database.GetCollection<PhotoCheer>("PhotoCheers");

        public static readonly ILiteCollection<PhotoComment> PhotoComments =
            Database.GetCollection<PhotoComment>("PhotoComments");

        public static readonly ILiteCollection<SavedImage> SavedImages =
            Database.GetCollection<SavedImage>("SavedImages");

        public static readonly ILiteCollection<ModerationLock> ModerationLocks =
            Database.GetCollection<ModerationLock>("ModerationLocks");

        public static readonly ILiteCollection<Event> Events =
            Database.GetCollection<Event>("Events");

        public static readonly ILiteCollection<Announcement> Announcements =
            Database.GetCollection<Announcement>("Announcements");

        public static readonly ILiteCollection<ChallengeProgress> ChallengeProgresses =
            Database.GetCollection<ChallengeProgress>("ChallengeProgresses");

        public static readonly ILiteCollection<ChallengeCompletionEvent> ChallengeCompletionEvents =
            Database.GetCollection<ChallengeCompletionEvent>("ChallengeCompletionEvents");

        public static readonly ILiteCollection<FeatureSetting> FeatureSettings =
            Database.GetCollection<FeatureSetting>("FeatureSettings");

        public static readonly ILiteCollection<IpBan> IpBans =
            Database.GetCollection<IpBan>("IpBans");

        private static readonly object ChallengeProgressLock = new();
        private static readonly object FeatureSettingsLock = new();
        private const string RecNetSignupSettingKey = "RecNet.SignupEnabled";
        private const string AccountCreationSettingKey = "Accounts.CreationEnabled";
        private const string VpnBlockingSettingKey = "Security.VpnBlockingEnabled";

        static RecNetDB()
        {
            SavedImages.EnsureIndex(image => image.AccountId);
            SavedImages.EnsureIndex(image => image.ImageId);
            SavedImages.EnsureIndex(image => image.ContentHash);
            SavedImages.EnsureIndex(image => image.LookupName);
            SavedImages.EnsureIndex(image => image.CreatedAt);
            PhotoCheers.EnsureIndex(cheer => cheer.PhotoPath);
            PhotoCheers.EnsureIndex(cheer => cheer.AccountId);
            IpBans.EnsureIndex(ban => ban.Network, unique: true);
            IpBans.EnsureIndex(ban => ban.CreatedAt);

            foreach (PhotoCheer cheer in PhotoCheers.FindAll().ToList())
            {
                string normalizedPath = NormalizePhotoPath(cheer.PhotoPath);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    PhotoCheers.Delete(cheer.Id);
                    continue;
                }

                string normalizedId = PhotoCheerId(normalizedPath, cheer.AccountId);
                if (string.Equals(cheer.Id, normalizedId, StringComparison.Ordinal) &&
                    string.Equals(cheer.PhotoPath, normalizedPath, StringComparison.Ordinal))
                {
                    continue;
                }

                PhotoCheers.Delete(cheer.Id);
                cheer.Id = normalizedId;
                cheer.PhotoPath = normalizedPath;
                PhotoCheers.Upsert(cheer);
            }

            foreach (var image in SavedImages.FindAll()
                         .Where(image => string.IsNullOrWhiteSpace(image.LookupName)))
            {
                string lookupName = Path.GetFileName(image.PhotoPath ?? string.Empty)
                    ?.ToLowerInvariant() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(lookupName))
                    continue;

                image.LookupName = lookupName;
                SavedImages.Update(image);
            }
        }

        public class PhotoCheer
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;
            public string PhotoPath { get; set; } = string.Empty;
            public long AccountId { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public static string NormalizePhotoPath(string? photoPath) =>
            (photoPath ?? string.Empty)
                .Replace('\\', '/')
                .Trim()
                .TrimStart('/')
                .ToLowerInvariant();

        public static string PhotoCheerId(string? photoPath, long accountId) =>
            $"{accountId}:{NormalizePhotoPath(photoPath)}";

        public static bool SetPhotoCheer(
            string? photoPath,
            long accountId,
            bool cheered)
        {
            string normalizedPath = NormalizePhotoPath(photoPath);
            if (accountId <= 0 || string.IsNullOrWhiteSpace(normalizedPath))
                return false;

            string id = PhotoCheerId(normalizedPath, accountId);
            if (!cheered)
            {
                PhotoCheers.Delete(id);
                return true;
            }

            PhotoCheers.Upsert(new PhotoCheer
            {
                Id = id,
                PhotoPath = normalizedPath,
                AccountId = accountId,
                CreatedAt = DateTime.UtcNow
            });
            return true;
        }

        public static bool HasPhotoCheer(string? photoPath, long accountId)
        {
            if (accountId <= 0)
                return false;
            return PhotoCheers.Exists(value =>
                value.Id == PhotoCheerId(photoPath, accountId));
        }

        public static int CountPhotoCheers(string? photoPath)
        {
            string normalizedPath = NormalizePhotoPath(photoPath);
            return string.IsNullOrWhiteSpace(normalizedPath)
                ? 0
                : PhotoCheers.Count(value => value.PhotoPath == normalizedPath);
        }

        public class PhotoComment
        {
            [BsonId]
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public string PhotoPath { get; set; } = string.Empty;
            public long AccountId { get; set; }
            public string Text { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
        }

        public class SavedImage
        {
            [BsonId]
            public string PhotoPath { get; set; } = string.Empty;
            public long ImageId { get; set; }
            public long AccountId { get; set; }
            public long? RoomId { get; set; }
            public long? PlayerEventId { get; set; }
            public int SavedImageType { get; set; }
            public int Accessibility { get; set; }
            public List<int> TaggedPlayerIds { get; set; } = new();
            public string ContentHash { get; set; } = string.Empty;
            public string LookupName { get; set; } = string.Empty;
            public long ByteLength { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class ModerationLock
        {
            [BsonId]
            public long AccountId { get; set; }
            public string Reason { get; set; } = string.Empty;
            public long IssuedByAccountId { get; set; }
            public DateTime IssuedAt { get; set; }
            public string BanGroupId { get; set; } = string.Empty;
            public long? RelatedAccountId { get; set; }
            public string? RelatedUsername { get; set; }
        }

        public class Event
        {
            [BsonId]
            public long Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string? ImageName { get; set; }
            public DateTime StartsAt { get; set; }
            public DateTime? EndsAt { get; set; }
            public bool Pinned { get; set; }
            public long CreatedByAccountId { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class Announcement
        {
            [BsonId]
            public long Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string BodyMarkdown { get; set; } = string.Empty;
            public string Kind { get; set; } = "info";
            public bool Pinned { get; set; }
            public bool Published { get; set; } = true;
            public long CreatedByAccountId { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        public class ChallengeProgress
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;
            public long AccountId { get; set; }
            public int ChallengeMapId { get; set; }
            public int ChallengeId { get; set; }
            public int Progress { get; set; }
            public int Goal { get; set; }
            public bool Complete { get; set; }
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        public class ChallengeCompletionEvent
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;
            public long AccountId { get; set; }
            public int ChallengeMapId { get; set; }
            public int ChallengeId { get; set; }
            public string SessionId { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class FeatureSetting
        {
            [BsonId]
            public string Key { get; set; } = string.Empty;
            public bool Enabled { get; set; }
            public long UpdatedByAccountId { get; set; }
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        public class IpBan
        {
            [BsonId]
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public string Network { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
            public long CreatedByAccountId { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        private static bool IsFeatureEnabled(string key, bool defaultValue)
        {
            lock (FeatureSettingsLock)
                return FeatureSettings.FindById(key)?.Enabled ?? defaultValue;
        }

        private static void SetFeatureEnabled(
            string key,
            bool enabled,
            long updatedByAccountId)
        {
            lock (FeatureSettingsLock)
            {
                FeatureSettings.Upsert(new FeatureSetting
                {
                    Key = key,
                    Enabled = enabled,
                    UpdatedByAccountId = updatedByAccountId,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        public static bool IsAccountCreationEnabled() =>
            IsFeatureEnabled(AccountCreationSettingKey, true);

        public static void SetAccountCreationEnabled(bool enabled, long updatedByAccountId) =>
            SetFeatureEnabled(AccountCreationSettingKey, enabled, updatedByAccountId);

        public static bool IsVpnBlockingEnabled() =>
            IsFeatureEnabled(VpnBlockingSettingKey, true);

        public static void SetVpnBlockingEnabled(bool enabled, long updatedByAccountId) =>
            SetFeatureEnabled(VpnBlockingSettingKey, enabled, updatedByAccountId);

        public static IReadOnlyList<IpBan> GetIpBans() =>
            IpBans.FindAll().OrderByDescending(value => value.CreatedAt).ToList();

        public static IpBan AddIpBan(
            string network,
            string? reason,
            long createdByAccountId)
        {
            if (!IpNetwork.TryNormalize(network, out string normalized))
                throw new ArgumentException("Enter a valid IPv4/IPv6 address or CIDR range.");

            string cleanedReason = (reason ?? string.Empty).Trim();
            if (cleanedReason.Length > 500)
                cleanedReason = cleanedReason[..500];

            IpBan? existing = IpBans.FindOne(value => value.Network == normalized);
            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(cleanedReason))
                    existing.Reason = cleanedReason;
                existing.CreatedByAccountId = createdByAccountId;
                existing.CreatedAt = DateTime.UtcNow;
                IpBans.Update(existing);
                return existing;
            }

            var record = new IpBan
            {
                Network = normalized,
                Reason = cleanedReason,
                CreatedByAccountId = createdByAccountId,
                CreatedAt = DateTime.UtcNow
            };
            IpBans.Insert(record);
            return record;
        }

        public static bool RemoveIpBan(string id) =>
            !string.IsNullOrWhiteSpace(id) && IpBans.Delete(id);

        public static bool TryGetIpBan(IPAddress address, out IpBan? record)
        {
            record = IpBans.FindAll().FirstOrDefault(value =>
                IpNetwork.Contains(value.Network, address));
            return record != null;
        }

        public static bool IsRecNetSignupEnabled() =>
            IsFeatureEnabled(RecNetSignupSettingKey, true);

        public static void SetRecNetSignupEnabled(bool enabled, long updatedByAccountId) =>
            SetFeatureEnabled(RecNetSignupSettingKey, enabled, updatedByAccountId);

        public static (int MapId, DateTime StartsAt, DateTime EndsAt) GetCurrentChallengeWindow()
        {
            DateTime now = DateTime.UtcNow;
            int daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
            DateTime startsAt = now.Date.AddDays(-daysSinceMonday);
            DateTime endsAt = startsAt.AddDays(7);
            int mapId = ((startsAt.Year * 1000) + startsAt.DayOfYear) * 10 +
                        ChallengeDefinitionVersion;
            return (mapId, startsAt, endsAt);
        }

        public static ChallengeProgress GetChallengeProgress(
            long accountId,
            int challengeMapId,
            int challengeId,
            int goal)
        {
            string id = ChallengeProgressId(accountId, challengeMapId, challengeId);
            var saved = ChallengeProgresses.FindById(id);
            return saved ?? new ChallengeProgress
            {
                Id = id,
                AccountId = accountId,
                ChallengeMapId = challengeMapId,
                ChallengeId = challengeId,
                Goal = Math.Max(1, goal)
            };
        }

        public static ChallengeProgress SetChallengeProgress(
            long accountId,
            int challengeMapId,
            int challengeId,
            int progress,
            int goal,
            bool? complete = null)
        {
            goal = Math.Max(1, goal);
            progress = Math.Clamp(progress, 0, goal);

            lock (ChallengeProgressLock)
            {
                var saved = GetChallengeProgress(accountId, challengeMapId, challengeId, goal);
                saved.Goal = goal;
                saved.Progress = progress;
                saved.Complete = complete ?? progress >= goal;
                if (saved.Complete)
                    saved.Progress = goal;
                saved.UpdatedAt = DateTime.UtcNow;
                ChallengeProgresses.Upsert(saved);
                return saved;
            }
        }

        public static ChallengeProgress AddChallengeProgress(
            long accountId,
            int challengeMapId,
            int challengeId,
            int amount,
            int goal)
        {
            lock (ChallengeProgressLock)
            {
                var saved = GetChallengeProgress(accountId, challengeMapId, challengeId, goal);
                int next = Math.Clamp(saved.Progress + Math.Max(0, amount), 0, Math.Max(1, goal));
                saved.Goal = Math.Max(1, goal);
                saved.Progress = next;
                saved.Complete = next >= saved.Goal;
                saved.UpdatedAt = DateTime.UtcNow;
                ChallengeProgresses.Upsert(saved);
                return saved;
            }
        }

        public static void ApplyChallengeTelemetry(long accountId, string eventType, JsonElement eventData)
        {
            if (accountId <= 0 || eventData.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return;

            var (mapId, _, _) = GetCurrentChallengeWindow();
            string searchable = eventData.GetRawText();

            bool isFinishedEvent =
                eventType.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
                eventType.Contains("end", StringComparison.OrdinalIgnoreCase) ||
                HasTruthyMatchingValue(eventData, "complete", "victory", "won", "win");

            bool isCrimsonCauldron =
                searchable.Contains("CrimsonCauldron", StringComparison.OrdinalIgnoreCase) ||
                searchable.Contains("Crimson Cauldron", StringComparison.OrdinalIgnoreCase) ||
                searchable.Contains("Goblin2", StringComparison.OrdinalIgnoreCase);
            if (isCrimsonCauldron && isFinishedEvent)
            {
                SetChallengeProgress(accountId, mapId, challengeId: 1, progress: 1, goal: 1);
            }

            bool isGoldenTrophy = searchable.Contains("GoldenTrophy", StringComparison.OrdinalIgnoreCase) ||
                                  searchable.Contains("Golden Trophy", StringComparison.OrdinalIgnoreCase);
            if (isGoldenTrophy && isFinishedEvent)
            {
                SetChallengeProgress(accountId, mapId, challengeId: 2, progress: 1, goal: 1);
            }

            bool isJumbotron = searchable.Contains("Jumbotron", StringComparison.OrdinalIgnoreCase);
            if (isJumbotron && isFinishedEvent)
            {
                SetChallengeProgress(accountId, mapId, challengeId: 3, progress: 1, goal: 1);
            }

            bool isPaintball = searchable.Contains("Paintball", StringComparison.OrdinalIgnoreCase);
            if (isPaintball && isFinishedEvent)
            {
                AddUniqueGameCompletion(accountId, mapId, challengeId: 4, eventData);
            }

            bool isDodgeball = searchable.Contains("Dodgeball", StringComparison.OrdinalIgnoreCase);
            if (isDodgeball && isFinishedEvent)
            {
                AddUniqueGameCompletion(accountId, mapId, challengeId: 5, eventData);
            }
        }

        private static void AddUniqueGameCompletion(
            long accountId,
            int challengeMapId,
            int challengeId,
            JsonElement eventData)
        {
            string? sessionId = FindMatchingScalar(
                eventData,
                "_GameSessionId",
                "GameSessionId",
                "SubroomInstanceId",
                "subroom_instance_id");

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                AddChallengeProgress(accountId, challengeMapId, challengeId, amount: 1, goal: 5);
                return;
            }

            string completionId = $"{accountId}:{challengeMapId}:{challengeId}:{sessionId}";
            lock (ChallengeProgressLock)
            {
                if (ChallengeCompletionEvents.FindById(completionId) != null)
                    return;

                ChallengeCompletionEvents.Insert(new ChallengeCompletionEvent
                {
                    Id = completionId,
                    AccountId = accountId,
                    ChallengeMapId = challengeMapId,
                    ChallengeId = challengeId,
                    SessionId = sessionId
                });
                AddChallengeProgress(accountId, challengeMapId, challengeId, amount: 1, goal: 5);
            }
        }

        private static string? FindMatchingScalar(JsonElement element, params string[] names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (names.Any(name => string.Equals(
                            property.Name,
                            name,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                            return property.Value.GetString();
                        if (property.Value.ValueKind == JsonValueKind.Number)
                            return property.Value.GetRawText();
                    }

                    string? nested = FindMatchingScalar(property.Value, names);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    string? nested = FindMatchingScalar(item, names);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }

            return null;
        }

        private static string ChallengeProgressId(long accountId, int challengeMapId, int challengeId) =>
            $"{accountId}:{challengeMapId}:{challengeId}";

        private static int FindLargestMatchingNumber(JsonElement element, params string[] names)
        {
            int largest = 0;
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (names.Any(name => property.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) &&
                        property.Value.ValueKind == JsonValueKind.Number &&
                        property.Value.TryGetInt32(out int value))
                    {
                        largest = Math.Max(largest, value);
                    }

                    largest = Math.Max(largest, FindLargestMatchingNumber(property.Value, names));
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    largest = Math.Max(largest, FindLargestMatchingNumber(item, names));
            }

            return largest;
        }

        private static bool HasTruthyMatchingValue(JsonElement element, params string[] names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    bool matchingName = names.Any(name =>
                        property.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
                    if (matchingName &&
                        (property.Value.ValueKind == JsonValueKind.True ||
                         (property.Value.ValueKind == JsonValueKind.Number &&
                          property.Value.TryGetInt32(out int value) && value > 0) ||
                         (property.Value.ValueKind == JsonValueKind.String &&
                          bool.TryParse(property.Value.GetString(), out bool parsed) && parsed)))
                    {
                        return true;
                    }

                    if (HasTruthyMatchingValue(property.Value, names))
                        return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (HasTruthyMatchingValue(item, names))
                        return true;
                }
            }

            return false;
        }
    }
}
