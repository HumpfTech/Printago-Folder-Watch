using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PrintagoFolderWatch.CrossPlatform.Views;

public partial class LogsWindow : Window
{
    private readonly ObservableCollection<string> _logs = new();
    private readonly string _logsPath;

    public LogsWindow()
    {
        InitializeComponent();
        LogsList.ItemsSource = _logs;

        // Set logs path
        _logsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrintagoFolderWatch",
            "logs"
        );
        LogsPathText.Text = _logsPath;
    }

    public void AddLog(string message, string level)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _logs.Add($"[{timestamp}] [{level}] {message}");

        // Keep only last 500 logs
        while (_logs.Count > 500)
            _logs.RemoveAt(0);

        // Auto-scroll to bottom
        if (LogsList.ItemCount > 0)
        {
            LogsList.ScrollIntoView(_logs[_logs.Count - 1]);
        }
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        _logs.Clear();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenLogsFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Create directory if it doesn't exist
            if (!Directory.Exists(_logsPath))
            {
                Directory.CreateDirectory(_logsPath);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = _logsPath,
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = _logsPath,
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = _logsPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open logs folder: {ex.Message}");
        }
    }
}
