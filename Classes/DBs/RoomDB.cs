using System;
using System.Linq;
using LiteDB;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs.DBClasses;
using static Mocha2023.Classes.DBs.DBClasses.RoomDBClasses;
using SysJson = System.Text.Json;

namespace Mocha2023.Classes.DBs
{
    public class RoomDB
    {
        public const int CompatibleLegacyPersistenceVersion = 38;
        public const int CompatibleLegacyUgcSubVersion = 0;
        public const int CompatibleLegacyOMVersion = 0;

        public static int NormalizeLegacyPersistenceVersion(int? sourceVersion)
        {
            int value = sourceVersion.GetValueOrDefault(CompatibleLegacyPersistenceVersion);
            return value <= 0
                ? CompatibleLegacyPersistenceVersion
                : Math.Min(value, CompatibleLegacyPersistenceVersion);
        }

        public static int NormalizeLegacyUgcSubVersion(int sourceVersion)
        {
            return CompatibleLegacyUgcSubVersion;
        }

        public static int NormalizeLegacyOMVersion(int sourceVersion)
        {
            return CompatibleLegacyOMVersion;
        }

        public static LiteDatabase RoomDBFile = new LiteDatabase(Path.Combine(Program.dataDir, "DBs", "Rooms.db"));
        public static readonly ILiteCollection<Room> Rooms = RoomDBFile.GetCollection<Room>("Rooms");
        public static readonly ILiteCollection<SubRoomDataSave> SubRoomDataSaves = RoomDBFile.GetCollection<SubRoomDataSave>("SubRoomDataSaves");
        public static readonly ILiteCollection<RoomBan> RoomBans = RoomDBFile.GetCollection<RoomBan>("RoomBans");
        private static readonly object SaveIdLock = new();
        private static readonly object DormCreationLock = new();
        private static readonly object SubRoomMutationLock = new();
        private static readonly string[] CanonicalBaseRoomNames =
        {
            "PerformanceHall",
            "MakerRoom",
            "Park",
            "Lounge",
            "RecCenter"
        };

        static RoomDB()
        {
            LiteDbMaintenance.StartPeriodicCheckpoint("Rooms.db", RoomDBFile);

            RepairCanonicalBaseRoomMetadata();
        }

