using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace LastEpochHelper;

internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WM_HOTKEY = 0x0312;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    private static string TitleOf(IntPtr hwnd)
    {
        var text = new System.Text.StringBuilder(256);
        return GetWindowText(hwnd, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    /// <summary>Whether a visible top-level window has a title that passes <paramref name="wanted"/>.</summary>
    public static bool AnyWindowTitled(Func<string, bool> wanted)
    {
        bool found = false;
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && wanted(TitleOf(hwnd))) found = true;
            return !found;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Never take focus from the game, stay out of alt-tab, and optionally let clicks fall through.</summary>
    public static void ApplyOverlayStyle(IntPtr hwnd, bool clickThrough)
    {
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        if (clickThrough) style |= WS_EX_TRANSPARENT; else style &= ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    public static void BringToTop(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>Process id, screen rectangle and title of the window that currently has focus.</summary>
    public static (uint ProcessId, RECT Bounds, string Title) Foreground()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return (0, default, "");
        GetWindowThreadProcessId(hwnd, out uint pid);
        GetWindowRect(hwnd, out RECT rect);
        return (pid, rect, TitleOf(hwnd));
    }
}

/// <summary>Knows whether Last Epoch is running and whether it (or this tool) has focus.</summary>
internal sealed class GameWatcher
{
    private const string ProcessName = "Last Epoch";
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private HashSet<uint> _gamePids = new();
    private DateTime _lastScan = DateTime.MinValue;

    public bool Running { get; private set; }
    /// <summary>
    /// The game is streamed from GeForce NOW: it runs on Nvidia's machine and is shown here in a window
    /// of the GeForce NOW app. The screen can be read; its Player.log and filter folder are not on this PC.
    /// </summary>
    public bool Streamed { get; private set; }
    public bool GameFocused { get; private set; }
    public bool OwnFocused { get; private set; }
    public Native.RECT GameBounds { get; private set; }

    public void Refresh()
    {
        // Enumerating processes is comparatively slow; focus changes are checked every call.
        if (DateTime.UtcNow - _lastScan > TimeSpan.FromSeconds(3))
        {
            _lastScan = DateTime.UtcNow;
            var processes = Process.GetProcessesByName(ProcessName);
            _gamePids = processes.Select(p => (uint)p.Id).ToHashSet();
            foreach (var p in processes) p.Dispose();
            Streamed = _gamePids.Count == 0 && Native.AnyWindowTitled(IsStreamedGame);
            Running = _gamePids.Count > 0 || Streamed;
        }
        var (pid, bounds, title) = Native.Foreground();
        GameFocused = _gamePids.Contains(pid) || IsStreamedGame(title);
        OwnFocused = pid == _ownPid;
        if (GameFocused) GameBounds = bounds;
    }

    /// <summary>GeForce NOW titles its window after the game: "Last Epoch on GeForce NOW".</summary>
    internal static bool IsStreamedGame(string title) =>
        title.Contains("Last Epoch", StringComparison.OrdinalIgnoreCase) && title.Contains("GeForce NOW", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Observes key presses without consuming them, so the tree view can follow the game's own
/// "open passives" / "open skills" keys. Keys still reach the game exactly as before.
/// </summary>
internal sealed class KeyboardWatcher : IDisposable
{
    private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105;
    private const long RepeatGapMs = 700;
    /// <summary>Keys that are down, and when each last reported itself: Windows repeats a held key.</summary>
    private readonly Dictionary<int, long> _held = new();
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    private readonly HookProc _proc; // kept in a field so the delegate is not collected while hooked
    private readonly IntPtr _hook;

    /// <summary>A plain key (no Ctrl/Alt/Shift held) went down. Argument is the virtual-key code.</summary>
    public event Action<int>? KeyDown;

    public KeyboardWatcher()
    {
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int key = Marshal.ReadInt32(lParam);
            if (wParam == WM_KEYUP || wParam == WM_SYSKEYUP) _held.Remove(key);
            else if (wParam == WM_KEYDOWN)
            {
                // A key held a moment too long repeats, and a panel key counted twice opens and
                // closes the tree. (The time limit covers a key-up that never arrived.)
                long now = Environment.TickCount64;
                bool repeat = _held.TryGetValue(key, out long last) && now - last < RepeatGapMs;
                _held[key] = now;
                if (!repeat && !Held(VK_CONTROL) && !Held(VK_MENU) && !Held(VK_SHIFT)) KeyDown?.Invoke(key);
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool Held(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    public static int VirtualKey(string name) =>
        Enum.TryParse<Key>(name.Trim(), ignoreCase: true, out var key) ? KeyInterop.VirtualKeyFromKey(key) : 0;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
    }
}

/// <summary>System-wide hotkeys delivered to a window's message loop.</summary>
internal sealed class HotkeyManager : IDisposable
{
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager(HwndSource source)
    {
        _source = source;
        _source.AddHook(WndProc);
    }

    /// <summary>Registers e.g. "Ctrl+Shift+Right". Returns false if the text is invalid or the combo is taken.</summary>
    public bool Register(string combo, Action action)
    {
        if (!TryParse(combo, out uint mods, out uint vk)) return false;
        int id = _nextId++;
        if (!Native.RegisterHotKey(_source.Handle, id, mods | MOD_NOREPEAT, vk)) return false;
        _actions[id] = action;
        return true;
    }

    public void Clear()
    {
        foreach (var id in _actions.Keys) Native.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    internal static bool TryParse(string combo, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        foreach (var raw in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= MOD_CONTROL; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "alt": mods |= MOD_ALT; break;
                case "win": mods |= MOD_WIN; break;
                default:
                    if (vk != 0 || !Enum.TryParse<Key>(raw, ignoreCase: true, out var key)) return false;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return vk != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        Clear();
    }
}
