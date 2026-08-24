using System.Collections.Concurrent;

namespace Mocha2023.Classes.DBs;

public static class MassImportJobs
{
    public sealed class RoomOutcome
    {
        public string Name { get; init; } = string.Empty;
        public string? SourceUrl { get; init; }
        public bool Success { get; set; }
        public string? Error { get; set; }
        public long? RoomId { get; set; }
        public int SubRoomsImported { get; set; }
        public int SavesImported { get; set; }
        public int BakedAssetsImported { get; set; }
        public int AssetBundlesCopied { get; set; }
    }

    public sealed class Job
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Kind { get; init; } = string.Empty;
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public DateTime? FinishedAt { get; set; }

        public string Status { get; set; } = "pending";
        public string? FatalError { get; set; }
        public int TotalFound { get; set; }
        public string? CurrentRoomName { get; set; }

        private readonly object listLock = new();
        private readonly List<RoomOutcome> results = new();

        public void AddResult(RoomOutcome outcome)
        {
            lock (listLock)
                results.Add(outcome);
        }

        public List<RoomOutcome> SnapshotResults()
        {
            lock (listLock)
                return new List<RoomOutcome>(results);
        }
    }

    private static readonly ConcurrentDictionary<string, Job> Jobs = new();

    private static readonly TimeSpan Retention = TimeSpan.FromHours(6);

    public static Job Create(string kind)
    {
        var job = new Job { Kind = kind };
        Jobs[job.Id] = job;

        DateTime cutoff = DateTime.UtcNow - Retention;
        foreach (var pair in Jobs)
        {
            if (pair.Value.FinishedAt is DateTime finishedAt && finishedAt < cutoff)
                Jobs.TryRemove(pair.Key, out _);
        }

        return job;
    }

    public static Job? Get(string id) => Jobs.TryGetValue(id, out Job? job) ? job : null;
}
