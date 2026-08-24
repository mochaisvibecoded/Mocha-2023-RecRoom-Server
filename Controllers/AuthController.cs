using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Mocha2023.Auth;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs;
using Mocha2023.Classes.DBs.DBClasses;

namespace Mocha2023.Controllers
{
    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    [Route("/auth")]
    public class AuthController : ControllerBase
    {

        private static readonly TimeSpan RefreshTokenLifetime =
            TimeSpan.FromDays(90);

        private static readonly object PlatformAccountCreationLock = new();

        private static readonly ConcurrentDictionary<
            string,
            RefreshTokenEntry
        > RefreshTokens = new(StringComparer.Ordinal);

        private sealed record RefreshTokenEntry(
            long AccountId,
            DateTimeOffset ExpiresAt);

        [HttpGet("eac/challenge")]
        public IActionResult GetEACChallenge()
        {
            string challenge = "\"e\"";

            return Ok(challenge);
        }

        [HttpGet("role/{role}/{playerId}")]
        public IActionResult HasRole(
            string role,
            long playerId)
        {
            if (!Enum.TryParse<PlayerDBClasses.PlayerRoles>(
                    role,
                    true,
                    out var parsedRole))
            {
                return Ok(false);
            }

            var player =
                PlayerDB.Players.FindById(playerId);

            bool hasRole =
                player?.PlayerRoles?.Contains(parsedRole)
                ?? false;

            return Ok(hasRole);
        }

