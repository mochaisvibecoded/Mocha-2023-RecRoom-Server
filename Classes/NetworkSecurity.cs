using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace Mocha2023.Classes
{
    public static class ClientNetwork
    {
        private static readonly Lazy<string[]> TrustedProxyNetworks = new(
            LoadTrustedProxyNetworks);

        public static IPAddress? GetClientIp(HttpRequest request)
        {
            IPAddress? remoteAddress = request.HttpContext.Connection.RemoteIpAddress;

            if (remoteAddress != null && IsTrustedForwardingProxy(remoteAddress))
            {
                if (TryGetHeaderAddress(request, "CF-Connecting-IP", out IPAddress? cloudflareAddress))
                    return Normalize(cloudflareAddress);

                if (request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
                {
                    string? first = forwardedFor.FirstOrDefault()?.Split(',')[0].Trim();
                    if (IPAddress.TryParse(first, out IPAddress? forwardedAddress))
                        return Normalize(forwardedAddress);
                }
            }

            return Normalize(remoteAddress);
        }

        private static bool IsTrustedForwardingProxy(IPAddress address)
        {
            IPAddress normalized = Normalize(address)!;
            if (IPAddress.IsLoopback(normalized))
                return true;

            return TrustedProxyNetworks.Value.Any(network =>
                IpNetwork.Contains(network, normalized));
        }

        private static string[] LoadTrustedProxyNetworks()
        {
            string configured =
                Program.LoadLocalSetting("TRUSTED_PROXY_NETWORKS") ??
                string.Empty;

            var networks = new List<string>();
            foreach (string candidate in configured.Split(
                         new[] { ',', ';', ' ', '\t', '\r', '\n' },
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                if (IpNetwork.TryNormalize(candidate, out string normalized))
                {
                    networks.Add(normalized);
                }
                else
                {
                    Console.WriteLine(
                        $"[NETWORK SECURITY] Ignoring invalid trusted proxy network: {candidate}");
                }
            }

            return networks.Distinct(StringComparer.Ordinal).ToArray();
        }

        public static bool IsPrivateOrLoopback(IPAddress? address)
        {
            if (address == null)
                return false;
            if (IPAddress.IsLoopback(address))
                return true;
            if (address.IsIPv4MappedToIPv6)
                return IsPrivateOrLoopback(address.MapToIPv4());

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] bytes = address.GetAddressBytes();
                return bytes[0] == 10 ||
                       (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168) ||
                       (bytes[0] == 169 && bytes[1] == 254);
            }

            byte[] ipv6 = address.GetAddressBytes();
            return address.IsIPv6LinkLocal ||
                   (ipv6.Length == 16 && (ipv6[0] & 0xFE) == 0xFC);
        }

        private static IPAddress? Normalize(IPAddress? address) =>
            address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address;

        private static bool TryGetHeaderAddress(
            HttpRequest request,
            string header,
            out IPAddress? address)
        {
            address = null;
            string? value = request.Headers[header].FirstOrDefault()?.Trim();
            return !string.IsNullOrWhiteSpace(value) && IPAddress.TryParse(value, out address);
        }
    }

    public static class IpNetwork
    {
        public static bool TryNormalize(string? value, out string normalized)
        {
            normalized = string.Empty;
            string input = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(input))
                return false;

            string[] parts = input.Split('/', 2, StringSplitOptions.TrimEntries);
            if (!IPAddress.TryParse(parts[0], out IPAddress? address))
                return false;
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            int maxBits = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            int prefix = maxBits;
            if (parts.Length == 2 &&
                (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > maxBits))
            {
                return false;
            }

            byte[] network = ApplyMask(address.GetAddressBytes(), prefix);
            var canonical = new IPAddress(network);
            normalized = prefix == maxBits
                ? canonical.ToString()
                : $"{canonical}/{prefix}";
            return true;
        }

        public static bool Contains(string networkText, IPAddress address)
        {
            if (!TryNormalize(networkText, out string normalized))
                return false;
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            string[] parts = normalized.Split('/', 2);
            if (!IPAddress.TryParse(parts[0], out IPAddress? networkAddress))
                return false;
            if (networkAddress.AddressFamily != address.AddressFamily)
                return false;

            int maxBits = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            int prefix = parts.Length == 2 && int.TryParse(parts[1], out int parsed)
                ? parsed
                : maxBits;

            byte[] expected = ApplyMask(networkAddress.GetAddressBytes(), prefix);
            byte[] actual = ApplyMask(address.GetAddressBytes(), prefix);
            return expected.SequenceEqual(actual);
        }

        private static byte[] ApplyMask(byte[] bytes, int prefixLength)
        {
            byte[] output = bytes.ToArray();
            int fullBytes = prefixLength / 8;
            int remainingBits = prefixLength % 8;

            if (remainingBits != 0 && fullBytes < output.Length)
            {
                int mask = 0xFF << (8 - remainingBits);
                output[fullBytes] = (byte)(output[fullBytes] & mask);
                fullBytes++;
            }

            for (int index = fullBytes; index < output.Length; index++)
                output[index] = 0;
            return output;
        }
    }

    public static class VpnDetectionService
    {
        private sealed record CacheEntry(bool IsAnonymous, DateTime ExpiresAtUtc);

        private static readonly ConcurrentDictionary<string, CacheEntry> Cache =
            new(StringComparer.Ordinal);
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(4)
        };

        public static async Task<bool> IsVpnOrProxyAsync(
            IPAddress address,
            CancellationToken cancellationToken)
        {
            if (ClientNetwork.IsPrivateOrLoopback(address))
                return false;

            string ip = address.ToString();
            DateTime now = DateTime.UtcNow;
            if (Cache.TryGetValue(ip, out CacheEntry? cached) && cached.ExpiresAtUtc > now)
                return cached.IsAnonymous;

            try
            {
                string apiKey = Program.LoadLocalSetting("PROXYCHECK_API_KEY")?.Trim() ?? string.Empty;
                string keyQuery = string.IsNullOrWhiteSpace(apiKey)
                    ? string.Empty
                    : "&key=" + Uri.EscapeDataString(apiKey);
                string url = "https://proxycheck.io/v3/" + Uri.EscapeDataString(ip) +
                    "?tag=0&p=0" + keyQuery;

                using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    Cache[ip] = new CacheEntry(false, now.AddMinutes(10));
                    return false;
                }

                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using JsonDocument document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);
                JsonElement root = document.RootElement;

                bool detected = false;
                if (root.TryGetProperty(ip, out JsonElement details))
                    detected = ReadDetection(details);
                else
                    detected = ReadDetection(root);

                Cache[ip] = new CacheEntry(
                    detected,
                    now.AddHours(detected ? 24 : 8));
                return detected;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                Cache[ip] = new CacheEntry(false, now.AddMinutes(10));
                return false;
            }
        }

        private static bool ReadDetection(JsonElement details)
        {
            if (details.ValueKind != JsonValueKind.Object)
                return false;

            if (details.TryGetProperty("detections", out JsonElement detections) &&
                detections.ValueKind == JsonValueKind.Object)
            {
                if (ReadBool(detections, "anonymous") ||
                    ReadBool(detections, "vpn") ||
                    ReadBool(detections, "proxy") ||
                    ReadBool(detections, "tor") ||
                    ReadBool(detections, "hosting"))
                {
                    return true;
                }
            }

            return ReadBool(details, "anonymous") ||
                   ReadBool(details, "vpn") ||
                   ReadBool(details, "proxy") ||
                   ReadBool(details, "hosting") ||
                   string.Equals(ReadString(details, "proxy"), "yes", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ReadString(details, "type"), "vpn", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ReadString(details, "type"), "tor", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ReadNetworkType(details), "hosting", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ReadBool(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
                return false;
            return value.ValueKind == JsonValueKind.True ||
                   (value.ValueKind == JsonValueKind.String &&
                    bool.TryParse(value.GetString(), out bool parsed) && parsed);
        }

        private static string? ReadString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static string? ReadNetworkType(JsonElement element)
        {
            if (!element.TryGetProperty("network", out JsonElement network) ||
                network.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            return ReadString(network, "type");
        }
    }
}
