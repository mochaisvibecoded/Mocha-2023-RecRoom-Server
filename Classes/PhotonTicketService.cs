using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Mocha2023.Classes
{

    public static class PhotonTicketService
    {
        private const string TicketVersion = "v1";
        private static readonly TimeSpan TicketLifetime = TimeSpan.FromHours(2);
        private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

        public static string Issue(
            long playerId,
            long? roomInstanceId,
            long? roomId,
            string? displayName)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            var payload = new PhotonTicketPayload
            {
                PlayerId = playerId,
                RoomInstanceId = roomInstanceId,
                RoomId = roomId,
                DisplayName = displayName ?? string.Empty,
                IssuedAtUnix = now.ToUnixTimeSeconds(),
                ExpiresAtUnix = now.Add(TicketLifetime).ToUnixTimeSeconds(),
                Nonce = Base64UrlEncode(RandomNumberGenerator.GetBytes(16))
            };

            byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            string encodedPayload = Base64UrlEncode(payloadBytes);
            string signingInput = $"{TicketVersion}.{encodedPayload}";
            string signature = Base64UrlEncode(Sign(signingInput));
            string token = $"{signingInput}.{signature}";

            Console.WriteLine(
                $"[PHOTON TICKET] Issued player={playerId} " +
                $"room={roomId?.ToString() ?? "none"} " +
                $"instance={roomInstanceId?.ToString() ?? "none"} " +
                $"fingerprint={GetFingerprint(token)}");

            return token;
        }

        public static bool TryValidate(string? token, out PhotonTicket ticket)
        {
            return TryValidate(token, out ticket, out _);
        }

        public static bool TryValidate(
            string? token,
            out PhotonTicket ticket,
            out string failureReason)
        {
            ticket = null!;
            failureReason = string.Empty;

            token = NormalizeToken(token);
            if (string.IsNullOrWhiteSpace(token))
            {
                failureReason = "missing";
                return false;
            }

            string[] parts = token.Split('.', StringSplitOptions.None);
            if (parts.Length != 3 ||
                !string.Equals(parts[0], TicketVersion, StringComparison.Ordinal))
            {
                failureReason = "wrong-format";
                return false;
            }

            string signingInput = $"{parts[0]}.{parts[1]}";
            byte[] expectedSignature = Sign(signingInput);

            if (!TryBase64UrlDecode(parts[2], out byte[] suppliedSignature) ||
                suppliedSignature.Length != expectedSignature.Length ||
                !CryptographicOperations.FixedTimeEquals(
                    suppliedSignature,
                    expectedSignature))
            {
                failureReason = "bad-signature";
                return false;
            }

            if (!TryBase64UrlDecode(parts[1], out byte[] payloadBytes))
            {
                failureReason = "bad-payload-encoding";
                return false;
            }

            PhotonTicketPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<PhotonTicketPayload>(payloadBytes);
            }
            catch (JsonException)
            {
                failureReason = "bad-payload-json";
                return false;
            }

            if (payload == null || payload.PlayerId <= 0)
            {
                failureReason = "bad-player";
                return false;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            long nowUnix = now.ToUnixTimeSeconds();

            if (payload.ExpiresAtUnix <= nowUnix)
            {
                failureReason = "expired";
                return false;
            }

            if (payload.IssuedAtUnix > now.Add(AllowedClockSkew).ToUnixTimeSeconds())
            {
                failureReason = "issued-in-future";
                return false;
            }

            ticket = new PhotonTicket
            {
                PlayerId = payload.PlayerId,
                RoomInstanceId = payload.RoomInstanceId,
                RoomId = payload.RoomId,
                DisplayName = payload.DisplayName ?? string.Empty,
                IssuedAt = DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAtUnix),
                ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnix)
            };

            return true;
        }

        public static string GetFingerprint(string? token)
        {
            token = NormalizeToken(token);
            if (string.IsNullOrWhiteSpace(token))
                return "none";

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(hash.AsSpan(0, 6));
        }

        private static byte[] Sign(string signingInput)
        {
            byte[] key = Encoding.UTF8.GetBytes(ServerConfig.PhotonCustomAuthSecret);
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        }

        private static string? NormalizeToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            string normalized = token.Trim().Trim('"');
            const string bearerPrefix = "Bearer ";
            if (normalized.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                normalized = normalized[bearerPrefix.Length..].Trim();

            return normalized;
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static bool TryBase64UrlDecode(string value, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();

            try
            {
                string base64 = value
                    .Replace('-', '+')
                    .Replace('_', '/');

                int padding = base64.Length % 4;
                if (padding == 2)
                    base64 += "==";
                else if (padding == 3)
                    base64 += "=";
                else if (padding == 1)
                    return false;

                bytes = Convert.FromBase64String(base64);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private sealed class PhotonTicketPayload
        {
            public long PlayerId { get; set; }
            public long? RoomInstanceId { get; set; }
            public long? RoomId { get; set; }
            public string? DisplayName { get; set; }
            public long IssuedAtUnix { get; set; }
            public long ExpiresAtUnix { get; set; }
            public string? Nonce { get; set; }
        }
    }

    public sealed class PhotonTicket
    {
        public long PlayerId { get; init; }
        public long? RoomInstanceId { get; init; }
        public long? RoomId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public DateTimeOffset IssuedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
    }
}
