using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Mocha2023.Classes.DBs.DBClasses;
using static Mocha2023.Classes.DBs.DBClasses.RoomDBClasses;

namespace Mocha2023.Classes.DBs;

public static class ShowdownImporter
{
    private const int CompatiblePersistenceVersion = 38;
    private const int CompatibleUgcSubVersion = 0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public sealed record ImportResult(
        long RoomId,
        int SubRoomsImported,
        int SavesImported,
        int BlobsCopied,
        int SavesSkipped,
        string PlayableSubRoomName,
        long PlayableSubRoomId)
    {
        public int BakedAssetsImported { get; init; }
        public int AssetBundlesCopied { get; init; }
        public int AssetBundlesMissing { get; init; }
        public bool ImageCopied { get; init; }
        public IReadOnlyList<string> UnityEngineVersions { get; init; } = Array.Empty<string>();
    }

    public static ImportResult ImportUnityMetadata(
        string json,
        long creatorAccountId = 1,
        bool replaceExisting = true)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("The room metadata JSON is empty.");

        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        JsonElement root = document.RootElement;
        foreach (string wrapper in new[] { "room", "Room", "value", "Value", "data", "Data" })
        {
            if (root.ValueKind == JsonValueKind.Object &&
                TryGetProperty(root, wrapper, out JsonElement nested) &&
                nested.ValueKind == JsonValueKind.Object)
            {
                root = nested;
                break;
            }
        }

        ExportRoom exportRoom = JsonSerializer.Deserialize<ExportRoom>(
            root.GetRawText(),
            JsonOptions) ?? throw new InvalidDataException("Could not deserialize the room metadata JSON.");

