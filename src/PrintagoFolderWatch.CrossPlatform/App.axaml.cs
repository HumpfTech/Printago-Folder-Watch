using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PrintagoFolderWatch.Core;
using PrintagoFolderWatch.CrossPlatform.Views;

namespace PrintagoFolderWatch.CrossPlatform;

public partial class App : Application
{
    public const string VERSION = "2.9.3";

    private FileWatcherService? _watcherService;
    private StatusWindow? _statusWindow;
    private SettingsWindow? _settingsWindow;
    private LogsWindow? _logsWindow;
    private bool _isRunning = false;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _startMenuItem;
    private NativeMenuItem? _stopMenuItem;
    private NativeMenuItem? _syncNowMenuItem;
    private CrossPlatformUpdateChecker? _updateChecker;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Don't create a main window - run as tray app only
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Initialize the watcher service
            _watcherService = new FileWatcherService();
            _watcherService.OnLog += (message, level) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _logsWindow?.AddLog(message, level);
                });
            };

            // Create tray icon programmatically
            CreateTrayIcon();

            // Initialize update checker
            _updateChecker = new CrossPlatformUpdateChecker(VERSION);

            // Auto-start if configured
            if (_watcherService.Config.IsValid())
            {
                _ = StartWatchingAsync();
            }

            // Check for updates on startup (delayed)
            _ = CheckForUpdatesOnStartupAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon()
    {
        // Load icon
        WindowIcon? icon = null;
        try
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            Debug.WriteLine($"Looking for icon at: {iconPath}");
            if (File.Exists(iconPath))
            {
                icon = new WindowIcon(iconPath);
                Debug.WriteLine("Icon loaded successfully");
            }
            else
            {
                Debug.WriteLine("Icon file not found");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load icon: {ex.Message}");
        }

        // Create menu items
        var showStatusItem = new NativeMenuItem("Show Status");
        showStatusItem.Click += (s, e) => ShowStatusWindow();

        _startMenuItem = new NativeMenuItem("Start Watching");
        _startMenuItem.Click += async (s, e) => await StartWatchingAsync();

        _stopMenuItem = new NativeMenuItem("Stop Watching") { IsEnabled = false };
        _stopMenuItem.Click += (s, e) => StopWatching();

        var settingsItem = new NativeMenuItem("Settings...");
        settingsItem.Click += (s, e) => ShowSettingsWindow();

        var logsItem = new NativeMenuItem("View Logs...");
        logsItem.Click += (s, e) => ShowLogsWindow();

        _syncNowMenuItem = new NativeMenuItem("Sync Now") { IsEnabled = false };
        _syncNowMenuItem.Click += async (s, e) =>
        {
            if (_watcherService != null && _isRunning)
                await _watcherService.TriggerSyncNow();
        };

        var checkUpdatesItem = new NativeMenuItem("Check for Updates...");
        checkUpdatesItem.Click += async (s, e) => await CheckForUpdatesAsync(showNotification: true);

        var aboutItem = new NativeMenuItem("About...");
        aboutItem.Click += (s, e) => ShowAboutWindow();

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (s, e) => ExitApp();

        // Build menu
        var menu = new NativeMenu();
        menu.Items.Add(showStatusItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_startMenuItem);
        menu.Items.Add(_stopMenuItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(logsItem);
        menu.Items.Add(_syncNowMenuItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(checkUpdatesItem);
        menu.Items.Add(aboutItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        // Create tray icon
        _trayIcon = new TrayIcon
        {
            ToolTipText = $"Printago Folder Watch v{VERSION}",
            Menu = menu,
            IsVisible = true
        };

        if (icon != null)
        {
            _trayIcon.Icon = icon;
        }

        _trayIcon.Clicked += (s, e) => ShowStatusWindow();

        // Register with application
        var icons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(this, icons);

        Debug.WriteLine("Tray icon created and registered");
    }

    private void UpdateTrayTooltip()
    {
        if (_trayIcon != null)
        {
            var status = _isRunning ? "Running" : "Stopped";
            _trayIcon.ToolTipText = $"Printago Folder Watch v{VERSION} - {status}";
        }
    }

    private void UpdateMenuState()
    {
        if (_startMenuItem != null)
            _startMenuItem.IsEnabled = !_isRunning;
        if (_stopMenuItem != null)
            _stopMenuItem.IsEnabled = _isRunning;
        if (_syncNowMenuItem != null)
            _syncNowMenuItem.IsEnabled = _isRunning;
    }

    private async Task StartWatchingAsync()
    {
        if (_watcherService == null) return;

        if (await _watcherService.Start())
        {
            _isRunning = true;
            UpdateTrayTooltip();
            UpdateMenuState();
            _statusWindow?.SetRunningState(true);
            _statusWindow?.UpdateStatus("Running - Watching for changes");
        }
    }

    private void StopWatching()
    {
        _watcherService?.Stop();
        _isRunning = false;
        UpdateTrayTooltip();
        UpdateMenuState();
        _statusWindow?.SetRunningState(false);
        _statusWindow?.UpdateStatus("Stopped");
    }

    private void ShowStatusWindow()
    {
        if (_statusWindow == null || !_statusWindow.IsVisible)
        {
            _statusWindow = new StatusWindow(_watcherService!, _isRunning);
            _statusWindow.OnSettingsClicked += ShowSettingsWindow;
            _statusWindow.OnLogsClicked += ShowLogsWindow;
            _statusWindow.OnSyncNowClicked += async () => await _watcherService!.TriggerSyncNow();
            _statusWindow.OnStartClicked += async () => await StartWatchingAsync();
            _statusWindow.OnStopClicked += StopWatching;
        }
        _statusWindow.Show();
        _statusWindow.Activate();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow == null || !_settingsWindow.IsVisible)
        {
            _settingsWindow = new SettingsWindow(_watcherService!.Config);
            _settingsWindow.OnSettingsSaved += () =>
            {
                _watcherService!.Config.Save();
            };
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowLogsWindow()
    {
        if (_logsWindow == null || !_logsWindow.IsVisible)
        {
            _logsWindow = new LogsWindow();
        }
        _logsWindow.Show();
        _logsWindow.Activate();
    }

    private void ShowAboutWindow()
    {
        var aboutWindow = new AboutWindow();
        aboutWindow.Show();
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        // Wait a few seconds before checking to let the app settle
        await Task.Delay(5000);
        await CheckForUpdatesAsync(showNotification: false);
    }

    private async Task CheckForUpdatesAsync(bool showNotification)
    {
        if (_updateChecker == null) return;

        try
        {
            var updateInfo = await _updateChecker.CheckForUpdatesAsync();

            if (updateInfo != null && updateInfo.DownloadUrl != null)
            {
                // Update available - show dialog
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await ShowUpdateDialogAsync(updateInfo);
                });
            }
            else if (showNotification)
            {
                // No update available - only show if user manually checked
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var dialog = new Window
                    {
                        Title = "Check for Updates",
                        Width = 350,
                        Height = 150,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        CanResize = false
                    };

                    var panel = new StackPanel
                    {
                        Margin = new Avalonia.Thickness(20),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };

                    panel.Children.Add(new TextBlock
                    {
                        Text = $"You're running the latest version (v{VERSION})",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Margin = new Avalonia.Thickness(0, 0, 0, 20)
                    });

                    var okButton = new Button
                    {
                        Content = "OK",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Padding = new Avalonia.Thickness(30, 8)
                    };
                    okButton.Click += (s, e) => dialog.Close();
                    panel.Children.Add(okButton);

                    dialog.Content = panel;
                    dialog.Show();
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
            if (showNotification)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var dialog = new Window
                    {
                        Title = "Update Check Failed",
                        Width = 350,
                        Height = 150,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        CanResize = false
                    };

                    var panel = new StackPanel
                    {
                        Margin = new Avalonia.Thickness(20),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };

                    panel.Children.Add(new TextBlock
                    {
                        Text = "Failed to check for updates. Please try again later.",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(0, 0, 0, 20)
                    });

                    var okButton = new Button
                    {
                        Content = "OK",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Padding = new Avalonia.Thickness(30, 8)
                    };
                    okButton.Click += (s, e) => dialog.Close();
                    panel.Children.Add(okButton);

                    dialog.Content = panel;
                    dialog.Show();
                });
            }
        }
    }

    private Task ShowUpdateDialogAsync(UpdateInfo updateInfo)
    {
        var dialog = new Window
        {
            Title = "Update Available",
            Width = 450,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false
        };

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 10
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"A new version is available!",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 16,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"Current: v{updateInfo.CurrentVersion}  →  New: v{updateInfo.NewVersion}",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 10, 0, 10)
        });

        var progressText = new TextBlock
        {
            Text = "",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            IsVisible = false
        };
        panel.Children.Add(progressText);

        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 20,
            IsVisible = false
        };
        panel.Children.Add(progressBar);

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 10,
            Margin = new Avalonia.Thickness(0, 20, 0, 0)
        };

        var downloadButton = new Button
        {
            Content = "Download & Install",
            Padding = new Avalonia.Thickness(20, 8)
        };

        var laterButton = new Button
        {
            Content = "Later",
            Padding = new Avalonia.Thickness(20, 8)
        };

        laterButton.Click += (s, e) => dialog.Close();

        downloadButton.Click += async (s, e) =>
        {
            downloadButton.IsEnabled = false;
            laterButton.IsEnabled = false;
            progressText.IsVisible = true;
            progressBar.IsVisible = true;

            _updateChecker!.OnUpdateProgress += (msg) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    progressText.Text = msg;
                    // Try to parse percentage from message
                    if (msg.Contains("(") && msg.Contains("%)"))
                    {
                        var start = msg.LastIndexOf('(') + 1;
                        var end = msg.LastIndexOf('%');
                        if (int.TryParse(msg.Substring(start, end - start), out var percent))
                        {
                            progressBar.Value = percent;
                        }
                    }
                });
            };

            var downloadPath = await _updateChecker.DownloadUpdateAsync(updateInfo.DownloadUrl!, updateInfo.NewVersion);

            if (downloadPath != null)
            {
                dialog.Close();
                _updateChecker.LaunchInstaller(downloadPath);
                ExitApp();
            }
            else
            {
                progressText.Text = "Download failed. Please try again.";
                downloadButton.IsEnabled = true;
                laterButton.IsEnabled = true;
            }
        };

        buttonPanel.Children.Add(downloadButton);
        buttonPanel.Children.Add(laterButton);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;
        dialog.Show();
        return Task.CompletedTask;
    }

    private void ExitApp()
    {
        _watcherService?.Stop();
        _watcherService?.Dispose();

        _trayIcon?.Dispose();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
