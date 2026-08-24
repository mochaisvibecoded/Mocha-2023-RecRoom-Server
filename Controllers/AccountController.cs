using Microsoft.AspNetCore.Mvc;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs;
using static Mocha2023.Classes.DBs.PlayerDB;
using Mocha2023.Classes.DBs.DBClasses;
using Mocha2023.Auth;
using System.Globalization;
using System.Text.Json;

namespace Mocha2023.Controllers
{
    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    [Route("/acc")]
    public class AccountController : ControllerBase
    {
        [HttpGet("account/bulk")]
        public IActionResult GetAccountsBulk([FromQuery] List<long> id)
        {
            var authId = AuthStuff.GetPlayerId(Request);

            var accounts = PlayerDB.GetAccountsBulk(id, authId);

            return Ok(accounts);
        }

        [HttpPut("account/me/displayname")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> UpdateDisplayName([FromForm] string displayName)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            displayName = displayName?.Trim() ?? string.Empty;
            if (displayName.Length is < 1 or > 32 || displayName.Any(char.IsControl))
                return BadRequest(new { error = "invalid_display_name" });

            if (!PlayerDB.UpdateDisplayName(id.Value, displayName))
            {
                DiscordLogger.Log(
                    $"? **Display Name Change Failed**\n**Player ID:** `{id.Value}`\n**Attempted:** `{displayName}`"
                );
                return BadRequest();
            }

            DiscordLogger.Log(
                $"?? **Display Name Changed**\n**Player ID:** `{id.Value}`\n**New Display Name:** `{displayName}`"
            );

            await NotiController.NotifyPlayerProfileUpdatedAsync(id.Value);

            return Ok(new { success = true });
        }

        [HttpPut("/acc/account/me/emoji")]
        [Consumes("application/x-www-form-urlencoded")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> UpdateEmoji(
    [FromForm] string? displayEmoji)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);