        return ImportUnityExport(exportRoom, creatorAccountId, replaceExisting);
    }

    public static ImportResult Import(
        string exportRoot,
        long creatorAccountId = 1,
        bool replaceExisting = true)
    {
        exportRoot = Path.GetFullPath(exportRoot.Trim().Trim('"'));
        string roomJsonPath = Path.Combine(exportRoot, "room.json");

        if (!File.Exists(roomJsonPath))
            throw new FileNotFoundException("room.json was not found.", roomJsonPath);

        ExportRoom exportRoom = ReadJson<ExportRoom>(roomJsonPath);
        if (string.IsNullOrWhiteSpace(exportRoom.Name))
            throw new InvalidDataException("room.json does not contain a valid Name.");

        string roomBlobDestination = Path.Combine(Program.dataDir, "CDN", "room");
        string assetBundleDestination = Path.Combine(Program.dataDir, "CDN", "assetbundles");
        string imageDestination = Path.Combine(Program.dataDir, "Images");
        Directory.CreateDirectory(roomBlobDestination);
        Directory.CreateDirectory(assetBundleDestination);
        Directory.CreateDirectory(imageDestination);

        string? assetBundleSource = FindDirectory(exportRoot, "AssetBundles");

        long assignedRoomId = RoomDB.GetNextRoomId();
        string normalizedRoomName = exportRoom.Name.Trim().TrimStart('^');
        Room? existingByName = RoomDB.Rooms.FindAll().FirstOrDefault(room =>
            string.Equals(room.Name, normalizedRoomName, StringComparison.OrdinalIgnoreCase));

        if (replaceExisting)
        {
            if (existingByName != null)
            {
                RoomDB.SubRoomDataSaves.DeleteMany(save => save.RoomId == existingByName.RoomId);
                RoomDB.Rooms.Delete(existingByName.RoomId);
            }
        }
        else if (existingByName != null)
        {
            throw new InvalidOperationException(
                $"A room named \"{normalizedRoomName}\" already exists. Enable replaceExisting to overwrite it.");
        }

        long nextLocalSaveId = RoomDB.SubRoomDataSaves.Count() == 0
            ? 1
            : Convert.ToInt64(RoomDB.SubRoomDataSaves.Max(save => save.SubRoomDataSaveId)) + 1;

        var importedSubRooms = new List<SubRooms>();
        int savesImported = 0;
        int blobsCopied = 0;
        int savesSkipped = 0;
        int bakedAssetsImported = 0;
        int assetBundlesCopied = 0;
        int assetBundlesMissing = 0;
        var unityEngineVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        SubRooms? playableSubRoom = null;
        SubRoomDataSave? playableCurrentSave = null;

        foreach (ExportSubRoom listedSubRoom in exportRoom.SubRooms ?? new List<ExportSubRoom>())
        {
            string subRoomDirectory = Path.Combine(exportRoot, listedSubRoom.SubRoomId.ToString());
            string subRoomJsonPath = Path.Combine(subRoomDirectory, "subRoom.json");
            if (!File.Exists(subRoomJsonPath))
            {
                Console.WriteLine($"[ROOM IMPORT] Missing subRoom.json for {listedSubRoom.SubRoomId}; skipped.");
                continue;
            }

            ExportSubRoom sourceSubRoom = ReadJson<ExportSubRoom>(subRoomJsonPath);
            MergeSubRoomMetadata(listedSubRoom, sourceSubRoom);

            string savesDirectory = Path.Combine(subRoomDirectory, "Saves");
            if (!Directory.Exists(savesDirectory))
            {
                Console.WriteLine($"[ROOM IMPORT] Missing Saves directory for {sourceSubRoom.SubRoomId}; skipped.");
                continue;
            }

            var importedForThisSubRoom = new List<(long SourceSaveId, SubRoomDataSave LocalSave)>();

            foreach (string saveDirectory in Directory.EnumerateDirectories(savesDirectory))
            {
                string directoryName = Path.GetFileName(saveDirectory);
                string saveJsonPath = Path.Combine(saveDirectory, directoryName + ".json");

                if (!File.Exists(saveJsonPath))
                {
                    saveJsonPath = Directory.EnumerateFiles(saveDirectory, "*.json")
                        .FirstOrDefault(path =>
                            !Path.GetFileName(path).StartsWith(
                                "bakedUnityAsset-",
                                StringComparison.OrdinalIgnoreCase))
                        ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(saveJsonPath) || !File.Exists(saveJsonPath))
                {
                    savesSkipped++;
                    continue;
                }

                ExportSave sourceSave;
                try
                {
                    sourceSave = ReadJson<ExportSave>(saveJsonPath);
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"[ROOM IMPORT] Invalid save JSON {saveJsonPath}: {exception.Message}");
                    savesSkipped++;
                    continue;
                }

                string safeBlobName = Path.GetFileName(sourceSave.DataBlob ?? string.Empty);
                string sourceBlobPath = Path.Combine(saveDirectory, safeBlobName);
                if (string.IsNullOrWhiteSpace(safeBlobName) || !File.Exists(sourceBlobPath))
                {
                    Console.WriteLine($"[ROOM IMPORT] Missing blob for source save {sourceSave.SubRoomDataSaveId}; skipped.");
                    savesSkipped++;
                    continue;
                }

                string destinationBlobPath = Path.Combine(roomBlobDestination, safeBlobName);
                if (CopyIfDifferent(sourceBlobPath, destinationBlobPath))
                    blobsCopied++;

                var bakedAssets = new List<BakedUnityAsset>();
                foreach (string bakedJsonPath in Directory.EnumerateFiles(
                             saveDirectory,
                             "bakedUnityAsset-*.json",
                             SearchOption.TopDirectoryOnly))
                {
                    BakedUnityAsset? bakedAsset;
                    try
                    {
                        bakedAsset = ReadJson<BakedUnityAsset>(bakedJsonPath);
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine($"[ROOM IMPORT] Invalid baked asset JSON {bakedJsonPath}: {exception.Message}");
                        continue;
                    }

                    bakedAsset.UnityAssetId = string.IsNullOrWhiteSpace(bakedAsset.UnityAssetId)
                        ? sourceSave.UnityAssetId ?? string.Empty
                        : bakedAsset.UnityAssetId.Trim();
                    bakedAsset.Filename = Path.GetFileName(bakedAsset.Filename ?? string.Empty);
                    bakedAsset.UnityVersion = ParseUnityVersionFromMetadataName(Path.GetFileName(bakedJsonPath));

                    if (!string.IsNullOrWhiteSpace(bakedAsset.UnityVersion))
                        unityEngineVersions.Add(bakedAsset.UnityVersion);

                    if (string.IsNullOrWhiteSpace(bakedAsset.UnityAssetId) ||
                        string.IsNullOrWhiteSpace(bakedAsset.Filename))
                    {
                        Console.WriteLine($"[ROOM IMPORT] Baked asset metadata {bakedJsonPath} is missing an ID or filename.");
                        continue;
                    }

                    if (assetBundleSource == null)
                    {
                        bakedAsset.IsAvailable = false;
                        assetBundlesMissing++;
                    }
                    else
                    {
                        string sourceBundlePath = Path.Combine(assetBundleSource, bakedAsset.Filename);
                        string destinationBundlePath = Path.Combine(assetBundleDestination, bakedAsset.Filename);
                        if (!File.Exists(sourceBundlePath))
                        {
                            bakedAsset.IsAvailable = false;
                            assetBundlesMissing++;
                            Console.WriteLine(
                                $"[ROOM IMPORT] Missing asset bundle {bakedAsset.Filename} " +
                                $"for UnityAssetId={bakedAsset.UnityAssetId} target={bakedAsset.Target}.");
                        }
                        else
                        {
                            if (CopyIfDifferent(sourceBundlePath, destinationBundlePath))
                                assetBundlesCopied++;

                            string actualBundleHash = ComputeSha256Base64(destinationBundlePath);
                            if (!string.Equals(bakedAsset.Hash, actualBundleHash, StringComparison.Ordinal))
                            {
                                Console.WriteLine(
                                    $"[ROOM IMPORT] Asset bundle hash normalized " +
                                    $"file={bakedAsset.Filename} old={bakedAsset.Hash ?? "null"} " +
                                    $"new={actualBundleHash}");
                                bakedAsset.Hash = actualBundleHash;
                            }

                            bakedAsset.IsAvailable = true;
                        }
                    }

                    bakedAssets.Add(bakedAsset);
                    bakedAssetsImported++;
                }

                string actualHash = ComputeSha256Base64(destinationBlobPath);
                bool isSingleBlobBakedSave = !string.IsNullOrWhiteSpace(sourceSave.UnityAssetId);
                var localSave = new SubRoomDataSave
                {
                    SubRoomDataSaveId = nextLocalSaveId++,
                    RoomId = assignedRoomId,
                    SubRoomId = sourceSubRoom.SubRoomId,
                    SavedByAccountId = creatorAccountId,
                    DataBlob = safeBlobName,
                    RoomDataBlob = isSingleBlobBakedSave ? safeBlobName : null,
                    DataBlobHash = actualHash,
                    PersistenceVersion = NormalizePersistenceVersion(sourceSave.PersistenceVersion),
                    SavedOnPlatform = sourceSave.SavedOnPlatform,
                    SavedOnDeviceClass = sourceSave.SavedOnDeviceClass,
                    Description = sourceSave.Description,
                    CreatedAt = NormalizeCreatedAt(sourceSave.CreatedAt),
                    IsPublished = sourceSave.ModerationState != 0,
                    UnityAssetId = sourceSave.UnityAssetId,
                    ReferencedUnityAssetIds = sourceSave.ReferencedUnityAssetIds ?? new List<string>(),
                    OMVersion = RoomDB.NormalizeLegacyOMVersion(sourceSave.OMVersion),
                    UgcSubVersion = NormalizeUgcSubVersion(sourceSave.UgcSubVersion),
                    ModerationState = sourceSave.ModerationState,
                    Tags = sourceSave.Tags ?? new List<string>(),
                    BakedUnityAssets = bakedAssets
                };

                RoomDB.SubRoomDataSaves.Insert(localSave);
                importedForThisSubRoom.Add((sourceSave.SubRoomDataSaveId, localSave));
                savesImported++;
            }

            if (importedForThisSubRoom.Count == 0)
            {
                Console.WriteLine($"[ROOM IMPORT] Subroom {sourceSubRoom.Name} has no usable saves; skipped.");
                continue;
            }

            long requestedCurrentSaveId = sourceSubRoom.CurrentSave?.SubRoomDataSaveId
                ?? listedSubRoom.CurrentSave?.SubRoomDataSaveId
                ?? 0;

            (long SourceSaveId, SubRoomDataSave LocalSave) selectedItem =
                importedForThisSubRoom.FirstOrDefault(item => item.SourceSaveId == requestedCurrentSaveId);

            if (selectedItem.LocalSave == null)
            {
                selectedItem = importedForThisSubRoom
                    .OrderByDescending(item => item.LocalSave.CreatedAt)
                    .ThenByDescending(item => item.SourceSaveId)
                    .First();
            }

            SubRoomDataSave currentSave = selectedItem.LocalSave;
            Console.WriteLine(
                $"[ROOM IMPORT SELECT] subroom={sourceSubRoom.SubRoomId}:{sourceSubRoom.Name} " +
                $"sourceSave={selectedItem.SourceSaveId} requested={requestedCurrentSaveId} " +
                $"created={currentSave.CreatedAt:O} blob={currentSave.DataBlob} " +
                $"persistence={currentSave.PersistenceVersion} " +
                $"unityAsset={currentSave.UnityAssetId ?? "none"}");

            int accessibilityValue = Enum.IsDefined(typeof(RoomAccessibility), sourceSubRoom.Accessibility)
                ? sourceSubRoom.Accessibility
                : (int)RoomAccessibility.Public;

            var localSubRoom = new SubRooms
            {
                SubRoomId = sourceSubRoom.SubRoomId,
                RoomId = assignedRoomId,
                Name = string.IsNullOrWhiteSpace(sourceSubRoom.Name) ? "Home" : sourceSubRoom.Name,
                DataBlob = currentSave.DataBlob,
                SubRoomDataSaveId = currentSave.SubRoomDataSaveId,
                SubRoomDataSave = currentSave,
                IsSandbox = sourceSubRoom.IsSandbox,
                MaxPlayers = Math.Clamp(sourceSubRoom.MaxPlayers > 0 ? sourceSubRoom.MaxPlayers : 6, 1, 100),
                Accessibility = (RoomAccessibility)accessibilityValue,
                UnitySceneId = sourceSubRoom.UnitySceneId ?? string.Empty,
                SavedByAccountId = creatorAccountId,
                Permissions = new List<SubRoomPermission>()
            };

            importedSubRooms.Add(localSubRoom);

            bool isNamedHome = string.Equals(localSubRoom.Name, "Home", StringComparison.OrdinalIgnoreCase);
            bool isPublic = localSubRoom.Accessibility != RoomAccessibility.Private;
            bool currentIsPublic = playableSubRoom == null ||
                playableSubRoom.Accessibility != RoomAccessibility.Private;
            if (playableSubRoom == null ||
                (isPublic && !currentIsPublic) ||
                (isPublic == currentIsPublic && isNamedHome))
            {
                playableSubRoom = localSubRoom;
                playableCurrentSave = currentSave;
            }
        }

        if (importedSubRooms.Count == 0 &&
            exportRoom.SubRooms?.Any(subRoom => !string.IsNullOrWhiteSpace(subRoom.UnitySceneId)) == true)
        {
            return ImportUnityExport(exportRoom, creatorAccountId, replaceExisting: false);
        }

        if (importedSubRooms.Count == 0)
            throw new InvalidDataException("No compatible save blobs or UnitySceneId subrooms could be imported.");

        playableSubRoom ??= importedSubRooms[0];
        playableCurrentSave ??= RoomDB.SubRoomDataSaves.FindById(playableSubRoom.SubRoomDataSaveId);
        playableSubRoom.Name = "Home";
        importedSubRooms.Remove(playableSubRoom);
        importedSubRooms.Insert(0, playableSubRoom);

        bool imageCopied = CopyRoomImage(exportRoot, exportRoom.ImageName, imageDestination, out string imageName);
        int roomAccessibility = Enum.IsDefined(typeof(RoomAccessibility), exportRoom.Accessibility)
            ? exportRoom.Accessibility
            : (int)RoomAccessibility.Public;

        bool hasBakedAssets = importedSubRooms.Any(subRoom =>
            RoomDB.SubRoomDataSaves.FindById(subRoom.SubRoomDataSaveId)?.BakedUnityAssets?.Count > 0);

        var room = new Room
        {
            RoomId = assignedRoomId,
            IsDorm = false,
            MaxPlayerCalculationMode = 0,
            MaxPlayers = Math.Clamp(
                exportRoom.MaxPlayers > 0 ? exportRoom.MaxPlayers : playableSubRoom.MaxPlayers,
                1,
                100),
            CloningAllowed = false,
            DisableMicAutoMute = false,
            DisableRoomComments = false,
            EncryptVoiceChat = false,
            ToxmodEnabled = true,
            LoadScreenLocked = false,
            PersistenceVersion = NormalizePersistenceVersion(
                playableCurrentSave?.PersistenceVersion ?? exportRoom.PersistenceVersion),
            AutoLocalizeRoom = false,
            IsDeveloperOwned = true,
            Name = exportRoom.Name.Trim().TrimStart('^'),
            Description = string.IsNullOrWhiteSpace(exportRoom.Description)
                ? "Imported room export."
                : exportRoom.Description,
            ImageName = imageName,
            WarningMask = 0,
            CustomWarning = null,
            CreatorAccountId = creatorAccountId,
            State = RoomState.Active,
            Accessibility = (RoomAccessibility)roomAccessibility,
            SupportsLevelVoting = false,
            IsRRO = false,
            IsBaseRoom = false,
            CreativeToolsBetaEnabled = hasBakedAssets,
            SupportsScreens = true,
            SupportsWalkVR = true,
            SupportsTeleportVR = true,
            SupportsVRLow = true,
            SupportsQuest2 = true,
            SupportsMobile = true,
            SupportsJuniors = true,
            MinLevel = 0,
            CreatedAt = DateTime.UtcNow,
            Stats = new Stats(),
            RankedEntityId = assignedRoomId.ToString(),
            RankingContext = null,
            SubRooms = importedSubRooms,
            Roles = new List<Roles>
            {
                new()
                {
                    AccountId = creatorAccountId,
                    Role = Role.Creator,
                    InvitedRole = Role.None
                }
            },

            DataBlob = playableCurrentSave?.RoomDataBlob,
            UgcVersion = 1,
            Tags = new List<Tags>(),
            PromoImages = new List<string>(),
            PromoExternalContent = new List<PromoExternalContent>(),
            LoadScreens = new List<LoadScreens>()
        };

        if (!RoomDB.Rooms.Upsert(room))
            throw new InvalidOperationException("LiteDB failed to upsert the imported room.");

        Room? verifiedRoom = RoomDB.Rooms.FindById(room.RoomId);
        if (verifiedRoom?.SubRooms == null || verifiedRoom.SubRooms.Count == 0)
            throw new InvalidOperationException("The room was written, but LiteDB returned no subrooms after import.");

        Console.WriteLine(
            $"[ROOM IMPORT] Imported {room.Name} room={room.RoomId} " +
            $"subrooms={importedSubRooms.Count} saves={savesImported} " +
            $"bakedAssets={bakedAssetsImported} bundlesCopied={assetBundlesCopied} " +
            $"bundlesMissing={assetBundlesMissing} playable={playableSubRoom.SubRoomId}:{playableSubRoom.Name}");

        return new ImportResult(
            room.RoomId,
            importedSubRooms.Count,
            savesImported,
            blobsCopied,
            savesSkipped,
            playableSubRoom.Name,
            playableSubRoom.SubRoomId)
        {
            BakedAssetsImported = bakedAssetsImported,
            AssetBundlesCopied = assetBundlesCopied,
            AssetBundlesMissing = assetBundlesMissing,
            ImageCopied = imageCopied,
            UnityEngineVersions = unityEngineVersions.OrderBy(value => value).ToArray()
        };
    }

    private static readonly HttpClient DownloadClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public static async Task<ImportResult> ImportFromUrlAsync(
        string rootUrl,
        long creatorAccountId = 1,
        bool replaceExisting = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootUrl) ||
            !Uri.TryCreate(rootUrl, UriKind.Absolute, out Uri? rootUri) ||
            (rootUri.Scheme != Uri.UriSchemeHttp && rootUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException("Enter a valid http:// or https:// URL to the room's own folder.");
        }

        string normalizedRoot = rootUrl.TrimEnd('/') + "/";
        string importId = Guid.NewGuid().ToString("N");
        string stagingRoot = Path.Combine(Program.dataDir, "Temp", "RoomImports", importId);
        Directory.CreateDirectory(stagingRoot);

        const long maxTotalBytes = 1024L * 1024L * 1024L;
        const int maxSubRooms = 500;
        const int maxSavesPerSubRoom = 500;
        long totalDownloadedBytes = 0;

        async Task DownloadFileAsync(string fileUrl, string destinationPath)
        {
            using HttpResponseMessage response = await DownloadClient.GetAsync(
                fileUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidDataException($"Could not download {fileUrl} ({(int)response.StatusCode}).");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            byte[] buffer = new byte[81920];
            while (true)
            {
                int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                    break;

                totalDownloadedBytes += read;
                if (totalDownloadedBytes > maxTotalBytes)
                {
                    throw new InvalidDataException(
                        "This room's files add up to more than the 1 GiB limit for URL imports. " +
                        "Download it and use the ZIP import instead.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        try
        {
            List<(string Name, bool IsDirectory)> rootEntries =
                await ListRemoteDirectoryAsync(normalizedRoot, cancellationToken);

            if (!rootEntries.Any(entry => !entry.IsDirectory &&
                    string.Equals(entry.Name, "room.json", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "No room.json was found at that URL - point this at a room's own folder " +
                    "(the page that directly lists room.json and the subroom folders).");
            }

            await DownloadFileAsync(normalizedRoot + "room.json", Path.Combine(stagingRoot, "room.json"));
            ExportRoom exportRoom = ReadJson<ExportRoom>(Path.Combine(stagingRoot, "room.json"));

            if (!string.IsNullOrWhiteSpace(exportRoom.ImageName))
            {
                string imageFileName = Path.GetFileName(exportRoom.ImageName);
                (string Name, bool IsDirectory) imageEntry = rootEntries.FirstOrDefault(entry =>
                    !entry.IsDirectory &&
                    string.Equals(entry.Name, imageFileName, StringComparison.OrdinalIgnoreCase));

                if (imageEntry.Name != null)
                {
                    try
                    {
                        await DownloadFileAsync(
                            normalizedRoot + Uri.EscapeDataString(imageEntry.Name),
                            Path.Combine(stagingRoot, imageEntry.Name));
                    }
                    catch (Exception ex)
                    {

                        Console.WriteLine($"[ROOM IMPORT URL] Could not fetch thumbnail: {ex.Message}");
                    }
                }
            }

            var referencedBundleFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int subRoomsFound = 0;

            foreach ((string Name, bool IsDirectory) entry in rootEntries.Where(entry =>
                         entry.IsDirectory &&
                         !string.Equals(entry.Name, "AssetBundles", StringComparison.OrdinalIgnoreCase) &&
                         long.TryParse(entry.Name, out _)))
            {
                if (subRoomsFound >= maxSubRooms)
                    break;

                string subRoomUrl = normalizedRoot + Uri.EscapeDataString(entry.Name) + "/";
                List<(string Name, bool IsDirectory)> subRoomEntries =
                    await ListRemoteDirectoryAsync(subRoomUrl, cancellationToken);

                if (!subRoomEntries.Any(e => !e.IsDirectory &&
                        string.Equals(e.Name, "subRoom.json", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                subRoomsFound++;
                string localSubRoomDir = Path.Combine(stagingRoot, entry.Name);
                await DownloadFileAsync(subRoomUrl + "subRoom.json", Path.Combine(localSubRoomDir, "subRoom.json"));

                (string Name, bool IsDirectory) savesFolder = subRoomEntries.FirstOrDefault(e =>
                    e.IsDirectory && string.Equals(e.Name, "Saves", StringComparison.OrdinalIgnoreCase));
                if (savesFolder.Name == null)
                    continue;

                string savesUrl = subRoomUrl + "Saves/";
                List<(string Name, bool IsDirectory)> saveEntries =
                    await ListRemoteDirectoryAsync(savesUrl, cancellationToken);

                int savesForThisSubRoom = 0;
                foreach ((string Name, bool IsDirectory) saveEntry in saveEntries.Where(e => e.IsDirectory))
                {
                    if (savesForThisSubRoom >= maxSavesPerSubRoom)
                        break;
                    savesForThisSubRoom++;

                    string saveUrl = savesUrl + Uri.EscapeDataString(saveEntry.Name) + "/";
                    List<(string Name, bool IsDirectory)> saveFileEntries =
                        await ListRemoteDirectoryAsync(saveUrl, cancellationToken);
                    string localSaveDir = Path.Combine(localSubRoomDir, "Saves", saveEntry.Name);

                    foreach ((string Name, bool IsDirectory) file in saveFileEntries.Where(e => !e.IsDirectory))
                    {
                        await DownloadFileAsync(
                            saveUrl + Uri.EscapeDataString(file.Name),
                            Path.Combine(localSaveDir, file.Name));

                        if (file.Name.StartsWith("bakedUnityAsset-", StringComparison.OrdinalIgnoreCase) &&
                            file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                BakedUnityAsset baked = ReadJson<BakedUnityAsset>(
                                    Path.Combine(localSaveDir, file.Name));
                                if (!string.IsNullOrWhiteSpace(baked.Filename))
                                    referencedBundleFilenames.Add(Path.GetFileName(baked.Filename));
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(
                                    $"[ROOM IMPORT URL] Unreadable baked asset metadata {file.Name}: {ex.Message}");
                            }
                        }
                    }
                }
            }

            if (subRoomsFound == 0)
            {
                throw new InvalidDataException(
                    "No subroom folders with a subRoom.json were found under that URL.");
            }

            if (referencedBundleFilenames.Count > 0 &&
                rootEntries.Any(entry => entry.IsDirectory &&
                    string.Equals(entry.Name, "AssetBundles", StringComparison.OrdinalIgnoreCase)))
            {
                string bundlesUrl = normalizedRoot + "AssetBundles/";
                string localBundlesDir = Path.Combine(stagingRoot, "AssetBundles");
                foreach (string filename in referencedBundleFilenames)
                {
                    try
                    {
                        await DownloadFileAsync(
                            bundlesUrl + Uri.EscapeDataString(filename),
                            Path.Combine(localBundlesDir, filename));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[ROOM IMPORT URL] Could not fetch asset bundle {filename}: {ex.Message}");
                    }
                }
            }

            return Import(stagingRoot, creatorAccountId, replaceExisting);
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingRoot))
                    Directory.Delete(stagingRoot, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ROOM IMPORT URL] Could not clean up staging folder: {ex.Message}");
            }
        }
    }

    public static async Task<List<(string Name, string Url)>> DiscoverRoomFoldersAsync(
        string rootUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootUrl) ||
            !Uri.TryCreate(rootUrl, UriKind.Absolute, out Uri? rootUri) ||
            (rootUri.Scheme != Uri.UriSchemeHttp && rootUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException("Enter a valid http:// or https:// URL to the rooms index folder.");
        }

        string normalizedRoot = rootUrl.TrimEnd('/') + "/";
        List<(string Name, bool IsDirectory)> entries =
            await ListRemoteDirectoryAsync(normalizedRoot, cancellationToken);

        if (entries.Any(entry => !entry.IsDirectory &&
                string.Equals(entry.Name, "room.json", StringComparison.OrdinalIgnoreCase)))
        {
            string singleName = rootUri.Segments.Length > 0
                ? Uri.UnescapeDataString(rootUri.Segments[^1].Trim('/'))
                : normalizedRoot;
            return new List<(string Name, string Url)>
            {
                (string.IsNullOrWhiteSpace(singleName) ? normalizedRoot : singleName, normalizedRoot)
            };
        }

        const int maxCandidates = 300;
        var found = new List<(string Name, string Url)>();
        foreach ((string Name, bool IsDirectory) entry in entries.Where(entry =>
                     entry.IsDirectory &&
                     !string.Equals(entry.Name, "AssetBundles", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (found.Count >= maxCandidates)
                break;

            string candidateUrl = normalizedRoot + Uri.EscapeDataString(entry.Name) + "/";
            List<(string Name, bool IsDirectory)> candidateEntries;
            try
            {
                candidateEntries = await ListRemoteDirectoryAsync(candidateUrl, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ROOM IMPORT URL DISCOVER] Skipping {candidateUrl}: {ex.Message}");
                continue;
            }

            if (candidateEntries.Any(e => !e.IsDirectory &&
                    string.Equals(e.Name, "room.json", StringComparison.OrdinalIgnoreCase)))
            {
                found.Add((entry.Name, candidateUrl));
            }
        }

        if (found.Count == 0)
        {
            throw new InvalidDataException(
                "No room folders (containing room.json) were found directly under that URL.");
        }

        return found;
    }

    private static async Task<List<(string Name, bool IsDirectory)>> ListRemoteDirectoryAsync(
        string url,
        CancellationToken cancellationToken)
    {
        string html = await DownloadClient.GetStringAsync(url, cancellationToken);
        var entries = new List<(string Name, bool IsDirectory)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(html, "href\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
        {
            string href = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
            if (string.IsNullOrWhiteSpace(href) ||
                href.StartsWith('#') ||
                href.StartsWith('?') ||
                href is "../" or ".." or "/" or "./")
            {
                continue;
            }

            if (href.Contains("://"))
            {

                if (!Uri.TryCreate(href, UriKind.Absolute, out Uri? absolute) ||
                    !Uri.TryCreate(url, UriKind.Absolute, out Uri? baseUri) ||
                    !string.Equals(absolute.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            bool isDirectory = href.EndsWith('/');
            string trimmed = href.TrimEnd('/');
            string rawName = trimmed.Contains('/') ? trimmed[(trimmed.LastIndexOf('/') + 1)..] : trimmed;
            string name = Uri.UnescapeDataString(rawName);

            if (string.IsNullOrWhiteSpace(name) || name is ".." or "." || name.Contains('\0'))
                continue;

            if (seen.Add(name))
                entries.Add((name, isDirectory));
        }

        return entries;
    }

    private static ImportResult ImportUnityExport(
        ExportRoom exportRoom,
        long creatorAccountId,
        bool replaceExisting)
    {
        if (string.IsNullOrWhiteSpace(exportRoom.Name))
            throw new InvalidDataException("Room metadata must contain a Name.");

        var sourceSubRooms = (exportRoom.SubRooms ?? new List<ExportSubRoom>())
            .Where(subRoom => !string.IsNullOrWhiteSpace(subRoom.UnitySceneId))
            .ToList();

        long assignedRoomId = RoomDB.GetNextRoomId();
        string normalizedRoomName = exportRoom.Name.Trim().TrimStart('^');
        Room? existingByName = RoomDB.Rooms.FindAll().FirstOrDefault(room =>
            string.Equals(room.Name, normalizedRoomName, StringComparison.OrdinalIgnoreCase));

        if (sourceSubRooms.Count == 0 && !string.IsNullOrWhiteSpace(exportRoom.UnitySceneId))
        {
            sourceSubRooms.Add(new ExportSubRoom
            {
                RoomId = assignedRoomId,
                SubRoomId = exportRoom.SubRoomId,
                Name = "Home",
                UnitySceneId = exportRoom.UnitySceneId,
                MaxPlayers = exportRoom.MaxPlayers,
                Accessibility = exportRoom.Accessibility
            });
        }

        if (sourceSubRooms.Count == 0)
            throw new InvalidDataException("No UnitySceneId was found in the room metadata.");

        if (replaceExisting)
        {
            if (existingByName != null)
            {
                RoomDB.SubRoomDataSaves.DeleteMany(save => save.RoomId == existingByName.RoomId);
                RoomDB.Rooms.Delete(existingByName.RoomId);
            }
        }
        else if (existingByName != null)
        {
            throw new InvalidOperationException(
                $"A room named \"{normalizedRoomName}\" already exists. Enable replaceExisting to overwrite it.");
        }

        long generatedSubRoomId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var importedSubRooms = new List<SubRooms>();

        foreach ((ExportSubRoom source, int index) in sourceSubRooms.Select((value, index) => (value, index)))
        {
            long subRoomId = source.SubRoomId > 0 ? source.SubRoomId : generatedSubRoomId + index;
            int accessibilityValue = Enum.IsDefined(typeof(RoomAccessibility), source.Accessibility)
                ? source.Accessibility
                : (int)RoomAccessibility.Public;

            importedSubRooms.Add(new SubRooms
            {
                SubRoomId = subRoomId,
                RoomId = assignedRoomId,
                Name = string.IsNullOrWhiteSpace(source.Name) ? $"Scene {index + 1}" : source.Name,
                DataBlob = null,
                SubRoomDataSaveId = 0,
                SubRoomDataSave = null,
                IsSandbox = source.IsSandbox,
                MaxPlayers = source.MaxPlayers > 0
                    ? Math.Clamp(source.MaxPlayers, 1, 100)
                    : Math.Clamp(exportRoom.MaxPlayers > 0 ? exportRoom.MaxPlayers : 6, 1, 100),
                Accessibility = (RoomAccessibility)accessibilityValue,
                UnitySceneId = source.UnitySceneId!.Trim(),
                SavedByAccountId = creatorAccountId,
                Permissions = new List<SubRoomPermission>()
            });
        }

        SubRooms playableSubRoom = importedSubRooms.FirstOrDefault(subRoom =>
            string.Equals(subRoom.Name, "Home", StringComparison.OrdinalIgnoreCase)) ?? importedSubRooms[0];
        playableSubRoom.Name = "Home";
        importedSubRooms.Remove(playableSubRoom);
        importedSubRooms.Insert(0, playableSubRoom);

        int roomAccessibility = Enum.IsDefined(typeof(RoomAccessibility), exportRoom.Accessibility)
            ? exportRoom.Accessibility
            : (int)RoomAccessibility.Public;

        var room = new Room
        {
            RoomId = assignedRoomId,
            IsDorm = false,
            MaxPlayerCalculationMode = 0,
            MaxPlayers = Math.Clamp(
                exportRoom.MaxPlayers > 0 ? exportRoom.MaxPlayers : playableSubRoom.MaxPlayers,
                1,
                100),
            CloningAllowed = false,
            DisableMicAutoMute = false,
            DisableRoomComments = false,
            EncryptVoiceChat = false,
            ToxmodEnabled = true,
            LoadScreenLocked = false,
            PersistenceVersion = NormalizePersistenceVersion(
                exportRoom.PersistenceVersion > 0 ? exportRoom.PersistenceVersion : 27),
            AutoLocalizeRoom = false,
            IsDeveloperOwned = true,
            Name = exportRoom.Name.Trim().TrimStart('^'),
            Description = string.IsNullOrWhiteSpace(exportRoom.Description)
                ? "Imported built-in Unity scene room."
                : exportRoom.Description,
            ImageName = exportRoom.ImageName ?? string.Empty,
            WarningMask = 0,
            CustomWarning = null,
            CreatorAccountId = creatorAccountId,
            State = RoomState.Active,
            Accessibility = (RoomAccessibility)roomAccessibility,
            SupportsLevelVoting = false,
            IsRRO = true,
            IsBaseRoom = true,
            SupportsScreens = true,
            SupportsWalkVR = true,
            SupportsTeleportVR = true,
            SupportsVRLow = true,
            SupportsQuest2 = true,
            SupportsMobile = true,
            SupportsJuniors = true,
            MinLevel = 0,
            CreatedAt = DateTime.UtcNow,
            Stats = new Stats(),
            RankedEntityId = assignedRoomId.ToString(),
            RankingContext = null,
            SubRooms = importedSubRooms,
            Roles = new List<Roles>
            {
                new()
                {
                    AccountId = creatorAccountId,
                    Role = Role.Creator,
                    InvitedRole = Role.None
                }
            },
            DataBlob = null,
            UgcVersion = 1,
            Tags = new List<Tags>
            {
                new() { Tag = "base", Type = TagType.Auto },
                new() { Tag = "rro", Type = TagType.AGOnly }
            },
            PromoImages = new List<string>(),
            PromoExternalContent = new List<PromoExternalContent>(),
            LoadScreens = new List<LoadScreens>()
        };

        if (!RoomDB.Rooms.Upsert(room))
            throw new InvalidOperationException("LiteDB failed to save the imported Unity-scene room.");

        return new ImportResult(
            room.RoomId,
            importedSubRooms.Count,
            0,
            0,
            0,
            playableSubRoom.Name,
            playableSubRoom.SubRoomId);
    }

    public sealed record RoomExportResult(
        long RoomId,
        string ExportRoot,
        int SubRoomsExported,
        int SavesExported,
        bool ImageExported,
        int BakedAssetsExported,
        int AssetBundlesExported);

    public static RoomExportResult ExportRoomToDirectory(
        long roomId,
        string outputRoot)
    {
        RoomDBClasses.Room room = RoomDB.GetRoom(roomId);
        if (room == null || room.RoomId <= 0)
            throw new FileNotFoundException($"Room {roomId} was not found.");

        Directory.CreateDirectory(outputRoot);

        string roomBlobSource = Path.Combine(Program.dataDir, "CDN", "room");
        string assetBundleSourceRoot = Path.Combine(Program.dataDir, "CDN", "assetbundles");
        string imageSourceRoot = Path.Combine(Program.dataDir, "Images");
        string assetBundleDestination = Path.Combine(outputRoot, "AssetBundles");

        var exportSubRooms = new List<ExportSubRoom>();
        int savesExported = 0;
        int bakedAssetsExported = 0;
        var exportedBundleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (SubRooms subRoom in room.SubRooms ?? new List<SubRooms>())
        {
            string subRoomDirectory = Path.Combine(outputRoot, subRoom.SubRoomId.ToString());
            string savesDirectory = Path.Combine(subRoomDirectory, "Saves");
            Directory.CreateDirectory(savesDirectory);

            List<SubRoomDataSave> saves = RoomDB.SubRoomDataSaves
                .Find(save => save.RoomId == roomId && save.SubRoomId == subRoom.SubRoomId)
                .OrderBy(save => save.CreatedAt)
                .ToList();

            foreach (SubRoomDataSave save in saves)
            {
                string safeBlobName = Path.GetFileName(save.DataBlob ?? string.Empty);
                string sourceBlobPath = Path.Combine(roomBlobSource, safeBlobName);
                if (string.IsNullOrWhiteSpace(safeBlobName) || !File.Exists(sourceBlobPath))
                {
                    Console.WriteLine(
                        $"[ROOM EXPORT] Missing blob for save {save.SubRoomDataSaveId}; skipped.");
                    continue;
                }

                string saveDirectoryName = save.SubRoomDataSaveId.ToString();
                string saveDirectory = Path.Combine(savesDirectory, saveDirectoryName);
                Directory.CreateDirectory(saveDirectory);

                File.Copy(
                    sourceBlobPath,
                    Path.Combine(saveDirectory, safeBlobName),
                    overwrite: true);

                var exportSave = new ExportSave
                {
                    SubRoomDataSaveId = save.SubRoomDataSaveId,
                    SubRoomId = save.SubRoomId,
                    UnityAssetId = save.UnityAssetId,
                    DataBlob = safeBlobName,
                    DataBlobHash = save.DataBlobHash,
                    ReferencedUnityAssetIds = save.ReferencedUnityAssetIds,
                    PersistenceVersion = save.PersistenceVersion.GetValueOrDefault(),
                    OMVersion = save.OMVersion,
                    UgcSubVersion = save.UgcSubVersion,
                    SavedOnPlatform = save.SavedOnPlatform,
                    SavedOnDeviceClass = save.SavedOnDeviceClass,
                    Description = save.Description,
                    Tags = save.Tags,
                    ModerationState = save.ModerationState,
                    CreatedAt = save.CreatedAt
                };
                WriteJson(Path.Combine(saveDirectory, saveDirectoryName + ".json"), exportSave);

                int bakedIndex = 0;
                foreach (BakedUnityAsset bakedAsset in save.BakedUnityAssets ?? new List<BakedUnityAsset>())
                {
                    string versionSuffix = string.IsNullOrWhiteSpace(bakedAsset.UnityVersion)
                        ? string.Empty
                        : $"-Unity_{bakedAsset.UnityVersion.Replace('.', '_')}";
                    string bakedJsonName = $"bakedUnityAsset-{bakedIndex++}{versionSuffix}.json";
                    WriteJson(Path.Combine(saveDirectory, bakedJsonName), bakedAsset);
                    bakedAssetsExported++;

                    string bundleFileName = Path.GetFileName(bakedAsset.Filename ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(bundleFileName))
                        continue;

                    string bundleSourcePath = Path.Combine(assetBundleSourceRoot, bundleFileName);
                    if (!File.Exists(bundleSourcePath))
                        continue;

                    if (exportedBundleNames.Add(bundleFileName))
                    {
                        Directory.CreateDirectory(assetBundleDestination);
                        File.Copy(
                            bundleSourcePath,
                            Path.Combine(assetBundleDestination, bundleFileName),
                            overwrite: true);
                    }
                }

                savesExported++;
            }

            var exportSubRoom = new ExportSubRoom
            {
                RoomId = roomId,
                SubRoomId = subRoom.SubRoomId,
                Name = subRoom.Name,
                UnitySceneId = subRoom.UnitySceneId,
                IsSandbox = subRoom.IsSandbox,
                MaxPlayers = subRoom.MaxPlayers,
                Accessibility = (int)subRoom.Accessibility,
                CurrentSave = new ExportSave { SubRoomDataSaveId = subRoom.SubRoomDataSaveId }
            };
            WriteJson(Path.Combine(subRoomDirectory, "subRoom.json"), exportSubRoom);
            exportSubRooms.Add(exportSubRoom);
        }

        if (savesExported == 0)
            throw new InvalidDataException($"Room {roomId} has no save blobs on disk to export.");

        bool imageExported = false;
        if (!string.IsNullOrWhiteSpace(room.ImageName))
        {
            string imageSourcePath = Path.Combine(imageSourceRoot, room.ImageName);
            if (File.Exists(imageSourcePath))
            {
                File.Copy(
                    imageSourcePath,
                    Path.Combine(outputRoot, Path.GetFileName(room.ImageName)),
                    overwrite: true);
                imageExported = true;
            }
        }

        var exportRoom = new ExportRoom
        {
            RoomId = room.RoomId,
            Name = room.Name,
            Description = room.Description,
            ImageName = room.ImageName,
            MaxPlayers = room.MaxPlayers,
            Accessibility = (int)room.Accessibility,
            PersistenceVersion = room.PersistenceVersion,
            SubRoomId = room.SubRooms?.FirstOrDefault()?.SubRoomId ?? 0,
            UnitySceneId = room.SubRooms?.FirstOrDefault()?.UnitySceneId,
            SubRooms = exportSubRooms
        };
        WriteJson(Path.Combine(outputRoot, "room.json"), exportRoom);

        return new RoomExportResult(
            room.RoomId,
            outputRoot,
            exportSubRooms.Count,
            savesExported,
            imageExported,
            bakedAssetsExported,
            exportedBundleNames.Count);
    }

    private static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }

    private static void MergeSubRoomMetadata(ExportSubRoom listed, ExportSubRoom source)
    {
        listed.Name = string.IsNullOrWhiteSpace(source.Name) ? listed.Name : source.Name;
        listed.UnitySceneId = string.IsNullOrWhiteSpace(source.UnitySceneId)
            ? listed.UnitySceneId
            : source.UnitySceneId;
        listed.MaxPlayers = source.MaxPlayers > 0 ? source.MaxPlayers : listed.MaxPlayers;
        listed.Accessibility = source.Accessibility;
        listed.IsSandbox = source.IsSandbox;
        listed.CurrentSave = source.CurrentSave ?? listed.CurrentSave;
    }

    private static bool CopyRoomImage(
        string exportRoot,
        string? requestedImageName,
        string destinationRoot,
        out string imageName)
    {
        string requestedFileName = Path.GetFileName(requestedImageName ?? string.Empty);
        imageName = requestedFileName;
        string? sourcePath = null;

        if (!string.IsNullOrWhiteSpace(requestedFileName))
        {
            sourcePath = Directory.EnumerateFiles(exportRoot, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => string.Equals(
                    Path.GetFileName(path),
                    requestedFileName,
                    StringComparison.OrdinalIgnoreCase));
        }

        sourcePath ??= Directory.EnumerateFiles(exportRoot, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => IsImageExtension(Path.GetExtension(path)));

        if (sourcePath == null)
        {
            imageName = string.Empty;
            return false;
        }

        imageName = Path.GetFileName(sourcePath);
        string destinationPath = Path.Combine(destinationRoot, imageName);
        return CopyIfDifferent(sourcePath, destinationPath) || File.Exists(destinationPath);
    }

    private static bool IsImageExtension(string extension) =>
        extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".gif", StringComparison.OrdinalIgnoreCase);

    private static string? FindDirectory(string root, string name)
    {
        string direct = Path.Combine(root, name);
        if (Directory.Exists(direct))
            return direct;

        return Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => string.Equals(
                Path.GetFileName(path),
                name,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool CopyIfDifferent(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (File.Exists(destinationPath))
        {
            var sourceInfo = new FileInfo(sourcePath);
            var destinationInfo = new FileInfo(destinationPath);
            if (sourceInfo.Length == destinationInfo.Length &&
                string.Equals(
                    ComputeSha256Base64(sourcePath),
                    ComputeSha256Base64(destinationPath),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
        return true;
    }

    private static string? ParseUnityVersionFromMetadataName(string fileName)
    {

        const string marker = "Unity_";
        int markerIndex = fileName.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return null;

        int start = markerIndex + marker.Length;
        int end = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? fileName.Length - 5
            : fileName.Length;
        if (start >= end)
            return null;

        return fileName[start..end].Replace('_', '.');
    }

    private static int NormalizePersistenceVersion(int sourceVersion)
    {
        if (sourceVersion <= 0)
            return CompatiblePersistenceVersion;

        return Math.Min(sourceVersion, CompatiblePersistenceVersion);
    }

    private static int NormalizeUgcSubVersion(int sourceVersion)
    {
        return CompatibleUgcSubVersion;
    }

    private static DateTime NormalizeCreatedAt(DateTime createdAt)
    {
        if (createdAt == default)
            return DateTime.UtcNow;
        return createdAt.Kind == DateTimeKind.Utc
            ? createdAt
            : createdAt.ToUniversalTime();
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
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

    private static T ReadJson<T>(string path)
    {
        string json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidDataException($"Could not deserialize {path}.");
    }

    private static string ComputeSha256Base64(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToBase64String(SHA256.HashData(stream));
    }

    private sealed class ExportRoom
    {
        public long RoomId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImageName { get; set; }
        public int MaxPlayers { get; set; }
        public int Accessibility { get; set; }
        public int PersistenceVersion { get; set; }
        public long SubRoomId { get; set; }
        public string? UnitySceneId { get; set; }
        public List<ExportSubRoom>? SubRooms { get; set; }
    }

    private sealed class ExportSubRoom
    {
        public long RoomId { get; set; }
        public long SubRoomId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? UnitySceneId { get; set; }
        public ExportSave? CurrentSave { get; set; }
        public bool IsSandbox { get; set; }
        public int MaxPlayers { get; set; }
        public int Accessibility { get; set; }
    }

    private sealed class ExportSave
    {
        public long SubRoomDataSaveId { get; set; }
        public long SubRoomId { get; set; }
        public string? UnityAssetId { get; set; }
        public string DataBlob { get; set; } = string.Empty;
        public string? DataBlobHash { get; set; }
        public List<string>? ReferencedUnityAssetIds { get; set; }
        public int PersistenceVersion { get; set; }
        public int OMVersion { get; set; }
        public int UgcSubVersion { get; set; }
        public int? SavedOnPlatform { get; set; }
        public int? SavedOnDeviceClass { get; set; }
        public string? Description { get; set; }
        public List<string>? Tags { get; set; }
        public int ModerationState { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
