using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PrintagoFolderWatch.Core;

namespace PrintagoFolderWatch.CrossPlatform.Views;

public partial class StatusWindow : Window
{
    private readonly FileWatcherService _watcherService;
    private bool _isRunning;
    private readonly DispatcherTimer _updateTimer;

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
            Interval = TimeSpan.FromMilliseconds(500)
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

        // Update queue items
        var items = _watcherService.GetQueueItems();
        QueueList.ItemsSource = items;

        // Update status text based on activity
        if (_isRunning)
        {
            var activeCount = _watcherService.GetActiveUploads().Count;
            if (activeCount > 0)
            {
                StatusText.Text = $"Uploading {activeCount} files ({_watcherService.UploadQueueCount} in queue)";
            }
            else if (_watcherService.UploadQueueCount > 0)
            {
                StatusText.Text = $"Processing queue ({_watcherService.UploadQueueCount} files)";
            }
            else
            {
                StatusText.Text = "All files synced";
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
