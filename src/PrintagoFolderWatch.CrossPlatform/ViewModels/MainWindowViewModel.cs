using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using PrintagoFolderWatch.Core;
using PrintagoFolderWatch.Core.Models;

namespace PrintagoFolderWatch.CrossPlatform.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly FileWatcherService _watcherService;
    private string _status = "Not Running";
    private string _watchPath = "";
    private string _apiUrl = "";
    private string _apiKey = "";
    private string _storeId = "";
    private bool _isRunning = false;
    private int _queueCount = 0;
    private int _foldersCount = 0;
    private int _syncedCount = 0;

    public string Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public string WatchPath
    {
        get => _watchPath;
        set => this.RaiseAndSetIfChanged(ref _watchPath, value);
    }

    public string ApiUrl
    {
        get => _apiUrl;
        set => this.RaiseAndSetIfChanged(ref _apiUrl, value);
    }

    public string ApiKey
    {
        get => _apiKey;
        set => this.RaiseAndSetIfChanged(ref _apiKey, value);
    }

    public string StoreId
    {
        get => _storeId;
        set => this.RaiseAndSetIfChanged(ref _storeId, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    public int QueueCount
    {
        get => _queueCount;
        set => this.RaiseAndSetIfChanged(ref _queueCount, value);
    }

    public int FoldersCount
    {
        get => _foldersCount;
        set => this.RaiseAndSetIfChanged(ref _foldersCount, value);
    }

    public int SyncedCount
    {
        get => _syncedCount;
        set => this.RaiseAndSetIfChanged(ref _syncedCount, value);
    }

    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<string> QueueItems { get; } = new();

    public ReactiveCommand<Unit, Unit> StartCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> SyncNowCommand { get; }

    public string Version => "2.7";

    public MainWindowViewModel()
    {
        _watcherService = new FileWatcherService();

        // Load config
        WatchPath = _watcherService.Config.WatchPath;
        ApiUrl = _watcherService.Config.ApiUrl;
        ApiKey = _watcherService.Config.ApiKey;
        StoreId = _watcherService.Config.StoreId;

        // Subscribe to logs
        _watcherService.OnLog += (message, level) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                Logs.Add($"[{timestamp}] [{level}] {message}");

                // Keep only last 100 logs
                while (Logs.Count > 100)
                    Logs.RemoveAt(0);
            });
        };

        // Create commands
        var canStart = this.WhenAnyValue(x => x.IsRunning, running => !running);
        var canStop = this.WhenAnyValue(x => x.IsRunning);

        StartCommand = ReactiveCommand.CreateFromTask(StartAsync, canStart);
        StopCommand = ReactiveCommand.Create(Stop, canStop);
        SaveSettingsCommand = ReactiveCommand.Create(SaveSettings);
        SyncNowCommand = ReactiveCommand.CreateFromTask(SyncNowAsync, canStop);

        // Start update timer
        StartStatusUpdater();

        // Auto-start if configured
        if (_watcherService.Config.IsValid())
        {
            _ = StartAsync();
        }
    }

    private async Task StartAsync()
    {
        SaveSettings();

        if (await _watcherService.Start())
        {
            IsRunning = true;
            Status = "Running - Watching for changes";
            AddLog("Started watching folder", "SUCCESS");
        }
        else
        {
            Status = "Failed to start - Check settings";
            AddLog("Failed to start - please configure settings", "ERROR");
        }
    }

    private void Stop()
    {
        _watcherService.Stop();
        IsRunning = false;
        Status = "Stopped";
        AddLog("Stopped watching", "INFO");
    }

    private void SaveSettings()
    {
        _watcherService.Config.WatchPath = WatchPath;
        _watcherService.Config.ApiUrl = ApiUrl;
        _watcherService.Config.ApiKey = ApiKey;
        _watcherService.Config.StoreId = StoreId;
        _watcherService.Config.Save();
        AddLog("Settings saved", "INFO");
    }

    private async Task SyncNowAsync()
    {
        AddLog("Manual sync triggered", "INFO");
        await _watcherService.TriggerSyncNow();
    }

    private void AddLog(string message, string level)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        Logs.Add($"[{timestamp}] [{level}] {message}");
    }

    private void StartStatusUpdater()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(500);

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    QueueCount = _watcherService.UploadQueueCount;
                    FoldersCount = _watcherService.FoldersCreatedCount;
                    SyncedCount = _watcherService.SyncedFilesCount;

                    // Update queue items
                    var items = _watcherService.GetQueueItems();
                    QueueItems.Clear();
                    foreach (var item in items)
                        QueueItems.Add(item);

                    // Update status text
                    if (IsRunning)
                    {
                        var activeCount = _watcherService.GetActiveUploads().Count;
                        if (activeCount > 0)
                        {
                            Status = $"Uploading {activeCount} files ({QueueCount} in queue)";
                        }
                        else if (QueueCount > 0)
                        {
                            Status = $"Processing queue ({QueueCount} files)";
                        }
                        else
                        {
                            Status = "All files synced";
                        }
                    }
                });
            }
        });
    }
}
