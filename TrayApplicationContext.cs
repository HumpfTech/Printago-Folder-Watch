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

            // Create tray icon
            trayIcon = new NotifyIcon()
            {
                Icon = printagoIcon,
                ContextMenuStrip = new ContextMenuStrip(),
                Visible = true,
                Text = "Printago Folder Watch"
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
            var exitItem = new ToolStripMenuItem("Exit");

            trayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[] {
                startItem,
                stopItem,
                new ToolStripSeparator(),
                configItem,
                logsItem,
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
                    trayIcon.Text = "Printago Folder Watch - Running";
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
                trayIcon.Text = "Printago Folder Watch - Stopped";
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

            // Auto-start if configured
            if (watcherService.Config.IsValid())
            {
                _ = Task.Run(async () =>
                {
                    if (await watcherService.Start())
                    {
                        trayIcon.Text = "Printago Folder Watch - Running";
                        startItem.Enabled = false;
                        stopItem.Enabled = true;
                    }
                });
            }
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
