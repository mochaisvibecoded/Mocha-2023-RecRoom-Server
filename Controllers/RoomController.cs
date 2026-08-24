using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs;
using Mocha2023.Classes.DBs.DBClasses;
using Mocha2023.Auth;
using System.Text.Json;
using System.IO;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Mocha2023.Controllers
{
    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    [Route("/roomserver")]
    public class RoomController : ControllerBase
    {
        private static object MutationSuccess(object value) => new
        {
            Value = value,
            Success = true,
            ErrorId = (string?)null,
            Error = (string?)null,
            LocalizationContext = (object?)null
        };

        private static object MutationFailure(string errorId, string error) => new
        {
            Value = (object?)null,
            Success = false,
            ErrorId = errorId,
            Error = error,
            LocalizationContext = (object?)null
        };

        private sealed class RecentBlobUpload
        {
            public long AccountId { get; init; }
            public DateTime UploadedAtUtc { get; init; }
            public string? FileType { get; init; }
            public string Hash { get; init; } = string.Empty;
            public long Size { get; init; }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<
            string,
            RecentBlobUpload> RecentBlobUploads =
                new(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan RecentBlobUploadLifetime =
            TimeSpan.FromHours(24);
        private static readonly object BlobCleanupLock = new();
        private static DateTime LastBlobCleanupUtc = DateTime.MinValue;

        private sealed class RoomBlobUsage
        {
            public DateTime CreatedAtUtc { get; init; }
            public long Bytes { get; init; }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<
            long,
            Queue<RoomBlobUsage>> RoomBlobUsageByAccount = new();
        private const int MaxRoomBlobUploadsPerHour = 240;
        private const long MaxRoomBlobBytesPerHour = 2L * 1024L * 1024L * 1024L;
        private const long MaxRoomBlobBytesPerDay = 10L * 1024L * 1024L * 1024L;

        private sealed class PendingRoomSaveAttribution
        {
            public long RoomId { get; init; }
            public long SubRoomId { get; init; }
            public long SubRoomDataSaveId { get; init; }
            public long RoomInstanceId { get; init; }
            public long TransportAccountId { get; init; }
            public long? ClaimedRequesterAccountId { get; init; }
            public DateTime SavedAtUtc { get; init; }
        }

        private static readonly TimeSpan SaveAttributionTelemetryLifetime =
            TimeSpan.FromSeconds(30);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<
            long,
            List<PendingRoomSaveAttribution>> PendingSaveAttributions = new();

        private static bool CanAcceptRoomBlobUpload(
            long accountId,
            long bytes,
            out int retryAfterSeconds)
        {
            retryAfterSeconds = 0;
            DateTime now = DateTime.UtcNow;
            Queue<RoomBlobUsage> usage = RoomBlobUsageByAccount.GetOrAdd(
                accountId,
                _ => new Queue<RoomBlobUsage>());

            lock (usage)
            {
                DateTime dayCutoff = now.AddHours(-24);
                while (usage.Count > 0 && usage.Peek().CreatedAtUtc < dayCutoff)
                    usage.Dequeue();

                DateTime hourCutoff = now.AddHours(-1);
                var hourly = usage.Where(item => item.CreatedAtUtc >= hourCutoff).ToList();
                long hourlyBytes = hourly.Sum(item => item.Bytes);
                long dailyBytes = usage.Sum(item => item.Bytes);

                bool hourlyBlocked =
                    hourly.Count >= MaxRoomBlobUploadsPerHour ||
                    hourlyBytes + bytes > MaxRoomBlobBytesPerHour;
                bool dailyBlocked = dailyBytes + bytes > MaxRoomBlobBytesPerDay;

                if (hourlyBlocked || dailyBlocked)
                {
                    DateTime hourlyUnlock = hourlyBlocked
                        ? (hourly.FirstOrDefault()?.CreatedAtUtc ?? now).AddHours(1)
                        : now;
                    DateTime dailyUnlock = dailyBlocked
                        ? (usage.FirstOrDefault()?.CreatedAtUtc ?? now).AddHours(24)
                        : now;
                    DateTime unlockAt = hourlyUnlock > dailyUnlock
                        ? hourlyUnlock
                        : dailyUnlock;
                    retryAfterSeconds = Math.Max(
                        60,
                        (int)Math.Ceiling((unlockAt - now).TotalSeconds));
                    return false;
                }

                return true;
            }
        }

        private static void RecordRoomBlobUpload(long accountId, long bytes)
        {
            Queue<RoomBlobUsage> usage = RoomBlobUsageByAccount.GetOrAdd(
                accountId,
                _ => new Queue<RoomBlobUsage>());
            lock (usage)
            {
                usage.Enqueue(new RoomBlobUsage
                {
                    CreatedAtUtc = DateTime.UtcNow,
                    Bytes = Math.Max(0, bytes)
                });
            }
        }

        private static async Task<string> ComputeFileSha256Base64Async(
            string path,
            CancellationToken cancellationToken)
        {
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
                System.Security.Cryptography.HashAlgorithmName.SHA256);
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            byte[] buffer = new byte[81_920];
            while (true)
            {
                int bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken);
                if (bytesRead == 0)
                    break;

                hash.AppendData(buffer, 0, bytesRead);
            }

            return Convert.ToBase64String(hash.GetHashAndReset());
        }

        private static void RememberBlobUploader(
            string blobName,
            long accountId,
            string? fileType,
            string hash,
            long size)
        {
            var receipt = new RecentBlobUpload
            {
                AccountId = accountId,
                UploadedAtUtc = DateTime.UtcNow,
                FileType = fileType,
                Hash = hash,
                Size = size
            };
            RecentBlobUploads[blobName] = receipt;

            try
            {
                string metadataPath = GetBlobReceiptPath(blobName);
                string temporaryPath = metadataPath + ".tmp";
                string json = JsonSerializer.Serialize(receipt);
                System.IO.File.WriteAllText(temporaryPath, json);
                System.IO.File.Move(temporaryPath, metadataPath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[/upload] Could not persist uploader receipt: {ex.Message}");
            }

            if (RecentBlobUploads.Count > 4096)
            {
                DateTime cutoff = DateTime.UtcNow - RecentBlobUploadLifetime;
                foreach (var entry in RecentBlobUploads)
                {
                    if (entry.Value.UploadedAtUtc < cutoff)
                        RecentBlobUploads.TryRemove(entry.Key, out _);
                }
            }

            TryCleanupOrphanedRoomBlobs();
        }

        private static bool TryGetRecentBlobUpload(
            string? blobName,
            out RecentBlobUpload upload)
        {
            upload = null!;
            if (string.IsNullOrWhiteSpace(blobName))
                return false;

            string safeName = Path.GetFileName(blobName);
            if (!string.Equals(safeName, blobName, StringComparison.Ordinal))
                return false;

            if (RecentBlobUploads.TryGetValue(safeName, out var cached))
            {
                if (DateTime.UtcNow - cached.UploadedAtUtc <= RecentBlobUploadLifetime)
                {
                    upload = cached;
                    return cached.AccountId > 0;
                }

                RecentBlobUploads.TryRemove(safeName, out _);
            }

            try
            {
                string metadataPath = GetBlobReceiptPath(safeName);
                if (!System.IO.File.Exists(metadataPath))
                    return false;

                var persisted = JsonSerializer.Deserialize<RecentBlobUpload>(
                    System.IO.File.ReadAllText(metadataPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (persisted == null ||
                    persisted.AccountId <= 0 ||
                    DateTime.UtcNow - persisted.UploadedAtUtc > RecentBlobUploadLifetime)
                {
                    return false;
                }

                RecentBlobUploads[safeName] = persisted;
                upload = persisted;
                return true;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[ROOM SAVE] Could not read uploader receipt for {safeName}: {ex.Message}");
                return false;
            }
        }

        private static string GetBlobReceiptPath(string blobName)
        {
            string directory = Path.Combine(
                Program.dataDir,
                "DBs",
                "RoomUploadReceipts");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, Path.GetFileName(blobName) + ".json");
        }

        private static void TryCleanupOrphanedRoomBlobs()
        {
            lock (BlobCleanupLock)
            {
                DateTime now = DateTime.UtcNow;
                if (now - LastBlobCleanupUtc < TimeSpan.FromMinutes(30))
                    return;
                LastBlobCleanupUtc = now;

                try
                {
                    var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var room in RoomDB.Rooms.FindAll())
                    {
                        if (!string.IsNullOrWhiteSpace(room.DataBlob))
                            referenced.Add(Path.GetFileName(room.DataBlob));
                        foreach (var subRoom in room.SubRooms ?? new List<RoomDBClasses.SubRooms>())
                        {
                            if (!string.IsNullOrWhiteSpace(subRoom.DataBlob))
                                referenced.Add(Path.GetFileName(subRoom.DataBlob));
                        }
                    }

                    foreach (var save in RoomDB.SubRoomDataSaves.FindAll())
                    {
                        if (!string.IsNullOrWhiteSpace(save.DataBlob))
                            referenced.Add(Path.GetFileName(save.DataBlob));
                        if (!string.IsNullOrWhiteSpace(save.RoomDataBlob))
                            referenced.Add(Path.GetFileName(save.RoomDataBlob));
                    }

                    string receiptDirectory = Path.Combine(
                        Program.dataDir,
                        "DBs",
                        "RoomUploadReceipts");
                    string blobDirectory = Path.Combine(
                        Program.dataDir,
                        "CDN",
                        "room");
                    if (!Directory.Exists(receiptDirectory))
                        return;

                    DateTime orphanCutoff = now - RecentBlobUploadLifetime;
                    foreach (string receiptPath in Directory.EnumerateFiles(
                                 receiptDirectory,
                                 "*.json",
                                 SearchOption.TopDirectoryOnly))
                    {
                        string blobName = Path.GetFileNameWithoutExtension(receiptPath);
                        bool isReferenced = referenced.Contains(blobName);

                        DateTime uploadedAt = System.IO.File.GetLastWriteTimeUtc(receiptPath);
                        try
                        {
                            var receipt = JsonSerializer.Deserialize<RecentBlobUpload>(
                                System.IO.File.ReadAllText(receiptPath));
                            if (receipt?.UploadedAtUtc != default)
                                uploadedAt = receipt.UploadedAtUtc;
                        }
                        catch
                        {

                        }

                        if (uploadedAt > orphanCutoff)
                            continue;

                        if (!isReferenced)
                        {
                            string blobPath = Path.Combine(blobDirectory, blobName);
                            if (System.IO.File.Exists(blobPath))
                                System.IO.File.Delete(blobPath);
                        }

                        System.IO.File.Delete(receiptPath);
                        RecentBlobUploads.TryRemove(blobName, out _);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    Console.WriteLine($"[ROOM BLOB CLEANUP] {ex.Message}");
                }
            }
        }

        private static string? FirstBlobName(params string?[] candidates)
        {
            foreach (string? candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                string trimmed = candidate.Trim();
                string safeName = Path.GetFileName(trimmed);
                if (string.Equals(trimmed, safeName, StringComparison.Ordinal) &&
                    safeName.Length <= 128 &&
                    safeName.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'))
                {
                    return safeName;
                }
            }

            return null;
        }

        private static readonly string[] SaveRequesterInputKeys =
        {
            "SavedByAccountId",
            "savedByAccountId",
            "SavedByPlayerId",
            "savedByPlayerId",
            "SaveRequesterAccountId",
            "saveRequesterAccountId",
            "SaveRequesterPlayerId",
            "saveRequesterPlayerId",
            "SaveRequestedByAccountId",
            "saveRequestedByAccountId",
            "SaveRequestedByPlayerId",
            "saveRequestedByPlayerId",
            "RequesterAccountId",
            "requesterAccountId",
            "RequesterPlayerId",
            "requesterPlayerId",
            "RequestingAccountId",
            "requestingAccountId",
            "RequestingPlayerId",
            "requestingPlayerId",
            "RequestedByAccountId",
            "requestedByAccountId",
            "RequestedByPlayerId",
            "requestedByPlayerId"
        };

        private static readonly string[] SaveRequesterHeaderKeys =
        {
            "X-Room-Save-Requester-Account-Id",
            "X-Room-Save-Requester-Player-Id",
            "X-Save-Requester-Account-Id",
            "X-Save-Requester-Player-Id"
        };

        private static long? ReadPositiveFormLong(
            IFormCollection form,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                if (long.TryParse(
                        form[key].FirstOrDefault(),
                        out long value) &&
                    value > 0)
                {
                    return value;
                }
            }

            return null;
        }

        private HashSet<long> GetClaimedSaveRequesterIds(
            SubRoomDataRequest payload)
        {
            var claimedIds = new HashSet<long>();

            static void AddClaim(HashSet<long> values, long? candidate)
            {
                if (candidate is > 0)
                    values.Add(candidate.Value);
            }

            AddClaim(claimedIds, payload.SavedByAccountId);
            AddClaim(claimedIds, payload.SavedByPlayerId);
            AddClaim(claimedIds, payload.SaveRequesterAccountId);
            AddClaim(claimedIds, payload.SaveRequesterPlayerId);
            AddClaim(claimedIds, payload.SaveRequestedByAccountId);
            AddClaim(claimedIds, payload.SaveRequestedByPlayerId);
            AddClaim(claimedIds, payload.RequesterAccountId);
            AddClaim(claimedIds, payload.RequesterPlayerId);
            AddClaim(claimedIds, payload.RequestingAccountId);
            AddClaim(claimedIds, payload.RequestingPlayerId);
            AddClaim(claimedIds, payload.RequestedByAccountId);
            AddClaim(claimedIds, payload.RequestedByPlayerId);

            foreach (string key in SaveRequesterInputKeys)
            {
                if (long.TryParse(
                        Request.Query[key].FirstOrDefault(),
                        out long value) &&
                    value > 0)
                {
                    claimedIds.Add(value);
                }
            }

            foreach (string key in SaveRequesterInputKeys.Concat(
                         SaveRequesterHeaderKeys))
            {
                if (long.TryParse(
                        Request.Headers[key].FirstOrDefault(),
                        out long value) &&
                    value > 0)
                {
                    claimedIds.Add(value);
                }
            }

            return claimedIds;
        }

        private static bool CanAccountSaveRoom(
            RoomDBClasses.Room room,
            long accountId,
            out bool isDeveloper)
        {
            var account = PlayerDB.Players.FindById(accountId);
            isDeveloper = account?.PlayerRoles?.Contains(
                PlayerDBClasses.PlayerRoles.Developer) == true;

            return isDeveloper ||
                room.CreatorAccountId == accountId ||
                room.Roles?.Any(role =>
                    role.AccountId == accountId &&
                    (role.Role == RoomDBClasses.Role.Creator ||
                     role.Role == RoomDBClasses.Role.CoOwner ||
                     role.Role == RoomDBClasses.Role.TemporaryCoOwner)) == true;
        }

        private static bool AreActiveInSameRoomInstance(
            long roomId,
            long subRoomId,
            long transportAccountId,
            long requesterAccountId)
        {
            if (transportAccountId == requesterAccountId)
                return true;

            PlayerDBClasses.Heartbeat transportHeartbeat =
                PlayerDB.GetPlayerHeartbeat(transportAccountId);
            PlayerDBClasses.Heartbeat requesterHeartbeat =
                PlayerDB.GetPlayerHeartbeat(requesterAccountId);

            long transportInstanceId =
                transportHeartbeat.roomInstance?.roomInstanceId ?? 0;
            long requesterInstanceId =
                requesterHeartbeat.roomInstance?.roomInstanceId ?? 0;

            return transportHeartbeat.isOnline &&
                requesterHeartbeat.isOnline &&
                transportHeartbeat.roomInstance?.roomId == roomId &&
                requesterHeartbeat.roomInstance?.roomId == roomId &&
                transportHeartbeat.roomInstance?.subRoomId == subRoomId &&
                requesterHeartbeat.roomInstance?.subRoomId == subRoomId &&
                transportInstanceId > 0 &&
                transportInstanceId == requesterInstanceId &&
                Sessions.IsConfirmedParticipant(
                    transportAccountId,
                    transportInstanceId) &&
                Sessions.IsConfirmedParticipant(
                    requesterAccountId,
                    requesterInstanceId);
        }

        private static void RememberPendingSaveAttribution(
            long roomId,
            long subRoomId,
            long subRoomDataSaveId,
            long transportAccountId,
            long? claimedRequesterAccountId)
        {
            PlayerDBClasses.Heartbeat transportHeartbeat =
                PlayerDB.GetPlayerHeartbeat(transportAccountId);
            long roomInstanceId =
                transportHeartbeat.roomInstance?.roomInstanceId ?? 0;

            if (!transportHeartbeat.isOnline ||
                transportHeartbeat.roomInstance?.roomId != roomId ||
                transportHeartbeat.roomInstance?.subRoomId != subRoomId ||
                roomInstanceId <= 0 ||
                !Sessions.IsConfirmedParticipant(
                    transportAccountId,
                    roomInstanceId))
            {
                return;
            }

            var pending = PendingSaveAttributions.GetOrAdd(
                roomInstanceId,
                _ => new List<PendingRoomSaveAttribution>());
            DateTime now = DateTime.UtcNow;

            lock (pending)
            {
                pending.RemoveAll(value =>
                    now - value.SavedAtUtc >
                        SaveAttributionTelemetryLifetime);
                while (pending.Count >= 16)
                    pending.RemoveAt(0);

                pending.Add(new PendingRoomSaveAttribution
                {
                    RoomId = roomId,
                    SubRoomId = subRoomId,
                    SubRoomDataSaveId = subRoomDataSaveId,
                    RoomInstanceId = roomInstanceId,
                    TransportAccountId = transportAccountId,
                    ClaimedRequesterAccountId =
                        claimedRequesterAccountId,
                    SavedAtUtc = now
                });
            }
        }

        public static bool TryApplySaveRequesterTelemetry(
            long requesterAccountId)
        {
            if (requesterAccountId <= 0)
                return false;

            PlayerDBClasses.Heartbeat requesterHeartbeat =
                PlayerDB.GetPlayerHeartbeat(requesterAccountId);
            long roomId = requesterHeartbeat.roomInstance?.roomId ?? 0;
            long subRoomId =
                requesterHeartbeat.roomInstance?.subRoomId ?? 0;
            long roomInstanceId =
                requesterHeartbeat.roomInstance?.roomInstanceId ?? 0;

            if (!requesterHeartbeat.isOnline ||
                roomId <= 0 ||
                subRoomId <= 0 ||
                roomInstanceId <= 0 ||
                !Sessions.IsConfirmedParticipant(
                    requesterAccountId,
                    roomInstanceId) ||
                !PendingSaveAttributions.TryGetValue(
                    roomInstanceId,
                    out List<PendingRoomSaveAttribution>? pending))
            {
                return false;
            }

            var room = RoomDB.GetRoom(roomId);
            if (room == null ||
                !CanAccountSaveRoom(
                    room,
                    requesterAccountId,
                    out _))
            {
                return false;
            }

            PendingRoomSaveAttribution? candidate;
            DateTime now = DateTime.UtcNow;
            lock (pending)
            {
                pending.RemoveAll(value =>
                    now - value.SavedAtUtc >
                        SaveAttributionTelemetryLifetime);

                candidate = pending.FirstOrDefault(value =>
                    value.RoomId == roomId &&
                    value.SubRoomId == subRoomId &&
                    value.RoomInstanceId == roomInstanceId &&
                    value.TransportAccountId != requesterAccountId &&
                    (!value.ClaimedRequesterAccountId.HasValue ||
                     value.ClaimedRequesterAccountId.Value ==
                         requesterAccountId ||
                     value.ClaimedRequesterAccountId.Value ==
                         value.TransportAccountId));

                if (candidate == null)
                    return false;

                pending.Remove(candidate);
            }

            var save = RoomDB.SubRoomDataSaves.FindById(
                candidate.SubRoomDataSaveId);
            var subRoom = room.SubRooms?.FirstOrDefault(value =>
                value.SubRoomId == candidate.SubRoomId);
            if (save == null || subRoom == null)
                return false;

            long previousRequesterAccountId = save.SavedByAccountId;
            save.SavedByAccountId = requesterAccountId;
            if (!RoomDB.SubRoomDataSaves.Update(save))
                return false;

            if (subRoom.SubRoomDataSaveId == save.SubRoomDataSaveId)
            {
                subRoom.SavedByAccountId = requesterAccountId;
                if (subRoom.SubRoomDataSave?.SubRoomDataSaveId ==
                    save.SubRoomDataSaveId)
                {
                    subRoom.SubRoomDataSave.SavedByAccountId =
                        requesterAccountId;
                }

                RoomDB.Rooms.Update(room);
            }

            Console.WriteLine(
                $"[ROOM SAVE REQUESTER CONFIRM] room={roomId} " +
                $"subroom={subRoomId} instance={roomInstanceId} " +
                $"transportAuthority={candidate.TransportAccountId} " +
                $"requester={requesterAccountId} " +
                $"previousRequester={previousRequesterAccountId} " +
                $"saveId={candidate.SubRoomDataSaveId} " +
                "source=authenticated-ugc-room-save-stats");
            return true;
        }

        [HttpPost("/upload")]
        [HttpPut("/upload")]
        [RequestSizeLimit((50 * 1024 * 1024) + (512 * 1024))]
        [RequestFormLimits(MultipartBodyLengthLimit = (50 * 1024 * 1024) + (512 * 1024))]
        public async Task<IActionResult> UploadBlob()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            if (!Request.HasFormContentType)
                return BadRequest(new { success = false, error = "Expected multipart form data." });

            var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
            var uploadedFile = form.Files.FirstOrDefault(file =>
                                   string.Equals(file.Name, "File", StringComparison.OrdinalIgnoreCase))
                               ?? form.Files.FirstOrDefault();

            if (uploadedFile == null || uploadedFile.Length <= 0)
                return BadRequest(new { success = false, error = "No room blob was uploaded." });

            if (uploadedFile.Length > 50L * 1024L * 1024L)
                return BadRequest(new { success = false, error = "Room blobs must be 50 MB or smaller." });

            if (!CanAcceptRoomBlobUpload(id.Value, uploadedFile.Length, out int retryAfterSeconds))
            {
                Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    success = false,
                    error = "room_blob_upload_quota_exceeded",
                    retryAfterSeconds
                });
            }

            string? fileType = form["FileType"].FirstOrDefault();
            bool isAssetBundle = fileType?.Contains("assetbundle", StringComparison.OrdinalIgnoreCase) == true ||
                fileType?.Contains("bakedasset", StringComparison.OrdinalIgnoreCase) == true;
            string blobsDir = Path.Combine(
                Program.dataDir,
                "CDN",
                isAssetBundle ? "assetbundles" : "room");
            Directory.CreateDirectory(blobsDir);

            string blobName = Guid.NewGuid().ToString("N");
            string finalPath = Path.Combine(blobsDir, blobName);
            string temporaryPath = finalPath + ".uploading";

            byte[] hashBytes;
            long bytesWritten = 0;

            try
            {
                using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
                    System.Security.Cryptography.HashAlgorithmName.SHA256);

                await using (var input = uploadedFile.OpenReadStream())
                await using (var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    byte[] buffer = new byte[81920];
                    while (true)
                    {
                        int read = await input.ReadAsync(
                            buffer.AsMemory(0, buffer.Length),
                            HttpContext.RequestAborted);
                        if (read == 0)
                            break;

                        await output.WriteAsync(
                            buffer.AsMemory(0, read),
                            HttpContext.RequestAborted);
                        hash.AppendData(buffer, 0, read);
                        bytesWritten += read;
                    }

                    await output.FlushAsync(HttpContext.RequestAborted);
                }

                hashBytes = hash.GetHashAndReset();

                if (bytesWritten != uploadedFile.Length)
                {
                    throw new InvalidDataException(
                        $"Uploaded room blob length mismatch. Expected {uploadedFile.Length}, got {bytesWritten}.");
                }

                System.IO.File.Move(temporaryPath, finalPath, overwrite: false);
            }
            catch
            {
                if (System.IO.File.Exists(temporaryPath))
                    System.IO.File.Delete(temporaryPath);
                throw;
            }

            string sha256Hex = Convert.ToHexString(hashBytes).ToLowerInvariant();
            string sha256Base64 = Convert.ToBase64String(hashBytes);

            if (fileType?.Contains("invent", StringComparison.OrdinalIgnoreCase) == true)
            {
                string inventionDirectory = Path.Combine(
                    Program.dataDir,
                    "CDN",
                    "invention");
                Directory.CreateDirectory(inventionDirectory);
                string inventionPath = Path.Combine(inventionDirectory, blobName);
                if (!System.IO.File.Exists(inventionPath))
                    System.IO.File.Copy(finalPath, inventionPath, overwrite: false);
            }

            string ownershipProof = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(
                        $"{blobName}:{id.Value}:{sha256Base64}")));

            RecordRoomBlobUpload(id.Value, bytesWritten);
            RememberBlobUploader(
                blobName,
                id.Value,
                fileType,
                sha256Base64,
                bytesWritten);

            Console.WriteLine(
                $"[/upload] player={id.Value} fileType={fileType ?? "unknown"} " +
                $"blobName={blobName} bytes={bytesWritten} " +
                $"hashBase64={sha256Base64} hashHex={sha256Hex}");

            return Ok(new
            {
                success = true,
                filename = blobName,
                blobName,

                Hash = sha256Base64,
                OwnershipProof = ownershipProof
            });
        }

        [HttpGet("rooms")]
        public async Task<IActionResult> GetRoomBy([FromQuery] string? name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var room = RoomDB.GetRoomByName(name);
                long? accountId = AuthStuff.GetPlayerId(Request);
                return CanViewRoomDirectly(room, accountId)
                    ? Ok(RoomDB.PrepareRoomForClient(room))
                    : NotFound();
            }

            var rooms = RoomDB.PrepareRoomsForClient(
                RoomDB.Rooms.FindAll().Where(IsPubliclyDiscoverableRoom));

            return Ok(new
            {
                Results = rooms,
                TotalResults = rooms.Count
            });
        }

        [HttpGet("rooms/search")]
        [HttpGet("/api/rooms/v1/search")]
        [HttpGet("/api/rooms/v2/search")]
        public IActionResult SearchRooms(
            [FromQuery] string? query = null,
            [FromQuery(Name = "q")] string? shortQuery = null,
            [FromQuery(Name = "search")] string? search = null,
            [FromQuery(Name = "searchTerm")] string? searchTerm = null,
            [FromQuery(Name = "name")] string? name = null,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 100);

            string rawQuery = new[]
                {
                    query,
                    shortQuery,
                    search,
                    searchTerm,
                    name,
                    Request.Query["term"].FirstOrDefault(),
                    Request.Query["text"].FirstOrDefault()
                }
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?.Trim() ?? string.Empty;

            static string NormalizeWord(string value)
            {
                string normalized = value.Trim();

                while (normalized.StartsWith('^'))
                    normalized = normalized[1..];

                return normalized.Trim().ToLowerInvariant();
            }

            string[] terms = rawQuery.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            string[] tags = terms
                .Where(term => term.StartsWith('#') && term.Length > 1)
                .Select(term => NormalizeWord(term[1..]))
                .Where(term => term.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            string[] words = terms
                .Where(term => !term.StartsWith('#'))
                .Select(NormalizeWord)
                .Where(term => term.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            bool HasTag(RoomDBClasses.Room room, string tag)
            {
                if (tag == "base")
                    return RoomDB.IsCanonicalBaseRoom(room);

                if (tag is "rro" or "recroomoriginal")
                {
                    return room.IsRRO || room.Tags?.Any(value =>
                        string.Equals(value.Tag, "rro", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(value.Tag, "recroomoriginal", StringComparison.OrdinalIgnoreCase)) == true;
                }

                return room.Tags?.Any(value =>
                    string.Equals(value.Tag, tag, StringComparison.OrdinalIgnoreCase)) == true;
            }

            bool ContainsWord(RoomDBClasses.Room room, string word)
            {
                string roomName = NormalizeWord(room.Name ?? string.Empty);

                return roomName.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                    (room.Description?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (room.Tags?.Any(value =>
                        value.Tag?.Contains(word, StringComparison.OrdinalIgnoreCase) == true) ?? false);
            }

            int SearchScore(RoomDBClasses.Room room)
            {
                if (words.Length == 0)
                    return 0;

                string roomName = NormalizeWord(room.Name ?? string.Empty);
                string normalizedQuery = NormalizeWord(rawQuery);
                int score = 0;

                if (roomName.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                    score += 10_000;
                else if (roomName.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                    score += 5_000;
                else if (roomName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                    score += 2_500;

                foreach (string word in words)
                {
                    if (roomName.Equals(word, StringComparison.OrdinalIgnoreCase))
                        score += 1_000;
                    else if (roomName.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                        score += 500;
                    else if (roomName.Contains(word, StringComparison.OrdinalIgnoreCase))
                        score += 250;
                    else if (room.Description?.Contains(word, StringComparison.OrdinalIgnoreCase) == true)
                        score += 50;
                    else if (room.Tags?.Any(value =>
                                 value.Tag?.Contains(word, StringComparison.OrdinalIgnoreCase) == true) == true)
                        score += 25;
                }

                return score;
            }

            var discoverableRooms = RoomDB.Rooms.FindAll()
                .Where(IsPubliclyDiscoverableRoom)
                .Where(room => tags.All(tag => HasTag(room, tag)))
                .ToList();

            var matches = discoverableRooms
                .Where(room => words.Length == 0 || words.Any(word => ContainsWord(room, word)))
                .OrderByDescending(SearchScore)
                .ThenByDescending(room => room.Stats?.VisitCount ?? 0)
                .ThenByDescending(room => room.Stats?.CheerCount ?? 0)
                .ThenBy(room => room.Name)
                .ThenBy(room => room.RoomId)
                .ToList();

            if (tags.Contains("base", StringComparer.OrdinalIgnoreCase))
                matches = matches.OrderBy(RoomDB.GetCanonicalBaseRoomOrder).ToList();

            var page = matches.Skip(skip).Take(take).ToList();
            RoomDB.PrepareRoomsForClient(page);

            Response.Headers["X-Total-Count"] = matches.Count.ToString();
            Console.WriteLine(
                $"[ROOM SEARCH] query=\"{rawQuery}\" public={discoverableRooms.Count} " +
                $"matches={matches.Count} skip={skip} take={take}");

            return Ok(new
            {
                Results = page,
                TotalResults = matches.Count
            });
        }

        [HttpGet("rooms/base")]
        public IActionResult GetBaseRooms(
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            var (results, total) = RoomDB.GetHotRooms("base", skip, take);

            return Ok(new
            {
                Results = results ?? new List<RoomDBClasses.Room>(),
                TotalResults = total
            });
        }

        [HttpGet("rooms/{roomId}")]
        public async Task<IActionResult> GetRoomById(string roomId)
        {
            if (!long.TryParse(roomId, out long resolvedId))
                return NotFound();

            var room = RoomDB.GetRoom(resolvedId);
            long? accountId = AuthStuff.GetPlayerId(Request);

            return CanViewRoomDirectly(room, accountId)
                ? Ok(RoomDB.PrepareRoomForClient(room))
                : NotFound();
        }

        [HttpDelete("rooms/{roomId}")]
        public IActionResult DeleteRoom(long roomId)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
            {
                return Unauthorized(MutationFailure(
                    "unauthorized",
                    "Authentication is required to delete a room."));
            }

            var room = RoomDB.GetRoom(roomId);

            if (room == null)
                return Ok(MutationSuccess(true));

            if (room.IsDorm || room.IsRRO || RoomDB.IsCanonicalBaseRoom(room))
            {
                return BadRequest(MutationFailure(
                    "protected_room",
                    "This server-managed room cannot be deleted."));
            }

            if (room.CreatorAccountId != accountId.Value)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    MutationFailure(
                        "forbidden",
                        "Only the room creator can delete this room."));
            }

            int deletedSaveCount = RoomDB.SubRoomDataSaves.DeleteMany(save =>
                save.RoomId == roomId);
            int deletedBanCount = RoomDB.RoomBans.DeleteMany(ban =>
                ban.RoomId == roomId);

            if (!RoomDB.DeleteRoom(roomId))
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    MutationFailure(
                        "room_delete_failed",
                        "The room could not be deleted."));
            }

            Console.WriteLine(
                $"[ROOM DELETE] room={roomId} by={accountId.Value} " +
                $"deletedSaves={deletedSaveCount} deletedBans={deletedBanCount} " +
                $"format=api-result success=true");

            return Ok(MutationSuccess(true));
        }

        [HttpPut("rooms/{roomid}/description")]
        public IActionResult UpdateDescription(long roomId, [FromForm] string description)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);

            if (room == null)
                return NotFound();

            bool canEdit =
                room.CreatorAccountId == accountId.Value ||
                room.Roles?.Any(role =>
                    role.AccountId == accountId.Value &&
                    role.Role is RoomDBClasses.Role.Creator or RoomDBClasses.Role.CoOwner) == true;
            if (!canEdit)
                return StatusCode(403);

            description = description?.Trim() ?? string.Empty;
            if (description.Length > 1_000 || description.Any(ch => ch == '\0'))
                return BadRequest(new { error = "Room descriptions cannot exceed 1,000 characters." });

            room.Description = description;

            RoomDB.Rooms.Update(room);

            return Ok(new { success = true });
        }

        [HttpPut("rooms/{roomId}/image")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> UpdateRoomImage(long roomId)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
            {
                return Unauthorized(MutationFailure(
                    "unauthorized",
                    "Authentication is required to change a room image."));
            }

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
            {
                return NotFound(MutationFailure(
                    "room_not_found",
                    "Room was not found."));
            }

            if (!CanEditRoom(room, accountId.Value))
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    MutationFailure(
                        "forbidden",
                        "You do not have permission to change this room image."));
            }

            string? imageName =
                Request.Query["imageName"].FirstOrDefault() ??
                Request.Query["ImageName"].FirstOrDefault();

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
                imageName ??=
                    form["imageName"].FirstOrDefault() ??
                    form["ImageName"].FirstOrDefault() ??
                    form["image"].FirstOrDefault() ??
                    form["Image"].FirstOrDefault();
            }
            else if (Request.ContentLength.GetValueOrDefault() > 0)
            {
                using var reader = new StreamReader(
                    Request.Body,
                    System.Text.Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1_024,
                    leaveOpen: true);

                string rawBody = await reader.ReadToEndAsync(HttpContext.RequestAborted);
                if (!string.IsNullOrWhiteSpace(rawBody))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(rawBody);
                        var root = document.RootElement;

                        if (root.ValueKind == JsonValueKind.String)
                        {
                            imageName ??= root.GetString();
                        }
                        else if (root.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var property in root.EnumerateObject())
                            {
                                if (property.Name.Equals("imageName", StringComparison.OrdinalIgnoreCase) ||
                                    property.Name.Equals("image", StringComparison.OrdinalIgnoreCase))
                                {
                                    imageName ??= property.Value.ValueKind == JsonValueKind.String
                                        ? property.Value.GetString()
                                        : property.Value.ToString();
                                    break;
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        imageName ??= rawBody.Trim().Trim('"');
                    }
                }
            }

            imageName = imageName?.Trim();
            if (string.IsNullOrWhiteSpace(imageName))
            {
                return BadRequest(MutationFailure(
                    "invalid_image_name",
                    "A valid imageName is required."));
            }

            if (imageName.Length > 512 || imageName.IndexOf('\0') >= 0)
            {
                return BadRequest(MutationFailure(
                    "invalid_image_name",
                    "The room image name is invalid."));
            }

            room.ImageName = imageName;
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);

            if (!RoomDB.Rooms.Update(room))
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    MutationFailure(
                        "room_image_update_failed",
                        "The room image could not be saved."));
            }

            var preparedRoom = RoomDB.PrepareRoomForClient(room);

            Console.WriteLine(
                $"[ROOM IMAGE] room={roomId} imageName={imageName} " +
                $"by={accountId.Value} format=api-result-room");

            return Ok(MutationSuccess(preparedRoom));
        }

        [HttpPut("rooms/{roomId}/accessibility")]
        public async Task<IActionResult> UpdateRoomAccessibility(long roomId)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound(new { success = false, error = "Room was not found." });
            if (!CanEditRoom(room, accountId.Value))
                return StatusCode(403);

            var accessibility = await ReadRoomAccessibilityAsync();
            if (accessibility == null)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "Accessibility must be Private (0), Public (1), or Unlisted (2)."
                });
            }

            room.Accessibility = accessibility.Value;
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            RoomDB.Rooms.Update(room);

            Console.WriteLine(
                $"[ROOM ACCESSIBILITY] room={roomId} accessibility={accessibility.Value} by={accountId.Value}");
            return Ok(RoomDB.PrepareRoomForClient(room));
        }

        [HttpPut("rooms/{roomId}/subrooms/{subRoomId}/accessibility")]
        public async Task<IActionResult> UpdateSubRoomAccessibility(long roomId, long subRoomId)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            var subRoom = room?.SubRooms?.FirstOrDefault(value => value.SubRoomId == subRoomId);
            if (room == null || subRoom == null)
                return NotFound(new { success = false, error = "Room or subroom was not found." });
            if (!CanEditRoom(room, accountId.Value))
                return StatusCode(403);

            var accessibility = await ReadRoomAccessibilityAsync();
            if (accessibility == null)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "Accessibility must be Private (0), Public (1), or Unlisted (2)."
                });
            }

            subRoom.Accessibility = accessibility.Value;
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            RoomDB.Rooms.Update(room);

            Console.WriteLine(
                $"[SUBROOM ACCESSIBILITY] room={roomId} subroom={subRoomId} accessibility={accessibility.Value} by={accountId.Value}");
            return Ok(MutationSuccess(RoomDB.PrepareRoomForClient(room)));
        }

        [HttpPut("rooms/{roomId}/subrooms/{subRoomId}/modify")]
        [HttpPost("rooms/{roomId}/subrooms/{subRoomId}/modify")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> ModifySubRoom(long roomId, long subRoomId)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            var subRoom = room?.SubRooms?.FirstOrDefault(value =>
                value.SubRoomId == subRoomId);

            if (room == null || subRoom == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = "Room or subroom was not found."
                });
            }

            if (!CanEditRoom(room, accountId.Value))
                return StatusCode(StatusCodes.Status403Forbidden);

            string? name = Request.Query["name"].FirstOrDefault();
            string? accessibilityValue =
                Request.Query["accessibility"].FirstOrDefault();
            string? maxPlayersValue =
                Request.Query["maxPlayers"].FirstOrDefault();

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(HttpContext.RequestAborted);

                name ??= form["name"].FirstOrDefault()
                    ?? form["Name"].FirstOrDefault();
                accessibilityValue ??= form["accessibility"].FirstOrDefault()
                    ?? form["Accessibility"].FirstOrDefault();
                maxPlayersValue ??= form["maxPlayers"].FirstOrDefault()
                    ?? form["MaxPlayers"].FirstOrDefault();
            }
            else if (Request.ContentLength.GetValueOrDefault() > 0)
            {
                using var reader = new StreamReader(
                    Request.Body,
                    System.Text.Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 4_096,
                    leaveOpen: true);

                string rawBody = await reader.ReadToEndAsync(
                    HttpContext.RequestAborted);

                if (!string.IsNullOrWhiteSpace(rawBody))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(rawBody);
                        JsonElement root = document.RootElement;

                        if (root.ValueKind == JsonValueKind.Object)
                        {
                            if (name == null &&
                                TryGetProperty(root, "name", out var nameElement) &&
                                nameElement.ValueKind == JsonValueKind.String)
                            {
                                name = nameElement.GetString();
                            }

                            if (accessibilityValue == null &&
                                TryGetProperty(
                                    root,
                                    "accessibility",
                                    out var accessibilityElement))
                            {
                                accessibilityValue = accessibilityElement.ToString();
                            }

                            if (maxPlayersValue == null &&
                                TryGetProperty(
                                    root,
                                    "maxPlayers",
                                    out var maxPlayersElement))
                            {
                                maxPlayersValue = maxPlayersElement.ToString();
                            }
                        }
                    }
                    catch (JsonException)
                    {

                        foreach (string pair in rawBody.Split(
                                     '&',
                                     StringSplitOptions.RemoveEmptyEntries))
                        {
                            string[] parts = pair.Split('=', 2);
                            if (parts.Length != 2)
                                continue;

                            string key = Uri.UnescapeDataString(parts[0]);
                            string value = Uri.UnescapeDataString(
                                parts[1].Replace('+', ' '));

                            if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                                name ??= value;
                            else if (key.Equals(
                                         "accessibility",
                                         StringComparison.OrdinalIgnoreCase))
                                accessibilityValue ??= value;
                            else if (key.Equals(
                                         "maxPlayers",
                                         StringComparison.OrdinalIgnoreCase))
                                maxPlayersValue ??= value;
                        }
                    }
                }
            }

            bool changed = false;

            if (name != null)
            {
                name = name.Trim();
                if (name.Length is < 1 or > 50 ||
                    name.Any(character => character == '\0'))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Subroom names must be 1-50 characters."
                    });
                }

                if (!string.Equals(subRoom.Name, name, StringComparison.Ordinal))
                {
                    subRoom.Name = name;
                    changed = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(accessibilityValue))
            {
                RoomDBClasses.RoomAccessibility accessibility;
                bool validAccessibility;

                if (int.TryParse(
                        accessibilityValue,
                        out int numericAccessibility) &&
                    Enum.IsDefined(
                        typeof(RoomDBClasses.RoomAccessibility),
                        numericAccessibility))
                {
                    accessibility =
                        (RoomDBClasses.RoomAccessibility)numericAccessibility;
                    validAccessibility = true;
                }
                else
                {
                    validAccessibility = Enum.TryParse(
                            accessibilityValue,
                            true,
                            out accessibility) &&
                        Enum.IsDefined(
                            typeof(RoomDBClasses.RoomAccessibility),
                            accessibility);
                }

                if (!validAccessibility)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Accessibility must be Private (0), Public (1), or Unlisted (2)."
                    });
                }

                if (subRoom.Accessibility != accessibility)
                {
                    subRoom.Accessibility = accessibility;
                    changed = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(maxPlayersValue))
            {
                if (!int.TryParse(maxPlayersValue, out int maxPlayers) ||
                    maxPlayers is < 1 or > 40)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "MaxPlayers must be between 1 and 40."
                    });
                }

                if (subRoom.MaxPlayers != maxPlayers)
                {
                    subRoom.MaxPlayers = maxPlayers;
                    changed = true;
                }
            }

            if (changed)
            {
                room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
                if (!RoomDB.Rooms.Update(room))
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, new
                    {
                        success = false,
                        error = "The subroom changes could not be saved."
                    });
                }
            }

            Console.WriteLine(
                $"[SUBROOM MODIFY] room={roomId} subroom={subRoomId} " +
                $"name={subRoom.Name} accessibility={subRoom.Accessibility} " +
                $"maxPlayers={subRoom.MaxPlayers} changed={changed.ToString().ToLowerInvariant()} " +
                $"by={accountId.Value}");

            return Ok(MutationSuccess(RoomDB.PrepareRoomForClient(room)));
        }

        [HttpGet("rooms/ownedby/me")]
        public IActionResult GetRoomsOwnedByMe(
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 200);

            var all = RoomDB.Rooms.Find(r => r.CreatorAccountId == (long)id).ToList();
            var page = RoomDB.PrepareRoomsForClient(all.Skip(skip).Take(take));

            return Ok(new
            {
                Results = page,
                TotalResults = all.Count
            });
        }

        [HttpGet("rooms/ownedby/{ownerId}")]
        public IActionResult GetRoomsOwnedBy(
            long ownerId,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            long? callerId = AuthStuff.GetPlayerId(Request);
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 200);

            var all = RoomDB.Rooms.Find(r => r.CreatorAccountId == ownerId)
                .Where(room =>
                    !room.IsDorm &&
                    (callerId == ownerId ||
                     IsPubliclyDiscoverableRoom(room)))
                .ToList();
            var page = RoomDB.PrepareRoomsForClient(all.Skip(skip).Take(take));

            return Ok(new
            {
                Results = page,
                TotalResults = all.Count
            });
        }

        [HttpPost("rooms/{roomId}/subrooms/{subRoomId}/data")]
        [HttpPut("rooms/{roomId}/subrooms/{subRoomId}/data")]
        [RequestSizeLimit(1024 * 1024)]
        public async Task<IActionResult> SetSubRoomData(long roomId, long subRoomId)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            var subRoom = room?.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);

            if (room == null || subRoom == null)
                return NotFound(new { success = false, error = "Room or subroom was not found." });

            SubRoomDataRequest? payload;

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                payload = new SubRoomDataRequest
                {
                    Filename = form["filename"].FirstOrDefault(),
                    SubRoomDataFilename = form["subRoomDataFilename"].FirstOrDefault(),
                    ObjectDataFilename = form["objectDataFilename"].FirstOrDefault(),
                    DataBlob = form["dataBlob"].FirstOrDefault(),
                    ObjectDataBlob = form["objectDataBlob"].FirstOrDefault(),
                    SubRoomDataBlob = form["subRoomDataBlob"].FirstOrDefault(),
                    RoomDataFilename = form["roomDataFilename"].FirstOrDefault(),
                    SuperRoomDataFilename = form["superRoomDataFilename"].FirstOrDefault(),
                    RoomDataBlob = form["roomDataBlob"].FirstOrDefault(),
                    SuperRoomDataBlob = form["superRoomDataBlob"].FirstOrDefault(),
                    InventionUsage = form["inventionUsage"].FirstOrDefault(),
                    Description = form["description"].FirstOrDefault(),
                    PersistenceVersion = int.TryParse(
                        form["persistenceVersion"].FirstOrDefault(),
                        out int persistenceVersion)
                            ? persistenceVersion
                            : null,
                    AutoPublish = bool.TryParse(
                        form["autoPublish"].FirstOrDefault(),
                        out bool autoPublish)
                            ? autoPublish
                            : null,
                    SavedByAccountId = ReadPositiveFormLong(
                        form,
                        "SavedByAccountId",
                        "savedByAccountId"),
                    SavedByPlayerId = ReadPositiveFormLong(
                        form,
                        "SavedByPlayerId",
                        "savedByPlayerId"),
                    SaveRequesterAccountId = ReadPositiveFormLong(
                        form,
                        "SaveRequesterAccountId",
                        "saveRequesterAccountId"),
                    SaveRequesterPlayerId = ReadPositiveFormLong(
                        form,
                        "SaveRequesterPlayerId",
                        "saveRequesterPlayerId"),
                    SaveRequestedByAccountId = ReadPositiveFormLong(
                        form,
                        "SaveRequestedByAccountId",
                        "saveRequestedByAccountId"),
                    SaveRequestedByPlayerId = ReadPositiveFormLong(
                        form,
                        "SaveRequestedByPlayerId",
                        "saveRequestedByPlayerId"),
                    RequesterAccountId = ReadPositiveFormLong(
                        form,
                        "RequesterAccountId",
                        "requesterAccountId"),
                    RequesterPlayerId = ReadPositiveFormLong(
                        form,
                        "RequesterPlayerId",
                        "requesterPlayerId"),
                    RequestingAccountId = ReadPositiveFormLong(
                        form,
                        "RequestingAccountId",
                        "requestingAccountId"),
                    RequestingPlayerId = ReadPositiveFormLong(
                        form,
                        "RequestingPlayerId",
                        "requestingPlayerId"),
                    RequestedByAccountId = ReadPositiveFormLong(
                        form,
                        "RequestedByAccountId",
                        "requestedByAccountId"),
                    RequestedByPlayerId = ReadPositiveFormLong(
                        form,
                        "RequestedByPlayerId",
                        "requestedByPlayerId")
                };
            }
            else
            {

                if (Request.ContentLength is 0)
                    return BadRequest(new { success = false, error = "Save payload was empty." });

                try
                {
                    payload = await JsonSerializer.DeserializeAsync<SubRoomDataRequest>(
                        Request.Body,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        },
                        HttpContext.RequestAborted);
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"[ROOM SAVE] Invalid JSON for room {roomId}: {ex.Message}");
                    return BadRequest(new { success = false, error = "Invalid save payload." });
                }
            }

            if (payload == null)
                return BadRequest(new { success = false, error = "Save payload was invalid." });

            long transportAccountId = accountId.Value;

            string? roomBlob = FirstBlobName(
                payload.RoomDataFilename,
                payload.SuperRoomDataFilename,
                payload.RoomData?.Filename,
                payload.RoomDataBlob,
                payload.SuperRoomDataBlob);
            string? objectBlob = FirstBlobName(
                payload.Filename,
                payload.SubRoomDataFilename,
                payload.ObjectDataFilename,
                payload.SubRoomData?.Filename,
                payload.DataBlob,
                payload.SubRoomDataBlob,
                payload.ObjectDataBlob);

            bool hasObjectUploader = TryGetRecentBlobUpload(
                objectBlob,
                out var objectUpload);
            bool hasRoomUploader = TryGetRecentBlobUpload(
                roomBlob,
                out var roomUpload);
            long objectUploaderAccountId = hasObjectUploader
                ? objectUpload.AccountId
                : 0;
            long roomUploaderAccountId = hasRoomUploader
                ? roomUpload.AccountId
                : 0;

            if (hasObjectUploader &&
                objectUploaderAccountId != transportAccountId)
            {
                Console.WriteLine(
                    $"[ROOM SAVE SECURITY] rejected subroom blob owner mismatch " +
                    $"room={roomId} subroom={subRoomId} transport={transportAccountId} " +
                    $"uploader={objectUploaderAccountId} blob={objectBlob ?? "null"}");

                return StatusCode(403, new
                {
                    success = false,
                    error = "The subroom data was uploaded by a different player.",
                    roomId,
                    subRoomId
                });
            }

            if (hasRoomUploader &&
                roomUploaderAccountId != transportAccountId)
            {
                Console.WriteLine(
                    $"[ROOM SAVE SECURITY] rejected room blob owner mismatch " +
                    $"room={roomId} subroom={subRoomId} transport={transportAccountId} " +
                    $"uploader={roomUploaderAccountId} blob={roomBlob ?? "null"}");

                return StatusCode(403, new
                {
                    success = false,
                    error = "The room data was uploaded by a different player.",
                    roomId,
                    subRoomId
                });
            }

            HashSet<long> claimedRequesterIds =
                GetClaimedSaveRequesterIds(payload);
            if (claimedRequesterIds.Count > 1)
            {
                string conflictingIds = string.Join(
                    ",",
                    claimedRequesterIds.OrderBy(value => value));
                Console.WriteLine(
                    $"[ROOM SAVE SECURITY] conflicting requester claims " +
                    $"room={roomId} subroom={subRoomId} " +
                    $"transport={transportAccountId} claims={conflictingIds}");

                return BadRequest(new
                {
                    success = false,
                    error = "Conflicting Save Room requester account IDs were supplied.",
                    errorCode = "conflicting_save_requester_ids",
                    roomId,
                    subRoomId
                });
            }

            long? claimedRequesterAccountId = claimedRequesterIds.Count == 1
                ? claimedRequesterIds.First()
                : null;
            long saveActorAccountId =
                claimedRequesterAccountId ?? transportAccountId;
            bool delegatedByRoomAuthority =
                saveActorAccountId != transportAccountId;
            string actorSource = delegatedByRoomAuthority
                ? "validated-save-requester-via-room-authority"
                : claimedRequesterAccountId.HasValue
                    ? "requester-matches-authenticated-transport"
                    : "authenticated-transport-fallback";

            bool requesterCanSave = CanAccountSaveRoom(
                room,
                saveActorAccountId,
                out bool requesterIsDeveloper);
            if (!requesterCanSave)
            {
                Console.WriteLine(
                    $"[ROOM SAVE SECURITY] requester lacks permission " +
                    $"room={roomId} subroom={subRoomId} " +
                    $"transport={transportAccountId} " +
                    $"requester={saveActorAccountId} " +
                    $"owner={room.CreatorAccountId} " +
                    $"isDeveloper={requesterIsDeveloper}");

                return StatusCode(403, new
                {
                    success = false,
                    error = "The player who requested Save Room does not have permission.",
                    transportAccountId,
                    saveRequesterAccountId = saveActorAccountId,
                    creatorAccountId = room.CreatorAccountId,
                    roomId,
                    subRoomId
                });
            }

            if (delegatedByRoomAuthority)
            {
                DateTime newestAllowedUpload =
                    DateTime.UtcNow - TimeSpan.FromMinutes(5);
                bool hasFreshAuthorityUploads =
                    hasObjectUploader &&
                    hasRoomUploader &&
                    objectUpload.UploadedAtUtc >= newestAllowedUpload &&
                    roomUpload.UploadedAtUtc >= newestAllowedUpload;

                if (!hasFreshAuthorityUploads)
                {
                    Console.WriteLine(
                        $"[ROOM SAVE SECURITY] delegated requester missing fresh uploads " +
                        $"room={roomId} subroom={subRoomId} " +
                        $"transport={transportAccountId} " +
                        $"requester={saveActorAccountId}");

                    return StatusCode(403, new
                    {
                        success = false,
                        error = "Room Authority must upload both save blobs immediately before a delegated save.",
                        errorCode = "delegated_save_upload_proof_required",
                        roomId,
                        subRoomId
                    });
                }

                if (!AreActiveInSameRoomInstance(
                        roomId,
                        subRoomId,
                        transportAccountId,
                        saveActorAccountId))
                {
                    Console.WriteLine(
                        $"[ROOM SAVE SECURITY] delegated requester instance mismatch " +
                        $"room={roomId} subroom={subRoomId} " +
                        $"transport={transportAccountId} " +
                        $"requester={saveActorAccountId}");

                    return StatusCode(403, new
                    {
                        success = false,
                        error = "Room Authority and the Save Room requester must be active in the same room instance.",
                        errorCode = "save_requester_instance_mismatch",
                        roomId,
                        subRoomId
                    });
                }
            }

            Console.WriteLine(
                $"[ROOM SAVE REQUESTER] room={roomId} subroom={subRoomId} " +
                $"transportAuthority={transportAccountId} " +
                $"requester={saveActorAccountId} " +
                $"claimed={claimedRequesterAccountId?.ToString() ?? "none"} " +
                $"source={actorSource} objectBlob={objectBlob ?? "null"}");

            static bool BlobExists(string? filename)
            {
                if (string.IsNullOrWhiteSpace(filename))
                    return false;

                string safeName = Path.GetFileName(filename);
                if (!string.Equals(safeName, filename, StringComparison.Ordinal))
                    return false;

                return System.IO.File.Exists(
                    Path.Combine(Program.dataDir, "CDN", "room", safeName));
            }

            if (string.IsNullOrWhiteSpace(roomBlob) || !BlobExists(roomBlob))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "Room data blob does not exist."
                });
            }

            if (string.IsNullOrWhiteSpace(objectBlob) || !BlobExists(objectBlob))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "Subroom data blob does not exist."
                });
            }

            if (string.Equals(roomBlob, objectBlob, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(
                    $"[CV2 SNAPSHOT GUARD] rejected aliased save room={roomId} " +
                    $"subroom={subRoomId} blob={roomBlob}");
                return BadRequest(new
                {
                    success = false,
                    error = "Room data and subroom data must be separate uploaded blobs."
                });
            }

            room.DataBlob = roomBlob;
            subRoom.DataBlob = objectBlob;

            if (payload.Description != null)
            {
                string description = payload.Description.Trim();
                if (description.Length > 1_000 || description.Any(ch => ch == '\0'))
                    return BadRequest(new { success = false, error = "Room description is too long." });
                room.Description = description;
            }

            room.PersistenceVersion =
                payload.PersistenceVersion ?? room.PersistenceVersion;

            string objectBlobPath = Path.Combine(
                Program.dataDir,
                "CDN",
                "room",
                Path.GetFileName(objectBlob));

            long objectBlobLength = new FileInfo(objectBlobPath).Length;
            string objectBlobHash =
                hasObjectUploader &&
                objectUpload.Size == objectBlobLength &&
                !string.IsNullOrWhiteSpace(objectUpload.Hash)
                    ? objectUpload.Hash
                    : await ComputeFileSha256Base64Async(
                        objectBlobPath,
                        HttpContext.RequestAborted);

            subRoom.SavedByAccountId = saveActorAccountId;
            var dataSave = RoomDB.GetOrCreateSubRoomDataSave(
                roomId,
                subRoomId,
                saveActorAccountId,
                objectBlob,
                room.PersistenceVersion,
                payload.AutoPublish == true,
                objectBlobHash,
                payload.Description ?? room.Description,
                roomBlob,
                out bool createdSave);

            if (!string.IsNullOrWhiteSpace(payload.UnityAssetId) &&
                Guid.TryParse(payload.UnityAssetId, out _))
            {
                dataSave.UnityAssetId = payload.UnityAssetId.Trim();
                dataSave.RoomDataBlob = string.IsNullOrWhiteSpace(dataSave.RoomDataBlob)
                    ? dataSave.DataBlob
                    : dataSave.RoomDataBlob;
                RoomDB.SubRoomDataSaves.Update(dataSave);
            }

            if (payload.BakedUnityAssets is { Count: > 0 })
            {
                var resolvedBakedAssets = new List<RoomDBClasses.BakedUnityAsset>();
                foreach (var candidate in payload.BakedUnityAssets)
                {
                    if (string.IsNullOrWhiteSpace(candidate.UnityAssetId) ||
                        !Guid.TryParse(candidate.UnityAssetId, out _))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            error = "Each baked Unity asset needs a valid UnityAssetId.",
                            errorCode = "invalid_baked_asset_id"
                        });
                    }

                    if (!TryGetRecentBlobUpload(candidate.Filename, out var bundleUpload) ||
                        bundleUpload.AccountId != transportAccountId)
                    {
                        Console.WriteLine(
                            $"[ROOM SAVE SECURITY] rejected baked asset owner mismatch " +
                            $"room={roomId} subroom={subRoomId} transport={transportAccountId} " +
                            $"unityAssetId={candidate.UnityAssetId} filename={candidate.Filename ?? "null"}");

                        return StatusCode(403, new
                        {
                            success = false,
                            error = "A baked asset bundle was not uploaded by this player.",
                            errorCode = "baked_asset_owner_mismatch",
                            unityAssetId = candidate.UnityAssetId
                        });
                    }

                    string bundlePath = Path.Combine(
                        Program.dataDir,
                        "CDN",
                        "assetbundles",
                        Path.GetFileName(candidate.Filename!));
                    if (!System.IO.File.Exists(bundlePath))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            error = "A referenced baked asset bundle does not exist.",
                            unityAssetId = candidate.UnityAssetId
                        });
                    }

                    resolvedBakedAssets.Add(new RoomDBClasses.BakedUnityAsset
                    {
                        UnityAssetId = candidate.UnityAssetId.Trim(),
                        Target = candidate.Target,
                        Version = candidate.Version > 0 ? candidate.Version : dataSave.SubRoomDataSaveId is > 0 and <= int.MaxValue
                            ? (int)dataSave.SubRoomDataSaveId
                            : 1,
                        Filename = Path.GetFileName(candidate.Filename!),
                        Hash = bundleUpload.Hash,
                        UnityVersion = candidate.UnityVersion,
                        IsAvailable = true
                    });
                }

                dataSave.BakedUnityAssets = resolvedBakedAssets;
                RoomDB.SubRoomDataSaves.Update(dataSave);

                Console.WriteLine(
                    $"[ROOM SAVE] room={roomId} subroom={subRoomId} " +
                    $"subRoomDataSaveId={dataSave.SubRoomDataSaveId} " +
                    $"bakedUnityAssets={resolvedBakedAssets.Count} " +
                    $"targets=[{string.Join(",", resolvedBakedAssets.Select(a => a.Target))}]");
            }

            room.UgcVersion = createdSave
                ? Math.Max(1, room.UgcVersion + 1)
                : Math.Max(1, room.UgcVersion);
            subRoom.SubRoomDataSaveId = dataSave.SubRoomDataSaveId;
            subRoom.SubRoomDataSave = dataSave;

            if (!RoomDB.Rooms.Update(room))
            {
                return StatusCode(500, new
                {
                    error = "Failed to save the room database record."
                });
            }

            RememberPendingSaveAttribution(
                roomId,
                subRoomId,
                dataSave.SubRoomDataSaveId,
                transportAccountId,
                claimedRequesterAccountId);

            Console.WriteLine(
                $"[ROOM SAVE] room={roomId} subroom={subRoomId} " +
                $"superRoomBlob={room.DataBlob ?? "null"} " +
                $"subRoomBlob={subRoom.DataBlob ?? "null"} " +
                $"subRoomDataSaveId={dataSave.SubRoomDataSaveId} " +
                $"transport={transportAccountId} actor={saveActorAccountId} " +
                $"actorSource={actorSource} created={createdSave} " +
                $"persistenceVersion={room.PersistenceVersion}"
            );

            var saveResult = new
            {
                Room = RoomDB.PrepareRoomForClient(room),
                SubRoomDataSave = dataSave
            };

            return Ok(MutationSuccess(saveResult));
        }

        public class SubRoomDataRequest
        {
            public string? Filename { get; set; }
            public string? SubRoomDataFilename { get; set; }
            public string? ObjectDataFilename { get; set; }
            public string? DataBlob { get; set; }
            public string? ObjectDataBlob { get; set; }
            public string? SubRoomDataBlob { get; set; }
            public string? RoomDataFilename { get; set; }
            public string? SuperRoomDataFilename { get; set; }
            public string? RoomDataBlob { get; set; }
            public string? SuperRoomDataBlob { get; set; }
            public long? SavedByAccountId { get; set; }
            public long? SavedByPlayerId { get; set; }
            public long? SaveRequesterAccountId { get; set; }
            public long? SaveRequesterPlayerId { get; set; }
            public long? SaveRequestedByAccountId { get; set; }
            public long? SaveRequestedByPlayerId { get; set; }
            public long? RequesterAccountId { get; set; }
            public long? RequesterPlayerId { get; set; }
            public long? RequestingAccountId { get; set; }
            public long? RequestingPlayerId { get; set; }
            public long? RequestedByAccountId { get; set; }
            public long? RequestedByPlayerId { get; set; }
            public string? UnityAssetId { get; set; }
            public BlobRef? RoomData { get; set; }
            public BlobRef? SubRoomData { get; set; }
            public string? InventionUsage { get; set; }
            public int? PersistenceVersion { get; set; }
            public string? Description { get; set; }
            public bool? AutoPublish { get; set; }
            public List<BakedUnityAssetRef>? BakedUnityAssets { get; set; }

            public class BlobRef
            {
                public string? Filename { get; set; }
                public string? Hash { get; set; }
                public string? OwnershipProof { get; set; }
            }

            public class BakedUnityAssetRef
            {
                public string? UnityAssetId { get; set; }
                public int Target { get; set; }
                public int Version { get; set; }
                public string? Filename { get; set; }
                public string? UnityVersion { get; set; }
            }
        }

        private static RoomDBClasses.BakedUnityAsset? SelectBakedUnityAsset(
            RoomDBClasses.SubRoomDataSave save,
            int target,
            int version)
        {
            var matches = (save.BakedUnityAssets ?? new List<RoomDBClasses.BakedUnityAsset>())
                .Where(asset => asset.Target == target)
                .ToList();

            if (matches.Count == 0)
                matches = (save.BakedUnityAssets ?? new List<RoomDBClasses.BakedUnityAsset>()).ToList();
            if (matches.Count == 0)
                return null;

            if (version > 0)
            {
                var exact = matches.FirstOrDefault(asset => asset.Version == version);
                if (exact != null)
                    return exact;
            }

            return matches
                .OrderByDescending(asset => asset.Version)
                .ThenBy(asset => asset.Target == target ? 0 : 1)
                .First();
        }

        private static object ToBakedUnityAssetPayload(RoomDBClasses.BakedUnityAsset asset)
        {
            string baseUrl = ServerConfig.BaseURL.TrimEnd('/');
            string safeName = Path.GetFileName(asset.Filename);
            string url = $"{baseUrl}/cdn/assetbundles/{Uri.EscapeDataString(safeName)}";
            bool exists = System.IO.File.Exists(Path.Combine(
                Program.dataDir,
                "CDN",
                "assetbundles",
                safeName));

            return new
            {
                asset.UnityAssetId,
                asset.Target,
                asset.Version,
                Filename = safeName,
                asset.Hash,
                asset.UnityVersion,
                IsAvailable = exists,
                Url = url,
                AssetBundleUrl = url,
                CdnPath = $"assetbundles/{safeName}"
            };
        }

        private static object ToSubRoomSavePayload(
            RoomDBClasses.SubRoomDataSave save,
            int unityAssetTarget,
            int unityAssetVersion)
        {
            var selectedAsset = SelectBakedUnityAsset(
                save,
                unityAssetTarget,
                unityAssetVersion);
            object? selectedPayload = selectedAsset == null
                ? null
                : ToBakedUnityAssetPayload(selectedAsset);

            return new
            {
                save.SubRoomDataSaveId,
                save.RoomId,
                save.SubRoomId,
                save.SavedByAccountId,
                save.DataBlob,
                SubRoomDataFilename = save.DataBlob,
                save.DataBlobHash,
                SubRoomDataHash = save.DataBlobHash,
                PersistenceVersion = RoomDB.NormalizeLegacyPersistenceVersion(
                    save.PersistenceVersion),
                save.SavedOnPlatform,
                save.SavedOnDeviceClass,
                save.Description,
                save.UnityAssetId,
                ReferencedUnityAssetIds = save.ReferencedUnityAssetIds ?? new List<string>(),
                OMVersion = RoomDB.NormalizeLegacyOMVersion(save.OMVersion),
                UgcSubVersion = RoomDB.NormalizeLegacyUgcSubVersion(
                    save.UgcSubVersion),
                save.ModerationState,
                Tags = save.Tags ?? new List<string>(),
                save.CreatedAt,
                DataSavedAt = save.CreatedAt,
                UnityAsset = selectedPayload,
                BakedUnityAsset = selectedPayload,
                BakedUnityAssets = (save.BakedUnityAssets ?? new List<RoomDBClasses.BakedUnityAsset>())
                    .Select(ToBakedUnityAssetPayload)
                    .ToList()
            };
        }

        [HttpGet("bakedunityassets/{unityAssetId}")]
        [HttpGet("bakedunityassets/{unityAssetId}/{target:int}")]
        [HttpGet("unityassets/{unityAssetId}")]
        [HttpGet("unityassets/{unityAssetId}/{target:int}")]
        [HttpGet("/api/rooms/v1/bakedunityassets/{unityAssetId}")]
        [HttpGet("/api/rooms/v2/bakedunityassets/{unityAssetId}")]
        [HttpGet("/api/rooms/v1/unityassets/{unityAssetId}")]
        [HttpGet("/api/rooms/v2/unityassets/{unityAssetId}")]
        public IActionResult GetBakedUnityAsset(
            string unityAssetId,
            int? target = null,
            [FromQuery] int? unityAssetTarget = null,
            [FromQuery] int? version = null,
            [FromQuery] int? unityAssetVersion = null)
        {
            if (!Guid.TryParse(unityAssetId, out _))
                return BadRequest(new { error = "UnityAssetId must be a GUID." });

            int requestedTarget = target ?? unityAssetTarget ?? 0;
            int requestedVersion = unityAssetVersion ?? version ?? 0;

            var saves = RoomDB.SubRoomDataSaves
                .FindAll()
                .Where(save =>
                    string.Equals(
                        save.UnityAssetId,
                        unityAssetId,
                        StringComparison.OrdinalIgnoreCase) ||
                    (save.ReferencedUnityAssetIds ?? new List<string>())
                        .Any(reference => string.Equals(
                            reference,
                            unityAssetId,
                            StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(save => save.CreatedAt)
                .ToList();

            foreach (var save in saves)
            {
                var asset = SelectBakedUnityAsset(save, requestedTarget, requestedVersion);
                if (asset != null && string.Equals(
                        asset.UnityAssetId,
                        unityAssetId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Ok(ToBakedUnityAssetPayload(asset));
                }
            }

            return NotFound(new
            {
                error = "Baked Unity asset not found.",
                unityAssetId,
                target = requestedTarget,
                version = requestedVersion
            });
        }

        [HttpGet("rooms/{roomId}/subrooms/{subRoomId}/saves/{subRoomDataSaveId}/bakedunityasset")]
        [HttpGet("rooms/{roomId}/subrooms/{subRoomId}/saves/{subRoomDataSaveId}/unityasset")]
        public IActionResult GetSubRoomSaveBakedUnityAsset(
            long roomId,
            long subRoomId,
            long subRoomDataSaveId,
            [FromQuery] int unityAssetTarget = 0,
            [FromQuery] int unityAssetVersion = 0)
        {
            var save = RoomDB.SubRoomDataSaves.FindById(subRoomDataSaveId);
            if (save == null || save.RoomId != roomId || save.SubRoomId != subRoomId)
                return NotFound();

            var asset = SelectBakedUnityAsset(save, unityAssetTarget, unityAssetVersion);
            return asset == null
                ? NotFound(new { error = "This save has no baked Unity asset for that target." })
                : Ok(ToBakedUnityAssetPayload(asset));
        }

        [HttpGet("rooms/{roomId}/subrooms/{subRoomId}/saves")]
        public IActionResult GetSubRoomSaves(
            long roomId,
            long subRoomId,
            [FromQuery] long? subRoomDataSaveId = null,
            [FromQuery] int unityAssetTarget = 0,
            [FromQuery] int unityAssetVersion = 1,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 20)
        {
            var room = RoomDB.GetRoom(roomId);
            var subRoom = room?.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (room == null ||
                subRoom == null ||
                !CanViewSubRoomDirectly(room, subRoom, accountId))
                return NotFound();

            RoomDB.EnsureSubRoomDataSave(room, subRoom);

            if (subRoomDataSaveId.HasValue)
            {
                return GetSubRoomDataSave(
                    roomId,
                    subRoomId,
                    subRoomDataSaveId.Value,
                    unityAssetTarget,
                    unityAssetVersion);
            }

            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 100);
            var all = RoomDB.SubRoomDataSaves.Find(x => x.RoomId == roomId && x.SubRoomId == subRoomId)
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.SubRoomDataSaveId)
                .Skip(skip)
                .Take(take)
                .Select(save => ToSubRoomSavePayload(
                    save,
                    unityAssetTarget,
                    unityAssetVersion))
                .ToList();

            return Ok(all);
        }

        [HttpGet("rooms/{roomId}/subrooms/{subRoomId}/saves/{subRoomDataSaveId}")]
        [HttpGet("rooms/{roomId}/subrooms/{subRoomId}/datasaves/{subRoomDataSaveId}")]
        public IActionResult GetSubRoomDataSave(
            long roomId,
            long subRoomId,
            long subRoomDataSaveId,
            [FromQuery] int unityAssetTarget = 0,
            [FromQuery] int unityAssetVersion = 0)
        {
            var room = RoomDB.GetRoom(roomId);
            var subRoom = room?.SubRooms?.FirstOrDefault(s => s.SubRoomId == subRoomId);
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (room == null ||
                subRoom == null ||
                !CanViewSubRoomDirectly(room, subRoom, accountId))
                return NotFound();

            RoomDB.EnsureSubRoomDataSave(room, subRoom);
            var save = RoomDB.SubRoomDataSaves.FindById(subRoomDataSaveId);
            bool exists = save != null && save.RoomId == roomId && save.SubRoomId == subRoomId;
            Console.WriteLine($"[SUBROOM SAVE GET] saveId={subRoomDataSaveId} blob={save?.DataBlob ?? "null"} exists={exists.ToString().ToLowerInvariant()}");
            return exists
                ? Ok(ToSubRoomSavePayload(save!, unityAssetTarget, unityAssetVersion))
                : NotFound();
        }

        [HttpPost("subroomdatasaves/{subRoomDataSaveId}/publish")]
        [HttpPut("subroomdatasaves/{subRoomDataSaveId}/publish")]
        [HttpPost("rooms/subroomdatasaves/{subRoomDataSaveId}/publish")]
        [HttpPut("rooms/subroomdatasaves/{subRoomDataSaveId}/publish")]
        public IActionResult PublishSubRoomDataSave(long subRoomDataSaveId)
        {
            return PublishSubRoomDataSaveCore(null, null, subRoomDataSaveId);
        }

        [HttpPost("rooms/{roomId}/subrooms/{subRoomId}/publish_save")]
        [HttpPut("rooms/{roomId}/subrooms/{subRoomId}/publish_save")]
        [HttpPost("rooms/{roomId}/subrooms/{subRoomId}/saves/{subRoomDataSaveId}/publish")]
        [HttpPut("rooms/{roomId}/subrooms/{subRoomId}/saves/{subRoomDataSaveId}/publish")]
        [HttpPost("rooms/{roomId}/subrooms/{subRoomId}/datasaves/{subRoomDataSaveId}/publish")]
        [HttpPut("rooms/{roomId}/subrooms/{subRoomId}/datasaves/{subRoomDataSaveId}/publish")]
        public IActionResult PublishSubRoomDataSaveForSubRoom(
            long roomId,
            long subRoomId,
            long subRoomDataSaveId)
        {
            return PublishSubRoomDataSaveCore(roomId, subRoomId, subRoomDataSaveId);
        }

        [HttpPost("rooms/{roomId}/subrooms/{subRoomId}/publish")]
        [HttpPut("rooms/{roomId}/subrooms/{subRoomId}/publish")]
        public IActionResult PublishSubRoomDataSaveFromQuery(
            long roomId,
            long subRoomId,
            [FromQuery] long subRoomDataSaveId)
        {
            return PublishSubRoomDataSaveCore(roomId, subRoomId, subRoomDataSaveId);
        }

        private IActionResult PublishSubRoomDataSaveCore(
            long? expectedRoomId,
            long? expectedSubRoomId,
            long subRoomDataSaveId)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized(MutationFailure("unauthorized", "Authentication is required to publish a subroom save."));

            var save = RoomDB.SubRoomDataSaves.FindById(subRoomDataSaveId);
            var room = save == null ? null : RoomDB.GetRoom(save.RoomId);
            var subRoom = room?.SubRooms?.FirstOrDefault(value =>
                value.SubRoomId == save!.SubRoomId);

            if (save == null || room == null || subRoom == null ||
                (expectedRoomId.HasValue && save.RoomId != expectedRoomId.Value) ||
                (expectedSubRoomId.HasValue && save.SubRoomId != expectedSubRoomId.Value))
            {
                return NotFound(MutationFailure(
                    "subroom_save_not_found",
                    "The subroom save was not found."));
            }

            if (!CanEditRoom(room, accountId.Value))
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    MutationFailure("forbidden", "You cannot publish saves for this room."));
            }

            save.IsPublished = true;
            if (!RoomDB.SubRoomDataSaves.Update(save))
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    MutationFailure("subroom_publish_failed", "The subroom save could not be published."));
            }

            subRoom.SubRoomDataSaveId = save.SubRoomDataSaveId;
            subRoom.SubRoomDataSave = save;
            subRoom.DataBlob = save.DataBlob;
            room.DataBlob = save.RoomDataBlob;
            room.PersistenceVersion = save.PersistenceVersion ?? room.PersistenceVersion;
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            if (!RoomDB.Rooms.Update(room))
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    MutationFailure("room_publish_failed", "The published room pointers could not be saved."));
            }

            var preparedRoom = RoomDB.PrepareRoomForClient(room);
            Console.WriteLine(
                $"[SUBROOM PUBLISH] room={room.RoomId} subroom={save.SubRoomId} " +
                $"save={save.SubRoomDataSaveId} by={accountId.Value} format=api-result-room");

            return Ok(MutationSuccess(preparedRoom));
        }

        [HttpPost("rooms/{roomId}/subrooms")]
        public async Task<IActionResult> AddSubRoom(long roomId)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound(new { success = false, error = "Room was not found." });
            if (!CanEditRoom(room, accountId.Value))
                return StatusCode(403);

            string? name = Request.Query["name"].FirstOrDefault();
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
                name ??= form["name"].FirstOrDefault();
            }
            else if (Request.ContentLength.GetValueOrDefault() > 0)
            {
                using var reader = new StreamReader(Request.Body);
                string rawBody = await reader.ReadToEndAsync();
                try
                {
                    using var document = JsonDocument.Parse(rawBody);
                    if (TryGetProperty(document.RootElement, "name", out var nameElement) &&
                        nameElement.ValueKind == JsonValueKind.String)
                    {
                        name = nameElement.GetString();
                    }
                }
                catch (JsonException)
                {
                    name ??= rawBody.Trim().Trim('"');
                }
            }

            name = name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { success = false, error = "A subroom name is required." });
            if (name.Length > 50 || name.Any(character => character == '\0'))
                return BadRequest(new { success = false, error = "Subroom names must be 1-50 characters." });

            var updatedRoom = RoomDB.AddSubRoom(roomId, accountId.Value, name);
            if (updatedRoom == null)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    MutationFailure("subroom_create_failed", "The subroom could not be created."));
            }

            var preparedRoom = RoomDB.PrepareRoomForClient(updatedRoom);
            Console.WriteLine(
                $"[SUBROOM CREATE] room={roomId} name={name} by={accountId.Value} " +
                $"subrooms={preparedRoom.SubRooms?.Count ?? 0} format=api-result-room");
            return Ok(MutationSuccess(preparedRoom));
        }

        [HttpPost("rooms/{roomId}/subrooms/{subRoomId}/clone")]
        public IActionResult CloneSubRoom(long roomId, long subRoomId)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (room?.SubRooms == null ||
                room.SubRooms.All(subRoom => subRoom.SubRoomId != subRoomId))
                return NotFound(new { success = false, error = "Room or subroom was not found." });
            if (!CanEditRoom(room, accountId.Value))
                return StatusCode(403);

            var updatedRoom = RoomDB.CloneSubRoom(roomId, subRoomId, accountId.Value);
            if (updatedRoom == null)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    MutationFailure("subroom_clone_failed", "The subroom could not be cloned."));
            }

            var preparedRoom = RoomDB.PrepareRoomForClient(updatedRoom);
            Console.WriteLine(
                $"[SUBROOM CLONE] room={roomId} sourceSubroom={subRoomId} " +
                $"by={accountId.Value} format=api-result-room");
            return Ok(MutationSuccess(preparedRoom));
        }

        [HttpDelete("rooms/{roomId}/subrooms/{subRoomId}")]
        public IActionResult DeleteSubRoom(long roomId, long subRoomId)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized(MutationFailure("unauthorized", "Authentication is required to delete a subroom."));

            var room = RoomDB.GetRoom(roomId);
            var subRoom = room?.SubRooms?.FirstOrDefault(value =>
                value.SubRoomId == subRoomId);
            if (room == null || subRoom == null)
            {
                return NotFound(MutationFailure(
                    "subroom_not_found",
                    "Room or subroom was not found."));
            }

            if (!CanEditRoom(room, accountId.Value))
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    MutationFailure("forbidden", "You cannot delete this subroom."));
            }

            if (room.SubRooms == null || room.SubRooms.Count <= 1)
            {
                return BadRequest(MutationFailure(
                    "last_subroom",
                    "A room must contain at least one subroom."));
            }

            room.SubRooms.RemoveAll(value => value.SubRoomId == subRoomId);
            int deletedSaveCount = RoomDB.SubRoomDataSaves.DeleteMany(save =>
                save.RoomId == roomId && save.SubRoomId == subRoomId);
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);

            if (!RoomDB.Rooms.Update(room))
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    MutationFailure("subroom_delete_failed", "The subroom could not be deleted."));
            }

            var preparedRoom = RoomDB.PrepareRoomForClient(room);
            Console.WriteLine(
                $"[SUBROOM DELETE] room={roomId} subroom={subRoomId} " +
                $"deletedSaves={deletedSaveCount} by={accountId.Value} format=api-result-room");
            return Ok(MutationSuccess(preparedRoom));
        }

        [HttpPut("rooms/{roomId}/subrooms/{subRoomId}/permissions")]
        [HttpPost("rooms/{roomId}/subrooms/{subRoomId}/permissions")]
        [HttpPatch("rooms/{roomId}/subrooms/{subRoomId}/permissions")]
        [HttpPut("rooms/{roomId}/subrooms/{subRoomId}/permissions/modify")]
        [HttpPost("rooms/{roomId}/subrooms/{subRoomId}/permissions/modify")]
        [HttpPatch("rooms/{roomId}/subrooms/{subRoomId}/permissions/modify")]
        public async Task<IActionResult> UpdateSubRoomPermissions(long roomId, long subRoomId)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            var subRoom = room?.SubRooms?.FirstOrDefault(value => value.SubRoomId == subRoomId);
            if (room == null || subRoom == null)
                return NotFound(new { success = false, error = "Room or subroom was not found." });
            if (!CanEditRoom(room, accountId.Value))
                return StatusCode(403);

            bool requestHadPayload = Request.QueryString.HasValue ||
                Request.HasFormContentType ||
                (Request.ContentLength ?? 0) > 0 ||
                Request.Headers.TransferEncoding.Count > 0;
            List<RoomDBClasses.SubRoomPermission> permissions =
                await ReadPermissionPayloadFromRequestAsync();

            if (permissions.Count == 0)
            {
                if (requestHadPayload)
                {
                    return BadRequest(MutationFailure(
                        "invalid_permissions",
                        "The permission payload could not be parsed."));
                }

                permissions = subRoom.Permissions?.Count > 0
                    ? subRoom.Permissions.Select(SanitizePermission).ToList()
                    : CreateDefaultSubRoomPermissions();
            }

            subRoom.Permissions = permissions
                .Where(permission => !string.IsNullOrWhiteSpace(permission.Permission))
                .Select(SanitizePermission)
                .GroupBy(permission => new
                {
                    permission.Permission,
                    permission.Role,
                    permission.Type
                })
                .Select(group => group.Last())
                .Take(250)
                .ToList();
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);

            if (!RoomDB.Rooms.Update(room))
                return StatusCode(500, new { success = false, error = "Permissions could not be saved." });

            Console.WriteLine(
                $"[SUBROOM PERMISSIONS] room={roomId} subroom={subRoomId} count={subRoom.Permissions.Count} by={accountId.Value}");
            return Ok(MutationSuccess(RoomDB.PrepareRoomForClient(room)));
        }

        [HttpGet("rooms/visitedby/me")]
        public IActionResult GetVisitedByMe(
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player?.Player == null)
                return Unauthorized();

            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 100);

            var visits = player.Player.PlayerExtra?.RoomVisits?
                .OrderByDescending(v => v.VisitedAt)
                .ToList() ?? new List<PlayerDBClasses.RoomVisit>();

            var roomMap = RoomDB.Rooms.Find(r =>
                    visits.Select(v => v.RoomId).Contains(r.RoomId))
                .ToDictionary(r => r.RoomId);

            var orderedRooms = visits
                .Where(v => roomMap.ContainsKey(v.RoomId))
                .Select(v => roomMap[v.RoomId])
                .Where(room =>
                    RoomDB.CanPlayerAccessRoom(room, player.PlayerId))
                .ToList();

            RoomDB.PrepareRoomsForClient(orderedRooms);

            return Ok(new
            {
                Results = orderedRooms.Skip(skip).Take(take).ToList(),
                TotalResults = orderedRooms.Count
            });
        }

        [HttpGet("rooms/favoritedby/me")]
        public IActionResult GetFavoritedByMe([FromQuery] int skip = 0, [FromQuery] int take = 100)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized();

            var favIds = player.Player?.FavoritedRooms ?? new List<long>();
            var allFavoriteRooms = RoomDB.Rooms.Find(r => favIds.Contains(r.RoomId))
                .Where(room =>
                    RoomDB.CanPlayerAccessRoom(room, player.PlayerId))
                .ToList();

            var rooms = allFavoriteRooms
                .Skip(skip)
                .Take(take)
                .ToList();

            RoomDB.PrepareRoomsForClient(rooms);

            return Ok(new
            {
                Results = rooms,
                TotalResults = allFavoriteRooms.Count
            });
        }

        [HttpPut("rooms/{roomId}/interactionby/me/cheer")]
        public IActionResult CheerRoomInteraction(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            if (!PlayerDB.ToggleCheer((long)id, roomId, out bool nowCheered))
                return NotFound();

            return Ok(new { cheered = nowCheered });
        }

        [HttpGet("/roomserver/rooms/createdby/me")]
        public IActionResult GetMyCreatedRooms(
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 100);

            var allRooms = RoomDB.Rooms.Find(r =>
                    r.CreatorAccountId == id.Value && !r.IsDorm)
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.RoomId)
                .ToList();

            RoomDB.PrepareRoomsForClient(allRooms);

            return Ok(allRooms.Skip(skip).Take(take).ToList());
        }

        [HttpGet("/roomserver/rooms/{roomId}/playerdata/me")]
        public IActionResult GetMyRoomPlayerData(long roomId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (!CanUseRoomPlayerData(room, accountId.Value))
                return NotFound();

            CreatorFeatureDB.RoomPlayerDataRecord? record =
                CreatorFeatureDB.GetRoomPlayerData(roomId, accountId.Value);
            return Ok(BuildRoomPlayerDataResponse(
                roomId,
                accountId.Value,
                record?.DataJson ?? "{}",
                record?.Version ?? 0,
                record?.UpdatedAtUtc));
        }

        [HttpPut("/roomserver/rooms/{roomId}/playerdata/me")]
        [HttpPost("/roomserver/rooms/{roomId}/playerdata/me")]
        [HttpPatch("/roomserver/rooms/{roomId}/playerdata/me")]
        [RequestSizeLimit(256 * 1024)]
        public async Task<IActionResult> SaveMyRoomPlayerData(long roomId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (!CanUseRoomPlayerData(room, accountId.Value))
                return NotFound();

            string rawPlayerData = string.Empty;
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(
                    HttpContext.RequestAborted);
                rawPlayerData =
                    form["data"].FirstOrDefault() ??
                    form["Data"].FirstOrDefault() ??
                    form["playerData"].FirstOrDefault() ??
                    form["PlayerData"].FirstOrDefault() ??
                    form["value"].FirstOrDefault() ??
                    form["Value"].FirstOrDefault() ??
                    string.Empty;
            }
            else
            {
                using var reader = new StreamReader(Request.Body);
                rawPlayerData = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(rawPlayerData))
            {

                CreatorFeatureDB.RoomPlayerDataRecord? currentRecord =
                    CreatorFeatureDB.GetRoomPlayerData(roomId, accountId.Value);
                return Ok(BuildRoomPlayerDataResponse(
                    roomId,
                    accountId.Value,
                    currentRecord?.DataJson ?? "{}",
                    currentRecord?.Version ?? 0,
                    currentRecord?.UpdatedAtUtc));
            }

            JsonElement root;
            try
            {
                using JsonDocument document = JsonDocument.Parse(rawPlayerData);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                Console.WriteLine(
                    $"[ROOM PLAYER DATA] invalid JSON room={roomId} " +
                    $"account={accountId.Value} contentType={Request.ContentType ?? "unknown"} " +
                    $"bytes={rawPlayerData.Length}");
                return BadRequest(MutationFailure(
                    "invalid_player_data",
                    "Room player data must be valid JSON."));
            }

            JsonElement data = UnwrapRoomPlayerData(root);
            string dataJson = data.GetRawText();

            try
            {
                CreatorFeatureDB.RoomPlayerDataRecord record =
                    CreatorFeatureDB.SaveRoomPlayerData(
                        roomId,
                        accountId.Value,
                        dataJson,
                        accountId.Value);

                MirrorPlayerDataToCloudVariables(
                    roomId,
                    accountId.Value,
                    data);

                Console.WriteLine(
                    $"[ROOM PLAYER DATA SET] room={roomId} account={accountId.Value} " +
                    $"version={record.Version}");

                return Ok(BuildRoomPlayerDataResponse(
                    roomId,
                    accountId.Value,
                    record.DataJson,
                    record.Version,
                    record.UpdatedAtUtc));
            }
            catch (ArgumentException exception)
            {
                return BadRequest(MutationFailure(
                    "invalid_player_data",
                    exception.Message));
            }
        }

        [HttpDelete("/roomserver/rooms/{roomId}/playerdata/me")]
        public IActionResult DeleteMyRoomPlayerData(long roomId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (!CanUseRoomPlayerData(room, accountId.Value))
                return NotFound();

            CreatorFeatureDB.DeleteRoomPlayerData(roomId, accountId.Value);
            return Ok(BuildRoomPlayerDataResponse(
                roomId,
                accountId.Value,
                "{}",
                0,
                null));
        }

        [HttpGet("rooms/{roomId}/similar")]
        public IActionResult GetSimilarRooms(long roomId, [FromQuery] int skip = 0, [FromQuery] int take = 100)
        {
            var sourceRoom = RoomDB.GetRoom(roomId);
            long? callerId = AuthStuff.GetPlayerId(Request);
            if (!CanViewRoomDirectly(sourceRoom, callerId))
                return NotFound();

            var sourceTags = sourceRoom.Tags?.Select(t => t.Tag).ToHashSet() ?? new HashSet<string>();

            var matches = RoomDB.Rooms.FindAll()
                .Where(r => r.RoomId != roomId)
                .Where(IsPubliclyDiscoverableRoom)
                .Where(r => r.Tags != null && r.Tags.Any(t => sourceTags.Contains(t.Tag)))
                .ToList();

            var page = matches.Skip(skip).Take(take).ToList();
            RoomDB.PrepareRoomsForClient(page);

            return Ok(new
            {
                Results = page,
                TotalResults = matches.Count
            });
        }

        [HttpDelete("rooms/{roomId}/interactionby/me/favorite")]
        public IActionResult UnfavoriteRoomInteraction(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            if (!PlayerDB.ToggleFavorite((long)id, roomId, out bool nowFavorited))
                return NotFound();

            return Ok(new { favorited = nowFavorited });
        }

        [HttpDelete("rooms/{roomId}/interactionby/me/cheer")]
        public IActionResult UncheerRoomInteraction(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            if (!PlayerDB.ToggleCheer((long)id, roomId, out bool nowCheered))
                return NotFound();

            return Ok(new { cheered = nowCheered });
        }

        [HttpPut("rooms/{roomId}/interactionby/me/favorite")]
        public IActionResult FavoriteRoomInteraction(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            if (!PlayerDB.ToggleFavorite((long)id, roomId, out bool nowFavorited))
                return NotFound();

            return Ok(new { favorited = nowFavorited });
        }

        [HttpPost("rooms/{roomId}/cheer")]
        public IActionResult CheerRoom(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            if (!PlayerDB.ToggleCheer((long)id, roomId, out bool nowCheered))
                return NotFound();

            return Ok(new { cheered = nowCheered });
        }

        [HttpPost("rooms/{roomId}/favorite")]
        public IActionResult FavoriteRoom(long roomId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            if (!PlayerDB.ToggleFavorite((long)id, roomId, out bool nowFavorited))
                return NotFound();

            return Ok(new { favorited = nowFavorited });
        }

        [HttpPut("rooms/{roomId}/cloning")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> UpdateRoomCloning(long roomId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            var room = RoomDB.Rooms.FindById(roomId);
            if (room == null)
                return NotFound(new { success = false, error = "room_not_found" });

            var requester = PlayerDB.Players.FindById(accountId.Value);
            bool isDeveloper = requester?.PlayerRoles?.Contains(
                PlayerDBClasses.PlayerRoles.Developer) == true;

            if (room.CreatorAccountId != accountId.Value && !isDeveloper)
            {
                return StatusCode(403, new
                {
                    success = false,
                    error = "not_room_owner"
                });
            }

            bool? cloningAllowed = await ReadBooleanRequestAsync(
                "CloningAllowed",
                "cloningAllowed",
                "Enabled",
                "enabled",
                "Value",
                "value");

            room.CloningAllowed = cloningAllowed ?? !room.CloningAllowed;
            RoomDB.Rooms.Update(room);

            Console.WriteLine(
                $"[ROOM CLONING] room={roomId} account={accountId.Value} " +
                $"allowed={room.CloningAllowed}");

            return Ok(new
            {
                RoomId = room.RoomId,
                CloningAllowed = room.CloningAllowed
            });
        }

        private async Task<bool?> ReadBooleanRequestAsync(params string[] keys)
        {
            foreach (string key in keys)
            {
                if (TryParseBoolean(Request.Query[key].FirstOrDefault(), out bool queryValue))
                    return queryValue;
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (string key in keys)
                {
                    if (TryParseBoolean(form[key].FirstOrDefault(), out bool formValue))
                        return formValue;
                }

                return null;
            }

            if ((Request.ContentLength ?? 0) <= 0)
                return null;

            using var reader = new StreamReader(Request.Body);
            string body = await reader.ReadToEndAsync();
            if (TryParseBoolean(body.Trim().Trim('"'), out bool rawValue))
                return rawValue;

            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                return FindBoolean(document.RootElement, keys);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool? FindBoolean(
            JsonElement element,
            IReadOnlyCollection<string> keys)
        {
            if (element.ValueKind == JsonValueKind.True)
                return true;
            if (element.ValueKind == JsonValueKind.False)
                return false;
            if (element.ValueKind == JsonValueKind.Number &&
                element.TryGetInt32(out int number))
            {
                return number != 0;
            }
            if (element.ValueKind == JsonValueKind.String &&
                TryParseBoolean(element.GetString(), out bool stringValue))
            {
                return stringValue;
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (keys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        bool? direct = FindBoolean(property.Value, keys);
                        if (direct.HasValue)
                            return direct;
                    }
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    bool? nested = FindBoolean(property.Value, keys);
                    if (nested.HasValue)
                        return nested;
                }
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    bool? nested = FindBoolean(item, keys);
                    if (nested.HasValue)
                        return nested;
                }
            }

            return null;
        }

        private static bool TryParseBoolean(string? value, out bool result)
        {
            if (bool.TryParse(value, out result))
                return true;

            if (int.TryParse(value, out int number))
            {
                result = number != 0;
                return true;
            }

            string normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalized is "on" or "yes" or "enabled" or "allow" or "allowed")
            {
                result = true;
                return true;
            }

            if (normalized is "off" or "no" or "disabled" or "deny" or "denied")
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        [HttpPost("rooms/{roomId}/clone")]
        public IActionResult CloneRoom(long roomId, [FromForm] string? name)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
            {
                return Unauthorized(new
                {
                    Value = (object?)null,
                    Success = false,
                    ErrorId = "unauthorized",
                    Error = "Authentication is required to clone a room.",
                    LocalizationContext = (object?)null
                });
            }

            var source = RoomDB.GetRoom(roomId);
            if (source == null)
            {
                return NotFound(new
                {
                    Value = (object?)null,
                    Success = false,
                    ErrorId = "room_not_found",
                    Error = "Source room was not found.",
                    LocalizationContext = (object?)null
                });
            }

            if (!source.CloningAllowed)
            {
                return BadRequest(new
                {
                    Value = (object?)null,
                    Success = false,
                    ErrorId = "room_cloning_disabled",
                    Error = "This room does not allow cloning.",
                    LocalizationContext = (object?)null
                });
            }

            var clone = RoomDB.CloneRoom(source, player.PlayerId, name);
            if (clone == null)
            {
                return StatusCode(500, new
                {
                    Value = (object?)null,
                    Success = false,
                    ErrorId = "room_clone_failed",
                    Error = "The room could not be cloned.",
                    LocalizationContext = (object?)null
                });
            }

            PlayerDB.RecordRoomVisit(player.PlayerId, clone.RoomId);

            var preparedClone = RoomDB.PrepareRoomForClient(clone);
            if (preparedClone == null)
            {
                return StatusCode(500, new
                {
                    Value = (object?)null,
                    Success = false,
                    ErrorId = "room_clone_prepare_failed",
                    Error = "The cloned room could not be prepared for the client.",
                    LocalizationContext = (object?)null
                });
            }

            Console.WriteLine(
                $"[ROOM CLONE RESPONSE] source={roomId} clone={preparedClone.RoomId} " +
                $"format=api-result success=true subrooms={preparedClone.SubRooms?.Count ?? 0}");

            return Ok(new
            {
                Value = preparedClone,
                Success = true,
                ErrorId = (string?)null,
                Error = (string?)null,
                LocalizationContext = (object?)null
            });
        }

        [HttpGet("rooms/bulk")]
        public async Task<IActionResult> GetRoomsByNames([FromQuery] List<string> name)
        {
            long? callerId = AuthStuff.GetPlayerId(Request);
            var rooms = RoomDB.GetRoomsByNames(name)
                .Where(room => CanViewRoomDirectly(room, callerId))
                .ToList();

            return Ok(new
            {
                Results = rooms,
                TotalResults = rooms.Count
            });
        }

        [HttpGet("photon_access_token")]
        public async Task<IActionResult> GetPhotonAccessToken()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            var heartbeat = PlayerDB.GetPlayerHeartbeat((long)id);
            var activeRoom = heartbeat?.roomInstance == null
                ? null
                : RoomDB.GetRoom(heartbeat.roomInstance.roomId);
            var activeSubRoom = activeRoom?.SubRooms?.FirstOrDefault(subRoom =>
                subRoom.SubRoomId == heartbeat?.roomInstance?.subRoomId);
            var permissions = activeSubRoom?.Permissions?.Count > 0
                ? activeSubRoom.Permissions.Select(SanitizePermission).ToList()
                : CreateDefaultSubRoomPermissions();

            if (heartbeat?.roomInstance != null)
            {
                var player = PlayerDB.Players.FindById((long)id);
                DiscordLogger.Log($"🚪 **Joined Room** — `{player?.Player?.Username ?? "unknown"}` (ID: `{id}`) → room `{heartbeat.roomInstance.roomId}` (`{heartbeat.roomInstance.Name}`, instance `{heartbeat.roomInstance.roomInstanceId}`)");
            }

            var playerRecord = PlayerDB.Players.FindById((long)id);
            string photonTicket = PhotonTicketService.Issue(
                playerId: (long)id,
                roomInstanceId: heartbeat?.roomInstance?.roomInstanceId,
                roomId: heartbeat?.roomInstance?.roomId,
                displayName: playerRecord?.Player?.DisplayName
                    ?? playerRecord?.Player?.Username);

            Response.Headers.CacheControl = "no-store";

            var response = new
            {
                Permissions = permissions.ToArray(),
                PhotonAccessToken = photonTicket,
                RoomInstanceId = heartbeat?.roomInstance?.roomInstanceId
            };

            return Ok(response);
        }

        [HttpGet("rooms/hot")]
        public IActionResult HotRooms(
            [FromQuery] string? tag = null,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 100);

            string selectedTag = string.IsNullOrWhiteSpace(tag)
                ? "hot"
                : tag.Trim();

            var (results, total) = RoomDB.GetHotRooms(selectedTag, skip, take);

            if (skip > 0 && total > 0 && (results == null || results.Count == 0))
            {
                Console.WriteLine(
                    $"[ROOM DISCOVERY] stale cursor recovered tag={selectedTag} skip={skip} total={total}");
                (results, total) = RoomDB.GetHotRooms(selectedTag, 0, take);
            }

            Response.Headers.CacheControl = "no-store";
            return Ok(new
            {
                Results = results ?? new List<RoomDBClasses.Room>(),
                TotalResults = total
            });
        }

        [HttpGet("rooms/curated_playlists")]
        public IActionResult GetCuratedPlaylistIds()
        {

            return Ok(new
            {
                Results = Array.Empty<long>(),
                TotalResults = 0
            });
        }

        [HttpGet("rooms/carousel/rising")]
        public IActionResult GetRisingRooms(
            [FromQuery] int skip = 0,
            [FromQuery] int take = 100)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 100);

            var allRooms = RoomDB.Rooms.FindAll()
                .Where(IsPubliclyDiscoverableRoom)
                .OrderByDescending(room => room.CreatedAt)
                .ThenByDescending(room => room.RoomId)
                .ToList();

            RoomDB.PrepareRoomsForClient(allRooms);

            List<RoomDBClasses.Room> page = allRooms.Skip(skip).Take(take).ToList();
            if (skip > 0 && allRooms.Count > 0 && page.Count == 0)
                page = allRooms.Take(take).ToList();

            Response.Headers.CacheControl = "no-store";
            return Ok(new
            {
                Results = page,
                TotalResults = allRooms.Count
            });
        }

        [HttpGet("featuredrooms/current")]
        public IActionResult GetCurrentFeaturedRooms()
        {
            var allRooms = RoomDB.Rooms.FindAll()
                .Where(IsPubliclyDiscoverableRoom)
                .OrderByDescending(room => room.IsDeveloperOwned)
                .ThenByDescending(room => room.CreatedAt)
                .ThenBy(room => room.RoomId)
                .Take(12)
                .ToList();

            var featuredRooms = allRooms.Select(room => new
            {
                RoomId = room.RoomId,
                Name = room.Name,
                RoomName = room.Name,
                ImageName = (string?)null
            }).ToList();

            return Ok(new
            {
                Id = 1L,
                FeaturedRoomGroupId = 1L,
                Name = "Mocha Featured",
                Rooms = featuredRooms,
                FeaturedRooms = featuredRooms
            });
        }

        [HttpPost("rooms/{roomId}/bans")]
        public async Task<IActionResult> BanFromRoom(long roomId)
        {
            var callerId = AuthStuff.GetPlayerId(Request);
            if (callerId == null)
            {
                return Unauthorized(new
                {
                    success = false,
                    error = "Authentication is required."
                });
            }

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = "Room was not found."
                });
            }

            bool canBan =
                room.CreatorAccountId == callerId.Value ||
                room.Roles?.Any(role =>
                    role.AccountId == callerId.Value &&
                    (role.Role == RoomDBClasses.Role.Creator ||
                     role.Role == RoomDBClasses.Role.CoOwner)) == true;

            if (!canBan)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    success = false,
                    error = "You do not have permission to ban players from this room."
                });
            }

            long targetAccountId = 0;
            int banMask = 0;
            string? reason = null;
            string rawBody = string.Empty;

            string[] accountIdKeys =
            {
        "id",
        "Id",
        "accountId",
        "AccountId",
        "targetAccountId",
        "TargetAccountId",
        "playerId",
        "PlayerId",
        "targetPlayerId",
        "TargetPlayerId"
    };

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();

                rawBody = string.Join(
                    "&",
                    form.SelectMany(entry =>
                        entry.Value.Select(value =>
                            $"{entry.Key}={value}")));

                foreach (string key in accountIdKeys)
                {
                    string? value = form[key].FirstOrDefault();

                    if (long.TryParse(value, out long parsedId) && parsedId > 0)
                    {
                        targetAccountId = parsedId;
                        break;
                    }
                }

                int.TryParse(
                    form["banMask"].FirstOrDefault(),
                    out banMask);

                reason =
                    form["reason"].FirstOrDefault() ??
                    form["Reason"].FirstOrDefault();
            }
            else
            {

                foreach (string key in accountIdKeys)
                {
                    string? value = Request.Query[key].FirstOrDefault();

                    if (long.TryParse(value, out long parsedId) && parsedId > 0)
                    {
                        targetAccountId = parsedId;
                        break;
                    }
                }

                int.TryParse(
                    Request.Query["banMask"].FirstOrDefault(),
                    out banMask);

                reason =
                    Request.Query["reason"].FirstOrDefault() ??
                    Request.Query["Reason"].FirstOrDefault();

                using var reader = new StreamReader(Request.Body);
                rawBody = await reader.ReadToEndAsync();

                if (targetAccountId == 0 &&
                    !string.IsNullOrWhiteSpace(rawBody))
                {
                    string cleanedBody = rawBody.Trim().Trim('"');

                    if (long.TryParse(
                            cleanedBody,
                            out long rawAccountId) &&
                        rawAccountId > 0)
                    {
                        targetAccountId = rawAccountId;
                    }
                    else
                    {
                        try
                        {
                            using var document = JsonDocument.Parse(rawBody);
                            JsonElement root = document.RootElement;

                            if (root.ValueKind == JsonValueKind.Object)
                            {
                                foreach (JsonProperty property in root.EnumerateObject())
                                {
                                    bool isAccountIdProperty =
                                        accountIdKeys.Any(key =>
                                            property.Name.Equals(
                                                key,
                                                StringComparison.OrdinalIgnoreCase));

                                    if (isAccountIdProperty)
                                    {
                                        if (property.Value.ValueKind ==
                                                JsonValueKind.Number &&
                                            property.Value.TryGetInt64(
                                                out long jsonNumberId))
                                        {
                                            targetAccountId = jsonNumberId;
                                            break;
                                        }

                                        if (property.Value.ValueKind ==
                                                JsonValueKind.String &&
                                            long.TryParse(
                                                property.Value.GetString(),
                                                out long jsonStringId))
                                        {
                                            targetAccountId = jsonStringId;
                                            break;
                                        }
                                    }
                                }

                                foreach (JsonProperty property in root.EnumerateObject())
                                {
                                    if (property.Name.Equals(
                                            "banMask",
                                            StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (property.Value.ValueKind ==
                                                JsonValueKind.Number)
                                        {
                                            property.Value.TryGetInt32(out banMask);
                                        }
                                        else if (property.Value.ValueKind ==
                                                     JsonValueKind.String)
                                        {
                                            int.TryParse(
                                                property.Value.GetString(),
                                                out banMask);
                                        }
                                    }

                                    if (property.Name.Equals(
                                            "reason",
                                            StringComparison.OrdinalIgnoreCase) &&
                                        property.Value.ValueKind ==
                                            JsonValueKind.String)
                                    {
                                        reason = property.Value.GetString();
                                    }
                                }
                            }
                        }
                        catch (JsonException ex)
                        {
                            Console.WriteLine(
                                $"[ROOM BAN] JSON parse failed: {ex.Message}");
                        }
                    }
                }
            }

            reason = string.IsNullOrWhiteSpace(reason)
                ? null
                : reason.Trim();

            Console.WriteLine(
                $"[ROOM BAN] room={roomId} " +
                $"caller={callerId.Value} " +
                $"target={targetAccountId} " +
                $"banMask={banMask} " +
                $"contentType={Request.ContentType ?? "null"} " +
                $"rawBody={rawBody}");

            if (targetAccountId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "A valid player ID was not provided.",
                    contentType = Request.ContentType,
                    receivedBody = rawBody
                });
            }

            if (targetAccountId == callerId.Value)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "You cannot ban yourself."
                });
            }

            var targetPlayer = PlayerDB.Players.FindById(targetAccountId);
            if (targetPlayer == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = $"Player {targetAccountId} was not found."
                });
            }

            var existingBan = RoomDB.GetActiveBans(roomId)
                .FirstOrDefault(existing =>
                    existing.AccountId == targetAccountId);

            if (existingBan != null)
            {
                return Ok(new
                {
                    success = true,
                    alreadyBanned = true,
                    roomId,
                    accountId = targetAccountId,
                    banMask,
                    ban = existingBan
                });
            }

            var ban = RoomDB.BanPlayerFromRoom(
                roomId,
                targetAccountId,
                callerId.Value,
                reason);

            DiscordLogger.Log(
                $"🔨 **Room Ban** — `{targetAccountId}` banned from room " +
                $"`{roomId}` (`{room.Name}`) by `{callerId.Value}`" +
                $" — mask `{banMask}`" +
                (reason == null ? "" : $" — {reason}"));

            return Ok(new
            {
                success = true,
                alreadyBanned = false,
                roomId,
                accountId = targetAccountId,
                banMask,
                ban
            });
        }

        public class BanRequest
        {
            public long AccountId { get; set; }
            public string? Reason { get; set; }
        }

        [HttpGet("rooms/{roomId}/bans")]
        public IActionResult GetRoomBans(long roomId)
        {
            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound();

            var bans = RoomDB.GetActiveBans(roomId);
            return Ok(bans);
        }

        [HttpDelete("rooms/{roomId}/bans/{targetAccountId}")]
        public IActionResult UnbanFromRoom(long roomId, long targetAccountId)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound();

            bool canBan =
                room.CreatorAccountId == accountId.Value ||
                room.Roles?.Any(r =>
                    r.AccountId == accountId.Value &&
                    (r.Role == RoomDBClasses.Role.Creator || r.Role == RoomDBClasses.Role.CoOwner)) == true;
            if (!canBan)
                return StatusCode(403);

            if (!RoomDB.UnbanPlayerFromRoom(roomId, targetAccountId))
                return NotFound(new { success = false, error = "No active ban found for that player." });

            DiscordLogger.Log($"✅ **Room Unban** — `{targetAccountId}` unbanned from room `{roomId}` by `{accountId}`");

            return Ok(new { success = true });
        }

        [HttpGet("rooms/recommendations")]
        public IActionResult GetRoomRecommendations()
        {
            return Ok(new
            {
                Results = Array.Empty<object>(),
                TotalResults = 0
            });
        }

        [HttpGet("rooms/{roomId}/interactionby/me")]
        public IActionResult GetInteractionByMe(long roomId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized();

            bool cheered = player.Player?.CheeredRooms?.Contains(roomId) ?? false;
            bool favorited = player.Player?.FavoritedRooms?.Contains(roomId) ?? false;
            var visit = player.Player?.PlayerExtra?.RoomVisits?.FirstOrDefault(v => v.RoomId == roomId);

            return Ok(new
            {
                Cheered = cheered,
                Favorited = favorited,
                LastVisitedAt = visit?.VisitedAt
            });
        }

        private static bool CanEditRoom(RoomDBClasses.Room room, long accountId)
        {
            return room.CreatorAccountId == accountId ||
                room.Roles?.Any(role =>
                    role.AccountId == accountId &&
                    role.Role is RoomDBClasses.Role.Creator or RoomDBClasses.Role.CoOwner) == true;
        }

        private static bool IsPubliclyDiscoverableRoom(
            RoomDBClasses.Room room)
        {
            return !room.IsDorm &&
                   room.State != RoomDBClasses.RoomState.MarkedForDelete &&
                   room.State != RoomDBClasses.RoomState.Moderation_Closed &&
                   room.Accessibility ==
                       RoomDBClasses.RoomAccessibility.Public;
        }

        private static bool CanViewRoomDirectly(
            RoomDBClasses.Room? room,
            long? accountId)
        {
            if (room == null ||
                room.State == RoomDBClasses.RoomState.MarkedForDelete ||
                room.State == RoomDBClasses.RoomState.Moderation_Closed)
            {
                return false;
            }

            if (!room.IsDorm &&
                room.Accessibility !=
                    RoomDBClasses.RoomAccessibility.Private)
            {

                return true;
            }

            return accountId.HasValue &&
                   RoomDB.CanPlayerAccessRoom(room, accountId.Value);
        }

        private static bool CanViewSubRoomDirectly(
            RoomDBClasses.Room room,
            RoomDBClasses.SubRooms subRoom,
            long? accountId)
        {
            if (!CanViewRoomDirectly(room, accountId))
                return false;

            if (subRoom.Accessibility !=
                RoomDBClasses.RoomAccessibility.Private)
            {
                return true;
            }

            return accountId.HasValue &&
                   RoomDB.CanPlayerAccessSubRoom(
                       room,
                       subRoom,
                       accountId.Value);
        }

        private async Task<RoomDBClasses.RoomAccessibility?> ReadRoomAccessibilityAsync()
        {
            string? rawValue = Request.Query["accessibility"].FirstOrDefault();

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
                rawValue ??= form["accessibility"].FirstOrDefault()
                    ?? form["Accessibility"].FirstOrDefault();
            }
            else
            {
                using var reader = new StreamReader(
                    Request.Body,
                    System.Text.Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1_024,
                    leaveOpen: true);
                string rawBody = await reader.ReadToEndAsync(HttpContext.RequestAborted);
                if (!string.IsNullOrWhiteSpace(rawBody))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(rawBody);
                        JsonElement root = document.RootElement;
                        if (root.ValueKind == JsonValueKind.Object &&
                            TryGetProperty(root, "accessibility", out var property))
                        {
                            root = property;
                        }

                        if (root.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                            rawValue ??= root.ToString();
                    }
                    catch (JsonException)
                    {
                        rawValue ??= rawBody.Trim().Trim('"');
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(rawValue))
                return null;

            rawValue = rawValue.Trim();
            if (int.TryParse(rawValue, out int numericValue) &&
                Enum.IsDefined(typeof(RoomDBClasses.RoomAccessibility), numericValue))
            {
                return (RoomDBClasses.RoomAccessibility)numericValue;
            }

            return Enum.TryParse<RoomDBClasses.RoomAccessibility>(rawValue, true, out var namedValue) &&
                Enum.IsDefined(typeof(RoomDBClasses.RoomAccessibility), namedValue)
                    ? namedValue
                    : null;
        }

        [HttpPut("rooms/{roomId}/name")]
        [HttpPost("rooms/{roomId}/name")]
        [HttpPatch("rooms/{roomId}/name")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> UpdateRoomName(long roomId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound(MutationFailure("room_not_found", "Room was not found."));
            if (!CanEditRoom(room, accountId.Value))
                return StatusCode(StatusCodes.Status403Forbidden);

            RoomSettingsPayload payload = await ReadRoomSettingsPayloadAsync();
            string? requestedName = ReadFlexibleString(
                payload.Values,
                "name", "Name", "roomName", "RoomName", "value", "Value");
            if (string.IsNullOrWhiteSpace(requestedName) &&
                !string.IsNullOrWhiteSpace(payload.RawBody))
            {
                requestedName = payload.RawBody.Trim().Trim('"');
            }

            string clean = (requestedName ?? string.Empty).Trim().TrimStart('^');
            if (clean.Length == 0 || clean.Length > 64 ||
                clean.Any(character => character == '\0' || character == '/' || character == '\\'))
            {
                return BadRequest(MutationFailure(
                    "invalid_room_name",
                    "A room name between 1 and 64 characters is required."));
            }

            var duplicate = RoomDB.GetRoomByName(clean);
            if (duplicate != null && duplicate.RoomId != roomId)
            {
                return Conflict(MutationFailure(
                    "room_name_taken",
                    "Another room already uses that name."));
            }

            room.Name = clean;
            if (RoomDB.IsCanonicalBaseRoom(room))
            {
                room.IsBaseRoom = true;
                room.IsRRO = true;
                room.IsDorm = false;
                room.Accessibility = RoomDBClasses.RoomAccessibility.Public;
                EnsureRoomTag(room, "base", RoomDBClasses.TagType.Auto);
                EnsureRoomTag(room, "rro", RoomDBClasses.TagType.AGOnly);
            }

            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            if (!RoomDB.Rooms.Update(room))
                return StatusCode(StatusCodes.Status500InternalServerError);

            Console.WriteLine($"[ROOM NAME] room={roomId} name={clean} by={accountId.Value}");
            return Ok(MutationSuccess(RoomDB.PrepareRoomForClient(room)!));
        }

        [HttpPut("rooms/{roomId}/beta")]
        [HttpPost("rooms/{roomId}/beta")]
        [HttpPatch("rooms/{roomId}/beta")]
        [HttpPut("rooms/{roomId}/beta-content")]
        [HttpPost("rooms/{roomId}/beta-content")]
        [HttpPatch("rooms/{roomId}/beta-content")]
        [HttpPut("rooms/{roomId}/creative-tools-beta")]
        [HttpPost("rooms/{roomId}/creative-tools-beta")]
        [HttpPatch("rooms/{roomId}/creative-tools-beta")]
        [HttpPut("rooms/{roomId}/enablebeta")]
        [HttpPost("rooms/{roomId}/enablebeta")]
        [HttpPatch("rooms/{roomId}/enablebeta")]
        [HttpPut("rooms/{roomId}/supportsbetacontent")]
        [HttpPost("rooms/{roomId}/supportsbetacontent")]
        [HttpPatch("rooms/{roomId}/supportsbetacontent")]
        [HttpPut("rooms/{roomId}/supports-beta-content")]
        [HttpPost("rooms/{roomId}/supports-beta-content")]
        [HttpPatch("rooms/{roomId}/supports-beta-content")]
        [HttpPut("rooms/{roomId}/betacontent")]
        [HttpPost("rooms/{roomId}/betacontent")]
        [HttpPatch("rooms/{roomId}/betacontent")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> UpdateRoomBeta(long roomId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound(MutationFailure("room_not_found", "Room was not found."));
            if (!CanEditRoom(room, accountId.Value))
                return StatusCode(StatusCodes.Status403Forbidden);

            RoomSettingsPayload payload = await ReadRoomSettingsPayloadAsync();
            bool? enabled = ReadFlexibleBool(
                payload.Values,
                "enabled", "Enabled", "isEnabled", "IsEnabled",
                "beta", "Beta", "isBeta", "IsBeta",
                "supportsBetaContent", "SupportsBetaContent",
                "creativeToolsBetaEnabled", "CreativeToolsBetaEnabled",
                "betaContentEnabled", "BetaContentEnabled");
            if (!enabled.HasValue && !string.IsNullOrWhiteSpace(payload.RawBody))
            {
                string raw = payload.RawBody.Trim().Trim('"');
                if (bool.TryParse(raw, out bool parsedBool))
                    enabled = parsedBool;
                else if (int.TryParse(raw, out int parsedInt))
                    enabled = parsedInt != 0;
            }

            if (!enabled.HasValue)
            {
                return BadRequest(MutationFailure(
                    "beta_value_required",
                    "A boolean beta/enabled value is required."));
            }

            ApplyRoomBeta(room, enabled.Value);
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            if (!RoomDB.Rooms.Update(room))
                return StatusCode(StatusCodes.Status500InternalServerError);

            Console.WriteLine(
                $"[ROOM BETA] room={roomId} enabled={enabled.Value} by={accountId.Value}");
            return Ok(MutationSuccess(RoomDB.PrepareRoomForClient(room)!));
        }

        [HttpPut("rooms/{roomId}/tags")]
        [HttpPost("rooms/{roomId}/tags")]
        [HttpPatch("rooms/{roomId}/tags")]
        [HttpPut("rooms/{roomId}/tag")]
        [HttpPost("rooms/{roomId}/tag")]
        [HttpPatch("rooms/{roomId}/tag")]
        [HttpPut("rooms/{roomId}/tags/modify")]
        [HttpPost("rooms/{roomId}/tags/modify")]
        [HttpPatch("rooms/{roomId}/tags/modify")]
        [HttpPut("rooms/{roomId}/settags")]
        [HttpPost("rooms/{roomId}/settags")]
        [HttpPatch("rooms/{roomId}/settags")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> UpdateRoomTags(long roomId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound(MutationFailure("room_not_found", "Room was not found."));
            if (!CanEditRoom(room, accountId.Value))
                return StatusCode(StatusCodes.Status403Forbidden);

            RoomSettingsPayload payload = await ReadRoomSettingsPayloadAsync();
            List<string> requestedTags = ParseRoomTags(payload);
            bool replace = !string.Equals(Request.Method, "POST", StringComparison.OrdinalIgnoreCase) ||
                ReadFlexibleBool(payload.Values, "replace", "Replace") == true;

            room.Tags ??= new List<RoomDBClasses.Tags>();
            List<RoomDBClasses.Tags> protectedTags = room.Tags
                .Where(tag => tag.Type != RoomDBClasses.TagType.General ||
                    string.Equals(tag.Tag, "base", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tag.Tag, "rro", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tag.Tag, "beta", StringComparison.OrdinalIgnoreCase))
                .Select(tag => new RoomDBClasses.Tags
                {
                    Tag = NormalizeRoomTag(tag.Tag),
                    Type = tag.Type
                })
                .Where(tag => !string.IsNullOrWhiteSpace(tag.Tag))
                .ToList();

            IEnumerable<RoomDBClasses.Tags> userTags = requestedTags.Select(tag =>
                new RoomDBClasses.Tags
                {
                    Tag = tag,
                    Type = RoomDBClasses.TagType.General
                });

            if (!replace)
            {
                userTags = room.Tags
                    .Where(tag => tag.Type == RoomDBClasses.TagType.General)
                    .Concat(userTags);
            }

            room.Tags = protectedTags
                .Concat(userTags)
                .GroupBy(tag => tag.Tag, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(30)
                .ToList();

            if (room.CreativeToolsBetaEnabled)
                ApplyRoomBeta(room, true);
            if (RoomDB.IsCanonicalBaseRoom(room))
            {
                room.IsBaseRoom = true;
                EnsureRoomTag(room, "base", RoomDBClasses.TagType.Auto);
                EnsureRoomTag(room, "rro", RoomDBClasses.TagType.AGOnly);
            }

            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            if (!RoomDB.Rooms.Update(room))
                return StatusCode(StatusCodes.Status500InternalServerError);

            Console.WriteLine(
                $"[ROOM TAGS] room={roomId} count={room.Tags.Count} replace={replace} " +
                $"by={accountId.Value}");
            return Ok(MutationSuccess(RoomDB.PrepareRoomForClient(room)!));
        }

        [HttpDelete("rooms/{roomId}/tags/{tag}")]
        [HttpDelete("rooms/{roomId}/tag/{tag}")]
        public IActionResult DeleteRoomTag(long roomId, string tag)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound();
            if (!CanEditRoom(room, accountId.Value))
                return StatusCode(StatusCodes.Status403Forbidden);

            string normalized = NormalizeRoomTag(tag);
            if ((normalized is "base" or "rro") && RoomDB.IsCanonicalBaseRoom(room))
            {
                return BadRequest(MutationFailure(
                    "protected_tag",
                    "Canonical base-room tags cannot be removed."));
            }
            if (normalized == "beta")
                room.CreativeToolsBetaEnabled = false;

            room.Tags ??= new List<RoomDBClasses.Tags>();
            room.Tags.RemoveAll(value => string.Equals(
                value.Tag,
                normalized,
                StringComparison.OrdinalIgnoreCase));
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            RoomDB.Rooms.Update(room);
            return Ok(MutationSuccess(RoomDB.PrepareRoomForClient(room)!));
        }

        [HttpPut("rooms/{roomId}/modify")]
        [HttpPost("rooms/{roomId}/modify")]
        [HttpPatch("rooms/{roomId}/modify")]
        [HttpPut("rooms/{roomId}")]
        [HttpPost("rooms/{roomId}")]
        [HttpPatch("rooms/{roomId}")]
        [RequestSizeLimit(128 * 1024)]
        public async Task<IActionResult> ModifyRoom(long roomId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound(MutationFailure("room_not_found", "Room was not found."));
            if (!CanEditRoom(room, accountId.Value))
                return StatusCode(StatusCodes.Status403Forbidden);

            RoomSettingsPayload payload = await ReadRoomSettingsPayloadAsync();
            bool changed = false;

            string? name = ReadFlexibleString(payload.Values, "name", "Name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                string clean = name.Trim().TrimStart('^');
                if (clean.Length > 64 || clean.Any(character => character == '\0'))
                    return BadRequest(MutationFailure("invalid_room_name", "The room name is invalid."));
                room.Name = clean;
                changed = true;
            }

            string? description = ReadFlexibleString(
                payload.Values,
                "description", "Description");
            if (description != null)
            {
                room.Description = description.Length > 1_000
                    ? description[..1_000]
                    : description;
                changed = true;
            }

            string? imageName = ReadFlexibleString(
                payload.Values,
                "imageName", "ImageName", "image", "Image");
            if (!string.IsNullOrWhiteSpace(imageName))
            {
                room.ImageName = imageName.Trim();
                changed = true;
            }

            string? accessibilityRaw = ReadFlexibleString(
                payload.Values,
                "accessibility", "Accessibility");
            if (!string.IsNullOrWhiteSpace(accessibilityRaw) &&
                TryParseRoomAccessibility(accessibilityRaw, out var accessibility))
            {
                room.Accessibility = accessibility;
                changed = true;
            }

            bool? cloningAllowed = ReadFlexibleBool(
                payload.Values,
                "cloningAllowed", "CloningAllowed", "allowCloning", "AllowCloning");
            if (cloningAllowed.HasValue)
            {
                room.CloningAllowed = cloningAllowed.Value;
                changed = true;
            }

            bool? beta = ReadFlexibleBool(
                payload.Values,
                "beta", "Beta", "isBeta", "IsBeta",
                "supportsBetaContent", "SupportsBetaContent",
                "creativeToolsBetaEnabled", "CreativeToolsBetaEnabled",
                "betaContentEnabled", "BetaContentEnabled");
            if (beta.HasValue)
            {
                ApplyRoomBeta(room, beta.Value);
                changed = true;
            }

            if (payload.Values.ContainsKey("tags") ||
                payload.Values.ContainsKey("Tags") ||
                payload.Values.ContainsKey("tagNames") ||
                payload.Values.ContainsKey("TagNames"))
            {
                List<string> tags = ParseRoomTags(payload);
                room.Tags ??= new List<RoomDBClasses.Tags>();
                room.Tags.RemoveAll(tag => tag.Type == RoomDBClasses.TagType.General);
                room.Tags.AddRange(tags.Select(tag => new RoomDBClasses.Tags
                {
                    Tag = tag,
                    Type = RoomDBClasses.TagType.General
                }));
                changed = true;
            }

            int? maxPlayers = ReadFlexibleInt(payload.Values, "maxPlayers", "MaxPlayers");
            if (maxPlayers.HasValue)
            {
                room.MaxPlayers = Math.Clamp(maxPlayers.Value, 1, 100);
                changed = true;
            }

            if (RoomDB.IsCanonicalBaseRoom(room))
            {
                room.IsBaseRoom = true;
                room.IsRRO = true;
                room.IsDorm = false;
                room.Accessibility = RoomDBClasses.RoomAccessibility.Public;
                EnsureRoomTag(room, "base", RoomDBClasses.TagType.Auto);
                EnsureRoomTag(room, "rro", RoomDBClasses.TagType.AGOnly);
            }

            if (!changed)
                return BadRequest(MutationFailure("no_changes", "No supported room settings were provided."));

            room.Tags = room.Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag.Tag))
                .GroupBy(tag => tag.Tag, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(30)
                .ToList();
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            if (!RoomDB.Rooms.Update(room))
                return StatusCode(StatusCodes.Status500InternalServerError);

            Console.WriteLine($"[ROOM MODIFY] room={roomId} by={accountId.Value}");
            return Ok(MutationSuccess(RoomDB.PrepareRoomForClient(room)!));
        }

        [HttpPut("rooms/{roomId}/permissions")]
        [HttpPost("rooms/{roomId}/permissions")]
        [HttpPatch("rooms/{roomId}/permissions")]
        [HttpPut("rooms/{roomId}/permissions/modify")]
        [HttpPost("rooms/{roomId}/permissions/modify")]
        [HttpPatch("rooms/{roomId}/permissions/modify")]
        [RequestSizeLimit(128 * 1024)]
        public async Task<IActionResult> UpdateRoomPermissions(long roomId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound(MutationFailure("room_not_found", "Room was not found."));
            if (!CanEditRoom(room, accountId.Value))
                return StatusCode(StatusCodes.Status403Forbidden);

            bool requestHadPayload = Request.QueryString.HasValue ||
                Request.HasFormContentType ||
                (Request.ContentLength ?? 0) > 0 ||
                Request.Headers.TransferEncoding.Count > 0;
            List<RoomDBClasses.SubRoomPermission> permissions =
                await ReadPermissionPayloadFromRequestAsync();
            if (permissions.Count == 0)
            {
                if (requestHadPayload)
                {
                    return BadRequest(MutationFailure(
                        "invalid_permissions",
                        "The permission payload could not be parsed."));
                }

                permissions = room.SubRooms?
                    .FirstOrDefault(subRoom => subRoom.Permissions?.Count > 0)?
                    .Permissions?
                    .Select(SanitizePermission)
                    .ToList() ?? CreateDefaultSubRoomPermissions();
            }

            List<RoomDBClasses.SubRoomPermission> sanitized = permissions
                .Where(permission => !string.IsNullOrWhiteSpace(permission.Permission))
                .Select(SanitizePermission)
                .GroupBy(permission => new
                {
                    Permission = permission.Permission.ToUpperInvariant(),
                    permission.Role,
                    permission.Type
                })
                .Select(group => group.Last())
                .Take(250)
                .ToList();

            room.SubRooms ??= new List<RoomDBClasses.SubRooms>();
            foreach (var subRoom in room.SubRooms)
            {
                subRoom.Permissions = sanitized.Select(permission =>
                    new RoomDBClasses.SubRoomPermission
                    {
                        Override = permission.Override,
                        Permission = permission.Permission,
                        Role = permission.Role,
                        Type = permission.Type,
                        Value = permission.Value
                    }).ToList();
            }

            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            if (!RoomDB.Rooms.Update(room))
                return StatusCode(StatusCodes.Status500InternalServerError);

            Console.WriteLine(
                $"[ROOM PERMISSIONS] room={roomId} subrooms={room.SubRooms.Count} " +
                $"permissions={sanitized.Count} by={accountId.Value}");
            return Ok(MutationSuccess(RoomDB.PrepareRoomForClient(room)!));
        }

        private sealed class RoomSettingsPayload
        {
            public Dictionary<string, string> Values { get; } =
                new(StringComparer.OrdinalIgnoreCase);
            public string RawBody { get; set; } = string.Empty;
        }

        private sealed class RoomRoleInviteTarget
        {
            public long AccountId { get; set; }
            public string? Username { get; set; }
        }

        private async Task<RoomRoleInviteTarget> ReadRoomRoleInviteTargetAsync()
        {
            RoomSettingsPayload payload = await ReadRoomSettingsPayloadAsync();
            long accountId = 0;
            foreach (string key in new[]
                     {
                         "accountId", "AccountId", "id", "Id",
                         "playerId", "PlayerId", "targetAccountId", "TargetAccountId"
                     })
            {
                if (long.TryParse(ReadFlexibleString(payload.Values, key)?.Trim('"'), out long parsed) &&
                    parsed > 0)
                {
                    accountId = parsed;
                    break;
                }
            }

            return new RoomRoleInviteTarget
            {
                AccountId = accountId,
                Username = ReadFlexibleString(
                    payload.Values,
                    "username", "Username", "displayName", "DisplayName")
            };
        }

        private async Task<RoomSettingsPayload> ReadRoomSettingsPayloadAsync()
        {
            var payload = new RoomSettingsPayload();
            foreach (var item in Request.Query)
                AddRoomSettingValues(payload.Values, item.Key, item.Value);

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
                foreach (var item in form)
                    AddRoomSettingValues(payload.Values, item.Key, item.Value);
                return payload;
            }

            if ((Request.ContentLength ?? 0) <= 0 &&
                Request.Headers.TransferEncoding.Count == 0)
                return payload;

            using var reader = new StreamReader(Request.Body);
            payload.RawBody = await reader.ReadToEndAsync(HttpContext.RequestAborted);
            if (string.IsNullOrWhiteSpace(payload.RawBody))
                return payload;

            try
            {
                using var document = JsonDocument.Parse(payload.RawBody);
                JsonElement root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    AddRoomSettingsJsonObject(payload.Values, root);
                    foreach (string wrapperName in new[]
                             {
                                 "room", "Room", "settings", "Settings",
                                 "roomSettings", "RoomSettings", "update", "Update",
                                 "request", "Request", "data", "Data"
                             })
                    {
                        if (TryGetProperty(root, wrapperName, out JsonElement nested) &&
                            nested.ValueKind == JsonValueKind.Object)
                        {
                            AddRoomSettingsJsonObject(payload.Values, nested);
                        }
                    }
                }
                else
                {
                    payload.Values["value"] = root.ValueKind == JsonValueKind.String
                        ? root.GetString() ?? string.Empty
                        : root.GetRawText();
                }
            }
            catch (JsonException)
            {
                payload.Values["value"] = payload.RawBody.Trim();
            }

            return payload;
        }

        private static void AddRoomSettingValues(
            IDictionary<string, string> destination,
            string key,
            Microsoft.Extensions.Primitives.StringValues values)
        {
            if (values.Count <= 0)
                return;

            bool isList = key.Equals("tags", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("tagNames", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("permissions", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("roomPermissions", StringComparison.OrdinalIgnoreCase);
            destination[key] = isList && values.Count > 1
                ? JsonSerializer.Serialize(values.ToArray())
                : values[values.Count - 1] ?? string.Empty;
        }

        private static void AddRoomSettingsJsonObject(
            IDictionary<string, string> destination,
            JsonElement element)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                destination[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText();
            }
        }

        private static bool? ReadFlexibleBool(
            IReadOnlyDictionary<string, string> values,
            params string[] keys)
        {
            string? raw = ReadFlexibleString(values, keys)?.Trim('"');
            if (bool.TryParse(raw, out bool boolean))
                return boolean;
            if (int.TryParse(raw, out int number))
                return number != 0;
            return null;
        }

        private static int? ReadFlexibleInt(
            IReadOnlyDictionary<string, string> values,
            params string[] keys) =>
            int.TryParse(ReadFlexibleString(values, keys)?.Trim('"'), out int value)
                ? value
                : null;

        private static string? ReadFlexibleString(
            IReadOnlyDictionary<string, string> values,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                if (!values.TryGetValue(key, out string? value))
                    continue;
                string clean = value.Trim();
                if (clean.Length >= 2 && clean[0] == '"' && clean[^1] == '"')
                {
                    try
                    {
                        return JsonSerializer.Deserialize<string>(clean);
                    }
                    catch (JsonException)
                    {
                    }
                }
                return clean;
            }
            return null;
        }

        private static bool TryParseRoomAccessibility(
            string raw,
            out RoomDBClasses.RoomAccessibility accessibility)
        {
            raw = raw.Trim().Trim('"');
            if (int.TryParse(raw, out int number) &&
                Enum.IsDefined(typeof(RoomDBClasses.RoomAccessibility), number))
            {
                accessibility = (RoomDBClasses.RoomAccessibility)number;
                return true;
            }
            return Enum.TryParse(raw, true, out accessibility) &&
                Enum.IsDefined(typeof(RoomDBClasses.RoomAccessibility), accessibility);
        }

        private static List<string> ParseRoomTags(RoomSettingsPayload payload)
        {
            string? raw = ReadFlexibleString(
                payload.Values,
                "tags", "Tags", "tagNames", "TagNames", "tag", "Tag", "value");
            if (string.IsNullOrWhiteSpace(raw))
                return new List<string>();

            var output = new List<string>();
            try
            {
                using var document = JsonDocument.Parse(raw);
                JsonElement root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    TryGetProperty(root, "tags", out JsonElement nested))
                {
                    root = nested;
                }

                IEnumerable<JsonElement> entries = root.ValueKind == JsonValueKind.Array
                    ? root.EnumerateArray().ToArray()
                    : new[] { root };
                foreach (JsonElement entry in entries)
                {
                    string? tag;
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        tag = entry.GetString();
                    }
                    else if (entry.ValueKind == JsonValueKind.Object &&
                             (TryGetProperty(entry, "tag", out JsonElement tagElement) ||
                              TryGetProperty(entry, "name", out tagElement)))
                    {
                        tag = tagElement.ToString();
                    }
                    else
                    {
                        tag = entry.ToString();
                    }

                    string normalized = NormalizeRoomTag(tag);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        output.Add(normalized);
                }
            }
            catch (JsonException)
            {
                foreach (string tag in raw.Split(
                             new[] { ',', ';', '\n', '\r' },
                             StringSplitOptions.RemoveEmptyEntries |
                             StringSplitOptions.TrimEntries))
                {
                    string normalized = NormalizeRoomTag(tag);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        output.Add(normalized);
                }
            }

            return output
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .ToList();
        }

        private static string NormalizeRoomTag(string? tag)
        {
            string value = (tag ?? string.Empty).Trim().TrimStart('#');
            value = new string(value
                .Where(character => char.IsLetterOrDigit(character) ||
                    character is '-' or '_' or ' ')
                .Take(32)
                .ToArray());
            return value.Trim().ToLowerInvariant();
        }

        private static void EnsureRoomTag(
            RoomDBClasses.Room room,
            string tag,
            RoomDBClasses.TagType type)
        {
            room.Tags ??= new List<RoomDBClasses.Tags>();
            if (!room.Tags.Any(value => string.Equals(
                    value.Tag,
                    tag,
                    StringComparison.OrdinalIgnoreCase)))
            {
                room.Tags.Add(new RoomDBClasses.Tags { Tag = tag, Type = type });
            }
        }

        private static void ApplyRoomBeta(RoomDBClasses.Room room, bool enabled)
        {
            room.CreativeToolsBetaEnabled = enabled;
            room.Tags ??= new List<RoomDBClasses.Tags>();
            room.Tags.RemoveAll(value => string.Equals(
                value.Tag,
                "beta",
                StringComparison.OrdinalIgnoreCase));
            if (enabled)
                room.Tags.Add(new RoomDBClasses.Tags
                {
                    Tag = "beta",
                    Type = RoomDBClasses.TagType.Auto
                });
        }

        private async Task<List<RoomDBClasses.SubRoomPermission>>
            ReadPermissionPayloadFromRequestAsync()
        {
            string rawBody = Request.Query["permissions"].FirstOrDefault()
                ?? Request.Query["Permissions"].FirstOrDefault()
                ?? Request.Query["permissionSettings"].FirstOrDefault()
                ?? Request.Query["roomPermissions"].FirstOrDefault()
                ?? string.Empty;

            string? singlePermission = Request.Query["permission"].FirstOrDefault()
                ?? Request.Query["Permission"].FirstOrDefault()
                ?? Request.Query["permissionName"].FirstOrDefault()
                ?? Request.Query["PermissionName"].FirstOrDefault();
            string? overrideRaw = Request.Query["override"].FirstOrDefault()
                ?? Request.Query["Override"].FirstOrDefault();
            string? roleRaw = Request.Query["role"].FirstOrDefault()
                ?? Request.Query["Role"].FirstOrDefault();
            string? typeRaw = Request.Query["type"].FirstOrDefault()
                ?? Request.Query["Type"].FirstOrDefault();
            string? valueRaw = Request.Query["value"].FirstOrDefault()
                ?? Request.Query["Value"].FirstOrDefault();

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
                rawBody = form["permissions"].FirstOrDefault()
                    ?? form["Permissions"].FirstOrDefault()
                    ?? form["permissionSettings"].FirstOrDefault()
                    ?? form["PermissionSettings"].FirstOrDefault()
                    ?? form["roomPermissions"].FirstOrDefault()
                    ?? form["RoomPermissions"].FirstOrDefault()
                    ?? form["data"].FirstOrDefault()
                    ?? form["json"].FirstOrDefault()
                    ?? rawBody;
                singlePermission = form["permission"].FirstOrDefault()
                    ?? form["Permission"].FirstOrDefault()
                    ?? form["permissionName"].FirstOrDefault()
                    ?? form["PermissionName"].FirstOrDefault()
                    ?? singlePermission;
                overrideRaw = form["override"].FirstOrDefault()
                    ?? form["Override"].FirstOrDefault()
                    ?? overrideRaw;
                roleRaw = form["role"].FirstOrDefault()
                    ?? form["Role"].FirstOrDefault()
                    ?? roleRaw;
                typeRaw = form["type"].FirstOrDefault()
                    ?? form["Type"].FirstOrDefault()
                    ?? typeRaw;
                valueRaw = form["value"].FirstOrDefault()
                    ?? form["Value"].FirstOrDefault()
                    ?? valueRaw;
            }
            else if (string.IsNullOrWhiteSpace(rawBody) &&
                     ((Request.ContentLength ?? 0) > 0 ||
                      Request.Headers.TransferEncoding.Count > 0))
            {
                using var reader = new StreamReader(Request.Body);
                rawBody = await reader.ReadToEndAsync(HttpContext.RequestAborted);
            }

            List<RoomDBClasses.SubRoomPermission> parsed =
                ParsePermissionPayload(rawBody);
            if (parsed.Count > 0)
                return parsed;

            if (string.IsNullOrWhiteSpace(singlePermission))
                return parsed;

            return new List<RoomDBClasses.SubRoomPermission>
            {
                new()
                {
                    Permission = singlePermission,
                    Override = !bool.TryParse(overrideRaw, out bool overrideValue) ||
                        overrideValue,
                    Role = int.TryParse(roleRaw, out int role) ? role : 0,
                    Type = int.TryParse(typeRaw, out int type) ? type : 0,
                    Value = string.IsNullOrWhiteSpace(valueRaw) ? "True" : valueRaw
                }
            };
        }

        [HttpPut("rooms/{roomId}/roles/{roleId}")]
        [HttpPost("rooms/{roomId}/roles/{roleId}")]
        [HttpPatch("rooms/{roomId}/roles/{roleId}")]
        public IActionResult AcceptRoomRole(long roomId, int roleId)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            if (!Enum.IsDefined(typeof(RoomDBClasses.Role), roleId))
                return BadRequest(MutationFailure("invalid_role", "That room role is invalid."));

            var requestedRole = (RoomDBClasses.Role)roleId;
            if (requestedRole is RoomDBClasses.Role.Banned or
                RoomDBClasses.Role.Creator)
            {
                return BadRequest(MutationFailure(
                    "invalid_role",
                    "That room role cannot be accepted through an invitation."));
            }

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound(MutationFailure("room_not_found", "Room was not found."));

            room.Roles ??= new List<RoomDBClasses.Roles>();
            var assignment = room.Roles.FirstOrDefault(role =>
                role.AccountId == accountId.Value);

            if (assignment == null || assignment.InvitedRole != requestedRole)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    MutationFailure(
                        "room_role_invite_required",
                        "A matching room-role invitation is required."));
            }

            assignment.Role = requestedRole;
            assignment.InvitedRole = RoomDBClasses.Role.None;
            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);

            if (!RoomDB.Rooms.Update(room))
                return StatusCode(StatusCodes.Status500InternalServerError);

            Console.WriteLine(
                $"[ROOM ROLE ACCEPTED] room={roomId} role={roleId} player={accountId.Value}");
            return Ok(MutationSuccess(RoomDB.PrepareRoomForClient(room)!));
        }

        [HttpPut("rooms/{roomId}/roles/{roleId}/invite")]
        [HttpPost("rooms/{roomId}/roles/{roleId}/invite")]
        [HttpPatch("rooms/{roomId}/roles/{roleId}/invite")]
        public async Task<IActionResult> InviteToRoomRole(long roomId, int roleId)
        {
            long? callerId = AuthStuff.GetPlayerId(Request);
            if (!callerId.HasValue)
                return Unauthorized();

            if (!Enum.IsDefined(typeof(RoomDBClasses.Role), roleId))
                return BadRequest(MutationFailure("invalid_role", "That room role is invalid."));

            var invitedRole = (RoomDBClasses.Role)roleId;
            if (invitedRole is not (RoomDBClasses.Role.Host or
                RoomDBClasses.Role.Moderator or
                RoomDBClasses.Role.CoOwner or
                RoomDBClasses.Role.TemporaryCoOwner))
            {
                return BadRequest(MutationFailure(
                    "invalid_role",
                    "Only host, moderator, co-owner, and temporary co-owner can be invited."));
            }

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound(MutationFailure("room_not_found", "Room was not found."));
            if (!CanEditRoom(room, callerId.Value))
                return StatusCode(StatusCodes.Status403Forbidden);

            RoomRoleInviteTarget target = await ReadRoomRoleInviteTargetAsync();
            long targetAccountId = target.AccountId;
            if (targetAccountId <= 0 && !string.IsNullOrWhiteSpace(target.Username))
            {
                var matched = PlayerDB.Players.FindAll().FirstOrDefault(player =>
                    string.Equals(
                        player.Player?.Username,
                        target.Username,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        player.Player?.DisplayName,
                        target.Username,
                        StringComparison.OrdinalIgnoreCase));
                targetAccountId = matched?.PlayerId ?? 0;
            }

            if (targetAccountId <= 0)
            {
                return BadRequest(MutationFailure(
                    "account_id_required",
                    "A valid accountId or username is required."));
            }
            if (targetAccountId == callerId.Value)
            {
                return BadRequest(MutationFailure(
                    "cannot_invite_self",
                    "You cannot invite yourself to a room role."));
            }
            if (PlayerDB.Players.FindById(targetAccountId)?.Player == null)
                return NotFound(MutationFailure("player_not_found", "Player was not found."));

            room.Roles ??= new List<RoomDBClasses.Roles>();
            var existingRole = room.Roles.FirstOrDefault(role =>
                role.AccountId == targetAccountId);

            if (existingRole == null)
            {
                room.Roles.Add(new RoomDBClasses.Roles
                {
                    AccountId = targetAccountId,
                    Role = RoomDBClasses.Role.None,
                    InvitedRole = invitedRole
                });
            }
            else
            {
                existingRole.InvitedRole = invitedRole;
            }

            room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
            if (!RoomDB.Rooms.Update(room))
                return StatusCode(StatusCodes.Status500InternalServerError);

            Console.WriteLine(
                $"[ROOM ROLE INVITE] room={roomId} role={roleId} " +
                $"from={callerId.Value} to={targetAccountId}");

            return Ok(new
            {
                Success = true,
                Room = RoomDB.PrepareRoomForClient(room),
                AccountId = targetAccountId,
                Role = invitedRole,
                InvitedRole = invitedRole
            });
        }

        [HttpDelete("rooms/{roomId}/roles/{targetAccountId:long}")]
        public IActionResult RemoveRoomRole(long roomId, long targetAccountId)
        {
            long? callerId = AuthStuff.GetPlayerId(Request);
            if (!callerId.HasValue)
                return Unauthorized();

            var room = RoomDB.GetRoom(roomId);
            if (room == null)
                return NotFound();
            if (!CanEditRoom(room, callerId.Value))
                return StatusCode(StatusCodes.Status403Forbidden);
            if (targetAccountId == room.CreatorAccountId)
                return BadRequest(MutationFailure("cannot_remove_creator", "The room creator cannot be removed."));

            room.Roles ??= new List<RoomDBClasses.Roles>();
            int removed = room.Roles.RemoveAll(role => role.AccountId == targetAccountId);
            if (removed > 0)
            {
                room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
                RoomDB.Rooms.Update(room);
            }

            return Ok(MutationSuccess(RoomDB.PrepareRoomForClient(room)!));
        }

        private static List<RoomDBClasses.SubRoomPermission> CreateDefaultSubRoomPermissions()
        {
            string[] standardPermissions =
            {
                "CAN_USE_ROOM_RESET_BUTTON",
                "CAN_USE_DELETE_ALL_BUTTON",
                "CAN_SAVE_INVENTIONS",
                "CAN_SPAWN_INVENTIONS",
                "CAN_USE_PLAY_GIZMOS_TOGGLE"
            };

            var permissions = standardPermissions.Select(permission =>
                new RoomDBClasses.SubRoomPermission
                {
                    Override = true,
                    Permission = permission,
                    Role = 0,
                    Type = 0,
                    Value = "True"
                }).ToList();
            permissions.Add(new RoomDBClasses.SubRoomPermission
            {
                Override = false,
                Permission = "CAN_USE_MAKER_PEN",
                Role = (int)RoomDBClasses.Role.CoOwner,
                Type = 0,
                Value = "True"
            });
            permissions.AddRange(standardPermissions.Select(permission =>
                new RoomDBClasses.SubRoomPermission
                {
                    Override = true,
                    Permission = permission,
                    Role = (int)RoomDBClasses.Role.CoOwner,
                    Type = 0,
                    Value = "True"
                }));
            return permissions;
        }

        private static RoomDBClasses.SubRoomPermission SanitizePermission(
            RoomDBClasses.SubRoomPermission permission)
        {
            string permissionName = (permission.Permission ?? string.Empty).Trim();
            string permissionValue = (permission.Value ?? "True").Trim();
            if (permissionName.Length > 128)
                permissionName = permissionName[..128];
            if (permissionValue.Length > 256)
                permissionValue = permissionValue[..256];

            return new RoomDBClasses.SubRoomPermission
            {
                Override = permission.Override,
                Permission = permissionName,
                Role = Math.Clamp(permission.Role, 0, byte.MaxValue),
                Type = Math.Clamp(permission.Type, 0, 32),
                Value = permissionValue
            };
        }

        private static List<RoomDBClasses.SubRoomPermission> ParsePermissionPayload(string? rawBody)
        {
            if (string.IsNullOrWhiteSpace(rawBody))
                return new List<RoomDBClasses.SubRoomPermission>();

            try
            {
                using var document = JsonDocument.Parse(rawBody);
                JsonElement root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    (TryGetProperty(root, "permissions", out var nestedPermissions) ||
                     TryGetProperty(root, "permissionSettings", out nestedPermissions) ||
                     TryGetProperty(root, "roomPermissions", out nestedPermissions) ||
                     TryGetProperty(root, "values", out nestedPermissions)))
                {
                    root = nestedPermissions;
                }

                var permissions = new List<RoomDBClasses.SubRoomPermission>();
                if (root.ValueKind == JsonValueKind.Object &&
                    !TryGetProperty(root, "permission", out _) &&
                    !TryGetProperty(root, "permissionName", out _))
                {

                    foreach (JsonProperty property in root.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Object)
                        {
                            permissions.Add(ParsePermissionEntry(
                                property.Value,
                                property.Name));
                        }
                        else
                        {
                            permissions.Add(new RoomDBClasses.SubRoomPermission
                            {
                                Permission = property.Name,
                                Override = true,
                                Role = 0,
                                Type = 0,
                                Value = property.Value.ValueKind == JsonValueKind.String
                                    ? property.Value.GetString() ?? "True"
                                    : property.Value.ToString()
                            });
                        }
                    }
                    return permissions;
                }

                IEnumerable<JsonElement> entries = root.ValueKind == JsonValueKind.Array
                    ? root.EnumerateArray().ToArray()
                    : new[] { root };

                foreach (JsonElement entry in entries)
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                        continue;

                    RoomDBClasses.SubRoomPermission permission =
                        ParsePermissionEntry(entry, null);
                    if (!string.IsNullOrWhiteSpace(permission.Permission))
                        permissions.Add(permission);
                }

                return permissions;
            }
            catch (JsonException)
            {
                return new List<RoomDBClasses.SubRoomPermission>();
            }
        }

        private static RoomDBClasses.SubRoomPermission ParsePermissionEntry(
            JsonElement entry,
            string? fallbackName)
        {
            string permissionName = fallbackName ?? string.Empty;
            foreach (string key in new[]
                     {
                         "permission", "Permission", "permissionName",
                         "PermissionName", "name", "Name", "key", "Key"
                     })
            {
                if (TryGetProperty(entry, key, out JsonElement permissionElement))
                {
                    permissionName = permissionElement.ToString();
                    break;
                }
            }

            bool isOverride = true;
            JsonElement overrideElement;
            bool hasOverride = TryGetProperty(entry, "override", out overrideElement);
            if (hasOverride)
            {
                isOverride = overrideElement.ValueKind == JsonValueKind.True ||
                    (overrideElement.ValueKind == JsonValueKind.String &&
                     bool.TryParse(overrideElement.GetString(), out bool overrideValue) &&
                     overrideValue) ||
                    (overrideElement.ValueKind == JsonValueKind.Number &&
                     overrideElement.TryGetInt32(out int overrideNumber) &&
                     overrideNumber != 0);
            }

            string permissionValue = "True";
            JsonElement valueElement;
            bool hasValue = TryGetProperty(entry, "value", out valueElement) ||
                            TryGetProperty(entry, "enabled", out valueElement);
            if (hasValue)
            {
                permissionValue = valueElement.ValueKind == JsonValueKind.String
                    ? valueElement.GetString() ?? "True"
                    : valueElement.ToString();
            }

            return new RoomDBClasses.SubRoomPermission
            {
                Override = isOverride,
                Permission = permissionName,
                Role = ReadJsonInt(entry, "role") != 0
                    ? ReadJsonInt(entry, "role")
                    : ReadJsonInt(entry, "Role"),
                Type = ReadJsonInt(entry, "type") != 0
                    ? ReadJsonInt(entry, "type")
                    : ReadJsonInt(entry, "Type"),
                Value = permissionValue
            };
        }

        private static int ReadJsonInt(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var value))
                return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;
            return int.TryParse(value.ToString(), out number) ? number : 0;
        }

        private static bool CanUseRoomPlayerData(
            RoomDBClasses.Room? room,
            long accountId)
        {
            if (RoomDB.CanPlayerAccessRoom(room, accountId))
                return true;

            PlayerDBClasses.Heartbeat heartbeat =
                PlayerDB.GetPlayerHeartbeat(accountId);
            return room != null &&
                   heartbeat.isOnline &&
                   heartbeat.roomInstance?.roomId == room.RoomId;
        }

        private static object BuildRoomPlayerDataResponse(
            long roomId,
            long accountId,
            string dataJson,
            int version,
            DateTime? updatedAtUtc)
        {

            _ = dataJson;
            _ = version;
            _ = updatedAtUtc;
            return new
            {
                roomId,
                accountId,
                data = (object?)null
            };
        }

        private static JsonElement UnwrapRoomPlayerData(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
                return root;

            foreach (string name in new[]
                     {
                         "data", "Data", "playerData", "PlayerData",
                         "value", "Value"
                     })
            {
                if (!TryGetProperty(root, name, out JsonElement nested))
                    continue;

                if (nested.ValueKind == JsonValueKind.String)
                {
                    string? raw = nested.GetString();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        try
                        {
                            using JsonDocument nestedDocument = JsonDocument.Parse(raw);
                            return nestedDocument.RootElement.Clone();
                        }
                        catch (JsonException)
                        {
                        }
                    }
                }

                if (nested.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    return nested;
            }

            return root;
        }

        private static void MirrorPlayerDataToCloudVariables(
            long roomId,
            long accountId,
            JsonElement data)
        {
            IEnumerable<(string Key, JsonElement Value)> entries =
                EnumeratePlayerDataVariables(data);

            foreach ((string key, JsonElement value) in entries.Take(512))
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                try
                {
                    CreatorFeatureDB.SetCloudVariable(
                        roomId,
                        accountId,
                        key,
                        value.GetRawText(),
                        accountId);
                }
                catch (ArgumentException)
                {

                }
                catch (InvalidOperationException)
                {
                    break;
                }
            }
        }

        private static IEnumerable<(string Key, JsonElement Value)>
            EnumeratePlayerDataVariables(JsonElement data)
        {
            if (data.ValueKind == JsonValueKind.Object &&
                (TryGetProperty(data, "variables", out JsonElement variables) ||
                 TryGetProperty(data, "Variables", out variables)) &&
                variables.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in variables.EnumerateArray())
                {
                    if (TryReadPlayerDataVariable(item, out string key, out JsonElement value))
                        yield return (key, value);
                }
                yield break;
            }

            if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in data.EnumerateArray())
                {
                    if (TryReadPlayerDataVariable(item, out string key, out JsonElement value))
                        yield return (key, value);
                }
                yield break;
            }

            if (data.ValueKind != JsonValueKind.Object)
                yield break;

            foreach (JsonProperty property in data.EnumerateObject())
            {
                if (property.Name.Equals("roomId", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("accountId", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("playerId", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("version", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Equals("updatedAt", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryReadPlayerDataVariable(
                        property.Value,
                        out string nestedKey,
                        out JsonElement nestedValue,
                        property.Name))
                {
                    yield return (nestedKey, nestedValue);
                }
                else
                {
                    yield return (property.Name, property.Value);
                }
            }
        }

        private static bool TryReadPlayerDataVariable(
            JsonElement item,
            out string key,
            out JsonElement value,
            string? fallbackKey = null)
        {
            key = fallbackKey ?? string.Empty;
            value = default;
            if (item.ValueKind != JsonValueKind.Object)
                return false;

            foreach (string keyName in new[]
                     {
                         "key", "Key", "name", "Name", "variableName",
                         "VariableName", "cloudVariableName", "CloudVariableName"
                     })
            {
                if (TryGetProperty(item, keyName, out JsonElement keyElement))
                {
                    key = keyElement.ToString();
                    break;
                }
            }

            foreach (string valueName in new[]
                     {
                         "value", "Value", "data", "Data", "variableValue",
                         "VariableValue", "cloudVariableValue", "CloudVariableValue"
                     })
            {
                if (TryGetProperty(item, valueName, out value))
                    return !string.IsNullOrWhiteSpace(key);
            }

            return false;
        }

        private static bool TryGetProperty(
            JsonElement element,
            string propertyName,
            out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }
    }
}
