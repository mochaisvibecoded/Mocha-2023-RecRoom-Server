using System.Text.Json;
using Mocha2023.Classes.DBs.DBClasses;

namespace Mocha2023.Classes
{

    public static class EquipmentInventoryStore
    {
        private static readonly object EquipmentLock = new();
        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true
        };

        public static string GetPlayerEquipmentPath(long accountId)
        {
            string directory = Path.Combine(
                Program.dataDir,
                "SkinUnlocked",
                accountId.ToString());
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "Equipment.json");
        }

        public static void EnsureInitialized(long accountId)
        {
            lock (EquipmentLock)
            {
                string playerPath = GetPlayerEquipmentPath(accountId);
                if (File.Exists(playerPath))
                    return;

                string catalogPath = Path.Combine(
                    Program.dataDir,
                    "APIS",
                    "Items",
                    "EquipmentDefault.json");

                if (!File.Exists(catalogPath))
                {
                    File.WriteAllText(playerPath, "[]");
                    return;
                }

                try
                {
                    string catalogJson = File.ReadAllText(catalogPath);
                    List<PlayerDBClasses.EquipmentItem> catalog =
                        JsonSerializer.Deserialize<List<PlayerDBClasses.EquipmentItem>>(
                            catalogJson,
                            ReadOptions) ?? new List<PlayerDBClasses.EquipmentItem>();

                    File.WriteAllText(
                        playerPath,
                        JsonSerializer.Serialize(catalog, WriteOptions));
                }
                catch (JsonException ex)
                {
                    Console.WriteLine(
                        $"[EQUIPMENT INVENTORY] Could not seed account {accountId} from Equipment.json: {ex.Message}");
                    File.WriteAllText(playerPath, "[]");
                }
            }
        }

        public static List<PlayerDBClasses.EquipmentItem> GetOrCreate(long accountId)
        {
            lock (EquipmentLock)
            {
                EnsureInitialized(accountId);
                string json = File.ReadAllText(GetPlayerEquipmentPath(accountId));
                return JsonSerializer.Deserialize<List<PlayerDBClasses.EquipmentItem>>(
                    json,
                    ReadOptions) ?? new List<PlayerDBClasses.EquipmentItem>();
            }
        }

        public static bool AddOwnedItem(
            long accountId,
            string prefabName,
            string modificationGuid) =>
            AddOwnedItem(
                accountId,
                prefabName,
                modificationGuid,
                friendlyName: null,
                tooltip: null,
                rarity: null,
                thumbnailImage: null);

        public static bool AddOwnedItem(
            long accountId,
            string prefabName,
            string modificationGuid,
            string? friendlyName,
            string? tooltip,
            int? rarity,
            string? thumbnailImage)
        {
            if (accountId <= 0 ||
                string.IsNullOrWhiteSpace(prefabName) ||
                string.IsNullOrWhiteSpace(modificationGuid))
            {
                return false;
            }

            lock (EquipmentLock)
            {
                List<PlayerDBClasses.EquipmentItem> equipment =
                    GetOrCreate(accountId);

                if (equipment.Any(item =>
                        string.Equals(
                            item.PrefabName,
                            prefabName,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            item.ModificationGuid,
                            modificationGuid,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                PlayerDBClasses.EquipmentItem? catalogItem = null;
                string catalogPath = Path.Combine(
                    Program.dataDir,
                    "APIS",
                    "Items",
                    "EquipmentDefault.json");

                if (File.Exists(catalogPath))
                {
                    try
                    {
                        List<PlayerDBClasses.EquipmentItem> catalog =
                            JsonSerializer.Deserialize<List<PlayerDBClasses.EquipmentItem>>(
                                File.ReadAllText(catalogPath),
                                ReadOptions) ?? new List<PlayerDBClasses.EquipmentItem>();

                        catalogItem = catalog.FirstOrDefault(item =>
                            string.Equals(
                                item.PrefabName,
                                prefabName,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                item.ModificationGuid,
                                modificationGuid,
                                StringComparison.OrdinalIgnoreCase));
                    }
                    catch (JsonException exception)
                    {
                        Console.WriteLine(
                            $"[EQUIPMENT INVENTORY] Could not read catalog while granting gift: {exception.Message}");
                    }
                }

                PlayerDBClasses.EquipmentItem newItem = catalogItem ?? new PlayerDBClasses.EquipmentItem
                {
                    PrefabName = prefabName,
                    ModificationGuid = modificationGuid,
                    UnlockedLevel = 0,
                    Favorited = false,
                    PlatformMask = -1
                };

                if (!string.IsNullOrWhiteSpace(friendlyName))
                    newItem.FriendlyName = friendlyName.Trim();
                if (!string.IsNullOrWhiteSpace(tooltip))
                    newItem.Tooltip = tooltip.Trim();
                if (rarity.HasValue)
                    newItem.Rarity = rarity.Value;
                if (!string.IsNullOrWhiteSpace(thumbnailImage))
                    newItem.ThumbnailImage = thumbnailImage.Trim();

                equipment.Add(newItem);

                Save(accountId, equipment);
                return true;
            }
        }

        public static void Save(
            long accountId,
            List<PlayerDBClasses.EquipmentItem> equipment)
        {
            lock (EquipmentLock)
            {
                string json = JsonSerializer.Serialize(equipment, WriteOptions);
                File.WriteAllText(GetPlayerEquipmentPath(accountId), json);
            }
        }
    }
}
