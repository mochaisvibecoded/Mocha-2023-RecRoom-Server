using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Mocha2023.Auth;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs;
using Mocha2023.Classes.DBs.DBClasses;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Text.Json;
using System.Threading;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Mocha2023.Controllers
{
    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    public class ImageController : ControllerBase
    {
        private const long MaxUploadBytes = 10 * 1024 * 1024;
        private const int MaxServedDimension = 2048;
        private const int ShareCameraImageType = 1;
        private const int OutfitThumbnailImageType = 2;
        private const int ProfileThumbnailImageType = 4;
        private const int PublicImageAccessibility = 1;
        private const int MaxDailyImageUploadsPerAccount = 100;
        private const long MaxDailyImageBytesPerAccount = 256L * 1024L * 1024L;
        private const int MaxStoredImagesPerAccount = 2_000;
        private const int MaxBulkImageIds = 200;
        private const int MaxUploadAttemptsPerMinutePerAccount = 10;
        private const int MaxUploadAttemptsPerHourPerAccount = 60;
        private const int MaxUploadAttemptsPerMinutePerIp = 24;
        private const int MaxUploadAttemptsPerHourPerIp = 180;
        private const int MaxGlobalUploadAttemptsPerMinute = 80;
        private const int MaxGlobalUploadAttemptsPerHour = 600;
        private const int MaxConcurrentImageUploads = 6;
        private const int MaxGlobalNewImagesPerHour = 120;
        private const long MaxGlobalImageBytesPerHour = 512L * 1024L * 1024L;
        private const long MaxGlobalImageBytesPerDay = 2L * 1024L * 1024L * 1024L;
        private const int StaticImageCacheSeconds = 24 * 60 * 60;
        private const int RemoteImageCacheSeconds = 5 * 60;
        private const int RandomImageCacheSeconds = 30;
        private const int MissingImageCacheSeconds = 60;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, string>
            PhotoPathsByImageId = new();
        private static readonly object SavedImageIdLock = new();
        private static readonly object LegacyPhotoIndexLock = new();
        private static bool LegacyPhotoIndexBuilt;
        private sealed class CachedImagePath
        {
            public string FullPath { get; init; } = string.Empty;
            public DateTime ExpiresAtUtc { get; init; }
        }
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedImagePath>
            LocalImagePathCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>
            ImageSignaturesByHash = new(StringComparer.Ordinal);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>
            ImageIssueLogTimes = new(StringComparer.OrdinalIgnoreCase);
        private sealed class UploadRateWindow
        {
            public Queue<DateTime> Attempts { get; } = new();
            public DateTime LastSeenUtc { get; set; }
        }

        private sealed class StoredImageUsage
        {
            public DateTime CreatedAtUtc { get; init; }
            public long Bytes { get; init; }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, UploadRateWindow>
            ImageUploadAttemptsByAccount = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, UploadRateWindow>
            ImageUploadAttemptsByIp = new(StringComparer.OrdinalIgnoreCase);
        private static readonly UploadRateWindow GlobalImageUploadAttempts = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, byte>
            ActiveImageUploadsByAccount = new();
        private static readonly SemaphoreSlim GlobalImageUploadGate =
            new(MaxConcurrentImageUploads, MaxConcurrentImageUploads);
        private static readonly Queue<StoredImageUsage> GlobalStoredImageUsage = new();
        private static readonly object GlobalStoredImageUsageLock = new();
        private static bool GlobalStoredImageUsageInitialized;

        [HttpGet("/imageserver/{*img_path}")]
        [HttpGet("/imageserver-v2/{*img_path}")]
        public async Task<IActionResult> ImgServer(
            string img_path,
            int width = 0,
            int height = 0,
            string? sig = null)
        {
            if (width < 0 || height < 0 ||
                width > MaxServedDimension || height > MaxServedDimension)
            {
                return BadRequest(new { error = "Invalid image dimensions." });
            }

            try
            {
                img_path = Uri.UnescapeDataString(img_path ?? string.Empty).TrimStart('/');
            }
            catch (UriFormatException)
            {
                return BadRequest(new { error = "Invalid image path." });
            }

            if (string.IsNullOrWhiteSpace(img_path))
                img_path = "DefaultPFP.png";

            string responseFileName = new string((Path.GetFileName(img_path) ?? "image.png")
                .Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_')
                .Take(120)
                .ToArray());
            if (string.IsNullOrWhiteSpace(responseFileName))
                responseFileName = "image.png";

            Response.Headers["Content-Disposition"] =
                $"inline; filename=\"{responseFileName}\"";
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Access-Control-Allow-Headers"] =
                "Cache-Control, If-None-Match, If-Modified-Since";
            Response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
            Response.Headers["Access-Control-Expose-Headers"] =
                "Content-Signature, Content-Type, ETag, Last-Modified";

            bool cropSquare = Request.Query.ContainsKey("cropsquare") &&
                (string.Equals(Request.Query["cropsquare"].ToString(), "true", StringComparison.OrdinalIgnoreCase) ||
                 Request.Query["cropsquare"].ToString() == "1");
            bool shouldProcess = cropSquare || width > 0 || height > 0;
            bool wantsSignature = WantsP1Signature(sig);
            bool isRemoteAlias = LoadingScreenImageService.IsRemoteAlias(img_path);
            bool isRandomRequest = IsRandomRequest(img_path, out string randomFolder);
            int cacheSeconds = isRandomRequest
                ? RandomImageCacheSeconds
                : isRemoteAlias
                    ? RemoteImageCacheSeconds
                    : StaticImageCacheSeconds;

            string baseImagesPath = Path.Combine(Program.dataDir, "Images");
            string foundLocalPath;

            if (isRemoteAlias)
            {
                foundLocalPath = await LoadingScreenImageService.GetLocalImageAsync(
                    img_path,
                    HttpContext.RequestAborted);
                foundLocalPath ??= ResolveLocalImagePath(baseImagesPath, "RROs/DormRoom.jpg");
            }
            else if (isRandomRequest)
            {
                foundLocalPath = GetRandomImagePath(baseImagesPath, randomFolder);
            }
            else
            {
                foundLocalPath = ResolveLocalImagePath(baseImagesPath, img_path);
            }

            if (foundLocalPath != null)
            {
                try
                {
                    var localFile = new FileInfo(foundLocalPath);
                    if ((shouldProcess || wantsSignature) &&
                        localFile.Length > 32L * 1024L * 1024L)
                    {
                        return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                        {
                            error = "Image is too large to transform or sign."
                        });
                    }

                    string variant =
                        $"crop={cropSquare};width={width};height={height}";
                    string etag = BuildImageEtag(localFile, variant);
                    DateTimeOffset lastModified = new(localFile.LastWriteTimeUtc, TimeSpan.Zero);
                    ApplyImageCacheHeaders(etag, lastModified, cacheSeconds);

                    if (ClientImageCacheIsFresh(etag, lastModified))
                        return StatusCode(StatusCodes.Status304NotModified);

                    if (!shouldProcess && !wantsSignature)
                    {
                        return PhysicalFile(
                            Path.GetFullPath(foundLocalPath),
                            GetMimeType(foundLocalPath),
                            enableRangeProcessing: false);
                    }

                    byte[] imageBytes = await System.IO.File.ReadAllBytesAsync(
                        foundLocalPath,
                        HttpContext.RequestAborted);
                    if (shouldProcess)
                    {
                        imageBytes = await ProcessImageAsync(
                            imageBytes,
                            cropSquare,
                            width,
                            height,
                            HttpContext.RequestAborted);
                        return WithSignature(imageBytes, "image/png", sig);
                    }

                    return WithSignature(imageBytes, GetMimeType(foundLocalPath), sig);
                }
                catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
                {
                    return new EmptyResult();
                }
                catch (Exception ex)
                {
                    LogImageIssue(
                        $"serve:{img_path}:{ex.GetType().Name}:{ex.Message}",
                        $"[image serve failed] path={img_path} error={ex.Message}");
                }
            }

            string fallbackPath = Path.Combine(baseImagesPath, "DefaultPFP.png");
            if (System.IO.File.Exists(fallbackPath))
            {
                var fallbackFile = new FileInfo(fallbackPath);
                string fallbackEtag = BuildImageEtag(
                    fallbackFile,
                    $"fallback;crop={cropSquare};width={width};height={height}");
                DateTimeOffset fallbackLastModified =
                    new(fallbackFile.LastWriteTimeUtc, TimeSpan.Zero);
                ApplyImageCacheHeaders(
                    fallbackEtag,
                    fallbackLastModified,
                    MissingImageCacheSeconds);
                Response.Headers["X-Image-Fallback"] = "1";

                if (ClientImageCacheIsFresh(fallbackEtag, fallbackLastModified))
                    return StatusCode(StatusCodes.Status304NotModified);

                byte[] fallbackBytes = await System.IO.File.ReadAllBytesAsync(
                    fallbackPath,
                    HttpContext.RequestAborted);
                if (shouldProcess)
                {
                    fallbackBytes = await ProcessImageAsync(
                        fallbackBytes,
                        cropSquare,
                        width,
                        height,
                        HttpContext.RequestAborted);
                }

                return WithSignature(fallbackBytes, "image/png", sig);
            }

            Response.Headers["Cache-Control"] =
                $"public, max-age={MissingImageCacheSeconds}";
            return NotFound();
        }

        private static bool WantsP1Signature(string? signature)
        {
            return string.Equals(
                signature?.Trim(),
                "p1",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildImageEtag(FileInfo file, string variant)
        {
            string fingerprint =
                $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{variant}";
            byte[] hash = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(fingerprint));
            return $"\"{Convert.ToHexString(hash)[..24].ToLowerInvariant()}\"";
        }

        private void ApplyImageCacheHeaders(
            string etag,
            DateTimeOffset lastModified,
            int maxAgeSeconds)
        {
            int staleSeconds = Math.Max(60, maxAgeSeconds * 4);
            Response.Headers["Cache-Control"] =
                $"public, max-age={maxAgeSeconds}, stale-while-revalidate={staleSeconds}";
            Response.Headers["ETag"] = etag;
            Response.Headers["Last-Modified"] = lastModified.ToUniversalTime().ToString("R");
            Response.Headers["X-Content-Type-Options"] = "nosniff";
        }

        private bool ClientImageCacheIsFresh(
            string etag,
            DateTimeOffset lastModified)
        {
            string ifNoneMatch = Request.Headers["If-None-Match"].ToString();
            if (!string.IsNullOrWhiteSpace(ifNoneMatch))
            {
                return ifNoneMatch
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(candidate =>
                        candidate == "*" ||
                        string.Equals(candidate, etag, StringComparison.Ordinal));
            }

            string ifModifiedSince = Request.Headers["If-Modified-Since"].ToString();
            return DateTimeOffset.TryParse(ifModifiedSince, out DateTimeOffset cachedAt) &&
                lastModified <= cachedAt.ToUniversalTime().AddSeconds(1);
        }

        private static void LogImageIssue(string key, string message)
        {
            DateTime now = DateTime.UtcNow;
            if (ImageIssueLogTimes.TryGetValue(key, out DateTime lastLogged) &&
                now - lastLogged < TimeSpan.FromMinutes(1))
            {
                return;
            }

            ImageIssueLogTimes[key] = now;
            Console.WriteLine(message);

            if (ImageIssueLogTimes.Count <= 2048)
                return;

            DateTime cutoff = now.AddMinutes(-10);
            foreach (var entry in ImageIssueLogTimes)
            {
                if (entry.Value < cutoff)
                    ImageIssueLogTimes.TryRemove(entry.Key, out _);
            }
        }

        private FileContentResult WithSignature(
            byte[] data,
            string contentType,
            string? signature)
        {
            Response.Headers["X-Content-Type-Options"] = "nosniff";

            if (WantsP1Signature(signature))
            {
                string contentHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(data));
                byte[] signed = ImageSignaturesByHash.GetOrAdd(
                    contentHash,
                    _ => ImageSigner.Shared.SignImage(data));
                if (ImageSignaturesByHash.Count > 4096)
                    ImageSignaturesByHash.Clear();

                string headerValue =
                    "key-id=KEY:RSA:p1.rec.net;data=" +
                    Convert.ToBase64String(signed);

                Response.Headers["Content-Signature"] = headerValue;
                Response.OnStarting(() =>
                {
                    Response.Headers["Content-Signature"] = headerValue;
                    return Task.CompletedTask;
                });
            }

            return File(data, contentType);
        }

        [HttpGet("/api/images/v6")]
        public IActionResult GetImageV6(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest();

            name = Path.GetFileName(name);
            string baseImagesPath = Path.Combine(Program.dataDir, "Images");
            var savedImage = RecNetDB.SavedImages.FindAll()
                .OrderByDescending(image => image.CreatedAt)
                .FirstOrDefault(image => string.Equals(
                    Path.GetFileName(image.PhotoPath),
                    name,
                    StringComparison.OrdinalIgnoreCase));

            if (savedImage == null ||
                !TryResolveContainedPath(baseImagesPath, savedImage.PhotoPath, out string found) ||
                !System.IO.File.Exists(found))
            {
                LogImageIssue(
                    $"v6-missing:{name}",
                    $"[ImageV6] Missing saved image metadata or file: {name}");
                return NotFound();
            }

            int cheerCount = RecNetDB.CountPhotoCheers(savedImage.PhotoPath);
            int commentCount = RecNetDB.PhotoComments.FindAll()
                .Count(comment => string.Equals(
                    comment.PhotoPath,
                    savedImage.PhotoPath,
                    StringComparison.OrdinalIgnoreCase));
            long? callerAccountId = AuthStuff.GetPlayerId(Request);
            bool cheered = callerAccountId.HasValue &&
                RecNetDB.HasPhotoCheer(savedImage.PhotoPath, callerAccountId.Value);
            long imageId = GetOrCreateSavedImageId(savedImage);

            return Ok(new
            {
                Id = imageId,
                ImageName = name,
                PlayerId = checked((int)savedImage.AccountId),
                AccountId = checked((int)savedImage.AccountId),
                RoomId = savedImage.RoomId,
                PlayerEventId = savedImage.PlayerEventId,
                Accessibility = savedImage.Accessibility,
                IsDeleted = false,
                SavedImageType = savedImage.SavedImageType,
                CreatedAt = savedImage.CreatedAt,
                TaggedPlayerIds = savedImage.TaggedPlayerIds ?? new List<int>(),
                CheerCount = cheerCount,
                Cheered = cheered,
                IsCheered = cheered,
                CommentCount = commentCount
            });
        }

        [HttpPost("/api/images/v4/uploadsaved")]
        [RequestSizeLimit(MaxUploadBytes + (256 * 1024))]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes + (256 * 1024))]
        public async Task<IActionResult> UploadSaved()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player?.Player == null)
                return Unauthorized(new { success = false, error = "Authentication required." });

            if (!Request.HasFormContentType)
                return BadRequest(new { success = false, error = "Expected multipart form data." });

            if (Request.ContentLength is long contentLength &&
                contentLength > MaxUploadBytes + (256 * 1024))
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                {
                    success = false,
                    error = "image_upload_too_large",
                    message = "Images must be 10 MB or smaller."
                });
            }

            if (!TryAcceptImageUploadAttempt(
                    player.PlayerId,
                    out int retryAfterSeconds,
                    out string rateLimitScope))
            {
                Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    success = false,
                    error = "image_upload_rate_limited",
                    scope = rateLimitScope,
                    retryAfterSeconds
                });
            }

            if (!ActiveImageUploadsByAccount.TryAdd(player.PlayerId, 0))
            {
                Response.Headers["Retry-After"] = "2";
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    success = false,
                    error = "image_upload_already_in_progress",
                    retryAfterSeconds = 2
                });
            }

            bool globalGateAcquired = false;
            try
            {
                globalGateAcquired = await GlobalImageUploadGate.WaitAsync(
                    0,
                    HttpContext.RequestAborted);
                if (!globalGateAcquired)
                {
                    Response.Headers["Retry-After"] = "2";
                    return StatusCode(StatusCodes.Status429TooManyRequests, new
                    {
                        success = false,
                        error = "image_upload_server_busy",
                        retryAfterSeconds = 2
                    });
                }

                var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
                var file = form.Files.FirstOrDefault();
                if (file == null || file.Length == 0)
                    return BadRequest(new { success = false, error = "No file was uploaded." });

                if (file.Length > MaxUploadBytes)
                {
                    return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                    {
                        success = false,
                        error = "image_upload_too_large",
                        message = "Images must be 10 MB or smaller."
                    });
                }

                string folderName = form["folder"].ToString().Trim();
                string imageMetaJson = form["imgMeta"].ToString();
                SavedImageMeta? imageMeta = ParseSavedImageMeta(imageMetaJson);
                if (!string.IsNullOrWhiteSpace(imageMetaJson) && imageMeta == null)
                    return BadRequest(new { success = false, error = "Invalid saved-image metadata." });

                if (imageMeta?.SavedImageType is int requestedType &&
                    requestedType is < 0 or > 7)
                {
                    return BadRequest(new { success = false, error = "Invalid saved-image type." });
                }

                if (imageMeta?.Accessibility is int requestedAccessibility &&
                    requestedAccessibility is < 0 or > 2)
                {
                    return BadRequest(new { success = false, error = "Invalid image accessibility." });
                }

                int inferredImageType = InferSavedImageType(folderName);
                int savedImageType = imageMeta?.SavedImageType is > 0
                    ? imageMeta.SavedImageType.Value
                    : inferredImageType;
                int accessibility = imageMeta?.Accessibility ?? PublicImageAccessibility;
                long? taggedRoomId = imageMeta?.RoomId is > 0
                    ? imageMeta.RoomId
                    : long.TryParse(form["roomId"].ToString(), out var parsedRoomId) &&
                      parsedRoomId > 0
                        ? parsedRoomId
                        : null;

                string targetFolder = savedImageType == ShareCameraImageType && taggedRoomId.HasValue
                    ? $"WhereTaken/{taggedRoomId.Value}"
                    : folderName.ToLowerInvariant() switch
                    {
                        "polaroid" => "PolaroidImages",
                        "polaroidimages" => "PolaroidImages",
                        "player" => "PlayerImages",
                        "playerimages" => "PlayerImages",
                        "photo" => "PlayerImages",
                        "photos" => "PlayerImages",
                        "avatar" => "PlayerImages",
                        "saved" => "PlayerImages",
                        "room" when taggedRoomId.HasValue => $"WhereTaken/{taggedRoomId.Value}",
                        "wheretaken" when taggedRoomId.HasValue => $"WhereTaken/{taggedRoomId.Value}",
                        _ => "PlayerImages"
                    };

                string baseImagesPath = Path.Combine(Program.dataDir, "Images", targetFolder);
                Directory.CreateDirectory(baseImagesPath);

                string originalName = Path.GetFileNameWithoutExtension(file.FileName);
                string safeName = new string(originalName
                    .Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
                    .ToArray());
                if (string.IsNullOrWhiteSpace(safeName))
                    safeName = "upload";
                if (safeName.Length > 48)
                    safeName = safeName[..48];

                byte[] fileBytes;
                await using (var input = file.OpenReadStream())
                using (var ms = new MemoryStream((int)Math.Min(file.Length, int.MaxValue)))
                {
                    await input.CopyToAsync(ms, HttpContext.RequestAborted);
                    if (ms.Length != file.Length || ms.Length > MaxUploadBytes)
                    {
                        return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                        {
                            success = false,
                            error = "image_upload_size_mismatch"
                        });
                    }

                    fileBytes = ms.ToArray();
                }

                SixLabors.ImageSharp.Formats.IImageFormat? format;
                SixLabors.ImageSharp.ImageInfo? info;
                try
                {
                    format = SixLabors.ImageSharp.Image.DetectFormat(fileBytes);
                    info = SixLabors.ImageSharp.Image.Identify(fileBytes);
                }
                catch
                {
                    return BadRequest(new { success = false, error = "That file is not a valid image." });
                }

                if (format == null || info == null || info.Width <= 0 || info.Height <= 0 ||
                    info.Width > 4096 || info.Height > 4096 ||
                    (long)info.Width * info.Height > 16_777_216)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Images cannot exceed 4096 x 4096 pixels or 16 megapixels."
                    });
                }

                string detectedExtension =
                    format.FileExtensions.FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;
                if (detectedExtension is not ("png" or "jpg" or "jpeg" or "webp" or "gif" or "bmp"))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Use a PNG, JPG, WebP, GIF, or BMP image."
                    });
                }

                DateTime createdAt = DateTime.UtcNow;
                string contentHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(fileBytes))
                    .ToLowerInvariant();
                var taggedPlayerIds = imageMeta?.PlayerIds?
                    .Where(id => id > 0)
                    .Distinct()
                    .Take(100)
                    .ToList() ?? new List<int>();

                var savedImage = RecNetDB.SavedImages.Find(image =>
                        image.AccountId == player.PlayerId)
                    .Where(image =>
                        image.SavedImageType == savedImageType &&
                        image.RoomId == taggedRoomId &&
                        image.ContentHash == contentHash &&
                        image.CreatedAt >= createdAt.AddHours(-24))
                    .OrderByDescending(image => image.CreatedAt)
                    .FirstOrDefault(image =>
                        TryResolveContainedPath(
                            Path.Combine(Program.dataDir, "Images"),
                            image.PhotoPath,
                            out string duplicatePath) &&
                        System.IO.File.Exists(duplicatePath));

                bool createdNewImage = savedImage == null;
                if (createdNewImage)
                {
                    DateTime dailyCutoff = createdAt.AddHours(-24);
                    var accountImages = RecNetDB.SavedImages.Find(image =>
                            image.AccountId == player.PlayerId)
                        .ToList();
                    var recentImages = accountImages
                        .Where(image => image.CreatedAt >= dailyCutoff)
                        .ToList();
                    long recentBytes = recentImages.Sum(image => Math.Max(0, image.ByteLength));

                    if (accountImages.Count >= MaxStoredImagesPerAccount ||
                        recentImages.Count >= MaxDailyImageUploadsPerAccount ||
                        recentBytes + fileBytes.LongLength > MaxDailyImageBytesPerAccount)
                    {
                        Response.Headers["Retry-After"] = "3600";
                        return StatusCode(StatusCodes.Status429TooManyRequests, new
                        {
                            success = false,
                            error = "image_upload_quota_exceeded",
                            message = "This account has reached its image storage quota."
                        });
                    }

                    if (!TryReserveGlobalStoredImage(
                            fileBytes.LongLength,
                            out int globalRetryAfterSeconds))
                    {
                        Response.Headers["Retry-After"] = globalRetryAfterSeconds.ToString();
                        return StatusCode(StatusCodes.Status429TooManyRequests, new
                        {
                            success = false,
                            error = "global_image_upload_quota_exceeded",
                            retryAfterSeconds = globalRetryAfterSeconds
                        });
                    }
                }

                byte[]? shareCameraLogBytes = null;
                string savedFileName;
                string relativePath;

                if (createdNewImage)
                {
                    savedFileName = savedImageType == ShareCameraImageType
                        ? CreateNetworkImageName(detectedExtension)
                        : $"{safeName}_{player.PlayerId}_{Guid.NewGuid():N}.{detectedExtension}";
                    string savedPath = Path.Combine(baseImagesPath, savedFileName);
                    string temporaryPath = savedPath + ".uploading";
                    relativePath = Path.Combine(targetFolder, savedFileName).Replace('\\', '/');

                    try
                    {
                        await System.IO.File.WriteAllBytesAsync(
                            temporaryPath,
                            fileBytes,
                            HttpContext.RequestAborted);
                        System.IO.File.Move(temporaryPath, savedPath, overwrite: false);

                        savedImage = new RecNetDB.SavedImage
                        {
                            PhotoPath = relativePath,
                            ImageId = 0,
                            AccountId = player.PlayerId,
                            RoomId = taggedRoomId,
                            PlayerEventId = imageMeta?.PlayerEventId is > 0
                                ? imageMeta.PlayerEventId
                                : null,
                            SavedImageType = savedImageType,
                            Accessibility = accessibility,
                            TaggedPlayerIds = taggedPlayerIds,
                            ContentHash = contentHash,
                            LookupName = savedFileName.ToLowerInvariant(),
                            ByteLength = fileBytes.LongLength,
                            CreatedAt = createdAt
                        };

                        if (!RecNetDB.SavedImages.Upsert(savedImage))
                            throw new IOException("The image index could not be saved.");

                        RememberResolvedImagePath(relativePath, savedPath);
                        RememberResolvedImagePath(savedFileName, savedPath);

                        if (savedImageType == ShareCameraImageType)
                            shareCameraLogBytes = fileBytes;
                    }
                    catch
                    {
                        TryDeleteFile(temporaryPath);
                        TryDeleteFile(savedPath);
                        throw;
                    }
                }
                else
                {
                    savedFileName = Path.GetFileName(savedImage!.PhotoPath);
                    relativePath = savedImage.PhotoPath.Replace('\\', '/').TrimStart('/');
                    createdAt = savedImage.CreatedAt;
                    accessibility = savedImage.Accessibility;
                    taggedRoomId = savedImage.RoomId;
                    taggedPlayerIds = savedImage.TaggedPlayerIds ?? new List<int>();
                }

                long savedImageId = GetOrCreateSavedImageId(savedImage!);
                string imageUrl = $"{ServerConfig.BaseURL}/imageserver-v2/{relativePath}";
                bool playerChanged = false;

                if (savedImageType == ProfileThumbnailImageType)
                {
                    player.Player.ProfileImage = relativePath;
                    playerChanged = true;
                }

                if (!string.IsNullOrWhiteSpace(player.Player.BannerImage) &&
                    string.Equals(
                        Path.GetFileName(player.Player.BannerImage),
                        savedFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    player.Player.BannerImage = relativePath;
                    playerChanged = true;
                }

                if (playerChanged)
                {
                    PlayerDB.Players.Update(player);
                    await NotiController.NotifyPlayerProfileUpdatedAsync(player.PlayerId);
                }

                if (shareCameraLogBytes != null)
                {
                    var taggedRoom = taggedRoomId.HasValue
                        ? RoomDB.GetRoom(taggedRoomId.Value)
                        : null;
                    DiscordLogger.LogShareCameraPhoto(
                        player.PlayerId,
                        player.Player.Username,
                        shareCameraLogBytes,
                        savedFileName,
                        taggedRoomId,
                        taggedRoom?.Name);
                }

                return Ok(new
                {
                    success = true,
                    duplicate = !createdNewImage,
                    Id = savedImageId,
                    ImageId = savedImageId,
                    ImageName = savedFileName,
                    PlayerId = checked((int)player.PlayerId),
                    AccountId = checked((int)player.PlayerId),
                    RoomId = taggedRoomId,
                    PlayerEventId = savedImage!.PlayerEventId,
                    Accessibility = accessibility,
                    IsDeleted = false,
                    SavedImageType = savedImageType,
                    CreatedAt = createdAt,
                    PlayerIds = taggedPlayerIds,
                    TaggedPlayerIds = taggedPlayerIds,
                    CheerCount = 0,
                    Cheered = false,
                    IsCheered = false,
                    CommentCount = 0,
                    path = relativePath,
                    url = imageUrl
                });
            }
            finally
            {
                if (globalGateAcquired)
                    GlobalImageUploadGate.Release();
                ActiveImageUploadsByAccount.TryRemove(player.PlayerId, out _);
            }
        }

        private bool TryAcceptImageUploadAttempt(
            long accountId,
            out int retryAfterSeconds,
            out string scope)
        {
            DateTime now = DateTime.UtcNow;
            retryAfterSeconds = 0;
            scope = string.Empty;

            UploadRateWindow accountWindow = ImageUploadAttemptsByAccount.GetOrAdd(
                accountId,
                _ => new UploadRateWindow());
            if (!TryConsumeUploadAttempt(
                    accountWindow,
                    now,
                    MaxUploadAttemptsPerMinutePerAccount,
                    MaxUploadAttemptsPerHourPerAccount,
                    out retryAfterSeconds))
            {
                scope = "account";
                return false;
            }

            string ipKey = GetUploadIpKey();
            UploadRateWindow ipWindow = ImageUploadAttemptsByIp.GetOrAdd(
                ipKey,
                _ => new UploadRateWindow());
            if (!TryConsumeUploadAttempt(
                    ipWindow,
                    now,
                    MaxUploadAttemptsPerMinutePerIp,
                    MaxUploadAttemptsPerHourPerIp,
                    out retryAfterSeconds))
            {
                scope = "ip";
                return false;
            }

            if (!TryConsumeUploadAttempt(
                    GlobalImageUploadAttempts,
                    now,
                    MaxGlobalUploadAttemptsPerMinute,
                    MaxGlobalUploadAttemptsPerHour,
                    out retryAfterSeconds))
            {
                scope = "global";
                return false;
            }

            CleanupUploadRateWindows(now);
            return true;
        }

        private string GetUploadIpKey()
        {
            string? cloudflareIp = Request.Headers["CF-Connecting-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(cloudflareIp) &&
                System.Net.IPAddress.TryParse(cloudflareIp, out var parsedCloudflareIp))
            {
                return parsedCloudflareIp.ToString();
            }

            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        private static bool TryConsumeUploadAttempt(
            UploadRateWindow window,
            DateTime now,
            int maxPerMinute,
            int maxPerHour,
            out int retryAfterSeconds)
        {
            lock (window)
            {
                DateTime hourCutoff = now.AddHours(-1);
                while (window.Attempts.Count > 0 && window.Attempts.Peek() <= hourCutoff)
                    window.Attempts.Dequeue();

                DateTime minuteCutoff = now.AddMinutes(-1);
                DateTime? firstMinuteAttempt = null;
                int minuteCount = 0;
                foreach (DateTime attempt in window.Attempts)
                {
                    if (attempt <= minuteCutoff)
                        continue;

                    firstMinuteAttempt ??= attempt;
                    minuteCount++;
                }

                if (minuteCount >= maxPerMinute)
                {
                    retryAfterSeconds = Math.Max(
                        1,
                        (int)Math.Ceiling(
                            ((firstMinuteAttempt ?? now).AddMinutes(1) - now).TotalSeconds));
                    window.LastSeenUtc = now;
                    return false;
                }

                if (window.Attempts.Count >= maxPerHour)
                {
                    retryAfterSeconds = Math.Max(
                        1,
                        (int)Math.Ceiling(
                            (window.Attempts.Peek().AddHours(1) - now).TotalSeconds));
                    window.LastSeenUtc = now;
                    return false;
                }

                window.Attempts.Enqueue(now);
                window.LastSeenUtc = now;
                retryAfterSeconds = 0;
                return true;
            }
        }

        private static void CleanupUploadRateWindows(DateTime now)
        {
            if (ImageUploadAttemptsByAccount.Count > 4096)
            {
                foreach (var entry in ImageUploadAttemptsByAccount)
                {
                    if (now - entry.Value.LastSeenUtc > TimeSpan.FromHours(2))
                        ImageUploadAttemptsByAccount.TryRemove(entry.Key, out _);
                }
            }

            if (ImageUploadAttemptsByIp.Count > 4096)
            {
                foreach (var entry in ImageUploadAttemptsByIp)
                {
                    if (now - entry.Value.LastSeenUtc > TimeSpan.FromHours(2))
                        ImageUploadAttemptsByIp.TryRemove(entry.Key, out _);
                }
            }
        }

        private static bool TryReserveGlobalStoredImage(
            long bytes,
            out int retryAfterSeconds)
        {
            lock (GlobalStoredImageUsageLock)
            {
                DateTime now = DateTime.UtcNow;
                DateTime dayCutoff = now.AddHours(-24);

                if (!GlobalStoredImageUsageInitialized)
                {
                    foreach (var image in RecNetDB.SavedImages.FindAll()
                                 .Where(image => image.CreatedAt > dayCutoff)
                                 .OrderBy(image => image.CreatedAt))
                    {
                        GlobalStoredImageUsage.Enqueue(new StoredImageUsage
                        {
                            CreatedAtUtc = image.CreatedAt,
                            Bytes = Math.Max(0, image.ByteLength)
                        });
                    }

                    GlobalStoredImageUsageInitialized = true;
                }
                while (GlobalStoredImageUsage.Count > 0 &&
                       GlobalStoredImageUsage.Peek().CreatedAtUtc <= dayCutoff)
                {
                    GlobalStoredImageUsage.Dequeue();
                }

                DateTime hourCutoff = now.AddHours(-1);
                var hourly = GlobalStoredImageUsage
                    .Where(item => item.CreatedAtUtc > hourCutoff)
                    .ToList();
                long hourlyBytes = hourly.Sum(item => item.Bytes);
                long dailyBytes = GlobalStoredImageUsage.Sum(item => item.Bytes);

                bool hourlyBlocked =
                    hourly.Count >= MaxGlobalNewImagesPerHour ||
                    hourlyBytes + bytes > MaxGlobalImageBytesPerHour;
                bool dailyBlocked = dailyBytes + bytes > MaxGlobalImageBytesPerDay;

                if (hourlyBlocked || dailyBlocked)
                {
                    DateTime hourlyUnlock = hourlyBlocked
                        ? (hourly.FirstOrDefault()?.CreatedAtUtc ?? now).AddHours(1)
                        : now;
                    DateTime dailyUnlock = dailyBlocked
                        ? (GlobalStoredImageUsage.FirstOrDefault()?.CreatedAtUtc ?? now).AddHours(24)
                        : now;
                    DateTime unlockAt = hourlyUnlock > dailyUnlock
                        ? hourlyUnlock
                        : dailyUnlock;

                    retryAfterSeconds = Math.Max(
                        60,
                        (int)Math.Ceiling((unlockAt - now).TotalSeconds));
                    return false;
                }

                GlobalStoredImageUsage.Enqueue(new StoredImageUsage
                {
                    CreatedAtUtc = now,
                    Bytes = Math.Max(0, bytes)
                });
                retryAfterSeconds = 0;
                return true;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch
            {

            }
        }

        private static SavedImageMeta? ParseSavedImageMeta(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<SavedImageMeta>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static int InferSavedImageType(string? folderName)
        {
            return folderName?.Trim().ToLowerInvariant() switch
            {
                "outfit" or "outfitthumbnail" => OutfitThumbnailImageType,
                "profile" or "profilethumbnail" or "avatar" => ProfileThumbnailImageType,
                _ => ShareCameraImageType
            };
        }

        private static string CreateNetworkImageName(string extension)
        {

            const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
            Span<char> encoded = stackalloc char[25];
            var value = new System.Numerics.BigInteger(
                Guid.NewGuid().ToByteArray(),
                isUnsigned: true,
                isBigEndian: true);

            for (int i = encoded.Length - 1; i >= 0; i--)
            {
                value = System.Numerics.BigInteger.DivRem(value, 36, out var remainder);
                encoded[i] = alphabet[(int)remainder];
            }

            return $"{new string(encoded)}.{extension}";
        }

        private sealed class SavedImageMeta
        {
            public List<int> PlayerIds { get; set; } = new();
            public int? SavedImageType { get; set; }
            public long? RoomId { get; set; }
            public long? PlayerEventId { get; set; }
            public int? Accessibility { get; set; }
        }

        private sealed class RoomSavedImageResponse
        {
            public long Id { get; init; }
            public string ImageName { get; init; } = string.Empty;
            public int PlayerId { get; init; }
            public long? RoomId { get; init; }
            public long? PlayerEventId { get; init; }
            public long? ClubId { get; init; }
            public string Description { get; init; } = string.Empty;
            public int Accessibility { get; init; }
            public bool IsDeleted { get; init; }
            public int SavedImageType { get; init; }
            public DateTime CreatedAt { get; init; }
            public int CheerCount { get; init; }
            public bool Cheered { get; init; }
            public bool IsCheered { get; init; }
            public int CommentCount { get; init; }
        }

        private sealed class ImageCheerStateResponse
        {

            public long Id { get; init; }
            public long ImageId { get; init; }
            public bool Cheered { get; init; }
            public bool IsCheered { get; init; }
        }

        [HttpPost("/api/images/v1/modifyaccessibility")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> ModifySavedImageAccessibility()
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account?.Player == null)
                return Unauthorized();

            (string? imageName, long imageId, int? accessibility) =
                await ReadModifyAccessibilityRequestAsync();
            if (!accessibility.HasValue ||
                accessibility.Value is < 0 or > 2)
            {
                return BadRequest(new
                {
                    error = "Accessibility must be Private (0), Public (1), or FriendsOnly (2)."
                });
            }

            string requestedName = Path.GetFileName(
                (imageName ?? string.Empty).Replace('\\', '/'));
            var savedImage = RecNetDB.SavedImages.FindAll()
                .Where(image =>
                    image.SavedImageType == ShareCameraImageType &&
                    (imageId <= 0 || image.ImageId == imageId) &&
                    (string.IsNullOrWhiteSpace(requestedName) ||
                     string.Equals(
                         Path.GetFileName(image.PhotoPath),
                         requestedName,
                         StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(image => image.CreatedAt)
                .FirstOrDefault();

            if (savedImage == null)
                return NotFound(new { error = "Image not found." });
            if (savedImage.AccountId != account.PlayerId)
                return StatusCode(403);

            savedImage.Accessibility = accessibility.Value;
            if (!RecNetDB.SavedImages.Update(savedImage))
                return StatusCode(500, new { error = "Could not update image accessibility." });

            long savedImageId = GetOrCreateSavedImageId(savedImage);

            Console.WriteLine(
                $"[IMAGE ACCESSIBILITY] image={Path.GetFileName(savedImage.PhotoPath)} " +
                $"id={savedImageId} accessibility={savedImage.Accessibility} " +
                $"player={account.PlayerId}");

            return Ok(new
            {
                Id = savedImageId,
                ImageId = savedImageId,
                ImageName = Path.GetFileName(savedImage.PhotoPath),
                Accessibility = savedImage.Accessibility
            });
        }

        [HttpGet("/api/images/v4/room/{roomId}")]
        public IActionResult GetRoomImagesV4(long roomId, [FromQuery] int sort = 0, [FromQuery] int filter = 0, [FromQuery] int take = 100, [FromQuery] int skip = 0)
        {
            take = Math.Clamp(take, 1, 100);
            skip = Math.Max(skip, 0);
            long? callerId = AuthStuff.GetPlayerId(Request);
            var room = RoomDB.GetRoom(roomId);
            if (room == null ||
                ((room.IsDorm ||
                  room.Accessibility ==
                      RoomDBClasses.RoomAccessibility.Private) &&
                 (!callerId.HasValue ||
                  !RoomDB.CanPlayerAccessRoom(room, callerId.Value))))
            {

                return NotFound();
            }

            string roomImagesPath = Path.Combine(Program.dataDir, "Images", "WhereTaken", roomId.ToString());

            if (!Directory.Exists(roomImagesPath))
                return Ok(Array.Empty<RoomSavedImageResponse>());

            string roomPrefix = $"WhereTaken/{roomId}/";
            var savedImagesByPath = RecNetDB.SavedImages.FindAll()
                .Where(image =>
                    image.SavedImageType == ShareCameraImageType &&
                    (image.RoomId == roomId ||
                     image.PhotoPath.Replace('\\', '/')
                         .StartsWith(roomPrefix, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(
                    image => image.PhotoPath.Replace('\\', '/').TrimStart('/'),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(image => image.CreatedAt).First(),
                    StringComparer.OrdinalIgnoreCase);

            var cheerCounts = RecNetDB.PhotoCheers.FindAll()
                .GroupBy(
                    cheer => RecNetDB.NormalizePhotoPath(cheer.PhotoPath),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> callerCheeredPaths = callerId.HasValue
                ? RecNetDB.PhotoCheers.Find(cheer => cheer.AccountId == callerId.Value)
                    .Select(cheer => RecNetDB.NormalizePhotoPath(cheer.PhotoPath))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var commentCounts = RecNetDB.PhotoComments.FindAll()
                .GroupBy(
                    comment => comment.PhotoPath.Replace('\\', '/').TrimStart('/'),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            var roomImages = new List<RoomSavedImageResponse>();
            foreach (string file in Directory.EnumerateFiles(
                         roomImagesPath,
                         "*",
                         SearchOption.TopDirectoryOnly)
                     .Where(file => _imageExtensions.Contains(
                         Path.GetExtension(file).ToLowerInvariant())))
            {
                string filename = Path.GetFileName(file);
                string relativePath = $"{roomPrefix}{filename}";
                savedImagesByPath.TryGetValue(relativePath, out var savedImage);

                long ownerId = savedImage?.AccountId ?? InferLegacyRoomPhotoOwner(filename);
                if (ownerId is <= 0 or > int.MaxValue)
                {

                    continue;
                }

                int accessibility = savedImage?.Accessibility ?? PublicImageAccessibility;
                bool visible = filter switch
                {

                    2 => accessibility == PublicImageAccessibility &&
                         callerId.HasValue &&
                         (savedImage?.TaggedPlayerIds?.Contains((int)callerId.Value) ?? false),

                    1 => accessibility == PublicImageAccessibility,

                    _ => accessibility == PublicImageAccessibility ||
                         (callerId.HasValue &&
                          (ownerId == callerId.Value ||
                           (savedImage?.TaggedPlayerIds?.Contains((int)callerId.Value) ?? false)))
                };
                if (!visible)
                    continue;

                DateTime createdAt = savedImage?.CreatedAt is { } indexedCreatedAt &&
                                     indexedCreatedAt != default
                    ? indexedCreatedAt
                    : System.IO.File.GetCreationTimeUtc(file);
                string normalizedRelativePath = RecNetDB.NormalizePhotoPath(relativePath);
                cheerCounts.TryGetValue(normalizedRelativePath, out int cheerCount);
                commentCounts.TryGetValue(relativePath, out int commentCount);
                bool cheered = callerCheeredPaths.Contains(normalizedRelativePath);

                long imageId = savedImage != null
                    ? GetOrCreateSavedImageId(savedImage)
                    : Math.Max(createdAt.Ticks, 1);
                PhotoPathsByImageId[imageId] = relativePath;

                roomImages.Add(new RoomSavedImageResponse
                {
                    Id = imageId,
                    ImageName = filename,
                    PlayerId = (int)ownerId,
                    RoomId = roomId,
                    PlayerEventId = savedImage?.PlayerEventId,
                    ClubId = null,
                    Description = string.Empty,
                    Accessibility = accessibility,
                    IsDeleted = false,
                    SavedImageType = savedImage?.SavedImageType ?? ShareCameraImageType,
                    CreatedAt = createdAt,
                    CheerCount = cheerCount,
                    Cheered = cheered,
                    IsCheered = cheered,
                    CommentCount = commentCount
                });
            }

            IEnumerable<RoomSavedImageResponse> sorted = sort switch
            {

                1 => roomImages
                    .OrderByDescending(image => image.CheerCount)
                    .ThenByDescending(image => image.CreatedAt),

                2 => roomImages.OrderBy(image => image.CreatedAt),

                _ => roomImages.OrderByDescending(image => image.CreatedAt)
            };

            return Ok(sorted.Skip(skip).Take(take).ToList());
        }

        private async Task<(string? ImageName, long ImageId, int? Accessibility)>
            ReadModifyAccessibilityRequestAsync()
        {
            string? imageName = null;
            long imageId = 0;
            int? accessibility = null;

            void ReadValue(string key, string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                if (key.Equals("imageName", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    imageName = value;
                }
                else if ((key.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                          key.Equals("imageId", StringComparison.OrdinalIgnoreCase)) &&
                         long.TryParse(value, out long parsedImageId))
                {
                    imageId = parsedImageId;
                }
                else if (key.Equals("accessibility", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(value, out int parsedAccessibility))
                    {
                        accessibility = parsedAccessibility;
                    }
                    else
                    {
                        accessibility = value.Trim().ToLowerInvariant() switch
                        {
                            "private" => 0,
                            "public" => 1,
                            "friendsonly" or "friends_only" or "friends only" => 2,
                            _ => null
                        };
                    }
                }
            }

            foreach (var pair in Request.Query)
                ReadValue(pair.Key, pair.Value.FirstOrDefault());

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (var pair in form)
                    ReadValue(pair.Key, pair.Value.FirstOrDefault());
                return (imageName, imageId, accessibility);
            }

            try
            {
                using JsonDocument body = await JsonDocument.ParseAsync(Request.Body);
                if (body.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in body.RootElement.EnumerateObject())
                    {
                        string? value = property.Value.ValueKind switch
                        {
                            JsonValueKind.String => property.Value.GetString(),
                            JsonValueKind.Number => property.Value.GetRawText(),
                            _ => null
                        };
                        ReadValue(property.Name, value);
                    }
                }
            }
            catch (JsonException)
            {

            }

            return (imageName, imageId, accessibility);
        }

        [HttpGet("/api/images/v5/cheered/bulk")]
        [HttpPost("/api/images/v5/cheered/bulk")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> GetImageCheerStates(
            [FromQuery(Name = "id")] long[] imageIds)
        {
            var requestedImageIds = new HashSet<long>(
                (imageIds ?? Array.Empty<long>())
                    .Where(imageId => imageId > 0)
                    .Take(MaxBulkImageIds));

            foreach (var queryValue in Request.Query
                         .Where(pair =>
                             pair.Key.Contains("id", StringComparison.OrdinalIgnoreCase))
                         .SelectMany(pair => pair.Value))
            {
                AddBulkImageIds(queryValue, requestedImageIds);
            }

            if (HttpMethods.IsPost(Request.Method))
            {
                if (Request.HasFormContentType)
                {
                    var form = await Request.ReadFormAsync(
                        HttpContext.RequestAborted);
                    foreach (var pair in form.Where(pair =>
                                 pair.Key.Contains("id", StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (string? value in pair.Value)
                            AddBulkImageIds(value, requestedImageIds);
                    }
                }
                else
                {
                    try
                    {
                        using JsonDocument body = await JsonDocument.ParseAsync(
                            Request.Body,
                            cancellationToken: HttpContext.RequestAborted);
                        ExtractBulkImageIds(
                            body.RootElement,
                            requestedImageIds);
                    }
                    catch (JsonException)
                    {
                        return BadRequest(new
                        {
                            error = "Invalid bulk image ID payload."
                        });
                    }
                }
            }

            long? accountId = AuthStuff.GetPlayerId(Request);
            var states = requestedImageIds
                .Select(imageId =>
                {
                    bool cheered = accountId.HasValue &&
                                   TryResolvePhotoPathByImageId(imageId, out string photoPath) &&
                                   RecNetDB.HasPhotoCheer(
                                       photoPath,
                                       accountId.Value);
                    return new ImageCheerStateResponse
                    {
                        Id = imageId,
                        ImageId = imageId,
                        Cheered = cheered,
                        IsCheered = cheered
                    };
                })
                .ToList();

            Console.WriteLine(
                $"[IMAGE CHEER BULK] method={Request.Method} " +
                $"player={accountId?.ToString() ?? "anonymous"} ids={states.Count}");
            return Ok(states);
        }

        private static void AddBulkImageIds(
            string? value,
            ISet<long> imageIds)
        {
            if (string.IsNullOrWhiteSpace(value) || imageIds.Count >= MaxBulkImageIds)
                return;

            foreach (string token in value.Split(
                         new[] { ',', ';', ' ', '\t', '\r', '\n' },
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                if (long.TryParse(token, out long imageId) && imageId > 0)
                    imageIds.Add(imageId);
                if (imageIds.Count >= MaxBulkImageIds)
                    break;
            }
        }

        private static void ExtractBulkImageIds(
            JsonElement element,
            ISet<long> imageIds,
            bool idContext = true)
        {
            if (imageIds.Count >= MaxBulkImageIds)
                return;

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        bool propertyIsId =
                            property.Name.Contains(
                                "id",
                                StringComparison.OrdinalIgnoreCase);
                        ExtractBulkImageIds(
                            property.Value,
                            imageIds,
                            propertyIsId);
                    }
                    break;

                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                        ExtractBulkImageIds(item, imageIds, idContext);
                    break;

                case JsonValueKind.Number:
                    if (idContext &&
                        element.TryGetInt64(out long imageId) &&
                        imageId > 0)
                    {
                        imageIds.Add(imageId);
                    }
                    break;

                case JsonValueKind.String:
                    if (idContext)
                        AddBulkImageIds(element.GetString(), imageIds);
                    break;
            }
        }

        [HttpPost("/api/images/v1/cheer")]
        [HttpPut("/api/images/v1/cheer")]
        [HttpDelete("/api/images/v1/cheer")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> SetImageCheer()
        {
            var account = AuthStuff.GetCurrentPlayer(Request);
            if (account?.Player == null)
                return Unauthorized();

            (long imageId, bool? cheer) = await ReadImageCheerRequestAsync();
            if (imageId <= 0)
            {
                Console.WriteLine(
                    $"[IMAGE CHEER] Missing image id; contentType={Request.ContentType ?? "none"} " +
                    $"contentLength={Request.ContentLength?.ToString() ?? "unknown"}");
                return BadRequest(new { error = "Image id is required." });
            }
            if (!TryResolvePhotoPathByImageId(imageId, out string photoPath))
            {
                Console.WriteLine($"[IMAGE CHEER] Unknown image id: {imageId}");
                return NotFound(new { error = "Image not found." });
            }

            bool shouldCheer = HttpMethods.IsDelete(Request.Method)
                ? false
                : cheer ?? true;
            RecNetDB.SetPhotoCheer(
                photoPath,
                account.PlayerId,
                shouldCheer);

            return Ok(new
            {
                Id = imageId,
                ImageId = imageId,
                Cheered = shouldCheer,
                IsCheered = shouldCheer,
                CheerCount = RecNetDB.CountPhotoCheers(photoPath)
            });
        }

        private static long InferLegacyRoomPhotoOwner(string filename)
        {
            const string prefix = "file_";
            if (!filename.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return 0;

            int separator = filename.IndexOf('_', prefix.Length);
            return separator > prefix.Length &&
                   long.TryParse(filename[prefix.Length..separator], out long accountId)
                ? accountId
                : 0;
        }

        private async Task<(long ImageId, bool? Cheer)> ReadImageCheerRequestAsync()
        {
            long imageId = 0;
            bool? cheer = null;

            foreach (var pair in Request.Query)
            {
                if (pair.Key.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                    pair.Key.Equals("imageId", StringComparison.OrdinalIgnoreCase))
                {
                    long.TryParse(pair.Value.FirstOrDefault(), out imageId);
                }
                else if (pair.Key.Equals("cheer", StringComparison.OrdinalIgnoreCase) ||
                         pair.Key.Equals("cheered", StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(pair.Value.FirstOrDefault(), out bool parsed))
                        cheer = parsed;
                }
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (var pair in form)
                {
                    if ((pair.Key.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                         pair.Key.Equals("imageId", StringComparison.OrdinalIgnoreCase)) &&
                        long.TryParse(pair.Value.FirstOrDefault(), out long parsedId))
                    {
                        imageId = parsedId;
                    }
                    else if ((pair.Key.Equals("cheer", StringComparison.OrdinalIgnoreCase) ||
                              pair.Key.Equals("cheered", StringComparison.OrdinalIgnoreCase)) &&
                             bool.TryParse(pair.Value.FirstOrDefault(), out bool parsedCheer))
                    {
                        cheer = parsedCheer;
                    }
                    else if (imageId <= 0 &&
                             long.TryParse(pair.Value.FirstOrDefault(), out long fallbackId) &&
                             fallbackId > 0)
                    {
                        imageId = fallbackId;
                    }
                }
                return (imageId, cheer);
            }

            try
            {
                using JsonDocument body = await JsonDocument.ParseAsync(Request.Body);
                ExtractImageCheerValues(body.RootElement, ref imageId, ref cheer);
            }
            catch (JsonException)
            {

            }

            return (imageId, cheer);
        }

        private static void ExtractImageCheerValues(
            JsonElement element,
            ref long imageId,
            ref bool? cheer,
            string? propertyName = null)
        {
            bool isImageIdProperty =
                propertyName?.Equals("id", StringComparison.OrdinalIgnoreCase) == true ||
                propertyName?.Contains("imageId", StringComparison.OrdinalIgnoreCase) == true ||
                propertyName?.Equals("image", StringComparison.OrdinalIgnoreCase) == true;
            bool isCheerProperty =
                propertyName?.Contains("cheer", StringComparison.OrdinalIgnoreCase) == true;

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        ExtractImageCheerValues(
                            property.Value,
                            ref imageId,
                            ref cheer,
                            property.Name);
                    }
                    break;

                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                        ExtractImageCheerValues(item, ref imageId, ref cheer);
                    break;

                case JsonValueKind.Number:
                    if (element.TryGetInt64(out long number))
                    {
                        if ((isImageIdProperty || imageId <= 0) && number > 1)
                            imageId = number;
                        else if (isCheerProperty && number is 0 or 1)
                            cheer = number == 1;
                    }
                    break;

                case JsonValueKind.String:
                    string? text = element.GetString();
                    if ((isImageIdProperty || imageId <= 0) &&
                        long.TryParse(text, out long parsedId) &&
                        parsedId > 1)
                    {
                        imageId = parsedId;
                    }
                    else if (isCheerProperty &&
                             bool.TryParse(text, out bool parsedCheer))
                    {
                        cheer = parsedCheer;
                    }
                    break;

                case JsonValueKind.True:
                case JsonValueKind.False:
                    if (isCheerProperty || !cheer.HasValue)
                        cheer = element.GetBoolean();
                    break;
            }
        }

        private static bool TryResolvePhotoPathByImageId(
            long imageId,
            out string photoPath)
        {
            photoPath = string.Empty;
            if (imageId <= 0)
                return false;

            string imageRoot = Path.Combine(Program.dataDir, "Images");
            if (PhotoPathsByImageId.TryGetValue(imageId, out string? cachedPath) &&
                TryResolveContainedPath(imageRoot, cachedPath, out string cachedFile) &&
                System.IO.File.Exists(cachedFile))
            {
                photoPath = cachedPath;
                return true;
            }

            var savedImage = RecNetDB.SavedImages.FindOne(image =>
                image.SavedImageType == ShareCameraImageType &&
                image.ImageId == imageId);
            if (savedImage == null &&
                imageId <= DateTime.MaxValue.Ticks)
            {
                var legacyCreatedAt = new DateTime(imageId, DateTimeKind.Utc);
                savedImage = RecNetDB.SavedImages.FindOne(image =>
                    image.SavedImageType == ShareCameraImageType &&
                    image.CreatedAt == legacyCreatedAt);
            }
            if (savedImage != null)
            {
                string normalized = savedImage.PhotoPath
                    .Replace('\\', '/')
                    .TrimStart('/');
                if (TryResolveContainedPath(imageRoot, normalized, out string fullPath) &&
                    System.IO.File.Exists(fullPath))
                {
                    photoPath = normalized;
                    PhotoPathsByImageId[imageId] = normalized;
                    return true;
                }
            }

            EnsureLegacyPhotoIndex(imageRoot);
            if (PhotoPathsByImageId.TryGetValue(imageId, out string? legacyPath) &&
                TryResolveContainedPath(imageRoot, legacyPath, out string legacyFile) &&
                System.IO.File.Exists(legacyFile))
            {
                photoPath = legacyPath;
                return true;
            }

            return false;
        }

        private static void EnsureLegacyPhotoIndex(string imageRoot)
        {
            if (LegacyPhotoIndexBuilt)
                return;

            lock (LegacyPhotoIndexLock)
            {
                if (LegacyPhotoIndexBuilt)
                    return;

                string[] legacyFolders = { "WhereTaken", "PolaroidImages" };
                int indexed = 0;
                foreach (string folder in legacyFolders)
                {
                    string directory = Path.Combine(imageRoot, folder);
                    if (!Directory.Exists(directory))
                        continue;

                    foreach (string file in Directory.EnumerateFiles(
                                 directory,
                                 "*",
                                 SearchOption.AllDirectories))
                    {
                        if (!_imageExtensions.Contains(
                                Path.GetExtension(file).ToLowerInvariant()))
                        {
                            continue;
                        }

                        long legacyId = System.IO.File.GetCreationTimeUtc(file).Ticks;
                        string relativePath = Path.GetRelativePath(imageRoot, file)
                            .Replace('\\', '/');
                        PhotoPathsByImageId.TryAdd(legacyId, relativePath);
                        indexed++;
                        if (indexed >= 50_000)
                            break;
                    }

                    if (indexed >= 50_000)
                        break;
                }

                LegacyPhotoIndexBuilt = true;
            }
        }

        internal static long GetOrCreateSavedImageId(
            RecNetDB.SavedImage savedImage)
        {
            string normalized = savedImage.PhotoPath
                .Replace('\\', '/')
                .TrimStart('/');

            lock (SavedImageIdLock)
            {
                bool changed = false;
                if (savedImage.ImageId <= 0)
                {

                    long candidate = Math.Max(savedImage.CreatedAt.Ticks, 2);
                    while (RecNetDB.SavedImages.Exists(image =>
                               image.ImageId == candidate &&
                               image.PhotoPath != savedImage.PhotoPath))
                    {
                        candidate++;
                    }

                    savedImage.ImageId = candidate;
                    changed = true;
                }

                string lookupName = Path.GetFileName(normalized).ToLowerInvariant();
                if (!string.Equals(
                        savedImage.LookupName,
                        lookupName,
                        StringComparison.Ordinal))
                {
                    savedImage.LookupName = lookupName;
                    changed = true;
                }

                if (changed)
                    RecNetDB.SavedImages.Update(savedImage);

                PhotoPathsByImageId[savedImage.ImageId] = normalized;
                return savedImage.ImageId;
            }
        }

        private static bool IsRandomRequest(string img_path, out string folder)
        {
            folder = null;
            if (string.IsNullOrWhiteSpace(img_path))
                return false;

            string trimmed = img_path.Trim('/');
            var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return false;

            if (parts.Length == 1 && parts[0].Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                folder = null;
                return true;
            }

            if (parts[^1].Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                folder = string.Join('/', parts[..^1]);
                return true;
            }

            return false;
        }

        private static readonly string[] _imageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

        private static string GetRandomImagePath(string baseImagesPath, string folder)
        {

            string[] safeRoots =
            {
                "RROs",
                "RecNet",
                "LoadingScreen",
                "LoadingScreens",
                "CommunityBoard"
            };

            string normalizedFolder = (folder ?? string.Empty)
                .Replace('\\', '/')
                .Trim('/');
            string selectedRoot = string.IsNullOrWhiteSpace(normalizedFolder)
                ? "RROs"
                : safeRoots.FirstOrDefault(root =>
                    normalizedFolder.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                    normalizedFolder.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                  ?? string.Empty;

            if (string.IsNullOrWhiteSpace(selectedRoot) ||
                !TryResolveContainedPath(baseImagesPath, normalizedFolder.Length == 0 ? selectedRoot : normalizedFolder, out string searchRoot) ||
                !Directory.Exists(searchRoot))
            {
                return null;
            }

            try
            {
                var files = Directory.EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories)
                    .Where(file => _imageExtensions.Contains(
                        Path.GetExtension(file).ToLowerInvariant()))
                    .Take(10_000)
                    .ToList();

                if (files.Count == 0)
                    return null;

                return files[Random.Shared.Next(files.Count)];
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[random image pick failed] {ex.Message}");
                return null;
            }
        }

        [HttpPost("/data/event")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> DataEvent()
        {
            string eventType = "unknown";
            string? eventParams = null;
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                eventType = form["eventType"].FirstOrDefault() ?? eventType;
                eventParams = form["eventParams"].FirstOrDefault();
            }

            eventType = new string(eventType
                .Take(80)
                .Where(ch => !char.IsControl(ch))
                .ToArray());

            long? accountId = AuthStuff.GetPlayerId(Request);
            Console.WriteLine(
                $"[DATA EVENT] eventType={eventType} " +
                $"account={accountId?.ToString() ?? "anonymous"}");
            if (accountId.HasValue && !string.IsNullOrWhiteSpace(eventParams))
            {
                try
                {
                    using var document = JsonDocument.Parse(eventParams);
                    RecNetDB.ApplyChallengeTelemetry(accountId.Value, eventType, document.RootElement);
                    if (eventType.Equals(
                            "ugc_room_save_stats",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        RoomController.TryApplySaveRequesterTelemetry(
                            accountId.Value);
                    }
                }
                catch (JsonException)
                {

                }
            }

            return Ok(new
            {
                success = true
            });
        }

        private static string ResolveLocalImagePath(string baseImagesPath, string img_path)
        {
            if (!Directory.Exists(baseImagesPath) ||
                string.IsNullOrWhiteSpace(img_path) ||
                !_imageExtensions.Contains(Path.GetExtension(img_path).ToLowerInvariant()))
            {
                return null;
            }

            string cacheKey = img_path.Replace('\\', '/').TrimStart('/');
            if (LocalImagePathCache.TryGetValue(cacheKey, out var cached) &&
                cached.ExpiresAtUtc > DateTime.UtcNow &&
                System.IO.File.Exists(cached.FullPath))
            {
                return cached.FullPath;
            }
            LocalImagePathCache.TryRemove(cacheKey, out _);

            string[] candidateRelativePaths = new[]
            {
                img_path,
                Path.Combine("PlayerImages", img_path),
                Path.Combine("CustomPFPS", img_path),
                Path.Combine("PolaroidImages", img_path),
                Path.Combine("RROs", img_path),
                Path.Combine("RecNet", img_path)
            };

            foreach (var relativePath in candidateRelativePaths)
            {
                if (TryResolveContainedPath(baseImagesPath, relativePath, out string candidate) &&
                    System.IO.File.Exists(candidate))
                {
                    RememberResolvedImagePath(cacheKey, candidate);
                    return candidate;
                }
            }

            string fileName = Path.GetFileName(img_path);
            if (!string.Equals(fileName, img_path, StringComparison.Ordinal))
                return null;

            string lookupName = fileName.ToLowerInvariant();
            var indexedImage = RecNetDB.SavedImages.FindOne(image =>
                image.LookupName == lookupName);
            if (indexedImage != null &&
                TryResolveContainedPath(
                    baseImagesPath,
                    indexedImage.PhotoPath,
                    out string indexedPath) &&
                System.IO.File.Exists(indexedPath))
            {
                RememberResolvedImagePath(cacheKey, indexedPath);
                return indexedPath;
            }

            return null;
        }

        private static void RememberResolvedImagePath(string key, string fullPath)
        {
            LocalImagePathCache[key] = new CachedImagePath
            {
                FullPath = fullPath,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
            };

            if (LocalImagePathCache.Count <= 4096)
                return;

            DateTime now = DateTime.UtcNow;
            foreach (var entry in LocalImagePathCache)
            {
                if (entry.Value.ExpiresAtUtc <= now)
                    LocalImagePathCache.TryRemove(entry.Key, out _);
            }

            int overflow = LocalImagePathCache.Count - 8192;
            if (overflow > 0)
            {
                foreach (string staleKey in LocalImagePathCache.Keys.Take(overflow))
                    LocalImagePathCache.TryRemove(staleKey, out _);
            }
        }

        private static bool TryResolveContainedPath(
            string root,
            string relativePath,
            out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(relativePath) ||
                relativePath.IndexOf('\0') >= 0 ||
                Path.IsPathRooted(relativePath))
            {
                return false;
            }

            string[] segments = relativePath
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment is "." or ".."))
                return false;

            try
            {
                string canonicalRoot = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.Combine(canonicalRoot, relativePath));
                string requiredPrefix = canonicalRoot + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
                    return false;

                fullPath = candidate;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        private static string GetMimeType(string filePath)
        {
            var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(filePath, out var contentType))
            {
                contentType = "image/png";
            }
            return contentType;
        }

        private static async Task<byte[]> ProcessImageAsync(
            byte[] imageBytes,
            bool cropSquare,
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            var info = SixLabors.ImageSharp.Image.Identify(imageBytes);
            if (info == null || info.Width <= 0 || info.Height <= 0 ||
                info.Width > 8192 || info.Height > 8192 ||
                (long)info.Width * info.Height > 40_000_000)
            {
                throw new InvalidDataException("Image dimensions are unsafe to process.");
            }

            using var ms = new MemoryStream(imageBytes);
            cancellationToken.ThrowIfCancellationRequested();
            using var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(ms);

            if (cropSquare)
            {
                int size = Math.Min(image.Width, image.Height);
                int x = (image.Width - size) / 2;
                int y = (image.Height - size) / 2;
                image.Mutate(ctx => ctx.Crop(new Rectangle(x, y, size, size)));

                int targetSize = width > 0 ? width : height;
                if (targetSize > 0)
                {
                    image.Mutate(ctx => ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(targetSize, targetSize),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3
                    }));
                }
            }
            else if (width > 0 || height > 0)
            {
                int resizeWidth = width;
                int resizeHeight = height;

                if (width > 0 && height == 0)
                {
                    resizeHeight = (int)((double)image.Height / image.Width * width);
                }
                else if (height > 0 && width == 0)
                {
                    resizeWidth = (int)((double)image.Width / image.Height * height);
                }

                image.Mutate(ctx => ctx.Resize(resizeWidth, resizeHeight));
            }

            using var output = new MemoryStream();
            cancellationToken.ThrowIfCancellationRequested();
            await image.SaveAsync(output, new PngEncoder());
            return output.ToArray();
        }
    }
}
