using System.Text.Json;
using Mocha2023.Auth;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs;
using Mocha2023.Classes.DBs.DBClasses;
using Microsoft.AspNetCore.Mvc;

namespace Mocha2023.Controllers
{
    [ApiController]
    [ApiProtection]
    public sealed class CreatorFeatureController : ControllerBase
    {
        private const int MaxSmallPayloadBytes = 64 * 1024;

        [HttpPost("/api/meetupcodes/v1")]
        [HttpPut("/api/meetupcodes/v1")]
        [HttpPost("/api/meetupcodes/v1/create")]
        [HttpPut("/api/meetupcodes/v1/create")]
        [HttpPost("/api/meetups/v1/create")]
        [HttpPut("/api/meetups/v1/create")]
        [HttpPost("/api/meetup/v1/create")]
        [HttpPost("/api/codes/v1/create")]
        [HttpPost("/api/players/v1/meetupcode")]
        [HttpPut("/api/players/v1/meetupcode")]
        [HttpPost("/api/players/v1/meetupcode/create")]
        [RequestSizeLimit(MaxSmallPayloadBytes)]
        public async Task<IActionResult> CreateMeetupCode()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            PlayerDBClasses.Heartbeat heartbeat =
                PlayerDB.GetPlayerHeartbeat(accountId.Value);
            PlayerDBClasses.RoomInstance? instance = heartbeat.roomInstance;
            if (instance == null || instance.roomId <= 0 ||
                instance.roomInstanceId <= 0)
            {
                return BadRequest(new
                {
                    Success = false,
                    Error = "You must be inside a live room before creating a meetup code."
                });
            }

            Dictionary<string, string> values = await ReadValuesAsync();
            int maxUses = ReadInt(values, 100,
                "maxUses", "MaxUses", "uses", "Uses");
            int lifetimeMinutes = ReadInt(values, 360,
                "lifetimeMinutes", "LifetimeMinutes", "expiresInMinutes");

            CreatorFeatureDB.MeetupCodeRecord code =
                CreatorFeatureDB.CreateMeetupCode(
                    accountId.Value,
                    instance.roomId,
                    instance.subRoomId,
                    instance.roomInstanceId,
                    maxUses,
                    lifetimeMinutes);

            Console.WriteLine(
                $"[MEETUP CODE CREATE] code={code.Code} creator={accountId.Value} " +
                $"room={code.RoomId} instance={code.RoomInstanceId}");

            object dto = CreatorFeatureDB.ToClientMeetupCode(code);
            return Ok(new
            {
                Success = true,
                Value = dto,
                MeetupCode = code.Code,
                Code = code.Code,
                RoomId = code.RoomId,
                RoomInstanceId = code.RoomInstanceId,
                ExpiresAt = code.ExpiresAtUtc
            });
        }

        [HttpGet("/api/meetupcodes/v1/{code}")]
        [HttpGet("/api/meetups/v1/{code}")]
        [HttpGet("/api/meetup/v1/{code}")]
        [HttpGet("/api/codes/v1/{code}")]
        [HttpGet("/api/players/v1/meetupcode/{code}")]
        public IActionResult GetMeetupCode(string code)
        {
            if (!AuthStuff.GetPlayerId(Request).HasValue)
                return Unauthorized();

            CreatorFeatureDB.MeetupCodeRecord? record =
                CreatorFeatureDB.GetMeetupCode(code);
            return record == null
                ? NotFound(new { Success = false, Error = "meetup_code_not_found" })
                : Ok(CreatorFeatureDB.ToClientMeetupCode(record));
        }

