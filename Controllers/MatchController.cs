using Mocha2023.Auth;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs;
using Mocha2023.Classes.DBs.DBClasses;
using static Mocha2023.Classes.DBs.DBClasses.PlayerDBClasses;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mocha2023.Controllers
{
    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    [Route("/match")]
    public class MatchController : ControllerBase
    {
        [HttpGet("player")]
        public IActionResult GetPlayerHeartbeatBulk()
        {
            var playerId = AuthStuff.GetPlayerId(Request);
            if (playerId == null)
                return Unauthorized();

            var ids = new List<long>();
            foreach (string? rawValue in Request.Query["id"])
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                    continue;

                foreach (string part in rawValue.Split(
                             ',',
                             StringSplitOptions.RemoveEmptyEntries |
                             StringSplitOptions.TrimEntries))
                {
                    if (long.TryParse(part, out long id) && id > 0)
                        ids.Add(id);
                }
            }

            PlayerDB.TouchPlayerHeartbeat((long)playerId);
            return Ok(PlayerDB.GetPlayerHeartbeatsBulk(
                ids.Distinct().ToList(),
                viewingPlayerId: playerId.Value));
        }

        [HttpPost("player/login")]
        public async Task<IActionResult> PlayerLogin()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            Heartbeat before = PlayerDB.GetPlayerHeartbeat(
                id.Value,
                viewingPlayerId: id.Value);
            Heartbeat? heartbeat = PlayerDB.ResumePlayerHeartbeat(id.Value);

            if (heartbeat == null)
                return Unauthorized();

            if (!before.isOnline && heartbeat.roomInstance == null)
                Sessions.MarkPlayerLeft(id.Value);

            bool presenceChanged =
                before.isOnline != heartbeat.isOnline ||
                before.errorCode != heartbeat.errorCode ||
                before.roomInstance?.roomInstanceId !=
                    heartbeat.roomInstance?.roomInstanceId;

            if (presenceChanged)
                await NotiController.NotifyPlayerPresenceUpdatedAsync(id.Value);

            Console.WriteLine(
                $"[PLAYER LOGIN] player={id.Value} " +
                $"preservedInstance={heartbeat.roomInstance?.roomInstanceId.ToString() ?? "none"} " +
                $"reconnectSafe=true presenceChanged={presenceChanged.ToString().ToLowerInvariant()}");

            return Ok();
        }

        [HttpPost("player/exclusivelogin")]
        public IActionResult PlayerExclusiveLogin()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            Console.WriteLine(
                $"[PLAYER EXCLUSIVE LOGIN] player={id.Value} mutation=none");
            return Ok(new { errorCode = (int)MatchmakingErrorCode.Success });
        }

        [HttpPost("player/logout")]
        public async Task<IActionResult> PlayerLogout()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id != null)
            {
                Sessions.MarkPlayerLeft((long)id);
                PlayerDB.SetPlayerOffline((long)id);
                await NotiController.NotifyPlayerPresenceUpdatedAsync(id.Value);
            }

            return Ok();
        }

        public sealed class NotifyDisconnectRequest
        {
            public int type { get; set; }
        }

        [HttpPost("player/notifydisconnect")]
        public async Task<IActionResult> NotifyDisconnect()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            int? disconnectType = null;
            string rawBody = string.Empty;

            try
            {
                if (Request.HasFormContentType)
                {
                    var form = await Request.ReadFormAsync();

                    if (int.TryParse(form["type"], out int formType))
                        disconnectType = formType;
                }
                else
                {
                    using var reader = new StreamReader(Request.Body);
                    rawBody = await reader.ReadToEndAsync();

                    if (!string.IsNullOrWhiteSpace(rawBody))
                    {
                        try
                        {
                            using var json = JsonDocument.Parse(rawBody);

                            if (json.RootElement.TryGetProperty("type", out var typeElement) &&
                                typeElement.TryGetInt32(out int jsonType))
                            {
                                disconnectType = jsonType;
                            }
                        }
                        catch (JsonException)
                        {

                            if (int.TryParse(rawBody.Trim().Trim('"'), out int rawType))
                                disconnectType = rawType;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NOTIFY DISCONNECT] Body parse failed: {ex.Message}");
            }

            Console.WriteLine(
                $"[NOTIFY DISCONNECT] player={id.Value} " +
                $"type={disconnectType?.ToString() ?? "unknown"} " +
                $"contentType={Request.ContentType ?? "null"} " +
                $"rawBody={rawBody}");

            var heartbeat = PlayerDB.GetPlayerHeartbeat(id.Value);
            PlayerDB.TouchPlayerHeartbeat(id.Value);

            Console.WriteLine(
                $"[NOTIFY DISCONNECT] player={id.Value} isolated=true " +
                $"instance={heartbeat.roomInstance?.roomInstanceId.ToString() ?? "none"} " +
                "mutation=none transient-safe=true");

            return Ok(heartbeat);
        }

        [HttpPost("player/heartbeat")]
        public IActionResult GetPlayerHeartbeat()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(PlayerDB.TouchPlayerHeartbeat((long)id));
        }

        [HttpPost("/data/heartbeat")]
        public IActionResult DataHeartbeat()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(PlayerDB.TouchPlayerHeartbeat((long)id));
        }

        [HttpPost("invite")]
        [HttpPut("invite")]
        [HttpPost("player/invite")]
        [HttpPut("player/invite")]
        [HttpPost("room/invite")]
        [HttpPut("room/invite")]
        [HttpPost("invite/{targetPlayerId:long}")]
        [HttpPut("invite/{targetPlayerId:long}")]
        [HttpPost("player/invite/{targetPlayerId:long}")]
        [HttpPut("player/invite/{targetPlayerId:long}")]
        [HttpPost("/api/match/invite")]
        [HttpPut("/api/match/invite")]
        [HttpPost("/api/match/invite/{targetPlayerId:long}")]
        [HttpPut("/api/match/invite/{targetPlayerId:long}")]
        [HttpPost("roominstance/{roomInstanceId:long}/invite")]
        [HttpPut("roominstance/{roomInstanceId:long}/invite")]
        [HttpPost("roominstance/{roomInstanceId:long}/invite/{targetPlayerId:long}")]
        [HttpPut("roominstance/{roomInstanceId:long}/invite/{targetPlayerId:long}")]
        [HttpPost("matchmake/instance/{roomInstanceId:long}/invite")]
        [HttpPut("matchmake/instance/{roomInstanceId:long}/invite")]
        [HttpPost("matchmake/instance/{roomInstanceId:long}/invite/{targetPlayerId:long}")]
        [HttpPut("matchmake/instance/{roomInstanceId:long}/invite/{targetPlayerId:long}")]
        [HttpPost("/api/match/roominstance/{roomInstanceId:long}/invite")]
        [HttpPut("/api/match/roominstance/{roomInstanceId:long}/invite")]
        [HttpPost("/api/match/roominstance/{roomInstanceId:long}/invite/{targetPlayerId:long}")]
        [HttpPut("/api/match/roominstance/{roomInstanceId:long}/invite/{targetPlayerId:long}")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> InvitePlayer(
            long? targetPlayerId = null,
            long? roomInstanceId = null)
        {
            long? inviterId = AuthStuff.GetPlayerId(Request);
            if (inviterId == null)
            {
                return Unauthorized(new
                {
                    success = false,
                    error = "Authentication is required."
                });
            }

            var inviterPlayer = PlayerDB.Players.FindById(inviterId.Value);
            if (inviterPlayer?.Player == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = "Inviting player was not found."
                });
            }

            var inviterHeartbeat = PlayerDB.GetPlayerHeartbeat(inviterId.Value);
            var inviterInstance = inviterHeartbeat?.roomInstance;

            if (inviterInstance == null)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "You must be inside a room before inviting someone."
                });
            }

            if (!Sessions.IsConfirmedParticipant(
                    inviterId.Value,
                    inviterInstance.roomInstanceId))
            {
                Console.WriteLine(
                    $"[MATCH INVITE DENIED] inviter={inviterId.Value} " +
                    $"instance={inviterInstance.roomInstanceId} " +
                    "reason=membership-not-confirmed");
                return StatusCode(409, new
                {
                    success = false,
                    error = "room_instance_membership_not_confirmed",
                    errorMessage =
                        "Finish joining the room before inviting another player."
                });
            }

            var targetIds = new HashSet<long>();
            var claimedRoomInstanceIds = new HashSet<long>();
            string rawBody = string.Empty;

            if (targetPlayerId.HasValue && targetPlayerId.Value > 0)
                targetIds.Add(targetPlayerId.Value);
            if (roomInstanceId is > 0)
                claimedRoomInstanceIds.Add(roomInstanceId.Value);

            foreach (string key in InviteAccountIdKeys)
            {
                foreach (string value in Request.Query[key])
                    AddInviteIds(value, targetIds);
            }
            foreach (string key in RoomInstanceIdKeys)
            {
                foreach (string value in Request.Query[key])
                    AddInviteIds(value, claimedRoomInstanceIds);
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(
                    HttpContext.RequestAborted);

                rawBody = string.Join(
                    "&",
                    form.SelectMany(entry =>
                        entry.Value.Select(value =>
                            $"{entry.Key}={value}")));

                foreach (string key in InviteAccountIdKeys)
                {
                    foreach (string value in form[key])
                        AddInviteIds(value, targetIds);
                }
                foreach (string key in RoomInstanceIdKeys)
                {
                    foreach (string value in form[key])
                        AddInviteIds(value, claimedRoomInstanceIds);
                }

                if (targetIds.Count == 0)
                {
                    foreach (var entry in form)
                    {
                        if (entry.Key.Contains(
                                "id",
                                StringComparison.OrdinalIgnoreCase) &&
                            !RoomInstanceIdKeys.Any(key =>
                                entry.Key.Equals(
                                    key,
                                    StringComparison.OrdinalIgnoreCase)))
                        {
                            foreach (string value in entry.Value)
                                AddInviteIds(value, targetIds);
                        }
                    }
                }
            }
            else
            {
                using var reader = new StreamReader(Request.Body);
                rawBody = await reader.ReadToEndAsync();

                if (!string.IsNullOrWhiteSpace(rawBody))
                {
                    string cleanedBody = rawBody.Trim().Trim('"');

                    AddInviteIds(cleanedBody, targetIds);

                    try
                    {
                        using var document = JsonDocument.Parse(rawBody);

                        FindInviteIds(
                            document.RootElement,
                            targetIds);
                        FindRoomInstanceIds(
                            document.RootElement,
                            claimedRoomInstanceIds);
                    }
                    catch (JsonException)
                    {

                    }
                }
            }

            if (claimedRoomInstanceIds.Any(value =>
                    value != inviterInstance.roomInstanceId))
            {
                Console.WriteLine(
                    $"[MATCH INVITE DENIED] inviter={inviterId.Value} " +
                    $"currentInstance={inviterInstance.roomInstanceId} " +
                    $"claimedInstances={string.Join(',', claimedRoomInstanceIds)} " +
                    "reason=instance-mismatch");
                return StatusCode(409, new
                {
                    success = false,
                    error = "room_invite_source_mismatch",
                    currentRoomInstanceId = inviterInstance.roomInstanceId
                });
            }

            targetIds.Remove(inviterId.Value);
            targetIds.ExceptWith(claimedRoomInstanceIds);

            Console.WriteLine(
                $"[MATCH INVITE] inviter={inviterId.Value} " +
                $"targets={string.Join(",", targetIds)} " +
                $"room={inviterInstance.roomId} " +
                $"instance={inviterInstance.roomInstanceId} " +
                $"contentType={Request.ContentType ?? "null"} " +
                $"rawBody={rawBody}");

            if (targetIds.Count == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "No valid target player ID was provided.",
                    contentType = Request.ContentType,
                    receivedBody = rawBody
                });
            }

            var joinedPlayers = new List<long>();
            var missingPlayers = new List<long>();
            var failedPlayers = new List<long>();

            foreach (long targetId in targetIds.Take(100))
            {
                var targetPlayer = PlayerDB.Players.FindById(targetId);

                if (targetPlayer?.Player == null)
                {
                    missingPlayers.Add(targetId);
                    continue;
                }

                var relationship = RelationshipDB.GetClientRelationship(
                    targetId,
                    inviterId.Value);
                if (relationship?.Ignored is 1 or 3)
                {
                    failedPlayers.Add(targetId);
                    Console.WriteLine(
                        $"[MATCH INVITE DENIED] inviter={inviterId.Value} " +
                        $"target={targetId} reason=blocked-by-recipient");
                    continue;
                }

                joinedPlayers.Add(targetId);

                Console.WriteLine(
                    $"[MATCH INVITE] Queued invite for player {targetId}; " +
                    $"presence unchanged. room={inviterInstance.roomId} " +
                    $"instance={inviterInstance.roomInstanceId} " +
                    $"photonRoom={inviterInstance.photonRoomId}");

                DiscordLogger.Log(
                    $"📨 **Room Invite** — " +
                    $"`{inviterPlayer.Player.Username ?? "unknown"}` " +
                    $"(ID: `{inviterId.Value}`) invited player `{targetId}` " +
                    $"to room `{inviterInstance.roomId}` " +
                    $"instance `{inviterInstance.roomInstanceId}`");

                await NotiController.NotifyRoomInviteAsync(
                    inviterId.Value,
                    targetId,
                    inviterInstance.roomId,
                    inviterInstance.roomInstanceId,
                    inviterInstance.photonRoomId);
            }

            return Ok(new
            {
                success = joinedPlayers.Count > 0,

                invitedPlayerIds = joinedPlayers,

                joinedPlayerIds = Array.Empty<long>(),
                pendingInviteIds = joinedPlayers,

                missingPlayerIds = missingPlayers,
                failedPlayerIds = failedPlayers,

                roomId = inviterInstance.roomId,
                subRoomId = inviterInstance.subRoomId,
                roomInstanceId = inviterInstance.roomInstanceId,
                photonRoomId = inviterInstance.photonRoomId
            });
        }

        private static PlayerDBClasses.RoomInstance CloneRoomInstance(
            PlayerDBClasses.RoomInstance source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return new PlayerDBClasses.RoomInstance
            {
                encryptVoiceChat = source.encryptVoiceChat,
                clubId = source.clubId,
                dataBlob = source.dataBlob,
                eventId = source.eventId,
                isFull = source.isFull,
                isInProgress = source.isInProgress,
                isPrivate = source.isPrivate,
                location = source.location,
                maxCapacity = source.maxCapacity,
                Name = source.Name,
                photonRegion = source.photonRegion,
                photonRegionId = source.photonRegionId,
                photonRoomId = source.photonRoomId,
                roomCode = source.roomCode,
                roomId = source.roomId,
                roomInstanceId = source.roomInstanceId,
                roomInstanceType = source.roomInstanceType,
                subRoomId = source.subRoomId,
                subRoomDataSaveId = source.subRoomDataSaveId,
                createdAt = source.createdAt
            };
        }

        [HttpGet("room/{roomId}/instances")]
        public IActionResult GetRoomInstances(long roomId)
        {
            var accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized();

            var requestedRoom = RoomDB.GetRoom(roomId);
            if (requestedRoom == null ||
                ((requestedRoom.IsDorm ||
                  requestedRoom.Accessibility ==
                      RoomDBClasses.RoomAccessibility.Private) &&
                 !RoomDB.CanPlayerAccessRoom(
                     requestedRoom,
                     accountId.Value)))
            {
                return NotFound();
            }

            const long activeWindowSeconds = 120;
            long activeAfter = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                - activeWindowSeconds;

            var instances = PlayerDB.Players.FindAll()
                .Select(player => player.Player?.PlayerExtra?.Heartbeat)
                .Where(heartbeat =>
                    heartbeat != null &&
                    heartbeat.isOnline &&
                    heartbeat.lastHeartbeatUnixTime >= activeAfter &&
                    heartbeat.roomInstance != null &&
                    heartbeat.roomInstance.roomId == roomId &&

                    !Sessions.IsRestrictedInstance(heartbeat.roomInstance) &&
                    heartbeat.roomInstance.roomInstanceType ==
                        PlayerDBClasses.RoomInstanceType.Public)
                .GroupBy(heartbeat => heartbeat!.roomInstance!.roomInstanceId)
                .Select(group =>
                {
                    var instance = group.First()!.roomInstance!;
                    var playerIds = group
                        .Select(heartbeat => heartbeat!.playerId)
                        .Where(id => id > 0 && id <= int.MaxValue)
                        .Distinct()
                        .OrderBy(id => id)
                        .Select(id => (int)id)
                        .ToList();
                    int capacity = Math.Max(1, instance.maxCapacity);
                    DateTime createdAt = instance.createdAt == default
                        ? DateTimeOffset.FromUnixTimeSeconds(
                            group.Min(heartbeat => heartbeat!.lastHeartbeatUnixTime))
                            .UtcDateTime
                        : instance.createdAt.ToUniversalTime();

                    return new
                    {
                        roomInstanceId = instance.roomInstanceId,
                        roomId = instance.roomId,
                        subRoomId = instance.subRoomId,
                        isFull = instance.isFull || playerIds.Count >= capacity,
                        createdAt,
                        playerIds
                    };
                })
                .OrderByDescending(instance => instance.playerIds.Count)
                .ThenBy(instance => instance.roomInstanceId)
                .ToList();

            return Ok(instances);
        }

        private static readonly string[] InviteAccountIdKeys =
        {
    "id",
    "Id",

    "ids",
    "Ids",

    "accountId",
    "AccountId",

    "accountIds",
    "AccountIds",

    "playerId",
    "PlayerId",

    "playerIds",
    "PlayerIds",

    "targetId",
    "TargetId",

    "targetPlayerId",
    "TargetPlayerId",

    "targetAccountId",
    "TargetAccountId",

    "inviteeId",
    "InviteeId",

    "inviteeAccountId",
    "InviteeAccountId",

    "invitedPlayerId",
    "InvitedPlayerId",

    "invitedPlayerIds",
    "InvitedPlayerIds"
};

        private static readonly string[] RoomInstanceIdKeys =
        {
            "roomInstanceId",
            "RoomInstanceId",
            "requestedRoomInstanceId",
            "RequestedRoomInstanceId",
            "targetRoomInstanceId",
            "TargetRoomInstanceId"
        };

        private static void AddInviteIds(
            string? value,
            HashSet<long> output)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            string cleaned = Uri.UnescapeDataString(value)
                .Trim()
                .Trim('[', ']', '"', '\'');

            string[] parts = cleaned.Split(
                new[]
                {
            ',',
            ';',
            ' ',
            '|'
                },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            foreach (string part in parts)
            {
                string idText = part
                    .Trim()
                    .Trim('[', ']', '"', '\'');

                if (long.TryParse(idText, out long id) && id > 0)
                    output.Add(id);
            }
        }

        private static void FindInviteIds(
            JsonElement element,
            HashSet<long> output)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    {
                        if (element.TryGetInt64(out long numberId) &&
                            numberId > 0)
                        {
                            output.Add(numberId);
                        }

                        break;
                    }

                case JsonValueKind.String:
                    {
                        AddInviteIds(element.GetString(), output);
                        break;
                    }

                case JsonValueKind.Array:
                    {
                        foreach (JsonElement item in element.EnumerateArray())
                            FindInviteIds(item, output);

                        break;
                    }

                case JsonValueKind.Object:
                    {
                        foreach (JsonProperty property in element.EnumerateObject())
                        {
                            bool isIdProperty = InviteAccountIdKeys.Any(key =>
                                property.Name.Equals(
                                    key,
                                    StringComparison.OrdinalIgnoreCase));

                            if (isIdProperty)
                            {
                                FindInviteIds(
                                    property.Value,
                                    output);
                            }
                            else if (property.Value.ValueKind is
                                     JsonValueKind.Object or
                                     JsonValueKind.Array)
                            {
                                FindInviteIds(
                                    property.Value,
                                    output);
                            }
                        }

                        break;
                    }
            }
        }

        private static void FindRoomInstanceIds(
            JsonElement element,
            HashSet<long> output)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (RoomInstanceIdKeys.Any(key =>
                            property.Name.Equals(
                                key,
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        if (property.Value.ValueKind == JsonValueKind.Number &&
                            property.Value.TryGetInt64(out long numericValue) &&
                            numericValue > 0)
                        {
                            output.Add(numericValue);
                        }
                        else if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            AddInviteIds(property.Value.GetString(), output);
                        }
                    }
                    else if (property.Value.ValueKind is
                             JsonValueKind.Object or JsonValueKind.Array)
                    {
                        FindRoomInstanceIds(property.Value, output);
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                    FindRoomInstanceIds(item, output);
            }
        }

        [HttpPost("matchmake/none")]
        public async Task<IActionResult> MatchmakeNone()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            Sessions.MarkPlayerLeft(id.Value);
            Heartbeat? heartbeat = PlayerDB.LeaveCurrentRoom(id.Value);
            await NotiController.NotifyPlayerPresenceUpdatedAsync(id.Value);
            return Ok(heartbeat);
        }

        [HttpGet("rooms/requiring/{role}")]
        public IActionResult GetRoomsRequiringRole(string role)
        {
            if (!Enum.TryParse<RoomDBClasses.Role>(role, true, out var parsedRole))
                return Ok(Array.Empty<object>());

            var ids = RoomDB.Rooms.FindAll()
                .Where(r => r.Roles != null && r.Roles.Any(x => x.Role == parsedRole))
                .Select(r => r.RoomId)
                .ToList();

            return Ok(ids);
        }

        [HttpPut("/match/player/statusvisibility")]
        public IActionResult SetStatusVisibility([FromForm] int statusVisibility)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return NoContent();
        }

        [HttpPut("/match/player/photonregionpings")]
        public IActionResult SetPhotonRegionPings()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return NoContent();
        }

        [HttpPut("/match/player/gameserverregionpings")]
        public IActionResult SetGameServerRegionPings()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return NoContent();
        }

        [HttpPut("/match/player/vrmovementmode")]
        public IActionResult SetVrMovementMode()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return NoContent();
        }

        [HttpGet("/match/player/avoidjuniors")]
        [HttpPut("/match/player/avoidjuniors")]
        public IActionResult AvoidJuniors()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            return Ok(false);
        }

        [HttpGet("/match/player/connection-info")]
        public IActionResult GetPlayerConnectionInfo()
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            var heartbeat = PlayerDB.GetPlayerHeartbeat((long)id);
            var roomInstance = heartbeat?.roomInstance;
            var playerRecord = PlayerDB.Players.FindById((long)id);

            string photonTicket = PhotonTicketService.Issue(
                playerId: (long)id,
                roomInstanceId: roomInstance?.roomInstanceId,
                roomId: roomInstance?.roomId,
                displayName: playerRecord?.Player?.DisplayName
                    ?? playerRecord?.Player?.Username);

            Response.Headers.CacheControl = "no-store";

            return Ok(new
            {
                Enabled = ServerConfig.PhotonEnabled,
                AppId = ServerConfig.PhotonRealtimeAppId,
                AppVersion = ServerConfig.PhotonAppVersion,
                Region = string.IsNullOrWhiteSpace(roomInstance?.photonRegionId)
                    ? ServerConfig.PhotonRegion
                    : roomInstance!.photonRegionId,
                PhotonRegion = string.IsNullOrWhiteSpace(roomInstance?.photonRegion)
                    ? ServerConfig.PhotonRegion
                    : roomInstance!.photonRegion,
                UseCustomAuthentication = true,
                AuthenticationParameter = "token",
                PhotonAccessToken = photonTicket,
                UserId = id.ToString(),
                RoomId = roomInstance?.roomId,
                RoomInstanceId = roomInstance?.roomInstanceId,
                PhotonRoomId = roomInstance?.photonRoomId ?? string.Empty,
                IsInRoom = roomInstance != null
            });
        }

        [HttpPost("/match/roominstance/{roomInstanceId}/reportjoinresult")]
        public async Task<IActionResult> ReportJoinResult(long roomInstanceId)
        {
            var id = AuthStuff.GetPlayerId(Request);
            if (id == null)
                return Unauthorized();

            string report = await ReadJoinResultReportAsync();
            bool succeeded = !LooksLikeFailedJoinResult(report);
            bool applied = Sessions.ReportJoinResult(
                id.Value,
                roomInstanceId,
                succeeded);

            int consumedInviteRows = applied && succeeded
                ? NotificationDB.ConsumeRoomInvite(
                    id.Value,
                    roomInstanceId)
                : 0;

            if (applied)
                await NotiController.NotifyPlayerPresenceUpdatedAsync(id.Value);

            Console.WriteLine(
                $"[JOIN RESULT HTTP] player={id.Value} " +
                $"instance={roomInstanceId} success={succeeded} " +
                $"applied={applied} consumedInvite={consumedInviteRows} " +
                $"report={report}");

            return Ok(new
            {
                success = succeeded,
                applied,
                consumedInviteRows
            });
        }

        [HttpPost("matchmake/dorm")]
        public async Task<IActionResult> MatchmakeDorm()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized();

            if (PlayerDB.NeedsOrientation(player))
            {
                const long orientationRoomId = 13;
                var orientation = Sessions.CreateRoom(
                    player.PlayerId,
                    orientationRoomId,
                    isPrivate: true);
                if (orientation != null)
                {
                    PlayerDB.RecordRoomVisit(player.PlayerId, orientationRoomId);
                    Console.WriteLine(
                        $"[ORIENTATION] player={player.PlayerId} routed to room={orientationRoomId}");
                    await NotiController.NotifyPlayerPresenceUpdatedAsync(player.PlayerId);
                    return Ok(orientation);
                }

                Console.WriteLine(
                    $"[ORIENTATION] room={orientationRoomId} unavailable; falling back to dorm");
            }

            Heartbeat currentHeartbeat = PlayerDB.GetPlayerHeartbeat(player.PlayerId);
            if (Sessions.ShouldHoldInGuestDorm(player.PlayerId, currentHeartbeat))
            {
                Console.WriteLine(
                    $"[DORM GUEST HOLD] player={player.PlayerId} " +
                    $"instance={currentHeartbeat.roomInstance?.roomInstanceId} " +
                    "reason=recent-guest-dorm-skip-auto-reload");
                return Ok(currentHeartbeat);
            }

            Heartbeat dormHeartbeat = Sessions.CreateDorm(
                player.PlayerId,
                player.Player.Username);
            await NotiController.NotifyPlayerPresenceUpdatedAsync(player.PlayerId);
            return Ok(dormHeartbeat);
        }

        [HttpPost("matchmake/room/{roomId}")]
        public async Task<IActionResult> MatchmakeRoomRoomId(long roomId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized();

            IActionResult? invitedResult = await TryJoinLatestInviteForRoomAsync(
                player.PlayerId,
                roomId);
            if (invitedResult != null)
                return invitedResult;

            bool isPrivate = await ReadPrivateMatchmakingFlagAsync();
            var heartbeat = Sessions.CreateRoom(
                player.PlayerId,
                roomId,
                isPrivate: isPrivate);
            if (heartbeat == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = "room_not_found_or_access_denied",
                    errorMessage =
                        "That room is unavailable or you do not have access.",
                    roomId
                });
            }

            if (heartbeat != null)
            {
                PlayerDB.RecordRoomVisit((long)player.PlayerId, roomId);
                var room = RoomDB.GetRoom(roomId);
                if (room != null &&
                    (room.Name.Contains("RecCenter", StringComparison.OrdinalIgnoreCase) ||
                     room.Name.Contains("Rec Center", StringComparison.OrdinalIgnoreCase)))
                {
                    var (challengeMapId, _, _) = RecNetDB.GetCurrentChallengeWindow();
                    RecNetDB.SetChallengeProgress(
                        player.PlayerId,
                        challengeMapId,
                        challengeId: 1,
                        progress: 1,
                        goal: 1);
                }
            }

            await NotiController.NotifyPlayerPresenceUpdatedAsync(player.PlayerId);
            return Ok(heartbeat);
        }

        [HttpPost("matchmake/private/room/{roomId:long}")]
        [HttpPost("matchmake/room/{roomId:long}/private")]
        public async Task<IActionResult> MatchmakePrivateRoom(long roomId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized();

            var heartbeat = Sessions.CreateRoom(
                player.PlayerId,
                roomId,
                isPrivate: true);
            if (heartbeat == null)
                return NotFound();

            PlayerDB.RecordRoomVisit(player.PlayerId, roomId);
            Console.WriteLine(
                $"[PRIVATE MATCHMAKE] player={player.PlayerId} " +
                $"room={roomId} instance={heartbeat.roomInstance?.roomInstanceId}");
            await NotiController.NotifyPlayerPresenceUpdatedAsync(player.PlayerId);
            return Ok(heartbeat);
        }

        private async Task<IActionResult?> TryJoinLatestInviteForRoomAsync(
            long playerId,
            long roomId)
        {
            if (!NotificationDB.TryGetLatestRoomInviteForRoom(
                    playerId,
                    roomId,
                    out long inviteId,
                    out long inviterPlayerId,
                    out long roomInstanceId))
            {
                return null;
            }

            Heartbeat? heartbeat = Sessions.JoinInvitedRoomInstance(
                playerId,
                roomInstanceId,
                inviterPlayerId,
                roomId);
            if (heartbeat?.roomInstance == null)
            {
                Console.WriteLine(
                    $"[PRIVATE INVITE FALLBACK MISS] player={playerId} " +
                    $"room={roomId} invite={inviteId} instance={roomInstanceId}");
                return NotFound(new
                {
                    success = false,
                    error = "invited_room_instance_not_found_or_full",
                    errorMessage =
                        "The invited private instance is no longer available or is full.",
                    roomId,
                    inviteId,
                    roomInstanceId
                });
            }

            PlayerDB.RecordRoomVisit(playerId, roomId);
            Console.WriteLine(
                $"[PRIVATE INVITE FALLBACK JOIN] player={playerId} " +
                $"inviter={inviterPlayerId} room={roomId} " +
                $"invite={inviteId} instance={roomInstanceId} " +
                "grantPendingJoinResult=true");
            await NotiController.NotifyPlayerPresenceUpdatedAsync(playerId);
            return Ok(heartbeat);
        }

        [HttpPost("matchmake/instance/{roomInstanceId:long}")]
        [HttpPut("matchmake/instance/{roomInstanceId:long}")]
        [HttpPost("matchmake/roominstance/{roomInstanceId:long}")]
        [HttpPut("matchmake/roominstance/{roomInstanceId:long}")]
        [HttpPost("roominstance/{roomInstanceId:long}/join")]
        [HttpPut("roominstance/{roomInstanceId:long}/join")]
        [HttpPost("/api/match/matchmake/instance/{roomInstanceId:long}")]
        [HttpPut("/api/match/matchmake/instance/{roomInstanceId:long}")]
        [HttpPost("/api/match/matchmake/roominstance/{roomInstanceId:long}")]
        [HttpPut("/api/match/matchmake/roominstance/{roomInstanceId:long}")]
        public Task<IActionResult> MatchmakeRoomInstance(long roomInstanceId) =>
            JoinRoomInstanceAsync(roomInstanceId);

        [HttpPost("matchmake/instance")]
        [HttpPut("matchmake/instance")]
        [HttpPost("/api/match/matchmake/instance")]
        [HttpPut("/api/match/matchmake/instance")]
        [RequestSizeLimit(32 * 1024)]
        public async Task<IActionResult> MatchmakeRoomInstanceFromBody()
        {
            long roomInstanceId = 0;
            foreach (string key in new[]
                     {
                         "roomInstanceId", "RoomInstanceId",
                         "instanceId", "InstanceId", "id", "Id",
                         "inviteId", "InviteId"
                     })
            {
                if (long.TryParse(
                        Request.Query[key].FirstOrDefault(),
                        out roomInstanceId) &&
                    roomInstanceId > 0)
                {
                    break;
                }
            }

            if (roomInstanceId <= 0 && Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(
                    HttpContext.RequestAborted);
                foreach (string key in new[]
                         {
                             "roomInstanceId", "RoomInstanceId",
                             "instanceId", "InstanceId", "id", "Id",
                             "inviteId", "InviteId"
                         })
                {
                    if (long.TryParse(
                            form[key].FirstOrDefault(),
                            out roomInstanceId) &&
                        roomInstanceId > 0)
                    {
                        break;
                    }
                }
            }
            else if (roomInstanceId <= 0 &&
                     ((Request.ContentLength ?? 0) > 0 ||
                      Request.Headers.TransferEncoding.Count > 0))
            {
                using var reader = new StreamReader(Request.Body);
                string body = await reader.ReadToEndAsync(
                    HttpContext.RequestAborted);
                if (!long.TryParse(body.Trim().Trim('"'), out roomInstanceId))
                {
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(body);
                        foreach (string key in new[]
                                 {
                                     "roomInstanceId", "RoomInstanceId",
                                     "instanceId", "InstanceId", "id", "Id",
                                     "inviteId", "InviteId"
                                 })
                        {
                            if (TryReadJsonLong(
                                    document.RootElement,
                                    key,
                                    out roomInstanceId) &&
                                roomInstanceId > 0)
                            {
                                break;
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        roomInstanceId = 0;
                    }
                }
            }

            if (roomInstanceId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "room_instance_id_required"
                });
            }

            return await JoinRoomInstanceAsync(roomInstanceId);
        }

        private async Task<IActionResult> JoinRoomInstanceAsync(
            long roomInstanceId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized();

            bool hasInvite = NotificationDB.TryGetRoomInvite(
                player.PlayerId,
                roomInstanceId,
                out long inviterPlayerId,
                out long invitedRoomId,
                out long grantedRoomInstanceId);
            long resolvedRoomInstanceId = hasInvite
                ? grantedRoomInstanceId
                : roomInstanceId;

            Heartbeat? heartbeat = hasInvite
                ? Sessions.JoinInvitedRoomInstance(
                    player.PlayerId,
                    resolvedRoomInstanceId,
                    inviterPlayerId,
                    invitedRoomId)
                : Sessions.JoinRoomInstance(
                    player.PlayerId,
                    resolvedRoomInstanceId);

            if (heartbeat?.roomInstance == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = hasInvite
                        ? "invited_room_instance_not_found_or_full"
                        : "room_instance_not_found_or_full",
                    errorMessage =
                        "That room instance is no longer available or is full.",
                    roomInstanceId = resolvedRoomInstanceId,
                    requestedId = roomInstanceId
                });
            }

            PlayerDB.RecordRoomVisit(
                player.PlayerId,
                heartbeat.roomInstance.roomId);

            Console.WriteLine(
                $"[GOTO] player={player.PlayerId} " +
                $"room={heartbeat.roomInstance.roomId} " +
                $"instance={heartbeat.roomInstance.roomInstanceId} " +
                $"viaInvite={hasInvite.ToString().ToLowerInvariant()} " +
                $"grantPendingJoinResult={hasInvite.ToString().ToLowerInvariant()}");
            await NotiController.NotifyPlayerPresenceUpdatedAsync(player.PlayerId);
            return Ok(heartbeat);
        }

        [HttpPost("matchmake/invite/{inviteId:long}")]
        [HttpPut("matchmake/invite/{inviteId:long}")]
        [HttpPost("/api/match/matchmake/invite/{inviteId:long}")]
        [HttpPut("/api/match/matchmake/invite/{inviteId:long}")]
        public Task<IActionResult> MatchmakeRoomInvite(long inviteId) =>
            JoinRoomInviteAsync(inviteId);

        [HttpPost("matchmake/invite")]
        [HttpPut("matchmake/invite")]
        [HttpPost("/api/match/matchmake/invite")]
        [HttpPut("/api/match/matchmake/invite")]
        [RequestSizeLimit(32 * 1024)]
        public async Task<IActionResult> MatchmakeRoomInviteFromBody()
        {
            long inviteId = 0;
            foreach (string key in new[]
                     {
                         "inviteId", "InviteId", "id", "Id",
                         "roomInstanceId", "RoomInstanceId"
                     })
            {
                if (long.TryParse(Request.Query[key].FirstOrDefault(), out inviteId) &&
                    inviteId > 0)
                    break;
            }

            if (inviteId <= 0 && Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
                foreach (string key in new[]
                         {
                             "inviteId", "InviteId", "id", "Id",
                             "roomInstanceId", "RoomInstanceId"
                         })
                {
                    if (long.TryParse(form[key].FirstOrDefault(), out inviteId) &&
                        inviteId > 0)
                        break;
                }
            }
            else if (inviteId <= 0 &&
                     ((Request.ContentLength ?? 0) > 0 ||
                      Request.Headers.TransferEncoding.Count > 0))
            {
                using var reader = new StreamReader(Request.Body);
                string body = await reader.ReadToEndAsync(HttpContext.RequestAborted);
                if (!long.TryParse(body.Trim().Trim('"'), out inviteId))
                {
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(body);
                        foreach (string key in new[]
                                 {
                                     "inviteId", "InviteId", "id", "Id",
                                     "roomInstanceId", "RoomInstanceId"
                                 })
                        {
                            if (TryReadJsonLong(document.RootElement, key, out inviteId) &&
                                inviteId > 0)
                                break;
                        }
                    }
                    catch (JsonException)
                    {
                        inviteId = 0;
                    }
                }
            }

            if (inviteId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "invite_id_required"
                });
            }

            return await JoinRoomInviteAsync(inviteId);
        }

        private async Task<IActionResult> JoinRoomInviteAsync(long inviteId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized();

            if (!NotificationDB.TryGetRoomInvite(
                    player.PlayerId,
                    inviteId,
                    out long inviterPlayerId,
                    out long invitedRoomId,
                    out long roomInstanceId))
            {
                Console.WriteLine(
                    $"[MATCH INVITE MISS] player={player.PlayerId} " +
                    $"postedId={inviteId} reason=no-message-or-grant");

                return NotFound(new
                {
                    success = false,
                    error = "game_invite_not_found",
                    errorMessage =
                        "That room invitation has expired or is no longer available.",
                    inviteId
                });
            }

            var heartbeat = Sessions.JoinInvitedRoomInstance(
                player.PlayerId,
                roomInstanceId,
                inviterPlayerId,
                invitedRoomId);
            if (heartbeat?.roomInstance == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = "invited_room_instance_not_found_or_full",
                    errorMessage =
                        "The invited room is no longer available or is full.",
                    inviteId,
                    roomInstanceId
                });
            }

            PlayerDB.RecordRoomVisit(player.PlayerId, invitedRoomId);

            Console.WriteLine(
                $"[MATCH INVITE ACCEPT] player={player.PlayerId} " +
                $"inviter={inviterPlayerId} postedId={inviteId} " +
                $"room={invitedRoomId} instance={roomInstanceId} " +
                "grantPendingJoinResult=true");
            await NotiController.NotifyPlayerPresenceUpdatedAsync(player.PlayerId);
            return Ok(heartbeat);
        }

        private static bool TryReadJsonLong(
            JsonElement element,
            string name,
            out long value)
        {
            value = 0;
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (property.Value.ValueKind == JsonValueKind.Number &&
                            property.Value.TryGetInt64(out value))
                            return value > 0;
                        if (long.TryParse(property.Value.ToString(), out value))
                            return value > 0;
                    }

                    if (TryReadJsonLong(property.Value, name, out value))
                        return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (TryReadJsonLong(item, name, out value))
                        return true;
                }
            }
            return false;
        }

        [HttpPost("matchmake/room/{roomId}/{subRoomId}")]
        public async Task<IActionResult> MatchmakeRoomSubRoomId(
            long roomId,
            long subRoomId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized();

            IActionResult? invitedResult = await TryJoinLatestInviteForRoomAsync(
                player.PlayerId,
                roomId);
            if (invitedResult != null)
                return invitedResult;

            var room = RoomDB.GetRoom(roomId);
            var subRoom = room?.SubRooms?.FirstOrDefault(value =>
                value.SubRoomId == subRoomId);
            if (room == null || subRoom == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = "Room or subroom was not found.",
                    roomId,
                    subRoomId
                });
            }

            bool isPrivate = await ReadPrivateMatchmakingFlagAsync();
            var heartbeat = Sessions.CreateRoom(
                player.PlayerId,
                roomId,
                subRoomId,
                isPrivate);
            if (heartbeat == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = "room_not_found_or_access_denied",
                    errorMessage =
                        "That room is unavailable or you do not have access.",
                    roomId,
                    subRoomId
                });
            }

            PlayerDB.RecordRoomVisit(player.PlayerId, roomId);
            if (room.Name.Contains("RecCenter", StringComparison.OrdinalIgnoreCase) ||
                room.Name.Contains("Rec Center", StringComparison.OrdinalIgnoreCase))
            {
                var (challengeMapId, _, _) = RecNetDB.GetCurrentChallengeWindow();
                RecNetDB.SetChallengeProgress(
                    player.PlayerId,
                    challengeMapId,
                    challengeId: 1,
                    progress: 1,
                    goal: 1);
            }

            Console.WriteLine(
                $"[SUBROOM MATCHMAKE] player={player.PlayerId} room={roomId} " +
                $"subroom={subRoomId} instance={heartbeat.roomInstance?.roomInstanceId}");
            await NotiController.NotifyPlayerPresenceUpdatedAsync(player.PlayerId);
            return Ok(heartbeat);
        }

        [HttpPost("matchmake/private/room/{roomId:long}/{subRoomId:long}")]
        [HttpPost("matchmake/room/{roomId:long}/{subRoomId:long}/private")]
        public async Task<IActionResult> MatchmakePrivateRoomSubRoom(
            long roomId,
            long subRoomId)
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player == null)
                return Unauthorized();

            var heartbeat = Sessions.CreateRoom(
                player.PlayerId,
                roomId,
                subRoomId,
                isPrivate: true);
            if (heartbeat == null)
                return NotFound();

            PlayerDB.RecordRoomVisit(player.PlayerId, roomId);
            Console.WriteLine(
                $"[PRIVATE MATCHMAKE] player={player.PlayerId} " +
                $"room={roomId} subroom={subRoomId} " +
                $"instance={heartbeat.roomInstance?.roomInstanceId}");
            await NotiController.NotifyPlayerPresenceUpdatedAsync(player.PlayerId);
            return Ok(heartbeat);
        }

        private async Task<bool> ReadPrivateMatchmakingFlagAsync()
        {
            foreach (string key in new[]
                     {
                         "isPrivate", "private", "createPrivate",
                         "roomInstanceType", "instanceType"
                     })
            {
                foreach (string value in Request.Query[key])
                {
                    if (TryParsePrivateMatchmakingValue(key, value, out bool result))
                        return result;
                }
            }

            foreach (string key in new[]
                     {
                         "X-Room-Private", "X-Private-Room",
                         "RoomInstanceType"
                     })
            {
                foreach (string value in Request.Headers[key])
                {
                    if (TryParsePrivateMatchmakingValue(key, value, out bool result))
                        return result;
                }
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(
                    HttpContext.RequestAborted);
                foreach (var field in form)
                {
                    foreach (string value in field.Value)
                    {
                        if (TryParsePrivateMatchmakingValue(
                                field.Key,
                                value,
                                out bool result))
                        {
                            return result;
                        }
                    }
                }

                return false;
            }

            if ((Request.ContentLength ?? 0) <= 0)
                return false;

            using var reader = new StreamReader(Request.Body);
            string body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
                return false;

            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                foreach (JsonProperty property in
                         document.RootElement.EnumerateObject())
                {
                    if (TryParsePrivateMatchmakingValue(
                            property.Name,
                            property.Value.ToString(),
                            out bool result))
                    {
                        return result;
                    }
                }
            }
            catch (JsonException)
            {
                if (TryParsePrivateMatchmakingValue(
                        "isPrivate",
                        body,
                        out bool result))
                {
                    return result;
                }
            }

            return false;
        }

        private static bool TryParsePrivateMatchmakingValue(
            string key,
            string? value,
            out bool result)
        {
            result = false;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim().Trim('"').ToLowerInvariant();
            bool typeField = key.Contains(
                "type",
                StringComparison.OrdinalIgnoreCase);

            if (typeField)
            {
                if (normalized is
                    "privatenewinstance" or
                    "private_new_instance" or
                    "private-new-instance" or
                    "2")
                {
                    result = true;
                    return true;
                }

                if (normalized is
                    "publicmatchmaking" or
                    "public_matchmaking" or
                    "public-matchmaking" or
                    "publicnewinstance" or
                    "public_new_instance" or
                    "public-new-instance" or
                    "public" or
                    "0" or
                    "1")
                {
                    result = false;
                    return true;
                }

                if (normalized == "private")
                {
                    result = true;
                    return true;
                }
            }

            if (normalized is "private" or "true" or "yes" or "on" ||
                normalized == "1")
            {
                result = true;
                return true;
            }

            if (normalized is "public" or "false" or "no" or "off" ||
                normalized == "0")
            {
                result = false;
                return true;
            }

            return false;
        }

        private async Task<string> ReadJoinResultReportAsync()
        {
            var values = new List<string>();
            foreach (var query in Request.Query)
            {
                values.AddRange(query.Value.Select(value =>
                    $"{query.Key}={value}"));
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(
                    HttpContext.RequestAborted);
                foreach (var field in form)
                {
                    values.AddRange(field.Value.Select(value =>
                        $"{field.Key}={value}"));
                }
            }
            else if ((Request.ContentLength ?? 0) > 0)
            {
                using var reader = new StreamReader(Request.Body);
                string body = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(body))
                    values.Add(body);
            }

            return string.Join("&", values);
        }

        private static bool LooksLikeFailedJoinResult(string report)
        {
            if (string.IsNullOrWhiteSpace(report))
                return false;

            string normalized = report.ToLowerInvariant();
            if (normalized.Contains("fail", StringComparison.Ordinal) ||
                normalized.Contains("error", StringComparison.Ordinal) ||
                normalized.Contains("timeout", StringComparison.Ordinal) ||
                normalized.Contains("cancel", StringComparison.Ordinal) ||
                normalized.Contains("success=false", StringComparison.Ordinal) ||
                normalized.Contains("succeeded=false", StringComparison.Ordinal) ||
                normalized.Contains("\"success\":false", StringComparison.Ordinal) ||
                normalized.Contains("\"succeeded\":false", StringComparison.Ordinal))
            {
                return true;
            }

            Match numericResult = Regex.Match(
                normalized,
                @"(?:^|[?&{,\s""])(?:joinresult|joinresultcode|result)" +
                @"[""']?\s*[:=]\s*[""']?(?<code>-?\d+)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
            return numericResult.Success &&
                   long.TryParse(
                       numericResult.Groups["code"].Value,
                       out long resultCode) &&
                   resultCode != 0;
        }
    }
}