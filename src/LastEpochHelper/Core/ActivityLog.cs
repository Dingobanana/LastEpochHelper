using System.IO;

namespace LastEpochHelper.Core;

/// <summary>
/// A short running account of what the overlay saw and decided ("game shows Skills", "tree closed:
/// no panel in two reads"), kept in the data folder. It never leaves the machine by itself; a bug
/// report includes it, so a problem on someone else's computer can be followed step by step.
/// </summary>
public static class ActivityLog
{
    public const string FileName = "activity.log", OldFileName = "activity.old.log";
    private const long MaxBytes = 400_000;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> Last = new();
    private static string? _dir;

    public static void Start(string dataDir)
    {
        lock (Gate)
        {
            _dir = dataDir;
            Last.Clear();
        }
    }

    public static void Write(string message)
    {
        lock (Gate)
        {
            if (_dir is null) return;
            try
            {
                string path = Path.Combine(_dir, FileName);
                // Keep the previous stretch too, so the start of a long session is not lost at once.
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    File.Move(path, Path.Combine(_dir, OldFileName), overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Writes only when the message for this topic differs from the last one: for things checked several times a second.</summary>
    public static void Change(string topic, string message)
    {
        lock (Gate)
        {
            if (Last.GetValueOrDefault(topic) == message) return;
            Last[topic] = message;
        }
        Write(message);
    }
}
