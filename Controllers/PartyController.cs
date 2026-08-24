using Mocha2023.Auth;
using Mocha2023.Classes.DBs;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using static Mocha2023.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Mocha2023.Controllers
{
    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    public class PartyController : ControllerBase
    {
        [HttpGet("/api/party/v1/current")]
        [HttpGet("/api/parties/v1/current")]
        [HttpGet("/match/party/current")]
        public IActionResult GetCurrentParty()
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            PartySnapshot? party = PlayerDB.GetPartySnapshot(playerId.Value);
            return Ok(ToResponse(party));
        }

        [HttpGet("/settings/partyinvite")]
        public IActionResult GetPartyInviteSetting()
        {
            var player = AuthStuff.GetCurrentPlayer(Request);
            if (player?.Player == null)
                return Unauthorized();

            bool enabled = true;
            var setting = player.Player.PlayerExtra?.Settings?.FirstOrDefault(value =>
                string.Equals(value.Key, "PartyInvite", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value.Key, "AllowPartyInvites", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value.Key, "Settings.PartyInvite", StringComparison.OrdinalIgnoreCase));

            if (setting != null && bool.TryParse(setting.Value, out bool parsed))
                enabled = parsed;

            Console.WriteLine(
                $"[PARTY INVITE SETTING] player={player.PlayerId} enabled={enabled}");

            return Ok(enabled);
        }

        [HttpGet("/api/party/v1/invites")]
        [HttpGet("/api/parties/v1/invites")]
        public IActionResult GetPartyInvites()
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            var invites = PlayerDB.GetPendingPartyInvites(playerId.Value)
                .Select(invite => new
                {
                    partyId = invite.PartyId,
                    inviterPlayerId = invite.InviterPlayerId,
                    createdAt = invite.CreatedAt
                })
                .ToArray();

            return Ok(invites);
        }

        [HttpPost("/api/party/v1/invite")]
        [HttpPost("/api/parties/v1/invite")]
        [HttpPost("/match/party/invite")]
        public async Task<IActionResult> InviteToParty()
        {
            long? inviterPlayerId = AuthStuff.GetPlayerId(Request);
            if (!inviterPlayerId.HasValue)
                return Unauthorized();

            long? targetPlayerId = await ReadPlayerIdAsync(
                "targetPlayerId",
                "targetAccountId",
                "inviteePlayerId",
                "inviteeAccountId",
                "playerId",
                "accountId",
                "id");

            if (!targetPlayerId.HasValue)
                return BadRequest(new { success = false, error = "missing_target_player" });

            PartySnapshot? party = PlayerDB.InviteToParty(
                inviterPlayerId.Value,
                targetPlayerId.Value);

            if (party == null)
                return BadRequest(new { success = false, error = "party_invite_failed" });

            await NotiController.NotifyPartyInviteAsync(
                inviterPlayerId.Value,
                targetPlayerId.Value,
                party.PartyId);

            return Ok(new
            {
                success = true,
                invitedPlayerId = targetPlayerId.Value,
                party = ToResponse(party)
            });
        }

        [HttpPost("/api/party/v1/accept")]
        [HttpPost("/api/party/v1/join")]
        [HttpPost("/api/parties/v1/accept")]
        [HttpPost("/match/party/accept")]
        [HttpPost("/match/party/join")]
        public async Task<IActionResult> AcceptPartyInvite()
        {
            long? inviteePlayerId = AuthStuff.GetPlayerId(Request);
            if (!inviteePlayerId.HasValue)
                return Unauthorized();

            long? inviterPlayerId = await ReadPlayerIdAsync(
                "inviterPlayerId",
                "inviterAccountId",
                "leaderPlayerId",
                "leaderAccountId",
                "playerId",
                "accountId",
                "id");

            if (!inviterPlayerId.HasValue)
                return BadRequest(new { success = false, error = "missing_inviter_player" });

            PartySnapshot? party = PlayerDB.AcceptPartyInvite(
                inviteePlayerId.Value,
                inviterPlayerId.Value);

            return party == null
                ? BadRequest(new { success = false, error = "party_join_failed" })
                : Ok(new { success = true, party = ToResponse(party) });
        }

        [HttpPost("/api/party/v1/decline")]
        [HttpPost("/api/parties/v1/decline")]
        [HttpPost("/match/party/decline")]
        public async Task<IActionResult> DeclinePartyInvite()
        {
            long? inviteePlayerId = AuthStuff.GetPlayerId(Request);
            if (!inviteePlayerId.HasValue)
                return Unauthorized();

            long? inviterPlayerId = await ReadPlayerIdAsync(
                "inviterPlayerId",
                "inviterAccountId",
                "playerId",
                "accountId",
                "id");

            if (!inviterPlayerId.HasValue)
                return BadRequest(new { success = false, error = "missing_inviter_player" });

            return PlayerDB.DeclinePartyInvite(
                    inviteePlayerId.Value,
                    inviterPlayerId.Value)
                ? Ok(new { success = true })
                : NotFound(new { success = false, error = "party_invite_not_found" });
        }

        [HttpPost("/api/party/v1/leave")]
        [HttpPost("/api/parties/v1/leave")]
        [HttpPost("/match/party/leave")]
        public IActionResult LeaveParty()
        {
            long? playerId = AuthStuff.GetPlayerId(Request);
            if (!playerId.HasValue)
                return Unauthorized();

            PartySnapshot? party = PlayerDB.LeaveParty(playerId.Value);
            return Ok(new
            {
                success = true,
                party = ToResponse(party)
            });
        }

        private async Task<long?> ReadPlayerIdAsync(params string[] keys)
        {
            foreach (string key in keys)
            {
                if (long.TryParse(Request.Query[key].FirstOrDefault(), out long queryId))
                    return queryId;
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (string key in keys)
                {
                    if (long.TryParse(form[key].FirstOrDefault(), out long formId))
                        return formId;
                }
            }
            else if ((Request.ContentLength ?? 0) > 0)
            {
                using var reader = new StreamReader(Request.Body);
                string body = await reader.ReadToEndAsync();

                if (long.TryParse(body.Trim().Trim('"'), out long rawId))
                    return rawId;

                try
                {
                    using JsonDocument document = JsonDocument.Parse(body);
                    long? id = FindPlayerId(document.RootElement, keys);
                    if (id.HasValue)
                        return id;
                }
                catch (JsonException)
                {
                    foreach (string pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] parts = pair.Split('=', 2);
                        if (parts.Length != 2 ||
                            !keys.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (long.TryParse(Uri.UnescapeDataString(parts[1]), out long bodyId))
                            return bodyId;
                    }
                }
            }

            return null;
        }

        private static long? FindPlayerId(JsonElement element, IReadOnlyCollection<string> keys)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    return element.TryGetInt64(out long number) ? number : null;

                case JsonValueKind.String:
                    return long.TryParse(element.GetString(), out long textId) ? textId : null;

                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (keys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            long? direct = FindPlayerId(property.Value, keys);
                            if (direct.HasValue)
                                return direct;
                        }
                    }

                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        long? nested = FindPlayerId(property.Value, keys);
                        if (nested.HasValue)
                            return nested;
                    }
                    break;

                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        long? nested = FindPlayerId(item, keys);
                        if (nested.HasValue)
                            return nested;
                    }
                    break;
            }

            return null;
        }

        private static object ToResponse(PartySnapshot? party)
        {
            if (party == null)
            {
                return new
                {
                    partyId = 0L,
                    leaderPlayerId = 0L,
                    memberPlayerIds = Array.Empty<long>()
                };
            }

            return new
            {
                partyId = party.PartyId,
                leaderPlayerId = party.LeaderPlayerId,
                memberPlayerIds = party.MemberPlayerIds.ToArray()
            };
        }
    }
}
