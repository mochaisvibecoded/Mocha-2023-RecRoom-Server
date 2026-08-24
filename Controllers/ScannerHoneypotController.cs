using Microsoft.AspNetCore.Mvc;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs;

namespace Mocha2023.Controllers;

public class ScannerHoneypotController : ControllerBase
{

    private static readonly string[] SuspiciousMarkers =
    {
        ".env", ".git", "aws", "credentials", ".aws", "id_rsa", ".ssh",
        "config.js", "config.json", "appsettings", "web.config", ".htaccess",
        "wp-admin", "wp-login", "wp-content", "wp-json", ".php", "phpinfo",
        "docker-compose", "dockerfile", ".yml", ".yaml", "backup", ".sql",
        ".bak", ".zip.php", "shell", "eval-stdin", "cgi-bin", "vendor/",
        "node_modules", ".npmrc", ".dockerenv", "actuator", "swagger",
        ".well-known/traefik", "debug", "adminer", "phpmyadmin"
    };

    private static readonly HashSet<string> IgnoredPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/favicon.ico",
        "/robots.txt"
    };

    [Route("{*path}")]
    [HttpGet]
    [HttpPost]
    [HttpPut]
    [HttpDelete]
    [HttpHead]
    [HttpOptions]
    [HttpPatch]
    public IActionResult CatchAll(string? path)
    {
        string fullPath = "/" + (path ?? string.Empty);

        if (IgnoredPaths.Contains(fullPath))
            return NotFound();

        string? matchedMarker = SuspiciousMarkers.FirstOrDefault(marker =>
            fullPath.Contains(marker, StringComparison.OrdinalIgnoreCase));

        string ipAddress = ClientNetwork.GetClientIp(Request)?.ToString() ?? "unknown";
        string? userAgent = Request.Headers.TryGetValue("User-Agent", out var uaValues) && uaValues.Count > 0
            ? uaValues.ToString()
            : null;
        string? queryString = Request.QueryString.HasValue
            ? Request.QueryString.Value
            : null;

        ScannerLogDB.Log(
            ipAddress,
            Request.Method,
            fullPath,
            queryString,
            userAgent,
            matchedMarker);

        return NotFound();
    }
}
