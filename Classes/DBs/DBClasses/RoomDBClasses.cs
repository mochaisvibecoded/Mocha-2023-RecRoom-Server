using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using LiteDB;

namespace Mocha2023.Classes.DBs.DBClasses
{
    public class RoomDBClasses
    {
        public const string RoomsCollectionName = "Rooms";
        public const string SubRoomDataSavesCollectionName = "SubRoomDataSaves";
        public const string RoomBansCollectionName = "RoomBans";

        public class Room
        {
            [BsonId]
            public long RoomId { get; set; }

            public bool IsDorm { get; set; }

            public int MaxPlayerCalculationMode { get; set; }

            public int MaxPlayers { get; set; }

            public bool CloningAllowed { get; set; }

            public bool DisableMicAutoMute { get; set; }

            public bool DisableRoomComments { get; set; }

            public bool EncryptVoiceChat { get; set; }

            public bool ToxmodEnabled { get; set; }

            public bool LoadScreenLocked { get; set; }

            public int PersistenceVersion { get; set; }

            public bool AutoLocalizeRoom { get; set; }

            public bool IsDeveloperOwned { get; set; }

            public string Name { get; set; } = string.Empty;

            public string? Description { get; set; }

            public string ImageName { get; set; } = string.Empty;

            public WarningMaskType WarningMask { get; set; }

            public string? CustomWarning { get; set; }

            public long CreatorAccountId { get; set; }

            public RoomState? State { get; set; }

            public RoomAccessibility Accessibility { get; set; }

            public bool SupportsLevelVoting { get; set; }

            public bool IsRRO { get; set; }

            public bool IsBaseRoom { get; set; }

            [JsonIgnore]
            public bool CreativeToolsBetaEnabled { get; set; }

            [BsonIgnore]
            [JsonPropertyName("CreativeToolsBetaEnabled")]
            public bool CreativeToolsBetaEnabledForClient
            {
                get => CreativeToolsBetaEnabled;
                set => CreativeToolsBetaEnabled = value;
            }

            [BsonIgnore]
            [JsonPropertyName("SupportsBetaContent")]
            public bool SupportsBetaContentForClient
            {
                get => CreativeToolsBetaEnabled;
                set => CreativeToolsBetaEnabled = value;
            }

            [BsonIgnore]
            [JsonPropertyName("IsBeta")]
            public bool IsBetaForClient
            {
                get => CreativeToolsBetaEnabled;
                set => CreativeToolsBetaEnabled = value;
            }

            [BsonIgnore]
            [JsonPropertyName("BetaContentEnabled")]
            public bool BetaContentEnabledForClient
            {
                get => CreativeToolsBetaEnabled;
                set => CreativeToolsBetaEnabled = value;
            }

            public bool SupportsScreens { get; set; }

            public bool SupportsWalkVR { get; set; }

            public bool SupportsTeleportVR { get; set; }

            public bool SupportsVRLow { get; set; }

            public bool SupportsQuest2 { get; set; }

            public bool SupportsMobile { get; set; }

            public bool SupportsJuniors { get; set; }

            public int MinLevel { get; set; }

            public DateTime CreatedAt { get; set; }

            public Stats Stats { get; set; } = new();

            public string? RankedEntityId { get; set; }

            public string? RankingContext { get; set; }

            public List<SubRooms> SubRooms { get; set; } = new();

            public List<Roles> Roles { get; set; } = new();

            public string? DataBlob { get; set; }

            [JsonIgnore]
            public int UgcVersion { get; set; }

            [BsonIgnore]
            [JsonPropertyName("Version")]
            public int VersionForClient
            {
                get => Math.Max(1, UgcVersion);
                set => UgcVersion = Math.Max(1, value);
            }

            public List<Tags> Tags { get; set; } = new();

            public List<string> PromoImages { get; set; } = new();

            public List<PromoExternalContent> PromoExternalContent { get; set; }
                = new();

            public List<LoadScreens> LoadScreens { get; set; } = new();
        }

        public class SubRooms
        {
            public long SubRoomId { get; set; }

            public long RoomId { get; set; }

            public string Name { get; set; } = string.Empty;

