using Mocha2023.Auth;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs;
using Microsoft.AspNetCore.Mvc;
using System;
using static Mocha2023.Classes.DBs.DBClasses.PlayerDBClasses;

namespace Mocha2023.Controllers
{
    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    [Route("recnet/api")]
    public sealed class BanAppealController : ControllerBase
    {
        private const string AppealCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        [HttpGet("banappeal/generateCode")]
        public IActionResult GenerateCode()
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized(new { error = "You must be logged in." });

            var account = PlayerDB.Players.FindById(accountId.Value);
            ModerationBlockDetails? details = account?.Player?.PlayerExtra?.ModerationBlockDetails;
            if (account?.Player == null || details == null ||
                !PlayerDB.IsPlayerBanned(accountId.Value, out _))
            {
                return BadRequest(new { error = "This account isn't currently banned." });
            }

            if (string.IsNullOrWhiteSpace(details.AppealCode))
            {
                details.AppealCode = GenerateCodeString();
                PlayerDB.Players.Update(account);
            }

            return Ok(new
            {
                code = details.AppealCode,
                appealUrl = $"/recnet/banappeal?code={details.AppealCode}",
                alreadySubmitted = details.AppealSubmitted
            });
        }

        [HttpGet("banappeal/status")]
        public IActionResult GetAppealStatus([FromQuery] string code)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized(new { error = "You must be logged in." });

            if (!TryResolveAppeal(accountId.Value, code, out _, out ModerationBlockDetails? details, out IActionResult? error))
                return error!;

            return Ok(new
            {
                banReason = details!.Message,
                banIsPermanent = details.Duration <= 0,
                bannedAtUnix = details.ModerationSetUnixTime,
                alreadySubmitted = details.AppealSubmitted,
                submittedAtUtc = details.AppealSubmittedAt
            });
        }

        [HttpPost("banappeal/submit")]
        public IActionResult SubmitAppeal([FromBody] BanAppealSubmission request)
        {
            long? accountId = AuthStuff.GetPlayerId(Request);
            if (accountId == null)
                return Unauthorized(new { error = "You must be logged in." });

            string code = (request?.Code ?? string.Empty).Trim();
            if (!TryResolveAppeal(accountId.Value, code, out var account, out ModerationBlockDetails? details, out IActionResult? error))
                return error!;

            string message = (request?.Message ?? string.Empty).Trim();
            if (message.Length is < 10 or > 2000)
                return BadRequest(new { error = "Appeal message must be 10-2000 characters." });

            if (details!.AppealSubmitted)
            {
                return Ok(new
                {
                    success = true,
                    alreadySubmitted = true,
                    submittedAtUtc = details.AppealSubmittedAt
                });
            }

            details.AppealSubmitted = true;
            details.AppealSubmittedAt = DateTime.UtcNow;
            PlayerDB.Players.Update(account);

            string username = account!.Player?.Username ?? accountId.Value.ToString();

            Console.WriteLine(
                $"[BAN APPEAL] accountId={accountId.Value} username={username} " +
                $"code={details.AppealCode} banReason=\"{details.Message}\" " +
                $"message=\"{message}\"");

            DiscordLogger.Log(
                $"**Ban Appeal Submitted**\n" +
                $"Account: {username} (`{accountId.Value}`)\n" +
                $"Code: `{details.AppealCode}`\n" +
                $"Ban reason: {details.Message}\n" +
                $"Appeal: {message}");

            return Ok(new { success = true, alreadySubmitted = false });
        }

        private static bool TryResolveAppeal(
            long accountId,
            string? code,
            out FullPlayer? account,
            out ModerationBlockDetails? details,
            out IActionResult? error)
        {
            account = PlayerDB.Players.FindById(accountId);
            details = account?.Player?.PlayerExtra?.ModerationBlockDetails;
            error = null;

            if (account?.Player == null || details == null ||
                !PlayerDB.IsPlayerBanned(accountId, out _))
            {
                error = new BadRequestObjectResult(new { error = "This account isn't currently banned." });
                return false;
            }

            if (string.IsNullOrWhiteSpace(details.AppealCode) ||
                !string.Equals(details.AppealCode, (code ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                error = new BadRequestObjectResult(new { error = "That appeal code doesn't match this account's ban." });
                return false;
            }

            return true;
        }

        private static string GenerateCodeString()
        {
            Span<char> buffer = stackalloc char[10];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = AppealCodeChars[System.Random.Shared.Next(AppealCodeChars.Length)];
            return new string(buffer);
        }

        public sealed class BanAppealSubmission
        {
            public string? Code { get; set; }
            public string? Message { get; set; }
        }
    }
}
