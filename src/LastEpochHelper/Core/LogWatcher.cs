using System.IO;
using System.Text;

namespace LastEpochHelper.Core;

/// <summary>
/// Tails the game's Player.log without ever locking or writing it. The game truncates the file on
/// every launch, so a shrinking file means "start over from the top".
/// </summary>
public sealed class LogWatcher : IDisposable
{
    private readonly string _path;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private readonly StringBuilder _partial = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private long _position;
    private Task? _loop;

    /// <summary>Raised on a background thread. The bool is false for lines that already existed at startup.</summary>
    public event Action<LogEvent, bool>? Event;

    public LogWatcher(string path, TimeSpan? interval = null)
    {
        _path = path;
        _interval = interval ?? TimeSpan.FromMilliseconds(400);
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "Eleventh Hour Games", "Last Epoch", "Player.log");

    public void Start()
    {
        _loop = Task.Run(async () =>
        {
            ReadHistory();
            while (!_cts.IsCancellationRequested)
            {
                try { await Task.Delay(_interval, _cts.Token); } catch (OperationCanceledException) { break; }
                try { ReadNew(live: true, Raise); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        });
    }

    /// <summary>
    /// Replays what is already in the log, but only reports the latest state (character, last scene,
    /// last level) so a mid-session start knows where the player is without replaying every zone change.
    /// </summary>
    private void ReadHistory()
    {
        SceneLoadEvent? scene = null;
        CharacterLevelEvent? level = null;
        CharacterCreatedEvent? created = null;
        bool any = false;
        try
        {
            ReadNew(live: false, (e, _) =>
            {
                any = true;
                switch (e)
                {
                    case SceneLoadEvent s: scene = s; break;
                    case CharacterLevelEvent l: level = l; break;
                    case CharacterCreatedEvent c: created = c; break;
                    case CharacterSelectEvent: scene = null; level = null; created = null; break;
                }
            });
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        // Same order the game logs them in when a character enters the world.
        if (any) Raise(new CharacterSelectEvent(), false);
        if (created is not null) Raise(created, false);
        if (scene is not null) Raise(scene, false);
        if (level is not null) Raise(level, false);
    }

    private void Raise(LogEvent e, bool live) => Event?.Invoke(e, live);

    private void ReadNew(bool live, Action<LogEvent, bool> sink)
    {
        if (!File.Exists(_path)) return;
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length < _position)
        {
            _position = 0;
            _partial.Clear();
            _decoder.Reset();
        }
        if (fs.Length == _position) return;

        fs.Seek(_position, SeekOrigin.Begin);
        var bytes = new byte[64 * 1024];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        int read;
        while ((read = fs.Read(bytes, 0, bytes.Length)) > 0)
        {
            _position += read;
            int n = _decoder.GetChars(bytes, 0, read, chars, 0);
            for (int i = 0; i < n; i++)
            {
                char c = chars[i];
                if (c != '\n') { _partial.Append(c); continue; }
                var line = _partial.ToString().TrimEnd('\r');
                _partial.Clear();
                if (LogParser.Parse(line) is { } e) sink(e, live);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1000); } catch (AggregateException) { }
        _cts.Dispose();
    }
}
