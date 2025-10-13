using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PrintagoFolderWatch
{
    public class StatusForm : Form
    {
        private Label lblQueueCount;
        private Label lblFoldersCount;
        private Label lblSyncedCount;
        private Label lblStatus;
        private Button btnSettings;
        private Button btnLogs;
        private Button btnSyncNow;
        private System.Windows.Forms.Timer updateTimer;
        private IFileWatcherService service;
        private TabControl tabControl;
        private ListBox lstQueue;
        private Panel pnlUploading;
        private Dictionary<string, Panel> activeUploadPanels = new();

        public StatusForm(IFileWatcherService watcherService)
        {
            service = watcherService;
            InitializeComponents();
            SetupTimer();
        }

        private void InitializeComponents()
        {
            Text = "Printago Folder Watch - Status";
            Size = new Size(700, 550);
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(700, 550);
            MaximizeBox = true;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;

            // Title Label
            var lblTitle = new Label
            {
                Text = "📁 Printago Folder Watch",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 200, 255),
                Location = new Point(20, 20),
                Size = new Size(650, 35),
                TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(lblTitle);

            // Status Label
            lblStatus = new Label
            {
                Text = "⚡ Watching for changes...",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.LightGreen,
                Location = new Point(20, 60),
                Size = new Size(650, 25),
                TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(lblStatus);

            // Stats Panel
            var statsPanel = new Panel
            {
                Location = new Point(20, 100),
                Size = new Size(650, 70),
                BackColor = Color.FromArgb(30, 30, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(statsPanel);

            // Upload Queue Panel
            var panelQueue = CreateStatPanel("📤 Queue", 0);
            statsPanel.Controls.Add(panelQueue);
            lblQueueCount = (Label)panelQueue.Controls[1];

            // Folders Tracked Panel
            var panelFolders = CreateStatPanel("📂 Folders", 220);
            statsPanel.Controls.Add(panelFolders);
            lblFoldersCount = (Label)panelFolders.Controls[1];

            // Synced Files Panel
            var panelSynced = CreateStatPanel("✅ Synced", 440);
            statsPanel.Controls.Add(panelSynced);
            lblSyncedCount = (Label)panelSynced.Controls[1];

            // Tab Control
            tabControl = new TabControl
            {
                Location = new Point(20, 180),
                Size = new Size(650, 270),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(tabControl);

            // Currently Uploading Tab
            var uploadingTab = new TabPage("Currently Uploading (0/10)");
            uploadingTab.BackColor = Color.FromArgb(30, 30, 30);
            tabControl.TabPages.Add(uploadingTab);

            pnlUploading = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(640, 240),
                BackColor = Color.FromArgb(30, 30, 30),
                AutoScroll = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            uploadingTab.Controls.Add(pnlUploading);

            // Upload Queue Tab
            var queueTab = new TabPage("Upload Queue");
            queueTab.BackColor = Color.FromArgb(30, 30, 30);
            tabControl.TabPages.Add(queueTab);

            lstQueue = new ListBox
            {
                Location = new Point(10, 10),
                Size = new Size(620, 220),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9),
                BorderStyle = BorderStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            queueTab.Controls.Add(lstQueue);

            // Delete Queue Tab
            var deleteQueueTab = new TabPage("Delete Queue");
            deleteQueueTab.BackColor = Color.FromArgb(30, 30, 30);
            tabControl.TabPages.Add(deleteQueueTab);

            var lstDeleteQueue = new ListBox
            {
                Name = "lstDeleteQueue",
                Location = new Point(10, 10),
                Size = new Size(620, 220),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.LightCoral,
                Font = new Font("Consolas", 9),
                BorderStyle = BorderStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            deleteQueueTab.Controls.Add(lstDeleteQueue);

            // Move Queue Tab
            var moveQueueTab = new TabPage("Move Queue");
            moveQueueTab.BackColor = Color.FromArgb(30, 30, 30);
            tabControl.TabPages.Add(moveQueueTab);

            var lstMoveQueue = new ListBox
            {
                Name = "lstMoveQueue",
                Location = new Point(10, 10),
                Size = new Size(620, 220),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.FromArgb(150, 200, 255),
                Font = new Font("Consolas", 9),
                BorderStyle = BorderStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            moveQueueTab.Controls.Add(lstMoveQueue);

            // Recent Activity Tab
            var activityTab = new TabPage("Recent Activity");
            activityTab.BackColor = Color.FromArgb(30, 30, 30);
            tabControl.TabPages.Add(activityTab);

            var lstActivity = new ListBox
            {
                Name = "lstActivity",
                Location = new Point(10, 10),
                Size = new Size(620, 220),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9),
                BorderStyle = BorderStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            activityTab.Controls.Add(lstActivity);

            // Buttons Panel
            var buttonsPanel = new Panel
            {
                Location = new Point(20, 460),
                Size = new Size(650, 45),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(buttonsPanel);

            // Settings Button
            btnSettings = new Button
            {
                Text = "⚙️ Settings",
                Location = new Point(0, 0),
                Size = new Size(150, 40),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10)
            };
            btnSettings.FlatAppearance.BorderColor = Color.FromArgb(100, 100, 100);
            btnSettings.Click += BtnSettings_Click;
            buttonsPanel.Controls.Add(btnSettings);

            // Logs Button
            btnLogs = new Button
            {
                Text = "📋 View Logs",
                Location = new Point(160, 0),
                Size = new Size(150, 40),
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10)
            };
            btnLogs.FlatAppearance.BorderColor = Color.FromArgb(100, 100, 100);
            btnLogs.Click += BtnLogs_Click;
            buttonsPanel.Controls.Add(btnLogs);

            // Sync Now Button
            btnSyncNow = new Button
            {
                Text = "🔄 Sync Now",
                Location = new Point(320, 0),
                Size = new Size(150, 40),
                BackColor = Color.FromArgb(50, 100, 200),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10)
            };
            btnSyncNow.FlatAppearance.BorderColor = Color.FromArgb(100, 150, 255);
            btnSyncNow.Click += BtnSyncNow_Click;
            buttonsPanel.Controls.Add(btnSyncNow);
        }

        private Panel CreateStatPanel(string title, int x)
        {
            var panel = new Panel
            {
                Location = new Point(x, 0),
                Size = new Size(210, 70),
                BackColor = Color.FromArgb(40, 40, 40),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.LightGray,
                Location = new Point(10, 5),
                Size = new Size(190, 20)
            };
            panel.Controls.Add(lblTitle);

            var lblCount = new Label
            {
                Text = "0",
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 200, 255),
                Location = new Point(10, 28),
                Size = new Size(190, 35),
                TextAlign = ContentAlignment.MiddleLeft
            };
            panel.Controls.Add(lblCount);

            return panel;
        }

        private void SetupTimer()
        {
            updateTimer = new System.Windows.Forms.Timer
            {
                Interval = 300 // Update every 300ms for smoother progress
            };
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (service != null)
            {
                lblQueueCount.Text = service.UploadQueueCount.ToString();
                lblFoldersCount.Text = service.FoldersCreatedCount.ToString();
                lblSyncedCount.Text = service.SyncedFilesCount.ToString();

                // Update queue lists
                UpdateQueueList();
                UpdateDeleteQueueList();
                UpdateMoveQueueList();
                UpdateActivityList();

                // Update active uploads
                UpdateActiveUploads();

                // Update status based on queue
                var activeCount = service.GetActiveUploads().Count;
                if (activeCount > 0)
                {
                    lblStatus.Text = $"⚡ Uploading {activeCount} files... ({service.UploadQueueCount} in queue)";
                    lblStatus.ForeColor = Color.Yellow;
                }
                else if (service.UploadQueueCount > 0)
                {
                    lblStatus.Text = $"⏳ Processing queue... ({service.UploadQueueCount} files remaining)";
                    lblStatus.ForeColor = Color.LightBlue;
                }
                else
                {
                    lblStatus.Text = "✓ All files synced";
                    lblStatus.ForeColor = Color.LightGreen;
                }
            }
        }

        private void UpdateQueueList()
        {
            var queueItems = service.GetQueueItems();

            // Only update if changed
            if (lstQueue.Items.Count != queueItems.Count ||
                !lstQueue.Items.Cast<string>().SequenceEqual(queueItems))
            {
                lstQueue.Items.Clear();
                foreach (var item in queueItems)
                {
                    lstQueue.Items.Add(item);
                }
            }
        }

        private void UpdateDeleteQueueList()
        {
            var deleteQueueItems = service.GetDeleteQueueItems();

            // Find the delete queue list box
            var deleteQueueTab = tabControl.TabPages[2]; // Index 2 is Delete Queue tab
            var lstDeleteQueue = deleteQueueTab.Controls["lstDeleteQueue"] as ListBox;

            if (lstDeleteQueue != null)
            {
                // Only update if changed
                if (lstDeleteQueue.Items.Count != deleteQueueItems.Count ||
                    !lstDeleteQueue.Items.Cast<string>().SequenceEqual(deleteQueueItems))
                {
                    lstDeleteQueue.Items.Clear();
                    foreach (var item in deleteQueueItems)
                    {
                        lstDeleteQueue.Items.Add(item);
                    }
                }

                // Update tab title with count
                tabControl.TabPages[2].Text = $"Delete Queue ({deleteQueueItems.Count})";
            }
        }

        private void UpdateMoveQueueList()
        {
            // Move queue would show files being renamed/moved
            // For now, show a placeholder message
            var moveQueueTab = tabControl.TabPages[3]; // Index 3 is Move Queue tab
            var lstMoveQueue = moveQueueTab.Controls["lstMoveQueue"] as ListBox;

            if (lstMoveQueue != null && lstMoveQueue.Items.Count == 0)
            {
                lstMoveQueue.Items.Add("No active file moves/renames");
            }
        }

        private void UpdateActivityList()
        {
            // Recent Activity shows the last 20 log entries
            var recentLogs = service.GetRecentLogs(20);

            var activityTab = tabControl.TabPages[4]; // Index 4 is Recent Activity tab
            var lstActivity = activityTab.Controls["lstActivity"] as ListBox;

            if (lstActivity != null)
            {
                // Only update if changed
                if (lstActivity.Items.Count != recentLogs.Count ||
                    !lstActivity.Items.Cast<string>().SequenceEqual(recentLogs))
                {
                    lstActivity.Items.Clear();
                    foreach (var log in recentLogs)
                    {
                        lstActivity.Items.Add(log);
                    }

                    // Auto-scroll to bottom to show latest activity
                    if (lstActivity.Items.Count > 0)
                    {
                        lstActivity.TopIndex = lstActivity.Items.Count - 1;
                    }
                }
            }
        }

        private void UpdateActiveUploads()
        {
            var activeUploads = service.GetActiveUploads();

            // Update tab title
            tabControl.TabPages[0].Text = $"Currently Uploading ({activeUploads.Count}/10)";

            // Get current upload file paths
            var currentUploads = activeUploads.Select(u => u.FilePath).ToHashSet();

            // Remove panels for uploads that are no longer active
            var panelsToRemove = activeUploadPanels.Keys.Where(key => !currentUploads.Contains(key)).ToList();
            foreach (var key in panelsToRemove)
            {
                var panel = activeUploadPanels[key];
                pnlUploading.Controls.Remove(panel);
                panel.Dispose();
                activeUploadPanels.Remove(key);
            }

            // Add or update panels
            int y = 10;
            foreach (var upload in activeUploads.Take(10))
            {
                if (activeUploadPanels.ContainsKey(upload.FilePath))
                {
                    // Update existing panel
                    UpdateUploadProgressPanel(activeUploadPanels[upload.FilePath], upload, y);
                }
                else
                {
                    // Create new panel
                    var panel = CreateUploadProgressPanel(upload, y);
                    pnlUploading.Controls.Add(panel);
                    activeUploadPanels[upload.FilePath] = panel;
                }
                y += 70;
            }
        }

        private Panel CreateUploadProgressPanel(UploadProgress upload, int y)
        {
            var panel = new Panel
            {
                Location = new Point(10, y),
                Size = new Size(600, 60),
                BackColor = Color.FromArgb(40, 40, 40),
                BorderStyle = BorderStyle.FixedSingle
            };

            // File name label
            var lblFile = new Label
            {
                Name = "lblFile",
                Text = upload.RelativePath,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(10, 5),
                Size = new Size(580, 18),
                AutoEllipsis = true
            };
            panel.Controls.Add(lblFile);

            // Status label
            var lblStatusText = new Label
            {
                Name = "lblStatus",
                Text = upload.Status,
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.LightGray,
                Location = new Point(10, 23),
                Size = new Size(500, 15)
            };
            panel.Controls.Add(lblStatusText);

            // Progress bar
            var progressBar = new ProgressBar
            {
                Name = "progressBar",
                Location = new Point(10, 40),
                Size = new Size(580, 12),
                Minimum = 0,
                Maximum = 100,
                Value = Math.Min(upload.ProgressPercent, 100),
                Style = ProgressBarStyle.Continuous
            };
            panel.Controls.Add(progressBar);

            return panel;
        }

        private void UpdateUploadProgressPanel(Panel panel, UploadProgress upload, int y)
        {
            // Update position if needed
            if (panel.Location.Y != y)
            {
                panel.Location = new Point(10, y);
            }

            // Find and update controls
            var lblStatus = panel.Controls["lblStatus"] as Label;
            if (lblStatus != null && lblStatus.Text != upload.Status)
            {
                lblStatus.Text = upload.Status;
            }

            var progressBar = panel.Controls["progressBar"] as ProgressBar;
            if (progressBar != null)
            {
                var newValue = Math.Min(upload.ProgressPercent, 100);
                if (progressBar.Value != newValue)
                {
                    progressBar.Value = newValue;
                }
            }
        }

        private void BtnSettings_Click(object? sender, EventArgs e)
        {
            // Trigger settings event
            SettingsClicked?.Invoke(this, EventArgs.Empty);
        }

        private void BtnLogs_Click(object? sender, EventArgs e)
        {
            // Trigger logs event
            LogsClicked?.Invoke(this, EventArgs.Empty);
        }

        private void BtnSyncNow_Click(object? sender, EventArgs e)
        {
            // Trigger sync event
            SyncNowClicked?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? SettingsClicked;
        public event EventHandler? LogsClicked;
        public event EventHandler? SyncNowClicked;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                updateTimer?.Stop();
                updateTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