            public string? DataBlob { get; set; }

            [JsonIgnore]
            public long SubRoomDataSaveId { get; set; }

            [BsonIgnore]
            [JsonPropertyName("CurrentSave")]
            public SubRoomDataSave? SubRoomDataSave { get; set; }

            [BsonIgnore]
            [JsonPropertyName("DataSavedAt")]
            public DateTime? DataSavedAtForClient =>
                SubRoomDataSave?.CreatedAt;

            public bool IsSandbox { get; set; }

            public int MaxPlayers { get; set; }

            public RoomAccessibility Accessibility { get; set; }

            public string UnitySceneId { get; set; } = string.Empty;

            public List<SubRoomPermission> Permissions { get; set; } = new();

            [JsonIgnore]
            public long SavedByAccountId { get; set; }
        }

        public class SubRoomPermission
        {
            public bool Override { get; set; }

            public string Permission { get; set; } = string.Empty;

            public int Role { get; set; }

            public int Type { get; set; }

            public string Value { get; set; } = "True";
        }

        public class SubRoomDataSave
        {
            public sealed class SubRoomSaveResponse
            {
                [JsonPropertyName("Success")]
                public bool Success { get; set; } = true;

                [JsonPropertyName("RoomId")]
                public long RoomId { get; set; }

                [JsonPropertyName("SubRoomId")]
                public long SubRoomId { get; set; }

                [JsonPropertyName("SubRoomDataSaveId")]
                public long SubRoomDataSaveId { get; set; }

                [JsonPropertyName("SavedByAccountId")]
                public long SavedByAccountId { get; set; }

                [JsonPropertyName("DataBlob")]
                public string DataBlob { get; set; } = string.Empty;

                [JsonPropertyName("SubRoomDataFilename")]
                public string SubRoomDataFilename { get; set; } = string.Empty;

                [JsonPropertyName("DataBlobHash")]
                public string? DataBlobHash { get; set; }

                [JsonPropertyName("SubRoomDataHash")]
                public string? SubRoomDataHash { get; set; }

                [JsonPropertyName("UnityAssetId")]
                public string? UnityAssetId { get; set; }

                [JsonPropertyName("ReferencedUnityAssetIds")]
                public List<string> ReferencedUnityAssetIds { get; set; } = new();

                [JsonPropertyName("BakedUnityAssets")]
                public List<BakedUnityAsset> BakedUnityAssets { get; set; } = new();

                [JsonPropertyName("PersistenceVersion")]
                public int PersistenceVersion { get; set; }

                [JsonPropertyName("SavedOnPlatform")]
                public int? SavedOnPlatform { get; set; }

                [JsonPropertyName("SavedOnDeviceClass")]
                public int? SavedOnDeviceClass { get; set; }

                [JsonPropertyName("Description")]
                public string? Description { get; set; }

                [JsonPropertyName("CreatedAt")]
                public DateTime CreatedAt { get; set; }

                [JsonPropertyName("DataSavedAt")]
                public DateTime DataSavedAt { get; set; }

                [JsonPropertyName("CurrentSave")]
                public SubRoomDataSave CurrentSave { get; set; } = new();
            }
            [BsonId]
            [JsonPropertyName("SubRoomDataSaveId")]
            public long SubRoomDataSaveId { get; set; }

            [JsonIgnore]
            public long RoomId { get; set; }

            [JsonPropertyName("SubRoomId")]
            public long SubRoomId { get; set; }

            [JsonPropertyName("SavedByAccountId")]
            public long SavedByAccountId { get; set; }

            [JsonPropertyName("DataBlob")]
            public string DataBlob { get; set; } = string.Empty;

            [BsonIgnore]
            [JsonPropertyName("SubRoomDataFilename")]
            public string SubRoomDataFilename => DataBlob;

            [JsonIgnore]
            public string? RoomDataBlob { get; set; }

            [JsonPropertyName("DataBlobHash")]
            public string? DataBlobHash { get; set; }

            [BsonIgnore]
            [JsonPropertyName("SubRoomDataHash")]
            public string? SubRoomDataHash => DataBlobHash;

