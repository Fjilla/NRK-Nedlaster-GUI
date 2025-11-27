using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace NRKLastNed.Services
{
    public class AppUpdateService
    {
        private const string RepoOwner = "Emigrante";
        private const string RepoName = "NRK-Nedlaster-GUI";

        public class AppUpdateInfo
        {
            public bool IsNewVersionAvailable { get; set; }
            public string LatestVersion { get; set; }
            public string CurrentVersion { get; set; }
            public string DownloadUrl { get; set; }
            public string ReleaseNotes { get; set; }
            public string Title { get; set; }
            public string FileName { get; set; }
        }

        public async Task<AppUpdateInfo> CheckForAppUpdatesAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("NRK-Nedlaster-GUI");

                    string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                    var response = await client.GetStringAsync(url);

                    using (JsonDocument doc = JsonDocument.Parse(response))
                    {
                        var root = doc.RootElement;
                        string tagName = root.GetProperty("tag_name").GetString();
                        string body = root.GetProperty("body").GetString();
                        string name = root.GetProperty("name").GetString();

                        string downloadUrl = "";
                        string fileName = "";

                        if (root.TryGetProperty("assets", out var assets))
                        {
                            foreach (var asset in assets.EnumerateArray())
                            {
                                string assetName = asset.GetProperty("name").GetString();
                                if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                                    fileName = assetName;
                                    break;
                                }
                            }
                        }

                        // Hent lokal versjon
                        Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

                        // Parse GitHub tag (f.eks "v1.03")
                        string cleanTag = tagName.TrimStart('v', 'V');

                        // SPESIALHÅNDTERING FOR FORMATET v1.03, v1.04 osv.
                        // .NET vil tolke "1.03" som "1.3" (Major 1, Minor 3).
                        // Men din AssemblyVersion er sannsynligvis "1.0.3" (Major 1, Minor 0, Build 3).
                        // Vi konverterer derfor "1.03" til "1.0.3" manuelt her hvis den starter med 0.
                        var parts = cleanTag.Split('.');
                        if (parts.Length == 2 && parts[1].StartsWith("0") && parts[1].Length >= 2)
                        {
                            // Eksempel: "1.03" -> parts[0]="1", parts[1]="03"
                            if (int.TryParse(parts[1], out int minorBuild))
                            {
                                // Gjør om til formatet 1.0.3
                                cleanTag = $"{parts[0]}.0.{minorBuild}";
                            }
                        }

                        // Fallback: Hvis taggen mangler punktum helt (f.eks "v1")
                        if (cleanTag.Split('.').Length < 2) cleanTag += ".0";
                        if (cleanTag.Split('.').Length < 3) cleanTag += ".0";

                        if (Version.TryParse(cleanTag, out Version latestVersion))
                        {
                            bool updateAvailable = latestVersion > currentVersion;
                            return new AppUpdateInfo
                            {
                                IsNewVersionAvailable = updateAvailable,
                                LatestVersion = tagName, // Vi viser original tag (v1.03) til brukeren
                                CurrentVersion = $"v{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}",
                                DownloadUrl = downloadUrl,
                                ReleaseNotes = body,
                                Title = name,
                                FileName = fileName
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Feil ved sjekk av oppdatering: " + ex.Message);
            }

            return new AppUpdateInfo { IsNewVersionAvailable = false };
        }

        public async Task PerformAppUpdateAsync(AppUpdateInfo info)
        {
            if (string.IsNullOrEmpty(info.DownloadUrl))
            {
                MessageBox.Show("Fant ingen nedlastbar installasjonsfil i denne utgivelsen.", "Feil", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string tempPath = Path.GetTempPath();
            string installerPath = Path.Combine(tempPath, info.FileName ?? "NRKLastNed_Setup.exe");

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("NRK-Nedlaster-GUI");
                    var data = await client.GetByteArrayAsync(info.DownloadUrl);
                    await File.WriteAllBytesAsync(installerPath, data);
                }

                string currentDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                if (currentDir.EndsWith("\\")) currentDir = currentDir.Substring(0, currentDir.Length - 1);

                MessageBox.Show("Oppdatering lastet ned.\n\nProgrammet lukkes nå for å starte installasjonen.",
                                "Oppdatering", MessageBoxButton.OK, MessageBoxImage.Information);

                // Tving installasjon til nåværende mappe
                string arguments = $"/DIR=\"{currentDir}\"";

                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                    Arguments = arguments
                });

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Feil ved start av oppdatering: {ex.Message}", "Feil", MessageBoxButton.OK, MessageBoxImage.Error);

                if (File.Exists(installerPath))
                {
                    try { File.Delete(installerPath); } catch { }
                }
            }
        }

        public static void ShowReleaseNotesIfJustUpdated()
        {
        }
    }
}