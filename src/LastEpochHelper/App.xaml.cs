using System.Diagnostics;
using System.IO;
using System.Windows;
using LastEpochHelper.Core;

namespace LastEpochHelper;

public partial class App : Application
{
    private const string WaitArgument = "--wait-pid";
    private static Mutex? _singleInstance;

    /// <summary>Set once the overlay has started; until then an error means it never will.</summary>
    public static bool OverlayUp { get; set; }

    public App()
    {
        // A UI glitch should never take the overlay down mid-session: log it and keep running.
        // Before the overlay is up there is nothing to keep running: say so and quit, rather than
        // linger in Task Manager with no window.
        DispatcherUnhandledException += (_, e) =>
        {
            LogError("UI", e.Exception);
            e.Handled = true;
            if (!OverlayUp)
            {
                MessageBox.Show($"Last Epoch Helper could not start:\n{(e.Exception.InnerException ?? e.Exception).Message}\n\n"
                    + @"The details are in %AppData%\LastEpochHelper\errors.log. Please include that file if you report it.",
                    "Last Epoch Helper", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        };
        // Errors outside the UI thread cannot be survived, but they can at least leave a trace for a bug report.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogError("fatal", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogError("background", e.Exception);
            e.SetObserved();
        };
    }

    private static void LogError(string where, Exception? error)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LastEpochHelper");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{where}] version {Updater.Display(Updater.Current)}\n{error}\n\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        ActivityLog.Write($"ERROR [{where}] {error?.GetType().Name}: {error?.Message}");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Started by the updater: let the old copy finish exiting so its hotkeys and files are free.
        int index = Array.IndexOf(e.Args, WaitArgument);
        if (index >= 0 && index + 1 < e.Args.Length && int.TryParse(e.Args[index + 1], out int pid))
        {
            try { Process.GetProcessById(pid).WaitForExit(10_000); }
            catch (ArgumentException) { } // already gone
        }

        _singleInstance = new Mutex(initiallyOwned: true, "LastEpochHelper.SingleInstance", out bool first);
        if (!first)
        {
            // A second overlay would fight the first one for the hotkeys and the save files.
            Environment.Exit(0);
        }

        Updater.CleanUp(AppContext.BaseDirectory);
        base.OnStartup(e);
    }

    /// <summary>Starts the freshly installed version and closes this one.</summary>
    public static void RestartInto(string executable)
    {
        _singleInstance?.Dispose();
        _singleInstance = null;
        Process.Start(new ProcessStartInfo(executable, $"{WaitArgument} {Environment.ProcessId}") { UseShellExecute = false });
        Current.MainWindow?.Close();
    }
}
