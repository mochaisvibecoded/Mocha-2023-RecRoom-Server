using System;
using LiteDB;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs.DBClasses;
using Mocha2023.Controllers;
using static Mocha2023.Classes.DBs.DBClasses.PlayerDBClasses;
using System.Globalization;

namespace Mocha2023.Classes.DBs
{
    public class PlayerDB
    {
        private static readonly object SocialLock = new();
        private static readonly object ProgressionLock = new();
        private static readonly object PartyLock = new();
        private static readonly object GiftLock = new();
        private static long PartySequence = DateTime.UtcNow.Ticks & long.MaxValue;

        private static long GiftSequence =
            DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        private static long CheerSequence = DateTime.UtcNow.Ticks & long.MaxValue;
        private const int RoomLevelSeconds = 15 * 60;
        private const int MaximumLevel = 50;
        private const int MaximumCreditableHeartbeatGapSeconds = 120;
        private const int PresenceFreshnessSeconds = 120;
        private const int MaximumPendingGiftPackages = 200;
        public const int CheerCreditLimit = 15;
        private const long CheerCreditRefreshSeconds = 5 * 60 * 60;

        public static LiteDatabase PlayerDBFile =
            new LiteDatabase(Path.Combine(Program.dataDir, "DBs", "Players.db"));

        public static readonly ILiteCollection<FullPlayer> Players =
            PlayerDBFile.GetCollection<FullPlayer>("Players");

        private const string RRPlusSettingKey = "RecRoomPlus.IsActive";
        private const string InitialTokensSettingKey =
            "Mocha.InitialTokensGranted";
        private const string AlpacaShirtOwnershipResetSettingKey =
            "Mocha.AlpacaShirtOwnershipResetV1";
        public const long AlpacaShirtAvatarItemId = 1521;
        public const string AlpacaShirtAvatarItemDescriptor =
            "d0a9262f-5504-46a7-bb10-7507503db58e,941c046e-4e95-49f8-a7d7-19071fcc3c94,0440f08f-ef1d-49d8-942b-523056e8bb45,703ff56b-560d-4ff4-8c63-d195e879a328";
        public const int InitialTokenBalance = 1_000;
        private const string PlaceholderDisplayName =
            "Displaynamecouldbeverylong";
        private static readonly Random LevelBoxRng = new();

        private static int GetLevelBoxRarity(int level) => level switch
        {
            <= 19 => 10,
            <= 29 => 20,
            <= 39 => 30,
            <= 49 => 40,
            _ => 50
        };

        static PlayerDB()
        {
            LiteDbMaintenance.StartPeriodicCheckpoint("Players.db", PlayerDBFile);

            try
            {
                int repaired = RepairPlaceholderDisplayNames();
                if (repaired > 0)
                {
                    Console.WriteLine(
                        $"[ACCOUNT DISPLAYNAME MIGRATION] repaired={repaired}");
                }
            }
            catch (Exception exception)
            {

                Console.WriteLine(
                    $"[ACCOUNT DISPLAYNAME MIGRATION] failed={exception.Message}");
            }
        }

        private static int RepairPlaceholderDisplayNames()
        {
            int repaired = 0;

            foreach (FullPlayer account in Players.FindAll().ToList())
            {
                Player? player = account?.Player;
                if (player == null)
                    continue;

                string currentDisplayName =
                    player.DisplayName?.Trim() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(currentDisplayName) &&
                    !IsPlaceholderDisplayName(currentDisplayName))
                {
                    continue;
                }

                string username =
                    player.Username?.Trim().TrimStart('@') ?? string.Empty;
                string replacement = string.IsNullOrWhiteSpace(username)
                    ? $"Player{account.PlayerId}"
                    : username;

                if (string.Equals(
                    player.DisplayName,
                    replacement,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                player.DisplayName = replacement;
                if (Players.Update(account))
                    repaired++;
            }

            return repaired;
        }

        private static bool IsPlaceholderDisplayName(string? value)
        {
            return string.Equals(
                value?.Trim(),
                PlaceholderDisplayName,
                StringComparison.OrdinalIgnoreCase);
        }

        public static FullPlayer CreateAccount(
            Platforms platform,
            ulong platformId,
            bool isJunior,
            long? accountId = null,
            bool completeAccountCreation = true)
        {
            string username = NameGen.GetRandomName();

            var newPlayerData = new Player
            {
                Username = username,
                DisplayName = username,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow,
                ProfileImage = "DefaultPFP.png",
                AvailableUsernameChanges = 3,
                IsJunior = isJunior,
                Level = 1,
                XP = 0,

                PlayerExtra = new PlayerExtra
                {
                    Currencies = new List<PlayerCurrency>
                    {
                        new PlayerCurrency
                        {
                            CurrencyType = CurrencyType.RecCenterTokens,
                            BalanceType = BalanceType.NonPurchasedDefault,
                            Balance = InitialTokenBalance
                        }
                    },
                    Settings = new List<Setting>
                    {
                        new Setting
                        {
                            Key = InitialTokensSettingKey,
                            Value = "True"
                        },
                        new Setting
                        {
                            Key = AlpacaShirtOwnershipResetSettingKey,
                            Value = "True"
                        },
                        new Setting
                        {
                            Key = "Recroom.AccountCreation.HasStarted",
                            Value = "True"
                        },
                        new Setting
                        {
                            Key = "Recroom.AccountCreation.HasChosenUsername",
                            Value = completeAccountCreation ? "True" : "False"
                        },
                        new Setting
                        {
                            Key = "Recroom.AccountCreation.HasCreatedPassword",
                            Value = completeAccountCreation ? "True" : "False"
                        },
                        new Setting
                        {
                            Key = "Recroom.AccountCreation.HasFinished",
                            Value = completeAccountCreation ? "True" : "False"
                        },
                        new Setting
                        {
                            Key = "TUTORIAL_COMPLETE_MASK",
                            Value = completeAccountCreation ? "57" : "0"
                        },
                        new Setting
                        {
                            Key = "HAS_COMPLETED_ORIENTATION",
                            Value = "False"
                        },

                        new Setting
                        {
                            Key = RRPlusSettingKey,
                            Value = "False"
                        }
                    }
                }
            };

            var newFullPlayer = new FullPlayer
            {
                PlayerId = accountId ?? 0,

                PlatformIds = new List<mPlatformID>
                {
                    new mPlatformID
                    {
                        Platform = platform,
                        PlatformId = platformId
                    }
                },

                Player = newPlayerData,

                PlayerRoles = new List<PlayerRoles>(),

                AuthToken = Guid.NewGuid().ToString()
            };

            Players.Insert(newFullPlayer);
            EquipmentInventoryStore.EnsureInitialized(newFullPlayer.PlayerId);
            PlayerInventoryStore.EnsureInitialized(
                newFullPlayer.PlayerId,
                newFullPlayer.Player.PlayerExtra?.AvatarItems);
            RoomDB.GetOrCreatePlayerDorm(newFullPlayer.PlayerId);

            return newFullPlayer;
        }

        public static bool HasRRPlus(long playerId)
        {
            if (playerId <= 0)
                return false;

            var player = Players.FindById(playerId);

            if (player?.Player == null)
                return false;

            var setting = player.Player.PlayerExtra?.Settings?
                .FirstOrDefault(x => string.Equals(
                    x.Key,
                    RRPlusSettingKey,
                    StringComparison.OrdinalIgnoreCase));

            return setting != null &&
                string.Equals(setting.Value, "True", StringComparison.OrdinalIgnoreCase);
        }

        public static bool SetRRPlus(long playerId, bool enabled)
        {
            if (playerId <= 0)
                return false;

            var player = Players.FindById(playerId);

            if (player?.Player == null)
                return false;

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Settings ??= new List<Setting>();

            var setting = player.Player.PlayerExtra.Settings
                .FirstOrDefault(x => string.Equals(
                    x.Key,
                    RRPlusSettingKey,
                    StringComparison.OrdinalIgnoreCase));

            if (setting == null)
            {
                player.Player.PlayerExtra.Settings.Add(new Setting
                {
                    Key = RRPlusSettingKey,
                    Value = enabled ? "True" : "False"
                });
            }
            else
            {
                setting.Key = RRPlusSettingKey;
                setting.Value = enabled ? "True" : "False";
            }

            return Players.Update(player);
        }

        public static int ForceRRPlusForAllPlayers()
        {
            Console.WriteLine(
                "[RR+ MEMBERSHIP] ForceRRPlusForAllPlayers called but is disabled - no players were updated.");
            return 0;
        }

        public static bool ChangeAccountId(
            long currentAccountId,
            long newAccountId)
        {
            if (currentAccountId <= 0 ||
                newAccountId <= 0 ||
                currentAccountId == newAccountId)
            {
                return false;
            }

            if (Players.FindById(newAccountId) != null)
                return false;

            var player = Players.FindById(currentAccountId);

            if (player == null)
                return false;

            Players.Delete(currentAccountId);

            player.PlayerId = newAccountId;

            Players.Insert(player);

            return true;
        }

        public static bool GetLogins(
            Platforms platform,
            ulong platformId,
            out List<CachedLogins> accounts)
        {
            var results = Players
                .Find(x => x.PlatformIds
                    .Select(p => p.PlatformId)
                    .Any(id => id == platformId))
                .Where(p => p.PlatformIds.Any(
                    pid => pid.Platform == platform))
                .OrderByDescending(x => x.Player.LastLoginAt)
                .ToList();

            accounts = results
                .Select(p => new CachedLogins
                {
                    accountId = p.PlayerId,
                    lastLoginTime = p.Player.LastLoginAt,
                    platform = platform,
                    platformId = platformId.ToString(),

                    requirePassword = false
                })
                .ToList();

            return accounts.Count > 0;
        }

        private static PlayerDTOBase MapToDTO(
            FullPlayer player,
            bool accountMe)
        {
            int platformFlags = player.PlatformIds?.Aggregate(
                0,
                (acc, pid) => acc | (int)pid.Platform) ?? 0;

            var p = player.Player ?? new Player();

            PlayerDTOBase dto = accountMe
                ? new PlayerMeDTO()
                : new PlayerDTO();

            dto.accountId = player.PlayerId;
            dto.username = p.Username;
            dto.displayEmoji = p.DisplayEmoji ?? "";
            dto.displayName = GetClientDisplayName(p, player.PlayerId);
            dto.profileImage = p.ProfileImage;
            dto.bannerImage = p.BannerImage;
            dto.isJunior = p.IsJunior;
            dto.createdAt = p.CreatedAt;
            dto.platforms = platformFlags;
            dto.personalPronouns = p.PersonalPronouns;
            dto.identityFlags = p.IdentityFlags;

            if (accountMe && dto is PlayerMeDTO meDto)
            {
                meDto.availableUsernameChanges =
                    p.AvailableUsernameChanges;

                meDto.birthday = p.Birthday;
                meDto.email = p.Email;
                meDto.phone = null;
            }

            return dto;
        }

        private static string GetClientDisplayName(Player player, long playerId)
        {
            string displayName = player.DisplayName?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(displayName) &&
                !IsPlaceholderDisplayName(displayName))
            {
                return displayName;
            }

            string username = player.Username?.Trim().TrimStart('@') ??
                string.Empty;
            return string.IsNullOrWhiteSpace(username)
                ? $"Player{playerId}"
                : username;
        }

        public static List<PlayerDTOBase> GetAccountsBulk(
            List<long> playerIds,
            long? callerId = null)
        {
            var players = Players
                .Find(x => playerIds.Contains(x.PlayerId))
                .ToList();

            var results = players
                .Select(p => MapToDTO(
                    p,
                    callerId.HasValue &&
                    p.PlayerId == callerId.Value))
                .ToList();

            if (playerIds.Contains(1) &&
                !results.Any(account => account.accountId == 1))
            {
                results.Add(BuildCoachSystemAccount());
            }

            return results
                .OrderBy(a => a.accountId)
                .ToList();
        }

