using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading.RateLimiting;
using Mocha2023.Classes;
using Mocha2023.Classes.DBs;
using Mocha2023.Classes.DBs.DBClasses;
using Mocha2023.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Mocha2023.Classes.DBs.DBClasses.PlayerDBClasses;
using JsonSerializer = System.Text.Json.JsonSerializer;

// Modified by @mechanicalize on Discord.
// Original codebase by @nito9999.
// Repository: https://github.com/nito9999/Mocha-2023
// Target client: Rec Room 20230406.
// Licensed under the MIT License.

namespace Mocha2023
{

    public sealed class DualTextWriter : TextWriter
    {
        private readonly TextWriter _console;
        private readonly StreamWriter _file;
        private readonly object _sync = new();

        public DualTextWriter(TextWriter console, StreamWriter file)
        {
            _console = console;
            _file = file;
        }

        public override Encoding Encoding => _console.Encoding;

        public override void Write(char value)
        {
            lock (_sync)
            {
                _console.Write(value);
                _file.Write(value);
            }
        }

        public override void Write(string? value)
        {
            lock (_sync)
            {
                _console.Write(value);
                _file.Write(value);
                _file.Flush();
            }
        }

        public override void WriteLine(string? value)
        {
            lock (_sync)
            {
                _console.WriteLine(value);
                _file.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] {value}");
                _file.Flush();
            }
        }
    }

    internal static class ServerLog
    {
        private static readonly object Sync = new();

        public static void Info(string message) => Write("INFO", message, ConsoleColor.Cyan);
        public static void Success(string message) => Write("OK", message, ConsoleColor.Green);
        public static void Warning(string message) => Write("WARN", message, ConsoleColor.Yellow);
        public static void Error(string message) => Write("ERROR", message, ConsoleColor.Red);

        public static void Http(
            int statusCode,
            string method,
            string path,
            long elapsedMilliseconds,
            Exception? exception = null)
        {
            ConsoleColor color = statusCode switch
            {
                >= 500 => ConsoleColor.Red,
                >= 400 => ConsoleColor.Yellow,
                >= 300 => ConsoleColor.Cyan,
                >= 200 => ConsoleColor.Green,
                _ => ConsoleColor.Gray
            };

            string suffix = exception == null
                ? string.Empty
                : $" | {exception.GetType().Name}: {exception.Message}";

            WriteRaw(
                $"[{statusCode}] {method,-7} {path} ({elapsedMilliseconds} ms){suffix}",
                color
            );
        }

        private static void Write(string tag, string message, ConsoleColor color)
        {
            WriteRaw($"[{tag}] {message}", color);
        }

        private static void WriteRaw(string message, ConsoleColor color)
        {
            lock (Sync)
            {
                ConsoleColor previousColor = Console.ForegroundColor;

                try
                {
                    Console.ForegroundColor = color;
                    Console.WriteLine(message);
                }
                finally
                {
                    Console.ForegroundColor = previousColor;
                }
            }
        }
    }

    public class Program
    {
        private static readonly string ServerUrl =
            Environment.GetEnvironmentVariable("MOCHA_SERVER_URL") ??
            "https://localhost:443";
        private static readonly string SERVERSTATWEB = LoadLocalSetting("SERVERSTATWEB");

        internal static string LoadLocalSetting(string settingName)
        {
            string? environmentValue = Environment.GetEnvironmentVariable(settingName);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue.Trim();
            }

            string envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            if (!File.Exists(envPath))
            {
                return string.Empty;
            }

            foreach (string line in File.ReadLines(envPath))
            {
                string trimmedLine = line.Trim();
                if (trimmedLine.Length == 0 || trimmedLine.StartsWith('#'))
                {
                    continue;
                }

                int separatorIndex = trimmedLine.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = trimmedLine[..separatorIndex].Trim();
                if (string.Equals(key, settingName, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmedLine[(separatorIndex + 1)..].Trim().Trim('"', '\'');
                }
            }

            return string.Empty;
        }

        private static readonly HttpClient ServerStatusHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private static DateTimeOffset _serverStartedAt;
        private static int _serverIsOnline;
        private static int _offlineWebhookSent;

        public static string dataDir = Path.Combine(
            Directory.GetCurrentDirectory(),
            "Data"
        );

        // Set from builder.Environment.WebRootPath in Main - the framework's
        // own resolved wwwroot path, not a guess based on CWD/BaseDirectory
        // (which RecNetController.Serve() used to do and got wrong on the
        // container).
        public static string wwwRootDir = string.Empty;

        private static readonly string[] RequiredDirectories =
        {
            "DBs",
            "Images",
            Path.Combine("Images", "PlayerImages"),
            Path.Combine("Images", "CustomPFPS"),
            Path.Combine("Images", "PolaroidImages"),
            Path.Combine("Images", "RemoteLoadingScreens"),
            Path.Combine("Images", "EventImages"),
            Path.Combine("Images", "CommunityBoardUploads"),
            "CommunityBoardUploads",
            Path.Combine("CDN", "video"),
            Path.Combine("CDN", "DataBlobs"),
            Path.Combine("CDN", "InventionBlobs"),
            Path.Combine("CDN", "RoomBlobs"),
            "Imports",
            "Debug"
        };

        private sealed class ConsoleInputGuard
        {
            private readonly TextWriter _promptWriter;
            private readonly TextWriter _logWriter;

            public ConsoleInputGuard(
                TextWriter promptWriter,
                TextWriter logWriter)
            {
                _promptWriter = promptWriter;
                _logWriter = logWriter;
            }

            public string ReadLine()
            {
                _promptWriter.Write("> ");

                string line = Console.ReadLine()?.Trim() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(line))
                {
                    _logWriter.WriteLine($"[CMD] {line}");
                }

                return line;
            }
        }

        public static async Task Main(string[] args)
        {
            TextWriter originalConsole = Console.Out;
            string logFilePath = InitializeFileLogging(originalConsole);
            var consoleInput = new ConsoleInputGuard(originalConsole, Console.Out);

            ServerLog.Info($"Debug log: {logFilePath}");

            EnsureDataDirectories();

            WebApplication? app = null;
            bool serverStarted = false;
            string shutdownReason = "Server stopped normally";

            try
            {
                WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
                wwwRootDir = builder.Environment.WebRootPath;
                ConfigureFrameworkLogging(builder);
                ConfigureServices(builder);

                app = builder.Build();

                NotiController.Initialize(
                    app.Services.GetRequiredService<IHubContext<NotificationHub>>());

                await ImportBaseRoomsAsync();
                int orphanedDormsRemoved = RoomDB.RemoveOrphanedPlayerDorms();
                ServerLog.Info($"Orphaned player dorms removed: {orphanedDormsRemoved}");
                int dormsCreated = RoomDB.EnsurePlayerDormsForAllPlayers();
                ServerLog.Info($"Player dorm ownership: created={dormsCreated}");
                EnforceRRPlusForExistingPlayers();
                int socialAccountsUpdated = PlayerDB.EnsureSocialDefaultsForAllPlayers();
                ServerLog.Info($"Social defaults: updated={socialAccountsUpdated}");
                int tokenAccountsUpdated = PlayerDB.EnsureInitialTokensForAllPlayers();
                ServerLog.Info(
                    $"Initial token grant: updated={tokenAccountsUpdated}, " +
                    $"amount={PlayerDB.InitialTokenBalance}");
                int alpacaOwnershipsRemoved =
                    PlayerDB.ResetAlpacaShirtOwnershipForExistingPlayers();
                ServerLog.Info(
                    $"Alpaca Shirt ownership reset: removed={alpacaOwnershipsRemoved}");
                ConfigureRequestPipeline(app);

                app.Urls.Add(ServerUrl);
                await app.StartAsync();

                serverStarted = true;
                _serverStartedAt = DateTimeOffset.UtcNow;
                Volatile.Write(ref _serverIsOnline, 1);
                RegisterShutdownWebhookHandlers();

                ServerLog.Success($"Server listening on {ServerUrl}");
                ServerLog.Info("Type 'help' to view console commands.");

                Mocha2023.Cloudflare.StartCloudflared();

                await SendServerOnlineWebhookAsync();
                await RunConsoleLoopAsync(app, consoleInput);
            }
            catch (Exception ex)
            {
                shutdownReason = $"Unexpected shutdown: {ex.GetType().Name}";
                ServerLog.Error($"Fatal server error: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            finally
            {
                if (serverStarted)
                {
                    await SendServerOfflineWebhookOnceAsync(shutdownReason);
                }

                if (app != null)
                {
                    await app.DisposeAsync();
                }
            }
        }

        private static bool HasServerStatusWebhook()
        {
            return !string.IsNullOrWhiteSpace(SERVERSTATWEB) &&
                   !SERVERSTATWEB.Contains(
                       "PASTE_DISCORD_WEBHOOK_HERE",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void RegisterShutdownWebhookHandlers()
        {
            Console.CancelKeyPress += (_, _) =>
            {
                try
                {
                    SendServerOfflineWebhookOnceAsync("Server stopped with Ctrl+C")
                        .GetAwaiter()
                        .GetResult();
                }
                catch
                {

                }
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    SendServerOfflineWebhookOnceAsync("Server process exited")
                        .GetAwaiter()
                        .GetResult();
                }
                catch
                {

                }
            };
        }

        private static async Task SendServerOnlineWebhookAsync()
        {
            var fields = new object[]
            {
                new
                {
                    name = "Status",
                    value = "🟢 Online",
                    inline = true
                },
                new
                {
                    name = "Build",
                    value = "Rec Room 2023.04.06",
                    inline = true
                },
                new
                {
                    name = "Server",
                    value = $"`{ServerUrl}`",
                    inline = false
                }
            };

            await SendServerStatusWebhookAsync(
                title: "☕ Mocha is freshly brewed!",
                description:
                    "The Mocha 2023 server is online and ready for players. " +
                    "The café is officially open!",
                color: 0xA66A3F,
                fields: fields,
                footerText: "Mocha • Freshly brewed for Rec Room 2023"
            );
        }

        private static async Task SendServerOfflineWebhookOnceAsync(string reason)
        {
            if (Volatile.Read(ref _serverIsOnline) == 0 ||
                Interlocked.Exchange(ref _offlineWebhookSent, 1) != 0)
            {
                return;
            }

            TimeSpan uptime = _serverStartedAt == default
                ? TimeSpan.Zero
                : DateTimeOffset.UtcNow - _serverStartedAt;

            var fields = new object[]
            {
                new
                {
                    name = "Status",
                    value = "🔴 Offline",
                    inline = true
                },
                new
                {
                    name = "Uptime",
                    value = FormatUptime(uptime),
                    inline = true
                },
                new
                {
                    name = "Reason",
                    value = reason,
                    inline = false
                }
            };

            await SendServerStatusWebhookAsync(
                title: "🫘 Mocha has gone cold",
                description:
                    "The Mocha 2023 server is offline. " +
                    "The café is closed for now.",
                color: 0x4E342E,
                fields: fields,
                footerText: "Mocha • Coffee break in progress"
            );

            Volatile.Write(ref _serverIsOnline, 0);
        }

        private static async Task SendServerStatusWebhookAsync(
            string title,
            string description,
            int color,
            object[] fields,
            string footerText)
        {
            if (!HasServerStatusWebhook())
            {
                ServerLog.Warning(
                    "SERVERSTATWEB is not configured; status webhook skipped.");
                return;
            }

            var payload = new
            {
                username = "Mocha Server Status",
                embeds = new[]
                {
                    new
                    {
                        title,
                        description,
                        color,
                        fields,
                        footer = new
                        {
                            text = footerText
                        },
                        timestamp = DateTimeOffset.UtcNow.ToString("O")
                    }
                }
            };

            try
            {
                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json");

                using HttpResponseMessage response =
                    await ServerStatusHttpClient.PostAsync(SERVERSTATWEB, content);

                if (response.IsSuccessStatusCode)
                {
                    ServerLog.Success("Discord server-status webhook sent.");
                    return;
                }

                string responseBody = await response.Content.ReadAsStringAsync();
                ServerLog.Warning(
                    $"Discord status webhook returned " +
                    $"{(int)response.StatusCode}: {responseBody}");
            }
            catch (Exception ex)
            {

                ServerLog.Warning(
                    $"Discord status webhook failed: " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string FormatUptime(TimeSpan uptime)
        {
            if (uptime.TotalDays >= 1)
            {
                return $"{(int)uptime.TotalDays}d {uptime.Hours}h " +
                       $"{uptime.Minutes}m {uptime.Seconds}s";
            }

            if (uptime.TotalHours >= 1)
            {
                return $"{(int)uptime.TotalHours}h " +
                       $"{uptime.Minutes}m {uptime.Seconds}s";
            }

            return $"{uptime.Minutes}m {uptime.Seconds}s";
        }

        private static string InitializeFileLogging(TextWriter originalConsole)
        {
            string debugDirectory = Path.Combine(dataDir, "Debug");
            Directory.CreateDirectory(debugDirectory);

            string logFilePath = Path.Combine(
                debugDirectory,
                $"console_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log"
            );

            var fileWriter = new StreamWriter(logFilePath, append: true)
            {
                AutoFlush = true
            };

            Console.SetOut(new DualTextWriter(originalConsole, fileWriter));
            return logFilePath;
        }

        private static void EnsureDataDirectories()
        {
            Directory.CreateDirectory(dataDir);

            foreach (string relativePath in RequiredDirectories)
            {
                Directory.CreateDirectory(Path.Combine(dataDir, relativePath));
            }
        }

        private static void ConfigureFrameworkLogging(
            WebApplicationBuilder builder)
        {

            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.None);
        }

        private static void ConfigureServices(WebApplicationBuilder builder)
        {
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxRequestBodySize = 4 * 1024 * 1024;
                options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
                options.Limits.MaxRequestHeaderCount = 100;
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
                options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
            });

            builder.Services
                .AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = null;
                    options.JsonSerializerOptions.MaxDepth = 64;
                });

            builder.Services.AddAuthorization();

            builder.Services
                .AddSignalR(options =>
                {
                    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                    options.ClientTimeoutInterval = TimeSpan.FromSeconds(45);
                    options.MaximumReceiveMessageSize = 1024 * 1024;
                    options.EnableDetailedErrors = false;
                })
                .AddJsonProtocol(options =>
                {
                    options.PayloadSerializerOptions.PropertyNamingPolicy = null;
                    options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
                });

            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, cancellationToken) =>
                {
                    if (!context.HttpContext.Response.HasStarted)
                    {
                        context.HttpContext.Response.ContentType = "application/json";
                        await context.HttpContext.Response.WriteAsJsonAsync(
                            new { error = "Too many requests. Try again shortly." },
                            cancellationToken);
                    }
                };

                options.GlobalLimiter =
                    PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    {
                        string clientKey = GetRateLimitClientKey(context);
                        PathString path = context.Request.Path;

                        if (IsAuthenticationAttempt(path))
                        {
                            return RateLimitPartition.GetFixedWindowLimiter(
                                $"auth:{clientKey}",
                                _ => new FixedWindowRateLimiterOptions
                                {
                                    PermitLimit = 30,
                                    Window = TimeSpan.FromMinutes(1),
                                    QueueLimit = 0,
                                    AutoReplenishment = true
                                });
                        }

                        if (path.StartsWithSegments(
                                "/reports",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return RateLimitPartition.GetFixedWindowLimiter(
                                $"crash:{clientKey}",
                                _ => new FixedWindowRateLimiterOptions
                                {
                                    PermitLimit = 6,
                                    Window = TimeSpan.FromMinutes(10),
                                    QueueLimit = 0,
                                    AutoReplenishment = true
                                });
                        }

                        if (IsLargeUpload(context.Request))
                        {
                            return RateLimitPartition.GetFixedWindowLimiter(
                                $"upload:{clientKey}",
                                _ => new FixedWindowRateLimiterOptions
                                {
                                    PermitLimit = 60,
                                    Window = TimeSpan.FromMinutes(10),
                                    QueueLimit = 0,
                                    AutoReplenishment = true
                                });
                        }

                        return RateLimitPartition.GetNoLimiter($"normal:{clientKey}");
                    });
            });
        }

        private static bool IsAuthenticationAttempt(PathString path)
        {
            return path.StartsWithSegments(
                       "/auth/connect/token",
                       StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWithSegments(
                       "/auth/cachedlogin",
                       StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWithSegments(
                       "/recnet/api/auth/login",
                       StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWithSegments(
                       "/recnet/api/auth/register",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLargeUpload(HttpRequest request)
        {
            if (request.Method is not ("POST" or "PUT" or "PATCH"))
                return false;

            PathString path = request.Path;
            return request.ContentLength > 1024 * 1024 ||
                   path.StartsWithSegments(
                       "/api/images/v4/uploadsaved",
                       StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWithSegments(
                       "/recnet/api/account/profile-image",
                       StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWithSegments(
                       "/recnet/api/account/banner-image",
                       StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWithSegments(
                       "/recnet/api/admin/events/image",
                       StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWithSegments(
                       "/recnet/api/admin/community-board/media",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRateLimitClientKey(HttpContext context)
        {
            IPAddress? remoteAddress = context.Connection.RemoteIpAddress;
            if (remoteAddress != null &&
                IPAddress.IsLoopback(remoteAddress) &&
                context.Request.Headers.TryGetValue(
                    "CF-Connecting-IP",
                    out var forwardedValues) &&
                IPAddress.TryParse(
                    forwardedValues.FirstOrDefault(),
                    out IPAddress? forwardedAddress))
            {
                remoteAddress = forwardedAddress;
            }

            return remoteAddress?.MapToIPv6().ToString() ?? "unknown";
        }

        private static async Task ImportBaseRoomsAsync()
        {
            string importPath = Path.Combine(
                dataDir,
                "Imports",
                "ImportRooms.json"
            );

            if (!File.Exists(importPath))
            {
                return;
            }

            if (RoomDB.Rooms.Count() == 0)
            {
                ServerLog.Info("Importing rooms from ImportRooms.json...");
                await RoomDB.ImportRooms(importPath);
                ServerLog.Success("Room import complete.");
                return;
            }

            int repaired = await RoomDB.EnsureCanonicalBaseRooms(importPath);
            ServerLog.Info($"Base room reconciliation: changed={repaired}");
        }

        private static void EnforceRRPlusForExistingPlayers()
        {
            int settingsUpdated = PlayerDB.ForceRRPlusForAllPlayers();
            int rolesAdded = ForceRRPlusRoleForAllPlayers();

            ServerLog.Info(
                $"RR+ enforcement: settings={settingsUpdated}, " +
                $"roles={rolesAdded}, players={PlayerDB.Players.Count()}"
            );
        }

        private static void ConfigureRequestPipeline(WebApplication app)
        {
            app.UseWebSockets();

            app.UseWhen(
                context => context.Request.Path.StartsWithSegments(
                    "/recnet",
                    StringComparison.OrdinalIgnoreCase),
                recNetApp => recNetApp.Use(async (context, next) =>
                {
                    context.Response.OnStarting(() =>
                    {
                        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                        context.Response.Headers["X-Frame-Options"] = "DENY";
                        context.Response.Headers["Cross-Origin-Opener-Policy"] =
                            "same-origin";
                        context.Response.Headers["Cross-Origin-Resource-Policy"] =
                            "same-origin";
                        context.Response.Headers["Referrer-Policy"] = "no-referrer";
                        context.Response.Headers["Permissions-Policy"] =
                            "camera=(), microphone=(), geolocation=()";
                        context.Response.Headers["Content-Security-Policy"] =
                            "default-src 'self'; " +
                            "script-src 'self' https://static.cloudflareinsights.com; " +
                            "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://fonts.googleapis.com; " +
                            "font-src 'self' https://cdnjs.cloudflare.com https://fonts.gstatic.com; " +
                            "img-src 'self' data: blob:; " +
                            "connect-src 'self' ws: wss: https://cloudflareinsights.com; " +
                            "object-src 'none'; base-uri 'self'; " +
                            "form-action 'self'; frame-ancestors 'none'";

                        if (context.Request.Path.StartsWithSegments(
                                "/recnet/api",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            context.Response.Headers.CacheControl =
                                "no-store, no-cache, must-revalidate";
                            context.Response.Headers.Pragma = "no-cache";
                        }

                        return Task.CompletedTask;
                    });

                    await next();
                }));

            app.Use(async (context, next) =>
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                Exception? exception = null;
                long inboundBytes =
                    DeveloperTelemetry.EstimateInboundBytes(context.Request);
                DeveloperTelemetry.RecordInboundBytes(inboundBytes);

                Stream originalResponseBody = context.Response.Body;
                var countedResponseBody = new CountingResponseStream(
                    originalResponseBody,
                    bytes => DeveloperTelemetry.RecordOutboundBytes(bytes));
                context.Response.Body = countedResponseBody;

                try
                {
                    await next();
                }
                catch (Exception ex)
                {
                    exception = ex;

                    if (!context.Response.HasStarted)
                    {
                        context.Response.Clear();
                        context.Response.StatusCode =
                            StatusCodes.Status500InternalServerError;
                        context.Response.ContentType = "application/json";

                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = "Internal server error"
                        });
                    }
                }
                finally
                {
                    stopwatch.Stop();
                    context.Response.Body = originalResponseBody;

                    string requestPath =
                        $"{context.Request.PathBase}{context.Request.Path}";
                    DeveloperTelemetry.RecordRequest(
                        context.Response.StatusCode,
                        context.Request.Method,
                        requestPath,
                        stopwatch.ElapsedMilliseconds,
                        inboundBytes,
                        countedResponseBody.BytesWritten);

                    bool isSuccessfulRelationshipPoll =
                        context.Response.StatusCode >= StatusCodes.Status200OK &&
                        context.Response.StatusCode < StatusCodes.Status300MultipleChoices &&
                        context.Request.Path.Equals(
                            "/api/relationships/v2/get",
                            StringComparison.OrdinalIgnoreCase);

                    if (!isSuccessfulRelationshipPoll)
                    {

                        string path = $"{context.Request.PathBase}" +
                                      $"{context.Request.Path}";

                        ServerLog.Http(
                            context.Response.StatusCode,
                            context.Request.Method,
                            path,
                            stopwatch.ElapsedMilliseconds,
                            exception
                        );
                    }
                }
            });

            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseRateLimiter();
            app.UseAuthorization();
            app.MapHub<NotificationHub>("/noti/hub/v1");
            app.MapControllers();

        }

        private static async Task RunConsoleLoopAsync(
            WebApplication app,
            ConsoleInputGuard consoleInput)
        {
            while (true)
            {
                string line = consoleInput.ReadLine();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] commandParts = line.Split(' ', 2);
                string command = commandParts[0].Trim().ToLowerInvariant();
                string arguments = commandParts.Length == 2
                    ? commandParts[1].Trim()
                    : string.Empty;

                TryLogAdminCommand(
                    command,
                    command == "set-password" ? "(redacted)" : arguments
                );

                try
                {
                    bool keepRunning = await ExecuteCommandAsync(command, arguments);

                    if (!keepRunning)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    ServerLog.Error(
                        $"Command '{command}' failed: " +
                        $"{ex.GetType().Name}: {ex.Message}"
                    );

                    TryLogAdminError(command, arguments, ex);
                }
            }

            ServerLog.Info("Stopping server...");
            await app.StopAsync();
            ServerLog.Success("Server stopped.");
        }

        private static async Task<bool> ExecuteCommandAsync(
            string command,
            string arguments)
        {
            switch (command)
            {
                case "help":
                    PrintHelp();
                    break;

                case "give-rrplus":
                    HandleGiveRRPlus(arguments);
                    break;

                case "force-rrplus-all":
                    HandleForceRRPlusAll();
                    break;

                case "give-influencer":
                    HandleGiveInfluencer(arguments);
                    break;

                case "grant-role":
                case "grant-roles":
                    HandleGrantRoles(arguments);
                    break;

                case "set-bio":
                    HandleSetBio(arguments);
                    break;

                case "reset-usernamechanges":
                    HandleResetUsernameChanges(arguments);
                    break;

                case "set-pfp":
                    HandleSetProfileImage(arguments);
                    break;

                case "set-banner":
                    HandleSetBannerImage(arguments);
                    break;

                case "add-roomrole":
                    HandleAddRoomRole(arguments);
                    break;

                case "set-username":
                    HandleSetUsername(arguments);
                    break;

                case "set-display":
                    HandleSetDisplayName(arguments);
                    break;

                case "import-room-dump":
                    {
                        if (string.IsNullOrWhiteSpace(arguments))
                        {
                            Console.WriteLine("Usage: import-room-dump <extracted-folder> [creatorAccountId]");
                            break;
                        }

                        string path = arguments.Trim().Trim('"');
                        long creatorId = 1;

                        int finalSpace = path.LastIndexOf(' ');
                        if (finalSpace > 0 &&
                            long.TryParse(path[(finalSpace + 1)..], out long parsedCreatorId))
                        {
                            creatorId = parsedCreatorId;
                            path = path[..finalSpace].Trim().Trim('"');
                        }

                        ShowdownImporter.Import(path, creatorId, replaceExisting: true);
                        break;
                    }

                case "change-level":
                    HandleChangeLevel(arguments);
                    break;

                case "change-xp":
                    HandleChangeXp(arguments, add: false);
                    break;

                case "add-xp":
                    HandleChangeXp(arguments, add: true);
                    break;

                case "set-balance":
                    HandleBalance(arguments, add: false);
                    break;

                case "add-balance":
                    HandleBalance(arguments, add: true);
                    break;

                case "set-junior":
                    HandleSetJunior(arguments);
                    break;

                case "set-rep":
                    HandleSetReputation(arguments);
                    break;

                case "reset-password":
                    HandleResetPassword(arguments);
                    break;

                case "set-password":
                    HandleSetPassword(arguments);
                    break;

                case "ban-player":
                    HandleBanPlayer(arguments);
                    break;

                case "unban-player":
                    HandleUnbanPlayer(arguments);
                    break;

                case "set-room-state":
                    HandleSetRoomState(arguments);
                    break;

                case "set-room-access":
                    HandleSetRoomAccess(arguments);
                    break;

                case "clear-visits":
                    HandleClearPlayerData(arguments, PlayerDataToClear.Visits);
                    break;

                case "clear-favorites":
                    HandleClearPlayerData(arguments, PlayerDataToClear.Favorites);
                    break;

                case "clear-cheers":
                    HandleClearPlayerData(arguments, PlayerDataToClear.Cheers);
                    break;

                case "reset-profile":
                    HandleResetProfile(arguments);
                    break;

                case "create-account":
                    HandleCreateAccount(arguments);
                    break;

                case "change-accid":
                    HandleChangeAccountId(arguments);
                    break;

                case "maintenance":
                    await HandleMaintenanceAsync(arguments);
                    break;

                case "exit":
                case "quit":
                    return false;

                default:
                    ServerLog.Warning(
                        $"Unknown command '{command}'. Type 'help'."
                    );
                    break;
            }

            return true;
        }

        private static void PrintHelp()
        {
            Console.WriteLine();
            Console.WriteLine("=== Mocha2023 Admin Commands ===");
            Console.WriteLine("help");
            Console.WriteLine("give-rrplus <playerId>");
            Console.WriteLine("force-rrplus-all");
            Console.WriteLine("give-influencer <playerId1> [playerId2...]");
            Console.WriteLine("grant-role <playerIds> <role1> [role2...]");
            Console.WriteLine("set-bio <playerId> <text>");
            Console.WriteLine("reset-usernamechanges <playerId> [amount]");
            Console.WriteLine("set-pfp <playerId> <filename-or-path>");
            Console.WriteLine("set-banner <playerId> <filename-or-path>");
            Console.WriteLine("set-username <playerId> <name>");
            Console.WriteLine("set-display <playerId> <name>");
            Console.WriteLine("change-level <playerId> <level>");
            Console.WriteLine("change-xp <playerId> <xp>");
            Console.WriteLine("add-xp <playerId> <xp>");
            Console.WriteLine("set-balance <playerId> <amount>");
            Console.WriteLine("add-balance <playerId> <amount>");
            Console.WriteLine("set-junior <playerId> <true|false>");
            Console.WriteLine("set-rep <playerId> <value>");
            Console.WriteLine("reset-password <playerId>");
            Console.WriteLine("set-password <playerId> <password>");
            Console.WriteLine("ban-player <playerId> [hours]");
            Console.WriteLine("unban-player <playerId>");
            Console.WriteLine("set-room-state <roomId> <state>");
            Console.WriteLine("import-room-dump <folder> [creatorId] - Imports a legacy Rec Room dump");
            Console.WriteLine("set-room-access <roomId> <private|public|unlisted>");
            Console.WriteLine("clear-visits <playerId>");
            Console.WriteLine("clear-favorites <playerId>");
            Console.WriteLine("clear-cheers <playerId>");
            Console.WriteLine("add-roomrole <playerId> <roomId> <Host|Moderator|CoOwner|TemporaryCoOwner|Creator>");
            Console.WriteLine("reset-profile <playerId>");
            Console.WriteLine("create-account <username> <accountId> <true|false> <platform> <platformId>");
            Console.WriteLine("change-accid <currentAccountId> <newAccountId>");
            Console.WriteLine("maintenance <minutes>");
            Console.WriteLine("exit");
            Console.WriteLine();
        }

        private static void HandleGiveRRPlus(string arguments)
        {
            if (!TryParsePositiveLong(arguments, out long playerId))
            {
                Usage("give-rrplus <playerId>");
                return;
            }

            if (!ForceRRPlusForPlayer(playerId))
            {
                ServerLog.Error($"Player {playerId} was not found or updated.");
                return;
            }

            ServerLog.Success($"RR+ granted to player {playerId}.");
        }

        private static void HandleForceRRPlusAll()
        {
            int settingsUpdated = PlayerDB.ForceRRPlusForAllPlayers();
            int rolesAdded = ForceRRPlusRoleForAllPlayers();

            ServerLog.Success(
                $"RR+ enforced: settings={settingsUpdated}, " +
                $"roles={rolesAdded}, players={PlayerDB.Players.Count()}"
            );
        }

        private static void HandleGiveInfluencer(string arguments)
        {
            long[] playerIds = arguments
                .Split(
                    new[] { ',', ' ' },
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries
                )
                .Select(value => long.TryParse(value, out long id) ? id : -1)
                .Where(id => id > 0)
                .Distinct()
                .ToArray();

            if (playerIds.Length == 0)
            {
                Usage("give-influencer <playerId1> [playerId2...]");
                return;
            }

            foreach (long playerId in playerIds)
            {
                FullPlayer? player = PlayerDB.Players.FindById(playerId);

                if (player == null)
                {
                    ServerLog.Warning($"Player {playerId} was not found.");
                    continue;
                }

                player.PlayerRoles ??= new List<PlayerRoles>();

                if (player.PlayerRoles.Contains(PlayerRoles.Influencer))
                {
                    ServerLog.Info($"Player {playerId} is already verified.");
                    continue;
                }

                player.PlayerRoles.Add(PlayerRoles.Influencer);

                if (PlayerDB.Players.Update(player))
                {
                    ServerLog.Success($"Influencer granted to player {playerId}.");
                }
                else
                {
                    ServerLog.Error($"Failed to update player {playerId}.");
                }
            }
        }

        private static void HandleGrantRoles(string arguments)
        {
            string[] values = arguments.Split(' ', 2);

            if (values.Length != 2 ||
                string.IsNullOrWhiteSpace(values[0]) ||
                string.IsNullOrWhiteSpace(values[1]))
            {
                Usage("grant-role <playerIds> <role1> [role2...]");
                return;
            }

            GrantRoles(values[0], values[1]);
        }

        private static void HandleAddRoomRole(string arguments)
        {
            string[] values = arguments.Split(
                ' ',
                3,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            if (values.Length != 3)
            {
                Usage(
                    "add-roomrole <playerId> <roomId> " +
                    "<Host|Moderator|CoOwner|TemporaryCoOwner|Creator>");
                return;
            }

            if (!TryParsePositiveLong(values[0], out long playerId))
            {
                ServerLog.Warning(
                    $"Invalid player ID '{values[0]}'.");
                return;
            }

            if (!TryParsePositiveLong(values[1], out long roomId))
            {
                ServerLog.Warning(
                    $"Invalid room ID '{values[1]}'.");
                return;
            }

            if (!Enum.TryParse(
                    values[2],
                    ignoreCase: true,
                    out RoomDBClasses.Role role) ||
                !Enum.IsDefined(typeof(RoomDBClasses.Role), role) ||
                role is RoomDBClasses.Role.None or RoomDBClasses.Role.Banned)
            {
                ServerLog.Warning(
                    $"Invalid room role '{values[2]}'. Valid roles: " +
                    "Host, Moderator, CoOwner, TemporaryCoOwner, Creator.");
                return;
            }

            var player = PlayerDB.Players.FindById(playerId);

            if (player == null)
            {
                ServerLog.Warning(
                    $"Player {playerId} was not found.");
                return;
            }

            RoomDBClasses.Room? room =
                RoomDB.Rooms.FindById(roomId);

            if (room == null)
            {
                ServerLog.Warning(
                    $"Room {roomId} was not found.");
                return;
            }

            room.Roles ??= new List<RoomDBClasses.Roles>();

            RoomDBClasses.Roles? existingRole =
                room.Roles.FirstOrDefault(entry =>
                    entry.AccountId == playerId);

            if (existingRole == null)
            {
                room.Roles.Add(new RoomDBClasses.Roles
                {
                    AccountId = playerId,
                    Role = role,
                    InvitedRole = RoomDBClasses.Role.None
                });
            }
            else
            {
                existingRole.Role = role;
                existingRole.InvitedRole =
                    RoomDBClasses.Role.None;
            }

            room.UgcVersion =
                Math.Max(1, room.UgcVersion + 1);

            if (!RoomDB.Rooms.Update(room))
            {
                ServerLog.Error(
                    $"Failed to give player {playerId} the {role} role " +
                    $"in room {roomId}.");
                return;
            }

            ServerLog.Success(
                $"Player {playerId} is now {role} in " +
                $"room {roomId} ({room.Name}).");
        }

        private static void HandleSetBio(string arguments)
        {
            if (!TrySplitPlayerAndValue(
                    arguments,
                    "set-bio <playerId> <text>",
                    out long playerId,
                    out string bio))
            {
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }

            player.Player!.Bio = bio;
            SavePlayer(player, $"Bio updated for player {playerId}.", notifyProfile: true);
        }

        private static void HandleResetUsernameChanges(string arguments)
        {
            string[] values = arguments.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries
            );

            if (values.Length is < 1 or > 2 ||
                !TryParsePositiveLong(values[0], out long playerId))
            {
                Usage("reset-usernamechanges <playerId> [amount]");
                return;
            }

            int amount = 3;

            if (values.Length == 2 &&
                (!int.TryParse(values[1], out amount) || amount < 0))
            {
                Usage("reset-usernamechanges <playerId> [amount]");
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }

            player.Player!.AvailableUsernameChanges = amount;
            SavePlayer(
                player,
                $"Username changes for player {playerId} set to {amount}."
            );
        }

        private static void HandleSetProfileImage(string arguments)
        {
            if (!TrySplitPlayerAndValue(
                    arguments,
                    "set-pfp <playerId> <filename-or-path>",
                    out long playerId,
                    out string sourceInput))
            {
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }

            string? sourcePath = ResolveProfileImageSourcePath(sourceInput);

            if (sourcePath == null)
            {
                ServerLog.Error($"Image file not found: {sourceInput}");
                return;
            }

            string copiedPath = CopyProfileImageToCustomPfps(sourcePath);
            player.Player!.ProfileImage = copiedPath;
            SavePlayer(player, $"Profile image set to {copiedPath}.", notifyProfile: true);
        }

        private static void HandleSetBannerImage(string arguments)
        {
            if (!TrySplitPlayerAndValue(
                    arguments,
                    "set-banner <playerId> <filename-or-path>",
                    out long playerId,
                    out string sourceInput))
            {
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
                return;

            string? sourcePath = ResolveProfileImageSourcePath(sourceInput);
            if (sourcePath == null)
            {
                ServerLog.Error($"Image file not found: {sourceInput}");
                return;
            }

            string copiedPath = CopyProfileImageToCustomPfps(sourcePath);
            player.Player!.BannerImage = copiedPath;
            SavePlayer(player, $"Banner image for player {playerId} set to {copiedPath}.", notifyProfile: true);
        }

        private static void HandleSetUsername(string arguments)
        {
            if (!TrySplitPlayerAndValue(
                    arguments,
                    "set-username <playerId> <name>",
                    out long playerId,
                    out string username))
            {
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }

            player.Player!.Username = username;
            SavePlayer(player, $"Username for player {playerId} set to '{username}'.", notifyProfile: true);
        }

        private static void HandleSetDisplayName(string arguments)
        {
            if (!TrySplitPlayerAndValue(
                    arguments,
                    "set-display <playerId> <name>",
                    out long playerId,
                    out string displayName))
            {
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }

            player.Player!.DisplayName = displayName;
            SavePlayer(
                player,
                $"Display name for player {playerId} set to '{displayName}'.",
                notifyProfile: true
            );
        }

        private static void HandleChangeLevel(string arguments)
        {
            if (!TryParsePlayerAndInt(
                    arguments,
                    "change-level <playerId> <level>",
                    out long playerId,
                    out int level))
            {
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }

            PlayerProgressionDTO? progression = PlayerDB.SetProgression(
                playerId,
                level,
                player.Player!.XP);

            if (progression == null)
            {
                ServerLog.Error(
                    $"Failed to update level for player {playerId}.");
                return;
            }

            ServerLog.Success(
                $"Level for player {playerId} set to {progression.Level}.");
        }

        private static void HandleChangeXp(string arguments, bool add)
        {
            string usage = add
                ? "add-xp <playerId> <xp>"
                : "change-xp <playerId> <xp>";

            if (!TryParsePlayerAndInt(
                    arguments,
                    usage,
                    out long playerId,
                    out int xp))
            {
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }

            int updatedXp = add
                ? Math.Max(0, player.Player!.XP + xp)
                : Math.Max(0, xp);

            PlayerProgressionDTO? progression = PlayerDB.SetProgression(
                playerId,
                player.Player!.Level,
                updatedXp);

            if (progression == null)
            {
                ServerLog.Error(
                    $"Failed to update XP for player {playerId}.");
                return;
            }

            ServerLog.Success(
                $"XP for player {playerId} is now {progression.XP}.");
        }

        private static void HandleBalance(string arguments, bool add)
        {
            string usage = add
                ? "add-balance <playerId> <amount>"
                : "set-balance <playerId> <amount>";

            if (!TryParsePlayerAndInt(
                    arguments,
                    usage,
                    out long playerId,
                    out int amount))
            {
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }

            player.Player!.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.Currencies ??= new List<PlayerCurrency>();

            PlayerCurrency? currency = player.Player.PlayerExtra.Currencies
                .FirstOrDefault(item =>
                    item.CurrencyType == CurrencyType.RecCenterTokens);

            if (currency == null)
            {
                currency = new PlayerCurrency
                {
                    CurrencyType = CurrencyType.RecCenterTokens,
                    BalanceType = BalanceType.NonPurchasedDefault,
                    Balance = Math.Max(0, amount)
                };

                player.Player.PlayerExtra.Currencies.Add(currency);
            }
            else
            {
                currency.Balance = add
                    ? Math.Max(0, currency.Balance + amount)
                    : Math.Max(0, amount);
            }

            SavePlayer(
                player,
                $"Token balance for player {playerId} is now {currency.Balance}."
            );
        }

        private static void HandleSetJunior(string arguments)
        {
            string[] values = arguments.Split(' ', 2);

            if (values.Length != 2 ||
                !TryParsePositiveLong(values[0], out long playerId) ||
                !bool.TryParse(values[1], out bool isJunior))
            {
                Usage("set-junior <playerId> <true|false>");
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }

            player.Player!.IsJunior = isJunior;
            SavePlayer(
                player,
                $"Junior status for player {playerId} set to {isJunior}."
            );
        }

        private static void HandleSetReputation(string arguments)
        {
            string[] values = arguments.Split(' ', 2);

            if (values.Length != 2 ||
                !TryParsePositiveLong(values[0], out long playerId) ||
                !double.TryParse(values[1], out double reputation))
            {
                Usage("set-rep <playerId> <value>");
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }

            player.Player!.Reputation ??= new Reputation();
            player.Player.Reputation.Noteriety = reputation;
            SavePlayer(
                player,
                $"Reputation for player {playerId} set to {reputation}."
            );
        }

        private static void HandleResetPassword(string arguments)
        {
            if (!TryParsePositiveLong(arguments, out long playerId))
            {
                Usage("reset-password <playerId>");
                return;
            }

            FullPlayer? player = PlayerDB.Players.FindById(playerId);

            if (player == null)
            {
                ServerLog.Warning($"Player {playerId} was not found.");
                return;
            }

            player.Password = null;
            SavePlayer(player, $"Password cleared for player {playerId}.");
        }

        private static void HandleSetPassword(string arguments)
        {
            if (!TrySplitPlayerAndValue(
                    arguments,
                    "set-password <playerId> <password>",
                    out long playerId,
                    out string password))
            {
                return;
            }

            if (password.Length < 4 ||
                password.Length > PasswordSecurity.MaxPasswordLength)
            {
                ServerLog.Warning("Password must be 4-256 characters.");
                return;
            }

            FullPlayer? player = PlayerDB.Players.FindById(playerId);
            if (player == null)
            {
                ServerLog.Warning($"Player {playerId} was not found.");
                return;
            }

            player.Password = PasswordSecurity.Hash(password);
            SavePlayer(player, $"Password set for player {playerId}.");
        }

        private static void HandleBanPlayer(string arguments)
        {
            string[] values = arguments.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries
            );

            if (values.Length is < 1 or > 2 ||
                !TryParsePositiveLong(values[0], out long playerId))
            {
                Usage("ban-player <playerId> [hours]");
                return;
            }

            int durationHours = 24;

            if (values.Length == 2 &&
                (!int.TryParse(values[1], out durationHours) ||
                 durationHours < 1))
            {
                Usage("ban-player <playerId> [hours]");
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }
            if (durationHours > int.MaxValue / 3600)
            {
                ServerLog.Warning("Ban duration exceeds the 32-bit duration limit.");
                return;
            }

            PlayerDB.BanPlayer(
                playerId,
                durationHours * 3600,
                "Banned via console command"
            );
            ServerLog.Success($"Player {playerId} banned from gameplay for {durationHours} hour(s).");
        }

        private static void HandleUnbanPlayer(string arguments)
        {
            if (!TryParsePositiveLong(arguments, out long playerId))
            {
                Usage("unban-player <playerId>");
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }

            PlayerDB.UnbanPlayer(playerId);
            ServerLog.Success($"Player {playerId} unbanned from gameplay.");
        }

        private static void HandleSetRoomState(string arguments)
        {
            string[] values = arguments.Split(' ', 2);

            if (values.Length != 2 ||
                !TryParsePositiveLong(values[0], out long roomId) ||
                !Enum.TryParse(
                    values[1],
                    ignoreCase: true,
                    out RoomDBClasses.RoomState state))
            {
                Usage("set-room-state <roomId> <state>");
                return;
            }

            RoomDBClasses.Room? room = RoomDB.Rooms.FindById(roomId);

            if (room == null)
            {
                ServerLog.Warning($"Room {roomId} was not found.");
                return;
            }

            room.State = state;

            if (RoomDB.Rooms.Update(room))
            {
                ServerLog.Success($"Room {roomId} state set to {state}.");
            }
            else
            {
                ServerLog.Error($"Failed to update room {roomId}.");
            }
        }

        private static void HandleSetRoomAccess(string arguments)
        {
            string[] values = arguments.Split(' ', 2);

            if (values.Length != 2 ||
                !TryParsePositiveLong(values[0], out long roomId) ||
                !Enum.TryParse(
                    values[1],
                    ignoreCase: true,
                    out RoomDBClasses.RoomAccessibility accessibility))
            {
                Usage("set-room-access <roomId> <private|public|unlisted>");
                return;
            }

            RoomDBClasses.Room? room = RoomDB.Rooms.FindById(roomId);

            if (room == null)
            {
                ServerLog.Warning($"Room {roomId} was not found.");
                return;
            }

            room.Accessibility = accessibility;

            if (RoomDB.Rooms.Update(room))
            {
                ServerLog.Success(
                    $"Room {roomId} access set to {accessibility}."
                );
            }
            else
            {
                ServerLog.Error($"Failed to update room {roomId}.");
            }
        }

        private enum PlayerDataToClear
        {
            Visits,
            Favorites,
            Cheers
        }

        private static void HandleClearPlayerData(
            string arguments,
            PlayerDataToClear dataToClear)
        {
            if (!TryParsePositiveLong(arguments, out long playerId))
            {
                Usage(dataToClear switch
                {
                    PlayerDataToClear.Visits => "clear-visits <playerId>",
                    PlayerDataToClear.Favorites => "clear-favorites <playerId>",
                    _ => "clear-cheers <playerId>"
                });

                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }

            switch (dataToClear)
            {
                case PlayerDataToClear.Visits:
                    player.Player!.VisitedRooms ??= new List<long>();
                    player.Player.VisitedRooms.Clear();
                    player.Player.PlayerExtra ??= new PlayerExtra();
                    player.Player.PlayerExtra.RoomVisits ??= new List<RoomVisit>();
                    player.Player.PlayerExtra.RoomVisits.Clear();
                    break;

                case PlayerDataToClear.Favorites:
                    player.Player!.FavoritedRooms ??= new List<long>();
                    player.Player.FavoritedRooms.Clear();
                    break;

                case PlayerDataToClear.Cheers:
                    player.Player!.CheeredRooms ??= new List<long>();
                    player.Player.CheeredRooms.Clear();
                    player.Player.ReceivedCheers ??=
                        new List<PlayerCheerRecord>();
                    player.Player.ReceivedCheers.Clear();
                    player.Player.Reputation ??= new Reputation();
                    player.Player.Reputation.IsCheerful = false;
                    player.Player.Reputation.SelectedCheer =
                        CheerCategory.General;
                    player.Player.Reputation.CheerGeneral = 0;
                    player.Player.Reputation.CheerHelpful = 0;
                    player.Player.Reputation.CheerSportsman = 0;
                    player.Player.Reputation.CheerGreatHost = 0;
                    player.Player.Reputation.CheerCreative = 0;
                    NotificationDB.DeletePlayerCheerMessages(playerId);
                    break;
            }

            SavePlayer(
                player,
                $"Cleared {dataToClear.ToString().ToLowerInvariant()} " +
                $"for player {playerId}."
            );
        }

        private static void HandleResetProfile(string arguments)
        {
            if (!TryParsePositiveLong(arguments, out long playerId))
            {
                Usage("reset-profile <playerId>");
                return;
            }

            if (!TryGetPlayerWithData(playerId, out FullPlayer player))
            {
                return;
            }

            player.Player!.PlayerExtra ??= new PlayerExtra();
            player.Player.PlayerExtra.AvatarItems ??= new List<string>();
            player.Player.PlayerExtra.SavedAvatars ??= new List<SavedOutfit>();

            player.Player.Bio = string.Empty;
            player.Player.ProfileImage = "DefaultPFP.png";
            player.Player.BannerImage = null;
            player.Player.DisplayName = player.Player.Username;
            player.Player.PlayerExtra.Avatar = new Avatar();
            player.Player.PlayerExtra.AvatarItems.Clear();
            player.Player.PlayerExtra.SavedAvatars.Clear();

            SavePlayer(player, $"Profile reset for player {playerId}.", notifyProfile: true);
        }

        private static void HandleCreateAccount(string arguments)
        {
            string[] values = arguments.Split(' ', 5);

            if (values.Length != 5 ||
                string.IsNullOrWhiteSpace(values[0]) ||
                !TryParsePositiveLong(values[1], out long accountId) ||
                !bool.TryParse(values[2], out bool isJunior) ||
                !Enum.TryParse(
                    values[3],
                    ignoreCase: true,
                    out Platforms platform) ||
                !ulong.TryParse(values[4], out ulong platformId))
            {
                Usage(
                    "create-account <username> <accountId> " +
                    "<true|false> <platform> <platformId>"
                );
                return;
            }

            if (PlayerDB.Players.FindById(accountId) != null)
            {
                ServerLog.Error($"Account {accountId} already exists.");
                return;
            }

            FullPlayer created = PlayerDB.CreateAccount(
                platform,
                platformId,
                isJunior,
                accountId
            );

            created.Player ??= new Player();
            created.Player.Username = values[0].Trim();
            created.Player.DisplayName = values[0].Trim();
            created.Player.PlayerExtra ??= new PlayerExtra();
            created.Player.PlayerExtra.Settings ??= new List<Setting>();

            SavePlayer(
                created,
                $"Created account {accountId} as '{created.Player.Username}'."
            );
        }

        private static void HandleChangeAccountId(string arguments)
        {
            string[] values = arguments.Split(' ', 2);

            if (values.Length != 2 ||
                !TryParsePositiveLong(values[0], out long currentAccountId) ||
                !TryParsePositiveLong(values[1], out long newAccountId))
            {
                Usage("change-accid <currentAccountId> <newAccountId>");
                return;
            }

            if (currentAccountId == newAccountId)
            {
                ServerLog.Warning("The current and new account IDs are equal.");
                return;
            }

            if (PlayerDB.ChangeAccountId(currentAccountId, newAccountId))
            {
                ServerLog.Success(
                    $"Account ID changed from {currentAccountId} " +
                    $"to {newAccountId}."
                );
            }
            else
            {
                ServerLog.Error(
                    $"Could not change account ID {currentAccountId} " +
                    $"to {newAccountId}."
                );
            }
        }

        public static async Task SetMaintenanceCountdownAsync(int minutes)
        {
            if (minutes is < 0 or > 10_080)
                throw new ArgumentOutOfRangeException(
                    nameof(minutes),
                    "Maintenance must be between 0 and 10,080 minutes.");

            string configPath = Path.Combine(dataDir, "APIS", "ConfigV2.json");

            if (!File.Exists(configPath))
                throw new FileNotFoundException(
                    $"ConfigV2.json was not found at '{configPath}'.",
                    configPath);

            string json = File.ReadAllText(configPath);
            Dictionary<string, object?>? config =
                JsonSerializer.Deserialize<Dictionary<string, object?>>(json);

            if (config == null)
                throw new InvalidDataException("ConfigV2.json could not be parsed.");

            config.Remove("ServerMaintainence");
            config["ServerMaintenance"] = new
            {
                StartsInMinutes = minutes
            };

            File.WriteAllText(
                configPath,
                JsonSerializer.Serialize(
                    config,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );

            await NotiController.BroadcastServerMaintenanceAsync(minutes);
        }

        private static async Task HandleMaintenanceAsync(string arguments)
        {
            if (!int.TryParse(arguments, out int minutes) || minutes < 0)
            {
                Usage("maintenance <minutes>");
                return;
            }

            try
            {
                await SetMaintenanceCountdownAsync(minutes);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or
                FileNotFoundException or InvalidDataException or JsonException or IOException)
            {
                ServerLog.Error(ex.Message);
                return;
            }

            ServerLog.Success(
                $"Maintenance countdown set to {minutes} minute(s)."
            );
        }

        private static bool TrySplitPlayerAndValue(
            string arguments,
            string usage,
            out long playerId,
            out string value)
        {
            playerId = 0;
            value = string.Empty;

            string[] values = arguments.Split(' ', 2);

            if (values.Length != 2 ||
                !TryParsePositiveLong(values[0], out playerId) ||
                string.IsNullOrWhiteSpace(values[1]))
            {
                Usage(usage);
                return false;
            }

            value = values[1].Trim();
            return true;
        }

        private static bool TryParsePlayerAndInt(
            string arguments,
            string usage,
            out long playerId,
            out int value)
        {
            playerId = 0;
            value = 0;

            string[] values = arguments.Split(' ', 2);

            if (values.Length != 2 ||
                !TryParsePositiveLong(values[0], out playerId) ||
                !int.TryParse(values[1], out value))
            {
                Usage(usage);
                return false;
            }

            return true;
        }

        private static bool TryParsePositiveLong(
            string value,
            out long result)
        {
            return long.TryParse(value.Trim(), out result) && result > 0;
        }

        private static bool TryGetPlayerWithData(
            long playerId,
            out FullPlayer player)
        {
            FullPlayer? found = PlayerDB.Players.FindById(playerId);

            if (found?.Player == null)
            {
                ServerLog.Warning($"Player {playerId} was not found.");
                player = null!;
                return false;
            }

            player = found;
            return true;
        }

        private static void SavePlayer(
            FullPlayer player,
            string successMessage,
            bool notifyProfile = false)
        {
            if (PlayerDB.Players.Update(player))
            {
                ServerLog.Success(successMessage);
                if (notifyProfile)
                    _ = NotiController.NotifyPlayerProfileUpdatedAsync(player.PlayerId);
            }
            else
            {
                ServerLog.Error($"Failed to save player {player.PlayerId}.");
            }
        }

        private static void Usage(string usage)
        {
            ServerLog.Warning($"Usage: {usage}");
        }

        private static void TryLogAdminCommand(
            string command,
            string arguments)
        {
            try
            {
                DiscordLogger.Log(
                    "🛠️ **Admin Command Executed**\n" +
                    $"**Command:** `{command}`\n" +
                    $"**Args:** `{(string.IsNullOrWhiteSpace(arguments) ? "(none)" : arguments)}`"
                );
            }
            catch (Exception ex)
            {
                ServerLog.Warning(
                    $"Discord command log failed: {ex.Message}"
                );
            }
        }

        private static void TryLogAdminError(
            string command,
            string arguments,
            Exception exception)
        {
            try
            {
                DiscordLogger.Log(
                    "❌ **Admin Command Error**\n" +
                    $"**Command:** `{command}`\n" +
                    $"**Args:** `{(string.IsNullOrWhiteSpace(arguments) ? "(none)" : arguments)}`\n" +
                    $"**Error:** `{exception.GetType().Name}: {exception.Message}`"
                );
            }
            catch
            {

            }
        }

        private static string? ResolveProfileImageSourcePath(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                return null;
            }

            if (Path.IsPathRooted(inputPath) && File.Exists(inputPath))
            {
                return Path.GetFullPath(inputPath);
            }

            string[] candidatePaths =
            {
                inputPath,
                Path.Combine(Environment.CurrentDirectory, inputPath),
                Path.Combine(dataDir, inputPath),
                Path.Combine(dataDir, "Images", inputPath),
                Path.Combine(dataDir, "Images", "PlayerImages", inputPath),
                Path.Combine(dataDir, "Images", "CustomPFPS", inputPath)
            };

            return candidatePaths
                .Select(Path.GetFullPath)
                .FirstOrDefault(File.Exists);
        }

        private static string CopyProfileImageToCustomPfps(string sourcePath)
        {
            string extension = Path.GetExtension(sourcePath);

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".png";
            }

            string targetFolder = Path.Combine(
                dataDir,
                "Images",
                "CustomPFPS"
            );

            Directory.CreateDirectory(targetFolder);

            string assetName = $"{Guid.NewGuid():D}{extension}";
            string targetPath = Path.Combine(targetFolder, assetName);

            File.Copy(sourcePath, targetPath, overwrite: true);

            return Path.Combine("CustomPFPS", assetName)
                .Replace('\\', '/');
        }

        private static bool ForceRRPlusForPlayer(long playerId)
        {
            if (playerId <= 0 || !PlayerDB.SetRRPlus(playerId, true))
            {
                return false;
            }

            FullPlayer? player = PlayerDB.Players.FindById(playerId);

            if (player == null)
            {
                return false;
            }

            player.PlayerRoles ??= new List<PlayerRoles>();

            if (player.PlayerRoles.Contains(PlayerRoles.RRPlus))
            {
                return true;
            }

            player.PlayerRoles.Add(PlayerRoles.RRPlus);
            return PlayerDB.Players.Update(player);
        }

        private static int ForceRRPlusRoleForAllPlayers()
        {
            int rolesAdded = 0;

            foreach (FullPlayer player in PlayerDB.Players.FindAll())
            {
                player.PlayerRoles ??= new List<PlayerRoles>();

                if (player.PlayerRoles.Contains(PlayerRoles.RRPlus))
                {
                    continue;
                }

                player.PlayerRoles.Add(PlayerRoles.RRPlus);

                if (PlayerDB.Players.Update(player))
                {
                    rolesAdded++;
                }
            }

            return rolesAdded;
        }

        private static void GrantRoles(
            string playerIdsText,
            string rolesText)
        {
            List<long> playerIds = ParsePlayerIds(playerIdsText);

            if (playerIds.Count == 0)
            {
                ServerLog.Warning("No valid player IDs were supplied.");
                return;
            }

            string[] roleNames = rolesText.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries
            );

            if (roleNames.Length == 0)
            {
                ServerLog.Warning("No roles were supplied.");
                return;
            }

            foreach (long playerId in playerIds)
            {
                FullPlayer? player = PlayerDB.Players.FindById(playerId);

                if (player == null)
                {
                    ServerLog.Warning($"Player {playerId} was not found.");
                    continue;
                }

                player.PlayerRoles ??= new List<PlayerRoles>();
                var addedRoles = new List<PlayerRoles>();

                foreach (string roleName in roleNames)
                {
                    if (!RoleUtils.TryParseRole(roleName, out PlayerRoles role))
                    {
                        ServerLog.Warning(
                            $"Unknown role '{roleName}' for player {playerId}."
                        );
                        continue;
                    }

                    if (player.PlayerRoles.Contains(role))
                    {
                        continue;
                    }

                    player.PlayerRoles.Add(role);
                    addedRoles.Add(role);
                }

                if (addedRoles.Contains(PlayerRoles.Developer))
                {
                    PlayerDB.GrantDeveloperCheerAccess(
                        player,
                        selectDeveloperBadge: true);
                }

                if (addedRoles.Count == 0)
                {
                    ServerLog.Info(
                        $"Player {playerId} already has the requested roles."
                    );
                    continue;
                }

                if (PlayerDB.Players.Update(player))
                {
                    ServerLog.Success(
                        $"Added {string.Join(", ", addedRoles)} " +
                        $"to player {playerId}."
                    );
                }
                else
                {
                    ServerLog.Error($"Failed to update player {playerId}.");
                }
            }
        }

        private static List<long> ParsePlayerIds(string playerIdsText)
        {
            return playerIdsText
                .Split(
                    new[] { ',', ' ' },
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries
                )
                .Select(value => long.TryParse(value, out long id) ? id : -1)
                .Where(id => id > 0)
                .Distinct()
                .ToList();
        }
    }
}
