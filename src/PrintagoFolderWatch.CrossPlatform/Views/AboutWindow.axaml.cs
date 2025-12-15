using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PrintagoFolderWatch.CrossPlatform.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        VersionText.Text = $"Version {App.VERSION}";
        CopyrightText.Text = $"© {DateTime.Now.Year} Humpf Tech LLC";
    }

    private void GitHub_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/HumpfTech/Printago-Folder-Watch",
            UseShellExecute = true
        });
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