        public static PlayerDTOBase BuildCoachSystemAccount() => new PlayerDTO
        {
            accountId = 1,
            username = "Coach",
            displayName = "Coach",
            profileImage = "DefaultPFP.png",
            bannerImage = null,
            displayEmoji = string.Empty,
            isJunior = false,
            platforms = 0,
            personalPronouns = 0,
            identityFlags = 0,
            createdAt = new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        public static PlayerMeDTO? GetAccountMe(long accountId)
        {
            var player = Players.FindById(accountId);

            if (player == null)
                return null;

            return MapToDTO(player, true) as PlayerMeDTO;
        }

        public static bool UpdateUsername(
            long playerId,
            string username,
            bool initialAccountSetup = false)
        {
            var player = Players.FindById(playerId);

            if (player?.Player == null)
                return false;

            username = username.Trim().TrimStart('@');
            if (username.Length is < 3 or > 20 ||
                username.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_'))
            {
                return false;
            }

            if (Players.FindAll().Any(candidate =>
                    candidate.PlayerId != playerId &&
                    string.Equals(
                        candidate.Player?.Username,
                        username,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (!initialAccountSetup && player.Player.AvailableUsernameChanges <= 0)
                return false;

            player.Player.Username = username;
            if (initialAccountSetup)
            {
                player.Player.DisplayName = username;
                SetSettingValue(
                    player,
                    "Recroom.AccountCreation.HasChosenUsername",
                    "True");
            }
            else
            {
                player.Player.AvailableUsernameChanges--;
            }

            return Players.Update(player);
        }

        public static bool SetPassword(
            long playerId,
            string newPassword,
            string? oldPassword = null)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null || string.IsNullOrEmpty(newPassword) ||
                newPassword.Length < PasswordSecurity.MinPasswordLength ||
                newPassword.Length > PasswordSecurity.MaxPasswordLength)
                return false;

            if (!string.IsNullOrEmpty(player.Password) &&
                !PasswordSecurity.Verify(oldPassword, player.Password, out _))
            {
                return false;
            }

            player.Password = PasswordSecurity.Hash(newPassword);
            SetSettingValue(
                player,
                "Recroom.AccountCreation.HasCreatedPassword",
                "True");

            return Players.Update(player);
        }

        public static bool HasPassword(long playerId)
        {
            var player = Players.FindById(playerId);
            return !string.IsNullOrEmpty(player?.Password);
        }

        public static bool UpdateBirthday(long playerId, DateTime birthday)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null || birthday.Date > DateTime.UtcNow.Date)
                return false;

            player.Player.Birthday = birthday.Date;

            var today = DateTime.UtcNow.Date;
            int age = today.Year - birthday.Year;
            if (birthday.Date > today.AddYears(-age))
                age--;
            player.Player.IsJunior = age < 13;

            return Players.Update(player);
        }

        private static void SetSettingValue(
            FullPlayer player,
            string key,
            string value)
        {
            if (player.Player == null)
                return;

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Settings ??= new List<Setting>();

            var setting = player.Player.PlayerExtra.Settings.FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (setting == null)
            {
                player.Player.PlayerExtra.Settings.Add(new Setting
                {
                    Key = key,
                    Value = value
                });
            }
            else
            {
                setting.Value = value;
            }
        }

        public static bool UpdateDisplayName(
            long playerId,
            string displayName)
        {
            var player = Players.FindById(playerId);

            if (player?.Player == null)
                return false;

            displayName = displayName?.Trim() ?? string.Empty;
            player.Player.DisplayName = string.IsNullOrWhiteSpace(displayName) ||
                IsPlaceholderDisplayName(displayName)
                ? GetClientDisplayName(player.Player, playerId)
                : displayName;

            return Players.Update(player);
        }

        public static bool UpdatePersonalPronouns(long playerId, int personalPronouns)
        {

            if (personalPronouns < 0 || (personalPronouns & ~0x3F) != 0)
                return false;

            var player = Players.FindById(playerId);
            if (player?.Player == null)
                return false;

            player.Player.PersonalPronouns = personalPronouns;
            return Players.Update(player);
        }

        public static bool UpdateIdentityFlags(
    long playerId,
    int identityFlags)
        {
            if (identityFlags < 0)
                return false;

            var player = Players.FindById(playerId);

            if (player?.Player == null)
                return false;

            player.Player.IdentityFlags = identityFlags;

            return Players.Update(player);
        }

        public static IReadOnlyList<int> GetWishlist(long accountId)
        {
            var player = Players.FindById(accountId);
            string? stored = player?.Player?.PlayerExtra?.Settings?
                .FirstOrDefault(setting => string.Equals(
                    setting.Key,
                    "Store.Wishlist",
                    StringComparison.OrdinalIgnoreCase))?.Value;
            if (string.IsNullOrWhiteSpace(stored))
                return Array.Empty<int>();

            return stored.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.TryParse(value, out int id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .Take(500)
                .ToList();
        }

        public static bool SetWishlistItem(long accountId, int purchasableItemId, bool wished)
        {
            if (purchasableItemId <= 0)
                return false;

            var player = Players.FindById(accountId);
            if (player?.Player == null)
                return false;

            var ids = GetWishlist(accountId).ToHashSet();
            if (wished)
            {
                if (ids.Count >= 500 && !ids.Contains(purchasableItemId))
                    return false;
                ids.Add(purchasableItemId);
            }
            else
            {
                ids.Remove(purchasableItemId);
            }

            SetSettingValue(
                player,
                "Store.Wishlist",
                string.Join(',', ids.OrderBy(value => value)));
            return Players.Update(player);
        }

        public static bool SetAvatar(
            long accountId,
            Avatar avatar)
        {
            var player = Players.FindById(accountId);

            if (player?.Player == null)
                return false;

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Avatar = avatar;

            return Players.Update(player);
        }

        public static void SetPlayerSetting(
            string key,
            string value,
            long playerId)
        {
            key = key?.Trim() ?? string.Empty;
            value ??= string.Empty;
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128 ||
                value.Length > 4_096 || key.Any(char.IsControl) ||
                value.Any(ch => ch == '\0') || playerId <= 0)
                return;

            if (key is
                "SplitTestAssignedSegments" or
                "Growth.LastEmailPromptTime")
            {
                return;
            }

            var player = Players.FindById(playerId);

            if (player?.Player == null)
                return;

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Settings ??= new List<Setting>();

            var settings = player.Player.PlayerExtra.Settings;

            var existingSetting = settings.FirstOrDefault(
                s => s.Key == key);

            if (existingSetting != null)
            {
                existingSetting.Value = value;
            }
            else
            {
                settings.Add(new Setting
                {
                    Key = key,
                    Value = value
                });
            }

            if (string.Equals(
                    key,
                    "HAS_COMPLETED_ORIENTATION",
                    StringComparison.OrdinalIgnoreCase) &&
                bool.TryParse(value, out bool completedOrientation) &&
                completedOrientation)
            {
                SetSettingValue(
                    player,
                    "Recroom.AccountCreation.HasFinished",
                    "True");
                SetSettingValue(player, "TUTORIAL_COMPLETE_MASK", "57");
            }

            Players.Update(player);
        }

        public static bool NeedsOrientation(FullPlayer? player)
        {
            var settings = player?.Player?.PlayerExtra?.Settings;
            if (settings == null)
                return false;

            var orientation = settings.FirstOrDefault(setting =>
                string.Equals(
                    setting.Key,
                    "HAS_COMPLETED_ORIENTATION",
                    StringComparison.OrdinalIgnoreCase));
            if (orientation != null)
            {
                return !bool.TryParse(orientation.Value, out bool completed) ||
                       !completed;
            }

            var accountCreation = settings.FirstOrDefault(setting =>
                string.Equals(
                    setting.Key,
                    "Recroom.AccountCreation.HasFinished",
                    StringComparison.OrdinalIgnoreCase));
            return accountCreation != null &&
                   !string.Equals(
                       accountCreation.Value,
                       "True",
                       StringComparison.OrdinalIgnoreCase);
        }

        public static List<PlayerProgressionDTO> GetProgressionBulk(
            List<long> playerIds)
        {
            var players = Players
                .Find(x => playerIds.Contains(x.PlayerId))
                .ToList();

            return players
                .Select(p => new PlayerProgressionDTO
                {
                    PlayerId = p.PlayerId,
                    Level = p.Player?.Level ?? 1,
                    XP = p.Player?.XP ?? 0
                })
                .ToList();
        }

        public static PlayerProgressionDTO? SetProgression(
            long playerId,
            int level,
            int xp)
        {
            if (playerId <= 0)
                return null;

            int oldLevel;
            PlayerProgressionDTO result;

            lock (ProgressionLock)
            {
                var player = Players.FindById(playerId);
                if (player?.Player == null)
                    return null;

                oldLevel = player.Player.Level;
                player.Player.Level = Math.Clamp(level, 1, 50);
                player.Player.XP = Math.Max(0, xp);

                if (!Players.Update(player))
                    return null;

                result = new PlayerProgressionDTO
                {
                    PlayerId = playerId,
                    Level = player.Player.Level,
                    XP = player.Player.XP
                };
            }

            _ = NotiController.NotifyProgressionAsync(
                playerId,
                oldLevel,
                result.Level,
                result.XP);

            if (result.Level > oldLevel)
                GrantLevelUpBox(playerId, oldLevel, result.Level);

            return result;
        }

        public static PlayerProgressionDTO? AddExperience(
            long playerId,
            int xpToAdd,
            int? resultingLevel = null)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null)
                return null;

            int level = resultingLevel ?? player.Player.Level;
            int xp = Math.Max(0, player.Player.XP + xpToAdd);
            return SetProgression(playerId, level, xp);
        }

        public static List<GiftPackage> GetPendingGiftPackages(long playerId)
        {
            if (playerId <= 0)
                return new List<GiftPackage>();

            lock (GiftLock)
            {
                var player = Players.FindById(playerId);
                if (player?.Player == null)
                    return new List<GiftPackage>();

                player.Player.PlayerExtra ??= new PlayerExtra();
                player.Player.PlayerExtra.PendingGiftPackages ??=
                    new List<GiftPackage>();

                bool repaired = RepairPendingGiftIdsNoLock(
                    player.Player.PlayerExtra.PendingGiftPackages);
                if (repaired)
                    Players.Update(player);

                return player.Player.PlayerExtra.PendingGiftPackages
                    .OrderBy(gift => gift.GiftPackageId)
                    .ToList();
            }
        }

        public static ClearOutgoingGiftsResult ClearPendingGiftsFromSender(
            long fromPlayerId)
        {
            var result = new ClearOutgoingGiftsResult();
            if (fromPlayerId <= 0)
                return result;

            lock (GiftLock)
            {
                foreach (var account in Players.FindAll())
                {
                    if (account?.Player?.PlayerExtra?.PendingGiftPackages == null ||
                        account.Player.PlayerExtra.PendingGiftPackages.Count == 0)
                        continue;

                    int removed = account.Player.PlayerExtra.PendingGiftPackages
                        .RemoveAll(gift => gift.FromPlayerId == fromPlayerId);

                    if (removed <= 0)
                        continue;

                    if (Players.Update(account))
                    {
                        result.RemovedBoxes += removed;
                        result.AffectedPlayers++;
                    }
                }
            }

            return result;
        }

        public class ClearOutgoingGiftsResult
        {
            public int RemovedBoxes { get; set; }
            public int AffectedPlayers { get; set; }
        }

        public static bool RemoveGiftPackage(long playerId, long giftPackageId)
        {
            if (playerId <= 0 || giftPackageId <= 0)
                return false;

            lock (GiftLock)
            {
                var player = Players.FindById(playerId);
                var pending = player?.Player?.PlayerExtra?.PendingGiftPackages;
                if (pending == null)
                    return false;

                int removed = pending.RemoveAll(gift => gift.GiftPackageId == giftPackageId);
                return removed > 0 && Players.Update(player!);
            }
        }

        public static int ClearPendingGiftPackages(long playerId)
        {
            if (playerId <= 0)
                return 0;

            lock (GiftLock)
            {
                var player = Players.FindById(playerId);
                var pending = player?.Player?.PlayerExtra?.PendingGiftPackages;
                if (pending == null || pending.Count == 0)
                    return 0;

                int removed = pending.Count;
                pending.Clear();
                return Players.Update(player!) ? removed : 0;
            }
        }

        public static GiftPackage? QueueGiftPackage(
            long playerId,
            GiftPackage gift)
        {
            if (playerId <= 0 || gift == null)
                return null;

            lock (GiftLock)
            {
                var player = Players.FindById(playerId);
                if (player?.Player == null)
                    return null;

                player.Player.PlayerExtra ??= new PlayerExtra();
                player.Player.PlayerExtra.PendingGiftPackages ??=
                    new List<GiftPackage>();

                if (player.Player.PlayerExtra.PendingGiftPackages.Count >=
                    MaximumPendingGiftPackages)
                {
                    Console.WriteLine(
                        $"[GIFT QUEUE] recipient={playerId} rejected=queue_full " +
                        $"limit={MaximumPendingGiftPackages}");
                    return null;
                }

                if (gift.GiftPackageId <= 0 ||
                    gift.GiftPackageId > int.MaxValue ||
                    player.Player.PlayerExtra.PendingGiftPackages.Any(value =>
                        value.GiftPackageId == gift.GiftPackageId))
                {
                    gift.GiftPackageId = NextGiftPackageIdNoLock(
                        player.Player.PlayerExtra.PendingGiftPackages);
                }

                gift.PlayerId = checked((int)playerId);
                player.Player.PlayerExtra.PendingGiftPackages.Add(gift);

                return Players.Update(player) ? gift : null;
            }
        }

        public enum FriendotronGiftStatus
        {
            Success,
            InvalidRequest,
            SenderNotFound,
            RecipientNotFound,
            DailyLimitReached,
            RecipientQueueFull,
            PersistenceFailed
        }

