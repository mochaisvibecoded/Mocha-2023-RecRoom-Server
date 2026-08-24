using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mocha2023.Classes;
using LiteDB;

namespace Mocha2023.Classes.DBs.DBClasses
{
    public class PlayerDBClasses
    {
        public class FullPlayer
        {
            [BsonId]
            public long PlayerId { get; set; }
            public List<mPlatformID> PlatformIds { get; set; } = new();
            [JsonIgnore]
            public List<string>? DeviceIds { get; set; } = new();
            [JsonIgnore]
            public string? AuthToken { get; set; }
            [JsonIgnore]
            public string? Password { get; set; }
            public List<PlayerRoles> PlayerRoles { get; set; } = new();
            public Player? Player { get; set; }
        }

        public class Player
        {
            public string? Username { get; set; }
            public string? DisplayName { get; set; }
            public string? Bio { get; set; }
            public int AvailableUsernameChanges { get; set; } = 3;
            public bool? IsJunior { get; set; }
            public bool IsAgeVerified { get; set; }
            public DateTime? AgeVerifiedAt { get; set; }
            public int Level { get; set; } = 1;
            public int XP { get; set; } = 0;
            public string? ProfileImage { get; set; }
            public string? BannerImage { get; set; }
            public string? Email { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastLoginAt { get; set; }
            public DateTime? Birthday { get; set; }
            public CurrentAuthSession? CurrentAuthSession { get; set; } = new CurrentAuthSession();
            public Reputation Reputation { get; set; } = new Reputation();
            public List<long> VisitedRooms { get; set; } = new();
            public List<long> CheeredRooms { get; set; } = new List<long>();
            public List<long> FavoritedRooms { get; set; } = new List<long>();
            public List<PlayerRelationship> Relationships { get; set; } = new();
            public List<PlayerCheerRecord> ReceivedCheers { get; set; } = new();
            public List<long> SubscribedAccountIds { get; set; } = new();
            public int SupportedInfluencerId { get; set; }
            public string? InfluencerCode { get; set; }
            public PlayerExtra PlayerExtra { get; set; } = new PlayerExtra();
            public string DisplayEmoji { get; set; } = "";
            public int PersonalPronouns { get; set; }

            public int IdentityFlags { get; set; }
        }

        public class PlayerRelationship
        {
            public long Id { get; set; }
            public long PlayerID { get; set; }
            public RelationshipType RelationshipType { get; set; }
            public bool Favorited { get; set; }
            public bool Muted { get; set; }
            public bool Ignored { get; set; }
        }

        public sealed class ClientRelationshipDTO
        {
            [JsonPropertyName("PlayerID")]
            public int PlayerID { get; set; }

            [JsonPropertyName("RelationshipType")]
            public int RelationshipType { get; set; }

            [JsonPropertyName("Favorited")]
            public int Favorited { get; set; }

            [JsonPropertyName("Muted")]
            public int Muted { get; set; }

            [JsonPropertyName("Ignored")]
            public int Ignored { get; set; }
        }

        public class PlayerCheerRecord
        {
            public long CheerId { get; set; }
            public long NotificationId { get; set; }
            public long GiverPlayerId { get; set; }
            public long ReceiverPlayerId { get; set; }
            public CheerCategory CheerCategory { get; set; }
            public bool Anonymous { get; set; }
            public CheerStatus Status { get; set; } = CheerStatus.Pending;
            public DateTime CreatedAt { get; set; }
            public DateTime? HandledAt { get; set; }
        }

        public class PlayerDTOBase
        {
            public long accountId { get; set; }
            public DateTime createdAt { get; set; }
            public string? displayName { get; set; }
            public bool? isJunior { get; set; }
            public int platforms { get; set; }
            public string? profileImage { get; set; }
            public string? bannerImage { get; set; }
            public string? username { get; set; }
            public int personalPronouns { get; set; }
            public int identityFlags { get; set; }
            public string? displayEmoji { get; set; }
        }

        public class PlayerDTO : PlayerDTOBase { }
        public class PlayerMeDTO : PlayerDTOBase
        {
            public int availableUsernameChanges { get; set; } = 3;
            public DateTime? birthday { get; set; }
            public string? email { get; set; }
            public string? phone { get; set; }
        }

