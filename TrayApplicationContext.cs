using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PrintagoFolderWatch
{
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private FileWatcherServiceV2 watcherService;
        private ConfigForm configForm;
        private LogForm logForm;
        private StatusForm statusForm;
        private UpdateChecker updateChecker;

        public TrayApplicationContext()
        {
            // Load Printago icon
            Icon printagoIcon;
            try
            {
                var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    printagoIcon = new Icon(iconPath);
                }
                else
                {
                    printagoIcon = SystemIcons.Application;
                }
            }
            catch
            {
                printagoIcon = SystemIcons.Application;
            }

            // Create tray icon with version in tooltip
            trayIcon = new NotifyIcon()
            {
                Icon = printagoIcon,
                ContextMenuStrip = new ContextMenuStrip(),
                Visible = true,
                Text = $"Printago Folder Watch v{UpdateChecker.CurrentVersion}"
            };

            // Click to show status
            trayIcon.Click += (s, e) =>
            {
                if (e is MouseEventArgs mouseEvent && mouseEvent.Button == MouseButtons.Left)
                {
                    ShowStatusForm();
                }
            };

            // Create menu items
            var startItem = new ToolStripMenuItem("Start Watching");
            var stopItem = new ToolStripMenuItem("Stop Watching") { Enabled = false };
            var configItem = new ToolStripMenuItem("Settings...");
            var logsItem = new ToolStripMenuItem("View Logs...");
            var checkUpdateItem = new ToolStripMenuItem("Check for Updates...");
            var aboutItem = new ToolStripMenuItem("About...");
            var exitItem = new ToolStripMenuItem("Exit");

            trayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[] {
                startItem,
                stopItem,
                new ToolStripSeparator(),
                configItem,
                logsItem,
                new ToolStripSeparator(),
                checkUpdateItem,
                aboutItem,
                new ToolStripSeparator(),
                exitItem
            });

            // Initialize services
            watcherService = new FileWatcherServiceV2();
            watcherService.OnLog += (message, level) =>
            {
                logForm?.AddLog(message, level);
            };

            // Wire up events
            startItem.Click += async (s, e) =>
            {
                if (await watcherService.Start())
                {
                    startItem.Enabled = false;
                    stopItem.Enabled = true;
                    trayIcon.Text = $"Printago Folder Watch v{UpdateChecker.CurrentVersion} - Running";
                    trayIcon.ShowBalloonTip(2000, "Printago", "Watching folder", ToolTipIcon.Info);
                }
                else
                {
                    MessageBox.Show("Please configure settings first", "Configuration Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            stopItem.Click += (s, e) =>
            {
                watcherService.Stop();
                startItem.Enabled = true;
                stopItem.Enabled = false;
                trayIcon.Text = $"Printago Folder Watch v{UpdateChecker.CurrentVersion} - Stopped";
                trayIcon.ShowBalloonTip(2000, "Printago", "Stopped watching", ToolTipIcon.Info);
            };

            configItem.Click += (s, e) =>
            {
                if (configForm == null || configForm.IsDisposed)
                {
                    configForm = new ConfigForm(watcherService.Config);
                }
                configForm.Show();
                configForm.BringToFront();
            };

            logsItem.Click += (s, e) =>
            {
                if (logForm == null || logForm.IsDisposed)
                {
                    logForm = new LogForm();
                }
                logForm.Show();
                logForm.BringToFront();
            };

            exitItem.Click += (s, e) =>
            {
                watcherService.Stop();
                trayIcon.Visible = false;
                Application.Exit();
            };

            // Initialize update checker
            updateChecker = new UpdateChecker();

            // Subscribe to update available event for balloon notification
            updateChecker.OnUpdateAvailable += (version, message) =>
            {
                trayIcon.ShowBalloonTip(5000, "Update Available!", $"Printago Folder Watch v{version} is available. {message}", ToolTipIcon.Info);
            };

            checkUpdateItem.Click += async (s, e) =>
            {
                await updateChecker.CheckForUpdatesAsync(silentIfNoUpdate: false);
            };

            // About dialog
            aboutItem.Click += (s, e) =>
            {
                ShowAboutDialog();
            };

            // Auto-start if configured
            if (watcherService.Config.IsValid())
            {
                _ = Task.Run(async () =>
                {
                    if (await watcherService.Start())
                    {
                        trayIcon.Text = $"Printago Folder Watch v{UpdateChecker.CurrentVersion} - Running";
                        startItem.Enabled = false;
                        stopItem.Enabled = true;
                    }
                });
            }

            // Check for updates on startup (after a short delay)
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000); // Wait 5 seconds before checking
                await updateChecker.CheckForUpdatesAsync(silentIfNoUpdate: true);
            });
        }

        private void ShowAboutDialog()
        {
            var aboutForm = new Form
            {
                Text = "About Printago Folder Watch",
                Width = 400,
                Height = 280,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false
            };

            var titleLabel = new Label
            {
                Text = "Printago Folder Watch",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(20, 20)
            };

            var versionLabel = new Label
            {
                Text = $"Version {UpdateChecker.CurrentVersion}",
                Font = new Font("Segoe UI", 10),
                AutoSize = true,
                Location = new Point(20, 55)
            };

            var descLabel = new Label
            {
                Text = "Automatically sync your 3D print files (.stl, .3mf)\nto Printago cloud service.",
                Font = new Font("Segoe UI", 9),
                AutoSize = true,
                Location = new Point(20, 90)
            };

            var copyrightLabel = new Label
            {
                Text = $"© {DateTime.Now.Year} Humpf Tech LLC",
                Font = new Font("Segoe UI", 9),
                AutoSize = true,
                Location = new Point(20, 140)
            };

            var githubLink = new LinkLabel
            {
                Text = "View on GitHub",
                AutoSize = true,
                Location = new Point(20, 170)
            };
            githubLink.Click += (s, e) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/HumpfTech/Printago-Folder-Watch",
                    UseShellExecute = true
                });
            };

            var closeButton = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Location = new Point(290, 200),
                Width = 80
            };

            aboutForm.Controls.AddRange(new Control[] { titleLabel, versionLabel, descLabel, copyrightLabel, githubLink, closeButton });
            aboutForm.AcceptButton = closeButton;
            aboutForm.ShowDialog();
        }

        private void ShowStatusForm()
        {
            if (statusForm == null || statusForm.IsDisposed)
            {
                statusForm = new StatusForm(watcherService);
                statusForm.SettingsClicked += (s, e) =>
                {
                    if (configForm == null || configForm.IsDisposed)
                    {
                        configForm = new ConfigForm(watcherService.Config);
                    }
                    configForm.Show();
                    configForm.BringToFront();
                };
                statusForm.LogsClicked += (s, e) =>
                {
                    if (logForm == null || logForm.IsDisposed)
                    {
                        logForm = new LogForm();
                    }
                    logForm.Show();
                    logForm.BringToFront();
                };
                statusForm.SyncNowClicked += async (s, e) =>
                {
                    await watcherService.TriggerSyncNow();
                };
            }
            statusForm.Show();
            statusForm.BringToFront();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                trayIcon?.Dispose();
                watcherService?.Dispose();
                configForm?.Dispose();
                logForm?.Dispose();
                statusForm?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
