using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LastEpochHelper.Core;

namespace LastEpochHelper;

/// <summary>Ordinary (focusable) window for everything that used to require editing settings.json.</summary>
internal sealed class SettingsWindow : Window
{
    private const string NoPlan = "(none - milestones only)";

    private readonly Session _session;
    private readonly MainWindow _overlay;
    private readonly CheckBox _autoUpdate = new() { Content = "Look for new versions automatically (never installs without asking)" };
    private readonly CheckBox _shareCountry = new()
    {
        Content = "Tell us which country the overlay is used in",
        ToolTip = "Once per version the overlay sends two things: the country Windows is set to (like \"DK\") and the overlay's version. No name, no id, nothing about you or your machine.",
    };
    private readonly TextBlock _updateInfo = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 2, 0, 2) };
    private readonly Button _install = new() { Content = "Install update", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 4), Visibility = Visibility.Collapsed };
    private readonly ComboBox _profile = new();
    private readonly TextBox _name = new();
    private readonly ComboBox _route = new();
    private readonly TextBlock _routeInfo = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0) };
    private readonly ComboBox _plan = new();
    private readonly TextBox _importLink = new();

    private readonly Slider _opacity = new() { Minimum = 0.3, Maximum = 1, TickFrequency = 0.05, IsSnapToTickEnabled = true };
    private readonly Slider _fontSize = new() { Minimum = 10, Maximum = 22, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly Slider _width = new() { Minimum = 280, Maximum = 700, TickFrequency = 10, IsSnapToTickEnabled = true };
    private readonly CheckBox _autoHide = new() { Content = "Hide the overlay while another program has focus" };
    private readonly CheckBox _showTimer = new() { Content = "Show play time and chapter splits" };
    private readonly CheckBox _showBuild = new() { Content = "Show reminders for your level (skill slots, resistances, loot filter)" };
    private readonly CheckBox _autoTick = new() { Content = "Tick quest steps automatically when the game log reports them" };
    private readonly CheckBox _autoLearn = new() { Content = "Learn unknown zones automatically (new zones after a patch)" };
    private readonly CheckBox _followKeys = new() { Content = "Open the build tree with the game's own passive / skill keys" };
    private readonly CheckBox _followSkill = new() { Content = "Keep the build tree in step with the game's passive / skill panel (reads the screen)" };
    private readonly CheckBox _readPoints = new() { Content = "Read my real points from the game's passive / skill panel when it is open" };
    private readonly CheckBox _readMap = new() { Content = "Read the passive / idol counters from the game's map when I open it" };
    private readonly CheckBox _orderPassives = new() { Content = "Passive tree: number the next points in order" };
    private readonly CheckBox _orderSkills = new() { Content = "Skill trees: number the next points in order" };
    private readonly CheckBox _amountPassives = new() { Content = "Passive tree: show how many points go into the node (+N)" };
    private readonly CheckBox _amountSkills = new() { Content = "Skill trees: show how many points go into the node (+N)" };
    private readonly CheckBox _buildLines = new() { Content = "List the build's per-level steps as text in the overlay box" };
    private readonly TextBox _keyPassives = new();
    private readonly TextBox _keySkills = new();
    private readonly TextBox _keyMap = new();
    private readonly ComboBox _boxMode = new() { ItemsSource = new[] { "Always", "Only while the mouse is over it", "Hidden (the show / hide hotkey or the tray icon brings it up)" } };
    private readonly Slider _treeScale = new() { Minimum = 0.5, Maximum = 1, TickFrequency = 0.05, IsSnapToTickEnabled = true };
    private readonly CheckBox _hideGuide = new() { Content = "Hide the campaign guide for this character (endgame) - also in the ☰ menu" };
    /// <summary>The "show this part of the overlay" boxes: what each one reads and writes.</summary>
    private readonly List<(CheckBox Box, Func<Settings, bool> Get, Action<Settings, bool> Set)> _parts = new();
    private readonly ComboBox _mapMode = new() { ItemsSource = new[] { "Off", "Inside the overlay", "Large, centred on screen" } };
    private readonly Dictionary<string, TextBox> _hotkeys = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private bool _loading;

    /// <summary>Raised after settings were written, so the overlay can re-read them.</summary>
    public event Action? Applied;

    public SettingsWindow(Session session, MainWindow overlay)
    {
        _session = session;
        _overlay = overlay;
        Title = "Last Epoch Helper - Settings";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        // One tab per subject; Apply at the bottom saves all of them at once.
        var tabs = new TabControl();
        StackPanel Tab(string title)
        {
            var page = new StackPanel { Margin = new Thickness(12, 6, 12, 12) };
            tabs.Items.Add(new TabItem { Header = title, Content = page, Padding = new Thickness(10, 4, 10, 4) });
            return page;
        }
        static void Boxes(StackPanel page, params CheckBox[] boxes)
        {
            foreach (var box in boxes)
            {
                box.Margin = new Thickness(0, 4, 0, 0);
                page.Children.Add(box);
            }
        }
        CheckBox Part(string label, Func<Settings, bool> get, Action<Settings, bool> set)
        {
            var box = new CheckBox { Content = label };
            _parts.Add((box, get, set));
            return box;
        }

        // ---- Character
        var character = Tab("Character");
        character.Children.Add(Heading("Character"));
        character.Children.Add(Row("Profile", _profile));
        character.Children.Add(Row("Name", _name));
        character.Children.Add(Row("Route", _route));
        character.Children.Add(Indented(_routeInfo));
        character.Children.Add(Indented(Buttons(("New profile", NewProfile), ("Delete profile", DeleteProfile))));
        character.Children.Add(Heading("Build"));
        character.Children.Add(Row("Build plan", _plan));
        character.Children.Add(Row("Maxroll link", _importLink));
        _importLink.ToolTip = "Any of: a Maxroll Last Epoch planner link (maxroll.gg/last-epoch/planner/...), a Maxroll build guide link, "
                              + "the text the planner's Export button copies, or the path of a build file someone sent you.";
        character.Children.Add(Indented(Buttons(
            ("Import from Maxroll", Import), ("Import from file...", ImportFile), ("New empty plan", NewPlan), ("Open plans folder", () => Open(_session.BuildsDir)))));
        character.Children.Add(Indented(Buttons(("Get the Weaver trees from Maxroll", () => GetWeaver(announce: true)))));

        // ---- Overlay: looks, and which parts it shows
        var overlayTab = Tab("Overlay");
        overlayTab.Children.Add(Heading("Appearance"));
        overlayTab.Children.Add(Row("Opacity", _opacity));
        overlayTab.Children.Add(Row("Text size", _fontSize));
        overlayTab.Children.Add(Row("Width", _width));
        overlayTab.Children.Add(Row("Show the box", _boxMode));
        Boxes(overlayTab, _autoHide);
        overlayTab.Children.Add(Heading("Show in the overlay"));
        Boxes(overlayTab,
            Part("Zone name and the zone's steps", s => s.ShowZone, (s, on) => s.ShowZone = on),
            Part("Boss notes", s => s.ShowBoss, (s, on) => s.ShowBoss = on),
            Part("Unclaimed quest rewards", s => s.ShowPending, (s, on) => s.ShowPending = on),
            Part("Next zone", s => s.ShowNext, (s, on) => s.ShowNext = on),
            _showBuild, _buildLines,
            Part("Passive point and idol slot counters", s => s.ShowCounters, (s, on) => s.ShowCounters = on),
            Part("Your level (and the zone's)", s => s.ShowLevel, (s, on) => s.ShowLevel = on),
            _showTimer);
        overlayTab.Children.Add(Row("Zone map", _mapMode));
        overlayTab.Children.Add(Heading("Endgame"));
        Boxes(overlayTab, _hideGuide);
        overlayTab.Children.Add(Indented(new TextBlock
        {
            Text = "Puts zone, steps, boss notes, unclaimed rewards and map away for this one character, whatever is ticked above; the rest stays.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7,
        }));

        // ---- Game: what the overlay picks up from the game by itself
        var gameTab = Tab("Following the game");
        gameTab.Children.Add(Heading("Campaign"));
        Boxes(gameTab, _autoTick, _autoLearn, _readMap);
        gameTab.Children.Add(Heading("Build tree"));
        Boxes(gameTab, _followKeys, _followSkill, _readPoints, _orderPassives, _amountPassives, _orderSkills, _amountSkills);
        gameTab.Children.Add(Row("Tree size", _treeScale));
        gameTab.Children.Add(Indented(new TextBlock
        {
            Text = "The build tree is also shrunk by itself to fit a small screen. Its \"Mini\" switch (bottom right of the tree) leaves just the tabs and the next points.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7,
        }));
        gameTab.Children.Add(Heading("The game's own keys"));
        gameTab.Children.Add(Row("Passives", _keyPassives));
        gameTab.Children.Add(Row("Skills", _keySkills));
        gameTab.Children.Add(Row("Map", _keyMap));
        gameTab.Children.Add(Indented(new TextBlock
        {
            Text = "Set these to the keys you use in Last Epoch, if you changed them there. The overlay only listens for them; it never presses keys.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7,
        }));

        // ---- Hotkeys
        var hotkeyTab = Tab("Hotkeys");
        foreach (var (label, key) in new[]
                 {
                     ("Next step", nameof(Settings.HotkeyNext)), ("Previous step", nameof(Settings.HotkeyPrev)),
                     ("Show / hide", nameof(Settings.HotkeyToggle)), ("Lock / unlock", nameof(Settings.HotkeyLock)),
                     ("Box / compact / bar", nameof(Settings.HotkeyCompact)), ("Zone map mode", nameof(Settings.HotkeyMap)),
                     ("Capture zone map", nameof(Settings.HotkeyCapture)),
                     ("Build tree", nameof(Settings.HotkeyTree)),
                     ("Planner", nameof(Settings.HotkeyPlanner)),
                     ("Check hovered item", nameof(Settings.HotkeyLookup)),
                 })
        {
            var box = new TextBox();
            _hotkeys[key] = box;
            hotkeyTab.Children.Add(Row(label, box));
        }
        hotkeyTab.Children.Add(Indented(new TextBlock
        {
            Text = "Format: Ctrl+Shift+Right, Alt+F9 ... Leave empty to disable. " +
                   "Capture: open the in-game map, press the hotkey, and the picture is shown whenever you are in that zone.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        }));

        // ---- Version
        var versionTab = Tab("Version");
        versionTab.Children.Add(Heading("Version"));
        _updateInfo.Text = $"Installed: {Updater.Display(Updater.Current)}";
        versionTab.Children.Add(_updateInfo);
        var updateButtons = Buttons(("Look for update", LookForUpdate), ("What's new", _overlay.ShowChangelog), ("Releases on GitHub", () => Open(Updater.ReleasesPage)));
        _install.Click += (_, _) => InstallUpdate();
        updateButtons.Children.Insert(1, _install);
        versionTab.Children.Add(updateButtons);
        versionTab.Children.Add(_autoUpdate);
        versionTab.Children.Add(_shareCountry);
        versionTab.Children.Add(Indented(Buttons(("Open data folder", () => Open(_session.DataDir)))));
        ShowUpdateState();

        tabs.SelectedIndex = 0;
        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(tabs);
        root.Children.Add(_message);
        var footer = Buttons(("Apply", Apply), ("Close", Close));
        footer.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(footer);

        Content = root;

        _profile.SelectionChanged += (_, _) =>
        {
            if (_loading || _profile.SelectedItem is not ProfileItem item || item.Profile == _session.Profile) return;
            _session.Activate(item.Profile);
            LoadFromSession();
        };
        _route.SelectionChanged += (_, _) => _routeInfo.Text = (_route.SelectedItem as GuideRoute)?.Description ?? "";
        _route.DisplayMemberPath = nameof(GuideRoute.Name);
        LoadFromSession();
    }

    private sealed record ProfileItem(Profile Profile)
    {
        public override string ToString() => Profile.ClassId >= 0
            ? $"{Profile.Name}  ({Profile.ClassName(Profile.ClassId, Profile.Mastery)}, level {Profile.Level})"
            : Profile.Name;
    }

    // ------------------------------------------------------------------ layout helpers

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        FontSize = 14,
        Margin = new Thickness(0, 12, 0, 4),
    };

    private static Grid Row(string label, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        if (control is Slider slider) slider.AutoToolTipPlacement = AutoToolTipPlacement.TopLeft;
        if (control is Slider { Maximum: <= 1 } fine) fine.AutoToolTipPrecision = 2;
        return grid;
    }

    private static FrameworkElement Indented(FrameworkElement element)
    {
        element.Margin = new Thickness(120, 3, 0, 0);
        return element;
    }

    private static WrapPanel Buttons(params (string Text, Action Action)[] buttons)
    {
        var panel = new WrapPanel();
        foreach (var (text, action) in buttons)
        {
            var button = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 4) };
            button.Click += (_, _) => action();
            panel.Children.Add(button);
        }
        return panel;
    }

    private void ShowUpdateState()
    {
        if (_overlay.AvailableUpdate is not { } update)
        {
            _install.Visibility = Visibility.Collapsed;
            return;
        }
        _install.Content = $"Install {Updater.Display(update.Version)}";
        _install.Visibility = Visibility.Visible;
        string notes = update.Notes.Trim();
        _updateInfo.Text = $"Installed: {Updater.Display(Updater.Current)}.  Available: {Updater.Display(update.Version)}"
                           + (notes.Length > 0 ? "\n\n" + (notes.Length > 600 ? notes[..600] + "..." : notes) : "");
    }

    private async void LookForUpdate()
    {
        _updateInfo.Text = "Asking GitHub...";
        _updateInfo.Text = await _overlay.CheckForUpdateAsync();
        ShowUpdateState();
    }

    private async void InstallUpdate()
    {
        _install.IsEnabled = false;
        _updateInfo.Text = "Downloading and installing - the overlay restarts when it is done...";
        _updateInfo.Text = await _overlay.InstallUpdateAsync();
        _install.IsEnabled = true;
    }

    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    // ------------------------------------------------------------------ data

    private void LoadFromSession()
    {
        _loading = true;
        var settings = _session.Settings;
        var profile = _session.Profile;

        _profile.ItemsSource = _session.Store.Profiles.Select(p => new ProfileItem(p)).ToList();
        _profile.SelectedIndex = _session.Store.Profiles.IndexOf(profile);
        _name.Text = profile.Name;

        _route.ItemsSource = _session.Guide.Routes;
        _route.SelectedItem = _session.Route;

        var plans = new List<string> { NoPlan };
        plans.AddRange(Directory.GetFiles(_session.BuildsDir, "*.txt").Select(Path.GetFileName)!);
        _plan.ItemsSource = plans;
        _plan.SelectedItem = plans.Contains(profile.BuildPlan) ? profile.BuildPlan : NoPlan;

        _opacity.Value = settings.Opacity;
        _fontSize.Value = settings.FontSize;
        _width.Value = settings.Width;
        _mapMode.SelectedIndex = Math.Clamp(settings.MapMode, 0, 2);
        _autoHide.IsChecked = settings.AutoHide;
        _showTimer.IsChecked = settings.ShowTimer;
        _showBuild.IsChecked = settings.ShowBuild;
        _autoTick.IsChecked = settings.AutoTick;
        _autoLearn.IsChecked = settings.AutoLearn;
        _followKeys.IsChecked = settings.FollowGameKeys;
        _autoUpdate.IsChecked = settings.AutoCheckUpdates;
        _shareCountry.IsChecked = settings.ShareCountry;
        _followSkill.IsChecked = settings.FollowSkillOnScreen;
        _orderPassives.IsChecked = settings.ShowOrderPassives;
        _orderSkills.IsChecked = settings.ShowOrderSkills;
        _amountPassives.IsChecked = settings.ShowAmountPassives;
        _readPoints.IsChecked = settings.ReadPointsFromScreen;
        _readMap.IsChecked = settings.ReadCountersFromMap;
        _amountSkills.IsChecked = settings.ShowAmountSkills;
        _buildLines.IsChecked = settings.ShowBuildLines;
        _keyPassives.Text = settings.GameKeyPassives;
        _keySkills.Text = settings.GameKeySkills;
        _keyMap.Text = settings.GameKeyMap;
        _boxMode.SelectedIndex = Math.Clamp(settings.BoxMode, 0, 2);
        _treeScale.Value = Math.Clamp(settings.TreeScale, 0.5, 1);
        _hideGuide.IsChecked = profile.HideGuide;
        foreach (var (box, get, _) in _parts) box.IsChecked = get(settings);
        foreach (var (key, box) in _hotkeys)
            box.Text = (string)typeof(Settings).GetProperty(key)!.GetValue(settings)!;
        _loading = false;
    }

    private void Apply()
    {
        var settings = _session.Settings;
        var profile = _session.Profile;

        var invalid = _hotkeys.Values.Select(b => b.Text.Trim())
            .Where(t => t.Length > 0 && !HotkeyManager.TryParse(t, out _, out _)).ToList();
        if (invalid.Count > 0)
        {
            _message.Text = "Not a valid hotkey: " + string.Join(", ", invalid);
            return;
        }

        string name = _name.Text.Trim();
        if (name.Length > 0) profile.Name = name;

        string plan = _plan.SelectedItem as string ?? NoPlan;
        profile.BuildPlan = plan == NoPlan ? "" : plan;
        _session.ReloadPlan();

        settings.Opacity = _opacity.Value;
        settings.FontSize = _fontSize.Value;
        settings.Width = _width.Value;
        settings.MapMode = _mapMode.SelectedIndex;
        settings.AutoHide = _autoHide.IsChecked == true;
        settings.ShowTimer = _showTimer.IsChecked == true;
        settings.ShowBuild = _showBuild.IsChecked == true;
        settings.AutoTick = _autoTick.IsChecked == true;
        settings.AutoLearn = _autoLearn.IsChecked == true;
        settings.FollowGameKeys = _followKeys.IsChecked == true;
        settings.AutoCheckUpdates = _autoUpdate.IsChecked == true;
        settings.ShareCountry = _shareCountry.IsChecked == true;
        settings.FollowSkillOnScreen = _followSkill.IsChecked == true;
        settings.ShowOrderPassives = _orderPassives.IsChecked == true;
        settings.ShowOrderSkills = _orderSkills.IsChecked == true;
        settings.ShowAmountPassives = _amountPassives.IsChecked == true;
        settings.ReadPointsFromScreen = _readPoints.IsChecked == true;
        settings.ReadCountersFromMap = _readMap.IsChecked == true;
        settings.ShowAmountSkills = _amountSkills.IsChecked == true;
        settings.ShowBuildLines = _buildLines.IsChecked == true;
        if (KeyboardWatcher.VirtualKey(_keyPassives.Text) != 0) settings.GameKeyPassives = _keyPassives.Text.Trim();
        if (KeyboardWatcher.VirtualKey(_keySkills.Text) != 0) settings.GameKeySkills = _keySkills.Text.Trim();
        if (KeyboardWatcher.VirtualKey(_keyMap.Text) != 0) settings.GameKeyMap = _keyMap.Text.Trim();
        profile.HideGuide = _hideGuide.IsChecked == true;
        settings.BoxMode = Math.Max(0, _boxMode.SelectedIndex);
        settings.TreeScale = _treeScale.Value;
        foreach (var (box, _, set) in _parts) set(settings, box.IsChecked == true);
        foreach (var (key, box) in _hotkeys)
            typeof(Settings).GetProperty(key)!.SetValue(settings, box.Text.Trim());

        // Changing route restarts the step list, so only do it when it really changed.
        if (_route.SelectedItem is GuideRoute route && route != _session.Route
            && MainWindow.Confirm($"Switch to '{route.Name}'?\n\nThis restarts the step list for {profile.Name}: ticked lines and chapter times are cleared."))
            _session.SetRoute(route.Id);

        _session.Save();
        _session.ApplySettings();
        Applied?.Invoke();
        LoadFromSession();
        _message.Text = "Saved.";
    }

    private void Import()
    {
        if (string.IsNullOrWhiteSpace(_importLink.Text))
        {
            _message.Text = "Paste a Maxroll planner or build guide link first.";
            return;
        }
        ImportFrom(_importLink.Text);
    }

    /// <summary>
    /// Fetches Maxroll's Weaver trees (the endgame tree that is the same for every build) for the Weaver
    /// tab of the build tree. Asked for with the button, or done quietly along with a build import.
    /// </summary>
    private async void GetWeaver(bool announce)
    {
        if (announce) _message.Text = "Fetching the Weaver trees...";
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LastEpochHelper/0.2");
            var set = await MaxrollImporter.ImportWeaverAsync(_session.DataDir, http);
            _session.SetWeaver(set);
            Applied?.Invoke();
            if (announce)
                _message.Text = $"Got {set.Strategies.Count} Weaver trees ({string.Join(", ", set.Strategies.Select(s => s.Name))}). "
                                + (_session.Tree is null ? "They show as a tab of the build tree once a build is imported." : "They are on the Weaver tab of the build tree; choose one in the gold box at its bottom.");
        }
        catch (InvalidDataException e) { if (announce) _message.Text = e.Message; }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            if (announce) _message.Text = "Could not reach Maxroll (" + e.Message + "). Check your connection and try again.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (announce) _message.Text = "Could not save the Weaver trees: " + e.Message;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            ActivityLog.Write($"fetching the Weaver trees failed unexpectedly: {e.GetType().Name}: {e.Message}");
            if (announce) _message.Text = "Fetching the Weaver trees failed in a way the overlay did not expect (" + e.GetType().Name + ").";
        }
    }

    /// <summary>A build someone sent as a file: this overlay's own build file, or a planner saved as JSON.</summary>
    private void ImportFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a build from a file",
            Filter = "Build files (*.tree.json;*.json)|*.tree.json;*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true) ImportFrom(dialog.FileName);
    }

    private async void ImportFrom(string link)
    {
        _message.Text = "Importing...";
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LastEpochHelper/0.2");
            // A path to a file on this computer can be pasted in the link box too.
            string asPath = link.Trim().Trim('"');
            var results = asPath.IndexOfAny(Path.GetInvalidPathChars()) < 0 && File.Exists(asPath)
                ? await MaxrollImporter.ImportFileAsync(asPath, _session.DataDir, http)
                : await MaxrollImporter.ImportAsync(link, _session.DataDir, http);

            // A build guide brings several versions of the build; each gets its own pair of files, and
            // each knows the others' names so the tree window can switch without downloading again.
            var files = BuildFiles.Write(_session.BuildsDir, results);
            var result = results[0];
            _session.Profile.BuildPlan = files[0];
            // Importing again (a newer version of the guide) must not undo what the player has set:
            // only the ticks on the old plan's own lines mean nothing now.
            _session.Profile.PlanDone.RemoveWhere(key => key.StartsWith("plan:", StringComparison.Ordinal));
            var trees = result.Tree.Trees.Select(t => t.Name).ToHashSet();
            foreach (string gone in _session.Profile.SkillPoints.Keys.Where(k => !trees.Contains(k)).ToList())
                _session.Profile.SkillPoints.Remove(gone);
            if (!trees.Contains(_session.Profile.TreeTab)) _session.Profile.TreeTab = "";
            _session.Profile.StagePin = "";
            _session.ReloadPlan();
            _session.Save();
            LoadFromSession();
            Applied?.Invoke();
            // The Weaver trees come along, unless they were fetched recently.
            if (_session.Weaver is null || DateTime.Now - _session.Weaver.Fetched > TimeSpan.FromDays(7)) GetWeaver(announce: false);
            _message.Text = $"Imported '{result.Name}' ({result.Steps} points). The order is exact; the levels are estimates."
                + (results.Count > 1
                    ? $" This guide has {results.Count} versions of the build ({string.Join(", ", result.Tree.Variants)}); switch between them at the bottom of the build tree."
                    : result.Tree.Stages.Count > 1 ? $" It has {result.Tree.Stages.Count} stages by level; the overlay follows your level, or pick one at the bottom of the build tree." : "")
                // Some planners fill every node to show a tree off; a character has 113 passive points at most.
                + (result.Tree.Stages.Any(s => s.Passives.Count > 120) ? " Note: this planner has more passive points placed than a character can have - it is a showcase, not a tree to follow point by point." : "");
        }
        catch (InvalidDataException e)
        {
            _message.Text = e.Message; // already a sentence for the player
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            _message.Text = "Could not reach Maxroll (" + e.Message + "). Check your connection and try again.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _message.Text = "Could not save the build: " + e.Message;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Not foreseen: say so, and leave a trace for a bug report.
            ActivityLog.Write($"import failed unexpectedly: {e.GetType().Name}: {e.Message}");
            _message.Text = "Import failed in a way the overlay did not expect (" + e.GetType().Name + "). Menu (☰) → Report a bug sends us the details.";
        }
    }

    private void NewPlan()
    {
        string path = Path.Combine(_session.BuildsDir, "my-build.txt");
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(_session.BuildsDir, $"my-build-{n}.txt");
        File.WriteAllText(path, BuildPlan.Template);
        _session.Profile.BuildPlan = Path.GetFileName(path);
        _session.ReloadPlan();
        _session.Save();
        LoadFromSession();
        Applied?.Invoke();
        Open(path);
        _message.Text = "Edit the file, save it, then press Apply to reload it.";
    }

    private void NewProfile()
    {
        var profile = _session.CreateProfile("New character");
        _session.Activate(profile);
        LoadFromSession();
        _message.Text = "New profile created. It takes over the next character that enters the game, or rename it here.";
    }

    private void DeleteProfile()
    {
        if (_session.Store.Profiles.Count <= 1)
        {
            _message.Text = "The last profile cannot be deleted - use 'Reset this character's progress' in the overlay menu instead.";
            return;
        }
        var answer = MessageBox.Show(this, $"Delete profile '{_session.Profile.Name}' and its progress?", "Delete profile",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;
        _session.DeleteProfile(_session.Profile);
        LoadFromSession();
    }
}
