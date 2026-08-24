using Mocha2023.Auth;
using LiteDB;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mocha2023.Controllers
{

    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    public sealed class LegacyLeaderboardController : ControllerBase
    {
        private static readonly object DbLock = new();

        private static readonly LiteDatabase LeaderboardDb = OpenDatabase();

        private static readonly ILiteCollection<LeaderboardStat> Stats =
            LeaderboardDb.GetCollection<LeaderboardStat>("LeaderboardStats");

        static LegacyLeaderboardController()
        {
            Stats.EnsureIndex(x => x.RoomId);
            Stats.EnsureIndex(x => x.StatChannel);
            Stats.EnsureIndex(x => x.AccountId);
        }

        [HttpPost("/leaderboard/CheckAndSetStat")]
        [HttpPost("/api/Leaderboard/CheckAndSetStat")]
        public async Task<IActionResult> CheckAndSetStat()
        {
            long? authenticatedAccountId = AuthStuff.GetPlayerId(Request);
            if (authenticatedAccountId == null)
                return Unauthorized();

            RequestValues values;
            try
            {
                values = await ReadRequestValuesAsync();
            }
            catch (JsonException exception)
            {
                Console.WriteLine(
                    $"[LEADERBOARD] Invalid CheckAndSetStat JSON: {exception.Message}");
                return BadRequest((int)LeaderboardResult.InvalidStat);
            }

            int statChannel = values.GetInt32("StatChannel");
            long roomId = values.GetInt64("RoomId");
            int statValue = values.GetInt32("StatValue");
            int? expectedCurrentValue = values.GetNullableInt32("CurrentStatValue");

            if (statChannel < 0 || roomId < 0)
                return Ok((int)LeaderboardResult.InvalidStat);

            long accountId = authenticatedAccountId.Value;
            string id = CreateStatId(roomId, statChannel, accountId);

            lock (DbLock)
            {
                LeaderboardStat? existing = Stats.FindById(id);
                int currentValue = existing?.Score ?? 0;

                if (existing != null &&
                    expectedCurrentValue.HasValue &&
                    expectedCurrentValue.Value != currentValue)
                {
                    Console.WriteLine(
                        $"[LEADERBOARD CAS] rejected stale update " +
                        $"account={accountId} room={roomId} channel={statChannel} " +
                        $"expected={expectedCurrentValue.Value} actual={currentValue}");

                    return Ok((int)LeaderboardResult.Success);
                }

                var row = existing ?? new LeaderboardStat
                {
                    Id = id,
                    AccountId = accountId,
                    RoomId = roomId,
                    StatChannel = statChannel
                };

                row.Score = statValue;
                row.UpdatedAt = DateTime.UtcNow;
                Stats.Upsert(row);
            }

            Console.WriteLine(
                $"[LEADERBOARD SET] account={accountId} room={roomId} " +
                $"channel={statChannel} score={statValue}");

            return Ok((int)LeaderboardResult.Success);
        }

        [HttpPost("/leaderboard/GetPlayerRank")]
        [HttpPost("/api/Leaderboard/GetPlayerRank")]
        public async Task<IActionResult> GetPlayerRank()
        {
            long? authenticatedAccountId = AuthStuff.GetPlayerId(Request);
            if (authenticatedAccountId == null)
                return Unauthorized();

            RankRequest request;
            try
            {
                request = RankRequest.From(await ReadRequestValuesAsync());
            }
            catch (JsonException exception)
            {
                Console.WriteLine(
                    $"[LEADERBOARD] Invalid GetPlayerRank JSON: {exception.Message}");
                return BadRequest();
            }

            if (request.StatChannel < 0 || request.RoomId < 0)
                return BadRequest();

            long requestedPlayerId = request.PlayerId > 0
                ? request.PlayerId
                : authenticatedAccountId.Value;

            List<RankedStat> ranked = GetRankedStats(request);
            RankedStat? player = ranked.FirstOrDefault(
                x => x.AccountId == requestedPlayerId);

            return Ok(new PlayerRankResponse
            {
                Rank = player?.Rank ?? 0,
                Score = player?.Score ?? 0
            });
        }

        [HttpPost("/leaderboard/GetNearbyScores")]
        [HttpPost("/api/Leaderboard/GetNearbyScores")]
        public async Task<IActionResult> GetNearbyScores()
        {
            long? authenticatedAccountId = AuthStuff.GetPlayerId(Request);
            if (authenticatedAccountId == null)
                return Unauthorized();

            RankRequest request;
            try
            {
                RequestValues values = await ReadRequestValuesAsync();
                request = RankRequest.From(values);
                request.WindowSize = Math.Clamp(
                    values.GetInt32("WindowSize", 32),
                    1,
                    100);
            }
            catch (JsonException exception)
            {
                Console.WriteLine(
                    $"[LEADERBOARD] Invalid GetNearbyScores JSON: {exception.Message}");
                return BadRequest();
            }

            if (request.StatChannel < 0 || request.RoomId < 0)
                return BadRequest();

            long requestedPlayerId = request.PlayerId > 0
                ? request.PlayerId
                : authenticatedAccountId.Value;

            List<RankedStat> ranked = GetRankedStats(request);
            int playerIndex = ranked.FindIndex(
                x => x.AccountId == requestedPlayerId);

            if (playerIndex < 0)
            {
                return Ok(new NearbyScoresResponse
                {
                    Results = new List<LeaderboardEntry>()
                });
            }

            int halfWindow = request.WindowSize / 2;
            int start = Math.Max(0, playerIndex - halfWindow);

            if (start + request.WindowSize > ranked.Count)
                start = Math.Max(0, ranked.Count - request.WindowSize);

            List<LeaderboardEntry> results = ranked
                .Skip(start)
                .Take(request.WindowSize)
                .Select(ToEntry)
                .ToList();

            return Ok(new NearbyScoresResponse
            {
                Results = results
            });
        }

        [HttpPost("/leaderboard/GetRanks")]
        [HttpPost("/api/Leaderboard/GetRanks")]
        public async Task<IActionResult> GetRanks()
        {
            long? authenticatedAccountId = AuthStuff.GetPlayerId(Request);
            if (authenticatedAccountId == null)
                return Unauthorized();

            RankRequest request;
            int rankStart;
            int rankEnd;

            try
            {
                RequestValues values = await ReadRequestValuesAsync();
                request = RankRequest.From(values);
                rankStart = Math.Max(1, values.GetInt32("RankStart", 1));
                rankEnd = Math.Max(rankStart, values.GetInt32("RankEnd", rankStart + 49));
            }
            catch (JsonException exception)
            {
                Console.WriteLine(
                    $"[LEADERBOARD] Invalid GetRanks JSON: {exception.Message}");
                return BadRequest();
            }

            if (request.StatChannel < 0 || request.RoomId < 0)
                return BadRequest();

            rankEnd = Math.Min(rankEnd, rankStart + 99);

            List<LeaderboardEntry> results = GetRankedStats(request)
                .Where(x => x.Rank >= rankStart && x.Rank <= rankEnd)
                .Select(ToEntry)
                .ToList();

            return Ok(new NearbyScoresResponse
            {
                Results = results
            });
        }

        private static List<RankedStat> GetRankedStats(RankRequest request)
        {
            List<LeaderboardStat> rows;

            lock (DbLock)
            {
                rows = Stats.Find(x =>
                        x.RoomId == request.RoomId &&
                        x.StatChannel == request.StatChannel)
                    .ToList();
            }

            IOrderedEnumerable<LeaderboardStat> ordered =
                request.SortAscending
                    ? rows.OrderBy(x => x.Score)
                        .ThenBy(x => x.UpdatedAt)
                        .ThenBy(x => x.AccountId)
                    : rows.OrderByDescending(x => x.Score)
                        .ThenBy(x => x.UpdatedAt)
                        .ThenBy(x => x.AccountId);

            return ordered
                .Select((row, index) => new RankedStat
                {
                    AccountId = row.AccountId,
                    Rank = index + 1,
                    Score = row.Score
                })
                .ToList();
        }

        private static LeaderboardEntry ToEntry(RankedStat stat) =>
            new()
            {
                PlayerId = SafePlayerId(stat.AccountId),
                Rank = stat.Rank,
                Score = stat.Score
            };

        private static int SafePlayerId(long accountId)
        {
            if (accountId <= 0)
                return 0;

            return accountId > int.MaxValue
                ? int.MaxValue
                : (int)accountId;
        }

        private static string CreateStatId(
            long roomId,
            int statChannel,
            long accountId) =>
            FormattableString.Invariant(
                $"{roomId}:{statChannel}:{accountId}");

        private static LiteDatabase OpenDatabase()
        {
            string dbDirectory = Path.Combine(Program.dataDir, "DBs");
            Directory.CreateDirectory(dbDirectory);

            return new LiteDatabase(
                Path.Combine(dbDirectory, "Leaderboards.db"));
        }

        private async Task<RequestValues> ReadRequestValuesAsync()
        {
            var values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            if (Request.HasFormContentType)
            {
                IFormCollection form = await Request.ReadFormAsync();

                foreach (string key in form.Keys)
                    values[key] = form[key].FirstOrDefault() ?? string.Empty;

                return new RequestValues(values);
            }

            using var reader = new StreamReader(Request.Body);
            string rawBody = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(rawBody))
                return new RequestValues(values);

            using JsonDocument document = JsonDocument.Parse(rawBody);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Expected a JSON object.");

            foreach (JsonProperty property in
                     document.RootElement.EnumerateObject())
            {
                values[property.Name] =
                    property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
            }

            return new RequestValues(values);
        }

        private enum LeaderboardResult
        {
            Success = 0,
            InvalidStat = 1,
            RedisConnectionError = 2
        }

        private sealed class LeaderboardStat
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;

            public long AccountId { get; set; }

            public long RoomId { get; set; }

            public int StatChannel { get; set; }

            public int Score { get; set; }

            public DateTime UpdatedAt { get; set; }
        }

        private sealed class RankRequest
        {
            public long PlayerId { get; set; }

            public int StatChannel { get; set; }

            public long RoomId { get; set; }

            public int FilterType { get; set; }

            public bool SortAscending { get; set; }

            public int WindowSize { get; set; } = 32;

            public static RankRequest From(RequestValues values) =>
                new()
                {
                    PlayerId = values.GetInt64("PlayerId"),
                    StatChannel = values.GetInt32("StatChannel"),
                    RoomId = values.GetInt64("RoomId"),
                    FilterType = values.GetInt32("FilterType"),
                    SortAscending = values.GetBoolean("SortAscending")
                };
        }

        private sealed class RankedStat
        {
            public long AccountId { get; set; }

            public int Rank { get; set; }

            public int Score { get; set; }
        }

        private sealed class PlayerRankResponse
        {
            public int Rank { get; set; }

            public int Score { get; set; }
        }

        private sealed class NearbyScoresResponse
        {
            public List<LeaderboardEntry> Results { get; set; } = new();
        }

        private sealed class LeaderboardEntry
        {
            public int PlayerId { get; set; }

            public int Rank { get; set; }

            public int Score { get; set; }
        }

        private sealed class RequestValues
        {
            private readonly IReadOnlyDictionary<string, string> _values;

            public RequestValues(
                IReadOnlyDictionary<string, string> values)
            {
                _values = values;
            }

            public int GetInt32(string key, int fallback = 0)
            {
                return _values.TryGetValue(key, out string? value) &&
                       int.TryParse(
                           value,
                           NumberStyles.Integer,
                           CultureInfo.InvariantCulture,
                           out int parsed)
                    ? parsed
                    : fallback;
            }

            public long GetInt64(string key, long fallback = 0)
            {
                return _values.TryGetValue(key, out string? value) &&
                       long.TryParse(
                           value,
                           NumberStyles.Integer,
                           CultureInfo.InvariantCulture,
                           out long parsed)
                    ? parsed
                    : fallback;
            }

            public int? GetNullableInt32(string key)
            {
                if (!_values.TryGetValue(key, out string? value) ||
                    string.IsNullOrWhiteSpace(value) ||
                    string.Equals(
                        value,
                        "null",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsed)
                    ? parsed
                    : null;
            }

            public bool GetBoolean(string key, bool fallback = false)
            {
                if (!_values.TryGetValue(key, out string? value))
                    return fallback;

                if (bool.TryParse(value, out bool parsed))
                    return parsed;

                return value switch
                {
                    "1" => true,
                    "0" => false,
                    _ => fallback
                };
            }
        }
    }
}