        public static FriendotronGiftStatus QueueFriendotronGift(
            long senderPlayerId,
            long recipientPlayerId,
            GiftPackage gift,
            out GiftPackage? queuedGift,
            out DateTime nextAvailableAtUtc)
        {
            queuedGift = null;
            nextAvailableAtUtc = DateTime.UtcNow;

            if (senderPlayerId is <= 0 or > int.MaxValue ||
                recipientPlayerId is <= 0 or > int.MaxValue ||
                gift == null)
            {
                return FriendotronGiftStatus.InvalidRequest;
            }

            lock (GiftLock)
            {
                FullPlayer? sender = Players.FindById(senderPlayerId);
                if (sender?.Player == null)
                    return FriendotronGiftStatus.SenderNotFound;

                FullPlayer? recipient = senderPlayerId == recipientPlayerId
                    ? sender
                    : Players.FindById(recipientPlayerId);
                if (recipient?.Player == null)
                    return FriendotronGiftStatus.RecipientNotFound;

                sender.Player.PlayerExtra ??= new PlayerExtra();
                recipient.Player.PlayerExtra ??= new PlayerExtra();
                recipient.Player.PlayerExtra.PendingGiftPackages ??=
                    new List<GiftPackage>();

                long utcDay = DateTimeOffset.UtcNow.ToUnixTimeSeconds() /
                    (24L * 60L * 60L);
                nextAvailableAtUtc = DateTimeOffset.FromUnixTimeSeconds(
                        checked((utcDay + 1) * 24L * 60L * 60L))
                    .UtcDateTime;

                if (sender.Player.PlayerExtra.FriendotronLastSentUtcDay >=
                    utcDay)
                {
                    return FriendotronGiftStatus.DailyLimitReached;
                }

                if (recipient.Player.PlayerExtra.PendingGiftPackages.Count >=
                    MaximumPendingGiftPackages)
                {
                    return FriendotronGiftStatus.RecipientQueueFull;
                }

                if (gift.GiftPackageId <= 0 ||
                    gift.GiftPackageId > int.MaxValue ||
                    recipient.Player.PlayerExtra.PendingGiftPackages.Any(value =>
                        value.GiftPackageId == gift.GiftPackageId))
                {
                    gift.GiftPackageId = NextGiftPackageIdNoLock(
                        recipient.Player.PlayerExtra.PendingGiftPackages);
                }

                gift.FromPlayerId = checked((int)senderPlayerId);
                gift.PlayerId = checked((int)recipientPlayerId);
                recipient.Player.PlayerExtra.PendingGiftPackages.Add(gift);
                long previousSentUtcDay =
                    sender.Player.PlayerExtra.FriendotronLastSentUtcDay;
                sender.Player.PlayerExtra.FriendotronLastSentUtcDay = utcDay;

                int expectedUpdates = senderPlayerId == recipientPlayerId
                    ? 1
                    : 2;
                int updated = expectedUpdates == 1
                    ? (Players.Update(sender) ? 1 : 0)
                    : Players.Update(new[] { sender, recipient });
                if (updated != expectedUpdates)
                {
                    recipient.Player.PlayerExtra.PendingGiftPackages.Remove(gift);
                    sender.Player.PlayerExtra.FriendotronLastSentUtcDay =
                        previousSentUtcDay;
                    return FriendotronGiftStatus.PersistenceFailed;
                }

                queuedGift = gift;
                return FriendotronGiftStatus.Success;
            }
        }

        private static bool RepairPendingGiftIdsNoLock(
            List<GiftPackage> gifts)
        {
            bool changed = false;
            var used = new HashSet<long>();
            foreach (GiftPackage gift in gifts)
            {
                if (gift.GiftPackageId > 0 &&
                    gift.GiftPackageId <= int.MaxValue &&
                    used.Add(gift.GiftPackageId))
                {
                    GiftSequence = Math.Max(GiftSequence, gift.GiftPackageId);
                    continue;
                }

                gift.GiftPackageId = NextGiftPackageIdNoLock(gifts, used);
                used.Add(gift.GiftPackageId);
                changed = true;
            }

            return changed;
        }

        private static long NextGiftPackageIdNoLock(
            IEnumerable<GiftPackage> gifts,
            HashSet<long>? reserved = null)
        {
            var used = reserved ?? gifts
                .Where(value => value.GiftPackageId > 0 &&
                    value.GiftPackageId <= int.MaxValue)
                .Select(value => value.GiftPackageId)
                .ToHashSet();

            for (int attempt = 0; attempt < 100000; attempt++)
            {
                GiftSequence++;
                if (GiftSequence <= 0 || GiftSequence > int.MaxValue)
                    GiftSequence = 100000;
                if (!used.Contains(GiftSequence))
                    return GiftSequence;
            }

            throw new InvalidOperationException(
                "Unable to allocate a client-safe gift package ID.");
        }

        public static GiftPackage? ConsumeGiftPackage(
            long playerId,
            long giftPackageId)
        {
            if (playerId <= 0 || giftPackageId <= 0)
                return null;

            lock (GiftLock)
            {
                var player = Players.FindById(playerId);
                if (player?.Player == null)
                    return null;

                player.Player.PlayerExtra ??= new PlayerExtra();
                player.Player.PlayerExtra.PendingGiftPackages ??=
                    new List<GiftPackage>();
                player.Player.PlayerExtra.AvatarItems ??= new List<string>();

                GiftPackage? gift =
                    player.Player.PlayerExtra.PendingGiftPackages.FirstOrDefault(
                        value => value.GiftPackageId == giftPackageId);

                if (gift == null)
                    return null;

                if (!string.IsNullOrWhiteSpace(gift.AvatarItemDesc) &&
                    !player.Player.PlayerExtra.AvatarItems.Contains(
                        gift.AvatarItemDesc,
                        StringComparer.OrdinalIgnoreCase))
                {
                    player.Player.PlayerExtra.AvatarItems.Add(
                        gift.AvatarItemDesc);
                    PlayerInventoryStore.SetAvatarItemOwned(
                        playerId,
                        gift.AvatarItemDesc,
                        avatarItemId: 0,
                        friendlyName: null,
                        owned: true,
                        legacyAvatarDescriptors: player.Player.PlayerExtra.AvatarItems);
                }

                if (!string.IsNullOrWhiteSpace(gift.ConsumableItemDesc))
                {
                    PlayerInventoryStore.AddConsumable(
                        playerId,
                        gift.ConsumableItemDesc,
                        consumableItemId: 0,
                        friendlyName: null,
                        amount: Math.Clamp(gift.ConsumableQuantity, 1, 100000));
                }

                if (!string.IsNullOrWhiteSpace(gift.EquipmentPrefabName) &&
                    !string.IsNullOrWhiteSpace(gift.EquipmentModificationGuid))
                {
                    EquipmentInventoryStore.AddOwnedItem(
                        playerId,
                        gift.EquipmentPrefabName,
                        gift.EquipmentModificationGuid,
                        friendlyName: gift.FriendlyName,
                        tooltip: gift.Tooltip,
                        rarity: gift.Rarity > 0 ? gift.Rarity : null,
                        thumbnailImage: gift.ThumbnailImage);
                }

                if (gift.Currency != 0)
                {
                    player.Player.PlayerExtra.Currencies ??=
                        new List<PlayerCurrency>();

                    CurrencyType currencyType =
                        Enum.IsDefined(typeof(CurrencyType), gift.CurrencyType)
                            ? (CurrencyType)gift.CurrencyType
                            : CurrencyType.RecCenterTokens;

                    PlayerCurrency? currency =
                        player.Player.PlayerExtra.Currencies.FirstOrDefault(
                            value => value.CurrencyType == currencyType);

                    if (currency == null)
                    {
                        currency = new PlayerCurrency
                        {
                            CurrencyType = currencyType,
                            BalanceType = BalanceType.NonPurchasedDefault
                        };
                        player.Player.PlayerExtra.Currencies.Add(currency);
                    }

                    long adjustedBalance =
                        (long)currency.Balance + gift.Currency;
                    currency.Balance = (int)Math.Clamp(
                        adjustedBalance,
                        (long)int.MinValue,
                        (long)int.MaxValue);
                }

                if (gift.XP > 0)
                    player.Player.XP = Math.Max(0, player.Player.XP + gift.XP);

                player.Player.PlayerExtra.PendingGiftPackages.Remove(gift);
                return Players.Update(player) ? gift : null;
            }
        }

        public static PartySnapshot? GetPartySnapshot(long playerId)
        {
            lock (PartyLock)
                return GetPartySnapshotNoLock(playerId);
        }

        public static PartySnapshot? InviteToParty(
            long inviterPlayerId,
            long targetPlayerId)
        {
            if (inviterPlayerId <= 0 ||
                targetPlayerId <= 0 ||
                inviterPlayerId == targetPlayerId)
            {
                return null;
            }

            PartySnapshot? party;

            lock (PartyLock)
            {
                var inviter = Players.FindById(inviterPlayerId);
                var target = Players.FindById(targetPlayerId);
                if (inviter?.Player == null || target?.Player == null)
                    return null;

                party = EnsurePartyNoLock(inviterPlayerId);
                if (party == null || party.MemberPlayerIds.Contains(targetPlayerId))
                    return party;

                target.Player.PlayerExtra ??= new PlayerExtra();
                target.Player.PlayerExtra.PendingPartyInvites ??= new List<PartyInviteRecord>();

                DateTime expiresBefore = DateTime.UtcNow.AddMinutes(-10);
                target.Player.PlayerExtra.PendingPartyInvites.RemoveAll(invite =>
                    invite.CreatedAt < expiresBefore ||
                    invite.PartyId == party.PartyId ||
                    invite.InviterPlayerId == inviterPlayerId);

                target.Player.PlayerExtra.PendingPartyInvites.Add(new PartyInviteRecord
                {
                    PartyId = party.PartyId,
                    InviterPlayerId = inviterPlayerId,
                    CreatedAt = DateTime.UtcNow
                });

                if (!Players.Update(target))
                    return null;
            }

            return party;
        }

        public static PartySnapshot? AcceptPartyInvite(
            long inviteePlayerId,
            long inviterPlayerId)
        {
            if (inviteePlayerId <= 0 || inviterPlayerId <= 0)
                return null;

            PartySnapshot? party;
            List<long> notifyPlayerIds;

            lock (PartyLock)
            {
                var invitee = Players.FindById(inviteePlayerId);
                var inviter = Players.FindById(inviterPlayerId);
                if (invitee?.Player == null || inviter?.Player == null)
                    return null;

                invitee.Player.PlayerExtra ??= new PlayerExtra();
                invitee.Player.PlayerExtra.PendingPartyInvites ??= new List<PartyInviteRecord>();

                PartyInviteRecord? invite = invitee.Player.PlayerExtra.PendingPartyInvites
                    .Where(value => value.CreatedAt >= DateTime.UtcNow.AddMinutes(-10))
                    .OrderByDescending(value => value.CreatedAt)
                    .FirstOrDefault(value => value.InviterPlayerId == inviterPlayerId);

                party = EnsurePartyNoLock(inviterPlayerId);
                if (party == null ||
                    invite == null ||
                    invite.PartyId != party.PartyId ||
                    party.MemberPlayerIds.Count >= 8)
                {
                    return null;
                }

                RemovePlayerFromPartyNoLock(inviteePlayerId);

                party.MemberPlayerIds = party.MemberPlayerIds
                    .Append(inviteePlayerId)
                    .Where(value => value > 0)
                    .Distinct()
                    .ToList();

                invitee.Player.PlayerExtra.PendingPartyInvites.RemoveAll(value =>
                    value.PartyId == party.PartyId ||
                    value.InviterPlayerId == inviterPlayerId ||
                    value.CreatedAt < DateTime.UtcNow.AddMinutes(-10));

                Players.Update(invitee);
                PersistPartyNoLock(party);
                notifyPlayerIds = party.MemberPlayerIds.ToList();
            }

            _ = NotiController.NotifyPartyUpdatedAsync(
                notifyPlayerIds,
                CreatePartyNotificationPayload(party));
            return party;
        }

        public static PartySnapshot? LeaveParty(long playerId)
        {
            if (playerId <= 0)
                return null;

            PartySnapshot? remainingParty;
            List<long> notifyPlayerIds;

            lock (PartyLock)
            {
                PartySnapshot? existing = GetPartySnapshotNoLock(playerId);
                if (existing == null)
                {
                    ClearPartyStateNoLock(playerId);
                    return null;
                }

                notifyPlayerIds = existing.MemberPlayerIds.ToList();
                remainingParty = RemovePlayerFromPartyNoLock(playerId);
            }

            var payload = remainingParty ?? new PartySnapshot
            {
                PartyId = 0,
                LeaderPlayerId = 0,
                MemberPlayerIds = new List<long>()
            };

            _ = NotiController.NotifyPartyUpdatedAsync(
                notifyPlayerIds.Append(playerId),
                CreatePartyNotificationPayload(payload));

            return remainingParty;
        }

