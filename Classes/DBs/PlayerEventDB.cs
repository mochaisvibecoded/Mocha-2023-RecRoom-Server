using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;

namespace Mocha2023.Classes.DBs
{

    public static class PlayerEventDB
    {
        private static readonly LiteDatabase Database =
            new(Path.Combine(Program.dataDir, "DBs", "PlayerEvents.db"));

        private static readonly ILiteCollection<PlayerEvent> Events =
            Database.GetCollection<PlayerEvent>("Events");
        private static readonly ILiteCollection<PlayerEventResponse> Responses =
            Database.GetCollection<PlayerEventResponse>("Responses");

        private static readonly object Sync = new();

        static PlayerEventDB()
        {
            LiteDbMaintenance.StartPeriodicCheckpoint("PlayerEvents.db", Database);
            Events.EnsureIndex(value => value.CreatorAccountId);
            Events.EnsureIndex(value => value.ClubId);
            Events.EnsureIndex(value => value.StartsAt);
            Responses.EnsureIndex(value => value.EventId);
            Responses.EnsureIndex(value => value.AccountId);
        }

        public sealed class PlayerEvent
        {
            [BsonId]
            public long EventId { get; set; }
            public long CreatorAccountId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string ImageName { get; set; } = string.Empty;
            public DateTime StartsAt { get; set; }
            public DateTime? EndsAt { get; set; }
            public long? RoomId { get; set; }
            public long? ClubId { get; set; }

            public string Accessibility { get; set; } = "Public";
            public bool MultiInstance { get; set; }
            public List<string> Tags { get; set; } = new();
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        public sealed class PlayerEventResponse
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;
            public long EventId { get; set; }
            public long AccountId { get; set; }

            public string ResponseType { get; set; } = "Going";
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        private static string ResponseId(long eventId, long accountId) => $"{eventId}:{accountId}";

        public static long GetNextEventId()
        {
            lock (Sync)
                return Events.Count() == 0 ? 1 : Events.Max(value => value.EventId) + 1;
        }

        public static PlayerEvent Create(PlayerEvent evt)
        {
            lock (Sync)
            {
                evt.EventId = GetNextEventId();
                evt.CreatedAt = DateTime.UtcNow;
                evt.UpdatedAt = evt.CreatedAt;
                Events.Insert(evt);
                return evt;
            }
        }

        public static PlayerEvent? Get(long eventId) => Events.FindById(eventId);

        public static List<PlayerEvent> GetByIds(IEnumerable<long> ids)
        {
            var idSet = ids.ToHashSet();
            return Events.Find(value => idSet.Contains(value.EventId)).ToList();
        }

        public static List<PlayerEvent> GetByCreator(long accountId) =>
            Events.Find(value => value.CreatorAccountId == accountId)
                .OrderByDescending(value => value.StartsAt)
                .ToList();

        public static List<PlayerEvent> GetByClub(long clubId) =>
            Events.Find(value => value.ClubId == clubId)
                .OrderByDescending(value => value.StartsAt)
                .ToList();

        public static bool Update(PlayerEvent evt)
        {
            evt.UpdatedAt = DateTime.UtcNow;
            return Events.Update(evt);
        }

        public static bool Delete(long eventId)
        {
            lock (Sync)
            {
                Responses.DeleteMany(value => value.EventId == eventId);
                return Events.Delete(eventId);
            }
        }

        public static PlayerEventResponse SetResponse(long eventId, long accountId, string responseType)
        {
            var response = new PlayerEventResponse
            {
                Id = ResponseId(eventId, accountId),
                EventId = eventId,
                AccountId = accountId,
                ResponseType = responseType,
                CreatedAt = DateTime.UtcNow
            };
            Responses.Upsert(response);
            return response;
        }

        public static bool DeleteResponse(long eventId, long accountId) =>
            Responses.Delete(ResponseId(eventId, accountId));

        public static List<PlayerEventResponse> GetResponses(long eventId) =>
            Responses.Find(value => value.EventId == eventId).ToList();

        public static PlayerEventResponse? GetResponse(long eventId, long accountId) =>
            Responses.FindById(ResponseId(eventId, accountId));
    }
}
