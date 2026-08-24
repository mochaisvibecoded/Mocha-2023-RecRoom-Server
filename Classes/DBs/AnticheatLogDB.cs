using LiteDB;

namespace Mocha2023.Classes.DBs;

public static class AnticheatLogDB
{
    public class AnticheatEntry
    {
        [BsonId]
        public long Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string IpAddress { get; set; } = "unknown";
        public long? AccountId { get; set; }
        public string? SteamId { get; set; }
        public string? Build { get; set; }
        public string Flags { get; set; } = "";
        public string? UserAgent { get; set; }
    }

    private static readonly LiteDatabase DB = new(
        Path.Combine(Program.dataDir, "DBs", "AnticheatLogs.db"));

    public static readonly ILiteCollection<AnticheatEntry> Entries =
        DB.GetCollection<AnticheatEntry>("Entries");

    private const int MaxStored = 20_000;
    private const int TrimBatchSize = 1_000;

    public static void Log(
        string ipAddress,
        long? accountId,
        string? steamId,
        string? build,
        string flags,
        string? userAgent)
    {
        Entries.Insert(new AnticheatEntry
        {
            IpAddress = ipAddress,
            AccountId = accountId,
            SteamId = steamId,
            Build = build,
            Flags = flags,
            UserAgent = userAgent
        });

        long count = Entries.LongCount();
        if (count > MaxStored)
        {
            List<long> oldestIds = Entries.Query()
                .OrderBy(a => a.Id)
                .Limit(TrimBatchSize)
                .ToList()
                .Select(a => a.Id)
                .ToList();

            foreach (long id in oldestIds)
                Entries.Delete(id);
        }
    }
}