            [JsonPropertyName("PersistenceVersion")]
            public int? PersistenceVersion { get; set; }

            [JsonPropertyName("SavedOnPlatform")]
            public int? SavedOnPlatform { get; set; }

            [JsonPropertyName("SavedOnDeviceClass")]
            public int? SavedOnDeviceClass { get; set; }

            [JsonPropertyName("Description")]
            public string? Description { get; set; }

            [JsonPropertyName("UnityAssetId")]
            public string? UnityAssetId { get; set; }

            [JsonPropertyName("ReferencedUnityAssetIds")]
            public List<string> ReferencedUnityAssetIds { get; set; } = new();

            [JsonPropertyName("OMVersion")]
            public int OMVersion { get; set; }

            [JsonPropertyName("UgcSubVersion")]
            public int UgcSubVersion { get; set; }

            [JsonPropertyName("ModerationState")]
            public int ModerationState { get; set; }

            [JsonPropertyName("Tags")]
            public List<string> Tags { get; set; } = new();

            [JsonPropertyName("BakedUnityAssets")]
            public List<BakedUnityAsset> BakedUnityAssets { get; set; } = new();

            [JsonPropertyName("CreatedAt")]
            public DateTime CreatedAt { get; set; }

            [JsonIgnore]
            public bool IsPublished { get; set; }
        }

        public class BakedUnityAsset
        {
            [JsonPropertyName("UnityAssetId")]
            public string UnityAssetId { get; set; } = string.Empty;

            [JsonPropertyName("Target")]
            public int Target { get; set; }

            [JsonPropertyName("Version")]
            public int Version { get; set; }

            [JsonPropertyName("Filename")]
            public string Filename { get; set; } = string.Empty;

            [JsonPropertyName("Hash")]
            public string? Hash { get; set; }

            [JsonPropertyName("UnityVersion")]
            public string? UnityVersion { get; set; }

            [JsonPropertyName("IsAvailable")]
            public bool IsAvailable { get; set; }
        }

        public static void HydrateCurrentSaves(
            LiteDatabase database,
            Room room)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(room);

            room.SubRooms ??= new List<SubRooms>();

            var saves = database.GetCollection<SubRoomDataSave>(
                SubRoomDataSavesCollectionName,
                BsonAutoId.Int64
            );

            SubRoomDataSave? newestRoomSave = null;

            foreach (var subRoom in room.SubRooms)
            {
                SubRoomDataSave? currentSave = null;

                if (subRoom.SubRoomDataSaveId > 0)
                {
                    currentSave = saves.FindById(
                        subRoom.SubRoomDataSaveId
                    );

                    if (currentSave != null &&
                        (currentSave.RoomId != room.RoomId ||
                         currentSave.SubRoomId != subRoom.SubRoomId))
                    {
                        Console.WriteLine(
                            $"[ROOM LOAD WARNING] Stale save pointer " +
                            $"room={room.RoomId} " +
                            $"subroom={subRoom.SubRoomId} " +
                            $"saveId={subRoom.SubRoomDataSaveId}"
                        );

                        currentSave = null;
                    }
                }

                currentSave ??= FindNewestSubRoomSave(
                    saves,
                    room.RoomId,
                    subRoom.SubRoomId
                );

                if (currentSave == null)
                {
                    Console.WriteLine(
                        $"[ROOM LOAD] No save found " +
                        $"room={room.RoomId} " +
                        $"subroom={subRoom.SubRoomId}"
                    );

                    continue;
                }

                ApplyCurrentSave(subRoom, currentSave);

                if (newestRoomSave == null ||
                    currentSave.CreatedAt > newestRoomSave.CreatedAt)
                {
                    newestRoomSave = currentSave;
                }

                Console.WriteLine(
                    $"[ROOM LOAD] " +
                    $"room={room.RoomId} " +
                    $"subroom={subRoom.SubRoomId} " +
                    $"saveId={currentSave.SubRoomDataSaveId} " +
                    $"superRoomBlob={currentSave.RoomDataBlob ?? "null"} " +
                    $"subRoomBlob={currentSave.DataBlob} " +
                    $"persistenceVersion=" +
                    $"{currentSave.PersistenceVersion?.ToString() ?? "null"}"
                );
            }

