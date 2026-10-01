using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LastEpochHelper.Core;

namespace LastEpochHelper;

public partial class MainWindow : Window
{
    private static readonly Brush PassiveBrush = Frozen("#FFD35C");
    private static readonly Brush IdolBrush = Frozen("#C79BFF");
    private static readonly Brush UnderLevelBrush = Frozen("#FF7A6B");
    private static readonly Brush DoneBrush = Frozen("#66625A");
    private static readonly Brush DimBrush = Frozen("#8F8A7A");
    private static readonly Brush BuildBrush = Frozen("#9FE08A");
    private static readonly Brush RewardRowBrush = Frozen("#1FFFD35C");

    private static readonly Dictionary<string, (string Glyph, Brush Brush)> TaskStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["main"] = ("▸", Frozen("#E8E4D8")),
        ["side"] = ("◆", Frozen("#7FB2FF")),
        ["boss"] = ("☠", Frozen("#FF7A6B")),
        ["go"] = ("➜", Frozen("#5FC9B8")),
        ["res"] = ("🛡", Frozen("#B9A2FF")),
        ["waypoint"] = ("⚑", Frozen("#5FC9B8")),
        ["tip"] = ("•", Frozen("#A9A493")),
        ["skip"] = ("✕", Frozen("#77736A")),
    };

    private readonly Session _session;
    private readonly GameWatcher _game = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<string> _hotkeyErrors = new();
    private LogWatcher? _watcher;
    private HotkeyManager? _hotkeys;
    private Tray? _tray;
    private MapWindow? _mapWindow;
    private SettingsWindow? _settingsWindow;
    private TreeWindow? _treeWindow;
    private PlannerWindow? _plannerWindow;
    private bool _plannerWanted;
    private bool _alertShown;
    private KeyboardWatcher? _keyboard;
    private bool _treeWanted;
    private ScreenReader? _screenReader;
    // Following the open skill tree: a quick look at where the heading was last seen, several times a
    // second, and a full read of the game window only when that spot stops showing a skill name.
    private readonly DispatcherTimer _followTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Native.RECT? _panelRegion;
    private int _panelMisses;
    private bool _forceFullRead;
    private DateTime _lastFullRead = DateTime.MinValue;
    private DateTime _lastPanelKey = DateTime.MinValue;
    private DateTime _verifyUntil = DateTime.MinValue;
    private GamePanel _expectedPanel;
    /// <summary>Opened by hand (hotkey or menu), so it stays until closed by hand.</summary>
    private bool _treePinned;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(6);
    private DateTime _lastUpdateCheck = DateTime.MinValue;
    private bool _updating;

    /// <summary>The newer release found on GitHub, if any.</summary>
    public ReleaseInfo? AvailableUpdate { get; private set; }
    private bool _reading;
    private bool _chatting;
    private IntPtr _hwnd;
    private bool _userHidden;
    private bool _capturing;
    private string? _flash;
    private int _ticks;
    private (string Path, DateTime Stamp, ImageSource Image)? _mapCache;

    private Settings Settings => _session.Settings;

    public MainWindow()
    {
        InitializeComponent();

        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var storage = new Storage();
        var guide = Guide.Load(Path.Combine(dataDir, "guide.json"));
        var scenes = new SceneMap(
            ReadSceneFile(Path.Combine(dataDir, "scenes.json")),
            ReadSceneFile(storage.PathOf(Session.LearnedScenesFile)));
        _session = new Session(storage, guide, scenes, EndgameData.Load(Path.Combine(dataDir, "endgame.json")));
        _session.Changed += Render;

        Left = Settings.Left;
        Top = Settings.Top;
        ApplyAppearance();

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        Render();
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static Dictionary<string, string> ReadSceneFile(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), Guide.JsonOptions) ?? new();
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.ApplyOverlayStyle(_hwnd, Settings.Locked);
        KeepOnScreen();

        _hotkeys = new HotkeyManager(HwndSource.FromHwnd(_hwnd)!);
        RegisterHotkeys();

        _tray = new Tray(ToggleVisible, ToggleLock, ToggleCompact, OpenSettings, Close);

        _timer.Tick += (_, _) => OnTimer();
        _timer.Start();
        _followTimer.Tick += (_, _) => WatchPanel();
        _followTimer.Start();

        string logPath = string.IsNullOrWhiteSpace(Settings.LogPath) ? LogWatcher.DefaultPath : Settings.LogPath;
        _watcher = new LogWatcher(logPath);
        _watcher.Event += (ev, live) => Dispatcher.BeginInvoke(() => _session.Handle(ev, live));
        _watcher.Start();

        Render();
        ShowWhatsNew();
    }

    // ------------------------------------------------------------------ updates

    /// <summary>After an update (or the first run of a version with this feature), list what changed.</summary>
    private void ShowWhatsNew()
    {
        string current = Updater.Display(Updater.Current);
        string last = Settings.LastRunVersion;
        if (last == current) return;
        Settings.LastRunVersion = current;
        _session.SaveSettings();
        // A brand-new install has nothing to compare with; existing users came from before 0.5.
        if (last.Length == 0 && _session.Store.Profiles.All(p => p.ClassId < 0)) return;
        if (!Updater.TryParseVersion(last, out var previous)) previous = new Version(0, 4, 0);

        var changes = Updater.ChangesSince(Updater.ParseChangelog(Updater.LoadBundledChangelog()), previous, Updater.Current);
        if (changes.Count > 0) new ChangelogWindow(changes, $"Updated to {current}").Show();
    }

    public void ShowChangelog() =>
        new ChangelogWindow(Updater.ParseChangelog(Updater.LoadBundledChangelog()), $"Version {Updater.Display(Updater.Current)}").Show();

    /// <summary>Asks GitHub for the latest release. Returns a short human-readable result.</summary>
    public async Task<string> CheckForUpdateAsync()
    {
        _lastUpdateCheck = DateTime.UtcNow;
        try
        {
            using var http = Updater.CreateClient();
            AvailableUpdate = await Updater.CheckAsync(http);
            Render();
            return AvailableUpdate is { } update
                ? $"Version {Updater.Display(update.Version)} is available (you have {Updater.Display(Updater.Current)})."
                : $"You have the latest version ({Updater.Display(Updater.Current)}).";
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            return "Could not reach GitHub: " + e.Message;
        }
    }

    /// <summary>Downloads the available update, swaps the files and restarts into the new version.</summary>
    public async Task<string> InstallUpdateAsync()
    {
        if (AvailableUpdate is not { } update || _updating) return "No update to install.";
        _updating = true;
        Render();
        try
        {
            using var http = Updater.CreateClient();
            string executable = await Updater.InstallAsync(update, AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), http);
            App.RestartInto(executable);
            return "Restarting...";
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or IOException
                                      or UnauthorizedAccessException or InvalidDataException)
        {
            _updating = false;
            Render();
            return $"Update failed: {e.Message}  You can download it by hand from {Updater.ReleasesPage}";
        }
    }

    private async void UpdateText_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (AvailableUpdate is not { } update || _updating) return;
        if (!Confirm($"Install version {Updater.Display(update.Version)} now?\n\nThe overlay closes for a few seconds and starts again. Your progress is kept.")) return;
        string result = await InstallUpdateAsync();
        if (_updating) return; // restarting
        _flash = result;
        Render();
    }

    private async void CheckForUpdateInBackground()
    {
        if (!Settings.AutoCheckUpdates || _updating || DateTime.UtcNow - _lastUpdateCheck < UpdateInterval) return;
        await CheckForUpdateAsync();
    }

    // ------------------------------------------------------------------ hotkeys and toggles

    private void RegisterHotkeys()
    {
        _hotkeys!.Clear();
        _hotkeyErrors.Clear();
        Register(Settings.HotkeyNext, () => _session.Tracker.Next());
        Register(Settings.HotkeyPrev, () => _session.Tracker.Prev());
        Register(Settings.HotkeyToggle, ToggleVisible);
        Register(Settings.HotkeyLock, ToggleLock);
        Register(Settings.HotkeyCompact, ToggleCompact);
        Register(Settings.HotkeyMap, CycleMapMode);
        Register(Settings.HotkeyCapture, CaptureMap);
        Register(Settings.HotkeyTree, () => { _treePinned = !_treeWanted; ShowTree(!_treeWanted); });
        Register(Settings.HotkeyPlanner, () => ShowPlanner(!_plannerWanted));
        Register(Settings.HotkeyLookup, LookUpItem);

        _keyboard?.Dispose();
        _keyboard = null;
        if (Settings.FollowGameKeys)
        {
            _keyboard = new KeyboardWatcher();
            // Never do the work inside the hook callback: Windows forbids outgoing COM calls there (which
            // broke loading the icon sheet) and drops hooks that take too long.
            _keyboard.KeyDown += key => Dispatcher.BeginInvoke(() => OnGameKey(key));
        }

        void Register(string combo, Action action)
        {
            if (string.IsNullOrWhiteSpace(combo)) return;
            if (!_hotkeys.Register(combo, action)) _hotkeyErrors.Add(combo);
        }
    }

    private void ToggleVisible()
    {
        _userHidden = IsVisible;
        UpdateVisibility();
    }

    private void ToggleLock()
    {
        Settings.Locked = !Settings.Locked;
        Native.ApplyOverlayStyle(_hwnd, Settings.Locked);
        SaveSettings();
        Render();
    }

    /// <summary>Cycles box -> compact box -> bar across the screen -> box.</summary>
    private void ToggleCompact()
    {
        if (Settings.BarLayout) { Settings.BarLayout = false; Settings.Compact = false; }
        else if (Settings.Compact) { Settings.Compact = false; Settings.BarLayout = true; }
        else Settings.Compact = true;
        ApplyAppearance();
        SaveSettings();
        Render();
    }

    private void Layout_Click(object sender, RoutedEventArgs e)
    {
        Settings.BarLayout = false;
        ApplyAppearance();
        SaveSettings();
        Render();
    }

    // ------------------------------------------------------------------ item check

    /// <summary>
    /// Reads the tooltip under the mouse and says which of the build's affixes are on the item.
    /// The game shows the tooltip; this only reads the picture of it.
    /// </summary>
    private async void LookUpItem()
    {
        if (_reading) return;
        _game.Refresh();
        if (!_game.GameFocused) { _session.ShowAlert("Item check: hover an item in Last Epoch first."); return; }
        if (_session.Tree?.StageFor(_session.Profile.Level) is not { } stage)
        {
            _session.ShowAlert("Item check needs an imported build (settings: Import from Maxroll).");
            return;
        }
        _screenReader ??= new ScreenReader();
        if (!_screenReader.Available) { _session.ShowAlert("Item check: Windows text recognition is not available."); return; }

        _reading = true;
        try
        {
            // Tooltips open beside the cursor; a generous box around it catches them on either side.
            var cursor = System.Windows.Forms.Cursor.Position;
            var game = _game.GameBounds;
            var box = new Native.RECT
            {
                Left = Math.Max(game.Left, cursor.X - 750), Right = Math.Min(game.Right, cursor.X + 750),
                Top = Math.Max(game.Top, cursor.Y - 800), Bottom = Math.Min(game.Bottom, cursor.Y + 800),
            };
            var masks = new List<Native.RECT> { ScreenRect(this) };
            if (_treeWindow is not null) masks.Add(ScreenRect(_treeWindow));
            if (_plannerWindow is not null) masks.Add(ScreenRect(_plannerWindow));
            var lines = await _screenReader.ReadAsync(box, masks);
            _session.ShowAlert(ItemCheck.Describe(lines, stage), 20);
        }
        finally { _reading = false; }
    }

    // ------------------------------------------------------------------ planner

    private void ShowPlanner(bool show)
    {
        _plannerWanted = show;
        if (show)
        {
            if (_plannerWindow is null)
            {
                _plannerWindow = new PlannerWindow(_session);
                _plannerWindow.Moved += () =>
                {
                    Settings.PlannerLeft = _plannerWindow.Left;
                    Settings.PlannerTop = _plannerWindow.Top;
                    SaveSettings();
                };
                _plannerWindow.CloseRequested += () => ShowPlanner(false);
                _plannerWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                _plannerWindow.Left = Usable(Settings.PlannerLeft) ?? Math.Max(0, (SystemParameters.PrimaryScreenWidth - 600) / 2);
                _plannerWindow.Top = Usable(Settings.PlannerTop) ?? 80;
            }
            _plannerWindow.Render();
        }
        UpdateVisibility();
    }

    private void Planner_Click(object sender, RoutedEventArgs e) => ShowPlanner(!_plannerWanted);

    /// <summary>A saved window coordinate, unless it was never set (or is a NaN left by an older version).</summary>
    private static double? Usable(double? value) => value is { } v && double.IsFinite(v) ? v : null;

    private void CycleMapMode()
    {
        Settings.MapMode = (Settings.MapMode + 1) % 3;
        _flash = Settings.MapMode switch { 0 => "Zone map: off", 1 => "Zone map: in overlay", _ => "Zone map: large" };
        SaveSettings();
        Render();
    }

    // ------------------------------------------------------------------ tree view

    private void ShowTree(bool show, string? kind = null)
    {
        _treeWanted = show;
        if (!show) { _panelRegion = null; _panelMisses = 0; }
        if (show)
        {
            if (_treeWindow is null)
            {
                _treeWindow = new TreeWindow(_session);
                _treeWindow.Moved += () =>
                {
                    Settings.TreeLeft = _treeWindow.Left;
                    Settings.TreeTop = _treeWindow.Top;
                    SaveSettings();
                };
                _treeWindow.CloseRequested += () => { _treePinned = false; ShowTree(false); };
                _treeWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                _treeWindow.Left = Usable(Settings.TreeLeft) ?? Math.Max(0, (SystemParameters.PrimaryScreenWidth - 900) / 2);
                _treeWindow.Top = Usable(Settings.TreeTop) ?? 40;
            }
            if (kind is not null) _treeWindow.SelectKind(kind);
            _treeWindow.Render();
        }
        UpdateVisibility();
    }

    /// <summary>
    /// Keeps the tree view in step with the game by reading the screen: is the passive panel or a
    /// skill tree showing, and which skill. Runs several times a second; normally it only looks at
    /// the small area where the panel's heading was last seen, and reads the whole game window just
    /// after a panel key was pressed or when that area stops showing the heading.
    /// </summary>
    private async void WatchPanel()
    {
        if (_reading || !Settings.FollowSkillOnScreen || !_game.GameFocused || _session.Tree is null) return;
        bool verifying = DateTime.UtcNow < _verifyUntil;
        bool shown = _treeWanted && _treeWindow is { IsVisible: true };
        // Nothing to mirror while the tree is closed, unless a panel key was just pressed.
        if (!shown && !verifying) return;
        _screenReader ??= new ScreenReader();
        if (!_screenReader.Available) return;

        // While a panel is open, look at the whole window now and then to pick up newly spent points.
        if (shown && DateTime.UtcNow - _lastFullRead > TimeSpan.FromSeconds(1.5)) _forceFullRead = true;
        bool quick = shown && _panelRegion is not null && !_forceFullRead;
        // A panel this machine has never recognised is not worth reading the whole screen for, over and over.
        if (!quick && !verifying && !SeenOnScreen(_treeWindow?.CurrentKind)) return;
        // A full read of an ultrawide takes a few hundred milliseconds; do not chain them back to back.
        if (!quick && DateTime.UtcNow - _lastFullRead < TimeSpan.FromMilliseconds(450)) return;
        // Give the game a moment to draw the panel after the key press.
        if (!quick && DateTime.UtcNow - _lastPanelKey < TimeSpan.FromMilliseconds(250)) return;

        _reading = true;
        try
        {
            var masks = new List<Native.RECT> { ScreenRect(this) };
            if (_treeWindow is not null) masks.Add(ScreenRect(_treeWindow));
            if (_plannerWindow is not null) masks.Add(ScreenRect(_plannerWindow));
            if (!quick) { _lastFullRead = DateTime.UtcNow; _forceFullRead = false; }

            var tabs = (_treeWindow?.PassiveTabNames ?? PassiveTabNamesOf(_session.Tree)).ToList();
            var skills = _session.Tree.Trees.Where(t => t.Kind == TreeDef.SkillKind).Select(t => t.Name).ToList();
            List<ScreenLine> lines, words = new();
            if (quick) lines = await _screenReader.ReadAsync(_panelRegion!.Value, masks);
            else (lines, words) = await _screenReader.ReadBothAsync(_game.GameBounds, masks);
            var reading = PanelDetector.Detect(lines, tabs, skills, _expectedPanel);
            if (!quick && verifying) WritePanelDiagnostics(lines, reading);

            if (reading.Panel != GamePanel.None)
            {
                _panelMisses = 0;
                if (reading.Panel == GamePanel.Passives && !Settings.PanelSeenPassives) { Settings.PanelSeenPassives = true; SaveSettings(); }
                if (reading.Panel == GamePanel.Skills && !Settings.PanelSeenSkills) { Settings.PanelSeenSkills = true; SaveSettings(); }
                if (!quick) _panelRegion = reading.Anchor is { } anchor ? RegionAround(anchor) : null;

                string kind = reading.Panel == GamePanel.Passives ? TreeDef.PassiveKind : TreeDef.SkillKind;
                if (!_treeWanted) ShowTree(true, kind);                       // the game opened a panel we missed
                else if (_treeWindow!.CurrentKind != kind) _treeWindow.SelectKind(kind);
                if (reading.Skill is not null) _treeWindow?.SelectSkill(reading.Skill);
                if (!quick) ReadNodePoints(words, reading);
            }
            else if (quick)
            {
                // The heading left its spot: the panel closed or changed. Look at everything next time.
                if (++_panelMisses >= 2) { _panelRegion = null; _panelMisses = 0; _forceFullRead = true; }
            }
            else if (shown && SeenOnScreen(_treeWindow!.CurrentKind) && !_treePinned && ++_panelMisses >= 2)
            {
                // Two full reads without any panel: it is closed in the game, so close here too.
                _panelMisses = 0;
                ShowTree(false);
            }
        }
        finally { _reading = false; }
    }

    /// <summary>
    /// Takes the "2/6" labels under the game's nodes and stores them as the character's real points.
    /// For passives the labels themselves say which tab is showing; for a skill, its heading does.
    /// </summary>
    private void ReadNodePoints(List<ScreenLine> words, PanelReading reading)
    {
        if (!Settings.ReadPointsFromScreen || _session.Tree is not { } build) return;
        var tokens = TreeReader.Tokens(words);
        if (tokens.Count < 4) return;

        if (reading.Panel == GamePanel.Passives)
        {
            if (TreeReader.ReadBest(tokens, build.Trees.Where(t => t.Kind == TreeDef.PassiveKind)) is { } fit)
            {
                _session.SetReadPoints(fit.Tree, fit.Points);
                // Show the same tab the game is showing.
                _treeWindow?.SelectTab(fit.Tree);
            }
        }
        else if (reading.Skill is not null && build.Trees.FirstOrDefault(t => t.Kind == TreeDef.SkillKind && t.Name == reading.Skill) is { } skill
                 && TreeReader.Read(tokens, skill) is { } points)
            _session.SetReadPoints(skill, points);
    }

    /// <summary>Has the game's panel for this kind of tree ever been recognised here?</summary>
    private bool SeenOnScreen(string? kind) =>
        kind == TreeDef.PassiveKind ? Settings.PanelSeenPassives : kind == TreeDef.SkillKind && Settings.PanelSeenSkills;

    private static IEnumerable<string> PassiveTabNamesOf(BuildTree build) =>
        build.PassiveTabNames.Count > 0 ? build.PassiveTabNames : build.Trees.Where(t => t.Kind == TreeDef.PassiveKind).Select(t => t.Name);

    /// <summary>A band around a heading, big enough for the reader and for longer names in the same place.</summary>
    private Native.RECT RegionAround(ScreenLine line)
    {
        var game = _game.GameBounds;
        var region = new Native.RECT
        {
            Left = Math.Max(game.Left, (int)(line.X - 350)),
            Right = Math.Min(game.Right, (int)(line.X + line.Width + 350)),
            Top = Math.Max(game.Top, (int)(line.Y - line.Height * 1.5)),
            Bottom = Math.Min(game.Bottom, (int)(line.Y + line.Height * 3)),
        };
        if (region.Bottom - region.Top < 220) region.Bottom = Math.Min(game.Bottom, region.Top + 220);
        if (region.Right - region.Left < 220) region.Right = Math.Min(game.Right, region.Left + 220);
        return region;
    }

    /// <summary>
    /// Leaves what the reader saw right after a panel key in panel-ocr.txt (and, once per panel, a
    /// picture of the game window) in the data folder. Local only; it is how detection gets tuned.
    /// </summary>
    private void WritePanelDiagnostics(List<ScreenLine> lines, PanelReading reading)
    {
        try
        {
            string name = _expectedPanel == GamePanel.Skills ? "skills" : "passives";
            var text = new List<string> { $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  key={name}  detected={reading.Panel}  skill={reading.Skill}" };
            text.AddRange(lines.OrderByDescending(l => l.Height).Take(120).Select(l => $"{l.Height,4:0} @{l.X,5:0},{l.Y,5:0}  {l.Text}"));
            File.WriteAllLines(Path.Combine(_session.DataDir, $"panel-ocr-{name}.txt"), text);

            string picture = Path.Combine(_session.DataDir, $"panel-{name}.png");
            if (!File.Exists(picture)) ScreenCapture.Save(_game.GameBounds, picture, maxWidth: int.MaxValue);
        }
        catch (IOException) { }
    }

    private static Native.RECT ScreenRect(Window window)
    {
        if (!window.IsVisible) return default;
        var topLeft = window.PointToScreen(new Point(0, 0));
        var bottomRight = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
        return new Native.RECT { Left = (int)topLeft.X, Top = (int)topLeft.Y, Right = (int)bottomRight.X, Bottom = (int)bottomRight.Y };
    }

    /// <summary>
    /// The game's panel keys. A key press shows or hides the tree at once, so it feels immediate;
    /// <see cref="WatchPanel"/> then checks the screen and corrects it if the game did something
    /// else (the panel was already closed with the mouse, the key went to a text box, ...).
    /// </summary>
    private void OnGameKey(int key)
    {
        if (!_game.GameFocused) return;
        const int Enter = 0x0D, Escape = 0x1B;
        bool trustScreen = Settings.FollowSkillOnScreen && SeenOnScreen(_treeWindow?.CurrentKind);

        if (key == Enter) { _chatting = !_chatting; return; }
        if (key == Escape)
        {
            if (_chatting) { _chatting = false; return; }
            if (!_treeWanted || _treePinned) return;
            // Escape closes one panel at a time in the game; let the screen say whether ours went.
            if (trustScreen) { _forceFullRead = true; _lastPanelKey = DateTime.UtcNow; }
            else ShowTree(false);
            return;
        }

        string? kind = key == KeyboardWatcher.VirtualKey(Settings.GameKeyPassives) ? TreeDef.PassiveKind
            : key == KeyboardWatcher.VirtualKey(Settings.GameKeySkills) ? TreeDef.SkillKind : null;
        if (kind is null) return;

        _expectedPanel = kind == TreeDef.PassiveKind ? GamePanel.Passives : GamePanel.Skills;
        _lastPanelKey = DateTime.UtcNow;
        _verifyUntil = DateTime.UtcNow.AddSeconds(2.5);
        _forceFullRead = true;
        _panelMisses = 0;
        // While typing in chat the key is just a letter - unless the screen later shows a panel.
        if (_chatting) return;

        _treePinned = false;
        // Pressing the same panel's key again closes it in the game, so close here too.
        if (_treeWanted && _treeWindow?.CurrentKind == kind) ShowTree(false);
        else ShowTree(true, kind);
    }

    /// <summary>Screenshots the game (open the in-game map first) and files it under the current zone.</summary>
    private async void CaptureMap()
    {
        if (_capturing) return;
        _game.Refresh();
        if (!_game.GameFocused)
        {
            _flash = "Map capture: Last Epoch must be the focused window";
            Render();
            return;
        }
        _capturing = true;
        try
        {
            // Keep the overlay itself out of the picture.
            Hide();
            _mapWindow?.Hide();
            await Task.Delay(150);
            bool saved = ScreenCapture.Save(_game.GameBounds, MapPath(_session.Tracker.Step));
            _mapCache = null;
            _flash = saved ? $"Saved map for {_session.Tracker.Step.Zone}" : "Map capture failed";
            if (saved && Settings.MapMode == 0) Settings.MapMode = 1;
        }
        finally
        {
            _capturing = false;
            UpdateVisibility();
            Render();
        }
    }

    // ------------------------------------------------------------------ periodic work

    private void OnTimer()
    {
        double elapsed = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        _game.Refresh();
        // A long gap means the machine slept; that is not play time.
        if (_game.Running && elapsed < 5) _session.Tick(elapsed);

        UpdateVisibility();
        // Checked even while hidden, so the notice is waiting when the game gets focus again.
        CheckForUpdateInBackground();
        if (!IsVisible) return;
        if (_alertShown && _session.Alert is null) Render(); // the alert ran out
        RenderTimer();
        // Borderless games occasionally jump above topmost windows; re-assert without taking focus.
        if (++_ticks % 4 == 0) Native.BringToTop(_hwnd);
    }

    private void UpdateVisibility()
    {
        if (_capturing) return;
        bool focusOk = !Settings.AutoHide || !_game.Running || _game.GameFocused || _game.OwnFocused;
        bool show = !_userHidden && focusOk;
        if (show && !IsVisible) { Show(); Native.BringToTop(_hwnd); }
        else if (!show && IsVisible) Hide();
        UpdateMapWindow();

        if (_plannerWindow is not null)
        {
            bool showPlanner = _plannerWanted && focusOk;
            if (showPlanner && !_plannerWindow.IsVisible) _plannerWindow.Show();
            else if (!showPlanner && _plannerWindow.IsVisible) _plannerWindow.Hide();
        }

        if (_treeWindow is not null)
        {
            bool showTree = _treeWanted && focusOk;
            if (showTree && !_treeWindow.IsVisible) _treeWindow.Show();
            else if (!showTree && _treeWindow.IsVisible) _treeWindow.Hide();
        }
    }

    // ------------------------------------------------------------------ rendering

    private void ApplyAppearance()
    {
        Width = Settings.BarLayout ? Math.Clamp(Settings.BarWidth, 500, 3000) : Math.Clamp(Settings.Width, 260, 900);
        Panel.Visibility = Settings.BarLayout ? Visibility.Collapsed : Visibility.Visible;
        Bar.Visibility = Settings.BarLayout ? Visibility.Visible : Visibility.Collapsed;
        Bar.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(Settings.Opacity * 255, 40, 255), 0x0E, 0x0F, 0x14));
        FontSize = Math.Clamp(Settings.FontSize, 9, 28);
        ZoneText.FontSize = FontSize + 5;
        byte alpha = (byte)Math.Clamp(Settings.Opacity * 255, 40, 255);
        Panel.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x0E, 0x0F, 0x14));
    }

    private void Render()
    {
        var tracker = _session.Tracker;
        var chapter = tracker.Chapter;
        var step = tracker.Step;
        int? level = _session.Profile.Level > 0 ? _session.Profile.Level : null;

        ChapterText.Text = $"{chapter.Title} · {chapter.Era}  ({tracker.IndexInChapter + 1}/{chapter.Steps.Count})";
        string? alert = _session.Alert;
        AlertSection.Visibility = alert is null ? Visibility.Collapsed : Visibility.Visible;
        AlertText.Text = alert ?? "";
        _alertShown = alert is not null;
        UpdateText.Visibility = AvailableUpdate is null || Settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        if (AvailableUpdate is { } update)
            UpdateText.Text = _updating ? $"Downloading version {Updater.Display(update.Version)}..."
                : $"⬆ Version {Updater.Display(update.Version)} is available - click to install";
        ZoneText.Text = step.Zone;
        WaypointText.Visibility = step.Waypoint ? Visibility.Visible : Visibility.Collapsed;

        Details.Visibility = Settings.Compact ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Visibility = Settings.Compact ? Visibility.Collapsed : Visibility.Visible;

        TaskList.Children.Clear();
        BossList.Children.Clear();
        for (int i = 0; i < step.Tasks.Count; i++)
        {
            var task = step.Tasks[i];
            if (task.Type.Equals("boss", StringComparison.OrdinalIgnoreCase))
            {
                BossList.Children.Add(BuildBossLine(task.Text));
                continue;
            }
            string key = Session.Key(tracker.Index, i);
            TaskList.Children.Add(BuildTaskRow(task, _session.IsDone(key), () => _session.ToggleDone(key)));
        }
        BossSection.Visibility = BossList.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var (passive, idol, pending) = _session.Rewards();
        PendingList.Children.Clear();
        foreach (var reward in pending.TakeLast(4))
        {
            var row = BuildTaskRow(
                new GuideTask { Type = "side", Text = $"{reward.Task.Quest ?? reward.Task.Text}  ({reward.Zone})", Passive = reward.Task.Passive, Idol = reward.Task.Idol },
                done: false, () => _session.ToggleDone(reward.Key));
            row.MouseRightButtonUp += (_, e) => { _session.SkipReward(reward.Key); e.Handled = true; };
            PendingList.Children.Add(row);
        }
        PendingSection.Visibility = pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        RenderBuild(level);
        RenderMap(step);

        NextText.Text = tracker.NextStep is { } next ? $"Next ▸ {next.Zone}" : "End of the guide";

        RewardText.Inlines.Clear();
        RewardText.Inlines.Add(new Run($"Passives {passive}/{_session.Guide.PassiveCap}") { Foreground = PassiveBrush });
        RewardText.Inlines.Add(new Run("   "));
        RewardText.Inlines.Add(new Run($"Idol slots {idol}/{_session.Guide.IdolCap}") { Foreground = IdolBrush });

        LevelText.Inlines.Clear();
        if (level is { } lvl) LevelText.Inlines.Add(new Run($"You {lvl}"));
        if (step.Level > 0)
        {
            // Being well under the zone level is the usual reason the campaign starts to hurt.
            bool under = level is { } l && l < step.Level - 2;
            LevelText.Inlines.Add(new Run($"{(level is null ? "" : " · ")}Zone {step.Level}") { Foreground = under ? UnderLevelBrush : LevelText.Foreground });
        }

        Buttons.Visibility = Settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        Header.Cursor = Settings.Locked ? Cursors.Arrow : Cursors.SizeAll;
        RenderTimer();
        StatusText.Text = BuildStatus();
        if (Settings.BarLayout) RenderBar(step, passive, idol, level);
        if (_treeWanted) _treeWindow?.Render();
        if (_plannerWanted) _plannerWindow?.Render();
    }

    /// <summary>The whole step on one line: zone, what to do, where to go, boss, counters.</summary>
    private void RenderBar(GuideStep step, int passive, int idol, int? level)
    {
        var tracker = _session.Tracker;
        BarZone.Text = $"{tracker.Chapter.Id}.{tracker.IndexInChapter + 1}  {step.Zone}";
        BarText.Inlines.Clear();
        bool first = true;
        for (int i = 0; i < step.Tasks.Count; i++)
        {
            var task = step.Tasks[i];
            if (task.Type is "tip" or "skip" or "res" or "boss") continue; // bosses get their own row below; details stay in the box layout
            if (_session.IsDone(Session.Key(tracker.Index, i))) continue;
            var (glyph, brush) = TaskStyles.TryGetValue(task.Type, out var style) ? style : TaskStyles["main"];
            if (!first) BarText.Inlines.Add(new Run("    "));
            first = false;
            BarText.Inlines.Add(new Run($"{glyph} {task.Text}") { Foreground = brush });
            if (task.Passive > 0) BarText.Inlines.Add(new Run($" +{task.Passive}P") { Foreground = PassiveBrush, FontWeight = FontWeights.Bold });
            if (task.Idol > 0) BarText.Inlines.Add(new Run(" +Idol") { Foreground = IdolBrush, FontWeight = FontWeights.Bold });
        }
        if (tracker.NextStep is { } next)
            BarText.Inlines.Add(new Run($"{(first ? "" : "    ")}▸ next: {next.Zone}") { Foreground = DimBrush });

        BarRight.Inlines.Clear();
        BarRight.Inlines.Add(new Run($"{passive}/{_session.Guide.PassiveCap}") { Foreground = PassiveBrush });
        BarRight.Inlines.Add(new Run("  "));
        BarRight.Inlines.Add(new Run($"{idol}/{_session.Guide.IdolCap}") { Foreground = IdolBrush });
        if (level is { } lvl)
            BarRight.Inlines.Add(new Run($"   lvl {lvl}{(step.Level > 0 ? $" / zone {step.Level}" : "")}")
                { Foreground = step.Level > 0 && lvl < step.Level - 2 ? UnderLevelBrush : BarRight.Foreground });
        if (Settings.ShowTimer) BarRight.Inlines.Add(new Run($"   ⏱ {Clock(_session.Profile.PlaySeconds)}"));
        BarButtons.Visibility = Settings.Locked ? Visibility.Collapsed : Visibility.Visible;

        BarBoss.Children.Clear();
        foreach (var boss in step.Tasks.Where(t => t.Type == "boss"))
            BarBoss.Children.Add(BuildBossLine(boss.Text));
        BarBoss.Visibility = BarBoss.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BarAlert.Text = _session.Alert ?? "";
        BarAlert.Visibility = _session.Alert is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>"Name - what to watch for" with the name standing out.</summary>
    private static TextBlock BuildBossLine(string text)
    {
        var line = new TextBlock { TextWrapping = TextWrapping.Wrap };
        int split = text.IndexOf(" - ", StringComparison.Ordinal);
        line.Inlines.Add(new Run("☠ " + (split < 0 ? text : text[..split])) { Foreground = Frozen("#FF9C8F"), FontWeight = FontWeights.SemiBold });
        if (split >= 0) line.Inlines.Add(new Run("  " + text[(split + 3)..]) { Foreground = Frozen("#E8E4D8") });
        return line;
    }

    private void RenderBuild(int? level)
    {
        BuildList.Children.Clear();
        BuildSection.Visibility = Visibility.Collapsed;
        if (!Settings.ShowBuild || level is not { } lvl) return;

        // The build's own per-level lines are optional (the tree view shows them as a picture);
        // the reminders - skill slots, resistances, loot filter switches - always belong here.
        var shownPlan = Settings.ShowBuildLines ? _session.Plan : null;
        var (due, next) = BuildPlan.View(shownPlan, lvl, _session.Profile.PlanDone, extra: _session.FilterEntries());
        if (due.Count == 0 && next is null) return;

        BuildTitle.Text = (shownPlan is { } plan ? $"BUILD  ·  {plan.Name}" : "REMINDERS FOR YOUR LEVEL") + "  ·  click = done, right-click = done incl. earlier";
        foreach (var entry in due)
        {
            var row = BuildTaskRow(new GuideTask { Type = "main", Text = entry.Text }, done: false, () => _session.TogglePlanDone(entry.Key));
            ((TextBlock)((DockPanel)row.Child).Children[1]).Foreground = BuildBrush;
            ((TextBlock)((DockPanel)row.Child).Children[0]).Foreground = BuildBrush;
            row.MouseRightButtonUp += (_, e) => { _session.TickPlanThrough(entry.Level); e.Handled = true; };
            BuildList.Children.Add(row);
        }
        if (next is not null)
            BuildList.Children.Add(new TextBlock
            {
                Text = $"At level {next.Level}: {next.Text}",
                Foreground = DimBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 2, 0, 0),
            });
        BuildSection.Visibility = Visibility.Visible;
    }

    private string MapPath(GuideStep step)
    {
        string name = string.Concat(step.Key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_session.MapsDir, name + ".png");
    }

    private ImageSource? LoadMap(GuideStep step)
    {
        string path = MapPath(step);
        if (!File.Exists(path)) return null;
        DateTime stamp = File.GetLastWriteTimeUtc(path);
        if (_mapCache is { } cached && cached.Path == path && cached.Stamp == stamp) return cached.Image;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // read fully so the file is not kept open
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            _mapCache = (path, stamp, image);
            return image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    private void RenderMap(GuideStep step)
    {
        var image = Settings.MapMode == 0 ? null : LoadMap(step);
        MapImage.Source = Settings.MapMode == 1 ? image : null;
        MapSection.Visibility = MapImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateMapWindow();
    }

    private void UpdateMapWindow()
    {
        var image = Settings.MapMode == 2 && IsVisible && !Settings.Compact ? LoadMap(_session.Tracker.Step) : null;
        if (image is null) { _mapWindow?.Hide(); return; }
        _mapWindow ??= new MapWindow();
        _mapWindow.Display(image);
    }

    private void RenderTimer()
    {
        if (!Settings.ShowTimer)
        {
            TimerText.Visibility = Visibility.Collapsed;
            return;
        }
        var profile = _session.Profile;
        var (chapter, best) = _session.ChapterTimes();
        string text = $"⏱ {Clock(profile.PlaySeconds)}";
        if (chapter > 0) text += $"  ·  chapter {Clock(chapter)}";
        if (best is { } b) text += $" (best {Clock(b)})";
        if (profile.Deaths > 0) text += $"  ·  ☠ {profile.Deaths}";
        TimerText.Text = text;
        TimerText.Visibility = Visibility.Visible;
    }

    private static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private string BuildStatus()
    {
        var parts = new List<string> { _session.Profile.Name };
        var tracker = _session.Tracker;
        if (_flash is { } flash) { parts.Add(flash); _flash = null; }
        else if (_session.Notice is { } notice) parts.Add(notice);
        else if (tracker.LastEvent is { } ev) parts.Add(ev);
        else if (tracker.CurrentScene is { } scene && tracker.CurrentSceneZone is null) parts.Add($"{scene}: unknown zone");

        if (_hotkeyErrors.Count > 0) parts.Add("Hotkey in use: " + string.Join(", ", _hotkeyErrors));
        else if (Settings.Locked) parts.Add($"{Settings.HotkeyLock} to unlock");
        return string.Join("  ·  ", parts);
    }

    private Border BuildTaskRow(GuideTask task, bool done, Action onClick)
    {
        var (glyph, brush) = TaskStyles.TryGetValue(task.Type, out var style) ? style : TaskStyles["main"];

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = done ? DoneBrush : brush };
        text.Inlines.Add(new Run(task.Text));
        if (task.Type.Equals("tip", StringComparison.OrdinalIgnoreCase)) text.FontStyle = FontStyles.Italic;
        if (done) text.TextDecorations = TextDecorations.Strikethrough;
        if (task.Passive > 0)
            text.Inlines.Add(new Run($"  +{task.Passive} Passive") { Foreground = done ? DoneBrush : PassiveBrush, FontWeight = FontWeights.Bold });
        if (task.Idol > 0)
            text.Inlines.Add(new Run($"  +{task.Idol} Idol slot{(task.Idol > 1 ? "s" : "")}") { Foreground = done ? DoneBrush : IdolBrush, FontWeight = FontWeights.Bold });

        var icon = new TextBlock
        {
            Text = done ? "✔" : glyph,
            Foreground = done ? DoneBrush : brush,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Width = 20,
            TextAlignment = TextAlignment.Center,
        };

        var row = new DockPanel { Background = Brushes.Transparent };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);

        var border = new Border
        {
            Child = row,
            Padding = new Thickness(0, 2, 4, 2),
            CornerRadius = new CornerRadius(3),
            Background = task.HasReward && !done ? RewardRowBrush : Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        border.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
        return border;
    }

    // ------------------------------------------------------------------ window chrome

    private void SaveSettings()
    {
        Settings.Left = Left;
        Settings.Top = Top;
        _session.SaveSettings();
    }

    private void KeepOnScreen()
    {
        double maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60;
        double maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60;
        Left = Math.Clamp(Left, SystemParameters.VirtualScreenLeft, maxLeft);
        Top = Math.Clamp(Top, SystemParameters.VirtualScreenTop, maxTop);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Settings.Locked || e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();
        SaveSettings();
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => _session.Tracker.Prev();
    private void Next_Click(object sender, RoutedEventArgs e) => _session.Tracker.Next();
    private void Lock_Click(object sender, RoutedEventArgs e) => ToggleLock();
    private void Hide_Click(object sender, RoutedEventArgs e) => ToggleVisible();
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_session, this);
        _settingsWindow.Applied += () =>
        {
            ApplyAppearance();
            RegisterHotkeys();
            _mapCache = null;
            _treeWindow?.ResetIcons();
            Render();
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        var route = _session.Route;

        for (int i = 0; i < route.Chapters.Count; i++)
        {
            var chapter = route.Chapters[i];
            int start = route.FirstStepOf(i);
            var item = new MenuItem { Header = $"{chapter.Title} · {chapter.Era}", IsChecked = chapter == _session.Tracker.Chapter };
            item.Click += (_, _) => _session.Tracker.JumpTo(start);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var routes = new MenuItem { Header = "Route" };
        foreach (var option in _session.Guide.Routes)
        {
            var item = new MenuItem { Header = option.Name, ToolTip = option.Description, IsChecked = option == route };
            item.Click += (_, _) =>
            {
                if (option != _session.Route && Confirm($"Switch to '{option.Name}'?\n\nThis restarts the step list for {_session.Profile.Name}: ticked lines and chapter times are cleared."))
                    _session.SetRoute(option.Id);
            };
            routes.Items.Add(item);
        }
        menu.Items.Add(routes);

        var profiles = new MenuItem { Header = "Character" };
        foreach (var profile in _session.Store.Profiles)
        {
            string label = profile.ClassId >= 0
                ? $"{profile.Name}  ({Profile.ClassName(profile.ClassId, profile.Mastery)} {profile.Level})"
                : profile.Name;
            var item = new MenuItem { Header = label, IsChecked = profile == _session.Profile };
            item.Click += (_, _) => _session.Activate(profile);
            profiles.Items.Add(item);
        }
        menu.Items.Add(profiles);

        var reset = new MenuItem { Header = "Reset this character's progress" };
        reset.Click += (_, _) =>
        {
            if (Confirm($"Reset all progress for {_session.Profile.Name}?\n\nSteps, ticked lines, build plan ticks and times are cleared."))
                _session.ResetProgress();
        };
        menu.Items.Add(reset);

        menu.Items.Add(new Separator());
        var planner = new MenuItem { Header = "Planner: gear, idols, loot filter, Monolith, dungeons" };
        planner.Click += (_, _) => ShowPlanner(true);
        menu.Items.Add(planner);
        var layout = new MenuItem { Header = "Bar layout (one line across the screen)", IsChecked = Settings.BarLayout };
        layout.Click += (_, _) =>
        {
            Settings.BarLayout = !Settings.BarLayout;
            ApplyAppearance();
            SaveSettings();
            Render();
        };
        menu.Items.Add(layout);
        var settings = new MenuItem { Header = "Settings..." };
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);
        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => Close();
        menu.Items.Add(quit);

        menu.IsOpen = true;
    }

    /// <summary>Asks before anything that throws progress away; one stray click must not cost an evening's ticks.</summary>
    internal static bool Confirm(string question) =>
        MessageBox.Show(question, "Last Epoch Helper", MessageBoxButton.OKCancel, MessageBoxImage.Warning,
            MessageBoxResult.Cancel, MessageBoxOptions.DefaultDesktopOnly) == MessageBoxResult.OK;

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _watcher?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _followTimer.Stop();
        _mapWindow?.Close();
        _treeWindow?.Close();
        _plannerWindow?.Close();
        _keyboard?.Dispose();
        _settingsWindow?.Close();
        SaveSettings();
        _session.Save();
        Application.Current.Shutdown();
    }
}
