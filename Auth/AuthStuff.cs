using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mocha2023.Classes.DBs;
using static Mocha2023.Classes.DBs.DBClasses.PlayerDBClasses;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Mocha2023.Auth
{
    public static class AuthStuff
    {
        private const string Issuer = "mocha";

        private static readonly string Secret = LoadSecret();

        private static readonly string[] ExtraTokenHeaders =
        {
            "X-Access-Token",
            "X-Auth-Token",
            "X-RecNet-Token",
            "X-Authorization"
        };

        private static readonly string[] TokenCookies =
        {
            "recnet_session",
            "access_token",
            "auth_token"
        };

        private static readonly string[] TokenQueryParameters =
        {
            "access_token",
            "token"
        };

        private static string LoadSecret()
        {
            string? configured =
                Program.LoadLocalSetting("JWT_SECRET");

            if (!string.IsNullOrWhiteSpace(configured) &&
                Encoding.UTF8.GetByteCount(configured) >= 32)
            {
                return configured;
            }

            string authDirectory =
                Path.Combine(Program.dataDir, "Auth");

            string secretPath =
                Path.Combine(authDirectory, "jwt-secret");

            Directory.CreateDirectory(authDirectory);

            if (File.Exists(secretPath))
            {
                string persisted =
                    File.ReadAllText(secretPath).Trim();

                if (Encoding.UTF8.GetByteCount(persisted) >= 32)
                {
                    RestrictSecretPermissions(secretPath);
                    return persisted;
                }
            }

            string generated = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(64));

            File.WriteAllText(secretPath, generated);
            RestrictSecretPermissions(secretPath);

            return generated;
        }

        private static void RestrictSecretPermissions(string secretPath)
        {
            if (OperatingSystem.IsWindows())
                return;

            try
            {
                File.SetUnixFileMode(
                    secretPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[AUTH SECURITY] Could not restrict JWT secret permissions: " +
                    exception.Message);
            }
        }

        public static string Encode(long accountId)
        {
            if (accountId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(accountId),
                    "The account ID must be positive.");
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(Secret));

            var credentials = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

            var player =
                PlayerDB.Players.FindById(accountId);

            List<string> roles =
                player?.PlayerRoles?
                    .Select(role =>
                        role.ToString().ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                ?? new List<string>();

            DateTime now = DateTime.UtcNow;

            var claims = new List<Claim>
            {
                new(
                    JwtRegisteredClaimNames.Sub,
                    accountId.ToString()),

                new(
                    "account_id",
                    accountId.ToString(),
                    ClaimValueTypes.Integer64),

                new(
                    "role",
                    JsonSerializer.Serialize(roles),
                    JsonClaimValueTypes.JsonArray),

                new(
                    JwtRegisteredClaimNames.Jti,
                    Guid.NewGuid().ToString("N")),

                new(
                    JwtRegisteredClaimNames.Iat,
                    new DateTimeOffset(now)
                        .ToUnixTimeSeconds()
                        .ToString(),
                    ClaimValueTypes.Integer64)
            };

            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: null,
                claims: claims,

                notBefore: now.AddMinutes(-2),
                expires: now.AddDays(30),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler()
                .WriteToken(token);
        }

        public static long? GetPlayerId(HttpRequest request)
        {
            if (!TryGetAccessToken(
                    request,
                    out string token,
                    out string tokenSource))
            {
                return null;
            }

            try
            {
                var handler = new JwtSecurityTokenHandler
                {

                    MapInboundClaims = false
                };

                var key = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(Secret));

                ClaimsPrincipal principal =
                    handler.ValidateToken(
                        token,
                        new TokenValidationParameters
                        {
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = key,

                            ValidateIssuer = true,
                            ValidIssuer = Issuer,

                            ValidateAudience = false,

                            ValidateLifetime = true,
                            RequireExpirationTime = true,
                            RequireSignedTokens = true,

                            ValidAlgorithms = new[]
                            {
                                SecurityAlgorithms.HmacSha256
                            },

                            ClockSkew = TimeSpan.FromMinutes(2),

                            NameClaimType =
                                JwtRegisteredClaimNames.Sub,

                            RoleClaimType = "role"
                        },
                        out SecurityToken validatedToken);

                if (validatedToken is not JwtSecurityToken jwt ||
                    !string.Equals(
                        jwt.Header.Alg,
                        SecurityAlgorithms.HmacSha256,
                        StringComparison.Ordinal))
                {
                    return null;
                }

                string? accountIdText =
                    principal.FindFirst(
                        JwtRegisteredClaimNames.Sub)?.Value
                    ?? principal.FindFirst(
                        ClaimTypes.NameIdentifier)?.Value
                    ?? principal.FindFirst(
                        "account_id")?.Value
                    ?? jwt.Claims.FirstOrDefault(
                        claim =>
                            claim.Type ==
                            JwtRegisteredClaimNames.Sub)?.Value;

                if (!long.TryParse(
                        accountIdText,
                        out long accountId) ||
                    accountId <= 0)
                {
                    Console.WriteLine(
                        $"[AUTH JWT REJECTED] " +
                        $"source={tokenSource} missing_account_id");

                    return null;
                }

                if (PlayerDB.Players.FindById(accountId)?.Player == null)
                {
                    Console.WriteLine(
                        $"[AUTH JWT REJECTED] source={tokenSource} unknown_account");
                    return null;
                }

                if (RecNetDB.ModerationLocks.Exists(
                        moderationLock =>
                            moderationLock.AccountId == accountId))
                {
                    Console.WriteLine(
                        $"[AUTH JWT REJECTED] source={tokenSource} account_locked");
                    return null;
                }

                return accountId;
            }
            catch (SecurityTokenExpiredException)
            {
                Console.WriteLine(
                    $"[AUTH JWT EXPIRED] source={tokenSource}");

                return null;
            }
            catch (SecurityTokenException exception)
            {
                Console.WriteLine(
                    $"[AUTH JWT REJECTED] " +
                    $"source={tokenSource} " +
                    $"reason={exception.GetType().Name}");

                return null;
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[AUTH JWT ERROR] " +
                    $"source={tokenSource} " +
                    $"reason={exception.GetType().Name}");

                return null;
            }
        }

        private static bool TryGetAccessToken(
            HttpRequest request,
            out string token,
            out string source)
        {
            token = string.Empty;
            source = string.Empty;

            foreach (string? value in
                     request.Headers.Authorization)
            {
                if (TryNormalizeToken(value, out token))
                {
                    source = "Authorization";
                    return true;
                }
            }

            foreach (string headerName in ExtraTokenHeaders)
            {
                if (!request.Headers.TryGetValue(
                        headerName,
                        out var values))
                {
                    continue;
                }

                foreach (string? value in values)
                {
                    if (TryNormalizeToken(value, out token))
                    {
                        source = headerName;
                        return true;
                    }
                }
            }

            if (request.Path.StartsWithSegments(
                    "/recnet",
                    StringComparison.OrdinalIgnoreCase))
            {
                foreach (string cookieName in TokenCookies)
                {
                    if (request.Cookies.TryGetValue(
                            cookieName,
                            out string? value) &&
                        TryNormalizeToken(value, out token))
                    {
                        source = $"cookie:{cookieName}";
                        return true;
                    }
                }
            }

            if (request.Path.StartsWithSegments(
                    "/noti/hub/v1",
                    StringComparison.OrdinalIgnoreCase))
            {
                foreach (string parameter in TokenQueryParameters)
                {
                    if (!request.Query.TryGetValue(
                            parameter,
                            out var values))
                    {
                        continue;
                    }

                    foreach (string? value in values)
                    {
                        if (TryNormalizeToken(value, out token))
                        {
                            source = $"query:{parameter}";
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool TryNormalizeToken(
            string? input,
            out string token)
        {
            token = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string value = Unquote(input.Trim());

            if (value.Length is < 16 or > 8192)
            {
                return false;
            }

            int whitespaceIndex =
                value.IndexOfAny(new[] { ' ', '\t' });

            if (whitespaceIndex > 0)
            {
                string scheme =
                    value[..whitespaceIndex].Trim();

                if (scheme.Equals(
                        "Bearer",
                        StringComparison.OrdinalIgnoreCase) ||
                    scheme.Equals(
                        "Token",
                        StringComparison.OrdinalIgnoreCase) ||
                    scheme.Equals(
                        "JWT",
                        StringComparison.OrdinalIgnoreCase))
                {
                    value =
                        value[(whitespaceIndex + 1)..]
                            .Trim();
                }
            }

            value = Unquote(value.Trim());

            if (value.Length is < 16 or > 8192)
            {
                return false;
            }

            string[] parts = value.Split('.');

            if (parts.Length != 3 ||
                parts.Any(string.IsNullOrWhiteSpace))
            {
                return false;
            }

            token = value;
            return true;
        }

        private static string Unquote(string value)
        {
            if (value.Length < 2 ||
                value[0] != '"' ||
                value[^1] != '"')
            {
                return value;
            }

            try
            {
                return JsonSerializer.Deserialize<string>(value)?
                    .Trim()
                    ?? string.Empty;
            }
            catch
            {
                return value[1..^1].Trim();
            }
        }

        public static FullPlayer? GetCurrentPlayer(
            HttpRequest request)
        {
            long? id = GetPlayerId(request);

            if (!id.HasValue)
            {
                return null;
            }

            if (RecNetDB.ModerationLocks.Exists(
                    moderationLock =>
                        moderationLock.AccountId == id.Value))
            {
                return null;
            }

            return PlayerDB.Players.FindById(id.Value);
        }
    }
}