        [HttpPost("cachedlogin/forplatformids")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> CachedLoginForPlatformIds()
        {
            if (!Request.HasFormContentType)
            {
                Console.WriteLine(
                    "[QUEST AUTH] Cached-login request was not form data.");

                return Ok(
                    Array.Empty<PlayerDBClasses.CachedLogins>());
            }

            var form = await Request.ReadFormAsync(
                HttpContext.RequestAborted);

            Console.WriteLine(
                $"[QUEST AUTH] Cached-login fields=" +
                $"{string.Join(',', form.Keys.OrderBy(key => key))}");

            if (!TryReadPlatform(
                    form,
                    out var platform,
                    out ulong platformId))
            {
                Console.WriteLine(
                    "[QUEST AUTH] Missing or invalid platform identity.");

                return Ok(
                    Array.Empty<PlayerDBClasses.CachedLogins>());
            }

            return GetOrCreateCachedLogins(
                platform,
                platformId);
        }

        [HttpGet(
            "cachedlogin/forplatformid/{platformText}/{platformId}")]
        public IActionResult GetCachedLogins(
            string platformText,
            ulong platformId)
        {
            if (!TryParsePlatform(
                    platformText,
                    out var platform))
            {
                Console.WriteLine(
                    $"[AUTH CACHED LOGIN INVALID] " +
                    $"platform={platformText} " +
                    $"platformId={platformId}");

                return Ok(
                    Array.Empty<PlayerDBClasses.CachedLogins>());
            }

            return GetOrCreateCachedLogins(
                platform,
                platformId);
        }

        private IActionResult GetOrCreateCachedLogins(
            PlayerDBClasses.Platforms platform,
            ulong platformId)
        {
            if (!IsUsablePlatform(platform) ||
                platformId == 0)
            {
                Console.WriteLine(
                    $"[AUTH CACHED LOGIN INVALID] " +
                    $"platform={platform} " +
                    $"platformId={platformId}");

                return Ok(
                    Array.Empty<PlayerDBClasses.CachedLogins>());
            }

            if (platform == PlayerDBClasses.Platforms.Steam &&
                SteamAccessDB.IsBlacklisted(platformId))
            {
                return SteamIdForbidden(platformId);
            }

            if (TryGetCachedLogins(
                    platform,
                    platformId,
                    out var existingAccounts))
            {
                Console.WriteLine(
                    $"[AUTH CACHED LOGIN] " +
                    $"platform={platform} " +
                    $"platformId={platformId} " +
                    $"accounts={existingAccounts.Count}");

                return Ok(existingAccounts);
            }

            if (!RecNetDB.IsAccountCreationEnabled())
            {
                Console.WriteLine(
                    $"[AUTO ACCOUNT CREATE BLOCKED] platform={platform} platformId={platformId}");
                return Ok(Array.Empty<PlayerDBClasses.CachedLogins>());
            }

            lock (PlatformAccountCreationLock)
            {

                if (TryGetCachedLogins(
                        platform,
                        platformId,
                        out existingAccounts))
                {
                    return Ok(existingAccounts);
                }

                try
                {
                    var created =
                        PlayerDB.CreateAccount(
                            platform,
                            platformId,
                            false,
                            completeAccountCreation: false);

                    Console.WriteLine(
                        $"[AUTO ACCOUNT CREATE] " +
                        $"account={created.PlayerId} " +
                        $"platform={platform} " +
                        $"platformId={platformId}");
                }
                catch (Exception exception)
                {
                    Console.WriteLine(
                        $"[AUTO ACCOUNT CREATE FAILED] " +
                        $"platform={platform} " +
                        $"platformId={platformId} " +
                        $"reason={exception.GetType().Name}: " +
                        $"{exception.Message}");

                }

                if (TryGetCachedLogins(
                        platform,
                        platformId,
                        out var createdAccounts))
                {
                    return Ok(createdAccounts);
                }
            }

            return Ok(
                Array.Empty<PlayerDBClasses.CachedLogins>());
        }

        private static bool TryGetCachedLogins(
            PlayerDBClasses.Platforms platform,
            ulong platformId,
            out List<PlayerDBClasses.CachedLogins> accounts)
        {
            accounts = new List<PlayerDBClasses.CachedLogins>();

            if (!PlayerDB.GetLogins(
                    platform,
                    platformId,
                    out var foundAccounts) ||
                foundAccounts == null)
            {
                return false;
            }

            accounts = foundAccounts.ToList();

            return accounts.Count > 0;
        }

        [HttpPost("connect/token")]
        [HttpPost("connect/gametoken")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> ConnectToken()
        {
            if (!Request.HasFormContentType)
            {
                return OAuthError(
                    "invalid_request",
                    "Expected form data.");
            }

            var form = await Request.ReadFormAsync(
                HttpContext.RequestAborted);

            string grantType =
                FirstFormValue(
                        form,
                        "grant_type",
                        "grantType")
                    .Trim()
                    .Replace(
                        "-",
                        "_",
                        StringComparison.Ordinal)
                    .ToLowerInvariant();

            Console.WriteLine(
                $"[AUTH TOKEN] " +
                $"grant={grantType} " +
                $"fields={string.Join(',', form.Keys.OrderBy(key => key))}");

            bool isFreshLogin =
                grantType is "cached_login" or "cachedlogin" or
                    "password" or
                    "create_account" or "createaccount";

            if (isFreshLogin)
            {
                IActionResult gateResult = CheckModGate(form);
                if (gateResult != null)
                {
                    return gateResult;
                }
            }

            switch (grantType)
            {
                case "cached_login":
                case "cachedlogin":
                    {
                        return HandleCachedLoginGrant(form);
                    }

                case "password":
                    {
                        return HandlePasswordGrant(form);
                    }

                case "create_account":
                case "createaccount":
                    {
                        return HandleCreateAccountGrant(form);
                    }

                case "refresh_token":
                case "refreshtoken":
                    {
                        return HandleRefreshTokenGrant(form);
                    }

                default:
                    {
                        Console.WriteLine(
                            $"[AUTH TOKEN FAILED] " +
                            $"Unsupported grant type: {grantType}");

                        return OAuthError(
                            "unsupported_grant_type",
                            $"Unsupported grant_type: {grantType}");
                    }
            }
        }

        private IActionResult HandleCachedLoginGrant(
            IFormCollection form)
        {
            if (!TryReadAccountId(
                    form,
                    out long accountId))
            {
                return OAuthError(
                    "invalid_request",
                    "account_id is required.");
            }

            var player =
                PlayerDB.Players.FindById(accountId);

            if (player?.Player == null)
            {
                return OAuthError(
                    "invalid_grant",
                    "Account not found.");
            }

            if (TryReadPlatform(
                    form,
                    out var suppliedPlatform,
                    out ulong suppliedPlatformId) &&
                suppliedPlatform == PlayerDBClasses.Platforms.Steam &&
                SteamAccessDB.IsBlacklisted(suppliedPlatformId))
            {
                return SteamIdForbidden(suppliedPlatformId);
            }

            if (SteamAccessDB.TryGetBlockedSteamId(
                    player,
                    out ulong blockedSteamId))
            {
                return SteamIdForbidden(blockedSteamId);
            }

            bool linkedIdentity = false;

            if (TryReadPlatform(
                    form,
                    out var platform,
                    out ulong platformId))
            {
                linkedIdentity =
                    player.PlatformIds?.Any(identity =>
                        identity.Platform == platform &&
                        identity.PlatformId == platformId)
                    == true;
            }

            else if (TryReadPlatformId(
                         form,
                         out ulong platformIdOnly))
            {
                linkedIdentity =
                    player.PlatformIds?.Any(identity =>
                        identity.PlatformId == platformIdOnly)
                    == true;
            }

            if (!linkedIdentity)
            {
                Console.WriteLine(
                    $"[AUTH CACHED LOGIN FAILED] " +
                    $"account={accountId} " +
                    $"reason=platform_identity_not_linked");

                return OAuthError(
                    "invalid_grant",
                    "This account is not linked to the supplied " +
                    "platform identity.");
            }

            if (!IsKnownDeviceOrUnbound(player, form))
            {
                Console.WriteLine(
                    $"[AUTH CACHED LOGIN FAILED] " +
                    $"account={accountId} reason=device_not_linked");

                return OAuthError(
                    "invalid_grant",
                    "This device is not linked to the requested account.");
            }

            SaveDeviceIdIfPresent(
                player,
                form);

            return Login(
                player,
                logAsLogin: true);
        }

        private IActionResult HandlePasswordGrant(
            IFormCollection form)
        {
            string username =
                FirstFormValue(
                        form,
                        "username",
                        "user_name",
                        "userName")
                    .Trim()
                    .TrimStart('@');

            string password =
                FirstFormValue(
                    form,
                    "password");

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrEmpty(password))
            {
                return OAuthError(
                    "invalid_request",
                    "username and password are required.");
            }

            string clientAddress =
                ClientNetwork.GetClientIp(Request)?.ToString() ?? "unknown";
            if (!PasswordSecurity.TryBeginLoginAttempt(
                    username,
                    clientAddress,
                    out int retryAfterSeconds))
            {
                Response.Headers["Retry-After"] = retryAfterSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                return OAuthError(
                    "slow_down",
                    "Too many login attempts. Try again later.");
            }

            var player = PlayerDB.Players
                .FindAll()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Player?.Username,
                        username,
                        StringComparison.OrdinalIgnoreCase));

