using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;

namespace RVTrainer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                DebugLogger.Initialize();
                DebugLogger.Log("Application starting...");
                desktop.MainWindow = new MainWindow();
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                System.Console.WriteLine($"COM Error (WinRT not available): {ex.Message}");
                DebugLogger.Error($"COM initialization failed: {ex.Message}", ex);
                // Try to continue anyway - window might still work
                try
                {
                    desktop.MainWindow = new MainWindow();
                }
                catch
                {
                    throw; // If it still fails, rethrow
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Fatal Error: {ex.Message}\n{ex.StackTrace}");
                DebugLogger.Error($"Fatal error during initialization: {ex.Message}", ex);
                throw;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
