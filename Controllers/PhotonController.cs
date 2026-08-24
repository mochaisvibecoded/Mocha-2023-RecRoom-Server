using Mocha2023.Classes;
using Mocha2023.Classes.DBs;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Mocha2023.Auth;

namespace Mocha2023.Controllers
{
    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    public class PhotonController : ControllerBase
    {
        private static readonly string[] TokenKeys =
        {
            "token",
            "ticket",
            "accessToken",
            "authToken",
            "photonAccessToken",
            "PhotonAccessToken",
            "rrToken"
        };

        private static readonly string[] UserIdKeys =
        {
            "userId",
            "userid",
            "user",
            "accountId",
            "playerId"
        };

        [HttpGet("/api/photon/config")]
        [HttpGet("/photon/config")]
        public IActionResult GetPhotonConfig()
        {
            Response.Headers.CacheControl = "no-store";

            return Ok(new
            {
                Enabled = ServerConfig.PhotonEnabled,
                RealtimeAppId = ServerConfig.PhotonRealtimeAppId,
                VoiceAppId = ServerConfig.PhotonVoiceAppId,
                AppVersion = ServerConfig.PhotonAppVersion,
                Region = ServerConfig.PhotonRegion,
                UseCustomAuthentication = true,
                AuthenticationParameter = "token"
            });
        }

        [HttpGet("/voice/config")]
        public IActionResult GetVoiceConfig()
        {
            Response.Headers.CacheControl = "no-store";

            return Ok(new
            {
                Enabled = ServerConfig.PhotonEnabled &&
                          !string.IsNullOrWhiteSpace(ServerConfig.PhotonVoiceAppId),
                AppId = ServerConfig.PhotonVoiceAppId,
                AppIdVoice = ServerConfig.PhotonVoiceAppId,
                PhotonVoiceAppId = ServerConfig.PhotonVoiceAppId,
                AppVersion = ServerConfig.PhotonAppVersion,
                Region = ServerConfig.PhotonRegion,
                UseCustomAuthentication = true,
                AuthenticationParameter = "token"
            });
        }

        [HttpGet("/photon/authenticate")]
        public IActionResult AuthenticateGet()
        {
            var values = ReadQueryValues();
            return Authenticate(values);
        }

        [HttpPost("/photon/authenticate")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> AuthenticatePost()
        {
            var values = ReadQueryValues();

            try
            {
                if (Request.HasFormContentType)
                {
                    var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
                    foreach (var pair in form)
                        values[pair.Key] = pair.Value.FirstOrDefault() ?? string.Empty;
                }
                else if (Request.ContentLength.GetValueOrDefault() > 0)
                {
                    using var reader = new StreamReader(
                        Request.Body,
                        Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: false,
                        leaveOpen: true);

                    string body = await reader.ReadToEndAsync();
                    MergeBodyValues(values, body, Request.ContentType);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PHOTON AUTH] Could not read request body: {ex.Message}");
                return PhotonResult(3, "Invalid authentication request.");
            }

            return Authenticate(values);
        }

        private IActionResult Authenticate(Dictionary<string, string> values)
        {
            Response.Headers.CacheControl = "no-store";

            string? configuredSecret = ServerConfig.PhotonCustomAuthSecret;
            if (!string.IsNullOrWhiteSpace(configuredSecret))
            {
                string? suppliedSecret = GetFirst(values, "secret", "apiKey", "key");
                if (!FixedTimeEquals(configuredSecret, suppliedSecret))
                {
                    Console.WriteLine("[PHOTON AUTH] Rejected request with invalid dashboard secret.");
                    return PhotonResult(2, "Authentication failed.");
                }
            }

            string? token = GetFirst(values, TokenKeys);
            if (string.IsNullOrWhiteSpace(token))
                return PhotonResult(3, "Missing Photon access token.");

            long playerId;
            long? roomId = null;
            long? roomInstanceId = null;
            string ticketDisplayName = string.Empty;
            string authSource;

            if (PhotonTicketService.TryValidate(
                    token,
                    out PhotonTicket ticket,
                    out string failureReason))
            {
                playerId = ticket.PlayerId;
                roomId = ticket.RoomId;
                roomInstanceId = ticket.RoomInstanceId;
                ticketDisplayName = ticket.DisplayName;
                authSource = "photon-ticket";
            }
            else
            {

                long? apiPlayerId = TryValidateApiAccessToken(token);
                if (apiPlayerId == null)
                {
                    Console.WriteLine(
                        $"[PHOTON AUTH] Rejected token " +
                        $"ticketReason={failureReason} " +
                        $"apiToken=invalid " +
                        $"fingerprint={PhotonTicketService.GetFingerprint(token)} " +
                        $"length={token.Length} " +
                        $"keys={string.Join(',', values.Keys)}");

                    return PhotonResult(2, "Photon access token is invalid or expired.");
                }

                playerId = apiPlayerId.Value;
                authSource = "api-access-token";
            }

            string? requestedUserId = GetFirst(values, UserIdKeys);
            if (!string.IsNullOrWhiteSpace(requestedUserId) &&
                !string.Equals(
                    requestedUserId.Trim(),
                    playerId.ToString(),
                    StringComparison.Ordinal))
            {
                Console.WriteLine(
                    $"[PHOTON AUTH] User mismatch: requested={requestedUserId}, " +
                    $"validated={playerId}, source={authSource}");
                return PhotonResult(2, "Photon user does not match the access token.");
            }

            var player = PlayerDB.Players.FindById(playerId);
            if (player?.Player == null)
                return PhotonResult(2, "Player account no longer exists.");

            string nickname = !string.IsNullOrWhiteSpace(player.Player.DisplayName)
                ? player.Player.DisplayName!
                : player.Player.Username ?? ticketDisplayName;

            Console.WriteLine(
                $"[PHOTON AUTH] player={playerId} " +
                $"source={authSource} " +
                $"room={roomId?.ToString() ?? "none"} " +
                $"instance={roomInstanceId?.ToString() ?? "none"} accepted");

            var response = new Dictionary<string, object?>
            {
                ["ResultCode"] = 1,
                ["UserId"] = playerId.ToString(),
                ["Nickname"] = nickname,
                ["AuthCookie"] = new Dictionary<string, object?>
                {

                    ["PlayerId"] = playerId.ToString(),
                    ["RoomId"] = roomId?.ToString() ?? string.Empty,
                    ["RoomInstanceId"] =
                        roomInstanceId?.ToString() ?? string.Empty
                }
            };

            return new JsonResult(response);
        }

        private long? TryValidateApiAccessToken(string token)
        {
            bool hadAuthorization = Request.Headers.ContainsKey("Authorization");
            var originalAuthorization = Request.Headers["Authorization"];

            try
            {

                Request.Headers["Authorization"] = $"Bearer {token.Trim()}";
                return AuthStuff.GetPlayerId(Request);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PHOTON AUTH] API token validation failed: {ex.Message}");
                return null;
            }
            finally
            {
                if (hadAuthorization)
                    Request.Headers["Authorization"] = originalAuthorization;
                else
                    Request.Headers.Remove("Authorization");
            }
        }

        private IActionResult PhotonResult(int resultCode, string message)
        {
            return new JsonResult(new Dictionary<string, object?>
            {
                ["ResultCode"] = resultCode,
                ["Message"] = message
            });
        }

        private Dictionary<string, string> ReadQueryValues()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Request.Query)
                result[pair.Key] = pair.Value.FirstOrDefault() ?? string.Empty;
            return result;
        }

        private static void MergeBodyValues(
            Dictionary<string, string> values,
            string body,
            string? contentType)
        {
            if (string.IsNullOrWhiteSpace(body))
                return;

            if (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ||
                body.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                using JsonDocument document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return;

                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    values[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.ToString();
                }

                return;
            }

            foreach (string part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = part.Split('=', 2);
                string key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
                string value = pair.Length > 1
                    ? Uri.UnescapeDataString(pair[1].Replace('+', ' '))
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(key))
                    values[key] = value;
            }
        }

        private static string? GetFirst(
            IReadOnlyDictionary<string, string> values,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                if (values.TryGetValue(key, out string? value) &&
                    !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static bool FixedTimeEquals(string expected, string? supplied)
        {
            if (supplied == null)
                return false;

            byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
            byte[] suppliedBytes = Encoding.UTF8.GetBytes(supplied);

            return expectedBytes.Length == suppliedBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
        }
    }
}
