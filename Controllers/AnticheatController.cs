using Microsoft.AspNetCore.Mvc;
using Mocha2023.Auth;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs;
using System.Text.Json;

namespace Mocha2023.Controllers;

[ApiController]
[Mocha2023.Classes.ApiProtection]
public class AnticheatController : ControllerBase
{
    public class ReportBody
    {
        public string? flags { get; set; }
        public string? steamId { get; set; }
        public string? build { get; set; }
    }

    [HttpPost("api/anticheat/report")]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Report()
    {
        string body = await new StreamReader(Request.Body).ReadToEndAsync();

        ReportBody? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<ReportBody>(body);
        }
        catch
        {
        }

        string steamId = parsed?.steamId ?? Request.Headers["X-Mocha-SteamId"].ToString();
        string build = parsed?.build ?? Request.Headers["X-Mocha-Build"].ToString();
        string flags = string.IsNullOrWhiteSpace(parsed?.flags) ? "unknown" : parsed.flags;
        string ip = ClientNetwork.GetClientIp(Request)?.ToString() ?? "unknown";
        string? userAgent = Request.Headers.TryGetValue("User-Agent", out var uaValues) && uaValues.Count > 0
            ? uaValues.ToString()
            : null;

        long? accountId = AuthStuff.GetPlayerId(Request);

        AnticheatLogDB.Log(ip, accountId, steamId, build, flags, userAgent);

        Console.WriteLine($"[ANTICHEAT] ip={ip} account={accountId?.ToString() ?? "none"} steam={steamId} build={build} flags={flags}");

        return Ok(new { received = true });
    }
}
