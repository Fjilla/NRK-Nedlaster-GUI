using NRKLastNed.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace NRKLastNed.Services
{
    public class FfmpegUpdateService
    {
        private const string RepoUrl = "https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/latest";
        private readonly string _toolsPath;

        public class FfmpegUpdateInfo
        {
            public bool IsNewVersionAvailable { get; set; }
            public string LatestVersion { get; set; }
            public string DownloadUrl { get; set; }
            public DateTime PublishedAt { get; set; }
        }

        public FfmpegUpdateService()
        {
            _toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
        }

        public async Task<string> GetInstalledVersionAsync()
        {
            string exePath = Path.Combine(_toolsPath, "ffmpeg.exe");
            if (!File.Exists(exePath)) return "Ikke installert";

            try
            {
                // Vi bruker fildato som versjonsindikator for builds
                var fileInfo = new FileInfo(exePath);
                return fileInfo.LastWriteTime.ToString("yyyy-MM-dd");
            }
            catch
            {
                return "Ukjent";
            }
        }

        public async Task<FfmpegUpdateInfo> CheckForUpdatesAsync()
        {
            var info = new FfmpegUpdateInfo { IsNewVersionAvailable = false, LatestVersion = "Ukjent" };

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("NRK-Nedlaster-GUI");
                    var response = await client.GetStringAsync(RepoUrl);

                    using (JsonDocument doc = JsonDocument.Parse(response))
                    {
                        var root = doc.RootElement;
                        string published = root.GetProperty("published_at").GetString();
                        DateTime pubDate = DateTime.Parse(published);

                        info.LatestVersion = pubDate.ToString("yyyy-MM-dd");
                        info.PublishedAt = pubDate;

                        // Finn download URL
                        if (root.TryGetProperty("assets", out var assets))
                        {
                            foreach (var asset in assets.EnumerateArray())
                            {
                                string name = asset.GetProperty("name").GetString();
                                if (name.Contains("win64-gpl.zip") && !name.Contains("shared"))
                                {
                                    info.DownloadUrl = asset.GetProperty("browser_download_url").GetString();
                                    break;
                                }
                            }
                        }

                        // SJEKK: Er filen på disk eldre enn utgivelsen på GitHub?
                        string localPath = Path.Combine(_toolsPath, "ffmpeg.exe");
                        if (!File.Exists(localPath))
                        {
                            info.IsNewVersionAvailable = true;
                        }
                        else
                        {
                            DateTime localDate = File.GetLastWriteTimeUtc(localPath);
                            // Hvis GitHub-versjonen er mer enn 24 timer nyere enn den lokale filen
                            if (pubDate > localDate.AddHours(24))
                            {
                                info.IsNewVersionAvailable = true;
                            }
                        }
                    }
                }
            }
            catch
            {
                info.LatestVersion = "Feil ved sjekk";
            }

            return info;
        }

        public async Task UpdateFfmpegAsync(FfmpegUpdateInfo info, IProgress<string> progress)
        {
            if (string.IsNullOrEmpty(info.DownloadUrl)) return;

            string zipPath = Path.Combine(Path.GetTempPath(), "ffmpeg_update.zip");

            try
            {
                progress.Report("Laster ned...");
                using (var client = new HttpClient())
                {
                    var data = await client.GetByteArrayAsync(info.DownloadUrl);
                    await File.WriteAllBytesAsync(zipPath, data);
                }

                progress.Report("Pakker ut...");
                if (!Directory.Exists(_toolsPath)) Directory.CreateDirectory(_toolsPath);

                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith("bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            string dest = Path.Combine(_toolsPath, "ffmpeg.exe");
                            if (File.Exists(dest)) File.Delete(dest);
                            entry.ExtractToFile(dest, true);

                            // Sett fil-dato til "nå" eller published date for enklere sammenligning senere
                            File.SetLastWriteTimeUtc(dest, info.PublishedAt);
                        }
                        else if (entry.FullName.EndsWith("bin/ffprobe.exe", StringComparison.OrdinalIgnoreCase) ||
                                 entry.Name.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            string dest = Path.Combine(_toolsPath, "ffprobe.exe");
                            if (File.Exists(dest)) File.Delete(dest);
                            entry.ExtractToFile(dest, true);
                            File.SetLastWriteTimeUtc(dest, info.PublishedAt);
                        }
                    }
                }
                progress.Report("Ferdig!");
            }
            finally
            {
                if (File.Exists(zipPath)) try { File.Delete(zipPath); } catch { }
            }
        }
    }
}