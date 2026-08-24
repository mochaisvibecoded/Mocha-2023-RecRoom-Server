using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Mocha2023.Classes
{

    public static class LoadingScreenImageService
    {
        private const long MaxDownloadBytes = 8 * 1024 * 1024;
        private const int MaxSourceDimension = 4096;
        private const int MaxOutputDimension = 2048;
        private const int MaxRedirects = 3;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);
        private static readonly ConcurrentDictionary<string, Uri> RegisteredUrls =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> AliasLocks =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim DownloadSlots = new(3, 3);
        private static readonly HttpClient Client = CreateClient();

        public static string RewriteConfiguration(string json)
        {
            JsonNode? root = JsonNode.Parse(
                json,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            if (root == null)
                throw new JsonException("Loading-screen configuration was empty.");

            RewriteNode(root);
            return root.ToJsonString(new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                WriteIndented = false
            });
        }

        public static bool IsRemoteAlias(string? imageName)
        {
            if (string.IsNullOrWhiteSpace(imageName))
                return false;

            string normalized = imageName.Replace('\\', '/').TrimStart('/');
            if (!normalized.StartsWith("RemoteLoadingScreens/", StringComparison.OrdinalIgnoreCase) ||
                !normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return false;

            string hash = Path.GetFileNameWithoutExtension(normalized);
            return hash.Length == 64 && hash.All(Uri.IsHexDigit);
        }

        public static async Task<string?> GetLocalImageAsync(
            string imageName,
            CancellationToken cancellationToken)
        {
            string alias = imageName.Replace('\\', '/').TrimStart('/');
            if (!IsRemoteAlias(alias) || !RegisteredUrls.TryGetValue(alias, out Uri? source))
                return null;

            string cacheDirectory = Path.Combine(
                Program.dataDir,
                "Images",
                "RemoteLoadingScreens");
            Directory.CreateDirectory(cacheDirectory);
            string cachePath = Path.Combine(cacheDirectory, Path.GetFileName(alias));

            if (IsFresh(cachePath))
                return cachePath;

            SemaphoreSlim aliasLock = AliasLocks.GetOrAdd(alias, _ => new SemaphoreSlim(1, 1));
            await aliasLock.WaitAsync(cancellationToken);
            try
            {
                if (IsFresh(cachePath))
                    return cachePath;

                await DownloadSlots.WaitAsync(cancellationToken);
                try
                {
                    byte[] bytes = await DownloadAsync(source, cancellationToken);
                    await NormalizeAndSaveAsync(bytes, cachePath, cancellationToken);
                    return cachePath;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine($"[loading image] {source.Host}: download timed out");
                    return System.IO.File.Exists(cachePath) ? cachePath : null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.WriteLine($"[loading image] {source.Host}: {ex.Message}");
                    return System.IO.File.Exists(cachePath) ? cachePath : null;
                }
                finally
                {
                    DownloadSlots.Release();
                }
            }
            finally
            {
                aliasLock.Release();
            }
        }

        private static void RewriteNode(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                string? explicitUrlKey = FindKey(obj, "ImageUrl");
                JsonNode? explicitUrlNode = explicitUrlKey == null ? null : obj[explicitUrlKey];
                string? explicitImageUrl = explicitUrlNode is JsonValue explicitUrlValue &&
                    explicitUrlValue.TryGetValue(out string? parsedImageUrl)
                        ? parsedImageUrl
                        : null;
                if (TryRegister(explicitImageUrl, out string? explicitAlias))
                {

                    string targetKey = FindKey(obj, "ThumbnailBlobName") ??
                        FindKey(obj, "ThumbnailImageName") ??
                        FindKey(obj, "ImageName") ??
                        "ImageName";
                    obj[targetKey] = explicitAlias;
                }

                foreach (string key in obj.Select(pair => pair.Key).ToArray())
                {
                    JsonNode? value = obj[key];
                    if (IsImageNameField(key) &&
                        value is JsonValue jsonValue &&
                        jsonValue.TryGetValue(out string? imageName) &&
                        TryRegister(imageName, out string? alias))
                    {
                        obj[key] = alias;
                    }
                    else if (value != null)
                    {
                        RewriteNode(value);
                    }
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? value in array)
                {
                    if (value != null)
                        RewriteNode(value);
                }
            }
        }

        private static string? FindKey(JsonObject obj, string expected) =>
            obj.Select(pair => pair.Key).FirstOrDefault(key =>
                key.Equals(expected, StringComparison.OrdinalIgnoreCase));

        private static bool IsImageNameField(string key) =>
            key.Equals("ImageName", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("ThumbnailBlobName", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("ThumbnailImageName", StringComparison.OrdinalIgnoreCase);

        private static bool TryRegister(string? value, out string? alias)
        {
            alias = null;
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
                !string.IsNullOrEmpty(uri.UserInfo))
                return false;

            if (TryResolveLocalImageUrl(uri, out alias))
                return true;

            string canonical = uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();
            alias = $"RemoteLoadingScreens/{hash}.png";
            RegisteredUrls[alias] = uri;
            return true;
        }

        private static bool TryResolveLocalImageUrl(Uri uri, out string? imageName)
        {
            imageName = null;
            if (!Uri.TryCreate(ServerConfig.BaseURL, UriKind.Absolute, out Uri? configuredBase))
                configuredBase = null;

            bool isOwnHost = uri.IsLoopback || configuredBase != null &&
                uri.Scheme.Equals(configuredBase.Scheme, StringComparison.OrdinalIgnoreCase) &&
                uri.Host.Equals(configuredBase.Host, StringComparison.OrdinalIgnoreCase) &&
                uri.Port == configuredBase.Port;
            string? prefix = new[] { "/imageserver-v2/", "/imageserver/" }
                .FirstOrDefault(candidate => uri.AbsolutePath.StartsWith(
                    candidate,
                    StringComparison.OrdinalIgnoreCase));
            if (!isOwnHost || prefix == null)
                return false;

            string relative;
            try
            {
                relative = Uri.UnescapeDataString(uri.AbsolutePath[prefix.Length..])
                    .Replace('\\', '/')
                    .TrimStart('/');
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(relative) || relative.Contains(':'))
                return false;

            string root;
            string fullPath;
            try
            {
                root = Path.GetFullPath(Path.Combine(Program.dataDir, "Images"));
                fullPath = Path.GetFullPath(Path.Combine(
                    root,
                    relative.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
            if (!fullPath.StartsWith(
                    root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            imageName = relative;
            return true;
        }

        private static bool IsFresh(string path) =>
            System.IO.File.Exists(path) &&
            DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(path) < CacheLifetime;

        private static async Task<byte[]> DownloadAsync(
            Uri source,
            CancellationToken cancellationToken)
        {
            Uri current = source;
            for (int redirect = 0; redirect <= MaxRedirects; redirect++)
            {
                await EnsurePublicHostAsync(current, cancellationToken);
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.UserAgent.ParseAdd("MochaLoadingScreen/1.0");
                using HttpResponseMessage response = await Client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    if (redirect == MaxRedirects || response.Headers.Location == null)
                        throw new InvalidDataException("Too many image redirects.");
                    Uri next = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(current, response.Headers.Location);
                    if ((next.Scheme != Uri.UriSchemeHttps && next.Scheme != Uri.UriSchemeHttp) ||
                        (current.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp))
                        throw new InvalidDataException("Unsafe image redirect.");
                    current = next;
                    continue;
                }

                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxDownloadBytes)
                    throw new InvalidDataException("Remote image exceeds 8 MB.");

                string? mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType != null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Remote URL did not return an image.");

                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var output = new MemoryStream();
                byte[] buffer = new byte[64 * 1024];
                long total = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;
                    total += read;
                    if (total > MaxDownloadBytes)
                        throw new InvalidDataException("Remote image exceeds 8 MB.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                return output.ToArray();
            }
            throw new InvalidDataException("Unable to download remote image.");
        }

        private static async Task NormalizeAndSaveAsync(
            byte[] bytes,
            string cachePath,
            CancellationToken cancellationToken)
        {
            ImageInfo? info = Image.Identify(bytes);
            if (info == null || info.Width <= 0 || info.Height <= 0 ||
                info.Width > MaxSourceDimension || info.Height > MaxSourceDimension ||
                (long)info.Width * info.Height > 16_777_216)
                throw new InvalidDataException("Remote image dimensions are too large.");

            using Image image = Image.Load(bytes);
            while (image.Frames.Count > 1)
                image.Frames.RemoveFrame(image.Frames.Count - 1);
            if (image.Width > MaxOutputDimension || image.Height > MaxOutputDimension)
            {
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(MaxOutputDimension, MaxOutputDimension)
                }));
            }

            string tempPath = cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await image.SaveAsync(tempPath, new PngEncoder(), cancellationToken);
                System.IO.File.Move(tempPath, cachePath, true);
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }

        private static async Task EnsurePublicHostAsync(
            Uri uri,
            CancellationToken cancellationToken)
        {
            if (uri.IsLoopback || string.IsNullOrWhiteSpace(uri.Host))
                throw new InvalidDataException("Local image URLs are not allowed.");

            IPAddress[] addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
            if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
                throw new InvalidDataException("Private-network image URLs are not allowed.");
        }

        private static bool IsPublicAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None) ||
                address.Equals(IPAddress.IPv6None))
                return false;

            byte[] bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return !(bytes[0] is 0 or 10 or 127 ||
                    (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
                    (bytes[0] == 169 && bytes[1] == 254) ||
                    (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168) ||
                    (bytes[0] == 198 && bytes[1] is 18 or 19) ||
                    bytes[0] >= 224);
            }

            return !(address.IsIPv6LinkLocal || address.IsIPv6Multicast ||
                address.IsIPv6SiteLocal || bytes.All(value => value == 0) ||
                (bytes[0] & 0xFE) == 0xFC);
        }

        private static HttpClient CreateClient()
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(3),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectCallback = async (context, cancellationToken) =>
                {
                    IPAddress[] addresses = await Dns.GetHostAddressesAsync(
                        context.DnsEndPoint.Host,
                        cancellationToken);
                    if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
                        throw new InvalidDataException("Private-network image URLs are not allowed.");

                    IPAddress address = addresses[Random.Shared.Next(addresses.Length)];
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(
                            new IPEndPoint(address, context.DnsEndPoint.Port),
                            cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
        }
    }
}