        [HttpPost("/api/meetupcodes/v1/redeem")]
        [HttpPut("/api/meetupcodes/v1/redeem")]
        [HttpPost("/api/meetupcodes/v1/join")]
        [HttpPut("/api/meetupcodes/v1/join")]
        [HttpPost("/api/meetups/v1/redeem")]
        [HttpPost("/api/meetups/v1/join")]
        [HttpPost("/api/meetup/v1/redeem")]
        [HttpPost("/api/meetup/v1/join")]
        [HttpPost("/api/codes/v1/redeem")]
        [HttpPost("/api/codes/v1/join")]
        [HttpPost("/api/players/v1/meetupcode/redeem")]
        [HttpPut("/api/players/v1/meetupcode/redeem")]
        [HttpPost("/api/players/v1/meetupcode/join")]
        [HttpPost("/api/meetupcodes/v1/redeem/{code}")]
        [HttpPut("/api/meetupcodes/v1/redeem/{code}")]
        [HttpPost("/api/meetupcodes/v1/join/{code}")]
        [HttpPut("/api/meetupcodes/v1/join/{code}")]
        [HttpPost("/api/meetupcodes/v1/{code}")]
        [HttpPut("/api/meetupcodes/v1/{code}")]
        [HttpPost("/api/meetups/v1/{code}")]
        [HttpPut("/api/meetups/v1/{code}")]
        [RequestSizeLimit(MaxSmallPayloadBytes)]
        public async Task<IActionResult> RedeemMeetupCode(string? code = null)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            Dictionary<string, string> values = await ReadValuesAsync();
            code ??= ReadString(values,
                "code", "Code", "meetupCode", "MeetupCode", "inviteCode");

            CreatorFeatureDB.MeetupCodeRecord? record =
                CreatorFeatureDB.GetMeetupCode(code);
            if (record == null)
            {
                return NotFound(new
                {
                    Success = false,
                    Error = "meetup_code_not_found",
                    ErrorMessage = "That meetup code is invalid or expired."
                });
            }

            PlayerDBClasses.Heartbeat? heartbeat = Sessions.JoinInvitedRoomInstance(
                accountId.Value,
                record.RoomInstanceId,
                record.CreatorAccountId,
                record.RoomId);
            if (heartbeat?.roomInstance == null)
            {
                return NotFound(new
                {
                    Success = false,
                    Error = "meetup_room_unavailable",
                    ErrorMessage = "The room for this meetup code is no longer available."
                });
            }

            CreatorFeatureDB.RecordMeetupCodeUse(record.Code);
            PlayerDB.RecordRoomVisit(accountId.Value, record.RoomId);
            await NotiController.NotifyPlayerPresenceUpdatedAsync(accountId.Value);

