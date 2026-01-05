using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace NRKLastNed.Services
{
    public class UpdateService
    {
        private readonly string _toolsPath;
        private readonly string _ytDlpPath;
        private const string RepoUrl = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";

        public class ToolUpdateInfo
        {
            public bool IsNewVersionAvailable { get; set; }
            public string LatestVersion { get; set; }
            public string CurrentVersion { get; set; }
            public string DownloadUrl { get; set; } // NY: Lagrer URL for nedlasting
        }

        public UpdateService()
        {
            _toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            _ytDlpPath = Path.Combine(_toolsPath, "yt-dlp.exe");
        }

        public async Task<string> GetYtDlpVersionAsync()
        {
            if (!File.Exists(_ytDlpPath)) return "Ikke installert";

            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    return output.Trim();
                }
            }
            catch
            {
                return "Ukjent";
            }
        }

        public async Task<ToolUpdateInfo> CheckForYtDlpUpdateAsync()
        {
            string currentVer = await GetYtDlpVersionAsync();
            var info = new ToolUpdateInfo { CurrentVersion = currentVer, IsNewVersionAvailable = false };

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("NRK-Nedlaster-GUI");
                    var response = await client.GetStringAsync(RepoUrl);

                    using (JsonDocument doc = JsonDocument.Parse(response))
                    {
                        var root = doc.RootElement;
                        info.LatestVersion = root.GetProperty("tag_name").GetString();

                        // Finn nedlastings-URL for yt-dlp.exe
                        if (root.TryGetProperty("assets", out var assets))
                        {
                            foreach (var asset in assets.EnumerateArray())
                            {
                                string name = asset.GetProperty("name").GetString();
                                if (name == "yt-dlp.exe")
                                {
                                    info.DownloadUrl = asset.GetProperty("browser_download_url").GetString();
                                    break;
                                }
                            }
                        }

                        if (currentVer == "Ikke installert" || currentVer == "Ukjent")
                        {
                            info.IsNewVersionAvailable = true;
                        }
                        else
                        {
                            // Sjekk om versjonene er ulike
                            info.IsNewVersionAvailable = !string.Equals(currentVer, info.LatestVersion, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch
            {
                info.LatestVersion = "Kunne ikke sjekke";
            }

            return info;
        }

        // ENDRET: Tar nå imot info-objektet for å vite URL
        public async Task<string> UpdateYtDlpAsync(ToolUpdateInfo info = null)
        {
            // Hvis vi mangler info eller URL, prøv den gamle metoden (hvis filen finnes)
            if (info == null || string.IsNullOrEmpty(info.DownloadUrl))
            {
                if (!File.Exists(_ytDlpPath)) return "Mangler nedlastings-URL og filen finnes ikke.";

                // Fallback til innebygd update hvis filen finnes
                return await RunInternalUpdate();
            }

            // LAST NED FRA GITHUB (Fungerer både for oppdatering og ny-installasjon)
            try
            {
                if (!Directory.Exists(_toolsPath)) Directory.CreateDirectory(_toolsPath);

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("NRK-Nedlaster-GUI");
                    var data = await client.GetByteArrayAsync(info.DownloadUrl);

                    // Skriv til disk (overskriver eksisterende)
                    await File.WriteAllBytesAsync(_ytDlpPath, data);
                }

                return "yt-dlp er lastet ned og oppdatert!";
            }
            catch (Exception ex)
            {
                return $"Feil under nedlasting: {ex.Message}";
            }
        }

        private async Task<string> RunInternalUpdate()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = "--update",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    return output + Environment.NewLine + error;
                }
            }
            catch (Exception ex)
            {
                return $"Feil: {ex.Message}";
            }
        }
    }
}