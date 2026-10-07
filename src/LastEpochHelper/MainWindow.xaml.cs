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
    private BlessingWindow? _blessingWindow;
    private DateTime _lastBlessingLook = DateTime.MinValue;
    /// <summary>Looks in a row that did not find the blessing choice; one alone is often just a poor read.</summary>
    private int _blessingMisses;
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
    private TreeDef? _pendingSkillRead;
    private readonly StableReads _stableReads = new();
    private DateTime _lastIdleLook = DateTime.MinValue;
    /// <summary>The tree was closed by hand while the game's panel was open; do not reopen it for that panel.</summary>
    private bool _dismissed;
    /// <summary>What the game was last seen showing (panel and skill; passive tab), to notice when it changes.</summary>
    private string? _gameKind, _gameSkill, _gameTab;
    private bool _tabTitleRead;
    private ScreenReader? _labelReader;
    private bool _readingLabels;
    private DateTime _lastLabelRead = DateTime.MinValue;
    /// <summary>The skill whose labels are being read, since when it shows, how many looks so far, and whether one fitted.</summary>
    private string? _labelSkill;
    private DateTime _labelSince, _gameSkillSince = DateTime.UtcNow;
    private int _labelLooks;
    private bool _labelFitted;
    private int _mapReadsLeft;
    private DateTime _mapReadAt = DateTime.MinValue;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(6);
    private DateTime _lastUpdateCheck = DateTime.MinValue;
    private bool _updating;

    /// <summary>The newer release found on GitHub, if any.</summary>
    public ReleaseInfo? AvailableUpdate { get; private set; }
    private bool _reading;
    private bool _chatting;
    private readonly PanelKeyCheck _keyCheck = new();
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

        var storage = new Storage();
        var scenes = new SceneMap(
            ReadScenes(Bundled.Text(Bundled.Scenes)),
            ReadSceneFile(storage.PathOf(Session.LearnedScenesFile)));
        _session = new Session(storage, Guide.LoadBundled(), scenes, EndgameData.LoadBundled());
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
            if (File.Exists(path)) return ReadScenes(File.ReadAllText(path));
        }
        catch (IOException) { }
        return new();
    }

    private static Dictionary<string, string> ReadScenes(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json, Guide.JsonOptions) ?? new(); }
        catch (JsonException) { return new(); }
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
        _followTimer.Tick += (_, _) => { WatchPanel(); ReadSkillLabels(); ReadMapCounters(); UpdateHover(); WatchBlessingOffer(); };
        _followTimer.Start();

        string logPath = string.IsNullOrWhiteSpace(Settings.LogPath) ? LogWatcher.DefaultPath : Settings.LogPath;
        _watcher = new LogWatcher(logPath);
        _watcher.Event += (ev, live) => Dispatcher.BeginInvoke(() => _session.Handle(ev, live));
        _watcher.Start();

        ActivityLog.Start(_session.DataDir);
        ActivityLog.Write($"---- started, version {Updater.Display(Updater.Current)}, screen {SystemParameters.PrimaryScreenWidth:0}x{SystemParameters.PrimaryScreenHeight:0}, "
                          + $"build '{_session.Profile.BuildPlan}', follow keys={Settings.FollowGameKeys} screen={Settings.FollowSkillOnScreen} points={Settings.ReadPointsFromScreen}");

        Render();
        App.OverlayUp = true;
        // Only now: whatever goes wrong in a greeting must not count as the overlay failing to start.
        if (Settings.ShowSplash) SplashWindow.Play();
        var whatsNew = ShowWhatsNew();
        MentionNewErrors();
        WarnIfCannotRead();
        if (!Settings.TourOffered)
        {
            Settings.TourOffered = true;
            SaveSettings();
            TakeTour(); // the country question follows when it closes
        }
        // One window at a time: the question waits for "What's new" to be closed.
        else if (whatsNew is not null) whatsNew.Closed += (_, _) => AskAboutCountry();
        else AskAboutCountry();
    }

    /// <summary>
    /// At start: say once if something keeps the overlay from reading the game (no English text
    /// recognition, the game in another language). Otherwise the build tree just never follows.
    /// </summary>
    private void WarnIfCannotRead()
    {
        _screenReader ??= new ScreenReader();
        string? gameLanguage = GameLanguage.Read();
        ActivityLog.Write($"reading: text recognition {_screenReader.Language ?? "missing"}, game language {gameLanguage ?? "unknown"}");
        var problem = Readiness.Problems(_screenReader.Available, _screenReader.Language, gameLanguage)
            .FirstOrDefault(p => !Settings.ReadinessWarned.Contains(p.Key));
        if (problem is null) return;
        Settings.ReadinessWarned.Add(problem.Key);
        SaveSettings();
        _session.ShowAlert(problem.Short, 45);
    }

    /// <summary>Once per player: what does not work while the game is streamed from GeForce NOW.</summary>
    private void WarnIfStreamed()
    {
        if (!_game.Streamed || Settings.ReadinessWarned.Contains(Readiness.StreamedKey)) return;
        Settings.ReadinessWarned.Add(Readiness.StreamedKey);
        SaveSettings();
        ActivityLog.Write("game: streamed from GeForce NOW");
        _session.ShowAlert(Readiness.StreamedShort, 45);
    }

    /// <summary>What Settings → Following the game → Check shows: each condition for reading the game, met or not.</summary>
    public List<(bool Ok, string Text)> ReadinessReport()
    {
        _screenReader ??= new ScreenReader();
        string? gameLanguage = GameLanguage.Read();
        var report = new List<(bool, string)>();
        if (_screenReader.Available && _screenReader.English) report.Add((true, $"Windows reads text in English ({_screenReader.Language})."));
        if (GameLanguage.IsEnglish(gameLanguage)) report.Add((true, "Last Epoch is set to English."));
        else if (gameLanguage is null) report.Add((true, "Last Epoch's language is not known yet (the game saves it once it has run). It needs to be English."));
        report.AddRange(Readiness.Problems(_screenReader.Available, _screenReader.Language, gameLanguage).Select(p => (false, p.Long)));

        var game = _game.GameBounds;
        int width = game.Right - game.Left, height = game.Bottom - game.Top;
        report.Add(width > 0
            ? (true, $"The game window is {width} x {height}.")
            : (false, "The game window was not found. Start Last Epoch (in Borderless Windowed mode) and check again."));
        if (_game.Streamed) report.Add((false, Readiness.StreamedLong));
        if (_session.GameVersion is { } version)
            report.Add(Readiness.GuideBehind(version, _session.Guide.GameVersion) is { } behind
                ? (false, behind.Long)
                : (true, $"Last Epoch {version}: the campaign guide's data is for {_session.Guide.GameVersion}."));

        string Seen(string what, DateTime? at) => at is { } time
            ? $"The {what} panel was recognised {Ago(time)}."
            : $"The {what} panel has not been seen since the overlay started. Open it in the game and check again.";
        report.Add((_passivesSeenAt is not null, Seen("passive", _passivesSeenAt)));
        report.Add((_skillsSeenAt is not null, Seen("skill", _skillsSeenAt)));
        return report;
    }

    private static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        return span.TotalMinutes < 1 ? "just now" : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago" : $"at {utc.ToLocalTime():HH:mm}";
    }

    /// <summary>When the game's passive / skill panel was last recognised on screen, for the check in the settings.</summary>
    private DateTime? _passivesSeenAt, _skillsSeenAt, _weaverSeenAt;

    private CountryQuestionWindow? _countryWindow;

    /// <summary>Once: may the overlay send which country it is used in? Nothing is sent before a yes.</summary>
    private void AskAboutCountry()
    {
        string version = Updater.Display(Updater.Current);
        if (_countryWindow is not null || UsagePing.Endpoint is null || !UsagePing.ShouldAsk(Settings, version)) return;
        _countryWindow = new CountryQuestionWindow(UsagePing.Country(), version, yes =>
        {
            Settings.CountryAsked = true;
            Settings.ShareCountry = yes;
            SaveSettings();
        });
        _countryWindow.Closed += (_, _) =>
        {
            _countryWindow = null;
            if (Settings.CountryAsked) return;
            Settings.CountryAskedFor = version; // closed without an answer: asked again after an update
            SaveSettings();
        };
        _countryWindow.Show();
    }

    private TourWindow? _tourWindow;
    private bool _tourOpenedTree, _tourOpenedPlanner;

    /// <summary>Opens the tour; its steps bring up the windows they talk about and put them away again.</summary>
    private void TakeTour()
    {
        if (_tourWindow is not null) { _tourWindow.Activate(); return; }
        _tourWindow = new TourWindow(what =>
        {
            bool wantTree = what == "tree", wantPlanner = what is "planner" or "summary";
            if (wantTree && !_treeWanted) { _tourOpenedTree = true; _treePinned = true; ShowTree(true); }
            else if (!wantTree && _tourOpenedTree) { _tourOpenedTree = false; _treePinned = false; ShowTree(false); }

            if (wantPlanner)
            {
                if (!_plannerWanted) _tourOpenedPlanner = true;
                _session.Profile.PlannerTab = what == "summary" ? PlannerWindow.SummaryTab : "Gear";
                ShowPlanner(true);
            }
            else if (_tourOpenedPlanner) { _tourOpenedPlanner = false; ShowPlanner(false); }
        });
        _tourWindow.Closed += (_, _) => { _tourWindow = null; AskAboutCountry(); };
        _tourWindow.Show();
    }

    /// <summary>If something was written to the error log since the last look, point at the bug report.</summary>
    private void MentionNewErrors()
    {
        long size = 0;
        try { size = new FileInfo(Path.Combine(_session.DataDir, "errors.log")) is { Exists: true } log ? log.Length : 0; }
        catch (IOException) { }
        if (size == Settings.ErrorLogBytes) return;
        if (size > Settings.ErrorLogBytes) _session.ShowAlert("The overlay ran into an error last time. Menu (☰) → Report a bug packs the details for us.", 30);
        Settings.ErrorLogBytes = size;
        SaveSettings();
    }

    private BugReportWindow? _reportWindow;

    /// <summary>For a bug report: which icon sheet the build wants, and whether this machine can open it.</summary>
    private string IconSheetFacts()
    {
        if (_session.Tree is not { } build) return "no build";
        string name = build.AtlasName.Length > 0 ? build.AtlasName : BuildTree.AtlasFile;
        string path = Path.Combine(_session.DataDir, name);
        string facts = $"{name}, build expects {build.AtlasCells} cells";
        if (!File.Exists(path)) return facts + ", FILE MISSING";
        try
        {
            var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri(path), System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
            return facts + $", file {new FileInfo(path).Length} bytes, opens as {frame.PixelWidth}x{frame.PixelHeight}";
        }
        catch (Exception e) when (e is NotSupportedException or IOException or System.Runtime.InteropServices.COMException or FileFormatException)
        {
            return facts + $", file {new FileInfo(path).Length} bytes, CANNOT BE OPENED ({e.GetType().Name}: {e.Message})";
        }
    }

    /// <summary>Opens "Report a bug". The picture of the game is taken first, before the window covers it.</summary>
    private void ReportBug()
    {
        if (_reportWindow is not null) { _reportWindow.Activate(); return; }
        string shot = Path.Combine(_session.DataDir, "report-screenshot.png");
        bool haveShot = false;
        try
        {
            if (File.Exists(shot)) File.Delete(shot);
            haveShot = ScreenCapture.Save(_game.GameBounds, shot, maxWidth: 1920);
        }
        catch (IOException) { }

        string version = Updater.Display(Updater.Current);
        Func<string, string, Task<string?>>? send = ReportSender.Endpoint is not { } endpoint ? null : async (zip, description) =>
        {
            using var http = ReportSender.CreateClient(TimeSpan.FromSeconds(60));
            string? problem = await ReportSender.SendAsync(endpoint, zip, ReportSender.Summary(version, description), http);
            ActivityLog.Write(problem is null ? "bug report sent" : "bug report not sent: " + problem);
            return problem;
        };
        _reportWindow = new BugReportWindow((description, includeShot, folder) =>
        {
            var game = _game.GameBounds;
            var profile = _session.Profile;
            var facts = new List<string>
            {
                $"Version: {version}",
                $"Windows: {Environment.OSVersion.VersionString}",
                $"Primary screen: {SystemParameters.PrimaryScreenWidth:0}x{SystemParameters.PrimaryScreenHeight:0} (WPF units)",
                $"Game window: {game.Right - game.Left}x{game.Bottom - game.Top} at {game.Left},{game.Top}",
                $"Text recognition: {(_screenReader ?? new ScreenReader()).Language ?? "not available"}; game language: {GameLanguage.Read() ?? "unknown"}",
                $"Character: class {profile.ClassId}, mastery {profile.Mastery}, level {profile.Level}, route {profile.RouteId}, step {profile.Index}",
                $"Build: {profile.BuildPlan}; trees: {string.Join(", ", _session.Tree?.Trees.Select(t => t.Name) ?? Enumerable.Empty<string>())}",
                $"Icon sheet: {IconSheetFacts()}",
                $"Maxroll game data: {MaxrollImporter.GameDataFacts(_session.DataDir)}",
                "Nodes without an icon: " + string.Join(", ", _session.Tree?.Trees.Select(t => $"{t.Name} {t.Nodes.Count(n => n.IconIndex < 0)}/{t.Nodes.Count}") ?? Enumerable.Empty<string>()),
                $"Tree window: wanted={_treeWanted} pinned={_treePinned} tab={profile.TreeTab}",
            };
            var names = _session.Store.Profiles.Select(p => p.Name)
                // Profiles without a known character name are called after their class; that is no secret.
                .Where(n => _session.Tree?.PassiveTabNames.Contains(n) != true)
                .Append(Settings.AccountName).Append(Environment.UserName).ToList();
            string logPath = string.IsNullOrWhiteSpace(Settings.LogPath) ? LogWatcher.DefaultPath : Settings.LogPath;
            ActivityLog.Write("bug report created");
            var buildFiles = new List<string>();
            if (profile.BuildPlan.Length > 0)
            {
                string planFile = Path.Combine(_session.BuildsDir, profile.BuildPlan);
                buildFiles.Add(planFile);
                buildFiles.Add(BuildTree.PathFor(planFile));
            }
            return BugReport.Create(new BugReportInput(_session.DataDir, description, facts, logPath, includeShot ? shot : null, names, buildFiles), folder);
        }, send, haveShot, version);
        _reportWindow.Closed += (_, _) => _reportWindow = null;
        _reportWindow.Show();
    }

    // ------------------------------------------------------------------ updates

    /// <summary>After an update (or the first run of a version with this feature), list what changed.</summary>
    /// <returns>The "What's new" window, when one opened.</returns>
    private Window? ShowWhatsNew()
    {
        string current = Updater.Display(Updater.Current);
        string last = Settings.LastRunVersion;
        if (last == current) return null;
        Settings.LastRunVersion = current;
        _session.SaveSettings();
        // A brand-new install has nothing to compare with; existing users came from before 0.5.
        if (last.Length == 0 && _session.Store.Profiles.All(p => p.ClassId < 0)) return null;
        if (!Updater.TryParseVersion(last, out var previous)) previous = new Version(0, 4, 0);

        var changes = Updater.ChangesSince(Updater.ParseChangelog(Updater.LoadBundledChangelog()), previous, Updater.Current);
        if (changes.Count == 0) return null;
        var window = new ChangelogWindow(changes, $"Updated to {current}");
        window.Show();
        return window;
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

    private readonly DateTime _startedAt = DateTime.UtcNow;
    private bool _sendingCountry;

    /// <summary>Once per version, after the overlay has run a while: the country Windows is set to and the version. See <see cref="UsagePing"/>.</summary>
    private async void SendCountryInBackground()
    {
        if (_sendingCountry || UsagePing.Endpoint is not { } endpoint || DateTime.UtcNow - _startedAt < UsagePing.Delay) return;
        string version = Updater.Display(Updater.Current);
        if (!UsagePing.Due(Settings, version)) return;
        _sendingCountry = true; // one attempt per run; a failure is tried again next time the overlay starts
        try
        {
            using var http = ReportSender.CreateClient(TimeSpan.FromSeconds(20));
            if (!await UsagePing.SendAsync(endpoint, UsagePing.Message(UsagePing.Country(), version), http)) return;
            Settings.CountrySentFor = version;
            _session.SaveSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
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
        Register(Settings.HotkeyTree, () => { _treePinned = !_treeWanted; _dismissed = _treeWanted; ShowTree(!_treeWanted); });
        Register(Settings.HotkeyPlanner, () => ShowPlanner(!_plannerWanted));

        _keyboard?.Dispose();
        _keyboard = null;
        if (Settings.FollowGameKeys || Settings.ReadCountersFromMap)
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

    private bool _faded;
    private int _hoverTicks, _appliedBoxMode = -1;

    /// <summary>
    /// "Only while the mouse is over it": away from the mouse the box fades to a faint outline that
    /// lets clicks through to the game; resting the mouse on it brings it back. A message (a death,
    /// a warning) also brings it up, so nothing is said to an empty screen.
    /// </summary>
    private void UpdateHover()
    {
        if (Settings.BoxMode != 1 || !IsVisible) { Fade(false); return; }
        var cursor = System.Windows.Forms.Cursor.Position;
        var box = ScreenRect(this);
        bool inside = cursor.X >= box.Left && cursor.X <= box.Right && cursor.Y >= box.Top && cursor.Y <= box.Bottom;
        _hoverTicks = inside ? _hoverTicks + 1 : 0;
        // A moment of rest is needed to bring it up, so sweeping the mouse across in a fight does not.
        bool show = _session.Alert is not null || (inside && (!_faded || _hoverTicks >= 2));
        Fade(!show);
    }

    private void Fade(bool faded)
    {
        if (faded == _faded) return;
        _faded = faded;
        Opacity = faded ? 0.1 : 1;
        Native.ApplyOverlayStyle(_hwnd, faded || Settings.Locked);
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
        if (show != _treeWanted) ActivityLog.Write($"tree window {(show ? "shown" : "hidden")}{(kind is null ? "" : " for " + kind)}");
        _treeWanted = show;
        if (!show)
        {
            _panelRegion = null; _panelMisses = 0; _gameKind = _gameSkill = _gameTab = null;
            _tabTitleRead = false;
            _stableReads.ForgetAnswers();
            // A plan preview (the slider) lasts while the tree is open; next time it mirrors the game again.
            _session.ClearPlanViews();
            _treeWindow?.ResetView();
        }
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
                _treeWindow.CloseRequested += () => { _treePinned = false; _dismissed = true; ShowTree(false); };
                _treeWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                // First time: on the right and below the game panel's headings, which the overlay has to be able to read.
                _treeWindow.Left = Usable(Settings.TreeLeft) ?? Math.Max(0, SystemParameters.PrimaryScreenWidth - 930);
                _treeWindow.Top = Usable(Settings.TreeTop) ?? Math.Round(SystemParameters.PrimaryScreenHeight * 0.23);
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
        _screenReader ??= new ScreenReader();
        if (!_screenReader.Available) return;
        if (shown || verifying) WarnIfCoveringHeadings();
        // Nothing to mirror while the tree is closed, unless a panel key was just pressed - but keep
        // half an eye on the game: its panels also open by mouse (the "+" for unspent points).
        if (!shown && !verifying) { IdleLook(); return; }

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
            // How the picture of the screen came out says a lot when following fails on someone's machine (HDR).
            if (!quick) ActivityLog.Change("levels", $"screen picture: darkest {Levels.Last.Low}, brightest {Levels.Last.High}{(Levels.Last.Stretched ? " - washed out, contrast restored before reading" : "")}");
            // A quick look that misses is routine (the next full look decides), so it is not worth a line.
            if (!quick || reading.Panel != GamePanel.None)
                ActivityLog.Change(quick ? "panel-quick" : "panel", $"game shows {reading.Panel}{(reading.Skill ?? reading.Tab) switch { null => "", var what => " / " + what }} ({(quick ? "quick look" : "full look")})");
            if (!quick && verifying) WritePanelDiagnostics(lines, reading);

            // The game's Weaver tree: its tab, when this build shows one; otherwise there is nothing to show
            // (and a skill tree would be wrong).
            var weaverTab = reading.Panel == GamePanel.Weaver ? WeaverTab() : null;
            if (reading.Panel == GamePanel.Weaver && weaverTab is null)
            {
                _panelRegion = null;
                if (shown && !_treePinned)
                {
                    ActivityLog.Write("tree closed: the game shows its Weaver tree, and this build has no Weaver tab");
                    ShowTree(false);
                }
                return;
            }

            if (reading.Panel != GamePanel.None)
            {
                _panelMisses = 0;
                _keyCheck.Seen(KindOf(reading.Panel));
                if (reading.Panel == GamePanel.Passives) _passivesSeenAt = DateTime.UtcNow;
                else if (reading.Panel == GamePanel.Weaver) _weaverSeenAt = DateTime.UtcNow;
                else _skillsSeenAt = DateTime.UtcNow;
                if (reading.Panel == GamePanel.Passives && !Settings.PanelSeenPassives) { Settings.PanelSeenPassives = true; SaveSettings(); }
                if (reading.Panel == GamePanel.Skills && !Settings.PanelSeenSkills) { Settings.PanelSeenSkills = true; SaveSettings(); }
                if (!quick) _panelRegion = reading.Anchor is { } anchor ? RegionAround(anchor) : null;

                string kind = KindOf(reading.Panel);
                // Follow the game when it changes what it shows - not on every look, or a tab the
                // player picked here by hand would be taken away again a moment later.
                // (A quick look sees the heading only, so "no skill / no tab named" there is not a change.)
                // The first look after a panel opens is acted on at once. After that a change has to be
                // seen twice in a row: text recognition is not steady enough (less so with HDR on) for
                // one odd look to be allowed to flip the tree to another skill or tab and back.
                bool first = _gameKind is null;
                bool kindChanged = kind != _gameKind && (first || _stableReads.Twice("kind", kind));
                if (kind == _gameKind) _stableReads.Twice("kind", kind);
                if (kindChanged) _gameKind = kind;
                if (!_treeWanted) ShowTree(true, kind);                       // the game opened a panel we missed
                else if (kindChanged && _treeWindow!.CurrentKind != kind)
                {
                    ActivityLog.Write($"following: {kind} panel");
                    _treeWindow.SelectKind(kind);
                }
                if (kind == _gameKind && reading.Skill is { } skillShown && (_stableReads.Twice("skill", skillShown) || _gameSkill is null) && skillShown != _gameSkill)
                {
                    ActivityLog.Write($"following: skill {_gameSkill ?? "(none yet)"} -> {skillShown}");
                    _gameSkill = skillShown;
                    _labelSkill = null; // its labels are read at once, timed from now
                    _gameSkillSince = DateTime.UtcNow;
                    _treeWindow?.SelectSkill(skillShown);
                }
                // The skill's level is printed under its heading: that many points it has. Seen twice, it is taken.
                if (kind == _gameKind && reading.Skill is { } leveled && reading.Level is { } skillLevel
                    && _stableReads.Twice("level:" + leveled, skillLevel.ToString())
                    && _session.Tree.Trees.FirstOrDefault(t => t.Kind == TreeDef.SkillKind && t.Name == leveled) is { } leveledTree)
                    _session.SetSkillLevel(leveledTree, skillLevel);
                if (kind == _gameKind && reading.Tab is { } tabShown)
                {
                    _tabTitleRead = true;
                    if ((_stableReads.Twice("tab", tabShown) || _gameTab is null) && tabShown != _gameTab)
                    {
                        ActivityLog.Write($"following: passive tab {_gameTab ?? "(none yet)"} -> {tabShown}");
                        _gameTab = tabShown;
                        if (_session.Tree.Trees.FirstOrDefault(t => t.Kind == TreeDef.PassiveKind && t.Name == tabShown) is { } tabTree)
                            _treeWindow?.SelectTab(tabTree);
                    }
                }
                // The Weaver panel's nodes carry no names, so its labels are not read; its total is.
                if (kind == _gameKind && reading.WeaverPlaced is { } placed && weaverTab is not null && _stableReads.Twice("weaver", placed.ToString()))
                    _session.SetReadPoints(weaverTab, placed);
                if (!quick && reading.Panel != GamePanel.Weaver) ReadNodePoints(words, reading);
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
                ActivityLog.Write("tree closed: no panel in two full reads. Largest text: "
                                  + string.Join(" | ", lines.OrderByDescending(l => l.Height).Take(10).Select(l => $"{l.Height:0}px '{l.Text}'")));
                ShowTree(false);
                _labelSkill = null; // opened again later, its labels are read and timed afresh
            }
        }
        finally { _reading = false; }
    }

    private DateTime _lastCoverWarning = DateTime.MinValue;

    /// <summary>
    /// Says so when one of the overlay's own windows lies over the headings of the game's panel: those
    /// windows are blacked out of what the overlay reads, so the tree could not follow - and from the
    /// outside that just looks like the overlay not working.
    /// </summary>
    private void WarnIfCoveringHeadings()
    {
        if (DateTime.UtcNow - _lastCoverWarning < TimeSpan.FromMinutes(10)) return;
        var game = _game.GameBounds;
        if (game.Bottom - game.Top < 300) return;
        var zone = PanelZone.Headings(game.Left, game.Top, game.Right, game.Bottom);
        foreach (var (window, name) in new (Window? Window, string Name)[] { (this, "the guide box"), (_treeWindow, "the build tree"), (_plannerWindow, "the planner") })
        {
            if (window is not { IsVisible: true }) continue;
            var at = ScreenRect(window);
            if (!PanelZone.Hides(zone, at.Left, at.Top, at.Right, at.Bottom)) continue;
            _lastCoverWarning = DateTime.UtcNow;
            ActivityLog.Write($"{name} covers the panel headings: window {at.Left},{at.Top}-{at.Right},{at.Bottom}, headings {zone.Left},{zone.Top}-{zone.Right},{zone.Bottom}");
            _session.ShowAlert($"Move {name} a little: it lies over the top left of the game's passive / skill panel. The overlay cannot read what its own windows cover, so the build tree may not follow the panel.", 25);
            return;
        }
    }

    /// <summary>
    /// While the tree is closed: every couple of seconds, read the strip across the top of the game
    /// where its panels put their headings. If a panel is there - opened with the mouse, or with a key
    /// press that was missed - the tree opens to match. Only the game's own headings count here.
    /// </summary>
    private async void IdleLook()
    {
        if (_treeWanted || _reading || _session.Tree is null || _screenReader is null) return;
        if (DateTime.UtcNow - _lastIdleLook < TimeSpan.FromSeconds(2)) return;
        _lastIdleLook = DateTime.UtcNow;
        _reading = true;
        try
        {
            var game = _game.GameBounds;
            int width = game.Right - game.Left, height = game.Bottom - game.Top;
            if (width < 400 || height < 300) return;
            // The panels are centred; on an ultrawide the far sides are just the game world.
            int bandWidth = Math.Min(width, (int)(height * 2.4));
            var band = new Native.RECT
            {
                Left = game.Left + (width - bandWidth) / 2, Right = game.Left + (width + bandWidth) / 2,
                Top = game.Top, Bottom = game.Top + (int)(height * 0.24),
            };
            var masks = new List<Native.RECT> { ScreenRect(this) };
            if (_plannerWindow is not null) masks.Add(ScreenRect(_plannerWindow));
            var lines = await _screenReader.ReadAsync(band, masks);
            var tabs = PassiveTabNamesOf(_session.Tree).ToList();
            var skills = _session.Tree.Trees.Where(t => t.Kind == TreeDef.SkillKind).Select(t => t.Name).ToList();
            var reading = PanelDetector.Detect(lines, tabs, skills, strict: true);
            if (reading.Panel == GamePanel.None)
            {
                _dismissed = false; // the panel that was closed by hand here is gone in the game too
                return;
            }
            _keyCheck.Seen(KindOf(reading.Panel));
            // Closed here by hand while the game's panel stays open: leave it closed.
            if (_dismissed || _treeWanted) return;
            if (reading.Panel == GamePanel.Weaver && WeaverTab() is null) return;

            ActivityLog.Write($"noticed {reading.Panel}{(reading.Skill is null ? "" : " / " + reading.Skill)} without a key press: opening the tree");
            _expectedPanel = reading.Panel;
            _verifyUntil = DateTime.UtcNow.AddSeconds(2.5);
            _forceFullRead = true;
            ShowTree(true, KindOf(reading.Panel));
        }
        finally { _reading = false; }
    }

    private static string KindOf(GamePanel panel) =>
        panel == GamePanel.Passives ? TreeDef.PassiveKind : panel == GamePanel.Weaver ? TreeDef.WeaverKind : TreeDef.SkillKind;

    /// <summary>The build's Weaver tab, while it is offered (it is an endgame tree).</summary>
    private TreeDef? WeaverTab() =>
        _session.InEndgame || _treeWindow?.CurrentKind == TreeDef.WeaverKind
            ? _session.Tree?.Trees.FirstOrDefault(t => t.Kind == TreeDef.WeaverKind) : null;

    /// <summary>
    /// Takes the "2/6" labels under the game's nodes and stores them as the character's real points.
    /// For passives the labels themselves say which tab is showing; for a skill, its heading does.
    /// </summary>
    private void ReadNodePoints(List<ScreenLine> words, PanelReading reading)
    {
        if (!Settings.ReadPointsFromScreen || _session.Tree is not { } build) return;
        var tokens = TreeReader.Tokens(words);
        if (reading.Panel == GamePanel.Passives && tokens.Count < 4) return;

        if (reading.Panel == GamePanel.Passives)
        {
            // The tab's title says which tree the labels belong to; without it, the labels have to.
            var passiveTrees = build.Trees.Where(t => t.Kind == TreeDef.PassiveKind).ToList();
            (TreeDef Tree, Dictionary<int, int> Points)? fitted;
            if (reading.Tab is null) fitted = TreeReader.ReadBest(tokens, passiveTrees);
            else if (passiveTrees.FirstOrDefault(t => t.Name == reading.Tab) is { } titled && TreeReader.Read(tokens, titled, known: true) is { } titledPoints)
                fitted = (titled, titledPoints);
            else fitted = null; // a tab the build does not use, or too few labels to place
            ActivityLog.Change("passive-read", fitted is { } f
                ? $"passive labels: {tokens.Count} read, fit {f.Tree.Name}, {f.Points.Count} of {f.Tree.Nodes.Count(n => n.Max >= 1)} nodes, {f.Points.Values.Sum()} points"
                : $"passive labels: {tokens.Count} read, no fit{(reading.Tab is null ? "" : " for " + reading.Tab)}");
            if ((fitted?.Tree.Name ?? reading.Tab ?? _gameTab) is { } readTab) _treeWindow?.ReadResult(readTab, fitted is not null);
            if (fitted is { } fit)
            {
                // Two looks have to agree before a node changes - except the very first time, when
                // there is nothing drawn yet that could flicker.
                var agreed = _stableReads.Confirm("passive:" + fit.Tree.Name, fit.Points);
                _session.SetReadPoints(fit.Tree, _session.HasActual(fit.Tree) ? agreed : fit.Points);
                // Only while the tab's title has not been readable at all: show the tab the labels fit.
                // (Once the title has been read it is the one authority - two sources that can
                // disagree would take turns moving the tree.)
                if (!_tabTitleRead && fit.Tree.Name != _gameTab && (_stableReads.Twice("tab-fit", fit.Tree.Name) || _gameTab is null))
                {
                    ActivityLog.Write($"following: passive tab {_gameTab ?? "(none yet)"} -> {fit.Tree.Name} (from the node labels)");
                    _gameTab = fit.Tree.Name;
                    _treeWindow?.SelectTab(fit.Tree);
                }
            }
        }
        else if (reading.Skill is not null && build.Trees.FirstOrDefault(t => t.Kind == TreeDef.SkillKind && t.Name == reading.Skill) is { } skill)
        {
            _pendingSkillRead = skill; // skill labels need the slower, enlarged read: see ReadSkillLabels
        }
    }

    /// <summary>
    /// The labels under skill nodes sit in small dark plates that the ordinary read mostly misses, so
    /// an open skill tree gets a dedicated read of the panel, enlarged and in high contrast. It is
    /// slower, so it runs at most every few seconds and only while a skill tree is showing.
    /// </summary>
    private async void ReadSkillLabels()
    {
        if (_pendingSkillRead is not { } skill || _readingLabels || !_game.GameFocused) return;
        if (_treeWindow is not { IsVisible: true } || _treeWindow.CurrentKind != TreeDef.SkillKind) { _pendingSkillRead = null; return; }
        // A skill just opened is read at once; after that every few seconds.
        bool newSkill = skill.Name != _labelSkill;
        if (!newSkill && DateTime.UtcNow - _lastLabelRead < TimeSpan.FromSeconds(2.5)) return;
        // Its own reader, so that following the open panel carries on while this slower read runs.
        _labelReader ??= new ScreenReader();
        if (!_labelReader.Available) return;
        _pendingSkillRead = null;
        _lastLabelRead = DateTime.UtcNow;
        _readingLabels = true;
        if (newSkill) (_labelSkill, _labelSince, _labelLooks, _labelFitted) = (skill.Name, skill.Name == _gameSkill ? _gameSkillSince : DateTime.UtcNow, 0, false);
        _labelLooks++;
        try
        {
            // The game's panel is centred; on an ultrawide the sides are just the game world.
            var game = _game.GameBounds;
            int width = game.Right - game.Left, height = game.Bottom - game.Top;
            int panelWidth = Math.Min(width, (int)(height * 1.8));
            var area = new Native.RECT { Left = game.Left + (width - panelWidth) / 2, Right = game.Left + (width + panelWidth) / 2, Top = game.Top, Bottom = game.Bottom };
            var masks = new List<Native.RECT> { ScreenRect(this) };
            if (_treeWindow is not null) masks.Add(ScreenRect(_treeWindow));
            if (_plannerWindow is not null) masks.Add(ScreenRect(_plannerWindow));

            var reads = await _labelReader.ReadLabelsAsync(area, masks);
            // Another skill was opened meanwhile: this picture may be half one tree, half the other.
            if (_pendingSkillRead is { } now && now != skill) return;
            var tokens = TreeReader.Merge(reads.Select(TreeReader.Tokens).ToArray());
            // The panel's heading named this skill (and the tree follows it), so the labels only have to
            // be placed on it: on a low game window the reader catches a third of them, too few to also
            // prove which tree it is.
            var points = TreeReader.Read(tokens, skill, known: skill.Name == _gameSkill);
            _treeWindow?.ReadResult(skill.Name, points is not null);
            if (points is not null)
            {
                var agreed = _stableReads.Confirm("skill:" + skill.Name, points);
                _session.SetReadPoints(skill, _session.HasActual(skill) ? agreed : points);
            }
            string outcome = points is null
                ? $"skill labels ({skill.Name}): {tokens.Count} read, no fit"
                : $"skill labels ({skill.Name}): {tokens.Count} read, {points.Count} of {skill.Nodes.Count(n => n.Max >= 1)} nodes, {points.Values.Sum()} points";
            // Until the first fit, every look is logged with how long the skill has been showing: "the
            // points come late" (2026-10-07) can then be followed look by look in a report.
            if (!_labelFitted && skill.Name == _labelSkill)
            {
                ActivityLog.Write($"{outcome} (look {_labelLooks}, {(DateTime.UtcNow - _labelSince).TotalSeconds:0.0} s after the skill showed)");
                _labelFitted = points is not null;
            }
            else ActivityLog.Change("skill-read:" + skill.Name, outcome);
            // A look with no labels at all (the panel just closed) would replace one worth keeping.
            if (tokens.Count > 0) WriteSkillTreeDiagnostics(reads.SelectMany(r => r).ToList(), tokens, skill, points);
        }
        finally { _readingLabels = false; }
    }

    /// <summary>
    /// After the map key: reads the game's quest-reward counters ("3/15" and "1/8" in a corner of the
    /// map) and takes them as the truth for the overlay's passive and idol counters.
    /// </summary>
    private async void ReadMapCounters()
    {
        if (_mapReadsLeft <= 0 || _reading || DateTime.UtcNow < _mapReadAt || !_game.GameFocused) return;
        _screenReader ??= new ScreenReader();
        if (!_screenReader.Available) { _mapReadsLeft = 0; return; }
        _mapReadsLeft--;
        _mapReadAt = DateTime.UtcNow.AddMilliseconds(450);
        _reading = true;
        try
        {
            var masks = new List<Native.RECT> { ScreenRect(this) };
            if (_treeWindow is not null) masks.Add(ScreenRect(_treeWindow));
            if (_plannerWindow is not null) masks.Add(ScreenRect(_plannerWindow));
            // "PASSIVE POINTS REWARDS (6/15)" and "IDOL SLOT REWARDS (1/8)" stand in the map's bottom
            // left corner; reading just that corner is quick enough to repeat.
            var game = _game.GameBounds;
            var corner = new Native.RECT
            {
                Left = game.Left, Right = game.Left + (int)((game.Right - game.Left) * 0.35),
                Top = game.Bottom - (int)((game.Bottom - game.Top) * 0.3), Bottom = game.Bottom,
            };
            var lines = await _screenReader.ReadAsync(corner, masks);
            var counters = MapCounters.Parse(lines, _session.Guide.PassiveCap, _session.Guide.IdolCap);
            if (counters is not null || _mapReadsLeft == 0)
            {
                WriteMapDiagnostics(lines, counters);
                ActivityLog.Write(counters is { } seen ? $"map: {seen.Passive} passives, {seen.Idol} idol slots" : "map: counters not found");
            }
            if (counters is not { } found) return;
            _mapReadsLeft = 0;
            _session.SetMapCounters(found.Passive, found.Idol);
        }
        finally { _reading = false; }
    }

    /// <summary>What the reader saw on the map screen, and once a picture of it; local only, for tuning.</summary>
    private void WriteMapDiagnostics(List<ScreenLine> lines, (int Passive, int Idol)? counters)
    {
        try
        {
            var text = new List<string> { $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  counters={(counters is { } c ? $"{c.Passive} passives, {c.Idol} idols" : "not found")}" };
            text.AddRange(lines.OrderBy(l => l.Y).Take(400).Select(l => $"{l.Height,4:0} @{l.X,5:0},{l.Y,5:0}  {l.Text}"));
            string notes = Path.Combine(_session.DataDir, "panel-ocr-map.txt"), picture = Path.Combine(_session.DataDir, "panel-map.png");
            // The key closes the map as well as opening it, so keep the look before this one too.
            if (File.Exists(notes)) File.Copy(notes, Path.Combine(_session.DataDir, "panel-ocr-map-previous.txt"), overwrite: true);
            File.WriteAllLines(notes, text);
            if (!File.Exists(picture)) ScreenCapture.Save(_game.GameBounds, picture, maxWidth: int.MaxValue);
        }
        catch (IOException) { }
    }

    /// <summary>
    /// What was read from an open skill tree, and once a picture of it, in the data folder - local
    /// only. Skill trees were added without a real example to test against; this is that example.
    /// </summary>
    private void WriteSkillTreeDiagnostics(List<ScreenLine> words, List<TreeReader.Token> tokens, TreeDef skill, Dictionary<int, int>? points)
    {
        try
        {
            var text = new List<string>
            {
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  skill={skill.Name}  labels={tokens.Count}  nodes={skill.Nodes.Count(n => n.Max >= 1)}  matched={points?.Count.ToString() ?? "no fit"}",
                "labels: " + string.Join("  ", tokens.Select(t => $"{t.Have}/{t.Max}@{t.X:0},{t.Y:0}")),
                "points: " + (points is null ? "-" : string.Join(", ", points.Where(kv => kv.Value > 0).Select(kv => $"{skill.Nodes.First(n => n.Id == kv.Key).Name}={kv.Value}"))),
            };
            text.AddRange(words.Take(250).Select(w => $"{w.Height,4:0} @{w.X,5:0},{w.Y,5:0}  {w.Text}"));
            File.WriteAllLines(Path.Combine(_session.DataDir, "panel-ocr-skilltree.txt"), text);
            string picture = Path.Combine(_session.DataDir, "panel-skilltree.png");
            if (!File.Exists(picture)) ScreenCapture.Save(_game.GameBounds, picture, maxWidth: int.MaxValue);
        }
        catch (IOException) { }
    }

    /// <summary>Has the game's panel for this kind of tree ever been recognised here?</summary>
    private bool SeenOnScreen(string? kind) =>
        kind == TreeDef.PassiveKind ? Settings.PanelSeenPassives
        : kind == TreeDef.WeaverKind ? _weaverSeenAt is not null
        : kind == TreeDef.SkillKind && Settings.PanelSeenSkills;

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

    /// <summary>
    /// After a timeline boss the game offers three blessings by icon only. While the character is in
    /// one of a timeline's quest echoes (the boss is in the third), look at the middle of the game
    /// window now and then for that choice, and put the build's blessing for the timeline beside it.
    /// </summary>
    private async void WatchBlessingOffer()
    {
        string? timeline = _session.EchoTimeline;
        var wanted = BlessingAdvice.BuildBlessings(_session.Tree, _session.Stage);
        if (timeline is null || wanted.Count == 0 || !Settings.FollowSkillOnScreen || !_game.GameFocused)
        {
            if (_blessingWindow is { IsVisible: true }) _blessingWindow.Hide();
            return;
        }
        if (_reading || DateTime.UtcNow - _lastBlessingLook < TimeSpan.FromSeconds(1.5)) return;
        _screenReader ??= new ScreenReader();
        if (!_screenReader.Available) return;
        _lastBlessingLook = DateTime.UtcNow;

        var game = _game.GameBounds;
        int width = game.Right - game.Left, height = game.Bottom - game.Top;
        if (width < 400 || height < 300) return;
        // The choice sits in the middle of the game window and is about half as wide as it is tall.
        int bandWidth = Math.Min(width, height);
        var band = new Native.RECT
        {
            Left = game.Left + (width - bandWidth) / 2, Right = game.Left + (width + bandWidth) / 2,
            Top = game.Top, Bottom = game.Bottom - height / 6,
        };
        _reading = true;
        try
        {
            var masks = new List<Native.RECT> { ScreenRect(this) };
            if (_treeWindow is not null) masks.Add(ScreenRect(_treeWindow));
            if (_plannerWindow is not null) masks.Add(ScreenRect(_plannerWindow));
            if (_blessingWindow is not null) masks.Add(ScreenRect(_blessingWindow));
            var lines = await _screenReader.ReadAsync(band, masks);
            var heading = BlessingAdvice.FindOffer(lines);
            if (heading is null || BlessingAdvice.For(_session.Endgame, wanted, timeline) is not { } advice)
            {
                if (++_blessingMisses < 2) return;
                if (_blessingWindow is { IsVisible: true }) _blessingWindow.Hide();
                ActivityLog.Change("blessing", "no blessing choice on screen");
                return;
            }
            _blessingMisses = 0;
            ActivityLog.Change("blessing", $"blessing choice in {timeline}: suggesting {(advice.Wanted.Count == 0 ? "none (the build takes none here)" : string.Join(", ", advice.Wanted.Select(w => w.Name)))}");

            // Beside the game's window: it reaches about 1.4 widths of "TIMELINE STABILIZED" (or 2.3 of
            // "Choose a Blessing") either side of the heading's centre. Right of it, or left if no room.
            double centre = heading.X + heading.Width / 2;
            double half = heading.Width * (heading.Text.Contains("timeline", StringComparison.OrdinalIgnoreCase) ? 1.4 : 2.3);
            var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var right = fromDevice.Transform(new Point(centre + half + 16, heading.Y));
            var left = fromDevice.Transform(new Point(centre - half - 16, heading.Y));
            _blessingWindow ??= new BlessingWindow();
            double noteWidth = _blessingWindow.MaxWidth;
            var screenRight = fromDevice.Transform(new Point(game.Right, 0)).X;
            _blessingWindow.Display(advice, right.X + noteWidth <= screenRight ? right : new Point(Math.Max(0, left.X - noteWidth), left.Y));
        }
        finally { _reading = false; }
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

        if (key == KeyboardWatcher.VirtualKey(Settings.GameKeyMap) && Settings.ReadCountersFromMap && !_chatting)
        {
            // The map fades in over about a second; look a few times until the counters are there.
            _mapReadsLeft = 5;
            _mapReadAt = DateTime.UtcNow.AddMilliseconds(400);
            return;
        }
        if (!Settings.FollowGameKeys) return;

        string? kind = key == KeyboardWatcher.VirtualKey(Settings.GameKeyPassives) ? TreeDef.PassiveKind
            : key == KeyboardWatcher.VirtualKey(Settings.GameKeySkills) ? TreeDef.SkillKind : null;
        if (kind is null) return;

        _dismissed = false;
        ActivityLog.Write($"key for {kind} (tree wanted={_treeWanted}, pinned={_treePinned}, seen on screen={trustScreen})");
        _expectedPanel = kind == TreeDef.PassiveKind ? GamePanel.Passives : GamePanel.Skills;
        _gameKind = _gameSkill = _gameTab = null;
        _tabTitleRead = false;
        _stableReads.ForgetAnswers();
        _lastPanelKey = DateTime.UtcNow;
        _verifyUntil = DateTime.UtcNow.AddSeconds(2.5);
        _forceFullRead = true;
        _panelMisses = 0;
        // While typing in chat the key is just a letter - unless the screen later shows a panel.
        if (_chatting) return;
        // Only the screen can say whether the key opened the panel - and only on a machine where it has
        // recognised a panel before; where reading fails altogether, trust the key.
        bool readingWorks = Settings.FollowSkillOnScreen && _screenReader is { Available: true } && (Settings.PanelSeenPassives || Settings.PanelSeenSkills);
        if (readingWorks && _keyCheck.Pressed(kind))
        {
            IgnorePanelKey(kind);
            return;
        }

        _treePinned = false;
        // Pressing the same panel's key again closes it in the game, so close here too.
        if (_treeWanted && _treeWindow?.CurrentKind == kind) ShowTree(false);
        else ShowTree(true, kind);
    }

    /// <summary>
    /// The key for this panel has not opened it in the game several times in a row (WASD movement, a
    /// rebound key): stop following it, and say where the game's own key can be entered.
    /// </summary>
    private void IgnorePanelKey(string kind)
    {
        bool passives = kind == TreeDef.PassiveKind;
        string key = passives ? Settings.GameKeyPassives : Settings.GameKeySkills;
        if (passives) Settings.GameKeyPassives = ""; else Settings.GameKeySkills = "";
        SaveSettings();
        ActivityLog.Write($"key {key} for {kind} never opened that panel {PanelKeyCheck.Limit} times in a row: no longer followed");
        if (_treeWanted && !_treePinned) ShowTree(false);
        string panel = passives ? "passive" : "skill";
        _session.ShowAlert($"{key} did not open the game's {panel} panel the last few times (moving with WASD?), so the overlay no longer reacts to {key}. "
                           + $"It still notices the {panel} panel when it opens. If the game uses another key for it, enter that key under Settings → Following the game → The game's own keys.", 40);
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
        WarnIfStreamed();

        UpdateVisibility();
        // Checked even while hidden, so the notice is waiting when the game gets focus again.
        CheckForUpdateInBackground();
        SendCountryInBackground();
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
        // A box that is kept hidden still has things to say now and then (a death, a warning, a
        // warning): for as long as such a message lasts it comes up, showing only the message.
        bool messageOnly = _userHidden && Settings.BoxMode == 2 && _session.Alert is not null;
        if (messageOnly != _messageOnly)
        {
            _messageOnly = messageOnly;
            Render();
        }
        bool show = (!_userHidden || messageOnly) && focusOk;
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
        // "Hidden" starts the box hidden; the show/hide hotkey and the tray icon still bring it up.
        if (Settings.BoxMode != _appliedBoxMode)
        {
            if (Settings.BoxMode == 2) _userHidden = true;
            else if (_appliedBoxMode == 2) _userHidden = false;
            _appliedBoxMode = Settings.BoxMode;
            if (_hwnd != IntPtr.Zero) UpdateVisibility();
        }
        Width = Settings.BarLayout ? Math.Clamp(Settings.BarWidth, 500, 3000) : Math.Clamp(Settings.Width, 260, 900);
        Panel.Visibility = Settings.BarLayout ? Visibility.Collapsed : Visibility.Visible;
        Bar.Visibility = Settings.BarLayout ? Visibility.Visible : Visibility.Collapsed;
        Bar.Background = Theme.Plate((byte)Math.Clamp(Settings.Opacity * 255, 40, 255));
        FontSize = Math.Clamp(Settings.FontSize, 9, 28);
        ZoneText.FontSize = FontSize + 5;
        byte alpha = (byte)Math.Clamp(Settings.Opacity * 255, 40, 255);
        Panel.Background = Theme.Plate(alpha);
    }

    private void Render()
    {
        var tracker = _session.Tracker;
        var chapter = tracker.Chapter;
        var step = tracker.Step;
        int? level = _session.Profile.Level > 0 ? _session.Profile.Level : null;

        // With the campaign guide put away (endgame), the box keeps the build reminders, counters and timer.
        bool guide = !_session.Profile.HideGuide;
        ChapterText.Text = guide ? $"{chapter.Title} · {chapter.Era}  ({tracker.IndexInChapter + 1}/{chapter.Steps.Count})"
            : "Endgame" + (_session.Plan is { } followed ? $"  ·  {followed.Name}" : "");
        ZoneRow.Visibility = TaskList.Visibility = guide && Settings.ShowZone ? Visibility.Visible : Visibility.Collapsed;
        NextText.Visibility = guide && Settings.ShowNext ? Visibility.Visible : Visibility.Collapsed;
        RewardText.Visibility = Settings.ShowCounters ? Visibility.Visible : Visibility.Collapsed;
        LevelText.Visibility = Settings.ShowLevel ? Visibility.Visible : Visibility.Collapsed;
        if (guide && _session.ShouldOfferHidingGuide())
            // Not from inside this method: showing an alert draws the overlay again.
            Dispatcher.BeginInvoke(() => _session.ShowAlert("In the Monolith now? Menu (☰) → Hide the campaign guide puts the zone steps away and keeps your build reminders.", 40));
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
        BossSection.Visibility = guide && Settings.ShowBoss && BossList.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

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
        PendingSection.Visibility = guide && Settings.ShowPending && pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        RenderBuild(level);
        RenderMap(step);
        if (!guide) MapSection.Visibility = Visibility.Collapsed;

        NextText.Text = tracker.NextStep is { } next ? $"Next ▸ {next.Zone}" : "End of the guide";

        RewardText.Inlines.Clear();
        RewardText.Inlines.Add(new Run($"Passives {passive}/{_session.Guide.PassiveCap}") { Foreground = PassiveBrush });
        RewardText.Inlines.Add(new Run("   "));
        RewardText.Inlines.Add(new Run($"Idol slots {idol}/{_session.Guide.IdolCap}") { Foreground = IdolBrush });

        LevelText.Inlines.Clear();
        if (level is { } lvl) LevelText.Inlines.Add(new Run($"You {lvl}"));
        if (guide && step.Level > 0)
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

        // Up only for a message (the box is otherwise kept hidden): nothing but the message.
        Header.Visibility = Footer.Visibility = _messageOnly ? Visibility.Collapsed : Visibility.Visible;
        if (_messageOnly)
            ZoneRow.Visibility = BossSection.Visibility = Details.Visibility = UpdateText.Visibility = Visibility.Collapsed;
        // A message that has just arrived, or run out, changes whether a hidden box is up.
        if ((_session.Alert is not null) != _messageOnly && _userHidden && Settings.BoxMode == 2 && _hwnd != IntPtr.Zero) UpdateVisibility();
    }

    private bool _messageOnly;

    /// <summary>The hotkey that shows and hides the box, as the player set it ("" if they removed it).</summary>
    private string ToggleKey => string.IsNullOrWhiteSpace(Settings.HotkeyToggle) ? "" : Settings.HotkeyToggle.Trim();

    /// <summary>The whole step on one line: zone, what to do, where to go, boss, counters.</summary>
    private void RenderBar(GuideStep step, int passive, int idol, int? level)
    {
        var tracker = _session.Tracker;
        bool guide = !_session.Profile.HideGuide && Settings.ShowZone;
        BarZone.Text = guide ? $"{tracker.Chapter.Id}.{tracker.IndexInChapter + 1}  {step.Zone}" : "Endgame";
        BarText.Inlines.Clear();
        bool first = true;
        if (!guide)
        {
            // In place of the zone's steps: the reminders that are due, or just the build's name.
            var (due, _) = BuildPlan.View(Settings.ShowBuildLines ? _session.Plan : null, level ?? 0, _session.Profile.PlanDone, extra: _session.FilterEntries());
            BarText.Inlines.Add(new Run(due.Count > 0 ? string.Join("    ", due.Select(d => "• " + d.Text)) : _session.Plan?.Name ?? "")
                { Foreground = due.Count > 0 ? BuildBrush : DimBrush });
        }
        for (int i = 0; guide && i < step.Tasks.Count; i++)
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
        if (guide && Settings.ShowNext && tracker.NextStep is { } next)
            BarText.Inlines.Add(new Run($"{(first ? "" : "    ")}▸ next: {next.Zone}") { Foreground = DimBrush });

        BarRight.Inlines.Clear();
        if (Settings.ShowCounters)
        {
            BarRight.Inlines.Add(new Run($"{passive}/{_session.Guide.PassiveCap}") { Foreground = PassiveBrush });
            BarRight.Inlines.Add(new Run("  "));
            BarRight.Inlines.Add(new Run($"{idol}/{_session.Guide.IdolCap}") { Foreground = IdolBrush });
        }
        if (Settings.ShowLevel && level is { } lvl)
            BarRight.Inlines.Add(new Run($"   lvl {lvl}{(guide && step.Level > 0 ? $" / zone {step.Level}" : "")}")
                { Foreground = guide && step.Level > 0 && lvl < step.Level - 2 ? UnderLevelBrush : BarRight.Foreground });
        if (Settings.ShowTimer) BarRight.Inlines.Add(new Run($"   ⏱ {Clock(_session.Profile.PlaySeconds)}"));
        BarButtons.Visibility = Settings.Locked ? Visibility.Collapsed : Visibility.Visible;

        BarBoss.Children.Clear();
        foreach (var boss in step.Tasks.Where(t => !_session.Profile.HideGuide && Settings.ShowBoss && t.Type == "boss"))
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
        var menu = Theme.Styled(new ContextMenu { PlacementTarget = (UIElement)sender });
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
        // Put the whole box away: the build tree and planner keep working without it.
        var hideBox = new MenuItem { Header = ToggleKey.Length > 0 ? $"Hide this box  ({ToggleKey} brings it back)" : "Hide this box  (the tray icon brings it back)" };
        hideBox.ToolTip = "Keeps the box hidden, also the next time the overlay starts. The build tree and the planner work as before; "
                          + "messages (a death, a warning) still come up for a few seconds. Change it back under Settings → Overlay → Show the box.";
        hideBox.Click += (_, _) =>
        {
            Settings.BoxMode = 2;
            SaveSettings();
            ApplyAppearance();
            _session.ShowAlert(ToggleKey.Length > 0 ? $"The box is hidden. {ToggleKey} shows it again." : "The box is hidden. The tray icon by the clock shows it again.", 6);
        };
        menu.Items.Add(hideBox);
        var hideGuide = new MenuItem { Header = "Hide the campaign guide (endgame)", IsChecked = _session.Profile.HideGuide };
        hideGuide.ToolTip = "Puts the zone steps, boss notes and unclaimed rewards away for this character and keeps the build reminders, counters and timer. Tick again to bring the guide back.";
        hideGuide.Click += (_, _) => _session.SetGuideHidden(!_session.Profile.HideGuide);
        menu.Items.Add(hideGuide);
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
        var tour = new MenuItem { Header = "Take a tour" };
        tour.Click += (_, _) => TakeTour();
        menu.Items.Add(tour);
        var report = new MenuItem { Header = "Report a bug..." };
        report.Click += (_, _) => ReportBug();
        menu.Items.Add(report);
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
        _blessingWindow?.Close();
        _treeWindow?.Close();
        _plannerWindow?.Close();
        _keyboard?.Dispose();
        _settingsWindow?.Close();
        SaveSettings();
        _session.Save();
        Application.Current.Shutdown();
    }
}