        public class CurrentAuthSession { }

        public class PlayerExtra
        {
            public Avatar Avatar { get; set; } = new Avatar();
            public List<string> AvatarItems { get; set; } = new();
            public List<SavedOutfit> SavedAvatars { get; set; } = new();
            public ModerationBlockDetails? ModerationBlockDetails { get; set; } = new ModerationBlockDetails();
            public List<Setting> Settings { get; set; } = new();
            public Heartbeat Heartbeat { get; set; } = new Heartbeat();
            public List<PlayerCurrency> Currencies { get; set; } = new();
            public List<RoomVisit> RoomVisits { get; set; } = new();
            public List<EquipmentItem> Equipment { get; set; } = new();
            public List<GiftPackage> PendingGiftPackages { get; set; } = new();

            [JsonIgnore]
            public long FriendotronLastSentUtcDay { get; set; }

            public long LevelingRoomInstanceId { get; set; }
            public int LevelingRoomActiveSeconds { get; set; }
            public long LevelingLastHeartbeatUnixTime { get; set; }

            public long PartyId { get; set; }
            public long PartyLeaderPlayerId { get; set; }
            public List<long> PartyMemberPlayerIds { get; set; } = new();
            public List<PartyInviteRecord> PendingPartyInvites { get; set; } = new();

            public List<string> Warnings { get; set; } = new();
            public List<string> ItemWishlist { get; set; } = new();
        }

        public enum GiftContext
        {
            None = -1,
            Default,
            First_Activity,
            Game_Drop,
            All_Daily_Challenges_Complete,
            All_Weekly_Challenge_Complete,
            Daily_Challenge_Complete,
            Weekly_Challenge_Complete,
            Unassigned_Equipment = 10,
            Unassigned_Avatar,
            Unassigned_Consumable,
            Reacquisition = 20,
            Membership,
            NUX_TokensAndDressUp = 30,
            NUX_Experiment1,
            NUX_Experiment2,
            NUX_Experiment3,
            NUX_Experiment4,
            NUX_Experiment5,
            GameRewards = 50,
            GameRewards_Tokens,
            LevelUp = 100,
            Purchased_Gift_A = 500,
            Purchased_Gift_B,
            Purchased_Gift_C,
            Purchased_Gift_D,
            Holiday = 1000,
            Contest,
            Promotion,
            SubscribersOnly,
            Deprecated = 1100,
            RecRoyale = 1200,
            DEPRECATED_Paintball_ClearCut = 2000,
            DEPRECATED_Paintball_Homestead,
            DEPRECATED_Paintball_Quarry,
            DEPRECATED_Paintball_River,
            DEPRECATED_Paintball_Dam,
            DEPRECATED_Paintball_DriveIn,
            Paintball_ClearCut = 2010,
            Paintball_Homestead,
            Paintball_Quarry,
            Paintball_River,
            Paintball_Dam,
            Paintball_DriveIn,
            DEPRECATED_Discgolf_Propulsion = 3000,
            DEPRECATED_Discgolf_Lake,
            Discgolf_Propulsion = 3010,
            Discgolf_Lake,
            Discgolf_Mode_CoopCatch = 3500,
            Quest_Goblin_A = 4000,
            Quest_Goblin_B,
            Quest_Goblin_C,
            Quest_Goblin_S,
            Quest_Goblin_Consumable,
            Quest_Cauldron_A = 4010,
            Quest_Cauldron_B,
            Quest_Cauldron_C,
            Quest_Cauldron_S,
            Quest_Cauldron_Consumable,
            Quest_Pirate1_A = 4100,
            Quest_Pirate1_B,
            Quest_Pirate1_C,
            Quest_Pirate1_S,
            Quest_Pirate1_X,
            Quest_Pirate1_Consumable,
            Quest_Dracula1_A = 4200,
            Quest_Dracula1_B,
            Quest_Dracula1_C,
            Quest_Dracula1_S,
            Quest_Dracula1_X,
            Quest_Dracula1_Consumable,
            Quest_Dracula1_SS,
            Quest_SciFi_A = 4500,
            Quest_SciFi_B,
            Quest_SciFi_C,
            Quest_SciFi_S,
            Quest_Scifi_Consumable,
            DEPRECATED_Charades = 5000,
            Charades,
            DEPRECATED_Soccer = 6000,
            Soccer,
            DEPRECATED_Paddleball = 7000,
            Paddleball,
            DEPRECATED_Dodgeball = 8000,
            Dodgeball,
            DEPRECATED_Lasertag = 9000,
            Lasertag,
            DEPRECATED_Bowling = 10000,
            Bowling,
            StuntRunner_TheMainEvent_A = 11000,
            StuntRunner_TheMainEvent_B,
            StuntRunner_TheMainEvent_C,
            StuntRunner_TheMainEvent_D,
            StuntRunner_TheMainEvent_S,
            StuntRunner_TheMainEvent_X,
            StuntRunner_TheMainEvent_Consumable,
            StuntRunner_TheMainEvent_SS,
            Store_LaserTag = 100000,
            Store_RecCenter = 100010,
            Consumable = 110000,
            Token = 110100,
            Punchcard_Challenge_Complete = 110200,
            All_Punchcard_Challenges_Complete,
            Commerce_Purchase = 200000
        }

