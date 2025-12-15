using System;
using System.Drawing;
using System.Windows.Forms;
using PrintagoFolderWatch.Core;

namespace PrintagoFolderWatch.Windows
{
    public class ConfigForm : Form
    {
        private Config config;
        private TextBox txtWatchPath;
        private TextBox txtApiUrl;
        private TextBox txtApiKey;
        private TextBox txtStoreId;

        public ConfigForm(Config config)
        {
            this.config = config;
            InitializeComponent();
            LoadConfig();
        }

        private void InitializeComponent()
        {
            Text = "Printago Settings";
            Size = new Size(600, 350);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var y = 20;

            // Watch Path
            var lblWatchPath = new Label
            {
                Text = "Watch Folder:",
                Location = new Point(20, y),
                AutoSize = true
            };
            Controls.Add(lblWatchPath);

            txtWatchPath = new TextBox
            {
                Location = new Point(150, y - 3),
                Size = new Size(350, 23)
            };
            Controls.Add(txtWatchPath);

            var btnBrowse = new Button
            {
                Text = "Browse...",
                Location = new Point(510, y - 3),
                Size = new Size(70, 23)
            };
            btnBrowse.Click += BtnBrowse_Click;
            Controls.Add(btnBrowse);

            y += 40;

            // API URL
            var lblApiUrl = new Label
            {
                Text = "API URL:",
                Location = new Point(20, y),
                AutoSize = true
            };
            Controls.Add(lblApiUrl);

            txtApiUrl = new TextBox
            {
                Location = new Point(150, y - 3),
                Size = new Size(430, 23)
            };
            Controls.Add(txtApiUrl);

            y += 40;

            // API Key
            var lblApiKey = new Label
            {
                Text = "API Key:",
                Location = new Point(20, y),
                AutoSize = true
            };
            Controls.Add(lblApiKey);

            txtApiKey = new TextBox
            {
                Location = new Point(150, y - 3),
                Size = new Size(430, 23),
                UseSystemPasswordChar = true
            };
            Controls.Add(txtApiKey);

            y += 40;

            // Store ID
            var lblStoreId = new Label
            {
                Text = "Store ID:",
                Location = new Point(20, y),
                AutoSize = true
            };
            Controls.Add(lblStoreId);

            txtStoreId = new TextBox
            {
                Location = new Point(150, y - 3),
                Size = new Size(430, 23)
            };
            Controls.Add(txtStoreId);

            y += 60;

            // Buttons
            var btnSave = new Button
            {
                Text = "Save",
                Location = new Point(400, y),
                Size = new Size(80, 30)
            };
            btnSave.Click += BtnSave_Click;
            Controls.Add(btnSave);

            var btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(490, y),
                Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel
            };
            btnCancel.Click += (s, e) => Close();
            Controls.Add(btnCancel);
        }

        private void LoadConfig()
        {
            txtWatchPath.Text = config.WatchPath;
            txtApiUrl.Text = config.ApiUrl;
            txtApiKey.Text = config.ApiKey;
            txtStoreId.Text = config.StoreId;
        }

        private void BtnBrowse_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select folder to watch",
                UseDescriptionForTitle = true,
                SelectedPath = config.WatchPath
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                txtWatchPath.Text = dialog.SelectedPath;
            }
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            config.WatchPath = txtWatchPath.Text.Trim();
            config.ApiUrl = txtApiUrl.Text.Trim();
            config.ApiKey = txtApiKey.Text.Trim();
            config.StoreId = txtStoreId.Text.Trim();

            config.Save();

            MessageBox.Show("Settings saved! Please restart watching for changes to take effect.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }
}
