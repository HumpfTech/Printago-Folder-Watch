using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PrintagoFolderWatch.CrossPlatform.Views;

public partial class LogsWindow : Window
{
    private readonly ObservableCollection<string> _logs = new();

    public LogsWindow()
    {
        InitializeComponent();
        LogsList.ItemsSource = _logs;
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
}
