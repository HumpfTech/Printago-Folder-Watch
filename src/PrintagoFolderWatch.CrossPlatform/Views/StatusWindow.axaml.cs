using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PrintagoFolderWatch.Core;

namespace PrintagoFolderWatch.CrossPlatform.Views;

public partial class StatusWindow : Window
{
    private readonly FileWatcherService _watcherService;
    private bool _isRunning;
    private readonly DispatcherTimer _updateTimer;
    private readonly Dictionary<string, Border> _activeUploadPanels = new();

    public event Action? OnSettingsClicked;
    public event Action? OnLogsClicked;
    public event Action? OnSyncNowClicked;
    public event Action? OnStartClicked;
    public event Action? OnStopClicked;

    public StatusWindow(FileWatcherService watcherService, bool isRunning)
    {
        InitializeComponent();
        _watcherService = watcherService;
        _isRunning = isRunning;

        UpdateButtonStates();
        UpdateStatus(_isRunning ? "Running - Watching for changes" : "Stopped");

        // Set up timer to update stats
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _updateTimer.Tick += UpdateStats;
        _updateTimer.Start();

        Closed += (s, e) => _updateTimer.Stop();
    }

    private void UpdateStats(object? sender, EventArgs e)
    {
        QueueCount.Text = _watcherService.UploadQueueCount.ToString();
        FoldersCount.Text = _watcherService.FoldersCreatedCount.ToString();
        SyncedCount.Text = _watcherService.SyncedFilesCount.ToString();

        // Update upload queue items
        var queueItems = _watcherService.GetQueueItems();
        UploadQueueList.ItemsSource = queueItems;

        // Update delete queue items
        var deleteQueueItems = _watcherService.GetDeleteQueueItems();
        DeleteQueueList.ItemsSource = deleteQueueItems;
        DeleteQueueTab.Header = $"Delete Queue ({deleteQueueItems.Count})";

        // Update recent activity
        var recentLogs = _watcherService.GetRecentLogs(20);
        ActivityList.ItemsSource = recentLogs;

        // Update active uploads
        UpdateActiveUploads();

        // Update status text based on activity
        if (_isRunning)
        {
            var activeCount = _watcherService.GetActiveUploads().Count;
            if (activeCount > 0)
            {
                StatusText.Text = $"Uploading {activeCount} files ({_watcherService.UploadQueueCount} in queue)";
                StatusText.Foreground = Brushes.Yellow;
            }
            else if (_watcherService.UploadQueueCount > 0)
            {
                StatusText.Text = $"Processing queue ({_watcherService.UploadQueueCount} files)";
                StatusText.Foreground = Brushes.LightBlue;
            }
            else
            {
                StatusText.Text = "All files synced";
                StatusText.Foreground = Brushes.LightGreen;
            }
        }
    }

    private void UpdateActiveUploads()
    {
        var activeUploads = _watcherService.GetActiveUploads();
        UploadingTab.Header = $"Currently Uploading ({activeUploads.Count}/10)";

        var currentUploads = activeUploads.Select(u => u.FilePath).ToHashSet();

        // Remove panels for completed uploads
        var panelsToRemove = _activeUploadPanels.Keys.Where(key => !currentUploads.Contains(key)).ToList();
        foreach (var key in panelsToRemove)
        {
            var panel = _activeUploadPanels[key];
            UploadingPanel.Children.Remove(panel);
            _activeUploadPanels.Remove(key);
        }

        // Add or update panels for active uploads
        foreach (var upload in activeUploads.Take(10))
        {
            if (_activeUploadPanels.TryGetValue(upload.FilePath, out var existingPanel))
            {
                UpdateUploadPanel(existingPanel, upload);
            }
            else
            {
                var panel = CreateUploadPanel(upload);
                UploadingPanel.Children.Add(panel);
                _activeUploadPanels[upload.FilePath] = panel;
            }
        }
    }

    private Border CreateUploadPanel(Core.Models.UploadProgress upload)
    {
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            CornerRadius = new Avalonia.CornerRadius(4),
            Padding = new Avalonia.Thickness(10),
            Margin = new Avalonia.Thickness(0, 2)
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto")
        };

        var fileLabel = new TextBlock
        {
            Text = upload.RelativePath,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(fileLabel, 0);
        grid.Children.Add(fileLabel);

        var statusLabel = new TextBlock
        {
            Name = "StatusLabel",
            Text = upload.Status,
            Foreground = Brushes.LightGray,
            FontSize = 10,
            Margin = new Avalonia.Thickness(0, 3, 0, 3)
        };
        Grid.SetRow(statusLabel, 1);
        grid.Children.Add(statusLabel);

        var progressBar = new ProgressBar
        {
            Name = "ProgressBar",
            Minimum = 0,
            Maximum = 100,
            Value = Math.Min(upload.ProgressPercent, 100),
            Height = 8
        };
        Grid.SetRow(progressBar, 2);
        grid.Children.Add(progressBar);

        panel.Child = grid;
        return panel;
    }

    private void UpdateUploadPanel(Border panel, Core.Models.UploadProgress upload)
    {
        if (panel.Child is Grid grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is TextBlock tb && tb.Name == "StatusLabel")
                {
                    tb.Text = upload.Status;
                }
                else if (child is ProgressBar pb && pb.Name == "ProgressBar")
                {
                    pb.Value = Math.Min(upload.ProgressPercent, 100);
                }
            }
        }
    }

    public void UpdateStatus(string status)
    {
        StatusText.Text = status;
    }

    public void SetRunningState(bool isRunning)
    {
        _isRunning = isRunning;
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        StartButton.IsEnabled = !_isRunning;
        StopButton.IsEnabled = _isRunning;
        SyncNowButton.IsEnabled = _isRunning;
    }

    private void Start_Click(object? sender, RoutedEventArgs e)
    {
        OnStartClicked?.Invoke();
        _isRunning = true;
        UpdateButtonStates();
        UpdateStatus("Running - Watching for changes");
    }

    private void Stop_Click(object? sender, RoutedEventArgs e)
    {
        OnStopClicked?.Invoke();
        _isRunning = false;
        UpdateButtonStates();
        UpdateStatus("Stopped");
    }

    private void SyncNow_Click(object? sender, RoutedEventArgs e)
    {
        OnSyncNowClicked?.Invoke();
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        OnSettingsClicked?.Invoke();
    }

    private void Logs_Click(object? sender, RoutedEventArgs e)
    {
        OnLogsClicked?.Invoke();
    }
}
