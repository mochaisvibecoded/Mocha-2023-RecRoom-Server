using Mocha2023.Classes.DBs;
using Mocha2023.Classes.DBs.DBClasses;
using Mocha2023.Classes;
using Mocha2023.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text.Json;

namespace Mocha2023.Controllers
{
    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    public class RecNetPageController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;

        public RecNetPageController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpGet("/recnet/")]
        public IActionResult Index()
        {
            return Serve("index.html", "text/html; charset=utf-8");
        }

        [HttpGet("/recnet/banappeal")]
        public IActionResult BanAppeal()
        {

            return Serve("index.html", "text/html; charset=utf-8");
        }

        [HttpGet("/recnet/mocha")]
        public IActionResult MochaAdminIndex()
        {
            return Serve("mocha.html", "text/html; charset=utf-8");
        }

        [HttpGet("/recnet/mocha.html")]
        public IActionResult MochaAdminHtml()
        {
            return Serve("mocha.html", "text/html; charset=utf-8");
        }

        [HttpGet("/recnet/mocha.css")]
        public IActionResult MochaAdminCss()
        {
            return Serve("mocha.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/mocha.js")]
        public IActionResult MochaAdminScript()
        {
            return Serve("mocha.js", "text/javascript; charset=utf-8");
        }

        [HttpGet("/recnet/developer")]
        public IActionResult DeveloperDashboard()
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account?.PlayerRoles?.Contains(
                    PlayerDBClasses.PlayerRoles.Developer) != true)
                return StatusCode(403);

            return Serve("developer.html", "text/html; charset=utf-8");
        }

        [HttpGet("/privacy")]
        public IActionResult Privacy()
        {
            return Serve("privacy.html", "text/html; charset=utf-8");
        }

        [HttpGet("/tos")]
        public IActionResult TermsOfService()
        {
            return Serve("tos.html", "text/html; charset=utf-8");
        }

        [HttpGet("/recnet/app.js")]
        public IActionResult Script()
        {
            return Serve("app.js", "text/javascript; charset=utf-8");
        }

        [HttpGet("/recnet/styles.css")]
        public IActionResult Styles()
        {
            return Serve("styles.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/tokens.css")]
        public IActionResult TokenStyles()
        {
            return Serve("tokens.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/dark.css")]
        public IActionResult DarkStyles()
        {
            return Serve("dark.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/badges.css")]
        public IActionResult BadgeStyles()
        {
            return Serve("badges.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/features.css")]
        public IActionResult FeatureStyles()
        {
            return Serve("features.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/rrplus.png")]
        public IActionResult RrPlusImage()
        {
            return Serve("rrplus.png", "image/png");
        }

        [HttpGet("/recnet/mocha-logo.png")]
        public IActionResult MochaLogo()
        {
            return Serve("mocha-logo.png", "image/png");
        }

        [HttpGet("/recnet/font.css")]
        public IActionResult FontStyles()
        {
            return Serve("font.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/photo.css")]
        public IActionResult PhotoStyles()
        {
            return Serve("photo.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/admin.css")]
        public IActionResult AdminStyles()
        {
            return Serve("admin.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/community-board.css")]
        public IActionResult CommunityBoardStyles()
        {
            return Serve("community-board.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/community-board-media/{fileName}")]
        public IActionResult CommunityBoardMedia(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                fileName.Length > 180 ||
                !string.Equals(
                    Path.GetFileName(fileName),
                    fileName,
                    StringComparison.Ordinal) ||
                fileName.Any(character => character == '\0'))
                return NotFound();

            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            string? contentType = extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".mp4" or ".m4v" => "video/mp4",
                ".webm" => "video/webm",
                ".mov" => "video/quicktime",
                _ => null
            };
            if (contentType == null)
                return NotFound();

            string root = Path.GetFullPath(
                Path.Combine(Program.dataDir, "CommunityBoardUploads"));
            string path = Path.GetFullPath(Path.Combine(root, fileName));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                !System.IO.File.Exists(path))
                return NotFound();

            Response.Headers.CacheControl = "public,max-age=31536000,immutable";
            return PhysicalFile(path, contentType, enableRangeProcessing: true);
        }

        [HttpGet("/recnet/developer.css")]
        public IActionResult DeveloperStyles()
        {
            return Serve("developer.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/developer.js")]
        public IActionResult DeveloperScript()
        {
            return Serve("developer.js", "text/javascript; charset=utf-8");
        }

        [HttpGet("/recnet/shop.css")]
        public IActionResult ShopStyles()
        {
            return Serve("shop.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/events.css")]
        public IActionResult EventStyles()
        {
            return Serve("events.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/privacy.css")]
        public IActionResult PrivacyStyles()
        {
            return Serve("privacy.css", "text/css; charset=utf-8");
        }

        [HttpGet("/recnet/fonts/{fontName}")]
        public IActionResult Font(string fontName)
        {
            string? file = fontName switch
            {
                "FuturaPT-Book.ttf" => "fonts/FuturaPT-Book.ttf",
                "FuturaPT-Bold.ttf" => "fonts/FuturaPT-Bold.ttf",
                "FuturaPT-Heavy.ttf" => "fonts/FuturaPT-Heavy.ttf",
                _ => null
            };
            return file == null ? NotFound() : Serve(file, "font/ttf");
        }

        private IActionResult Serve(string fileName, string contentType)
        {
            string path = string.Empty;

            string? webRootDir = _env.WebRootPath;
            if (!string.IsNullOrEmpty(webRootDir))
            {
                string candidate = Path.Combine(webRootDir, "recnet", fileName);
                if (System.IO.File.Exists(candidate))
                    path = candidate;
            }

            if (string.IsNullOrEmpty(path))
            {
                string candidate = Path.GetFullPath(
                    Path.Combine(Program.dataDir, "..", "wwwroot", "recnet", fileName));
                if (System.IO.File.Exists(candidate))
                    path = candidate;
            }

            if (string.IsNullOrEmpty(path))
            {
                string candidate = Path.Combine(
                    Directory.GetCurrentDirectory(), "wwwroot", "recnet", fileName);
                if (System.IO.File.Exists(candidate))
                    path = candidate;
            }

            if (string.IsNullOrEmpty(path))
                return NotFound("The RecNet web files are missing from wwwroot/recnet.");

            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["X-Frame-Options"] = "DENY";
            Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            Response.Headers["Permissions-Policy"] =
                "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

            if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                Response.Headers["Content-Security-Policy"] =
                    "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; " +
                    "object-src 'none'; " +
                    "script-src 'self' https://static.cloudflareinsights.com; " +
                    "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://fonts.googleapis.com; " +
                    "font-src 'self' https://cdnjs.cloudflare.com https://fonts.gstatic.com data:; " +
                    "img-src 'self' data: blob: https:; media-src 'self' blob: https:; " +
                    "connect-src 'self' https://cloudflareinsights.com; form-action 'self'";
            }

            return PhysicalFile(path, contentType);
        }
    }

    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    [Route("recnet/api")]
    public class RecNetController : ControllerBase
    {
        private static readonly Random BonusRarity50Rng = new();
        private const int BonusRarity50PercentChance = 5;

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"
        };
        private static readonly SemaphoreSlim CommunityBoardWriteLock = new(1, 1);
        private static readonly object DeveloperCpuSync = new();
        private static DateTime DeveloperCpuSampleAt = DateTime.UtcNow;
        private static TimeSpan DeveloperCpuTime =
            Process.GetCurrentProcess().TotalProcessorTime;
        private static double DeveloperCpuPercent;

        [HttpPost("auth/login")]
        [RequestSizeLimit(16 * 1024)]
        public IActionResult Login([FromBody] RecNetLogin request)
        {
            string identity = request.Identity?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrEmpty(request.Password))
                return BadRequest(new { error = "Enter your username and password." });

            string clientAddress =
                ClientNetwork.GetClientIp(Request)?.ToString() ?? "unknown";
            if (!PasswordSecurity.TryBeginLoginAttempt(
                    identity,
                    clientAddress,
                    out int retryAfterSeconds))
            {
                Response.Headers["Retry-After"] = retryAfterSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                return StatusCode(
                    StatusCodes.Status429TooManyRequests,
                    new { error = "Too many login attempts. Try again later." });
            }

            var account = PlayerDB.Players.FindAll().FirstOrDefault(x => x.Player != null &&
                (string.Equals(x.Player.Username, identity, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(x.Player.DisplayName, identity, StringComparison.OrdinalIgnoreCase) ||
                 x.PlayerId.ToString() == identity));

            bool validPassword = PasswordSecurity.VerifyLogin(
                request.Password,
                account?.Password,
                out bool needsUpgrade);

            PasswordSecurity.CompleteLoginAttempt(
                identity,
                clientAddress,
                account?.Player != null && validPassword);

            if (account?.Player == null || !validPassword)
                return Unauthorized(new { error = "That username or password is incorrect." });

            if (SteamAccessDB.TryGetBlockedSteamId(
                    account,
                    out ulong blockedSteamId))
            {
                Console.WriteLine(
                    $"[RECNET STEAM BLOCKED] account={account.PlayerId} " +
                    $"steamId={blockedSteamId}");

                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        error = "This Steam ID has been blocked from Mocha.",
                        code = "steam_id_blacklisted"
                    });
            }

            if (needsUpgrade)
                account.Password = PasswordSecurity.Hash(request.Password);

            Response.Cookies.Append("recnet_session", AuthStuff.Encode(account.PlayerId), new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(30), IsEssential = true,
                Path = "/recnet"
            });
            account.Player!.LastLoginAt = DateTime.UtcNow;
            PlayerDB.Players.Update(account);
            return Ok(ToSession(account));
        }

        [HttpPost("auth/register")]
        [RequestSizeLimit(16 * 1024)]
        public IActionResult Register([FromBody] RecNetRegistration request)
        {
            if (!RecNetDB.IsAccountCreationEnabled() ||
                !RecNetDB.IsRecNetSignupEnabled())
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        error = "New account creation is currently disabled.",
                        code = "account_creation_disabled"
                    });
            }

            if (!TryValidateRegistration(request,
                    out string username, out var platform, out ulong platformId, out string error, out int status))
                return StatusCode(status, new { error });

            if (platform == PlayerDBClasses.Platforms.Steam &&
                SteamAccessDB.IsBlacklisted(platformId))
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        error = "This Steam ID has been blocked from Mocha.",
                        code = "steam_id_blacklisted"
                    });
            }

            var account = CreateRegisteredAccount(username, request.Password!, platform, platformId);

            Response.Cookies.Append("recnet_session", AuthStuff.Encode(account.PlayerId), new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(30), IsEssential = true,
                Path = "/recnet"
            });
            return Ok(ToSession(account));
        }

        [HttpGet("admin/accounts")]
        public IActionResult GetAdminAccounts([FromQuery] string? search = null)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            string term = search?.Trim() ?? string.Empty;
            var moderationLocks = RecNetDB.ModerationLocks.FindAll().ToDictionary(x => x.AccountId);
            var accounts = PlayerDB.Players.FindAll()
                .Where(x => x.Player != null)
                .Where(x => string.IsNullOrEmpty(term) || x.PlayerId.ToString().Contains(term) ||
                    (x.Player!.Username?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.Player.DisplayName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                .OrderBy(x => x.PlayerId)
                .Take(200)
                .Select(x =>
                {
                    moderationLocks.TryGetValue(x.PlayerId, out var accountLock);
                    PlayerDB.IsPlayerBanned(x.PlayerId, out var nativeBan);
                    return new
                    {
                        accountId = x.PlayerId,
                        username = x.Player!.Username,
                        displayName = x.Player.DisplayName,
                        bio = x.Player.Bio,
                        email = x.Player.Email,
                        profileImage = ImageUrl(x.Player.ProfileImage),
                        profileImagePath = NormalizeImagePath(x.Player.ProfileImage) ?? "DefaultPFP.png",
                        bannerImagePath = NormalizeImagePath(x.Player.BannerImage) ?? string.Empty,
                        level = x.Player.Level,
                        xp = x.Player.XP,
                        isJunior = x.Player.IsJunior ?? false,
                        availableUsernameChanges = x.Player.AvailableUsernameChanges,
                        displayEmoji = x.Player.DisplayEmoji ?? string.Empty,
                        personalPronouns = x.Player.PersonalPronouns,
                        balance = PlayerDB.GetCurrencyBalance(
                            x.PlayerId,
                            PlayerDBClasses.CurrencyType.RecCenterTokens),
                        platforms = x.PlatformIds.Select(p => new { platform = p.Platform.ToString(), platformId = p.PlatformId.ToString() }),
                        roles = x.PlayerRoles.Select(r => r.ToString()),
                        ban = nativeBan == null ? null : new
                        {
                            reason = nativeBan.Message,
                            duration = nativeBan.Duration,
                            issuedAt = DateTimeOffset.FromUnixTimeSeconds(nativeBan.ModerationSetUnixTime).UtcDateTime
                        },
                        moderationLock = accountLock == null ? null : new
                        {
                            reason = accountLock.Reason,
                            issuedAt = accountLock.IssuedAt,
                            relatedUsername = accountLock.RelatedUsername,
                            isRelated = accountLock.RelatedAccountId.HasValue
                        }
                    };
                }).ToList();
            return Ok(accounts);
        }

        [HttpGet("shop")]
        public IActionResult GetWebsiteShop()
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            long? accountId = account?.Player == null ? null : account.PlayerId;
            var items = APIController.GetWebsiteStorefrontItems()
                .Select(item => new
                {
                    skuId = item.SkuId,
                    avatarItemId = item.AvatarItemId,
                    friendlyName = item.FriendlyName,
                    itemType = string.IsNullOrWhiteSpace(item.EquipmentModificationGuid)
                        ? "avatar"
                        : "skin",
                    avatarItemDesc = item.AvatarItemDesc,
                    equipmentPrefabName = item.EquipmentPrefabName,
                    equipmentModificationGuid = item.EquipmentModificationGuid,
                    tooltip = string.IsNullOrWhiteSpace(item.AvatarItemDesc)
                        ? $"{item.EquipmentPrefabName} skin"
                        : item.AvatarItemDesc,
                    thumbnailUrl = string.IsNullOrWhiteSpace(item.ThumbnailImage)
                        ? null
                        : "https://img.rec.net/" +
                          Uri.EscapeDataString(item.ThumbnailImage) +
                          "?width=512&height=512",
                    rarity = item.Rarity,
                    stars = item.Rarity switch
                    {
                        50 => 5,
                        30 => 4,
                        20 => 3,
                        10 => 2,
                        _ => 1
                    },
                    price = item.Price,
                    owned = accountId.HasValue &&
                        APIController.IsWebsiteStoreItemOwned(accountId.Value, item)
                })
                .ToList();

            return Ok(new
            {
                nextRefresh = DateTime.Today.AddDays(1).ToUniversalTime(),
                loggedIn = accountId.HasValue,
                balance = accountId.HasValue
                    ? PlayerDB.GetCurrencyBalance(
                        accountId.Value,
                        PlayerDBClasses.CurrencyType.RecCenterTokens)
                    : (int?)null,
                items
            });
        }

        [HttpPost("shop/purchase")]
        public IActionResult PurchaseWebsiteShopItem(
            [FromBody] WebsiteShopPurchase request)
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account?.Player == null)
                return Unauthorized(new { error = "Log in to purchase shop items." });

            if (!APIController.TryPurchaseWebsiteStoreItem(
                    account.PlayerId,
                    request.SkuId,
                    out var item,
                    out int balance,
                    out bool alreadyOwned,
                    out string error))
            {
                return BadRequest(new { error, balance });
            }

            return Ok(new
            {
                success = true,
                balance,
                alreadyOwned,
                itemName = item?.FriendlyName ?? "Item"
            });
        }

        [HttpGet("status")]
        public IActionResult GetWebsiteStatus()
        {
            var accounts = PlayerDB.Players.FindAll()
                .Where(value => value.Player != null)
                .ToList();

            return Ok(new
            {
                status = "online",
                generatedAt = DateTimeOffset.UtcNow,
                registeredPlayers = accounts.Count,
                onlinePlayers = accounts.Count(value =>
                    PlayerDB.GetPlayerHeartbeat(value.PlayerId).isOnline),
                connectedSockets = NotiController.ConnectedSocketCount,
                rooms = RoomDB.Rooms.Count(),
                photos = EnumeratePublicPhotos().Count()
            });
        }

        [HttpGet("ageverification/status/{code}")]
        public IActionResult GetAgeVerificationStatus(string code)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var request = AgeVerificationDB.GetByCode(code);
            if (request == null || request.AccountId != accountId.Value)
                return NotFound(new { error = "That code doesn't match an active request for your account." });

            return Ok(new
            {
                code = request.Code,
                status = request.Status,
                method = request.Method,
                createdAt = request.CreatedAt,
                submittedAt = request.SubmittedAt,
                reviewedAt = request.ReviewedAt,
                rejectionReason = request.RejectionReason
            });
        }

        [HttpPost("ageverification/submit")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> SubmitAgeVerification()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player?.Player == null)
                return Unauthorized(new { error = "Log in first." });
            if (!Request.HasFormContentType)
                return BadRequest(new { error = "Send the code, method, and a photo as multipart form data." });

            var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
            string code = (form["code"].FirstOrDefault() ?? string.Empty).Trim().ToUpperInvariant();
            string method = (form["method"].FirstOrDefault() ?? string.Empty).Trim();
            if (method is not ("ManualId" or "FaceVerification"))
                return BadRequest(new { error = "Choose Manual ID Verification or Face Verification." });

            var request = AgeVerificationDB.GetByCode(code);
            if (request == null || request.AccountId != player.PlayerId)
                return BadRequest(new { error = "That code doesn't match an active request for your account. Ask the client to generate a new one." });
            if (request.Status != "Pending")
                return BadRequest(new { error = $"This code is already {request.Status.ToLowerInvariant()}." });

            var file = form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "Attach a photo." });
            if (file.Length > 10 * 1024 * 1024)
                return BadRequest(new { error = "Photos must be 10 MB or smaller." });

            byte[] bytes;
            using (var buffer = new MemoryStream())
            {
                await file.CopyToAsync(buffer, HttpContext.RequestAborted);
                bytes = buffer.ToArray();
            }

            SixLabors.ImageSharp.Formats.IImageFormat? format;
            try
            {
                format = SixLabors.ImageSharp.Image.DetectFormat(bytes);
            }
            catch
            {
                return BadRequest(new { error = "That file is not a valid image." });
            }

            string extension = format?.FileExtensions.FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;
            if (extension is not ("png" or "jpg" or "jpeg" or "webp"))
                return BadRequest(new { error = "Use a PNG, JPG, or WebP image." });

            string methodLabel = method == "ManualId" ? "Manual ID Verification" : "Face Verification (Manually)";
            string content =
                $"🪪 **Age verification submitted** - {methodLabel}\n" +
                $"👤 `{player.Player.Username}` (ID: `{player.PlayerId}`)\n" +
                $"🔑 Code: `{code}`\n" +
                $"Review in the admin panel's Age Verification queue with this code, then Approve or Reject there.";

            DiscordLogger.LogImage(content, bytes, $"ageverification_{code}.{extension}");

            if (!AgeVerificationDB.MarkUnderReview(code, player.PlayerId, method))
                return StatusCode(500, new { error = "Could not record the submission. Try again." });

            return Ok(new { success = true, status = "UnderReview" });
        }

        [HttpPost("admin/accounts")]
        public IActionResult AdminCreateAccount([FromBody] AdminRegistration request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            if (!RecNetDB.IsAccountCreationEnabled())
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        error = "New account creation is currently disabled.",
                        code = "account_creation_disabled"
                    });
            }

            if (!TryValidateRegistration(request,
                    out string username, out var platform, out ulong platformId, out string error, out int status))
                return StatusCode(status, new { error });

            if (platform == PlayerDBClasses.Platforms.Steam &&
                SteamAccessDB.IsBlacklisted(platformId))
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        error = "That Steam ID is currently blacklisted.",
                        code = "steam_id_blacklisted"
                    });
            }

            var account = CreateRegisteredAccount(username, request.Password!, platform, platformId);
            return Ok(new
            {
                success = true,
                accountId = account.PlayerId,
                username = account.Player!.Username,
                platformAccountLimit = "unlimited"
            });
        }

        [HttpGet("admin/overview")]
        public IActionResult GetAdminOverview()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var accounts = PlayerDB.Players.FindAll().ToList();
            return Ok(new
            {
                accounts = accounts.Count,
                admins = accounts.Count(IsAdmin),
                verified = accounts.Count(IsVerified),
                rrPlus = accounts.Count(HasRRPlus),
                moderationLocks = RecNetDB.ModerationLocks.Count(),
                rooms = RoomDB.Rooms.Count(),
                photos = EnumeratePublicPhotos().Count(),
                cheers = RecNetDB.PhotoCheers.Count(),
                comments = RecNetDB.PhotoComments.Count()
            });
        }

        public class AdminRoomImportFromUrlRequest
        {
            public string? Url { get; set; }
            public long? CreatorAccountId { get; set; }
            public bool ReplaceExisting { get; set; } = true;
        }

        [HttpPost("admin/rooms/import-url")]
        public IActionResult ImportAdminRoomFromUrl(
            [FromBody] AdminRoomImportFromUrlRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can import room archives." });

            if (string.IsNullOrWhiteSpace(request.Url))
                return BadRequest(new { error = "Enter the URL of the room's own folder." });

            long creatorAccountId = request.CreatorAccountId is > 0
                ? request.CreatorAccountId.Value
                : admin!.PlayerId;
            if (request.CreatorAccountId is > 0 &&
                PlayerDB.Players.FindById(request.CreatorAccountId.Value)?.Player == null)
            {
                return BadRequest(new { error = "The requested creator account does not exist." });
            }

            string url = request.Url.Trim();
            bool replaceExisting = request.ReplaceExisting;
            MassImportJobs.Job job = MassImportJobs.Create("single-url");
            _ = Task.Run(() => RunSingleUrlImport(job, url, creatorAccountId, replaceExisting));

            return Ok(new { jobId = job.Id });
        }

        private static async Task RunSingleUrlImport(
            MassImportJobs.Job job,
            string url,
            long creatorAccountId,
            bool replaceExisting)
        {
            job.TotalFound = 1;
            job.Status = "running";
            job.CurrentRoomName = url;

            var outcome = new MassImportJobs.RoomOutcome { Name = url, SourceUrl = url };
            try
            {
                ShowdownImporter.ImportResult result = await ShowdownImporter.ImportFromUrlAsync(
                    url,
                    creatorAccountId,
                    replaceExisting,
                    CancellationToken.None);
                outcome.Success = true;
                outcome.RoomId = result.RoomId;
                outcome.SubRoomsImported = result.SubRoomsImported;
                outcome.SavesImported = result.SavesImported;
                outcome.BakedAssetsImported = result.BakedAssetsImported;
                outcome.AssetBundlesCopied = result.AssetBundlesCopied;
            }
            catch (Exception ex)
            {
                outcome.Success = false;
                outcome.Error = ex.Message;
                Console.WriteLine($"[ADMIN ROOM IMPORT URL FAILED] job={job.Id} {ex}");
            }

            job.AddResult(outcome);
            job.Status = "completed";
            job.FinishedAt = DateTime.UtcNow;
        }

        [HttpPost("admin/rooms/import")]
        [HttpPut("admin/rooms/import")]
        [HttpPost("admin/rooms")]
        [HttpPut("admin/rooms")]
        [RequestSizeLimit(512L * 1024L * 1024L)]
        [RequestFormLimits(MultipartBodyLengthLimit = 512L * 1024L * 1024L)]
        public async Task<IActionResult> ImportAdminRoom()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can import room archives." });

            if (!Request.HasFormContentType)
            {
                string rawJson;
                using (var reader = new StreamReader(Request.Body))
                    rawJson = await reader.ReadToEndAsync(HttpContext.RequestAborted);

                if (string.IsNullOrWhiteSpace(rawJson))
                {
                    return BadRequest(new
                    {
                        error = "Send either room metadata JSON or multipart/form-data containing a ZIP/room.json file."
                    });
                }

                try
                {
                    var directResult = ShowdownImporter.ImportUnityMetadata(
                        rawJson,
                        admin!.PlayerId,
                        replaceExisting: true);
                    return Ok(new
                    {
                        success = true,
                        importMode = "unity-scene-json",
                        roomId = directResult.RoomId,
                        subRoomsImported = directResult.SubRoomsImported,
                        savesImported = directResult.SavesImported,
                        blobsCopied = directResult.BlobsCopied,
                        savesSkipped = directResult.SavesSkipped,
                        playableSubRoomName = directResult.PlayableSubRoomName,
                        playableSubRoomId = directResult.PlayableSubRoomId,
                        bakedAssetsImported = directResult.BakedAssetsImported,
                        assetBundlesCopied = directResult.AssetBundlesCopied,
                        assetBundlesMissing = directResult.AssetBundlesMissing,
                        imageCopied = directResult.ImageCopied,
                        unityEngineVersions = directResult.UnityEngineVersions
                    });
                }
                catch (Exception exception) when (
                    exception is JsonException or InvalidDataException or InvalidOperationException)
                {
                    return BadRequest(new { error = exception.Message });
                }
            }

            var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
            var archive = form.Files.FirstOrDefault(file =>
                              string.Equals(file.Name, "file", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(file.Name, "archive", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(file.Name, "room", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(file.Name, "zip", StringComparison.OrdinalIgnoreCase))
                          ?? form.Files.FirstOrDefault();

            if (archive == null || archive.Length <= 0)
                return BadRequest(new { error = "No room export ZIP was uploaded." });
            if (archive.Length > 512L * 1024L * 1024L)
                return StatusCode(413, new { error = "Room export ZIPs must be 512 MB or smaller." });

            long creatorAccountId = admin!.PlayerId;
            if (long.TryParse(form["creatorAccountId"].FirstOrDefault(), out long requestedCreator) &&
                requestedCreator > 0)
            {
                if (PlayerDB.Players.FindById(requestedCreator)?.Player == null)
                    return BadRequest(new { error = "The requested creator account does not exist." });
                creatorAccountId = requestedCreator;
            }

            bool replaceExisting = !bool.TryParse(
                form["replaceExisting"].FirstOrDefault(),
                out bool parsedReplace) || parsedReplace;

            string archiveExtension = Path.GetExtension(archive.FileName ?? string.Empty);
            bool looksLikeJson = archiveExtension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                archive.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
            if (looksLikeJson)
            {
                try
                {
                    using var reader = new StreamReader(archive.OpenReadStream());
                    string metadataJson = await reader.ReadToEndAsync(HttpContext.RequestAborted);
                    var directResult = ShowdownImporter.ImportUnityMetadata(
                        metadataJson,
                        creatorAccountId,
                        replaceExisting);
                    return Ok(new
                    {
                        success = true,
                        importMode = "unity-scene-json",
                        roomId = directResult.RoomId,
                        subRoomsImported = directResult.SubRoomsImported,
                        savesImported = directResult.SavesImported,
                        blobsCopied = directResult.BlobsCopied,
                        savesSkipped = directResult.SavesSkipped,
                        playableSubRoomName = directResult.PlayableSubRoomName,
                        playableSubRoomId = directResult.PlayableSubRoomId,
                        bakedAssetsImported = directResult.BakedAssetsImported,
                        assetBundlesCopied = directResult.AssetBundlesCopied,
                        assetBundlesMissing = directResult.AssetBundlesMissing,
                        imageCopied = directResult.ImageCopied,
                        unityEngineVersions = directResult.UnityEngineVersions
                    });
                }
                catch (Exception exception) when (
                    exception is JsonException or InvalidDataException or InvalidOperationException)
                {
                    return BadRequest(new { error = exception.Message });
                }
            }

            string importId = Guid.NewGuid().ToString("N");
            string importRoot = Path.Combine(Program.dataDir, "Temp", "RoomImports", importId);
            string archivePath = Path.Combine(importRoot, "room-export.zip");
            string extractedRoot = Path.Combine(importRoot, "extracted");
            Directory.CreateDirectory(extractedRoot);

            try
            {
                await using (var input = archive.OpenReadStream())
                await using (var output = new FileStream(
                    archivePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await input.CopyToAsync(output, HttpContext.RequestAborted);
                    await output.FlushAsync(HttpContext.RequestAborted);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                try
                {
                    if (Directory.Exists(importRoot))
                        Directory.Delete(importRoot, recursive: true);
                }
                catch (Exception cleanupException)
                {
                    Console.WriteLine($"[ADMIN ROOM IMPORT CLEANUP] {cleanupException.Message}");
                }

                Console.WriteLine($"[ADMIN ROOM IMPORT FAILED] {exception}");
                return StatusCode(500, new { error = "Could not save the uploaded ZIP.", detail = exception.Message });
            }

            MassImportJobs.Job job = MassImportJobs.Create("single-zip");
            _ = Task.Run(() => RunSingleZipImport(job, importRoot, extractedRoot, creatorAccountId, replaceExisting));

            Console.WriteLine(
                $"[ADMIN ROOM IMPORT] admin={admin.PlayerId} creator={creatorAccountId} job={job.Id} started");

            return Ok(new { jobId = job.Id });
        }

        private static void RunSingleZipImport(
            MassImportJobs.Job job,
            string importRoot,
            string extractedRoot,
            long creatorAccountId,
            bool replaceExisting)
        {
            try
            {
                string archivePath = Path.Combine(importRoot, "room-export.zip");
                ExtractZipSafely(archivePath, extractedRoot);

                string[] roomJsonFiles = Directory.GetFiles(extractedRoot, "room.json", SearchOption.AllDirectories);
                if (roomJsonFiles.Length == 0)
                {
                    job.Status = "failed";
                    job.FatalError = "The ZIP does not contain room.json.";
                    return;
                }

                string roomJson = roomJsonFiles
                    .OrderBy(path => Path.GetRelativePath(extractedRoot, path).Count(ch =>
                        ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
                    .ThenBy(path => path.Length)
                    .First();
                string exportRoot = Path.GetDirectoryName(roomJson)!;
                string roomName = Path.GetFileName(exportRoot);

                job.TotalFound = 1;
                job.Status = "running";
                job.CurrentRoomName = roomName;

                var outcome = new MassImportJobs.RoomOutcome { Name = roomName };
                try
                {
                    var result = ShowdownImporter.Import(exportRoot, creatorAccountId, replaceExisting);
                    outcome.Success = true;
                    outcome.RoomId = result.RoomId;
                    outcome.SubRoomsImported = result.SubRoomsImported;
                    outcome.SavesImported = result.SavesImported;
                    outcome.BakedAssetsImported = result.BakedAssetsImported;
                    outcome.AssetBundlesCopied = result.AssetBundlesCopied;

                    Console.WriteLine(
                        $"[ADMIN ROOM IMPORT] job={job.Id} room={result.RoomId} " +
                        $"subrooms={result.SubRoomsImported} saves={result.SavesImported}");
                }
                catch (Exception ex)
                {
                    outcome.Success = false;
                    outcome.Error = ex.Message;
                    Console.WriteLine($"[ADMIN ROOM IMPORT FAILED] job={job.Id} {ex}");
                }

                job.AddResult(outcome);
                job.Status = "completed";
            }
            catch (Exception ex)
            {
                job.Status = "failed";
                job.FatalError = ex.Message;
                Console.WriteLine($"[ADMIN ROOM IMPORT FAILED] job={job.Id} {ex}");
            }
            finally
            {
                job.FinishedAt = DateTime.UtcNow;
                try
                {
                    if (Directory.Exists(importRoot))
                        Directory.Delete(importRoot, recursive: true);
                }
                catch (Exception cleanupException)
                {
                    Console.WriteLine($"[ADMIN ROOM IMPORT CLEANUP] {cleanupException.Message}");
                }
            }
        }

        public class AdminRoomImportBatchFromUrlRequest
        {
            public string? Url { get; set; }
            public long? CreatorAccountId { get; set; }
            public bool ReplaceExisting { get; set; } = true;
        }

        [HttpGet("admin/rooms/import-batch/{jobId}")]
        public IActionResult GetAdminRoomBatchStatus(string jobId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can import room archives." });

            MassImportJobs.Job? job = MassImportJobs.Get(jobId);
            if (job == null)
                return NotFound(new { error = "Unknown import job - it may have finished more than 6 hours ago." });

            List<MassImportJobs.RoomOutcome> results = job.SnapshotResults();
            return Ok(new
            {
                jobId = job.Id,
                kind = job.Kind,
                status = job.Status,
                fatalError = job.FatalError,
                totalFound = job.TotalFound,
                currentRoomName = job.CurrentRoomName,
                completedCount = results.Count,
                successCount = results.Count(r => r.Success),
                failedCount = results.Count(r => !r.Success),
                startedAt = job.StartedAt,
                finishedAt = job.FinishedAt,
                results = results.Select(r => new
                {
                    r.Name,
                    r.SourceUrl,
                    r.Success,
                    r.Error,
                    r.RoomId,
                    r.SubRoomsImported,
                    r.SavesImported,
                    r.BakedAssetsImported,
                    r.AssetBundlesCopied
                })
            });
        }

        [HttpPost("admin/rooms/import-batch/url")]
        public async Task<IActionResult> ImportAdminRoomBatchFromUrl(
            [FromBody] AdminRoomImportBatchFromUrlRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can import room archives." });

            if (string.IsNullOrWhiteSpace(request.Url))
                return BadRequest(new { error = "Enter the URL of the rooms index folder." });

            long creatorAccountId = request.CreatorAccountId is > 0
                ? request.CreatorAccountId.Value
                : admin!.PlayerId;
            if (request.CreatorAccountId is > 0 &&
                PlayerDB.Players.FindById(request.CreatorAccountId.Value)?.Player == null)
            {
                return BadRequest(new { error = "The requested creator account does not exist." });
            }

            List<(string Name, string Url)> roomFolders;
            try
            {
                roomFolders = await ShowdownImporter.DiscoverRoomFoldersAsync(
                    request.Url.Trim(),
                    HttpContext.RequestAborted);
            }
            catch (Exception exception) when (
                exception is InvalidDataException or HttpRequestException or TaskCanceledException)
            {
                return BadRequest(new { error = exception.Message });
            }

            MassImportJobs.Job job = MassImportJobs.Create("url");
            job.TotalFound = roomFolders.Count;
            job.Status = "running";

            bool replaceExisting = request.ReplaceExisting;
            _ = Task.Run(() => RunUrlBatchImport(job, roomFolders, creatorAccountId, replaceExisting));

            return Ok(new { jobId = job.Id, roomsFound = roomFolders.Count });
        }

        private static async Task RunUrlBatchImport(
            MassImportJobs.Job job,
            List<(string Name, string Url)> roomFolders,
            long creatorAccountId,
            bool replaceExisting)
        {
            foreach ((string Name, string Url) folder in roomFolders)
            {
                job.CurrentRoomName = folder.Name;
                var outcome = new MassImportJobs.RoomOutcome { Name = folder.Name, SourceUrl = folder.Url };
                try
                {
                    ShowdownImporter.ImportResult result = await ShowdownImporter.ImportFromUrlAsync(
                        folder.Url,
                        creatorAccountId,
                        replaceExisting,
                        CancellationToken.None);
                    outcome.Success = true;
                    outcome.RoomId = result.RoomId;
                    outcome.SubRoomsImported = result.SubRoomsImported;
                    outcome.SavesImported = result.SavesImported;
                    outcome.BakedAssetsImported = result.BakedAssetsImported;
                    outcome.AssetBundlesCopied = result.AssetBundlesCopied;
                }
                catch (Exception ex)
                {
                    outcome.Success = false;
                    outcome.Error = ex.Message;
                    Console.WriteLine($"[ADMIN ROOM BATCH IMPORT URL] {folder.Name} failed: {ex.Message}");
                }

                job.AddResult(outcome);
            }

            job.Status = "completed";
            job.FinishedAt = DateTime.UtcNow;
        }

        [HttpPost("admin/rooms/import-batch/zip")]
        [RequestSizeLimit(2L * 1024L * 1024L * 1024L)]
        [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024L * 1024L * 1024L)]
        public async Task<IActionResult> ImportAdminRoomBatchFromZip()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can import room archives." });

            if (!Request.HasFormContentType)
                return BadRequest(new { error = "Send the ZIP as multipart/form-data." });

            var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
            var archive = form.Files.FirstOrDefault(file =>
                              string.Equals(file.Name, "file", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(file.Name, "archive", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(file.Name, "zip", StringComparison.OrdinalIgnoreCase))
                          ?? form.Files.FirstOrDefault();

            if (archive == null || archive.Length <= 0)
                return BadRequest(new { error = "No room export ZIP was uploaded." });

            long creatorAccountId = admin!.PlayerId;
            if (long.TryParse(form["creatorAccountId"].FirstOrDefault(), out long requestedCreator) &&
                requestedCreator > 0)
            {
                if (PlayerDB.Players.FindById(requestedCreator)?.Player == null)
                    return BadRequest(new { error = "The requested creator account does not exist." });
                creatorAccountId = requestedCreator;
            }

            bool replaceExisting = !bool.TryParse(
                form["replaceExisting"].FirstOrDefault(),
                out bool parsedReplace) || parsedReplace;

            string importId = Guid.NewGuid().ToString("N");
            string importRoot = Path.Combine(Program.dataDir, "Temp", "RoomImports", importId);
            string archivePath = Path.Combine(importRoot, "room-export.zip");
            string extractedRoot = Path.Combine(importRoot, "extracted");
            Directory.CreateDirectory(extractedRoot);

            await using (var input = archive.OpenReadStream())
            await using (var output = new FileStream(
                archivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, HttpContext.RequestAborted);
                await output.FlushAsync(HttpContext.RequestAborted);
            }

            MassImportJobs.Job job = MassImportJobs.Create("zip");
            _ = Task.Run(() => RunZipBatchImport(job, importRoot, extractedRoot, creatorAccountId, replaceExisting));

            return Ok(new { jobId = job.Id });
        }

        private static void RunZipBatchImport(
            MassImportJobs.Job job,
            string importRoot,
            string extractedRoot,
            long creatorAccountId,
            bool replaceExisting)
        {
            try
            {
                string archivePath = Path.Combine(importRoot, "room-export.zip");
                ExtractZipSafely(archivePath, extractedRoot);

                string[] roomJsonFiles = Directory.GetFiles(extractedRoot, "room.json", SearchOption.AllDirectories);
                job.TotalFound = roomJsonFiles.Length;

                if (roomJsonFiles.Length == 0)
                {
                    job.Status = "failed";
                    job.FatalError = "The ZIP does not contain any room.json files.";
                    return;
                }

                job.Status = "running";
                foreach (string roomJsonPath in roomJsonFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    string exportRoot = Path.GetDirectoryName(roomJsonPath)!;
                    string roomName = Path.GetFileName(exportRoot);
                    job.CurrentRoomName = roomName;

                    var outcome = new MassImportJobs.RoomOutcome { Name = roomName };
                    try
                    {
                        var result = ShowdownImporter.Import(exportRoot, creatorAccountId, replaceExisting);
                        outcome.Success = true;
                        outcome.RoomId = result.RoomId;
                        outcome.SubRoomsImported = result.SubRoomsImported;
                        outcome.SavesImported = result.SavesImported;
                        outcome.BakedAssetsImported = result.BakedAssetsImported;
                        outcome.AssetBundlesCopied = result.AssetBundlesCopied;
                    }
                    catch (Exception ex)
                    {
                        outcome.Success = false;
                        outcome.Error = ex.Message;
                        Console.WriteLine($"[ADMIN ROOM BATCH IMPORT ZIP] {roomName} failed: {ex.Message}");
                    }

                    job.AddResult(outcome);
                }

                job.Status = "completed";
            }
            catch (Exception ex)
            {
                job.Status = "failed";
                job.FatalError = ex.Message;
            }
            finally
            {
                job.FinishedAt = DateTime.UtcNow;
                try
                {
                    if (Directory.Exists(importRoot))
                        Directory.Delete(importRoot, recursive: true);
                }
                catch (Exception cleanupEx)
                {
                    Console.WriteLine($"[ADMIN ROOM BATCH IMPORT CLEANUP] {cleanupEx.Message}");
                }
            }
        }

        private static void ExtractZipSafely(string archivePath, string extractedRoot)
        {
            const int maxEntries = 20_000;
            const long maxExtractedBytes = 2L * 1024L * 1024L * 1024L;
            int entryCount = 0;
            long extractedBytes = 0;
            string canonicalExtractedRoot = Path.GetFullPath(extractedRoot) + Path.DirectorySeparatorChar;

            using var zip = System.IO.Compression.ZipFile.OpenRead(archivePath);
            foreach (var entry in zip.Entries)
            {
                entryCount++;
                if (entryCount > maxEntries)
                    throw new InvalidDataException("The room archive contains too many files.");

                string entryName = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(entryName) ||
                    entryName.Length > 1_024 ||
                    entryName.Contains('\0'))
                {
                    throw new InvalidDataException("The room archive contains an invalid filename.");
                }

                string destination = Path.GetFullPath(
                    Path.Combine(extractedRoot, entryName.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(canonicalExtractedRoot, StringComparison.Ordinal))
                    throw new InvalidDataException("The room archive attempted to write outside its import folder.");

                bool isDirectory = entryName.EndsWith("/", StringComparison.Ordinal);
                if (isDirectory)
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                if (entry.Length < 0 || entry.Length > maxExtractedBytes - extractedBytes)
                    throw new InvalidDataException("The extracted room export is larger than 2 GB.");
                extractedBytes += entry.Length;

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var entryInput = entry.Open();
                using var entryOutput = new FileStream(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                entryInput.CopyTo(entryOutput);
            }
        }

        [HttpPost("admin/rooms/import-batch/json")]
        public async Task<IActionResult> ImportAdminRoomBatchFromJson()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can import room archives." });

            string rawBody;
            using (var reader = new StreamReader(Request.Body))
                rawBody = await reader.ReadToEndAsync(HttpContext.RequestAborted);

            if (string.IsNullOrWhiteSpace(rawBody))
                return BadRequest(new { error = "Send a JSON array of room metadata objects." });

            long creatorAccountId = admin!.PlayerId;
            if (long.TryParse(Request.Query["creatorAccountId"], out long requestedCreator) && requestedCreator > 0)
            {
                if (PlayerDB.Players.FindById(requestedCreator)?.Player == null)
                    return BadRequest(new { error = "The requested creator account does not exist." });
                creatorAccountId = requestedCreator;
            }
            bool replaceExisting = !bool.TryParse(Request.Query["replaceExisting"], out bool parsedReplace) || parsedReplace;

            List<(string Json, string Label)> roomJsonBlobs;
            try
            {
                using JsonDocument document = JsonDocument.Parse(rawBody, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return BadRequest(new
                    {
                        error = "Expected a JSON array of room metadata objects, e.g. [{...}, {...}]."
                    });
                }

                roomJsonBlobs = document.RootElement.EnumerateArray()
                    .Select((element, index) =>
                    {
                        string label = element.ValueKind == JsonValueKind.Object &&
                            element.TryGetProperty("Name", out JsonElement nameProp) &&
                            nameProp.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(nameProp.GetString())
                                ? nameProp.GetString()!
                                : $"item {index + 1}";
                        return (Json: element.GetRawText(), Label: label);
                    })
                    .ToList();
            }
            catch (JsonException exception)
            {
                return BadRequest(new { error = $"Invalid JSON: {exception.Message}" });
            }

            if (roomJsonBlobs.Count == 0)
                return BadRequest(new { error = "The JSON array is empty." });

            MassImportJobs.Job job = MassImportJobs.Create("json");
            job.TotalFound = roomJsonBlobs.Count;
            job.Status = "running";

            _ = Task.Run(() => RunJsonBatchImport(job, roomJsonBlobs, creatorAccountId, replaceExisting));

            return Ok(new { jobId = job.Id, roomsFound = roomJsonBlobs.Count });
        }

        private static void RunJsonBatchImport(
            MassImportJobs.Job job,
            List<(string Json, string Label)> roomJsonBlobs,
            long creatorAccountId,
            bool replaceExisting)
        {
            foreach ((string Json, string Label) item in roomJsonBlobs)
            {
                job.CurrentRoomName = item.Label;
                var outcome = new MassImportJobs.RoomOutcome { Name = item.Label };
                try
                {
                    var result = ShowdownImporter.ImportUnityMetadata(item.Json, creatorAccountId, replaceExisting);
                    outcome.Success = true;
                    outcome.RoomId = result.RoomId;
                    outcome.SubRoomsImported = result.SubRoomsImported;
                    outcome.SavesImported = result.SavesImported;
                    outcome.BakedAssetsImported = result.BakedAssetsImported;
                    outcome.AssetBundlesCopied = result.AssetBundlesCopied;
                }
                catch (Exception ex)
                {
                    outcome.Success = false;
                    outcome.Error = ex.Message;
                    Console.WriteLine($"[ADMIN ROOM BATCH IMPORT JSON] {item.Label} failed: {ex.Message}");
                }

                job.AddResult(outcome);
            }

            job.Status = "completed";
            job.FinishedAt = DateTime.UtcNow;
        }

        [HttpGet("admin/rooms/{roomId:long}/export")]
        public IActionResult ExportAdminRoom(long roomId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can export room backups." });

            var room = RoomDB.GetRoom(roomId);
            if (room == null || room.RoomId <= 0)
                return NotFound(new { error = "Room not found." });

            string exportId = Guid.NewGuid().ToString("N");
            string exportRoot = Path.Combine(Program.dataDir, "Temp", "RoomExports", exportId);
            string zipPath = exportRoot + ".zip";

            try
            {
                ShowdownImporter.RoomExportResult result =
                    ShowdownImporter.ExportRoomToDirectory(roomId, exportRoot);

                if (System.IO.File.Exists(zipPath))
                    System.IO.File.Delete(zipPath);
                System.IO.Compression.ZipFile.CreateFromDirectory(
                    exportRoot,
                    zipPath,
                    System.IO.Compression.CompressionLevel.Optimal,
                    includeBaseDirectory: false);

                Console.WriteLine(
                    $"[ADMIN ROOM EXPORT] admin={admin!.PlayerId} room={roomId} " +
                    $"subrooms={result.SubRoomsExported} saves={result.SavesExported} " +
                    $"image={result.ImageExported} bakedAssets={result.BakedAssetsExported} " +
                    $"bundles={result.AssetBundlesExported}");

                byte[] zipBytes = System.IO.File.ReadAllBytes(zipPath);
                string safeName = string.Concat(
                    room.Name.Where(character =>
                        !System.IO.Path.GetInvalidFileNameChars().Contains(character)));
                if (string.IsNullOrWhiteSpace(safeName))
                    safeName = $"room-{roomId}";

                return File(
                    zipBytes,
                    "application/zip",
                    $"{safeName}-{roomId}-backup.zip");
            }
            catch (FileNotFoundException exception)
            {
                return NotFound(new { error = exception.Message });
            }
            catch (InvalidDataException exception)
            {
                return BadRequest(new { error = exception.Message });
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[ADMIN ROOM EXPORT FAILED] {exception}");
                return StatusCode(500, new { error = "Room export failed.", detail = exception.Message });
            }
            finally
            {
                try
                {
                    if (Directory.Exists(exportRoot))
                        Directory.Delete(exportRoot, recursive: true);
                    if (System.IO.File.Exists(zipPath))
                        System.IO.File.Delete(zipPath);
                }
                catch (Exception cleanupException)
                {
                    Console.WriteLine($"[ADMIN ROOM EXPORT CLEANUP] {cleanupException.Message}");
                }
            }
        }

        [HttpGet("admin/rooms")]
        public IActionResult GetAdminRooms(
            [FromQuery] string? search = null,
            [FromQuery] bool includeDorms = false,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            string term = search?.Trim() ?? string.Empty;
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 250);

            var query = RoomDB.Rooms.FindAll()
                .Where(room => includeDorms || !room.IsDorm)
                .Where(room =>
                    string.IsNullOrEmpty(term) ||
                    room.RoomId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (room.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (room.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    room.CreatorAccountId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(room => room.State == RoomDBClasses.RoomState.Active)
                .ThenByDescending(room => room.CreatedAt)
                .ThenByDescending(room => room.RoomId);

            int total = query.Count();
            var rooms = query
                .Skip(skip)
                .Take(take)
                .Select(ToAdminRoomSummary)
                .ToList();

            return Ok(new { results = rooms, total, skip, take });
        }

        [HttpGet("admin/rooms/{roomId:long}")]
        public IActionResult GetAdminRoom(long roomId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var room = RoomDB.Rooms.FindById(roomId);
            return room == null
                ? NotFound(new { error = "Room not found." })
                : Ok(ToAdminRoomDetails(room));
        }

        [HttpPut("admin/rooms/{roomId:long}")]
        public IActionResult UpdateAdminRoom(
            long roomId,
            [FromBody] AdminRoomUpdate request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound(new { error = "Room not found." });

            string name = request.Name?.Trim().TrimStart('^') ?? string.Empty;
            if (name.Length is < 1 or > 50 ||
                name.Any(character => character == '\0'))
            {
                return BadRequest(new { error = "Room name must be 1-50 characters." });
            }

            bool duplicateName = RoomDB.Rooms.FindAll().Any(other =>
                other.RoomId != roomId &&
                string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase));
            if (duplicateName)
                return Conflict(new { error = "Another room already uses that name." });

            string description = request.Description?.Trim() ?? string.Empty;
            if (description.Length > 2_000 || description.Any(character => character == '\0'))
                return BadRequest(new { error = "Description cannot exceed 2,000 characters." });
            if (request.MaxPlayers is < 1 or > 100)
                return BadRequest(new { error = "Max players must be between 1 and 100." });
            if (request.MinLevel is < 0 or > 50)
                return BadRequest(new { error = "Minimum level must be between 0 and 50." });
            if (!Enum.TryParse(
                    request.Accessibility,
                    true,
                    out RoomDBClasses.RoomAccessibility accessibility))
            {
                return BadRequest(new { error = "Choose Private, Public, or Unlisted accessibility." });
            }
            if (!Enum.TryParse(
                    request.State,
                    true,
                    out RoomDBClasses.RoomState state))
            {
                return BadRequest(new { error = "Choose a valid room state." });
            }

            string imageName = request.ImageName?.Trim() ?? string.Empty;
            if (imageName.Length > 300 ||
                imageName.Contains("..", StringComparison.Ordinal) ||
                imageName.Any(character => character == '\0'))
            {
                return BadRequest(new { error = "Room image path is invalid." });
            }

            string[] tags = (request.Tags ?? Array.Empty<string>())
                .Select(tag => tag.Trim().TrimStart('#').ToLowerInvariant())
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (tags.Length > 30 ||
                tags.Any(tag =>
                    tag.Length > 32 ||
                    tag.Any(character =>
                        !char.IsLetterOrDigit(character) &&
                        character is not '-' and not '_')))
            {
                return BadRequest(new { error = "Use up to 30 tags containing letters, numbers, hyphens, or underscores." });
            }

            room.Name = name;
            room.Description = description;
            room.ImageName = imageName;
            room.Accessibility = accessibility;
            room.State = state;
            room.MaxPlayers = request.MaxPlayers;
            room.MinLevel = request.MinLevel;
            room.CloningAllowed = request.CloningAllowed;
            room.DisableMicAutoMute = request.DisableMicAutoMute;
            room.DisableRoomComments = request.DisableRoomComments;
            room.EncryptVoiceChat = request.EncryptVoiceChat;
            room.ToxmodEnabled = request.ToxmodEnabled;
            room.LoadScreenLocked = request.LoadScreenLocked;
            room.AutoLocalizeRoom = request.AutoLocalizeRoom;
            room.IsDeveloperOwned = request.IsDeveloperOwned;
            room.SupportsLevelVoting = request.SupportsLevelVoting;
            room.IsRRO = request.IsRRO;
            room.SupportsScreens = request.SupportsScreens;
            room.SupportsWalkVR = request.SupportsWalkVR;
            room.SupportsTeleportVR = request.SupportsTeleportVR;
            room.SupportsVRLow = request.SupportsVRLow;
            room.SupportsQuest2 = request.SupportsQuest2;
            room.SupportsMobile = request.SupportsMobile;
            room.SupportsJuniors = request.SupportsJuniors;

            bool betaEnabled = request.CreativeToolsBetaEnabled
                ?? request.SupportsBetaContent
                ?? request.IsBeta
                ?? room.CreativeToolsBetaEnabled;
            room.CreativeToolsBetaEnabled = betaEnabled;

            bool canonicalBaseRoom = RoomDB.IsCanonicalBaseRoom(room);
            room.IsBaseRoom = canonicalBaseRoom ||
                (request.IsBaseRoom ?? room.IsBaseRoom);
            if (room.IsBaseRoom)
            {
                room.IsDorm = false;
                room.IsRRO = true;
                room.Accessibility = RoomDBClasses.RoomAccessibility.Public;
                room.State = RoomDBClasses.RoomState.Active;
            }

            var persistedTags = tags.Select(tag => new RoomDBClasses.Tags
            {
                Tag = tag,
                Type = RoomDBClasses.TagType.General
            }).ToList();
            if (betaEnabled && !persistedTags.Any(tag =>
                    string.Equals(tag.Tag, "beta", StringComparison.OrdinalIgnoreCase)))
            {
                persistedTags.Add(new RoomDBClasses.Tags
                {
                    Tag = "beta",
                    Type = RoomDBClasses.TagType.Auto
                });
            }
            if (room.IsBaseRoom)
            {
                persistedTags.RemoveAll(tag =>
                    string.Equals(tag.Tag, "base", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tag.Tag, "rro", StringComparison.OrdinalIgnoreCase));
                persistedTags.Add(new RoomDBClasses.Tags
                {
                    Tag = "base",
                    Type = RoomDBClasses.TagType.Auto
                });
                persistedTags.Add(new RoomDBClasses.Tags
                {
                    Tag = "rro",
                    Type = RoomDBClasses.TagType.AGOnly
                });
            }
            room.Tags = persistedTags
                .GroupBy(tag => tag.Tag, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(30)
                .ToList();
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);

            if (!RoomDB.Rooms.Update(room))
                return StatusCode(500, new { error = "Room changes could not be saved." });

            LogAdminRoomAction(admin!, room, "updated room settings");
            return Ok(ToAdminRoomDetails(room));
        }

        [HttpPut("admin/rooms/{roomId:long}/stats")]
        public IActionResult UpdateAdminRoomStats(
            long roomId,
            [FromBody] AdminRoomStatsUpdate request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);
            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound(new { error = "Room not found." });
            if (request.Cheers < 0 || request.Favorites < 0 ||
                request.Visitors < 0 || request.Visits < 0)
            {
                return BadRequest(new { error = "Room stats cannot be negative." });
            }

            room.Stats ??= new RoomDBClasses.Stats();
            room.Stats.CheerCount = request.Cheers;
            room.Stats.FavoriteCount = request.Favorites;
            room.Stats.VisitorCount = request.Visitors;
            room.Stats.VisitCount = request.Visits;
            RoomDB.Rooms.Update(room);
            LogAdminRoomAction(admin!, room, "updated room stats");
            return Ok(ToAdminRoomDetails(room));
        }

        [HttpPut("admin/rooms/{roomId:long}/roles/{accountId:long}")]
        public IActionResult SetAdminRoomRole(
            long roomId,
            long accountId,
            [FromBody] AdminRoomRoleUpdate request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);
            var room = RoomDB.Rooms.FindById(roomId);
            var account = PlayerDB.Players.FindById(accountId);
            if (room == null)
                return NotFound(new { error = "Room not found." });
            if (account?.Player == null)
                return NotFound(new { error = "Player not found." });
            if (!Enum.TryParse(request.Role, true, out RoomDBClasses.Role role) ||
                !Enum.TryParse(request.InvitedRole, true, out RoomDBClasses.Role invitedRole))
            {
                return BadRequest(new { error = "Choose valid assigned and invited roles." });
            }
            if (role == RoomDBClasses.Role.Creator ||
                invitedRole == RoomDBClasses.Role.Creator)
            {
                return BadRequest(new { error = "Use Transfer Ownership to assign the Creator role." });
            }
            if (accountId == room.CreatorAccountId)
                return BadRequest(new { error = "The room creator's role cannot be changed here." });

            room.Roles ??= new List<RoomDBClasses.Roles>();
            room.Roles.RemoveAll(existing => existing.AccountId == accountId);
            if (role != RoomDBClasses.Role.None ||
                invitedRole != RoomDBClasses.Role.None)
            {
                room.Roles.Add(new RoomDBClasses.Roles
                {
                    AccountId = accountId,
                    Role = role,
                    InvitedRole = invitedRole
                });
            }
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            RoomDB.Rooms.Update(room);
            LogAdminRoomAction(
                admin!,
                room,
                $"set player {accountId} role={role} invitedRole={invitedRole}");
            return Ok(ToAdminRoomDetails(room));
        }

        [HttpDelete("admin/rooms/{roomId:long}/roles/{accountId:long}")]
        public IActionResult DeleteAdminRoomRole(long roomId, long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);
            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound(new { error = "Room not found." });
            if (accountId == room.CreatorAccountId)
                return BadRequest(new { error = "Transfer ownership before removing the creator." });

            room.Roles ??= new List<RoomDBClasses.Roles>();
            int removed = room.Roles.RemoveAll(role => role.AccountId == accountId);
            if (removed == 0)
                return NotFound(new { error = "That player does not have a room role." });
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            RoomDB.Rooms.Update(room);
            LogAdminRoomAction(admin!, room, $"removed player {accountId} room role");
            return Ok(ToAdminRoomDetails(room));
        }

        [HttpPost("admin/rooms/{roomId:long}/transfer-owner")]
        public IActionResult TransferAdminRoomOwner(
            long roomId,
            [FromBody] AdminRoomOwnerTransfer request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can transfer room ownership." });
            var room = RoomDB.Rooms.FindById(roomId);
            var newOwner = PlayerDB.Players.FindById(request.AccountId);
            if (room == null)
                return NotFound(new { error = "Room not found." });
            if (room.IsDorm)
                return BadRequest(new { error = "Dorm ownership cannot be transferred." });
            if (newOwner?.Player == null)
                return NotFound(new { error = "New owner account not found." });
            if (request.AccountId == room.CreatorAccountId)
                return BadRequest(new { error = "That player already owns the room." });

            long previousOwnerId = room.CreatorAccountId;
            room.CreatorAccountId = request.AccountId;
            room.Roles ??= new List<RoomDBClasses.Roles>();
            room.Roles.RemoveAll(role =>
                role.AccountId == previousOwnerId ||
                role.AccountId == request.AccountId);
            room.Roles.Add(new RoomDBClasses.Roles
            {
                AccountId = previousOwnerId,
                Role = RoomDBClasses.Role.CoOwner,
                InvitedRole = RoomDBClasses.Role.None
            });
            room.Roles.Add(new RoomDBClasses.Roles
            {
                AccountId = request.AccountId,
                Role = RoomDBClasses.Role.Creator,
                InvitedRole = RoomDBClasses.Role.None
            });
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            RoomDB.Rooms.Update(room);
            LogAdminRoomAction(
                admin!,
                room,
                $"transferred ownership {previousOwnerId}->{request.AccountId}");
            return Ok(ToAdminRoomDetails(room));
        }

        [HttpPost("admin/rooms/{roomId:long}/change-id")]
        public IActionResult ChangeAdminRoomId(
            long roomId,
            [FromBody] AdminRoomIdChange request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can change a room's ID." });

            if (!RoomDB.ChangeRoomId(roomId, request.NewRoomId, out string error))
                return BadRequest(new { error });

            CreatorFeatureDB.MigrateRoomId(roomId, request.NewRoomId);

            var room = RoomDB.Rooms.FindById(request.NewRoomId);
            Console.WriteLine(
                $"[ADMIN ROOM] admin={admin!.PlayerId} changed room id {roomId}->{request.NewRoomId}");
            return Ok(ToAdminRoomDetails(room));
        }

        public class AdminRoomIdChange
        {
            public long NewRoomId { get; set; }
        }

        [HttpPost("admin/rooms/{roomId:long}/subrooms")]
        public IActionResult AddAdminSubRoom(
            long roomId,
            [FromBody] AdminSubRoomCreate request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);
            if (RoomDB.Rooms.FindById(roomId) == null)
                return NotFound(new { error = "Room not found." });
            string name = request.Name?.Trim() ?? string.Empty;
            if (name.Length is < 1 or > 50)
                return BadRequest(new { error = "Subroom name must be 1-50 characters." });

            var updated = RoomDB.AddSubRoom(roomId, admin!.PlayerId, name);
            var room = RoomDB.Rooms.FindById(roomId);
            if (updated == null || room == null)
                return StatusCode(500, new { error = "Subroom could not be created." });
            LogAdminRoomAction(admin, room, $"created subroom {name}");
            return Ok(ToAdminRoomDetails(room));
        }

        [HttpPost("admin/rooms/{roomId:long}/subrooms/{subRoomId:long}/clone")]
        public IActionResult CloneAdminSubRoom(long roomId, long subRoomId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);
            var updated = RoomDB.CloneSubRoom(
                roomId,
                subRoomId,
                admin!.PlayerId);
            var room = RoomDB.Rooms.FindById(roomId);
            if (updated == null || room == null)
                return NotFound(new { error = "Room or subroom not found." });
            LogAdminRoomAction(admin, room, $"cloned subroom {subRoomId}");
            return Ok(ToAdminRoomDetails(room));
        }

        [HttpPut("admin/rooms/{roomId:long}/subrooms/{subRoomId:long}")]
        public IActionResult UpdateAdminSubRoom(
            long roomId,
            long subRoomId,
            [FromBody] AdminSubRoomUpdate request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);
            var room = RoomDB.Rooms.FindById(roomId);
            var subRoom = room?.SubRooms?.FirstOrDefault(value =>
                value.SubRoomId == subRoomId);
            if (room == null || subRoom == null)
                return NotFound(new { error = "Room or subroom not found." });
            string name = request.Name?.Trim() ?? string.Empty;
            if (name.Length is < 1 or > 50)
                return BadRequest(new { error = "Subroom name must be 1-50 characters." });
            if (request.MaxPlayers is < 1 or > 100)
                return BadRequest(new { error = "Subroom capacity must be between 1 and 100." });
            if (!Enum.TryParse(
                    request.Accessibility,
                    true,
                    out RoomDBClasses.RoomAccessibility accessibility))
            {
                return BadRequest(new { error = "Choose valid subroom accessibility." });
            }
            string unitySceneId = request.UnitySceneId?.Trim() ?? string.Empty;
            if (unitySceneId.Length > 200 || unitySceneId.Any(character => character == '\0'))
                return BadRequest(new { error = "Unity scene ID is invalid." });

            subRoom.Name = name;
            subRoom.MaxPlayers = request.MaxPlayers;
            subRoom.Accessibility = accessibility;
            subRoom.IsSandbox = request.IsSandbox;
            subRoom.UnitySceneId = unitySceneId;
            if (request.Permissions != null)
            {
                subRoom.Permissions = request.Permissions
                    .Where(permission =>
                        !string.IsNullOrWhiteSpace(permission.Permission))
                    .Select(permission => new RoomDBClasses.SubRoomPermission
                    {
                        Override = permission.Override,
                        Permission = permission.Permission.Trim()[..Math.Min(
                            permission.Permission.Trim().Length, 128)],
                        Role = Math.Clamp(permission.Role, 0, byte.MaxValue),
                        Type = Math.Clamp(permission.Type, 0, 32),
                        Value = (permission.Value ?? "True").Trim()[..Math.Min(
                            (permission.Value ?? "True").Trim().Length, 256)]
                    })
                    .GroupBy(permission => new
                    {
                        Name = permission.Permission.ToUpperInvariant(),
                        permission.Role,
                        permission.Type
                    })
                    .Select(group => group.Last())
                    .Take(256)
                    .ToList();
            }
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            RoomDB.Rooms.Update(room);
            LogAdminRoomAction(admin!, room, $"updated subroom {subRoomId}");
            return Ok(ToAdminRoomDetails(room));
        }

        [HttpPut("admin/rooms/{roomId:long}/subrooms/{subRoomId:long}/blobs")]
        public IActionResult UpdateAdminSubRoomBlobs(
            long roomId,
            long subRoomId,
            [FromBody] AdminRoomBlobUpdate request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            var room = RoomDB.Rooms.FindById(roomId);
            var subRoom = room?.SubRooms?.FirstOrDefault(value =>
                value.SubRoomId == subRoomId);
            if (room == null || subRoom == null)
                return NotFound(new { error = "Room or subroom not found." });

            static bool TryNormalizeBlobName(
                string? input,
                out string blobName,
                out string error)
            {
                blobName = input?.Trim() ?? string.Empty;
                error = string.Empty;

                if (blobName.Length is < 1 or > 255)
                {
                    error = "Blob names must be between 1 and 255 characters.";
                    return false;
                }

                if (blobName.Any(character => character == '\0') ||
                    blobName is "." or ".." ||
                    !string.Equals(
                        Path.GetFileName(blobName),
                        blobName,
                        StringComparison.Ordinal))
                {
                    error = "Blob names must be filenames only, without folders or traversal characters.";
                    return false;
                }

                return true;
            }

            if (!TryNormalizeBlobName(
                    request.RoomBlob,
                    out string roomBlob,
                    out string roomBlobError))
            {
                return BadRequest(new { error = $"RoomBlob: {roomBlobError}" });
            }

            if (!TryNormalizeBlobName(
                    request.MetadataBlob,
                    out string metadataBlob,
                    out string metadataBlobError))
            {
                return BadRequest(new { error = $"Metadata blob: {metadataBlobError}" });
            }

            if (string.Equals(
                    roomBlob,
                    metadataBlob,
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    error = "RoomBlob and metadata blob must point to two different files."
                });
            }

            string blobDirectory = Path.Combine(
                Program.dataDir,
                "CDN",
                "room");
            string roomBlobPath = Path.Combine(blobDirectory, roomBlob);
            string metadataBlobPath = Path.Combine(blobDirectory, metadataBlob);

            if (!System.IO.File.Exists(roomBlobPath))
            {
                return BadRequest(new
                {
                    error = $"RoomBlob '{roomBlob}' does not exist in CDN/room."
                });
            }

            if (!System.IO.File.Exists(metadataBlobPath))
            {
                return BadRequest(new
                {
                    error = $"Metadata blob '{metadataBlob}' does not exist in CDN/room."
                });
            }

            string roomBlobHash;
            using (var stream = System.IO.File.OpenRead(roomBlobPath))
            {
                roomBlobHash = Convert
                    .ToHexString(System.Security.Cryptography.SHA256.HashData(stream))
                    .ToLowerInvariant();
            }

            var save = RoomDB.CreateSubRoomDataSave(
                roomId,
                subRoomId,
                admin!.PlayerId,
                roomBlob,
                room.PersistenceVersion,
                isPublished: true,
                dataBlobHash: roomBlobHash,
                description: "Blob pair changed from the RecNet Admin panel.",
                roomDataBlob: metadataBlob);

            room.DataBlob = metadataBlob;
            subRoom.DataBlob = roomBlob;
            subRoom.SubRoomDataSaveId = save.SubRoomDataSaveId;
            subRoom.SubRoomDataSave = save;
            subRoom.SavedByAccountId = admin.PlayerId;
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);

            if (!RoomDB.Rooms.Update(room))
            {
                RoomDB.SubRoomDataSaves.DeleteMany(existing =>
                    existing.SubRoomDataSaveId == save.SubRoomDataSaveId);
                return StatusCode(500, new
                {
                    error = "The blob pair could not be saved to the room database."
                });
            }

            LogAdminRoomAction(
                admin,
                room,
                $"changed subroom {subRoomId} blobs " +
                $"RoomBlob={roomBlob} MetadataBlob={metadataBlob}");
            return Ok(ToAdminRoomDetails(room));
        }

        [HttpDelete("admin/rooms/{roomId:long}/subrooms/{subRoomId:long}")]
        public IActionResult DeleteAdminSubRoom(long roomId, long subRoomId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can delete subrooms." });
            var room = RoomDB.Rooms.FindById(roomId);
            if (room?.SubRooms == null)
                return NotFound(new { error = "Room not found." });
            if (room.SubRooms.Count <= 1)
                return BadRequest(new { error = "A room must keep at least one subroom." });
            int removed = room.SubRooms.RemoveAll(value =>
                value.SubRoomId == subRoomId);
            if (removed == 0)
                return NotFound(new { error = "Subroom not found." });

            RoomDB.SubRoomDataSaves.DeleteMany(save =>
                save.RoomId == roomId &&
                save.SubRoomId == subRoomId);
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            RoomDB.Rooms.Update(room);
            LogAdminRoomAction(admin!, room, $"deleted subroom {subRoomId}");
            return Ok(ToAdminRoomDetails(room));
        }

        [HttpPost("admin/rooms/{roomId:long}/bans")]
        public IActionResult AddAdminRoomBan(
            long roomId,
            [FromBody] AdminRoomBanUpdate request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);
            var room = RoomDB.Rooms.FindById(roomId);
            var target = PlayerDB.Players.FindById(request.AccountId);
            if (room == null)
                return NotFound(new { error = "Room not found." });
            if (target?.Player == null)
                return NotFound(new { error = "Player not found." });
            if (IsDeveloper(target) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "Moderators cannot room-ban Developer accounts." });
            if (request.AccountId == room.CreatorAccountId)
                return BadRequest(new { error = "The room creator cannot be banned." });
            string reason = request.Reason?.Trim() ?? string.Empty;
            if (reason.Length is < 1 or > 500)
                return BadRequest(new { error = "Room-ban reason must be 1-500 characters." });

            RoomDB.BanPlayerFromRoom(
                roomId,
                request.AccountId,
                admin!.PlayerId,
                reason);
            LogAdminRoomAction(admin, room, $"banned player {request.AccountId}");
            return Ok(ToAdminRoomDetails(room));
        }

        [HttpDelete("admin/rooms/{roomId:long}/bans/{accountId:long}")]
        public IActionResult DeleteAdminRoomBan(long roomId, long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);
            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound(new { error = "Room not found." });
            if (!RoomDB.UnbanPlayerFromRoom(roomId, accountId))
                return NotFound(new { error = "That player is not banned from this room." });
            LogAdminRoomAction(admin!, room, $"unbanned player {accountId}");
            return Ok(ToAdminRoomDetails(room));
        }

        [HttpGet("admin/instances")]
        public IActionResult GetAdminLiveInstances()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var accounts = PlayerDB.Players.FindAll()
                .Where(value => value.Player != null)
                .ToList();

            var onlinePlayers = accounts
                .Select(account => new
                {
                    Account = account,
                    Heartbeat = PlayerDB.GetPlayerHeartbeat(account.PlayerId)
                })
                .Where(value => value.Heartbeat.isOnline)
                .OrderBy(value => value.Account.Player!.DisplayName)
                .Select(value => new
                {
                    accountId = value.Account.PlayerId,
                    username = value.Account.Player!.Username,
                    displayName = value.Account.Player.DisplayName,
                    profileImage = ImageUrl(value.Account.Player.ProfileImage),
                    device = value.Heartbeat.deviceClass?.ToString() ?? "Unknown",
                    room = value.Heartbeat.roomInstance?.Name ??
                        value.Heartbeat.roomInstance?.location ??
                        "Online",
                    roomId = value.Heartbeat.roomInstance?.roomId,
                    roomInstanceId = value.Heartbeat.roomInstance?.roomInstanceId,
                    lastHeartbeat = value.Heartbeat.lastHeartbeatUnixTime > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(
                            value.Heartbeat.lastHeartbeatUnixTime)
                        : (DateTimeOffset?)null,
                    sockets = NotiController.GetPlayerSocketCount(
                        value.Account.PlayerId)
                })
                .ToList();

            var groupedByRoom = onlinePlayers
                .GroupBy(p => new { p.room, p.roomId, p.roomInstanceId })
                .Select(g => new
                {
                    room = g.Key.room,
                    roomId = g.Key.roomId,
                    roomInstanceId = g.Key.roomInstanceId,
                    playerCount = g.Count(),
                    players = g.ToList()
                })
                .OrderByDescending(g => g.playerCount)
                .ThenBy(g => g.room)
                .ToList();

            return Ok(new
            {
                totalOnline = onlinePlayers.Count,
                instances = groupedByRoom
            });
        }

        [HttpPost("admin/instances/{instanceId:long}/shutdown")]
        public async Task<IActionResult> ShutdownAdminInstance(long instanceId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var accounts = PlayerDB.Players.FindAll()
                .Where(value => value.Player != null)
                .ToList();

            var playersInInstance = accounts
                .Select(account => new
                {
                    Account = account,
                    Heartbeat = PlayerDB.GetPlayerHeartbeat(account.PlayerId)
                })
                .Where(value => value.Heartbeat.isOnline &&
                    value.Heartbeat.roomInstance?.roomInstanceId == instanceId)
                .ToList();

            if (!playersInInstance.Any())
                return BadRequest(new { error = "No players found in this instance." });

            int movedCount = 0;
            foreach (var playerData in playersInInstance)
            {
                try
                {

                    var dormHeartbeat = Sessions.CreateDorm(
                        playerData.Account.PlayerId,
                        playerData.Account.Player!.Username ?? "Unknown"
                    );

                    if (dormHeartbeat != null)
                    {
                        Sessions.MarkGuestDormEntry(playerData.Account.PlayerId);
                        movedCount++;
                    }
                }
                catch (Exception ex)
                {

                    Console.WriteLine($"Failed to move player {playerData.Account.PlayerId} to dorm: {ex.Message}");
                }
            }

            return Ok(new
            {
                success = true,
                movedCount,
                totalPlayers = playersInInstance.Count
            });
        }

        [HttpGet("developer/snapshot")]
        public IActionResult GetDeveloperSnapshot()
        {
            var developer = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(developer))
                return StatusCode(403);

            return Ok(BuildDeveloperSnapshot());
        }

        [HttpGet("developer/chats")]
        public IActionResult GetDeveloperChats(
            [FromQuery] int take = 100,
            [FromQuery] long? beforeMessageId = null)
        {
            var developer = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(developer))
                return StatusCode(403);

            return Ok(BuildDeveloperChats(take, beforeMessageId));
        }

        [HttpGet("developer/stream")]
        public async Task<IActionResult> StreamDeveloperDashboard()
        {
            var developer = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(developer))
                return StatusCode(403);

            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Connection = "keep-alive";
            Response.Headers.Append("X-Accel-Buffering", "no");

            try
            {
                while (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    string json = JsonSerializer.Serialize(
                        BuildDeveloperSnapshot());
                    await Response.WriteAsync(
                        $"event: snapshot\ndata: {json}\n\n",
                        HttpContext.RequestAborted);
                    await Response.Body.FlushAsync(
                        HttpContext.RequestAborted);
                    await Task.Delay(
                        TimeSpan.FromSeconds(1),
                        HttpContext.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {

            }

            return new EmptyResult();
        }

        [HttpGet("admin/settings")]
        public IActionResult GetAdminSiteSettings()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            return Ok(new
            {
                accountCreationEnabled = RecNetDB.IsAccountCreationEnabled(),
                recNetSignupEnabled = RecNetDB.IsRecNetSignupEnabled(),
                vpnBlockingEnabled = RecNetDB.IsVpnBlockingEnabled(),
                proxyCheckConfigured = !string.IsNullOrWhiteSpace(
                    Program.LoadLocalSetting("PROXYCHECK_API_KEY"))
            });
        }

        [HttpPut("admin/settings")]
        public IActionResult UpdateAdminSiteSettings(
            [FromBody] AdminSiteSettings request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            if (request.AccountCreationEnabled.HasValue)
            {
                RecNetDB.SetAccountCreationEnabled(
                    request.AccountCreationEnabled.Value,
                    admin!.PlayerId);
            }
            if (request.RecNetSignupEnabled.HasValue)
            {
                RecNetDB.SetRecNetSignupEnabled(
                    request.RecNetSignupEnabled.Value,
                    admin!.PlayerId);
            }
            if (request.VpnBlockingEnabled.HasValue)
            {
                RecNetDB.SetVpnBlockingEnabled(
                    request.VpnBlockingEnabled.Value,
                    admin!.PlayerId);
            }

            bool accountCreationEnabled = RecNetDB.IsAccountCreationEnabled();
            bool recNetSignupEnabled = RecNetDB.IsRecNetSignupEnabled();
            bool vpnBlockingEnabled = RecNetDB.IsVpnBlockingEnabled();

            DiscordLogger.Log(
                $"🔐 **Security settings changed** by `{admin!.PlayerId}` — " +
                $"account creation: `{accountCreationEnabled}`, " +
                $"RecNet signup: `{recNetSignupEnabled}`, " +
                $"VPN blocking: `{vpnBlockingEnabled}`");

            return Ok(new
            {
                success = true,
                accountCreationEnabled,
                recNetSignupEnabled,
                vpnBlockingEnabled
            });
        }

        [HttpGet("admin/logs")]
        public IActionResult GetAdminLogs([FromQuery] int take = 500)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can view server logs." });

            take = Math.Clamp(take, 1, 5000);
            List<string> lines = LogBuffer.Snapshot(take);

            return Ok(new { lines, count = lines.Count });
        }

        [HttpGet("admin/scanner-logs")]
        public IActionResult GetAdminScannerLogs(
            [FromQuery] int skip = 0,
            [FromQuery] int take = 200,
            [FromQuery] string? ip = null)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can view scanner logs." });

            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 500);

            var query = ScannerLogDB.Attempts.Query();
            if (!string.IsNullOrWhiteSpace(ip))
                query = query.Where(a => a.IpAddress == ip.Trim());

            List<ScannerLogDB.ScannerAttempt> page = query
                .OrderByDescending(a => a.Id)
                .Skip(skip)
                .Limit(take)
                .ToList();

            return Ok(new
            {
                total = ScannerLogDB.Attempts.LongCount(),
                results = page.Select(a => new
                {
                    id = a.Id,
                    timestamp = a.Timestamp,
                    ipAddress = a.IpAddress,
                    method = a.Method,
                    path = a.Path,
                    queryString = a.QueryString,
                    userAgent = a.UserAgent,
                    matchedPattern = a.MatchedPattern
                })
            });
        }

        [HttpGet("admin/anticheat-logs")]
        public IActionResult GetAdminAnticheatLogs(
            [FromQuery] int skip = 0,
            [FromQuery] int take = 200,
            [FromQuery] string? ip = null)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can view anticheat logs." });

            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 500);

            var query = AnticheatLogDB.Entries.Query();
            if (!string.IsNullOrWhiteSpace(ip))
                query = query.Where(a => a.IpAddress == ip.Trim());

            List<AnticheatLogDB.AnticheatEntry> page = query
                .OrderByDescending(a => a.Id)
                .Skip(skip)
                .Limit(take)
                .ToList();

            return Ok(new
            {
                total = AnticheatLogDB.Entries.LongCount(),
                results = page.Select(a => new
                {
                    id = a.Id,
                    timestamp = a.Timestamp,
                    ipAddress = a.IpAddress,
                    accountId = a.AccountId,
                    steamId = a.SteamId,
                    build = a.Build,
                    flags = a.Flags,
                    userAgent = a.UserAgent
                })
            });
        }

        [HttpGet("admin/ip-bans")]
        public IActionResult GetAdminIpBans()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            return Ok(RecNetDB.GetIpBans().Select(ToAdminIpBan));
        }

        [HttpPost("admin/ip-bans")]
        public IActionResult AddAdminIpBan([FromBody] AdminIpBanRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            try
            {
                RecNetDB.IpBan record = RecNetDB.AddIpBan(
                    request.Network ?? string.Empty,
                    request.Reason,
                    admin!.PlayerId);

                DiscordLogger.Log(
                    $"🚫 **IP ban added** by `{admin.PlayerId}` — " +
                    $"`{record.Network}` — {record.Reason}");
                return Ok(ToAdminIpBan(record));
            }
            catch (ArgumentException exception)
            {
                return BadRequest(new { error = exception.Message });
            }
        }

        [HttpDelete("admin/ip-bans/{id}")]
        public IActionResult DeleteAdminIpBan(string id)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            RecNetDB.IpBan? record = RecNetDB.IpBans.FindById(id);
            if (record == null || !RecNetDB.RemoveIpBan(id))
                return NotFound(new { error = "IP ban not found." });

            DiscordLogger.Log(
                $"✅ **IP ban removed** by `{admin!.PlayerId}` — `{record.Network}`");
            return Ok(new { success = true });
        }

        private static object ToAdminIpBan(RecNetDB.IpBan value) => new
        {
            id = value.Id,
            network = value.Network,
            reason = value.Reason,
            createdByAccountId = value.CreatedByAccountId,
            createdAt = value.CreatedAt
        };

        [HttpGet("admin/community-board")]
        public IActionResult GetAdminCommunityBoard()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            string path = Path.Combine(Program.dataDir, "communityboard.json");
            if (!System.IO.File.Exists(path))
            {
                return NotFound(new
                {
                    error = "communityboard.json was not found in the live Data folder."
                });
            }

            try
            {
                string json = System.IO.File.ReadAllText(path);
                using JsonDocument document = JsonDocument.Parse(json);
                return Content(json, "application/json");
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return StatusCode(500, new
                {
                    error = $"The live community board could not be read: {ex.Message}"
                });
            }
        }

        [HttpPut("admin/community-board")]
        [RequestSizeLimit(256 * 1024)]
        public async Task<IActionResult> UpdateAdminCommunityBoard(
            [FromBody] JsonElement request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            if (!TryValidateCommunityBoard(request, out string error))
                return BadRequest(new { error });

            string path = Path.Combine(Program.dataDir, "communityboard.json");
            try
            {
                string json = JsonSerializer.Serialize(
                    request,
                    new JsonSerializerOptions { WriteIndented = true });
                await WriteCommunityBoardFileAsync(
                    path,
                    json,
                    HttpContext.RequestAborted);
                await NotiController.BroadcastAnnouncementsUpdatedAsync();
                return Content(json, "application/json");
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return StatusCode(500, new
                {
                    error = $"The community board could not be saved: {ex.Message}"
                });
            }
        }

        [HttpPost("admin/community-board/media")]
        [RequestSizeLimit(105 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 105 * 1024 * 1024)]
        public async Task<IActionResult> UploadAdminCommunityBoardMedia()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (admin?.Player == null)
                return Unauthorized(new { error = "Log in as a Developer to upload board media." });
            if (!IsAdmin(admin))
                return StatusCode(403, new { error = "Developer access is required." });
            if (!Request.HasFormContentType)
                return BadRequest(new { error = "Choose a photo or video to upload." });

            var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
            var file = form.Files.FirstOrDefault();
            string kind = form["kind"].FirstOrDefault()?.Trim().ToLowerInvariant() ?? string.Empty;
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "Choose a photo or video to upload." });
            if (kind is not ("image" or "video"))
                return BadRequest(new { error = "Choose whether this is an image or video." });

            long maximumBytes = kind == "image"
                ? 15L * 1024 * 1024
                : 100L * 1024 * 1024;
            if (file.Length > maximumBytes)
            {
                return BadRequest(new
                {
                    error = kind == "image"
                        ? "Community Board images must be 15 MB or smaller."
                        : "Community Board videos must be 100 MB or smaller."
                });
            }

            byte[] bytes;
            using (var buffer = new MemoryStream())
            {
                await file.CopyToAsync(buffer, HttpContext.RequestAborted);
                bytes = buffer.ToArray();
            }

            string extension;
            string contentType;
            if (kind == "image")
            {
                SixLabors.ImageSharp.Formats.IImageFormat? format;
                SixLabors.ImageSharp.ImageInfo? info;
                try
                {
                    format = SixLabors.ImageSharp.Image.DetectFormat(bytes);
                    info = SixLabors.ImageSharp.Image.Identify(bytes);
                }
                catch
                {
                    return BadRequest(new { error = "That file is not a valid image." });
                }

                if (format == null || info == null || info.Width <= 0 || info.Height <= 0 ||
                    info.Width > 8192 || info.Height > 8192 ||
                    (long)info.Width * info.Height > 40_000_000)
                {
                    return BadRequest(new
                    {
                        error = "Board images cannot exceed 8192 x 8192 or 40 megapixels."
                    });
                }

                extension = format.FileExtensions.FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;
                if (extension is not ("png" or "jpg" or "jpeg" or "webp" or "gif" or "bmp"))
                    return BadRequest(new { error = "Use a PNG, JPG, WebP, GIF, or BMP image." });
                contentType = extension switch
                {
                    "png" => "image/png",
                    "webp" => "image/webp",
                    "gif" => "image/gif",
                    "bmp" => "image/bmp",
                    _ => "image/jpeg"
                };
            }
            else
            {
                string requestedExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
                bool isIsoBaseMedia = bytes.Length >= 12 &&
                    bytes[4] == (byte)'f' && bytes[5] == (byte)'t' &&
                    bytes[6] == (byte)'y' && bytes[7] == (byte)'p';
                bool isWebM = bytes.Length >= 4 &&
                    bytes[0] == 0x1A && bytes[1] == 0x45 &&
                    bytes[2] == 0xDF && bytes[3] == 0xA3;

                if (isWebM && requestedExtension == ".webm")
                {
                    extension = "webm";
                    contentType = "video/webm";
                }
                else if (isIsoBaseMedia && requestedExtension is ".mp4" or ".m4v" or ".mov")
                {
                    extension = requestedExtension.TrimStart('.');
                    contentType = requestedExtension == ".mov"
                        ? "video/quicktime"
                        : "video/mp4";
                }
                else
                {
                    return BadRequest(new
                    {
                        error = "Use an MP4, M4V, MOV, or WebM video with a matching file extension."
                    });
                }
            }

            string folder = kind == "image"
                ? Path.Combine(Program.dataDir, "Images", "CommunityBoardUploads")
                : Path.Combine(Program.dataDir, "CDN", "video");
            Directory.CreateDirectory(folder);
            string storedFileName =
                $"board_{kind}_{admin!.PlayerId}_{Guid.NewGuid():N}.{extension}";
            await System.IO.File.WriteAllBytesAsync(
                Path.Combine(folder, storedFileName),
                bytes,
                HttpContext.RequestAborted);

            string relativeUrl = kind == "image"
                ? "/imageserver/CommunityBoardUploads/" + Uri.EscapeDataString(storedFileName)
                : "/cdn/video/" + Uri.EscapeDataString(storedFileName);
            string absoluteUrl = $"{Request.Scheme}://{Request.Host}{relativeUrl}";
            return Ok(new
            {
                success = true,
                kind,
                fileName = storedFileName,
                originalName = Path.GetFileName(file.FileName),
                contentType,
                size = file.Length,
                imageName = kind == "image"
                    ? "CommunityBoardUploads/" + storedFileName
                    : null,
                path = relativeUrl,
                url = absoluteUrl
            });
        }

        private static async Task WriteCommunityBoardFileAsync(
            string path,
            string json,
            CancellationToken cancellationToken)
        {
            await CommunityBoardWriteLock.WaitAsync(cancellationToken);
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await System.IO.File.WriteAllTextAsync(
                    temporaryPath,
                    json,
                    cancellationToken);
                System.IO.File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (System.IO.File.Exists(temporaryPath))
                    System.IO.File.Delete(temporaryPath);
                CommunityBoardWriteLock.Release();
            }
        }

        [HttpPost("admin/maintenance")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> StartAdminMaintenance(
            [FromBody] AdminMaintenanceRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);
            if (request.Minutes is < 0 or > 10_080)
                return BadRequest(new
                {
                    error = "Maintenance must be between 0 and 10,080 minutes."
                });

            try
            {
                await Program.SetMaintenanceCountdownAsync(request.Minutes);
                return Ok(new
                {
                    success = true,
                    minutes = request.Minutes,
                    message = request.Minutes == 0
                        ? "The maintenance notice was cleared."
                        : $"The {request.Minutes}-minute maintenance countdown is live in-game."
                });
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or
                FileNotFoundException or InvalidDataException or JsonException or IOException)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("admin/steam-blacklist")]
        public IActionResult GetAdminSteamBlacklist()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            return Ok(
                SteamAccessDB.GetAll()
                    .Select(ToAdminSteamBlacklistEntry)
                    .ToList());
        }

        [HttpPost("admin/steam-blacklist")]
        public IActionResult AddAdminSteamBlacklist(
            [FromBody] AdminSteamBlacklistRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            if (!ulong.TryParse(
                    request.SteamId?.Trim(),
                    out ulong steamId) ||
                steamId == 0)
            {
                return BadRequest(new
                {
                    error = "Enter a valid numeric Steam ID."
                });
            }

            if ((request.Reason?.Trim().Length ?? 0) > 500)
            {
                return BadRequest(new
                {
                    error = "Blacklist reasons cannot exceed 500 characters."
                });
            }

            SteamAccessDB.SteamBlacklistEntry entry =
                SteamAccessDB.AddOrUpdate(
                    steamId,
                    request.Reason,
                    admin!.PlayerId);

            string actor = admin.Player?.Username ??
                admin.Player?.DisplayName ??
                admin.PlayerId.ToString();

            Console.WriteLine(
                $"[ADMIN STEAM BLACKLIST] admin={admin.PlayerId} " +
                $"steamId={steamId} action=blocked");
            DiscordLogger.Log(
                $"🚫 **Steam blacklist** — `@{actor}` (`{admin.PlayerId}`) " +
                $"blocked Steam ID `{steamId}`. Reason: `{entry.Reason}`");

            return Ok(ToAdminSteamBlacklistEntry(entry));
        }

        [HttpDelete("admin/steam-blacklist/{steamIdText}")]
        public IActionResult RemoveAdminSteamBlacklist(
            string steamIdText)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            if (!ulong.TryParse(steamIdText, out ulong steamId) ||
                steamId == 0)
            {
                return BadRequest(new
                {
                    error = "Enter a valid numeric Steam ID."
                });
            }

            if (!SteamAccessDB.Remove(steamId))
            {
                return NotFound(new
                {
                    error = "That Steam ID is not blacklisted."
                });
            }

            string actor = admin!.Player?.Username ??
                admin.Player?.DisplayName ??
                admin.PlayerId.ToString();

            Console.WriteLine(
                $"[ADMIN STEAM BLACKLIST] admin={admin.PlayerId} " +
                $"steamId={steamId} action=unblocked");
            DiscordLogger.Log(
                $"✅ **Steam blacklist** — `@{actor}` (`{admin.PlayerId}`) " +
                $"unblocked Steam ID `{steamId}`.");

            return Ok(new
            {
                success = true,
                steamId = steamId.ToString()
            });
        }

        [HttpGet("admin/shop")]
        public IActionResult GetAdminShop()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            return Ok(ToAdminShopPayload(APIController.GetStorefrontAdminInfo()));
        }

        [HttpGet("admin/shop/catalog")]
        public IActionResult SearchAdminShopCatalog(
            [FromQuery] string? search = null,
            [FromQuery] string? type = null,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 30)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            var (items, total) = APIController.SearchStorefrontCatalog(search, type, skip, take);
            return Ok(new
            {
                results = items.Select(ToAdminShopItemPayload),
                total,
                skip,
                take
            });
        }

        [HttpPost("admin/shop/refresh")]
        public IActionResult AdminRefreshShop()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            long rotation = APIController.RefreshStorefrontRotation();
            return Ok(new
            {
                success = true,
                rotation,
                message = "The shop has been refreshed. Reopen the storefront in-game to see it."
            });
        }

        [HttpPost("admin/shop/items")]
        public IActionResult AdminAddShopItem([FromBody] AdminShopItemRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            if (!APIController.TryAddCustomStoreItem(request.SkuId, out string error))
                return BadRequest(new { error });

            return Ok(new
            {
                success = true,
                shop = ToAdminShopPayload(APIController.GetStorefrontAdminInfo())
            });
        }

        [HttpDelete("admin/shop/items/{skuId:long}")]
        public IActionResult AdminRemoveShopItem(long skuId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            if (!APIController.RemoveCustomStoreItem(skuId))
                return NotFound(new { error = "That custom shop item was not found." });

            return Ok(new
            {
                success = true,
                shop = ToAdminShopPayload(APIController.GetStorefrontAdminInfo())
            });
        }

        [HttpGet("admin/gifts/catalog")]
        public IActionResult SearchAdminGiftCatalog(
            [FromQuery] string? search = null,
            [FromQuery] string? type = null,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 50)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            var (catalogItems, total) = APIController.SearchStorefrontCatalog(search, type, skip, take);
            var items = catalogItems
                .Select(item => new
                {
                    skuId = item.SkuId,
                    avatarItemId = item.AvatarItemId,
                    friendlyName = item.FriendlyName,
                    avatarItemDesc = item.AvatarItemDesc,
                    consumableItemDesc = item.ConsumableItemDesc,
                    equipmentPrefabName = item.EquipmentPrefabName,
                    equipmentModificationGuid = item.EquipmentModificationGuid,
                    thumbnailImage = item.ThumbnailImage,
                    rarity = item.Rarity,
                    type = !string.IsNullOrWhiteSpace(item.ConsumableItemDesc)
                        ? "consumable"
                        : !string.IsNullOrWhiteSpace(item.EquipmentModificationGuid)
                            ? "equipment"
                            : "avatar"
                })
                .ToList();

            return Ok(new { results = items, total, skip, take });
        }

        [HttpPost("admin/gifts")]
        public async Task<IActionResult> AdminSendGift(
            [FromBody] AdminGiftRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            const int coachAccountId = 1;
            string giftType = request.GiftType?.Trim().ToLowerInvariant() ?? string.Empty;
            if (giftType is not ("avatar" or "equipment" or "consumable" or "tokens" or "xp" or "box"))
                return BadRequest(new { error = "Choose avatar, equipment, consumable, tokens, XP, or box." });

            List<long> explicitRecipients = (request.RecipientAccountIds ?? new List<long>())
                .Concat(request.RecipientAccountId > 0
                    ? new[] { request.RecipientAccountId }
                    : Array.Empty<long>())
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (!request.SendToAll && explicitRecipients.Count == 0)
                return BadRequest(new { error = "Choose a recipient, paste multiple IDs, or enable Send to all." });
            if (giftType == "tokens" && request.Amount == 0)
                return BadRequest(new
                {
                    error = "Token gifts must be a non-zero signed 32-bit amount. Use a negative amount to deduct tokens."
                });
            if (giftType == "xp" && request.Amount is < 1 or > 1000000)
                return BadRequest(new
                {
                    error = "XP must be between 1 and 1,000,000."
                });
            if (giftType == "consumable" && request.Amount is < 1 or > 100000)
                return BadRequest(new
                {
                    error = "Consumable quantity must be between 1 and 100,000."
                });
            if (giftType == "box" && request.BoxRarity is not (10 or 20 or 30 or 40 or 50))
                return BadRequest(new
                {
                    error = "Box rarity must be 10, 20, 30, 40, or 50."
                });

            if (request.BoxDesign == 0)
                request.BoxDesign = (int)PlayerDBClasses.GiftContext.Game_Drop;

            APIController.StorefrontAdminItem? catalogItem = null;
            List<APIController.StorefrontAdminItem> boxPool = new();
            if (giftType is "avatar" or "equipment" or "consumable")
            {
                catalogItem = APIController.GetStorefrontCatalogItem(request.SkuId);
                if (catalogItem == null)
                    return BadRequest(new { error = "Choose an item from the gift catalog." });

                bool matchesType = giftType switch
                {
                    "avatar" => !string.IsNullOrWhiteSpace(catalogItem.AvatarItemDesc),
                    "equipment" => !string.IsNullOrWhiteSpace(catalogItem.EquipmentPrefabName) &&
                        !string.IsNullOrWhiteSpace(catalogItem.EquipmentModificationGuid),
                    "consumable" => !string.IsNullOrWhiteSpace(catalogItem.ConsumableItemDesc),
                    _ => false
                };
                if (!matchesType)
                    return BadRequest(new { error = "The selected item does not match the gift type." });
            }
            else if (giftType == "box")
            {

                boxPool = APIController.GetWebsiteStorefrontItems()
                    .Where(item =>
                        item.Rarity == request.BoxRarity &&
                        !string.IsNullOrWhiteSpace(item.ConsumableItemDesc))
                    .ToList();
                if (boxPool.Count == 0)
                    return BadRequest(new { error = $"No consumables found at rarity {request.BoxRarity}." });
            }

            List<long> recipients = request.SendToAll
                ? PlayerDB.Players.FindAll()
                    .Where(account => account?.Player != null &&
                        account.PlayerId > 0 &&
                        account.PlayerId <= int.MaxValue &&
                        account.PlayerId != coachAccountId)
                    .Select(account => account.PlayerId)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList()
                : explicitRecipients
                    .Where(id => id != coachAccountId)
                    .OrderBy(id => id)
                    .ToList();

            if (request.OnlineOnly)
                recipients = recipients
                    .Where(id => PlayerDB.GetPlayerHeartbeat(id).isOnline)
                    .ToList();

            if (recipients.Count == 0)
                return BadRequest(new
                {
                    error = request.OnlineOnly
                        ? "No eligible recipients are currently online."
                        : "No eligible recipients were found."
                });

            string message = (request.Message ?? string.Empty).Trim();
            if (message.Length == 0)
                message = "A gift for you!";
            if (message.Length > 200)
                return BadRequest(new { error = "Gift message cannot exceed 200 characters." });

            int queued = 0;
            var failed = new List<long>();
            var liveDeliveries =
                new List<NotiController.GiftLiveDelivery>();
            foreach (long recipientId in recipients)
            {
                var recipient = PlayerDB.Players.FindById(recipientId);
                if (recipient?.Player == null || recipientId > int.MaxValue)
                {
                    failed.Add(recipientId);
                    continue;
                }

                APIController.StorefrontAdminItem? boxRoll = giftType == "box"
                    ? boxPool[BonusRarity50Rng.Next(boxPool.Count)]
                    : null;

                var gift = new PlayerDBClasses.GiftPackage
                {
                    FromPlayerId = coachAccountId,
                    Message = message,
                    AvatarItemDesc = giftType == "avatar"
                        ? catalogItem!.AvatarItemDesc
                        : string.Empty,
                    ConsumableItemDesc = giftType == "consumable"
                        ? catalogItem!.ConsumableItemDesc
                        : giftType == "box"
                            ? boxRoll!.ConsumableItemDesc
                            : string.Empty,
                    ConsumableQuantity = giftType == "consumable"
                        ? Math.Clamp(request.Amount, 1, 100000)
                        : 1,
                    EquipmentPrefabName = giftType == "equipment"
                        ? catalogItem!.EquipmentPrefabName
                        : string.Empty,
                    EquipmentModificationGuid = giftType == "equipment"
                        ? catalogItem!.EquipmentModificationGuid
                        : string.Empty,
                    FriendlyName = giftType == "equipment"
                        ? catalogItem!.FriendlyName
                        : giftType == "box"
                            ? boxRoll!.FriendlyName
                            : string.Empty,
                    ThumbnailImage = giftType == "equipment"
                        ? catalogItem!.ThumbnailImage
                        : giftType == "box"
                            ? boxRoll!.ThumbnailImage
                            : string.Empty,
                    CurrencyType = (int)PlayerDBClasses.CurrencyType.RecCenterTokens,
                    Currency = giftType == "tokens" ? request.Amount : 0,
                    XP = giftType == "xp" ? request.Amount : 0,
                    GiftContext = request.BoxDesign,
                    Rarity = giftType == "box" ? request.BoxRarity : catalogItem?.Rarity ?? 50,
                    Platform = -1,
                    PlatformMask = -1,
                    BalanceType = (int)PlayerDBClasses.BalanceType.NonPurchasedNotUsableInP2P,
                    IsQuery = false,
                    Unique = giftType is "avatar" or "equipment"
                };

                PlayerDBClasses.GiftPackage? queuedGift =
                    PlayerDB.QueueGiftPackage(recipientId, gift);
                if (queuedGift == null)
                {
                    failed.Add(recipientId);
                    continue;
                }

                queued++;
                liveDeliveries.Add(
                    new NotiController.GiftLiveDelivery(
                        recipientId,
                        queuedGift));

                if (BonusRarity50Rng.Next(100) < BonusRarity50PercentChance)
                {
                    APIController.StorefrontAdminItem? bonusItem =
                        APIController.GetWebsiteStorefrontItems()
                            .Where(item =>
                                item.Rarity == 50 &&
                                !string.IsNullOrWhiteSpace(item.ConsumableItemDesc))
                            .OrderBy(_ => BonusRarity50Rng.Next())
                            .FirstOrDefault();

                    if (bonusItem != null)
                    {
                        var bonusGift = new PlayerDBClasses.GiftPackage
                        {
                            FromPlayerId = coachAccountId,
                            Message = "Bonus gift!",
                            ConsumableItemDesc = bonusItem.ConsumableItemDesc,
                            ConsumableQuantity = 1,
                            FriendlyName = bonusItem.FriendlyName,
                            ThumbnailImage = bonusItem.ThumbnailImage,
                            CurrencyType = (int)PlayerDBClasses.CurrencyType.RecCenterTokens,
                            GiftContext = (int)PlayerDBClasses.GiftContext.Consumable,
                            Rarity = 50,
                            Platform = -1,
                            PlatformMask = -1,
                            BalanceType = (int)PlayerDBClasses.BalanceType.NonPurchasedNotUsableInP2P,
                            IsQuery = false,
                            Unique = false
                        };

                        PlayerDBClasses.GiftPackage? queuedBonus =
                            PlayerDB.QueueGiftPackage(recipientId, bonusGift);
                        if (queuedBonus != null)
                        {
                            liveDeliveries.Add(
                                new NotiController.GiftLiveDelivery(
                                    recipientId,
                                    queuedBonus));
                        }
                    }
                }
            }

            NotiController.GiftLiveDeliveryResult liveDelivery = new();
            try
            {
                liveDelivery = await NotiController.NotifyGiftsAsync(
                    coachAccountId,
                    liveDeliveries,
                    immediate: true);
            }
            catch (Exception exception)
            {

                liveDelivery.TargetPlayers = queued;
                liveDelivery.OfflinePlayers = queued;
                Console.WriteLine(
                    $"[ADMIN GIFT PUSH ERROR] broadcast={request.SendToAll} " +
                    $"queued={queued} error={exception.Message}");
            }

            string giftName = giftType switch
            {
                "tokens" => $"{request.Amount:N0} tokens",
                "xp" => $"{request.Amount:N0} XP",
                "consumable" => $"{catalogItem!.FriendlyName} x{Math.Clamp(request.Amount, 1, 100000):N0}",
                "box" => $"a rarity {request.BoxRarity} box",
                _ => catalogItem?.FriendlyName ?? giftType
            };
            Console.WriteLine(
                $"[ADMIN GIFT] admin={admin!.PlayerId} from=Coach(1) " +
                $"gift={giftName} boxDesign={request.BoxDesign} queued={queued} failed={failed.Count} " +
                $"all={request.SendToAll} onlineOnly={request.OnlineOnly}");
            Console.WriteLine(
                $"[ADMIN GIFT LIVE] queued={queued} " +
                $"livePlayers={liveDelivery.LivePlayers} " +
                $"liveSockets={liveDelivery.LiveSockets} " +
                $"offline={liveDelivery.OfflinePlayers} " +
                $"all={request.SendToAll} onlineOnly={request.OnlineOnly}");
            string targetDescription = request.SendToAll
                ? $"{queued} player{(queued == 1 ? "" : "s")}{(request.OnlineOnly ? " (online only)" : "")}"
                : recipients.Count == 1
                    ? $"account `{recipients[0]}`"
                    : $"{recipients.Count} accounts (`{string.Join(", ", recipients)}`)";
            DiscordLogger.Log(
                $"🎁 **Admin gift** — account `{admin.PlayerId}` sent **{giftName}** " +
                $"from Coach to {targetDescription}.");

            if (queued == 0)
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "The gift could not be queued.", failedAccountIds = failed });

            return Ok(new
            {
                success = true,
                fromAccountId = coachAccountId,
                from = "Coach",
                gift = giftName,
                queued,
                livePlayers = liveDelivery.LivePlayers,
                liveSockets = liveDelivery.LiveSockets,
                pendingPlayers = Math.Max(
                    0,
                    queued - liveDelivery.LivePlayers),
                failed = failed.Count,
                failedAccountIds = failed
            });
        }

        [HttpPost("admin/gifts/clear-outgoing")]
        public IActionResult AdminClearOutgoingGifts(
            [FromBody] AdminClearOutgoingGiftsRequest? request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            long fromPlayerId = request?.FromPlayerId ?? 1;
            if (fromPlayerId <= 0)
                return BadRequest(new { error = "Invalid sender account id." });

            PlayerDB.ClearOutgoingGiftsResult result =
                PlayerDB.ClearPendingGiftsFromSender(fromPlayerId);

            Console.WriteLine(
                $"[ADMIN GIFT CLEAR] admin={admin!.PlayerId} from={fromPlayerId} " +
                $"removedBoxes={result.RemovedBoxes} affectedPlayers={result.AffectedPlayers}");
            DiscordLogger.Log(
                $"🧹 **Admin gift clear** — account `{admin.PlayerId}` cleared " +
                $"**{result.RemovedBoxes}** unclaimed outgoing box(es) from account `{fromPlayerId}` " +
                $"across **{result.AffectedPlayers}** player(s).");

            return Ok(new
            {
                success = true,
                removedBoxes = result.RemovedBoxes,
                affectedPlayers = result.AffectedPlayers
            });
        }

        [HttpPut("admin/accounts/{accountId:long}/profile")]
        public async Task<IActionResult> AdminUpdateProfile(long accountId, [FromBody] AdminProfileUpdate request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);
            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (IsDeveloper(account) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Moderators cannot edit Developer profiles." });

            string username = request.Username?.Trim().TrimStart('@') ?? string.Empty;
            string displayName = request.DisplayName?.Trim() ?? string.Empty;
            if (username.Length is < 3 or > 20 || username.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_'))
                return BadRequest(new { error = "Username must be 3-20 letters, numbers, or underscores." });
            if (displayName.Length is < 1 or > 32)
                return BadRequest(new { error = "Display name must be 1-32 characters." });
            if (PlayerDB.Players.FindAll().Any(x => x.PlayerId != accountId &&
                string.Equals(x.Player?.Username, username, StringComparison.OrdinalIgnoreCase)))
                return Conflict(new { error = "That username is already taken." });

            account.Player.Username = username;
            account.Player.DisplayName = displayName;
            account.Player.Bio = (request.Bio ?? string.Empty).Trim()[..Math.Min((request.Bio ?? string.Empty).Trim().Length, 500)];
            account.Player.Email = (request.Email ?? string.Empty).Trim();
            PlayerDB.Players.Update(account);
            await NotiController.NotifyPlayerProfileUpdatedAsync(accountId);
            return Ok(new { success = true });
        }

        [HttpPost("admin/accounts/{accountId:long}/username/reset")]
        public async Task<IActionResult> AdminResetUsername(long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (IsDeveloper(account) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Moderators cannot reset Developer usernames." });

            var existingUsernames = PlayerDB.Players.FindAll()
                .Where(candidate => candidate.PlayerId != accountId)
                .Select(candidate => candidate.Player?.Username)
                .Where(username => !string.IsNullOrWhiteSpace(username))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            string? generatedUsername = null;
            for (int attempt = 0; attempt < 256; attempt++)
            {
                string candidate = NameGen.GetRandomName();
                if (candidate.Length is >= 3 and <= 20 &&
                    candidate.All(ch => char.IsLetterOrDigit(ch) || ch == '_') &&
                    !existingUsernames.Contains(candidate))
                {
                    generatedUsername = candidate;
                    break;
                }
            }

            if (generatedUsername == null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Could not generate an available username. Try again." });

            string? previousUsername = account.Player.Username;
            account.Player.Username = generatedUsername;
            account.Player.DisplayName = generatedUsername;
            if (!PlayerDB.Players.Update(account))
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Could not save the generated username." });

            await NotiController.NotifyPlayerProfileUpdatedAsync(accountId);
            return Ok(new
            {
                success = true,
                previousUsername,
                username = generatedUsername,
                displayName = generatedUsername,
                availableUsernameChanges = account.Player.AvailableUsernameChanges
            });
        }

        [HttpPost("admin/accounts/{accountId:long}/images/{imageKind}/reset")]
        public async Task<IActionResult> AdminResetProfileImage(long accountId, string imageKind)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (IsDeveloper(account) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Moderators cannot reset Developer profile images." });

            string normalizedKind = imageKind.Trim().ToLowerInvariant();
            if (normalizedKind == "pfp")
            {
                account.Player.ProfileImage = "DefaultPFP.png";
            }
            else if (normalizedKind == "banner")
            {
                account.Player.BannerImage = null;
            }
            else
            {
                return BadRequest(new { error = "Image type must be pfp or banner." });
            }

            if (!PlayerDB.Players.Update(account))
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Could not reset the profile image." });

            await NotiController.NotifyPlayerProfileUpdatedAsync(accountId);
            return Ok(new
            {
                success = true,
                imageKind = normalizedKind,
                profileImage = account.Player.ProfileImage,
                bannerImage = account.Player.BannerImage
            });
        }

        [HttpPut("admin/accounts/{accountId:long}/details")]
        public async Task<IActionResult> AdminUpdateAccountDetails(long accountId, [FromBody] AdminAccountDetailsUpdate request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (IsDeveloper(account) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Moderators cannot edit Developer account details." });
            if (request.Level is < 1 or > 50)
                return BadRequest(new { error = "Level must be between 1 and 50." });
            if (request.XP < 0)
                return BadRequest(new { error = "XP cannot be negative." });
            if (request.AvailableUsernameChanges is < 0 or > 1000000)
                return BadRequest(new { error = "Username changes must be between 0 and 1,000,000." });

            string displayEmoji = request.DisplayEmoji?.Trim() ?? string.Empty;
            if (displayEmoji.Length > 16 || displayEmoji.Any(char.IsControl))
                return BadRequest(new { error = "Display emoji must be 16 characters or fewer and cannot contain control characters." });
            if (request.PersonalPronouns < 0 || (request.PersonalPronouns & ~0x3F) != 0)
                return BadRequest(new { error = "Personal pronouns must be a valid six-bit value from 0 to 63." });

            string rawProfileImage = request.ProfileImage?.Trim() ?? string.Empty;
            string? profileImage = NormalizeImagePath(rawProfileImage);
            if (rawProfileImage.Length > 0 && profileImage == null)
                return BadRequest(new { error = "Profile image path is invalid." });

            string rawBannerImage = request.BannerImage?.Trim() ?? string.Empty;
            string? bannerImage = NormalizeImagePath(rawBannerImage);
            if (rawBannerImage.Length > 0 && bannerImage == null)
                return BadRequest(new { error = "Banner image path is invalid." });

            account.Player.IsJunior = request.IsJunior;
            account.Player.AvailableUsernameChanges = request.AvailableUsernameChanges;
            account.Player.ProfileImage = profileImage ?? "DefaultPFP.png";
            account.Player.BannerImage = bannerImage;
            account.Player.DisplayEmoji = displayEmoji;
            account.Player.PersonalPronouns = request.PersonalPronouns;
            if (!PlayerDB.Players.Update(account))
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Could not save account details." });

            var progression = PlayerDB.SetProgression(accountId, request.Level, request.XP);
            if (progression == null)
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Account details saved, but progression could not be updated." });

            await NotiController.NotifyPlayerProfileUpdatedAsync(accountId);
            return Ok(new
            {
                success = true,
                level = progression.Level,
                xp = progression.XP,
                profileImage = ImageUrl(account.Player.ProfileImage)
            });
        }

        [HttpPost("admin/accounts/{accountId:long}/platforms")]
        public IActionResult AdminSetPlatform(long accountId, [FromBody] AdminPlatformChange request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);
            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (IsDeveloper(account) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Moderators cannot edit Developer platforms." });
            if (!Enum.TryParse(request.Platform, true, out PlayerDBClasses.Platforms platform) ||
                platform is PlayerDBClasses.Platforms.All or PlayerDBClasses.Platforms.HeadlessBot ||
                !ulong.TryParse(request.PlatformId, out ulong platformId) || platformId == 0)
                return BadRequest(new { error = "Enter a valid platform and numeric platform ID." });

            account.PlatformIds ??= new List<PlayerDBClasses.mPlatformID>();
            bool exists = account.PlatformIds.Any(x => x.Platform == platform && x.PlatformId == platformId);
            if (request.Enabled && !exists)
            {
                account.PlatformIds.Add(new PlayerDBClasses.mPlatformID { Platform = platform, PlatformId = platformId });
            }
            else if (!request.Enabled && exists)
            {
                if (account.PlatformIds.Count == 1)
                    return BadRequest(new { error = "An account must keep at least one platform identity." });
                account.PlatformIds.RemoveAll(x => x.Platform == platform && x.PlatformId == platformId);
            }

            PlayerDB.Players.Update(account);
            return Ok(new { success = true, platforms = account.PlatformIds.Select(x => new { platform = x.Platform.ToString(), platformId = x.PlatformId.ToString() }) });
        }

        [HttpPost("admin/accounts/{accountId:long}/roles")]
        public IActionResult AdminSetRole(long accountId, [FromBody] AdminRoleChange request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (!Enum.TryParse(request.Role, true, out PlayerDBClasses.PlayerRoles role))
                return BadRequest(new { error = "Choose a valid role." });
            if (role == PlayerDBClasses.PlayerRoles.Developer && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Only Developers can change the Developer role." });

            account.PlayerRoles ??= new List<PlayerDBClasses.PlayerRoles>();
            if (!request.Enabled && accountId == admin!.PlayerId &&
                role is PlayerDBClasses.PlayerRoles.Developer or PlayerDBClasses.PlayerRoles.Moderator &&
                account.PlayerRoles.Count(x => x is PlayerDBClasses.PlayerRoles.Developer or PlayerDBClasses.PlayerRoles.Moderator) == 1)
                return BadRequest(new { error = "You cannot remove your own final admin role." });

            if (request.Enabled && !account.PlayerRoles.Contains(role))
            {
                account.PlayerRoles.Add(role);
                if (role == PlayerDBClasses.PlayerRoles.Developer)
                    PlayerDB.GrantDeveloperCheerAccess(
                        account,
                        selectDeveloperBadge: true);
            }
            else if (!request.Enabled)
            {
                account.PlayerRoles.Remove(role);
                if (role == PlayerDBClasses.PlayerRoles.Developer &&
                    account.Player.Reputation?.SelectedCheer ==
                    PlayerDBClasses.CheerCategory.RecRoomDeveloper)
                {
                    account.Player.Reputation.SelectedCheer =
                        PlayerDBClasses.CheerCategory.General;
                }
            }

            PlayerDB.Players.Update(account);
            return Ok(new { success = true, roles = account.PlayerRoles.Select(x => x.ToString()) });
        }

        [HttpDelete("admin/accounts/{accountId:long}/roles")]
        public IActionResult AdminClearRoles(long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });

            if (accountId == admin!.PlayerId)
                return BadRequest(new { error = "You cannot clear your own roles." });
            if (IsDeveloper(account) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "Only Developers can clear a Developer's roles." });

            bool wasDeveloper = account.PlayerRoles?.Contains(PlayerDBClasses.PlayerRoles.Developer) == true;
            account.PlayerRoles = new List<PlayerDBClasses.PlayerRoles>();

            if (wasDeveloper &&
                account.Player.Reputation?.SelectedCheer == PlayerDBClasses.CheerCategory.RecRoomDeveloper)
            {
                account.Player.Reputation.SelectedCheer = PlayerDBClasses.CheerCategory.General;
            }

            PlayerDB.Players.Update(account);

            Console.WriteLine(
                $"[ADMIN ROLES] admin={admin.PlayerId} cleared all roles on account={accountId}");

            return Ok(new { success = true, roles = Array.Empty<string>() });
        }

        [HttpPost("admin/accounts/{accountId:long}/force-join-instance")]
        public async Task<IActionResult> AdminForceJoinInstance(long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            if (accountId == admin!.PlayerId)
                return BadRequest(new { error = "You're already in your own instance." });

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });

            var adminHeartbeat = PlayerDB.GetPlayerHeartbeat(admin.PlayerId);
            var instance = adminHeartbeat?.roomInstance;
            if (instance == null || !adminHeartbeat!.isOnline)
                return BadRequest(new { error = "You need to be in a live room instance in-game first." });

            var targetInstance = new PlayerDBClasses.RoomInstance
            {
                encryptVoiceChat = instance.encryptVoiceChat,
                clubId = instance.clubId,
                dataBlob = instance.dataBlob,
                eventId = instance.eventId,
                isFull = instance.isFull,
                isInProgress = instance.isInProgress,
                isPrivate = instance.isPrivate,
                location = instance.location,
                maxCapacity = instance.maxCapacity,
                Name = instance.Name,
                photonRegion = instance.photonRegion,
                photonRegionId = instance.photonRegionId,
                photonRoomId = instance.photonRoomId,
                roomCode = instance.roomCode,
                roomId = instance.roomId,
                roomInstanceId = instance.roomInstanceId,
                roomInstanceType = instance.roomInstanceType,
                subRoomId = instance.subRoomId,
                subRoomDataSaveId = instance.subRoomDataSaveId,

                createdAt = DateTime.UtcNow
            };

            PlayerDB.UpdatePlayerHeartbeat(accountId, targetInstance, online: true);

            if (targetInstance.roomInstanceType == PlayerDBClasses.RoomInstanceType.Dormroom)
                Sessions.MarkGuestDormEntry(accountId);

            await NotiController.NotifyRoomInviteAsync(
                admin.PlayerId,
                accountId,
                instance.roomId,
                instance.roomInstanceId,
                instance.photonRoomId);

            bool delivered = NotiController.IsPlayerConnected(accountId);

            return Ok(new
            {
                success = true,
                delivered,
                roomInstanceId = instance.roomInstanceId,
                roomName = instance.Name
            });
        }

        private async Task<(PlayerDBClasses.RoomInstance instance, bool delivered)> ForceIntoInstanceAsync(
            long moverAccountId,
            long fromPlayerId,
            PlayerDBClasses.RoomInstance sourceInstance,
            bool notifyMover)
        {
            var movedInstance = new PlayerDBClasses.RoomInstance
            {
                encryptVoiceChat = sourceInstance.encryptVoiceChat,
                clubId = sourceInstance.clubId,
                dataBlob = sourceInstance.dataBlob,
                eventId = sourceInstance.eventId,
                isFull = sourceInstance.isFull,
                isInProgress = sourceInstance.isInProgress,
                isPrivate = sourceInstance.isPrivate,
                location = sourceInstance.location,
                maxCapacity = sourceInstance.maxCapacity,
                Name = sourceInstance.Name,
                photonRegion = sourceInstance.photonRegion,
                photonRegionId = sourceInstance.photonRegionId,
                photonRoomId = sourceInstance.photonRoomId,
                roomCode = sourceInstance.roomCode,
                roomId = sourceInstance.roomId,
                roomInstanceId = sourceInstance.roomInstanceId,
                roomInstanceType = sourceInstance.roomInstanceType,
                subRoomId = sourceInstance.subRoomId,
                subRoomDataSaveId = sourceInstance.subRoomDataSaveId,
                createdAt = DateTime.UtcNow
            };

            PlayerDB.UpdatePlayerHeartbeat(moverAccountId, movedInstance, online: true);

            if (movedInstance.roomInstanceType == PlayerDBClasses.RoomInstanceType.Dormroom)
                Sessions.MarkGuestDormEntry(moverAccountId);

            if (notifyMover)
            {
                await NotiController.NotifyRoomInviteAsync(
                    fromPlayerId,
                    moverAccountId,
                    movedInstance.roomId,
                    movedInstance.roomInstanceId,
                    movedInstance.photonRoomId);
            }

            bool delivered = NotiController.IsPlayerConnected(moverAccountId);
            return (movedInstance, delivered);
        }

        [HttpPost("admin/accounts/{targetAccountId:long}/force-me-into")]
        public async Task<IActionResult> AdminForceMeIntoInstance(long targetAccountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            if (targetAccountId == admin!.PlayerId)
                return BadRequest(new { error = "That's your own account." });

            var target = PlayerDB.Players.FindById(targetAccountId);
            if (target?.Player == null)
                return NotFound(new { error = "Account not found." });

            var targetHeartbeat = PlayerDB.GetPlayerHeartbeat(targetAccountId);
            var sourceInstance = targetHeartbeat?.roomInstance;
            if (sourceInstance == null || !targetHeartbeat!.isOnline)
                return BadRequest(new { error = "That account isn't currently in a live room instance." });

            var (instance, delivered) = await ForceIntoInstanceAsync(
                admin.PlayerId,
                admin.PlayerId,
                sourceInstance,
                notifyMover: true);

            return Ok(new
            {
                success = true,
                delivered,
                roomInstanceId = instance.roomInstanceId,
                roomName = instance.Name
            });
        }

        [HttpPost("admin/accounts/{moveAccountId:long}/force-into/{targetAccountId:long}")]
        public async Task<IActionResult> AdminForceUserIntoInstance(
            long moveAccountId,
            long targetAccountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            if (moveAccountId == targetAccountId)
                return BadRequest(new { error = "Those are the same account." });

            var mover = PlayerDB.Players.FindById(moveAccountId);
            if (mover?.Player == null)
                return NotFound(new { error = "Account to move not found." });

            var target = PlayerDB.Players.FindById(targetAccountId);
            if (target?.Player == null)
                return NotFound(new { error = "Target account not found." });

            var targetHeartbeat = PlayerDB.GetPlayerHeartbeat(targetAccountId);
            var sourceInstance = targetHeartbeat?.roomInstance;
            if (sourceInstance == null || !targetHeartbeat!.isOnline)
                return BadRequest(new { error = "The target account isn't currently in a live room instance." });

            var (instance, delivered) = await ForceIntoInstanceAsync(
                moveAccountId,
                admin!.PlayerId,
                sourceInstance,
                notifyMover: true);

            Console.WriteLine(
                $"[ADMIN FORCE-INTO] admin={admin.PlayerId} moved={moveAccountId} " +
                $"into={targetAccountId}'s instance ({instance.Name}) targetNotified=false");

            return Ok(new
            {
                success = true,
                delivered,
                roomInstanceId = instance.roomInstanceId,
                roomName = instance.Name
            });
        }

        [HttpPost("admin/accounts/{accountId:long}/troll/kick-to-room")]
        public async Task<IActionResult> AdminTrollKickToRoom(
            long accountId,
            [FromBody] AdminKickToRoomRequest? request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can do this." });

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });

            RoomDBClasses.Room? targetRoom = null;
            if (request?.RoomId is > 0)
                targetRoom = RoomDB.GetRoom(request.RoomId.Value);
            else if (!string.IsNullOrWhiteSpace(request?.RoomName))
                targetRoom = RoomDB.GetRoomByName(request!.RoomName!.Trim());

            RoomDBClasses.SubRooms? targetSubRoom = targetRoom?.SubRooms?.FirstOrDefault();

            PlayerDBClasses.RoomInstance targetInstance;
            if (targetRoom != null && targetSubRoom != null)
            {
                long instanceId = System.Random.Shared.NextInt64(1_000_000L, 1_000_000_000_000L);
                string roomName = targetRoom.Name ?? "UnknownRoom";

                targetInstance = new PlayerDBClasses.RoomInstance
                {
                    roomId = targetRoom.RoomId,
                    subRoomId = targetSubRoom.SubRoomId,
                    subRoomDataSaveId = targetSubRoom.SubRoomDataSaveId,
                    roomInstanceId = instanceId,
                    Name = $"^{roomName}",
                    location = targetSubRoom.UnitySceneId,
                    isPrivate = false,
                    isFull = false,
                    isInProgress = false,
                    maxCapacity = Math.Max(1, targetSubRoom.MaxPlayers),
                    roomInstanceType = PlayerDBClasses.RoomInstanceType.Public,
                    photonRegion = ServerConfig.PhotonRegion,
                    photonRegionId = ServerConfig.PhotonRegion,
                    photonRoomId = $"MochaRoom-{roomName}-{instanceId}",
                    createdAt = DateTime.UtcNow
                };
            }
            else
            {

                var adminHeartbeat = PlayerDB.GetPlayerHeartbeat(admin!.PlayerId);
                var instance = adminHeartbeat?.roomInstance;
                if (instance == null || !adminHeartbeat!.isOnline)
                    return BadRequest(new
                    {
                        error = "Give a roomId/roomName, or be in a live room instance yourself first."
                    });

                targetInstance = new PlayerDBClasses.RoomInstance
                {
                    encryptVoiceChat = instance.encryptVoiceChat,
                    clubId = instance.clubId,
                    dataBlob = instance.dataBlob,
                    eventId = instance.eventId,
                    isFull = instance.isFull,
                    isInProgress = instance.isInProgress,
                    isPrivate = instance.isPrivate,
                    location = instance.location,
                    maxCapacity = instance.maxCapacity,
                    Name = instance.Name,
                    photonRegion = instance.photonRegion,
                    photonRegionId = instance.photonRegionId,
                    photonRoomId = instance.photonRoomId,
                    roomCode = instance.roomCode,
                    roomId = instance.roomId,
                    roomInstanceId = instance.roomInstanceId,
                    roomInstanceType = instance.roomInstanceType,
                    subRoomId = instance.subRoomId,
                    subRoomDataSaveId = instance.subRoomDataSaveId,
                    createdAt = DateTime.UtcNow
                };
            }

            int disconnected = NotiController.ForceDisconnectPlayer(accountId);

            PlayerDB.UpdatePlayerHeartbeat(accountId, targetInstance, online: true);

            if (targetInstance.roomInstanceType == PlayerDBClasses.RoomInstanceType.Dormroom)
                Sessions.MarkGuestDormEntry(accountId);

            await NotiController.NotifyRoomInviteAsync(
                admin!.PlayerId,
                accountId,
                targetInstance.roomId,
                targetInstance.roomInstanceId,
                targetInstance.photonRoomId);

            Console.WriteLine(
                $"[ADMIN TROLL] admin={admin.PlayerId} kicked player={accountId} " +
                $"disconnectedSockets={disconnected} toRoom={targetInstance.Name}");

            return Ok(new
            {
                success = true,
                disconnectedSockets = disconnected,
                roomInstanceId = targetInstance.roomInstanceId,
                roomName = targetInstance.Name
            });
        }

        [HttpPost("admin/accounts/{accountId:long}/troll/fakebox-and-ban")]
        public async Task<IActionResult> AdminTrollFakeBoxAndBan(
            long accountId,
            [FromBody] AdminTrollFakeBoxRequest? request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can do this." });

            var target = PlayerDB.Players.FindById(accountId);
            if (target?.Player == null)
                return NotFound(new { error = "Account not found." });

            int fakeAmount = Math.Clamp(request?.TokenAmount ?? 5000, 1, 1_000_000);
            int currentLevel = target.Player.Level;
            int fakeLevel = currentLevel + 1;

            var fakeBox = new PlayerDBClasses.GiftPackage
            {
                FromPlayerId = 1,
                Message = $"Congrats on getting to level {fakeLevel}!",
                Currency = 0,
                CurrencyType = (int)PlayerDBClasses.CurrencyType.RecCenterTokens,
                GiftContext = (int)PlayerDBClasses.GiftContext.LevelUp,
                Rarity = 50,
                Platform = -1,
                PlatformMask = -1,
                BalanceType = (int)PlayerDBClasses.BalanceType.NonPurchasedNotUsableInP2P,
                IsQuery = false,
                Unique = false
            };

            PlayerDBClasses.GiftPackage? queuedGift =
                PlayerDB.QueueGiftPackage(accountId, fakeBox);
            if (queuedGift == null)
                return BadRequest(new { error = "Couldn't queue the fake box." });

            var liveDeliveries = new List<NotiController.GiftLiveDelivery>
            {
                new(accountId, queuedGift)
            };

            try
            {
                await NotiController.NotifyGiftsAsync(1, liveDeliveries, immediate: true);

                await NotiController.NotifyProgressionAsync(
                    accountId,
                    currentLevel,
                    fakeLevel,
                    target.Player.XP);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[ADMIN TROLL FAKEBOX] delivery push failed: {exception.Message}");
            }

            if (request?.BanImmediately != true)
            {
                Console.WriteLine(
                    $"[ADMIN TROLL FAKEBOX] admin={admin!.PlayerId} target={accountId} " +
                    $"amount={fakeAmount} fakeLevel={fakeLevel} banImmediately=false");
                return Ok(new { success = true, tokenAmount = fakeAmount, fakeLevel, banned = false });
            }

            string reason = "Cheating";
            const int durationSeconds = int.MaxValue;

            var targets = new List<PlayerDBClasses.FullPlayer> { target };
            var identities = (target.PlatformIds ?? new())
                .Select(x => (x.Platform, x.PlatformId))
                .ToHashSet();
            targets.AddRange(PlayerDB.Players.FindAll().Where(x =>
                x.PlayerId != accountId &&
                x.PlatformIds?.Any(p => identities.Contains((p.Platform, p.PlatformId))) == true));

            if (!IsDeveloper(admin) && targets.Any(IsDeveloper))
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    error = "A linked Developer account cannot be banned by a Moderator."
                });

            var distinctTargets = targets.DistinctBy(x => x.PlayerId).ToList();
            var linkedAccountSummaries = distinctTargets
                .Select(x => (x.PlayerId, (string?)(x.Player?.Username ?? x.PlayerId.ToString())))
                .ToList();
            var platformIdentities = (target.PlatformIds ?? new())
                .Select(x => (x.Platform.ToString(), x.PlatformId.ToString()))
                .ToList();
            string targetUsername = (target.Player.Username ?? accountId.ToString()).TrimStart('@');

            foreach (var bannedAccount in distinctTargets)
            {
                string banReason = bannedAccount.PlayerId == accountId
                    ? reason
                    : $"{reason}\n\nRelated account: @{targetUsername} (auto link-banned)";
                PlayerDB.BanPlayer(bannedAccount.PlayerId, durationSeconds, banReason, (ulong)admin!.PlayerId);
                RecNetDB.ModerationLocks.Delete(bannedAccount.PlayerId);

                DiscordLogger.LogBan(
                    bannedAccount.PlayerId,
                    bannedAccount.Player?.Username,
                    admin.PlayerId,
                    admin.Player?.Username,
                    banReason,
                    durationSeconds,
                    linkedAccounts: distinctTargets.Count > 1 ? linkedAccountSummaries : null,
                    platformIdentities: bannedAccount.PlayerId == accountId ? platformIdentities : null);
            }

            Console.WriteLine(
                $"[ADMIN TROLL FAKEBOX+BAN] admin={admin.PlayerId} target={accountId} " +
                $"amount={fakeAmount} permaBanned=true linkBanned={distinctTargets.Count}");

            return Ok(new
            {
                success = true,
                tokenAmount = fakeAmount,
                banned = true,
                permanent = true,
                affectedCount = distinctTargets.Count
            });
        }

        [HttpPut("admin/accounts/{accountId:long}/password")]
        public IActionResult AdminResetPassword(long accountId, [FromBody] AdminPasswordReset request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (IsDeveloper(account) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Moderators cannot reset a Developer's password." });
            if (string.IsNullOrEmpty(request.NewPassword) ||
                request.NewPassword.Length < PasswordSecurity.MinPasswordLength ||
                request.NewPassword.Length > PasswordSecurity.MaxPasswordLength)
                return BadRequest(new { error = "Password must be at least 8 characters." });

            account.Password = PasswordSecurity.Hash(request.NewPassword);
            PlayerDB.Players.Update(account);
            return Ok(new { success = true });
        }

        [HttpGet("admin/accounts/{accountId:long}")]
        public IActionResult GetAdminAccountDetail(long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });

            var accountLock = RecNetDB.ModerationLocks.FindById(accountId);
            PlayerDB.IsPlayerBanned(accountId, out var nativeBan);

            return Ok(new
            {
                accountId = account.PlayerId,
                username = account.Player.Username,
                displayName = account.Player.DisplayName,
                bio = account.Player.Bio,
                email = account.Player.Email,
                profileImage = ImageUrl(account.Player.ProfileImage),
                profileImagePath = NormalizeImagePath(account.Player.ProfileImage) ?? "DefaultPFP.png",
                bannerImagePath = NormalizeImagePath(account.Player.BannerImage) ?? string.Empty,
                level = account.Player.Level,
                xp = account.Player.XP,
                isJunior = account.Player.IsJunior ?? false,
                createdAt = account.Player.CreatedAt,
                availableUsernameChanges = account.Player.AvailableUsernameChanges,
                displayEmoji = account.Player.DisplayEmoji ?? string.Empty,
                personalPronouns = account.Player.PersonalPronouns,
                balance = PlayerDB.GetCurrencyBalance(
                    accountId,
                    PlayerDBClasses.CurrencyType.RecCenterTokens),
                platforms = account.PlatformIds.Select(p => new { platform = p.Platform.ToString(), platformId = p.PlatformId.ToString() }),
                roles = account.PlayerRoles.Select(r => r.ToString()),
                ban = nativeBan == null ? null : new
                {
                    reason = nativeBan.Message,
                    duration = nativeBan.Duration,
                    issuedAt = DateTimeOffset.FromUnixTimeSeconds(nativeBan.ModerationSetUnixTime).UtcDateTime
                },
                moderationLock = accountLock == null ? null : new
                {
                    reason = accountLock.Reason,
                    issuedAt = accountLock.IssuedAt,
                    relatedUsername = accountLock.RelatedUsername,
                    isRelated = accountLock.RelatedAccountId.HasValue
                }
            });
        }

        [HttpGet("admin/accounts/{accountId:long}/settings")]
        public IActionResult GetAdminAccountSettings(
            long accountId,
            [FromQuery] string? search = null)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });

            string term = search?.Trim() ?? string.Empty;
            var settings = (account.Player.PlayerExtra?.Settings ?? new List<PlayerDBClasses.Setting>())
                .Where(s => string.IsNullOrEmpty(term) ||
                    s.Key.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    s.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
                .Select(s => new { key = s.Key, value = s.Value })
                .ToList();

            return Ok(settings);
        }

        [HttpPost("admin/avatar-items")]
        public IActionResult AdminAddAvatarItem([FromBody] object avatarItem)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403);

            try
            {
                return Ok(new
                {
                    success = true,
                    message = "Avatar item received. Note: This endpoint currently only validates the JSON - actual catalog modification requires file system access.",
                    item = avatarItem
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("admin/preferences")]
        public IActionResult GetAdminPreferences()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (admin?.Player == null)
                return Unauthorized();

            admin.Player.PlayerExtra ??= new PlayerDBClasses.PlayerExtra();
            admin.Player.PlayerExtra.Settings ??= new List<PlayerDBClasses.Setting>();

            var prefs = admin.Player.PlayerExtra.Settings
                .Where(s => s.Key.StartsWith("mocha_", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);

            return Ok(new
            {
                theme = prefs.TryGetValue("mocha_theme", out var theme) ? theme : "dark",
                accentColor = prefs.TryGetValue("mocha_accentColor", out var accent) ? accent : "#7c3aed"
            });
        }

        [HttpPut("admin/preferences")]
        public IActionResult SetAdminPreferences([FromBody] AdminPreferencesRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (admin?.Player == null)
                return Unauthorized();

            admin.Player.PlayerExtra ??= new PlayerDBClasses.PlayerExtra();
            admin.Player.PlayerExtra.Settings ??= new List<PlayerDBClasses.Setting>();

            if (!string.IsNullOrWhiteSpace(request.Theme))
            {
                var existing = admin.Player.PlayerExtra.Settings
                    .FirstOrDefault(s => string.Equals(s.Key, "mocha_theme", StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                    existing.Value = request.Theme;
                else
                    admin.Player.PlayerExtra.Settings.Add(new PlayerDBClasses.Setting { Key = "mocha_theme", Value = request.Theme });
            }

            if (!string.IsNullOrWhiteSpace(request.AccentColor))
            {
                var existing = admin.Player.PlayerExtra.Settings
                    .FirstOrDefault(s => string.Equals(s.Key, "mocha_accentColor", StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                    existing.Value = request.AccentColor;
                else
                    admin.Player.PlayerExtra.Settings.Add(new PlayerDBClasses.Setting { Key = "mocha_accentColor", Value = request.AccentColor });
            }

            PlayerDB.Players.Update(admin);

            return Ok(new { success = true });
        }

        [HttpGet("admin/accounts/{accountId:long}/reputation")]
        public IActionResult GetAdminAccountReputation(long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });

            account.Player.Reputation ??= new PlayerDBClasses.Reputation();

            return Ok(new
            {
                accountId,
                isCheerful = account.Player.Reputation.IsCheerful,
                selectedCheer = account.Player.Reputation.SelectedCheer.ToString(),
                cheerGeneral = account.Player.Reputation.CheerGeneral,
                cheerHelpful = account.Player.Reputation.CheerHelpful,
                cheerSportsman = account.Player.Reputation.CheerSportsman,
                cheerGreatHost = account.Player.Reputation.CheerGreatHost,
                cheerCreative = account.Player.Reputation.CheerCreative,
                availableBadges = Enum.GetNames(typeof(PlayerDBClasses.CheerCategory))
            });
        }

        [HttpPut("admin/accounts/{accountId:long}/reputation")]
        public IActionResult SetAdminAccountReputation(
            long accountId,
            [FromBody] AdminReputationRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (IsDeveloper(account) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "Moderators cannot edit a Developer's reputation." });

            account.Player.Reputation ??= new PlayerDBClasses.Reputation();

            if (request.IsCheerful.HasValue)
                account.Player.Reputation.IsCheerful = request.IsCheerful.Value;

            if (request.CheerGeneral.HasValue)
                account.Player.Reputation.CheerGeneral = request.CheerGeneral.Value;

            if (request.CheerHelpful.HasValue)
                account.Player.Reputation.CheerHelpful = request.CheerHelpful.Value;

            if (request.CheerSportsman.HasValue)
                account.Player.Reputation.CheerSportsman = request.CheerSportsman.Value;

            if (request.CheerGreatHost.HasValue)
                account.Player.Reputation.CheerGreatHost = request.CheerGreatHost.Value;

            if (request.CheerCreative.HasValue)
                account.Player.Reputation.CheerCreative = request.CheerCreative.Value;

            if (!string.IsNullOrWhiteSpace(request.SelectedCheer) &&
                Enum.TryParse<PlayerDBClasses.CheerCategory>(request.SelectedCheer, true, out var selectedCheer))
            {
                account.Player.Reputation.SelectedCheer = selectedCheer;
            }

            PlayerDB.Players.Update(account);

            Console.WriteLine(
                $"[ADMIN REPUTATION] admin={admin!.PlayerId} account={accountId} updated reputation");

            return Ok(new { success = true });
        }

        [HttpGet("admin/accounts/{accountId:long}/photos")]
        public IActionResult GetAdminAccountPhotos(
            long accountId,
            [FromQuery] string? type = null)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });

            var ownedPhotos = GetOwnedPhotos(account)
                .OrderByDescending(x => x.takenAt)
                .ToList();

            if (type == "mine")
            {
                return Ok(new
                {
                    accountId,
                    photoCount = ownedPhotos.Count,
                    photos = ownedPhotos
                });
            }
            else if (type == "of-me")
            {

                return Ok(new
                {
                    accountId,
                    photoCount = 0,
                    photos = Array.Empty<RecNetPhoto>()
                });
            }

            return Ok(new
            {
                accountId,
                ownedPhotoCount = ownedPhotos.Count,
                ownedPhotos,
                photosOfMe = Array.Empty<RecNetPhoto>()
            });
        }

        [HttpGet("admin/accounts/{accountId:long}/inventory")]
        public IActionResult GetAdminPlayerInventory(
            long accountId,
            [FromQuery] string? search = null)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (IsDeveloper(account) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "Moderators cannot edit a Developer's inventory." });

            account.Player.PlayerExtra ??= new PlayerDBClasses.PlayerExtra();
            account.Player.PlayerExtra.AvatarItems ??= new List<string>();
            PlayerInventoryStore.EnsureInitialized(
                accountId,
                account.Player.PlayerExtra.AvatarItems);

            string term = search?.Trim() ?? string.Empty;
            bool Matches(string name, string descriptor, long id) =>
                string.IsNullOrWhiteSpace(term) ||
                name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                descriptor.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                id.ToString().Contains(term, StringComparison.OrdinalIgnoreCase);

            var avatarItems = PlayerInventoryStore.GetAvatarCatalog()
                .Where(item => Matches(
                    item.FriendlyName ?? string.Empty,
                    item.AvatarItemDesc ?? string.Empty,
                    item.AvatarItemId))
                .OrderBy(item => item.FriendlyName, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .Select(item => new
                {
                    avatarItemId = item.AvatarItemId,
                    avatarItemDesc = item.AvatarItemDesc,
                    friendlyName = item.FriendlyName,
                    owned = PlayerInventoryStore.OwnsAvatarItem(
                        accountId,
                        item.AvatarItemDesc,
                        item.AvatarItemId,
                        account.Player.PlayerExtra.AvatarItems)
                })
                .ToList();

            var consumables = PlayerInventoryStore.GetConsumableCatalog()
                .Where(item => Matches(
                    item.FriendlyName ?? string.Empty,
                    item.ConsumableItemDesc ?? string.Empty,
                    item.ConsumableItemId))
                .OrderBy(item => item.FriendlyName, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .Select(item => new
                {
                    consumableItemId = item.ConsumableItemId,
                    consumableItemDesc = item.ConsumableItemDesc,
                    friendlyName = item.FriendlyName,
                    quantity = PlayerInventoryStore.GetConsumableQuantity(
                        accountId,
                        item.ConsumableItemDesc,
                        item.ConsumableItemId)
                })
                .ToList();

            return Ok(new
            {
                accountId,
                avatarItems,
                consumables
            });
        }

        [HttpPost("admin/accounts/{accountId:long}/avatar-items")]
        public IActionResult AdminSetAvatarItemOwnership(
            long accountId,
            [FromBody] AdminAvatarItemOwnershipChange request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (IsDeveloper(account) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "Moderators cannot edit a Developer's inventory." });

            var catalogItem = PlayerInventoryStore.GetAvatarCatalog()
                .FirstOrDefault(item =>
                    (request.AvatarItemId > 0 &&
                     item.AvatarItemId == request.AvatarItemId) ||
                    (!string.IsNullOrWhiteSpace(request.AvatarItemDesc) &&
                     string.Equals(
                         item.AvatarItemDesc,
                         request.AvatarItemDesc.Trim(),
                         StringComparison.OrdinalIgnoreCase)));

            if (catalogItem == null)
                return BadRequest(new { error = "Avatar item was not found in AvatarItems.json." });

            bool success;
            if (request.Owned)
            {
                success = string.IsNullOrWhiteSpace(catalogItem.AvatarItemDesc)
                    ? true
                    : PlayerDB.GrantAvatarItem(
                        accountId,
                        catalogItem.AvatarItemDesc);
                if (success)
                {
                    account = PlayerDB.Players.FindById(accountId);
                    success = PlayerInventoryStore.SetAvatarItemOwned(
                        accountId,
                        catalogItem.AvatarItemDesc,
                        catalogItem.AvatarItemId,
                        catalogItem.FriendlyName,
                        owned: true,
                        legacyAvatarDescriptors:
                            account?.Player?.PlayerExtra?.AvatarItems);
                }
            }
            else
            {
                success = PlayerDB.RemoveAvatarItem(
                    accountId,
                    catalogItem.AvatarItemDesc,
                    catalogItem.AvatarItemId);
            }

            if (!success)
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "Avatar item ownership could not be saved." });

            bool owned = PlayerInventoryStore.OwnsAvatarItem(
                accountId,
                catalogItem.AvatarItemDesc,
                catalogItem.AvatarItemId,
                PlayerDB.Players.FindById(accountId)?
                    .Player?.PlayerExtra?.AvatarItems);

            Console.WriteLine(
                $"[ADMIN INVENTORY] admin={admin!.PlayerId} account={accountId} " +
                $"avatar={catalogItem.AvatarItemDesc} owned={owned}");

            return Ok(new
            {
                success = true,
                owned,
                avatarItemId = catalogItem.AvatarItemId,
                avatarItemDesc = catalogItem.AvatarItemDesc
            });
        }

        [HttpPost("admin/accounts/{accountId:long}/consumables")]
        public IActionResult AdminSetConsumableQuantity(
            long accountId,
            [FromBody] AdminConsumableQuantityChange request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (IsDeveloper(account) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "Moderators cannot edit a Developer's inventory." });
            if (request.Quantity is < 0 or > 100000)
                return BadRequest(new { error = "Consumable quantity must be between 0 and 100000." });

            var catalogItem = PlayerInventoryStore.GetConsumableCatalog()
                .FirstOrDefault(item =>
                    (request.ConsumableItemId > 0 &&
                     item.ConsumableItemId == request.ConsumableItemId) ||
                    (!string.IsNullOrWhiteSpace(request.ConsumableItemDesc) &&
                     string.Equals(
                         item.ConsumableItemDesc,
                         request.ConsumableItemDesc.Trim(),
                         StringComparison.OrdinalIgnoreCase)));

            if (catalogItem == null)
                return BadRequest(new { error = "Consumable was not found in Consumables.json." });

            int quantity = PlayerInventoryStore.SetConsumableQuantity(
                accountId,
                catalogItem.ConsumableItemDesc,
                catalogItem.ConsumableItemId,
                catalogItem.FriendlyName,
                request.Quantity);

            Console.WriteLine(
                $"[ADMIN INVENTORY] admin={admin!.PlayerId} account={accountId} " +
                $"consumable={catalogItem.ConsumableItemDesc} quantity={quantity}");

            return Ok(new
            {
                success = true,
                quantity,
                consumableItemId = catalogItem.ConsumableItemId,
                consumableItemDesc = catalogItem.ConsumableItemDesc
            });
        }

        [HttpPost("admin/accounts/{accountId:long}/balance")]
        public IActionResult AdminUpdateBalance(
            long accountId,
            [FromBody] AdminBalanceChange request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (IsDeveloper(account) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "Moderators cannot change a Developer's balance." });
            if (request.Amount < 0)
                return BadRequest(new { error = "Token amount cannot be negative." });

            int? balance = PlayerDB.SetCurrencyBalance(
                accountId,
                PlayerDBClasses.CurrencyType.RecCenterTokens,
                request.Amount,
                request.Add);

            return balance.HasValue
                ? Ok(new { success = true, balance = balance.Value })
                : StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "The token balance could not be saved." });
        }

        [HttpPost("admin/accounts/{accountId:long}/moderation-lock")]
        public IActionResult AdminApplyModerationLock(long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);
            var target = PlayerDB.Players.FindById(accountId);
            if (target?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (accountId == admin!.PlayerId)
                return BadRequest(new { error = "You cannot apply a Moderation Lock to yourself." });
            if (IsDeveloper(target) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Moderators cannot lock Developer accounts." });

            string groupId = Guid.NewGuid().ToString("N");
            PlayerDB.BanPlayer(accountId, int.MaxValue, "Moderation Lock", (ulong)admin.PlayerId);
            RecNetDB.ModerationLocks.Upsert(new RecNetDB.ModerationLock
            {
                AccountId = accountId,
                Reason = "Moderation Lock",
                IssuedByAccountId = admin.PlayerId,
                IssuedAt = DateTime.UtcNow,
                BanGroupId = groupId
            });
            return Ok(new { success = true, affectedCount = 1 });
        }

        [HttpPost("admin/accounts/{accountId:long}/ban")]
        public IActionResult AdminBanAccount(long accountId, [FromBody] AdminBanRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);
            var target = PlayerDB.Players.FindById(accountId);
            if (target?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (accountId == admin!.PlayerId)
                return BadRequest(new { error = "You cannot ban yourself." });
            if (IsDeveloper(target) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Moderators cannot ban Developer accounts." });

            string reason = request.Reason?.Trim() ?? string.Empty;
            if (reason.Length is < 3 or > 500)
                return BadRequest(new { error = "Ban reason must be 3-500 characters." });
            string durationUnit = request.DurationUnit?.Trim().ToLowerInvariant() ?? string.Empty;
            int durationSeconds;
            if (durationUnit == "permanent")
            {

                durationSeconds = int.MaxValue;
            }
            else
            {
                long multiplier = durationUnit switch
                {
                    "seconds" => 1,
                    "minutes" => 60,
                    "hours" => 60 * 60,
                    "days" => 24 * 60 * 60,
                    "weeks" => 7 * 24 * 60 * 60,
                    _ => 0
                };
                long total = (long)request.DurationAmount * multiplier;
                if (request.DurationAmount < 1 || multiplier == 0 || total > int.MaxValue)
                    return BadRequest(new { error = "Choose a valid ban duration within the 32-bit limit." });
                durationSeconds = (int)total;
            }
            var targets = new List<PlayerDBClasses.FullPlayer> { target };
            if (request.LinkBan)
            {
                var identities = (target.PlatformIds ?? new()).Select(x => (x.Platform, x.PlatformId)).ToHashSet();
                targets.AddRange(PlayerDB.Players.FindAll().Where(x => x.PlayerId != accountId &&
                    x.PlatformIds?.Any(p => identities.Contains((p.Platform, p.PlatformId))) == true));
            }
            if (!IsDeveloper(admin) && targets.Any(IsDeveloper))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "A linked Developer account cannot be banned by a Moderator." });

            string targetUsername = (target.Player.Username ?? accountId.ToString()).TrimStart('@');
            var distinctTargets = targets.DistinctBy(x => x.PlayerId).ToList();
            var linkedAccountSummaries = distinctTargets
                .Select(x => (x.PlayerId, (string?)(x.Player?.Username ?? x.PlayerId.ToString())))
                .ToList();
            var platformIdentities = (target.PlatformIds ?? new())
                .Select(x => (x.Platform.ToString(), x.PlatformId.ToString()))
                .ToList();

            foreach (var bannedAccount in distinctTargets)
            {
                string banReason = bannedAccount.PlayerId == accountId
                    ? reason
                    : $"{reason}\n\nRelated account: @{targetUsername}";
                PlayerDB.BanPlayer(bannedAccount.PlayerId, durationSeconds, banReason, (ulong)admin.PlayerId);
                RecNetDB.ModerationLocks.Delete(bannedAccount.PlayerId);

                DiscordLogger.LogBan(
                    bannedAccount.PlayerId,
                    bannedAccount.Player?.Username,
                    admin.PlayerId,
                    admin.Player?.Username,
                    banReason,
                    durationSeconds,
                    linkedAccounts: distinctTargets.Count > 1 ? linkedAccountSummaries : null,
                    platformIdentities: bannedAccount.PlayerId == accountId ? platformIdentities : null);
            }
            return Ok(new { success = true, linkBan = request.LinkBan, durationSeconds, affectedCount = distinctTargets.Count });
        }

        private const int TimeoutMaxDurationSeconds = 30 * 24 * 60 * 60;

        private static bool TryComputeDurationSeconds(
            string durationUnit,
            int durationAmount,
            out int durationSeconds)
        {
            long multiplier = durationUnit switch
            {
                "seconds" => 1,
                "minutes" => 60,
                "hours" => 60 * 60,
                "days" => 24 * 60 * 60,
                "weeks" => 7 * 24 * 60 * 60,
                _ => 0
            };
            long total = (long)durationAmount * multiplier;
            if (durationAmount < 1 || multiplier == 0 || total > int.MaxValue)
            {
                durationSeconds = 0;
                return false;
            }
            durationSeconds = (int)total;
            return true;
        }

        private static object ToAdminPlayerReport(ReportsDB.PlayerReport report) => new
        {
            id = report.Id,
            reporterId = report.ReporterId,
            reporterUsername = report.ReporterUsername,
            reportedPlayerId = report.ReportedPlayerId,
            reportedUsername = report.ReportedUsername,
            reportCategory = report.ReportCategory,
            details = report.Details,
            roomId = report.RoomId,
            roomInstanceType = report.RoomInstanceType,
            createdAt = report.CreatedAt,
            status = report.Status,
            resolvedByAccountId = report.ResolvedByAccountId,
            resolvedByUsername = report.ResolvedByUsername,
            resolvedAt = report.ResolvedAt,
            resolutionNote = report.ResolutionNote,
            actionDurationSeconds = report.ActionDurationSeconds
        };

        private static object ToAdminBugReport(ReportsDB.BugReport report) => new
        {
            id = report.Id,
            reporterId = report.ReporterId,
            reporterUsername = report.ReporterUsername,
            description = report.Description,
            category = report.Category,
            createdAt = report.CreatedAt,
            status = report.Status,
            resolvedByAccountId = report.ResolvedByAccountId,
            resolvedByUsername = report.ResolvedByUsername,
            resolvedAt = report.ResolvedAt
        };

        [HttpGet("admin/reports/players")]
        public IActionResult GetAdminPlayerReports(string? status)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            string filter = string.IsNullOrWhiteSpace(status) ? "Pending" : status.Trim();
            IEnumerable<ReportsDB.PlayerReport> reports = ReportsDB.PlayerReports.FindAll();
            if (!string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
                reports = reports.Where(report => report.Status == filter);

            return Ok(reports.OrderByDescending(report => report.CreatedAt).Take(200).Select(ToAdminPlayerReport));
        }

        [HttpPost("admin/reports/players/{reportId:guid}/resolve")]
        public IActionResult ResolveAdminPlayerReport(
            Guid reportId,
            [FromBody] AdminReportResolveRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var report = ReportsDB.PlayerReports.FindById(reportId);
            if (report == null)
                return NotFound(new { error = "Report not found." });
            if (report.Status != "Pending")
                return BadRequest(new { error = $"Report was already resolved as {report.Status}." });

            string action = request.Action?.Trim().ToLowerInvariant() ?? string.Empty;

            if (action == "noaction")
            {
                report.Status = "NoAction";
                report.ResolvedByAccountId = admin!.PlayerId;
                report.ResolvedByUsername = admin.Player?.Username;
                report.ResolvedAt = DateTime.UtcNow;
                report.ResolutionNote = request.Reason?.Trim();
                ReportsDB.PlayerReports.Update(report);
                return Ok(new { success = true, status = report.Status });
            }

            if (action != "ban" && action != "timeout")
                return BadRequest(new { error = "Action must be 'ban', 'timeout', or 'noaction'." });

            if (!report.ReportedPlayerId.HasValue)
                return BadRequest(new { error = "This report has no reported player to act on." });

            long accountId = report.ReportedPlayerId.Value;
            var target = PlayerDB.Players.FindById(accountId);
            if (target?.Player == null)
                return NotFound(new { error = "Reported account no longer exists." });
            if (accountId == admin!.PlayerId)
                return BadRequest(new { error = "You cannot ban yourself." });
            if (IsDeveloper(target) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Moderators cannot ban Developer accounts." });

            string reason = request.Reason?.Trim() ?? string.Empty;
            if (reason.Length is < 3 or > 500)
                return BadRequest(new { error = "Reason must be 3-500 characters." });

            int durationSeconds;
            string durationUnit = request.DurationUnit?.Trim().ToLowerInvariant() ?? string.Empty;

            if (action == "ban")
            {
                if (string.IsNullOrEmpty(durationUnit) || durationUnit == "permanent")
                {
                    durationSeconds = int.MaxValue;
                }
                else if (!TryComputeDurationSeconds(durationUnit, request.DurationAmount, out durationSeconds))
                {
                    return BadRequest(new { error = "Choose a valid ban duration within the 32-bit limit." });
                }
            }
            else
            {
                if (string.IsNullOrEmpty(durationUnit) || durationUnit == "permanent")
                    return BadRequest(new { error = "Timeouts require a duration - use Ban for a permanent action." });
                if (!TryComputeDurationSeconds(durationUnit, request.DurationAmount, out durationSeconds))
                    return BadRequest(new { error = "Choose a valid timeout duration within the 32-bit limit." });
                if (durationSeconds > TimeoutMaxDurationSeconds)
                    return BadRequest(new { error = "Timeouts are capped at 30 days - use Ban for longer durations." });
            }

            PlayerDB.BanPlayer(accountId, durationSeconds, reason, (ulong)admin.PlayerId);
            RecNetDB.ModerationLocks.Delete(accountId);

            DiscordLogger.LogBan(
                accountId,
                target.Player?.Username,
                admin.PlayerId,
                admin.Player?.Username,
                reason,
                durationSeconds,
                linkedAccounts: null,
                platformIdentities: null);

            report.Status = action == "ban" ? "Banned" : "TimedOut";
            report.ResolvedByAccountId = admin.PlayerId;
            report.ResolvedByUsername = admin.Player?.Username;
            report.ResolvedAt = DateTime.UtcNow;
            report.ResolutionNote = reason;
            report.ActionDurationSeconds = durationSeconds;
            ReportsDB.PlayerReports.Update(report);

            return Ok(new { success = true, status = report.Status, durationSeconds });
        }

        [HttpGet("admin/reports/bugs")]
        public IActionResult GetAdminBugReports(string? status)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            string filter = string.IsNullOrWhiteSpace(status) ? "Open" : status.Trim();
            IEnumerable<ReportsDB.BugReport> reports = ReportsDB.BugReports.FindAll();
            if (!string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
                reports = reports.Where(report => report.Status == filter);

            return Ok(reports.OrderByDescending(report => report.CreatedAt).Take(200).Select(ToAdminBugReport));
        }

        [HttpPost("admin/reports/bugs/{reportId:guid}/resolve")]
        public IActionResult ResolveAdminBugReport(
            Guid reportId,
            [FromBody] AdminBugReportResolveRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var report = ReportsDB.BugReports.FindById(reportId);
            if (report == null)
                return NotFound(new { error = "Bug report not found." });

            string status = request.Status?.Trim() ?? string.Empty;
            if (status != "Open" && status != "Closed")
                return BadRequest(new { error = "Status must be 'Open' or 'Closed'." });

            report.Status = status;
            if (status == "Closed")
            {
                report.ResolvedByAccountId = admin!.PlayerId;
                report.ResolvedByUsername = admin.Player?.Username;
                report.ResolvedAt = DateTime.UtcNow;
            }
            else
            {
                report.ResolvedByAccountId = null;
                report.ResolvedByUsername = null;
                report.ResolvedAt = null;
            }
            ReportsDB.BugReports.Update(report);

            return Ok(new { success = true, status = report.Status });
        }

        [HttpPost("admin/accounts/{accountId:long}/message")]
        public async Task<IActionResult> AdminMessageAccount(
            long accountId,
            [FromBody] AdminMessageRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsDeveloper(admin))
                return StatusCode(403, new { error = "Only Developers can message as Coach." });

            var target = PlayerDB.Players.FindById(accountId);
            if (target?.Player == null)
                return NotFound(new { error = "Account not found." });

            string body = (request.Message ?? string.Empty).Trim();
            if (body.Length is < 1 or > 2000)
                return BadRequest(new { error = "Message must be 1-2000 characters." });

            const long coachAccountId = 1;

            ChatDB.ChatThread? existingThread = ChatDB.FindThreadWithMembers(
                coachAccountId,
                new[] { accountId });
            ChatDB.ChatThread thread = existingThread ?? ChatDB.GetOrCreateThread(
                coachAccountId,
                new[] { accountId });

            ChatDB.ChatMessage? message = ChatDB.AddMessage(
                thread.ThreadId,
                coachAccountId,
                body);

            if (message == null)
                return BadRequest(new { error = "Message failed to send." });

            var messageDto = new
            {
                chatMessageId = message.MessageId,
                chatThreadId = message.ThreadId,
                senderPlayerId = (int)coachAccountId,
                contents = message.Body,
                createdAt = message.CreatedAt
            };

            _ = NotiController.NotifyChatMessageReceivedAsync(accountId, messageDto);

            Console.WriteLine(
                $"[ADMIN CHAT] admin={admin!.PlayerId} sentAsCoach=true " +
                $"to={accountId} threadId={thread.ThreadId}");

            return Ok(new { success = true, threadId = thread.ThreadId, messageId = message.MessageId });
        }

        [HttpDelete("admin/accounts/{accountId:long}/ban")]
        public IActionResult AdminUnbanAccount(long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);
            var target = PlayerDB.Players.FindById(accountId);
            if (target?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (IsDeveloper(target) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Moderators cannot unban Developer accounts." });
            PlayerDB.UnbanPlayer(accountId);
            RecNetDB.ModerationLocks.Delete(accountId);
            return Ok(new { success = true });
        }

        [HttpDelete("admin/accounts/{accountId:long}/moderation-lock")]
        public IActionResult AdminRemoveModerationLock(long accountId, [FromBody] AdminModerationUnlockRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);
            var existing = RecNetDB.ModerationLocks.FindById(accountId);
            if (existing == null)
                return NotFound(new { error = "This account does not have a Moderation Lock." });

            var locks = request.RemoveLinkedAccounts && !string.IsNullOrEmpty(existing.BanGroupId)
                ? RecNetDB.ModerationLocks.Find(x => x.BanGroupId == existing.BanGroupId).ToList()
                : new List<RecNetDB.ModerationLock> { existing };
            var lockedAccounts = locks.Select(x => PlayerDB.Players.FindById(x.AccountId)).Where(x => x != null).ToList();
            if (!IsDeveloper(admin) && lockedAccounts.Any(IsDeveloper))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "A linked Developer lock cannot be removed by a Moderator." });

            foreach (var moderationLock in locks)
            {
                PlayerDB.UnbanPlayer(moderationLock.AccountId);
                RecNetDB.ModerationLocks.Delete(moderationLock.AccountId);
            }
            return Ok(new { success = true, affectedCount = locks.Count });
        }

        [HttpGet("admin/system-status")]
        public IActionResult GetAdminSystemStatus()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var globalActive = ApiProtectionAttribute.GetGlobalActiveRequests();
            var isUnderAttack = globalActive > 500;

            return Ok(new
            {
                globalActiveRequests = globalActive,
                isUnderAttack,
                timestamp = DateTime.UtcNow
            });
        }

        [HttpGet("admin/clubs")]
        public IActionResult GetAdminClubs([FromQuery] string? search = null, [FromQuery] int skip = 0, [FromQuery] int count = 50)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var (results, total) = ClubDB.SearchWithTotal(null, search, 0, skip, Math.Clamp(count, 1, 100));
            return Ok(new { clubs = results.Select(ToAdminClubSummary).ToList(), total });
        }

        [HttpGet("admin/clubs/{clubId:long}")]
        public IActionResult GetAdminClub(long clubId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var club = ClubDB.Get(clubId);
            if (club == null)
                return NotFound(new { error = "Club not found." });

            return Ok(ToAdminClubDetails(club));
        }

        [HttpPut("admin/clubs/{clubId:long}")]
        public IActionResult UpdateAdminClub(long clubId, [FromBody] JsonElement request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var club = ClubDB.Get(clubId);
            if (club == null)
                return NotFound(new { error = "Club not found." });

            if (request.TryGetProperty("name", out JsonElement nameProp))
            {
                string name = nameProp.GetString()?.Trim() ?? string.Empty;
                if (name.Length is < 3 or > 50)
                    return BadRequest(new { error = "Club name must be 3-50 characters." });
                club.Name = name;
            }

            if (request.TryGetProperty("description", out JsonElement descProp))
            {
                string description = descProp.GetString()?.Trim() ?? string.Empty;
                if (description.Length > 1_000)
                    return BadRequest(new { error = "Description too long." });
                club.Description = description;
            }

            if (request.TryGetProperty("state", out JsonElement stateProp) && Enum.TryParse<ClubDBClasses.ClubState>(stateProp.GetString(), true, out var state))
            {
                club.State = state;
            }

            if (request.TryGetProperty("visibility", out JsonElement visProp) && Enum.TryParse<ClubDBClasses.ClubVisibility>(visProp.GetString(), true, out var visibility))
            {
                club.Visibility = visibility;
            }

            if (request.TryGetProperty("joinability", out JsonElement joinProp) && Enum.TryParse<ClubDBClasses.ClubJoinability>(joinProp.GetString(), true, out var joinability))
            {
                club.Joinability = joinability;
            }

            if (request.TryGetProperty("allowJuniors", out JsonElement juniorsProp))
            {
                club.AllowJuniors = juniorsProp.GetBoolean();
            }

            if (request.TryGetProperty("minLevel", out JsonElement levelProp))
            {
                club.MinLevel = Math.Clamp(levelProp.GetInt32(), 0, 50);
            }

            club.UpdatedAt = DateTime.UtcNow;
            if (!ClubDB.Update(club))
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Could not save club." });

            return Ok(ToAdminClubDetails(club));
        }

        [HttpGet("admin/clubs/{clubId:long}/members")]
        public IActionResult GetAdminClubMembers(long clubId, [FromQuery] bool includePending = false)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            if (ClubDB.Get(clubId) == null)
                return NotFound(new { error = "Club not found." });

            var memberships = ClubDB.GetMemberships(clubId, includePending);
            var memberIds = memberships.Select(m => m.AccountId).ToHashSet();
            var players = PlayerDB.Players.FindAll()
                .Where(p => memberIds.Contains(p.PlayerId))
                .ToDictionary(p => p.PlayerId);

            return Ok(memberships.Select(m => new
            {
                accountId = m.AccountId,
                username = players.TryGetValue(m.AccountId, out var p) ? p.Player?.Username : null,
                displayName = players.TryGetValue(m.AccountId, out p) ? p.Player?.DisplayName : null,
                membershipType = m.MembershipType.ToString(),
                createdAt = m.CreatedAt
            }).ToList());
        }

        [HttpPost("admin/clubs/{clubId:long}/members/{accountId:long}")]
        public IActionResult SetAdminClubMember(long clubId, long accountId, [FromBody] JsonElement request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            if (ClubDB.Get(clubId) == null)
                return NotFound(new { error = "Club not found." });

            if (!Enum.TryParse<ClubDBClasses.ClubMembershipType>(request.GetProperty("membershipType").GetString(), true, out var membershipType))
                return BadRequest(new { error = "Invalid membership type." });

            if (!ClubDB.SetMembership(clubId, accountId, membershipType))
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Could not set membership." });

            return Ok(new { success = true });
        }

        [HttpDelete("admin/clubs/{clubId:long}/members/{accountId:long}")]
        public IActionResult RemoveAdminClubMember(long clubId, long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            if (!ClubDB.RemoveMembership(clubId, accountId))
                return BadRequest(new { error = "Could not remove member (creator cannot be removed)." });

            return Ok(new { success = true });
        }

        [HttpGet("admin/accounts/{accountId:long}/relationships")]
        public IActionResult GetAdminAccountRelationships(long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var relationships = RelationshipDB.GetRelationships(accountId);
            var playerIds = relationships.Select(r => r.PlayerID).ToHashSet();
            var players = PlayerDB.Players.FindAll()
                .Where(p => playerIds.Contains(p.PlayerId))
                .ToDictionary(p => p.PlayerId);

            return Ok(relationships.Select(r => new
            {
                playerId = r.PlayerID,
                username = players.TryGetValue(r.PlayerID, out var p) ? p.Player?.Username : null,
                displayName = players.TryGetValue(r.PlayerID, out p) ? p.Player?.DisplayName : null,
                relationshipType = r.RelationshipType.ToString(),
                favorited = r.Favorited,
                muted = r.Muted,
                ignored = r.Ignored
            }).ToList());
        }

        [HttpPost("admin/accounts/{accountId:long}/relationships/friend")]
        public IActionResult AdminForceAddFriend(
            long accountId,
            [FromBody] AdminFriendChange request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);
            if (request.TargetAccountId <= 0 || request.TargetAccountId == accountId)
                return BadRequest(new { error = "Choose a different, valid account ID." });

            var account = PlayerDB.Players.FindById(accountId);
            var target = PlayerDB.Players.FindById(request.TargetAccountId);
            if (account?.Player == null || target?.Player == null)
                return NotFound(new { error = "Account not found." });

            RelationshipDB.AddFriend(accountId, request.TargetAccountId);
            Console.WriteLine(
                $"[ADMIN RELATIONSHIP] admin={admin!.PlayerId} force-added friend " +
                $"{request.TargetAccountId} for account={accountId}");
            return Ok(new { success = true });
        }

        [HttpDelete("admin/accounts/{accountId:long}/relationships/friend/{targetAccountId:long}")]
        public IActionResult AdminRemoveFriend(long accountId, long targetAccountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            bool removed = RelationshipDB.RemoveFriend(accountId, targetAccountId);
            return removed
                ? Ok(new { success = true })
                : NotFound(new { error = "That friendship does not exist." });
        }

        [HttpGet("admin/accounts/{accountId:long}/gifts")]
        public IActionResult GetAdminAccountGifts(long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });

            var senderIds = PlayerDB.GetPendingGiftPackages(accountId)
                .Select(gift => (long)gift.FromPlayerId)
                .Distinct()
                .ToList();
            var senders = PlayerDB.Players.FindAll()
                .Where(p => senderIds.Contains(p.PlayerId))
                .ToDictionary(p => p.PlayerId);

            return Ok(PlayerDB.GetPendingGiftPackages(accountId).Select(gift => new
            {
                giftPackageId = gift.GiftPackageId,
                fromPlayerId = gift.FromPlayerId,
                fromDisplayName = senders.TryGetValue(gift.FromPlayerId, out var sender)
                    ? (sender.Player?.DisplayName ?? sender.Player?.Username)
                    : (gift.FromPlayerId == 1 ? "Coach" : $"Player {gift.FromPlayerId}"),
                message = gift.Message,
                friendlyName = gift.FriendlyName,
                tooltip = gift.Tooltip,
                thumbnailImage = gift.ThumbnailImage,
                avatarItemDesc = gift.AvatarItemDesc,
                consumableItemDesc = gift.ConsumableItemDesc,
                consumableQuantity = gift.ConsumableQuantity,
                equipmentPrefabName = gift.EquipmentPrefabName,
                currency = gift.Currency,
                xp = gift.XP,
                giftContext = gift.GiftContext,
                rarity = gift.Rarity
            }).ToList());
        }

        [HttpDelete("admin/accounts/{accountId:long}/gifts/{giftPackageId:long}")]
        public IActionResult DeleteAdminAccountGift(long accountId, long giftPackageId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            return PlayerDB.RemoveGiftPackage(accountId, giftPackageId)
                ? Ok(new { success = true })
                : NotFound(new { error = "That pending gift no longer exists." });
        }

        [HttpDelete("admin/accounts/{accountId:long}/gifts")]
        public IActionResult ClearAdminAccountGifts(long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            int removed = PlayerDB.ClearPendingGiftPackages(accountId);
            return Ok(new { success = true, removedBoxes = removed });
        }

        [HttpGet("admin/age-verification")]
        public IActionResult GetAdminAgeVerificationQueue()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var queue = AgeVerificationDB.GetForReview();
            var accountIds = queue.Select(value => value.AccountId).ToHashSet();
            var accounts = PlayerDB.Players.FindAll()
                .Where(value => accountIds.Contains(value.PlayerId))
                .ToDictionary(value => value.PlayerId);

            return Ok(queue.Select(request =>
            {
                accounts.TryGetValue(request.AccountId, out var account);
                return new
                {
                    code = request.Code,
                    accountId = request.AccountId,
                    username = account?.Player?.Username,
                    displayName = account?.Player?.DisplayName ?? account?.Player?.Username ?? $"Player {request.AccountId}",
                    method = request.Method,
                    submittedAt = request.SubmittedAt
                };
            }).ToList());
        }

        [HttpPost("admin/age-verification/{code}/approve")]
        public IActionResult ApproveAgeVerification(string code)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var request = AgeVerificationDB.GetByCode(code);
            if (request == null || request.Status != "UnderReview")
                return NotFound(new { error = "That request is not awaiting review." });

            var account = PlayerDB.Players.FindById(request.AccountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });

            if (!AgeVerificationDB.Review(code, approve: true, admin!.PlayerId, reason: null))
                return StatusCode(500, new { error = "Could not save the review." });

            account.Player.IsAgeVerified = true;
            account.Player.AgeVerifiedAt = DateTime.UtcNow;
            account.Player.IsJunior = false;
            PlayerDB.Players.Update(account);

            Console.WriteLine(
                $"[AGE VERIFICATION] admin={admin.PlayerId} approved code={code} " +
                $"account={request.AccountId} method={request.Method}");
            return Ok(new { success = true });
        }

        [HttpPost("admin/age-verification/{code}/reject")]
        public IActionResult RejectAgeVerification(
            string code,
            [FromBody] AdminAgeVerificationRejection? request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var record = AgeVerificationDB.GetByCode(code);
            if (record == null || record.Status != "UnderReview")
                return NotFound(new { error = "That request is not awaiting review." });

            if (!AgeVerificationDB.Review(code, approve: false, admin!.PlayerId, request?.Reason))
                return StatusCode(500, new { error = "Could not save the review." });

            Console.WriteLine(
                $"[AGE VERIFICATION] admin={admin.PlayerId} rejected code={code} " +
                $"account={record.AccountId}");
            return Ok(new { success = true });
        }

        public class AdminAgeVerificationRejection
        {
            public string? Reason { get; set; }
        }

        [HttpGet("admin/accounts/{accountId:long}/chat/threads")]
        public IActionResult GetAdminAccountChatThreads(long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });

            var threads = ChatDB.GetThreadsForPlayer(accountId, 50);
            var memberIds = threads.SelectMany(t => t.MemberIds ?? new List<long>()).ToHashSet();
            var members = PlayerDB.Players.FindAll()
                .Where(p => memberIds.Contains(p.PlayerId))
                .ToDictionary(p => p.PlayerId);

            return Ok(threads.Select(thread =>
            {
                var last = ChatDB.GetLastMessage(thread.ThreadId);
                return new
                {
                    threadId = thread.ThreadId,
                    name = thread.Name,
                    updatedAt = thread.UpdatedAt,
                    members = (thread.MemberIds ?? new List<long>()).Select(id => new
                    {
                        accountId = id,
                        displayName = members.TryGetValue(id, out var member)
                            ? (member.Player?.DisplayName ?? member.Player?.Username ?? $"Player {id}")
                            : $"Player {id}"
                    }),
                    lastMessage = last == null ? null : new
                    {
                        body = last.Body,
                        senderAccountId = last.SenderAccountId,
                        createdAt = last.CreatedAt
                    }
                };
            }));
        }

        [HttpGet("admin/accounts/{accountId:long}/chat/threads/{threadId:long}/messages")]
        public IActionResult GetAdminAccountChatMessages(
            long accountId,
            long threadId,
            [FromQuery] int take = 100)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var messages = ChatDB.GetMessages(threadId, accountId, take);
            var senderIds = messages.Select(m => m.SenderAccountId).Distinct().ToList();
            var senders = PlayerDB.Players.FindAll()
                .Where(p => senderIds.Contains(p.PlayerId))
                .ToDictionary(p => p.PlayerId);

            return Ok(messages.Select(message => new
            {
                messageId = message.MessageId,
                senderAccountId = message.SenderAccountId,
                senderDisplayName = message.SenderAccountId < 0
                    ? "System"
                    : senders.TryGetValue(message.SenderAccountId, out var sender)
                        ? (sender.Player?.DisplayName ?? sender.Player?.Username ?? $"Player {message.SenderAccountId}")
                        : $"Player {message.SenderAccountId}",
                body = message.Body,
                createdAt = message.CreatedAt
            }));
        }

        public class AdminFriendChange
        {
            public long TargetAccountId { get; set; }
        }

        [HttpGet("admin/accounts/{accountId:long}/clubs")]
        public IActionResult GetAdminAccountClubs(long accountId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsStaff(admin))
                return StatusCode(403);

            var clubs = ClubDB.GetMemberClubs(accountId);
            return Ok(clubs.Select(ToAdminClubSummary).ToList());
        }

        private static object ToAdminClubSummary(ClubDBClasses.ClubRecord club)
        {
            return new
            {
                clubId = club.ClubId,
                name = club.Name,
                description = club.Description,
                mainImageName = club.MainImageName,
                memberCount = club.MemberCount,
                primaryCategory = club.PrimaryCategory,
                categoryTags = club.CategoryTags,
                visibility = club.Visibility.ToString(),
                joinability = club.Joinability.ToString(),
                allowJuniors = club.AllowJuniors,
                minLevel = club.MinLevel,
                clubType = club.ClubType.ToString(),
                state = club.State.ToString(),
                creatorAccountId = club.CreatorAccountId,
                createdAt = club.CreatedAt,
                updatedAt = club.UpdatedAt
            };
        }

        private static object ToAdminClubDetails(ClubDBClasses.ClubRecord club)
        {
            var creator = PlayerDB.Players.FindById(club.CreatorAccountId);
            return new
            {
                summary = ToAdminClubSummary(club),
                creator = creator != null ? new
                {
                    accountId = creator.PlayerId,
                    username = creator.Player?.Username,
                    displayName = creator.Player?.DisplayName
                } : null
            };
        }

        [HttpDelete("admin/accounts/{accountId:long}")]
        public IActionResult AdminDeleteAccount(long accountId, [FromBody] AdminAccountDeletion request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);
            if (accountId == admin!.PlayerId)
                return BadRequest(new { error = "You cannot delete your own account from the Admin panel." });

            var account = PlayerDB.Players.FindById(accountId);
            if (account?.Player == null)
                return NotFound(new { error = "Account not found." });
            if (IsDeveloper(account) && !IsDeveloper(admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Moderators cannot delete Developer accounts." });
            if (!string.Equals(request.Confirmation?.Trim(), $"DELETE {accountId}", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = $"Type DELETE {accountId} to confirm." });

            RecNetDB.PhotoCheers.DeleteMany(x => x.AccountId == accountId);
            RecNetDB.PhotoComments.DeleteMany(x => x.AccountId == accountId);
            RecNetDB.ModerationLocks.Delete(accountId);
            RoomDB.DeletePlayerDorms(accountId);
            PlayerDB.Players.Delete(accountId);
            return Ok(new { success = true });
        }

        private static bool TryValidateRegistration(
            RecNetRegistration request,
            out string username,
            out PlayerDBClasses.Platforms platform,
            out ulong platformId,
            out string error,
            out int status)
        {
            username = request.Username?.Trim().TrimStart('@') ?? string.Empty;
            platform = default;
            platformId = 0;
            error = string.Empty;
            status = StatusCodes.Status400BadRequest;

            if (username.Length is < 3 or > 20 || username.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_'))
                error = "Username must be 3-20 letters, numbers, or underscores.";
            else if (string.IsNullOrEmpty(request.Password) ||
                     request.Password.Length < PasswordSecurity.MinPasswordLength ||
                     request.Password.Length > PasswordSecurity.MaxPasswordLength)
                error = "Password must be at least 8 characters.";
            else if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
                error = "Passwords do not match.";
            else if (!Enum.TryParse(request.Platform, true, out platform) ||
                     platform is PlayerDBClasses.Platforms.All or PlayerDBClasses.Platforms.HeadlessBot)
                error = "Choose a valid platform.";
            else if (!ulong.TryParse(request.PlatformId, out platformId) || platformId == 0)
                error = "Enter a valid numeric platform ID.";
            else if (PlayerDB.Players.FindAll().Any(x =>
                         string.Equals(x.Player?.Username, request.Username?.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase)))
            {
                error = "That username is already taken.";
                status = StatusCodes.Status409Conflict;
            }

            return string.IsNullOrEmpty(error);
        }

        private static PlayerDBClasses.FullPlayer CreateRegisteredAccount(
            string username,
            string password,
            PlayerDBClasses.Platforms platform,
            ulong platformId)
        {
            var account = PlayerDB.CreateAccount(platform, platformId, false);
            account.Player!.Username = username;
            account.Player.DisplayName = username;
            account.Password = PasswordSecurity.Hash(password);
            PlayerDB.Players.Update(account);
            return account;
        }

        private static object BuildDeveloperSnapshot()
        {
            DeveloperTelemetry.TelemetrySnapshot telemetry =
                DeveloperTelemetry.Snapshot(160);
            var accounts = PlayerDB.Players.FindAll()
                .Where(value => value.Player != null)
                .ToList();

            var onlinePlayers = accounts
                .Select(account => new
                {
                    Account = account,
                    Heartbeat = PlayerDB.GetPlayerHeartbeat(account.PlayerId)
                })
                .Where(value => value.Heartbeat.isOnline)
                .OrderBy(value => value.Account.Player!.DisplayName)
                .Select(value => new
                {
                    accountId = value.Account.PlayerId,
                    username = value.Account.Player!.Username,
                    displayName = value.Account.Player.DisplayName,
                    profileImage = ImageUrl(value.Account.Player.ProfileImage),
                    device = value.Heartbeat.deviceClass?.ToString() ?? "Unknown",
                    room = value.Heartbeat.roomInstance?.Name ??
                        value.Heartbeat.roomInstance?.location ??
                        "Online",
                    roomId = value.Heartbeat.roomInstance?.roomId,
                    roomInstanceId =
                        value.Heartbeat.roomInstance?.roomInstanceId,
                    lastHeartbeat = value.Heartbeat.lastHeartbeatUnixTime > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(
                            value.Heartbeat.lastHeartbeatUnixTime)
                        : (DateTimeOffset?)null,
                    sockets = NotiController.GetPlayerSocketCount(
                        value.Account.PlayerId)
                })
                .ToList();

            using Process process = Process.GetCurrentProcess();
            process.Refresh();

            return new
            {
                generatedAt = DateTimeOffset.UtcNow,
                server = new
                {
                    status = "online",
                    uptimeSeconds = Math.Max(
                        0,
                        (long)(DateTime.UtcNow -
                            process.StartTime.ToUniversalTime()).TotalSeconds),
                    cpuPercent = SampleDeveloperCpu(process),
                    memoryBytes = process.WorkingSet64,
                    managedMemoryBytes = GC.GetTotalMemory(false),
                    threads = process.Threads.Count,
                    processorCount = Environment.ProcessorCount
                },
                totals = new
                {
                    requests = telemetry.TotalRequests,
                    inboundBytes = telemetry.TotalInboundBytes,
                    outboundBytes = telemetry.TotalOutboundBytes,
                    errors = telemetry.TotalErrors,
                    registeredPlayers = accounts.Count,
                    onlinePlayers = onlinePlayers.Count,
                    connectedSockets = NotiController.ConnectedSocketCount,
                    rooms = RoomDB.Rooms.Count(),
                    chatThreads = ChatDB.DeveloperThreadCount,
                    chatMessages = ChatDB.DeveloperMessageCount
                },
                onlinePlayers,
                requests = telemetry.Requests.Select(value => new
                {
                    at = value.At,
                    method = value.Method,
                    path = value.Path,
                    status = value.StatusCode,
                    durationMs = value.ElapsedMilliseconds,
                    inboundBytes = value.InboundBytes,
                    outboundBytes = value.OutboundBytes
                }).ToArray(),
                series = telemetry.Points.Select(value => new
                {
                    at = DateTimeOffset.FromUnixTimeSeconds(
                        value.UnixSecond),
                    requests = value.Requests,
                    inboundBytes = value.InboundBytes,
                    outboundBytes = value.OutboundBytes,
                    errors = value.Errors,
                    latencyMs = Math.Round(
                        value.AverageLatencyMilliseconds,
                        2)
                }).ToArray(),
                chats = BuildDeveloperChatItems(40, null)
            };
        }

        private static object BuildDeveloperChats(
            int take,
            long? beforeMessageId)
        {
            take = Math.Clamp(take, 1, 250);
            object[] messages = BuildDeveloperChatItems(
                take,
                beforeMessageId);
            long oldestMessageId = messages
                .Select(value =>
                {
                    JsonElement json = JsonSerializer.SerializeToElement(value);
                    return json.GetProperty("messageId").GetInt64();
                })
                .DefaultIfEmpty(0)
                .Min();

            return new
            {
                messages,
                oldestMessageId,
                hasMore = messages.Length == take
            };
        }

        private static object[] BuildDeveloperChatItems(
            int take,
            long? beforeMessageId)
        {
            List<ChatDB.ChatMessage> messages =
                ChatDB.GetMessagesForDeveloper(take, beforeMessageId);
            Dictionary<long, ChatDB.ChatThread> threads =
                ChatDB.GetThreadsForDeveloper(
                    messages.Select(value => value.ThreadId));
            Dictionary<long, PlayerDBClasses.FullPlayer> players =
                PlayerDB.Players.FindAll()
                    .Where(value => value.Player != null)
                    .ToDictionary(value => value.PlayerId);

            return messages.Select(message =>
            {
                threads.TryGetValue(message.ThreadId, out var thread);
                players.TryGetValue(message.SenderAccountId, out var sender);

                return (object)new
                {
                    messageId = message.MessageId,
                    threadId = message.ThreadId,
                    threadName = string.IsNullOrWhiteSpace(thread?.Name)
                        ? $"Thread {message.ThreadId}"
                        : thread.Name,
                    sender = new
                    {
                        accountId = message.SenderAccountId,
                        username = sender?.Player?.Username,
                        displayName = message.SenderAccountId < 0
                            ? "System"
                            : sender?.Player?.DisplayName ??
                              sender?.Player?.Username ??
                              $"Player {message.SenderAccountId}"
                    },
                    members = (thread?.MemberIds ?? new List<long>())
                        .Select(accountId =>
                        {
                            players.TryGetValue(accountId, out var member);
                            return new
                            {
                                accountId,
                                displayName =
                                    member?.Player?.DisplayName ??
                                    member?.Player?.Username ??
                                    $"Player {accountId}"
                            };
                        })
                        .ToArray(),
                    body = GetDeveloperChatBody(message.Body),
                    createdAt = message.CreatedAt
                };
            }).ToArray();
        }

        private static string GetDeveloperChatBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty(
                        "Data",
                        out JsonElement data) &&
                    data.ValueKind == JsonValueKind.String)
                    return data.GetString() ?? body;
            }
            catch (JsonException)
            {

            }

            return body;
        }

        private static double SampleDeveloperCpu(Process process)
        {
            lock (DeveloperCpuSync)
            {
                DateTime now = DateTime.UtcNow;
                TimeSpan cpu = process.TotalProcessorTime;
                double elapsedMilliseconds =
                    (now - DeveloperCpuSampleAt).TotalMilliseconds;

                if (elapsedMilliseconds >= 250)
                {
                    double cpuMilliseconds =
                        (cpu - DeveloperCpuTime).TotalMilliseconds;
                    DeveloperCpuPercent = Math.Clamp(
                        cpuMilliseconds /
                        (elapsedMilliseconds * Environment.ProcessorCount) *
                        100,
                        0,
                        100);
                    DeveloperCpuSampleAt = now;
                    DeveloperCpuTime = cpu;
                }

                return Math.Round(DeveloperCpuPercent, 2);
            }
        }

        private static object ToAdminSteamBlacklistEntry(
            SteamAccessDB.SteamBlacklistEntry entry)
        {
            var addedBy =
                PlayerDB.Players.FindById(entry.AddedByAccountId);

            return new
            {
                steamId = entry.SteamId.ToString(),
                entry.Reason,
                entry.AddedByAccountId,
                addedByUsername = addedBy?.Player?.Username,
                addedByDisplayName = addedBy?.Player?.DisplayName,
                addedAt = entry.AddedAt.ToUniversalTime().ToString("O")
            };
        }

        private static bool TryValidateCommunityBoard(
            JsonElement board,
            out string error)
        {
            error = string.Empty;
            if (board.ValueKind != JsonValueKind.Object)
            {
                error = "The community board must be a JSON object.";
                return false;
            }

            if (TryGetPropertyIgnoreCase(board, "FeaturedPlayer", out JsonElement featured) &&
                featured.ValueKind == JsonValueKind.Object)
            {
                if (!TryGetPropertyIgnoreCase(featured, "Id", out JsonElement featuredId) ||
                    !featuredId.TryGetInt64(out long accountId) || accountId < 1 ||
                    PlayerDB.Players.FindById(accountId)?.Player == null)
                {
                    error = "Choose an existing account for the featured creator.";
                    return false;
                }
                if (!TryValidateBoardString(featured, "TitleOverride", 120, out error) ||
                    !TryValidateBoardUrl(featured, "UrlOverride", false, out error))
                    return false;
            }

            if (TryGetPropertyIgnoreCase(board, "FeaturedRoomGroup", out JsonElement roomGroup) &&
                roomGroup.ValueKind == JsonValueKind.Object)
            {
                foreach (string property in new[] { "FeaturedRooms", "Rooms" })
                {
                    if (!TryGetPropertyIgnoreCase(roomGroup, property, out JsonElement rooms))
                        continue;
                    if (rooms.ValueKind != JsonValueKind.Array || rooms.GetArrayLength() > 10)
                    {
                        error = "The board can contain at most 10 pinned rooms.";
                        return false;
                    }

                    foreach (JsonElement room in rooms.EnumerateArray())
                    {
                        if (room.ValueKind != JsonValueKind.Object ||
                            !TryGetPropertyIgnoreCase(room, "RoomId", out JsonElement roomId) ||
                            !roomId.TryGetInt64(out long id) || id < 1 ||
                            RoomDB.Rooms.FindById(id) == null)
                        {
                            error = "Every pinned room must use an existing room ID.";
                            return false;
                        }

                        if (!TryValidateBoardString(room, "RoomName", 50, out error) ||
                            !TryValidateBoardString(room, "ImageName", 2_048, out error))
                            return false;
                    }
                }
            }

            if (!TryValidateBoardArray(board, "InstagramImages", 12, out JsonElement images, out error))
                return false;
            foreach (JsonElement image in images.EnumerateArray())
            {
                if (image.ValueKind != JsonValueKind.Object ||
                    !TryValidateBoardString(image, "ImageName", 300, out error) ||
                    !TryValidateBoardUrl(image, "ImageUrl", true, out error))
                    return false;
            }

            if (!TryValidateBoardArray(board, "Videos", 3, out JsonElement videos, out error))
                return false;
            foreach (JsonElement video in videos.EnumerateArray())
            {
                if (video.ValueKind != JsonValueKind.Object ||
                    !TryValidateBoardString(video, "BlobName", 300, out error) ||
                    !TryValidateBoardString(video, "Title", 120, out error) ||
                    !TryValidateBoardString(video, "Description", 1_000, out error) ||
                    !TryValidateBoardUrl(video, "ThumbnailBlobName", true, out error) ||
                    !TryValidateBoardUrl(video, "SourceUrl", true, out error))
                    return false;
            }

            if (TryGetPropertyIgnoreCase(board, "CurrentAnnouncement", out JsonElement announcement) &&
                announcement.ValueKind == JsonValueKind.Object &&
                (!TryValidateBoardString(announcement, "Message", 500, out error) ||
                 !TryValidateBoardUrl(announcement, "MoreInfoUrl", false, out error)))
                return false;

            return true;
        }

        private static bool TryValidateBoardArray(
            JsonElement parent,
            string property,
            int maximum,
            out JsonElement array,
            out string error)
        {
            error = string.Empty;
            if (!TryGetPropertyIgnoreCase(parent, property, out array))
            {
                using JsonDocument empty = JsonDocument.Parse("[]");
                array = empty.RootElement.Clone();
                return true;
            }
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > maximum)
            {
                error = $"{property} can contain at most {maximum} items.";
                return false;
            }
            return true;
        }

        private static bool TryValidateBoardString(
            JsonElement parent,
            string property,
            int maximum,
            out string error)
        {
            error = string.Empty;
            if (!TryGetPropertyIgnoreCase(parent, property, out JsonElement value))
                return true;
            if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                error = $"{property} must be text.";
                return false;
            }
            string text = value.GetString() ?? string.Empty;
            if (text.Length > maximum || text.Any(character => character == '\0'))
            {
                error = $"{property} is too long or contains invalid characters.";
                return false;
            }
            return true;
        }

        private static bool TryValidateBoardUrl(
            JsonElement parent,
            string property,
            bool required,
            out string error)
        {
            error = string.Empty;
            if (!TryGetPropertyIgnoreCase(parent, property, out JsonElement value) ||
                value.ValueKind == JsonValueKind.Null)
            {
                if (!required)
                    return true;
                error = $"{property} is required.";
                return false;
            }
            if (value.ValueKind != JsonValueKind.String)
            {
                error = $"{property} must be a URL.";
                return false;
            }

            string url = value.GetString()?.Trim() ?? string.Empty;
            if (url.Length == 0)
            {
                if (!required)
                    return true;
                error = $"{property} is required.";
                return false;
            }
            if (url.Length > 2_048)
            {
                error = $"{property} is too long.";
                return false;
            }

            bool safeLocal = url.StartsWith("/imageserver/", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("/recnet/", StringComparison.OrdinalIgnoreCase);
            bool safeRemote = Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) &&
                parsed.Scheme is "http" or "https";
            if (!safeLocal && !safeRemote)
            {
                error = $"{property} must use http, https, or a local site path.";
                return false;
            }
            return true;
        }

        private static bool TryGetPropertyIgnoreCase(
            JsonElement parent,
            string property,
            out JsonElement value)
        {
            if (parent.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty candidate in parent.EnumerateObject())
                {
                    if (string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase))
                    {
                        value = candidate.Value;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        private static bool IsAdmin(PlayerDBClasses.FullPlayer? account) =>
            IsDeveloper(account);

        private static bool IsModerator(PlayerDBClasses.FullPlayer? account) =>
            account?.PlayerRoles?.Contains(PlayerDBClasses.PlayerRoles.Moderator) == true;

        private static bool IsStaff(PlayerDBClasses.FullPlayer? account) =>
            IsDeveloper(account) || IsModerator(account);

        private static object ToAdminRoomSummary(RoomDBClasses.Room room)
        {
            var creator = PlayerDB.Players.FindById(room.CreatorAccountId);
            long activeAfter =
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 120;
            int onlinePlayers = PlayerDB.Players.FindAll()
                .Select(player => player.Player?.PlayerExtra?.Heartbeat)
                .Count(heartbeat =>
                    heartbeat?.isOnline == true &&
                    heartbeat.lastHeartbeatUnixTime >= activeAfter &&
                    heartbeat.roomInstance?.roomId == room.RoomId);

            return new
            {
                roomId = room.RoomId,
                name = room.Name,
                description = room.Description,
                image = ImageUrl(room.ImageName),
                imageName = room.ImageName,
                creatorAccountId = room.CreatorAccountId,
                creatorName = creator?.Player?.DisplayName ??
                    creator?.Player?.Username ??
                    $"Player {room.CreatorAccountId}",
                accessibility = room.Accessibility.ToString(),
                state = room.State?.ToString() ?? RoomDBClasses.RoomState.Active.ToString(),
                isDorm = room.IsDorm,
                isBaseRoom = room.IsBaseRoom || RoomDB.IsCanonicalBaseRoom(room),
                isRRO = room.IsRRO,
                creativeToolsBetaEnabled = room.CreativeToolsBetaEnabled,
                supportsBetaContent = room.CreativeToolsBetaEnabled,
                isDeveloperOwned = room.IsDeveloperOwned,
                maxPlayers = room.MaxPlayers,
                minLevel = room.MinLevel,
                subRoomCount = room.SubRooms?.Count ?? 0,
                roleCount = room.Roles?.Count ?? 0,
                banCount = RoomDB.GetActiveBans(room.RoomId).Count,
                onlinePlayers,
                createdAt = room.CreatedAt,
                version = Math.Max(1, room.UgcVersion),
                stats = new
                {
                    cheers = room.Stats?.CheerCount ?? 0,
                    favorites = room.Stats?.FavoriteCount ?? 0,
                    visitors = room.Stats?.VisitorCount ?? 0,
                    visits = room.Stats?.VisitCount ?? 0
                }
            };
        }

        private static object ToAdminRoomDetails(RoomDBClasses.Room room)
        {
            var playerMap = PlayerDB.Players.FindAll()
                .Where(account => account.Player != null)
                .ToDictionary(account => account.PlayerId);

            static bool AdminBlobExists(string? blobName)
            {
                if (string.IsNullOrWhiteSpace(blobName))
                    return false;

                string safeName = Path.GetFileName(blobName);
                if (!string.Equals(safeName, blobName, StringComparison.Ordinal))
                    return false;

                return System.IO.File.Exists(Path.Combine(
                    Program.dataDir,
                    "CDN",
                    "room",
                    safeName));
            }

            object PlayerIdentity(long accountId)
            {
                playerMap.TryGetValue(
                    accountId,
                    out PlayerDBClasses.FullPlayer? account);
                return new
                {
                    accountId,
                    username = account?.Player?.Username,
                    displayName = account?.Player?.DisplayName ??
                        account?.Player?.Username ??
                        $"Player {accountId}",
                    profileImage = ImageUrl(
                        account?.Player?.ProfileImage ??
                        "DefaultPFP.png")
                };
            }

            return new
            {
                summary = ToAdminRoomSummary(room),
                roomId = room.RoomId,
                name = room.Name,
                description = room.Description ?? string.Empty,
                imageName = room.ImageName ?? string.Empty,
                creatorAccountId = room.CreatorAccountId,
                creator = PlayerIdentity(room.CreatorAccountId),
                accessibility = room.Accessibility.ToString(),
                state = room.State?.ToString() ??
                    RoomDBClasses.RoomState.Active.ToString(),
                isDorm = room.IsDorm,
                isBaseRoom = room.IsBaseRoom || RoomDB.IsCanonicalBaseRoom(room),
                creativeToolsBetaEnabled = room.CreativeToolsBetaEnabled,
                supportsBetaContent = room.CreativeToolsBetaEnabled,
                isBeta = room.CreativeToolsBetaEnabled,
                maxPlayers = room.MaxPlayers,
                minLevel = room.MinLevel,
                persistenceVersion = room.PersistenceVersion,
                version = Math.Max(1, room.UgcVersion),
                tags = room.Tags?
                    .Select(tag => tag.Tag)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(tag => tag)
                    .ToArray() ?? Array.Empty<string>(),
                flags = new
                {
                    room.CloningAllowed,
                    room.DisableMicAutoMute,
                    room.DisableRoomComments,
                    room.EncryptVoiceChat,
                    room.ToxmodEnabled,
                    room.LoadScreenLocked,
                    room.AutoLocalizeRoom,
                    room.IsDeveloperOwned,
                    room.SupportsLevelVoting,
                    room.IsRRO,
                    room.SupportsScreens,
                    room.SupportsWalkVR,
                    room.SupportsTeleportVR,
                    room.SupportsVRLow,
                    room.SupportsQuest2,
                    room.SupportsMobile,
                    room.SupportsJuniors,
                    room.IsBaseRoom,
                    room.CreativeToolsBetaEnabled
                },
                stats = new
                {
                    cheers = room.Stats?.CheerCount ?? 0,
                    favorites = room.Stats?.FavoriteCount ?? 0,
                    visitors = room.Stats?.VisitorCount ?? 0,
                    visits = room.Stats?.VisitCount ?? 0
                },
                roles = (room.Roles ?? new List<RoomDBClasses.Roles>())
                    .OrderByDescending(role => role.Role)
                    .ThenBy(role => role.AccountId)
                    .Select(role => new
                    {
                        role.AccountId,
                        player = PlayerIdentity(role.AccountId),
                        role = role.Role.ToString(),
                        invitedRole = role.InvitedRole.ToString()
                    })
                    .ToList(),
                subRooms = (room.SubRooms ?? new List<RoomDBClasses.SubRooms>())
                    .OrderBy(subRoom =>
                        string.Equals(
                            subRoom.Name,
                            "Home",
                            StringComparison.OrdinalIgnoreCase)
                            ? 0
                            : 1)
                    .ThenBy(subRoom => subRoom.SubRoomId)
                    .Select(subRoom =>
                    {
                        var currentSave = subRoom.SubRoomDataSaveId > 0
                            ? RoomDB.SubRoomDataSaves.FindById(
                                subRoom.SubRoomDataSaveId)
                            : null;
                        currentSave ??= RoomDBClasses.FindNewestSubRoomSave(
                            RoomDB.SubRoomDataSaves,
                            room.RoomId,
                            subRoom.SubRoomId);

                        string roomBlob = currentSave?.DataBlob ??
                            subRoom.DataBlob ??
                            string.Empty;
                        bool isBakedSave = !string.IsNullOrWhiteSpace(
                            currentSave?.UnityAssetId);
                        string metadataBlob = currentSave?.RoomDataBlob ??
                            room.DataBlob ??
                            (isBakedSave ? roomBlob : string.Empty);
                        int bakedAssetCount = currentSave?.BakedUnityAssets?.Count ?? 0;

                        return new
                        {
                            subRoomId = subRoom.SubRoomId,
                            name = subRoom.Name,
                            maxPlayers = subRoom.MaxPlayers,
                            accessibility = subRoom.Accessibility.ToString(),
                            isSandbox = subRoom.IsSandbox,
                            unitySceneId = subRoom.UnitySceneId,
                            currentSaveId = currentSave?.SubRoomDataSaveId ??
                                subRoom.SubRoomDataSaveId,
                            hasData = !string.IsNullOrWhiteSpace(roomBlob) &&
                                (isBakedSave || !string.IsNullOrWhiteSpace(metadataBlob)),
                            roomBlob,
                            metadataBlob,
                            roomBlobExists = AdminBlobExists(roomBlob),
                            metadataBlobExists = isBakedSave
                                ? AdminBlobExists(roomBlob)
                                : AdminBlobExists(metadataBlob),
                            unityAssetId = currentSave?.UnityAssetId,
                            bakedAssetCount,
                            bakedTargets = currentSave?.BakedUnityAssets?
                                .Select(asset => asset.Target)
                                .Distinct()
                                .OrderBy(target => target)
                                .ToArray() ?? Array.Empty<int>(),
                            permissionCount = subRoom.Permissions?.Count ?? 0
                        };
                    })
                    .ToList(),
                bans = RoomDB.GetActiveBans(room.RoomId)
                    .OrderByDescending(ban => ban.BannedAt)
                    .Select(ban => new
                    {
                        ban.RoomBanId,
                        ban.AccountId,
                        player = PlayerIdentity(ban.AccountId),
                        ban.BannedByAccountId,
                        ban.Reason,
                        ban.BannedAt
                    })
                    .ToList()
            };
        }

        private static void LogAdminRoomAction(
            PlayerDBClasses.FullPlayer admin,
            RoomDBClasses.Room room,
            string action)
        {
            string actor = admin.Player?.Username ??
                admin.Player?.DisplayName ??
                admin.PlayerId.ToString();
            Console.WriteLine(
                $"[ADMIN ROOM] admin={admin.PlayerId} room={room.RoomId} action={action}");
            DiscordLogger.Log(
                $"🛠️ **Admin Room** — `@{actor}` (`{admin.PlayerId}`) {action} " +
                $"in `^{room.Name}` (`{room.RoomId}`)");
        }

        private static object ToAdminShopPayload(APIController.StorefrontAdminInfo shop) =>
            new
            {
                rotationNonce = shop.RotationNonce,
                nextRefresh = shop.NextRefresh,
                customItems = shop.CustomItems.Select(ToAdminShopItemPayload).ToList()
            };

        private static object ToAdminShopItemPayload(APIController.StorefrontAdminItem item) =>
            new
            {
                skuId = item.SkuId,
                avatarItemId = item.AvatarItemId,
                friendlyName = item.FriendlyName,
                avatarItemDesc = item.AvatarItemDesc,
                consumableItemDesc = item.ConsumableItemDesc,
                equipmentPrefabName = item.EquipmentPrefabName,
                equipmentModificationGuid = item.EquipmentModificationGuid,
                thumbnailImage = item.ThumbnailImage,
                rarity = item.Rarity,
                price = item.Price
            };

        private static bool IsDeveloper(PlayerDBClasses.FullPlayer? account) =>
            account?.PlayerRoles?.Contains(PlayerDBClasses.PlayerRoles.Developer) == true;

        [HttpPost("auth/logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("recnet_session", new CookieOptions
            {
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/recnet"
            });
            return Ok(new { success = true });
        }

        [HttpGet("auth/me")]
        public IActionResult Me()
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            return account?.Player == null ? Unauthorized() : Ok(ToSession(account));
        }

        [HttpGet("account/settings")]
        public IActionResult GetSettings()
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account?.Player == null)
                return Unauthorized(new { error = "Log in to view settings." });

            return Ok(new
            {
                accountId = account.PlayerId,
                username = account.Player.Username,
                displayName = account.Player.DisplayName,
                bio = account.Player.Bio,
                email = account.Player.Email,
                profileImage = account.Player.ProfileImage,
                bannerImage = account.Player.BannerImage,
                availableUsernameChanges = account.Player.AvailableUsernameChanges
            });
        }

        [HttpGet("account/cheer-badge")]
        public IActionResult GetCheerBadgeSettings()
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account?.Player == null)
                return Unauthorized(new { error = "Log in to choose a profile badge." });

            if (account.PlayerRoles.Contains(PlayerDBClasses.PlayerRoles.Developer) &&
                PlayerDB.GrantDeveloperCheerAccess(
                    account,
                    selectDeveloperBadge: false))
            {
                PlayerDB.Players.Update(account);
            }

            return Ok(ToCheerBadgePayload(account));
        }

        [HttpPut("account/cheer-badge")]
        public IActionResult UpdateCheerBadge(
            [FromBody] CheerBadgeSelection request)
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account?.Player == null)
                return Unauthorized(new { error = "Log in to choose a profile badge." });
            if (!Enum.IsDefined(typeof(PlayerDBClasses.CheerCategory), request.Badge))
                return BadRequest(new { error = "Choose a valid profile badge." });

            bool isDeveloper = account.PlayerRoles.Contains(
                PlayerDBClasses.PlayerRoles.Developer);
            var badge = (PlayerDBClasses.CheerCategory)request.Badge;
            if (badge == PlayerDBClasses.CheerCategory.RecRoomDeveloper &&
                !isDeveloper)
            {
                return StatusCode(403);
            }

            if (isDeveloper)
                PlayerDB.GrantDeveloperCheerAccess(account, selectDeveloperBadge: false);

            account.Player.Reputation ??= new PlayerDBClasses.Reputation();
            int earnedCheers = badge switch
            {
                PlayerDBClasses.CheerCategory.General =>
                    account.Player.Reputation.CheerGeneral,
                PlayerDBClasses.CheerCategory.Helpful =>
                    account.Player.Reputation.CheerHelpful,
                PlayerDBClasses.CheerCategory.Sportmanship =>
                    account.Player.Reputation.CheerSportsman,
                PlayerDBClasses.CheerCategory.GreatHost =>
                    account.Player.Reputation.CheerGreatHost,
                PlayerDBClasses.CheerCategory.Creative =>
                    account.Player.Reputation.CheerCreative,
                PlayerDBClasses.CheerCategory.RecRoomDeveloper when isDeveloper => 1,
                _ => 0
            };
            if (earnedCheers <= 0)
                return BadRequest(new { error = "You have not unlocked that badge yet." });

            account.Player.Reputation.IsCheerful = true;
            account.Player.Reputation.SelectedCheer = badge;
            PlayerDB.Players.Update(account);
            return Ok(ToCheerBadgePayload(account));
        }

        [HttpPost("account/profile-image")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> UploadProfileImage()
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account?.Player == null)
                return Unauthorized(new { error = "Log in to upload a profile picture." });
            if (!Request.HasFormContentType)
                return BadRequest(new { error = "Choose an image file to upload." });

            var form = await Request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "Choose an image file to upload." });
            if (file.Length > 10 * 1024 * 1024)
                return BadRequest(new { error = "Profile pictures must be 10 MB or smaller." });

            byte[] bytes;
            using (var buffer = new MemoryStream())
            {
                await file.CopyToAsync(buffer);
                bytes = buffer.ToArray();
            }

            SixLabors.ImageSharp.Formats.IImageFormat? format;
            SixLabors.ImageSharp.ImageInfo? info;
            try
            {
                format = SixLabors.ImageSharp.Image.DetectFormat(bytes);
                info = SixLabors.ImageSharp.Image.Identify(bytes);
            }
            catch
            {
                return BadRequest(new { error = "That file is not a valid image." });
            }

            if (format == null || info == null || info.Width > 4096 || info.Height > 4096 ||
                (long)info.Width * info.Height > 16_777_216)
                return BadRequest(new { error = "Images cannot be larger than 4096 x 4096 pixels." });

            string extension = format.FileExtensions.FirstOrDefault()?.ToLowerInvariant() ?? "png";
            if (extension is not ("png" or "jpg" or "jpeg" or "webp" or "gif" or "bmp"))
                return BadRequest(new { error = "Use a PNG, JPG, WebP, GIF, or BMP image." });

            string folder = Path.Combine(Program.dataDir, "Images", "CustomPFPS");
            Directory.CreateDirectory(folder);
            string fileName = $"profile_{account.PlayerId}_{Guid.NewGuid():N}.{extension}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), bytes);
            string relativePath = $"CustomPFPS/{fileName}";
            account.Player.ProfileImage = relativePath;
            PlayerDB.Players.Update(account);
            await NotiController.NotifyPlayerProfileUpdatedAsync(account.PlayerId);

            return Ok(new
            {
                success = true,
                path = relativePath,
                url = ImageUrl(relativePath),
                session = ToSession(account)
            });
        }

        [HttpPost("account/banner-image")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> UploadBannerImage()
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account?.Player == null)
                return Unauthorized(new { error = "Log in to upload a banner." });
            if (!Request.HasFormContentType)
                return BadRequest(new { error = "Choose an image file to upload." });

            var form = await Request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "Choose an image file to upload." });
            if (file.Length > 10 * 1024 * 1024)
                return BadRequest(new { error = "Banners must be 10 MB or smaller." });

            byte[] bytes;
            using (var buffer = new MemoryStream())
            {
                await file.CopyToAsync(buffer);
                bytes = buffer.ToArray();
            }

            SixLabors.ImageSharp.Formats.IImageFormat? format;
            SixLabors.ImageSharp.ImageInfo? info;
            try
            {
                format = SixLabors.ImageSharp.Image.DetectFormat(bytes);
                info = SixLabors.ImageSharp.Image.Identify(bytes);
            }
            catch
            {
                return BadRequest(new { error = "That file is not a valid image." });
            }

            if (format == null || info == null || info.Width > 8192 || info.Height > 4096 ||
                (long)info.Width * info.Height > 16_777_216)
                return BadRequest(new { error = "Banners cannot be larger than 8192 x 4096 pixels." });

            string extension = format.FileExtensions.FirstOrDefault()?.ToLowerInvariant() ?? "png";
            if (extension is not ("png" or "jpg" or "jpeg" or "webp" or "gif" or "bmp"))
                return BadRequest(new { error = "Use a PNG, JPG, WebP, GIF, or BMP image." });

            string folder = Path.Combine(Program.dataDir, "Images", "CustomPFPS");
            Directory.CreateDirectory(folder);
            string fileName = $"banner_{account.PlayerId}_{Guid.NewGuid():N}.{extension}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), bytes);
            string relativePath = $"CustomPFPS/{fileName}";
            account.Player.BannerImage = relativePath;
            PlayerDB.Players.Update(account);
            await NotiController.NotifyPlayerProfileUpdatedAsync(account.PlayerId);

            return Ok(new
            {
                success = true,
                path = relativePath,
                url = ImageUrl(relativePath)
            });
        }

        [HttpPut("account/settings")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> UpdateSettings([FromBody] RecNetSettings request)
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account?.Player == null)
                return Unauthorized(new { error = "Log in to change settings." });

            string displayName = request.DisplayName?.Trim() ?? string.Empty;
            string username = request.Username?.Trim().TrimStart('@') ?? string.Empty;
            string bio = request.Bio?.Trim() ?? string.Empty;
            string email = request.Email?.Trim() ?? string.Empty;

            if (displayName.Length is < 1 or > 32 || displayName.Any(char.IsControl))
                return BadRequest(new { error = "Display name must be between 1 and 32 characters." });
            if (bio.Length > 500 || bio.Any(ch => ch == '\0'))
                return BadRequest(new { error = "Bio must be 500 characters or fewer." });
            if (!string.IsNullOrEmpty(email))
            {
                bool validEmail = false;
                if (email.Length <= 254)
                {
                    try
                    {
                        validEmail = string.Equals(
                            new System.Net.Mail.MailAddress(email).Address,
                            email,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    catch (FormatException) { }
                }

                if (!validEmail)
                    return BadRequest(new { error = "Enter a valid email address." });
            }

            bool usernameChanged = !string.Equals(account.Player.Username, username, StringComparison.OrdinalIgnoreCase);
            if (usernameChanged)
            {
                if (username.Length is < 3 or > 20 || username.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_'))
                    return BadRequest(new { error = "Username must be 3-20 letters, numbers, or underscores." });
                if (account.Player.AvailableUsernameChanges <= 0)
                    return BadRequest(new { error = "You have no username changes remaining." });
                bool inUse = PlayerDB.Players.FindAll().Any(x => x.PlayerId != account.PlayerId &&
                    string.Equals(x.Player?.Username, username, StringComparison.OrdinalIgnoreCase));
                if (inUse)
                    return Conflict(new { error = "That username is already taken." });
                account.Player.AvailableUsernameChanges--;
            }

            account.Player.DisplayName = displayName;
            account.Player.Username = username;
            account.Player.Bio = bio;
            account.Player.Email = email;
            account.Player.ProfileImage = NormalizeImagePath(request.ProfileImage) ?? "DefaultPFP.png";
            account.Player.BannerImage = string.IsNullOrWhiteSpace(request.BannerImage)
                ? null
                : NormalizeImagePath(request.BannerImage);
            PlayerDB.Players.Update(account);
            await NotiController.NotifyPlayerProfileUpdatedAsync(account.PlayerId);
            return Ok(new { success = true, session = ToSession(account) });
        }

        [HttpPut("account/password")]
        [RequestSizeLimit(16 * 1024)]
        public IActionResult ChangePassword([FromBody] RecNetPasswordChange request)
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account == null)
                return Unauthorized(new { error = "Log in to change your password." });
            if (!PasswordSecurity.Verify(request.CurrentPassword, account.Password, out _))
                return Unauthorized(new { error = "Your current password is incorrect." });
            if (string.IsNullOrEmpty(request.NewPassword) ||
                request.NewPassword.Length < PasswordSecurity.MinPasswordLength ||
                request.NewPassword.Length > PasswordSecurity.MaxPasswordLength)
                return BadRequest(new { error = "New password must be at least 8 characters." });

            account.Password = PasswordSecurity.Hash(request.NewPassword);
            PlayerDB.Players.Update(account);
            return Ok(new { success = true });
        }

        [HttpDelete("account")]
        [RequestSizeLimit(16 * 1024)]
        public IActionResult DeleteAccount([FromBody] RecNetDeleteAccount request)
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account == null)
                return Unauthorized(new { error = "Log in to delete your account." });
            if (!string.Equals(request.Confirmation, "DELETE", StringComparison.Ordinal))
                return BadRequest(new { error = "Type DELETE exactly to confirm." });
            if (!PasswordSecurity.Verify(request.Password, account.Password, out _))
                return Unauthorized(new { error = "Your password is incorrect." });

            RecNetDB.PhotoCheers.DeleteMany(x => x.AccountId == account.PlayerId);
            RecNetDB.PhotoComments.DeleteMany(x => x.AccountId == account.PlayerId);
            RecNetDB.ModerationLocks.Delete(account.PlayerId);
            RecNetDB.ChallengeProgresses.DeleteMany(x => x.AccountId == account.PlayerId);
            RoomDB.DeletePlayerDorms(account.PlayerId);
            bool deleted = PlayerDB.Players.Delete(account.PlayerId);
            if (!deleted)
                return StatusCode(500, new { error = "The account could not be deleted." });

            Response.Cookies.Delete("recnet_session", new CookieOptions
            {
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/recnet"
            });
            return Ok(new { success = true });
        }

        [HttpGet("rooms")]
        public IActionResult GetRooms([FromQuery] string? search = null)
        {
            string term = search?.Trim() ?? string.Empty;
            var rooms = RoomDB.Rooms.FindAll()
                .Where(IsPublishedRecNetRoom)
                .Where(x => string.IsNullOrEmpty(term) ||
                    (x.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.Tags?.Any(t => t.Tag.Contains(term, StringComparison.OrdinalIgnoreCase)) ?? false))
                .OrderByDescending(x => x.Stats?.VisitCount ?? 0)
                .ThenBy(x => x.Name)
                .Take(250)
                .Select(ToRoomSummary)
                .ToList();
            return Ok(rooms);
        }

        [HttpGet("rooms/{roomId:long}")]
        public IActionResult GetRoom(long roomId)
        {
            var room = RoomDB.Rooms.FindById(roomId);
            return room == null || !IsPublishedRecNetRoom(room)
                ? NotFound(new { error = "Room not found." })
                : Ok(ToRoomSummary(room));
        }

        [HttpGet("announcements")]
        public IActionResult GetAnnouncements()
        {
            var announcements = RecNetDB.Announcements.FindAll()
                .Where(x => x.Published)
                .OrderByDescending(x => x.Pinned)
                .ThenByDescending(x => x.UpdatedAt)
                .Take(25)
                .Select(ToAnnouncementSummary)
                .ToList();
            return Ok(announcements);
        }

        [HttpGet("admin/announcements")]
        public IActionResult GetAdminAnnouncements()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            return Ok(RecNetDB.Announcements.FindAll()
                .OrderByDescending(x => x.Pinned)
                .ThenByDescending(x => x.UpdatedAt)
                .Select(ToAnnouncementSummary)
                .ToList());
        }

        [HttpPost("admin/announcements")]
        public IActionResult CreateAnnouncement(
            [FromBody] AdminAnnouncementRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            if (!TryValidateAnnouncement(
                    request,
                    out string title,
                    out string bodyMarkdown,
                    out string kind,
                    out string error))
                return BadRequest(new { error });

            DateTime now = DateTime.UtcNow;
            var announcement = new RecNetDB.Announcement
            {
                Title = title,
                BodyMarkdown = bodyMarkdown,
                Kind = kind,
                Pinned = request.Pinned,
                Published = request.Published,
                CreatedByAccountId = admin!.PlayerId,
                CreatedAt = now,
                UpdatedAt = now
            };
            RecNetDB.Announcements.Insert(announcement);

            return Ok(new
            {
                success = true,
                announcement = ToAnnouncementSummary(announcement)
            });
        }

        [HttpPut("admin/announcements/{announcementId:long}")]
        public IActionResult UpdateAnnouncement(
            long announcementId,
            [FromBody] AdminAnnouncementRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            var announcement = RecNetDB.Announcements.FindById(announcementId);
            if (announcement == null)
                return NotFound(new { error = "Announcement not found." });

            if (!TryValidateAnnouncement(
                    request,
                    out string title,
                    out string bodyMarkdown,
                    out string kind,
                    out string error))
                return BadRequest(new { error });

            announcement.Title = title;
            announcement.BodyMarkdown = bodyMarkdown;
            announcement.Kind = kind;
            announcement.Pinned = request.Pinned;
            announcement.Published = request.Published;
            announcement.UpdatedAt = DateTime.UtcNow;
            RecNetDB.Announcements.Update(announcement);

            return Ok(new
            {
                success = true,
                announcement = ToAnnouncementSummary(announcement)
            });
        }

        [HttpDelete("admin/announcements/{announcementId:long}")]
        public IActionResult DeleteAnnouncement(long announcementId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            return RecNetDB.Announcements.Delete(announcementId)
                ? Ok(new { success = true })
                : NotFound(new { error = "Announcement not found." });
        }

        private static bool TryValidateAnnouncement(
            AdminAnnouncementRequest request,
            out string title,
            out string bodyMarkdown,
            out string kind,
            out string error)
        {
            title = request.Title?.Trim() ?? string.Empty;
            bodyMarkdown = request.BodyMarkdown?.Trim() ?? string.Empty;
            kind = (request.Kind?.Trim().ToLowerInvariant()) switch
            {
                "update" => "update",
                "warning" => "warning",
                "maintenance" => "maintenance",
                _ => "info"
            };
            error = string.Empty;

            if (title.Length is < 1 or > 100)
            {
                error = "Title must be 1-100 characters.";
                return false;
            }
            if (bodyMarkdown.Length is < 1 or > 12000)
            {
                error = "Announcement body must be 1-12,000 characters.";
                return false;
            }

            return true;
        }

        private static object ToAnnouncementSummary(
            RecNetDB.Announcement announcement)
        {
            var author = PlayerDB.Players.FindById(
                announcement.CreatedByAccountId);
            string authorName = author?.Player?.DisplayName ??
                author?.Player?.Username ??
                $"Account #{announcement.CreatedByAccountId}";

            return new
            {
                id = announcement.Id,
                title = announcement.Title,
                bodyMarkdown = announcement.BodyMarkdown,
                kind = announcement.Kind,
                pinned = announcement.Pinned,
                published = announcement.Published,
                createdByAccountId = announcement.CreatedByAccountId,
                authorName,
                createdAt = announcement.CreatedAt,
                updatedAt = announcement.UpdatedAt
            };
        }

        [HttpGet("events")]
        public IActionResult GetEvents()
        {
            var now = DateTime.UtcNow;
            var events = RecNetDB.Events.FindAll()
                .Where(x => x.EndsAt == null || x.EndsAt >= now)
                .OrderByDescending(x => x.Pinned)
                .ThenBy(x => x.StartsAt)
                .Select(ToEventSummary)
                .ToList();
            return Ok(events);
        }

        [HttpGet("admin/events")]
        public IActionResult GetAdminEvents()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            var events = RecNetDB.Events.FindAll()
                .OrderByDescending(x => x.Pinned)
                .ThenByDescending(x => x.StartsAt)
                .Select(ToEventSummary)
                .ToList();
            return Ok(events);
        }

        [HttpPost("admin/events/image")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> UploadEventImage()
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);
            if (!Request.HasFormContentType)
                return BadRequest(new { error = "Choose an image file to upload." });

            var form = await Request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "Choose an image file to upload." });
            if (file.Length > 10 * 1024 * 1024)
                return BadRequest(new { error = "Event images must be 10 MB or smaller." });

            byte[] bytes;
            using (var buffer = new MemoryStream())
            {
                await file.CopyToAsync(buffer, HttpContext.RequestAborted);
                bytes = buffer.ToArray();
            }

            SixLabors.ImageSharp.Formats.IImageFormat? format;
            SixLabors.ImageSharp.ImageInfo? info;
            try
            {
                format = SixLabors.ImageSharp.Image.DetectFormat(bytes);
                info = SixLabors.ImageSharp.Image.Identify(bytes);
            }
            catch
            {
                return BadRequest(new { error = "That file is not a valid image." });
            }

            if (format == null || info == null || info.Width <= 0 || info.Height <= 0 ||
                info.Width > 8192 || info.Height > 4096 ||
                (long)info.Width * info.Height > 20_000_000)
            {
                return BadRequest(new
                {
                    error = "Event images cannot exceed 8192 x 4096 or 20 megapixels."
                });
            }

            string extension = format.FileExtensions.FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;
            if (extension is not ("png" or "jpg" or "jpeg" or "webp" or "gif" or "bmp"))
                return BadRequest(new { error = "Use a PNG, JPG, WebP, GIF, or BMP image." });

            string folder = Path.Combine(Program.dataDir, "Images", "EventImages");
            Directory.CreateDirectory(folder);
            string fileName = $"event_{admin!.PlayerId}_{Guid.NewGuid():N}.{extension}";
            await System.IO.File.WriteAllBytesAsync(
                Path.Combine(folder, fileName),
                bytes,
                HttpContext.RequestAborted);
            string relativePath = $"EventImages/{fileName}";

            return Ok(new
            {
                success = true,
                path = relativePath,
                url = ImageUrl(relativePath)
            });
        }

        [HttpPost("admin/events")]
        public IActionResult CreateEvent([FromBody] AdminEventRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            if (!TryValidateEvent(request, out string title, out string description,
                    out DateTime startsAt, out DateTime? endsAt, out string? imagePath, out string error))
                return BadRequest(new { error });

            var evt = new RecNetDB.Event
            {
                Title = title,
                Description = description,
                ImageName = imagePath,
                StartsAt = startsAt,
                EndsAt = endsAt,
                Pinned = request.Pinned,
                CreatedByAccountId = admin!.PlayerId,
                CreatedAt = DateTime.UtcNow
            };
            RecNetDB.Events.Insert(evt);
            return Ok(new { success = true, @event = ToEventSummary(evt) });
        }

        [HttpPut("admin/events/{eventId:long}")]
        public IActionResult UpdateEvent(long eventId, [FromBody] AdminEventRequest request)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            var evt = RecNetDB.Events.FindById(eventId);
            if (evt == null)
                return NotFound(new { error = "Event not found." });

            if (!TryValidateEvent(request, out string title, out string description,
                    out DateTime startsAt, out DateTime? endsAt, out string? imagePath, out string error))
                return BadRequest(new { error });

            string? previousImage = evt.ImageName;
            evt.Title = title;
            evt.Description = description;
            evt.ImageName = imagePath;
            evt.StartsAt = startsAt;
            evt.EndsAt = endsAt;
            evt.Pinned = request.Pinned;
            RecNetDB.Events.Update(evt);
            if (!string.Equals(previousImage, imagePath, StringComparison.OrdinalIgnoreCase))
                DeleteUnusedEventImage(previousImage);
            return Ok(new { success = true, @event = ToEventSummary(evt) });
        }

        [HttpDelete("admin/events/{eventId:long}")]
        public IActionResult DeleteEvent(long eventId)
        {
            var admin = AuthStuff.GetCurrentPlayer(Request);
            if (!IsAdmin(admin))
                return StatusCode(403);

            var evt = RecNetDB.Events.FindById(eventId);
            if (evt == null || !RecNetDB.Events.Delete(eventId))
                return NotFound(new { error = "Event not found." });
            DeleteUnusedEventImage(evt.ImageName);
            return Ok(new { success = true });
        }

        private static bool TryValidateEvent(
            AdminEventRequest request,
            out string title,
            out string description,
            out DateTime startsAt,
            out DateTime? endsAt,
            out string? imagePath,
            out string error)
        {
            title = request.Title?.Trim() ?? string.Empty;
            description = request.Description?.Trim() ?? string.Empty;
            startsAt = default;
            endsAt = null;
            imagePath = NormalizeImagePath(request.ImageName);
            error = string.Empty;

            if (title.Length is < 1 or > 80)
            {
                error = "Title must be 1-80 characters.";
                return false;
            }
            if (description.Length > 1000)
            {
                error = "Description must be 1000 characters or fewer.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(request.ImageName) &&
                !IsStoredImage(imagePath))
            {
                error = "Upload a valid event image first.";
                return false;
            }
            if (request.StartsAt == default)
            {
                error = "Choose a start date and time.";
                return false;
            }

            startsAt = request.StartsAt.ToUniversalTime();
            if (request.EndsAt.HasValue && request.EndsAt.Value != default)
            {
                DateTime end = request.EndsAt.Value.ToUniversalTime();
                if (end <= startsAt)
                {
                    error = "End time must be after the start time.";
                    return false;
                }
                endsAt = end;
            }

            return true;
        }

        private static bool IsStoredImage(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                return false;

            if (!imagePath.StartsWith("EventImages/", StringComparison.OrdinalIgnoreCase))
                return false;

            string root = Path.GetFullPath(Path.Combine(Program.dataDir, "Images", "EventImages"));
            string fullPath = Path.GetFullPath(Path.Combine(
                Program.dataDir,
                "Images",
                imagePath.Replace('/', Path.DirectorySeparatorChar)));
            return fullPath.StartsWith(
                    root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) &&
                System.IO.File.Exists(fullPath) &&
                ImageExtensions.Contains(Path.GetExtension(fullPath));
        }

        private static void DeleteUnusedEventImage(string? imagePath)
        {
            string? normalized = NormalizeImagePath(imagePath);
            if (normalized == null ||
                !normalized.StartsWith("EventImages/", StringComparison.OrdinalIgnoreCase) ||
                RecNetDB.Events.Exists(evt => evt.ImageName == normalized))
                return;

            string root = Path.GetFullPath(Path.Combine(Program.dataDir, "Images", "EventImages"));
            string fullPath = Path.GetFullPath(Path.Combine(
                Program.dataDir,
                "Images",
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (fullPath.StartsWith(
                    root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) &&
                System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }

        private static object ToEventSummary(RecNetDB.Event evt) => new
        {
            id = evt.Id,
            title = evt.Title,
            description = evt.Description,
            image = string.IsNullOrWhiteSpace(evt.ImageName) ? null : ImageUrl(evt.ImageName),
            startsAt = evt.StartsAt,
            endsAt = evt.EndsAt,
            pinned = evt.Pinned,
            createdAt = evt.CreatedAt
        };

        public class AdminAnnouncementRequest
        {
            public string? Title { get; set; }
            public string? BodyMarkdown { get; set; }
            public string? Kind { get; set; }
            public bool Pinned { get; set; }
            public bool Published { get; set; } = true;
        }

        public class AdminEventRequest
        {
            public string? Title { get; set; }
            public string? Description { get; set; }
            public string? ImageName { get; set; }
            public DateTime StartsAt { get; set; }
            public DateTime? EndsAt { get; set; }
            public bool Pinned { get; set; }
        }

        private static bool IsPublishedRecNetRoom(RoomDBClasses.Room room)
        {
            return !room.IsDorm &&
                room.State == RoomDBClasses.RoomState.Active &&
                room.Accessibility == RoomDBClasses.RoomAccessibility.Public;
        }

        private static object ToRoomSummary(RoomDBClasses.Room room)
        {
            var creator = PlayerDB.Players.FindById(room.CreatorAccountId);
            return new
            {
                roomId = room.RoomId,
                name = room.Name,
                description = room.Description,
                image = ImageUrl(room.ImageName),
                creatorAccountId = room.CreatorAccountId,
                creatorName = creator?.Player?.DisplayName ?? creator?.Player?.Username ?? $"Player {room.CreatorAccountId}",
                maxPlayers = room.MaxPlayers,
                accessibility = room.Accessibility.ToString(),
                state = room.State?.ToString(),
                isDorm = room.IsDorm,
                isRRO = room.IsRRO,
                supportsJuniors = room.SupportsJuniors,
                createdAt = room.CreatedAt,
                tags = room.Tags?.Select(x => x.Tag).ToArray() ?? Array.Empty<string>(),
                subRooms = room.SubRooms?.Select(x => (object)new { x.SubRoomId, x.Name, x.MaxPlayers }).ToArray() ?? Array.Empty<object>(),
                stats = new
                {
                    cheers = room.Stats?.CheerCount ?? 0,
                    favorites = room.Stats?.FavoriteCount ?? 0,
                    visitors = room.Stats?.VisitorCount ?? 0,
                    visits = room.Stats?.VisitCount ?? 0
                }
            };
        }

        private static object ToSession(PlayerDBClasses.FullPlayer account) => new
        {
            accountId = account.PlayerId, username = account.Player!.Username,
            displayName = account.Player.DisplayName, profileImage = ImageUrl(account.Player.ProfileImage),
            isAdmin = IsAdmin(account),
            isDeveloper = IsDeveloper(account),
            isModerator = IsModerator(account)
        };

        private static object ToCheerBadgePayload(
            PlayerDBClasses.FullPlayer account)
        {
            account.Player!.Reputation ??= new PlayerDBClasses.Reputation();
            var reputation = account.Player.Reputation;
            bool isDeveloper = account.PlayerRoles.Contains(
                PlayerDBClasses.PlayerRoles.Developer);
            var badges = new List<object>
            {
                new { value = 0, name = "General", count = reputation.CheerGeneral, unlocked = reputation.CheerGeneral > 0 },
                new { value = 40, name = "Creative", count = reputation.CheerCreative, unlocked = reputation.CheerCreative > 0 },
                new { value = 30, name = "Great Host", count = reputation.CheerGreatHost, unlocked = reputation.CheerGreatHost > 0 },
                new { value = 20, name = "Sportsmanship", count = reputation.CheerSportsman, unlocked = reputation.CheerSportsman > 0 },
                new { value = 10, name = "Helpful", count = reputation.CheerHelpful, unlocked = reputation.CheerHelpful > 0 }
            };
            if (isDeveloper)
            {
                badges.Add(new
                {
                    value = 9000,
                    name = "Rec Room Developer",
                    count = 1,
                    unlocked = true
                });
            }

            return new
            {
                selectedBadge = (int)reputation.SelectedCheer,
                isDeveloper,
                badges
            };
        }

        [HttpGet("users")]
        public IActionResult GetUsers([FromQuery] string? search = null)
        {
            string term = search?.Trim() ?? string.Empty;
            var users = PlayerDB.Players.FindAll()
                .Where(x => x.Player != null)
                .Where(x => string.IsNullOrEmpty(term) ||
                    (x.Player!.Username?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.Player.DisplayName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                .OrderBy(x => x.Player!.DisplayName ?? x.Player.Username)
                .Take(100)
                .Select(ToUserSummary)
                .ToList();

            return Ok(users);
        }

        [HttpGet("users/{playerId:long}")]
        public IActionResult GetUser(long playerId)
        {
            var account = PlayerDB.Players.FindById(playerId);
            if (account?.Player == null)
                return NotFound(new { error = "User not found" });

            var photos = GetOwnedPhotos(account)
                .OrderByDescending(x => x.takenAt)
                .ToList();

            return Ok(new
            {
                accountId = account.PlayerId,
                username = account.Player.Username,
                displayName = account.Player.DisplayName,
                bio = account.Player.Bio,
                level = account.Player.Level,
                createdAt = account.Player.CreatedAt,
                profileImage = ImageUrl(account.Player.ProfileImage),
                bannerImage = string.IsNullOrWhiteSpace(account.Player.BannerImage)
                    ? null
                    : ImageUrl(account.Player.BannerImage),
                verified = IsVerified(account),
                hasRRPlus = HasRRPlus(account),
                roles = account.PlayerRoles.Select(x => x.ToString()),
                photoCount = photos.Count,
                photos
            });
        }

        [HttpGet("photos/newest")]
        public IActionResult GetNewestPhotos([FromQuery] int take = 30)
        {
            take = Math.Clamp(take, 1, 100);

            var owners = PlayerDB.Players.FindAll()
                .Where(x => x.Player != null)
                .SelectMany(account => GetOwnedPhotos(account))
                .GroupBy(x => x.path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var photos = EnumeratePublicPhotos()
                .Select(photo => owners.TryGetValue(photo.path, out var owned)
                    ? photo with { owner = owned.owner }
                    : photo)
                .Concat(owners.Values)
                .GroupBy(photo => photo.path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(photo => photo.owner != null).First())
                .OrderByDescending(x => x.takenAt)
                .Take(take)
                .ToList();

            return Ok(photos);
        }

        [HttpGet("photos/detail")]
        public IActionResult GetPhotoDetail([FromQuery] string path)
        {
            if (!TryResolvePublicPhoto(path, out string normalized, out string fullPath))
                return NotFound(new { error = "Photo not found." });

            var current = AuthStuff.GetCurrentPlayer(Request);
            var owner = PlayerDB.Players.FindAll()
                .Where(x => x.Player != null)
                .SelectMany(GetOwnedPhotos)
                .FirstOrDefault(x => string.Equals(x.path, normalized, StringComparison.OrdinalIgnoreCase))
                ?.owner;

            var comments = RecNetDB.PhotoComments.Find(x => x.PhotoPath == normalized)
                .OrderBy(x => x.CreatedAt)
                .Select(x =>
                {
                    var author = PlayerDB.Players.FindById(x.AccountId);
                    return new
                    {
                        id = x.Id,
                        text = x.Text,
                        createdAt = x.CreatedAt,
                        author = author?.Player == null ? null : new RecNetOwner(
                            author.PlayerId, author.Player.Username, author.Player.DisplayName,
                            ImageUrl(author.Player.ProfileImage), IsVerified(author), HasRRPlus(author))
                    };
                }).ToList();

            int cheerCount = RecNetDB.CountPhotoCheers(normalized);
            bool cheered = current != null &&
                RecNetDB.HasPhotoCheer(normalized, current.PlayerId);

            return Ok(new
            {
                path = normalized,
                url = ImageUrl(normalized),
                takenAt = System.IO.File.GetLastWriteTimeUtc(fullPath),
                owner,
                cheerCount,
                cheered,
                comments
            });
        }

        [HttpPost("photos/cheer")]
        [RequestSizeLimit(16 * 1024)]
        public IActionResult TogglePhotoCheer([FromBody] PhotoPathRequest request)
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account == null)
                return Unauthorized(new { error = "Log in to cheer photos." });
            if (!TryResolvePublicPhoto(request.Path, out string normalized, out _))
                return NotFound(new { error = "Photo not found." });

            bool cheered = !RecNetDB.HasPhotoCheer(normalized, account.PlayerId);
            RecNetDB.SetPhotoCheer(normalized, account.PlayerId, cheered);

            return Ok(new
            {
                cheered,
                isCheered = cheered,
                cheerCount = RecNetDB.CountPhotoCheers(normalized)
            });
        }

        [HttpPost("photos/comments")]
        [RequestSizeLimit(16 * 1024)]
        public IActionResult AddPhotoComment([FromBody] PhotoCommentRequest request)
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account?.Player == null)
                return Unauthorized(new { error = "Log in to comment." });
            if (!TryResolvePublicPhoto(request.Path, out string normalized, out _))
                return NotFound(new { error = "Photo not found." });

            string text = request.Text?.Trim() ?? string.Empty;
            if (text.Length is < 1 or > 300 || text.Any(ch => ch == '\0'))
                return BadRequest(new { error = "Comments must be between 1 and 300 characters." });

            var comment = new RecNetDB.PhotoComment
            {
                PhotoPath = normalized,
                AccountId = account.PlayerId,
                Text = text,
                CreatedAt = DateTime.UtcNow
            };
            RecNetDB.PhotoComments.Insert(comment);
            return Ok(new { success = true, id = comment.Id });
        }

        private static bool TryResolvePublicPhoto(string? path, out string normalized, out string fullPath)
        {
            normalized = NormalizeImagePath(path) ?? string.Empty;
            fullPath = string.Empty;
            if (string.IsNullOrEmpty(normalized))
                return false;

            string first = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (first is not ("PlayerImages" or "PolaroidImages" or "WhereTaken"))
                return false;

            string root = Path.GetFullPath(Path.Combine(Program.dataDir, "Images"));
            fullPath = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !System.IO.File.Exists(fullPath) || !ImageExtensions.Contains(Path.GetExtension(fullPath)))
            {
                return false;
            }

            string photoPath = normalized;
            var indexed = RecNetDB.SavedImages.FindOne(image => image.PhotoPath == photoPath);
            if (indexed != null)
                return indexed.SavedImageType == 1 && indexed.Accessibility == 1;

            if (first is "PolaroidImages" or "WhereTaken")
                return true;

            return PlayerDB.Players.FindAll().Any(account =>
                (account.Player?.PlayerExtra?.SavedAvatars ?? new List<PlayerDBClasses.SavedOutfit>())
                    .Any(outfit => !HasOutfitState(outfit) &&
                        string.Equals(
                            NormalizeImagePath(outfit.PreviewImageName),
                            photoPath,
                            StringComparison.OrdinalIgnoreCase)));
        }

        private static object ToUserSummary(PlayerDBClasses.FullPlayer account)
        {
            var player = account.Player!;
            return new
            {
                accountId = account.PlayerId,
                username = player.Username,
                displayName = player.DisplayName,
                bio = player.Bio,
                level = player.Level,
                profileImage = ImageUrl(player.ProfileImage),
                verified = IsVerified(account),
                hasRRPlus = HasRRPlus(account),
                photoCount = GetOwnedPhotos(account).Count()
            };
        }

        private static IEnumerable<RecNetPhoto> GetOwnedPhotos(PlayerDBClasses.FullPlayer account)
        {
            var owner = new RecNetOwner(
                account.PlayerId,
                account.Player?.Username,
                account.Player?.DisplayName,
                ImageUrl(account.Player?.ProfileImage),
                IsVerified(account),
                HasRRPlus(account));
            var returned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var image in RecNetDB.SavedImages.Find(savedImage =>
                         savedImage.AccountId == account.PlayerId &&
                         savedImage.SavedImageType == 1 &&
                         savedImage.Accessibility == 1))
            {
                string? path = NormalizeImagePath(image.PhotoPath);
                if (path == null)
                    continue;

                string fullPath = Path.Combine(Program.dataDir, "Images", path.Replace('/', Path.DirectorySeparatorChar));
                if (!System.IO.File.Exists(fullPath) || !returned.Add(path))
                    continue;

                yield return CreatePhoto(path, fullPath, owner);
            }

            var legacy = account.Player?.PlayerExtra?.SavedAvatars ?? new();
            foreach (var item in legacy.Where(item => !HasOutfitState(item)))
            {
                string? path = NormalizeImagePath(item.PreviewImageName);
                if (path == null || !returned.Add(path))
                    continue;

                string fullPath = Path.Combine(Program.dataDir, "Images", path.Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(fullPath))
                    yield return CreatePhoto(path, fullPath, owner);
            }
        }

        private static IEnumerable<RecNetPhoto> EnumeratePublicPhotos()
        {
            string root = Path.Combine(Program.dataDir, "Images");
            var allIndexed = RecNetDB.SavedImages.FindAll()
                .Select(image => new { image, path = NormalizeImagePath(image.PhotoPath) })
                .Where(entry => entry.path != null)
                .ToList();
            var indexedPaths = allIndexed
                .Select(entry => entry.path!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in allIndexed.Where(entry =>
                         entry.image.SavedImageType == 1 &&
                         entry.image.Accessibility == 1))
            {
                string path = entry.path!;
                string file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(file) && ImageExtensions.Contains(Path.GetExtension(file)))
                    yield return CreatePhoto(path, file, null);
            }

            string[] folders = { "PolaroidImages", "WhereTaken" };

            foreach (string folder in folders)
            {
                string directory = Path.Combine(root, folder);
                if (!Directory.Exists(directory))
                    continue;

                foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    if (!ImageExtensions.Contains(Path.GetExtension(file)))
                        continue;

                    string path = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (indexedPaths.Contains(path))
                        continue;
                    yield return CreatePhoto(path, file, null);
                }
            }
        }

        private static bool HasOutfitState(PlayerDBClasses.SavedOutfit outfit)
        {
            return !string.IsNullOrWhiteSpace(outfit.OutfitSelections) ||
                   !string.IsNullOrWhiteSpace(outfit.FaceFeatures) ||
                   !string.IsNullOrWhiteSpace(outfit.SkinColor) ||
                   !string.IsNullOrWhiteSpace(outfit.HairColor);
        }

        private static RecNetPhoto CreatePhoto(string path, string fullPath, RecNetOwner? owner)
        {
            DateTime created = System.IO.File.GetCreationTimeUtc(fullPath);
            DateTime modified = System.IO.File.GetLastWriteTimeUtc(fullPath);
            return new RecNetPhoto(path, "/imageserver/" + Uri.EscapeDataString(path).Replace("%2F", "/"),
                created > modified ? created : modified, owner);
        }

        private static string? NormalizeImagePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            string clean = path.Replace('\\', '/').TrimStart('/');
            if (clean.StartsWith("imageserver/", StringComparison.OrdinalIgnoreCase))
                clean = clean[12..];
            string[] segments = clean.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (clean.Length > 260 || segments.Length == 0 ||
                clean.Any(char.IsControl) || clean.Contains(':') ||
                Path.IsPathRooted(clean) ||
                segments.Any(segment => segment is "." or ".."))
            {
                return null;
            }

            return string.Join('/', segments);
        }

        private static string ImageUrl(string? path)
        {
            string clean = NormalizeImagePath(path) ?? "DefaultPFP.png";
            return "/imageserver/" + Uri.EscapeDataString(clean).Replace("%2F", "/");
        }

        private static bool IsVerified(PlayerDBClasses.FullPlayer account) =>
            account.PlayerRoles?.Contains(PlayerDBClasses.PlayerRoles.Influencer) == true;

        private static bool HasRRPlus(PlayerDBClasses.FullPlayer account) =>
            account.PlayerRoles?.Contains(PlayerDBClasses.PlayerRoles.RRPlus) == true;

        public record RecNetOwner(long accountId, string? username, string? displayName,
            string profileImage, bool verified, bool hasRRPlus);
        public record RecNetPhoto(string path, string url, DateTime takenAt, RecNetOwner? owner);
        public class RecNetLogin
        {
            public string? Identity { get; set; }
            public string? Password { get; set; }
        }
        public class RecNetRegistration
        {
            public string? Username { get; set; }
            public string? Password { get; set; }
            public string? ConfirmPassword { get; set; }
            public string? Platform { get; set; }
            public string? PlatformId { get; set; }
        }
        public class AdminRegistration : RecNetRegistration
        {
        }
        public class AdminRoleChange
        {
            public string? Role { get; set; }
            public bool Enabled { get; set; }
        }
        public class AdminPasswordReset
        {
            public string? NewPassword { get; set; }
        }
        public class AdminAvatarItemOwnershipChange
        {
            public long AvatarItemId { get; set; }
            public string? AvatarItemDesc { get; set; }
            public bool Owned { get; set; }
        }
        public class AdminConsumableQuantityChange
        {
            public long ConsumableItemId { get; set; }
            public string? ConsumableItemDesc { get; set; }
            public int Quantity { get; set; }
        }
        public class AdminBalanceChange
        {
            public int Amount { get; set; }
            public bool Add { get; set; }
        }
        public class WebsiteShopPurchase
        {
            public long SkuId { get; set; }
        }
        public class CheerBadgeSelection
        {
            public int Badge { get; set; }
        }
        public class AdminShopItemRequest
        {
            public long SkuId { get; set; }
        }
        public class AdminGiftRequest
        {
            public long RecipientAccountId { get; set; }
            public List<long>? RecipientAccountIds { get; set; }
            public bool SendToAll { get; set; }
            public bool OnlineOnly { get; set; }
            public string? GiftType { get; set; }
            public long SkuId { get; set; }
            public int Amount { get; set; } = 1;
            public int BoxRarity { get; set; }

            public int BoxDesign { get; set; } = (int)PlayerDBClasses.GiftContext.Game_Drop;
            public string? Message { get; set; }
        }
        public class AdminClearOutgoingGiftsRequest
        {
            public long FromPlayerId { get; set; } = 1;
        }
        public class AdminSiteSettings
        {
            public bool? AccountCreationEnabled { get; set; }
            public bool? RecNetSignupEnabled { get; set; }
            public bool? VpnBlockingEnabled { get; set; }
        }
        public class AdminIpBanRequest
        {
            public string? Network { get; set; }
            public string? Reason { get; set; }
        }
        public class AdminMaintenanceRequest
        {
            public int Minutes { get; set; }
        }
        public class AdminSteamBlacklistRequest
        {
            public string? SteamId { get; set; }
            public string? Reason { get; set; }
        }
        public class AdminRoomUpdate
        {
            public string? Name { get; set; }
            public string? Description { get; set; }
            public string? ImageName { get; set; }
            public string? Accessibility { get; set; }
            public string? State { get; set; }
            public int MaxPlayers { get; set; }
            public int MinLevel { get; set; }
            public string[]? Tags { get; set; }
            public bool? CreativeToolsBetaEnabled { get; set; }
            public bool? SupportsBetaContent { get; set; }
            public bool? IsBeta { get; set; }
            public bool? IsBaseRoom { get; set; }
            public bool CloningAllowed { get; set; }
            public bool DisableMicAutoMute { get; set; }
            public bool DisableRoomComments { get; set; }
            public bool EncryptVoiceChat { get; set; }
            public bool ToxmodEnabled { get; set; }
            public bool LoadScreenLocked { get; set; }
            public bool AutoLocalizeRoom { get; set; }
            public bool IsDeveloperOwned { get; set; }
            public bool SupportsLevelVoting { get; set; }
            public bool IsRRO { get; set; }
            public bool SupportsScreens { get; set; }
            public bool SupportsWalkVR { get; set; }
            public bool SupportsTeleportVR { get; set; }
            public bool SupportsVRLow { get; set; }
            public bool SupportsQuest2 { get; set; }
            public bool SupportsMobile { get; set; }
            public bool SupportsJuniors { get; set; }
        }
        public class AdminRoomStatsUpdate
        {
            public int Cheers { get; set; }
            public int Favorites { get; set; }
            public int Visitors { get; set; }
            public int Visits { get; set; }
        }
        public class AdminRoomRoleUpdate
        {
            public string? Role { get; set; } = "None";
            public string? InvitedRole { get; set; } = "None";
        }
        public class AdminRoomOwnerTransfer
        {
            public long AccountId { get; set; }
        }
        public class AdminRoomBlobUpdate
        {
            public string? RoomBlob { get; set; }
            public string? MetadataBlob { get; set; }
        }
        public class AdminSubRoomCreate
        {
            public string? Name { get; set; }
        }
        public class AdminSubRoomUpdate
        {
            public string? Name { get; set; }
            public int MaxPlayers { get; set; }
            public string? Accessibility { get; set; }
            public bool IsSandbox { get; set; }
            public string? UnitySceneId { get; set; }
            public List<RoomDBClasses.SubRoomPermission>? Permissions { get; set; }
        }
        public class AdminRoomBanUpdate
        {
            public long AccountId { get; set; }
            public string? Reason { get; set; }
        }
        public class AdminBanRequest
        {
            public string? Reason { get; set; }
            public bool LinkBan { get; set; }
            public int DurationAmount { get; set; }
            public string? DurationUnit { get; set; }
        }
        public class AdminMessageRequest
        {
            public string? Message { get; set; }
        }
        public class AdminReportResolveRequest
        {
            public string? Action { get; set; }
            public string? Reason { get; set; }
            public int DurationAmount { get; set; }
            public string? DurationUnit { get; set; }
        }
        public class AdminBugReportResolveRequest
        {
            public string? Status { get; set; }
        }
        public class AdminSettingRequest
        {
            public string? Key { get; set; }
            public string? Value { get; set; }
        }
        public class AdminReputationRequest
        {
            public bool? IsCheerful { get; set; }
            public int? CheerGeneral { get; set; }
            public int? CheerHelpful { get; set; }
            public int? CheerSportsman { get; set; }
            public int? CheerGreatHost { get; set; }
            public int? CheerCreative { get; set; }
            public string? SelectedCheer { get; set; }
        }
        public class AdminPreferencesRequest
        {
            public string? Theme { get; set; }
            public string? AccentColor { get; set; }
        }
        public class AdminKickToRoomRequest
        {
            public long? RoomId { get; set; }
            public string? RoomName { get; set; }
        }
        public class AdminTrollFakeBoxRequest
        {
            public int? TokenAmount { get; set; }
            public bool BanImmediately { get; set; }
        }
        public class AdminModerationUnlockRequest
        {
            public bool RemoveLinkedAccounts { get; set; }
        }
        public class AdminProfileUpdate
        {
            public string? Username { get; set; }
            public string? DisplayName { get; set; }
            public string? Bio { get; set; }
            public string? Email { get; set; }
        }
        public class AdminAccountDetailsUpdate
        {
            public int Level { get; set; }
            public int XP { get; set; }
            public bool IsJunior { get; set; }
            public int AvailableUsernameChanges { get; set; }
            public string? ProfileImage { get; set; }
            public string? BannerImage { get; set; }
            public string? DisplayEmoji { get; set; }
            public int PersonalPronouns { get; set; }
        }
        public class AdminPlatformChange
        {
            public string? Platform { get; set; }
            public string? PlatformId { get; set; }
            public bool Enabled { get; set; }
        }
        public class AdminAccountDeletion
        {
            public string? Confirmation { get; set; }
        }
        public class RecNetSettings
        {
            public string? DisplayName { get; set; }
            public string? Username { get; set; }
            public string? Bio { get; set; }
            public string? Email { get; set; }
            public string? ProfileImage { get; set; }
            public string? BannerImage { get; set; }
        }
        public class RecNetPasswordChange
        {
            public string? CurrentPassword { get; set; }
            public string? NewPassword { get; set; }
        }
        public class RecNetDeleteAccount
        {
            public string? Password { get; set; }
            public string? Confirmation { get; set; }
        }
        public class PhotoPathRequest
        {
            public string? Path { get; set; }
        }
        public class PhotoCommentRequest : PhotoPathRequest
        {
            public string? Text { get; set; }
        }
    }
}