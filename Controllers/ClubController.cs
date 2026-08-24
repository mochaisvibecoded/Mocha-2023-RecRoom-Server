using System.Text.Json;
using Mocha2023.Auth;
using Mocha2023.Classes.DBs;
using Mocha2023.Classes.DBs.DBClasses;
using Microsoft.AspNetCore.Mvc;
using static Mocha2023.Classes.DBs.DBClasses.ClubDBClasses;

namespace Mocha2023.Controllers
{
    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    [Route("/club")]
    public sealed class ClubController : ControllerBase
    {
        [HttpPost("create")]
        [HttpPut("create")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> CreateClub()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            RequestValues values = await ReadRequestValuesAsync();
            string name = values.String("name", "clubName")?.Trim() ?? string.Empty;
            string description = values.String("description", "clubDescription")?.Trim() ?? string.Empty;

            if (name.Length is < 3 or > 50 || name.Any(char.IsControl))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "invalid_club_name",
                    message = "Club names must be 3 to 50 characters."
                });
            }
            if (description.Length > 1_000 || description.Any(value => value == '\0'))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "invalid_club_description",
                    message = "Club descriptions cannot exceed 1,000 characters."
                });
            }

            var visibility = ParseEnum(
                values.Int("visibility", "clubVisibility"),
                ClubVisibility.Public);
            var joinability = ParseEnum(
                values.Int("joinability", "clubJoinability"),
                ClubJoinability.Open);
            var clubType = ParseEnum(
                values.Int("clubType", "type"),
                ClubType.Generic);

            if (!ClubDB.TryCreate(
                    accountId.Value,
                    name,
                    description,
                    values.String("mainImageName", "imageName", "image"),
                    visibility,
                    joinability,
                    values.Bool(true, "allowJuniors", "supportsJuniors"),
                    values.Int("minLevel", "minimumLevel") ?? 0,
                    clubType,
                    ReadRequestedClubTags(values),
                    out ClubRecord? club,
                    out string? error))
            {
                Console.WriteLine(
                    $"[CLUB CREATE REJECTED] account={accountId.Value} " +
                    $"name={name} error={error ?? "unknown"}");

                return BadRequest(new { success = false, error });
            }

            Console.WriteLine(
                $"[CLUB CREATE] account={accountId.Value} club={club!.ClubId} " +
                $"name={club.Name} primaryTag={club.PrimaryCategory} " +
                $"tags={string.Join(',', club.CategoryTags ?? new List<string>())}");
            return Ok(ClubDB.ToDetails(club, accountId.Value));
        }

        [HttpGet("mine/created")]
        public IActionResult GetMyCreatedClubs()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            List<ClubSummaryDto> results = ClubDB.GetCreatedBy(accountId.Value)
                .Select(club => ClubDB.ToSummary(club, accountId.Value))
                .ToList();

            Console.WriteLine(
                $"[CLUB MINE CREATED] account={accountId.Value} " +
                $"clubs={results.Count} ids={string.Join(',', results.Select(value => value.ClubId))}");

            return Ok(results);
        }

        [HttpGet("account/{accountId:long}/created")]
        public IActionResult GetCreatedClubsForAccount(long accountId) =>
            Ok(ClubDB.GetCreatedBy(accountId)
                .Select(club => ClubDB.ToSummary(club))
                .ToList());

        [HttpGet("mine/member")]
        public IActionResult GetMyMembershipClubs()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            List<ClubSummaryDto> results = ClubDB.GetMemberClubs(accountId.Value)
                .Select(club => ClubDB.ToSummary(club, accountId.Value))
                .ToList();

            Console.WriteLine(
                $"[CLUB MINE MEMBER] account={accountId.Value} " +
                $"clubs={results.Count} ids={string.Join(',', results.Select(value => value.ClubId))}");

            return Ok(results);
        }

        [HttpGet("home/me")]
        [HttpGet("home")]
        [HttpGet("homeclub/me")]
        [HttpGet("mine/home")]
        [HttpGet("mine/homeclub")]
        public IActionResult GetMyHomeClub()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            ClubRecord? club = ClubDB.GetHomeClub(accountId.Value);
            return club == null
                ? Content("null", "application/json")
                : Ok(ClubDB.ToSummary(club, accountId.Value));
        }

        [HttpPut("home/me")]
        [HttpPost("home/me")]
        [HttpPut("home")]
        [HttpPost("home")]
        [HttpPut("mine/home")]
        [HttpPost("mine/home")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> SetMyHomeClub()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            RequestValues values = await ReadRequestValuesAsync();
            long? clubId = values.Long("clubId", "id");
            if (!ClubDB.SetHomeClub(accountId.Value, clubId))
                return BadRequest(new { success = false, error = "not_a_club_member" });
            return Ok(new { success = true, ClubId = clubId });
        }

        [HttpGet("categoryTags")]
        public IActionResult GetCategoryTags() => Ok(ClubDB.CategoryTags);

        [HttpGet("~/announcements/club/{clubId:long}")]
        public IActionResult GetClubAnnouncements(
            long clubId,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 50,
            [FromQuery] int count = 50,
            [FromQuery] int sort = 0)
        {
            if (ClubDB.Get(clubId) == null)
                return NotFound();

            int requestedCount = Math.Clamp(take > 0 ? take : count, 1, 100);
            Console.WriteLine(
                $"[CLUB ANNOUNCEMENTS] club={clubId} skip={Math.Max(0, skip)} " +
                $"take={requestedCount} sort={sort} returned=0");

            return Ok(Array.Empty<object>());
        }

        [HttpGet("~/api/playerevents/v1/club/{clubId}")]
        public IActionResult GetClubEvents(
            long clubId,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 50,
            [FromQuery] int count = 50)
        {
            if (ClubDB.Get(clubId) == null)
                return NotFound();

            int requestedCount = Math.Clamp(take > 0 ? take : count, 1, 100);
            Console.WriteLine(
                $"[CLUB EVENTS] club={clubId} skip={Math.Max(0, skip)} " +
                $"take={requestedCount} returned=0");

            return Ok(Array.Empty<object>());
        }

        [HttpGet("~/api/playerevents/v1/clubs")]
        public IActionResult GetClubEventsForClubs()
        {
            var requestedClubIds = new HashSet<long>();

            foreach (string? rawValue in Request.Query["id"])
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                    continue;

                foreach (string part in rawValue.Split(
                             ',',
                             StringSplitOptions.RemoveEmptyEntries |
                             StringSplitOptions.TrimEntries))
                {
                    if (long.TryParse(part, out long clubId) && clubId > 0)
                        requestedClubIds.Add(clubId);
                }
            }

            long[] existingClubIds = requestedClubIds
                .Where(clubId => ClubDB.Get(clubId) != null)
                .OrderBy(clubId => clubId)
                .ToArray();

            Console.WriteLine(
                $"[CLUB EVENTS BATCH] requested={requestedClubIds.Count} " +
                $"existing={existingClubIds.Length} " +
                $"ids={string.Join(',', existingClubIds)} returned=0");

            return Ok(Array.Empty<object>());
        }

        [HttpGet("search")]
        public IActionResult SearchClubs(
            [FromQuery] int sort = 0,
            [FromQuery] string? category = null,
            [FromQuery] string? query = null,
            [FromQuery] string? name = null,
            [FromQuery] int skip = 0,
            [FromQuery] int count = 32)
        {
            long? viewerAccountId = AuthStuff.GetPlayerId(Request);
            var page = ClubDB.SearchWithTotal(
                category,
                query ?? name,
                sort,
                skip,
                count);

            List<ClubSummaryDto> results = page.Results
                .Select(club => ClubDB.ToSummary(club, viewerAccountId))
                .ToList();

            Console.WriteLine(
                $"[CLUB SEARCH] category={category ?? "all"} " +
                $"query={query ?? name ?? "none"} skip={skip} count={count} " +
                $"returned={results.Count} total={page.TotalResults} " +
                $"ids={string.Join(',', results.Select(value => value.ClubId))}");

            return Ok(new
            {
                Clubs = results,
                Results = results,
                SearchResults = results,
                TotalResults = page.TotalResults,
                Count = results.Count,
                ContinuationToken = (string?)null
            });
        }

        [HttpGet("mostactivetoday")]
        public IActionResult GetMostActiveToday()
        {
            long? viewerAccountId = AuthStuff.GetPlayerId(Request);
            return Ok(ClubDB.Search(null, null, 0, 0, 32)
                .Select(club => ClubDB.ToSummary(club, viewerAccountId))
                .ToList());
        }

        [HttpGet("{clubId:long}")]
        public IActionResult GetClub(long clubId)
        {
            var club = ClubDB.Get(clubId);
            return club == null || !CanViewClub(club)
                ? NotFound()
                : Ok(ClubDB.ToSummary(club));
        }

        [HttpGet("{clubId:long}/details")]
        public IActionResult GetClubDetails(long clubId)
        {
            var club = ClubDB.Get(clubId);
            return club == null || !CanViewClub(club)
                ? NotFound()
                : Ok(ClubDB.ToDetails(club, AuthStuff.GetPlayerId(Request)));
        }

        [HttpGet("{clubId:long}/members")]
        public IActionResult GetClubMembers(
            long clubId,
            [FromQuery] int? membershipType = null,
            [FromQuery] int sortBy = 0,
            [FromQuery] int skip = 0,
            [FromQuery] int count = 100)
        {
            var club = ClubDB.Get(clubId);
            if (club == null || !CanViewClub(club))
                return NotFound();

            IEnumerable<ClubMembershipRecord> memberships = ClubDB.GetMemberships(clubId);
            if (membershipType.HasValue &&
                Enum.IsDefined(typeof(ClubMembershipType), membershipType.Value))
            {
                ClubMembershipType requestedType = (ClubMembershipType)membershipType.Value;
                memberships = memberships.Where(value => value.MembershipType == requestedType);
            }

            memberships = sortBy switch
            {
                1 => memberships.OrderBy(value => value.CreatedAt),
                2 => memberships.OrderByDescending(value => value.CreatedAt),
                _ => memberships.OrderByDescending(value => value.MembershipType)
            };
            var all = memberships.Select(membership => ClubDB.ToMembership(membership)).ToList();
            var results = all.Skip(Math.Max(0, skip))
                .Take(Math.Clamp(count, 1, 100))
                .ToList();
            return Ok(new
            {
                Memberships = results,
                Results = results,
                TotalResults = all.Count,
                ContinuationToken = (string?)null
            });
        }

        [HttpGet("{clubId:long}/members/{accountId:long}")]
        public IActionResult GetClubMember(long clubId, long accountId)
        {
            var club = ClubDB.Get(clubId);
            if (club == null || !CanViewClub(club))
                return NotFound();
            var membership = ClubDB.GetMembership(clubId, accountId);
            return membership == null ? NotFound() : Ok(ClubDB.ToMembership(membership));
        }

        [HttpGet("{clubId:long}/members/search")]
        public IActionResult SearchClubMembers(
            long clubId,
            [FromQuery] string? name = null,
            [FromQuery] int skip = 0,
            [FromQuery] int count = 100)
        {
            var club = ClubDB.Get(clubId);
            if (club == null || !CanViewClub(club))
                return NotFound();
            var memberships = ClubDB.GetMemberships(clubId);
            if (!string.IsNullOrWhiteSpace(name))
            {
                memberships = memberships.Where(value =>
                    PlayerDB.Players.FindById(value.AccountId)?.Player?.Username?
                        .Contains(name, StringComparison.OrdinalIgnoreCase) == true ||
                    PlayerDB.Players.FindById(value.AccountId)?.Player?.DisplayName?
                        .Contains(name, StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
            }
            var all = memberships.Select(membership => ClubDB.ToMembership(membership)).ToList();
            var results = all.Skip(Math.Max(0, skip))
                .Take(Math.Clamp(count, 1, 100))
                .ToList();
            return Ok(new
            {
                Memberships = results,
                Results = results,
                TotalResults = all.Count,
                ContinuationToken = (string?)null
            });
        }

        [HttpPost("{clubId:long}/members/directJoin")]
        [HttpPut("{clubId:long}/members/directJoin")]
        public IActionResult DirectJoin(long clubId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();
            var club = ClubDB.Get(clubId);
            if (club == null)
                return NotFound();
            ClubMembershipType existingType = ClubDB.GetMembership(
                clubId,
                accountId.Value)?.MembershipType ?? ClubMembershipType.None;
            if (existingType == ClubMembershipType.Banned)
                return StatusCode(403);
            if (existingType == ClubMembershipType.PendingInvited)
            {
                ClubDB.SetMembership(clubId, accountId.Value, ClubMembershipType.Member);
                return Ok(new
                {
                    success = true,
                    ClubMembershipType = (int)ClubMembershipType.Member
                });
            }
            if (club.Visibility == ClubVisibility.Private ||
                club.Joinability == ClubJoinability.InviteOnly)
                return BadRequest(new { success = false, error = "invite_required" });

            ClubMembershipType type = club.Joinability == ClubJoinability.AskToJoin
                ? ClubMembershipType.PendingRequested
                : ClubMembershipType.Member;
            ClubDB.SetMembership(clubId, accountId.Value, type);
            return Ok(new { success = true, ClubMembershipType = (int)type });
        }

        [HttpPost("{clubId:long}/members/requesttojoin")]
        [HttpPut("{clubId:long}/members/requesttojoin")]
        public IActionResult RequestToJoin(long clubId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            ClubRecord? club = ClubDB.Get(clubId);
            if (club == null)
                return NotFound();

            ClubMembershipType existingType = ClubDB.GetMembership(
                clubId,
                accountId.Value)?.MembershipType ?? ClubMembershipType.None;

            if (existingType == ClubMembershipType.Banned)
                return StatusCode(403);

            if (existingType >= ClubMembershipType.Member)
            {
                return Ok(new
                {
                    success = true,
                    alreadyMember = true,
                    ClubMembershipType = (int)existingType
                });
            }

            if (existingType == ClubMembershipType.PendingRequested)
            {
                return Ok(new
                {
                    success = true,
                    pending = true,
                    ClubMembershipType = (int)existingType
                });
            }

            if (existingType == ClubMembershipType.PendingInvited)
            {
                ClubDB.SetMembership(clubId, accountId.Value, ClubMembershipType.Member);
                return Ok(new
                {
                    success = true,
                    ClubMembershipType = (int)ClubMembershipType.Member
                });
            }

            if (club.Visibility == ClubVisibility.Private ||
                club.Joinability == ClubJoinability.InviteOnly)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "invite_required"
                });
            }

            ClubMembershipType newType = club.Joinability == ClubJoinability.AskToJoin
                ? ClubMembershipType.PendingRequested
                : ClubMembershipType.Member;

            if (!ClubDB.SetMembership(clubId, accountId.Value, newType))
                return NotFound();

            Console.WriteLine(
                $"[CLUB JOIN] club={clubId} account={accountId.Value} " +
                $"type={(int)newType} verb={Request.Method}");

            return Ok(new
            {
                success = true,
                pending = newType == ClubMembershipType.PendingRequested,
                ClubMembershipType = (int)newType
            });
        }

        [HttpPost("{clubId:long}/members/acceptinvite")]
        [HttpPut("{clubId:long}/members/acceptinvite")]
        public IActionResult AcceptInvite(long clubId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();
            var existing = ClubDB.GetMembership(clubId, accountId.Value);
            if (existing?.MembershipType != ClubMembershipType.PendingInvited)
                return BadRequest(new { success = false, error = "invite_not_found" });
            ClubDB.SetMembership(clubId, accountId.Value, ClubMembershipType.Member);
            return Ok(new { success = true });
        }

        [HttpPost("{clubId:long}/members/declineinvite")]
        [HttpPut("{clubId:long}/members/declineinvite")]
        public IActionResult DeclineInvite(long clubId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();
            ClubDB.RemoveMembership(clubId, accountId.Value);
            return Ok(new { success = true });
        }

        [HttpPost("{clubId:long}/members/leave")]
        [HttpPut("{clubId:long}/members/leave")]
        public IActionResult LeaveClub(long clubId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();
            return ClubDB.RemoveMembership(clubId, accountId.Value)
                ? Ok(new { success = true })
                : BadRequest(new { success = false, error = "creator_cannot_leave" });
        }

        [HttpPost("{clubId:long}/members/invite")]
        [HttpPut("{clubId:long}/members/invite")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> InviteMember(long clubId)
        {
            long? actorId = AuthStuff.GetPlayerId(Request);
            if (!actorId.HasValue)
                return Unauthorized();
            if (!HasPermission(clubId, actorId.Value, ClubMembershipType.Moderator))
                return StatusCode(403);

            RequestValues values = await ReadRequestValuesAsync();
            long? targetId = values.Long("accountId", "playerId", "id");
            if (!targetId.HasValue || PlayerDB.Players.FindById(targetId.Value) == null)
                return BadRequest(new { success = false, error = "invalid_account" });
            ClubMembershipType invitedType = ParseMembershipType(
                values.Int("membershipType", "clubMembershipType"),
                ClubMembershipType.Member);
            invitedType = invitedType is ClubMembershipType.Moderator or
                ClubMembershipType.CoOwner
                ? invitedType
                : ClubMembershipType.Member;
            ClubDB.SetMembership(clubId, targetId.Value, ClubMembershipType.PendingInvited);
            return Ok(new
            {
                success = true,
                AccountId = targetId.Value,
                InvitedMembershipType = (int)invitedType
            });
        }

        [HttpGet("{clubId:long}/members/requests")]
        [HttpGet("{clubId:long}/members/requests/search")]
        public IActionResult GetJoinRequests(long clubId)
        {
            long? actorId = AuthStuff.GetPlayerId(Request);
            if (!actorId.HasValue)
                return Unauthorized();
            if (!HasPermission(clubId, actorId.Value, ClubMembershipType.Moderator))
                return StatusCode(403);
            var results = ClubDB.GetMemberships(clubId, includePending: true)
                .Where(value => value.MembershipType is
                    ClubMembershipType.PendingRequested or
                    ClubMembershipType.PendingInvited or
                    ClubMembershipType.PendingDenied)
                .Select(membership => ClubDB.ToMembership(membership))
                .ToList();
            return Ok(new
            {
                Memberships = results,
                Results = results,
                TotalResults = results.Count,
                ContinuationToken = (string?)null
            });
        }

        [HttpPost("{clubId:long}/members/acceptrequest")]
        [HttpPut("{clubId:long}/members/acceptrequest")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> AcceptJoinRequest(long clubId) =>
            await ChangePendingMember(clubId, ClubMembershipType.Member);

        [HttpPost("{clubId:long}/members/denyrequest")]
        [HttpPut("{clubId:long}/members/denyrequest")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> DenyJoinRequest(long clubId) =>
            await ChangePendingMember(clubId, ClubMembershipType.PendingDenied);

        [HttpPost("{clubId:long}/members/changetype")]
        [HttpPut("{clubId:long}/members/changetype")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> ChangeMembershipType(long clubId)
        {
            long? actorId = AuthStuff.GetPlayerId(Request);
            if (!actorId.HasValue)
                return Unauthorized();
            if (!HasPermission(clubId, actorId.Value, ClubMembershipType.CoOwner))
                return StatusCode(403);

            RequestValues values = await ReadRequestValuesAsync();
            long? targetId = values.Long("accountId", "playerId", "id");
            var type = ParseMembershipType(
                values.Int("newMembershipType", "membershipType", "clubMembershipType"),
                ClubMembershipType.Member);
            var club = ClubDB.Get(clubId);
            ClubMembershipType actorType = ClubDB.GetMembership(
                clubId,
                actorId.Value)?.MembershipType ?? ClubMembershipType.None;
            if (!targetId.HasValue || club == null || targetId.Value == club.CreatorAccountId ||
                type < ClubMembershipType.Member || type == ClubMembershipType.Creator ||
                (actorType != ClubMembershipType.Creator && type >= actorType))
                return BadRequest(new { success = false, error = "invalid_membership_change" });
            ClubDB.SetMembership(clubId, targetId.Value, type);
            return Ok(new { success = true });
        }

        [HttpPost("{clubId:long}/members/remove")]
        [HttpPut("{clubId:long}/members/remove")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> RemoveMember(long clubId)
        {
            long? actorId = AuthStuff.GetPlayerId(Request);
            if (!actorId.HasValue)
                return Unauthorized();
            if (!HasPermission(clubId, actorId.Value, ClubMembershipType.Moderator))
                return StatusCode(403);
            RequestValues values = await ReadRequestValuesAsync();
            long? targetId = values.Long("accountId", "playerId", "id");
            ClubMembershipType actorType = ClubDB.GetMembership(
                clubId,
                actorId.Value)?.MembershipType ?? ClubMembershipType.None;
            ClubMembershipType targetType = targetId.HasValue
                ? ClubDB.GetMembership(clubId, targetId.Value)?.MembershipType ??
                    ClubMembershipType.None
                : ClubMembershipType.None;
            return targetId.HasValue && actorType > targetType &&
                ClubDB.RemoveMembership(clubId, targetId.Value)
                ? Ok(new { success = true })
                : BadRequest(new { success = false, error = "member_not_removed" });
        }

        [HttpPost("{clubId:long}/members/ban")]
        [HttpPut("{clubId:long}/members/ban")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> BanMember(long clubId) =>
            await ChangeMemberAsModerator(clubId, ClubMembershipType.Banned);

        [HttpPost("{clubId:long}/members/unban")]
        [HttpPut("{clubId:long}/members/unban")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> UnbanMember(long clubId) =>
            await ChangeMemberAsModerator(clubId, ClubMembershipType.None);

        [HttpGet("{clubId:long}/members/banned")]
        public IActionResult GetBannedMembers(long clubId)
        {
            long? actorId = AuthStuff.GetPlayerId(Request);
            if (!actorId.HasValue)
                return Unauthorized();
            if (!HasPermission(clubId, actorId.Value, ClubMembershipType.Moderator))
                return StatusCode(403);
            var results = ClubDB.GetMemberships(clubId, includePending: true)
                .Where(value => value.MembershipType == ClubMembershipType.Banned)
                .Select(membership => ClubDB.ToMembership(membership))
                .ToList();
            return Ok(new
            {
                Memberships = results,
                Results = results,
                TotalResults = results.Count,
                ContinuationToken = (string?)null
            });
        }

        [HttpPut("{clubId:long}/modify")]
        [HttpPost("{clubId:long}/modify")]
        [HttpPut("{clubId:long}/modifydetails")]
        [HttpPost("{clubId:long}/modifydetails")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> ModifyClub(long clubId)
        {
            long? actorId = AuthStuff.GetPlayerId(Request);
            if (!actorId.HasValue)
                return Unauthorized();
            if (!HasPermission(clubId, actorId.Value, ClubMembershipType.CoOwner))
                return StatusCode(403);
            var club = ClubDB.Get(clubId);
            if (club == null)
                return NotFound();

            RequestValues values = await ReadRequestValuesAsync();
            string? name = values.String("name", "clubName");
            string? description = values.String("description", "clubDescription");
            if (name != null)
            {
                name = name.Trim();
                if (name.Length is < 3 or > 50 || name.Any(char.IsControl))
                    return BadRequest(new { success = false, error = "invalid_club_name" });
                club.Name = name;
            }
            if (description != null)
            {
                description = description.Trim();
                if (description.Length > 1_000 || description.Any(value => value == '\0'))
                    return BadRequest(new { success = false, error = "invalid_club_description" });
                club.Description = description;
            }
            club.Visibility = ParseEnum(values.Int("visibility"), club.Visibility);
            club.Joinability = ParseEnum(values.Int("joinability"), club.Joinability);
            club.ClubType = ParseEnum(values.Int("clubType"), club.ClubType);
            club.AllowJuniors = values.Bool(club.AllowJuniors, "allowJuniors", "supportsJuniors");
            club.MinLevel = values.Int("minLevel", "minimumLevel") ?? club.MinLevel;
            List<string> tags = ReadRequestedClubTags(values);
            if (tags.Count > 0)
            {

                club.PrimaryCategory = tags[0];
                club.CategoryTags = tags;
            }
            string? imageName = values.String("mainImageName", "imageName", "image");
            if (imageName != null)
                club.MainImageName = imageName;
            ClubDB.Update(club);
            Console.WriteLine(
                $"[CLUB MODIFY] club={clubId} actor={actorId.Value} " +
                $"primaryTag={club.PrimaryCategory} " +
                $"tags={string.Join(',', club.CategoryTags ?? new List<string>())}");
            return Ok(ClubDB.ToDetails(club, actorId.Value));
        }

        [HttpPut("{clubId:long}/mainimage")]
        [HttpPost("{clubId:long}/mainimage")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> ModifyMainImage(long clubId)
        {
            long? actorId = AuthStuff.GetPlayerId(Request);
            if (!actorId.HasValue)
                return Unauthorized();
            if (!HasPermission(clubId, actorId.Value, ClubMembershipType.CoOwner))
                return StatusCode(403);
            var club = ClubDB.Get(clubId);
            if (club == null)
                return NotFound();
            RequestValues values = await ReadRequestValuesAsync();
            club.MainImageName = values.String("mainImageName", "imageName", "image") ??
                "DefaultClubImage2k.jpg";
            ClubDB.Update(club);
            return Ok(ClubDB.ToDetails(club, actorId.Value));
        }

        [HttpPut("{clubId:long}/additionalimage/{imageIndex:int}")]
        [HttpPost("{clubId:long}/additionalimage/{imageIndex:int}")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> ModifyAdditionalImage(long clubId, int imageIndex)
        {
            long? actorId = AuthStuff.GetPlayerId(Request);
            if (!actorId.HasValue)
                return Unauthorized();
            if (!HasPermission(clubId, actorId.Value, ClubMembershipType.CoOwner))
                return StatusCode(403);
            if (imageIndex is < 0 or > 2)
                return BadRequest(new { success = false, error = "invalid_image_index" });
            var club = ClubDB.Get(clubId);
            if (club == null)
                return NotFound();
            RequestValues values = await ReadRequestValuesAsync();
            string imageName = values.String("imageName", "image")?.Trim() ?? string.Empty;
            while (club.AdditionalImages.Count <= imageIndex)
                club.AdditionalImages.Add(string.Empty);
            club.AdditionalImages[imageIndex] = imageName;
            ClubDB.Update(club);
            return Ok(ClubDB.ToDetails(club, actorId.Value));
        }

        [HttpPut("{clubId:long}/clubhouse")]
        [HttpPost("{clubId:long}/clubhouse")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> ModifyClubhouse(long clubId)
        {
            long? actorId = AuthStuff.GetPlayerId(Request);
            if (!actorId.HasValue)
                return Unauthorized();
            if (!HasPermission(clubId, actorId.Value, ClubMembershipType.CoOwner))
                return StatusCode(403);
            var club = ClubDB.Get(clubId);
            if (club == null)
                return NotFound();
            RequestValues values = await ReadRequestValuesAsync();
            long? roomId = values.Long("clubhouseRoomId", "roomId", "id");
            if (roomId.HasValue && RoomDB.GetRoom(roomId.Value) == null)
                return BadRequest(new { success = false, error = "room_not_found" });
            club.ClubhouseRoomId = roomId is > 0 ? roomId : null;
            ClubDB.Update(club);
            return Ok(ClubDB.ToDetails(club, actorId.Value));
        }

        [HttpGet("{clubId:long}/clubhouse")]
        public IActionResult GetClubhouse(long clubId)
        {
            var club = ClubDB.Get(clubId);
            if (club == null)
                return NotFound();
            if (!club.ClubhouseRoomId.HasValue)
                return NotFound(new { error = "club_has_no_clubhouse" });
            var room = RoomDB.GetRoom(club.ClubhouseRoomId.Value);
            return room == null ? NotFound() : Ok(RoomDB.PrepareRoomForClient(room));
        }

        [HttpPut("{clubId:long}/minlevel")]
        [HttpPost("{clubId:long}/minlevel")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> ModifyMinLevel(long clubId)
        {
            long? actorId = AuthStuff.GetPlayerId(Request);
            if (!actorId.HasValue)
                return Unauthorized();
            if (!HasPermission(clubId, actorId.Value, ClubMembershipType.CoOwner))
                return StatusCode(403);
            var club = ClubDB.Get(clubId);
            if (club == null)
                return NotFound();
            RequestValues values = await ReadRequestValuesAsync();
            club.MinLevel = Math.Clamp(values.Int("minLevel", "level") ?? 0, 0, 50);
            ClubDB.Update(club);
            return Ok(ClubDB.ToDetails(club, actorId.Value));
        }

        [HttpGet("{clubId:long}/clubChatEnabled")]
        public IActionResult GetClubChatEnabled(long clubId)
        {
            var club = ClubDB.Get(clubId);
            return club == null ? NotFound() : Ok(club.ClubChatEnabled);
        }

        [HttpGet("{clubId:long}/hasDisabledClubChat")]
        public IActionResult HasDisabledClubChat(long clubId)
        {
            var club = ClubDB.Get(clubId);
            return club == null ? NotFound() : Ok(!club.ClubChatEnabled);
        }

        [HttpPut("{clubId:long}/clubChatEnabled")]
        [HttpPost("{clubId:long}/clubChatEnabled")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> ModifyClubChatEnabled(long clubId)
        {
            long? actorId = AuthStuff.GetPlayerId(Request);
            if (!actorId.HasValue)
                return Unauthorized();
            if (!HasPermission(clubId, actorId.Value, ClubMembershipType.CoOwner))
                return StatusCode(403);
            var club = ClubDB.Get(clubId);
            if (club == null)
                return NotFound();
            RequestValues values = await ReadRequestValuesAsync();
            club.ClubChatEnabled = values.Bool(true, "enabled", "clubChatEnabled", "value");
            ClubDB.Update(club);
            return Ok(ClubDB.ToDetails(club, actorId.Value));
        }

        [HttpGet("{clubId:long}/permissions/{membershipType:int}")]
        public IActionResult GetPermissions(long clubId, int membershipType)
        {
            if (ClubDB.Get(clubId) == null)
                return NotFound();
            return Ok(ClubDB.Permissions(
                clubId,
                ParseMembershipType(membershipType, ClubMembershipType.None)));
        }

        [HttpDelete("{clubId:long}")]
        public IActionResult DeleteClub(long clubId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();
            return ClubDB.Delete(clubId, accountId.Value)
                ? Ok(new { success = true })
                : Forbid();
        }

        private async Task<IActionResult> ChangePendingMember(
            long clubId,
            ClubMembershipType membershipType)
        {
            long? actorId = AuthStuff.GetPlayerId(Request);
            if (!actorId.HasValue)
                return Unauthorized();
            if (!HasPermission(clubId, actorId.Value, ClubMembershipType.Moderator))
                return StatusCode(403);
            RequestValues values = await ReadRequestValuesAsync();
            long? targetId = values.Long("accountId", "playerId", "id");
            if (!targetId.HasValue ||
                ClubDB.GetMembership(clubId, targetId.Value)?.MembershipType !=
                    ClubMembershipType.PendingRequested)
                return BadRequest(new { success = false, error = "request_not_found" });
            ClubDB.SetMembership(clubId, targetId.Value, membershipType);
            return Ok(new { success = true });
        }

        private async Task<IActionResult> ChangeMemberAsModerator(
            long clubId,
            ClubMembershipType membershipType)
        {
            long? actorId = AuthStuff.GetPlayerId(Request);
            if (!actorId.HasValue)
                return Unauthorized();
            if (!HasPermission(clubId, actorId.Value, ClubMembershipType.Moderator))
                return StatusCode(403);
            RequestValues values = await ReadRequestValuesAsync();
            long? targetId = values.Long("accountId", "playerId", "id");
            var club = ClubDB.Get(clubId);
            ClubMembershipType actorType = ClubDB.GetMembership(
                clubId,
                actorId.Value)?.MembershipType ?? ClubMembershipType.None;
            ClubMembershipType targetType = targetId.HasValue
                ? ClubDB.GetMembership(clubId, targetId.Value)?.MembershipType ??
                    ClubMembershipType.None
                : ClubMembershipType.None;
            if (!targetId.HasValue || club == null || targetId.Value == club.CreatorAccountId ||
                actorType <= targetType)
                return BadRequest(new { success = false, error = "invalid_account" });
            ClubDB.SetMembership(clubId, targetId.Value, membershipType);
            return Ok(new { success = true });
        }

        private static bool HasPermission(
            long clubId,
            long accountId,
            ClubMembershipType required) =>
            ClubDB.GetMembership(clubId, accountId)?.MembershipType >= required;

        private bool CanViewClub(ClubRecord club)
        {
            if (club.Visibility == ClubVisibility.Public)
                return true;
            long? accountId = AuthStuff.GetPlayerId(Request);
            ClubMembershipType membershipType = accountId.HasValue
                ? ClubDB.GetMembership(club.ClubId, accountId.Value)?.MembershipType ??
                    ClubMembershipType.None
                : ClubMembershipType.None;
            return membershipType > ClubMembershipType.None;
        }

        private static List<string> ReadRequestedClubTags(RequestValues values)
        {

            string? primaryTag = values.String(
                "primaryTag",
                "primaryCategory",
                "category",
                "clubCategory");

            List<string> tags = values.List(
                "categoryTags",
                "customTags",
                "tags");

            if (!string.IsNullOrWhiteSpace(primaryTag))
            {
                tags.RemoveAll(value => string.Equals(
                    value?.Trim(),
                    primaryTag.Trim(),
                    StringComparison.OrdinalIgnoreCase));
                tags.Insert(0, primaryTag.Trim());
            }

            return tags
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static T ParseEnum<T>(int? value, T fallback) where T : struct, Enum =>
            value.HasValue && Enum.IsDefined(typeof(T), value.Value)
                ? (T)Enum.ToObject(typeof(T), value.Value)
                : fallback;

        private static ClubMembershipType ParseMembershipType(
            int? value,
            ClubMembershipType fallback) =>
            value.HasValue && Enum.IsDefined(typeof(ClubMembershipType), value.Value)
                ? (ClubMembershipType)value.Value
                : fallback;

        private async Task<RequestValues> ReadRequestValuesAsync()
        {
            var values = new RequestValues();
            foreach (var item in Request.Query)
                values.Add(item.Key, item.Value.ToString());

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (var item in form)
                    values.Add(item.Key, item.Value.ToString());
                return values;
            }

            if ((Request.ContentLength ?? 0) <= 0)
                return values;

            try
            {
                using JsonDocument document = await JsonDocument.ParseAsync(
                    Request.Body,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    });
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                    values.AddObject(document.RootElement);
            }
            catch (JsonException)
            {

            }
            return values;
        }

        private sealed class RequestValues
        {
            private readonly Dictionary<string, string> _values =
                new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<string>> _lists =
                new(StringComparer.OrdinalIgnoreCase);

            public void Add(string key, string? value)
            {
                if (!string.IsNullOrWhiteSpace(key) && value != null)
                    _values[key] = value;
            }

            public void AddObject(JsonElement element)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        _lists[property.Name] = property.Value.EnumerateArray()
                            .Where(value => value.ValueKind == JsonValueKind.String)
                            .Select(value => value.GetString() ?? string.Empty)
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .ToList();
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Object &&
                             property.Name.Equals("club", StringComparison.OrdinalIgnoreCase))
                    {
                        AddObject(property.Value);
                    }
                    else
                    {
                        Add(property.Name, property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : property.Value.GetRawText());
                    }
                }
            }

            public string? String(params string[] keys)
            {
                foreach (string key in keys)
                    if (_values.TryGetValue(key, out string? value))
                        return value;
                return null;
            }

            public int? Int(params string[] keys) =>
                int.TryParse(String(keys), out int value) ? value : null;

            public long? Long(params string[] keys) =>
                long.TryParse(String(keys), out long value) ? value : null;

            public bool Bool(bool fallback, params string[] keys)
            {
                string? value = String(keys);
                if (bool.TryParse(value, out bool parsed))
                    return parsed;
                if (int.TryParse(value, out int numeric))
                    return numeric != 0;
                return fallback;
            }

            public List<string> List(params string[] keys)
            {
                foreach (string key in keys)
                {
                    if (_lists.TryGetValue(key, out List<string>? list))
                        return list;
                    if (_values.TryGetValue(key, out string? value))
                    {
                        return value.Split(',', StringSplitOptions.RemoveEmptyEntries |
                                StringSplitOptions.TrimEntries)
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .ToList();
                    }
                }
                return new List<string>();
            }
        }
    }
}
