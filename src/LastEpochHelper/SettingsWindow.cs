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
    private readonly CheckBox _followSkill = new() { Content = "Switch skill tab to the skill tree open in the game (reads the screen)" };
    private readonly CheckBox _orderPassives = new() { Content = "Passive tree: number the next points in order" };
    private readonly CheckBox _orderSkills = new() { Content = "Skill trees: number the next points in order" };
    private readonly CheckBox _amountPassives = new() { Content = "Passive tree: show how many points go into the node (+N)" };
    private readonly CheckBox _amountSkills = new() { Content = "Skill trees: show how many points go into the node (+N)" };
    private readonly CheckBox _buildLines = new() { Content = "List the build's per-level steps as text in the overlay box" };
    private readonly TextBox _keyPassives = new();
    private readonly TextBox _keySkills = new();
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
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        var root = new StackPanel { Margin = new Thickness(14) };

        root.Children.Add(Heading("Character"));
        root.Children.Add(Row("Profile", _profile));
        root.Children.Add(Row("Name", _name));
        root.Children.Add(Row("Route", _route));
        root.Children.Add(Indented(_routeInfo));
        root.Children.Add(Row("Build plan", _plan));
        root.Children.Add(Row("Maxroll link", _importLink));
        _importLink.ToolTip = "A Maxroll Last Epoch planner link (maxroll.gg/last-epoch/planner/...) or build guide link";
        root.Children.Add(Indented(Buttons(
            ("Import from Maxroll", Import), ("New empty plan", NewPlan), ("Open plans folder", () => Open(_session.BuildsDir)),
            ("New profile", NewProfile), ("Delete profile", DeleteProfile))));

        root.Children.Add(Heading("Overlay"));
        root.Children.Add(Row("Opacity", _opacity));
        root.Children.Add(Row("Text size", _fontSize));
        root.Children.Add(Row("Width", _width));
        root.Children.Add(Row("Zone map", _mapMode));
        foreach (var box in new[] { _autoHide, _showTimer, _showBuild, _autoTick, _autoLearn, _buildLines, _followKeys, _followSkill, _orderPassives, _amountPassives, _orderSkills, _amountSkills })
        {
            box.Margin = new Thickness(0, 4, 0, 0);
            root.Children.Add(box);
        }
        root.Children.Add(Row("Game key: passives", _keyPassives));
        root.Children.Add(Row("Game key: skills", _keySkills));

        root.Children.Add(Heading("Hotkeys"));
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
            root.Children.Add(Row(label, box));
        }
        root.Children.Add(Indented(new TextBlock
        {
            Text = "Format: Ctrl+Shift+Right, Alt+F9 ... Leave empty to disable. " +
                   "Capture: open the in-game map, press the hotkey, and the picture is shown whenever you are in that zone.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        }));

        root.Children.Add(Heading("Version"));
        _updateInfo.Text = $"Installed: {Updater.Display(Updater.Current)}";
        root.Children.Add(_updateInfo);
        var updateButtons = Buttons(("Look for update", LookForUpdate), ("What's new", _overlay.ShowChangelog), ("Releases on GitHub", () => Open(Updater.ReleasesPage)));
        _install.Click += (_, _) => InstallUpdate();
        updateButtons.Children.Insert(1, _install);
        root.Children.Add(updateButtons);
        root.Children.Add(_autoUpdate);
        ShowUpdateState();

        root.Children.Add(_message);
        var footer = Buttons(("Apply", Apply), ("Open data folder", () => Open(_session.DataDir)), ("Close", Close));
        footer.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Margin = new Thickness(0, 12, 0, 0);
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
        _followSkill.IsChecked = settings.FollowSkillOnScreen;
        _orderPassives.IsChecked = settings.ShowOrderPassives;
        _orderSkills.IsChecked = settings.ShowOrderSkills;
        _amountPassives.IsChecked = settings.ShowAmountPassives;
        _amountSkills.IsChecked = settings.ShowAmountSkills;
        _buildLines.IsChecked = settings.ShowBuildLines;
        _keyPassives.Text = settings.GameKeyPassives;
        _keySkills.Text = settings.GameKeySkills;
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
        settings.FollowSkillOnScreen = _followSkill.IsChecked == true;
        settings.ShowOrderPassives = _orderPassives.IsChecked == true;
        settings.ShowOrderSkills = _orderSkills.IsChecked == true;
        settings.ShowAmountPassives = _amountPassives.IsChecked == true;
        settings.ShowAmountSkills = _amountSkills.IsChecked == true;
        settings.ShowBuildLines = _buildLines.IsChecked == true;
        if (KeyboardWatcher.VirtualKey(_keyPassives.Text) != 0) settings.GameKeyPassives = _keyPassives.Text.Trim();
        if (KeyboardWatcher.VirtualKey(_keySkills.Text) != 0) settings.GameKeySkills = _keySkills.Text.Trim();
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

    private async void Import()
    {
        if (string.IsNullOrWhiteSpace(_importLink.Text))
        {
            _message.Text = "Paste a Maxroll planner or build guide link first.";
            return;
        }
        _message.Text = "Importing...";
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LastEpochHelper/0.2");
            var result = await MaxrollImporter.ImportAsync(_importLink.Text, _session.DataDir, http);

            string file = string.Concat(result.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim() + ".txt";
            string path = Path.Combine(_session.BuildsDir, file);
            File.WriteAllText(path, result.Text);
            result.Tree.Save(BuildTree.PathFor(path));
            _session.Profile.BuildPlan = file;
            _session.Profile.PlanDone.Clear();
            _session.Profile.SkillPoints.Clear();
            _session.Profile.PassiveOffset = 0;
            _session.Profile.TreeTab = "";
            _session.ReloadPlan();
            _session.Save();
            LoadFromSession();
            Applied?.Invoke();
            _message.Text = $"Imported '{result.Name}' ({result.Steps} points). The order is exact; the levels are estimates.";
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or InvalidDataException or IOException
                                      or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException
                                      or KeyNotFoundException or NullReferenceException or ArgumentException)
        {
            _message.Text = "Import failed: " + e.Message;
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
