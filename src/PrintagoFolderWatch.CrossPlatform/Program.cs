using System;
using System.Threading;
using Avalonia;
using Avalonia.ReactiveUI;

namespace PrintagoFolderWatch.CrossPlatform;

class Program
{
    private const string MutexName = "PrintagoFolderWatch_SingleInstance_8F4C3D2E";
    private static Mutex? _mutex;

    [STAThread]
    public static void Main(string[] args)
    {
        // Try to create a mutex to ensure single instance
        _mutex = new Mutex(true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            // Another instance is already running
            // On Windows, we could try to bring the existing window to front
            // For now, just exit silently
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}
