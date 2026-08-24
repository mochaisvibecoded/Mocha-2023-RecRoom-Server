using Mocha2023.Auth;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs;
using Mocha2023.Classes.DBs.DBClasses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Mocha2023.Classes.DBs.DBClasses.PlayerDBClasses;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Mocha2023.Controllers
{
    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    public class APIController : ControllerBase
    {
        private const int StoreItemPrice = 100;
        private const int FriendotronGiftContext = (int)PlayerDBClasses.GiftContext.Consumable;
        private const string WeeklyChallengeRewardEquipmentPrefab = "[Basketball]";
        private const string WeeklyChallengeRewardModificationGuid =
            "WOWwmg7jg0mH2g4LJUp6fA";
        private static readonly object CatalogLock = new();
        private static readonly object EquipmentInventoryLock = new();
        private static DateTime CatalogCacheTimestampUtc;
        private static DateTime EquipmentCatalogCacheTimestampUtc;
        private static DateTime ConsumableCatalogCacheTimestampUtc;
        private static List<CatalogSku> CatalogCache = new();
        private static readonly object StorefrontAdminLock = new();
        private static StorefrontAdminState? StorefrontAdminCache;
        private static readonly FriendotronConsumableReward[]
            FriendotronConsumableRewards =
            {
                new("Film (Black & White)", "frOMH6WxDEG1fBqC4_83vg", 0),
                new("Film (Dawn)", "m0bVLwWGj0GuIBSb6wCk6Q", 0),
                new("Film (Sepia)", "A5M-yf9tgUihq1uab3v58g", 0),
                new("High Five Potion (Golden)", "-hy0qD-iUk-v4NHxNzanmg", 0),
                new("High Five Potion (Magic)", "VQSgL2pTLkWx4B3kwYG7UA", 0),
                new("Assorted Donuts", "ZuvkidodzkuOfGLDnTOFyg", 20),
                new("Candy Apples", "EmPvh3I6L0uK_1i8Wy_ylQ", 20),
                new("Cheese Pizza", "5hIAZ9wg5EyG1cILf4FS2A", 0),
                new("Chocolate Donuts", "mMCGPgK3tki5S_15q2Z81A", 10),
                new("Glazed Donuts", "7OZ5AE3uuUyqa0P-2W1ptg", 0),
                new("Hawaiian Pizza", "_jnjYGBcyEWY5Ub4OezXcA", 10),
                new("Tray of Lattes", "P15H1ONBhk-5DYYjid1ttg", 10),
                new("Pepperoni Pizza", "mq23W-RSP0G8iGNLdrcpUw", 10),
                new("Root Beer", "JfnVXFmilU6ysv-VbTAe3A", 0),
                new("Salted Pretzels", "InQ25wQMGkG_bvuD5rf2Ag", 0),
                new("Supreme Pizza", "wUCIKdJSvEmiQHYMyx4X4w", 20),
                new("KO Icon - Grenade", "EAhk3ZZdXEmH5wRAXXT24Q", 20),
                new("KO Icon - Sword & Shield", "5AJin8T2iEG7BzOPOgx2HA", 20),
                new("KO Icon - Winged Skull", "U38Qe6rhEk6mFvArHfYjng", 20),
                new("KO Icon - Tire Track", "J1WqFNUWo0OBi4LGKPDHWw", 20)
            };

        [HttpGet("/")]
        public IActionResult GetNS()
        {

            if (Request.Headers.Accept.Any(value =>
                    value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true))
            {
                return Redirect("/recnet/");
            }

            return BuildNameServerResponse();
        }

        [HttpGet("/nameserver")]
        public IActionResult GetNameServer([FromQuery] int? v = null)
        {
            return BuildNameServerResponse();
        }

        private IActionResult BuildNameServerResponse()
        {
            string url = ServerConfig.BaseURL.TrimEnd('/');
            return Ok(new
            {
                Accounts = url + "/acc",
                API = url,
                Auth = url + "/auth",
                BugReporting = url,
                Cards = url,
                CDN = url + "/cdn",
                Chat = url,
                Clubs = url,
                CMS = url,
                Commerce = url,
                Data = url,
                DataCollection = url,
                Discovery = url,
                Econ = url,
                GameLogs = url,
                Geo = url,

                Images = url + "/imageserver-v2",
                Leaderboard = url,
                Link = url,
                Lists = url,
                Matchmaking = url + "/match",
                Moderation = url,
                Notifications = url + "/noti",
                PlatformNotifications = url,
                PlayerSettings = url,
                RoomComments = url,
                Rooms = url + "/roomserver",
                Storage = url,
                Strings = url,
                StringsCDN = url,
                Studio = url,
                Thorn = url,
                Videos = url,
                WWW = url
            });
        }

        [HttpPost("/api/gamesight/event")]
        public IActionResult StubGameSightEvent()
        {

            return Ok(new { success = true });
        }

        [HttpGet("/api/storefronts/v3/giftdropstore/{storeId:int}")]
        public IActionResult StubGetGiftDropStore([FromRoute] int storeId)
        {

            List<CatalogSku> storeItems = storeId == 3
                ? GetWatchStoreSkusWithHairDyes()
                : GetDailyStoreSkus(storeId);

            if (storeId == 3)
            {
                Console.WriteLine(
                    $"[HAIR DYE STOREFRONT] total={storeItems.Count} " +
                    $"dyes={storeItems.Count(IsHairDyeCatalogItem)}");
            }

            return Ok(new CatalogStorefront
            {
                StorefrontType = storeId,
                NextUpdate = DateTime.Today.AddDays(1).ToUniversalTime(),
                StoreItems = storeItems
            });
        }

        [HttpGet("/api/quickPlay/v1/getandclear")]
        public IActionResult StubGetAndClearQuickPlay()
        {
            return Ok(System.Array.Empty<object>());
        }

        [HttpGet("/api/challenge/v2/getCurrent")]
        public IActionResult GetCurrentChallenges()
        {
            var (challengeMapId, startsAt, endsAt) = RecNetDB.GetCurrentChallengeWindow();
            long accountId = AuthStuff.GetPlayerId(Request) ?? 0;
            object[] challenges =
            {
                CurrentChallenge(
                    accountId: accountId,
                    challengeMapId: challengeMapId,
                    id: 1,
                    title: "Complete Crimson Cauldron",
                    description: "Complete Curse of the Crimson Cauldron.",
                    tooltip: "Complete ^CrimsonCauldron.",
                    config: "{\"ct\":6,\"vs\":[2],\"ex\":true,\"in\":true}",
                    goal: 1),
                CurrentChallenge(
                    accountId: accountId,
                    challengeMapId: challengeMapId,
                    id: 2,
                    title: "Complete Quest for the Golden Trophy",
                    description: "Complete Quest for the Golden Trophy.",
                    tooltip: "Complete ^GoldenTrophy.",
                    config: "{\"ct\":6,\"vs\":[2],\"ex\":true,\"in\":true}",
                    goal: 1),
                CurrentChallenge(
                    accountId: accountId,
                    challengeMapId: challengeMapId,
                    id: 3,
                    title: "Beat the Rise of the Jumbotron",
                    description: "Complete The Rise of Jumbotron.",
                    tooltip: "Complete ^TheRiseOfJumbotron.",
                    config: "{\"ct\":6,\"vs\":[2],\"ex\":true,\"in\":true}",
                    goal: 1),
                CurrentChallenge(
                    accountId: accountId,
                    challengeMapId: challengeMapId,
                    id: 4,
                    title: "Complete 5 games of Paintball",
                    description: "Complete 5 games of Paintball.",
                    tooltip: "Finish 5 full games in ^Paintball.",
                    config: "{\"ct\":1,\"ctc\":[{\"ct\":6,\"vs\":[2],\"ex\":true,\"in\":true}],\"cc\":5}",
                    goal: 5),
                CurrentChallenge(
                    accountId: accountId,
                    challengeMapId: challengeMapId,
                    id: 5,
                    title: "Complete 5 games of Dodgeball",
                    description: "Complete 5 games of Dodgeball.",
                    tooltip: "Finish 5 full games in ^Dodgeball.",
                    config: "{\"ct\":1,\"ctc\":[{\"ct\":6,\"vs\":[2],\"ex\":true,\"in\":true}],\"cc\":5}",
                    goal: 5)
            };

            int completedChallengeCount = Enumerable.Range(1, 5)
                .Count(id => RecNetDB.GetChallengeProgress(
                    accountId,
                    challengeMapId,
                    id,
                    ChallengeGoal(id)).Complete);

            if (accountId > 0 && completedChallengeCount >= 3)
            {
                GrantPlayerEquipment(
                    accountId,
                    WeeklyChallengeRewardEquipmentPrefab,
                    WeeklyChallengeRewardModificationGuid);
            }

            Response.Headers.CacheControl = "private, max-age=30";
            return Ok(new
            {
                ChallengeMapId = challengeMapId,
                StartAt = startsAt,
                EndAt = endsAt,
                ServerTime = DateTime.UtcNow,
                Challenges = challenges,
                Gift = new
                {
                    GiftDropId = 52,
                    AvatarItemDesc = string.Empty,
                    Xp = 750,
                    Level = 0,
                    EquipmentPrefabName = WeeklyChallengeRewardEquipmentPrefab,
                    EquipmentModificationGuid = WeeklyChallengeRewardModificationGuid
                },
                ChallengeThemeString = "Mocha Weekly Challenges"
            });
        }

        private static object CurrentChallenge(
            long accountId,
            int challengeMapId,
            int id,
            string title,
            string description,
            string tooltip,
            string config,
            int goal)
        {
            var progress = RecNetDB.GetChallengeProgress(accountId, challengeMapId, id, goal);
            return new
            {
                ChallengeId = id,
                Name = title,
                Config = config,
                Description = description,
                Tooltip = tooltip,
                Complete = progress.Complete
            };
        }

        private static int ChallengeGoal(int challengeId) =>
            challengeId is 4 or 5 ? 5 : 1;

        [HttpPost("/api/challenge/v2/updateProgress")]
        [HttpPut("/api/challenge/v2/updateProgress")]
        public IActionResult UpdateChallengeProgress([FromBody] JsonElement body)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var (currentMapId, _, _) = RecNetDB.GetCurrentChallengeWindow();
            int challengeMapId = FindJsonInt(body, "ChallengeMapId") ?? currentMapId;
            int challengeId = FindJsonInt(body, "ChallengeId") ?? 0;
            if (challengeId is < 1 or > 5)
                return BadRequest(new { success = false, error = "invalid_challenge" });

            int goal = ChallengeGoal(challengeId);
            int? suppliedProgress = FindJsonInt(body, "Progress", "CurrentProgress", "Count");
            bool? suppliedComplete = FindJsonBool(body, "Complete", "Completed", "IsCompleted");
            var existing = RecNetDB.GetChallengeProgress(
                accountId.Value,
                challengeMapId,
                challengeId,
                goal);
            int progress = suppliedProgress ?? (suppliedComplete == true ? goal : existing.Progress + 1);
            bool? completionOverride = suppliedComplete == true ? true : null;
            var saved = RecNetDB.SetChallengeProgress(
                accountId.Value,
                challengeMapId,
                challengeId,
                progress,
                goal,
                completionOverride);

            return Ok(new
            {
                success = true,
                ChallengeMapId = saved.ChallengeMapId,
                ChallengeId = saved.ChallengeId,
                Progress = saved.Progress,
                Goal = saved.Goal,
                Complete = saved.Complete
            });
        }

        private static int? FindJsonInt(JsonElement element, params string[] names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) &&
                        ((property.Value.ValueKind == JsonValueKind.Number &&
                          property.Value.TryGetInt32(out int value)) ||
                         (property.Value.ValueKind == JsonValueKind.String &&
                          int.TryParse(property.Value.GetString(), out value))))
                    {
                        return value;
                    }

                    int? nested = FindJsonInt(property.Value, names);
                    if (nested.HasValue)
                        return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    int? nested = FindJsonInt(item, names);
                    if (nested.HasValue)
                        return nested;
                }
            }

            return null;
        }

        private static bool? FindJsonBool(JsonElement element, params string[] names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (property.Value.ValueKind == JsonValueKind.True)
                            return true;
                        if (property.Value.ValueKind == JsonValueKind.False)
                            return false;
                    }

                    bool? nested = FindJsonBool(property.Value, names);
                    if (nested.HasValue)
                        return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    bool? nested = FindJsonBool(item, names);
                    if (nested.HasValue)
                        return nested;
                }
            }

            return null;
        }

        private static long? FindJsonLong(
    JsonElement element,
    params string[] names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    bool nameMatches = names.Any(name =>
                        string.Equals(
                            property.Name,
                            name,
                            StringComparison.OrdinalIgnoreCase));

                    if (nameMatches)
                    {
                        if (property.Value.ValueKind == JsonValueKind.Number &&
                            property.Value.TryGetInt64(out long numberValue))
                        {
                            return numberValue;
                        }

                        if (property.Value.ValueKind == JsonValueKind.String &&
                            long.TryParse(
                                property.Value.GetString(),
                                out long stringValue))
                        {
                            return stringValue;
                        }
                    }

                    long? nestedValue = FindJsonLong(property.Value, names);

                    if (nestedValue.HasValue)
                        return nestedValue;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    long? nestedValue = FindJsonLong(item, names);

                    if (nestedValue.HasValue)
                        return nestedValue;
                }
            }

            return null;
        }

        private static string? FindJsonString(
            JsonElement element,
            params string[] names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    bool nameMatches = names.Any(name =>
                        string.Equals(
                            property.Name,
                            name,
                            StringComparison.OrdinalIgnoreCase));

                    if (nameMatches)
                    {
                        string? result = property.Value.ValueKind switch
                        {
                            JsonValueKind.String => property.Value.GetString(),

                            JsonValueKind.Number or
                            JsonValueKind.True or
                            JsonValueKind.False => property.Value.GetRawText(),

                            _ => null
                        };

                        if (!string.IsNullOrWhiteSpace(result))
                            return result;
                    }

                    string? nestedValue = FindJsonString(property.Value, names);

                    if (!string.IsNullOrWhiteSpace(nestedValue))
                        return nestedValue;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string? nestedValue = FindJsonString(item, names);

                    if (!string.IsNullOrWhiteSpace(nestedValue))
                        return nestedValue;
                }
            }

            return null;
        }

        [HttpGet("/api/roomcurrencies/v1/getAllBalances")]
        public IActionResult StubGetAllRoomCurrencyBalances(
            [FromQuery] long roomId)
        {
            return Ok(System.Array.Empty<object>());
        }

        [HttpGet("/acc/parentalcontrol/me")]
        public IActionResult StubGetMyParentalControls()
        {
            return Ok(new
            {
                IsEnabled = false,
                IsJunior = false
            });
        }

        [HttpGet("/subscription/mine/member")]
        public IActionResult GetMySubscriptionMembership()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            bool hasRRPlus = PlayerDB.HasRRPlus(accountId.Value);

            return Ok(hasRRPlus
                ? new[]
                {
                    new
                    {
                        IsActive = true,
                        IsSubscribed = true,
                        Status = "Active",
                        ExpiresAt = "2099-12-31T23:59:59Z"
                    }
                }
                : Array.Empty<object>());
        }

        [HttpPost("/api/CampusCard/v1/UpdateAndGetSubscription")]
        public IActionResult UpdateAndGetSubscription()
        {
            var rawAccountId = AuthStuff.GetPlayerId(Request);

            if (rawAccountId == null)
                return Unauthorized();

            long playerId = (long)rawAccountId;
            int recNetPlayerId = checked((int)playerId);

            bool hasRRPlus = PlayerDB.HasRRPlus(playerId);
            if (!hasRRPlus)
                return Ok(new { subscription = (object?)null, platformAccountSubscribedPlayerId = (int?)null });

            DateTime now = DateTime.UtcNow;

            var response = new
            {
                subscription = new
                {
                    subscriptionId = playerId,
                    recNetPlayerId = recNetPlayerId,

                    platformType = 0,

                    platformId = $"local-{playerId}",
                    platformPurchaseId = $"local-rrplus-{playerId}",

                    level = 1,

                    period = 0,

                    expirationDate = now.AddYears(10),
                    isAutoRenewing = false,
                    createdAt = now.AddDays(-1),
                    modifiedAt = now
                },

                platformAccountSubscribedPlayerId = (int?)null
            };

            return Ok(response);
        }

        [HttpGet("/api/roomkeys/v1/room")]
        public IActionResult StubGetRoomKeys([FromQuery] long roomId)
        {
            return Ok(System.Array.Empty<object>());
        }

        [HttpGet("/api/keepsakes/categories")]
        [HttpGet("/api/keepsakes/v1/categories")]
        public IActionResult StubGetKeepsakeCategories()
        {
            return Ok(System.Array.Empty<object>());
        }

        [HttpGet("/api/influencerpartnerprogram/influencers")]
        public IActionResult GetInfluencers(
            [FromQuery] int take = 1000,
            [FromQuery] string? continuationToken = null)
        {
            int safeTake = Math.Clamp(take, 1, 1000);

            var influencerIds = PlayerDB.Players
                .FindAll()
                .Where(player =>
                    player.PlayerId > 0 &&
                    player.PlayerId <= int.MaxValue &&
                    player.PlayerRoles != null &&
                    player.PlayerRoles.Contains(
                        PlayerDBClasses.PlayerRoles.Influencer))
                .OrderBy(player => player.PlayerId)
                .Take(safeTake)
                .Select(player => (int)player.PlayerId)
                .ToList();

            Console.WriteLine(
                $"[INFLUENCER] Returning {influencerIds.Count}: " +
                string.Join(", ", influencerIds)
            );

            return Ok(new
            {
                influencerIds,
                continuationToken = (string?)null
            });
        }

        [HttpGet("/api/incentivizedreferrals/progress")]
        public IActionResult StubGetIncentivizedReferralProgress()
        {
            return Ok(new
            {
                Progress = 0,
                Goal = 0,
                IsComplete = false,
                Rewards = System.Array.Empty<object>()
            });
        }

        [HttpGet("/api/progressionEvents/active")]
        [HttpGet("/api/progressionEvents/v1/active")]
        public IActionResult GetActiveProgressionEventId()
        {
            return Content("null", "application/json");
        }

        [HttpGet("api/versioncheck/v4")]
        public IActionResult VersionCheck()
        {
            return Ok(new
            {
                ValidVersion = 0,
                VersionStatus = 0,
                UpdateNotificationStage = 0,
                IsVersionIslanded = false,
                IsCrossPlayDisabled = false
            });
        }

        [HttpGet("api/gameconfigs/v1/all")]
        public IActionResult GetGameConfigs()
        {
            string path = Path.Combine(Program.dataDir, "APIS", "GameConfigs.json");
            if (!System.IO.File.Exists(path))
                return NotFound();

            string json = System.IO.File.ReadAllText(path);
            try
            {
                if (JsonNode.Parse(json) is not JsonArray configs)
                    return Content(json, "application/json");

                SetGameConfigValue(
                    configs,
                    "Friendotron_AllowSelfGifting",
                    "True");
                SetGameConfigValue(
                    configs,
                    "Friendotron_MaxUsersPerSession",
                    "1");
                return Content(configs.ToJsonString(), "application/json");
            }
            catch (JsonException)
            {
                return Content(json, "application/json");
            }
        }

        private static void SetGameConfigValue(
            JsonArray configs,
            string key,
            string value)
        {
            bool found = false;
            foreach (JsonNode? node in configs)
            {
                if (node is not JsonObject config ||
                    !string.Equals(
                        config["Key"]?.ToString(),
                        key,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                config["Value"] = value;
                found = true;
            }

            if (!found)
            {
                configs.Add(new JsonObject
                {
                    ["Key"] = key,
                    ["Value"] = value
                });
            }
        }

        [HttpGet("api/config/v1/amplitude")]
        public IActionResult GetAmplitude()
        {
            return Ok(new
            {
                AmplitudeKey = "cb2fb2ecb9953512c29af5bca58f2b4a",
                UseRudderStack = true,
                RudderStackKey = "23NiJHIgu3koaGNCZIiuYvIQNCu",
                UseStatSig = true,
                StatSigKey = "client-SBZkOrjD3r1Cat3f3W8K6sBd11WKlXZXIlCWj6l4Aje",
                StatSigEnvironment = 0
            });
        }

        [HttpGet("/api/avatar/")]
        [HttpGet("/api/avatar")]
        public IActionResult GetAvatarBase()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();
            return Ok(new { AccountId = accountId.Value });
        }

        [HttpGet("/api/config/")]
        [HttpGet("/api/config")]
        public IActionResult GetGeneralConfig()
        {
            return Ok(new
            {
                ServerName = "Mocha",
                GameVersion = ServerConfig.GameVersion,
                MaintenanceMinutes = 0
            });
        }

        [HttpGet("/api/config/v1/freegiftbutton")]
        public IActionResult GetFreeGiftButtonConfig()
        {

            return Ok(new { Enabled = false, Available = false });
        }

        [HttpGet("/api/players/v4/current/contact")]
        public IActionResult GetMyContactInfo()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var account = PlayerDB.Players.FindById(accountId.Value);
            if (account?.Player == null)
                return NotFound();

            return Ok(new
            {
                Email = account.Player.Email ?? string.Empty,
                EmailVerified = !string.IsNullOrWhiteSpace(account.Player.Email),
                Phone = (string?)null,
                PhoneVerified = false
            });
        }

        [HttpGet("/api/playerwarnings")]
        public IActionResult GetMyPlayerWarnings()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var warnings = (PlayerDB.Players.FindById(accountId.Value)?.Player?.PlayerExtra?.Warnings
                ?? new List<string>())
                .Select(warningKey => new { WarningKey = warningKey, Acknowledged = false })
                .ToList();
            return Ok(warnings);
        }

        public class AcknowledgeWarningRequest
        {
            public string? WarningKey { get; set; }
        }

        [HttpPost("/api/playerwarnings/acknowledge")]
        public IActionResult AcknowledgePlayerWarning([FromBody] AcknowledgeWarningRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var account = PlayerDB.Players.FindById(accountId.Value);
            var warnings = account?.Player?.PlayerExtra?.Warnings;
            if (account?.Player?.PlayerExtra != null && warnings != null &&
                !string.IsNullOrWhiteSpace(request.WarningKey))
            {
                warnings.RemoveAll(value => value == request.WarningKey);
                PlayerDB.Players.Update(account);
            }
            return Ok(new { success = true });
        }

        public class ConsumableTransferRequest
        {
            public string? ConsumableItemDesc { get; set; }
            public long ConsumableItemId { get; set; }
            public long ToAccountId { get; set; }
            public int Amount { get; set; } = 1;
        }

        [HttpPost("/api/consumables/v1/transfer")]
        public IActionResult TransferConsumable([FromBody] ConsumableTransferRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();
            if (request.ToAccountId == accountId.Value)
                return BadRequest(new { error = "Can't transfer to yourself." });
            if (request.Amount <= 0)
                return BadRequest(new { error = "Amount must be positive." });
            if (PlayerDB.Players.FindById(request.ToAccountId)?.Player == null)
                return NotFound(new { error = "Recipient not found." });

            string descriptor = request.ConsumableItemDesc ?? string.Empty;
            int owned = PlayerInventoryStore.GetConsumableQuantity(accountId.Value, descriptor, request.ConsumableItemId);
            if (owned < request.Amount)
                return BadRequest(new { error = "You don't have enough of that item." });

            PlayerInventoryStore.SetConsumableQuantity(accountId.Value, descriptor, request.ConsumableItemId, null, owned - request.Amount);
            PlayerInventoryStore.AddConsumable(request.ToAccountId, descriptor, request.ConsumableItemId, null, request.Amount);

            return Ok(new { success = true });
        }

        [HttpGet("/api/customAvatarItems")]
        [HttpGet("/api/customAvatarItems/v1/me")]
        public IActionResult GetMyCustomAvatarItems()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            return Ok(LoadCustomAvatarItems(accountId.Value));
        }

        public class CustomAvatarItemsBulkRequest
        {
            public List<long>? CreatorAccountIds { get; set; }
        }

        [HttpPost("/api/customAvatarItems/v1/bulk")]
        public IActionResult GetCustomAvatarItemsBulk([FromBody] CustomAvatarItemsBulkRequest request)
        {
            if (AuthStuff.GetPlayerId(Request) == null)
                return Unauthorized();

            var items = (request.CreatorAccountIds ?? new List<long>())
                .Distinct()
                .SelectMany(LoadCustomAvatarItems)
                .ToList();
            return Ok(items);
        }

        public class ItemWishlistRequest
        {
            public string? ItemDesc { get; set; }
        }

        [HttpGet("/api/itemWishlists")]
        public IActionResult GetMyItemWishlist()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var wishlist = PlayerDB.Players.FindById(accountId.Value)?.Player?.PlayerExtra?.ItemWishlist
                ?? new List<string>();
            return Ok(wishlist);
        }

        [HttpPost("/api/itemWishlists/v1/wishlist/")]
        [HttpPost("/api/itemWishlists/v1/wishlist")]
        public IActionResult AddToItemWishlist([FromBody] ItemWishlistRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.ItemDesc))
                return BadRequest(new { error = "Missing item." });

            var account = PlayerDB.Players.FindById(accountId.Value);
            if (account?.Player == null)
                return Unauthorized();

            account.Player.PlayerExtra ??= new PlayerDBClasses.PlayerExtra();
            account.Player.PlayerExtra.ItemWishlist ??= new List<string>();
            if (!account.Player.PlayerExtra.ItemWishlist.Contains(request.ItemDesc))
            {
                account.Player.PlayerExtra.ItemWishlist.Add(request.ItemDesc);
                PlayerDB.Players.Update(account);
            }
            return Ok(new { success = true, wishlist = account.Player.PlayerExtra.ItemWishlist });
        }

        [HttpDelete("/api/itemWishlists/v1/wishlist/")]
        [HttpDelete("/api/itemWishlists/v1/wishlist")]
        public IActionResult RemoveFromItemWishlist([FromBody] ItemWishlistRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var account = PlayerDB.Players.FindById(accountId.Value);
            var wishlist = account?.Player?.PlayerExtra?.ItemWishlist;
            if (wishlist != null && !string.IsNullOrWhiteSpace(request.ItemDesc))
            {
                wishlist.Remove(request.ItemDesc);
                PlayerDB.Players.Update(account!);
            }
            return Ok(new { success = true });
        }

        public class GenericReportRequest
        {
            public long TargetId { get; set; }
            public string? Reason { get; set; }
            public string? Details { get; set; }
        }

        [HttpPost("/api/rooms/v2/report")]
        public IActionResult ReportRoomV2([FromBody] GenericReportRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var room = RoomDB.Rooms.FindById(request.TargetId);
            DiscordLogger.LogReport(
                $"🚩 **Room reported** - \"{room?.Name ?? $"Room {request.TargetId}"}\" (ID {request.TargetId}) " +
                $"by account {accountId.Value}: {request.Reason ?? "No reason given."} {request.Details}");
            return Ok(new { success = true });
        }

        [HttpPost("/api/clubreporting/v1/report")]
        public IActionResult ReportClub([FromBody] GenericReportRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            DiscordLogger.LogReport(
                $"🚩 **Club reported** - Club {request.TargetId} by account {accountId.Value}: " +
                $"{request.Reason ?? "No reason given."} {request.Details}");
            return Ok(new { success = true });
        }

        [HttpPost("/api/screensharereports/v1/report")]
        public IActionResult ReportScreenShare([FromBody] GenericReportRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            DiscordLogger.LogReport(
                $"🚩 **Screen share reported** - target account {request.TargetId} by account {accountId.Value}: " +
                $"{request.Reason ?? "No reason given."} {request.Details}");
            return Ok(new { success = true });
        }

        [HttpPost("/api/inventions/v1/report")]
        public IActionResult ReportInvention([FromBody] GenericReportRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            DiscordLogger.LogReport(
                $"🚩 **Invention reported** - Invention {request.TargetId} by account {accountId.Value}: " +
                $"{request.Reason ?? "No reason given."} {request.Details}");
            return Ok(new { success = true });
        }

        public class BugReportRequest
        {
            public string? Description { get; set; }
            public string? Category { get; set; }
        }

        [HttpPost("/api/bugreports/v1/report")]
        public IActionResult ReportBug([FromBody] BugReportRequest request)
        {
            var reporter = AuthStuff.GetCurrentPlayer(Request);
            if (reporter?.Player == null)
                return Unauthorized();

            string description = request.Description?.Trim() ?? string.Empty;
            if (description.Length is < 3 or > 4000)
                return BadRequest(new { error = "Description must be 3-4000 characters." });

            var bugReport = new ReportsDB.BugReport
            {
                ReporterId = reporter.PlayerId,
                ReporterUsername = reporter.Player.Username,
                Description = description,
                Category = request.Category?.Trim()
            };
            ReportsDB.BugReports.Insert(bugReport);

            DiscordLogger.LogReport(
                $"🐛 **Bug report** from {reporter.Player.Username ?? reporter.PlayerId.ToString()} " +
                $"({(string.IsNullOrEmpty(bugReport.Category) ? "uncategorized" : bugReport.Category)}): " +
                $"{description}");

            return Ok(new { success = true, id = bugReport.Id });
        }

        [HttpGet("/api/roomconsumables/v1/roomConsumable")]
        public IActionResult GetRoomConsumableCatalog()
        {
            if (AuthStuff.GetPlayerId(Request) == null)
                return Unauthorized();
            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/api/roomEarningsDistributions/v1/earningsDistribution")]
        public IActionResult GetRoomEarningsDistribution()
        {
            if (AuthStuff.GetPlayerId(Request) == null)
                return Unauthorized();
            return Ok(new { TotalEarnings = 0, Distributions = Array.Empty<object>() });
        }

        [HttpPost("/api/roomCurrencies/v2/purchase")]
        [HttpPost("/api/roomconsumables/v1/roomconsumable/{skuId}/purchase/tokens")]
        public IActionResult PurchaseRoomCurrencyOrConsumable()
        {
            if (AuthStuff.GetPlayerId(Request) == null)
                return Unauthorized();

            return Ok(new { success = true });
        }

        [HttpGet("api/avatar/v1/defaultunlocked")]
        public IActionResult GetDefaultUnlocked()
        {
            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("api/avatar/v1/defaultbaseavataritems")]
        public IActionResult GetDefaultBaseAvatarItems()
        {

            return Ok(Array.Empty<object>());
        }

        [HttpPost("/api/ageverification/generatecode")]
        [HttpGet("/api/ageverification/generatecode")]
        public IActionResult GenerateAgeVerificationCode()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            string code = AgeVerificationDB.GenerateCode(id.Value);
            string verificationUrl = $"{ServerConfig.BaseURL.TrimEnd('/')}/recnet/#ageverification?code={code}";

            return Ok(new
            {
                ActionCode = code,
                VerificationUrl = verificationUrl
            });
        }

        [HttpGet("/api/banappeal/generatecode")]
        public IActionResult GenerateBanAppealCodeDirect()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized(new { error = "You must be logged in." });

            var account = PlayerDB.Players.FindById(accountId.Value);
            PlayerDBClasses.ModerationBlockDetails? details = account?.Player?.PlayerExtra?.ModerationBlockDetails;
            if (account?.Player == null || details == null ||
                !PlayerDB.IsPlayerBanned(accountId.Value, out _))
            {
                return BadRequest(new { error = "This account isn't currently banned." });
            }

            if (string.IsNullOrWhiteSpace(details.AppealCode))
            {
                const string appealCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
                Span<char> buffer = stackalloc char[10];
                for (int i = 0; i < buffer.Length; i++)
                    buffer[i] = appealCodeChars[System.Random.Shared.Next(appealCodeChars.Length)];
                details.AppealCode = new string(buffer);
                PlayerDB.Players.Update(account);
            }

            return Ok(new
            {
                code = details.AppealCode,
                appealUrl = $"{ServerConfig.BaseURL.TrimEnd('/')}/recnet/banappeal?code={details.AppealCode}",
                alreadySubmitted = details.AppealSubmitted
            });
        }

        private static object ToPlayerEventDto(PlayerEventDB.PlayerEvent evt)
        {
            var creator = PlayerDB.Players.FindById(evt.CreatorAccountId);
            return new
            {
                Id = evt.EventId,
                EventId = evt.EventId,
                CreatorAccountId = evt.CreatorAccountId,
                CreatorDisplayName = creator?.Player?.DisplayName ?? creator?.Player?.Username ?? $"Player {evt.CreatorAccountId}",
                Name = evt.Name,
                Description = evt.Description,
                Image = string.IsNullOrWhiteSpace(evt.ImageName) ? null : $"{ServerConfig.BaseURL.TrimEnd('/')}/imageserver-v2/{evt.ImageName}",
                ImageName = evt.ImageName,
                StartsAt = evt.StartsAt,
                EndsAt = evt.EndsAt,
                RoomId = evt.RoomId,
                ClubId = evt.ClubId,
                Accessibility = evt.Accessibility,
                MultiInstance = evt.MultiInstance,
                Tags = evt.Tags,
                CreatedAt = evt.CreatedAt,
                UpdatedAt = evt.UpdatedAt
            };
        }

        private bool CanManagePlayerEvent(PlayerEventDB.PlayerEvent evt, long accountId)
        {
            if (evt.CreatorAccountId == accountId)
                return true;
            var account = PlayerDB.Players.FindById(accountId);
            return account?.PlayerRoles?.Contains(PlayerDBClasses.PlayerRoles.Developer) == true ||
                account?.PlayerRoles?.Contains(PlayerDBClasses.PlayerRoles.Moderator) == true;
        }

        public class PlayerEventCreateRequest
        {
            public string? Name { get; set; }
            public string? Description { get; set; }
            public string? ImageName { get; set; }
            public DateTime StartsAt { get; set; }
            public DateTime? EndsAt { get; set; }
            public long? RoomId { get; set; }
            public long? ClubId { get; set; }
            public string? Accessibility { get; set; }
            public bool MultiInstance { get; set; }
            public List<string>? Tags { get; set; }
        }

        [HttpPost("/api/playerevents/v2")]
        public IActionResult CreatePlayerEvent([FromBody] PlayerEventCreateRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            string name = (request.Name ?? string.Empty).Trim();
            if (name.Length is < 1 or > 100)
                return BadRequest(new { error = "Event name must be 1-100 characters." });

            var evt = PlayerEventDB.Create(new PlayerEventDB.PlayerEvent
            {
                CreatorAccountId = accountId.Value,
                Name = name,
                Description = (request.Description ?? string.Empty).Trim(),
                ImageName = request.ImageName ?? string.Empty,
                StartsAt = request.StartsAt == default ? DateTime.UtcNow : request.StartsAt.ToUniversalTime(),
                EndsAt = request.EndsAt?.ToUniversalTime(),
                RoomId = request.RoomId,
                ClubId = request.ClubId,
                Accessibility = request.Accessibility is "Private" or "ClubOnly" ? request.Accessibility : "Public",
                MultiInstance = request.MultiInstance,
                Tags = (request.Tags ?? new List<string>()).Take(20).ToList()
            });

            return Ok(ToPlayerEventDto(evt));
        }

        [HttpGet("/api/playerevents/v2/{eventId:long}")]
        [HttpGet("/api/playerevents/v1/{eventId:long}")]
        public IActionResult GetPlayerEvent(long eventId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var evt = PlayerEventDB.Get(eventId);
            if (evt == null)
                return NotFound(new { error = "Event not found." });

            return Ok(ToPlayerEventDto(evt));
        }

        [HttpDelete("/api/playerevents/v2/delete/{eventId:long}")]
        public IActionResult DeletePlayerEvent(long eventId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var evt = PlayerEventDB.Get(eventId);
            if (evt == null)
                return NotFound(new { error = "Event not found." });
            if (!CanManagePlayerEvent(evt, accountId.Value))
                return StatusCode(403);

            PlayerEventDB.Delete(eventId);
            return Ok(new { success = true });
        }

        public class PlayerEventFieldUpdate
        {
            public string? Value { get; set; }
        }

        private IActionResult UpdatePlayerEventField(long eventId, Action<PlayerEventDB.PlayerEvent> apply)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var evt = PlayerEventDB.Get(eventId);
            if (evt == null)
                return NotFound(new { error = "Event not found." });
            if (!CanManagePlayerEvent(evt, accountId.Value))
                return StatusCode(403);

            apply(evt);
            PlayerEventDB.Update(evt);
            return Ok(ToPlayerEventDto(evt));
        }

        [HttpPut("/api/playerevents/v2/{eventId:long}/name")]
        public IActionResult UpdatePlayerEventName(long eventId, [FromBody] PlayerEventFieldUpdate request) =>
            UpdatePlayerEventField(eventId, evt =>
            {
                string name = (request.Value ?? string.Empty).Trim();
                if (name.Length is >= 1 and <= 100)
                    evt.Name = name;
            });

        [HttpPut("/api/playerevents/v2/{eventId:long}/description")]
        public IActionResult UpdatePlayerEventDescription(long eventId, [FromBody] PlayerEventFieldUpdate request) =>
            UpdatePlayerEventField(eventId, evt => evt.Description = (request.Value ?? string.Empty).Trim());

        [HttpPut("/api/playerevents/v2/{eventId:long}/image")]
        public IActionResult UpdatePlayerEventImage(long eventId, [FromBody] PlayerEventFieldUpdate request) =>
            UpdatePlayerEventField(eventId, evt => evt.ImageName = request.Value ?? string.Empty);

        public class PlayerEventTimeUpdate
        {
            public DateTime StartsAt { get; set; }
            public DateTime? EndsAt { get; set; }
        }

        [HttpPut("/api/playerevents/v2/{eventId:long}/time")]
        public IActionResult UpdatePlayerEventTime(long eventId, [FromBody] PlayerEventTimeUpdate request) =>
            UpdatePlayerEventField(eventId, evt =>
            {
                if (request.StartsAt != default)
                    evt.StartsAt = request.StartsAt.ToUniversalTime();
                evt.EndsAt = request.EndsAt?.ToUniversalTime();
            });

        public class PlayerEventRoomUpdate
        {
            public long? RoomId { get; set; }
        }

        [HttpPut("/api/playerevents/v2/{eventId:long}/room")]
        public IActionResult UpdatePlayerEventRoom(long eventId, [FromBody] PlayerEventRoomUpdate request) =>
            UpdatePlayerEventField(eventId, evt => evt.RoomId = request.RoomId);

        public class PlayerEventClubUpdate
        {
            public long? ClubId { get; set; }
        }

        [HttpPut("/api/playerevents/v2/{eventId:long}/club")]
        public IActionResult UpdatePlayerEventClub(long eventId, [FromBody] PlayerEventClubUpdate request) =>
            UpdatePlayerEventField(eventId, evt => evt.ClubId = request.ClubId);

        [HttpPut("/api/playerevents/v2/{eventId:long}/accessibility")]
        public IActionResult UpdatePlayerEventAccessibility(long eventId, [FromBody] PlayerEventFieldUpdate request) =>
            UpdatePlayerEventField(eventId, evt =>
            {
                if (request.Value is "Public" or "Private" or "ClubOnly")
                    evt.Accessibility = request.Value;
            });

        public class PlayerEventMultiInstanceUpdate
        {
            public bool MultiInstance { get; set; }
        }

        [HttpPut("/api/playerevents/v2/{eventId:long}/multiinstance")]
        public IActionResult UpdatePlayerEventMultiInstance(long eventId, [FromBody] PlayerEventMultiInstanceUpdate request) =>
            UpdatePlayerEventField(eventId, evt => evt.MultiInstance = request.MultiInstance);

        public class PlayerEventTagsUpdate
        {
            public List<string>? Tags { get; set; }
        }

        [HttpPut("/api/playerevents/v2/{eventId:long}/tags")]
        public IActionResult UpdatePlayerEventTags(long eventId, [FromBody] PlayerEventTagsUpdate request) =>
            UpdatePlayerEventField(eventId, evt => evt.Tags = (request.Tags ?? new List<string>()).Take(20).ToList());

        [HttpGet("/api/playerevents/v1/all/{accountId:long}")]
        public IActionResult GetPlayerEventsForAccount(long accountId)
        {
            if (AuthStuff.GetPlayerId(Request) == null)
                return Unauthorized();

            return Ok(PlayerEventDB.GetByCreator(accountId).Select(ToPlayerEventDto).ToList());
        }

        [HttpGet("/api/playerevents/v1/club/{clubId:long}")]
        public IActionResult GetPlayerEventsForClub(long clubId)
        {
            if (AuthStuff.GetPlayerId(Request) == null)
                return Unauthorized();

            return Ok(PlayerEventDB.GetByClub(clubId).Select(ToPlayerEventDto).ToList());
        }

        [HttpGet("/api/playerevents/v1/clubs")]
        public IActionResult GetPlayerEventsForMyClubs()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var clubIds = ClubDB.GetMemberClubs(accountId.Value).Select(club => club.ClubId).ToHashSet();
            var events = clubIds
                .SelectMany(PlayerEventDB.GetByClub)
                .DistinctBy(evt => evt.EventId)
                .Select(ToPlayerEventDto)
                .ToList();
            return Ok(events);
        }

        public class PlayerEventBulkRequest
        {
            public List<long>? EventIds { get; set; }
        }

        [HttpPost("/api/playerevents/v1/bulk")]
        public IActionResult GetPlayerEventsBulk([FromBody] PlayerEventBulkRequest request)
        {
            if (AuthStuff.GetPlayerId(Request) == null)
                return Unauthorized();

            var ids = request.EventIds ?? new List<long>();
            return Ok(PlayerEventDB.GetByIds(ids).Select(ToPlayerEventDto).ToList());
        }

        public class PlayerEventRespondRequest
        {
            public long EventId { get; set; }
            public string? ResponseType { get; set; }
        }

        [HttpPost("/api/playerevents/v1/respond")]
        public IActionResult RespondToPlayerEvent([FromBody] PlayerEventRespondRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var evt = PlayerEventDB.Get(request.EventId);
            if (evt == null)
                return NotFound(new { error = "Event not found." });

            string responseType = request.ResponseType is "Going" or "Interested" or "NotGoing"
                ? request.ResponseType
                : "Going";
            var response = PlayerEventDB.SetResponse(request.EventId, accountId.Value, responseType);
            return Ok(new { response.EventId, response.AccountId, response.ResponseType, response.CreatedAt });
        }

        [HttpPost("/api/playerevents/v1/deleteResponse")]
        [HttpDelete("/api/playerevents/v1/deleteResponse")]
        public IActionResult DeletePlayerEventResponse([FromBody] PlayerEventRespondRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            PlayerEventDB.DeleteResponse(request.EventId, accountId.Value);
            return Ok(new { success = true });
        }

        [HttpGet("/api/playerevents/v1/{eventId:long}/responses")]
        public IActionResult GetPlayerEventResponses(long eventId)
        {
            if (AuthStuff.GetPlayerId(Request) == null)
                return Unauthorized();

            var responses = PlayerEventDB.GetResponses(eventId);
            var accountIds = responses.Select(value => value.AccountId).ToHashSet();
            var players = PlayerDB.Players.FindAll()
                .Where(value => accountIds.Contains(value.PlayerId))
                .ToDictionary(value => value.PlayerId);

            return Ok(responses.Select(response =>
            {
                players.TryGetValue(response.AccountId, out var player);
                return new
                {
                    response.AccountId,
                    DisplayName = player?.Player?.DisplayName ?? player?.Player?.Username ?? $"Player {response.AccountId}",
                    response.ResponseType,
                    response.CreatedAt
                };
            }).ToList());
        }

        public class PlayerEventBulkInviteRequest
        {
            public long EventId { get; set; }
            public List<long>? AccountIds { get; set; }
        }

        [HttpPost("/api/playerevents/v1/bulkInvite")]
        public IActionResult BulkInvitePlayerEvent([FromBody] PlayerEventBulkInviteRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var evt = PlayerEventDB.Get(request.EventId);
            if (evt == null)
                return NotFound(new { error = "Event not found." });

            var targets = (request.AccountIds ?? new List<long>()).Take(200).ToList();
            int invited = 0;
            foreach (long targetId in targets)
            {
                if (targetId == accountId.Value)
                    continue;
                if (PlayerEventDB.GetResponse(evt.EventId, targetId) == null)
                    PlayerEventDB.SetResponse(evt.EventId, targetId, "Interested");
                invited++;
            }

            return Ok(new { success = true, invited });
        }

        [HttpPost("/api/playerevents/v1/broadcast")]
        public IActionResult BroadcastPlayerEvent([FromBody] PlayerEventBulkInviteRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var evt = PlayerEventDB.Get(request.EventId);
            if (evt == null || !CanManagePlayerEvent(evt, accountId.Value))
                return NotFound(new { error = "Event not found." });

            return Ok(new { success = true });
        }

        public class PlayerEventReportRequest
        {
            public long EventId { get; set; }
            public string? Reason { get; set; }
        }

        [HttpPost("/api/playerevents/v1/report")]
        public IActionResult ReportPlayerEvent([FromBody] PlayerEventReportRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var evt = PlayerEventDB.Get(request.EventId);
            if (evt == null)
                return NotFound(new { error = "Event not found." });

            DiscordLogger.LogReport(
                $"🚩 **Player event reported** - \"{evt.Name}\" (Event {evt.EventId}) " +
                $"by account {accountId.Value}: {request.Reason ?? "No reason given."}");
            return Ok(new { success = true });
        }

        [HttpGet("/api/objectives/v1/myprogress")]
        public IActionResult GetMyObjectiveProgress()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(new
            {
                Objectives = new List<object>(),
                ObjectiveGroups = new List<object>()
            });
        }

        [HttpGet("/api/avatar/v2")]
        public IActionResult GetMyAvatar()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized();

            return Ok(player.Player.PlayerExtra.Avatar);
        }

        [HttpPost("/api/avatar/v2/set")]
        public IActionResult SetMyAvatar([FromBody] PlayerDBClasses.Avatar request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(PlayerDB.SetAvatar((long)id, request));
        }

        [HttpGet("/api/avatar/v4/items")]
        public IActionResult GetMyAvatarItems()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            string path = Path.Combine(
                Program.dataDir,
                "APIS",
                "Items",
                "AvatarItems.json");

            if (!System.IO.File.Exists(path))
                return NotFound();

            return Ok(GetAvatarItemsVisibleToPlayer((long)id, path));
        }

        [HttpGet("/api/avatar/v1/lockeditems")]
        public IActionResult GetMyLockedAvatarItems()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            string path = Path.Combine(
                Program.dataDir,
                "APIS",
                "Items",
                "AvatarItems.json");

            if (!System.IO.File.Exists(path))
                return Ok(Array.Empty<object>());

            return Ok(GetAvatarItemsNotOwnedByPlayer((long)id, path));
        }

        private static List<JsonElement> GetAvatarItemsNotOwnedByPlayer(
            long playerId,
            string path)
        {
            string json = System.IO.File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<List<JsonElement>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<JsonElement>();

            var player = PlayerDB.Players.FindById(playerId);
            IEnumerable<string>? legacyItems = player?.Player?.PlayerExtra?.AvatarItems;

            List<PlayerInventoryStore.AvatarItemOwnership> ownedItems =
                PlayerInventoryStore.GetAvatarItems(playerId, legacyItems);

            HashSet<long> ownedIds = ownedItems
                .Where(item => item.AvatarItemId > 0)
                .Select(item => item.AvatarItemId)
                .ToHashSet();
            HashSet<string> ownedDescriptors = ownedItems
                .Select(item => item.AvatarItemDesc)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return items
                .Where(item =>
                {
                    string descriptor = item.TryGetProperty("AvatarItemDesc", out JsonElement descElement)
                        ? descElement.GetString() ?? string.Empty
                        : string.Empty;
                    long itemId = item.TryGetProperty("AvatarItemId", out JsonElement idElement) &&
                                  idElement.TryGetInt64(out long parsedId)
                        ? parsedId
                        : 0;
                    string friendlyName = item.TryGetProperty("FriendlyName", out JsonElement nameElement)
                        ? nameElement.GetString() ?? string.Empty
                        : string.Empty;
                    int rarity = item.TryGetProperty("Rarity", out JsonElement rarityElement) &&
                                 rarityElement.TryGetInt32(out int parsedRarity)
                        ? parsedRarity
                        : 0;

                    bool owned = (itemId > 0 && ownedIds.Contains(itemId)) ||
                        (!string.IsNullOrWhiteSpace(descriptor) &&
                         ownedDescriptors.Contains(descriptor));
                    bool unfinished = rarity <= 0 && string.IsNullOrWhiteSpace(friendlyName);

                    return !owned && !unfinished && !IsExcludedAvatarItemName(friendlyName);
                })
                .ToList();
        }

        private static List<JsonElement> GetAvatarItemsVisibleToPlayer(
            long playerId,
            string path)
        {
            string json = System.IO.File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<List<JsonElement>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<JsonElement>();

            var player = PlayerDB.Players.FindById(playerId);
            IEnumerable<string>? legacyItems = player?.Player?.PlayerExtra?.AvatarItems;

            List<PlayerInventoryStore.AvatarItemOwnership> ownedItems =
                PlayerInventoryStore.GetAvatarItems(playerId, legacyItems);

            HashSet<long> ownedIds = ownedItems
                .Where(item => item.AvatarItemId > 0)
                .Select(item => item.AvatarItemId)
                .ToHashSet();
            HashSet<string> ownedDescriptors = ownedItems
                .Select(item => item.AvatarItemDesc)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return items
                .Where(item =>
                {
                    string descriptor = item.TryGetProperty("AvatarItemDesc", out JsonElement descElement)
                        ? descElement.GetString() ?? string.Empty
                        : string.Empty;
                    long itemId = item.TryGetProperty("AvatarItemId", out JsonElement idElement) &&
                                  idElement.TryGetInt64(out long parsedId)
                        ? parsedId
                        : 0;

                    return (itemId > 0 && ownedIds.Contains(itemId)) ||
                           (!string.IsNullOrWhiteSpace(descriptor) &&
                            ownedDescriptors.Contains(descriptor));
                })
                .ToList();
        }

        private static bool IsAlpacaShirt(JsonElement item)
        {
            if (item.TryGetProperty("AvatarItemId", out JsonElement itemId) &&
                itemId.TryGetInt64(out long parsedItemId) &&
                parsedItemId == PlayerDB.AlpacaShirtAvatarItemId)
            {
                return true;
            }

            return item.TryGetProperty("FriendlyName", out JsonElement name) &&
                string.Equals(
                    name.GetString(),
                    "Alpaca Shirt",
                    StringComparison.OrdinalIgnoreCase);
        }

        private IActionResult GetJsonItems(string fileName)
        {
            string path = Path.Combine(Program.dataDir, "APIS", "Items", fileName);
            return System.IO.File.Exists(path) ? Content(System.IO.File.ReadAllText(path), "application/json") : Ok(ServerConfig.Bracket);
        }

        private IActionResult GetNormalizedConsumables(long? accountId = null)
        {
            string path = Path.Combine(
                Program.dataDir,
                "APIS",
                "Items",
                "Consumables.json");

            if (!System.IO.File.Exists(path))
                return Ok(Array.Empty<object>());

            try
            {
                JsonNode? root = JsonNode.Parse(System.IO.File.ReadAllText(path));
                if (root is not JsonArray sourceItems)
                {
                    Console.WriteLine(
                        "[CONSUMABLE NORMALIZE] Invalid root; expected JSON array.");
                    return Ok(Array.Empty<object>());
                }

                var normalizedItems = new JsonArray();
                int repairedCreatedAts = 0;
                int repairedKeys = 0;
                int skipped = 0;
                long returnedQuantity = 0;

                Dictionary<long, int>? ownedConsumablesById = null;
                Dictionary<string, int>? ownedConsumablesByDescriptor = null;
                if (accountId.HasValue)
                {
                    List<PlayerInventoryStore.ConsumableOwnership> ownedConsumables =
                        PlayerInventoryStore.GetConsumables(accountId.Value);

                    ownedConsumablesById = ownedConsumables
                        .Where(item => item.ConsumableItemId > 0)
                        .GroupBy(item => item.ConsumableItemId)
                        .ToDictionary(
                            group => group.Key,
                            group => (int)Math.Min(
                                100_000L,
                                group.Sum(item => (long)Math.Max(0, item.Quantity))));

                    ownedConsumablesByDescriptor = ownedConsumables
                        .Where(item => !string.IsNullOrWhiteSpace(item.ConsumableItemDesc))
                        .GroupBy(
                            item => item.ConsumableItemDesc,
                            StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => (int)Math.Min(
                                100_000L,
                                group.Sum(item => (long)Math.Max(0, item.Quantity))),
                            StringComparer.OrdinalIgnoreCase);
                }

                foreach (JsonNode? node in sourceItems)
                {
                    if (node is not JsonObject source)
                    {
                        skipped++;
                        continue;
                    }

                    var item = source.DeepClone().AsObject();
                    List<long> ids = ReadConsumableIds(item);
                    if (ids.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    item["Ids"] = new JsonArray(
                        ids.Select(id => JsonValue.Create(id)).ToArray());

                    List<string> createdAts = ReadConsumableCreatedAts(item);
                    string fallbackCreatedAt =
                        NormalizeConsumableTimestamp(ReadString(item, "CreatedAt"))
                        ?? "2023-04-06T00:00:00.0000000Z";

                    if (createdAts.Count != ids.Count)
                    {
                        repairedCreatedAts++;

                        if (createdAts.Count > ids.Count)
                            createdAts = createdAts.Take(ids.Count).ToList();

                        while (createdAts.Count < ids.Count)
                            createdAts.Add(fallbackCreatedAt);
                    }

                    item["CreatedAts"] = new JsonArray(
                        createdAts.Select(value => JsonValue.Create(value)).ToArray());

                    string? descriptor = ReadString(item, "ConsumableItemDesc");
                    string? friendlyName = ReadString(item, "FriendlyName");

                    if (string.IsNullOrWhiteSpace(descriptor))
                    {
                        item["ConsumableItemDesc"] = $"legacy-consumable-{ids[0]}";
                        repairedKeys++;
                    }

                    if (string.IsNullOrWhiteSpace(friendlyName))
                    {
                        friendlyName = $"Consumable {ids[0]}";
                        item["FriendlyName"] = friendlyName;
                        repairedKeys++;
                    }

                    int ownedQuantity;
                    if (accountId.HasValue)
                    {
                        ownedQuantity = 0;

                        if (ownedConsumablesById != null &&
                            ownedConsumablesById.TryGetValue(ids[0], out int byId))
                        {
                            ownedQuantity = Math.Max(ownedQuantity, byId);
                        }

                        if (!string.IsNullOrWhiteSpace(descriptor) &&
                            ownedConsumablesByDescriptor != null &&
                            ownedConsumablesByDescriptor.TryGetValue(descriptor, out int byDescriptor))
                        {
                            ownedQuantity = Math.Max(ownedQuantity, byDescriptor);
                        }
                    }
                    else
                    {
                        ownedQuantity = Math.Max(1, ids.Count);
                    }

                    if (accountId.HasValue && ownedQuantity <= 0)
                        continue;

                    ownedQuantity = Math.Max(1, ownedQuantity);
                    long firstId = ids[0];
                    string firstCreatedAt = createdAts.FirstOrDefault() ?? fallbackCreatedAt;

                    item["Ids"] = new JsonArray(
                        JsonValue.Create(firstId));
                    item["CreatedAts"] = new JsonArray(
                        JsonValue.Create(firstCreatedAt));

                    item["Count"] = ownedQuantity;
                    item["InitialCount"] = ownedQuantity;
                    item["Quantity"] = ownedQuantity;
                    item["IsActive"] = false;
                    item["ActiveDurationMinutes"] = 0;
                    item["IsTransferable"] = false;
                    item["IsOwned"] = true;
                    item["Owned"] = true;
                    item["Purchased"] = true;
                    returnedQuantity += ownedQuantity;

                    if (IsHairDyeFriendlyName(friendlyName))
                    {
                        item["ConsumableItemType"] = 6;
                        item["ItemType"] = 1;
                    }

                    normalizedItems.Add(item);
                }

                Console.WriteLine(
                    $"[CONSUMABLE NORMALIZE] source={sourceItems.Count} " +
                    $"returned={normalizedItems.Count} " +
                    $"quantity={returnedQuantity} countContract=v25 " +
                    $"createdAtsFixed={repairedCreatedAts} " +
                    $"keysFixed={repairedKeys} skipped={skipped}");

                return Content(
                    normalizedItems.ToJsonString(
                        new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = null,
                            WriteIndented = false
                        }),
                    "application/json");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[CONSUMABLE NORMALIZE] Failed: {ex.Message}");
                return Ok(Array.Empty<object>());
            }
        }

        private static List<long> ReadConsumableIds(JsonObject item)
        {
            var ids = new List<long>();

            if (item["Ids"] is JsonArray idArray)
            {
                foreach (JsonNode? idNode in idArray)
                {
                    if (TryReadLong(idNode, out long id) && id > 0)
                        ids.Add(id);
                }
            }

            if (ids.Count == 0)
            {
                foreach (string propertyName in new[]
                {
                    "ConsumableItemId",
                    "ItemId",
                    "Id"
                })
                {
                    if (TryReadLong(item[propertyName], out long id) && id > 0)
                    {
                        ids.Add(id);
                        break;
                    }
                }
            }

            return ids.Distinct().ToList();
        }

        private static List<string> ReadConsumableCreatedAts(JsonObject item)
        {
            var values = new List<string>();

            if (item["CreatedAts"] is not JsonArray createdAtArray)
                return values;

            foreach (JsonNode? valueNode in createdAtArray)
            {
                string? value = null;
                try
                {
                    value = valueNode?.GetValue<string>();
                }
                catch
                {
                    value = valueNode?.ToJsonString().Trim('"');
                }

                if (!string.IsNullOrWhiteSpace(value) &&
                    DateTime.TryParse(
                        value,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out DateTime parsed))
                {
                    values.Add(parsed.ToUniversalTime().ToString("O"));
                }
            }

            return values;
        }

        private static string? NormalizeConsumableTimestamp(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return DateTime.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out DateTime parsed)
                ? parsed.ToUniversalTime().ToString("O")
                : null;
        }

        private static string? ReadString(
            JsonObject item,
            string propertyName)
        {
            JsonNode? node = item[propertyName];
            if (node == null)
                return null;

            try
            {
                return node.GetValue<string>();
            }
            catch
            {
                string raw = node.ToJsonString().Trim('"');
                return string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : raw;
            }
        }

        private static bool TryReadLong(
            JsonNode? node,
            out long value)
        {
            value = 0;
            if (node == null)
                return false;

            try
            {
                if (node is JsonValue jsonValue &&
                    jsonValue.TryGetValue<long>(out value))
                {
                    return true;
                }
            }
            catch
            {

            }

            return long.TryParse(
                node.ToJsonString().Trim('"'),
                out value);
        }

        private static string GetPlayerEquipmentPath(long accountId)
        {
            return EquipmentInventoryStore.GetPlayerEquipmentPath(accountId);
        }

        private static List<PlayerDBClasses.EquipmentItem> GetOrCreatePlayerEquipment(long accountId)
        {
            return EquipmentInventoryStore.GetOrCreate(accountId);
        }

        private static void SavePlayerEquipment(
            long accountId,
            List<PlayerDBClasses.EquipmentItem> equipment)
        {
            EquipmentInventoryStore.Save(accountId, equipment);
        }

        private static bool OwnsEquipmentItem(
            long accountId,
            string prefabName,
            string modificationGuid)
        {
            if (string.IsNullOrWhiteSpace(prefabName) ||
                string.IsNullOrWhiteSpace(modificationGuid))
            {
                return false;
            }

            return GetOrCreatePlayerEquipment(accountId).Any(item =>
                string.Equals(
                    item.PrefabName,
                    prefabName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    item.ModificationGuid,
                    modificationGuid,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static void GrantPlayerEquipment(
            long accountId,
            string prefabName,
            string modificationGuid)
        {
            var equipment = GetOrCreatePlayerEquipment(accountId);
            var existingReward = equipment.FirstOrDefault(item =>
                string.Equals(item.PrefabName, prefabName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ModificationGuid, modificationGuid, StringComparison.OrdinalIgnoreCase));
            if (existingReward != null)
            {
                existingReward.FriendlyName = "Ghost Bow Skin";
                existingReward.Rarity = 50;
                SavePlayerEquipment(accountId, equipment);
                return;
            }

            string masterPath = Path.Combine(Program.dataDir, "APIS", "Items", "Equipment.json");
            PlayerDBClasses.EquipmentItem? reward = null;
            if (System.IO.File.Exists(masterPath))
            {
                var masterEquipment = JsonSerializer.Deserialize<List<PlayerDBClasses.EquipmentItem>>(
                    System.IO.File.ReadAllText(masterPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                reward = masterEquipment?.FirstOrDefault(item =>
                    string.Equals(item.PrefabName, prefabName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.ModificationGuid, modificationGuid, StringComparison.OrdinalIgnoreCase));
            }

            if (reward != null)
            {
                reward.FriendlyName = "Ghost Bow Skin";
                reward.Rarity = 50;
            }

            equipment.Add(reward ?? new PlayerDBClasses.EquipmentItem
            {
                PrefabName = prefabName,
                ModificationGuid = modificationGuid,
                FriendlyName = "Ghost Bow Skin",
                Rarity = 50,
                PlatformMask = -1
            });
            SavePlayerEquipment(accountId, equipment);
        }

        private static bool GrantPurchasedEquipment(
            long accountId,
            CatalogSku sku)
        {
            if (!IsEquipmentSkin(sku))
                return false;

            var equipment = GetOrCreatePlayerEquipment(accountId);
            if (equipment.Any(item =>
                    string.Equals(
                        item.PrefabName,
                        sku.EquipmentPrefabName,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        item.ModificationGuid,
                        sku.EquipmentModificationGuid,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            equipment.Add(new PlayerDBClasses.EquipmentItem
            {
                PrefabName = sku.EquipmentPrefabName,
                ModificationGuid = sku.EquipmentModificationGuid,
                UnlockedLevel = 0,
                Favorited = false,
                PlatformMask = sku.PlatformMask,
                FriendlyName = sku.FriendlyName,
                Tooltip = sku.Tooltip,
                Rarity = sku.Rarity,
                ThumbnailImage = sku.ThumbnailImage
            });
            SavePlayerEquipment(accountId, equipment);
            return true;
        }

        [HttpGet("/api/equipment/v2/getUnlocked")]
        public IActionResult GetMyEquipment()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            var equipment = GetOrCreatePlayerEquipment((long)id);
            return Ok(equipment);
        }

        [HttpPost("/api/equipment/v1/update")]
        public IActionResult UpdateMyEquipment([FromBody] JsonElement request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            List<PlayerDBClasses.EquipmentItem> updates;
            try
            {
                JsonElement items = request;
                if (request.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in request.EnumerateObject())
                    {
                        if (property.Name.Equals("items", StringComparison.OrdinalIgnoreCase) ||
                            property.Name.Equals("equipment", StringComparison.OrdinalIgnoreCase) ||
                            property.Name.Equals("results", StringComparison.OrdinalIgnoreCase))
                        {
                            items = property.Value;
                            break;
                        }
                    }
                }

                updates = items.ValueKind switch
                {
                    JsonValueKind.Array =>
                        JsonSerializer.Deserialize<List<PlayerDBClasses.EquipmentItem>>(
                            items.GetRawText(),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
                        new List<PlayerDBClasses.EquipmentItem>(),
                    JsonValueKind.Object => new List<PlayerDBClasses.EquipmentItem>
                    {
                        JsonSerializer.Deserialize<PlayerDBClasses.EquipmentItem>(
                            items.GetRawText(),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
                    },
                    _ => new List<PlayerDBClasses.EquipmentItem>()
                };

                updates.RemoveAll(item => item == null);
            }
            catch (JsonException)
            {
                updates = new List<PlayerDBClasses.EquipmentItem>();
            }

            if (updates.Count == 0)
                return BadRequest(new { success = false, error = "Invalid request body." });

            var equipment = GetOrCreatePlayerEquipment((long)id);

            foreach (var incoming in updates)
            {
                var item = equipment.FirstOrDefault(e => e.ModificationGuid == incoming.ModificationGuid && e.PrefabName == incoming.PrefabName);

                if (item != null)
                    item.Favorited = incoming.Favorited;

            }

            SavePlayerEquipment((long)id, equipment);

            return Ok(new { success = true });
        }

        [HttpPost("/api/gamerewards/v1/request")]
        public IActionResult RequestGameRewards()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            string path = Path.Combine(Program.dataDir, "APIS", "Items", "AvatarItems.json");
            if (!System.IO.File.Exists(path))
                return Ok(new { Rewards = Array.Empty<object>() });

            string json = System.IO.File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<List<JsonElement>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<JsonElement>();

            if (items.Count == 0)
                return Ok(new { Rewards = Array.Empty<object>() });

            var random = new Random();
            var rewards = items.OrderBy(_ => random.Next()).Take(Math.Min(3, items.Count)).ToList();

            return Ok(new { Rewards = rewards });
        }

        public class EquipmentUpdateRequest
        {
            public string PrefabName { get; set; } = "";
            public string ModificationGuid { get; set; } = "";
            public bool Favorited { get; set; }
        }

        [HttpGet("/api/PlayerReporting/v1/moderationBlockDetails")]
        public IActionResult GetMyModerationBlockDetails()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized();

            return Ok(player.Player.PlayerExtra.ModerationBlockDetails);
        }

        [HttpPost("/api/freegifts/v1/sendmultiple")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> SendMultipleFreeGifts()
        {
            long? senderId = AuthStuff.GetPlayerId(Request);
            if (!senderId.HasValue)
                return Unauthorized();

            ParsedFreeGiftRequest parsed = await ReadFreeGiftRequestAsync();
            parsed.Message = parsed.Message.Trim();
            if (parsed.Message.Length > 200)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "gift_message_too_long"
                });
            }

            Console.WriteLine(
                $"[FRIENDOTRON REQUEST] sender={senderId.Value} " +
                $"recipients=[{string.Join(",", parsed.RecipientIds)}] " +
                $"messageLength={parsed.Message.Length} " +
                $"ContentType={Request.ContentType}, ContentLength={Request.ContentLength ?? 0}");

            List<long> recipientIds = parsed.RecipientIds
                .Where(id =>
                    id > 0 &&
                    id <= int.MaxValue)
                .Distinct()
                .ToList();

            if (recipientIds.Count == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "missing_recipient_ids",
                    contentType = Request.ContentType
                });
            }

            if (recipientIds.Count != 1)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "friendotron_one_recipient_only"
                });
            }

            long recipientId = recipientIds[0];
            if (recipientId != senderId.Value)
            {
                PlayerRelationship? relationship =
                    RelationshipDB.GetRelationship(
                        senderId.Value,
                        recipientId);
                if (relationship?.RelationshipType != RelationshipType.Friend)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new
                    {
                        success = false,
                        error = "friendotron_recipient_not_friend"
                    });
                }
            }

            HashSet<string> installedConsumables = GetCatalogSkus()
                .Where(IsConsumableCatalogItem)
                .Select(item => item.ConsumableItemDesc)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<FriendotronConsumableReward> rewardPool =
                FriendotronConsumableRewards
                    .Where(reward => installedConsumables.Contains(
                        reward.ConsumableItemDesc))
                    .ToList();

            if (rewardPool.Count == 0)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = "friendotron_consumable_pool_empty"
                });
            }

            FriendotronConsumableReward reward =
                rewardPool[Random.Shared.Next(rewardPool.Count)];
            var pendingGift = new GiftPackage
            {
                FromPlayerId = checked((int)senderId.Value),
                Message = parsed.Message,
                AvatarItemDesc = string.Empty,
                ConsumableItemDesc = reward.ConsumableItemDesc,
                ConsumableQuantity = 1,
                EquipmentPrefabName = string.Empty,
                EquipmentModificationGuid = string.Empty,
                CurrencyType = (int)CurrencyType.RecCenterTokens,
                GiftContext = FriendotronGiftContext,
                Rarity = reward.Rarity,
                Platform = -1,
                PlatformMask = -1,
                BalanceType =
                    (int)BalanceType.NonPurchasedNotUsableInP2P,
                IsQuery = false,
                Unique = false
            };

            PlayerDB.FriendotronGiftStatus queueStatus =
                PlayerDB.QueueFriendotronGift(
                    senderId.Value,
                    recipientId,
                    pendingGift,
                    out GiftPackage? package,
                    out DateTime nextAvailableAtUtc);

            if (queueStatus ==
                PlayerDB.FriendotronGiftStatus.DailyLimitReached)
            {
                long retryAfterSeconds = Math.Max(
                    1,
                    (long)Math.Ceiling(
                        (nextAvailableAtUtc - DateTime.UtcNow).TotalSeconds));
                Response.Headers["Retry-After"] = retryAfterSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);

                Console.WriteLine(
                    $"[FRIENDOTRON DENIED] sender={senderId.Value} " +
                    $"recipient={recipientId} reason=daily_limit " +
                    $"next={nextAvailableAtUtc:O}");

                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    success = false,
                    error = "friendotron_daily_limit",
                    nextGiftAt = nextAvailableAtUtc,
                    retryAfterSeconds
                });
            }

            if (queueStatus != PlayerDB.FriendotronGiftStatus.Success ||
                package == null)
            {
                Console.WriteLine(
                    $"[FRIENDOTRON ERROR] sender={senderId.Value} " +
                    $"recipient={recipientId} status={queueStatus}");

                return queueStatus switch
                {
                    PlayerDB.FriendotronGiftStatus.RecipientNotFound =>
                        NotFound(new
                        {
                            success = false,
                            error = "friendotron_recipient_not_found"
                        }),
                    PlayerDB.FriendotronGiftStatus.RecipientQueueFull =>
                        StatusCode(StatusCodes.Status409Conflict, new
                        {
                            success = false,
                            error = "friendotron_recipient_gift_queue_full"
                        }),
                    _ => StatusCode(
                        StatusCodes.Status500InternalServerError,
                        new
                        {
                            success = false,
                            error = "friendotron_delivery_failed"
                        })
                };
            }

            try
            {
                await NotiController.NotifyGiftAsync(
                    senderId.Value,
                    recipientId,
                    package);
            }
            catch (Exception exception)
            {

                Console.WriteLine(
                    $"[FRIENDOTRON PUSH ERROR] sender={senderId.Value} " +
                    $"recipient={recipientId} package={package.GiftPackageId} " +
                    $"error={exception.Message}");
            }

            Console.WriteLine(
                $"[FRIENDOTRON SENT] sender={senderId.Value} " +
                $"recipient={recipientId} reward={reward.FriendlyName} " +
                $"stars={reward.Stars} rarity={reward.Rarity} " +
                $"package={package.GiftPackageId} " +
                $"next={nextAvailableAtUtc:O}");

            return Ok();
        }

        [HttpPost("/api/avatar/v2/gifts/generate")]
        [HttpPost("/api/avatar/v3/gifts/generate")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> GenerateGiftPackage()
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            var player = PlayerDB.Players.FindById(playerId.Value);
            if (player?.Player == null)
                return NotFound();
            if (player.PlayerRoles?.Contains(PlayerRoles.Developer) != true)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    success = false,
                    error = "developer_only_legacy_reward"
                });
            }

            ParsedFreeGiftRequest parsed = await ReadFreeGiftRequestAsync();
            parsed.Message = parsed.Message.Trim();
            if (parsed.Message.Length > 200)
                return BadRequest(new { success = false, error = "gift_message_too_long" });

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.AvatarItems ??= new List<string>();

            HashSet<string> ownedItems =
                player.Player.PlayerExtra.AvatarItems.ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

            List<CatalogSku> availableGifts = GetCatalogSkus()
                .Where(IsUsableStoreItem)
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.AvatarItemDesc) &&
                    !ownedItems.Contains(item.AvatarItemDesc))
                .ToList();

            if (availableGifts.Count == 0)
            {
                return NotFound(new
                {
                    success = false,
                    error = "no_available_gifts"
                });
            }

            CatalogSku gift =
                availableGifts[Random.Shared.Next(availableGifts.Count)];

            GiftPackage? package = PlayerDB.QueueGiftPackage(
                playerId.Value,
                new GiftPackage
                {
                    FromPlayerId = 0,
                    Message = parsed.Message,
                    AvatarItemDesc = gift.AvatarItemDesc,
                    ConsumableItemDesc = gift.ConsumableItemDesc,
                    EquipmentPrefabName = gift.EquipmentPrefabName,
                    EquipmentModificationGuid =
                        gift.EquipmentModificationGuid,
                    CurrencyType = gift.CurrencyType,
                    GiftContext = parsed.GiftContext,
                    Rarity = gift.Rarity,
                    Platform = -1,
                    PlatformMask = gift.PlatformMask,
                    IsQuery = gift.GiftDrop.IsQuery,
                    Unique = gift.GiftDrop.Unique
                });

            if (package == null)
                return StatusCode(500);

            await NotiController.NotifyGiftAsync(
                senderPlayerId: 0,
                receiverPlayerId: playerId.Value,
                package);

            return Ok(package);
        }

        private sealed class FriendotronConsumableReward
        {
            public FriendotronConsumableReward(
                string friendlyName,
                string consumableItemDesc,
                int rarity)
            {
                FriendlyName = friendlyName;
                ConsumableItemDesc = consumableItemDesc;
                Rarity = rarity;
            }

            public string FriendlyName { get; }
            public string ConsumableItemDesc { get; }
            public int Rarity { get; }
            public int Stars => (Rarity / 10) + 1;
        }

        private sealed class ParsedFreeGiftRequest
        {
            public HashSet<long> RecipientIds { get; } = new();
            public string Message { get; set; } = string.Empty;
            public int GiftContext { get; set; }
        }

        private static readonly string[] FreeGiftRecipientKeys =
        {
    "ToPlayerIds",
    "toPlayerIds",
    "to_player_ids",
    "PlayerIds",
    "playerIds",
    "RecipientIds",
    "recipientIds",
    "AccountIds",
    "accountIds",
    "ToPlayerId",
    "toPlayerId",
    "PlayerId",
    "playerId",
    "RecipientId",
    "recipientId",
    "AccountId",
    "accountId",
    "Ids",
    "ids"
};

        private async Task<ParsedFreeGiftRequest> ReadFreeGiftRequestAsync()
        {
            var result = new ParsedFreeGiftRequest();

            foreach (string key in FreeGiftRecipientKeys)
            {
                foreach (string? value in Request.Query[key])
                    AddIdsFromText(value, result.RecipientIds);
            }

            result.Message =
                Request.Query["Message"].FirstOrDefault() ??
                Request.Query["message"].FirstOrDefault() ??
                string.Empty;

            if (int.TryParse(
                Request.Query["GiftContext"].FirstOrDefault() ??
                Request.Query["giftContext"].FirstOrDefault(),
                out int queryContext))
            {
                result.GiftContext = queryContext;
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();

                foreach (string key in FreeGiftRecipientKeys)
                {
                    foreach (string? value in form[key])
                        AddIdsFromText(value, result.RecipientIds);
                }

                result.Message =
                    form["Message"].FirstOrDefault() ??
                    form["message"].FirstOrDefault() ??
                    result.Message;

                if (int.TryParse(
                    form["GiftContext"].FirstOrDefault() ??
                    form["giftContext"].FirstOrDefault(),
                    out int formContext))
                {
                    result.GiftContext = formContext;
                }

                return result;
            }

            if (!Request.Body.CanRead)
                return result;

            using var reader = new StreamReader(
                Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            string body = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(body))
                return result;

            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                JsonElement root = document.RootElement;

                CollectRecipientIds(root, result.RecipientIds);

                result.Message =
                    FindJsonString(root, "Message", "message") ??
                    result.Message;

                result.GiftContext =
                    FindJsonInt(root, "GiftContext", "giftContext") ??
                    result.GiftContext;
            }
            catch (JsonException exception)
            {
                Console.WriteLine(
                    $"[FREE GIFTS] Invalid JSON: {exception.Message}");

                AddIdsFromText(body, result.RecipientIds);
            }

            return result;
        }

        private static void CollectRecipientIds(
            JsonElement element,
            HashSet<long> recipientIds)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (JsonElement child in element.EnumerateArray())
                        AddIdsFromJsonValue(child, recipientIds);
                    break;

                case JsonValueKind.Object:
                    bool foundKnownProperty = false;

                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (FreeGiftRecipientKeys.Any(key =>
                            string.Equals(
                                key,
                                property.Name,
                                StringComparison.OrdinalIgnoreCase)))
                        {
                            foundKnownProperty = true;
                            AddIdsFromJsonValue(property.Value, recipientIds);
                        }
                    }

                    if (!foundKnownProperty)
                    {
                        foreach (JsonProperty property in element.EnumerateObject())
                        {
                            if (property.Value.ValueKind is
                                JsonValueKind.Object or JsonValueKind.Array)
                            {
                                CollectRecipientIds(
                                    property.Value,
                                    recipientIds);
                            }
                        }
                    }

                    break;

                case JsonValueKind.Number:
                case JsonValueKind.String:
                    AddIdsFromJsonValue(element, recipientIds);
                    break;
            }
        }

        private static void AddIdsFromJsonValue(
            JsonElement value,
            HashSet<long> recipientIds)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                    if (value.TryGetInt64(out long number))
                        recipientIds.Add(number);
                    break;

                case JsonValueKind.String:
                    AddIdsFromText(value.GetString(), recipientIds);
                    break;

                case JsonValueKind.Array:
                    foreach (JsonElement child in value.EnumerateArray())
                        AddIdsFromJsonValue(child, recipientIds);
                    break;

                case JsonValueKind.Object:
                    CollectRecipientIds(value, recipientIds);
                    break;
            }
        }

        private static void AddIdsFromText(
            string? value,
            HashSet<long> recipientIds)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            string cleaned = value
                .Replace("[", " ")
                .Replace("]", " ")
                .Replace("{", " ")
                .Replace("}", " ")
                .Replace("\"", " ")
                .Replace("'", " ");

            string[] pieces = cleaned.Split(
                new[]
                {
            ',', ';', '|', ' ', '\t', '\r', '\n'
                },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            foreach (string piece in pieces)
            {
                string candidate = piece;

                int equalsIndex = candidate.LastIndexOf('=');
                if (equalsIndex >= 0)
                    candidate = candidate[(equalsIndex + 1)..];

                int colonIndex = candidate.LastIndexOf(':');
                if (colonIndex >= 0)
                    candidate = candidate[(colonIndex + 1)..];

                if (long.TryParse(candidate, out long playerId))
                    recipientIds.Add(playerId);
            }
        }

        [HttpGet("/api/relationships/v2/get")]
        [HttpGet("/api/relationships/")]
        [HttpGet("/api/relationships")]
        public IActionResult GetMyRelationships([FromQuery] long? id = null)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Ok(Array.Empty<ClientRelationshipDTO>());

            return Ok(RelationshipDB.GetClientRelationships(accountId.Value));
        }

        [HttpPost("/api/relationships/sendfriendintroductions")]
        public IActionResult SendFriendIntroductions()
        {

            if (AuthStuff.GetPlayerId(Request) == null)
                return Unauthorized();
            return Ok(new { success = true, introductions = Array.Empty<object>() });
        }

        public class AddFriendWithCodeRequest
        {
            public string? Code { get; set; }
        }

        [HttpPost("/api/relationships/v1/addfriendwithcode")]
        public IActionResult AddFriendWithCode([FromBody] AddFriendWithCodeRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            string username = (request.Code ?? string.Empty).Trim().TrimStart('@');
            if (username.Length == 0)
                return BadRequest(new { error = "Enter a friend code." });

            var target = PlayerDB.Players.FindAll().FirstOrDefault(value =>
                string.Equals(value.Player?.Username, username, StringComparison.OrdinalIgnoreCase));
            if (target?.Player == null)
                return NotFound(new { error = "No player found with that code." });
            if (target.PlayerId == accountId.Value)
                return BadRequest(new { error = "That's your own code." });

            RelationshipDB.AddFriend(accountId.Value, target.PlayerId);
            return Ok(RelationshipDB.GetClientRelationship(accountId.Value, target.PlayerId));
        }

        [HttpGet("/api/messages/v2/get")]
        public IActionResult GetMyMessages()
        {
            long? id = AuthStuff.GetPlayerId(Request);
            if (!id.HasValue)
                return Unauthorized();

            List<NotificationDB.ClientNotification> messages =
                NotificationDB.GetMessages(id.Value);
            Console.WriteLine(
                $"[MESSAGES GET] player={id.Value} count={messages.Count} " +
                $"types=[{string.Join(",", messages.Select(value => value.Type))}]");
            return Ok(messages);
        }

        [HttpPost("/api/messages/v2/send")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> SendPlayerMessage(
            [FromForm] long ToPlayerId,
            [FromForm] int Type,
            [FromForm] string? Data,
            [FromForm] long? RoomId)
        {
            long? senderPlayerId = AuthStuff.GetPlayerId(Request);
            if (!senderPlayerId.HasValue)
                return Unauthorized();
            if (ToPlayerId <= 0 ||
                ToPlayerId == senderPlayerId.Value ||
                PlayerDB.Players.FindById(ToPlayerId)?.Player == null)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "invalid_message_recipient"
                });
            }

            var recipientRelationship =
                RelationshipDB.GetClientRelationship(
                    ToPlayerId,
                    senderPlayerId.Value);
            if ((Type is
                     (int)NotificationDB.MessageType.GameInvite or
                     (int)NotificationDB.MessageType.GameInviteV2) &&
                (recipientRelationship?.Ignored is 1 or 3))
            {
                return StatusCode(403, new
                {
                    success = false,
                    error = "gameplay_invites_not_allowed"
                });
            }

            NotificationDB.ClientNotification message;
            if (Type is
                (int)NotificationDB.MessageType.GameInvite or
                (int)NotificationDB.MessageType.GameInviteV2)
            {
                RoomInstance? instance = PlayerDB
                    .GetPlayerHeartbeat(senderPlayerId.Value)
                    .roomInstance;
                if (instance == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "sender_not_in_room"
                    });
                }

                if (!Sessions.IsConfirmedParticipant(
                        senderPlayerId.Value,
                        instance.roomInstanceId))
                {
                    return StatusCode(409, new
                    {
                        success = false,
                        error = "room_instance_membership_not_confirmed"
                    });
                }

                if (RoomId.HasValue && RoomId.Value > 0 &&
                    RoomId.Value != instance.roomId)
                {
                    return StatusCode(409, new
                    {
                        success = false,
                        error = "room_invite_source_mismatch",
                        requestedRoomId = RoomId.Value,
                        currentRoomId = instance.roomId
                    });
                }

                message = await NotiController.NotifyRoomInviteAsync(
                    senderPlayerId.Value,
                    ToPlayerId,
                    instance.roomId,
                    instance.roomInstanceId,
                    instance.photonRoomId);
            }
            else
            {
                NotificationDB.MessageType messageType =
                    Enum.IsDefined(typeof(NotificationDB.MessageType), Type)
                        ? (NotificationDB.MessageType)Type
                        : NotificationDB.MessageType.TextMessage;

                message = NotificationDB.CreatePlayerMessage(
                    senderPlayerId.Value,
                    ToPlayerId,
                    messageType,
                    Data,
                    RoomId);

                await NotiController.NotifyMessageAsync(ToPlayerId, message);
            }

            Console.WriteLine(
                $"[MESSAGES SEND] from={senderPlayerId.Value} " +
                $"to={ToPlayerId} type={Type} room={RoomId?.ToString() ?? "none"} " +
                $"messageId={message.Id}");

            return NoContent();
        }

        [HttpPost("/api/messages/v2/delete")]
        [HttpDelete("/api/messages/v2/delete")]
        [HttpPost("/api/messages/v3/delete")]
        [HttpDelete("/api/messages/v3/delete")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> DeleteMyMessages()
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            HashSet<long> ids = await ReadLongValuesAsync(
                "MessageIds", "messageIds", "MessageId", "messageId", "id");
            int acceptedCheers = PlayerDB.ResolvePlayerCheers(
                playerId.Value,
                ids,
                CheerStatus.Accepted);
            int deleted = NotificationDB.DeleteMessages(playerId.Value, ids);
            return Ok(new
            {
                success = true,
                deleted,
                acceptedCheers
            });
        }

        [HttpPost("/api/messages/v1/sendMultiple")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> SendMultipleMessages()
        {
            long? senderPlayerId = AuthStuff.GetPlayerId(Request);
            if (!senderPlayerId.HasValue)
                return Unauthorized();

            HashSet<long> recipientIds = await ReadLongValuesAsync(
                "ToPlayerIds",
                "toPlayerIds",
                "RecipientPlayerIds",
                "recipientPlayerIds",
                "RecipientIds",
                "recipientIds",
                "PlayerIds",
                "playerIds",
                "ToAccountIds",
                "toAccountIds");

            HashSet<long> aboutIds = await ReadLongValuesAsync(
                "AboutPlayerId",
                "aboutPlayerId",
                "AboutAccountId",
                "aboutAccountId");

            long aboutPlayerId = aboutIds.FirstOrDefault();
            if (aboutPlayerId > 0 &&
                PlayerDB.Players.FindById(aboutPlayerId)?.Player == null)
            {
                Console.WriteLine(
                    $"[MESSAGES SEND MULTIPLE] sender={senderPlayerId.Value} " +
                    $"about={aboutPlayerId} rejected=about_player_not_found");
                return NotFound(new
                {
                    success = false,
                    error = "about_player_not_found"
                });
            }

            long[] validRecipients = recipientIds
                .Where(id =>
                    id > 0 &&
                    id <= int.MaxValue &&
                    id != senderPlayerId.Value &&
                    (aboutPlayerId <= 0 || id != aboutPlayerId) &&
                    PlayerDB.Players.FindById(id)?.Player != null)
                .Distinct()
                .Take(50)
                .ToArray();

            if (validRecipients.Length == 0)
            {
                Console.WriteLine(
                    $"[MESSAGES SEND MULTIPLE] sender={senderPlayerId.Value} " +
                    $"about={aboutPlayerId} rejected=no_valid_recipients");
                return BadRequest(new
                {
                    success = false,
                    error = "missing_recipient_ids"
                });
            }

            if (aboutPlayerId > 0)
            {
                foreach (long receiverPlayerId in validRecipients)
                {
                    await NotiController.NotifyFriendIntroductionAsync(
                        senderPlayerId.Value,
                        receiverPlayerId,
                        aboutPlayerId);
                }

                Console.WriteLine(
                    $"[MESSAGES SEND MULTIPLE] sender={senderPlayerId.Value} " +
                    $"type={(int)NotificationDB.MessageType.FriendIntroduction} " +
                    $"about={aboutPlayerId} recipients=" +
                    $"[{string.Join(",", validRecipients)}] " +
                    $"sent={validRecipients.Length}");

                return Ok();
            }

            (int type, string data) = await ReadMultipleMessagePayloadAsync();
            NotificationDB.MessageType messageType =
                Enum.IsDefined(typeof(NotificationDB.MessageType), type)
                    ? (NotificationDB.MessageType)type
                    : NotificationDB.MessageType.TextMessage;

            bool isGameInvite = messageType is
                NotificationDB.MessageType.GameInvite or
                NotificationDB.MessageType.GameInviteV2;
            RoomInstance? inviteInstance = null;
            if (isGameInvite)
            {
                inviteInstance = PlayerDB
                    .GetPlayerHeartbeat(senderPlayerId.Value)
                    .roomInstance;
                if (inviteInstance == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "sender_not_in_room"
                    });
                }

                if (!Sessions.IsConfirmedParticipant(
                        senderPlayerId.Value,
                        inviteInstance.roomInstanceId))
                {
                    return StatusCode(409, new
                    {
                        success = false,
                        error = "room_instance_membership_not_confirmed"
                    });
                }
            }

            var deliveredRecipients = new List<long>();
            var blockedRecipients = new List<long>();
            foreach (long receiverPlayerId in validRecipients)
            {
                if (isGameInvite)
                {
                    var relationship = RelationshipDB.GetClientRelationship(
                        receiverPlayerId,
                        senderPlayerId.Value);
                    if (relationship?.Ignored is 1 or 3)
                    {
                        blockedRecipients.Add(receiverPlayerId);
                        continue;
                    }

                    await NotiController.NotifyRoomInviteAsync(
                        senderPlayerId.Value,
                        receiverPlayerId,
                        inviteInstance!.roomId,
                        inviteInstance.roomInstanceId,
                        inviteInstance.photonRoomId);
                    deliveredRecipients.Add(receiverPlayerId);
                }
                else
                {
                    NotificationDB.ClientNotification message =
                        NotificationDB.CreatePlayerMessage(
                        senderPlayerId.Value,
                        receiverPlayerId,
                        messageType,
                        data);

                    await NotiController.NotifyMessageAsync(
                        receiverPlayerId,
                        message);
                    deliveredRecipients.Add(receiverPlayerId);
                }
            }

            Console.WriteLine(
                $"[MESSAGES SEND MULTIPLE] sender={senderPlayerId.Value} " +
                $"type={(int)messageType} recipients=" +
                $"[{string.Join(",", deliveredRecipients)}] " +
                $"sent={deliveredRecipients.Count} " +
                $"blocked={blockedRecipients.Count}");

            return Ok();
        }

        private async Task<(int Type, string Data)>
            ReadMultipleMessagePayloadAsync()
        {
            string? rawType =
                Request.Query["Type"].FirstOrDefault() ??
                Request.Query["type"].FirstOrDefault() ??
                Request.Query["MessageType"].FirstOrDefault() ??
                Request.Query["messageType"].FirstOrDefault();
            string data =
                Request.Query["Data"].FirstOrDefault() ??
                Request.Query["data"].FirstOrDefault() ??
                string.Empty;

            if (Request.HasFormContentType)
            {
                IFormCollection form =
                    await Request.ReadFormAsync(HttpContext.RequestAborted);
                rawType =
                    form["Type"].FirstOrDefault() ??
                    form["type"].FirstOrDefault() ??
                    form["MessageType"].FirstOrDefault() ??
                    form["messageType"].FirstOrDefault() ??
                    rawType;
                data =
                    form["Data"].FirstOrDefault() ??
                    form["data"].FirstOrDefault() ??
                    data;
            }
            else if (Request.Body.CanRead)
            {
                Request.EnableBuffering();
                if (Request.Body.CanSeek)
                    Request.Body.Position = 0;

                using var reader = new StreamReader(
                    Request.Body,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);
                string body = await reader.ReadToEndAsync();
                if (Request.Body.CanSeek)
                    Request.Body.Position = 0;

                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(body);
                        JsonElement root = document.RootElement;
                        rawType =
                            FindJsonInt(
                                root,
                                "Type",
                                "type",
                                "MessageType",
                                "messageType")?
                            .ToString() ??
                            rawType;
                        data =
                            FindJsonString(root, "Data", "data") ??
                            data;
                    }
                    catch (JsonException)
                    {

                    }
                }
            }

            return (
                int.TryParse(rawType, out int type)
                    ? type
                    : (int)NotificationDB.MessageType.TextMessage,
                data);
        }

        [HttpGet("/playersettings")]
        public IActionResult GetMySettings()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized();

            return Ok(player.Player.PlayerExtra.Settings);
        }

        [HttpPut("/playersettings")]
        [RequestSizeLimit(16 * 1024)]
        public IActionResult SetMySettings([FromForm] string key, [FromForm] string value)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            PlayerDB.SetPlayerSetting(key, value ?? "", (long)id);

            return NoContent();
        }

        [HttpGet("/econ/customAvatarItems/v1/owned")]
        public IActionResult GetMyOwnedCustomAvatarItems([FromQuery] int skip, [FromQuery] int take)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            skip = Math.Max(0, skip);
            take = Math.Clamp(take <= 0 ? 100 : take, 1, 100);

            List<JsonObject> items = LoadCustomAvatarItems(accountId.Value);
            List<JsonObject> page = items.Skip(skip).Take(take).ToList();

            return Ok(new
            {
                Results = page,
                TotalResults = items.Count
            });
        }

        [HttpGet("/api/checklist/v1/current")]
        public IActionResult GetMyChecklist()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/api/players/v2/progression/bulk")]
        public IActionResult GetProgressionForPlayers([FromQuery] List<long> id)
        {
            var authId = AuthStuff.GetPlayerId(Request);
            if (authId == null)
                return Unauthorized();

            if (id == null || id.Count == 0)
                return Ok(new List<PlayerDBClasses.PlayerProgressionDTO>());

            var progressions = PlayerDB.GetProgressionBulk(id);

            return Ok(progressions);
        }

        [HttpPost("/api/players/v2/progression/update")]
        [HttpPost("/api/players/v1/progression/update")]
        [HttpPost("/api/progression/v1/update")]
        [RequestSizeLimit(32 * 1024)]
        public async Task<IActionResult> UpdateMyProgression()
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            var current = PlayerDB.Players.FindById(playerId.Value);
            if (current?.Player == null)
                return NotFound(new { success = false, error = "player_not_found" });
            if (current.PlayerRoles?.Contains(PlayerRoles.Developer) != true)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    success = false,
                    error = "server_authoritative_progression"
                });
            }

            var values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var queryValue in Request.Query)
                values[queryValue.Key] = queryValue.Value.ToString();

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (var formValue in form)
                    values[formValue.Key] = formValue.Value.ToString();
            }
            else if ((Request.ContentLength ?? 0) > 0)
            {
                using var reader = new StreamReader(Request.Body);
                string body = await reader.ReadToEndAsync();

                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(body);
                        AddJsonValues(document.RootElement, values);
                    }
                    catch (JsonException)
                    {
                        foreach (var bodyValue in
                            Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
                                "?" + body))
                        {
                            values[bodyValue.Key] = bodyValue.Value.ToString();
                        }
                    }
                }
            }

            int level = current.Player.Level;
            int xp = current.Player.XP;

            string? levelText = GetValue(values,
                "Level", "level", "NewLevel", "newLevel");
            string? xpText = GetValue(values,
                "XP", "Xp", "xp", "Experience", "experience", "NewXP", "newXp");
            string? xpDeltaText = GetValue(values,
                "XPDelta", "xpDelta", "ExperienceDelta", "experienceDelta", "AddXP", "addXp");

            if (int.TryParse(levelText, out int parsedLevel))
                level = parsedLevel;
            if (int.TryParse(xpText, out int parsedXp))
                xp = parsedXp;
            if (int.TryParse(xpDeltaText, out int xpDelta))
                xp = Math.Max(0, xp + xpDelta);

            var progression = PlayerDB.SetProgression(
                playerId.Value,
                level,
                xp);

            return progression == null
                ? StatusCode(500, new { success = false, error = "progression_update_failed" })
                : Ok(progression);
        }

        [HttpGet("/api/playerReputation/v2/bulk")]
        public IActionResult GetReputationBulk([FromQuery] List<long> id)
        {
            var authId = AuthStuff.GetPlayerId(Request);
            if (authId == null)
                return Unauthorized();

            if (id == null || id.Count == 0)
                return Ok(new List<PlayerDBClasses.Reputation>());

            var results = PlayerDB.GetReputationBulk(id);
            return Ok(results);
        }

        [HttpPost("/api/PlayerReporting/v1/hile")]
        public IActionResult PlayerReportingHile()
        {
            var authId = AuthStuff.GetPlayerId(Request);
            if (authId == null)
                return Unauthorized();

            return Ok(false);
        }

        [HttpGet("api/config/v2")]
        public IActionResult GetConfigV2()
        {
            string path = Path.Combine(Program.dataDir, "APIS", "ConfigV2.json");
            return System.IO.File.Exists(path) ? Content(System.IO.File.ReadAllText(path), "application/json") : NotFound();
        }

        [HttpGet("/api/avatar/v3/saved")]
        public IActionResult GetSavedAvatars()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player?.Player == null)
                return Unauthorized();

            player.Player.PlayerExtra ??= new PlayerDBClasses.PlayerExtra();
            player.Player.PlayerExtra.SavedAvatars ??= new List<PlayerDBClasses.SavedOutfit>();
            var saved = player.Player.PlayerExtra.SavedAvatars;

            var photoEntries = saved.Where(s => !HasSavedOutfitState(s)).ToList();
            foreach (var photoEntry in photoEntries)
                MigrateLegacyPhotoEntry(player, photoEntry);

            if (photoEntries.Count > 0)
            {
                foreach (var photoEntry in photoEntries)
                    saved.Remove(photoEntry);
                PlayerDB.Players.Update(player);
            }

            var result = saved
                .OrderBy(s => s.Slot)
                .Select(s => new
                {
                    Slot = s.Slot,
                    OutfitSelections = s.OutfitSelections,
                    FaceFeatures = s.FaceFeatures,
                    SkinColor = s.SkinColor,
                    HairColor = s.HairColor,
                    PreviewImageName = s.PreviewImageName
                }).ToList();

            return Ok(result);
        }

        [HttpPost("/api/avatar/v3/saved/set")]
        [HttpPost("/api/avatar/v4/saved/set")]
        [RequestSizeLimit(256 * 1024)]
        public IActionResult SetSavedAvatar([FromBody] PlayerDBClasses.SavedOutfit request)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player?.Player == null)
                return Unauthorized();

            if (request == null)
                return BadRequest(new { success = false, error = "Invalid request body." });

            if (request.Slot > 350)
                return BadRequest(new { success = false, error = "Invalid outfit slot." });

            if (!HasSavedOutfitState(request))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "An outfit save must include avatar data."
                });
            }

            if ((request.OutfitSelections?.Length ?? 0) > 64_000 ||
                (request.FaceFeatures?.Length ?? 0) > 64_000 ||
                (request.SkinColor?.Length ?? 0) > 1_024 ||
                (request.HairColor?.Length ?? 0) > 1_024 ||
                request.PreviewImageName?.Length > 260)
            {
                return BadRequest(new { success = false, error = "Outfit data is too large." });
            }

            player.Player.PlayerExtra ??= new PlayerDBClasses.PlayerExtra();
            player.Player.PlayerExtra.SavedAvatars ??= new List<PlayerDBClasses.SavedOutfit>();

            if (request.Slot < 0)
            {
                request.Slot = Enumerable.Range(0, 100).FirstOrDefault(slot =>
                    player.Player.PlayerExtra.SavedAvatars.All(saved => saved.Slot != slot), -1);
                if (request.Slot < 0)
                    return Conflict(new { success = false, error = "All saved outfit slots are in use." });
            }

            var existing = player.Player.PlayerExtra.SavedAvatars.FirstOrDefault(x => x.Slot == request.Slot);
            if (existing != null)
            {
                existing.OutfitSelections = request.OutfitSelections;
                existing.FaceFeatures = request.FaceFeatures;
                existing.SkinColor = request.SkinColor;
                existing.HairColor = request.HairColor;
                existing.PreviewImageName = request.PreviewImageName;
            }
            else
            {
                player.Player.PlayerExtra.SavedAvatars.Add(request);
            }

            PlayerDB.Players.Update(player);

            return Ok(new
            {
                success = true,
                slot = request.Slot,
                previewImageName = request.PreviewImageName,
                url = request.PreviewImageName != null ? $"{ServerConfig.BaseURL}/imageserver-v2/{request.PreviewImageName}" : null
            });
        }

        private static bool HasSavedOutfitState(PlayerDBClasses.SavedOutfit outfit)
        {
            return !string.IsNullOrWhiteSpace(outfit.OutfitSelections) ||
                   !string.IsNullOrWhiteSpace(outfit.FaceFeatures) ||
                   !string.IsNullOrWhiteSpace(outfit.SkinColor) ||
                   !string.IsNullOrWhiteSpace(outfit.HairColor);
        }

        private static void MigrateLegacyPhotoEntry(
            PlayerDBClasses.FullPlayer player,
            PlayerDBClasses.SavedOutfit entry)
        {
            string? path = NormalizeLegacyImagePath(entry.PreviewImageName);
            if (path == null)
                return;

            string? profilePath = NormalizeLegacyImagePath(player.Player?.ProfileImage);
            if (string.Equals(path, profilePath, StringComparison.OrdinalIgnoreCase))
                return;

            string fullPath = Path.Combine(
                Program.dataDir,
                "Images",
                path.Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(fullPath))
                return;

            RecNetDB.SavedImages.Upsert(new RecNetDB.SavedImage
            {
                PhotoPath = path,
                AccountId = player.PlayerId,
                SavedImageType = 1,
                Accessibility = 1,
                CreatedAt = System.IO.File.GetCreationTimeUtc(fullPath)
            });
        }

        private static string? NormalizeLegacyImagePath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string clean = value.Trim().Replace('\\', '/');
            if (Uri.TryCreate(clean, UriKind.Absolute, out var uri))
            {
                string? marker = new[] { "/imageserver-v2/", "/imageserver/" }
                    .FirstOrDefault(candidate => uri.AbsolutePath.Contains(
                        candidate,
                        StringComparison.OrdinalIgnoreCase));
                if (marker == null)
                    return null;
                int markerIndex = uri.AbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                clean = Uri.UnescapeDataString(uri.AbsolutePath[(markerIndex + marker.Length)..]);
            }

            clean = clean.TrimStart('/');
            if (!clean.Contains('/'))
                clean = $"PlayerImages/{Path.GetFileName(clean)}";

            string[] segments = clean.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (clean.Length > 260 || segments.Length == 0 ||
                clean.Any(char.IsControl) || clean.Contains(':') ||
                Path.IsPathRooted(clean) || segments.Any(segment => segment is "." or ".."))
            {
                return null;
            }

            string extension = Path.GetExtension(clean);
            if (!new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" }
                    .Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            return string.Join('/', segments);
        }

        [HttpGet("/api/PlayerReporting/v1/voteToKickReasons")]
        [HttpGet("/api/images/v2/named")]
        [HttpGet("/api/gamerewards/v1/pending")]
        [HttpGet("/api/roomkeys/v1/mine")]
        [HttpGet("/api/roomcurrencies/v1/currencies")]
        [HttpGet("/api/AppIntegrity/v1/iosproducts")]
        [HttpGet("/api/externalfriendinvite/v1/gettextmessagereferrers")]
        [HttpGet("/api/images/")]
        [HttpGet("/api/images")]
        [HttpGet("/api/incentivizedreferrals/")]
        [HttpGet("/api/incentivizedreferrals")]
        [HttpGet("/api/inventions/")]
        [HttpGet("/api/inventions")]
        [HttpGet("/api/inventions/v1/versions")]
        [HttpGet("/api/keepsakes")]
        [HttpGet("/api/keepsakes/events")]
        [HttpGet("/api/progressionEvents")]
        [HttpGet("/api/roomEarningsDistributions")]
        [HttpGet("/api/roomconsumables")]
        [HttpGet("/api/roomcurrencies")]
        [HttpGet("/api/roomkeys/")]
        [HttpGet("/api/roomkeys/v1/")]
        [HttpGet("/api/storefronts/")]
        [HttpGet("/api/storefronts")]
        [HttpGet("/api/storefronts/v1/objectives")]
        [HttpGet("/api/testcasemanagement/")]
        [HttpGet("/api/testcasemanagement/v1/testcase/")]
        [HttpGet("/api/testcasemanagement/v1/testplans")]
        [HttpGet("/api/testcasemanagement/v1/testpasssummary")]
        [HttpGet("/api/ugcPurchasables")]
        [HttpGet("/api/consumables/")]
        [HttpGet("/api/consumables")]
        public IActionResult TodoImplement()
        {
            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/api/CampusCard/PS5RecRoomPlusEnabledForAllPlayers")]
        [HttpGet("/api/apple/musicpromotion/active")]
        [HttpGet("/api/playstationplus/membership")]
        [HttpGet("/api/roomkeys/v1/owns")]
        public IActionResult TodoImplementFalse() => Ok(false);

        [HttpGet("/api/roomcurrencies/v1/getBalance")]
        [HttpGet("/api/storefronts/v1/trialInvention/duration")]
        public IActionResult TodoImplementZero() => Ok(0);

        [HttpGet("/api/apple/musicpromotion/code")]
        [HttpGet("/api/inventions/v1/fulllineageowner")]
        [HttpGet("/api/royale/v1/current")]
        public IActionResult TodoImplementNull() => Ok(null);

        [HttpPost("/api/AppIntegrity/v1/iospaymentqueuefailed")]
        [HttpPost("/api/PlayerReporting/v1/instantKick")]
        [HttpPost("/api/PlayerReporting/v3/voteToKick")]
        [HttpPost("/api/checklist/v1/complete")]
        [HttpPost("/api/externalfriendinvite/v1/createplatforminvite")]
        [HttpPost("/api/externalfriendinvite/v1/sendtextmessageinvite")]
        [HttpPost("/api/gamerewards/v1/select")]
        [HttpPost("/api/incentivizedreferrals/claim")]
        [HttpPost("/api/inventions/v1/delete")]
        [HttpDelete("/api/inventions/v1/delete")]
        [HttpPost("/api/inventions/v1/dormskinsfromids")]
        [HttpPost("/api/inventions/v1/unpublish")]
        [HttpPost("/api/inventions/v1/updateprice")]
        [HttpPost("/api/inventions/v3/publish")]
        [HttpPost("/api/inventions/v4/addversion")]
        [HttpPost("/api/objectives/v1/completegroup")]
        [HttpPost("/api/objectives/v1/updateobjective")]
        [HttpPost("/api/playstationplus/expire")]
        [HttpPost("/api/roomconsumables/v1/roomConsumable/awardBulk")]
        [HttpPost("/api/roomcurrencies/v1/awardCurrency/bulk")]
        [HttpPost("/api/roomcurrencies/v1/createCurrency")]
        [HttpPost("/api/roomcurrencies/v1/createPurchaseOffer")]
        [HttpPost("/api/roomcurrencies/v1/deletePurchaseOffer")]
        [HttpPost("/api/roomcurrencies/v1/updateCurrency")]
        [HttpPost("/api/roomcurrencies/v1/updatePurchaseOffer")]
        [HttpPost("/api/roomkeys/v1/awardbulk")]
        [HttpPost("/api/roomkeys/v1/create")]
        [HttpPost("/api/roomkeys/v1/revoke")]
        [HttpPost("/api/royale/v2/matchcomplete")]
        [HttpPost("/api/storefronts/v1/PurchaseRoomKeyWithCurrency")]
        [HttpPost("/api/storefronts/v1/buyForFreeGiftButton")]
        [HttpPost("/api/storefronts/v1/buyProgressionEventXpBoost")]
        [HttpPost("/api/storefronts/v1/buyPurchaseReminder")]
        [HttpPost("/api/storefronts/v1/buyRoomKey")]
        [HttpPost("/api/storefronts/v1/trialInvention")]
        [HttpPost("/api/storefronts/v2/buyElite")]
        [HttpPost("/api/storefronts/v2/buyTier")]
        [HttpPost("/api/roomconsumables/v1/roomconsumable/{skuId}/purchase/currency")]
        public IActionResult AckOnly()
        {
            if (AuthStuff.GetPlayerId(Request) == null)
                return Unauthorized();
            return Ok(new { success = true });
        }

        [HttpGet("/api/roomcurrencies/v1/getPurchaseOffersBatch")]
        [HttpPost("/api/roomcurrencies/v1/getPurchaseOffersBatch")]
        [HttpPost("/api/roomkeys/v1/owns/bulk")]
        public IActionResult AckOnlyEmptyList()
        {
            if (AuthStuff.GetPlayerId(Request) == null)
                return Unauthorized();
            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/api/avatar/v2/gifts")]
        public IActionResult GetPendingGiftPackages()
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            List<GiftPackage> pendingGifts =
                PlayerDB.GetPendingGiftPackages(playerId.Value);
            object[] gifts = pendingGifts
                .Select(gift => ToClientGiftPackage(gift, playerId.Value))
                .ToArray();
            string packageSummary = string.Join(",", pendingGifts.Select(gift =>
                $"{gift.GiftPackageId}:from{NormalizeGiftSenderId(gift.FromPlayerId, playerId.Value)}:" +
                GiftKind(gift)));

            Console.WriteLine(
                $"[GIFTS] pending player={playerId.Value} count={gifts.Length} " +
                $"packages=[{packageSummary}]");
            return Ok(gifts);
        }

        [HttpPost("/api/avatar/v2/gifts/consume")]
        [HttpPost("/api/avatar/v2/gifts/consume/")]
        [RequestSizeLimit(32 * 1024)]
        public async Task<IActionResult> ConsumeGiftPackage()
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            long? giftPackageId = await ReadGiftPackageIdAsync();
            if (!giftPackageId.HasValue || giftPackageId.Value <= 0)
            {
                return BadRequest(new
                {
                    Success = false,
                    Error = "missing_gift_package_id"
                });
            }

            GiftPackage? consumed = PlayerDB.ConsumeGiftPackage(
                playerId.Value,
                giftPackageId.Value);

            if (consumed == null)
            {
                return NotFound(new
                {
                    Success = false,
                    Error = "gift_package_not_found"
                });
            }

            List<GiftPackage> remaining =
                PlayerDB.GetPendingGiftPackages(playerId.Value);
            await NotiController.NotifyGiftConsumedAsync(
                playerId.Value,
                consumed.GiftPackageId,
                remaining);

            Console.WriteLine(
                $"[GIFTS] consumed player={playerId.Value} " +
                $"gift={consumed.GiftPackageId} remaining={remaining.Count}");

            return Ok(ToClientGiftPackage(consumed, playerId.Value));
        }

        private static object ToClientGiftPackage(
            GiftPackage gift,
            long recipientPlayerId)
        {
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
                FromPlayerId = NormalizeGiftSenderId(
                    gift.FromPlayerId,
                    recipientPlayerId),
                GiftContext = NormalizeGiftContext(gift.GiftContext),
                GiftRarity = gift.Rarity,
                Id = gift.GiftPackageId,
                Level = 0,
                Message = gift.Message ?? string.Empty,
                Platform = gift.Platform,
                PlatformsToSpawnOn = gift.PlatformMask,
                Xp = gift.XP
            };
        }

        private static int NormalizeGiftSenderId(
            int senderId,
            long recipientPlayerId)
        {

            if (senderId > 0 && senderId == recipientPlayerId)
                return recipientPlayerId == 1 ? 2 : 1;
            return senderId;
        }

        private static int NormalizeGiftContext(int context) =>
            context is (int)PlayerDBClasses.GiftContext.Default
                or (int)PlayerDBClasses.GiftContext.Store_RecCenter
                ? (int)PlayerDBClasses.GiftContext.Game_Drop
                : context;

        private static string GiftKind(GiftPackage gift) =>
            gift.Currency != 0 ? "tokens" :
            gift.XP > 0 ? "xp" :
            !string.IsNullOrWhiteSpace(gift.ConsumableItemDesc) ? "consumable" :
            !string.IsNullOrWhiteSpace(gift.EquipmentModificationGuid) ? "equipment" :
            !string.IsNullOrWhiteSpace(gift.AvatarItemDesc) ? "avatar" :
            "empty";

        private static string EmptyIfWhiteSpace(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value;

        private async Task<long?> ReadGiftPackageIdAsync()
        {
            string[] names =
            {
                "GiftPackageId",
                "giftPackageId",
                "PackageId",
                "packageId",
                "Id",
                "id"
            };

            foreach (string name in names)
            {
                if (long.TryParse(
                        Request.Query[name].FirstOrDefault(),
                        out long queryValue))
                {
                    return queryValue;
                }
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (string name in names)
                {
                    if (long.TryParse(
                            form[name].FirstOrDefault(),
                            out long formValue))
                    {
                        return formValue;
                    }
                }

                return null;
            }

            if (!Request.Body.CanRead ||
                ((Request.ContentLength ?? 0) <= 0 &&
                 Request.Headers.TransferEncoding.Count == 0))
            {
                return null;
            }

            try
            {
                using JsonDocument document =
                    await JsonDocument.ParseAsync(Request.Body);
                return FindJsonLong(document.RootElement, names);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        [HttpGet("/api/consumables/v2/getUnlocked")]
        public IActionResult GetUnlockedConsumables()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";

            PlayerInventoryStore.EnsureConsumablesInitialized(accountId.Value);
            return GetNormalizedConsumables(accountId.Value);
        }

        [HttpGet("/cdn/config/LoadingScreenTipData")]
        public IActionResult GetLoadingScreenTipData()
        {
            return GetLoadingScreenData();
        }

        [HttpGet("/api/playerevents/v1")]
        [HttpGet("/api/playerevents/v1/all")]
        public IActionResult GetAllPlayerEvents()
        {
            return Ok(new
            {
                Created = Array.Empty<object>(),
                Responses = Array.Empty<object>()
            });
        }

        [HttpGet("/api/customAvatarItems/v1/isCreationAllowedForAccount")]
        public IActionResult GetCustomAvatarItemsIsCreationAllowedForAccount()
        {
            return Ok(new
            {
                success = true,
                value = (object?)null
            });
        }

        [HttpGet("/api/influencerpartnerprogram/myinfluencer")]
        [HttpGet("/api/influencerpartnerprogram/v1/myinfluencer")]
        public IActionResult GetMySupportedInfluencer()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            FullPlayer? account = PlayerDB.Players.FindById(accountId.Value);
            int supportedInfluencerId = account?.Player?.SupportedInfluencerId ?? 0;

            Console.WriteLine(
                $"[INFLUENCER MY] account={accountId.Value} " +
                $"supported={supportedInfluencerId}");

            return Ok(supportedInfluencerId);
        }

        [HttpGet("/api/influencerpartnerprogram/influencer")]
        [HttpGet("/api/influencerpartnerprogram/v1/influencer")]
        public IActionResult GetSupportedInfluencer(
            [FromQuery] int accountId)
        {
            if (accountId <= 0)
                return Content("null", "application/json");

            FullPlayer? account = PlayerDB.Players.FindById((long)accountId);
            int supportedInfluencerId = account?.Player?.SupportedInfluencerId ?? 0;

            Console.WriteLine(
                $"[INFLUENCER GET] account={accountId} " +
                $"supported={supportedInfluencerId}");

            return Ok(supportedInfluencerId);
        }

        [HttpGet("/api/influencerpartnerprogram/isinfluencer")]
        [HttpGet("/api/influencerpartnerprogram/accountisinfluencer")]
        [HttpGet("/api/influencerpartnerprogram/v1/isinfluencer")]
        public IActionResult GetAccountIsInfluencer(
            [FromQuery] int accountId)
        {
            if (accountId <= 0)
                return Ok(false);

            FullPlayer? account = PlayerDB.Players.FindById((long)accountId);
            bool isInfluencer = account?.PlayerRoles?.Contains(
                PlayerDBClasses.PlayerRoles.Influencer) == true;

            Console.WriteLine(
                $"[INFLUENCER CHECK] account={accountId} " +
                $"isInfluencer={isInfluencer}");

            return Ok(isInfluencer);
        }

        [HttpPost("/api/influencerpartnerprogram/support")]
        [HttpPost("/api/influencerpartnerprogram/support/{influencerAccountId:int}")]
        public async Task<IActionResult> SupportInfluencer(
            [FromRoute] int? influencerAccountId = null)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            int? requestedInfluencerId =
                await ReadInfluencerAccountIdAsync(influencerAccountId);

            if (!requestedInfluencerId.HasValue)
            {
                return BadRequest(new
                {
                    error = "missing_influencer_account_id"
                });
            }

            if (requestedInfluencerId.Value == 0)
                return SetSupportedInfluencer(accountId.Value, 0);

            if (requestedInfluencerId.Value < 0)
            {
                return BadRequest(new
                {
                    error = "invalid_influencer_account_id"
                });
            }

            if (accountId.Value == requestedInfluencerId.Value)
            {
                return BadRequest(new
                {
                    error = "cannot_support_self"
                });
            }

            FullPlayer? influencer =
                PlayerDB.Players.FindById((long)requestedInfluencerId.Value);

            if (influencer?.Player == null ||
                influencer.PlayerRoles?.Contains(
                    PlayerDBClasses.PlayerRoles.Influencer) != true)
            {
                return NotFound(new
                {
                    error = "influencer_not_found"
                });
            }

            return SetSupportedInfluencer(
                accountId.Value,
                requestedInfluencerId.Value);
        }

        [HttpDelete("/api/influencerpartnerprogram/support")]
        [HttpDelete("/api/influencerpartnerprogram/support/{influencerAccountId:int}")]
        [HttpPost("/api/influencerpartnerprogram/unsupport")]
        [HttpPost("/api/influencerpartnerprogram/remove")]
        public IActionResult RemoveSupportedInfluencer(
            [FromRoute] int? influencerAccountId = null)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            return SetSupportedInfluencer(accountId.Value, 0);
        }

        private IActionResult SetSupportedInfluencer(
            long accountId,
            int supportedInfluencerId)
        {
            FullPlayer? account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "account_not_found" });

            account.Player.SupportedInfluencerId = supportedInfluencerId;

            if (!PlayerDB.Players.Update(account))
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "support_update_failed" });
            }

            Console.WriteLine(
                $"[INFLUENCER SUPPORT] supporter={accountId} " +
                $"influencer={supportedInfluencerId} persisted=true");

            return Ok(new
            {
                Value = true,
                Success = true,
                ErrorId = (string?)null,
                Error = (string?)null,
                LocalizationContext = (object?)null,
                InfluencerAccountId = supportedInfluencerId,
                SupportedInfluencerId = supportedInfluencerId
            });
        }

        private async Task<int?> ReadInfluencerAccountIdAsync(
            int? routeInfluencerAccountId)
        {
            if (routeInfluencerAccountId.HasValue)
                return routeInfluencerAccountId.Value;

            string[] names =
            {
                "influencerAccountId",
                "InfluencerAccountId",
                "accountId",
                "AccountId",
                "id",
                "Id"
            };

            foreach (string name in names)
            {
                if (int.TryParse(
                        Request.Query[name].FirstOrDefault(),
                        out int queryValue))
                {
                    return queryValue;
                }
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(
                    HttpContext.RequestAborted);

                foreach (string name in names)
                {
                    if (int.TryParse(
                            form[name].FirstOrDefault(),
                            out int formValue))
                    {
                        return formValue;
                    }
                }

                return null;
            }

            if (!Request.Body.CanRead ||
                ((Request.ContentLength ?? 0) <= 0 &&
                 Request.Headers.TransferEncoding.Count == 0))
            {
                return null;
            }

            using var reader = new StreamReader(
                Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4_096,
                leaveOpen: true);

            string rawBody = await reader.ReadToEndAsync(
                HttpContext.RequestAborted);
            rawBody = rawBody.Trim();

            if (rawBody.Length == 0)
                return null;

            if (int.TryParse(rawBody.Trim('"'), out int rawValue))
                return rawValue;

            try
            {
                using JsonDocument document = JsonDocument.Parse(rawBody);
                JsonElement root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Number &&
                    root.TryGetInt32(out int numberValue))
                {
                    return numberValue;
                }

                if (root.ValueKind == JsonValueKind.String &&
                    int.TryParse(root.GetString(), out int stringValue))
                {
                    return stringValue;
                }

                return FindJsonInt(root, names);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        [HttpPost("/api/consumables/v1/consume")]
        public async Task<IActionResult> ConsumeConsumable()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            JsonElement? request = null;
            try
            {
                if (Request.Body.CanRead &&
                    ((Request.ContentLength ?? 0) > 0 ||
                     Request.Headers.TransferEncoding.Count > 0))
                {
                    using JsonDocument document =
                        await JsonDocument.ParseAsync(
                            Request.Body,
                            cancellationToken: HttpContext.RequestAborted);
                    request = document.RootElement.Clone();
                }
            }
            catch (JsonException)
            {

            }

            long? consumableId = request.HasValue
                ? FindJsonLong(request.Value, "Id", "id")
                : null;
            long? requestedDelta = request.HasValue
                ? FindJsonLong(
                    request.Value,
                    "DeltaCount",
                    "deltaCount")
                : null;
            int delta = requestedDelta is > 0 and <= int.MaxValue
                ? (int)requestedDelta.Value
                : 1;

            string disposition = "missing_id";
            int? loggedPreviousQuantity = null;
            int? loggedRemainingQuantity = null;
            if (consumableId is > 0)
            {
                bool resolved = TryGetConsumableInfo(
                    consumableId.Value,
                    out string descriptor,
                    out string friendlyName,
                    out bool isHairDye,
                    out string createdAt);

                if (!resolved)
                {
                    resolved = TryGetOwnedConsumableInfo(
                        accountId.Value,
                        consumableId.Value,
                        out descriptor,
                        out friendlyName,
                        out isHairDye,
                        out createdAt);
                }

                if (resolved)
                {
                    bool consumed = PlayerInventoryStore.TryConsumeConsumable(
                        accountId.Value,
                        descriptor,
                        consumableId.Value,
                        delta,
                        out int previousQuantity,
                        out int remainingQuantity);

                    disposition = consumed ? "consumed" : "already_spent";
                    if (consumed)
                    {
                        loggedPreviousQuantity = previousQuantity;
                        loggedRemainingQuantity = remainingQuantity;

                        if (isHairDye)
                        {
                            PlayerDB.SetPlayerSetting(
                                $"Avatar.HairDye.{descriptor}",
                                "True",
                                accountId.Value);

                            Console.WriteLine(
                                $"[HAIR DYE CONSUME] player={accountId.Value} " +
                                $"id={consumableId.Value} name={friendlyName} " +
                                $"desc={descriptor}");
                        }

                        if (remainingQuantity <= 0)
                        {
                            await NotiController.NotifyConsumableRemovedAsync(
                                accountId.Value,
                                consumableId.Value,
                                descriptor,
                                createdAt,
                                previousQuantity,
                                remainingQuantity);
                        }
                    }
                }
                else
                {
                    disposition = "unknown_id";
                }
            }

            Console.WriteLine(
                $"[CONSUMABLE CONSUME] player={accountId.Value} " +
                $"id={consumableId?.ToString() ?? "none"} delta={delta} " +
                $"result={disposition} " +
                $"before={loggedPreviousQuantity?.ToString() ?? "n/a"} " +
                $"remaining={loggedRemainingQuantity?.ToString() ?? "n/a"}");

            return Ok(new
            {
                error = string.Empty,
                success = true,
                value = (object?)null
            });
        }

        private static bool TryGetConsumableInfo(
            long consumableId,
            out string descriptor,
            out string friendlyName,
            out bool isHairDye,
            out string createdAt)
        {
            descriptor = string.Empty;
            friendlyName = string.Empty;
            isHairDye = false;
            createdAt = "2023-04-06T00:00:00.0000000Z";

            string path = Path.Combine(
                Program.dataDir,
                "APIS",
                "Items",
                "Consumables.json");

            if (!System.IO.File.Exists(path))
                return false;

            try
            {
                JsonNode? root = JsonNode.Parse(System.IO.File.ReadAllText(path));
                if (root is not JsonArray items)
                    return false;

                foreach (JsonNode? node in items)
                {
                    if (node is not JsonObject item)
                    {
                        continue;
                    }

                    List<long> ids = ReadConsumableIds(item);
                    int mappingIndex = ids.IndexOf(consumableId);
                    if (mappingIndex < 0)
                        continue;

                    friendlyName = ReadString(item, "FriendlyName") ?? string.Empty;
                    descriptor = ReadString(item, "ConsumableItemDesc") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(descriptor))
                        descriptor = $"legacy-consumable-{consumableId}";
                    isHairDye = IsHairDyeFriendlyName(friendlyName);
                    List<string> createdAts = ReadConsumableCreatedAts(item);
                    createdAt = mappingIndex < createdAts.Count
                        ? createdAts[mappingIndex]
                        : NormalizeConsumableTimestamp(
                            ReadString(item, "CreatedAt"))
                          ?? createdAt;
                    return true;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Console.WriteLine(
                    $"[CONSUMABLE LOOKUP] Could not read Consumables.json: {ex.Message}");
            }

            return false;
        }

        private static bool TryGetOwnedConsumableInfo(
            long accountId,
            long consumableId,
            out string descriptor,
            out string friendlyName,
            out bool isHairDye,
            out string createdAt)
        {
            descriptor = string.Empty;
            friendlyName = string.Empty;
            isHairDye = false;
            createdAt = "2023-04-06T00:00:00.0000000Z";

            try
            {
                PlayerInventoryStore.ConsumableOwnership? owned =
                    PlayerInventoryStore.GetConsumables(accountId)
                        .FirstOrDefault(item =>
                            item.ConsumableItemId == consumableId &&
                            item.Quantity > 0);

                if (owned == null)
                    return false;

                descriptor = string.IsNullOrWhiteSpace(owned.ConsumableItemDesc)
                    ? $"legacy-consumable-{consumableId}"
                    : owned.ConsumableItemDesc.Trim();
                friendlyName = string.IsNullOrWhiteSpace(owned.FriendlyName)
                    ? descriptor
                    : owned.FriendlyName.Trim();
                isHairDye = IsHairDyeFriendlyName(friendlyName);

                Console.WriteLine(
                    $"[CONSUMABLE LOOKUP] player={accountId} id={consumableId} " +
                    "source=owned_inventory_fallback");
                return true;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Console.WriteLine(
                    $"[CONSUMABLE LOOKUP] Could not read player inventory: {ex.Message}");
                return false;
            }
        }

        private static bool TryGetHairDyeDescriptor(
            long consumableId,
            out string descriptor,
            out string friendlyName)
        {
            descriptor = string.Empty;
            friendlyName = string.Empty;

            string path = Path.Combine(
                Program.dataDir,
                "APIS",
                "Items",
                "Consumables.json");

            if (!System.IO.File.Exists(path))
                return false;

            try
            {
                JsonNode? root = JsonNode.Parse(System.IO.File.ReadAllText(path));
                if (root is not JsonArray items)
                    return false;

                foreach (JsonNode? node in items)
                {
                    if (node is not JsonObject item ||
                        !ReadConsumableIds(item).Contains(consumableId))
                    {
                        continue;
                    }

                    friendlyName = ReadString(item, "FriendlyName") ?? string.Empty;
                    if (!IsHairDyeFriendlyName(friendlyName))
                        return false;

                    descriptor = ReadString(item, "ConsumableItemDesc") ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(descriptor);
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine(
                    $"[HAIR DYE CONSUME] Could not parse Consumables.json: {ex.Message}");
            }
            catch (IOException ex)
            {
                Console.WriteLine(
                    $"[HAIR DYE CONSUME] Could not read Consumables.json: {ex.Message}");
            }

            return false;
        }

        [HttpGet("/api/storefronts/v4/balance/{currencyType:int}")]
        public IActionResult GetStorefrontBalance(int currencyType)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            if (!Enum.IsDefined(typeof(CurrencyType), currencyType))
                return BadRequest(new { error = "invalid_currency" });

            var parsedCurrency = (CurrencyType)currencyType;
            int balance = PlayerDB.GetCurrencyBalance(
                accountId.Value,
                parsedCurrency);
            int balanceType = (int)BalanceType.NonPurchasedDefault;

            return Ok(new[]
            {
                new
                {
                    Balance = (long)balance,
                    CurrencyType = currencyType,
                    BalanceType = balanceType
                }
            });
        }

        [HttpPost("/api/storefronts/v2/balance")]
        public async Task<IActionResult> GetStorefrontBalancesV2()
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            HashSet<long> requestedTypes = await ReadLongValuesAsync(
                "currencyType", "CurrencyType",
                "currencyTypes", "CurrencyTypes",
                "type", "Type");

            int balanceType = (int)BalanceType.NonPurchasedDefault;

            IEnumerable<CurrencyType> typesToReturn = requestedTypes.Count > 0
                ? requestedTypes
                    .Where(value => Enum.IsDefined(typeof(CurrencyType), (int)value))
                    .Select(value => (CurrencyType)value)
                    .Distinct()
                : PlayerDB.GetAllCurrencyBalances(accountId.Value)
                    .Select(entry => entry.CurrencyType);

            var balances = typesToReturn
                .Select(currency => new
                {
                    Balance = (long)PlayerDB.GetCurrencyBalance(accountId.Value, currency),
                    CurrencyType = (int)currency,
                    BalanceType = balanceType
                })
                .ToArray();

            return Ok(balances);
        }

        [HttpGet("/api/storefronts/v1/balanceAddType/{currencyType:int}/{balanceAddType:int}")]
        public IActionResult GetStorefrontBalanceByAddType(
            int currencyType,
            int balanceAddType)
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account?.Player == null)
                return Unauthorized();

            if (!Enum.IsDefined(typeof(CurrencyType), currencyType) ||
                !Enum.IsDefined(typeof(BalanceType), balanceAddType))
            {
                return BadRequest(new { error = "invalid_balance_type" });
            }

            int totalBalance = PlayerDB.GetCurrencyBalance(
                account.PlayerId,
                (CurrencyType)currencyType);
            bool isPlayStation = account.PlatformIds?.Any(platform =>
                platform.Platform == Platforms.PlayStation) == true;

            int applicableBalanceType = (int)(isPlayStation
                ? BalanceType.PlayStationNonPurchasedP2P
                : BalanceType.NonPlayStationNonPurchasedP2P);
            long balance = balanceAddType == applicableBalanceType
                ? totalBalance
                : 0L;

            return Ok(new
            {
                Balance = balance,
                CurrencyType = currencyType,
                BalanceType = balanceAddType,
                BalanceAddType = balanceAddType
            });
        }

        [HttpGet("/subscription/subscriberCount/{accountId}")]
        public IActionResult GetSubscriberCount(long accountId)
        {

            return Ok(PlayerDB.GetSubscriberCount(accountId));
        }

        [HttpGet("/subscription/details/{accountId:long}")]
        public IActionResult GetSubscriptionDetails(long accountId)
        {
            if (accountId <= 0 || accountId > int.MaxValue)
            {
                return BadRequest(new
                {
                    error = "invalid_account_id"
                });
            }

            int subscriberCount = PlayerDB.GetSubscriberCount(accountId);
            Console.WriteLine(
                $"[SUBSCRIPTION DETAILS] account={accountId} " +
                $"club=0 subscribers={subscriberCount}");

            return Ok(new
            {
                accountId,
                clubId = 0,
                subscriberCount
            });
        }

        [HttpGet("/subscription/details/{subscription}")]
        public IActionResult GetNamedSubscriptionDetails(string subscription)
        {
            Console.WriteLine(
                $"[SUBSCRIPTION DETAILS] name={subscription} simulated=empty");

            return Content("{}", "application/json");
        }

        [HttpPost("/subscription/{accountId:long}")]
        [HttpPut("/subscription/{accountId:long}")]
        [HttpPost("/subscription/{accountId:long}/subscribe")]
        [HttpPut("/subscription/{accountId:long}/subscribe")]
        public IActionResult SubscribeToPlayer(long accountId)
        {
            var subscriberId = AuthStuff.GetPlayerId(Request);
            if (subscriberId == null)
                return Unauthorized();

            if (!PlayerDB.SetSubscription(
                    (long)subscriberId,
                    accountId,
                    subscribe: true))
                return BadRequest(new { success = false, error = "invalid_player" });

            return Ok(new
            {
                AccountId = accountId,
                SubscriberId = subscriberId,
                IsSubscribed = true,
                Subscribed = true,
                SubscriberCount = PlayerDB.GetSubscriberCount(accountId)
            });
        }

        [HttpDelete("/subscription/{accountId:long}")]
        [HttpDelete("/subscription/{accountId:long}/unsubscribe")]
        [HttpPost("/subscription/{accountId:long}/unsubscribe")]
        [HttpPut("/subscription/{accountId:long}/unsubscribe")]
        public IActionResult UnsubscribeFromPlayer(long accountId)
        {
            var subscriberId = AuthStuff.GetPlayerId(Request);
            if (subscriberId == null)
                return Unauthorized();

            if (!PlayerDB.SetSubscription(
                    (long)subscriberId,
                    accountId,
                    subscribe: false))
                return BadRequest(new { success = false, error = "invalid_player" });

            return Ok(new
            {
                AccountId = accountId,
                SubscriberId = subscriberId,
                IsSubscribed = false,
                Subscribed = false,
                SubscriberCount = PlayerDB.GetSubscriberCount(accountId)
            });
        }

        [HttpGet("/subscription/{accountId:long}")]
        [HttpGet("/subscription/{accountId:long}/status")]
        [HttpGet("/subscription/subscribed/{accountId:long}")]
        public IActionResult GetPlayerSubscription(long accountId)
        {
            var subscriberId = AuthStuff.GetPlayerId(Request);
            if (subscriberId == null)
                return Unauthorized();

            bool subscribed = PlayerDB.IsSubscribed(
                (long)subscriberId,
                accountId);
            return Ok(new
            {
                AccountId = accountId,
                SubscriberId = subscriberId,
                IsSubscribed = subscribed,
                Subscribed = subscribed,
                SubscriberCount = PlayerDB.GetSubscriberCount(accountId)
            });
        }

        [HttpGet("/api/customAvatarItems/v2/fromCreator/{creatorId}")]
        public IActionResult GetCustomAvatarItemsFromCreator(long creatorId)
        {
            List<JsonObject> items = LoadCustomAvatarItems(creatorId);
            return Ok(new
            {
                CreatorId = creatorId,
                Results = items,
                TotalResults = items.Count
            });
        }

        [HttpGet("/api/customAvatarItems/v1/isCreationEnabled")]
        [HttpGet("/api/customAvatarItems/v1/isRenderingEnabled")]
        public IActionResult CustomAvatarItemsIsEnabled()
        {
            return Ok(true);
        }

        [HttpGet("/api/roomconsumables/v1/roomConsumable/room/{roomId}")]
        public IActionResult GetRoomConsumablesForRoom(long roomId)
        {

            Console.WriteLine(
                $"[ROOM CONSUMABLES] room={roomId} definitions=0 compatibility=april-2023");

            return Ok(Array.Empty<object>());
        }

        [HttpGet("/api/roomconsumables/v1/roomConsumable/room/{roomId}/me")]
        public IActionResult GetRoomConsumablesForRoomMe(long roomId)
        {

            long? accountId = AuthStuff.GetPlayerId(Request);

            Console.WriteLine(
                $"[ROOM CONSUMABLES] room={roomId} account={accountId?.ToString() ?? "unknown"} " +
                "inventory=0 compatibility=april-2023");

            return Ok(Array.Empty<object>());
        }

        [HttpPost("/api/sanitize/v1")]
        public IActionResult SanitizeV1([FromBody] SanitizeRequest request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id != null)
            {
                var player = PlayerDB.Players.FindById((long)id);
                var username = player?.Player?.Username;
                var severity = ProfanityFilter.GetSeverity(request.Value);

                switch (severity)
                {
                    case ProfanitySeverity.SevereBypass:
                        PlayerDB.BanPlayer((long)id, 2147483647, "Attempted to bypass filter with slur/severe language");
                        DiscordLogger.LogSanitize($"🔨 **PERMA BANNED** — `{username ?? "unknown"}` (ID: `{id}`) tried to bypass filter: {request.Value}");
                        break;

                    case ProfanitySeverity.Severe:
                        PlayerDB.BanPlayer((long)id, 1814400, "Slur/severe language");
                        DiscordLogger.LogSanitize($"🔨 **3 WEEK BAN** — `{username ?? "unknown"}` (ID: `{id}`) said: {request.Value}");
                        break;

                    default:
                        DiscordLogger.LogSanitizeChat((long)id, request.Value, username);
                        break;
                }
            }

            var cleaned = ProfanityFilter.Blur(request.Value);
            return Ok(JsonSerializer.Serialize(cleaned));
        }

        [HttpPost("/api/sanitize/v1/isPure")]
        public IActionResult SanitizeV1IsPure()
        {
            return Ok(new
            {
                IsPure = true
            });
        }

        [HttpGet("/api/AppIntegrity/v1/token")]
        [HttpPost("/api/AppIntegrity/v1/verify")]
        public IActionResult AppIntegrityStub()
        {
            return Ok(new { valid = true });
        }

        [HttpGet("/api/PlayersBanned/v1/check/{accountId}")]
        public IActionResult PlayersBannedCheck(long accountId)
        {
            bool isBanned = PlayerDB.IsPlayerBanned(accountId, out var details);

            return Ok(new
            {
                accountId,
                isBanned,
                banExpiresAt = isBanned ? DateTimeOffset.FromUnixTimeSeconds(details!.ModerationSetUnixTime + details.Duration).UtcDateTime : (DateTime?)null,
                reason = isBanned ? details!.Message : (string?)null
            });
        }

        [HttpGet("/api/challenge/v1/current")]
        [HttpGet("/api/challenge/v1/all")]
        public IActionResult ChallengeStub()
        {
            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/api/communityboard/v1/get")]
        public IActionResult CommunityBoardStub()
        {
            return Ok(new
            {
                Results = Array.Empty<object>(),
                TotalResults = 0
            });
        }

        [HttpGet("/api/communityboard/v2/current")]
        public IActionResult CommunityBoardCurrent()
        {
            string path = Path.Combine(Program.dataDir, "communityboard.json");

            if (!System.IO.File.Exists(path))
            {
                string defaultJson = @"{
                ""Id"": 1,
                ""Title"": ""Community Board"",
                ""Posts"": []
            }";
                System.IO.File.WriteAllText(path, defaultJson);
                Console.WriteLine($"[CommunityBoard] No communityboard.json found, created default template at {path}");
            }

            try
            {
                string rewritten = LoadingScreenImageService.RewriteConfiguration(
                    System.IO.File.ReadAllText(path));
                return Content(rewritten, "application/json");
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Console.WriteLine($"[CommunityBoard] Could not rewrite image URLs: {ex.Message}");
                return Content(System.IO.File.ReadAllText(path), "application/json");
            }
        }

        [HttpPost("/api/communityboard/v2/current")]
        [HttpPut("/api/communityboard/v2/current")]
        [RequestSizeLimit(256 * 1024)]
        public async Task<IActionResult> SetCommunityBoardCurrent([FromBody] JsonElement request)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player?.Player == null)
                return Unauthorized();
            if (player.PlayerRoles?.Any(role =>
                    role is PlayerDBClasses.PlayerRoles.Developer or
                        PlayerDBClasses.PlayerRoles.Moderator) != true)
                return StatusCode(403);

            string path = Path.Combine(Program.dataDir, "communityboard.json");
            string json = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(path, json);
            await NotiController.BroadcastAnnouncementsUpdatedAsync();

            try
            {
                string rewritten = LoadingScreenImageService.RewriteConfiguration(json);
                return Content(rewritten, "application/json");
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[CommunityBoard] Saved board but could not rewrite image URLs: {ex.Message}");
                return Ok(request);
            }
        }

        [HttpPost("/api/externalfriendinvite/v1/send")]
        public IActionResult ExternalFriendInviteStub()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(new { success = true });
        }

        [HttpPost("/api/gamesight/v1/event")]
        public IActionResult GamesightStub()
        {
            return Ok();
        }

        [HttpGet("/api/incentivizedreferrals/v1/status")]
        public IActionResult IncentivizedReferralsStub()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(new
            {
                referralCode = (string?)null,
                referralsCompleted = 0,
                rewardsAvailable = Array.Empty<object>()
            });
        }

        [HttpGet("/api/keepsakes/v1/mine")]
        [HttpGet("/api/keepsakes/mine")]
        public IActionResult KeepsakesStub()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(new
            {
                KeepsakeInstances = Array.Empty<object>(),
                CollectionRecords = Array.Empty<object>(),
                CollectedKeepsakeIds = Array.Empty<long>()
            });
        }

        [HttpGet("/api/itemWishlists/v1/wishlist/me")]
        public IActionResult ItemWishlistMe()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(CreateWishlistItems(id.Value));
        }

        [HttpPut("/api/itemWishlists/v1/wishlist/me/{purchasableItemId:int}")]
        public IActionResult AddItemToWishlist(int purchasableItemId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            if (!PlayerDB.SetWishlistItem(id.Value, purchasableItemId, wished: true))
                return BadRequest(new { success = false, error = "wishlist_limit_or_invalid_item" });

            return Ok(new
            {
                WishlistItemId = DeterministicWishlistId(id.Value, purchasableItemId),
                AccountId = id.Value,
                PurchasableItemId = purchasableItemId,
                CreatedAt = DateTime.UtcNow
            });
        }

        [HttpDelete("/api/itemWishlists/v1/wishlist/me/{purchasableItemId:int}")]
        public IActionResult RemoveItemFromWishlist(int purchasableItemId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            PlayerDB.SetWishlistItem(id.Value, purchasableItemId, wished: false);
            return Ok(new
            {
                success = true,
                AccountId = id.Value,
                PurchasableItemId = purchasableItemId
            });
        }

        [HttpGet("/api/itemWishlists/v1/wishlist/{accountId:long}")]
        public IActionResult GetAccountWishlist(long accountId)
        {
            if (PlayerDB.Players.FindById(accountId) == null)
                return NotFound();
            return Ok(CreateWishlistItems(accountId));
        }

        private static object[] CreateWishlistItems(long accountId) =>
            PlayerDB.GetWishlist(accountId)
                .Select(purchasableItemId => (object)new
                {
                    WishlistItemId = DeterministicWishlistId(accountId, purchasableItemId),
                    AccountId = accountId,
                    PurchasableItemId = purchasableItemId,
                    CreatedAt = DateTime.UnixEpoch
                })
                .ToArray();

        private static Guid DeterministicWishlistId(long accountId, int itemId)
        {
            byte[] bytes = System.Security.Cryptography.MD5.HashData(
                Encoding.UTF8.GetBytes($"wishlist:{accountId}:{itemId}"));
            return new Guid(bytes);
        }

        [HttpPost("/api/offlineinvite/v1/send")]
        [HttpPost("/api/offlineinvite/send")]
        [HttpPost("/api/offlineinvite/{targetPlayerId:long}")]
        [HttpPost("/api/offlineinvite/v1/{targetPlayerId:long}")]
        [HttpPost("/api/offlineinvite/send/{targetPlayerId:long}")]
        [HttpPost("/api/offlineinvite/{targetPlayerId:long}/send")]
        [HttpPost("/api/offlineinvite/v1/send/{targetPlayerId:long}")]
        [HttpPost("/api/offlineinvite/{targetPlayerId:long}/{requestedRoomInstanceId:long}")]
        [HttpPost("/api/offlineinvite/v1/{targetPlayerId:long}/{requestedRoomInstanceId:long}")]
        [HttpPost("/api/offlineinvite/v1/send/{targetPlayerId:long}/{requestedRoomInstanceId:long}")]
        [HttpPut("/api/offlineinvite/{targetPlayerId:long}")]
        [HttpPut("/api/offlineinvite/v1/{targetPlayerId:long}")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> SendOfflineInvite(
            long? targetPlayerId = null,
            long? requestedRoomInstanceId = null,
            long? roomInstanceId = null)
        {
            long? inviterPlayerId = AuthStuff.GetPlayerId(Request);
            if (!inviterPlayerId.HasValue)
                return Unauthorized();

            HashSet<long> targetPlayerIds = await ReadLongValuesAsync(
                "id", "playerId", "PlayerId", "accountId", "AccountId",
                "targetPlayerId", "TargetPlayerId", "targetAccountId", "TargetAccountId",
                "inviteePlayerId", "InviteePlayerId", "inviteeAccountId", "InviteeAccountId",
                "playerIds", "PlayerIds", "accountIds", "AccountIds");
            if (targetPlayerId.HasValue && targetPlayerId.Value > 0)
                targetPlayerIds.Add(targetPlayerId.Value);
            targetPlayerIds.Remove(inviterPlayerId.Value);

            HashSet<long> claimedInstanceIds = await ReadLongValuesAsync(
                "roomInstanceId", "RoomInstanceId",
                "requestedRoomInstanceId", "RequestedRoomInstanceId",
                "targetRoomInstanceId", "TargetRoomInstanceId");
            if (requestedRoomInstanceId is > 0)
                claimedInstanceIds.Add(requestedRoomInstanceId.Value);
            if (roomInstanceId is > 0)
                claimedInstanceIds.Add(roomInstanceId.Value);
            targetPlayerIds.ExceptWith(claimedInstanceIds);

            Heartbeat? heartbeat = PlayerDB.GetPlayerHeartbeat(inviterPlayerId.Value);
            RoomInstance? room = heartbeat?.roomInstance;
            if (room == null)
                return BadRequest(new { success = false, error = "inviter_not_in_room" });

            if (!Sessions.IsConfirmedParticipant(
                    inviterPlayerId.Value,
                    room.roomInstanceId))
            {
                return StatusCode(409, new
                {
                    success = false,
                    error = "room_instance_membership_not_confirmed"
                });
            }

            if (claimedInstanceIds.Any(value =>
                    value != room.roomInstanceId))
            {
                return StatusCode(409, new
                {
                    success = false,
                    error = "room_invite_source_mismatch",
                    requestedRoomInstanceIds = claimedInstanceIds,
                    currentRoomInstanceId = room.roomInstanceId
                });
            }

            if (targetPlayerIds.Count == 0)
                return BadRequest(new { success = false, error = "missing_target_player" });

            var invited = new List<long>();
            var missing = new List<long>();
            var blocked = new List<long>();
            foreach (long receiverPlayerId in targetPlayerIds.Take(100))
            {
                if (PlayerDB.Players.FindById(receiverPlayerId)?.Player == null)
                {
                    missing.Add(receiverPlayerId);
                    continue;
                }

                var relationship = RelationshipDB.GetClientRelationship(
                    receiverPlayerId,
                    inviterPlayerId.Value);
                if (relationship?.Ignored is 1 or 3)
                {
                    blocked.Add(receiverPlayerId);
                    continue;
                }

                await NotiController.NotifyRoomInviteAsync(
                    inviterPlayerId.Value,
                    receiverPlayerId,
                    room.roomId,
                    room.roomInstanceId,
                    room.photonRoomId);
                invited.Add(receiverPlayerId);
            }

            bool success = invited.Count > 0;
            Console.WriteLine(
                $"[OFFLINE INVITE] inviter={inviterPlayerId.Value} " +
                $"targets={string.Join(',', targetPlayerIds)} " +
                $"invited={string.Join(',', invited)} " +
                $"missing={string.Join(',', missing)} " +
                $"blocked={blocked.Count} " +
                $"room={room.roomId} instance={room.roomInstanceId} " +
                $"requestedInstance={string.Join(',', claimedInstanceIds)} " +
                $"path={Request.Path} contentType={Request.ContentType ?? "null"}");

            return Ok(new
            {
                Success = success,
                Result = success ? 0 : 1,
                Error = success ? string.Empty : "invite_failed",
                RoomId = room.roomId,
                RoomInstanceId = room.roomInstanceId,
                InvitedPlayerIds = invited,
                MissingPlayerIds = missing,
                BlockedPlayerIds = blocked,
                Results = invited.Select(playerId => new
                {
                    PlayerId = playerId,
                    Error = string.Empty
                }).ToArray()
            });
        }

        [HttpPost("/api/platformlogin/v1/link")]
        public IActionResult PlatformLoginStub()
        {
            return Ok(new { success = true });
        }

        [HttpGet("/api/playerwarnings/v1/mine")]
        public IActionResult PlayerWarningsStub()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(Array.Empty<object>());
        }

        [HttpGet("/api/playstationplus/v1/membership")]
        public IActionResult PlaystationPlusMembership()
        {
            return Ok(new
            {
                isMember = false,
                expiresAt = (DateTime?)null
            });
        }

        [HttpPost("/api/playstationplus/v1/expire")]
        public IActionResult PlaystationPlusExpire()
        {
            return Ok();
        }

        [HttpGet("/api/progressionEvents/v1/all")]
        public IActionResult ProgressionEventsStub()
        {
            return Ok(Array.Empty<object>());
        }

        [HttpGet("/api/roomEarningsDistributions/v1/mine")]
        public IActionResult RoomEarningsDistributionsStub()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            var results = RoomDB.Rooms.Find(room =>
                    room.CreatorAccountId == id.Value && !room.IsDorm)
                .Select(CreateRoomEarningsDistribution)
                .ToList();

            return Ok(new
            {
                Results = results,
                TotalResults = results.Count
            });
        }

        [HttpGet("/api/roomEarningsDistributions/v1/earningsDistribution/{roomId:long}")]
        public IActionResult GetRoomEarningsDistribution(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            return room == null
                ? NotFound()
                : Ok(CreateRoomEarningsDistribution(room));
        }

        [HttpPost("/api/rooms/v1/verifyRole")]
        public async Task<IActionResult> VerifyRoomRole()
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();
            if (!Request.HasFormContentType)
                return BadRequest(new { success = false, error = "Expected room role form data." });

            var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
            if (!long.TryParse(form["roomId"].FirstOrDefault(), out long roomId))
                return BadRequest(new { success = false, error = "A valid room ID is required." });

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound();

            int.TryParse(form["role"].FirstOrDefault(), out int claimedRole);
            var assignedRole = room.CreatorAccountId == accountId.Value
                ? RoomDBClasses.Role.Creator
                : room.Roles?.FirstOrDefault(role => role.AccountId == accountId.Value)?.Role
                    ?? RoomDBClasses.Role.None;
            string? context = form["context"].FirstOrDefault();

            if (claimedRole != (int)assignedRole)
            {
                Console.WriteLine(
                    $"[ROOM ROLE VERIFY] room={roomId} account={accountId.Value} " +
                    $"claimed={claimedRole} assigned={(int)assignedRole} context={context ?? "null"}");
            }

            return Ok();
        }

        private static RoomEarningsDistributionResponse CreateRoomEarningsDistribution(
            RoomDBClasses.Room room)
        {
            var mapping = new Dictionary<int, byte>();
            if (room.CreatorAccountId is > 0 and <= int.MaxValue)
                mapping[(int)room.CreatorAccountId] = 100;

            return new RoomEarningsDistributionResponse
            {
                roomId = room.RoomId,
                earningsDistributionMapping = mapping,
                earningsDistributionMethod = 0
            };
        }

        private sealed class RoomEarningsDistributionResponse
        {
            public long roomId { get; set; }
            public Dictionary<int, byte> earningsDistributionMapping { get; set; } = new();
            public int earningsDistributionMethod { get; set; }
        }

        [HttpGet("/api/royale/v1/status")]
        public IActionResult RoyaleStatusStub()
        {
            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/api/subscriptionseasons/v1/seasons/current")]
        public IActionResult SubscriptionSeasonsCurrent()
        {
            return Ok(new
            {
                seasonId = 0,
                name = "",
                startsAt = DateTime.UtcNow,
                endsAt = DateTime.UtcNow.AddDays(30)
            });
        }

        [HttpGet("/api/ugcPurchasables/v1/mine")]
        public IActionResult UgcPurchasablesStub()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(new
            {
                Results = Array.Empty<object>(),
                TotalResults = 0
            });
        }

        [HttpGet("/cdn/{*filePath}")]
        public IActionResult GetCDNFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return NotFound();

            string normalized = filePath.Replace('\\', '/').TrimStart('/');
            if (normalized.Contains("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(normalized))
            {
                return BadRequest();
            }

            if (normalized.StartsWith("room/invention/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[5..];
            if (normalized.StartsWith("invention/room/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[10..];

            string cdnRoot = Path.GetFullPath(Path.Combine(Program.dataDir, "CDN"));
            var candidates = new List<string>
            {
                Path.Combine(cdnRoot, normalized.Replace('/', Path.DirectorySeparatorChar))
            };

            string safeName = Path.GetFileName(normalized);
            if (!string.IsNullOrWhiteSpace(safeName))
            {

                candidates.Add(Path.Combine(cdnRoot, "assetbundles", safeName));
                candidates.Add(Path.Combine(cdnRoot, "assetbundle", safeName));
                candidates.Add(Path.Combine(cdnRoot, "unity", safeName));
                candidates.Add(Path.Combine(cdnRoot, "invention", safeName));
                candidates.Add(Path.Combine(cdnRoot, "room", safeName));

                if (safeName.EndsWith(".inv", StringComparison.OrdinalIgnoreCase))
                {
                    string extensionless = safeName[..^4];
                    if (!string.IsNullOrWhiteSpace(extensionless))
                    {
                        candidates.Add(Path.Combine(cdnRoot, "invention", extensionless));
                        candidates.Add(Path.Combine(cdnRoot, "room", extensionless));
                    }
                }
                else
                {
                    string baseName = Path.GetFileNameWithoutExtension(safeName);
                    if (string.IsNullOrWhiteSpace(baseName))
                        baseName = safeName;
                    string invName = baseName + ".inv";
                    candidates.Add(Path.Combine(cdnRoot, "invention", invName));
                    candidates.Add(Path.Combine(cdnRoot, "room", invName));
                }
            }

            string? path = null;
            foreach (string candidate in candidates)
            {
                string resolved;
                try
                {
                    resolved = Path.GetFullPath(candidate);
                }
                catch (Exception exception) when (
                    exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    continue;
                }

                if (!resolved.StartsWith(cdnRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) ||
                    !System.IO.File.Exists(resolved))
                {
                    continue;
                }

                path = resolved;
                break;
            }

            if (path == null)
                return NotFound();

            var contentType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".mp4" or ".m4v" => "video/mp4",
                ".mov" => "video/quicktime",
                ".webm" => "video/webm",
                ".wav" => "audio/wav",
                ".assetbundle" => "application/octet-stream",
                ".json" => "application/json",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };

            Response.Headers.CacheControl = "public,max-age=31536000,immutable";
            return PhysicalFile(
                path,
                contentType,
                enableRangeProcessing: contentType.StartsWith(
                    "video/",
                    StringComparison.OrdinalIgnoreCase));
        }

        [HttpGet("/purchase/v1/hasspentmoney")]
        public IActionResult HasSpentMoney()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(new { hasSpentMoney = false });
        }

        [HttpGet("/purchasecampaign/allcurrent/v2")]
        public IActionResult PurchaseCampaignAllCurrent()
        {
            return Ok(Array.Empty<object>());
        }

        [HttpGet("/api/apple/musicpromotion")]
        public IActionResult AppleMusicPromotionStub()
        {
            return Ok(new { eligible = false });
        }

        [HttpGet("/preferences")]
        public IActionResult GetPreferences()
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            return Ok(new
            {
                NotificationPreferences = new[]
                {
                    0,
                    1,
                    2,
                    3,
                    4,
                    5,
                    6,
                    7,
                    8,
                    9,
                    10
                }
            });
        }

        [HttpGet("api/storefronts/v1/adcarouselitems")]
        public IActionResult GetAdCarouselItems()
        {

            List<CatalogSku> catalog = GetCatalogSkus()
                .Where(IsUsableStoreItem)
                .ToList();

            List<int> purchasableIds = catalog
                .Select(item => item.PurchasableItemId)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            int avatarItems = catalog.Count(IsAvatarCatalogItem);
            int equipmentSkins = catalog.Count(IsEquipmentSkin);
            int consumables = catalog.Count(IsConsumableCatalogItem);

            Console.WriteLine(
                $"[ALL-I-GOT STORE] items={purchasableIds.Count} " +
                $"avatar={avatarItems} skins={equipmentSkins} consumables={consumables}");

            var items = new[]
            {
                new StorefrontAdCarouselItem
                {
                    AdCarouselItemId = 1,
                    ImageName = "DefaultPFP.png",
                    Title = "The All-I-Got Store",
                    Description =
                        $"Everything Mocha has: {avatarItems} avatar items, " +
                        $"{equipmentSkins} skins, and {consumables} consumables. " +
                        $"100 tokens each.",
                    PurchasableItemIds = purchasableIds,
                    PurchaseReminderId = null
                }
            };
            return Ok(items);
        }

        [HttpGet("/api/rooms/v1/filters")]
        public IActionResult GetRoomFilters()
        {
            string[] pinnedTags = { "rro", "quest", "base" };
            string[] popularTags = { "rro", "quest", "base" };
            string[] tags =
            {
                "rro", "quest", "base", "pvp", "game", "hangout",
                "art", "music", "sports", "parkour", "story"
            };

            Console.WriteLine(
                $"[ROOM FILTERS] format=object pinned={pinnedTags.Length} " +
                $"popular={popularTags.Length} tags={tags.Length}");

            return Ok(new
            {
                PinnedTags = pinnedTags,
                PopularTags = popularTags,
                Tags = tags
            });
        }

        [HttpGet("/api/relationships/v2/sendfriendrequest")]
        [HttpPost("/api/relationships/v2/sendfriendrequest")]
        public async Task<IActionResult> SendFriendRequest()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            long targetId = (await ReadLongValuesAsync(
                "id", "playerId", "PlayerId", "accountId", "AccountId",
                "targetPlayerId", "TargetPlayerId", "targetAccountId", "TargetAccountId"))
                .FirstOrDefault();
            if (targetId <= 0)
                return BadRequest(new { success = false, error = "missing_target_player" });

            ClientRelationshipDTO? before =
                RelationshipDB.GetClientRelationship(accountId.Value, targetId);

            PlayerRelationship? relationship = PlayerDB.SendFriendRequest(
                accountId.Value,
                targetId);
            if (relationship == null)
                return BadRequest(new { success = false, error = "invalid_player" });

            ClientRelationshipDTO? clientRelationship =
                RelationshipDB.GetClientRelationship(accountId.Value, targetId);
            if (clientRelationship == null)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = "relationship_not_persisted"
                });
            }

            await NotifyRelationshipMutationAsync(
                accountId.Value,
                targetId,
                before,
                clientRelationship);

            return Ok(clientRelationship);
        }

        [HttpGet("/api/relationships/v2/addfriend")]
        [HttpPost("/api/relationships/v2/addfriend")]
        [HttpGet("/api/relationships/v2/acceptfriendrequest")]
        [HttpPost("/api/relationships/v2/acceptfriendrequest")]
        public async Task<IActionResult> AcceptFriendRequest()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            long requesterId = (await ReadLongValuesAsync(
                "id", "playerId", "PlayerId", "accountId", "AccountId",
                "requesterPlayerId", "RequesterPlayerId", "requesterAccountId", "RequesterAccountId"))
                .FirstOrDefault();
            if (requesterId <= 0)
                return BadRequest(new { success = false, error = "missing_requester_player" });

            ClientRelationshipDTO? before =
                RelationshipDB.GetClientRelationship(accountId.Value, requesterId);

            PlayerRelationship? relationship = PlayerDB.AcceptFriendRequest(
                accountId.Value,
                requesterId);

            relationship ??= PlayerDB.SendFriendRequest(accountId.Value, requesterId);
            if (relationship == null)
                return BadRequest(new { success = false, error = "invalid_player" });

            ClientRelationshipDTO? clientRelationship =
                RelationshipDB.GetClientRelationship(accountId.Value, requesterId);
            if (clientRelationship == null)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = "relationship_not_persisted"
                });
            }

            await NotifyRelationshipMutationAsync(
                accountId.Value,
                requesterId,
                before,
                clientRelationship);

            return Ok(clientRelationship);
        }

        [HttpPost("/api/relationships/v3/{id:long}")]
        public async Task<IActionResult> AddFriendV3(long id)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            ClientRelationshipDTO? before =
                RelationshipDB.GetClientRelationship(accountId.Value, id);

            PlayerRelationship? relationship = PlayerDB.SendFriendRequest(
                accountId.Value,
                id);
            if (relationship == null)
                return BadRequest(new { success = false, error = "invalid_player" });

            ClientRelationshipDTO? clientRelationship =
                RelationshipDB.GetClientRelationship(accountId.Value, id);
            if (clientRelationship == null)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = "relationship_not_persisted"
                });
            }

            await NotifyRelationshipMutationAsync(
                accountId.Value,
                id,
                before,
                clientRelationship);

            return Ok(clientRelationship);
        }

        [HttpDelete("/api/relationships/v3/{id:long}")]
        [HttpDelete("/api/relationships/v2/removefriend/{id:long}")]
        public async Task<IActionResult> RemoveFriend(long id)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            bool removed = PlayerDB.RemoveFriend(accountId.Value, id);
            if (!removed)
                return NotFound();

            await NotiController.NotifyFriendRemovedAsync(
                accountId.Value,
                id);
            return NoContent();
        }

        [HttpGet("/api/relationships/v2/removefriend")]
        [HttpPost("/api/relationships/v2/removefriend")]
        public async Task<IActionResult> RemoveFriendV2([FromQuery] long id)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            if (id <= 0 || id == accountId.Value)
                return BadRequest(new { success = false, error = "invalid_player" });

            bool removed = PlayerDB.RemoveFriend(accountId.Value, id);
            if (removed)
            {
                await NotiController.NotifyFriendRemovedAsync(
                    accountId.Value,
                    id);
            }

            Console.WriteLine(
                $"[RELATIONSHIP REMOVE] source={accountId.Value} target={id} " +
                $"removed={removed} route=v2-query live={removed}");

            return Ok();
        }

        private static async Task NotifyRelationshipMutationAsync(
            long actorPlayerId,
            long otherPlayerId,
            ClientRelationshipDTO? before,
            ClientRelationshipDTO after)
        {
            bool unchanged = before != null &&
                before.PlayerID == after.PlayerID &&
                before.RelationshipType == after.RelationshipType &&
                before.Favorited == after.Favorited &&
                before.Muted == after.Muted &&
                before.Ignored == after.Ignored;
            if (unchanged)
                return;

            RelationshipType finalType =
                (RelationshipType)after.RelationshipType;
            RelationshipType? previousType = before == null
                ? null
                : (RelationshipType)before.RelationshipType;

            if (finalType == RelationshipType.Friend &&
                previousType != RelationshipType.Friend)
            {
                await NotiController.NotifyFriendAcceptedAsync(
                    actorPlayerId,
                    otherPlayerId);
                return;
            }

            if (finalType == RelationshipType.OutgoingFriendRequest &&
                previousType != RelationshipType.OutgoingFriendRequest)
            {
                await NotiController.NotifyFriendRequestAsync(
                    actorPlayerId,
                    otherPlayerId);
                return;
            }

            await NotiController.NotifyRelationshipFlagsChangedAsync(
                actorPlayerId,
                otherPlayerId,
                "friendship-change");
        }

        [HttpGet("/api/relationships/mutualfriends")]
        public IActionResult GetMutualFriends([FromQuery] long id)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var mine = PlayerDB.GetRelationships((long)accountId)
                .Where(value => value.RelationshipType == RelationshipType.Friend)
                .Select(value => value.PlayerID)
                .ToHashSet();
            var theirs = PlayerDB.GetRelationships(id)
                .Where(value => value.RelationshipType == RelationshipType.Friend)
                .Select(value => value.PlayerID);

            return Ok(theirs.Where(mine.Contains).ToArray());
        }

        [HttpPost("/api/PlayerReporting/v3/create")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> CreatePlayerReportV3()
        {
            var reporter = AuthStuff.GetCurrentPlayer(Request);

            if (reporter?.Player == null)
                return Unauthorized();

            JsonElement requestData;

            try
            {
                if (Request.HasFormContentType)
                {
                    var form = await Request.ReadFormAsync(
                        HttpContext.RequestAborted);

                    var formData = form.ToDictionary(
                        entry => entry.Key,
                        entry => entry.Value.ToString(),
                        StringComparer.OrdinalIgnoreCase);

                    requestData = JsonSerializer.SerializeToElement(formData);
                }
                else
                {
                    using var reader = new StreamReader(
                        Request.Body,
                        Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: true,
                        leaveOpen: true);

                    string rawBody = await reader.ReadToEndAsync(
                        HttpContext.RequestAborted);

                    if (string.IsNullOrWhiteSpace(rawBody))
                    {
                        requestData = JsonSerializer.SerializeToElement(
                            new Dictionary<string, string>());
                    }
                    else
                    {
                        try
                        {
                            using JsonDocument document =
                                JsonDocument.Parse(rawBody);

                            requestData = document.RootElement.Clone();
                        }
                        catch (JsonException)
                        {
                            var parsedData =
                                Microsoft.AspNetCore.WebUtilities.QueryHelpers
                                    .ParseQuery(rawBody)
                                    .ToDictionary(
                                        entry => entry.Key,
                                        entry => entry.Value.ToString(),
                                        StringComparer.OrdinalIgnoreCase);

                            if (parsedData.Count == 0)
                                parsedData["RawBody"] = rawBody;

                            requestData =
                                JsonSerializer.SerializeToElement(parsedData);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[PLAYER REPORT] Failed reading request: {exception}");

                return BadRequest(new
                {
                    success = false,
                    error = "invalid_report_body"
                });
            }

            long reporterId = reporter.PlayerId;
            string? reporterUsername = reporter.Player.Username;

            long? reportedPlayerId = FindJsonLong(
                requestData,
                "PlayerIdReported",
                "ReportedPlayerId",
                "ReportedAccountId",
                "TargetPlayerId",
                "TargetAccountId",
                "SuspectPlayerId");

            var reportedPlayer = reportedPlayerId.HasValue
                ? PlayerDB.Players.FindById(reportedPlayerId.Value)
                : null;

            string? reportedUsername =
                reportedPlayer?.Player?.Username ??
                FindJsonString(
                    requestData,
                    "UsernameReported",
                    "ReportedPlayerUsername",
                    "ReportedUsername",
                    "TargetUsername");

            int? reportCategory = FindJsonInt(
                requestData,
                "ReportCategory",
                "ReportCategoryId",
                "CategoryId",
                "ReportType");

            string? details = FindJsonString(
                requestData,
                "Details",
                "Description",
                "Comment",
                "Message",
                "AdditionalDetails",
                "Notes");

            long? roomId = FindJsonLong(
                requestData,
                "RoomId",
                "SourceRoomId");

            string? roomInstanceType = FindJsonString(
                requestData,
                "RoomInstanceType",
                "InstanceType",
                "RoomType");

            Guid reportId = Guid.NewGuid();

            var storedReport = new ReportsDB.PlayerReport
            {
                Id = reportId,
                ReporterId = reporterId,
                ReporterUsername = reporterUsername,
                ReportedPlayerId = reportedPlayerId,
                ReportedUsername = reportedUsername,
                ReportCategory = reportCategory,
                Details = details,
                RoomId = roomId,
                RoomInstanceType = roomInstanceType
            };
            ReportsDB.PlayerReports.Insert(storedReport);

            DiscordLogger.LogPlayerReport(
                reportId,
                reporterId,
                reporterUsername,
                reportedPlayerId,
                reportedUsername,
                reportCategory,
                details,
                roomId,
                roomInstanceType);

            Console.WriteLine(
                $"[PLAYER REPORT] Report ID: {reportId}");

            Console.WriteLine(
                $"[PLAYER REPORT] Reporter: " +
                $"{reporterUsername ?? "unknown"} ({reporterId})");

            Console.WriteLine(
                $"[PLAYER REPORT] Reported: " +
                $"{reportedUsername ?? "unknown"} " +
                $"({reportedPlayerId?.ToString() ?? "unknown"})");

            Console.WriteLine(
                $"[PLAYER REPORT] Payload: {requestData.GetRawText()}");

            bool reporterIsDeveloper =
                reporter.PlayerRoles?.Contains(PlayerRoles.Developer) == true;
            bool reporterIsModerator =
                reporter.PlayerRoles?.Contains(PlayerRoles.Moderator) == true;

            if ((reporterIsDeveloper || reporterIsModerator) &&
                TryParseReportBanCommand(
                    details,
                    out int banDurationSeconds,
                    out string banReason))
            {
                if (!reportedPlayerId.HasValue ||
                    reportedPlayer?.Player == null)
                {
                    Console.WriteLine(
                        $"[REPORT BAN] reporter={reporterId} rejected=true " +
                        "reason=reported_player_not_found");
                }
                else if (reportedPlayerId.Value == reporterId)
                {
                    Console.WriteLine(
                        $"[REPORT BAN] reporter={reporterId} rejected=true " +
                        "reason=self_ban");
                }
                else
                {
                    bool targetIsDeveloper =
                        reportedPlayer.PlayerRoles?.Contains(
                            PlayerRoles.Developer) == true;

                    if (targetIsDeveloper && !reporterIsDeveloper)
                    {
                        Console.WriteLine(
                            $"[REPORT BAN] reporter={reporterId} " +
                            $"target={reportedPlayerId.Value} rejected=true " +
                            "reason=moderator_cannot_ban_developer");
                    }
                    else
                    {
                        PlayerDB.BanPlayer(
                            reportedPlayerId.Value,
                            banDurationSeconds,
                            banReason,
                            checked((ulong)reporterId));

                        storedReport.Status = banDurationSeconds > 0 &&
                            banDurationSeconds < int.MaxValue
                                ? "TimedOut"
                                : "Banned";
                        storedReport.ResolvedByAccountId = reporterId;
                        storedReport.ResolvedByUsername = reporterUsername;
                        storedReport.ResolvedAt = DateTime.UtcNow;
                        storedReport.ResolutionNote = banReason;
                        storedReport.ActionDurationSeconds = banDurationSeconds;
                        ReportsDB.PlayerReports.Update(storedReport);

                        Console.WriteLine(
                            $"[REPORT BAN] reporter={reporterId} " +
                            $"target={reportedPlayerId.Value} " +
                            $"durationSeconds={banDurationSeconds} " +
                            $"reason={banReason}");
                    }
                }
            }

            return Ok(new
            {
                success = true
            });
        }

        [HttpPut("/reports/{reportId:guid}")]
        [RequestSizeLimit(16 * 1024 * 1024)]
        public async Task<IActionResult> UploadCrashReport(Guid reportId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
            {
                return Unauthorized(new
                {
                    success = false,
                    reportId,
                    error = "authentication_required"
                });
            }

            string reportsDirectory = Path.Combine(
                Program.dataDir,
                "Debug",
                "CrashReports");
            Directory.CreateDirectory(reportsDirectory);

            bool isJson =
                Request.ContentType?.Contains(
                    "json",
                    StringComparison.OrdinalIgnoreCase) == true;
            string extension = isJson ? ".json" : ".bin";
            string reportPath = Path.Combine(
                reportsDirectory,
                reportId.ToString("D") + extension);

            try
            {
                await using var reportFile = new FileStream(
                    reportPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 81920,
                    useAsync: true);
                await Request.Body.CopyToAsync(
                    reportFile,
                    HttpContext.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499, new
                {
                    success = false,
                    reportId,
                    error = "crash_report_upload_cancelled"
                });
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[CRASH REPORT] Failed report={reportId}: {exception}");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        reportId,
                        error = "crash_report_storage_failed"
                    });
            }

            long storedBytes = new FileInfo(reportPath).Length;
            Console.WriteLine(
                $"[CRASH REPORT] Stored report={reportId} " +
                $"account={accountId.Value} bytes={storedBytes} " +
                $"contentType={Request.ContentType ?? "unknown"}");

            return Ok(new
            {
                success = true,
                id = reportId,
                reportId
            });
        }

        [HttpPut("/reports/{reportId:guid}/attachments/{fileName}")]
        [RequestSizeLimit(16 * 1024 * 1024)]
        public async Task<IActionResult> UploadCrashReportAttachment(
            Guid reportId,
            string fileName)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
            {
                return Unauthorized(new
                {
                    success = false,
                    reportId,
                    error = "authentication_required"
                });
            }

            string safeFileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeFileName) ||
                safeFileName != fileName)
            {
                return BadRequest(new
                {
                    success = false,
                    reportId,
                    error = "invalid_attachment_name"
                });
            }

            string attachmentsDirectory = Path.Combine(
                Program.dataDir,
                "Debug",
                "CrashReports",
                reportId.ToString("D") + "_attachments");
            Directory.CreateDirectory(attachmentsDirectory);

            string attachmentPath = Path.Combine(
                attachmentsDirectory,
                safeFileName);

            try
            {
                await using var attachmentFile = new FileStream(
                    attachmentPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 81920,
                    useAsync: true);
                await Request.Body.CopyToAsync(
                    attachmentFile,
                    HttpContext.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499, new
                {
                    success = false,
                    reportId,
                    error = "crash_report_attachment_upload_cancelled"
                });
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[CRASH REPORT] Failed attachment report={reportId} " +
                    $"file={safeFileName}: {exception}");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        reportId,
                        error = "crash_report_attachment_storage_failed"
                    });
            }

            long storedBytes = new FileInfo(attachmentPath).Length;
            Console.WriteLine(
                $"[CRASH REPORT] Stored attachment report={reportId} " +
                $"file={safeFileName} account={accountId.Value} " +
                $"bytes={storedBytes}");

            return Ok(new
            {
                success = true,
                id = reportId,
                reportId,
                fileName = safeFileName
            });
        }

        private static bool TryParseReportBanCommand(
            string? details,
            out int durationSeconds,
            out string reason)
        {
            durationSeconds = 0;
            reason = string.Empty;

            string command = details?.Trim() ?? string.Empty;
            int separatorIndex = command.IndexOf(':');
            if (separatorIndex <= 1 || separatorIndex == command.Length - 1)
                return false;

            string duration = command[..separatorIndex];
            reason = command[(separatorIndex + 1)..].Trim();

            if (duration.Any(char.IsWhiteSpace) ||
                reason.Length is < 1 or > 500)
            {
                return false;
            }

            char unit = duration[^1];
            string quantityText = duration[..^1];
            if (quantityText.Length == 0 ||
                quantityText.Any(character => !char.IsAsciiDigit(character)) ||
                !long.TryParse(quantityText, out long quantity) ||
                quantity <= 0)
            {
                return false;
            }

            long multiplier = unit switch
            {
                'm' => 60,
                'h' => 60 * 60,
                'd' => 24 * 60 * 60,
                'w' => 7 * 24 * 60 * 60,
                _ => 0
            };
            if (multiplier == 0 ||
                quantity > int.MaxValue / multiplier)
            {
                return false;
            }

            durationSeconds = checked((int)(quantity * multiplier));
            return true;
        }

        [HttpGet("/api/messages/v1/favoriteFriendOnlineStatus")]
        public IActionResult GetFavoriteFriendOnlineStatus()
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var statuses = PlayerDB.GetRelationships((long)accountId)
                .Where(value =>
                    value.RelationshipType == RelationshipType.Friend &&
                    value.Favorited)
                .Select(value => new
                {
                    PlayerId = value.PlayerID,
                    IsOnline = PlayerDB.GetPlayerHeartbeat(value.PlayerID).isOnline
                });

            return Ok(statuses);
        }

        [HttpPost("purchase/v1/cleanuppending")]
        public IActionResult CleanupPendingPurchases()
        {
            return Ok(new { cleaned = 0, status = "ok" });
        }

        [HttpGet("api/playerevents/v1/tagfilters")]
        [HttpGet("api/playerevents/v1/filters")]
        public IActionResult GetPlayerEventTagFilters()
        {
            string[] filters = { "social", "competitive", "creative" };

            return Ok(new
            {
                PinnedTags = Array.Empty<string>(),
                PopularTags = filters,
                Tags = filters
            });
        }

        [HttpGet("api/playerevents/v1/room/{roomId}")]
        public IActionResult GetPlayerEventsForRoom(long roomId)
        {
            return Ok(Array.Empty<object>());
        }

        [HttpGet("api/inventions/v1/room")]
        public IActionResult GetInventionsForRoom(
            [FromQuery] long id,
            [FromQuery] long roomId = 0,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            long resolvedRoomId = roomId > 0 ? roomId : id;
            object[] results = CreatorFeatureDB.SearchInventions(
                    roomId: resolvedRoomId > 0 ? resolvedRoomId : null,
                    skip: skip,
                    take: take)
                .Select(CreatorFeatureDB.ToClientInvention)
                .ToArray();
            return Ok(results);
        }

        private static readonly string[] ChatMemberIdKeys =
        {
            "MemberIds", "memberIds",
            "Members", "members",
            "ThreadMembers", "threadMembers",
            "WithMembers", "withMembers",
            "AccountIds", "accountIds",
            "PlayerAccountIds", "playerAccountIds",
            "PlayerIds", "playerIds",
            "UserIds", "userIds",
            "Ids", "ids",
            "RecipientIds", "recipientIds",
            "Recipients", "recipients",
            "ReceiverIds", "receiverIds",
            "TargetIds", "targetIds",
            "TargetPlayerIds", "targetPlayerIds",
            "OtherPlayerIds", "otherPlayerIds",
            "ToAccountIds", "toAccountIds",
            "ToPlayerIds", "toPlayerIds",
            "WithPlayerIds", "withPlayerIds",
            "AccountId", "accountId",
            "PlayerAccountId", "playerAccountId",
            "PlayerId", "playerId",
            "UserId", "userId",
            "RecipientId", "recipientId",
            "ReceiverId", "receiverId",
            "TargetId", "targetId",
            "OtherPlayerId", "otherPlayerId",
            "Id", "id",
            "a"
        };

        private static readonly string[] ChatMessageKeys =
        {

            "MessageContents", "messageContents",
            "Contents", "contents",
            "Message", "message",
            "Body", "body",
            "Text", "text",
            "Content", "content"
        };

        [HttpGet("thread")]
        public IActionResult GetThreads(
            [FromQuery] int maxCount = 50,
            [FromQuery] int mode = 0)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            object[] threads = ChatDB
                .GetThreadsForPlayer(accountId.Value, maxCount)
                .Select(thread => BuildChatThreadDto(thread, accountId.Value, messageCount: 50))
                .ToArray();

            return Ok(threads);
        }

        [HttpPost("thread/withmembers")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> GetOrCreateThreadWithMembers()
        {

            return await CreateOrOpenChatThreadAsync(wrapResult: false);
        }

        [HttpPost("thread")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> CreateThread()
        {
            ParsedChatThreadRequest parsed = await ReadChatThreadRequestAsync();
            string? messageContents = await ReadChatMessageBodyAsync();

            if (!string.IsNullOrWhiteSpace(messageContents))
            {
                return await SendOrCreateChatMessageAsync(
                    parsed,
                    messageContents);
            }

            return await CreateOrOpenChatThreadAsync(parsed);
        }

        [HttpGet("thread/{threadId:long}")]
        public IActionResult GetThread(
            long threadId,
            [FromQuery] int messageCount = 50)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            ChatDB.ChatThread? thread = ChatDB.GetThread(threadId, accountId.Value);
            if (thread == null)
                return NotFound();

            messageCount = Math.Clamp(messageCount, 1, 100);
            object dto = BuildChatThreadDto(
                thread,
                accountId.Value,
                messageCount);

            Console.WriteLine(
                $"[CHAT OPEN] thread={threadId} viewer={accountId.Value} " +
                $"messages={ChatDB.GetMessages(threadId, accountId.Value, messageCount).Count}");

            return Ok(dto);
        }

        [HttpGet("thread/{threadId:long}/messages")]
        [HttpGet("thread/{threadId:long}/message")]
        public IActionResult GetThreadMessages(
            long threadId,
            [FromQuery] int maxCount = 50,
            [FromQuery] long? beforeMessageId = null)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            if (ChatDB.GetThread(threadId, accountId.Value) == null)
                return NotFound();

            object[] messages = ChatDB
                .GetMessages(threadId, accountId.Value, maxCount, beforeMessageId)
                .Select(BuildChatMessageDto)
                .ToArray();

            return Ok(messages);
        }

        [HttpPost("thread/{threadId:long}/message/{messageId:long}/read")]
        [HttpPut("thread/{threadId:long}/message/{messageId:long}/read")]
        public IActionResult MarkThreadMessageRead(long threadId, long messageId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            if (!ChatDB.MarkMessageRead(threadId, messageId, accountId.Value))
                return NotFound();

            Console.WriteLine(
                $"[CHAT READ] thread={threadId} message={messageId} " +
                $"viewer={accountId.Value}");

            return NoContent();
        }

        [HttpPost("thread/{threadId:long}/message")]
        [HttpPost("thread/{threadId:long}/messages")]
        [HttpPost("thread/{threadId:long}")]
        [RequestSizeLimit(32 * 1024)]
        public async Task<IActionResult> SendThreadMessage(long threadId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            ChatDB.ChatThread? thread = ChatDB.GetThread(threadId, accountId.Value);
            if (thread == null)
            {
                return NotFound(new
                {
                    chatMessage = (object?)null,
                    chatResult = 2
                });
            }

            string? rawContents = await ReadChatMessageBodyAsync();
            if (string.IsNullOrWhiteSpace(rawContents))
            {
                return BadRequest(new
                {
                    chatMessage = (object?)null,
                    chatResult = 1
                });
            }

            string contents = NormalizeChatMessageContents(rawContents);
            ChatDB.ChatMessage? message = ChatDB.AddMessage(
                threadId,
                accountId.Value,
                contents);

            if (message == null)
            {
                return BadRequest(new
                {
                    chatMessage = (object?)null,
                    chatResult = 6
                });
            }

            object messageDto = BuildChatMessageDto(message);
            long[] recipients = thread.MemberIds
                .Where(id => id != accountId.Value)
                .Distinct()
                .ToArray();

            await Task.WhenAll(recipients.Select(memberId =>
                NotiController.NotifyChatMessageReceivedAsync(
                    memberId,
                    messageDto)));

            Console.WriteLine(
                $"[CHAT LIVE] message={message.MessageId} thread={threadId} " +
                $"sender={accountId.Value} recipients={string.Join(',', recipients)}");

            return Ok(new
            {
                chatMessage = messageDto,
                chatResult = 0
            });
        }

        [HttpDelete("thread/{threadId:long}")]
        public IActionResult LeaveOrDeleteThread(long threadId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            return ChatDB.LeaveThread(threadId, accountId.Value)
                ? NoContent()
                : NotFound();
        }

        private async Task<IActionResult> CreateOrOpenChatThreadAsync(
            ParsedChatThreadRequest? parsedRequest = null,
            bool wrapResult = true)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            ParsedChatThreadRequest parsed =
                parsedRequest ?? await ReadChatThreadRequestAsync();
            HashSet<long> selectedIds = ResolveChatMemberIds(
                parsed,
                accountId.Value);

            LogChatRequest(parsed, selectedIds);

            if (selectedIds.Count == 0)
            {
                return BadRequest(new
                {
                    chatThread = (object?)null,
                    chatResult = 1
                });
            }

            if (selectedIds.Count > 99)
            {
                return BadRequest(new
                {
                    chatThread = (object?)null,
                    chatResult = 8
                });
            }

            ChatDB.ChatThread? existingThread = ChatDB.FindThreadWithMembers(
                accountId.Value,
                selectedIds);
            bool createdNewThread = existingThread == null;

            ChatDB.ChatThread thread = existingThread ?? ChatDB.GetOrCreateThread(
                accountId.Value,
                selectedIds,
                parsed.Name,
                parsed.Type);

            object dto = BuildChatThreadDto(
                thread,
                accountId.Value,
                messageCount: 50);

            if (createdNewThread)
            {
                ChatDB.ChatMessage? starterMessage = ChatDB
                    .GetMessages(
                        thread.ThreadId,
                        accountId.Value,
                        maxCount: 1)
                    .FirstOrDefault();

                if (starterMessage != null)
                {
                    object starterMessageDto = BuildChatMessageDto(starterMessage);
                    long[] liveRecipients = thread.MemberIds
                        .Where(id => id > 0)
                        .Distinct()
                        .ToArray();

                    await Task.WhenAll(liveRecipients.Select(memberId =>
                        NotiController.NotifyChatMessageReceivedAsync(
                            memberId,
                            starterMessageDto)));

                    Console.WriteLine(
                        $"[CHAT LIVE] created thread={thread.ThreadId} " +
                        $"starter={starterMessage.MessageId} " +
                        $"recipients={string.Join(',', liveRecipients)}");
                }
            }

            Console.WriteLine(
                $"[CHAT] opened thread={thread.ThreadId} requester={accountId.Value} " +
                $"members={string.Join(',', thread.MemberIds)} new={createdNewThread}");

            if (!wrapResult)
            {
                Console.WriteLine(
                    $"[CHAT RESPONSE] route=/thread/withmembers format=direct " +
                    $"thread={thread.ThreadId}");
                return Ok(dto);
            }

            Console.WriteLine(
                $"[CHAT RESPONSE] route=/thread format=wrapped " +
                $"thread={thread.ThreadId}");

            return Ok(new
            {
                chatThread = dto,
                chatResult = 0
            });
        }

        private async Task<IActionResult> SendOrCreateChatMessageAsync(
            ParsedChatThreadRequest parsed,
            string rawMessageContents)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            HashSet<long> selectedIds = ResolveChatMemberIds(
                parsed,
                accountId.Value);
            LogChatRequest(parsed, selectedIds);

            if (selectedIds.Count == 0)
            {
                return BadRequest(new
                {
                    chatThread = (object?)null,
                    chatResult = 1
                });
            }

            if (selectedIds.Count > 99)
            {
                return BadRequest(new
                {
                    chatThread = (object?)null,
                    chatResult = 8
                });
            }

            ChatDB.ChatThread thread = ChatDB.GetOrCreateThread(
                accountId.Value,
                selectedIds,
                parsed.Name,
                parsed.Type);

            string contents = NormalizeChatMessageContents(rawMessageContents);
            ChatDB.ChatMessage? message = ChatDB.AddMessage(
                thread.ThreadId,
                accountId.Value,
                contents);

            if (message == null)
            {
                return BadRequest(new
                {
                    chatThread = (object?)null,
                    chatResult = 6
                });
            }

            object messageDto = BuildChatMessageDto(message);
            long[] recipients = thread.MemberIds
                .Where(id => id != accountId.Value)
                .Distinct()
                .ToArray();

            await Task.WhenAll(recipients.Select(memberId =>
                NotiController.NotifyChatMessageReceivedAsync(
                    memberId,
                    messageDto)));

            object threadDto = BuildChatThreadDto(
                thread,
                accountId.Value,
                messageCount: 50);

            Console.WriteLine(
                $"[CHAT LIVE] message={message.MessageId} thread={thread.ThreadId} " +
                $"sender={accountId.Value} recipients={string.Join(',', recipients)}");

            return Ok(new
            {
                chatResult = 0,
                chatThread = threadDto
            });
        }

        private static HashSet<long> ResolveChatMemberIds(
            ParsedChatThreadRequest parsed,
            long requestingAccountId)
        {
            parsed.MemberIds.Remove(requestingAccountId);
            parsed.CandidateIds.Remove(requestingAccountId);

            HashSet<long> validExplicitIds = parsed.MemberIds
                .Where(ChatPlayerExists)
                .ToHashSet();

            HashSet<long> selectedIds = validExplicitIds.Count > 0
                ? validExplicitIds
                : parsed.CandidateIds
                    .Where(ChatPlayerExists)
                    .ToHashSet();

            selectedIds.Remove(requestingAccountId);
            return selectedIds;
        }

        private void LogChatRequest(
            ParsedChatThreadRequest parsed,
            IEnumerable<long> selectedIds)
        {
            Console.WriteLine(
                $"[CHAT REQUEST] contentType={Request.ContentType ?? "none"} " +
                $"contentLength={(Request.ContentLength?.ToString() ?? "null")} " +
                $"explicit={string.Join(',', parsed.MemberIds)} " +
                $"candidates={string.Join(',', parsed.CandidateIds)} " +
                $"selected={string.Join(',', selectedIds)} " +
                $"body={parsed.RawBodyPreview}");
        }

        private static bool ChatPlayerExists(long playerId) =>
            playerId > 0 && PlayerDB.Players.FindById(playerId)?.Player != null;

        private object BuildChatThreadDto(
            ChatDB.ChatThread thread,
            long viewerAccountId,
            int messageCount)
        {
            messageCount = Math.Clamp(messageCount, 1, 100);

            object[] messages = ChatDB
                .GetMessages(thread.ThreadId, viewerAccountId, messageCount)
                .Select(BuildChatMessageDto)
                .ToArray();

            int[] playerIds = thread.MemberIds
                .Where(id => id is > 0 and <= int.MaxValue)
                .Select(id => checked((int)id))
                .ToArray();

            return new
            {
                chatThreadId = thread.ThreadId,
                playerIds,
                lastReadMessageId = ChatDB.GetLastReadMessageId(
                    thread.ThreadId,
                    viewerAccountId),
                messages,
                chatThreadName = thread.Name,
                snoozedUntil = (DateTime?)null,
                isFavorited = false
            };
        }

        private static object BuildChatMessageDto(ChatDB.ChatMessage message)
        {
            int senderPlayerId = message.SenderAccountId is >= int.MinValue and <= int.MaxValue
                ? checked((int)message.SenderAccountId)
                : 0;

            return new
            {
                chatMessageId = message.MessageId,
                chatThreadId = message.ThreadId,
                senderPlayerId,
                timeSent = message.CreatedAt,
                contents = message.Body,
                moderationState = 0
            };
        }

        private async Task<ParsedChatThreadRequest> ReadChatThreadRequestAsync()
        {
            var parsed = new ParsedChatThreadRequest();

            foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> entry in Request.Query)
            {
                bool memberKey = IsChatMemberKey(entry.Key);
                foreach (string? value in entry.Value)
                {
                    if (memberKey)
                        AddChatIds(value, parsed.MemberIds);
                    AddChatIds(value, parsed.CandidateIds);
                }
            }

            parsed.Name =
                Request.Query["Name"].FirstOrDefault() ??
                Request.Query["name"].FirstOrDefault();

            if (int.TryParse(
                    Request.Query["Type"].FirstOrDefault() ??
                    Request.Query["type"].FirstOrDefault(),
                    out int queryType))
            {
                parsed.Type = queryType;
            }

            if (Request.HasFormContentType)
            {
                IFormCollection form = await Request.ReadFormAsync();

                foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> entry in form)
                {
                    bool memberKey = IsChatMemberKey(entry.Key);

                    if (memberKey || LooksLikeChatIdData(entry.Key))
                    {
                        AddChatIds(
                            entry.Key,
                            memberKey ? parsed.MemberIds : parsed.CandidateIds);
                    }

                    foreach (string? value in entry.Value)
                    {
                        if (memberKey)
                            AddChatIds(value, parsed.MemberIds);
                        AddChatIds(value, parsed.CandidateIds);
                    }
                }

                parsed.Name ??=
                    form["Name"].FirstOrDefault() ??
                    form["name"].FirstOrDefault();

                if (int.TryParse(
                        form["Type"].FirstOrDefault() ??
                        form["type"].FirstOrDefault(),
                        out int formType))
                {
                    parsed.Type = formType;
                }

                return parsed;
            }

            Request.EnableBuffering();
            if (Request.Body.CanSeek)
                Request.Body.Position = 0;

            string rawBody;
            using (var reader = new StreamReader(
                       Request.Body,
                       Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: true,
                       bufferSize: 4096,
                       leaveOpen: true))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            if (Request.Body.CanSeek)
                Request.Body.Position = 0;

            parsed.RawBodyPreview = MakeChatBodyPreview(rawBody);
            if (string.IsNullOrWhiteSpace(rawBody))
                return parsed;

            try
            {
                using JsonDocument document = JsonDocument.Parse(rawBody);
                ReadChatThreadJson(
                    document.RootElement,
                    parsed,
                    memberContext: document.RootElement.ValueKind != JsonValueKind.Object);
            }
            catch (JsonException)
            {
                foreach (string pair in rawBody.Split(
                             '&',
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = pair.Split('=', 2);
                    string key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
                    string value = parts.Length > 1
                        ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                        : string.Empty;

                    bool memberKey = IsChatMemberKey(key);
                    if (memberKey)
                        AddChatIds(value, parsed.MemberIds);
                    AddChatIds(value, parsed.CandidateIds);

                    if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                        parsed.Name = value;
                    else if (string.Equals(key, "type", StringComparison.OrdinalIgnoreCase) &&
                             int.TryParse(value, out int bodyType))
                        parsed.Type = bodyType;
                }
            }

            AddChatIds(rawBody, parsed.CandidateIds);
            return parsed;
        }

        private static void ReadChatThreadJson(
            JsonElement element,
            ParsedChatThreadRequest parsed,
            bool memberContext)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out long number) && number > 0)
                    {
                        parsed.CandidateIds.Add(number);
                        if (memberContext)
                            parsed.MemberIds.Add(number);
                    }
                    return;

                case JsonValueKind.String:
                {
                    string? value = element.GetString();
                    AddChatIds(value, parsed.CandidateIds);
                    if (memberContext)
                        AddChatIds(value, parsed.MemberIds);

                    if (!string.IsNullOrWhiteSpace(value) &&
                        (value.TrimStart().StartsWith('{') ||
                         value.TrimStart().StartsWith('[')))
                    {
                        try
                        {
                            using JsonDocument nested = JsonDocument.Parse(value);
                            ReadChatThreadJson(nested.RootElement, parsed, memberContext);
                        }
                        catch (JsonException)
                        {
                        }
                    }
                    return;
                }

                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                        ReadChatThreadJson(item, parsed, memberContext: true);
                    return;

                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (string.Equals(property.Name, "name", StringComparison.OrdinalIgnoreCase) &&
                            property.Value.ValueKind == JsonValueKind.String)
                        {
                            parsed.Name = property.Value.GetString();
                            continue;
                        }

                        if (string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase) &&
                            property.Value.TryGetInt32(out int type))
                        {
                            parsed.Type = type;
                            continue;
                        }

                        ReadChatThreadJson(
                            property.Value,
                            parsed,
                            memberContext || IsChatMemberKey(property.Name));
                    }
                    return;
            }
        }

        private static bool IsChatMemberKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (ChatMemberIdKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                return true;

            string normalized = new string(key
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

            return normalized.Contains("memberid") ||
                   normalized.Contains("playerid") ||
                   normalized.Contains("accountid") ||
                   normalized.Contains("userid") ||
                   normalized.Contains("recipientid") ||
                   normalized.Contains("receiverid") ||
                   normalized.Contains("targetid") ||
                   normalized is "members" or "players" or "accounts" or
                       "recipients" or "receivers" or "ids";
        }

        private static bool LooksLikeChatIdData(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            return char.IsDigit(trimmed[0]) ||
                   trimmed[0] is '[' or '{' or '"';
        }

        private static string MakeChatBodyPreview(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "<empty>";

            string oneLine = body
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            return oneLine.Length <= 500
                ? oneLine
                : oneLine[..500] + "...";
        }

        private async Task<string?> ReadChatMessageBodyAsync()
        {
            foreach (string key in ChatMessageKeys)
            {
                string? value = Request.Query[key].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            if (Request.HasFormContentType)
            {
                IFormCollection form = await Request.ReadFormAsync();
                foreach (string key in ChatMessageKeys)
                {
                    string? value = form[key].FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }

                return null;
            }

            Request.EnableBuffering();
            if (Request.Body.CanSeek)
                Request.Body.Position = 0;

            string rawBody;
            using (var reader = new StreamReader(
                       Request.Body,
                       Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: true,
                       bufferSize: 4096,
                       leaveOpen: true))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            if (Request.Body.CanSeek)
                Request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(rawBody))
                return null;

            try
            {
                using JsonDocument document = JsonDocument.Parse(rawBody);
                string? found = FindChatContents(
                    document.RootElement,
                    ChatMessageKeys,
                    allowBareString: true);

                if (!string.IsNullOrWhiteSpace(found))
                    return found;

                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    (TryGetJsonPropertyIgnoreCase(
                         document.RootElement,
                         "Data",
                         out _) ||
                     TryGetJsonPropertyIgnoreCase(
                         document.RootElement,
                         "Type",
                         out _)))
                {
                    return document.RootElement.GetRawText();
                }
            }
            catch (JsonException)
            {
                foreach (string pair in rawBody.Split(
                             '&',
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = pair.Split('=', 2);
                    string key = Uri.UnescapeDataString(
                        parts[0].Replace('+', ' '));
                    string value = parts.Length > 1
                        ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                        : string.Empty;

                    if (ChatMessageKeys.Contains(
                            key,
                            StringComparer.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                return rawBody.Trim().Trim('"');
            }

            return null;
        }

        private static string? FindChatContents(
            JsonElement element,
            IReadOnlyCollection<string> keys,
            bool allowBareString)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                return allowBareString
                    ? element.GetString()
                    : null;
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!keys.Contains(
                            property.Name,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.GetRawText();
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string? nested = FindChatContents(
                        property.Value,
                        keys,
                        allowBareString: false);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string? nested = FindChatContents(
                        item,
                        keys,
                        allowBareString: false);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }

            return null;
        }

        private static bool TryGetJsonPropertyIgnoreCase(
            JsonElement element,
            string name,
            out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (string.Equals(
                            property.Name,
                            name,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static string NormalizeChatMessageContents(string rawContents)
        {
            string value = rawContents.Trim();

            try
            {
                using JsonDocument document = JsonDocument.Parse(value);

                if (document.RootElement.ValueKind == JsonValueKind.String)
                {
                    value = document.RootElement.GetString() ?? string.Empty;
                }
                else if (document.RootElement.ValueKind is
                         JsonValueKind.Object or JsonValueKind.Array)
                {
                    return document.RootElement.GetRawText();
                }
            }
            catch (JsonException)
            {
            }

            return JsonSerializer.Serialize(new
            {
                Data = value,
                Type = 0,
                Version = 1,
                Blocks = new[]
                {
                    new
                    {
                        Placeholder = value,
                        Type = "Text"
                    }
                }
            });
        }

        private static void AddChatIds(string? value, ISet<long> output)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(
                         value,
                         @"(?<![0-9])-?[0-9]+"))
            {
                if (long.TryParse(match.Value, out long id) && id > 0)
                    output.Add(id);
            }
        }

        private sealed class ParsedChatThreadRequest
        {
            public HashSet<long> MemberIds { get; } = new();
            public HashSet<long> CandidateIds { get; } = new();
            public string? Name { get; set; }
            public int Type { get; set; }
            public string RawBodyPreview { get; set; } = "<not-read>";
        }

        [HttpGet("api/inventions/v1/featured")]
        public IActionResult GetFeaturedInventions(
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            return Ok(CreatorFeatureDB.SearchInventions(skip: skip, take: take)
                .Select(CreatorFeatureDB.ToClientInvention)
                .ToArray());
        }

        [HttpPost("/api/inventions/v6/save")]
        [HttpPost("/api/inventions/v5/save")]
        [HttpPost("/api/inventions/v4/save")]
        [HttpPost("/api/inventions/v3/save")]
        [HttpPost("/api/inventions/v2/save")]
        [HttpPost("/api/inventions/v1/save")]
        [HttpPut("/api/inventions/v6/save")]
        [HttpPut("/api/inventions/v5/save")]
        [HttpPut("/api/inventions/v4/save")]
        [HttpPut("/api/inventions/v3/save")]
        [HttpPut("/api/inventions/v2/save")]
        [HttpPut("/api/inventions/v1/save")]
        [HttpGet("/api/inventions/v3/update")]
        [HttpGet("/api/inventions/v2/update")]
        [HttpGet("/api/inventions/v1/update")]
        [HttpPost("/api/inventions/v3/update")]
        [HttpPost("/api/inventions/v2/update")]
        [HttpPost("/api/inventions/v1/update")]
        [HttpPut("/api/inventions/v3/update")]
        [HttpPut("/api/inventions/v2/update")]
        [HttpPut("/api/inventions/v1/update")]
        [HttpPost("/api/inventions/v6")]
        [HttpPut("/api/inventions/v6")]
        [HttpPost("/api/inventions/v5")]
        [HttpPut("/api/inventions/v5")]
        [HttpPost("/api/inventions/v4")]
        [HttpPut("/api/inventions/v4")]
        [HttpPost("/api/inventions/v3")]
        [HttpPut("/api/inventions/v3")]
        [HttpPost("/api/inventions/v2")]
        [HttpPut("/api/inventions/v2")]
        [HttpPost("/api/inventions/v1")]
        [HttpPut("/api/inventions/v1")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024)]
        public async Task<IActionResult> SaveInvention()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            JsonElement request;
            var createdFiles = new List<string>();
            try
            {
                if (Request.HasFormContentType)
                {
                    IFormCollection form = await Request.ReadFormAsync(
                        HttpContext.RequestAborted);
                    var payload = new JsonObject();
                    foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> field
                             in form)
                    {
                        if (field.Value.Count > 0)
                            payload[field.Key] = field.Value[field.Value.Count - 1];
                    }

                    IFormFile? previewFile = form.Files.FirstOrDefault(
                        IsInventionPreviewFile);
                    IFormFile? dataFile = form.Files
                        .Where(file =>
                            !ReferenceEquals(file, previewFile) &&
                            !IsInventionPreviewFile(file))
                        .OrderByDescending(IsInventionDataFile)
                        .ThenByDescending(file => file.Length)
                        .FirstOrDefault();

                    if (previewFile == null && dataFile == null && form.Files.Count > 0)
                    {
                        IFormFile first = form.Files[0];
                        if (IsInventionPreviewFile(first))
                            previewFile = first;
                        else
                            dataFile = first;
                    }

                    if (previewFile != null && previewFile.Length > 0)
                    {
                        string imageName = await SaveInventionPreviewAsync(
                            previewFile,
                            createdFiles,
                            HttpContext.RequestAborted);
                        payload["ImageName"] = imageName;
                        payload["Image"] = imageName;
                        payload["PreviewImageName"] = imageName;
                    }

                    if (dataFile != null && dataFile.Length > 0)
                    {
                        string dataBlob = await SaveInventionDataAsync(
                            dataFile,
                            createdFiles,
                            HttpContext.RequestAborted);
                        payload["DataBlob"] = dataBlob;
                        payload["DataBlobName"] = Path.GetFileName(dataBlob);
                        payload["Filename"] = Path.GetFileName(dataBlob);
                    }

                    request = JsonSerializer.SerializeToElement(payload);
                }
                else if ((Request.ContentLength ?? 0) > 0)
                {
                    using JsonDocument document = await JsonDocument.ParseAsync(
                        Request.Body,
                        cancellationToken: HttpContext.RequestAborted);
                    request = document.RootElement.Clone();
                }
                else
                {

                    using JsonDocument emptyDocument = JsonDocument.Parse("{}");
                    request = emptyDocument.RootElement.Clone();
                }

                request = NormalizeInventionRequest(request, createdFiles);
                request = MergeInventionQuery(request, Request.Query);
            }
            catch (JsonException)
            {
                DeleteNewInventionFiles(createdFiles);
                return BadRequest(new { Success = false, Error = "invalid_invention_payload" });
            }
            catch (InvalidDataException exception)
            {
                DeleteNewInventionFiles(createdFiles);
                return BadRequest(new { Success = false, Error = exception.Message });
            }
            catch (IOException exception)
            {
                DeleteNewInventionFiles(createdFiles);
                Console.WriteLine($"[INVENTIONS] upload failed: {exception.Message}");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { Success = false, Error = "invention_file_save_failed" });
            }

            try
            {
                CreatorFeatureDB.InventionRecord record =
                    CreatorFeatureDB.SaveInvention(accountId.Value, request);

                Console.WriteLine(
                    $"[INVENTIONS] saved={record.InventionId} " +
                    $"creator={accountId.Value} room={record.RoomId} " +
                    $"version={record.Version}");

                bool hasValidObjects = CreatorFeatureDB.HasValidInventionBlob(record);
                object clientValue = CreatorFeatureDB.ToClientInvention(record);
                if (!hasValidObjects)
                {
                    Console.WriteLine(
                        $"[INVENTIONS] missing-object-blob id={record.InventionId} " +
                        $"creator={record.CreatorAccountId} dataBlob={record.DataBlob}");
                }

                object? clientVersion = CreatorFeatureDB.ToClientInventionVersion(record);
                return Ok(new
                {

                    Result = hasValidObjects ? 0 : 8,
                    InventionResponse = clientValue,
                    InventionVersionResponse = clientVersion,

                    InventionId = record.InventionId,
                    Id = record.InventionId,
                    Version = record.Version,
                    Success = hasValidObjects,
                    CanSpawn = hasValidObjects,
                    Error = hasValidObjects ? null : "invention_object_blob_missing",
                    Value = clientValue,
                    Invention = clientValue,
                    CurrentVersion = clientVersion
                });
            }
            catch (UnauthorizedAccessException exception)
            {
                DeleteNewInventionFiles(createdFiles);
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new { Success = false, Error = exception.Message });
            }
            catch (ArgumentException exception)
            {
                DeleteNewInventionFiles(createdFiles);
                return BadRequest(new { Success = false, Error = exception.Message });
            }
        }

        private static JsonElement MergeInventionQuery(
            JsonElement request,
            IQueryCollection query)
        {
            if (query.Count == 0)
                return request;

            JsonObject payload = request.ValueKind == JsonValueKind.Object
                ? JsonNode.Parse(request.GetRawText())?.AsObject() ?? new JsonObject()
                : new JsonObject { ["Value"] = request.GetRawText() };

            foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> field
                     in query)
            {
                if (payload.ContainsKey(field.Key) && payload[field.Key] != null)
                    continue;
                if (field.Value.Count == 1)
                    payload[field.Key] = field.Value[0];
                else if (field.Value.Count > 1)
                    payload[field.Key] = new JsonArray(
                        field.Value.Select(value => JsonValue.Create(value)).ToArray());
            }

            MergeNestedInventionMetadata(payload);
            return JsonSerializer.SerializeToElement(payload);
        }

        private static JsonElement NormalizeInventionRequest(
            JsonElement request,
            ICollection<string> createdFiles)
        {
            JsonObject payload = request.ValueKind == JsonValueKind.Object
                ? JsonNode.Parse(request.GetRawText())?.AsObject() ?? new JsonObject()
                : new JsonObject { ["Value"] = request.GetRawText() };

            MergeNestedInventionMetadata(payload);
            PromoteUploadedInventionBlob(payload, createdFiles);
            return JsonSerializer.SerializeToElement(payload);
        }

        private static void MergeNestedInventionMetadata(JsonObject payload)
        {
            foreach (string key in new[]
                     {
                         "invention", "Invention", "metadata", "Metadata",
                         "inventionData", "InventionData",
                         "inventionMetadata", "InventionMetadata",
                         "request", "Request", "payload", "Payload",
                         "data", "Data", "value", "Value", "json", "Json"
                     })
            {
                if (!payload.TryGetPropertyValue(key, out JsonNode? node) || node == null)
                    continue;

                JsonObject? nested = node as JsonObject;
                if (nested == null && node is JsonValue value &&
                    value.TryGetValue<string>(out string? raw) &&
                    !string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        nested = JsonNode.Parse(raw) as JsonObject;
                    }
                    catch (JsonException)
                    {
                    }
                }

                if (nested == null)
                    continue;

                foreach (KeyValuePair<string, JsonNode?> property in nested.ToArray())
                {
                    if (!payload.ContainsKey(property.Key) || payload[property.Key] == null)
                        payload[property.Key] = property.Value?.DeepClone();
                }
            }
        }

        private static void PromoteUploadedInventionBlob(
            JsonObject payload,
            ICollection<string> createdFiles)
        {
            string? raw = ReadJsonNodeString(
                payload,
                "DataBlob", "dataBlob", "DataBlobName", "dataBlobName",
                "Filename", "filename", "FileName", "fileName",
                "ObjectDataFilename", "objectDataFilename",
                "ObjectDataBlob", "objectDataBlob",
                "BlobName", "blobName");
            if (string.IsNullOrWhiteSpace(raw))
                return;

            string normalized = raw.Replace('\\', '/').Trim();
            if (normalized.StartsWith("/cdn/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[5..];
            normalized = normalized.TrimStart('/');
            if (normalized.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException("The invention blob path is invalid.");

            string safeName = Path.GetFileName(normalized);
            if (safeName.Length == 0 || safeName.Length > 180)
                throw new InvalidDataException("The invention blob name is invalid.");

            string baseName = Path.GetFileNameWithoutExtension(safeName);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = safeName;
            string canonicalName = safeName.EndsWith(
                    ".inv",
                    StringComparison.OrdinalIgnoreCase)
                ? safeName
                : baseName + ".inv";

            string inventionDirectory = Path.Combine(Program.dataDir, "CDN", "invention");
            Directory.CreateDirectory(inventionDirectory);
            string inventionPath = Path.Combine(inventionDirectory, canonicalName);
            string existingInventionPath = Path.Combine(inventionDirectory, safeName);
            string uploadedRoomPath = Path.Combine(Program.dataDir, "CDN", "room", safeName);
            string uploadedCanonicalRoomPath = Path.Combine(
                Program.dataDir,
                "CDN",
                "room",
                canonicalName);

            string? sourcePath = new[]
                {
                    existingInventionPath,
                    uploadedRoomPath,
                    uploadedCanonicalRoomPath
                }
                .FirstOrDefault(System.IO.File.Exists);
            if (!System.IO.File.Exists(inventionPath) && sourcePath != null)
            {
                System.IO.File.Copy(sourcePath, inventionPath, overwrite: false);
                createdFiles.Add(inventionPath);
            }

            string storedPath = "invention/" + canonicalName;
            payload["DataBlob"] = storedPath;
            payload["dataBlob"] = storedPath;
            payload["DataBlobName"] = canonicalName;
            payload["Filename"] = canonicalName;
            payload["ObjectDataFilename"] = canonicalName;
            payload["ObjectDataBlob"] = canonicalName;
        }

        private static string? ReadJsonNodeString(
            JsonObject payload,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                if (!payload.TryGetPropertyValue(key, out JsonNode? node) || node == null)
                    continue;
                if (node is JsonValue value && value.TryGetValue<string>(out string? text))
                    return text;
                string raw = node.ToJsonString().Trim();
                if (raw.Length > 1 && raw[0] == '"' && raw[^1] == '"')
                {
                    try
                    {
                        return JsonSerializer.Deserialize<string>(raw);
                    }
                    catch (JsonException)
                    {
                    }
                }
                return raw;
            }
            return null;
        }

        private static bool IsInventionDataFile(IFormFile file)
        {
            string fieldName = file.Name ?? string.Empty;
            if (fieldName.Contains("object", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("blob", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("data", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Equals("file", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("invention", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string extension = Path.GetExtension(file.FileName);
            return !extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                   !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                   !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
                   !extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) &&
                   !extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) &&
                   !extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInventionPreviewFile(IFormFile file)
        {
            string fieldName = file.Name ?? string.Empty;
            if (fieldName.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("thumbnail", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("photo", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Contains("icon", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (file.ContentType?.StartsWith(
                    "image/",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            string extension = Path.GetExtension(file.FileName);
            return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<string> SaveInventionPreviewAsync(
            IFormFile file,
            ICollection<string> createdFiles,
            CancellationToken cancellationToken)
        {
            const long maxPreviewBytes = 8L * 1024L * 1024L;
            if (file.Length <= 0 || file.Length > maxPreviewBytes)
                throw new InvalidDataException("Invention previews must be 8 MB or smaller.");

            byte[] bytes;
            await using (var buffer = new MemoryStream())
            {
                await file.CopyToAsync(buffer, cancellationToken);
                bytes = buffer.ToArray();
            }

            SixLabors.ImageSharp.ImageInfo? info = Image.Identify(bytes);
            SixLabors.ImageSharp.Formats.IImageFormat? format = Image.DetectFormat(bytes);
            if (info == null || format == null ||
                info.Width <= 0 || info.Height <= 0 ||
                info.Width > 8192 || info.Height > 8192 ||
                (long)info.Width * info.Height > 40_000_000)
            {
                throw new InvalidDataException("The invention preview is not a safe image.");
            }

            string extension = format.FileExtensions
                .Select(value => value.Trim().TrimStart('.').ToLowerInvariant())
                .FirstOrDefault(value => value.Length is > 0 and <= 8 &&
                    value.All(char.IsLetterOrDigit)) ?? "png";
            string filename = $"{Guid.NewGuid():N}.{extension}";
            string relativePath = $"Inventions/{filename}";
            string directory = Path.Combine(Program.dataDir, "Images", "Inventions");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, filename);
            await System.IO.File.WriteAllBytesAsync(path, bytes, cancellationToken);
            createdFiles.Add(path);
            return relativePath;
        }

        private static async Task<string> SaveInventionDataAsync(
            IFormFile file,
            ICollection<string> createdFiles,
            CancellationToken cancellationToken)
        {
            const long maxDataBytes = 9L * 1024L * 1024L;
            if (file.Length <= 0 || file.Length > maxDataBytes)
                throw new InvalidDataException("Invention data must be 9 MB or smaller.");

            string filename = $"{Guid.NewGuid():N}.inv";
            string directory = Path.Combine(Program.dataDir, "CDN", "invention");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, filename);
            await using (var output = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await file.CopyToAsync(output, cancellationToken);
            }
            createdFiles.Add(path);
            return $"invention/{filename}";
        }

        private static void DeleteNewInventionFiles(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                try
                {
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        [HttpGet("api/customAvatarItems/v1/hot")]
        public IActionResult GetHotCustomAvatarItems()
        {
            var items = new[]
            {
        new { itemId = 101, name = "Fire Hoodie", tags = new[] { "hot", "new" } },
        new { itemId = 102, name = "Drip Sneakers", tags = new[] { "hot" } },
        new { itemId = 103, name = "Sus Hat", tags = new[] { "trending" } }
    };
            return Ok(items);
        }

        [HttpGet("/config/categories")]
        public IActionResult GetConfigCategories()
        {
            string path = Path.Combine(Program.dataDir, "APIS", "GameConfigs.json");
            if (!System.IO.File.Exists(path))
                return Ok(new { Categories = Array.Empty<object>() });

            string json = System.IO.File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (entries == null)
                return Ok(new { Categories = Array.Empty<object>() });

            var categoryEntry = entries.FirstOrDefault(entry => entry.TryGetValue("Key", out var key) && key?.ToString() == "Growth.RoomCategoriesJson");
            if (categoryEntry == null || !categoryEntry.TryGetValue("Value", out var value) || value == null)
                return Ok(new { Categories = Array.Empty<object>() });

            try
            {
                JsonElement categories = JsonSerializer.Deserialize<JsonElement>(
                    value.ToString() ?? "[]");
                return Ok(new { Categories = categories });
            }
            catch
            {
                return Ok(new { Categories = Array.Empty<object>() });
            }
        }

        [HttpGet("/api/PlayerCheer/v1/images")]
        public IActionResult GetPlayerCheersForImages([FromQuery] List<string> imageIds)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(new Dictionary<string, object>());
        }

        [HttpPost("/api/PlayerCheer/v1/create")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> CreatePlayerCheer()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            var values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var queryValue in Request.Query)
                values[queryValue.Key] = queryValue.Value.ToString();

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (var formValue in form)
                    values[formValue.Key] = formValue.Value.ToString();
            }
            else
            {
                string body;
                using (var reader = new StreamReader(Request.Body))
                    body = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(body))
                    body = string.Empty;

                try
                {
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        using var document = JsonDocument.Parse(body);
                        AddJsonValues(document.RootElement, values);
                    }
                }
                catch (JsonException)
                {

                    foreach (var bodyValue in
                        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
                            "?" + body))
                    {
                        values[bodyValue.Key] = bodyValue.Value.ToString();
                    }
                }
            }

            foreach (string embeddedValue in values.Values.ToArray())
            {
                if (!embeddedValue.TrimStart().StartsWith('{'))
                    continue;

                try
                {
                    using var embedded = JsonDocument.Parse(embeddedValue);
                    AddJsonValues(embedded.RootElement, values);
                }
                catch (JsonException)
                {

                }
            }

            string? targetText = GetValue(values,
                "ReceiverAccountId", "ReceiverPlayerId", "ReceiverId",
                "CheerReceiverAccountId", "CheerReceiverPlayerId",
                "CheeredAccountId", "CheeredPlayerId", "PlayerToCheerId",
                "TargetAccountId", "TargetPlayerId", "ToAccountId",
                "ToPlayerId", "PlayerId", "PlayerID", "AccountId", "id");
            string? categoryText = GetValue(values,
                "CheerCategory", "CheerCategoryId", "CheerType",
                "Category", "CategoryId", "cheer");
            string? anonymousText = GetValue(values,
                "Anonymous", "anonymous", "IsAnonymous", "isAnonymous",
                "SendAnonymously", "sendAnonymously");

            if (!long.TryParse(targetText, out long receiverId))
            {

                var targetValue = values.FirstOrDefault(value =>
                    (value.Key.Contains("player", StringComparison.OrdinalIgnoreCase) ||
                     value.Key.Contains("account", StringComparison.OrdinalIgnoreCase) ||
                     value.Key.Contains("receiver", StringComparison.OrdinalIgnoreCase) ||
                     value.Key.Contains("target", StringComparison.OrdinalIgnoreCase)) &&
                    long.TryParse(value.Value, out long candidate) &&
                    candidate != (long)id);

                if (!long.TryParse(targetValue.Value, out receiverId))
                    return BadRequest(new { success = false, error = "invalid_player" });
            }

            if (!TryParseCheerCategory(categoryText, out CheerCategory category))
                category = CheerCategory.General;

            bool anonymous = ParseLooseBoolean(anonymousText);

            var cheer = PlayerDB.GivePlayerCheer(
                (long)id,
                receiverId,
                category,
                out NotificationDB.ClientNotification? notification,
                anonymous);

            if (cheer != null && notification != null)
            {

                await NotiController.NotifyCheerAsync(
                    receiverId,
                    notification,
                    (int)category);
            }

            Console.WriteLine(
                $"[PLAYER CHEER] sender={id.Value} receiver={receiverId} " +
                $"category={category} anonymous={anonymous} " +
                $"created={cheer != null}");

            return cheer == null
                ? BadRequest(new
                {
                    success = false,
                    message = "Unable to send cheer. Check the player and your available cheers.",
                    error = "invalid_player_or_no_cheer_credit"
                })
                : Ok(new
                {
                    success = true,
                    message = "Cheer sent.",
                    cheerId = cheer.CheerId,
                    notificationId = cheer.NotificationId,
                    remainingCheerCredits = PlayerDB
                        .GetReputationBulk(new List<long> { id.Value })
                        .FirstOrDefault()?.CheerCredit ?? 0
                });
        }

        [HttpGet("/api/PlayerCheer/v1/pending")]
        public IActionResult GetPendingPlayerCheers()
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            return Ok(PlayerDB.GetPendingPlayerCheers(playerId.Value)
                .Select(cheer => new
                {
                    cheerId = cheer.CheerId,
                    notificationId = cheer.NotificationId,
                    fromPlayerId = cheer.Anonymous
                        ? 0
                        : cheer.GiverPlayerId,
                    receiverPlayerId = cheer.ReceiverPlayerId,
                    cheerCategory = (int)cheer.CheerCategory,
                    anonymous = cheer.Anonymous,
                    status = (int)cheer.Status,
                    createdAt = cheer.CreatedAt
                }));
        }

        [HttpPost("/api/PlayerCheer/v1/accept")]
        [RequestSizeLimit(16 * 1024)]
        public Task<IActionResult> AcceptPlayerCheer() =>
            ResolvePlayerCheerAsync(CheerStatus.Accepted);

        [HttpPost("/api/PlayerCheer/v1/dismiss")]
        [RequestSizeLimit(16 * 1024)]
        public Task<IActionResult> DismissPlayerCheer() =>
            ResolvePlayerCheerAsync(CheerStatus.Dismissed);

        private async Task<IActionResult> ResolvePlayerCheerAsync(
            CheerStatus status)
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            HashSet<long> ids = await ReadLongValuesAsync(
                "CheerIds", "cheerIds", "CheerId", "cheerId",
                "NotificationIds", "notificationIds",
                "NotificationId", "notificationId",
                "MessageIds", "messageIds", "MessageId", "messageId",
                "Id", "id");

            if (ids.Count == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "missing_cheer_id"
                });
            }

            List<PlayerCheerRecord> matching =
                PlayerDB.GetPendingPlayerCheers(playerId.Value)
                    .Where(cheer =>
                        ids.Contains(cheer.CheerId) ||
                        ids.Contains(cheer.NotificationId))
                    .ToList();

            int resolved = PlayerDB.ResolvePlayerCheers(
                playerId.Value,
                ids,
                status);

            int deleted = NotificationDB.DeleteMessages(
                playerId.Value,
                matching.Select(cheer => cheer.NotificationId));

            return Ok(new
            {
                success = resolved > 0,
                resolved,
                deleted,
                status = status.ToString()
            });
        }

        [HttpPost("/api/PlayerCheer/v1/SetSelectedCheer")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> SetSelectedCheer()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player?.Player == null)
                return Unauthorized();

            string? categoryText = Request.Query["CheerCategory"].FirstOrDefault();
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                categoryText ??= form["CheerCategory"].FirstOrDefault();
            }

            if (!int.TryParse(categoryText, out int rawCategory) ||
                !Enum.IsDefined(typeof(CheerCategory), rawCategory))
                return BadRequest();

            bool isDeveloper = player.PlayerRoles?.Contains(
                PlayerDBClasses.PlayerRoles.Developer) == true;
            if (!isDeveloper &&
                rawCategory == (int)CheerCategory.RecRoomDeveloper)
                return StatusCode(403);

            player.Player.Reputation ??= new Reputation();
            player.Player.Reputation.IsCheerful = true;
            player.Player.Reputation.SelectedCheer = (CheerCategory)rawCategory;
            PlayerDB.Players.Update(player);
            return NoContent();
        }

        private static string? GetValue(
            Dictionary<string, string> values,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                if (values.TryGetValue(key, out string? value) &&
                    !string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        private static void AddJsonValues(
            JsonElement element,
            Dictionary<string, string> values)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Object or
                        JsonValueKind.Array)
                    {
                        AddJsonValues(property.Value, values);
                    }
                    else
                    {
                        values[property.Name] = property.Value.ToString();
                    }
                }

                return;
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    AddJsonValues(item, values);
            }
        }

        private static bool TryParseCheerCategory(
            string? value,
            out CheerCategory category)
        {
            category = CheerCategory.General;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (int.TryParse(value, out int rawCategory) &&
                Enum.IsDefined(typeof(CheerCategory), rawCategory))
            {
                category = (CheerCategory)rawCategory;
                return category is CheerCategory.General or
                    CheerCategory.Helpful or
                    CheerCategory.Sportmanship or
                    CheerCategory.GreatHost or
                    CheerCategory.Creative;
            }

            string normalized = value.Replace(" ", string.Empty)
                .Replace("Sportsmanship", "Sportmanship",
                    StringComparison.OrdinalIgnoreCase)
                .Replace("GoodSport", "Sportmanship",
                    StringComparison.OrdinalIgnoreCase);
            return Enum.TryParse(normalized, true, out category) &&
                   category is CheerCategory.General or
                       CheerCategory.Helpful or
                       CheerCategory.Sportmanship or
                       CheerCategory.GreatHost or
                       CheerCategory.Creative;
        }

        private static bool ParseLooseBoolean(string? value)
        {
            if (bool.TryParse(value, out bool parsed))
                return parsed;

            return string.Equals(value, "1", StringComparison.Ordinal) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        [HttpGet("/api/Leaderboard/v1/room/{roomId}")]
        public IActionResult GetRoomLeaderboard(long roomId)
        {
            return Ok(new
            {
                RoomId = roomId,
                Results = Array.Empty<object>(),
                TotalResults = 0
            });
        }

        [HttpGet("/api/catalog/v1/all")]
        public IActionResult GetCatalogAll(
            [FromQuery] bool onlyAvailableSkus = false)
        {
            return Ok(GetCatalogSkus());
        }

        [HttpPost("/api/storefronts/v1/purchase")]
        [HttpPost("/api/storefronts/v1/purchase/{skuId:long}")]
        [HttpPost("/api/storefronts/v2/purchase")]
        [HttpPost("/api/storefronts/v2/purchase/{skuId:long}")]
        [HttpPost("/api/storefronts/v2/buyItem")]
        [HttpPost("/api/storefronts/v3/purchase")]
        [HttpPost("/api/storefronts/v3/purchase/{skuId:long}")]
        [HttpPost("/api/storefronts/v4/purchase")]
        [HttpPost("/api/storefronts/v4/purchase/{skuId:long}")]
        [HttpPost("/api/catalog/v1/purchase")]
        [HttpPost("/api/catalog/v1/purchase/{skuId:long}")]
        [HttpPost("/purchase/v1/purchase")]
        [RequestSizeLimit(128 * 1024)]
        public async Task<IActionResult> PurchaseCatalogItem()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            ParsedCatalogPurchaseRequest purchaseRequest =
                await ReadCatalogPurchaseRequestAsync();
            purchaseRequest.Message = purchaseRequest.Message.Trim();
            if (purchaseRequest.Message.Length > 200)
            {
                return BadRequest(new
                {
                    Success = false,
                    Error = "gift_message_too_long"
                });
            }
            long? requestedSkuId = purchaseRequest.SkuId;

            Console.WriteLine(
                $"[STORE PURCHASE] buyer={accountId.Value} " +
                $"sku={requestedSkuId?.ToString() ?? "null"} " +
                $"recipient={purchaseRequest.RecipientPlayerId?.ToString() ?? "none"} " +
                $"giftContext={purchaseRequest.GiftContext}");

            if (!requestedSkuId.HasValue)
                return BadRequest(new { Success = false, Error = "missing_sku" });

            CatalogSku? sku = GetCatalogSkus().FirstOrDefault(value =>
                value.SkuId == requestedSkuId.Value ||
                value.PurchasableItemId == requestedSkuId.Value ||
                (value.AvatarItemId > 0 && value.AvatarItemId == requestedSkuId.Value) ||
                (IsConsumableCatalogItem(value) && value.ItemId == requestedSkuId.Value));

            if (sku == null)
                return NotFound(new { Success = false, Error = "sku_not_found" });

            long recipientId = purchaseRequest.RecipientPlayerId.GetValueOrDefault();
            bool isGift = recipientId > 0 && recipientId != accountId.Value;

            if (isGift)
            {
                if (recipientId > int.MaxValue ||
                    PlayerDB.Players.FindById(recipientId)?.Player == null)
                {
                    return NotFound(new
                    {
                        Success = false,
                        Error = "gift_recipient_not_found"
                    });
                }

                bool gifted = TryPurchaseCatalogSkuAsGift(
                    accountId.Value,
                    recipientId,
                    sku,
                    purchaseRequest.Message,
                    purchaseRequest.GiftContext,
                    out int giftBalance,
                    out bool recipientAlreadyOwned,
                    out GiftPackage? giftPackage);

                if (!gifted)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Error = "insufficient_funds",
                        Balance = giftBalance,
                        Price = StoreItemPrice
                    });
                }

                if (giftPackage != null)
                {
                    await NotiController.NotifyGiftAsync(
                        accountId.Value,
                        recipientId,
                        giftPackage);
                }

                Console.WriteLine(
                    $"[STORE GIFT] from={accountId.Value} to={recipientId} " +
                    $"sku={sku.SkuId} item={sku.FriendlyName} " +
                    $"package={giftPackage?.GiftPackageId ?? 0} " +
                    $"alreadyOwned={recipientAlreadyOwned}");

                return Ok(new
                {
                    Success = true,
                    Result = 0,
                    PurchaseResult = 0,
                    WasGift = true,
                    RecipientPlayerId = recipientId,
                    GiftPackageId = giftPackage?.GiftPackageId ?? 0,
                    AlreadyOwned = recipientAlreadyOwned,
                    Balance = giftBalance,
                    CurrencyType = (int)PlayerDBClasses.CurrencyType.RecCenterTokens,
                    BalanceType = (int)PlayerDBClasses.BalanceType.NonPurchasedDefault,
                    NewBalance = giftBalance,
                    Price = StoreItemPrice,
                    SkuId = sku.SkuId,
                    PurchasableItemId = sku.PurchasableItemId,
                    ItemId = sku.ItemId,
                    AvatarItemId = sku.AvatarItemId,
                    ConsumableItemDesc = sku.ConsumableItemDesc,
                    BalanceUpdates = PlayerDB.GetAllCurrencyBalances(accountId.Value)
                        .Select(entry => new
                        {
                            Result = 0,
                            PurchaseResult = 0,
                            Balance = entry.CurrencyType == PlayerDBClasses.CurrencyType.RecCenterTokens
                                ? giftBalance
                                : entry.Balance,
                            CurrencyType = (int)entry.CurrencyType,
                            Data = entry.CurrencyType == PlayerDBClasses.CurrencyType.RecCenterTokens
                                ? new object[]
                                {
                                    new
                                    {
                                        SkuId = sku.SkuId,
                                        ItemId = sku.ItemId,
                                        AvatarItemId = sku.AvatarItemId,
                                        PurchasableItemId = sku.PurchasableItemId,
                                        ConsumableItemDesc = sku.ConsumableItemDesc,
                                        AvatarItemDesc = sku.AvatarItemDesc,
                                        FriendlyName = sku.FriendlyName,
                                        Quantity = 1,
                                        Amount = 1,
                                        CurrencyType = (int)PlayerDBClasses.CurrencyType.RecCenterTokens,
                                        Result = 0
                                    }
                                }
                                : Array.Empty<object>()
                        })
                        .ToArray()
                });
            }

            bool purchased = TryPurchaseCatalogSku(
                accountId.Value,
                sku,
                out int newBalance,
                out bool alreadyOwned);

            if (!purchased)
            {
                return BadRequest(new
                {
                    Success = false,
                    Error = "insufficient_funds",
                    Balance = newBalance,
                    Price = StoreItemPrice
                });
            }

            return Ok(new
            {
                Success = true,
                Result = 0,
                PurchaseResult = 0,
                WasGift = false,
                AlreadyOwned = alreadyOwned,
                Balance = newBalance,
                CurrencyType = (int)PlayerDBClasses.CurrencyType.RecCenterTokens,
                BalanceType = (int)PlayerDBClasses.BalanceType.NonPurchasedDefault,
                NewBalance = newBalance,
                Price = StoreItemPrice,
                SkuId = sku.SkuId,
                PurchasableItemId = sku.PurchasableItemId,
                ItemId = sku.ItemId,
                AvatarItemId = sku.AvatarItemId,
                ConsumableItemDesc = sku.ConsumableItemDesc,
                BalanceUpdates = PlayerDB.GetAllCurrencyBalances(accountId.Value)
                    .Select(entry => new
                    {
                        Result = 0,
                        PurchaseResult = 0,
                        Balance = entry.CurrencyType == PlayerDBClasses.CurrencyType.RecCenterTokens
                            ? newBalance
                            : entry.Balance,
                        CurrencyType = (int)entry.CurrencyType,
                        Data = entry.CurrencyType == PlayerDBClasses.CurrencyType.RecCenterTokens
                            ? new object[]
                            {
                                new
                                {
                                    SkuId = sku.SkuId,
                                    ItemId = sku.ItemId,
                                    AvatarItemId = sku.AvatarItemId,
                                    PurchasableItemId = sku.PurchasableItemId,
                                    ConsumableItemDesc = sku.ConsumableItemDesc,
                                    AvatarItemDesc = sku.AvatarItemDesc,
                                    FriendlyName = sku.FriendlyName,
                                    Quantity = 1,
                                    Amount = 1,
                                    CurrencyType = (int)PlayerDBClasses.CurrencyType.RecCenterTokens,
                                    Result = 0
                                }
                            }
                            : Array.Empty<object>()
                    })
                    .ToArray()
            });
        }

        private sealed class ParsedCatalogPurchaseRequest
        {
            public long? SkuId { get; set; }
            public long? RecipientPlayerId { get; set; }
            public string Message { get; set; } = string.Empty;
            public int GiftContext { get; set; }
        }

        private async Task<ParsedCatalogPurchaseRequest>
            ReadCatalogPurchaseRequestAsync()
        {
            string[] skuNames =
            {
                "skuId", "sku", "id", "itemId",
                "avatarItemId", "purchasableItemId"
            };
            string[] recipientNames =
            {
                "giftRecipientPlayerId", "giftRecipientAccountId",
                "recipientPlayerId", "recipientAccountId", "recipientId",
                "receiverPlayerId", "receiverAccountId",
                "toPlayerId", "toAccountId",
                "targetPlayerId", "targetAccountId",
                "giftToPlayerId", "giftToAccountId", "giftPlayerId"
            };
            string[] messageNames =
            {
                "giftMessage", "message", "note"
            };
            string[] contextNames =
            {
                "giftContext", "context"
            };

            var result = new ParsedCatalogPurchaseRequest();

            if (Request.RouteValues.TryGetValue("skuId", out object? routeValue) &&
                long.TryParse(routeValue?.ToString(), out long routeSkuId))
            {
                result.SkuId = routeSkuId;
            }

            result.SkuId ??= ReadLongFromValues(Request.Query, skuNames);
            result.RecipientPlayerId ??=
                ReadLongFromValues(Request.Query, recipientNames);
            result.Message = ReadStringFromValues(Request.Query, messageNames) ??
                result.Message;
            result.GiftContext =
                (int)(ReadLongFromValues(Request.Query, contextNames) ??
                result.GiftContext);

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                result.SkuId ??= ReadLongFromValues(form, skuNames);
                result.RecipientPlayerId ??=
                    ReadLongFromValues(form, recipientNames);
                result.Message = ReadStringFromValues(form, messageNames) ??
                    result.Message;
                result.GiftContext =
                    (int)(ReadLongFromValues(form, contextNames) ??
                    result.GiftContext);
            }
            else if (Request.Body.CanRead &&
                     (Request.ContentLength.GetValueOrDefault() > 0 ||
                      Request.Headers.TransferEncoding.Count > 0))
            {
                try
                {
                    using JsonDocument document =
                        await JsonDocument.ParseAsync(Request.Body);
                    JsonElement root = document.RootElement;
                    result.SkuId ??= FindLongByNames(root, skuNames);
                    result.RecipientPlayerId ??=
                        FindLongByNames(root, recipientNames);
                    result.RecipientPlayerId ??=
                        FindGiftRecipientFromNestedObject(root);
                    result.Message = FindStringByNames(root, messageNames) ??
                        result.Message;
                    long? giftContext = FindLongByNames(root, contextNames);
                    if (giftContext.HasValue)
                        result.GiftContext = checked((int)giftContext.Value);
                }
                catch (JsonException exception)
                {
                    Console.WriteLine(
                        $"[STORE PURCHASE] invalid request JSON: {exception.Message}");
                }
            }

            return result;
        }

        private static long? ReadLongFromValues(
            IEnumerable<KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>> values,
            IEnumerable<string> names)
        {
            var nameSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values)
            {
                if (nameSet.Contains(pair.Key) &&
                    long.TryParse(pair.Value.FirstOrDefault(), out long value))
                {
                    return value;
                }
            }
            return null;
        }

        private static string? ReadStringFromValues(
            IEnumerable<KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>> values,
            IEnumerable<string> names)
        {
            var nameSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values)
            {
                if (!nameSet.Contains(pair.Key))
                    continue;
                string? value = pair.Value.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return null;
        }

        private static long? FindLongByNames(
            JsonElement element,
            IEnumerable<string> names)
        {
            var nameSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return FindLongByNames(element, nameSet);
        }

        private static long? FindLongByNames(
            JsonElement element,
            HashSet<string> names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (names.Contains(property.Name))
                    {
                        if (property.Value.ValueKind == JsonValueKind.Number &&
                            property.Value.TryGetInt64(out long numeric))
                        {
                            return numeric;
                        }

                        if (property.Value.ValueKind == JsonValueKind.String &&
                            long.TryParse(property.Value.GetString(), out long text))
                        {
                            return text;
                        }
                    }

                    long? nested = FindLongByNames(property.Value, names);
                    if (nested.HasValue)
                        return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                {
                    long? nested = FindLongByNames(child, names);
                    if (nested.HasValue)
                        return nested;
                }
            }

            return null;
        }

        private static long? FindGiftRecipientFromNestedObject(
            JsonElement element,
            bool recipientContext = false)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    bool nextRecipientContext = recipientContext ||
                        property.Name.Contains("recipient", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("receiver", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("giftTo", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(property.Name, "to", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(property.Name, "target", StringComparison.OrdinalIgnoreCase);

                    if (recipientContext &&
                        (string.Equals(property.Name, "PlayerId", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(property.Name, "AccountId", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (property.Value.ValueKind == JsonValueKind.Number &&
                            property.Value.TryGetInt64(out long numeric))
                        {
                            return numeric;
                        }

                        if (property.Value.ValueKind == JsonValueKind.String &&
                            long.TryParse(property.Value.GetString(), out long text))
                        {
                            return text;
                        }
                    }

                    long? nested = FindGiftRecipientFromNestedObject(
                        property.Value,
                        nextRecipientContext);
                    if (nested.HasValue)
                        return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                {
                    long? nested = FindGiftRecipientFromNestedObject(
                        child,
                        recipientContext);
                    if (nested.HasValue)
                        return nested;
                }
            }

            return null;
        }

        private static string? FindStringByNames(
            JsonElement element,
            IEnumerable<string> names)
        {
            var nameSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return FindStringByNames(element, nameSet);
        }

        private static string? FindStringByNames(
            JsonElement element,
            HashSet<string> names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (names.Contains(property.Name) &&
                        property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }

                    string? nested = FindStringByNames(property.Value, names);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                {
                    string? nested = FindStringByNames(child, names);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }

            return null;
        }

        private bool TryPurchaseCatalogSkuAsGift(
            long senderAccountId,
            long recipientAccountId,
            CatalogSku sku,
            string? message,
            int giftContext,
            out int newBalance,
            out bool alreadyOwned,
            out GiftPackage? giftPackage)
        {
            newBalance = PlayerDB.GetCurrencyBalance(
                senderAccountId,
                PlayerDBClasses.CurrencyType.RecCenterTokens);
            giftPackage = null;

            alreadyOwned = IsAvatarCatalogItem(sku)
                ? PlayerDB.OwnsAvatarItem(recipientAccountId, sku.AvatarItemDesc)
                : IsEquipmentSkin(sku)
                    ? OwnsEquipmentItem(
                        recipientAccountId,
                        sku.EquipmentPrefabName,
                        sku.EquipmentModificationGuid)
                    : false;

            if (alreadyOwned)
                return true;

            if (!PlayerDB.TrySpendCurrency(
                    senderAccountId,
                    PlayerDBClasses.CurrencyType.RecCenterTokens,
                    StoreItemPrice,
                    out newBalance))
            {
                return false;
            }

            var pendingGift = new GiftPackage
            {
                FromPlayerId = checked((int)senderAccountId),
                Message = message ?? string.Empty,
                AvatarItemDesc = sku.AvatarItemDesc,
                ConsumableItemDesc = sku.ConsumableItemDesc,
                EquipmentPrefabName = sku.EquipmentPrefabName,
                EquipmentModificationGuid = sku.EquipmentModificationGuid,
                CurrencyType = (int)PlayerDBClasses.CurrencyType.RecCenterTokens,
                GiftContext = giftContext == 0
                    ? (int)PlayerDBClasses.GiftContext.Store_RecCenter
                    : giftContext,
                Rarity = sku.Rarity,
                Platform = -1,
                PlatformMask = sku.PlatformMask,
                BalanceType = (int)BalanceType.NonPurchasedNotUsableInP2P,
                IsQuery = sku.GiftDrop.IsQuery,
                Unique = !IsConsumableCatalogItem(sku)
            };

            giftPackage = PlayerDB.QueueGiftPackage(
                recipientAccountId,
                pendingGift);

            if (giftPackage != null)
                return true;

            int? refunded = PlayerDB.SetCurrencyBalance(
                senderAccountId,
                PlayerDBClasses.CurrencyType.RecCenterTokens,
                StoreItemPrice,
                add: true);
            if (refunded.HasValue)
                newBalance = refunded.Value;
            return false;
        }

        private static List<CatalogSku> GetCatalogSkus()
        {
            string avatarPath = Path.Combine(Program.dataDir, "APIS", "Items", "AvatarItems.json");
            string equipmentPath = Path.Combine(Program.dataDir, "APIS", "Items", "Equipment.json");
            string consumablesPath = Path.Combine(Program.dataDir, "APIS", "Items", "Consumables.json");

            bool hasAvatarCatalog = System.IO.File.Exists(avatarPath);
            bool hasEquipmentCatalog = System.IO.File.Exists(equipmentPath);
            bool hasConsumableCatalog = System.IO.File.Exists(consumablesPath);
            if (!hasAvatarCatalog && !hasEquipmentCatalog && !hasConsumableCatalog)
                return new List<CatalogSku>();

            DateTime avatarTimestamp = hasAvatarCatalog ? System.IO.File.GetLastWriteTimeUtc(avatarPath) : DateTime.MinValue;
            DateTime equipmentTimestamp = hasEquipmentCatalog ? System.IO.File.GetLastWriteTimeUtc(equipmentPath) : DateTime.MinValue;
            DateTime consumableTimestamp = hasConsumableCatalog ? System.IO.File.GetLastWriteTimeUtc(consumablesPath) : DateTime.MinValue;

            lock (CatalogLock)
            {
                if (CatalogCache.Count > 0 &&
                    avatarTimestamp == CatalogCacheTimestampUtc &&
                    equipmentTimestamp == EquipmentCatalogCacheTimestampUtc &&
                    consumableTimestamp == ConsumableCatalogCacheTimestampUtc)
                    return CatalogCache;

                var catalog = new List<CatalogSku>();
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                if (hasAvatarCatalog)
                {
                    var avatarItems = JsonSerializer.Deserialize<List<CatalogSourceItem>>(System.IO.File.ReadAllText(avatarPath), jsonOptions) ?? new();
                    catalog.AddRange(avatarItems.Select(CreateAvatarCatalogSku));
                }

                if (hasEquipmentCatalog)
                {
                    var equipmentItems = JsonSerializer.Deserialize<List<PlayerDBClasses.EquipmentItem>>(System.IO.File.ReadAllText(equipmentPath), jsonOptions) ?? new();
                    catalog.AddRange(equipmentItems
                        .Where(item => !string.IsNullOrWhiteSpace(item.PrefabName) && !string.IsNullOrWhiteSpace(item.ModificationGuid))
                        .Select(CreateEquipmentCatalogSku));
                }

                if (hasConsumableCatalog)
                {
                    using JsonDocument document = JsonDocument.Parse(System.IO.File.ReadAllText(consumablesPath));
                    IEnumerable<JsonElement> consumables = document.RootElement.ValueKind == JsonValueKind.Array
                        ? document.RootElement.EnumerateArray().Select(item => item.Clone())
                        : Enumerable.Empty<JsonElement>();
                    catalog.AddRange(consumables.Select(CreateConsumableCatalogSku));
                }

                CatalogCache = catalog.Where(IsUsableStoreItem)
                    .GroupBy(GetCatalogIdentity, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First()).ToList();
                CatalogCacheTimestampUtc = avatarTimestamp;
                EquipmentCatalogCacheTimestampUtc = equipmentTimestamp;
                ConsumableCatalogCacheTimestampUtc = consumableTimestamp;
                return CatalogCache;
            }
        }

        private static CatalogSku CreateAvatarCatalogSku(
            CatalogSourceItem item,
            int index)
        {
            int purchasableItemId = 1_000_000 + index;
            string friendlyName = string.IsNullOrWhiteSpace(item.FriendlyName)
                ? $"Avatar Item {item.AvatarItemId}"
                : item.FriendlyName.Trim();
            CatalogGiftDrop giftDrop = CreateCatalogGiftDrop(item, index);

            return new CatalogSku
            {
                Id = purchasableItemId,
                SkuId = purchasableItemId,
                PurchasableItemId = purchasableItemId,
                Type = 0,
                IsFeatured = true,
                ItemId = item.AvatarItemId,
                AvatarItemId = item.AvatarItemId,
                AvatarItemType = item.AvatarItemType,
                ItemType = item.AvatarItemType,
                AvatarItemDesc = item.AvatarItemDesc ?? string.Empty,
                FriendlyName = friendlyName,
                Tooltip = item.Tooltip ?? string.Empty,
                ThumbnailImage = item.ThumbnailImage ?? string.Empty,
                Rarity = item.Rarity,
                PlatformMask = item.PlatformMask,
                IsBaseAvatarItem = item.IsBaseAvatarItem,
                HasRealName = !string.IsNullOrWhiteSpace(item.FriendlyName),
                CreatedAt = item.CreatedAt,
                Prices = CreateCatalogPrices(),
                GiftDrop = giftDrop,
                GiftDrops = new List<CatalogGiftDrop> { giftDrop }
            };
        }

        private static CatalogSku CreateEquipmentCatalogSku(
            PlayerDBClasses.EquipmentItem item,
            int index)
        {
            int purchasableItemId = 2_000_000 + index;
            string friendlyName = GetEquipmentFriendlyName(item, index);
            CatalogGiftDrop giftDrop = CreateCatalogGiftDrop(item, index, friendlyName);

            return new CatalogSku
            {
                Id = purchasableItemId,
                SkuId = purchasableItemId,
                PurchasableItemId = purchasableItemId,
                Type = 0,
                IsFeatured = true,
                ItemId = purchasableItemId,
                AvatarItemId = 0,
                AvatarItemType = 0,
                ItemType = 0,
                AvatarItemDesc = string.Empty,
                EquipmentPrefabName = item.PrefabName.Trim(),
                EquipmentModificationGuid = item.ModificationGuid.Trim(),
                FriendlyName = friendlyName,
                Tooltip = item.Tooltip ?? string.Empty,
                ThumbnailImage = item.ThumbnailImage ?? string.Empty,
                Rarity = item.Rarity,
                PlatformMask = item.PlatformMask,
                IsBaseAvatarItem = false,
                HasRealName = !string.IsNullOrWhiteSpace(item.FriendlyName),
                Prices = CreateCatalogPrices(),
                GiftDrop = giftDrop,
                GiftDrops = new List<CatalogGiftDrop> { giftDrop }
            };
        }

        private static CatalogSku CreateConsumableCatalogSku(JsonElement item, int index)
        {
            int purchasableItemId = 3_000_000 + index;
            string desc = GetJsonString(item, "ConsumableItemDesc", "ConsumableDesc", "ItemDesc", "DescriptionId");
            if (string.IsNullOrWhiteSpace(desc))
                desc = GetJsonString(item, "FriendlyName", "Name");
            string friendlyName = GetJsonString(item, "FriendlyName", "Name");
            bool hasRealName = !string.IsNullOrWhiteSpace(friendlyName);
            if (string.IsNullOrWhiteSpace(friendlyName))
                friendlyName = $"Consumable {index + 1}";

            bool isHairDye = IsHairDyeFriendlyName(friendlyName);
            int rarity = GetJsonInt(item, "Rarity");
            int itemType = GetJsonInt(item, "ConsumableItemType", "ItemType", "Type");

            if (isHairDye)
                itemType = 1;

            var giftDrop = new CatalogGiftDrop
            {
                GiftDropId = purchasableItemId,
                FriendlyName = friendlyName,
                Tooltip = GetJsonString(item, "Tooltip", "Description"),
                ConsumableItemDesc = desc,
                ItemType = itemType,
                ConsumableItemType = isHairDye ? 6 : itemType,
                Rarity = rarity,
                IsQuery = false,
                Unique = false,
                Level = rarity,
                Context = 100010,
                ThumbnailImage = GetJsonString(item, "ThumbnailImage", "Thumbnail", "ImageName")
            };

            return new CatalogSku
            {
                Id = purchasableItemId,
                SkuId = purchasableItemId,
                PurchasableItemId = purchasableItemId,
                Type = 0,
                IsFeatured = true,
                ItemId = GetJsonLong(
                    item,
                    "ConsumableItemId",
                    "ItemId",
                    "Id",
                    defaultValue: purchasableItemId),
                ItemType = itemType,
                ConsumableItemType = isHairDye ? 6 : itemType,
                ConsumableItemDesc = desc,
                FriendlyName = friendlyName,
                Tooltip = GetJsonString(item, "Tooltip", "Description"),
                ThumbnailImage = giftDrop.ThumbnailImage,
                Rarity = rarity,
                PlatformMask = GetJsonInt(item, "PlatformMask", defaultValue: -1),
                HasRealName = hasRealName,

                IsOwned = isHairDye,
                Owned = isHairDye,
                Purchased = isHairDye,

                Prices = CreateCatalogPrices(),
                GiftDrop = giftDrop,
                GiftDrops = new List<CatalogGiftDrop> { giftDrop }
            };
        }

        private static string GetJsonString(JsonElement item, params string[] names)
        {
            foreach (JsonProperty property in item.EnumerateObject())
                if (names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
                    return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.ToString();
            return string.Empty;
        }

        private static int GetJsonInt(JsonElement item, string name, int defaultValue = 0) => GetJsonInt(item, new[] { name }, defaultValue);
        private static int GetJsonInt(JsonElement item, string name1, string name2, string name3) => GetJsonInt(item, new[] { name1, name2, name3 }, 0);
        private static int GetJsonInt(JsonElement item, string[] names, int defaultValue)
        {
            foreach (JsonProperty property in item.EnumerateObject())
                if (names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    if (property.Value.TryGetInt32(out int value)) return value;
                    if (int.TryParse(property.Value.ToString(), out value)) return value;
                }
            return defaultValue;
        }

        private static long GetJsonLong(JsonElement item, string name1, string name2, string name3, long defaultValue)
        {
            foreach (JsonProperty property in item.EnumerateObject())
                if (property.Name.Equals(name1, StringComparison.OrdinalIgnoreCase) || property.Name.Equals(name2, StringComparison.OrdinalIgnoreCase) || property.Name.Equals(name3, StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.TryGetInt64(out long value)) return value;
                    if (long.TryParse(property.Value.ToString(), out value)) return value;
                }
            return defaultValue;
        }

        private static List<CatalogPrice> CreateCatalogPrices() =>
            new()
            {
                new CatalogPrice
                {
                    CurrencyType = (int)PlayerDBClasses.CurrencyType.RecCenterTokens,
                    Price = StoreItemPrice
                }
            };

        private static string GetEquipmentFriendlyName(
            PlayerDBClasses.EquipmentItem item,
            int index)
        {
            if (!string.IsNullOrWhiteSpace(item.FriendlyName))
                return item.FriendlyName.Trim();

            string prefabName = item.PrefabName?.Trim().Trim('[', ']') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(prefabName))
                return $"Equipment Skin {index + 1}";

            return prefabName.EndsWith("Skin", StringComparison.OrdinalIgnoreCase)
                ? prefabName
                : $"{prefabName} Skin";
        }

        private async Task<long?> ReadPurchaseSkuIdAsync()
        {
            string[] names =
            {
                "skuId",
                "sku",
                "id",
                "itemId",
                "avatarItemId",
                "purchasableItemId"
            };

            if (Request.RouteValues.TryGetValue("skuId", out object? routeValue) &&
                long.TryParse(routeValue?.ToString(), out long routeSkuId))
                return routeSkuId;

            foreach (string name in names)
            {
                if (long.TryParse(Request.Query[name].FirstOrDefault(), out long queryValue))
                    return queryValue;
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (string name in names)
                {
                    if (long.TryParse(form[name].FirstOrDefault(), out long formValue))
                        return formValue;
                }
            }
            else if (Request.Body.CanRead &&
                     (Request.ContentLength.GetValueOrDefault() > 0 ||
                      Request.Headers.TransferEncoding.Count > 0))
            {
                try
                {
                    using JsonDocument document = await JsonDocument.ParseAsync(Request.Body);
                    return FindSkuId(document.RootElement, names);
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            return null;
        }

        private static long? FindSkuId(JsonElement element, string[] names)
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
                        if (property.Value.ValueKind == JsonValueKind.String &&
                            long.TryParse(property.Value.GetString(), out long textNumber))
                            return textNumber;
                    }

                    long? nested = FindSkuId(property.Value, names);
                    if (nested.HasValue)
                        return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                {
                    long? nested = FindSkuId(child, names);
                    if (nested.HasValue)
                        return nested;
                }
            }

            return null;
        }

        private sealed class CatalogSourceItem
        {
            public string? AvatarItemDesc { get; set; }
            public int AvatarItemType { get; set; }
            public int PlatformMask { get; set; } = -1;
            public string? FriendlyName { get; set; }
            public string? Tooltip { get; set; }
            public int Rarity { get; set; }
            public long AvatarItemId { get; set; }
            public bool IsBaseAvatarItem { get; set; }
            public DateTime CreatedAt { get; set; }
            public string? ThumbnailImage { get; set; }
        }

        private static CatalogGiftDrop CreateCatalogGiftDrop(
            CatalogSourceItem item,
            int index)
        {
            return new CatalogGiftDrop
            {
                GiftDropId = 1_000_000 + index,
                FriendlyName = string.IsNullOrWhiteSpace(item.FriendlyName)
                    ? $"Avatar Item {item.AvatarItemId}"
                    : item.FriendlyName.Trim(),
                Tooltip = item.Tooltip ?? string.Empty,
                AvatarItemDesc = item.AvatarItemDesc ?? string.Empty,
                ConsumableItemDesc = string.Empty,
                EquipmentPrefabName = string.Empty,
                EquipmentModificationGuid = string.Empty,
                Rarity = item.Rarity,
                IsQuery = false,
                Unique = true,
                Level = item.Rarity,
                Context = 100010,
                ThumbnailImage = item.ThumbnailImage ?? string.Empty
            };
        }

        private static CatalogGiftDrop CreateCatalogGiftDrop(
            PlayerDBClasses.EquipmentItem item,
            int index,
            string friendlyName)
        {
            return new CatalogGiftDrop
            {
                GiftDropId = 2_000_000 + index,
                FriendlyName = friendlyName,
                Tooltip = item.Tooltip ?? string.Empty,
                AvatarItemDesc = string.Empty,
                ConsumableItemDesc = string.Empty,
                EquipmentPrefabName = item.PrefabName?.Trim() ?? string.Empty,
                EquipmentModificationGuid = item.ModificationGuid?.Trim() ?? string.Empty,
                Rarity = item.Rarity,
                IsQuery = false,
                Unique = true,
                Level = item.Rarity,
                Context = 100010,
                ThumbnailImage = item.ThumbnailImage ?? string.Empty
            };
        }

        private static List<CatalogSku> GetWatchStoreSkusWithHairDyes()
        {
            List<CatalogSku> storeItems = GetDailyStoreSkus(3);
            var identities = storeItems
                .Select(GetCatalogIdentity)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            IEnumerable<CatalogSku> hairDyes = GetCatalogSkus()
                .Where(IsHairDyeCatalogItem)
                .GroupBy(GetCatalogIdentity, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.FriendlyName, StringComparer.OrdinalIgnoreCase);

            foreach (CatalogSku hairDye in hairDyes)
            {
                if (identities.Add(GetCatalogIdentity(hairDye)))
                    storeItems.Add(hairDye);
            }

            return storeItems;
        }

        private static bool IsHairDyeFriendlyName(string? friendlyName) =>
            !string.IsNullOrWhiteSpace(friendlyName) &&
            friendlyName.Contains("Hair Dye", StringComparison.OrdinalIgnoreCase);

        private static bool IsHairDyeCatalogItem(CatalogSku? item) =>
            item != null &&
            IsConsumableCatalogItem(item) &&
            IsHairDyeFriendlyName(item.FriendlyName);

        private static List<CatalogSku> GetDailyStoreSkus(int storeId)
        {

            List<CatalogSku> catalog = GetCatalogSkus();

            return catalog
                .Where(IsUsableStoreItem)
                .GroupBy(GetCatalogIdentity, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.FriendlyName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsEquipmentSkin(CatalogSku? item) =>
            item != null &&
            !string.IsNullOrWhiteSpace(item.EquipmentPrefabName) &&
            !string.IsNullOrWhiteSpace(item.EquipmentModificationGuid);

        private static bool IsAvatarCatalogItem(CatalogSku? item) =>
            item != null &&
            !string.IsNullOrWhiteSpace(item.AvatarItemDesc);

        private static bool IsConsumableCatalogItem(CatalogSku? item) =>
            item != null && !string.IsNullOrWhiteSpace(item.ConsumableItemDesc);

        private static bool IsExcludedAvatarItemName(string? friendlyName) =>
            !string.IsNullOrWhiteSpace(friendlyName) &&
            (friendlyName.Contains("Custom", StringComparison.OrdinalIgnoreCase) ||
             friendlyName.Contains("Owner", StringComparison.OrdinalIgnoreCase) ||
             friendlyName.Contains("Shirt", StringComparison.OrdinalIgnoreCase));

        private static bool IsUnfinishedCatalogItem(CatalogSku item) =>
            item.Rarity <= 0 && !item.HasRealName;

        private static bool IsUsableStoreItem(CatalogSku? item) =>
            item != null && !string.IsNullOrWhiteSpace(item.FriendlyName) &&
            (IsAvatarCatalogItem(item) || IsEquipmentSkin(item) || IsConsumableCatalogItem(item)) &&
            !(IsAvatarCatalogItem(item) && IsExcludedAvatarItemName(item.FriendlyName)) &&
            !IsUnfinishedCatalogItem(item);

        private static string GetCatalogIdentity(CatalogSku item) =>
            IsEquipmentSkin(item)
                ? $"equipment|{item.EquipmentPrefabName}|{item.EquipmentModificationGuid}"
                : IsConsumableCatalogItem(item)
                    ? $"consumable|{item.ConsumableItemDesc}"
                    : $"avatar|{item.AvatarItemDesc}";

        private static bool TryPurchaseCatalogSku(
            long accountId,
            CatalogSku sku,
            out int newBalance,
            out bool alreadyOwned)
        {
            if (IsConsumableCatalogItem(sku))
            {
                alreadyOwned = false;
                if (!PlayerDB.TrySpendCurrency(
                        accountId,
                        PlayerDBClasses.CurrencyType.RecCenterTokens,
                        StoreItemPrice,
                        out newBalance))
                {
                    return false;
                }

                PlayerInventoryStore.AddConsumable(
                    accountId,
                    sku.ConsumableItemDesc,
                    sku.ItemId,
                    sku.FriendlyName,
                    amount: 1);
                return true;
            }

            if (!IsEquipmentSkin(sku))
            {
                return PlayerDB.TryPurchaseAvatarItem(
                    accountId,
                    sku.AvatarItemDesc,
                    StoreItemPrice,
                    out newBalance,
                    out alreadyOwned);
            }

            lock (EquipmentInventoryLock)
            {
                newBalance = PlayerDB.GetCurrencyBalance(
                    accountId,
                    PlayerDBClasses.CurrencyType.RecCenterTokens);
                alreadyOwned = OwnsEquipmentItem(
                    accountId,
                    sku.EquipmentPrefabName,
                    sku.EquipmentModificationGuid);

                if (alreadyOwned)
                    return true;

                if (!PlayerDB.TrySpendCurrency(
                        accountId,
                        PlayerDBClasses.CurrencyType.RecCenterTokens,
                        StoreItemPrice,
                        out newBalance))
                {
                    return false;
                }

                try
                {
                    if (GrantPurchasedEquipment(accountId, sku))
                        return true;
                }
                catch (IOException)
                {

                }
                catch (UnauthorizedAccessException)
                {

                }

                int? refundedBalance = PlayerDB.SetCurrencyBalance(
                    accountId,
                    PlayerDBClasses.CurrencyType.RecCenterTokens,
                    StoreItemPrice,
                    add: true);
                newBalance = refundedBalance ?? newBalance;
                return false;
            }
        }

        private static ulong GetDailyStoreSortKey(
            CatalogSku item,
            DateTime storeDate,
            int storeId,
            long rotationNonce)
        {
            string value = $"{storeDate:yyyyMMdd}|{storeId}|{rotationNonce}|{GetCatalogIdentity(item)}|{item.SkuId}";
            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                ulong hash = offset;
                foreach (char character in value)
                {
                    hash ^= character;
                    hash *= prime;
                }

                return hash;
            }
        }

        public static StorefrontAdminInfo GetStorefrontAdminInfo()
        {
            (long rotationNonce, List<long> customSkuIds) = GetStorefrontSelectionState();
            Dictionary<long, CatalogSku> catalog = GetCatalogSkus()
                .ToDictionary(item => item.SkuId);
            List<StorefrontAdminItem> customItems = customSkuIds
                .Select(skuId => catalog.GetValueOrDefault(skuId))
                .Where(IsUsableStoreItem)
                .Cast<CatalogSku>()
                .Select(ToStorefrontAdminItem)
                .ToList();

            return new StorefrontAdminInfo
            {
                RotationNonce = rotationNonce,
                CustomItems = customItems,
                NextRefresh = DateTime.Today.AddDays(1).ToUniversalTime()
            };
        }

        public static List<StorefrontAdminItem> GetWebsiteStorefrontItems(
            int storeId = 3) =>
            GetCatalogSkus()
                .Where(IsUsableStoreItem)
                .OrderBy(item => IsAvatarCatalogItem(item) ? 0 : IsEquipmentSkin(item) ? 1 : 2)
                .ThenBy(item => item.FriendlyName, StringComparer.OrdinalIgnoreCase)
                .Select(ToStorefrontAdminItem)
                .ToList();

        public static bool TryPurchaseWebsiteStoreItem(
            long accountId,
            long skuId,
            out StorefrontAdminItem? purchasedItem,
            out int newBalance,
            out bool alreadyOwned,
            out string error)
        {
            CatalogSku? sku = GetCatalogSkus().FirstOrDefault(value =>
                value.SkuId == skuId ||
                value.PurchasableItemId == skuId ||
                (value.AvatarItemId > 0 && value.AvatarItemId == skuId) ||
                (IsConsumableCatalogItem(value) && value.ItemId == skuId));
            if (sku == null)
            {
                purchasedItem = null;
                newBalance = PlayerDB.GetCurrencyBalance(
                    accountId,
                    PlayerDBClasses.CurrencyType.RecCenterTokens);
                alreadyOwned = false;
                error = "That shop item could not be found.";
                return false;
            }

            bool purchased = TryPurchaseCatalogSku(
                accountId,
                sku,
                out newBalance,
                out alreadyOwned);
            purchasedItem = ToStorefrontAdminItem(sku);
            error = purchased
                ? string.Empty
                : "You do not have enough tokens for that item.";
            return purchased;
        }

        public static (List<StorefrontAdminItem> Items, int Total) SearchStorefrontCatalog(
            string? search,
            string? type = null,
            int skip = 0,
            int take = 30)
        {
            string term = search?.Trim() ?? string.Empty;
            string requestedType = type?.Trim().ToLowerInvariant() ?? string.Empty;

            IEnumerable<StorefrontAdminItem> query = GetCatalogSkus()
                .Where(IsUsableStoreItem)
                .Where(item => string.IsNullOrWhiteSpace(term) ||
                    item.FriendlyName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.AvatarItemDesc.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.ConsumableItemDesc.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.EquipmentPrefabName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.EquipmentModificationGuid.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.AvatarItemId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.SkuId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(ToStorefrontAdminItem)
                .Where(item => requestedType switch
                {
                    "avatar" => !string.IsNullOrWhiteSpace(item.AvatarItemDesc),
                    "equipment" => !string.IsNullOrWhiteSpace(item.EquipmentPrefabName) &&
                        !string.IsNullOrWhiteSpace(item.EquipmentModificationGuid),
                    "consumable" => !string.IsNullOrWhiteSpace(item.ConsumableItemDesc),
                    _ => true
                })
                .OrderByDescending(item => item.Rarity)
                .ThenBy(item => item.FriendlyName, StringComparer.OrdinalIgnoreCase);

            int total = query.Count();
            List<StorefrontAdminItem> items = query
                .Skip(Math.Max(0, skip))
                .Take(Math.Clamp(take, 1, 50))
                .ToList();

            return (items, total);
        }

        public static StorefrontAdminItem? GetStorefrontCatalogItem(long skuId) =>
            GetCatalogSkus()
                .Where(IsUsableStoreItem)
                .Where(item => item.SkuId == skuId)
                .Select(ToStorefrontAdminItem)
                .FirstOrDefault();

        public static bool IsWebsiteStoreItemOwned(
            long accountId,
            StorefrontAdminItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.EquipmentPrefabName) &&
                !string.IsNullOrWhiteSpace(item.EquipmentModificationGuid))
            {
                return OwnsEquipmentItem(
                    accountId,
                    item.EquipmentPrefabName,
                    item.EquipmentModificationGuid);
            }

            if (!string.IsNullOrWhiteSpace(item.ConsumableItemDesc))
            {
                return PlayerInventoryStore.GetConsumableQuantity(
                    accountId,
                    item.ConsumableItemDesc) > 0;
            }

            return PlayerDB.OwnsAvatarItem(accountId, item.AvatarItemDesc);
        }

        public static bool TryAddCustomStoreItem(long skuId, out string error)
        {
            CatalogSku? item = GetCatalogSkus()
                .FirstOrDefault(candidate => candidate.SkuId == skuId);
            if (!IsUsableStoreItem(item))
            {
                error = "That catalog item could not be found.";
                return false;
            }

            lock (StorefrontAdminLock)
            {
                StorefrontAdminState state = LoadStorefrontAdminState();
                if (state.CustomSkuIds.Contains(skuId))
                {
                    error = "That item is already pinned in the shop.";
                    return false;
                }

                if (state.CustomSkuIds.Count >= 10)
                {
                    error = "The shop already has the maximum of 10 custom items.";
                    return false;
                }

                state.CustomSkuIds.Add(skuId);
                SaveStorefrontAdminState(state);
            }

            error = string.Empty;
            return true;
        }

        public static bool RemoveCustomStoreItem(long skuId)
        {
            lock (StorefrontAdminLock)
            {
                StorefrontAdminState state = LoadStorefrontAdminState();
                if (!state.CustomSkuIds.Remove(skuId))
                    return false;
                SaveStorefrontAdminState(state);
                return true;
            }
        }

        public static long RefreshStorefrontRotation()
        {
            lock (StorefrontAdminLock)
            {
                StorefrontAdminState state = LoadStorefrontAdminState();
                state.RotationNonce = state.RotationNonce == long.MaxValue
                    ? 1
                    : state.RotationNonce + 1;
                SaveStorefrontAdminState(state);
                return state.RotationNonce;
            }
        }

        private static StorefrontAdminItem ToStorefrontAdminItem(CatalogSku item) =>
            new()
            {
                SkuId = item.SkuId,
                AvatarItemId = item.AvatarItemId,
                AvatarItemType = item.AvatarItemType,
                FriendlyName = item.FriendlyName,
                AvatarItemDesc = item.AvatarItemDesc,
                ConsumableItemDesc = item.ConsumableItemDesc,
                EquipmentPrefabName = item.EquipmentPrefabName,
                EquipmentModificationGuid = item.EquipmentModificationGuid,
                ThumbnailImage = item.ThumbnailImage,
                Rarity = item.Rarity,
                Price = StoreItemPrice
            };

        private static (long rotationNonce, List<long> customSkuIds)
            GetStorefrontSelectionState()
        {
            lock (StorefrontAdminLock)
            {
                StorefrontAdminState state = LoadStorefrontAdminState();
                return (state.RotationNonce, state.CustomSkuIds.ToList());
            }
        }

        private static StorefrontAdminState LoadStorefrontAdminState()
        {
            if (StorefrontAdminCache != null)
                return StorefrontAdminCache;

            string path = GetStorefrontAdminPath();
            try
            {
                if (System.IO.File.Exists(path))
                {
                    StorefrontAdminCache = JsonSerializer.Deserialize<StorefrontAdminState>(
                        System.IO.File.ReadAllText(path),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch (JsonException)
            {
                StorefrontAdminCache = null;
            }

            StorefrontAdminCache ??= new StorefrontAdminState();
            StorefrontAdminCache.CustomSkuIds ??= new List<long>();
            return StorefrontAdminCache;
        }

        private static void SaveStorefrontAdminState(StorefrontAdminState state)
        {
            string path = GetStorefrontAdminPath();
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            System.IO.File.WriteAllText(
                path,
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string GetStorefrontAdminPath() =>
            Path.Combine(Program.dataDir, "StorefrontAdmin.json");

        private sealed class StorefrontAdminState
        {
            public long RotationNonce { get; set; }
            public List<long> CustomSkuIds { get; set; } = new();
        }

        public sealed class StorefrontAdminInfo
        {
            public long RotationNonce { get; set; }
            public DateTime NextRefresh { get; set; }
            public List<StorefrontAdminItem> CustomItems { get; set; } = new();
        }

        public sealed class StorefrontAdminItem
        {
            public long SkuId { get; set; }
            public long AvatarItemId { get; set; }
            public int AvatarItemType { get; set; }
            public string FriendlyName { get; set; } = string.Empty;
            public string AvatarItemDesc { get; set; } = string.Empty;
            public string ConsumableItemDesc { get; set; } = string.Empty;
            public string EquipmentPrefabName { get; set; } = string.Empty;
            public string EquipmentModificationGuid { get; set; } = string.Empty;
            public string ThumbnailImage { get; set; } = string.Empty;
            public int Rarity { get; set; }
            public int Price { get; set; }
        }

        public sealed class CatalogStorefront
        {
            public int StorefrontType { get; set; }
            public DateTime NextUpdate { get; set; }
            public List<CatalogSku> StoreItems { get; set; } = new();
        }

        public sealed class StorefrontAdCarouselItem
        {
            public int AdCarouselItemId { get; set; }
            public string ImageName { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<int> PurchasableItemIds { get; set; } = new();
            public int? PurchaseReminderId { get; set; }
        }

        public sealed class CatalogPrice
        {
            public int CurrencyType { get; set; }
            public int Price { get; set; }
        }

        public sealed class CatalogGiftDrop
        {
            public int GiftDropId { get; set; }
            public string FriendlyName { get; set; } = string.Empty;
            public string Tooltip { get; set; } = string.Empty;
            public string AvatarItemDesc { get; set; } = string.Empty;
            public string ConsumableItemDesc { get; set; } = string.Empty;
            public string EquipmentPrefabName { get; set; } = string.Empty;
            public string EquipmentModificationGuid { get; set; } = string.Empty;
            public int ItemType { get; set; }
            public int ConsumableItemType { get; set; }
            public int Rarity { get; set; }
            public bool IsQuery { get; set; }
            public bool Unique { get; set; }
            public int Level { get; set; }
            public int Context { get; set; }
            public string ThumbnailImage { get; set; } = string.Empty;
        }

        public sealed class CatalogSku
        {
            public long Id { get; set; }
            public long SkuId { get; set; }
            public int PurchasableItemId { get; set; }
            public int Type { get; set; }
            public bool IsFeatured { get; set; }
            public long ItemId { get; set; }
            public long AvatarItemId { get; set; }
            public int ItemType { get; set; }
            public int ConsumableItemType { get; set; }
            public int AvatarItemType { get; set; }
            public string AvatarItemDesc { get; set; } = string.Empty;
            public string ConsumableItemDesc { get; set; } = string.Empty;
            public string EquipmentPrefabName { get; set; } = string.Empty;
            public string EquipmentModificationGuid { get; set; } = string.Empty;
            public string FriendlyName { get; set; } = string.Empty;
            public string Tooltip { get; set; } = string.Empty;
            public string ThumbnailImage { get; set; } = string.Empty;
            public int Rarity { get; set; }
            public int PlatformMask { get; set; } = -1;
            public int CurrencyType { get; set; } = (int)PlayerDBClasses.CurrencyType.RecCenterTokens;
            public int Price { get; set; } = StoreItemPrice;
            public int OriginalPrice { get; set; } = StoreItemPrice;
            public int DiscountedPrice { get; set; } = StoreItemPrice;
            public bool IsAvailable { get; set; } = true;
            public bool IsPurchasable { get; set; } = true;
            public bool IsEnabled { get; set; } = true;
            public bool IsOwned { get; set; }
            public bool Owned { get; set; }
            public bool Purchased { get; set; }
            public bool IsBaseAvatarItem { get; set; }
            public bool HasRealName { get; set; } = true;
            public DateTime CreatedAt { get; set; }
            public List<CatalogPrice> Prices { get; set; } = new();
            public CatalogGiftDrop GiftDrop { get; set; } = new();
            public List<CatalogGiftDrop> GiftDrops { get; set; } = new();
        }

        [HttpGet("/api/customAvatarItems/v1/featured")]
        public IActionResult GetFeaturedCustomAvatarItems()
        {
            return Ok(Array.Empty<object>());
        }

        [HttpGet("/api/inventions/v1/{inventionId:long}/version")]
        [HttpGet("/api/inventions/v2/{inventionId:long}/version")]
        public IActionResult GetInventionVersionByPath(long inventionId)
        {
            CreatorFeatureDB.InventionRecord? record =
                CreatorFeatureDB.GetInvention(inventionId);
            if (record == null)
                return NotFound(new { Success = false, Error = "invention_not_found" });

            object? version = CreatorFeatureDB.ToClientInventionVersion(record);
            return version == null
                ? StatusCode(
                    StatusCodes.Status409Conflict,
                    new { Success = false, Error = "invention_object_blob_missing" })
                : Ok(version);
        }

        [HttpGet("/api/inventions/v1/version")]
        public IActionResult GetInventionVersion(
            [FromQuery] long inventionId,
            [FromQuery] long id = 0,
            [FromQuery] int version = 0)
        {
            long resolvedId = inventionId > 0 ? inventionId : id;
            CreatorFeatureDB.InventionRecord? record =
                CreatorFeatureDB.GetInvention(resolvedId);
            if (record == null)
                return NotFound();

            object? currentVersion = CreatorFeatureDB.ToClientInventionVersion(record);
            return currentVersion == null
                ? StatusCode(
                    StatusCodes.Status409Conflict,
                    new { Success = false, Error = "invention_object_blob_missing" })
                : Ok(currentVersion);
        }

        [HttpGet("/api/storefronts/v1/toptoday")]
        public IActionResult GetTopStorefrontsToday()
        {
            return Ok(GetCatalogSkus());
        }

        [HttpGet("/api/inventions/v1/toptoday")]
        public IActionResult GetTopInventionsToday(
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            return Ok(CreatorFeatureDB.SearchInventions(skip: skip, take: take)
                .Select(CreatorFeatureDB.ToClientInvention)
                .ToArray());
        }

        [HttpGet("/subscription/top/creators/today")]
        public IActionResult GetTopSubscriptionCreatorsToday()
        {
            return Ok(Array.Empty<object>());
        }

        private static object ToSavedImageDto(RecNetDB.SavedImage savedImage)
        {
            long imageId = ImageController.GetOrCreateSavedImageId(savedImage);
            return new
            {
                Id = imageId,
                ImageId = imageId,
                SavedImageId = imageId,
                AccountId = savedImage.AccountId,
                RoomId = savedImage.RoomId,
                PlayerEventId = savedImage.PlayerEventId,
                Url = $"{ServerConfig.BaseURL.TrimEnd('/')}/imageserver/{savedImage.PhotoPath}",
                LookupName = savedImage.LookupName,
                Accessibility = savedImage.Accessibility,
                CreatedAt = savedImage.CreatedAt
            };
        }

        [HttpGet("/api/images/v5/player/{accountId:long}")]
        public IActionResult GetPlayerImagesV5(
            long accountId,
            [FromQuery] int sort = 0)
        {
            var images = RecNetDB.SavedImages.Find(value => value.AccountId == accountId)
                .OrderByDescending(value => value.CreatedAt)
                .Select(ToSavedImageDto)
                .ToList();
            return Ok(images);
        }

        [HttpGet("/api/images/v1/listsaved")]
        public IActionResult ListSavedImages()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var images = RecNetDB.SavedImages.Find(value => value.AccountId == accountId.Value)
                .OrderByDescending(value => value.CreatedAt)
                .Select(ToSavedImageDto)
                .ToList();
            return Ok(images);
        }

        public class SavedImageBulkRequest
        {
            public List<string>? PhotoPaths { get; set; }
        }

        [HttpPost("/api/images/v5/bulk")]
        public IActionResult GetSavedImagesBulk([FromBody] SavedImageBulkRequest request)
        {
            if (AuthStuff.GetPlayerId(Request) == null)
                return Unauthorized();

            var paths = (request.PhotoPaths ?? new List<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var images = RecNetDB.SavedImages.Find(value => paths.Contains(value.PhotoPath))
                .Select(ToSavedImageDto)
                .ToList();
            return Ok(images);
        }

        public class DeleteSavedImageRequest
        {
            public string? PhotoPath { get; set; }
        }

        [HttpPost("/api/images/v1/deletesaved")]
        [HttpDelete("/api/images/v1/deletesaved")]
        public IActionResult DeleteSavedImage([FromBody] DeleteSavedImageRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            string path = request.PhotoPath ?? string.Empty;
            var image = RecNetDB.SavedImages.FindById(path);
            if (image == null || image.AccountId != accountId.Value)
                return NotFound(new { error = "Saved image not found." });

            RecNetDB.SavedImages.Delete(path);
            return Ok(new { success = true });
        }

        [HttpPost("/api/images/v1/sendlink")]
        public IActionResult SendSavedImageLink([FromBody] DeleteSavedImageRequest request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            string path = request.PhotoPath ?? string.Empty;
            var image = RecNetDB.SavedImages.FindById(path);
            if (image == null)
                return NotFound(new { error = "Saved image not found." });

            return Ok(new { success = true, url = $"{ServerConfig.BaseURL.TrimEnd('/')}/imageserver/{image.PhotoPath}" });
        }

        [HttpGet("/api/images/v1/slideshow")]
        public IActionResult GetImageSlideshow()
        {
            string imageRoot = Path.GetFullPath(
                Path.Combine(Program.dataDir, "Images"));
            string requiredPrefix = imageRoot
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            bool TryResolveSavedImage(
                RecNetDB.SavedImage savedImage,
                out string relativePath,
                out string fullPath)
            {
                relativePath = savedImage.PhotoPath
                    .Replace('\\', '/')
                    .TrimStart('/');
                fullPath = string.Empty;
                if (string.IsNullOrWhiteSpace(relativePath) ||
                    relativePath.Split('/').Any(segment => segment is "." or ".."))
                {
                    return false;
                }

                try
                {
                    fullPath = Path.GetFullPath(
                        Path.Combine(imageRoot, relativePath));
                    return fullPath.StartsWith(
                               requiredPrefix,
                               StringComparison.OrdinalIgnoreCase) &&
                           System.IO.File.Exists(fullPath);
                }
                catch
                {
                    return false;
                }
            }

            var images = RecNetDB.SavedImages.FindAll()
                .Where(savedImage =>
                    savedImage.SavedImageType == 1 &&
                    savedImage.Accessibility == 1 &&
                    savedImage.AccountId is > 0 and <= int.MaxValue)
                .Select(savedImage =>
                {
                    if (!TryResolveSavedImage(
                            savedImage,
                            out string relativePath,
                            out _))
                    {
                        return null;
                    }

                    var player = PlayerDB.Players.FindById(
                        savedImage.AccountId);
                    if (player?.Player == null)
                        return null;

                    var room = savedImage.RoomId is > 0
                        ? RoomDB.Rooms.FindById(savedImage.RoomId.Value)
                        : null;
                    long imageId =
                        ImageController.GetOrCreateSavedImageId(savedImage);

                    return new SlideshowImageResponse
                    {
                        Id = imageId,
                        ImageId = imageId,
                        SavedImageId = imageId,
                        ImageName = relativePath,
                        Username =
                            player.Player.Username ??
                            player.Player.DisplayName ??
                            $"Player{player.PlayerId}",
                        PlayerName =
                            player.Player.Username ??
                            player.Player.DisplayName ??
                            $"Player{player.PlayerId}",
                        PlayerId = checked((int)player.PlayerId),
                        RoomName = room?.Name ?? "Rec Room",
                        RoomId = savedImage.RoomId
                    };
                })
                .Where(image => image != null)
                .Cast<SlideshowImageResponse>()
                .GroupBy(
                    image => image.ImageName,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (images.Count == 0)
            {
                var fallbackPlayer =
                    (AuthStuff.GetPlayerId(Request) is long callerId
                        ? PlayerDB.Players.FindById(callerId)
                        : null) ??
                    PlayerDB.Players.FindAll()
                        .FirstOrDefault(player => player.Player != null);
                int fallbackPlayerId =
                    fallbackPlayer?.PlayerId is > 0 and <= int.MaxValue
                        ? checked((int)fallbackPlayer.PlayerId)
                        : 1;
                string fallbackPlayerName =
                    fallbackPlayer?.Player?.Username ??
                    fallbackPlayer?.Player?.DisplayName ??
                    "Mocha";
                string rroRoot = Path.Combine(imageRoot, "RROs");

                if (Directory.Exists(rroRoot))
                {
                    images.AddRange(Directory
                        .EnumerateFiles(rroRoot, "*", SearchOption.AllDirectories)
                        .Where(file => new[]
                        {
                            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"
                        }.Contains(
                            Path.GetExtension(file),
                            StringComparer.OrdinalIgnoreCase))
                        .Select(file =>
                        {
                            long imageId = Math.Max(
                                System.IO.File.GetCreationTimeUtc(file).Ticks,
                                1);
                            return new SlideshowImageResponse
                            {
                                Id = imageId,
                                ImageId = imageId,
                                SavedImageId = imageId,
                                ImageName = Path
                                    .GetRelativePath(imageRoot, file)
                                    .Replace('\\', '/'),
                                Username = fallbackPlayerName,
                                PlayerName = fallbackPlayerName,
                                PlayerId = fallbackPlayerId,
                                RoomName = Path.GetFileNameWithoutExtension(file),
                                RoomId = null
                            };
                        }));
                }
            }

            for (int index = images.Count - 1; index > 0; index--)
            {
                int swapIndex = Random.Shared.Next(index + 1);
                (images[index], images[swapIndex]) =
                    (images[swapIndex], images[index]);
            }

            DateTime validTill = DateTime.UtcNow.AddMinutes(5);
            Console.WriteLine(
                $"[IMAGE SLIDESHOW] images={images.Count} " +
                $"order=random loop=wrap validTill={validTill:O}");

            return Ok(new
            {
                ValidTill = validTill,
                Images = images
            });
        }

        private sealed class SlideshowImageResponse
        {
            public long Id { get; init; }
            public long ImageId { get; init; }
            public long SavedImageId { get; init; }
            public string ImageName { get; init; } = string.Empty;
            public string Username { get; init; } = string.Empty;
            public string PlayerName { get; init; } = string.Empty;
            public int PlayerId { get; init; }
            public string RoomName { get; init; } = string.Empty;
            public long? RoomId { get; init; }
        }

        private IActionResult GetLoadingScreenData()
        {
            string path = Path.Combine(Program.dataDir, "loadingscreen.json");

            if (System.IO.File.Exists(path))
            {
                try
                {
                    string rewritten = LoadingScreenImageService.RewriteConfiguration(
                        System.IO.File.ReadAllText(path));
                    return Content(rewritten, "application/json");
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    Console.WriteLine($"[loading screen config] {ex.Message}");
                }
            }

            return Ok(new[]
            {
                new
                {
                    ImageName = "RROs/DormRoom.jpg",
                    Message = "Welcome to Mocha! Meet up with friends and explore community rooms.",
                    PlatformMask = -1,
                    RoomNames = Array.Empty<string>(),
                    Title = "Welcome to Mocha"
                },
                new
                {
                    ImageName = "RROs/RecCenter.jpg",
                    Message = "Open the People menu to find, friend, and subscribe to other players.",
                    PlatformMask = -1,
                    RoomNames = Array.Empty<string>(),
                    Title = "Play together"
                }
            });
        }

        [HttpPost("/api/objectives/v1/cleargroup")]
        public IActionResult ClearObjectiveGroup()
        {
            return Ok(new
            {
                success = true
            });
        }

        [HttpGet("/cdn/sigs/{buildId}")]
        public IActionResult GetBuildSignature(string buildId)
        {
            Response.Headers.CacheControl = "public, max-age=3600";
            return Content("p1", "text/plain");
        }

        [HttpPost("/api/screensharereports")]
        public IActionResult CreateScreenShareReport([FromBody] JsonElement request)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            DiscordLogger.Log($"🖥️ Screen share report submitted by player ID `{id}`");

            return Ok(new { success = true });
        }

        [HttpGet("/api/config/v1/azurespeech")]
        [HttpGet("/api/config/v1/backtrace")]
        public IActionResult DisabledFeatureConfigStub()
        {
            return Ok(new { enabled = false });
        }

        [HttpGet("/api/keepsakes/globalconfig")]
        [HttpGet("/api/keepsakes/config")]
        [HttpGet("/api/keepsakes/v1/globalconfig")]
        [HttpGet("/api/keepsakes/v1/config")]
        public IActionResult KeepsakesGlobalConfig()
        {
            return Ok(new
            {
                Enabled = true,
                MaxKeepsakesPerRoom = 0,
                CollectionEnabled = true
            });
        }

        [HttpGet("/api/keepsakes/rooms/{roomId}")]
        [HttpGet("/api/keepsakes/room/{roomId}")]
        [HttpGet("/api/keepsakes/v1/rooms/{roomId}")]
        [HttpGet("/api/keepsakes/v1/room/{roomId}")]
        public IActionResult KeepsakesForRoom(long roomId)
        {
            return Ok(new
            {
                RoomId = roomId,
                KeepsakeInstances = Array.Empty<object>(),
                CollectionRecords = Array.Empty<object>(),
                CollectedKeepsakeIds = Array.Empty<long>()
            });
        }

        private IActionResult ReadAnnouncementList()
        {
            string path = Path.Combine(Program.dataDir, "announcements.json");

            if (!System.IO.File.Exists(path))
                return Ok(Array.Empty<object>());

            string json = System.IO.File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
                return Ok(Array.Empty<object>());

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);

                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("[Announcements] announcements.json must contain a JSON array.");
                    return Ok(Array.Empty<object>());
                }

                Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                Response.Headers.Pragma = "no-cache";
                Response.Headers.Expires = "0";

                Console.WriteLine($"[Announcements] Returning: {json}");

                return Content(json, "application/json");
            }
            catch (JsonException exception)
            {
                Console.WriteLine($"[Announcements] Invalid JSON: {exception.Message}");
                return Ok(Array.Empty<object>());
            }
        }

        [HttpGet("/announcements/v2/mine/unread")]
        public IActionResult GetUnreadAnnouncements()
        {

            return Ok(Array.Empty<object>());
        }

        [HttpGet("/announcements/v2/subscription/mine/unread")]
        public IActionResult GetUnreadSubscriptionAnnouncements()
        {
            return Ok(Array.Empty<object>());
        }

        [HttpGet("/api/announcement/v1/get")]
        public IActionResult GetAnnouncements()
        {
            return ReadAnnouncementList();
        }

        [HttpPost("/announcements/v2/mine/read")]
        [HttpPost("/announcements/v2/mine/markread")]
        [HttpPost("/announcements/v2/subscription/mine/read")]
        public IActionResult MarkAnnouncementsRead()
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            return Ok(new { Success = true });
        }

        [HttpPost("/api/announcement/v1/refresh")]
        public async Task<IActionResult> RefreshAnnouncements()
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            await NotiController.NotifyAnnouncementsUpdatedAsync(playerId.Value);
            return Ok(new { Success = true });
        }

        [HttpPost("/api/relationships/v1/bulkignoreplatformusers")]
        public IActionResult BulkIgnorePlatformUsers()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(new { success = true });
        }

        [HttpGet("/api/relationships/v1/favorite")]
        public IActionResult GetFavoriteRelationship([FromQuery] long id)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            ClientRelationshipDTO? relationship =
                RelationshipDB.GetClientRelationship(accountId.Value, id);
            return Ok(relationship != null &&
                      (relationship.Favorited & 1) != 0);
        }

        [HttpGet("/thread/club/{clubId:long}")]
        public IActionResult GetClubThreads(
            long clubId,
            [FromQuery] int maxCount = 10,
            [FromQuery] int mode = 0)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            return Ok(Array.Empty<object>());
        }

        [HttpPost("/pageview/consume")]
        public IActionResult ConsumePageView() => NoContent();

        [HttpPost("/api/ugcPurchasables/v1/items/bulk")]
        public IActionResult UgcPurchasablesBulk()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(Array.Empty<object>());
        }

        [HttpPost("/api/PlayerReporting/v1/deviceId")]
        public IActionResult PlayerReportingDeviceId()
        {
            var id = AuthStuff.GetPlayerId(Request);
            return Ok(new { accountId = id });
        }

        [HttpGet("/api/testcasemanagement/v1/cases")]
        public IActionResult TestCaseManagementStub()
        {
            return Ok(ServerConfig.Bracket);
        }

        [HttpGet("/api/customAvatarItems/v1/design")]
        public IActionResult GetCustomAvatarItemDesign()
        {
            long? rawPlayerId = AuthStuff.GetPlayerId(Request);

            if (!rawPlayerId.HasValue)
                return Unauthorized();

            long playerId = rawPlayerId.Value;

            string directory = Path.Combine(
                Program.dataDir,
                "CustomAvatarItems",
                playerId.ToString()
            );

            string designPath = Path.Combine(directory, "design.json");

            Console.WriteLine(
                $"[CUSTOM AVATAR ITEMS] Player ({playerId}) requested their design."
            );

            if (!System.IO.File.Exists(designPath))
            {

                return Ok(new
                {
                    customAvatarItemId = 0,
                    creatorPlayerId = playerId,
                    design = new { }
                });
            }

            try
            {
                string json = System.IO.File.ReadAllText(designPath);

                return Content(
                    json,
                    "application/json",
                    Encoding.UTF8
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[CUSTOM AVATAR ITEMS] Failed reading design: {ex}"
                );

                return StatusCode(500, new
                {
                    success = false,
                    error = "Failed to load custom avatar item design."
                });
            }
        }

        [HttpPost("/api/customAvatarItems/v1")]
        [RequestSizeLimit(12 * 1024 * 1024)]
        public async Task<IActionResult> CreateCustomAvatarItem()
        {
            long? rawPlayerId = AuthStuff.GetPlayerId(Request);
            if (!rawPlayerId.HasValue)
                return Unauthorized();

            long playerId = rawPlayerId.Value;
            string metadataJson = string.Empty;
            IFormFile? thumbnailImage = null;

            if (Request.HasFormContentType)
            {
                IFormCollection form;
                try
                {
                    form = await Request.ReadFormAsync(HttpContext.RequestAborted);
                }
                catch (InvalidDataException ex)
                {
                    Console.WriteLine(
                        $"[CUSTOM AVATAR ITEMS] Invalid multipart request from player ({playerId}): {ex.Message}");
                    return BadRequest(new { success = false, error = "Invalid multipart form." });
                }

                metadataJson = new[]
                    {
                        "metadata",
                        "Metadata",
                        "data",
                        "Data",
                        "json",
                        "Json"
                    }
                    .Select(name => form[name].FirstOrDefault())
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    ?? string.Empty;

                thumbnailImage = form.Files.GetFile("thumbnailImage")
                    ?? form.Files.GetFile("ThumbnailImage")
                    ?? form.Files.FirstOrDefault();
            }
            else
            {
                using var reader = new StreamReader(
                    Request.Body,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    leaveOpen: true);
                metadataJson = await reader.ReadToEndAsync();
            }

            Console.WriteLine(
                $"[CUSTOM AVATAR ITEMS] Player ({playerId}) metadata: {metadataJson}");

            if (string.IsNullOrWhiteSpace(metadataJson))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "Custom avatar item metadata was empty."
                });
            }

            JsonNode? receivedJson;
            try
            {
                receivedJson = JsonNode.Parse(metadataJson);
            }
            catch (JsonException ex)
            {
                Console.WriteLine(
                    $"[CUSTOM AVATAR ITEMS] Invalid metadata JSON from player ({playerId}): {ex.Message}");
                return BadRequest(new
                {
                    success = false,
                    error = "Custom avatar item metadata contained invalid JSON."
                });
            }

            if (receivedJson is not JsonObject item)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "Custom avatar item metadata must be a JSON object."
                });
            }

            long customAvatarItemId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            while (LoadCustomAvatarItems(playerId).Any(existing =>
                       ReadCustomAvatarItemId(existing) == customAvatarItemId))
            {
                customAvatarItemId++;
            }

            string createdAt = DateTime.UtcNow.ToString("O");
            item["CustomAvatarItemId"] = customAvatarItemId;
            item["customAvatarItemId"] = customAvatarItemId;
            item["Id"] ??= customAvatarItemId;
            item["CreatorPlayerId"] = playerId;
            item["creatorPlayerId"] = playerId;
            item["CreatorAccountId"] ??= playerId;
            item["CreatedAt"] ??= createdAt;
            item["createdAt"] ??= createdAt;

            string directory = Path.Combine(
                Program.dataDir,
                "CustomAvatarItems",
                playerId.ToString());
            string designPath = Path.Combine(directory, "design.json");
            string itemsPath = Path.Combine(directory, "items.json");

            try
            {
                Directory.CreateDirectory(directory);

                if (thumbnailImage is { Length: > 0 })
                {
                    string imageDirectory = Path.Combine(
                        Program.dataDir,
                        "Images",
                        "CustomAvatarItems",
                        playerId.ToString());
                    Directory.CreateDirectory(imageDirectory);

                    string extension = string.Equals(
                            thumbnailImage.ContentType,
                            "image/jpeg",
                            StringComparison.OrdinalIgnoreCase)
                        ? ".jpg"
                        : ".png";
                    string imageName = customAvatarItemId + extension;
                    string imagePath = Path.Combine(imageDirectory, imageName);

                    await using (FileStream stream = new(
                        imagePath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        await thumbnailImage.CopyToAsync(
                            stream,
                            HttpContext.RequestAborted);
                    }

                    string relativeImagePath =
                        $"CustomAvatarItems/{playerId}/{imageName}";
                    item["ThumbnailImage"] = relativeImagePath;
                    item["thumbnailImage"] = relativeImagePath;
                }

                JsonNode designToSave = item["Design"]?.DeepClone()
                    ?? item["design"]?.DeepClone()
                    ?? item.DeepClone();

                await System.IO.File.WriteAllTextAsync(
                    designPath,
                    designToSave.ToJsonString(new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }),
                    HttpContext.RequestAborted);

                JsonArray items = new();
                if (System.IO.File.Exists(itemsPath))
                {
                    try
                    {
                        items = JsonNode.Parse(
                                    await System.IO.File.ReadAllTextAsync(
                                        itemsPath,
                                        HttpContext.RequestAborted)) as JsonArray
                                ?? new JsonArray();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[CUSTOM AVATAR ITEMS] Existing items file was invalid: {ex.Message}");
                    }
                }

                items.Add(item.DeepClone());
                await System.IO.File.WriteAllTextAsync(
                    itemsPath,
                    items.ToJsonString(new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }),
                    HttpContext.RequestAborted);

                Console.WriteLine(
                    $"[CUSTOM AVATAR ITEMS] Created item ({customAvatarItemId}) for player ({playerId}).");

                return Content(
                    item.ToJsonString(),
                    "application/json",
                    Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[CUSTOM AVATAR ITEMS] Failed saving item: {ex}");
                return StatusCode(500, new
                {
                    success = false,
                    error = "Failed to save custom avatar item."
                });
            }
        }

        private static List<JsonObject> LoadCustomAvatarItems(long playerId)
        {
            string itemsPath = Path.Combine(
                Program.dataDir,
                "CustomAvatarItems",
                playerId.ToString(),
                "items.json");

            if (!System.IO.File.Exists(itemsPath))
                return new List<JsonObject>();

            try
            {
                JsonNode? root = JsonNode.Parse(System.IO.File.ReadAllText(itemsPath));
                if (root is not JsonArray array)
                    return new List<JsonObject>();

                return array
                    .OfType<JsonObject>()
                    .Select(value => value.DeepClone().AsObject())
                    .OrderByDescending(ReadCustomAvatarItemId)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[CUSTOM AVATAR ITEMS] Could not read items for player ({playerId}): {ex.Message}");
                return new List<JsonObject>();
            }
        }

        private static long ReadCustomAvatarItemId(JsonObject item)
        {
            foreach (string key in new[] { "CustomAvatarItemId", "customAvatarItemId", "Id", "id" })
            {
                if (TryReadLong(item[key], out long id))
                    return id;
            }

            return 0;
        }

        [HttpGet("/reminder/currentTokenBundles/v2")]
        public IActionResult GetCurrentTokenBundles()
        {
            DateTime expiresAt = DateTime.UtcNow.AddYears(10);

            var bundles = new List<TokenBundleResponse>
    {
        CreateTokenBundle(
            id: 9_000_001,
            productId: "mocha.tokens.2500",
            baseTokens: 2_500,
            bonusTokens: 0,
            displayPrice: "$1.99",
            expiresAt
        ),

        CreateTokenBundle(
            id: 9_000_002,
            productId: "mocha.tokens.5500",
            baseTokens: 5_000,
            bonusTokens: 500,
            displayPrice: "$3.99",
            expiresAt
        ),

        CreateTokenBundle(
            id: 9_000_003,
            productId: "mocha.tokens.12000",
            baseTokens: 10_000,
            bonusTokens: 2_000,
            displayPrice: "$7.99",
            expiresAt
        ),

        CreateTokenBundle(
            id: 9_000_004,
            productId: "mocha.tokens.35000",
            baseTokens: 25_000,
            bonusTokens: 10_000,
            displayPrice: "$19.99",
            expiresAt
        ),

        CreateTokenBundle(
            id: 9_000_005,
            productId: "mocha.tokens.70000",
            baseTokens: 50_000,
            bonusTokens: 20_000,
            displayPrice: "$34.99",
            expiresAt
        )
    };

            return Ok(bundles);
        }

        [HttpPost("/api/relationships/v1/ignore")]
        [HttpPost("/api/relationships/v2/ignore")]
        [HttpPost("/api/relationships/v1/block")]
        [HttpPost("/api/relationships/v2/block")]
        [HttpPut("/api/relationships/v1/ignore")]
        [HttpPut("/api/relationships/v2/ignore")]
        [HttpPut("/api/relationships/v1/block")]
        [HttpPut("/api/relationships/v2/block")]
        public Task<IActionResult> IgnorePlayer()
        {
            return SetRelationshipFlag(
                RelationshipFlag.Ignored,
                forcedEnabled: null);
        }

        [HttpPost("/api/relationships/v1/mute")]
        [HttpPost("/api/relationships/v2/mute")]
        [HttpPut("/api/relationships/v1/mute")]
        [HttpPut("/api/relationships/v2/mute")]
        public Task<IActionResult> MutePlayer()
        {
            return SetRelationshipFlag(
                RelationshipFlag.Muted,
                forcedEnabled: null);
        }

        [HttpPost("/api/relationships/v1/unignore")]
        [HttpPost("/api/relationships/v2/unignore")]
        [HttpPost("/api/relationships/v1/unblock")]
        [HttpPost("/api/relationships/v2/unblock")]
        [HttpDelete("/api/relationships/v1/ignore")]
        [HttpDelete("/api/relationships/v2/ignore")]
        [HttpDelete("/api/relationships/v1/block")]
        [HttpDelete("/api/relationships/v2/block")]
        public Task<IActionResult> UnignorePlayer()
        {
            return SetRelationshipFlag(
                RelationshipFlag.Ignored,
                forcedEnabled: false);
        }

        [HttpPost("/api/relationships/v1/unmute")]
        [HttpPost("/api/relationships/v2/unmute")]
        [HttpDelete("/api/relationships/v1/mute")]
        [HttpDelete("/api/relationships/v2/mute")]
        public Task<IActionResult> UnmutePlayer()
        {
            return SetRelationshipFlag(
                RelationshipFlag.Muted,
                forcedEnabled: false);
        }

        [HttpPost("/api/relationships/v1/favorite")]
        [HttpPost("/api/relationships/v2/favorite")]
        [HttpPut("/api/relationships/v1/favorite")]
        [HttpPut("/api/relationships/v2/favorite")]
        public Task<IActionResult> FavoritePlayer()
        {
            return SetRelationshipFlag(
                RelationshipFlag.Favorited,
                forcedEnabled: null);
        }

        [HttpPost("/api/relationships/v1/unfavorite")]
        [HttpPost("/api/relationships/v2/unfavorite")]
        [HttpDelete("/api/relationships/v1/favorite")]
        [HttpDelete("/api/relationships/v2/favorite")]
        public Task<IActionResult> UnfavoritePlayer()
        {
            return SetRelationshipFlag(
                RelationshipFlag.Favorited,
                forcedEnabled: false);
        }

        private enum RelationshipFlag
        {
            Ignored,
            Muted,
            Favorited
        }

        private async Task<IActionResult> SetRelationshipFlag(
            RelationshipFlag flag,
            bool? forcedEnabled)
        {
            long? sourceId = AuthStuff.GetPlayerId(Request);

            if (sourceId == null)
                return Unauthorized();

            var action = await ReadRelationshipAction(
                flag switch
                {
                    RelationshipFlag.Ignored =>
                        new[] { "Ignored", "IsIgnored", "Ignore", "Blocked", "IsBlocked", "Block" },
                    RelationshipFlag.Muted =>
                        new[] { "Muted", "IsMuted", "Mute" },
                    RelationshipFlag.Favorited =>
                        new[] { "Favorited", "IsFavorited", "Favorite" },
                    _ => Array.Empty<string>()
                });

            if (action == null)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "missing_target_player"
                });
            }

            bool enabled = forcedEnabled ?? action.Value.Enabled;

            var relationship = PlayerDB.SetRelationshipFlags(
                sourceId.Value,
                action.Value.TargetId,
                ignored: flag == RelationshipFlag.Ignored ? enabled : null,
                muted: flag == RelationshipFlag.Muted ? enabled : null,
                favorited: flag == RelationshipFlag.Favorited ? enabled : null);

            if (relationship == null)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "relationship_update_failed"
                });
            }

            ClientRelationshipDTO? clientRelationship =
                RelationshipDB.GetClientRelationship(
                    sourceId.Value,
                    action.Value.TargetId);

            if (clientRelationship == null)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = "relationship_not_persisted"
                });
            }

            string actionName = flag switch
            {
                RelationshipFlag.Ignored =>
                    enabled ? "block" : "unblock",
                RelationshipFlag.Muted =>
                    enabled ? "mute" : "unmute",
                RelationshipFlag.Favorited =>
                    enabled ? "favorite" : "unfavorite",
                _ => "relationship-change"
            };

            await NotiController.NotifyRelationshipFlagsChangedAsync(
                sourceId.Value,
                action.Value.TargetId,
                actionName);

            Console.WriteLine(
                $"[RELATIONSHIP] source={sourceId.Value} " +
                $"target={action.Value.TargetId} action={actionName} " +
                $"ignored={clientRelationship.Ignored} " +
                $"muted={clientRelationship.Muted} " +
                $"favorited={clientRelationship.Favorited} live=true");

            return Ok(clientRelationship);
        }

        private async Task<(long TargetId, bool Enabled)?>
            ReadRelationshipAction(string[] enabledKeys)
        {
            var values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var entry in Request.Query)
                values[entry.Key] = entry.Value.ToString();

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();

                foreach (var entry in form)
                    values[entry.Key] = entry.Value.ToString();
            }
            else
            {
                using var reader = new StreamReader(Request.Body);
                string body = await reader.ReadToEndAsync();

                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(body);
                        AddJsonValues(document.RootElement, values);
                    }
                    catch (JsonException)
                    {
                        foreach (var entry in
                            Microsoft.AspNetCore.WebUtilities.QueryHelpers
                                .ParseQuery("?" + body))
                        {
                            values[entry.Key] = entry.Value.ToString();
                        }

                        if (long.TryParse(
                            body.Trim().Trim('"', '\''),
                            out long rawTargetId))
                        {
                            values["accountId"] =
                                rawTargetId.ToString();
                        }
                    }
                }
            }

            string? targetText = GetValue(
                values,
                "TargetAccountId",
                "TargetPlayerId",
                "AccountId",
                "PlayerId",
                "PlayerID",
                "targetId",
                "id");

            if (!long.TryParse(targetText, out long targetId) ||
                targetId <= 0)
            {
                return null;
            }

            bool enabled = true;

            string? enabledText = GetValue(
                values,
                enabledKeys
                    .Concat(new[]
                    {
                "Enabled",
                "Value",
                "State"
                    })
                    .ToArray());

            if (bool.TryParse(enabledText, out bool parsedBool))
            {
                enabled = parsedBool;
            }
            else if (int.TryParse(enabledText, out int parsedInt))
            {
                enabled = parsedInt != 0;
            }

            return (targetId, enabled);
        }

        private async Task<HashSet<long>> ReadLongValuesAsync(params string[] keys)
        {
            var result = new HashSet<long>();
            var keySet = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in Request.Query)
            {
                if (keySet.Contains(entry.Key))
                    AddLongValuesFromText(entry.Value.ToString(), result);
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
                foreach (var entry in form)
                {
                    if (keySet.Contains(entry.Key))
                        AddLongValuesFromText(entry.Value.ToString(), result);
                }
            }
            else
            {

                Request.EnableBuffering();
                if (Request.Body.CanSeek)
                    Request.Body.Position = 0;

                using var reader = new StreamReader(
                    Request.Body,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);

                string body = await reader.ReadToEndAsync();
                if (Request.Body.CanSeek)
                    Request.Body.Position = 0;

                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(body);
                        CollectLongValues(
                            document.RootElement,
                            keySet,
                            result,
                            collectAll: document.RootElement.ValueKind == JsonValueKind.Array);
                    }
                    catch (JsonException)
                    {
                        foreach (var entry in Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery("?" + body))
                        {
                            if (keySet.Contains(entry.Key))
                                AddLongValuesFromText(entry.Value.ToString(), result);
                        }

                        AddLongValuesFromText(body, result);
                    }
                }
            }

            return result;
        }

        private static void CollectLongValues(
            JsonElement element,
            HashSet<string> keys,
            HashSet<long> result,
            bool collectAll)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        bool propertyMatches = keys.Contains(property.Name);
                        CollectLongValues(
                            property.Value,
                            keys,
                            result,
                            collectAll || propertyMatches);
                    }
                    break;

                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                        CollectLongValues(item, keys, result, collectAll);
                    break;

                case JsonValueKind.Number when collectAll:
                    if (element.TryGetInt64(out long numericValue) && numericValue > 0)
                        result.Add(numericValue);
                    break;

                case JsonValueKind.String when collectAll:
                    AddLongValuesFromText(element.GetString(), result);
                    break;
            }
        }

        private static void AddLongValuesFromText(
            string? text,
            HashSet<long> result)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            string cleaned = text.Replace("[", " ").Replace("]", " ")
                .Replace("{", " ").Replace("}", " ").Replace("\"", " ")
                .Replace("'", " ");

            foreach (string part in cleaned.Split(
                         new[] { ',', ';', '|', ' ', '\t', '\r', '\n', '=', ':' },
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (long.TryParse(part, out long value) && value > 0)
                    result.Add(value);
            }
        }

        private static TokenBundleResponse CreateTokenBundle(
            int id,
            string productId,
            int baseTokens,
            int bonusTokens,
            string displayPrice,
            DateTime expiresAt)
        {
            int totalTokens = baseTokens + bonusTokens;

            return new TokenBundleResponse
            {
                Id = id,
                TokenBundleId = id,
                BundleId = id,
                SkuId = id,
                PurchasableItemId = id,

                ProductId = productId,
                PlatformProductId = productId,
                Sku = productId,

                Name = $"{totalTokens:N0} Tokens",
                DisplayName = $"{totalTokens:N0} Tokens",

                TokenAmount = baseTokens,
                BaseTokenAmount = baseTokens,
                BonusTokenAmount = bonusTokens,
                BonusTokens = bonusTokens,
                Tokens = totalTokens,
                TotalTokens = totalTokens,

                Price = displayPrice,
                DisplayPrice = displayPrice,
                CurrencyCode = "CAD",

                ExpirationDate = expiresAt,
                ExpiresAt = expiresAt,
                EndDate = expiresAt,

                IsActive = true,
                IsAvailable = true,

                GiftDrops = Array.Empty<object>(),
                BonusGiftDrops = Array.Empty<object>()
            };
        }

        public sealed class TokenBundleResponse
        {
            public int Id { get; set; }
            public int TokenBundleId { get; set; }
            public int BundleId { get; set; }
            public int SkuId { get; set; }
            public int PurchasableItemId { get; set; }

            public string ProductId { get; set; } = "";
            public string PlatformProductId { get; set; } = "";
            public string Sku { get; set; } = "";

            public string Name { get; set; } = "";
            public string DisplayName { get; set; } = "";

            public int TokenAmount { get; set; }
            public int BaseTokenAmount { get; set; }
            public int BonusTokenAmount { get; set; }
            public int BonusTokens { get; set; }
            public int Tokens { get; set; }
            public int TotalTokens { get; set; }

            public string Price { get; set; } = "";
            public string DisplayPrice { get; set; } = "";
            public string CurrencyCode { get; set; } = "";

            public DateTime ExpirationDate { get; set; }
            public DateTime ExpiresAt { get; set; }
            public DateTime EndDate { get; set; }

            public bool IsActive { get; set; }
            public bool IsAvailable { get; set; }

            public object[] GiftDrops { get; set; } = Array.Empty<object>();
            public object[] BonusGiftDrops { get; set; } = Array.Empty<object>();
        }

        public class SanitizeRequest
        {
            public string Value { get; set; } = string.Empty;
        }
    }
}
