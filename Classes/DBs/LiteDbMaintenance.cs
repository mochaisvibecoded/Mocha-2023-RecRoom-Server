using System;
using System.Collections.Generic;
using System.Threading;
using LiteDB;

namespace Mocha2023.Classes.DBs
{

    public static class LiteDbMaintenance
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

        private static readonly List<Timer> ActiveTimers = new();

        public static void StartPeriodicCheckpoint(string name, LiteDatabase database)
        {
            var timer = new Timer(_ =>
            {
                try
                {
                    database.Checkpoint();
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"[LITEDB CHECKPOINT] {name} failed: {exception.Message}");
                }
            }, null, Interval, Interval);

            lock (ActiveTimers)
                ActiveTimers.Add(timer);
        }
    }
}
