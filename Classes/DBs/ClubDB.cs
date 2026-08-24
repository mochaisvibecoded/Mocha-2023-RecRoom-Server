using LiteDB;
using static Mocha2023.Classes.DBs.DBClasses.ClubDBClasses;

namespace Mocha2023.Classes.DBs
{
    public static class ClubDB
    {
        private const int MaxCreatedClubsPerAccount = 100;
        private static readonly object Sync = new();
        private static readonly LiteDatabase Database =
            new(Path.Combine(Program.dataDir, "DBs", "Clubs.db"));

        public static readonly ILiteCollection<ClubRecord> Clubs =
            Database.GetCollection<ClubRecord>("Clubs");
        public static readonly ILiteCollection<ClubMembershipRecord> Memberships =
            Database.GetCollection<ClubMembershipRecord>("Memberships");
        public static readonly ILiteCollection<HomeClubRecord> HomeClubs =
            Database.GetCollection<HomeClubRecord>("HomeClubs");

        public static readonly IReadOnlyList<string> CategoryTags = new[]
        {
            "Social",
            "Creative",
            "Competitive",
            "Casual",
            "Educational",
            "Entertainment",
            "Lifestyle"
        };

        private static bool _legacyDataRepaired;

        static ClubDB()
        {
            Clubs.EnsureIndex(value => value.CreatorAccountId);
            Clubs.EnsureIndex(value => value.Name);
            Clubs.EnsureIndex(value => value.State);
            Memberships.EnsureIndex(value => value.ClubId);
            Memberships.EnsureIndex(value => value.AccountId);
            Memberships.EnsureIndex(value => value.MembershipType);

            RepairLegacyData();
        }