        private static string NormalizeCanonicalRoomName(string? name) =>
            new((name ?? string.Empty)
                .Trim()
                .TrimStart('^')
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

        public static bool IsCanonicalBaseRoom(Room? room)
        {
            if (room == null)
                return false;
            if (room.IsBaseRoom)
                return true;

            string normalized = NormalizeCanonicalRoomName(room.Name);
            return CanonicalBaseRoomNames.Any(name =>
                NormalizeCanonicalRoomName(name) == normalized);
        }

        public static int GetCanonicalBaseRoomOrder(Room room)
        {
            string normalized = NormalizeCanonicalRoomName(room.Name);
            int index = Array.FindIndex(
                CanonicalBaseRoomNames,
                name => NormalizeCanonicalRoomName(name) == normalized);
            return index < 0 ? int.MaxValue : index;
        }

        public static int RepairCanonicalBaseRoomMetadata()
        {
            int changed = 0;
            foreach (Room room in Rooms.FindAll().Where(IsCanonicalBaseRoom).ToList())
            {
                bool dirty = false;
                if (!room.IsBaseRoom)
                {
                    room.IsBaseRoom = true;
                    dirty = true;
                }
                if (room.IsDorm)
                {
                    room.IsDorm = false;
                    dirty = true;
                }
                if (!room.IsRRO)
                {
                    room.IsRRO = true;
                    dirty = true;
                }
                if (room.Accessibility != RoomAccessibility.Public)
                {
                    room.Accessibility = RoomAccessibility.Public;
                    dirty = true;
                }
                if (room.State != RoomState.Active)
                {
                    room.State = RoomState.Active;
                    dirty = true;
                }

                room.Tags ??= new List<Tags>();
                if (!room.Tags.Any(tag => string.Equals(
                        tag.Tag, "base", StringComparison.OrdinalIgnoreCase)))
                {
                    room.Tags.Add(new Tags { Tag = "base", Type = TagType.Auto });
                    dirty = true;
                }
                if (!room.Tags.Any(tag => string.Equals(
                        tag.Tag, "rro", StringComparison.OrdinalIgnoreCase)))
                {
                    room.Tags.Add(new Tags { Tag = "rro", Type = TagType.AGOnly });
                    dirty = true;
                }

                if (dirty && Rooms.Update(room))
                    changed++;
            }
            return changed;
        }

        public static bool CanPlayerAccessRoom(Room? room, long accountId)
        {
            if (room == null || accountId <= 0 ||
                room.State == RoomState.MarkedForDelete ||
                room.State == RoomState.Moderation_Closed)
            {
                return false;
            }

            if (IsCanonicalBaseRoom(room))
                return true;

            if (room.IsDorm)
                return room.CreatorAccountId == accountId || IsConfirmedLiveGuest(room, accountId);

            if (room.Accessibility != RoomAccessibility.Private)
                return true;

            return IsRoomCollaborator(room, accountId) || IsConfirmedLiveGuest(room, accountId);
        }

        private static bool IsConfirmedLiveGuest(Room room, long accountId)
        {
            PlayerDBClasses.Heartbeat heartbeat = PlayerDB.GetPlayerHeartbeat(accountId);
            PlayerDBClasses.RoomInstance? instance = heartbeat.roomInstance;

            if (!heartbeat.isOnline || instance == null || instance.roomId != room.RoomId)
                return false;

            return Sessions.IsConfirmedParticipant(accountId, instance.roomInstanceId);
        }

        public static bool CanPlayerAccessSubRoom(
            Room? room,
            SubRooms? subRoom,
            long accountId)
        {
            if (!CanPlayerAccessRoom(room, accountId) ||
                room == null ||
                subRoom == null)
            {
                return false;
            }

            if (IsCanonicalBaseRoom(room))
                return true;

            return subRoom.Accessibility != RoomAccessibility.Private ||
                   IsRoomCollaborator(room, accountId) ||
                   IsConfirmedLiveGuest(room, accountId);
        }

        private static bool IsRoomCollaborator(Room room, long accountId)
        {
            return room.CreatorAccountId == accountId ||
                   room.Roles?.Any(role =>
                       role.AccountId == accountId &&
                       role.Role is Role.Host or
                           Role.Moderator or
                           Role.CoOwner or
                           Role.TemporaryCoOwner or
                           Role.Creator) == true;
        }

        public static RoomBan BanPlayerFromRoom(long roomId, long accountId, long bannedByAccountId, string? reason)
        {
            var existing = RoomBans.FindOne(b => b.RoomId == roomId && b.AccountId == accountId);
            if (existing != null)
                return existing;

            var ban = new RoomBan
            {
                RoomId = roomId,
                AccountId = accountId,
                BannedByAccountId = bannedByAccountId,
                Reason = reason,
                BannedAt = DateTime.UtcNow
            };

            RoomBans.Insert(ban);
            Console.WriteLine($"[ROOM BAN] room={roomId} account={accountId} bannedBy={bannedByAccountId}");
            return ban;
        }

        public static bool UnbanPlayerFromRoom(long roomId, long accountId)
        {
            int removed = RoomBans.DeleteMany(b => b.RoomId == roomId && b.AccountId == accountId);
            if (removed > 0)
                Console.WriteLine($"[ROOM UNBAN] room={roomId} account={accountId}");

            return removed > 0;
        }

        public static bool IsPlayerBannedFromRoom(long roomId, long accountId)
        {
            return RoomBans.Exists(b => b.RoomId == roomId && b.AccountId == accountId);
        }

        public static List<RoomBan> GetActiveBans(long roomId)
        {
            return RoomBans.Find(b => b.RoomId == roomId).ToList();
        }

        public static SubRoomDataSave CreateSubRoomDataSave(long roomId, long subRoomId, long accountId, string dataBlob, int persistenceVersion, bool isPublished, string? dataBlobHash = null, string? description = null, string? roomDataBlob = null)
        {
            lock (SaveIdLock)
            {
                return InsertSubRoomDataSave(
                    roomId,
                    subRoomId,
                    accountId,
                    dataBlob,
                    persistenceVersion,
                    isPublished,
                    dataBlobHash,
                    description,
                    roomDataBlob);
            }
        }

        public static SubRoomDataSave GetOrCreateSubRoomDataSave(
            long roomId,
            long subRoomId,
            long accountId,
            string dataBlob,
            int persistenceVersion,
            bool isPublished,
            string? dataBlobHash,
            string? description,
            string? roomDataBlob,
            out bool created)
        {
            lock (SaveIdLock)
            {

                var existing = SubRoomDataSaves
                    .Find(save =>
                        save.RoomId == roomId &&
                        save.SubRoomId == subRoomId)
                    .OrderByDescending(save => save.CreatedAt)
                    .ThenByDescending(save => save.SubRoomDataSaveId)
                    .FirstOrDefault(save =>
                        save.SavedByAccountId == accountId &&
                        string.Equals(
                            save.DataBlob,
                            dataBlob,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            save.RoomDataBlob,
                            roomDataBlob,
                            StringComparison.OrdinalIgnoreCase) &&
                        save.PersistenceVersion == persistenceVersion &&
                        string.Equals(
                            save.Description ?? string.Empty,
                            description ?? string.Empty,
                            StringComparison.Ordinal));

                if (existing != null)
                {
                    bool changed = false;
                    if (string.IsNullOrWhiteSpace(existing.DataBlobHash) &&
                        !string.IsNullOrWhiteSpace(dataBlobHash))
                    {
                        existing.DataBlobHash = dataBlobHash;
                        changed = true;
                    }

                    if (isPublished && !existing.IsPublished)
                    {
                        existing.IsPublished = true;
                        changed = true;
                    }

                    if (changed && !SubRoomDataSaves.Update(existing))
                    {
                        throw new InvalidOperationException(
                            $"Failed to update existing subroom save {existing.SubRoomDataSaveId}.");
                    }

                    created = false;
                    return existing;
                }

                created = true;
                return InsertSubRoomDataSave(
                    roomId,
                    subRoomId,
                    accountId,
                    dataBlob,
                    persistenceVersion,
                    isPublished,
                    dataBlobHash,
                    description,
                    roomDataBlob);
            }
        }

        private static SubRoomDataSave InsertSubRoomDataSave(
            long roomId,
            long subRoomId,
            long accountId,
            string dataBlob,
            int persistenceVersion,
            bool isPublished,
            string? dataBlobHash,
            string? description,
            string? roomDataBlob)
        {
            long nextId = SubRoomDataSaves.Count() == 0
                ? 1
                : Convert.ToInt64(SubRoomDataSaves.Max(x => x.SubRoomDataSaveId)) + 1;

            var save = new SubRoomDataSave
            {
                SubRoomDataSaveId = nextId,
                RoomId = roomId,
                SubRoomId = subRoomId,
                SavedByAccountId = accountId,
                DataBlob = dataBlob,
                RoomDataBlob = roomDataBlob,
                DataBlobHash = dataBlobHash,
                PersistenceVersion = persistenceVersion,
                Description = description,
                CreatedAt = DateTime.UtcNow,
                IsPublished = isPublished
            };
            SubRoomDataSaves.Insert(save);
            return save;
        }

        public static SubRoomDataSave? EnsureSubRoomDataSave(Room room, SubRooms subRoom)
        {
            ArgumentNullException.ThrowIfNull(room);
            ArgumentNullException.ThrowIfNull(subRoom);

            if (subRoom.SubRoomDataSaveId > 0)
            {
                var current = SubRoomDataSaves.FindById(subRoom.SubRoomDataSaveId);
                if (IsUsablePersistencePair(room, subRoom, current))
                {
                    NormalizeSubRoomDataSaveHash(current!);
                    SynchronizePersistencePointers(room, subRoom, current!);
                    return current;
                }

                Console.WriteLine(
                    $"[CV2 SNAPSHOT GUARD] rejected current save room={room.RoomId} " +
                    $"subroom={subRoom.SubRoomId} save={subRoom.SubRoomDataSaveId}");
            }

            var newestValidSave = SubRoomDataSaves
                .Find(save => save.RoomId == room.RoomId && save.SubRoomId == subRoom.SubRoomId)
                .OrderByDescending(save => save.CreatedAt)
                .ThenByDescending(save => save.SubRoomDataSaveId)
                .FirstOrDefault(save => IsUsablePersistencePair(room, subRoom, save));

            if (newestValidSave != null)
            {
                NormalizeSubRoomDataSaveHash(newestValidSave);
                SynchronizePersistencePointers(room, subRoom, newestValidSave);
                Console.WriteLine(
                    $"[CV2 SNAPSHOT REPAIR] room={room.RoomId} subroom={subRoom.SubRoomId} " +
                    $"save={newestValidSave.SubRoomDataSaveId}");
                return newestValidSave;
            }

            if (HasCompleteDirectBlobPair(room, subRoom))
            {
                var save = CreateSubRoomDataSave(
                    room.RoomId,
                    subRoom.SubRoomId,
                    subRoom.SavedByAccountId,
                    subRoom.DataBlob!,
                    room.PersistenceVersion,
                    false,
                    roomDataBlob: room.DataBlob);

                NormalizeSubRoomDataSaveHash(save);
                SynchronizePersistencePointers(room, subRoom, save);
                Console.WriteLine(
                    $"[CV2 SNAPSHOT REPAIR] synthesized save room={room.RoomId} " +
                    $"subroom={subRoom.SubRoomId} save={save.SubRoomDataSaveId}");
                return save;
            }

            bool clearedInvalidSavePointer = subRoom.SubRoomDataSaveId != 0;
            subRoom.SubRoomDataSave = null;
            subRoom.SubRoomDataSaveId = 0;
            if (clearedInvalidSavePointer)
                Rooms.Update(room);

            Console.WriteLine(
                $"[CV2 SNAPSHOT GUARD] fresh scene room={room.RoomId} " +
                $"subroom={subRoom.SubRoomId} roomBlob={room.DataBlob ?? "null"} " +
                $"subRoomBlob={subRoom.DataBlob ?? "null"}");
            return null;
        }

        public static bool TryGetPersistencePair(
            Room room,
            SubRooms subRoom,
            out SubRoomDataSave? save,
            out string roomDataBlob,
            out string subRoomDataBlob)
        {
            save = EnsureSubRoomDataSave(room, subRoom);
            if (!IsUsablePersistencePair(room, subRoom, save))
            {
                roomDataBlob = string.Empty;
                subRoomDataBlob = string.Empty;
                return false;
            }

            roomDataBlob = string.IsNullOrWhiteSpace(save!.RoomDataBlob)
                ? string.Empty
                : save.RoomDataBlob;
            subRoomDataBlob = save.DataBlob;
            return true;
        }

        private static bool IsBakedUnitySave(SubRoomDataSave? save) =>
            save != null && !string.IsNullOrWhiteSpace(save.UnityAssetId);

        private static bool IsUsablePersistencePair(
            Room room,
            SubRooms subRoom,
            SubRoomDataSave? save)
        {
            if (save == null ||
                save.RoomId != room.RoomId ||
                save.SubRoomId != subRoom.SubRoomId ||
                string.IsNullOrWhiteSpace(save.DataBlob))
            {
                return false;
            }

            if (IsBakedUnitySave(save))
            {
                string bakedRoomBlob = string.IsNullOrWhiteSpace(save.RoomDataBlob)
                    ? save.DataBlob
                    : save.RoomDataBlob;
                return RoomBlobExists(save.DataBlob) && RoomBlobExists(bakedRoomBlob);
            }

            if (string.IsNullOrWhiteSpace(save.RoomDataBlob))
            {

                return RoomBlobExists(save.DataBlob);
            }

            if (string.Equals(
                    save.RoomDataBlob,
                    save.DataBlob,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return RoomBlobExists(save.RoomDataBlob) && RoomBlobExists(save.DataBlob);
        }

        private static bool HasCompleteDirectBlobPair(Room room, SubRooms subRoom)
        {
            return !string.IsNullOrWhiteSpace(room.DataBlob) &&
                !string.IsNullOrWhiteSpace(subRoom.DataBlob) &&
                !string.Equals(
                    room.DataBlob,
                    subRoom.DataBlob,
                    StringComparison.OrdinalIgnoreCase) &&
                RoomBlobExists(room.DataBlob) &&
                RoomBlobExists(subRoom.DataBlob);
        }

        private static void SynchronizePersistencePointers(
            Room room,
            SubRooms subRoom,
            SubRoomDataSave save)
        {
            bool changed = false;

            string? effectiveRoomBlob;
            if (IsBakedUnitySave(save))
            {
                effectiveRoomBlob = string.IsNullOrWhiteSpace(save.RoomDataBlob)
                    ? save.DataBlob
                    : save.RoomDataBlob;
            }
            else
            {
                effectiveRoomBlob = string.IsNullOrWhiteSpace(save.RoomDataBlob)
                    ? null
                    : save.RoomDataBlob;
            }

            if (!string.Equals(room.DataBlob, effectiveRoomBlob, StringComparison.Ordinal))
            {
                room.DataBlob = effectiveRoomBlob;
                changed = true;
            }

            if (!string.Equals(subRoom.DataBlob, save.DataBlob, StringComparison.Ordinal))
            {
                subRoom.DataBlob = save.DataBlob;
                changed = true;
            }

            if (subRoom.SubRoomDataSaveId != save.SubRoomDataSaveId)
            {
                subRoom.SubRoomDataSaveId = save.SubRoomDataSaveId;
                changed = true;
            }

            subRoom.SubRoomDataSave = save;
            if (changed)
                Rooms.Update(room);
        }

        private static void NormalizeSubRoomDataSaveHash(SubRoomDataSave save)
        {
            string? actualHash = ComputeBlobHashBase64(save.DataBlob);
            if (actualHash == null ||
                string.Equals(save.DataBlobHash, actualHash, StringComparison.Ordinal))
            {
                return;
            }

            string? oldHash = save.DataBlobHash;
            save.DataBlobHash = actualHash;
            SubRoomDataSaves.Update(save);

            Console.WriteLine(
                $"[ROOM HASH REPAIR] save={save.SubRoomDataSaveId} " +
                $"blob={save.DataBlob} old={oldHash ?? "null"} new={actualHash}");
        }

        private static string? ComputeBlobHashBase64(string? filename)
        {
            if (!RoomBlobExists(filename))
                return null;

            string path = Path.Combine(
                Program.dataDir,
                "CDN",
                "room",
                Path.GetFileName(filename));

            using var stream = File.OpenRead(path);
            byte[] digest = System.Security.Cryptography.SHA256.HashData(stream);
            return Convert.ToBase64String(digest);
        }

        public static Room? PrepareRoomForClient(Room? room)
        {
            if (room == null)
                return null;

            room.PersistenceVersion = NormalizeLegacyPersistenceVersion(
                room.PersistenceVersion);
            room.UgcVersion = 1;

            var normalizedFields = new List<string>();

            if (string.IsNullOrWhiteSpace(room.Name))
            {
                room.Name = $"Room{room.RoomId}";
                normalizedFields.Add(nameof(room.Name));
            }

            if (room.Description == null)
            {
                room.Description = string.Equals(
                    room.Name,
                    "RecCenter",
                    StringComparison.OrdinalIgnoreCase)
                    ? "A social hub to meet and mingle with friends new and old!"
                    : "No description has been added yet.";
                normalizedFields.Add(nameof(room.Description));
            }

            if (string.IsNullOrWhiteSpace(room.ImageName))
            {
                room.ImageName = "DefaultRoomImage.jpg";
                normalizedFields.Add(nameof(room.ImageName));
            }

            if (string.IsNullOrWhiteSpace(room.RankedEntityId))
            {
                room.RankedEntityId = room.RoomId.ToString();
                normalizedFields.Add(nameof(room.RankedEntityId));
            }

            room.Stats ??= new Stats();
            room.SubRooms ??= new List<SubRooms>();
            room.Roles ??= new List<Roles>();
            room.Tags ??= new List<Tags>();
            room.PromoImages ??= new List<string>();
            room.PromoExternalContent ??= new List<PromoExternalContent>();
            room.LoadScreens ??= new List<LoadScreens>();

            if (IsCanonicalBaseRoom(room))
            {
                room.IsBaseRoom = true;
                room.IsDorm = false;
                room.IsRRO = true;
                room.Accessibility = RoomAccessibility.Public;
                room.State = RoomState.Active;
                if (!room.Tags.Any(tag => string.Equals(
                        tag.Tag, "base", StringComparison.OrdinalIgnoreCase)))
                {
                    room.Tags.Add(new Tags { Tag = "base", Type = TagType.Auto });
                }
                if (!room.Tags.Any(tag => string.Equals(
                        tag.Tag, "rro", StringComparison.OrdinalIgnoreCase)))
                {
                    room.Tags.Add(new Tags { Tag = "rro", Type = TagType.AGOnly });
                }
            }

            bool hasBetaTag = room.Tags.Any(tag => string.Equals(
                tag.Tag, "beta", StringComparison.OrdinalIgnoreCase));
            if (room.CreativeToolsBetaEnabled && !hasBetaTag)
            {
                room.Tags.Add(new Tags { Tag = "beta", Type = TagType.Auto });
            }
            else if (!room.CreativeToolsBetaEnabled && hasBetaTag)
            {

                room.CreativeToolsBetaEnabled = true;
            }

            ResetInvalidPersonalDormToFreshState(room);

            foreach (var subRoom in room.SubRooms)
            {
                subRoom.Name ??= string.Empty;
                subRoom.UnitySceneId ??= string.Empty;
                subRoom.Permissions ??= new List<SubRoomPermission>();
                subRoom.SubRoomDataSave = EnsureSubRoomDataSave(room, subRoom);

                if (subRoom.SubRoomDataSave != null)
                {
                    subRoom.SubRoomDataSave.DataBlob ??= string.Empty;
                    subRoom.SubRoomDataSave.Description ??= room.Description;
                    subRoom.SubRoomDataSave.PersistenceVersion =
                        NormalizeLegacyPersistenceVersion(
                            subRoom.SubRoomDataSave.PersistenceVersion);
                    subRoom.SubRoomDataSave.UgcSubVersion =
                        NormalizeLegacyUgcSubVersion(
                            subRoom.SubRoomDataSave.UgcSubVersion);
                    subRoom.SubRoomDataSave.OMVersion =
                        NormalizeLegacyOMVersion(
                            subRoom.SubRoomDataSave.OMVersion);
                }
            }

            foreach (var tag in room.Tags)
                tag.Tag ??= string.Empty;

            foreach (var externalContent in room.PromoExternalContent)
                externalContent.Reference ??= string.Empty;

            foreach (var loadScreen in room.LoadScreens)
            {
                loadScreen.ImageName ??= string.Empty;
                loadScreen.Title ??= string.Empty;
                loadScreen.Subtitle ??= string.Empty;
            }

            if (normalizedFields.Count > 0)
            {
                Console.WriteLine(
                    $"[ROOM DTO NORMALIZE] room={room.RoomId} name={room.Name} " +
                    $"fields={string.Join(',', normalizedFields)}");
            }

            return room;
        }

        private static void ResetInvalidPersonalDormToFreshState(Room room)
        {
            if (!room.IsDorm || room.RoomId == 1 || room.CreatorAccountId <= 0 ||
                room.SubRooms == null || room.SubRooms.Count == 0)
            {
                return;
            }

            bool roomBlobSet = !string.IsNullOrWhiteSpace(room.DataBlob);
            bool anySubRoomBlobSet = room.SubRooms.Any(subRoom =>
                !string.IsNullOrWhiteSpace(subRoom.DataBlob));
            bool hasMissingFile =
                (roomBlobSet && !RoomBlobExists(room.DataBlob)) ||
                room.SubRooms.Any(subRoom =>
                    !string.IsNullOrWhiteSpace(subRoom.DataBlob) &&
                    !RoomBlobExists(subRoom.DataBlob));
            bool hasAliasedPayload = roomBlobSet && room.SubRooms.Any(subRoom =>
                string.Equals(room.DataBlob, subRoom.DataBlob, StringComparison.OrdinalIgnoreCase));
            bool hasIncompletePair = roomBlobSet != anySubRoomBlobSet;

            if (!hasMissingFile && !hasAliasedPayload && !hasIncompletePair)
                return;

            room.DataBlob = null;
            room.PersistenceVersion = 0;
            foreach (var subRoom in room.SubRooms)
            {
                subRoom.DataBlob = null;
                subRoom.SubRoomDataSaveId = 0;
                subRoom.SubRoomDataSave = null;
            }

            SubRoomDataSaves.DeleteMany(save => save.RoomId == room.RoomId);
            Rooms.Update(room);
            Console.WriteLine(
                $"[DORM FRESH] account={room.CreatorAccountId} room={room.RoomId} " +
                "discarded invalid persistence data");
        }

        private static bool RoomBlobExists(string? filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                return false;

            string safeName = Path.GetFileName(filename);
            return string.Equals(safeName, filename, StringComparison.Ordinal) &&
                File.Exists(Path.Combine(Program.dataDir, "CDN", "room", safeName));
        }

        public static List<Room> PrepareRoomsForClient(IEnumerable<Room> rooms)
        {
            var prepared = rooms.ToList();
            foreach (var room in prepared)
                PrepareRoomForClient(room);

            return prepared;
        }

        public static async Task ImportRooms(string path)
        {
            try
            {
                string jsonData = await File.ReadAllTextAsync(path);

                var options = new SysJson.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = SysJson.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                var roomsList = SysJson.JsonSerializer.Deserialize<List<Room>>(jsonData, options);

                if (roomsList == null) return;

                foreach (var room in roomsList)
                {
                    if (room == null) continue;
                    if (Rooms.FindById(room.RoomId) != null) continue;

                    await AddRoom(room, log: true, shouldAssignNewIds: false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Import Error] Failed to import JSON: {ex.Message}");
            }
        }

        public static async Task<bool> AddRoom(Room newRoom, bool log = false, bool shouldAssignNewIds = true)
        {
            if (newRoom == null)
                return false;

            return await Task.Run(() =>
            {
                try
                {
                    if (shouldAssignNewIds || newRoom.RoomId <= 0)
                        newRoom.RoomId = GetNextRoomId();

                    newRoom.RankedEntityId = newRoom.RoomId.ToString();
                    newRoom.CreatedAt = newRoom.CreatedAt == default
                        ? DateTime.UtcNow
                        : newRoom.CreatedAt;

                    newRoom.Stats ??= new Stats();
                    newRoom.SubRooms ??= new List<SubRooms>();
                    newRoom.Roles ??= new List<Roles>();
                    newRoom.Tags ??= new List<Tags>();
                    newRoom.PromoImages ??= new List<string>();
                    newRoom.PromoExternalContent ??= new List<PromoExternalContent>();
                    newRoom.LoadScreens ??= new List<LoadScreens>();

                    if (IsCanonicalBaseRoom(newRoom))
                    {
                        newRoom.IsBaseRoom = true;
                        newRoom.IsDorm = false;
                        newRoom.IsRRO = true;
                        newRoom.Accessibility = RoomAccessibility.Public;
                        newRoom.State = RoomState.Active;
                        if (!newRoom.Tags.Any(tag => string.Equals(
                                tag.Tag, "base", StringComparison.OrdinalIgnoreCase)))
                        {
                            newRoom.Tags.Add(new Tags { Tag = "base", Type = TagType.Auto });
                        }
                        if (!newRoom.Tags.Any(tag => string.Equals(
                                tag.Tag, "rro", StringComparison.OrdinalIgnoreCase)))
                        {
                            newRoom.Tags.Add(new Tags { Tag = "rro", Type = TagType.AGOnly });
                        }
                    }

                    long nextSubRoomId = GetNextSubRoomId();

                    foreach (var sub in newRoom.SubRooms)
                    {
                        if (shouldAssignNewIds || sub.SubRoomId <= 0)
                            sub.SubRoomId = nextSubRoomId++;

                        sub.RoomId = newRoom.RoomId;
                        sub.Name ??= "Home";
                        sub.UnitySceneId ??= string.Empty;
                        sub.Permissions ??= new List<SubRoomPermission>();
                    }

                    Rooms.Insert(newRoom);

                    if (log)
                        Console.WriteLine($"[DB] Added room: {newRoom.Name} (ID: {newRoom.RoomId})");

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB Error] Failed to add room {newRoom.Name}: {ex}");
                    return false;
                }
            });
        }

        public static long GetNextRoomId()
        {
            if (Rooms.Count() == 0)
                return 1;

            var maxId = Rooms.Max(x => x.RoomId);

            return Convert.ToInt64(maxId) + 1;
        }

        public static bool ChangeRoomId(long oldRoomId, long newRoomId, out string error)
        {
            error = string.Empty;
            if (newRoomId <= 0)
            {
                error = "The new room ID must be a positive number.";
                return false;
            }
            if (newRoomId == oldRoomId)
            {
                error = "That's already this room's ID.";
                return false;
            }

            lock (SubRoomMutationLock)
            {
                Room? room = Rooms.FindById(oldRoomId);
                if (room == null)
                {
                    error = "Room not found.";
                    return false;
                }
                if (Rooms.Exists(candidate => candidate.RoomId == newRoomId))
                {
                    error = $"Room {newRoomId} already exists.";
                    return false;
                }

                room.RoomId = newRoomId;
                room.RankedEntityId = newRoomId.ToString();
                if (room.SubRooms != null)
                {
                    foreach (SubRooms subRoom in room.SubRooms)
                        subRoom.RoomId = newRoomId;
                }

                Rooms.Insert(room);
                Rooms.Delete(oldRoomId);

                foreach (SubRoomDataSave save in SubRoomDataSaves.Find(value => value.RoomId == oldRoomId).ToList())
                {
                    save.RoomId = newRoomId;
                    SubRoomDataSaves.Update(save);
                }

                foreach (RoomBan ban in RoomBans.Find(value => value.RoomId == oldRoomId).ToList())
                {
                    ban.RoomId = newRoomId;
                    RoomBans.Update(ban);
                }

                return true;
            }
        }

        public static long GetNextSubRoomId()
        {

            lock (SubRoomMutationLock)
            {
                long maxSafeSubRoomId = 0;
                int ignoredImportedIds = 0;

                foreach (var room in Rooms.FindAll())
                {
                    if (room.SubRooms == null)
                        continue;

                    foreach (var subRoom in room.SubRooms)
                    {
                        long id = subRoom.SubRoomId;
                        if (id <= 0 || id > int.MaxValue)
                        {
                            ignoredImportedIds++;
                            continue;
                        }

                        if (id > maxSafeSubRoomId)
                            maxSafeSubRoomId = id;
                    }
                }

                if (maxSafeSubRoomId >= int.MaxValue)
                    throw new InvalidOperationException(
                        "No client-safe SubRoomId values remain in the positive Int32 range.");

                long nextId = maxSafeSubRoomId + 1;
                Console.WriteLine(
                    $"[SUBROOM ID ALLOCATOR] next={nextId} " +
                    $"maxSafeExisting={maxSafeSubRoomId} ignoredImported={ignoredImportedIds}");
                return nextId;
            }
        }

        public static Room? AddSubRoom(long roomId, long accountId, string requestedName)
        {
            lock (SubRoomMutationLock)
            {
                var room = Rooms.FindById(roomId);
                if (room == null)
                    return null;

                room.SubRooms ??= new List<SubRooms>();
                string name = GetUniqueSubRoomName(
                    room,
                    string.IsNullOrWhiteSpace(requestedName) ? "New Room" : requestedName.Trim());
                var template = room.SubRooms.FirstOrDefault(subRoom =>
                        string.Equals(subRoom.Name, "Home", StringComparison.OrdinalIgnoreCase))
                    ?? room.SubRooms.FirstOrDefault();

                var subRoom = new SubRooms
                {
                    SubRoomId = GetNextSubRoomId(),
                    RoomId = room.RoomId,
                    Name = name,
                    DataBlob = null,
                    SubRoomDataSaveId = 0,
                    IsSandbox = template?.IsSandbox ?? true,
                    MaxPlayers = Math.Max(1, template?.MaxPlayers ?? room.MaxPlayers),
                    Accessibility = template?.Accessibility ?? room.Accessibility,
                    UnitySceneId = template?.UnitySceneId ?? string.Empty,
                    Permissions = ClonePermissions(template?.Permissions),
                    SavedByAccountId = accountId
                };

                room.SubRooms.Add(subRoom);
                room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
                Rooms.Update(room);
                Console.WriteLine(
                    $"[SUBROOM ADD] room={roomId} subroom={subRoom.SubRoomId} name={subRoom.Name} by={accountId}");
                return PrepareRoomForClient(room);
            }
        }

        public static Room? CloneSubRoom(long roomId, long subRoomId, long accountId)
        {
            lock (SubRoomMutationLock)
            {
                var room = Rooms.FindById(roomId);
                var source = room?.SubRooms?.FirstOrDefault(subRoom =>
                    subRoom.SubRoomId == subRoomId);
                if (room == null || source == null)
                    return null;

                string name = GetUniqueSubRoomName(room, $"{source.Name} Copy");
                var clone = new SubRooms
                {
                    SubRoomId = GetNextSubRoomId(),
                    RoomId = room.RoomId,
                    Name = name,
                    DataBlob = source.DataBlob,
                    SubRoomDataSaveId = 0,
                    IsSandbox = source.IsSandbox,
                    MaxPlayers = Math.Max(1, source.MaxPlayers),
                    Accessibility = source.Accessibility,
                    UnitySceneId = source.UnitySceneId ?? string.Empty,
                    Permissions = ClonePermissions(source.Permissions),
                    SavedByAccountId = accountId
                };

                room.SubRooms!.Add(clone);
                room.UgcVersion = Math.Max(1, room.UgcVersion + 1);
                Rooms.Update(room);
                Console.WriteLine(
                    $"[SUBROOM CLONE] room={roomId} source={subRoomId} clone={clone.SubRoomId} name={clone.Name} by={accountId}");
                return PrepareRoomForClient(room);
            }
        }

        private static string GetUniqueSubRoomName(Room room, string requestedName)
        {
            string baseName = requestedName.Trim();
            if (baseName.Length > 50)
                baseName = baseName[..50].TrimEnd();
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "New Room";

            var usedNames = (room.SubRooms ?? new List<SubRooms>())
                .Select(subRoom => subRoom.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!usedNames.Contains(baseName))
                return baseName;

            for (int suffix = 2; suffix < 10_000; suffix++)
            {
                string suffixText = $" {suffix}";
                int prefixLength = Math.Min(baseName.Length, 50 - suffixText.Length);
                string candidate = baseName[..prefixLength].TrimEnd() + suffixText;
                if (!usedNames.Contains(candidate))
                    return candidate;
            }

            return $"Room {Guid.NewGuid():N}";
        }

        private static List<SubRoomPermission> ClonePermissions(
            IEnumerable<SubRoomPermission>? permissions)
        {
            return permissions?.Select(permission => new SubRoomPermission
            {
                Override = permission.Override,
                Permission = permission.Permission,
                Role = permission.Role,
                Type = permission.Type,
                Value = permission.Value
            }).ToList() ?? new List<SubRoomPermission>();
        }

        public static Room GetRoom(long roomId)
        {
            return PrepareRoomForClient(Rooms.FindById(roomId));
        }

        public static Room GetRoomByName(string name)
        {
            Room? room = Rooms.FindOne(x =>
                x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (room == null)
            {
                string normalized = NormalizeCanonicalRoomName(name);
                if (CanonicalBaseRoomNames.Any(canonical =>
                        NormalizeCanonicalRoomName(canonical) == normalized))
                {
                    room = Rooms.FindAll().FirstOrDefault(candidate =>
                        NormalizeCanonicalRoomName(candidate.Name) == normalized);
                }
            }
            return PrepareRoomForClient(room);
        }

        public static List<Room> GetRoomsByNames(List<string> names)
        {
            if (names == null || names.Count == 0)
                return new List<Room>();

            return PrepareRoomsForClient(Rooms.Find(room =>
                names.Contains(room.Name, StringComparer.OrdinalIgnoreCase)
            ));
        }

        public static Room? GetOrCreatePlayerDorm(long accountId)
        {
            if (accountId <= 0)
                return null;

            lock (DormCreationLock)
            {

                var existing = Rooms.Find(room =>
                        room.IsDorm &&
                        room.RoomId != 1 &&
                        room.CreatorAccountId == accountId &&
                        room.State != RoomState.MarkedForDelete)
                    .OrderBy(room => room.RoomId)
                    .FirstOrDefault();

                if (existing != null)
                {
                    existing.Roles ??= new List<Roles>();
                    if (!existing.Roles.Any(role =>
                            role.AccountId == accountId &&
                            role.Role == Role.Creator))
                    {
                        existing.Roles.RemoveAll(role => role.AccountId == accountId);
                        existing.Roles.Add(new Roles
                        {
                            AccountId = accountId,
                            Role = Role.Creator,
                            InvitedRole = Role.None
                        });
                        Rooms.Update(existing);
                    }

                    return PrepareRoomForClient(existing);
                }

                var template = Rooms.FindById(1) ??
                    Rooms.FindOne(room => room.IsDorm);
                if (template == null)
                    return null;

                long roomId = GetNextRoomId();
                long nextSubRoomId = GetNextSubRoomId();
                var dorm = new Room
                {
                    RoomId = roomId,
                    IsDorm = true,
                    MaxPlayerCalculationMode = template.MaxPlayerCalculationMode,
                    MaxPlayers = template.MaxPlayers,
                    CloningAllowed = false,
                    DisableMicAutoMute = template.DisableMicAutoMute,
                    DisableRoomComments = template.DisableRoomComments,
                    EncryptVoiceChat = template.EncryptVoiceChat,
                    ToxmodEnabled = template.ToxmodEnabled,
                    LoadScreenLocked = template.LoadScreenLocked,
                    PersistenceVersion = template.PersistenceVersion,
                    AutoLocalizeRoom = template.AutoLocalizeRoom,
                    IsDeveloperOwned = false,
                    Name = "DormRoom",
                    Description = template.Description,
                    ImageName = template.ImageName,
                    WarningMask = template.WarningMask,
                    CustomWarning = template.CustomWarning,
                    CreatorAccountId = accountId,
                    State = RoomState.Active,
                    Accessibility = RoomAccessibility.Private,
                    SupportsLevelVoting = template.SupportsLevelVoting,
                    IsRRO = template.IsRRO,
                    SupportsScreens = template.SupportsScreens,
                    SupportsWalkVR = template.SupportsWalkVR,
                    SupportsTeleportVR = template.SupportsTeleportVR,
                    SupportsVRLow = template.SupportsVRLow,
                    SupportsQuest2 = template.SupportsQuest2,
                    SupportsMobile = template.SupportsMobile,
                    SupportsJuniors = template.SupportsJuniors,
                    MinLevel = template.MinLevel,
                    CreatedAt = DateTime.UtcNow,
                    Stats = new Stats(),
                    RankedEntityId = roomId.ToString(),
                    RankingContext = null,
                    DataBlob = template.DataBlob,
                    UgcVersion = Math.Max(1, template.UgcVersion),
                    Tags = template.Tags?
                        .Select(tag => new Tags { Tag = tag.Tag, Type = tag.Type })
                        .ToList() ?? new List<Tags>(),
                    PromoImages = template.PromoImages?.ToList() ?? new List<string>(),
                    PromoExternalContent = template.PromoExternalContent?
                        .Select(item => new PromoExternalContent
                        {
                            Type = item.Type,
                            Reference = item.Reference
                        })
                        .ToList() ?? new List<PromoExternalContent>(),
                    LoadScreens = template.LoadScreens?
                        .Select(screen => new LoadScreens
                        {
                            ImageName = screen.ImageName,
                            Title = screen.Title,
                            Subtitle = screen.Subtitle
                        })
                        .ToList() ?? new List<LoadScreens>(),
                    Roles = new List<Roles>
                    {
                        new Roles
                        {
                            AccountId = accountId,
                            Role = Role.Creator,
                            InvitedRole = Role.None
                        }
                    }
                };

                dorm.SubRooms = (template.SubRooms ?? new List<SubRooms>())
                    .Select(subRoom => new SubRooms
                    {
                        SubRoomId = nextSubRoomId++,
                        RoomId = roomId,
                        Name = subRoom.Name,
                        DataBlob = subRoom.DataBlob,
                        SubRoomDataSaveId = 0,
                        IsSandbox = subRoom.IsSandbox,
                        MaxPlayers = subRoom.MaxPlayers,
                        Accessibility = RoomAccessibility.Private,
                        UnitySceneId = subRoom.UnitySceneId,
                        Permissions = ClonePermissions(subRoom.Permissions),
                        SavedByAccountId = accountId
                    })
                    .ToList();

                if (dorm.SubRooms.Count == 0)
                    return null;

                Rooms.Insert(dorm);
                Console.WriteLine(
                    $"[DORM CREATE] account={accountId} room={roomId} " +
                    $"subrooms={dorm.SubRooms.Count}");
                return PrepareRoomForClient(dorm);
            }
        }

        public static async Task<int> EnsureCanonicalBaseRooms(string path)
        {
            if (!File.Exists(path))
                return 0;

            string jsonData = await File.ReadAllTextAsync(path);
            var options = new SysJson.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = SysJson.JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            var importedRooms = SysJson.JsonSerializer.Deserialize<List<Room>>(jsonData, options) ?? new();
            var templates = importedRooms
                .Where(IsCanonicalBaseRoom)
                .OrderBy(GetCanonicalBaseRoomOrder)
                .ToList();

            int changed = 0;
            foreach (var template in templates)
            {
                var existing = GetRoomByName(template.Name);
                if (existing != null)
                {
                    existing.IsDorm = false;
                    existing.IsRRO = true;
                    existing.IsBaseRoom = true;
                    existing.Accessibility = RoomAccessibility.Public;
                    existing.State = RoomState.Active;
                    existing.Tags ??= new List<Tags>();
                    if (!existing.Tags.Any(tag =>
                            string.Equals(tag.Tag, "base", StringComparison.OrdinalIgnoreCase)))
                    {
                        existing.Tags.Add(new Tags { Tag = "base", Type = TagType.Auto });
                        changed++;
                    }
                    if (!existing.Tags.Any(tag =>
                            string.Equals(tag.Tag, "rro", StringComparison.OrdinalIgnoreCase)))
                    {
                        existing.Tags.Add(new Tags { Tag = "rro", Type = TagType.AGOnly });
                        changed++;
                    }

                    Rooms.Update(existing);
                    continue;
                }

                if (await AddRoom(
                        template,
                        log: true,
                        shouldAssignNewIds: true))
                {
                    changed++;
                }
            }

            return changed;
        }

        public static int EnsurePlayerDormsForAllPlayers()
        {
            int created = 0;
            foreach (long accountId in PlayerDB.Players.FindAll()
                         .Select(player => player.PlayerId)
                         .Where(accountId => accountId > 0))
            {
                bool alreadyExisted = Rooms.Exists(room =>
                    room.IsDorm &&
                    room.RoomId != 1 &&
                    room.CreatorAccountId == accountId &&
                    room.State != RoomState.MarkedForDelete);

                if (GetOrCreatePlayerDorm(accountId) != null && !alreadyExisted)
                    created++;
            }

            return created;
        }

        public static int DeletePlayerDorms(long accountId)
        {
            if (accountId <= 0)
                return 0;

            lock (DormCreationLock)
            {
                var roomIds = Rooms.Find(room =>
                        room.IsDorm &&
                        room.RoomId != 1 &&
                        room.CreatorAccountId == accountId)
                    .Select(room => room.RoomId)
                    .ToList();

                return roomIds.Count(roomId => Rooms.Delete(roomId));
            }
        }

        public static int RemoveOrphanedPlayerDorms()
        {
            var accountIds = PlayerDB.Players.FindAll()
                .Select(player => player.PlayerId)
                .Where(accountId => accountId > 0)
                .ToHashSet();

            lock (DormCreationLock)
            {
                var orphanedRoomIds = Rooms.Find(room =>
                        room.IsDorm &&
                        room.RoomId != 1 &&
                        room.CreatorAccountId > 0 &&
                        !accountIds.Contains(room.CreatorAccountId))
                    .Select(room => room.RoomId)
                    .ToList();

                return orphanedRoomIds.Count(roomId => Rooms.Delete(roomId));
            }
        }

        public static (List<Room> Results, int Total) GetHotRooms(string tag, int skip, int take)
        {

            RepairCanonicalBaseRoomMetadata();

            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 100);

            string t = string.IsNullOrWhiteSpace(tag)
                ? "hot"
                : tag.Trim().ToLowerInvariant();

            IEnumerable<Room> query = Rooms.FindAll()
                .Where(r => !r.IsDorm && r.State != RoomState.MarkedForDelete);

            switch (t)
            {
                case "hot":
                    query = query.Where(r =>
                        r.Accessibility == RoomAccessibility.Public);
                    break;

                case "rro":
                case "recroomoriginal":
                    query = query.Where(r =>
                        r.Accessibility == RoomAccessibility.Public &&
                        (r.IsRRO ||
                         (r.Tags?.Any(x =>
                             string.Equals(x.Tag, "rro", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(x.Tag, "recroomoriginal", StringComparison.OrdinalIgnoreCase)) ?? false)));
                    break;

                case "base":
                    query = query.Where(r =>
                        r.Accessibility == RoomAccessibility.Public &&
                        IsCanonicalBaseRoom(r));
                    break;

                case "new":
                    query = query.Where(r => r.Accessibility == RoomAccessibility.Public);
                    break;

                default:
                    query = query.Where(r =>
                        r.Accessibility == RoomAccessibility.Public &&
                        (r.Tags?.Any(x =>
                            string.Equals(x.Tag, t, StringComparison.OrdinalIgnoreCase)) ?? false));
                    break;
            }

            query = t switch
            {
                "new" => query.OrderByDescending(r => r.CreatedAt)
                    .ThenByDescending(r => r.RoomId),
                "base" => query.OrderBy(GetCanonicalBaseRoomOrder),
                _ => query.OrderByDescending(r => r.Stats?.VisitCount ?? 0)
                    .ThenByDescending(r => r.CreatedAt)
            };

            var all = query.ToList();
            var page = PrepareRoomsForClient(all.Skip(skip).Take(take));

            return (
                page,
                all.Count
            );
        }

        public static Room? CloneRoom(Room source, long ownerId, string? newName)
        {
            if (source == null || ownerId <= 0 || !source.CloningAllowed)
                return null;

            try
            {
                long nextRoomId = GetNextRoomId();
                long nextSubRoomId = GetNextSubRoomId();

                string requestedName = string.IsNullOrWhiteSpace(newName)
                    ? $"{source.Name} Copy"
                    : newName.Trim();

                string uniqueName = requestedName;
                int suffix = 2;

                while (GetRoomByName(uniqueName) != null)
                    uniqueName = $"{requestedName} {suffix++}";

                var clonedRoom = new Room
                {
                    RoomId = nextRoomId,
                    IsDorm = false,
                    MaxPlayerCalculationMode = source.MaxPlayerCalculationMode,
                    MaxPlayers = source.MaxPlayers,
                    CloningAllowed = true,
                    DisableMicAutoMute = source.DisableMicAutoMute,
                    DisableRoomComments = source.DisableRoomComments,
                    EncryptVoiceChat = source.EncryptVoiceChat,
                    ToxmodEnabled = source.ToxmodEnabled,
                    LoadScreenLocked = false,
                    PersistenceVersion = source.PersistenceVersion,
                    AutoLocalizeRoom = source.AutoLocalizeRoom,
                    IsDeveloperOwned = false,
                    Name = uniqueName,
                    Description = source.Description,
                    ImageName = source.ImageName,
                    WarningMask = source.WarningMask,
                    CustomWarning = source.CustomWarning,
                    CreatorAccountId = ownerId,
                    State = RoomState.Active,
                    Accessibility = RoomAccessibility.Private,
                    SupportsLevelVoting = source.SupportsLevelVoting,
                    IsRRO = false,
                    SupportsScreens = source.SupportsScreens,
                    SupportsWalkVR = source.SupportsWalkVR,
                    SupportsTeleportVR = source.SupportsTeleportVR,
                    SupportsVRLow = source.SupportsVRLow,
                    SupportsQuest2 = source.SupportsQuest2,
                    SupportsMobile = source.SupportsMobile,
                    SupportsJuniors = source.SupportsJuniors,
                    MinLevel = source.MinLevel,
                    CreatedAt = DateTime.UtcNow,
                    Stats = new Stats(),
                    RankedEntityId = nextRoomId.ToString(),
                    RankingContext = null,
                    UgcVersion = Math.Max(1, source.UgcVersion),
                    Tags = source.Tags?
                        .Where(t => !string.Equals(t.Tag, "rro", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(t.Tag, "base", StringComparison.OrdinalIgnoreCase))
                        .Select(t => new Tags { Tag = t.Tag, Type = t.Type })
                        .ToList() ?? new List<Tags>(),
                    PromoImages = source.PromoImages?.ToList() ?? new List<string>(),
                    PromoExternalContent = source.PromoExternalContent?
                        .Select(x => new PromoExternalContent { Type = x.Type, Reference = x.Reference })
                        .ToList() ?? new List<PromoExternalContent>(),
                    LoadScreens = source.LoadScreens?
                        .Select(x => new LoadScreens
                        {
                            ImageName = x.ImageName,
                            Title = x.Title,
                            Subtitle = x.Subtitle
                        })
                        .ToList() ?? new List<LoadScreens>(),
                    Roles = new List<Roles>
                    {
                        new Roles
                        {
                            AccountId = ownerId,
                            Role = Role.Creator,
                            InvitedRole = Role.None
                        }
                    },
                    DataBlob = source.DataBlob
                };

                clonedRoom.SubRooms = (source.SubRooms ?? new List<SubRooms>())
                    .Select(sr => new SubRooms
                    {
                        SubRoomId = nextSubRoomId++,
                        RoomId = nextRoomId,
                        Name = sr.Name,
                        DataBlob = sr.DataBlob,
                        SubRoomDataSaveId = 0,
                        IsSandbox = sr.IsSandbox,
                        MaxPlayers = sr.MaxPlayers,
                        Accessibility = sr.Accessibility,
                        UnitySceneId = sr.UnitySceneId,
                        Permissions = ClonePermissions(sr.Permissions),
                        SavedByAccountId = ownerId
                    })
                    .ToList();

                if (clonedRoom.SubRooms.Count == 0)
                    return null;

                Rooms.Insert(clonedRoom);
                Console.WriteLine($"[DB] Cloned room {source.RoomId} -> {clonedRoom.RoomId} ({clonedRoom.Name})");
                return clonedRoom;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB Error] Failed to clone room {source.Name}: {ex}");
                return null;
            }
        }

        public static bool DeleteRoom(long roomId)
        {
            try
            {
                return Rooms.Delete(roomId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB Error] Failed to delete room {roomId}: {ex.Message}");
                return false;
            }
        }

        public static Room? RenameRoom(long roomId, string newName)
        {
            var room = Rooms.FindById(roomId);
            if (room == null) return null;

            room.Name = newName;

            try
            {
                Rooms.Update(room);
                return room;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB Error] Failed to rename room {roomId}: {ex.Message}");
                return null;
            }
        }

        public static (int RoomsOwned, int SubRoomSaves, int RoomBans) RenameAccountReferences(
            long oldAccountId,
            long newAccountId)
        {
            if (oldAccountId <= 0 || newAccountId <= 0 || oldAccountId == newAccountId)
                return (0, 0, 0);

            int roomsOwned = 0;
            foreach (Room room in Rooms.Find(value => value.CreatorAccountId == oldAccountId).ToList())
            {
                room.CreatorAccountId = newAccountId;
                if (Rooms.Update(room))
                    roomsOwned++;
            }

            int subRoomSaves = 0;
            foreach (SubRoomDataSave save in SubRoomDataSaves
                         .Find(value => value.SavedByAccountId == oldAccountId)
                         .ToList())
            {
                save.SavedByAccountId = newAccountId;
                if (SubRoomDataSaves.Update(save))
                    subRoomSaves++;
            }

            int roomBans = 0;
            foreach (RoomBan ban in RoomBans
                         .Find(value =>
                             value.AccountId == oldAccountId ||
                             value.BannedByAccountId == oldAccountId)
                         .ToList())
            {
                if (ban.AccountId == oldAccountId)
                    ban.AccountId = newAccountId;
                if (ban.BannedByAccountId == oldAccountId)
                    ban.BannedByAccountId = newAccountId;

                if (RoomBans.Update(ban))
                    roomBans++;
            }

            return (roomsOwned, subRoomSaves, roomBans);
        }
    }
}