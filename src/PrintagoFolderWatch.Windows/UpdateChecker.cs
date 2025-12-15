using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace PrintagoFolderWatch.Windows
{
    public class UpdateChecker
    {
        private const string GITHUB_RELEASES_URL = "https://api.github.com/repos/HumpfTech/Printago-Folder-Watch/releases/latest";
        private const string CURRENT_VERSION = "2.6"; // Update this with each release

        private readonly HttpClient httpClient;

        // Event for balloon notification
        public event Action<string, string>? OnUpdateAvailable;

        public UpdateChecker()
        {
            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "PrintagoFolderWatch");
        }

        public async Task CheckForUpdatesAsync(bool silentIfNoUpdate = true)
        {
            try
            {
                var response = await httpClient.GetStringAsync(GITHUB_RELEASES_URL);
                var release = JsonConvert.DeserializeAnonymousType(response, new
                {
                    tag_name = "",
                    html_url = "",
                    name = "",
                    body = "",
                    assets = new[] { new { browser_download_url = "", name = "" } }
                });

                if (release == null || string.IsNullOrEmpty(release.tag_name))
                    return;

                // Parse version (remove 'v' prefix if present)
                var latestVersion = release.tag_name.TrimStart('v');

                if (IsNewerVersion(latestVersion, CURRENT_VERSION))
                {
                    // Find the installer asset
                    string? downloadUrl = null;
                    if (release.assets != null)
                    {
                        foreach (var asset in release.assets)
                        {
                            if (asset.name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = asset.browser_download_url;
                                break;
                            }
                        }
                    }

                    // Fire balloon notification event if subscribed
                    OnUpdateAvailable?.Invoke(latestVersion, $"Click to update to v{latestVersion}");

                    ShowUpdateDialog(latestVersion, release.html_url, downloadUrl, release.body);
                }
                else if (!silentIfNoUpdate)
                {
                    MessageBox.Show(
                        $"You are running the latest version (v{CURRENT_VERSION}).",
                        "No Updates Available",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                if (!silentIfNoUpdate)
                {
                    MessageBox.Show(
                        $"Failed to check for updates: {ex.Message}",
                        "Update Check Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                // Silently fail on startup
                Debug.WriteLine($"Update check failed: {ex.Message}");
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

        private void ShowUpdateDialog(string newVersion, string releaseUrl, string? downloadUrl, string? releaseNotes)
        {
            var message = $"A new version of Printago Folder Watch is available!\n\n" +
                         $"Current version: v{CURRENT_VERSION}\n" +
                         $"New version: v{newVersion}\n\n";

            if (!string.IsNullOrEmpty(releaseNotes))
            {
                var notes = releaseNotes.Length > 500
                    ? releaseNotes.Substring(0, 500) + "..."
                    : releaseNotes;
                message += $"What's new:\n{notes}\n\n";
            }

            message += "Would you like to download the update now?";

            var result = MessageBox.Show(
                message,
                "Update Available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                if (!string.IsNullOrEmpty(downloadUrl))
                {
                    _ = DownloadAndInstallAsync(downloadUrl, newVersion);
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = releaseUrl,
                        UseShellExecute = true
                    });
                }
            }
        }

        private async Task DownloadAndInstallAsync(string downloadUrl, string version)
        {
            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"PrintagoFolderWatch-Setup-v{version}.exe");

                var progressForm = new Form
                {
                    Text = "Downloading Update...",
                    Width = 400,
                    Height = 120,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterScreen,
                    MaximizeBox = false,
                    MinimizeBox = false
                };

                var progressBar = new ProgressBar
                {
                    Style = ProgressBarStyle.Continuous,
                    Dock = DockStyle.Top,
                    Height = 30,
                    Margin = new Padding(10),
                    Minimum = 0,
                    Maximum = 100
                };

                var label = new Label
                {
                    Text = "Starting download...",
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };

                progressForm.Controls.Add(label);
                progressForm.Controls.Add(progressBar);
                progressForm.Show();

                // Use streaming download with progress
                using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var totalMB = totalBytes > 0 ? totalBytes / 1024.0 / 1024.0 : 0;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    // Update UI on main thread
                    progressForm.Invoke(() =>
                    {
                        var readMB = totalRead / 1024.0 / 1024.0;
                        if (totalBytes > 0)
                        {
                            var percent = (int)((double)totalRead / totalBytes * 100);
                            progressBar.Value = percent;
                            label.Text = $"Downloading... {readMB:F1} / {totalMB:F1} MB ({percent}%)";
                        }
                        else
                        {
                            label.Text = $"Downloading... {readMB:F1} MB";
                        }
                    });
                }

                progressForm.Close();

                var result = MessageBox.Show(
                    "Download complete! The installer will now launch.\n\n" +
                    "The application will close to allow the update to proceed.",
                    "Ready to Install",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information);

                if (result == DialogResult.OK)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tempPath,
                        UseShellExecute = true
                    });

                    Application.Exit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to download update: {ex.Message}\n\n" +
                    "Please download the update manually from GitHub.",
                    "Download Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        public static string CurrentVersion => CURRENT_VERSION;
    }
}