            if (!string.IsNullOrWhiteSpace(newestRoomSave?.RoomDataBlob))
            {
                room.DataBlob = newestRoomSave.RoomDataBlob;
            }

            if (newestRoomSave?.PersistenceVersion is int persistenceVersion)
            {
                room.PersistenceVersion = persistenceVersion;
            }

            room.UgcVersion = Math.Max(1, room.UgcVersion);
        }

        public static SubRoomDataSave? FindNewestSubRoomSave(
            ILiteCollection<SubRoomDataSave> saves,
            long roomId,
            long subRoomId)
        {
            ArgumentNullException.ThrowIfNull(saves);

            var query = Query.And(
                Query.EQ(
                    nameof(SubRoomDataSave.RoomId),
                    roomId
                ),
                Query.EQ(
                    nameof(SubRoomDataSave.SubRoomId),
                    subRoomId
                )
            );

            return saves
                .Find(query)
                .OrderByDescending(save => save.CreatedAt)
                .ThenByDescending(save => save.SubRoomDataSaveId)
                .FirstOrDefault();
        }

        public static void ApplyCurrentSave(
            SubRooms subRoom,
            SubRoomDataSave currentSave)
        {
            ArgumentNullException.ThrowIfNull(subRoom);
            ArgumentNullException.ThrowIfNull(currentSave);

            subRoom.SubRoomDataSaveId =
                currentSave.SubRoomDataSaveId;

            subRoom.SubRoomDataSave =
                currentSave;

            subRoom.DataBlob =
                currentSave.DataBlob;

            subRoom.SavedByAccountId =
                currentSave.SavedByAccountId;
        }

        public class Tags
        {
            public string Tag { get; set; } = string.Empty;

            public TagType Type { get; set; }
        }

        public class PromoExternalContent
        {
            public PromoExternalContentType Type { get; set; }

            public string Reference { get; set; } = string.Empty;
        }

        public class LoadScreens
        {
            public string ImageName { get; set; } = string.Empty;

            public string Title { get; set; } = string.Empty;

            public string Subtitle { get; set; } = string.Empty;
        }

        public class Stats
        {
            public int CheerCount { get; set; }

            public int FavoriteCount { get; set; }

            public int VisitorCount { get; set; }

            public int VisitCount { get; set; }
        }

        public class Roles
        {
            public long AccountId { get; set; }

            public Role Role { get; set; }

            public Role InvitedRole { get; set; }
        }

        public class RoomEditPermission
        {
            public bool CanEditRoom { get; set; }

            public string Error { get; set; } = string.Empty;
        }

        public class RoomBan
        {
            [BsonId]
            public long RoomBanId { get; set; }

            public long RoomId { get; set; }

            public long AccountId { get; set; }

            public long BannedByAccountId { get; set; }

            public string? Reason { get; set; }

            public DateTime BannedAt { get; set; }
        }

        public enum Role : byte
        {
            None = 0,
            Banned = 1,

            Host = 10,
            Moderator = 20,
            CoOwner = 30,
            TemporaryCoOwner = 31,

            Creator = 255
        }

        [Flags]
        public enum WarningMaskType
        {
            None = 0,
            Scary = 1,
            Mature = 2,
            FlashingLights = 4,
            IntenseMotion = 8,
            Violence = 16,
            Custom = 32,
            Reports = 64
        }

        public enum RoomAccessibility
        {
            Private = 0,
            Public = 1,
            Unlisted = 2
        }

        public enum RoomState
        {
            Active = 0,

            PendingJunior = 11,

            Moderation_PendingReview = 100,
            Moderation_Closed = 101,

            MarkedForDelete = 1000
        }

        public enum TagType
        {
            General = 0,
            Auto = 1,
            AGOnly = 2,
            Banned = 3
        }

        public enum PromoExternalContentType
        {
            YouTube = 0
        }

        public enum JoinMode
        {
            PublicMatchmaking = 0,
            PublicNewInstance = 1,
            PrivateNewInstance = 2
        }
    }
}
