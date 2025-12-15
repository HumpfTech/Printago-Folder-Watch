using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrintagoFolderWatch.Windows
{
    public class LogForm : Form
    {
        private RichTextBox txtLogs;

        public LogForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Printago Logs";
            Size = new Size(800, 600);
            StartPosition = FormStartPosition.CenterScreen;

            txtLogs = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LightGray,
                Font = new Font("Consolas", 9F)
            };
            Controls.Add(txtLogs);

            var toolbar = new ToolStrip();
            var btnClear = new ToolStripButton("Clear Logs");
            btnClear.Click += (s, e) => txtLogs.Clear();
            toolbar.Items.Add(btnClear);
            Controls.Add(toolbar);
        }

        public void AddLog(string message, string level)
        {
            if (InvokeRequired)
            {
                try
                {
                    Invoke(new Action(() => AddLog(message, level)));
                }
                catch (ObjectDisposedException)
                {
                    // Form was disposed, ignore
                }
                return;
            }

            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var color = level switch
                {
                    "SUCCESS" => Color.LightGreen,
                    "ERROR" => Color.Red,
                    "INFO" => Color.LightBlue,
                    _ => Color.White
                };

                txtLogs.SelectionStart = txtLogs.TextLength;
                txtLogs.SelectionLength = 0;

                txtLogs.SelectionColor = Color.Gray;
                txtLogs.AppendText($"[{timestamp}] ");

                txtLogs.SelectionColor = color;
                txtLogs.AppendText($"[{level}] {message}\n");

                txtLogs.ScrollToCaret();
            }
            catch (ObjectDisposedException)
            {
                // Form was disposed, ignore
            }
        }
    }
}
