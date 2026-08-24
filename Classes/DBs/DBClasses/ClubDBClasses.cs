using LiteDB;

namespace Mocha2023.Classes.DBs.DBClasses
{
    public static class ClubDBClasses
    {
        public enum ClubState
        {
            Active = 0,
            PendingJunior = 11,
            ModerationPendingReview = 100,
            ModerationClosed = 101,
            MarkedForDelete = 1000
        }

        public enum ClubVisibility
        {
            Private = 0,
            Public = 1
        }

        public enum ClubJoinability
        {
            Open = 0,
            InviteOnly = 1,
            AskToJoin = 2
        }

        public enum ClubType
        {
            Generic = 0,
            Creator = 1
        }

        public enum ClubMembershipType
        {
            Banned = -1,
            None = 0,
            PendingRequested = 1,
            PendingInvited = 2,
            PendingDenied = 3,
            Member = 10,
            Moderator = 20,
            CoOwner = 30,
            Creator = 100
        }

        public sealed class ClubRecord
        {
            [BsonId]
            public long ClubId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string MainImageName { get; set; } = "DefaultClubImage2k.jpg";
            public ClubState State { get; set; } = ClubState.Active;
            public int MemberCount { get; set; }
            public string PrimaryCategory { get; set; } = "Social";
            public ClubVisibility Visibility { get; set; } = ClubVisibility.Public;
            public ClubJoinability Joinability { get; set; } = ClubJoinability.Open;
            public bool AllowJuniors { get; set; } = true;
            public int MinLevel { get; set; }
            public bool ClubChatEnabled { get; set; } = true;
            public long? ClubhouseRoomId { get; set; }
            public ClubType ClubType { get; set; } = ClubType.Generic;
            public long CreatorAccountId { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
            public List<string> CategoryTags { get; set; } = new();
            public List<string> AdditionalImages { get; set; } = new();
        }

        public sealed class ClubMembershipRecord
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;
            public long ClubId { get; set; }
            public long AccountId { get; set; }
            public ClubMembershipType MembershipType { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        public sealed class HomeClubRecord
        {
            [BsonId]
            public long AccountId { get; set; }
            public long ClubId { get; set; }
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        public sealed class ClubSummaryDto
        {
            public long ClubId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string MainImageName { get; set; } = string.Empty;
            public string ImageName => MainImageName;
            public int State { get; set; }
            public int MemberCount { get; set; }
            public string PrimaryCategory { get; set; } = string.Empty;
            public string PrimaryTag => PrimaryCategory;
            public string Category => PrimaryCategory;
            public string ClubCategory => PrimaryCategory;
            public List<string> CategoryTags { get; set; } = new();
            public List<string> CustomTags => CategoryTags;
            public List<string> Tags => CategoryTags;
            public int Visibility { get; set; }
            public int Joinability { get; set; }
            public bool AllowJuniors { get; set; }
            public int MinLevel { get; set; }
            public bool ClubChatEnabled { get; set; }
            public long? ClubhouseRoomId { get; set; }
            public int ClubType { get; set; }
            public long CreatorAccountId { get; set; }
            public long CreatorPlayerId => CreatorAccountId;
            public int ClubMembershipType { get; set; }
            public int MembershipType => ClubMembershipType;
            public int MyMembershipType => ClubMembershipType;
            public DateTime CreatedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        public sealed class ClubMembershipDto
        {
            public long AccountId { get; set; }
            public long ClubId { get; set; }
            public int ClubMembershipType { get; set; }
            public int MembershipType => ClubMembershipType;
            public DateTime CreatedAt { get; set; }
        }

        public sealed class ClubPermissionsDto
        {
            public long ClubId { get; set; }
            public int MembershipType { get; set; }
            public bool CanInvite { get; set; }
            public bool CanKick { get; set; }
            public bool CanBan { get; set; }
            public bool CanModify { get; set; }
            public bool CanManageRoles { get; set; }
            public bool CanManageClubhouse { get; set; }
        }

        public sealed class ClubImageDto
        {
            public string ImageName { get; set; } = string.Empty;
            public int ImageIndex { get; set; }
        }

        public sealed class ClubDetailsDto
        {
            public ClubSummaryDto Club { get; set; } = new();
            public ClubSummaryDto ClubModel => Club;

            public long ClubId => Club.ClubId;
            public string Name => Club.Name;
            public string Description => Club.Description;
            public string ImageName => Club.ImageName;
            public string MainImageName => Club.MainImageName;
            public int State => Club.State;
            public int MemberCount => Club.MemberCount;
            public string PrimaryCategory => Club.PrimaryCategory;
            public int Visibility => Club.Visibility;
            public int Joinability => Club.Joinability;
            public bool AllowJuniors => Club.AllowJuniors;
            public int MinLevel => Club.MinLevel;
            public int ClubType => Club.ClubType;
            public List<string> CategoryTags { get; set; } = new();
            public List<string> CustomTags => CategoryTags;
            public List<ClubImageDto> AdditionalImages { get; set; } = new();
            public List<ClubImageDto> Images => AdditionalImages;
            public ClubPermissionsDto CreatorPermissions { get; set; } = new();
            public ClubPermissionsDto CoOwnerPermissions { get; set; } = new();
            public ClubPermissionsDto ModeratorPermissions { get; set; } = new();
            public int MyMembershipType { get; set; }
        }
    }
}
