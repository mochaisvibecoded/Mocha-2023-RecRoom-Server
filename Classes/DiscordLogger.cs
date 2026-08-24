using System.IO;
using System.Text;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Mocha2023.Classes
{
    public static class DiscordLogger
    {
        private const string DISCORD_WEBHOOK_URL = "";
        private const string SHARECAM_DISCORD_WEBHOOK_URL = "";
        private const string SANITIZE_DISCORD_WEBHOOK_URL = "";
        private const string REPORT_DISCORD_WEBHOOK_URL = "";

        private static readonly Uri? WebhookUrl = LoadWebhookUrl(
            "DISCORD_WEBHOOK_URL",
            DISCORD_WEBHOOK_URL);
        private static readonly Uri? ShareCameraWebhookUrl = LoadWebhookUrl(
            "SHARECAM_WEBHOOK_URL",
            SHARECAM_DISCORD_WEBHOOK_URL);
        private static readonly Uri? SanitizeWebhookUrl = LoadWebhookUrl(
            "SANITIZE_DISCORD_WEBHOOK_URL",
            SANITIZE_DISCORD_WEBHOOK_URL);
        private static readonly Uri? ReportWebhookUrl = LoadWebhookUrl(
            "REPORT_DISCORD_WEBHOOK_URL",
            REPORT_DISCORD_WEBHOOK_URL);
        private static readonly HttpClient _client = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static Uri? LoadWebhookUrl(
            string environmentVariable,
            string fallbackUrl)
        {
            string? configured =
                Mocha2023.Program.LoadLocalSetting(environmentVariable);

            if (string.IsNullOrWhiteSpace(configured))
                configured = fallbackUrl;

            if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !(uri.Host.Equals("discord.com", StringComparison.OrdinalIgnoreCase) ||
                  uri.Host.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase) ||
                  uri.Host.Equals("discordapp.com", StringComparison.OrdinalIgnoreCase) ||
                  uri.Host.EndsWith(".discordapp.com", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            return uri;
        }

        public static void Log(string content)
        {
            _ = SendEmbedAsync(WebhookUrl, "Mocha Server Logs", content, 0x6F4E37);
        }

        public static void LogSanitize(string content)
        {
            _ = SendEmbedAsync(SanitizeWebhookUrl, "Mocha Sanitize", content, 0xE67E22);
        }
        public static void LogReport(string content)
        {
            _ = SendEmbedAsync(ReportWebhookUrl, "Mocha Reports", content, 0x6F4E37);
        }

        public static void LogBan(
            long targetPlayerId,
            string? targetUsername,
            long bannedByPlayerId,
            string? bannedByUsername,
            string reason,
            int durationSeconds,
            IEnumerable<(long PlayerId, string? Username)>? linkedAccounts = null,
            IEnumerable<(string Platform, string PlatformId)>? platformIdentities = null)
        {
            _ = SendBanEmbedAsync(
                targetPlayerId,
                targetUsername,
                bannedByPlayerId,
                bannedByUsername,
                reason,
                durationSeconds,
                linkedAccounts,
                platformIdentities);
        }

        private static async Task SendBanEmbedAsync(
            long targetPlayerId,
            string? targetUsername,
            long bannedByPlayerId,
            string? bannedByUsername,
            string reason,
            int durationSeconds,
            IEnumerable<(long PlayerId, string? Username)>? linkedAccounts,
            IEnumerable<(string Platform, string PlatformId)>? platformIdentities)
        {
            if (WebhookUrl == null)
                return;

            try
            {
                string targetName = CleanDiscordValue(targetUsername, "Unknown", 100);
                string bannedByName = CleanDiscordValue(bannedByUsername, "Unknown", 100);
                string reasonValue = CleanDiscordValue(reason, "No reason provided.", 1_000);

                string durationValue = durationSeconds <= 0 || durationSeconds == int.MaxValue
                    ? "Permanent"
                    : FormatDuration(durationSeconds);

                var linked = (linkedAccounts ?? Enumerable.Empty<(long, string?)>())
                    .Where(account => account.Item1 != targetPlayerId)
                    .ToList();

                var fields = new List<object>
                {
                    new
                    {
                        name = "🎯 Banned Player",
                        value = $"**{targetName}**\n`Player ID: {targetPlayerId}`",
                        inline = true
                    },
                    new
                    {
                        name = "🛡️ Banned By",
                        value = $"**{bannedByName}**\n`Player ID: {bannedByPlayerId}`",
                        inline = true
                    },
                    new
                    {
                        name = "⏱️ Duration",
                        value = $"**{durationValue}**",
                        inline = true
                    },
                    new
                    {
                        name = "📋 Reason",
                        value = reasonValue,
                        inline = false
                    }
                };

                if (platformIdentities != null)
                {
                    string identityList = string.Join(
                        "\n",
                        platformIdentities.Select(identity =>
                            $"`{CleanDiscordValue(identity.Platform, "Unknown", 30)}: " +
                            $"{CleanDiscordValue(identity.PlatformId, "unknown", 80)}`"));
                    if (!string.IsNullOrWhiteSpace(identityList))
                    {
                        fields.Add(new
                        {
                            name = "🔗 Linked Platform IDs",
                            value = identityList,
                            inline = false
                        });
                    }
                }

                if (linked.Count > 0)
                {
                    string linkedList = string.Join(
                        "\n",
                        linked.Select(account =>
                            $"**{CleanDiscordValue(account.Item2, "Unknown", 60)}** " +
                            $"`(ID: {account.Item1})`"));
                    fields.Add(new
                    {
                        name = $"👥 Also Banned (linked accounts, {linked.Count})",
                        value = linkedList.Length > 1000 ? linkedList[..1000] + "..." : linkedList,
                        inline = false
                    });
                }

                var payload = new
                {
                    username = "Mocha Moderation",
                    allowed_mentions = new { parse = Array.Empty<string>() },
                    embeds = new[]
                    {
                        new
                        {
                            title = "🔨 Player Banned",
                            description = $"### {targetName} was banned",
                            color = 0xC0392B,
                            fields = fields.ToArray(),
                            footer = new { text = "Mocha Moderation" },
                            timestamp = DateTimeOffset.UtcNow.ToString("O")
                        }
                    }
                };

                string json = JsonSerializer.Serialize(payload);
                using var body = new StringContent(json, Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await _client.PostAsync(WebhookUrl, body);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine(
                        $"[DISCORD BAN] Webhook returned {(int)response.StatusCode} {response.StatusCode}");
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[DISCORD BAN] Failed sending ban log: {exception.Message}");
            }
        }

        private static string FormatDuration(int totalSeconds)
        {
            if (totalSeconds <= 0)
                return "Permanent";

            TimeSpan span = TimeSpan.FromSeconds(totalSeconds);
            if (span.TotalDays >= 1)
                return $"{span.TotalDays:0.##} day(s) ({totalSeconds}s)";
            if (span.TotalHours >= 1)
                return $"{span.TotalHours:0.##} hour(s) ({totalSeconds}s)";
            if (span.TotalMinutes >= 1)
                return $"{span.TotalMinutes:0.##} minute(s) ({totalSeconds}s)";
            return $"{totalSeconds} second(s)";
        }

        public static void LogPlayerReport(
            Guid reportId,
            long reporterId,
            string? reporterUsername,
            long? reportedPlayerId,
            string? reportedUsername,
            int? reportCategory,
            string? details,
            long? roomId,
            string? roomInstanceType)
        {
            _ = SendPlayerReportAsync(
                reportId,
                reporterId,
                reporterUsername,
                reportedPlayerId,
                reportedUsername,
                reportCategory,
                details,
                roomId,
                roomInstanceType);
        }

        private static async Task SendPlayerReportAsync(
            Guid reportId,
            long reporterId,
            string? reporterUsername,
            long? reportedPlayerId,
            string? reportedUsername,
            int? reportCategory,
            string? details,
            long? roomId,
            string? roomInstanceType)
        {
            if (ReportWebhookUrl == null)
                return;

            try
            {
                string reporterName = CleanDiscordValue(
                    reporterUsername,
                    "Unknown reporter",
                    100);

                string reportedName = CleanDiscordValue(
                    reportedUsername,
                    "Unknown player",
                    100);

                string reportDetails = CleanDiscordValue(
                    details,
                    "No additional details were provided.",
                    1_000);

                string categoryName = GetReportCategoryName(reportCategory);

                string reportedPlayerValue = reportedPlayerId.HasValue
                    ? $"**{reportedName}**\n`Player ID: {reportedPlayerId.Value}`"
                    : $"**{reportedName}**\n`Player ID unavailable`";

                string reporterValue =
                    $"**{reporterName}**\n`Player ID: {reporterId}`";

                string roomValue = roomId.HasValue
                    ? $"`Room ID: {roomId.Value}`\n" +
                      $"Instance: **{CleanDiscordValue(roomInstanceType, "Unknown", 50)}**"
                    : "**Unknown room**";

                var payload = new
                {
                    username = "Mocha Reports",

                    allowed_mentions = new
                    {
                        parse = Array.Empty<string>()
                    },

                    embeds = new[]
                    {
                new
                {
                    title = "🚨 New Player Report",

                    description =
                        $"### {reportedName} was reported\n" +
                        reportDetails,

                    color = 0x6F4E37,

                    fields = new[]
                    {
                        new
                        {
                            name = "🎯 Reported Player",
                            value = reportedPlayerValue,
                            inline = true
                        },
                        new
                        {
                            name = "👤 Reported By",
                            value = reporterValue,
                            inline = true
                        },
                        new
                        {
                            name = "📋 Category",
                            value = $"**{categoryName}**",
                            inline = false
                        },
                        new
                        {
                            name = "🏠 Room",
                            value = roomValue,
                            inline = false
                        }
                    },

                    footer = new
                    {
                        text = $"Mocha Moderation • Report {reportId}"
                    },

                    timestamp = DateTimeOffset.UtcNow.ToString("O")
                }
            }
                };

                string json = JsonSerializer.Serialize(payload);

                using var body = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json");

                using HttpResponseMessage response =
                    await _client.PostAsync(ReportWebhookUrl, body);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine(
                        $"[DISCORD REPORT] Webhook returned " +
                        $"{(int)response.StatusCode} {response.StatusCode}");
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[DISCORD REPORT] Failed sending report: {exception.Message}");
            }
        }

        private static string CleanDiscordValue(
            string? value,
            string fallback,
            int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            string cleaned = value
                .Trim()
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("`", "'");

            if (cleaned.Length > maximumLength)
                cleaned = cleaned[..maximumLength] + "...";

            return cleaned;
        }

        private static string GetReportCategoryName(int? category)
        {
            if (!category.HasValue)
                return "Unspecified";

            return category.Value switch
            {
                104 => "Player Conduct",
                _ => $"Report Category {category.Value}"
            };
        }

        private static async Task SendEmbedAsync(
            Uri? webhookUrl,
            string senderName,
            string content,
            int color)
        {
            if (webhookUrl == null)
                return;

            try
            {
                if (content.Length > 4_000)
                    content = content[..4_000] + "...";

                var payload = JsonSerializer.Serialize(new
                {
                    username = senderName,
                    allowed_mentions = new { parse = Array.Empty<string>() },
                    embeds = new[]
                    {
                        new
                        {
                            description = content,
                            color,
                            timestamp = DateTimeOffset.UtcNow.ToString("O")
                        }
                    }
                });
                var body = new StringContent(payload, Encoding.UTF8, "application/json");
                await _client.PostAsync(webhookUrl, body);
            }
            catch
            {

            }
        }

        public static void LogLogin(long playerId, string? username = null)
        {
            Log($"🟢 **Login** — `{username ?? "unknown"}` (ID: `{playerId}`)");
        }

        public static void LogChat(long playerId, string message, string? username = null)
        {

            var safeMsg = message.Length > 1500 ? message[..1500] + "..." : message;
            Log($"💬 **Chat** — `{username ?? "unknown"}` (ID: `{playerId}`): {safeMsg}");
        }

        public static void LogSanitizeChat(long playerId, string message, string? username = null)
        {
            var safeMsg = message.Length > 1500 ? message[..1500] + "..." : message;
            LogSanitize($"**Sanitize** - `{username ?? "unknown"}` (ID: `{playerId}`): {safeMsg}");
        }

        public static void LogImage(string content, byte[] imageBytes, string fileName)
        {
            _ = SendImageAsync(
                WebhookUrl,
                content,
                imageBytes,
                fileName,
                "Mocha Server Logs");
        }

        public static void LogPhotoUpload(long playerId, string? username, byte[] imageBytes, string fileName)
        {
            LogImage($"📸 **Photo taken** by `{username ?? "unknown"}` (ID: `{playerId}`)", imageBytes, fileName);
        }

        public static void LogShareCameraPhoto(
            long playerId,
            string? username,
            byte[] imageBytes,
            string fileName,
            long? roomId,
            string? roomName)
        {
            string safeRoomName = string.IsNullOrWhiteSpace(roomName)
                ? roomId.HasValue ? $"Room {roomId.Value}" : "Rooms"
                : roomName.Replace("[", "\\[").Replace("]", "\\]");
            string roomUrl = roomId.HasValue
                ? $"{ServerConfig.BaseURL.TrimEnd('/')}/recnet/#room/{roomId.Value}"
                : $"{ServerConfig.BaseURL.TrimEnd('/')}/recnet/#rooms";
            string content =
                $"📸 **Share Camera photo** by `{username ?? "unknown"}` " +
                $"(ID: `{playerId}`)\n" +
                $"🏠 Room: [^{safeRoomName}]({roomUrl})";

            _ = SendImageAsync(
                ShareCameraWebhookUrl,
                content,
                imageBytes,
                fileName,
                "Sharecam Social");
        }

        private static async Task SendImageAsync(
            Uri? webhookUrl,
            string content,
            byte[] imageBytes,
            string fileName,
            string senderName)
        {
            if (webhookUrl == null || imageBytes.Length == 0 ||
                imageBytes.Length > 10 * 1024 * 1024)
            {
                return;
            }

            try
            {
                using var form = new MultipartFormDataContent();

                fileName = Path.GetFileName(fileName);
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = "upload.png";
                if (fileName.Length > 120)
                    fileName = fileName[^120..];

                var payload = JsonSerializer.Serialize(new
                {
                    username = senderName,
                    content = content.Length > 1_500 ? content[..1_500] : content,
                    allowed_mentions = new { parse = Array.Empty<string>() },
                    attachments = new[] { new { id = 0, filename = fileName } }
                });
                form.Add(new StringContent(payload, Encoding.UTF8, "application/json"), "payload_json");

                var extension = Path.GetExtension(fileName).ToLowerInvariant();
                var mimeType = extension switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    _ => "image/png"
                };

                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);

                form.Add(imageContent, "files[0]", fileName);

                using var response = await _client.PostAsync(webhookUrl, form);
            }
            catch
            {

            }
        }
    }
}
