using System.Diagnostics;
using System.IO;
using System.Windows;
using LastEpochHelper.Core;

namespace LastEpochHelper;

public partial class App : Application
{
    private const string WaitArgument = "--wait-pid";
    private static Mutex? _singleInstance;

    public App()
    {
        // A UI glitch should never take the overlay down mid-session: log it and keep running.
        DispatcherUnhandledException += (_, e) =>
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LastEpochHelper");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{e.Exception}\n\n");
            }
            catch (IOException) { }
            e.Handled = true;
        };
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
