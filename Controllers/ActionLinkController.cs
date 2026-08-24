using Mocha2023.Auth;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs;
using Mocha2023.Classes.DBs.DBClasses;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using static Mocha2023.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Mocha2023.Controllers
{
    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    public class ActionLinkController : ControllerBase
    {
        private const int InfluencerActionLinkType = 7;
        private const int UnknownPlatform = 0;

        [HttpGet("/actionlink/{code}")]
        [HttpGet("/api/actionlink/{code}")]
        public IActionResult CheckActionLink([FromRoute] string code)
        {
            string normalizedCode = NormalizeCode(code);
            FullPlayer? influencer = FindInfluencerByCode(normalizedCode);
            int accountId = influencer?.PlayerId is > 0 and <= int.MaxValue
                ? (int)influencer.PlayerId
                : 0;

            Console.WriteLine(
                $"[ACTION LINK] code={normalizedCode} " +
                $"type=Influencer account={accountId} valid={accountId > 0}");

            return Content(
                BuildActionLinkResponseJson(
                    normalizedCode,
                    accountId,
                    consumed: false),
                "application/json");
        }

        [HttpPost("/actionlink/{code}/consume")]
        [HttpPost("/api/actionlink/{code}/consume")]
        public IActionResult ConsumeActionLink([FromRoute] string code)
        {
            string normalizedCode = NormalizeCode(code);
            FullPlayer? influencer = FindInfluencerByCode(normalizedCode);
            int accountId = influencer?.PlayerId is > 0 and <= int.MaxValue
                ? (int)influencer.PlayerId
                : 0;

            Console.WriteLine(
                $"[ACTION LINK CONSUME] code={normalizedCode} " +
                $"type=Influencer account={accountId} valid={accountId > 0}");

            return Content(
                BuildActionLinkResponseJson(
                    normalizedCode,
                    accountId,
                    consumed: accountId > 0),
                "application/json");
        }

        [HttpGet("/api/influencerpartnerprogram/code")]
        public IActionResult GetMyInfluencerCode()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (!accountId.HasValue)
                return Unauthorized();

            FullPlayer? account = PlayerDB.Players.FindById(accountId.Value);
            if (account?.Player == null ||
                account.PlayerRoles?.Contains(PlayerRoles.Influencer) != true)
            {
                return NotFound(new { error = "influencer_not_found" });
            }

            string code = GetPlayerInfluencerCode(account);
            return Ok(new
            {
                accountId = account.PlayerId,
                influencerCode = code,
                code
            });
        }

        private static string BuildActionLinkResponseJson(
            string code,
            int accountId,
            bool consumed)
        {
            var extraPayload = new Dictionary<string, object?>
            {
                ["InfluencerAccountId"] = accountId,
                ["influencerAccountId"] = accountId,
                ["AccountId"] = accountId,
                ["accountId"] = accountId
            };

            string extraJson = JsonSerializer.Serialize(extraPayload);

            var actionLinkData = new Dictionary<string, object?>
            {
                ["ExtraJson"] = extraJson,
                ["Platform"] = UnknownPlatform,
                ["Type"] = InfluencerActionLinkType
            };

            string data = JsonSerializer.Serialize(actionLinkData);

            var response = new Dictionary<string, object?>
            {
                ["AccountId"] = accountId,
                ["accountId"] = accountId,
                ["Data"] = data,
                ["data"] = data,
                ["IsConsumed"] = consumed,
                ["isConsumed"] = consumed,
                ["Consumed"] = consumed,
                ["consumed"] = consumed,
                ["ActionCode"] = code,
                ["actionCode"] = code
            };

            return JsonSerializer.Serialize(response);
        }

        private static FullPlayer? FindInfluencerByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            if (long.TryParse(code, out long numericAccountId) &&
                numericAccountId > 0)
            {
                FullPlayer? numericAccount =
                    PlayerDB.Players.FindById(numericAccountId);

                if (IsInfluencer(numericAccount))
                    return numericAccount;
            }

            return PlayerDB.Players
                .FindAll()
                .FirstOrDefault(account =>
                    IsInfluencer(account) &&
                    string.Equals(
                        GetPlayerInfluencerCode(account),
                        code,
                        StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsInfluencer(FullPlayer? account)
        {
            return account?.Player != null &&
                account.PlayerRoles?.Contains(PlayerRoles.Influencer) == true;
        }

        private static string GetPlayerInfluencerCode(FullPlayer account)
        {
            string explicitCode = NormalizeCode(
                account.Player?.InfluencerCode ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(explicitCode))
                return explicitCode;

            return NormalizeCode(
                account.Player?.Username ?? account.PlayerId.ToString());
        }

        private static string NormalizeCode(string? code)
        {
            string value = Uri.UnescapeDataString(code ?? string.Empty)
                .Trim()
                .TrimStart('@');

            int queryIndex = value.IndexOf('?');
            if (queryIndex >= 0)
                value = value[..queryIndex];

            return value.Trim('/').Trim();
        }
    }
}
