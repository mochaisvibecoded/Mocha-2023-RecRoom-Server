using LiteDB;

namespace Mocha2023.Classes.DBs
{
    public static class ReportsDB
    {
        private static readonly LiteDatabase Database =
            new(Path.Combine(Program.dataDir, "DBs", "Reports.db"));

        public static readonly ILiteCollection<PlayerReport> PlayerReports =
            Database.GetCollection<PlayerReport>("PlayerReports");

        public static readonly ILiteCollection<BugReport> BugReports =
            Database.GetCollection<BugReport>("BugReports");

        static ReportsDB()
        {
            PlayerReports.EnsureIndex(report => report.Status);
            PlayerReports.EnsureIndex(report => report.CreatedAt);
            PlayerReports.EnsureIndex(report => report.ReportedPlayerId);
            BugReports.EnsureIndex(report => report.Status);
            BugReports.EnsureIndex(report => report.CreatedAt);
        }

        public class PlayerReport
        {
            [BsonId]
            public Guid Id { get; set; } = Guid.NewGuid();

            public long ReporterId { get; set; }
            public string? ReporterUsername { get; set; }
            public long? ReportedPlayerId { get; set; }
            public string? ReportedUsername { get; set; }
            public int? ReportCategory { get; set; }
            public string? Details { get; set; }
            public long? RoomId { get; set; }
            public string? RoomInstanceType { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            // Pending, Banned, TimedOut, NoAction
            public string Status { get; set; } = "Pending";

            public long? ResolvedByAccountId { get; set; }
            public string? ResolvedByUsername { get; set; }
            public DateTime? ResolvedAt { get; set; }
            public string? ResolutionNote { get; set; }
            public int? ActionDurationSeconds { get; set; }
        }

        public class BugReport
        {
            [BsonId]
            public Guid Id { get; set; } = Guid.NewGuid();

            public long ReporterId { get; set; }
            public string? ReporterUsername { get; set; }
            public string? Description { get; set; }
            public string? Category { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            // Open, Closed
            public string Status { get; set; } = "Open";

            public long? ResolvedByAccountId { get; set; }
            public string? ResolvedByUsername { get; set; }
            public DateTime? ResolvedAt { get; set; }
        }
    }
}
