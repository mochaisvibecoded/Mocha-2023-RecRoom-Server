using LiteDB;

namespace Mocha2023.Classes.DBs;

public static class ScannerLogDB
{
    public class ScannerAttempt
    {
        [BsonId]
        public long Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string IpAddress { get; set; } = "unknown";
        public string Method { get; set; } = "";
        public string Path { get; set; } = "";
        public string? QueryString { get; set; }
        public string? UserAgent { get; set; }

        public string? MatchedPattern { get; set; }
    }

    private static readonly LiteDatabase DB = new(
        Path.Combine(Program.dataDir, "DBs", "ScannerLogs.db"));

    public static readonly ILiteCollection<ScannerAttempt> Attempts =
        DB.GetCollection<ScannerAttempt>("Attempts");

    private const int MaxStored = 20_000;
    private const int TrimBatchSize = 1_000;

    public static void Log(
        string ipAddress,
        string method,
        string path,
        string? queryString,
        string? userAgent,
        string? matchedPattern)
    {
        Attempts.Insert(new ScannerAttempt
        {
            IpAddress = ipAddress,
            Method = method,
            Path = path,
            QueryString = queryString,
            UserAgent = userAgent,
            MatchedPattern = matchedPattern
        });

        long count = Attempts.LongCount();
        if (count > MaxStored)
        {
            List<long> oldestIds = Attempts.Query()
                .OrderBy(a => a.Id)
                .Limit(TrimBatchSize)
                .ToList()
                .Select(a => a.Id)
                .ToList();

            foreach (long id in oldestIds)
                Attempts.Delete(id);
        }
    }
}
