using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mocha2023.Classes.DBs.DBClasses;

namespace Mocha2023.Classes.DBs
{

    public static class SteamAccessDB
    {
        private static readonly object Sync = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static Dictionary<ulong, SteamBlacklistEntry>? Entries;

        private static string BlacklistPath =>
            Path.Combine(Program.dataDir, "SteamBlacklist.json");

        public static bool IsBlacklisted(ulong steamId)
        {
            if (steamId == 0)
                return false;

            lock (Sync)
            {
                EnsureLoadedLocked();
                return Entries!.ContainsKey(steamId);
            }
        }

        public static bool TryGetBlockedSteamId(
            PlayerDBClasses.FullPlayer? player,
            out ulong steamId)
        {
            steamId = 0;

            if (player?.PlatformIds == null)
                return false;

            ulong[] linkedSteamIds = player.PlatformIds
                .Where(identity =>
                    identity.Platform == PlayerDBClasses.Platforms.Steam &&
                    identity.PlatformId > 0)
                .Select(identity => identity.PlatformId)
                .Distinct()
                .ToArray();

            lock (Sync)
            {
                EnsureLoadedLocked();

                foreach (ulong linkedSteamId in linkedSteamIds)
                {
                    if (Entries!.ContainsKey(linkedSteamId))
                    {
                        steamId = linkedSteamId;
                        return true;
                    }
                }
            }

            return false;
        }

        public static IReadOnlyList<SteamBlacklistEntry> GetAll()
        {
            lock (Sync)
            {
                EnsureLoadedLocked();

                return Entries!.Values
                    .OrderByDescending(entry => entry.AddedAt)
                    .ThenBy(entry => entry.SteamId)
                    .Select(Clone)
                    .ToList();
            }
        }

        public static SteamBlacklistEntry AddOrUpdate(
            ulong steamId,
            string? reason,
            long addedByAccountId)
        {
            if (steamId == 0)
                throw new ArgumentOutOfRangeException(
                    nameof(steamId),
                    "Steam ID must be greater than zero.");

            string cleanReason = (reason ?? string.Empty).Trim();
            if (cleanReason.Length == 0)
                cleanReason = "Blacklisted by an administrator.";
            if (cleanReason.Length > 500)
                cleanReason = cleanReason[..500];

            lock (Sync)
            {
                EnsureLoadedLocked();

                var entry = new SteamBlacklistEntry
                {
                    SteamId = steamId,
                    Reason = cleanReason,
                    AddedByAccountId = addedByAccountId,
                    AddedAt = DateTime.UtcNow
                };

                Entries![steamId] = entry;
                SaveLocked();
                return Clone(entry);
            }
        }

        public static bool Remove(ulong steamId)
        {
            if (steamId == 0)
                return false;

            lock (Sync)
            {
                EnsureLoadedLocked();

                if (!Entries!.Remove(steamId))
                    return false;

                SaveLocked();
                return true;
            }
        }

        private static void EnsureLoadedLocked()
        {
            if (Entries != null)
                return;

            Entries = new Dictionary<ulong, SteamBlacklistEntry>();
            string path = BlacklistPath;

            if (!File.Exists(path))
                return;

            try
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<
                    List<SteamBlacklistEntry>>(json, JsonOptions)
                    ?? new List<SteamBlacklistEntry>();

                Entries = loaded
                    .Where(entry => entry.SteamId > 0)
                    .GroupBy(entry => entry.SteamId)
                    .ToDictionary(
                        group => group.Key,
                        group => Clone(group
                            .OrderByDescending(entry => entry.AddedAt)
                            .First()));
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[STEAM BLACKLIST LOAD FAILED] " +
                    $"{exception.GetType().Name}: {exception.Message}");

                Entries = new Dictionary<ulong, SteamBlacklistEntry>();
            }
        }

        private static void SaveLocked()
        {
            string path = BlacklistPath;
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = path + ".tmp";
            string json = JsonSerializer.Serialize(
                Entries!.Values
                    .OrderBy(entry => entry.SteamId)
                    .ToList(),
                JsonOptions);

            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }

        private static SteamBlacklistEntry Clone(
            SteamBlacklistEntry entry) =>
            new()
            {
                SteamId = entry.SteamId,
                Reason = entry.Reason,
                AddedByAccountId = entry.AddedByAccountId,
                AddedAt = entry.AddedAt
            };

        public sealed class SteamBlacklistEntry
        {
            public ulong SteamId { get; set; }
            public string Reason { get; set; } = string.Empty;
            public long AddedByAccountId { get; set; }
            public DateTime AddedAt { get; set; }
        }
    }
}