        public static bool DeclinePartyInvite(
            long inviteePlayerId,
            long inviterPlayerId)
        {
            lock (PartyLock)
            {
                var invitee = Players.FindById(inviteePlayerId);
                if (invitee?.Player == null)
                    return false;

                invitee.Player.PlayerExtra ??= new PlayerExtra();
                invitee.Player.PlayerExtra.PendingPartyInvites ??= new List<PartyInviteRecord>();

                int removed = invitee.Player.PlayerExtra.PendingPartyInvites.RemoveAll(value =>
                    value.InviterPlayerId == inviterPlayerId ||
                    value.CreatedAt < DateTime.UtcNow.AddMinutes(-10));

                return removed > 0 && Players.Update(invitee);
            }
        }

        public static List<PartyInviteRecord> GetPendingPartyInvites(long playerId)
        {
            lock (PartyLock)
            {
                var player = Players.FindById(playerId);
                if (player?.Player == null)
                    return new List<PartyInviteRecord>();

                player.Player.PlayerExtra ??= new PlayerExtra();
                player.Player.PlayerExtra.PendingPartyInvites ??= new List<PartyInviteRecord>();
                player.Player.PlayerExtra.PendingPartyInvites.RemoveAll(value =>
                    value.CreatedAt < DateTime.UtcNow.AddMinutes(-10));
                Players.Update(player);

                return player.Player.PlayerExtra.PendingPartyInvites
                    .OrderByDescending(value => value.CreatedAt)
                    .ToList();
            }
        }

        private static PartySnapshot? EnsurePartyNoLock(long playerId)
        {
            PartySnapshot? existing = GetPartySnapshotNoLock(playerId);
            if (existing != null)
                return existing;

            var player = Players.FindById(playerId);
            if (player?.Player == null)
                return null;

            long partyId = Interlocked.Increment(ref PartySequence);
            if (partyId <= 0)
            {
                PartySequence = DateTime.UtcNow.Ticks & long.MaxValue;
                partyId = Interlocked.Increment(ref PartySequence);
            }

            var party = new PartySnapshot
            {
                PartyId = partyId,
                LeaderPlayerId = playerId,
                MemberPlayerIds = new List<long> { playerId }
            };

            PersistPartyNoLock(party);
            return party;
        }

        private static PartySnapshot? GetPartySnapshotNoLock(long playerId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null)
                return null;

            player.Player.PlayerExtra ??= new PlayerExtra();
            PlayerExtra extra = player.Player.PlayerExtra;
            extra.PartyMemberPlayerIds ??= new List<long>();

            if (extra.PartyId <= 0)
                return null;

            List<long> members = extra.PartyMemberPlayerIds
                .Append(playerId)
                .Where(value => value > 0 && Players.FindById(value)?.Player != null)
                .Distinct()
                .ToList();

            if (members.Count == 0)
                return null;

            long leader = members.Contains(extra.PartyLeaderPlayerId)
                ? extra.PartyLeaderPlayerId
                : members[0];