            if (!accountId.HasValue)
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(displayEmoji))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "displayEmoji is required"
                });
            }

            if (displayEmoji.Length > 16 || displayEmoji.Any(char.IsControl))
                return BadRequest(new { success = false, error = "invalid_displayEmoji" });

            bool updated = PlayerDB.UpdateDisplayEmoji(
                accountId.Value,
                displayEmoji);

            if (!updated)
                return NotFound();

            Console.WriteLine(
                $"[EMOJI] Player {accountId.Value} selected {displayEmoji}");

            var account = PlayerDB.GetAccountMe(accountId.Value);
            await NotiController.NotifyPlayerProfileUpdatedAsync(accountId.Value);

            return Ok(new
            {
                success = true,
                account
            });
        }

        [HttpGet("/acc/emojiConfig/whitelistedEmojis")]
        public IActionResult GetWhitelistedEmojis()
        {
            string[] emojis =
            {
        "😀","😃","😄","😁","😆","😅","😂","🤣","😊","😇",
        "🙂","🙃","😉","😍","🥰","😘","😗","😚","😋","😛",
        "😜","🤪","😝","🤑","🤗","🤭","🤫","🤔","🤨","😐",
        "😑","😶","🙄","😏","😣","😥","😮","🤐","😯","😪",
        "😫","🥱","😴","😌","🤓","😎","🤩","🥳","😤","😭",
        "😢","🥺","😡","🤬","😱","😨","😰","😬","🤯","🥶",
        "🥵","🤠","🤖","👽","👻","💀","☠️","👹","👺","👾",
        "❤️","🧡","💛","💚","💙","💜","🖤","🤍","🤎",
        "💖","💗","💓","💕","💞","💘","💝","💟","❣️","💔",
        "👍","👎","👌","✌️","🤞","🤟","🤘","🤙",
        "👏","🙌","👐","🤲","🙏","👋","✋","🤚","🫶",
        "💪","🧠","👀","👁️","👄","🦾","🦿",
        "🐶","🐱","🐭","🐹","🐰","🦊","🐻","🐼","🐨","🐯",
        "🦁","🐸","🐵","🐧","🐦","🦅","🦆","🦄","🐴","🐢",
        "🐙","🦈","🐬","🐳","🦋","🐝","🐞","🦖","🦕","🐲",
        "🍎","🍌","🍇","🍉","🍓","🍒","🥝","🍍","🥑","🌮",
        "🍕","🍔","🍟","🌭","🥪","🍗","🍿","🍩","🍪","🎂",
        "🍫","🍬","🍭","🧋","☕","🥤","🍺","🥛",
        "☀️","🌤️","⛅","🌥️","☁️","🌧️","⛈️","❄️","🌈",
        "⭐","🌟","✨","⚡","🔥","💧","🌊","🌸","🌹","🍀","🌲",
        "🎮","🕹️","👾","💻","⌨️","🖥️","📱","🖱️","🎧","📷",
        "📹","💿","💾","🔋","🔌","🛰️","🚀",
        "🎵","🎶","🎼","🎤","🎧","🥁","🎸","🎹","🎺","🎷","🎻",
        "⚽","🏀","🏈","⚾","🎾","🏐","🏓","🥊","🏆","🥇","🥈","🥉",
        "💎","💰","💸","🪙","🎁","📦","🔑","🗝️","🛡️","⚔️",
        "🧸","🎈","🎉","🎊","🕯️","💡","📚","📖","✏️","🖊️",
        "🚗","🚕","🚌","🚓","🚑","🚒","🏎️","🚲","✈️","🚁","🚢",
        "✔️","✅","❌","⭕","❗","❓","💯","♾️","🔔","🔕","❤️‍🔥"
    };

            Console.WriteLine(
                $"[EMOJI] Whitelist requested; returning {emojis.Length} emojis."
            );

            return Ok(emojis);
        }

        [HttpPut("account/me/bio")]
        [RequestSizeLimit(16 * 1024)]
        public IActionResult UpdateMyBio([FromForm] string? bio)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            var player = PlayerDB.Players.FindById(id.Value);
            if (player?.Player == null)
                return NotFound();

            bio = bio?.Trim() ?? string.Empty;
            if (bio.Length > 500 || bio.Any(ch => ch == '\0'))
                return BadRequest(new { error = "invalid_bio" });

            player.Player.Bio = bio;
            PlayerDB.Players.Update(player);

            return Ok(new { success = true });
        }

        [HttpPut("account/me/bannerimage")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> UpdateBannerImage()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            var account = PlayerDB.Players.FindById(id.Value);
            if (account?.Player == null)
                return NotFound();

            string? value = await ReadRequestValueAsync(
                "bannerImage",
                "bannerImageName",
                "imageName",
                "ImageName",
                "filename",
                "Filename",
                "name",
                "value");
            string? requested = NormalizeProfileImagePath(
                value,
                id.Value,
                requireExistingFile: false);
            if (requested == null)
                return BadRequest(new { success = false, error = "invalid_banner_image" });

            account.Player.BannerImage = requested;
            PlayerDB.Players.Update(account);
            Console.WriteLine($"[BANNER IMAGE] Player {id.Value} set to {requested}");
            await NotiController.NotifyPlayerProfileUpdatedAsync(id.Value);
            return Ok(new
            {
                success = true,
                bannerImage = account.Player.BannerImage,
                url = $"{ServerConfig.BaseURL}/imageserver/{account.Player.BannerImage}"
            });
        }

        [HttpGet("namegen/options")]
        public IActionResult GetNameGenOptions()
        {
            var options = Enumerable.Range(0, 5)
                .Select(_ => Classes.NameGen.GetRandomName())
                .Distinct()
                .ToList();

            Console.WriteLine($"[NAMEGEN] Returning options: {string.Join(", ", options)}");

            return Ok(options);
        }

        [HttpPut("account/me/profileimage")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> UpdateProfileImage()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            var account = PlayerDB.Players.FindById(id.Value);
            if (account?.Player == null)
                return NotFound();

            string? value = await ReadRequestValueAsync(
                "profileImage",
                "profileImageName",
                "imageName",
                "ImageName",
                "value");
            string? requested = NormalizeProfileImagePath(value, id.Value);
            if (requested == null)
                return BadRequest(new { success = false, error = "invalid_profile_image" });

            account.Player.ProfileImage = requested;
            PlayerDB.Players.Update(account);

            Console.WriteLine($"[PROFILE IMAGE] Player {id.Value} set to {requested}");
            await NotiController.NotifyPlayerProfileUpdatedAsync(id.Value);

            return Ok(new
            {
                success = true,
                profileImage = account.Player.ProfileImage,
                url = $"{ServerConfig.BaseURL}/imageserver/{account.Player.ProfileImage}"
            });
        }

        private static string? NormalizeProfileImagePath(
            string? value,
            long? ownerId = null,
            bool requireExistingFile = true)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string requested = value.Trim().Replace('\\', '/');
            if (Uri.TryCreate(requested, UriKind.Absolute, out var uri))
            {
                const string marker = "/imageserver/";
                int markerIndex = uri.AbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0)
                    return null;
                requested = Uri.UnescapeDataString(uri.AbsolutePath[(markerIndex + marker.Length)..]);
            }

            requested = requested.TrimStart('/');
            string[] segments = requested.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (requested.Length > 260 || segments.Length == 0 ||
                requested.Any(char.IsControl) || requested.Contains(':') ||
                Path.IsPathRooted(requested) ||
                segments.Any(segment => segment is "." or "..") ||
                !new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" }
                    .Contains(Path.GetExtension(requested), StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            requested = string.Join('/', segments);
            if (!requested.Contains('/'))
            {
                string playerImage = Path.Combine(Program.dataDir, "Images", "PlayerImages", requested);
                if (System.IO.File.Exists(playerImage))
                {
                    requested = $"PlayerImages/{requested}";
                }
                else
                {

                    var savedImage = RecNetDB.SavedImages.FindAll()
                        .Where(image => !ownerId.HasValue || image.AccountId == ownerId.Value)
                        .OrderByDescending(image => image.CreatedAt)
                        .FirstOrDefault(image => string.Equals(
                            Path.GetFileName(image.PhotoPath),
                            requested,
                            StringComparison.OrdinalIgnoreCase));
                    if (savedImage != null)
                        requested = savedImage.PhotoPath.Replace('\\', '/').TrimStart('/');
                }
            }

            string imageRoot = Path.GetFullPath(Path.Combine(Program.dataDir, "Images"));
            string fullPath = Path.GetFullPath(Path.Combine(
                imageRoot,
                requested.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(
                    imageRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                (requireExistingFile && !System.IO.File.Exists(fullPath)))
            {
                return null;
            }

            return requested;
        }

        [HttpGet("accountprivacysettings/{accountId}")]
        public IActionResult GetAccountPrivacySettings(long accountId)
        {
            var callerId = AuthStuff.GetPlayerId(Request);
            bool isOwner = callerId.HasValue && callerId.Value == accountId;

            return Ok(new
            {
                accountId,
                isOwner,
                profileVisibility = 0,
                allowFriendRequests = true,
                allowPartyInvites = true,
                allowGifts = true,
                showOnlineStatus = true
            });
        }

        [HttpGet("account/{accountId}/bio")]
        public IActionResult GetAccountBio(long accountId)
        {
            var player = PlayerDB.Players.FindById(accountId);
            string bio = player?.Player?.Bio ?? "";

            return Ok(new { success = true, bio });
        }

        [HttpPut("account/me/username")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> UpdateUsername()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            string? username = await ReadRequestValueAsync("username");
            if (string.IsNullOrWhiteSpace(username))
                return BadRequest(new { error = "username_required", message = "Username cannot be empty" });

            var player = PlayerDB.Players.FindById(id.Value);
            bool initialAccountSetup = player?.Player?.PlayerExtra?.Settings?.Any(setting =>
                string.Equals(
                    setting.Key,
                    "Recroom.AccountCreation.HasChosenUsername",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(setting.Value, "True", StringComparison.OrdinalIgnoreCase)) == true;

            if (!PlayerDB.UpdateUsername(id.Value, username, initialAccountSetup))
            {
                DiscordLogger.Log(
                    $"? **Username Change Failed**\n**Player ID:** `{id.Value}`\n**Attempted:** `{username}`"
                );
                return BadRequest(new { error = "username_change_failed", message = "No username changes remaining or invalid name" });
            }

            DiscordLogger.Log(
                $"?? **Username Changed**\n**Player ID:** `{id.Value}`\n**New Username:** `{username}`"
            );

            await NotiController.NotifyPlayerProfileUpdatedAsync(id.Value);

            return Ok(new { success = true });
        }

        [HttpPut("account/me/birthday")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> UpdateBirthday()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            string? birthdayText = await ReadRequestValueAsync("birthday", "dateOfBirth");
            if (!DateTime.TryParse(
                    birthdayText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                    out DateTime birthday) ||
                !PlayerDB.UpdateBirthday(id.Value, birthday))
            {
                return BadRequest(new
                {
                    error = "invalid_birthday",
                    message = "Enter a valid birthday."
                });
            }

            return Ok(new { success = true });
        }

        [HttpPost("account/me/changepassword")]
        [HttpPut("account/me/changepassword")]
        [HttpPost("/auth/account/me/changepassword")]
        [HttpPut("/auth/account/me/changepassword")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> ChangePassword()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            string? oldPassword = await ReadRequestValueAsync("oldPassword", "old_password");
            string? newPassword = await ReadRequestValueAsync("newPassword", "new_password", "password");
            if (string.IsNullOrEmpty(newPassword) ||
                newPassword.Length < PasswordSecurity.MinPasswordLength ||
                newPassword.Length > PasswordSecurity.MaxPasswordLength)
            {
                return BadRequest(new
                {
                    error = "invalid_password",
                    message = "Password must be at least 8 characters."
                });
            }

            if (!PlayerDB.SetPassword(id.Value, newPassword, oldPassword))
            {
                return BadRequest(new
                {
                    error = "invalid_old_password",
                    message = "The old password did not match."
                });
            }

            return Ok(new { success = true });
        }

        [HttpGet("account/me/haspassword")]
        public IActionResult HasPassword()
        {
            var id = AuthStuff.GetPlayerId(Request);
            return id.HasValue
                ? Ok(PlayerDB.HasPassword(id.Value))
                : Unauthorized();
        }

        [HttpGet("account/me/email")]
        public IActionResult GetMyEmail()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            var email = PlayerDB.GetEmail(id.Value);
            if (email == null)
                return NotFound();

            return Ok(new { email });
        }

        [HttpPost("account/me/email")]
        [RequestSizeLimit(16 * 1024)]
        public IActionResult SetMyEmail([FromForm] string email)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            email = email?.Trim() ?? string.Empty;
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
                return BadRequest();

            if (!PlayerDB.UpdateEmail(id.Value, email))
                return BadRequest();

            Console.WriteLine($"[EMAIL] Player {id.Value} updated their email address.");

            return NoContent();
        }

        [HttpGet("account/me")]
        public IActionResult GetAccountMe()
        {
            var id = AuthStuff.GetPlayerId(Request);

            if (id == null)
                return Unauthorized();

            var account = PlayerDB.GetAccountMe(id.Value);

            if (account == null)
                return NotFound();

            Console.WriteLine($"[ACCOUNT ME] Auth ID: {id.Value}");
            return Ok(account);
        }

        [HttpPut("account/me/personalpronouns")]
        [RequestSizeLimit(8 * 1024)]
        public async Task<IActionResult> UpdatePersonalPronouns()
        {
            long? id = AuthStuff.GetPlayerId(Request);
            if (!id.HasValue)
                return Unauthorized();

            string? raw = await ReadRequestValueAsync(
                "personalPronouns",
                "PersonalPronouns",
                "personalPronoun",
                "PersonalPronoun",
                "personal_pronouns",
                "pronouns",
                "pronoun",
                "value");

            Console.WriteLine(
                $"[PERSONAL PRONOUNS] account={id.Value} " +
                $"contentType={Request.ContentType ?? "null"} " +
                $"raw={raw ?? "null"}");

            if (!TryParsePersonalPronouns(raw, out int personalPronouns))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "invalid_personal_pronouns",
                    received = raw,
                    contentType = Request.ContentType
                });
            }

            if (personalPronouns < 0 || (personalPronouns & ~0x3F) != 0)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "personal_pronouns_out_of_range",
                    received = personalPronouns,
                    allowedMinimum = 0,
                    allowedMaximum = 63
                });
            }

            if (!PlayerDB.UpdatePersonalPronouns(id.Value, personalPronouns))
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = "personal_pronouns_update_failed"
                });
            }

            var account = PlayerDB.GetAccountMe(id.Value);

            Console.WriteLine(
                $"[PERSONAL PRONOUNS] account={id.Value} saved={personalPronouns}");

            await NotiController.NotifyPlayerProfileUpdatedAsync(id.Value);

            return Ok(new
            {
                success = true,
                personalPronouns,
                account
            });
        }

        private static bool TryParsePersonalPronouns(
            string? raw,
            out int personalPronouns)
        {
            personalPronouns = 0;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string cleaned = Uri.UnescapeDataString(raw)
                .Trim()
                .Trim((char)34, (char)39);

            if (int.TryParse(cleaned, out personalPronouns))
                return true;

            int equalsIndex = cleaned.LastIndexOf('=');
            if (equalsIndex >= 0 && equalsIndex < cleaned.Length - 1)
            {
                string valuePart = cleaned[(equalsIndex + 1)..]
                    .Trim()
                    .Trim((char)34, (char)39);

                if (int.TryParse(valuePart, out personalPronouns))
                    return true;
            }

            return false;
        }

        [HttpPut("account/me/identityflags")]
        [RequestSizeLimit(8 * 1024)]
        public async Task<IActionResult> UpdateIdentityFlags()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);

            if (!accountId.HasValue)
                return Unauthorized();

            string? raw = await ReadRequestValueAsync(
                "identityFlags",
                "IdentityFlags",
                "identityFlag",
                "flags",
                "value");

            if (!TryParsePersonalPronouns(raw, out int identityFlags) ||
                identityFlags < 0)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "invalid_identity_flags",
                    received = raw
                });
            }

            if (!PlayerDB.UpdateIdentityFlags(
                    accountId.Value,
                    identityFlags))
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = "identity_flags_update_failed"
                });
            }

            Console.WriteLine(
                $"[IDENTITY FLAGS] account={accountId.Value} saved={identityFlags}");

            await NotiController.NotifyPlayerProfileUpdatedAsync(accountId.Value);

            return Ok(new
            {
                success = true,
                identityFlags,
                account = PlayerDB.GetAccountMe(accountId.Value)
            });
        }

        [HttpGet("account/search")]
        public IActionResult SearchAccounts([FromQuery] string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new
                {
                    error = "name_required",
                    message = "A name must be provided."
                });
            }

            var authId = AuthStuff.GetPlayerId(Request);
            string query = name.Trim();

            var matchingAccountIds = PlayerDB.Players
                .FindAll()
                .Where(account =>
                    account.Player != null &&
                    (
                        (!string.IsNullOrWhiteSpace(account.Player.Username) &&
                         account.Player.Username.Contains(
                             query,
                             StringComparison.OrdinalIgnoreCase
                         ))
                        ||
                        (!string.IsNullOrWhiteSpace(account.Player.DisplayName) &&
                         account.Player.DisplayName.Contains(
                             query,
                             StringComparison.OrdinalIgnoreCase
                         ))
                    )
                )
                .OrderByDescending(account =>
                    string.Equals(
                        account.Player!.Username,
                        query,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ThenByDescending(account =>
                    string.Equals(
                        account.Player!.DisplayName,
                        query,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ThenBy(account => account.Player!.Username)
                .Take(20)
                .Select(account => account.PlayerId)
                .ToList();

            var accounts = PlayerDB.GetAccountsBulk(
                matchingAccountIds,
                authId
            );

            Console.WriteLine(
                $"[ACCOUNT SEARCH] \"{query}\" returned {matchingAccountIds.Count} account(s)"
            );

            return Ok(accounts);
        }

        private async Task<string?> ReadRequestValueAsync(params string[] keys)
        {
            foreach (string key in keys)
            {
                string? queryValue = Request.Query[key].FirstOrDefault();
                if (queryValue != null)
                    return queryValue;
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(
                    HttpContext.RequestAborted);

                foreach (string key in keys)
                {
                    string? formValue = form[key].FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(formValue))
                        return formValue;
                }

                if (form.Count == 1)
                {
                    var onlyField = form.First();
                    string? onlyValue = onlyField.Value.FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(onlyValue))
                        return onlyValue;

                    if (!string.IsNullOrWhiteSpace(onlyField.Key))
                        return onlyField.Key;
                }

                return null;
            }

            if (Request.ContentLength == 0)
                return null;

            string rawBody;
            using (var reader = new StreamReader(Request.Body))
                rawBody = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(rawBody))
                return null;

            try
            {
                using var document = JsonDocument.Parse(rawBody);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return document.RootElement.ValueKind is
                        JsonValueKind.String or JsonValueKind.Number or
                        JsonValueKind.True or JsonValueKind.False
                            ? document.RootElement.ToString()
                            : null;
                }

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!keys.Any(key => string.Equals(key, property.Name, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    return property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.ToString();
                }

                var onlyProperty = document.RootElement.EnumerateObject().Take(2).ToArray();
                if (onlyProperty.Length == 1)
                {
                    return onlyProperty[0].Value.ValueKind == JsonValueKind.String
                        ? onlyProperty[0].Value.GetString()
                        : onlyProperty[0].Value.ToString();
                }
            }
            catch (JsonException)
            {

                return rawBody.Trim().Trim('"');
            }

            return null;
        }
    }
}
