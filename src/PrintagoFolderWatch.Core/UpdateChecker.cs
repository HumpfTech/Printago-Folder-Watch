using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PrintagoFolderWatch.Core
{
    /// <summary>
    /// Cross-platform update checker that works on Windows and macOS.
    /// Downloads appropriate installer (.exe) or zip based on platform.
    /// </summary>
    public class CrossPlatformUpdateChecker
    {
        private const string GITHUB_RELEASES_URL = "https://api.github.com/repos/HumpfTech/Printago-Folder-Watch/releases/latest";

        private readonly string _currentVersion;
        private readonly HttpClient _httpClient;

        public event Action<string, string>? OnUpdateAvailable;
        public event Action<string>? OnUpdateProgress;
        public event Action<string, string?>? OnUpdateReady; // version, downloadPath

        public CrossPlatformUpdateChecker(string currentVersion)
        {
            _currentVersion = currentVersion;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PrintagoFolderWatch");
        }

        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(GITHUB_RELEASES_URL);
                var release = JsonConvert.DeserializeAnonymousType(response, new
                {
                    tag_name = "",
                    html_url = "",
                    name = "",
                    body = "",
                    assets = new[] { new { browser_download_url = "", name = "" } }
                });

                if (release == null || string.IsNullOrEmpty(release.tag_name))
                    return null;

                var latestVersion = release.tag_name.TrimStart('v');

                if (!IsNewerVersion(latestVersion, _currentVersion))
                    return null;

                // Find the appropriate download based on platform
                string? downloadUrl = FindDownloadUrl(release.assets);

                var info = new UpdateInfo
                {
                    CurrentVersion = _currentVersion,
                    NewVersion = latestVersion,
                    ReleaseUrl = release.html_url,
                    DownloadUrl = downloadUrl,
                    ReleaseNotes = release.body
                };

                OnUpdateAvailable?.Invoke(latestVersion, $"v{latestVersion} is available");
                return info;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
                return null;
            }
        }

        private string? FindDownloadUrl(dynamic[]? assets)
        {
            if (assets == null) return null;

            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isMacArm = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                           RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            bool isMacIntel = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                             RuntimeInformation.ProcessArchitecture == Architecture.X64;

            foreach (var asset in assets)
            {
                string name = asset.name;
                string url = asset.browser_download_url;

                // Windows: prefer installer
                if (isWindows)
                {
                    if (name.Contains("CrossPlatform") && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        return url;
                }
                // Mac ARM (M1/M2/M3)
                else if (isMacArm)
                {
                    if (name.Contains("arm64") && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        return url;
                }
                // Mac Intel
                else if (isMacIntel)
                {
                    if (name.Contains("x64") && name.Contains("macOS") && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        return url;
                }
            }

            // Fallback: any exe for Windows, any mac zip for Mac
            foreach (var asset in assets)
            {
                string name = asset.name;
                string url = asset.browser_download_url;

                if (isWindows && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return url;
                if ((isMacArm || isMacIntel) && name.Contains("macOS") && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    return url;
            }

            return null;
        }

        public async Task<string?> DownloadUpdateAsync(string downloadUrl, string version)
        {
            try
            {
                OnUpdateProgress?.Invoke("Starting download...");

                var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                var tempPath = Path.Combine(Path.GetTempPath(), fileName);

                // Use streaming download with progress
                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var totalMB = totalBytes > 0 ? totalBytes / 1024.0 / 1024.0 : 0;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;
                var lastProgress = DateTime.Now;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    // Report progress every 500ms
                    if ((DateTime.Now - lastProgress).TotalMilliseconds > 500)
                    {
                        var readMB = totalRead / 1024.0 / 1024.0;
                        if (totalBytes > 0)
                        {
                            var percent = (int)((double)totalRead / totalBytes * 100);
                            OnUpdateProgress?.Invoke($"Downloading... {readMB:F1} / {totalMB:F1} MB ({percent}%)");
                        }
                        else
                        {
                            OnUpdateProgress?.Invoke($"Downloading... {readMB:F1} MB");
                        }
                        lastProgress = DateTime.Now;
                    }
                }

                OnUpdateProgress?.Invoke("Download complete!");
                OnUpdateReady?.Invoke(version, tempPath);

                return tempPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Download failed: {ex.Message}");
                OnUpdateProgress?.Invoke($"Download failed: {ex.Message}");
                return null;
            }
        }

        public void LaunchInstaller(string installerPath)
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            if (isWindows)
            {
                // Windows: run the installer exe
                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                });
            }
            else
            {
                // macOS: extract zip and open containing folder
                var extractPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads",
                    "PrintagoFolderWatch-Update"
                );

                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                ZipFile.ExtractToDirectory(installerPath, extractPath);

                // Open the folder in Finder
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = extractPath,
                    UseShellExecute = true
                });
            }
        }

        private bool IsNewerVersion(string latest, string current)
        {
            try
            {
                var latestParts = latest.Split('.');
                var currentParts = current.Split('.');

                for (int i = 0; i < Math.Max(latestParts.Length, currentParts.Length); i++)
                {
                    int latestNum = i < latestParts.Length ? int.Parse(latestParts[i]) : 0;
                    int currentNum = i < currentParts.Length ? int.Parse(currentParts[i]) : 0;

                    if (latestNum > currentNum)
                        return true;
                    if (latestNum < currentNum)
                        return false;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    public class UpdateInfo
    {
        public string CurrentVersion { get; set; } = "";
        public string NewVersion { get; set; } = "";
        public string ReleaseUrl { get; set; } = "";
        public string? DownloadUrl { get; set; }
        public string? ReleaseNotes { get; set; }
    }
}