        public sealed class GiftPackage
        {
            public long GiftPackageId { get; set; }
            public int FromPlayerId { get; set; }
            public int PlayerId { get; set; }
            public string Message { get; set; } = string.Empty;
            public string AvatarItemDesc { get; set; } = string.Empty;
            public string ConsumableItemDesc { get; set; } = string.Empty;
            public int ConsumableQuantity { get; set; } = 1;
            public string EquipmentPrefabName { get; set; } = string.Empty;
            public string EquipmentModificationGuid { get; set; } = string.Empty;
            public string FriendlyName { get; set; } = string.Empty;
            public string Tooltip { get; set; } = string.Empty;
            public string ThumbnailImage { get; set; } = string.Empty;
            public int CurrencyType { get; set; } = 2;
            public int Currency { get; set; }
            public int XP { get; set; }
            public int GiftContext { get; set; }
            public int Rarity { get; set; }
            public int Platform { get; set; } = -1;
            public int PlatformMask { get; set; } = -1;
            public int? BalanceType { get; set; } = -1;
            public bool IsQuery { get; set; }
            public bool Unique { get; set; } = true;
        }

        public class PartyInviteRecord
        {
            public long PartyId { get; set; }
            public long InviterPlayerId { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class PartySnapshot
        {
            public long PartyId { get; set; }
            public long LeaderPlayerId { get; set; }
            public List<long> MemberPlayerIds { get; set; } = new();
        }

        public class EquipmentItem
        {
            public string PrefabName { get; set; } = "";
            public string ModificationGuid { get; set; } = "";
            public int UnlockedLevel { get; set; } = 0;
            public bool Favorited { get; set; } = false;
            public int PlatformMask { get; set; } = -1;
            public string FriendlyName { get; set; } = "";
            public string Tooltip { get; set; } = "";
            public int Rarity { get; set; } = 0;
            public string ThumbnailImage { get; set; } = "";
        }

        public class RoomVisit
        {
            public long RoomId { get; set; }
            public DateTime VisitedAt { get; set; }
        }

        public class PlayerCurrency
        {
            public int Balance { get; set; }
            public CurrencyType CurrencyType { get; set; }
            public BalanceType BalanceType { get; set; }
        }

        public class Avatar
        {
            public string OutfitSelections { get; set; } = "";
            public string FaceFeatures { get; set; } = "";
            public string SkinColor { get; set; } = "";
            public string HairColor { get; set; } = "";
        }

        public class Heartbeat
        {
            public string appVersion { get; set; } = ServerConfig.GameVersion.ToString();
            public DeviceClasses? deviceClass { get; set; } = DeviceClasses.Unknown;
            public MatchmakingErrorCode? errorCode { get; set; } = null;
            public bool isOnline { get; set; } = false;
            public long playerId { get; set; } = 0;
            public RoomInstance? roomInstance { get; set; } = null;
            public StatusVisibility statusVisibility { get; set; } = StatusVisibility.Online;
            public int vrMovementMode { get; set; } = 0;

            [JsonIgnore]
            public long lastHeartbeatUnixTime { get; set; } = 0;
        }

        public class RoomInstance
        {
            public bool encryptVoiceChat { get; set; }
            public long clubId { get; set; } = 0;
            public string? dataBlob { get; set; }
            public long eventId { get; set; } = 0;
            public bool isFull { get; set; }
            public bool isInProgress { get; set; }
            public bool isPrivate { get; set; }
            public string location { get; set; }
            public int maxCapacity { get; set; }
            public string Name { get; set; }
            public string photonRegion { get; set; }
            public string photonRegionId { get; set; }
            public string photonRoomId { get; set; }
            public string roomCode { get; set; } = "";
            public long roomId { get; set; }
            public long roomInstanceId { get; set; }
            public RoomInstanceType roomInstanceType { get; set; }
            public long subRoomId { get; set; }
            public long subRoomDataSaveId { get; set; }

            [JsonIgnore]
            public DateTime createdAt { get; set; }
        }

        public class Reputation
        {
            public long AccountId { get; set; }
            public bool IsCheerful { get; set; }
            public double Noteriety { get; set; }
            public CheerCategory SelectedCheer { get; set; }
            public int CheerCredit { get; set; } = 15;

            [JsonIgnore]
            public long CheerCreditRefreshAtUnixTime { get; set; }
            public int CheerGeneral { get; set; }
            public int CheerHelpful { get; set; }
            public int CheerCreative { get; set; }
            public int CheerGreatHost { get; set; }
            public int CheerSportsman { get; set; }
            public int SubscriberCount { get; set; }
            public int SubscribedCount { get; set; }
        }
        public class ModerationBlockDetails
        {
            public ReportCategory ReportCategory { get; set; } = ReportCategory.Moderator;
            public int Duration { get; set; } = 0;
            public long GameSessionId { get; set; } = 0;
            public bool? IsBan { get; set; } = false;
            public bool? IsHostKick { get; set; } = false;
            public string? Message { get; set; } = "";
            public ulong? PlayerIdReporter { get; set; } = null;
            [JsonIgnore]
            public long ModerationSetUnixTime { get; set; } = 0;
            [JsonIgnore]
            public ulong BannedByPlayerId { get; set; } = 0;
            [JsonIgnore]
            public string? AppealCode { get; set; }
            [JsonIgnore]
            public bool AppealSubmitted { get; set; }
            [JsonIgnore]
            public DateTime? AppealSubmittedAt { get; set; }
        }

        public class Setting
        {
            public required string Key { get; set; }
            public required string Value { get; set; }
        }

        public class mPlatformID
        {
            public Platforms Platform { get; set; }
            public ulong PlatformId { get; set; }
        }

        public class CachedLogins
        {
            public Platforms platform { get; set; }
            public string? platformId { get; set; }
            public long accountId { get; set; }
            public DateTime? lastLoginTime { get; set; }
            public bool requirePassword { get; set; }
        }

        public class PlayerProgressionDTO
        {
            public long PlayerId { get; set; }
            public int Level { get; set; }
            public int XP { get; set; }
        }

        public enum Platforms
        {
            All = -1,
            Steam,
            Oculus,
            PlayStation,
            Xbox,
            HeadlessBot,
            IOS,
            GooglePlay
        }

        public enum PlayerRoles
        {
            Screenshare,
            Moderator,
            Developer,
            Influencer,
            RRPlus,
            Keepsake,

            CommunityTeam
        }

        public enum ReportCategory
        {
            Moderator = -1,
            Unknown,
            DEPRECATED_MicrophoneAbuse,
            Harassment,
            Cheating,
            DEPRECATED_ImmatureBehavior,
            AFK,
            Misc,
            Underage,
            VoteKick = 10,
            MisleadingPurchases,
            CoC_Underage = 100,
            CoC_Sexual,
            CoC_Discrimination,
            CoC_Trolling,
            CoC_NameOrProfile,
            IssuingInaccurateReports = 1000
        }

        public enum CheerCategory
        {
            General,
            Helpful = 10,
            Sportmanship = 20,
            GreatHost = 30,
            Creative = 40,

            RecRoomDeveloper = 9000,
            RecRoomCommunityTeam = 9001
        }

        public enum CheerStatus
        {
            Pending,
            Accepted,
            Dismissed
        }

        public enum RelationshipType
        {
            None,
            OutgoingFriendRequest,
            IncomingFriendRequest,
            Friend
        }

        public enum MatchmakingErrorCode
        {
            UnknownError = -1,
            Success,
            NoSuchGame,
            PlayerNotOnline,
            InsufficientSpace,
            EventNotStarted,
            EventAlreadyFinished,
            BlockedFromRoom = 7,
            JuniorNotAllowed = 11,
            Banned,
            AlreadyInBestInstance,
            InsufficientRelationship,
            UpdateRequired = 16,
            AlreadyInTargetInstance,
            UGCNotAllowed = 19,
            NoSuchRoom,
            RoomIsNotActive = 22,
            RoomBlockedByCreator,
            RoomIsPrivate = 25,
            RoomInstanceIsPrivate,
            DeviceClassNotSupported = 30,
            DeviceClassNotSupportedByRoomOwner,
            MovementModeNotSupportedByRoomOwner,
            EventIsPrivate = 35,
            RoomInviteExpired = 40,
            NoAvailableRegion = 45,
            NotorietyTooPoor = 50,
            BannedFromRoom = 55,
            NoSuchRoomPlaylist = 60,
            RoomPlaylistIsNotActive,
            RoomPlaylistIsPrivate,
            NoSuchClub = 70,
            ClubHasNoClubhouse,
            ClubIsNotActive = 73,
            NotAMemberOfClub,
            BannedFromClub,
            InstanceJoinNotPermitted,
            LevelTooLow
        }

        public enum DeviceClasses
        {
            Unknown,
            VR,
            Screen,
            Mobile,
            VRLow,
            Quest2
        }

        public enum CurrencyType
        {
            Invalid,
            LaserTagTickets,
            RecCenterTokens,
            LostSkullsGold = 100,
            DraculaSilver,
            RecRoyale_Season1 = 200,
            RoomCurrency = 300
        }

        public enum BalanceType
        {
            NonPurchasedNotUsableInP2P = -2,
            NonPurchasedDefault,
            SteamPurchased,
            OculusPurchased,
            PlayStationPurchased,
            MicrosoftPurchased,
            IOSPurchased = 5,
            GooglePlayPurchased,
            PlayStationNonPurchasedP2P = 100,
            NonPlayStationNonPurchasedP2P,
            NonPurchasedEarnedByP2P = 1000
        }

        public enum StatusVisibility
        {
            Online,
            Away,
            Offline,
            Unknown = 100
        }

        public enum RoomInstanceType
        {
            Public,
            Private,
            Dormroom,
            Event,
            Meetup,
            Clubhouse
        }

        public class SavedOutfit : Avatar
        {
            public int Slot { get; set; }
            public string? PreviewImageName { get; set; }
        }
    }

    public static class RoleUtils
    {
        public static bool TryParseRole(string roleName, out PlayerDBClasses.PlayerRoles role)
        {
            role = default;
            if (string.IsNullOrWhiteSpace(roleName))
                return false;

            switch (roleName.Trim().ToLowerInvariant())
            {
                case "developer":
                case "dev":
                    role = PlayerDBClasses.PlayerRoles.Developer;
                    return true;
                case "communityteam":
                case "community-team":
                case "community_team":
                case "community":
                case "ct":
                    role = PlayerDBClasses.PlayerRoles.CommunityTeam;
                    return true;
                case "moderator":
                    role = PlayerDBClasses.PlayerRoles.Moderator;
                    return true;
                case "screenshare":
                    role = PlayerDBClasses.PlayerRoles.Screenshare;
                    return true;
                case "influencer":
                    role = PlayerDBClasses.PlayerRoles.Influencer;
                    return true;
                case "rrplus":
                case "rr+":
                    role = PlayerDBClasses.PlayerRoles.RRPlus;
                    return true;
                case "keepsake":
                    role = PlayerDBClasses.PlayerRoles.Keepsake;
                    return true;
                default:
                    return Enum.TryParse(roleName, true, out role);
            }
        }
    }
}
