using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections.Concurrent;
using System.Net;
using Mocha2023.Classes.DBs;

namespace Mocha2023.Classes
{

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class ApiProtectionAttribute : Attribute, IAsyncResourceFilter, IOrderedFilter
    {
        private sealed class TokenBucket
        {
            public readonly object Sync = new();
            public double Tokens;
            public DateTime LastRefillUtc;
            public DateTime LastSeenUtc;

            public TokenBucket(double capacity, DateTime now)
            {
                Tokens = capacity;
                LastRefillUtc = now;
                LastSeenUtc = now;
            }
        }

        private readonly record struct Policy(
            string Name,
            int BurstCapacity,
            double BurstRefillPerSecond,
            int SustainedCapacity,
            double SustainedRefillPerSecond,
            int MaxConcurrent,
            long MaxBodyBytes);

        private static readonly ConcurrentDictionary<string, TokenBucket> Buckets = new();
        private static readonly ConcurrentDictionary<string, int> ActiveRequests = new();
        private static readonly ConcurrentDictionary<string, int> GlobalActiveRequests = new();
        private const string EacChallengeResponse = "AQAAAGd9O3h2ynQW6Y/1MhdZC8VoHygxyTzmiRvAfpiRtJEBQ+NVaXMStRTsYQk42H1hbB7NGKhIpgfShk+ADtRW9EU/YF5320eGmINJZAqkm3pyX9QF/w1QT4IB2EQfOqTfpryQ3QFchXYqDgg/SbX+/X9mgzssbflIw3OAK+c=";
        private static long _requestCounter;

        public int Order => int.MinValue + 100;

        public async Task OnResourceExecutionAsync(
            ResourceExecutingContext context,
            ResourceExecutionDelegate next)
        {
            HttpRequest request = context.HttpContext.Request;
            ApplySecurityHeaders(context.HttpContext.Response);
            Policy policy = ResolvePolicy(request);
            string requestPath = request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            IPAddress? clientAddress = ClientNetwork.GetClientIp(request);

            if (clientAddress != null &&
                !ShouldSkipIpBan(requestPath) &&
                RecNetDB.TryGetIpBan(clientAddress, out RecNetDB.IpBan? ipBan))
            {
                context.HttpContext.Response.Headers["X-Mocha-Blocked"] = "ip-ban";
                context.Result = Error(
                    StatusCodes.Status403Forbidden,
                    "ip_banned",
                    string.IsNullOrWhiteSpace(ipBan?.Reason)
                        ? "This network is blocked from Mocha."
                        : ipBan.Reason);
                return;
            }

            if (clientAddress != null &&
                RecNetDB.IsVpnBlockingEnabled() &&
                ShouldCheckVpn(requestPath) &&
                await VpnDetectionService.IsVpnOrProxyAsync(
                    clientAddress,
                    context.HttpContext.RequestAborted))
            {
                context.HttpContext.Response.Headers["X-Mocha-Blocked"] = "anonymous-network";
                context.Result = Error(
                    StatusCodes.Status403Forbidden,
                    "vpn_or_proxy_blocked",
                    "VPN, proxy, Tor, and hosting-provider connections are not allowed on Mocha.");
                return;
            }

            if ((request.QueryString.Value?.Length ?? 0) > 8_192)
            {
                context.Result = Error(
                    StatusCodes.Status414UriTooLong,
                    "query_too_large",
                    "The query string is too large.");
                return;
            }

            var maxBodyFeature = context.HttpContext.Features.Get<
                Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (maxBodyFeature != null &&
                !maxBodyFeature.IsReadOnly &&
                (!maxBodyFeature.MaxRequestBodySize.HasValue ||
                 maxBodyFeature.MaxRequestBodySize.Value > policy.MaxBodyBytes))
            {

                maxBodyFeature.MaxRequestBodySize = policy.MaxBodyBytes;
            }

            if (request.ContentLength.HasValue &&
                request.ContentLength.Value > policy.MaxBodyBytes)
            {
                context.Result = Error(
                    StatusCodes.Status413PayloadTooLarge,
                    "payload_too_large",
                    "The request body is too large for this endpoint.");
                return;
            }

            string clientKey = GetClientKey(request);

            string routeKey = $"{clientKey}:{GetRateLimitRouteKey(request, policy.Name)}";

            bool burstAllowed = TryConsume(
                routeKey + ":burst",
                policy.BurstCapacity,
                policy.BurstRefillPerSecond,
                out int burstRetryAfter);
            bool sustainedAllowed = TryConsume(
                routeKey + ":sustained",
                policy.SustainedCapacity,
                policy.SustainedRefillPerSecond,
                out int sustainedRetryAfter);

            if (!burstAllowed || !sustainedAllowed)
            {
                int retryAfter = Math.Max(1, Math.Max(burstRetryAfter, sustainedRetryAfter));
                context.HttpContext.Response.Headers["Retry-After"] = retryAfter.ToString();
                context.Result = Error(
                    StatusCodes.Status429TooManyRequests,
                    "rate_limited",
                    "Too many requests. Slow down and try again.",
                    new { retryAfterSeconds = retryAfter });
                return;
            }

            int active = ActiveRequests.AddOrUpdate(routeKey, 1, (_, current) => current + 1);
            if (active > policy.MaxConcurrent)
            {
                DecrementActive(routeKey, ActiveRequests);
                context.HttpContext.Response.Headers["Retry-After"] = "1";
                context.Result = Error(
                    StatusCodes.Status429TooManyRequests,
                    "too_many_concurrent_requests",
                    "Too many requests are already running for this client.",
                    new { retryAfterSeconds = 1 });
                return;
            }

            int globalLimit = GetGlobalConcurrencyLimit(policy.Name);
            int globalActive = GlobalActiveRequests.AddOrUpdate(
                policy.Name,
                1,
                (_, current) => current + 1);
            if (globalActive > globalLimit)
            {
                DecrementActive(policy.Name, GlobalActiveRequests);
                DecrementActive(routeKey, ActiveRequests);
                context.HttpContext.Response.Headers["Retry-After"] = "1";
                context.Result = Error(
                    StatusCodes.Status503ServiceUnavailable,
                    "server_busy",
                    "The server is busy. Try again in a moment.",
                    new { retryAfterSeconds = 1 });
                return;
            }

            try
            {
                if (requestPath.Equals("/auth/eac/challenge", StringComparison.Ordinal))
                {
                    context.HttpContext.Response.Headers["Cache-Control"] = "no-store";
                    context.HttpContext.Response.Headers["Pragma"] = "no-cache";
                    context.Result = new JsonResult(EacChallengeResponse)
                    {
                        ContentType = "application/json; charset=utf-8",
                        StatusCode = StatusCodes.Status200OK
                    };
                    return;
                }

                await next();
            }
            finally
            {
                DecrementActive(policy.Name, GlobalActiveRequests);
                DecrementActive(routeKey, ActiveRequests);
                CleanupOccasionally();
            }
        }

        private static Policy ResolvePolicy(HttpRequest request)
        {
            string path = request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            bool isWrite = HttpMethods.IsPost(request.Method) ||
                           HttpMethods.IsPut(request.Method) ||
                           HttpMethods.IsPatch(request.Method) ||
                           HttpMethods.IsDelete(request.Method);

            if (path.Equals("/api/images/v4/uploadsaved", StringComparison.Ordinal))
            {
                return new Policy(
                    "image-upload",
                    BurstCapacity: 12,
                    BurstRefillPerSecond: 12d / 60d,
                    SustainedCapacity: 90,
                    SustainedRefillPerSecond: 90d / 3600d,
                    MaxConcurrent: 3,
                    MaxBodyBytes: 12L * 1024L * 1024L);
            }

            if (path.Equals("/upload", StringComparison.Ordinal))
            {
                return new Policy(
                    "room-blob-upload",
                    BurstCapacity: 30,
                    BurstRefillPerSecond: 30d / 60d,
                    SustainedCapacity: 360,
                    SustainedRefillPerSecond: 360d / 3600d,
                    MaxConcurrent: 3,
                    MaxBodyBytes: 52L * 1024L * 1024L);
            }

            if (path.Contains("community-board/media", StringComparison.Ordinal))
            {
                return new Policy(
                    "admin-media-upload",
                    BurstCapacity: 6,
                    BurstRefillPerSecond: 6d / 60d,
                    SustainedCapacity: 30,
                    SustainedRefillPerSecond: 30d / 3600d,
                    MaxConcurrent: 1,
                    MaxBodyBytes: 106L * 1024L * 1024L);
            }

            if (isWrite &&
                (path.Equals("/recnet/api/admin/rooms/import", StringComparison.Ordinal) ||
                 path.Equals("/recnet/api/admin/rooms", StringComparison.Ordinal)))
            {
                return new Policy(
                    "admin-room-import",
                    BurstCapacity: 3,
                    BurstRefillPerSecond: 3d / 300d,
                    SustainedCapacity: 8,
                    SustainedRefillPerSecond: 8d / 3600d,
                    MaxConcurrent: 1,
                    MaxBodyBytes: 512L * 1024L * 1024L);
            }

            if (path.Equals("/recnet/api/auth/register", StringComparison.Ordinal))
            {
                return new Policy(
                    "account-creation",
                    BurstCapacity: 5,
                    BurstRefillPerSecond: 5d / 600d,
                    SustainedCapacity: 12,
                    SustainedRefillPerSecond: 12d / 3600d,
                    MaxConcurrent: 2,
                    MaxBodyBytes: 64L * 1024L);
            }

            if (path.Equals("/photon/authenticate", StringComparison.Ordinal))
            {
                return new Policy(
                    "photon-callback",
                    BurstCapacity: 1_000,
                    BurstRefillPerSecond: 100,
                    SustainedCapacity: 100_000,
                    SustainedRefillPerSecond: 100_000d / 3600d,
                    MaxConcurrent: 64,
                    MaxBodyBytes: 128L * 1024L);
            }

            if (path.Equals("/auth/connect/token", StringComparison.Ordinal))
            {
                return new Policy(
                    "token-exchange",
                    BurstCapacity: 300,
                    BurstRefillPerSecond: 15,
                    SustainedCapacity: 20_000,
                    SustainedRefillPerSecond: 20_000d / 3600d,
                    MaxConcurrent: 24,
                    MaxBodyBytes: 2L * 1024L * 1024L);
            }

            if (path.StartsWith("/auth/role/", StringComparison.Ordinal) ||
                path.Equals("/auth/eac/challenge", StringComparison.Ordinal) ||
                path.Equals("/api/photon/config", StringComparison.Ordinal) ||
                path.Equals("/photon/config", StringComparison.Ordinal) ||
                path.Equals("/voice/config", StringComparison.Ordinal))
            {
                return new Policy(
                    "game-auth-read",
                    BurstCapacity: 1_500,
                    BurstRefillPerSecond: 75,
                    SustainedCapacity: 100_000,
                    SustainedRefillPerSecond: 100_000d / 3600d,
                    MaxConcurrent: 48,
                    MaxBodyBytes: 1L * 1024L * 1024L);
            }

            if (path.Equals("/data/heartbeat", StringComparison.Ordinal) ||
                path.Equals("/match/player/exclusivelogin", StringComparison.Ordinal) ||
                path.Contains("heartbeat", StringComparison.Ordinal))
            {
                return new Policy(
                    "game-session",
                    BurstCapacity: 2_000,
                    BurstRefillPerSecond: 100,
                    SustainedCapacity: 200_000,
                    SustainedRefillPerSecond: 200_000d / 3600d,
                    MaxConcurrent: 64,
                    MaxBodyBytes: 2L * 1024L * 1024L);
            }

            if (path.StartsWith("/cdn/", StringComparison.Ordinal))
            {
                return new Policy(
                    "cdn-read",
                    BurstCapacity: 2_000,
                    BurstRefillPerSecond: 100,
                    SustainedCapacity: 100_000,
                    SustainedRefillPerSecond: 100_000d / 3600d,
                    MaxConcurrent: 48,
                    MaxBodyBytes: 16 * 1024);
            }

            if (path.StartsWith("/imageserver", StringComparison.Ordinal))
            {
                bool transformsImage = request.Query.ContainsKey("width") ||
                                       request.Query.ContainsKey("height") ||
                                       request.Query.ContainsKey("cropsquare");
                bool signsImage = string.Equals(
                    request.Query["sig"].FirstOrDefault()?.Trim(),
                    "p1",
                    StringComparison.OrdinalIgnoreCase);
                if (transformsImage)
                {
                    return new Policy(
                        "image-transform",
                        BurstCapacity: 300,
                        BurstRefillPerSecond: 10,
                        SustainedCapacity: 4_000,
                        SustainedRefillPerSecond: 4_000d / 3600d,
                        MaxConcurrent: 8,
                        MaxBodyBytes: 16 * 1024);
                }

                if (signsImage)
                {
                    return new Policy(
                        "image-signed-read",
                        BurstCapacity: 1_000,
                        BurstRefillPerSecond: 50,
                        SustainedCapacity: 50_000,
                        SustainedRefillPerSecond: 50_000d / 3600d,
                        MaxConcurrent: 32,
                        MaxBodyBytes: 16 * 1024);
                }

                return new Policy(
                    "image-read",
                    BurstCapacity: 2_000,
                    BurstRefillPerSecond: 100,
                    SustainedCapacity: 100_000,
                    SustainedRefillPerSecond: 100_000d / 3600d,
                    MaxConcurrent: 48,
                    MaxBodyBytes: 16 * 1024);
            }

            if (path.Contains("cloudvariables", StringComparison.Ordinal) ||
                path.Contains("roomvariables", StringComparison.Ordinal) ||
                path.Contains("/playerdata/", StringComparison.Ordinal))
            {
                return new Policy(
                    "cloud-variable",
                    BurstCapacity: 2_000,
                    BurstRefillPerSecond: 100,
                    SustainedCapacity: 200_000,
                    SustainedRefillPerSecond: 200_000d / 3600d,
                    MaxConcurrent: 32,
                    MaxBodyBytes: 256L * 1024L);
            }

            if (path.Equals("/recnet/api/auth/login", StringComparison.Ordinal))
            {
                return new Policy(
                    "recnet-login",
                    BurstCapacity: 20,
                    BurstRefillPerSecond: 1,
                    SustainedCapacity: 300,
                    SustainedRefillPerSecond: 300d / 3600d,
                    MaxConcurrent: 4,
                    MaxBodyBytes: 128L * 1024L);
            }

            if (isWrite)
            {
                return new Policy(
                    "game-write",
                    BurstCapacity: 2_000,
                    BurstRefillPerSecond: 100,
                    SustainedCapacity: 200_000,
                    SustainedRefillPerSecond: 200_000d / 3600d,
                    MaxConcurrent: 48,
                    MaxBodyBytes: 20L * 1024L * 1024L);
            }

            return new Policy(
                "game-read",
                BurstCapacity: 4_000,
                BurstRefillPerSecond: 200,
                SustainedCapacity: 400_000,
                SustainedRefillPerSecond: 400_000d / 3600d,
                MaxConcurrent: 96,
                MaxBodyBytes: 1L * 1024L * 1024L);
        }

        private static string GetRateLimitRouteKey(HttpRequest request, string policyName)
        {
            string rawPath = request.Path.Value ?? "/";
            string[] segments = rawPath.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            for (int index = 0; index < segments.Length; index++)
            {
                string segment = segments[index];
                if (long.TryParse(segment, out _) ||
                    Guid.TryParse(segment, out _) ||
                    LooksLikeOpaqueIdentifier(segment))
                {
                    segments[index] = "{id}";
                }
                else
                {
                    segments[index] = segment.ToLowerInvariant();
                }
            }

            string normalizedPath = segments.Length == 0
                ? "/"
                : "/" + string.Join('/', segments);
            return $"{policyName}:{request.Method.ToUpperInvariant()}:{normalizedPath}";
        }

        private static bool LooksLikeOpaqueIdentifier(string value)
        {
            if (value.Length < 24)
                return false;

            foreach (char character in value)
            {
                if (!char.IsLetterOrDigit(character) &&
                    character != '-' &&
                    character != '_')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryConsume(
            string key,
            int capacity,
            double refillPerSecond,
            out int retryAfterSeconds)
        {
            DateTime now = DateTime.UtcNow;
            TokenBucket bucket = Buckets.GetOrAdd(key, _ => new TokenBucket(capacity, now));

            lock (bucket.Sync)
            {
                double elapsedSeconds = Math.Max(
                    0,
                    (now - bucket.LastRefillUtc).TotalSeconds);
                bucket.Tokens = Math.Min(
                    capacity,
                    bucket.Tokens + (elapsedSeconds * refillPerSecond));
                bucket.LastRefillUtc = now;
                bucket.LastSeenUtc = now;

                if (bucket.Tokens >= 1)
                {
                    bucket.Tokens -= 1;
                    retryAfterSeconds = 0;
                    return true;
                }

                retryAfterSeconds = refillPerSecond <= 0
                    ? 60
                    : Math.Max(1, (int)Math.Ceiling((1 - bucket.Tokens) / refillPerSecond));
                return false;
            }
        }

        private static string GetClientKey(HttpRequest request)
        {
            IPAddress? address = ClientNetwork.GetClientIp(request);
            return "ip:" + (address?.ToString() ?? "unknown");
        }

        private static void ApplySecurityHeaders(HttpResponse response)
        {
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["X-Frame-Options"] = "DENY";
            response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            response.Headers["Permissions-Policy"] =
                "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        }

        private static bool ShouldSkipIpBan(string path) =>
            path is "/recnet" or "/recnet/" or "/recnet/api/session" or
                "/photon/authenticate" ||
            (path.StartsWith("/recnet/", StringComparison.Ordinal) &&
             !path.StartsWith("/recnet/api/", StringComparison.Ordinal)) ||
            path.StartsWith("/recnet/api/admin/ip-bans", StringComparison.Ordinal);

        private static bool ShouldCheckVpn(string path)
        {
            if (path is "/" or "/nameserver" or "/favicon.ico" or
                "/privacy" or "/tos" or "/recnet" or "/recnet/" or
                "/photon/authenticate" ||
                path.StartsWith("/recnet/api/admin/", StringComparison.Ordinal) ||
                path.StartsWith("/cdn/", StringComparison.Ordinal) ||
                path.StartsWith("/imageserver", StringComparison.Ordinal) ||
                (path.StartsWith("/recnet/", StringComparison.Ordinal) &&
                 !path.StartsWith("/recnet/api/", StringComparison.Ordinal)) ||
                path is "/recnet/api/status" or "/recnet/api/session")
            {
                return false;
            }

            return true;
        }

        private static ObjectResult Error(
            int statusCode,
            string error,
            string message,
            object? extra = null)
        {
            object payload = extra == null
                ? new { success = false, error, message }
                : new { success = false, error, message, details = extra };
            return new ObjectResult(payload) { StatusCode = statusCode };
        }

        private static int GetGlobalConcurrencyLimit(string policyName) =>
            policyName switch
            {
                "admin-media-upload" => 2,
                "image-upload" => 12,
                "room-blob-upload" => 12,
                "image-transform" => 32,
                "recnet-login" or "account-creation" => 32,
                "token-exchange" => 256,
                "photon-callback" => 512,
                "game-auth-read" or "game-session" => 1_024,
                "game-write" or "cloud-variable" => 1_024,
                "image-signed-read" or "image-read" or "cdn-read" => 1_024,
                _ => 2_048
            };

        private static void DecrementActive(
            string key,
            ConcurrentDictionary<string, int> counters)
        {
            while (counters.TryGetValue(key, out int current))
            {
                if (current <= 1)
                {
                    if (counters.TryRemove(key, out _))
                        return;
                }
                else if (counters.TryUpdate(key, current - 1, current))
                {
                    return;
                }
            }
        }

        public static int GetGlobalActiveRequests()
        {
            int total = 0;
            foreach (var kvp in GlobalActiveRequests)
                total += kvp.Value;
            return total;
        }

        private static void CleanupOccasionally()
        {
            long count = Interlocked.Increment(ref _requestCounter);
            if ((count & 4095) != 0)
                return;

            DateTime cutoff = DateTime.UtcNow.AddHours(-2);
            foreach (var pair in Buckets)
            {
                if (pair.Value.LastSeenUtc < cutoff)
                    Buckets.TryRemove(pair.Key, out _);
            }
        }
    }
}