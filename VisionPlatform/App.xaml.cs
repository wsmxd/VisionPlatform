using System.IO;
using System.Windows;
using VisionPlatform.Infrastructure;

namespace VisionPlatform;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            DumpCrash("UnhandledException", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            DumpCrash("DispatcherUnhandledException", args.Exception);
        };
        try
        {
            ServiceLocator.Initialize();
        }
        catch (Exception ex)
        {
            DumpCrash("Initialize", ex);
            throw;
        }
    }

    private static void DumpCrash(string source, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "AppData", "Logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ServiceLocator.Shutdown();
        base.OnExit(e);
    }
}
