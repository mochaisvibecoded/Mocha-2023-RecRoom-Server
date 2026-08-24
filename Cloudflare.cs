using System;
using System.Diagnostics;

namespace Mocha2023
{
    public class Cloudflare
    {
        public static void StartCloudflared()
        {
            try
            {
                const string cf = "/home/container/cloudflared";

                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    Process.Start("chmod", $"+x {cf}").WaitForExit();
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = cf,
                    Arguments = "tunnel --config /home/container/.cloudflared/config.yml run",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                Process process = new Process { StartInfo = startInfo };
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                        Console.WriteLine($"[Cloudflared] {e.Data}");
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                        Console.WriteLine($"[Cloudflared] {e.Data}");
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                Console.WriteLine($"[Cloudflared] Process started (pid {process.Id}). Watching for connection status...");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Cloudflared] Failed to start: {e.Message}");
            }
        }
    }
}