        public static void RepairLegacyData()
        {
            lock (Sync)
            {
                if (_legacyDataRepaired)
                    return;

                int clubsChanged = 0;
                int membershipsChanged = 0;
                int malformedMembershipsRemoved = 0;

                foreach (ClubMembershipRecord membership in Memberships.FindAll().ToList())
                {
                    if (membership.ClubId <= 0 || membership.AccountId <= 0)
                    {
                        if (!string.IsNullOrWhiteSpace(membership.Id))
                            Memberships.Delete(membership.Id);
                        malformedMembershipsRemoved++;
                        continue;
                    }

                    string expectedId = MembershipId(membership.ClubId, membership.AccountId);
                    if (string.Equals(membership.Id, expectedId, StringComparison.Ordinal))
                        continue;

                    ClubMembershipRecord? existing = Memberships.FindById(expectedId);
                    ClubMembershipType mergedType = existing == null
                        ? membership.MembershipType
                        : (ClubMembershipType)Math.Max(
                            (int)existing.MembershipType,
                            (int)membership.MembershipType);

                    if (!string.IsNullOrWhiteSpace(membership.Id))
                        Memberships.Delete(membership.Id);

                    UpsertMembershipUnsafe(
                        membership.ClubId,
                        membership.AccountId,
                        mergedType);
                    membershipsChanged++;
                }

                foreach (ClubRecord club in Clubs.FindAll().ToList())
                {
                    bool changed = false;

                    club.Name ??= string.Empty;
                    club.Description ??= string.Empty;
                    club.MainImageName = NormalizeImageName(club.MainImageName);
                    club.AdditionalImages ??= new List<string>();
                    club.CategoryTags ??= new List<string>();

                    string resolvedPrimary = ResolveLegacyPrimaryCategory(
                        club.PrimaryCategory,
                        club.CategoryTags);
                    List<string> normalizedTags = NormalizeTags(
                            new[] { resolvedPrimary }
                                .Concat(club.CategoryTags))
                        .ToList();

                    if (normalizedTags.Count == 0)
                        normalizedTags.Add(resolvedPrimary);

                    if (!string.Equals(
                            club.PrimaryCategory,
                            resolvedPrimary,
                            StringComparison.Ordinal))
                    {
                        Console.WriteLine(
                            $"[CLUB CATEGORY REPAIR] club={club.ClubId} " +
                            $"old={club.PrimaryCategory ?? "null"} new={resolvedPrimary} " +
                            $"tags={string.Join(',', club.CategoryTags)}");
                        club.PrimaryCategory = resolvedPrimary;
                        changed = true;
                    }

                    if (!club.CategoryTags.SequenceEqual(
                            normalizedTags,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        club.CategoryTags = normalizedTags;
                        changed = true;
                    }

                    ClubMembershipRecord? creatorMembership =
                        Memberships.FindById(MembershipId(
                            club.ClubId,
                            club.CreatorAccountId));

                    if (club.CreatorAccountId > 0 &&
                        creatorMembership?.MembershipType != ClubMembershipType.Creator)
                    {
                        UpsertMembershipUnsafe(
                            club.ClubId,
                            club.CreatorAccountId,
                            ClubMembershipType.Creator);
                        membershipsChanged++;
                    }

                    int actualMemberCount = Memberships.Count(value =>
                        value.ClubId == club.ClubId &&
                        value.MembershipType >= ClubMembershipType.Member);

                    if (club.MemberCount != actualMemberCount)
                    {
                        club.MemberCount = actualMemberCount;
                        changed = true;
                    }

                    if (changed)
                    {
                        club.UpdatedAt = DateTime.UtcNow;
                        Clubs.Update(club);
                        clubsChanged++;
                    }
                }

                foreach (HomeClubRecord home in HomeClubs.FindAll().ToList())
                {
                    ClubRecord? club = Clubs.FindById(home.ClubId);
                    if (club == null)
                    {
                        HomeClubs.Delete(home.AccountId);
                        continue;
                    }

                    ClubMembershipRecord? membership =
                        Memberships.FindById(MembershipId(
                            home.ClubId,
                            home.AccountId));

                    if (membership == null ||
                        membership.MembershipType < ClubMembershipType.Member)
                    {
                        ClubMembershipType type =
                            club.CreatorAccountId == home.AccountId
                                ? ClubMembershipType.Creator
                                : ClubMembershipType.Member;
                        UpsertMembershipUnsafe(home.ClubId, home.AccountId, type);
                        membershipsChanged++;
                    }
                }

                foreach (ClubRecord club in Clubs.FindAll().ToList())
                {
                    int actualMemberCount = Memberships.Count(value =>
                        value.ClubId == club.ClubId &&
                        value.MembershipType >= ClubMembershipType.Member);

                    if (club.MemberCount == actualMemberCount)
                        continue;

                    club.MemberCount = actualMemberCount;
                    club.UpdatedAt = DateTime.UtcNow;
                    Clubs.Update(club);
                    clubsChanged++;
                }

                _legacyDataRepaired = true;

                Console.WriteLine(
                    $"[CLUB REPAIR] clubs={clubsChanged} " +
                    $"memberships={membershipsChanged} " +
                    $"malformedRemoved={malformedMembershipsRemoved}");
            }
        }

        public static bool TryCreate(
            long creatorAccountId,
            string name,
            string description,
            string? mainImageName,
            ClubVisibility visibility,
            ClubJoinability joinability,
            bool allowJuniors,
            int minLevel,
            ClubType clubType,
            IEnumerable<string>? categoryTags,
            out ClubRecord? club,
            out string? error)
        {
            lock (Sync)
            {
                club = null;
                error = null;

                string normalizedName = name.Trim();

                ClubRecord? existingOwnedClub = Clubs.FindAll().FirstOrDefault(value =>
                    value.CreatorAccountId == creatorAccountId &&
                    value.State != ClubState.MarkedForDelete &&
                    string.Equals(
                        value.Name,
                        normalizedName,
                        StringComparison.OrdinalIgnoreCase));

                if (existingOwnedClub != null)
                {
                    UpsertMembershipUnsafe(
                        existingOwnedClub.ClubId,
                        creatorAccountId,
                        ClubMembershipType.Creator);

                    club = existingOwnedClub;
                    return true;
                }

                if (Clubs.Count(value =>
                        value.CreatorAccountId == creatorAccountId &&
                        value.State != ClubState.MarkedForDelete) >=
                    MaxCreatedClubsPerAccount)
                {
                    error = "club_creation_limit";
                    return false;
                }

                List<string> tags = NormalizeTags(categoryTags).ToList();
                if (tags.Count == 0)
                    tags.Add("Social");

                club = new ClubRecord
                {
                    ClubId = NextClubId(),
                    Name = normalizedName,
                    Description = description.Trim(),
                    MainImageName = NormalizeImageName(mainImageName),
                    MemberCount = 1,
                    PrimaryCategory = tags[0],
                    Visibility = visibility,
                    Joinability = joinability,
                    AllowJuniors = allowJuniors,
                    MinLevel = Math.Clamp(minLevel, 0, 50),
                    ClubType = clubType,
                    CreatorAccountId = creatorAccountId,
                    CategoryTags = tags,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                Clubs.Insert(club);
                UpsertMembershipUnsafe(
                    club.ClubId,
                    creatorAccountId,
                    ClubMembershipType.Creator);
                HomeClubs.Upsert(new HomeClubRecord
                {
                    AccountId = creatorAccountId,
                    ClubId = club.ClubId,
                    UpdatedAt = DateTime.UtcNow
                });
                return true;
            }
        }

        public static ClubRecord? Get(long clubId)
        {
            lock (Sync)
                return Clubs.FindById(clubId);
        }

        public static List<ClubRecord> GetCreatedBy(long accountId)
        {
            lock (Sync)
                return Clubs.Find(value =>
                        value.CreatorAccountId == accountId &&
                        value.State != ClubState.MarkedForDelete)
                    .OrderByDescending(value => value.CreatedAt)
                    .ToList();
        }

        public static List<ClubRecord> GetMemberClubs(long accountId)
        {
            lock (Sync)
            {
                HashSet<long> clubIds = Memberships.Find(value =>
                        value.AccountId == accountId &&
                        value.MembershipType >= ClubMembershipType.Member)
                    .Select(value => value.ClubId)
                    .ToHashSet();

                long? homeClubId = HomeClubs.FindById(accountId)?.ClubId;

                return Clubs.Find(value => value.State == ClubState.Active)
                    .Where(value =>
                        clubIds.Contains(value.ClubId) ||
                        value.CreatorAccountId == accountId ||
                        homeClubId == value.ClubId)
                    .OrderByDescending(value => value.MemberCount)
                    .ThenBy(value => value.Name)
                    .ToList();
            }
        }

        public static List<ClubRecord> Search(
            string? category,
            string? query,
            int sort,
            int skip,
            int count) =>
            SearchWithTotal(category, query, sort, skip, count).Results;

        public static (List<ClubRecord> Results, int TotalResults) SearchWithTotal(
            string? category,
            string? query,
            int sort,
            int skip,
            int count)
        {
            lock (Sync)
            {
                IEnumerable<ClubRecord> clubs = Clubs.Find(value =>
                    value.State == ClubState.Active &&
                    value.Visibility == ClubVisibility.Public);

                string? normalizedCategory = NormalizeCategory(category);
                if (!string.IsNullOrWhiteSpace(normalizedCategory))
                {
                    clubs = clubs.Where(value =>
                        ClubMatchesCategory(value, normalizedCategory));
                }

                if (!string.IsNullOrWhiteSpace(query))
                {
                    string trimmedQuery = query.Trim();
                    clubs = clubs.Where(value =>
                        (value.Name ?? string.Empty).Contains(
                            trimmedQuery,
                            StringComparison.OrdinalIgnoreCase) ||
                        (value.Description ?? string.Empty).Contains(
                            trimmedQuery,
                            StringComparison.OrdinalIgnoreCase));
                }

                clubs = sort switch
                {
                    1 => clubs.OrderByDescending(value => value.CreatedAt),
                    2 => clubs.OrderBy(value => value.CreatedAt),
                    _ => clubs.OrderByDescending(value => value.MemberCount)
                        .ThenBy(value => value.Name)
                };

                List<ClubRecord> all = clubs.ToList();
                List<ClubRecord> page = all
                    .Skip(Math.Max(0, skip))
                    .Take(Math.Clamp(count, 1, 100))
                    .ToList();

                return (page, all.Count);
            }
        }

        public static ClubMembershipRecord? GetMembership(long clubId, long accountId)
        {
            lock (Sync)
            {
                ClubMembershipRecord? membership =
                    Memberships.FindById(MembershipId(clubId, accountId));

                if (membership != null)
                    return membership;

                ClubRecord? club = Clubs.FindById(clubId);
                if (club?.CreatorAccountId != accountId)
                    return null;

                UpsertMembershipUnsafe(
                    clubId,
                    accountId,
                    ClubMembershipType.Creator);
                return Memberships.FindById(MembershipId(clubId, accountId));
            }
        }

        public static List<ClubMembershipRecord> GetMemberships(
            long clubId,
            bool includePending = false)
        {
            lock (Sync)
            {
                return Memberships.Find(value => value.ClubId == clubId &&
                        (includePending || value.MembershipType >= ClubMembershipType.Member))
                    .OrderByDescending(value => value.MembershipType)
                    .ThenBy(value => value.CreatedAt)
                    .ToList();
            }
        }

        public static bool SetMembership(
            long clubId,
            long accountId,
            ClubMembershipType membershipType)
        {
            lock (Sync)
            {
                if (Clubs.FindById(clubId) == null)
                    return false;
                UpsertMembershipUnsafe(clubId, accountId, membershipType);
                RefreshMemberCountUnsafe(clubId);
                return true;
            }
        }

        public static bool RemoveMembership(long clubId, long accountId)
        {
            lock (Sync)
            {
                ClubRecord? club = Clubs.FindById(clubId);
                if (club == null || club.CreatorAccountId == accountId)
                    return false;
                Memberships.Delete(MembershipId(clubId, accountId));
                if (HomeClubs.FindById(accountId)?.ClubId == clubId)
                    HomeClubs.Delete(accountId);
                RefreshMemberCountUnsafe(clubId);
                return true;
            }
        }

        public static bool SetHomeClub(long accountId, long? clubId)
        {
            lock (Sync)
            {
                if (!clubId.HasValue || clubId.Value <= 0)
                    return HomeClubs.Delete(accountId);

                ClubMembershipRecord? membership = GetMembership(clubId.Value, accountId);
                if (membership == null ||
                    membership.MembershipType < ClubMembershipType.Member)
                    return false;

                HomeClubs.Upsert(new HomeClubRecord
                {
                    AccountId = accountId,
                    ClubId = clubId.Value,
                    UpdatedAt = DateTime.UtcNow
                });
                return true;
            }
        }

        public static ClubRecord? GetHomeClub(long accountId)
        {
            lock (Sync)
            {
                HomeClubRecord? home = HomeClubs.FindById(accountId);
                return home == null ? null : Clubs.FindById(home.ClubId);
            }
        }

        public static bool Update(ClubRecord club)
        {
            lock (Sync)
            {
                club.Name = (club.Name ?? string.Empty).Trim();
                club.Description = (club.Description ?? string.Empty).Trim();
                club.MainImageName = NormalizeImageName(club.MainImageName);

                string primary = NormalizeCategory(club.PrimaryCategory) ??
                    NormalizeTags(club.CategoryTags).FirstOrDefault() ??
                    "Social";
                List<string> normalizedTags = NormalizeTags(
                        new[] { primary }
                            .Concat(club.CategoryTags ?? new List<string>()))
                    .ToList();
                if (normalizedTags.Count == 0)
                    normalizedTags.Add(primary);

                club.PrimaryCategory = primary;
                club.CategoryTags = normalizedTags;
                club.MinLevel = Math.Clamp(club.MinLevel, 0, 50);
                club.UpdatedAt = DateTime.UtcNow;
                return Clubs.Update(club);
            }
        }

        public static bool Delete(long clubId, long accountId)
        {
            lock (Sync)
            {
                ClubRecord? club = Clubs.FindById(clubId);
                if (club == null || club.CreatorAccountId != accountId)
                    return false;

                Clubs.Delete(clubId);
                foreach (ClubMembershipRecord membership in
                         Memberships.Find(value => value.ClubId == clubId).ToList())
                {
                    Memberships.Delete(membership.Id);
                }
                foreach (HomeClubRecord home in
                         HomeClubs.Find(value => value.ClubId == clubId).ToList())
                {
                    HomeClubs.Delete(home.AccountId);
                }
                return true;
            }
        }

        public static ClubSummaryDto ToSummary(ClubRecord club) =>
            ToSummary(club, null);

        public static ClubSummaryDto ToSummary(
            ClubRecord club,
            long? viewerAccountId)
        {
            ClubMembershipType membershipType = ClubMembershipType.None;
            if (viewerAccountId.HasValue)
            {
                membershipType = GetMembership(
                        club.ClubId,
                        viewerAccountId.Value)?.MembershipType ??
                    (club.CreatorAccountId == viewerAccountId.Value
                        ? ClubMembershipType.Creator
                        : ClubMembershipType.None);
            }

            string primary = NormalizeCategory(club.PrimaryCategory) ??
                NormalizeTags(club.CategoryTags).FirstOrDefault() ??
                "Social";
            List<string> normalizedTags = NormalizeTags(
                    new[] { primary }
                        .Concat(club.CategoryTags ?? new List<string>()))
                .ToList();
            if (normalizedTags.Count == 0)
                normalizedTags.Add(primary);

            return new ClubSummaryDto
            {
                ClubId = club.ClubId,
                Name = club.Name ?? string.Empty,
                Description = club.Description ?? string.Empty,
                MainImageName = NormalizeImageName(club.MainImageName),
                State = (int)club.State,
                MemberCount = club.MemberCount,
                PrimaryCategory = primary,
                CategoryTags = normalizedTags,
                Visibility = (int)club.Visibility,
                Joinability = (int)club.Joinability,
                AllowJuniors = club.AllowJuniors,
                MinLevel = club.MinLevel,
                ClubChatEnabled = club.ClubChatEnabled,
                ClubhouseRoomId = club.ClubhouseRoomId,
                ClubType = (int)club.ClubType,
                CreatorAccountId = club.CreatorAccountId,
                ClubMembershipType = (int)membershipType,
                CreatedAt = club.CreatedAt,
                UpdatedAt = club.UpdatedAt
            };
        }

        public static ClubMembershipDto ToMembership(ClubMembershipRecord membership) => new()
        {
            AccountId = membership.AccountId,
            ClubId = membership.ClubId,
            ClubMembershipType = (int)membership.MembershipType,
            CreatedAt = membership.CreatedAt
        };

        public static ClubDetailsDto ToDetails(ClubRecord club, long? viewerAccountId)
        {
            int myType = viewerAccountId.HasValue
                ? (int)(GetMembership(club.ClubId, viewerAccountId.Value)?.MembershipType ??
                    ClubMembershipType.None)
                : (int)ClubMembershipType.None;

            return new ClubDetailsDto
            {
                Club = ToSummary(club, viewerAccountId),
                CategoryTags = ToSummary(club).CategoryTags,
                AdditionalImages = (club.AdditionalImages ?? new List<string>())
                    .Select((imageName, imageIndex) => new ClubImageDto
                    {
                        ImageName = imageName,
                        ImageIndex = imageIndex
                    })
                    .ToList(),
                CreatorPermissions = Permissions(club.ClubId, ClubMembershipType.Creator),
                CoOwnerPermissions = Permissions(club.ClubId, ClubMembershipType.CoOwner),
                ModeratorPermissions = Permissions(club.ClubId, ClubMembershipType.Moderator),
                MyMembershipType = myType
            };
        }

        public static ClubPermissionsDto Permissions(
            long clubId,
            ClubMembershipType membershipType)
        {
            bool moderator = membershipType >= ClubMembershipType.Moderator;
            bool coOwner = membershipType >= ClubMembershipType.CoOwner;
            return new ClubPermissionsDto
            {
                ClubId = clubId,
                MembershipType = (int)membershipType,
                CanInvite = moderator,
                CanKick = moderator,
                CanBan = moderator,
                CanModify = coOwner,
                CanManageRoles = coOwner,
                CanManageClubhouse = coOwner
            };
        }

        private static string ResolveLegacyPrimaryCategory(
            string? storedPrimary,
            IEnumerable<string>? storedTags)
        {
            string? primary = NormalizeCategory(storedPrimary);
            List<string> tags = NormalizeTags(storedTags).ToList();

            if (primary == null)
                return tags.FirstOrDefault() ?? "Social";

            if (string.Equals(primary, "Social", StringComparison.OrdinalIgnoreCase))
            {
                string? recovered = tags.FirstOrDefault(value =>
                    !string.Equals(value, "Social", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(recovered))
                    return recovered;
            }

            return primary;
        }

        private static bool ClubMatchesCategory(
            ClubRecord club,
            string normalizedCategory)
        {
            string primary = NormalizeCategory(club.PrimaryCategory) ?? "Social";
            if (string.Equals(primary, normalizedCategory,
                    StringComparison.OrdinalIgnoreCase))
                return true;

            return (club.CategoryTags ?? new List<string>())
                .Select(NormalizeCategory)
                .Any(tag => string.Equals(
                    tag,
                    normalizedCategory,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static long NextClubId() =>
            Clubs.Count() == 0 ? 1 : Clubs.Max(value => value.ClubId) + 1;

        private static string MembershipId(long clubId, long accountId) =>
            $"{clubId}:{accountId}";

        private static void UpsertMembershipUnsafe(
            long clubId,
            long accountId,
            ClubMembershipType membershipType)
        {
            string id = MembershipId(clubId, accountId);
            ClubMembershipRecord record = Memberships.FindById(id) ??
                new ClubMembershipRecord
                {
                    Id = id,
                    ClubId = clubId,
                    AccountId = accountId,
                    CreatedAt = DateTime.UtcNow
                };
            record.Id = id;
            record.ClubId = clubId;
            record.AccountId = accountId;
            record.MembershipType = membershipType;
            record.UpdatedAt = DateTime.UtcNow;
            Memberships.Upsert(record);
        }

        private static void RefreshMemberCountUnsafe(long clubId)
        {
            ClubRecord? club = Clubs.FindById(clubId);
            if (club == null)
                return;
            club.MemberCount = Memberships.Count(value =>
                value.ClubId == clubId &&
                value.MembershipType >= ClubMembershipType.Member);
            club.UpdatedAt = DateTime.UtcNow;
            Clubs.Update(club);
        }

        private static IEnumerable<string> NormalizeTags(IEnumerable<string>? tags) =>
            (tags ?? Array.Empty<string>())
                .Select(NormalizeCategory)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3);

        private static string? NormalizeCategory(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return null;

            return category.Trim().ToLowerInvariant() switch
            {
                "social" => "Social",
                "hangout" => "Social",
                "creative" => "Creative",
                "art" => "Creative",
                "competitive" => "Competitive",
                "casual" => "Casual",
                "games" => "Casual",
                "roleplay" => "Casual",
                "educational" => "Educational",
                "education" => "Educational",
                "entertainment" => "Entertainment",
                "quest" => "Entertainment",
                "music" => "Entertainment",
                "lifestyle" => "Lifestyle",
                _ => null
            };
        }

        private static string NormalizeImageName(string? imageName)
        {
            string normalized = (imageName ?? string.Empty).Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized) ||
                normalized.Length > 260 ||
                normalized.Contains("..", StringComparison.Ordinal) ||
                Uri.TryCreate(normalized, UriKind.Absolute, out _))
                return "DefaultClubImage2k.jpg";
            return normalized.TrimStart('/');
        }
    }
}