            bool validPassword = PasswordSecurity.VerifyLogin(
                password,
                player?.Password,
                out bool needsUpgrade);

            PasswordSecurity.CompleteLoginAttempt(
                username,
                clientAddress,
                player?.Player != null && validPassword);

            if (player?.Player == null || !validPassword)
            {
                return OAuthError(
                    "invalid_username_or_password",
                    "Username and password do not match.");
            }

            if (needsUpgrade)
            {
                player.Password =
                    PasswordSecurity.Hash(password);

                PlayerDB.Players.Update(player);
            }

            SaveDeviceIdIfPresent(
                player,
                form);

            return Login(
                player,
                logAsLogin: true);
        }

        private IActionResult HandleCreateAccountGrant(
            IFormCollection form)
        {
            if (!RecNetDB.IsAccountCreationEnabled())
            {
                return OAuthError(
                    "account_creation_disabled",
                    "New account creation is currently disabled.");
            }

            if (!TryReadPlatform(
                    form,
                    out var platform,
                    out ulong platformId))
            {
                return OAuthError(
                    "invalid_request",
                    "A valid platform and platform_id are required.");
            }

            if (platform == PlayerDBClasses.Platforms.Steam &&
                SteamAccessDB.IsBlacklisted(platformId))
            {
                return SteamIdForbidden(platformId);
            }

            int accountCount = PlayerDB.Players
                .FindAll()
                .Count(player =>
                    player.PlatformIds?.Any(identity =>
                        identity.Platform == platform &&
                        identity.PlatformId == platformId)
                    == true);

            int accountLimit =
                platform == PlayerDBClasses.Platforms.Steam
                    ? 5
                    : 1;

            if (accountCount >= accountLimit)
            {
                return OAuthError(
                    "account_limit_reached",
                    platform ==
                    PlayerDBClasses.Platforms.Steam
                        ? "This Steam ID already has the maximum " +
                          "of 5 accounts."
                        : "That platform account is already registered.");
            }

            PlayerDBClasses.FullPlayer player;

            lock (PlatformAccountCreationLock)
            {

                var existingPlayer = PlayerDB.Players
                    .FindAll()
                    .FirstOrDefault(candidate =>
                        candidate.PlatformIds?.Any(identity =>
                            identity.Platform == platform &&
                            identity.PlatformId == platformId)
                        == true);

                if (existingPlayer?.Player != null)
                {
                    return OAuthError(
                        "account_limit_reached",
                        platform ==
                        PlayerDBClasses.Platforms.Steam
                            ? "This Steam ID already has the maximum " +
                              "number of accounts."
                            : "That platform account is already registered.");
                }

                player = PlayerDB.CreateAccount(
                    platform,
                    platformId,
                    false,
                    completeAccountCreation: false);
            }

            SaveDeviceIdIfPresent(
                player,
                form);

            Console.WriteLine(
                $"[ACCOUNT CREATE] " +
                $"account={player.PlayerId} " +
                $"platform={platform} " +
                $"platformId={platformId}");

            return Login(
                player,
                logAsLogin: true);
        }

