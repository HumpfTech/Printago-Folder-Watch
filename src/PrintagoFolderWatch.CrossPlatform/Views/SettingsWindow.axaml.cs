using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PrintagoFolderWatch.Core;

namespace PrintagoFolderWatch.CrossPlatform.Views;

public partial class SettingsWindow : Window
{
    private readonly Config _config;

    public event Action? OnSettingsSaved;

    public SettingsWindow(Config config)
    {
        InitializeComponent();
        _config = config;

        // Load current settings
        WatchPathText.Text = _config.WatchPath;
        ApiUrlText.Text = _config.ApiUrl;
        ApiKeyText.Text = _config.ApiKey;
        StoreIdText.Text = _config.StoreId;
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Watch Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            WatchPathText.Text = folders[0].Path.LocalPath;
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        _config.WatchPath = WatchPathText.Text ?? "";
        _config.ApiUrl = ApiUrlText.Text ?? "";
        _config.ApiKey = ApiKeyText.Text ?? "";
        _config.StoreId = StoreIdText.Text ?? "";

        OnSettingsSaved?.Invoke();
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