            return new PartySnapshot
            {
                PartyId = extra.PartyId,
                LeaderPlayerId = leader,
                MemberPlayerIds = members
            };
        }

        private static PartySnapshot? RemovePlayerFromPartyNoLock(long playerId)
        {
            PartySnapshot? party = GetPartySnapshotNoLock(playerId);
            if (party == null)
            {
                ClearPartyStateNoLock(playerId);
                return null;
            }

            party.MemberPlayerIds.RemoveAll(value => value == playerId);
            ClearPartyStateNoLock(playerId);

            if (party.MemberPlayerIds.Count <= 1)
            {
                foreach (long remainingPlayerId in party.MemberPlayerIds)
                    ClearPartyStateNoLock(remainingPlayerId);
                return null;
            }

            if (party.LeaderPlayerId == playerId ||
                !party.MemberPlayerIds.Contains(party.LeaderPlayerId))
            {
                party.LeaderPlayerId = party.MemberPlayerIds[0];
            }

            PersistPartyNoLock(party);
            return party;
        }

        private static void PersistPartyNoLock(PartySnapshot party)
        {
            party.MemberPlayerIds = party.MemberPlayerIds
                .Where(value => value > 0)
                .Distinct()
                .ToList();

            if (!party.MemberPlayerIds.Contains(party.LeaderPlayerId))
                party.LeaderPlayerId = party.MemberPlayerIds.FirstOrDefault();

            foreach (long memberPlayerId in party.MemberPlayerIds)
            {
                var member = Players.FindById(memberPlayerId);
                if (member?.Player == null)
                    continue;

                member.Player.PlayerExtra ??= new PlayerExtra();
                member.Player.PlayerExtra.PartyId = party.PartyId;
                member.Player.PlayerExtra.PartyLeaderPlayerId = party.LeaderPlayerId;
                member.Player.PlayerExtra.PartyMemberPlayerIds = party.MemberPlayerIds.ToList();
                Players.Update(member);
            }
        }

        private static object CreatePartyNotificationPayload(PartySnapshot party) => new
        {
            partyId = party.PartyId,
            leaderPlayerId = party.LeaderPlayerId,
            memberPlayerIds = party.MemberPlayerIds.ToArray()
        };

        private static void ClearPartyStateNoLock(long playerId)
        {
            var player = Players.FindById(playerId);
            if (player?.Player == null)
                return;

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.PartyId = 0;
            player.Player.PlayerExtra.PartyLeaderPlayerId = 0;
            player.Player.PlayerExtra.PartyMemberPlayerIds = new List<long>();
            Players.Update(player);
        }

        public static List<Reputation> GetReputationBulk(
            List<long> playerIds)
        {
            lock (SocialLock)
            {
                var allPlayers = Players.FindAll().ToList();
                var players = allPlayers
                    .Where(x => playerIds.Contains(x.PlayerId))
                    .ToList();
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var result = new List<Reputation>(players.Count);

                foreach (FullPlayer p in players)
                {
                    var rep = p.Player?.Reputation ?? new Reputation();
                    if (p.Player != null)
                    {
                        p.Player.Reputation = rep;
                        if (RefreshCheerCreditsNoLock(rep, p.PlayerId, now))
                            Players.Update(p);
                    }

                    bool isDeveloper = HasRole(
                        p,
                        PlayerRoles.Developer);
                    bool isCommunityTeam = HasRole(
                        p,
                        PlayerRoles.CommunityTeam);
                    bool hasStaffBadgeAccess =
                        isDeveloper || isCommunityTeam;

                    result.Add(new Reputation
                    {
                        AccountId = p.PlayerId,
                        IsCheerful =
                            hasStaffBadgeAccess || rep.IsCheerful,
                        Noteriety = rep.Noteriety,
                        SelectedCheer = NormalizeSelectedStaffBadge(
                            rep.SelectedCheer,
                            isDeveloper,
                            isCommunityTeam),
                        CheerCredit = Math.Clamp(
                            rep.CheerCredit,
                            0,
                            CheerCreditLimit),
                        CheerGeneral = hasStaffBadgeAccess
                            ? Math.Max(10, rep.CheerGeneral)
                            : rep.CheerGeneral,
                        CheerHelpful = hasStaffBadgeAccess
                            ? Math.Max(10, rep.CheerHelpful)
                            : rep.CheerHelpful,
                        CheerCreative = hasStaffBadgeAccess
                            ? Math.Max(10, rep.CheerCreative)
                            : rep.CheerCreative,
                        CheerGreatHost = hasStaffBadgeAccess
                            ? Math.Max(10, rep.CheerGreatHost)
                            : rep.CheerGreatHost,
                        CheerSportsman = hasStaffBadgeAccess
                            ? Math.Max(10, rep.CheerSportsman)
                            : rep.CheerSportsman,
                        SubscriberCount =
                            RelationshipDB.GetSubscriberCount(p.PlayerId),
                        SubscribedCount =
                            RelationshipDB.GetSubscribedCount(p.PlayerId)
                    });
                }

                return result;
            }
        }

        public static bool GrantDeveloperCheerAccess(
            FullPlayer account,
            bool selectDeveloperBadge)
        {
            return GrantStaffCheerAccess(
                account,
                CheerCategory.RecRoomDeveloper,
                selectDeveloperBadge);
        }

        public static bool GrantCommunityTeamCheerAccess(
            FullPlayer account,
            bool selectCommunityTeamBadge)
        {
            return GrantStaffCheerAccess(
                account,
                CheerCategory.RecRoomCommunityTeam,
                selectCommunityTeamBadge);
        }

        private static bool GrantStaffCheerAccess(
            FullPlayer account,
            CheerCategory staffBadge,
            bool selectStaffBadge)
        {
            if (account.Player == null ||
                staffBadge is not (
                    CheerCategory.RecRoomDeveloper or
                    CheerCategory.RecRoomCommunityTeam))
            {
                return false;
            }

            account.Player.Reputation ??= new Reputation();
            Reputation reputation = account.Player.Reputation;
            bool changed = false;

            if (!reputation.IsCheerful)
            {
                reputation.IsCheerful = true;
                changed = true;
            }

            if (reputation.CheerGeneral < 10)
            {
                reputation.CheerGeneral = 10;
                changed = true;
            }

            if (reputation.CheerHelpful < 10)
            {
                reputation.CheerHelpful = 10;
                changed = true;
            }

            if (reputation.CheerCreative < 10)
            {
                reputation.CheerCreative = 10;
                changed = true;
            }

            if (reputation.CheerGreatHost < 10)
            {
                reputation.CheerGreatHost = 10;
                changed = true;
            }

            if (reputation.CheerSportsman < 10)
            {
                reputation.CheerSportsman = 10;
                changed = true;
            }

            if (selectStaffBadge &&
                reputation.SelectedCheer != staffBadge)
            {
                reputation.SelectedCheer = staffBadge;
                changed = true;
            }

            return changed;
        }

        private static bool HasRole(
            FullPlayer account,
            PlayerRoles role)
        {
            return account.PlayerRoles?.Contains(role) == true;
        }

        private static CheerCategory NormalizeSelectedStaffBadge(
            CheerCategory selectedCheer,
            bool isDeveloper,
            bool isCommunityTeam)
        {
            if (selectedCheer == CheerCategory.RecRoomDeveloper &&
                !isDeveloper)
            {
                return CheerCategory.General;
            }

            if (selectedCheer ==
                    CheerCategory.RecRoomCommunityTeam &&
                !isCommunityTeam)
            {
                return CheerCategory.General;
            }

            return selectedCheer;
        }

        public static int EnsureSocialDefaultsForAllPlayers()
        {
            lock (SocialLock)
            {
                var allPlayers = Players.FindAll().ToList();
                int updated = 0;

                updated += RelationshipDB.MigrateLegacyData(allPlayers);

                foreach (var account in allPlayers)
                {
                    if (account.Player == null)
                        continue;

                    bool changed = false;
                    account.Player.Relationships ??= new List<PlayerRelationship>();
                    account.Player.ReceivedCheers ??= new List<PlayerCheerRecord>();
                    account.Player.SubscribedAccountIds ??= new List<long>();
                    account.Player.Reputation ??= new Reputation();

                    if (account.Player.Reputation.AccountId != account.PlayerId)
                    {
                        account.Player.Reputation.AccountId = account.PlayerId;
                        changed = true;
                    }

                    changed |= RefreshCheerCreditsNoLock(
                        account.Player.Reputation,
                        account.PlayerId,
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                    bool isDeveloper = HasRole(
                        account,
                        PlayerRoles.Developer);
                    bool isCommunityTeam = HasRole(
                        account,
                        PlayerRoles.CommunityTeam);

                    Reputation reputation =
                        account.Player.Reputation;
                    bool hasNoEarnedCheer =
                        reputation.CheerGeneral == 0 &&
                        reputation.CheerHelpful == 0 &&
                        reputation.CheerCreative == 0 &&
                        reputation.CheerGreatHost == 0 &&
                        reputation.CheerSportsman == 0;

                    if (isDeveloper)
                    {
                        changed |= GrantDeveloperCheerAccess(
                            account,
                            selectDeveloperBadge:
                                hasNoEarnedCheer &&
                                reputation.SelectedCheer ==
                                    CheerCategory.General);
                    }
                    else if (isCommunityTeam)
                    {
                        changed |= GrantCommunityTeamCheerAccess(
                            account,
                            selectCommunityTeamBadge:
                                hasNoEarnedCheer &&
                                reputation.SelectedCheer ==
                                    CheerCategory.General);
                    }

                    CheerCategory normalizedSelectedCheer =
                        NormalizeSelectedStaffBadge(
                            reputation.SelectedCheer,
                            isDeveloper,
                            isCommunityTeam);

                    if (normalizedSelectedCheer !=
                        reputation.SelectedCheer)
                    {
                        reputation.SelectedCheer =
                            normalizedSelectedCheer;
                        changed = true;
                    }

                    List<PlayerRelationship> authoritativeRelationships =
                        RelationshipDB.GetRelationships(account.PlayerId);
                    if (!RelationshipListsMatch(
                            account.Player.Relationships,
                            authoritativeRelationships))
                    {
                        account.Player.Relationships = authoritativeRelationships;
                        changed = true;
                    }

                    if (changed && Players.Update(account))
                        updated++;
                }

                return updated;
            }
        }

        private static bool RelationshipListsMatch(
            IReadOnlyCollection<PlayerRelationship> first,
            IReadOnlyCollection<PlayerRelationship> second)
        {
            if (first.Count != second.Count)
                return false;

            var firstByPlayer = first
                .GroupBy(value => value.PlayerID)
                .ToDictionary(group => group.Key, group => group.First());
            if (firstByPlayer.Count != first.Count)
                return false;

            foreach (PlayerRelationship expected in second)
            {
                if (!firstByPlayer.TryGetValue(expected.PlayerID, out PlayerRelationship? actual) ||
                    actual.Id != expected.Id ||
                    actual.RelationshipType != expected.RelationshipType ||
                    actual.Favorited != expected.Favorited ||
                    actual.Muted != expected.Muted ||
                    actual.Ignored != expected.Ignored)
                {
                    return false;
                }
            }

            return true;
        }

        private static PlayerDBClasses.RoomInstance CloneRoomInstance(
    PlayerDBClasses.RoomInstance source)
        {
            return new PlayerDBClasses.RoomInstance
            {
                encryptVoiceChat = source.encryptVoiceChat,
                clubId = source.clubId,
                dataBlob = source.dataBlob,
                eventId = source.eventId,

                isFull = source.isFull,
                isInProgress = source.isInProgress,
                isPrivate = source.isPrivate,

                location = source.location,
                maxCapacity = source.maxCapacity,
                Name = source.Name,

                photonRegion = source.photonRegion,
                photonRegionId = source.photonRegionId,
                photonRoomId = source.photonRoomId,

                roomCode = source.roomCode,
                roomId = source.roomId,
                roomInstanceId = source.roomInstanceId,
                roomInstanceType = source.roomInstanceType,
                subRoomId = source.subRoomId,
                subRoomDataSaveId = source.subRoomDataSaveId,
                createdAt = source.createdAt
            };
        }

        public static int EnsureInitialTokensForAllPlayers()
        {
            lock (SocialLock)
            {
                int updated = 0;

                foreach (var account in Players.FindAll().ToList())
                {
                    if (account.Player == null)
                        continue;

                    account.Player.PlayerExtra ??= new PlayerExtra();
                    account.Player.PlayerExtra.Settings ??= new List<Setting>();
                    account.Player.PlayerExtra.Currencies ??=
                        new List<PlayerCurrency>();

                    bool alreadyGranted = account.Player.PlayerExtra.Settings
                        .Any(setting =>
                            string.Equals(
                                setting.Key,
                                InitialTokensSettingKey,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                setting.Value,
                                "True",
                                StringComparison.OrdinalIgnoreCase));

                    if (alreadyGranted)
                        continue;

                    var tokenCurrency = account.Player.PlayerExtra.Currencies
                        .FirstOrDefault(currency =>
                            currency.CurrencyType ==
                            CurrencyType.RecCenterTokens);

                    if (tokenCurrency == null)
                    {
                        tokenCurrency = new PlayerCurrency
                        {
                            CurrencyType = CurrencyType.RecCenterTokens,
                            BalanceType = BalanceType.NonPurchasedDefault,
                            Balance = InitialTokenBalance
                        };
                        account.Player.PlayerExtra.Currencies.Add(tokenCurrency);
                    }
                    else if (tokenCurrency.Balance <= 0)
                    {
                        tokenCurrency.Balance = InitialTokenBalance;
                        tokenCurrency.BalanceType =
                            BalanceType.NonPurchasedDefault;
                    }

                    account.Player.PlayerExtra.Settings.Add(new Setting
                    {
                        Key = InitialTokensSettingKey,
                        Value = "True"
                    });

                    if (Players.Update(account))
                        updated++;
                }

                return updated;
            }
        }

        public static int ResetAlpacaShirtOwnershipForExistingPlayers()
        {
            lock (SocialLock)
            {
                int ownershipsRemoved = 0;

                foreach (var account in Players.FindAll().ToList())
                {
                    if (account.Player == null)
                        continue;

                    account.Player.PlayerExtra ??= new PlayerExtra();
                    account.Player.PlayerExtra.Settings ??= new List<Setting>();
                    account.Player.PlayerExtra.AvatarItems ??= new List<string>();

                    bool resetAlreadyApplied = account.Player.PlayerExtra.Settings
                        .Any(setting =>
                            string.Equals(
                                setting.Key,
                                AlpacaShirtOwnershipResetSettingKey,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                setting.Value,
                                "True",
                                StringComparison.OrdinalIgnoreCase));

                    if (resetAlreadyApplied)
                        continue;

                    int removedForAccount = account.Player.PlayerExtra.AvatarItems
                        .RemoveAll(descriptor => string.Equals(
                            descriptor,
                            AlpacaShirtAvatarItemDescriptor,
                            StringComparison.OrdinalIgnoreCase));

                    account.Player.PlayerExtra.Settings.Add(new Setting
                    {
                        Key = AlpacaShirtOwnershipResetSettingKey,
                        Value = "True"
                    });

                    if (Players.Update(account))
                        ownershipsRemoved += removedForAccount;
                }

                return ownershipsRemoved;
            }
        }

        public static bool OwnsAvatarItem(
            long playerId,
            string avatarItemDescriptor)
        {
            if (string.IsNullOrWhiteSpace(avatarItemDescriptor))
                return false;

            lock (SocialLock)
            {
                var legacyItems = Players.FindById(playerId)?
                    .Player?.PlayerExtra?.AvatarItems;

                return PlayerInventoryStore.OwnsAvatarItem(
                    playerId,
                    avatarItemDescriptor,
                    legacyAvatarDescriptors: legacyItems);
            }
        }

        public static bool RemoveAvatarItem(
            long playerId,
            string avatarItemDescriptor,
            long avatarItemId = 0)
        {
            if (playerId <= 0 ||
                (string.IsNullOrWhiteSpace(avatarItemDescriptor) && avatarItemId <= 0))
            {
                return false;
            }

            lock (SocialLock)
            {
                var player = Players.FindById(playerId);
                if (player?.Player == null)
                    return false;

                player.Player.PlayerExtra ??= new PlayerExtra();
                player.Player.PlayerExtra.AvatarItems ??= new List<string>();

                if (!string.IsNullOrWhiteSpace(avatarItemDescriptor))
                {
                    player.Player.PlayerExtra.AvatarItems.RemoveAll(value =>
                        string.Equals(
                            value,
                            avatarItemDescriptor,
                            StringComparison.OrdinalIgnoreCase));
                }

                bool saved = PlayerInventoryStore.SetAvatarItemOwned(
                    playerId,
                    avatarItemDescriptor,
                    avatarItemId,
                    friendlyName: null,
                    owned: false,
                    legacyAvatarDescriptors: player.Player.PlayerExtra.AvatarItems);

                return saved && Players.Update(player);
            }
        }

        public static List<PlayerRelationship> GetRelationships(long playerId)
        {
            return Players.FindById(playerId)?.Player == null
                ? new List<PlayerRelationship>()
                : RelationshipDB.GetRelationships(playerId);
        }

        public static PlayerRelationship? SetRelationshipFlags(
            long sourcePlayerId,
            long targetPlayerId,
            bool? ignored = null,
            bool? muted = null,
            bool? favorited = null)
        {
            if (!ValidateRelationshipPlayers(sourcePlayerId, targetPlayerId))
                return null;

            PlayerRelationship relationship;
            lock (SocialLock)
            {
                relationship = RelationshipDB.SetFlags(
                    sourcePlayerId,
                    targetPlayerId,
                    ignored: ignored,
                    muted: muted,
                    favorited: favorited);

                if (!SynchronizeLegacyRelationshipNoLock(sourcePlayerId, targetPlayerId))
                    return null;
            }

            return relationship;
        }

        private static bool ApplyBanStateToHeartbeat(
    FullPlayer player,
    long playerId,
    out Heartbeat heartbeat)
        {
            player.Player ??= new Player();
            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Heartbeat ??= new Heartbeat();

            heartbeat = player.Player.PlayerExtra.Heartbeat;
            heartbeat.playerId = playerId;

            var moderation =
                player.Player.PlayerExtra.ModerationBlockDetails;

            if (moderation?.IsBan != true)
            {
                if (heartbeat.errorCode ==
                    MatchmakingErrorCode.Banned)
                {
                    heartbeat.errorCode =
                        MatchmakingErrorCode.Success;
                }

                return false;
            }

            long now = DateTimeOffset.UtcNow
                .ToUnixTimeSeconds();

            bool permanent = moderation.Duration <= 0;

            bool active =
                permanent ||
                moderation.ModerationSetUnixTime +
                moderation.Duration > now;

            if (!active)
            {

                moderation.IsBan = false;
                moderation.Duration = 0;
                moderation.Message = "";
                moderation.ModerationSetUnixTime = 0;
                moderation.BannedByPlayerId = 0;

                if (heartbeat.errorCode ==
                    MatchmakingErrorCode.Banned)
                {
                    heartbeat.errorCode =
                        MatchmakingErrorCode.Success;
                }

                return false;
            }

            heartbeat.isOnline = false;
            heartbeat.roomInstance = null;
            heartbeat.errorCode =
                MatchmakingErrorCode.Banned;

            heartbeat.lastHeartbeatUnixTime =
                now;

            return true;
        }

        public static PlayerRelationship? SendFriendRequest(
            long sourcePlayerId,
            long targetPlayerId)
        {
            if (!ValidateRelationshipPlayers(sourcePlayerId, targetPlayerId))
                return null;

            PlayerRelationship? result;

            lock (SocialLock)
            {
                PlayerRelationship? existing = RelationshipDB.GetRelationship(
                    sourcePlayerId,
                    targetPlayerId);

                if (existing?.RelationshipType is RelationshipType.OutgoingFriendRequest
                    or RelationshipType.Friend)
                {
                    result = existing;
                }
                else
                {
                    result = RelationshipDB.SendFriendRequest(
                        sourcePlayerId,
                        targetPlayerId,
                        out _);
                }

                if (!SynchronizeLegacyRelationshipNoLock(sourcePlayerId, targetPlayerId))
                    return null;
            }

            return result;
        }

        public static PlayerRelationship? AcceptFriendRequest(
            long receiverPlayerId,
            long requesterPlayerId)
        {
            if (!ValidateRelationshipPlayers(receiverPlayerId, requesterPlayerId))
                return null;

            PlayerRelationship? result;
            lock (SocialLock)
            {
                result = RelationshipDB.AcceptFriendRequest(
                    receiverPlayerId,
                    requesterPlayerId);
                if (result == null)
                    return null;

                if (!SynchronizeLegacyRelationshipNoLock(receiverPlayerId, requesterPlayerId))
                    return null;
            }

            return result;
        }

        public static PlayerRelationship? AddFriendRequest(
            long sourcePlayerId,
            long targetPlayerId) =>
            SendFriendRequest(sourcePlayerId, targetPlayerId);

        private static bool ValidateRelationshipPlayers(
            long firstPlayerId,
            long secondPlayerId)
        {
            if (firstPlayerId <= 0 ||
                secondPlayerId <= 0 ||
                firstPlayerId == secondPlayerId)
            {
                return false;
            }

            return Players.FindById(firstPlayerId)?.Player != null &&
                   Players.FindById(secondPlayerId)?.Player != null;
        }

        private static bool SynchronizeLegacyRelationshipNoLock(
            long firstPlayerId,
            long secondPlayerId)
        {
            FullPlayer? first = Players.FindById(firstPlayerId);
            FullPlayer? second = Players.FindById(secondPlayerId);
            if (first?.Player == null || second?.Player == null)
                return false;

            first.Player.Relationships ??= new List<PlayerRelationship>();
            second.Player.Relationships ??= new List<PlayerRelationship>();

            SynchronizeOneLegacyRelationship(
                first.Player.Relationships,
                RelationshipDB.GetRelationship(firstPlayerId, secondPlayerId),
                secondPlayerId);
            SynchronizeOneLegacyRelationship(
                second.Player.Relationships,
                RelationshipDB.GetRelationship(secondPlayerId, firstPlayerId),
                firstPlayerId);

            return Players.Update(first) && Players.Update(second);
        }

        private static void SynchronizeOneLegacyRelationship(
            List<PlayerRelationship> relationships,
            PlayerRelationship? authoritative,
            long otherPlayerId)
        {
            relationships.RemoveAll(value => value.PlayerID == otherPlayerId);
            if (authoritative != null && authoritative.RelationshipType != RelationshipType.None)
                relationships.Add(authoritative);
        }

        private static long CreateRelationshipId(
            long firstPlayerId,
            long secondPlayerId) =>
            RelationshipDB.GetStableNumericId(firstPlayerId, secondPlayerId);

        public static bool SetSubscription(
            long subscriberPlayerId,
            long targetPlayerId,
            bool subscribe)
        {
            if (subscriberPlayerId == targetPlayerId)
                return false;

            lock (SocialLock)
            {
                var subscriber = Players.FindById(subscriberPlayerId);
                var target = Players.FindById(targetPlayerId);

                if (subscriber?.Player == null || target?.Player == null)
                    return false;

                if (!RelationshipDB.SetSubscription(
                        subscriberPlayerId,
                        targetPlayerId,
                        subscribe))
                    return false;

                subscriber.Player.SubscribedAccountIds ??= new List<long>();
                subscriber.Player.Reputation ??= new Reputation();
                target.Player.Reputation ??= new Reputation();

                if (subscribe)
                {
                    if (!subscriber.Player.SubscribedAccountIds.Contains(targetPlayerId))
                        subscriber.Player.SubscribedAccountIds.Add(targetPlayerId);
                }
                else
                {
                    subscriber.Player.SubscribedAccountIds.RemoveAll(value =>
                        value == targetPlayerId);
                }

                subscriber.Player.Reputation.SubscribedCount =
                    RelationshipDB.GetSubscribedCount(subscriberPlayerId);
                Players.Update(subscriber);

                target.Player.Reputation.SubscriberCount =
                    RelationshipDB.GetSubscriberCount(targetPlayerId);

                Players.Update(target);
                return true;
            }
        }

        public static int GetSubscriberCount(long targetPlayerId)
        {
            return RelationshipDB.GetSubscriberCount(targetPlayerId);
        }

        public static int GetCurrencyBalance(
            long playerId,
            CurrencyType currencyType)
        {
            var player = Players.FindById(playerId);
            return player?.Player?.PlayerExtra?.Currencies?
                .FirstOrDefault(currency =>
                    currency.CurrencyType == currencyType)?.Balance ?? 0;
        }

        public static List<(CurrencyType CurrencyType, int Balance)> GetAllCurrencyBalances(
            long playerId)
        {
            var player = Players.FindById(playerId);
            var currencies = player?.Player?.PlayerExtra?.Currencies ?? new List<PlayerCurrency>();

            var storedBalances = currencies
                .GroupBy(currency => currency.CurrencyType)
                .ToDictionary(group => group.Key, group => group.First().Balance);

            return Enum.GetValues<CurrencyType>()
                .Where(currency => currency != CurrencyType.Invalid)
                .Select(currency => (
                    currency,
                    storedBalances.TryGetValue(currency, out int balance) ? balance : 0))
                .ToList();
        }

        public static int? SetCurrencyBalance(
            long playerId,
            CurrencyType currencyType,
            int amount,
            bool add)
        {
            lock (SocialLock)
            {
                var player = Players.FindById(playerId);
                if (player?.Player == null)
                    return null;

                player.Player.PlayerExtra ??= new PlayerExtra();
                player.Player.PlayerExtra.Currencies ??= new List<PlayerCurrency>();

                var currency = player.Player.PlayerExtra.Currencies
                    .FirstOrDefault(value => value.CurrencyType == currencyType);

                if (currency == null)
                {
                    currency = new PlayerCurrency
                    {
                        CurrencyType = currencyType,
                        BalanceType = BalanceType.NonPurchasedDefault
                    };
                    player.Player.PlayerExtra.Currencies.Add(currency);
                }

                long requestedBalance = add
                    ? (long)currency.Balance + amount
                    : amount;
                currency.Balance = (int)Math.Clamp(
                    requestedBalance,
                    0L,
                    int.MaxValue);

                return Players.Update(player) ? currency.Balance : null;
            }
        }

        public static bool TrySpendCurrency(
            long playerId,
            CurrencyType currencyType,
            int amount,
            out int newBalance)
        {
            newBalance = 0;
            if (amount < 0)
                return false;

            lock (SocialLock)
            {
                var player = Players.FindById(playerId);
                if (player?.Player == null)
                    return false;

                player.Player.PlayerExtra ??= new PlayerExtra();
                player.Player.PlayerExtra.Currencies ??= new List<PlayerCurrency>();

                var currency = player.Player.PlayerExtra.Currencies
                    .FirstOrDefault(value => value.CurrencyType == currencyType);

                newBalance = currency?.Balance ?? 0;
                if (amount == 0)
                    return true;
                if (currency == null || currency.Balance < amount)
                    return false;

                int originalBalance = currency.Balance;
                currency.Balance -= amount;
                newBalance = currency.Balance;

                if (Players.Update(player))
                    return true;

                currency.Balance = originalBalance;
                newBalance = originalBalance;
                return false;
            }
        }

        public static bool TryPurchaseAvatarItem(
            long playerId,
            string avatarItemDescriptor,
            int price,
            out int newBalance,
            out bool alreadyOwned)
        {
            newBalance = 0;
            alreadyOwned = false;

            if (price < 0 || string.IsNullOrWhiteSpace(avatarItemDescriptor))
                return false;

            lock (SocialLock)
            {
                var player = Players.FindById(playerId);
                if (player?.Player == null)
                    return false;

                player.Player.PlayerExtra ??= new PlayerExtra();
                player.Player.PlayerExtra.Currencies ??= new List<PlayerCurrency>();
                player.Player.PlayerExtra.AvatarItems ??= new List<string>();

                var currency = player.Player.PlayerExtra.Currencies
                    .FirstOrDefault(value =>
                        value.CurrencyType == CurrencyType.RecCenterTokens);

                newBalance = currency?.Balance ?? 0;
                alreadyOwned = PlayerInventoryStore.OwnsAvatarItem(
                    playerId,
                    avatarItemDescriptor,
                    legacyAvatarDescriptors: player.Player.PlayerExtra.AvatarItems);

                if (alreadyOwned)
                    return true;
                if (currency == null || currency.Balance < price)
                    return false;

                currency.Balance -= price;
                player.Player.PlayerExtra.AvatarItems.Add(avatarItemDescriptor);
                PlayerInventoryStore.SetAvatarItemOwned(
                    playerId,
                    avatarItemDescriptor,
                    avatarItemId: 0,
                    friendlyName: null,
                    owned: true,
                    legacyAvatarDescriptors: player.Player.PlayerExtra.AvatarItems);
                newBalance = currency.Balance;
                return Players.Update(player);
            }
        }

        public static bool GrantAvatarItem(
            long playerId,
            string avatarItemDescriptor)
        {
            if (playerId <= 0 || string.IsNullOrWhiteSpace(avatarItemDescriptor))
                return false;

            lock (SocialLock)
            {
                var player = Players.FindById(playerId);
                if (player?.Player == null)
                    return false;

                player.Player.PlayerExtra ??= new PlayerExtra();
                player.Player.PlayerExtra.AvatarItems ??= new List<string>();

                if (player.Player.PlayerExtra.AvatarItems.Contains(
                        avatarItemDescriptor,
                        StringComparer.OrdinalIgnoreCase))
                {
                    PlayerInventoryStore.SetAvatarItemOwned(
                        playerId,
                        avatarItemDescriptor,
                        avatarItemId: 0,
                        friendlyName: null,
                        owned: true,
                        legacyAvatarDescriptors: player.Player.PlayerExtra.AvatarItems);
                    return true;
                }

                player.Player.PlayerExtra.AvatarItems.Add(avatarItemDescriptor);
                PlayerInventoryStore.SetAvatarItemOwned(
                    playerId,
                    avatarItemDescriptor,
                    avatarItemId: 0,
                    friendlyName: null,
                    owned: true,
                    legacyAvatarDescriptors: player.Player.PlayerExtra.AvatarItems);
                return Players.Update(player);
            }
        }

        public static bool IsSubscribed(long subscriberPlayerId, long targetPlayerId)
        {
            return RelationshipDB.IsSubscribed(subscriberPlayerId, targetPlayerId);
        }

        public static bool RemoveFriend(long sourcePlayerId, long targetPlayerId)
        {
            bool removed;

            lock (SocialLock)
            {
                var source = Players.FindById(sourcePlayerId);
                var target = Players.FindById(targetPlayerId);

                if (source?.Player == null || target?.Player == null)
                    return false;

                source.Player.Relationships ??= new List<PlayerRelationship>();
                target.Player.Relationships ??= new List<PlayerRelationship>();

                source.Player.Relationships.RemoveAll(value =>
                    value.PlayerID == targetPlayerId);
                target.Player.Relationships.RemoveAll(value =>
                    value.PlayerID == sourcePlayerId);

                Players.Update(source);
                Players.Update(target);
                removed = RelationshipDB.RemoveFriend(sourcePlayerId, targetPlayerId);
            }

            return removed;
        }

        public static PlayerCheerRecord? GivePlayerCheer(
            long giverPlayerId,
            long receiverPlayerId,
            CheerCategory category,
            out NotificationDB.ClientNotification? notification,
            bool anonymous = false)
        {
            notification = null;

            if (giverPlayerId == receiverPlayerId ||
                !IsPublicCheerCategory(category))
            {
                return null;
            }

            PlayerCheerRecord? cheer;

            lock (SocialLock)
            {
                var giver = Players.FindById(giverPlayerId);
                var receiver = Players.FindById(receiverPlayerId);

                if (giver?.Player == null || receiver?.Player == null)
                    return null;

                giver.Player.Reputation ??= new Reputation();
                receiver.Player.ReceivedCheers ??= new List<PlayerCheerRecord>();
                receiver.Player.Reputation ??= new Reputation();

                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                RefreshCheerCreditsNoLock(
                    giver.Player.Reputation,
                    giverPlayerId,
                    now);
                if (giver.Player.Reputation.CheerCredit <= 0)
                    return null;

                cheer = new PlayerCheerRecord
                {
                    CheerId = Interlocked.Increment(ref CheerSequence),
                    GiverPlayerId = giverPlayerId,
                    ReceiverPlayerId = receiverPlayerId,
                    CheerCategory = category,
                    Anonymous = anonymous,
                    Status = CheerStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                notification = NotificationDB.CreatePlayerCheer(
                    giverPlayerId,
                    receiverPlayerId,
                    (int)category,
                    anonymous);

                cheer.NotificationId = notification.Id;
                receiver.Player.ReceivedCheers.Add(cheer);

                var reputation = receiver.Player.Reputation;
                reputation.AccountId = receiverPlayerId;
                reputation.IsCheerful = true;
                giver.Player.Reputation.AccountId = giverPlayerId;
                giver.Player.Reputation.CheerCredit--;

                switch (category)
                {
                    case CheerCategory.General:
                        reputation.CheerGeneral++;
                        break;
                    case CheerCategory.Helpful:
                        reputation.CheerHelpful++;
                        break;
                    case CheerCategory.Sportmanship:
                        reputation.CheerSportsman++;
                        break;
                    case CheerCategory.GreatHost:
                        reputation.CheerGreatHost++;
                        break;
                    case CheerCategory.Creative:
                        reputation.CheerCreative++;
                        break;
                }

                if (Players.Update(new[] { giver, receiver }) != 2)
                {
                    NotificationDB.DeleteMessages(
                        receiverPlayerId,
                        new[] { notification.Id });
                    return null;
                }
            }

            return cheer;
        }

        private static bool RefreshCheerCreditsNoLock(
            Reputation reputation,
            long accountId,
            long nowUnixTime)
        {
            bool changed = false;

            if (reputation.AccountId != accountId)
            {
                reputation.AccountId = accountId;
                changed = true;
            }

            if (reputation.CheerCreditRefreshAtUnixTime <= 0)
            {

                reputation.CheerCredit = CheerCreditLimit;
                reputation.CheerCreditRefreshAtUnixTime =
                    nowUnixTime + CheerCreditRefreshSeconds;
                return true;
            }

            if (nowUnixTime >= reputation.CheerCreditRefreshAtUnixTime)
            {
                long elapsed =
                    nowUnixTime - reputation.CheerCreditRefreshAtUnixTime;
                long completedIntervals =
                    (elapsed / CheerCreditRefreshSeconds) + 1;

                reputation.CheerCredit = CheerCreditLimit;
                reputation.CheerCreditRefreshAtUnixTime +=
                    completedIntervals * CheerCreditRefreshSeconds;
                changed = true;
            }

            int clamped = Math.Clamp(
                reputation.CheerCredit,
                0,
                CheerCreditLimit);
            if (clamped != reputation.CheerCredit)
            {
                reputation.CheerCredit = clamped;
                changed = true;
            }

            return changed;
        }

        public static int ResolvePlayerCheers(
            long receiverPlayerId,
            IEnumerable<long> cheerOrNotificationIds,
            CheerStatus status)
        {
            if (receiverPlayerId <= 0 ||
                status == CheerStatus.Pending)
            {
                return 0;
            }

            HashSet<long> ids = cheerOrNotificationIds
                .Where(value => value > 0)
                .ToHashSet();
            if (ids.Count == 0)
                return 0;

            lock (SocialLock)
            {
                var receiver = Players.FindById(receiverPlayerId);
                if (receiver?.Player == null)
                    return 0;

                receiver.Player.ReceivedCheers ??=
                    new List<PlayerCheerRecord>();

                List<PlayerCheerRecord> matching =
                    receiver.Player.ReceivedCheers
                        .Where(value =>
                            value.Status == CheerStatus.Pending &&
                            (ids.Contains(value.CheerId) ||
                             ids.Contains(value.NotificationId)))
                        .ToList();

                if (matching.Count == 0)
                    return 0;

                DateTime handledAt = DateTime.UtcNow;
                foreach (PlayerCheerRecord cheer in matching)
                {
                    cheer.Status = status;
                    cheer.HandledAt = handledAt;
                }

                return Players.Update(receiver)
                    ? matching.Count
                    : 0;
            }
        }

        public static List<PlayerCheerRecord> GetPendingPlayerCheers(
            long receiverPlayerId)
        {
            if (receiverPlayerId <= 0)
                return new List<PlayerCheerRecord>();

            lock (SocialLock)
            {
                var receiver = Players.FindById(receiverPlayerId);
                return receiver?.Player?.ReceivedCheers?
                    .Where(value => value.Status == CheerStatus.Pending)
                    .OrderByDescending(value => value.CreatedAt)
                    .ToList() ?? new List<PlayerCheerRecord>();
            }
        }

        private static bool IsPublicCheerCategory(
            CheerCategory category) =>
            category is CheerCategory.General or
                CheerCategory.Helpful or
                CheerCategory.Sportmanship or
                CheerCategory.GreatHost or
                CheerCategory.Creative;

        public static Heartbeat GetPlayerHeartbeat(
            long playerId,
            long? viewingPlayerId = null)
        {
            var player = Players.FindOne(
                x => x.PlayerId == playerId);

            return CreateClientHeartbeat(
                player,
                playerId,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                viewingPlayerId);
        }

        public static List<Heartbeat> GetPlayerHeartbeatsBulk(
            List<long> playerIds,
            long? viewingPlayerId = null)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var players = Players
                .Find(x => playerIds.Contains(x.PlayerId))
                .ToList();

            return players
                .Select(p => CreateClientHeartbeat(
                    p,
                    p.PlayerId,
                    now,
                    viewingPlayerId))
                .ToList();
        }

        public static long[] GetActiveSameInstancePlayerIds(long subjectPlayerId)
        {
            if (subjectPlayerId <= 0)
                return Array.Empty<long>();

            Heartbeat? subjectHeartbeat = Players.FindById(subjectPlayerId)?
                .Player?.PlayerExtra?.Heartbeat;
            long roomInstanceId =
                subjectHeartbeat?.roomInstance?.roomInstanceId ?? 0;

            if (roomInstanceId <= 0)
                return Array.Empty<long>();

            long activeAfter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() -
                PresenceFreshnessSeconds;

            return Players.FindAll()
                .Where(account =>
                {
                    if (account.PlayerId == subjectPlayerId)
                        return false;

                    Heartbeat? heartbeat =
                        account.Player?.PlayerExtra?.Heartbeat;

                    return heartbeat?.isOnline == true &&
                           heartbeat.lastHeartbeatUnixTime >= activeAfter &&
                           heartbeat.roomInstance?.roomInstanceId == roomInstanceId;
                })
                .Select(account => account.PlayerId)
                .Distinct()
                .ToArray();
        }

        private static Heartbeat CreateClientHeartbeat(
            FullPlayer? player,
            long playerId,
            long now,
            long? viewingPlayerId)
        {
            Heartbeat stored =
                player?.Player?.PlayerExtra?.Heartbeat ??
                new Heartbeat();

            bool isFresh =
                stored.lastHeartbeatUnixTime > 0 &&
                stored.lastHeartbeatUnixTime >=
                    now - PresenceFreshnessSeconds;
            bool isOnline = stored.isOnline && isFresh;

            RoomInstance? roomInstance = isOnline &&
                stored.roomInstance != null
                ? CloneRoomInstance(stored.roomInstance)
                : null;

            bool viewerSharesRestrictedInstance = false;
            if (roomInstance != null &&
                viewingPlayerId.HasValue &&
                viewingPlayerId.Value != playerId)
            {
                Heartbeat? viewerHeartbeat = Players
                    .FindById(viewingPlayerId.Value)?
                    .Player?.PlayerExtra?.Heartbeat;
                viewerSharesRestrictedInstance =
                    viewerHeartbeat?.isOnline == true &&
                    viewerHeartbeat.roomInstance?.roomInstanceId ==
                        roomInstance.roomInstanceId;
            }

            if (roomInstance != null &&
                viewingPlayerId.HasValue &&
                viewingPlayerId.Value != playerId &&
                Sessions.IsRestrictedInstance(roomInstance) &&
                !viewerSharesRestrictedInstance &&
                !NotificationDB.HasActiveRoomInvite(
                    viewingPlayerId.Value,
                    playerId,
                    roomInstance.roomInstanceId))
            {
                roomInstance = null;
            }

            if (roomInstance != null &&
                roomInstance.roomInstanceType != RoomInstanceType.Dormroom &&
                roomInstance.roomId > 0)
            {
                var room = RoomDB.GetRoom(roomInstance.roomId);
                if (room != null && !string.IsNullOrWhiteSpace(room.Name))
                    roomInstance.Name = $"^{room.Name.Trim().TrimStart('^')}";
            }

            return new Heartbeat
            {
                appVersion = stored.appVersion,
                deviceClass = stored.deviceClass,
                errorCode = stored.errorCode,
                isOnline = isOnline,
                playerId = playerId,
                roomInstance = roomInstance,
                statusVisibility = isOnline
                    ? stored.statusVisibility
                    : StatusVisibility.Offline,
                vrMovementMode = stored.vrMovementMode,
                lastHeartbeatUnixTime = stored.lastHeartbeatUnixTime
            };
        }

        public static void RecordRoomVisit(
            long playerId,
            long roomId)
        {
            var player = Players.FindById(playerId);

            if (player?.Player == null)
                return;

            player.Player.VisitedRooms ??= new List<long>();

            if (!player.Player.VisitedRooms.Contains(roomId))
                player.Player.VisitedRooms.Add(roomId);

            player.Player.PlayerExtra ??= new PlayerExtra();

            player.Player.PlayerExtra.RoomVisits ??=
                new List<RoomVisit>();

            var existingVisit =
                player.Player.PlayerExtra.RoomVisits
                    .FirstOrDefault(v => v.RoomId == roomId);

            bool isNewVisitor = existingVisit == null;

            if (existingVisit != null)
            {
                existingVisit.VisitedAt = DateTime.UtcNow;
            }
            else
            {
                player.Player.PlayerExtra.RoomVisits.Add(
                    new RoomVisit
                    {
                        RoomId = roomId,
                        VisitedAt = DateTime.UtcNow
                    });
            }

            Players.Update(player);

            var room = RoomDB.GetRoom(roomId);

            if (room != null)
            {
                room.Stats ??= new RoomDBClasses.Stats();

                room.Stats.VisitCount++;

                if (isNewVisitor)
                    room.Stats.VisitorCount++;

                RoomDB.Rooms.Update(room);
            }
        }

        public static bool ToggleCheer(
            long playerId,
            long roomId,
            out bool isNowCheered)
        {
            var player = Players.FindById(playerId);

            isNowCheered = false;

            if (player?.Player == null)
                return false;

            player.Player.CheeredRooms ??= new List<long>();

            bool wasCheered =
                player.Player.CheeredRooms.Contains(roomId);

            if (wasCheered)
            {
                player.Player.CheeredRooms.Remove(roomId);
            }
            else
            {
                player.Player.CheeredRooms.Add(roomId);
            }

            isNowCheered = !wasCheered;

            Players.Update(player);

            var room = RoomDB.GetRoom(roomId);

            if (room != null)
            {
                room.Stats ??= new RoomDBClasses.Stats();

                room.Stats.CheerCount = Math.Max(
                    0,
                    room.Stats.CheerCount +
                    (wasCheered ? -1 : 1));

                RoomDB.Rooms.Update(room);
            }

            return true;
        }

        public static bool ToggleFavorite(
            long playerId,
            long roomId,
            out bool isNowFavorited)
        {
            var player = Players.FindById(playerId);

            isNowFavorited = false;

            if (player?.Player == null)
                return false;

            player.Player.FavoritedRooms ??= new List<long>();

            bool wasFavorited =
                player.Player.FavoritedRooms.Contains(roomId);

            if (wasFavorited)
            {
                player.Player.FavoritedRooms.Remove(roomId);
            }
            else
            {
                player.Player.FavoritedRooms.Add(roomId);
            }

            isNowFavorited = !wasFavorited;

            Players.Update(player);

            var room = RoomDB.GetRoom(roomId);

            if (room != null)
            {
                room.Stats ??= new RoomDBClasses.Stats();

                room.Stats.FavoriteCount = Math.Max(
                    0,
                    room.Stats.FavoriteCount +
                    (wasFavorited ? -1 : 1));

                RoomDB.Rooms.Update(room);
            }

            return true;
        }

        public static (
            List<PlayerDTOBase> Results,
            int Total)
            SearchAccounts(
                string query,
                int skip,
                int take)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return (
                    new List<PlayerDTOBase>(),
                    0
                );
            }

            var q = query
                .Trim()
                .ToLowerInvariant();

            var matches = Players
                .FindAll()
                .Where(p =>
                    (p.Player?.Username?
                        .ToLowerInvariant()
                        .Contains(q) ?? false)
                    ||
                    (p.Player?.DisplayName?
                        .ToLowerInvariant()
                        .Contains(q) ?? false))
                .OrderBy(p => p.Player?.Username)
                .ToList();

            int total = matches.Count;

            var page = matches
                .Skip(skip)
                .Take(take)
                .Select(p => MapToDTO(p, false))
                .ToList();

            return (page, total);
        }

        public static Heartbeat? UpdatePlayerHeartbeat(
            long playerId,
            RoomInstance? roomInstance,
            bool online = true,
            Platforms platform = Platforms.All,
            DeviceClasses deviceClasses = DeviceClasses.Unknown)
        {
            var player = Players.FindById(playerId);

            if (player == null)
                return null;

            player.Player ??= new Player();
            player.Player.PlayerExtra ??= new PlayerExtra();

            player.Player.PlayerExtra.Heartbeat ??=
                new Heartbeat();

            var hb = player.Player.PlayerExtra.Heartbeat;
            long? oldRoomInstanceId = hb.roomInstance?.roomInstanceId;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            hb.playerId = playerId;
            hb.roomInstance = online ? roomInstance : null;
            hb.errorCode = MatchmakingErrorCode.Success;
            hb.isOnline = online;
            hb.lastHeartbeatUnixTime = now;

            long newRoomInstanceId =
                online ? roomInstance?.roomInstanceId ?? 0 : 0;
            if (player.Player.PlayerExtra.LevelingRoomInstanceId !=
                newRoomInstanceId)
            {
                ResetRoomLevelTimer(
                    player.Player.PlayerExtra,
                    newRoomInstanceId,
                    newRoomInstanceId > 0 ? now : 0);
            }

            Players.Update(player);

            if (oldRoomInstanceId != hb.roomInstance?.roomInstanceId)
            {
                Console.WriteLine(
                    $"[ROOM MEMBERSHIP] player={playerId} " +
                    $"oldInstance={oldRoomInstanceId?.ToString() ?? "none"} " +
                    $"newInstance={hb.roomInstance?.roomInstanceId.ToString() ?? "none"} " +
                    "broadcast=false");
            }

            return hb;
        }
        public static Heartbeat? LeaveCurrentRoom(long playerId)
        {
            var player = Players.FindById(playerId);

            if (player == null)
                return null;

            player.Player ??= new Player();
            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Heartbeat ??= new Heartbeat();

            var heartbeat = player.Player.PlayerExtra.Heartbeat;
            long? oldRoomInstanceId = heartbeat.roomInstance?.roomInstanceId;

            heartbeat.playerId = playerId;

            heartbeat.isOnline = true;
            heartbeat.roomInstance = null;
            heartbeat.lastHeartbeatUnixTime =
                DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            ResetRoomLevelTimer(player.Player.PlayerExtra, 0, 0);

            Players.Update(player);

            Console.WriteLine(
                $"[ROOM LEAVE] player={playerId} " +
                $"instance={oldRoomInstanceId?.ToString() ?? "none"} " +
                "isolated=true presencePush=controller-awaited");

            return heartbeat;
        }

        public static Heartbeat? TouchPlayerHeartbeat(long playerId)
        {
            int? oldProgressionLevel = null;
            int? newProgressionLevel = null;
            int progressionXp = 0;
            Heartbeat? result;

            lock (ProgressionLock)
            {
                var player = Players.FindById(playerId);

                if (player == null)
                    return null;

                bool oldOnline =
                    player.Player?.PlayerExtra?.Heartbeat?.isOnline ?? false;
                MatchmakingErrorCode? oldErrorCode =
                    player.Player?.PlayerExtra?.Heartbeat?.errorCode;

                if (ApplyBanStateToHeartbeat(
                        player,
                        playerId,
                        out var heartbeat))
                {
                    if (player.Player?.PlayerExtra != null)
                        ResetRoomLevelTimer(
                            player.Player.PlayerExtra,
                            0,
                            0);

                    Players.Update(player);
                    if (oldOnline != heartbeat.isOnline ||
                        oldErrorCode != heartbeat.errorCode)
                    {
                        _ = NotiController.NotifyPlayerPresenceUpdatedAsync(
                            playerId);
                    }

                    return heartbeat;
                }

                heartbeat.playerId = playerId;
                heartbeat.isOnline = true;
                heartbeat.errorCode =
                    MatchmakingErrorCode.Success;

                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                heartbeat.lastHeartbeatUnixTime = now;

                if (player.Player != null &&
                    ApplyRoomTimeLeveling(
                        player.Player,
                        now,
                        out int awardedFromLevel,
                        out int awardedToLevel))
                {
                    oldProgressionLevel = awardedFromLevel;
                    newProgressionLevel = awardedToLevel;
                    progressionXp = player.Player.XP;
                }

                Players.Update(player);
                if (oldOnline != heartbeat.isOnline ||
                    oldErrorCode != heartbeat.errorCode)
                {
                    _ = NotiController.NotifyPlayerPresenceUpdatedAsync(
                        playerId);
                }

                result = heartbeat;
            }

            if (oldProgressionLevel.HasValue &&
                newProgressionLevel.HasValue)
            {
                Console.WriteLine(
                    $"[ROOM LEVEL] player={playerId} " +
                    $"level={oldProgressionLevel.Value}->{newProgressionLevel.Value} " +
                    $"interval={RoomLevelSeconds}s");

                GrantLevelUpBox(
                    playerId,
                    oldProgressionLevel.Value,
                    newProgressionLevel.Value);

                _ = NotiController.NotifyProgressionAsync(
                    playerId,
                    oldProgressionLevel.Value,
                    newProgressionLevel.Value,
                    progressionXp);
            }

            return result;
        }

        public static Heartbeat? ResumePlayerHeartbeat(long playerId)
        {
            lock (ProgressionLock)
            {
                var player = Players.FindById(playerId);
                if (player == null)
                    return null;

                player.Player ??= new Player();
                player.Player.PlayerExtra ??= new PlayerExtra();
                Heartbeat heartbeat = player.Player.PlayerExtra.Heartbeat ??=
                    new Heartbeat();

                if (ApplyBanStateToHeartbeat(
                        player,
                        playerId,
                        out heartbeat))
                {
                    ResetRoomLevelTimer(
                        player.Player.PlayerExtra,
                        0,
                        0);
                    Players.Update(player);
                    return heartbeat;
                }

                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                bool existingPresenceIsFresh =
                    heartbeat.isOnline &&
                    heartbeat.lastHeartbeatUnixTime > 0 &&
                    heartbeat.lastHeartbeatUnixTime >=
                        now - PresenceFreshnessSeconds;

                if (!existingPresenceIsFresh)
                {
                    heartbeat.roomInstance = null;
                    ResetRoomLevelTimer(
                        player.Player.PlayerExtra,
                        0,
                        0);
                }

                heartbeat.playerId = playerId;
                heartbeat.isOnline = true;
                heartbeat.errorCode = MatchmakingErrorCode.Success;
                heartbeat.lastHeartbeatUnixTime = now;
                Players.Update(player);

                return heartbeat;
            }
        }

        public static void SetPlayerOffline(long playerId)
        {
            var player = Players.FindById(playerId);
            var heartbeat = player?.Player?.PlayerExtra?.Heartbeat;

            if (player == null || heartbeat == null)
                return;

            long? preservedRoomInstanceId = heartbeat.roomInstance?.roomInstanceId;

            heartbeat.isOnline = false;
            heartbeat.lastHeartbeatUnixTime =
                DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (player.Player?.PlayerExtra != null)
                ResetRoomLevelTimer(player.Player.PlayerExtra, 0, 0);
            Players.Update(player);

            Console.WriteLine(
                $"[PLAYER OFFLINE] player={playerId} " +
                $"preservedInstance={preservedRoomInstanceId?.ToString() ?? "none"} " +
                "broadcast=controller-awaited");
        }

        private static bool ApplyRoomTimeLeveling(
            Player player,
            long now,
            out int oldLevel,
            out int newLevel)
        {
            oldLevel = player.Level;
            newLevel = player.Level;

            player.PlayerExtra ??= new PlayerExtra();
            Heartbeat heartbeat = player.PlayerExtra.Heartbeat ??=
                new Heartbeat();
            long roomInstanceId =
                heartbeat.isOnline
                    ? heartbeat.roomInstance?.roomInstanceId ?? 0
                    : 0;

            if (roomInstanceId <= 0 || player.Level >= MaximumLevel)
            {
                ResetRoomLevelTimer(
                    player.PlayerExtra,
                    roomInstanceId,
                    roomInstanceId > 0 ? now : 0);
                return false;
            }

            if (player.PlayerExtra.LevelingRoomInstanceId != roomInstanceId)
            {
                ResetRoomLevelTimer(
                    player.PlayerExtra,
                    roomInstanceId,
                    now);
                return false;
            }

            long previous =
                player.PlayerExtra.LevelingLastHeartbeatUnixTime;
            player.PlayerExtra.LevelingLastHeartbeatUnixTime = now;

            if (previous <= 0 || now <= previous)
                return false;

            long elapsed = now - previous;
            if (elapsed > MaximumCreditableHeartbeatGapSeconds)
            {

                return false;
            }

            player.PlayerExtra.LevelingRoomActiveSeconds = Math.Min(
                RoomLevelSeconds,
                player.PlayerExtra.LevelingRoomActiveSeconds +
                checked((int)elapsed));

            if (player.PlayerExtra.LevelingRoomActiveSeconds <
                RoomLevelSeconds)
            {
                return false;
            }

            oldLevel = player.Level;
            player.Level = Math.Min(MaximumLevel, player.Level + 1);
            newLevel = player.Level;
            player.PlayerExtra.LevelingRoomActiveSeconds = 0;

            return newLevel > oldLevel;
        }

        private static void GrantLevelUpBox(
            long playerId,
            int oldLevel,
            int newLevel)
        {
            if (playerId <= 0 || newLevel <= oldLevel)
                return;

            var player = Players.FindById(playerId);
            if (player?.Player == null)
                return;

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Currencies ??= new List<PlayerCurrency>();

            for (int level = oldLevel + 1; level <= newLevel; level++)
            {
                int rarity = GetLevelBoxRarity(level);

                APIController.StorefrontAdminItem? boxItem =
                    APIController.GetWebsiteStorefrontItems()
                        .Where(item =>
                            item.Rarity == rarity &&
                            !string.IsNullOrWhiteSpace(item.ConsumableItemDesc))
                        .OrderBy(_ => LevelBoxRng.Next())
                        .FirstOrDefault();

                if (boxItem != null)
                {
                    PlayerInventoryStore.AddConsumable(
                        playerId,
                        boxItem.ConsumableItemDesc,
                        consumableItemId: 0,
                        friendlyName: boxItem.FriendlyName,
                        amount: 1);
                }

                APIController.StorefrontAdminItem? avatarBoxItem =
                    APIController.GetWebsiteStorefrontItems()
                        .Where(item =>
                            item.Rarity == rarity &&
                            !string.IsNullOrWhiteSpace(item.AvatarItemDesc))
                        .OrderBy(_ => LevelBoxRng.Next())
                        .FirstOrDefault() ??
                    APIController.GetWebsiteStorefrontItems()
                        .Where(item => !string.IsNullOrWhiteSpace(item.AvatarItemDesc))
                        .OrderBy(_ => LevelBoxRng.Next())
                        .FirstOrDefault();

                if (avatarBoxItem != null)
                {
                    PlayerInventoryStore.SetAvatarItemOwned(
                        playerId,
                        avatarBoxItem.AvatarItemDesc,
                        avatarBoxItem.AvatarItemId,
                        avatarBoxItem.FriendlyName,
                        owned: true);
                }

                PlayerCurrency? currency =
                    player.Player.PlayerExtra.Currencies.FirstOrDefault(
                        value => value.CurrencyType == CurrencyType.RecCenterTokens);
                if (currency == null)
                {
                    currency = new PlayerCurrency
                    {
                        CurrencyType = CurrencyType.RecCenterTokens,
                        BalanceType = BalanceType.NonPurchasedDefault
                    };
                    player.Player.PlayerExtra.Currencies.Add(currency);
                }

                long adjustedBalance = (long)currency.Balance + 1000;
                currency.Balance = (int)Math.Clamp(
                    adjustedBalance,
                    (long)int.MinValue,
                    (long)int.MaxValue);

                Console.WriteLine(
                    $"[LEVEL BOX] player={playerId} level={level} " +
                    $"rarity={rarity} box={boxItem?.FriendlyName ?? "none"} " +
                    $"avatarItem={avatarBoxItem?.FriendlyName ?? "none"} " +
                    "tokens=1000");
            }

            Players.Update(player);
        }

        private static void ResetRoomLevelTimer(
            PlayerExtra playerExtra,
            long roomInstanceId,
            long lastHeartbeatUnixTime)
        {
            playerExtra.LevelingRoomInstanceId = roomInstanceId;
            playerExtra.LevelingRoomActiveSeconds = 0;
            playerExtra.LevelingLastHeartbeatUnixTime =
                lastHeartbeatUnixTime;
        }

        public static void BanPlayer(
            long playerId,
            int durationSeconds,
            string reason,
            ulong bannedByPlayerId = 0)
        {
            var player = Players.FindById(playerId);

            if (player?.Player == null)
                return;

            player.Player.PlayerExtra ??= new PlayerExtra();

            string appealCode = GenerateAppealCode();
            string reasonWithAppeal =
                $"{reason}\n\nAppeal this ban: /recnet/banappeal?code={appealCode}";

            player.Player.PlayerExtra.ModerationBlockDetails =
                new ModerationBlockDetails
                {
                    ReportCategory =
                        ReportCategory.CoC_Discrimination,

                    Duration = durationSeconds,
                    IsBan = true,
                    Message = reasonWithAppeal,

                    ModerationSetUnixTime =
                        DateTimeOffset.UtcNow
                            .ToUnixTimeSeconds(),

                    BannedByPlayerId = bannedByPlayerId,
                    AppealCode = appealCode
                };

            ApplyBanStateToHeartbeat(
                player,
                playerId,
                out _);

            Players.Update(player);

            NotiController.ForceDisconnectPlayer(playerId);
        }

        private const string AppealCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private static string GenerateAppealCode()
        {
            Span<char> buffer = stackalloc char[10];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = AppealCodeChars[System.Random.Shared.Next(AppealCodeChars.Length)];
            return new string(buffer);
        }

        public static bool UnbanPlayer(long playerId)
        {
            var player = Players.FindById(playerId);

            if (player?
                .Player?
                .PlayerExtra?
                .ModerationBlockDetails == null)
            {
                return false;
            }

            player.Player.PlayerExtra.ModerationBlockDetails =
                new ModerationBlockDetails();

            Players.Update(player);

            return true;
        }

        public static bool UpdateEquipment(
            long playerId,
            List<EquipmentItem> equipment)
        {
            var player = Players.FindById(playerId);

            if (player?.Player == null)
                return false;

            player.Player.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Equipment = equipment;

            return Players.Update(player);
        }

        public static string? GetEmail(long playerId)
        {
            var player = Players.FindById(playerId);

            return player?.Player?.Email;
        }

        public static bool UpdateDisplayEmoji(
    long playerId,
    string displayEmoji)
        {
            if (playerId <= 0)
                return false;

            var player = Players.FindById(playerId);

            if (player?.Player == null)
                return false;

            player.Player.DisplayEmoji = displayEmoji ?? "";

            return Players.Update(player);
        }

        public static bool UpdateEmail(
            long playerId,
            string email)
        {
            var player = Players.FindById(playerId);

            if (player?.Player == null)
                return false;

            player.Player.Email = email;

            return Players.Update(player);
        }

        public static bool IsPlayerBanned(
            long playerId,
            out ModerationBlockDetails? details)
        {
            details = null;

            var player = Players.FindById(playerId);

            var mod = player?
                .Player?
                .PlayerExtra?
                .ModerationBlockDetails;

            if (mod == null || mod.IsBan != true)
                return false;

            long now = DateTimeOffset.UtcNow
                .ToUnixTimeSeconds();

            bool permanentBan = mod.Duration <= 0;

            bool stillBanned =
                permanentBan ||
                (mod.ModerationSetUnixTime + mod.Duration) > now;

            details = stillBanned ? mod : null;

            return stillBanned;
        }
    }
}