        private IActionResult HandleRefreshTokenGrant(
            IFormCollection form)
        {
            string refreshToken =
                FirstFormValue(
                        form,
                        "refresh_token",
                        "refreshToken")
                    .Trim();

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                Console.WriteLine(
                    "[AUTH REFRESH FAILED] No token provided.");

                return OAuthError(
                    "invalid_request",
                    "refresh_token is required.");
            }

            string refreshTokenKey = HashRefreshToken(refreshToken);
            if (!RefreshTokens.TryRemove(
                    refreshTokenKey,
                    out RefreshTokenEntry? tokenEntry))
            {
                Console.WriteLine(
                    $"[AUTH REFRESH FAILED] " +
                    $"Unknown token length={refreshToken.Length}");

                return OAuthError(
                    "invalid_grant",
                    "Refresh token is invalid or expired.");
            }

            if (tokenEntry.ExpiresAt <=
                DateTimeOffset.UtcNow)
            {
                Console.WriteLine(
                    $"[AUTH REFRESH FAILED] " +
                    $"Expired token for " +
                    $"account={tokenEntry.AccountId}");

                return OAuthError(
                    "invalid_grant",
                    "Refresh token is invalid or expired.");
            }

            var player =
                PlayerDB.Players.FindById(
                    tokenEntry.AccountId);

            if (player?.Player == null)
            {
                return OAuthError(
                    "invalid_grant",
                    "Account no longer exists.");
            }

            SaveDeviceIdIfPresent(
                player,
                form);

            Console.WriteLine(
                $"[AUTH REFRESH] " +
                $"account={tokenEntry.AccountId}");