            Console.WriteLine(
                $"[MEETUP CODE JOIN] code={record.Code} player={accountId.Value} " +
                $"room={record.RoomId} instance={record.RoomInstanceId}");
            return Ok(heartbeat);
        }

        [HttpDelete("/api/meetupcodes/v1/{code}")]
        [HttpDelete("/api/meetups/v1/{code}")]
        public IActionResult RevokeMeetupCode(string code)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();
            return CreatorFeatureDB.RevokeMeetupCode(code, accountId.Value)
                ? Ok(new { Success = true })
                : NotFound(new { Success = false, Error = "meetup_code_not_found" });
        }

        [HttpGet("/api/cloudvariables/v1")]
        [HttpGet("/api/cloudvariables/v1/room/{roomId:long}")]
        [HttpGet("/api/roomvariables/v1")]
        [HttpGet("/api/roomvariables/v1/room/{roomId:long}")]
        [HttpGet("/api/roomvariables/v1/{roomId:long}")]
        [HttpGet("/api/cloudvariables/v1/list/{roomId:long}")]
        [HttpGet("/roomserver/rooms/{roomId:long}/variables")]
        [HttpGet("/roomserver/rooms/{roomId:long}/cloudvariables")]
        public IActionResult GetCloudVariables(long? roomId = null)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            long resolvedRoomId = ResolveRoomId(roomId);
            if (resolvedRoomId <= 0 || RoomDB.GetRoom(resolvedRoomId) == null)
                return NotFound(new { Success = false, Error = "room_not_found" });

            bool routeDefaultsShared = IsRoomVariableRoute();
            bool sharedOnly = ReadBoolFromQuery(
                routeDefaultsShared,
                "sharedOnly", "SharedOnly", "globalOnly", "GlobalOnly");
            long scopeAccountId = sharedOnly ? 0 : accountId.Value;

            List<object> values = CreatorFeatureDB
                .GetCloudVariables(
                    resolvedRoomId,
                    scopeAccountId,
                    includeShared: !sharedOnly)
                .Select(CreatorFeatureDB.ToClientCloudVariable)
                .ToList();

            return Ok(new
            {
                Results = values,
                Variables = values,
                TotalResults = values.Count,
                RoomId = resolvedRoomId
            });
        }

        [HttpGet("/api/cloudvariables/v1/{key}")]
        [HttpGet("/api/cloudvariables/v1/room/{roomId:long}/{key}")]
        [HttpGet("/api/cloudvariables/v1/{roomId:long}/{key}")]
        [HttpGet("/api/roomvariables/v1/{key}")]
        [HttpGet("/api/roomvariables/v1/room/{roomId:long}/{key}")]
        [HttpGet("/api/roomvariables/v1/{roomId:long}/{key}")]
        [HttpGet("/roomserver/rooms/{roomId:long}/cloudvariables/{key}")]
        public IActionResult GetCloudVariable(string key, long? roomId = null)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            long resolvedRoomId = ResolveRoomId(roomId);
            if (resolvedRoomId <= 0)
                return BadRequest(new { Success = false, Error = "room_id_required" });

            bool roomScopedRoute = IsRoomVariableRoute();
            CreatorFeatureDB.CloudVariableRecord? value = roomScopedRoute
                ? CreatorFeatureDB.GetCloudVariable(resolvedRoomId, 0, key)
                    ?? CreatorFeatureDB.GetCloudVariable(
                        resolvedRoomId,
                        accountId.Value,
                        key)
                : CreatorFeatureDB.GetCloudVariable(
                    resolvedRoomId,
                    accountId.Value,
                    key)
                    ?? CreatorFeatureDB.GetCloudVariable(resolvedRoomId, 0, key);

            return value == null
                ? NotFound(new { Success = false, Error = "cloud_variable_not_found" })
                : Ok(CreatorFeatureDB.ToClientCloudVariable(value));
        }

        [HttpPost("/api/cloudvariables/v1")]
        [HttpPut("/api/cloudvariables/v1")]
        [HttpPatch("/api/cloudvariables/v1")]
        [HttpPost("/api/cloudvariables/v1/{key}")]
        [HttpPut("/api/cloudvariables/v1/{key}")]
        [HttpPost("/api/cloudvariables/v1/room/{roomId:long}")]
        [HttpPut("/api/cloudvariables/v1/room/{roomId:long}")]
        [HttpPost("/api/cloudvariables/v1/room/{roomId:long}/{key}")]
        [HttpPut("/api/cloudvariables/v1/room/{roomId:long}/{key}")]
        [HttpPatch("/api/cloudvariables/v1/room/{roomId:long}/{key}")]
        [HttpPost("/api/cloudvariables/v1/{roomId:long}/{key}")]
        [HttpPut("/api/cloudvariables/v1/{roomId:long}/{key}")]
        [HttpPatch("/api/cloudvariables/v1/{roomId:long}/{key}")]
        [HttpPost("/api/roomvariables/v1")]
        [HttpPut("/api/roomvariables/v1")]
        [HttpPatch("/api/roomvariables/v1")]
        [HttpPost("/api/roomvariables/v1/{roomId:long}")]
        [HttpPut("/api/roomvariables/v1/{roomId:long}")]
        [HttpPost("/api/roomvariables/v1/room/{roomId:long}/{key}")]
        [HttpPut("/api/roomvariables/v1/room/{roomId:long}/{key}")]
        [HttpPatch("/api/roomvariables/v1/room/{roomId:long}/{key}")]
        [HttpPost("/api/roomvariables/v1/{roomId:long}/{key}")]
        [HttpPut("/api/roomvariables/v1/{roomId:long}/{key}")]
        [HttpPatch("/api/roomvariables/v1/{roomId:long}/{key}")]
        [HttpPost("/roomserver/rooms/{roomId:long}/cloudvariables")]
        [HttpPut("/roomserver/rooms/{roomId:long}/cloudvariables")]
        [HttpPost("/roomserver/rooms/{roomId:long}/variables")]
        [HttpPut("/roomserver/rooms/{roomId:long}/variables")]
        [HttpPost("/roomserver/rooms/{roomId:long}/cloudvariables/{key}")]
        [HttpPut("/roomserver/rooms/{roomId:long}/cloudvariables/{key}")]
        [RequestSizeLimit(MaxSmallPayloadBytes)]
        public async Task<IActionResult> SetCloudVariable(
            long? roomId = null,
            string? key = null)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            Dictionary<string, string> values = await ReadValuesAsync();
            long resolvedRoomId = ResolveRoomId(roomId, values);
            RoomDBClasses.Room? room = RoomDB.GetRoom(resolvedRoomId);
            if (room == null)
                return NotFound(new { Success = false, Error = "room_not_found" });

            key ??= ReadString(values,
                "key", "Key", "name", "Name", "variable", "Variable");
            if (string.IsNullOrWhiteSpace(key))
                return BadRequest(new { Success = false, Error = "cloud_variable_key_required" });

            bool isShared = ReadBool(values, IsRoomVariableRoute(),
                "shared", "Shared", "isShared", "IsShared",
                "global", "Global", "roomScoped", "RoomScoped") ||
                string.Equals(
                    ReadString(values, "scope", "Scope"),
                    "room",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    ReadString(values, "scope", "Scope"),
                    "global",
                    StringComparison.OrdinalIgnoreCase);

            if (isShared && !CanWriteSharedCloudVariable(room, accountId.Value))
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new { Success = false, Error = "shared_cloud_variable_requires_room_access" });
            }

            string valueJson = ReadValueJson(values);
            long scopeAccountId = isShared ? 0 : accountId.Value;

            try
            {
                CreatorFeatureDB.CloudVariableRecord record =
                    CreatorFeatureDB.SetCloudVariable(
                        resolvedRoomId,
                        scopeAccountId,
                        key,
                        valueJson,
                        accountId.Value);

                Console.WriteLine(
                    $"[CLOUD VARIABLE SET] room={resolvedRoomId} key={record.Key} " +
                    $"scope={(isShared ? "room" : "player")} by={accountId.Value} " +
                    $"version={record.Version}");

                return Ok(new
                {
                    Success = true,
                    Value = CreatorFeatureDB.ToClientCloudVariable(record),
                    Variable = CreatorFeatureDB.ToClientCloudVariable(record)
                });
            }
            catch (ArgumentException exception)
            {
                return BadRequest(new { Success = false, Error = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return StatusCode(
                    StatusCodes.Status409Conflict,
                    new { Success = false, Error = exception.Message });
            }
        }

        [HttpPost("/api/cloudvariables/v1/batch")]
        [HttpPut("/api/cloudvariables/v1/batch")]
        [HttpPost("/api/cloudvariables/v1/room/{roomId:long}/batch")]
        [HttpPut("/api/cloudvariables/v1/room/{roomId:long}/batch")]
        [HttpPost("/api/roomvariables/v1/batch")]
        [HttpPut("/api/roomvariables/v1/batch")]
        [HttpPost("/roomserver/rooms/{roomId:long}/cloudvariables/batch")]
        [HttpPut("/roomserver/rooms/{roomId:long}/cloudvariables/batch")]
        [RequestSizeLimit(MaxSmallPayloadBytes)]
        public async Task<IActionResult> SetCloudVariablesBatch(long? roomId = null)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            JsonElement root;
            try
            {
                using JsonDocument document = await JsonDocument.ParseAsync(
                    Request.Body,
                    cancellationToken: HttpContext.RequestAborted);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return BadRequest(new { Success = false, Error = "invalid_cloud_variable_payload" });
            }

            long bodyRoomId = root.ValueKind == JsonValueKind.Object
                ? ReadJsonLong(root, "roomId", "contextRoomId") ?? 0
                : 0;

            if (root.ValueKind == JsonValueKind.Object &&
                (TryGetJsonProperty(root, "variables", out JsonElement nested) ||
                 TryGetJsonProperty(root, "values", out nested) ||
                 TryGetJsonProperty(root, "items", out nested)))
            {
                root = nested;
            }

            if (root.ValueKind != JsonValueKind.Array)
                return BadRequest(new { Success = false, Error = "cloud_variable_array_required" });

            long resolvedRoomId = ResolveRoomId(roomId);
            if (resolvedRoomId <= 0 && bodyRoomId > 0)
                resolvedRoomId = bodyRoomId;
            RoomDBClasses.Room? room = RoomDB.GetRoom(resolvedRoomId);
            if (room == null)
                return NotFound(new { Success = false, Error = "room_not_found" });

            var saved = new List<object>();
            foreach (JsonElement item in root.EnumerateArray().Take(100))
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                string? key = ReadJsonString(item, "key", "name", "variable");
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                bool shared = ReadJsonNullableBool(item,
                    "shared", "isShared", "global", "roomScoped")
                    ?? IsRoomVariableRoute();
                shared = shared ||
                    string.Equals(
                        ReadJsonString(item, "scope"),
                        "room",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        ReadJsonString(item, "scope"),
                        "global",
                        StringComparison.OrdinalIgnoreCase);
                if (shared && !CanWriteSharedCloudVariable(room, accountId.Value))
                {
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            Success = false,
                            Error = "shared_cloud_variable_requires_room_access",
                            Key = key
                        });
                }

                string valueJson = "null";
                if (TryGetJsonProperty(item, "valueJson", out JsonElement valueElement) ||
                    TryGetJsonProperty(item, "value", out valueElement) ||
                    TryGetJsonProperty(item, "data", out valueElement))
                {
                    valueJson = valueElement.ValueKind == JsonValueKind.String
                        ? JsonSerializer.Serialize(valueElement.GetString())
                        : valueElement.GetRawText();
                }

                try
                {
                    CreatorFeatureDB.CloudVariableRecord record =
                        CreatorFeatureDB.SetCloudVariable(
                            resolvedRoomId,
                            shared ? 0 : accountId.Value,
                            key,
                            valueJson,
                            accountId.Value);
                    saved.Add(CreatorFeatureDB.ToClientCloudVariable(record));
                }
                catch (ArgumentException exception)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Error = exception.Message,
                        Key = key
                    });
                }
                catch (InvalidOperationException exception)
                {
                    return StatusCode(
                        StatusCodes.Status409Conflict,
                        new { Success = false, Error = exception.Message, Key = key });
                }
            }

            return Ok(new
            {
                Success = true,
                Results = saved,
                Variables = saved,
                Count = saved.Count,
                RoomId = resolvedRoomId
            });
        }

        [HttpDelete("/api/cloudvariables/v1/{key}")]
        [HttpDelete("/api/cloudvariables/v1/room/{roomId:long}/{key}")]
        [HttpDelete("/api/cloudvariables/v1/{roomId:long}/{key}")]
        [HttpDelete("/api/roomvariables/v1/{key}")]
        [HttpDelete("/api/roomvariables/v1/room/{roomId:long}/{key}")]
        [HttpDelete("/api/roomvariables/v1/{roomId:long}/{key}")]
        [HttpDelete("/roomserver/rooms/{roomId:long}/cloudvariables/{key}")]
        public IActionResult DeleteCloudVariable(string key, long? roomId = null)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            long resolvedRoomId = ResolveRoomId(roomId);
            RoomDBClasses.Room? room = RoomDB.GetRoom(resolvedRoomId);
            if (room == null)
                return NotFound(new { Success = false, Error = "room_not_found" });

            bool shared = ReadBoolFromQuery(
                IsRoomVariableRoute(),
                "shared", "Shared", "global", "Global");
            if (shared && !CanEditRoom(room, accountId.Value))
                return StatusCode(StatusCodes.Status403Forbidden);

            bool removed = CreatorFeatureDB.DeleteCloudVariable(
                resolvedRoomId,
                shared ? 0 : accountId.Value,
                key);
            return removed
                ? Ok(new { Success = true })
                : NotFound(new { Success = false, Error = "cloud_variable_not_found" });
        }

        private static bool TryGetJsonProperty(
            JsonElement element,
            string name,
            out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        private static string? ReadJsonString(
            JsonElement element,
            params string[] names)
        {
            foreach (string name in names)
            {
                if (!TryGetJsonProperty(element, name, out JsonElement value))
                    continue;
                return value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : value.ToString();
            }
            return null;
        }

        private static long? ReadJsonLong(
            JsonElement element,
            params string[] names)
        {
            foreach (string name in names)
            {
                if (!TryGetJsonProperty(element, name, out JsonElement value))
                    continue;
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
                    return number;
                if (long.TryParse(value.ToString().Trim('"'), out number))
                    return number;
            }
            return null;
        }

        private static bool? ReadJsonNullableBool(
            JsonElement element,
            params string[] names)
        {
            foreach (string name in names)
            {
                if (!TryGetJsonProperty(element, name, out JsonElement value))
                    continue;
                if (value.ValueKind == JsonValueKind.True)
                    return true;
                if (value.ValueKind == JsonValueKind.False)
                    return false;
                if (bool.TryParse(value.ToString(), out bool boolean))
                    return boolean;
                if (int.TryParse(value.ToString(), out int number))
                    return number != 0;
            }
            return null;
        }

        private async Task<Dictionary<string, string>> ReadValuesAsync()
        {
            var values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> item
                     in Request.Query)
            {
                if (item.Value.Count > 0)
                    values[item.Key] = item.Value[item.Value.Count - 1] ?? string.Empty;
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
                foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> item
                         in form)
                {
                    if (item.Value.Count > 0)
                        values[item.Key] = item.Value[item.Value.Count - 1] ?? string.Empty;
                }
                return values;
            }

            if ((Request.ContentLength ?? 0) <= 0 &&
                Request.Headers.TransferEncoding.Count == 0)
                return values;

            using var reader = new StreamReader(Request.Body);
            string rawBody = await reader.ReadToEndAsync(HttpContext.RequestAborted);
            if (string.IsNullOrWhiteSpace(rawBody))
                return values;

            values["__raw"] = rawBody;
            try
            {
                using JsonDocument document = JsonDocument.Parse(rawBody);
                JsonElement root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in root.EnumerateObject())
                    {
                        values[property.Name] = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString() ?? string.Empty
                            : property.Value.GetRawText();
                    }
                }
                else if (root.ValueKind == JsonValueKind.String)
                {
                    values["value"] = root.GetString() ?? string.Empty;
                    values["code"] = root.GetString() ?? string.Empty;
                }
                else
                {
                    values["value"] = root.GetRawText();
                }
            }
            catch (JsonException)
            {
                values["value"] = rawBody.Trim();
                values["code"] = rawBody.Trim().Trim('"');
            }

            return values;
        }

        private long ResolveRoomId(
            long? routeRoomId,
            IReadOnlyDictionary<string, string>? values = null)
        {
            if (routeRoomId.GetValueOrDefault() > 0)
                return routeRoomId!.Value;

            foreach (string key in new[] { "roomId", "RoomId", "contextRoomId" })
            {
                string? value = values != null && values.TryGetValue(key, out string? bodyValue)
                    ? bodyValue
                    : Request.Query[key].FirstOrDefault();
                if (long.TryParse(value?.Trim('"'), out long parsed) && parsed > 0)
                    return parsed;
            }

            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId.HasValue)
                return PlayerDB.GetPlayerHeartbeat(accountId.Value).roomInstance?.roomId ?? 0;
            return 0;
        }

        private bool IsRoomVariableRoute()
        {
            string path = Request.Path.Value ?? string.Empty;
            return path.Contains("roomvariables", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/roomserver/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanWriteSharedCloudVariable(
            RoomDBClasses.Room room,
            long accountId)
        {
            if (CanEditRoom(room, accountId))
                return true;

            PlayerDBClasses.Heartbeat heartbeat =
                PlayerDB.GetPlayerHeartbeat(accountId);
            return heartbeat.isOnline &&
                   heartbeat.roomInstance?.roomId == room.RoomId;
        }

        private static bool CanEditRoom(RoomDBClasses.Room room, long accountId) =>
            room.CreatorAccountId == accountId ||
            room.Roles?.Any(value =>
                value.AccountId == accountId &&
                value.Role is RoomDBClasses.Role.Creator or
                    RoomDBClasses.Role.CoOwner or
                    RoomDBClasses.Role.TemporaryCoOwner) == true;

        private static string ReadValueJson(
            IReadOnlyDictionary<string, string> values)
        {
            if (values.TryGetValue("__raw", out string? requestRaw) &&
                !string.IsNullOrWhiteSpace(requestRaw))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(requestRaw);
                    JsonElement root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        foreach (string propertyName in new[]
                                 {
                                     "valueJson", "ValueJson", "value",
                                     "Value", "data", "Data"
                                 })
                        {
                            if (TryGetJsonProperty(
                                    root,
                                    propertyName,
                                    out JsonElement propertyValue))
                            {
                                return propertyValue.GetRawText();
                            }
                        }
                    }
                    else
                    {
                        return root.GetRawText();
                    }
                }
                catch (JsonException)
                {
                }
            }

            string? raw = ReadString(values,
                "valueJson", "ValueJson", "value", "Value", "data", "Data");
            if (raw == null && values.TryGetValue("__raw", out requestRaw))
                raw = requestRaw;
            if (string.IsNullOrWhiteSpace(raw))
                return "null";

            try
            {
                using JsonDocument document = JsonDocument.Parse(raw);
                return document.RootElement.GetRawText();
            }
            catch (JsonException)
            {
                return JsonSerializer.Serialize(raw);
            }
        }

        private static string? ReadString(
            IReadOnlyDictionary<string, string> values,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                if (!values.TryGetValue(key, out string? value))
                    continue;
                string cleaned = value.Trim();
                if (cleaned.Length >= 2 && cleaned[0] == '"' && cleaned[^1] == '"')
                {
                    try
                    {
                        return JsonSerializer.Deserialize<string>(cleaned);
                    }
                    catch (JsonException)
                    {
                    }
                }
                return cleaned;
            }
            return null;
        }

        private static int ReadInt(
            IReadOnlyDictionary<string, string> values,
            int fallback,
            params string[] keys) =>
            int.TryParse(ReadString(values, keys)?.Trim('"'), out int result)
                ? result
                : fallback;

        private static bool ReadBool(
            IReadOnlyDictionary<string, string> values,
            bool fallback,
            params string[] keys)
        {
            string? raw = ReadString(values, keys)?.Trim('"');
            if (bool.TryParse(raw, out bool result))
                return result;
            return int.TryParse(raw, out int number) ? number != 0 : fallback;
        }

        private bool ReadBoolFromQuery(bool fallback, params string[] keys)
        {
            foreach (string key in keys)
            {
                string? raw = Request.Query[key].FirstOrDefault();
                if (bool.TryParse(raw, out bool result))
                    return result;
                if (int.TryParse(raw, out int number))
                    return number != 0;
            }
            return fallback;
        }
    }
}