            return Login(
                player,
                logAsLogin: false);
        }

        private IActionResult Login(
            PlayerDBClasses.FullPlayer player,
            bool logAsLogin)
        {
            if (player.Player == null)
            {
                return OAuthError(
                    "invalid_grant",
                    "Account not found.");
            }

            if (SteamAccessDB.TryGetBlockedSteamId(
                    player,
                    out ulong blockedSteamId))
            {
                return SteamIdForbidden(blockedSteamId);
            }

            if (RecNetDB.ModerationLocks.Exists(
                    moderationLock =>
                        moderationLock.AccountId ==
                        player.PlayerId))
            {
                Console.WriteLine(
                    $"[AUTH LOGIN BLOCKED] " +
                    $"account={player.PlayerId} " +
                    $"reason=moderation_lock");

                return OAuthError(
                    "account_locked",
                    "This account is currently locked.");
            }

            if (logAsLogin)
            {
                player.Player.LastLoginAt =
                    DateTime.UtcNow;

                PlayerDB.Players.Update(player);

                DiscordLogger.LogLogin(
                    player.PlayerId,
                    player.Player.Username);
            }

            CleanupExpiredRefreshTokens();
            LimitRefreshTokensForAccount(player.PlayerId, 16);

            string accessToken =
                AuthStuff.Encode(player.PlayerId);

            string refreshToken = CreateRefreshToken();
            RefreshTokens[HashRefreshToken(refreshToken)] =
                new RefreshTokenEntry(
                    player.PlayerId,
                    DateTimeOffset.UtcNow +
                    RefreshTokenLifetime);

            Console.WriteLine(
                $"[AUTH TOKEN ISSUED] " +
                $"account={player.PlayerId} " +
                $"type={(logAsLogin ? "login" : "refresh")}");

            return Ok(new
            {
                access_token = accessToken,

                token_type = "bearer",

                expires_in = 2_592_000,

                account_id = player.PlayerId,

                error = "",
                error_description = "",

                refresh_token = refreshToken,

                key = ""
            });
        }

        private static string CreateRefreshToken()
        {

            return Convert.ToHexString(
                RandomNumberGenerator.GetBytes(32));
        }

        private static string HashRefreshToken(string refreshToken)
        {
            return Convert.ToHexString(
                SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(refreshToken)));
        }

        private static void CleanupExpiredRefreshTokens()
        {
            DateTimeOffset now =
                DateTimeOffset.UtcNow;

            foreach (var token in RefreshTokens)
            {
                if (token.Value.ExpiresAt <= now)
                {
                    RefreshTokens.TryRemove(
                        token.Key,
                        out _);
                }
            }
        }

        private static void LimitRefreshTokensForAccount(
            long accountId,
            int maximumTokens)
        {
            var accountTokens = RefreshTokens
                .Where(entry => entry.Value.AccountId == accountId)
                .OrderBy(entry => entry.Value.ExpiresAt)
                .ToList();

            int removeCount = Math.Max(
                0,
                accountTokens.Count - Math.Max(1, maximumTokens - 1));
            foreach (var entry in accountTokens.Take(removeCount))
            {
                RefreshTokens.TryRemove(entry.Key, out _);
            }
        }

        private static void SaveDeviceIdIfPresent(
            PlayerDBClasses.FullPlayer player,
            IFormCollection form)
        {
            string deviceId = ReadDeviceId(form);

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return;
            }

            player.DeviceIds ??=
                new List<string>();

            bool alreadyExists =
                player.DeviceIds.Any(existingDeviceId =>
                    string.Equals(
                        existingDeviceId,
                        deviceId,
                        StringComparison.Ordinal));

            if (alreadyExists)
            {
                return;
            }

            if (player.DeviceIds.Count >= 32)
            {
                return;
            }

            player.DeviceIds.Add(deviceId);

            PlayerDB.Players.Update(player);

            Console.WriteLine(
                $"[AUTH DEVICE LINKED] " +
                $"account={player.PlayerId}");
        }

        private static bool IsKnownDeviceOrUnbound(
            PlayerDBClasses.FullPlayer player,
            IFormCollection form)
        {
            List<string> knownDevices = player.DeviceIds?
                .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
                .ToList() ?? new List<string>();
            if (knownDevices.Count == 0)
                return true;

            string suppliedDeviceId = ReadDeviceId(form);
            return suppliedDeviceId.Length > 0 &&
                   knownDevices.Any(deviceId =>
                       string.Equals(
                           deviceId,
                           suppliedDeviceId,
                           StringComparison.Ordinal));
        }

        private static string ReadDeviceId(IFormCollection form)
        {
            string deviceId = FirstFormValue(
                    form,
                    "device_id",
                    "deviceId",
                    "deviceid")
                .Trim();

            if (deviceId.Length is < 8 or > 256 ||
                deviceId.Any(char.IsControl))
            {
                return string.Empty;
            }

            return deviceId;
        }

        private IActionResult CheckModGate(IFormCollection form)
        {
            string buildHeader = Request.Headers["X-Mocha-Build"].ToString();

            if (string.IsNullOrWhiteSpace(buildHeader) ||
                !Version.TryParse(buildHeader, out Version build) ||
                build < ServerConfig.MinModVersion)
            {
                return OutdatedPatch();
            }

            string steamIdHeader = Request.Headers["X-Mocha-SteamId"].ToString();
            bool hasSteamId =
                ulong.TryParse(steamIdHeader, out ulong steamId) &&
                steamId != 0;

            TryReadPlatform(form, out var platform, out ulong platformId);
            bool isQuest = platform == PlayerDBClasses.Platforms.Oculus;

            if (!hasSteamId && !isQuest)
            {
                return OutdatedPatch();
            }

            if (hasSteamId && SteamAccessDB.IsBlacklisted(steamId))
            {
                return SteamIdForbidden(steamId);
            }

            return null;
        }

        private BadRequestObjectResult OutdatedPatch()
        {
            return OAuthError(
                "outdated_client",
                "You're on the old patch.\r\nUpdate the patch in the Discord :3\r\nThis shows up once.");
        }

        private static bool TryReadPlatform(
            IFormCollection form,
            out PlayerDBClasses.Platforms platform,
            out ulong platformId)
        {
            platform = default;
            platformId = 0;

            string platformText =
                FirstFormValue(
                    form,
                    "platform",
                    "platform_type",
                    "platformType",
                    "platform_name",
                    "platformName");

            if (!TryParsePlatform(
                    platformText,
                    out platform))
            {
                return false;
            }

            return TryReadPlatformId(
                form,
                out platformId);
        }

        private static bool TryReadPlatformId(
            IFormCollection form,
            out ulong platformId)
        {
            platformId = 0;

            string platformIdText =
                FirstFormValue(
                    form,
                    "platform_id",
                    "platformId",
                    "platformid",
                    "platform_user_id",
                    "platformUserId",
                    "platformuserid",
                    "oculus_user_id",
                    "oculusUserId",
                    "oculus_userid",
                    "oculus_id",
                    "oculusId",
                    "meta_user_id",
                    "metaUserId",
                    "meta_userid",
                    "quest_user_id",
                    "questUserId",
                    "quest_userid",
                    "user_id",
                    "userId");

            return ulong.TryParse(
                       platformIdText,
                       out platformId) &&
                   platformId != 0;
        }

        private static bool TryParsePlatform(
            string platformText,
            out PlayerDBClasses.Platforms platform)
        {
            platform = default;

            if (string.IsNullOrWhiteSpace(platformText))
            {
                return false;
            }

            platformText =
                platformText.Trim();

            if (int.TryParse(
                    platformText,
                    out int platformNumber))
            {
                platform =
                    (PlayerDBClasses.Platforms)platformNumber;

                return IsUsablePlatform(platform);
            }

            if (Enum.TryParse(
                    platformText,
                    true,
                    out platform) &&
                IsUsablePlatform(platform))
            {
                return true;
            }

            string normalized =
                NormalizeEnumText(platformText);

            bool isQuestAlias =
                normalized is
                    "quest" or
                    "quest2" or
                    "quest3" or
                    "questpro" or
                    "meta" or
                    "metaquest" or
                    "oculus" or
                    "oculusquest" or
                    "oculusstandalone";

            if (!isQuestAlias)
            {
                return false;
            }

            foreach (string enumName in
                     Enum.GetNames<PlayerDBClasses.Platforms>())
            {
                string normalizedEnumName =
                    NormalizeEnumText(enumName);

                bool looksLikeQuestPlatform =
                    normalizedEnumName.Contains(
                        "quest",
                        StringComparison.Ordinal) ||
                    normalizedEnumName.Contains(
                        "oculus",
                        StringComparison.Ordinal) ||
                    normalizedEnumName is
                        "meta" or
                        "metaquest";

                if (!looksLikeQuestPlatform)
                {
                    continue;
                }

                if (Enum.TryParse(
                        enumName,
                        true,
                        out platform) &&
                    IsUsablePlatform(platform))
                {
                    return true;
                }
            }

            platform = default;
            return false;
        }

        private static bool IsUsablePlatform(
            PlayerDBClasses.Platforms platform)
        {
            return Enum.IsDefined(
                       typeof(PlayerDBClasses.Platforms),
                       platform) &&
                   platform is not
                       PlayerDBClasses.Platforms.All and not
                       PlayerDBClasses.Platforms.HeadlessBot;
        }

        private static string NormalizeEnumText(
            string value)
        {
            return new string(
                    value
                        .Where(char.IsLetterOrDigit)
                        .ToArray())
                .ToLowerInvariant();
        }

        private static bool TryReadAccountId(
            IFormCollection form,
            out long accountId)
        {
            accountId = 0;

            string accountIdText =
                FirstFormValue(
                    form,
                    "account_id",
                    "accountId",
                    "accountid",
                    "player_id",
                    "playerId");

            return long.TryParse(
                       accountIdText,
                       out accountId) &&
                   accountId > 0;
        }

        private static string FirstFormValue(
            IFormCollection form,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                string? value =
                    form[key].FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private ObjectResult SteamIdForbidden(
            ulong steamId)
        {
            Console.WriteLine(
                $"[AUTH STEAM BLOCKED] steamId={steamId}");

            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    error = "steam_id_blacklisted",
                    error_description =
                        "This Steam ID has been blocked from Mocha."
                });
        }

        private BadRequestObjectResult OAuthError(
            string error,
            string description)
        {
            return BadRequest(new
            {
                error,
                error_description = description
            });
        }
    }
}
