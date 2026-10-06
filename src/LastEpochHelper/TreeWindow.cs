using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LastEpochHelper.Core;

namespace LastEpochHelper;

/// <summary>
/// A picture of the passive tree or a skill tree laid out as in the game, showing how many points
/// each node should have by now and which points come next. Sits beside the game's own tree panel.
/// </summary>
internal sealed class TreeWindow : Window
{
    private const double CanvasWidth = 860, CanvasHeight = 440, Inset = 50, NodeSize = 40, SmallNodeSize = 24;
    /// <summary>A tree with this many nodes (the Weaver tree has 80) gets a taller picture, smaller nodes and fewer names.</summary>
    private const int DenseNodes = 45;
    private const double DenseCanvasHeight = 680, DenseNodeSize = 32, DenseSmallNodeSize = 18;

    private static readonly Brush Gold = Frozen("#C9A85C");
    private static readonly Brush GoldFill = Frozen("#4A3A14");
    private static readonly Brush Green = Frozen("#7BE06A");
    private static readonly Brush GreenFill = Frozen("#1F4A1A");
    private static readonly Brush Planned = Frozen("#7A6A3E");
    private static readonly Brush PlannedFill = Frozen("#1B1A16");
    private static readonly Brush Dim = Frozen("#33FFFFFF");
    private static readonly Brush Line = Frozen("#3AFFFFFF");
    private static readonly Brush Red = Frozen("#FF6B5C");
    private static readonly Brush Text = Frozen("#E8E4D8");
    private static readonly Brush Muted = Frozen("#A9A493");
    // The two markers on a "next" node have their own colours, so they can be told apart at a glance.
    private static readonly Brush OrderBlue = Frozen("#5BB8FF");
    private static readonly Brush AmountOrange = Frozen("#FF9A3D");
    private static readonly Brush Amber = Frozen("#FFC857");
    private static readonly Brush Plate = Frozen("#F00E0F14");

    private readonly Session _session;
    // Wraps onto a second row rather than widening the window when the tabs (with their counts) do not fit.
    private readonly WrapPanel _tabs = new() { Orientation = Orientation.Horizontal, MaxWidth = CanvasWidth - 30 };
    private readonly Canvas _canvas = new() { Width = CanvasWidth, Height = CanvasHeight, ClipToBounds = true };
    private readonly TextBlock _next = new() { TextWrapping = TextWrapping.Wrap, Foreground = Text, Margin = new Thickness(2, 10, 2, 0), LineHeight = 20 };
    private readonly TextBlock _points = new() { Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
    // One line at the top saying what the picture is: the character's own points, the guide's plan, or a look ahead.
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Text, VerticalAlignment = VerticalAlignment.Center, MaxWidth = CanvasWidth - 200 };
    private Border _back = null!;
    private Border _lookChip = null!;
    /// <summary>The slider is out (for looking at the plan at another number of points).</summary>
    private bool _lookAhead;
    /// <summary>Reads of each tree's points in a row that could not be placed on the tree.</summary>
    private readonly Dictionary<string, int> _readMisses = new();
    private const int ReadTroubleAfter = 3;
    private Border _stage = null!;
    private Border _mini = null!;
    private Border _canvasFrame = null!;
    private Border _root = null!;
    private Border _minus = null!, _plus = null!;
    private readonly Slider _slider = new() { Minimum = 0, Maximum = 20, Width = 260, IsSnapToTickEnabled = true, TickFrequency = 1, Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _sliderLabel = new() { Foreground = Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _sliderValue = new() { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private DockPanel _sliderRow = null!;
    private bool _settingSlider;
    private readonly TextBlock _empty = new()
    {
        Foreground = Muted, TextWrapping = TextWrapping.Wrap, Width = 420, TextAlignment = TextAlignment.Center,
        Text = "No build imported for this character.\n\nOpen settings (the gear on the overlay), paste a Maxroll or Last Epoch Tools build link and press 'Import build'.",
    };

    private BitmapSource? _atlas;
    private bool _atlasTried;
    private string _atlasName = "";
    private string? _signature;
    private DateTime _atlasRetryAt = DateTime.MaxValue;
    private readonly Dictionary<int, ImageBrush> _icons = new();

    /// <summary>Raised when the window was dragged or closed by the user, so the owner can save that.</summary>
    public event Action? Moved;
    public event Action? CloseRequested;

    public TreeWindow(Session session)
    {
        _session = session;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI");
        Theme.Controls(this);
        FontSize = 13;

        var close = HeaderButton("✕", () => CloseRequested?.Invoke());
        var header = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(_tabs);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            DragMove();
            Moved?.Invoke();
        };

        var minus = _minus = HeaderButton("−", () => Adjust(-1));
        var plus = _plus = HeaderButton("+", () => Adjust(+1));
        minus.ToolTip = "One point fewer";
        plus.ToolTip = "One point more";
        // Which part of the guide the tree follows: a stage by level, or a version of the build.
        _stage = HeaderButton("stage", ChooseStage);
        // The slider is a tool for planning ahead; it stays put away until asked for.
        _lookChip = HeaderButton("↔ Look ahead", ToggleLookAhead);
        _back = HeaderButton("Back to my character", () => { if (Current() is { } tree) { _lookAhead = false; _session.BackToCharacter(tree); Render(); } });
        var adjust = new StackPanel { Orientation = Orientation.Horizontal };
        adjust.Children.Add(_stage);
        adjust.Children.Add(_lookChip);
        // For a small screen: put the picture away and keep just the tabs and the "next points" line.
        _mini = HeaderButton("mini", () =>
        {
            _session.Settings.TreeMini = !_session.Settings.TreeMini;
            _session.SaveSettings();
            Render();
        });
        adjust.Children.Add(_mini);

        // The slider is always there. It shows how many points the tree is drawn with; moving it asks
        // for the plan at that many points (hover any control for an explanation).
        _sliderRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        foreach (var chip in new[] { _stage, _lookChip, _mini, _back })
        {
            chip.BorderThickness = new Thickness(1);
            chip.CornerRadius = new CornerRadius(10);
            chip.Padding = new Thickness(10, 2, 10, 3);
            chip.Margin = new Thickness(6, 0, 0, 0);
        }
        _slider.ValueChanged += (_, _) =>
        {
            if (_settingSlider || Current() is not { } tree) return;
            _session.SetTreePoints(tree, (int)_slider.Value);
        };
        DockPanel.SetDock(adjust, Dock.Right);
        _sliderRow.Children.Add(adjust);
        foreach (var element in new UIElement[] { _sliderLabel, minus, _slider, plus, _sliderValue })
        {
            DockPanel.SetDock(element, Dock.Left);
            _sliderRow.Children.Add(element);
        }

        var body = new StackPanel();
        body.Children.Add(header);
        _back.Margin = new Thickness(10, 0, 0, 0);
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 6, 2, 2) };
        statusRow.Children.Add(_status);
        statusRow.Children.Add(_back);
        body.Children.Add(statusRow);
        _canvasFrame = new Border { Child = _canvas, Margin = new Thickness(0, 4, 0, 0), Background = Frozen("#12FFFFFF"), CornerRadius = new CornerRadius(7) };
        body.Children.Add(_canvasFrame);
        body.Children.Add(_next);
        body.Children.Add(_sliderRow);

        Content = _root = new Border
        {
            Background = Theme.PlateBrush,
            BorderBrush = Theme.Border,
            BorderThickness = Theme.Edge,
            CornerRadius = Theme.Corners,
            Padding = new Thickness(12, 7, 12, 12),
            Child = body,
        };
        SourceInitialized += (_, _) => Native.ApplyOverlayStyle(new WindowInteropHelper(this).Handle, clickThrough: false);
        // Growing or shrinking (another tab, mini, another size) must not leave part of it off the screen.
        SizeChanged += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            if (Left + ActualWidth > area.Right) Left = Math.Max(area.Left, area.Right - ActualWidth);
            if (Top + ActualHeight > area.Bottom) Top = Math.Max(area.Top, area.Bottom - ActualHeight);
        };
    }

    /// <summary>
    /// The size chosen in the settings - but never larger than the screen allows, so the window is
    /// usable on a small monitor without the player having to find the setting first.
    /// </summary>
    private double Scale()
    {
        var area = SystemParameters.WorkArea;
        double fit = Math.Min((area.Width - 16) / (CanvasWidth + 50), (area.Height - 16) / (_canvas.Height + 200));
        return Math.Clamp(Math.Min(_session.Settings.TreeScale, fit), 0.4, 1);
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static Border HeaderButton(string text, Action onClick, bool selected = false, int? count = null)
    {
        var label = new TextBlock { Text = text, Foreground = selected ? Brushes.White : Muted, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal };
        // Points, in the same orange as the amounts on the nodes.
        if (count is { } points) label.Inlines.Add(new Run($"  {points}") { Foreground = AmountOrange, FontWeight = FontWeights.SemiBold });
        var button = new Border
        {
            Child = label,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 3, 0),
            CornerRadius = new CornerRadius(5),
            Background = selected ? Frozen("#33C9A85C") : Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        button.MouseLeftButtonDown += (_, e) => { onClick(); e.Handled = true; };
        return button;
    }

    /// <summary>
    /// The icon sheet downloaded at import time. Needs Windows' WebP codec; without it (or if the
    /// sheet does not match the build's indexes) nodes are simply drawn without pictures.
    /// </summary>
    private ImageBrush? IconBrush(TreeNode node, BuildTree build)
    {
        if (node.IconIndex < 0) return null;
        string sheet = build.AtlasName.Length > 0 ? build.AtlasName : BuildTree.AtlasFile;
        if (sheet != _atlasName)
        {
            ResetIcons(); // another build, made with another sheet
            _atlasName = sheet;
        }
        if (!_atlasTried || (_atlas is null && DateTime.UtcNow >= _atlasRetryAt))
        {
            _atlasTried = true;
            _atlasRetryAt = DateTime.MaxValue;
            try
            {
                string path = System.IO.Path.Combine(_session.DataDir, sheet);
                if (File.Exists(path))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(path);
                    image.EndInit();
                    image.Freeze();
                    _atlas = image;
                }
            }
            catch (Exception e)
            {
                // Whatever went wrong, the tree is still usable without pictures - but say why, once.
                _atlas = null;
                _atlasRetryAt = DateTime.UtcNow.AddSeconds(5); // a busy moment should not cost the icons for the whole session
                try { File.AppendAllText(System.IO.Path.Combine(_session.DataDir, "errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\nTree icons unavailable: {e}\n\n"); }
                catch (IOException) { }
            }
        }
        if (_atlas is null) return null;
        int rows = (build.AtlasCells + BuildTree.AtlasColumns - 1) / BuildTree.AtlasColumns;
        if (_atlas.PixelHeight != rows * BuildTree.AtlasCell || _atlas.PixelWidth != BuildTree.AtlasColumns * BuildTree.AtlasCell) return null;
        if (_icons.TryGetValue(node.IconIndex, out var cached)) return cached;

        var cell = new Int32Rect(node.IconIndex % BuildTree.AtlasColumns * BuildTree.AtlasCell,
            node.IconIndex / BuildTree.AtlasColumns * BuildTree.AtlasCell, BuildTree.AtlasCell, BuildTree.AtlasCell);
        var brush = new ImageBrush(new CroppedBitmap(_atlas, cell)) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        return _icons[node.IconIndex] = brush;
    }

    /// <summary>Forget the loaded icon sheet, e.g. after a new import replaced it.</summary>
    public void ResetIcons()
    {
        _atlas = null;
        _atlasTried = false;
        _signature = null;
        _icons.Clear();
    }

    private TreeDef? Current()
    {
        var trees = _session.Tree?.Trees;
        if (trees is null || trees.Count == 0) return null;
        return trees.FirstOrDefault(t => t.Name == _session.Profile.TreeTab) ?? trees[0];
    }

    /// <summary>
    /// The tabs to offer. A guide with stages names different skills at different levels; showing only
    /// the ones of the stage in use keeps skills dropped long ago (or not picked up yet) out of the way.
    /// The tab that is showing always stays - the game may have a skill open that the stage does not use.
    /// </summary>
    private List<TreeDef> VisibleTrees(BuildTree build, TreeDef? showing)
    {
        // The Weaver tree is an endgame thing: no tab for it while the character is still leveling.
        bool Offered(TreeDef t) => t.Kind != TreeDef.WeaverKind || t == showing || _session.InEndgame;
        if (build.Stages.Count < 2 || _session.Stage is not { } stage) return build.Trees.Where(Offered).ToList();
        return build.Trees.Where(t => Offered(t) && (t.Kind == TreeDef.PassiveKind || t == showing || stage.Skills.ContainsKey(t.Name))).ToList();
    }

    /// <summary>Shows the first passive tab, or the last-used (else first) skill tab.</summary>
    public void SelectKind(string kind)
    {
        var trees = _session.Tree?.Trees;
        if (trees is null) return;
        if (Current() is { } current && current.Kind == kind) return;
        string? remembered = kind == TreeDef.SkillKind ? _session.Profile.LastSkillTab : null;
        var offered = VisibleTrees(_session.Tree!, null);
        var target = offered.FirstOrDefault(t => t.Kind == kind && t.Name == remembered) ?? offered.FirstOrDefault(t => t.Kind == kind)
                     ?? trees.FirstOrDefault(t => t.Kind == kind);
        if (target is not null) Select(target);
    }

    public string? CurrentKind => Current()?.Kind;

    /// <summary>Names of the skill tabs, for matching against text on screen.</summary>
    public IEnumerable<string> SkillNames =>
        _session.Tree?.Trees.Where(t => t.Kind == TreeDef.SkillKind).Select(t => t.Name) ?? Enumerable.Empty<string>();

    /// <summary>Tab names of the game's passive panel, for recognising it on screen.</summary>
    public IEnumerable<string> PassiveTabNames =>
        _session.Tree is not { } build ? Enumerable.Empty<string>()
        : build.PassiveTabNames.Count > 0 ? build.PassiveTabNames
        : build.Trees.Where(t => t.Kind == TreeDef.PassiveKind).Select(t => t.Name);

    /// <summary>Switches to the named skill tab if it is not already showing.</summary>
    public void SelectSkill(string name)
    {
        var tree = _session.Tree?.Trees.FirstOrDefault(t => t.Kind == TreeDef.SkillKind && t.Name == name);
        if (tree is not null && tree != Current()) Select(tree);
    }

    private bool MarkersOn(TreeDef tree) =>
        tree.Kind == TreeDef.PassiveKind ? _session.Settings.ShowOrderPassives : _session.Settings.ShowOrderSkills;

    private bool AmountsOn(TreeDef tree) =>
        tree.Kind == TreeDef.PassiveKind ? _session.Settings.ShowAmountPassives : _session.Settings.ShowAmountSkills;

    private void ToggleLookAhead()
    {
        if (Current() is not { } tree) return;
        bool open = _lookAhead || _session.IsPreviewing(tree);
        _lookAhead = !open;
        // Putting the slider away ends the look ahead too.
        if (open) _session.BackToCharacter(tree);
        Render();
    }

    /// <summary>How a read of a tree's points off the game's panel went: placed on the tree, or not.</summary>
    public void ReadResult(string treeName, bool fit)
    {
        int before = _readMisses.GetValueOrDefault(treeName);
        int now = fit ? 0 : before + 1;
        _readMisses[treeName] = now;
        // Only crossing the line changes what the window says.
        if ((before >= ReadTroubleAfter) != (now >= ReadTroubleAfter)) Render();
    }

    private bool ReadTrouble(TreeDef tree) => _readMisses.GetValueOrDefault(tree.Name) >= ReadTroubleAfter;

    /// <summary>The tree was closed: next time it opens on the character, with the slider put away.</summary>
    public void ResetView()
    {
        _lookAhead = false;
        _readMisses.Clear();
        _signature = null;
    }

    /// <summary>Switches to the given tab (if it is not the one showing).</summary>
    public void SelectTab(TreeDef tree)
    {
        if (tree != Current()) Select(tree);
    }

    private void Select(TreeDef tree)
    {
        _session.Profile.TreeTab = tree.Name;
        if (tree.Kind == TreeDef.SkillKind) _session.Profile.LastSkillTab = tree.Name;
        _session.Save();
        Render();
    }

    private void Adjust(int delta)
    {
        if (Current() is { } tree) _session.SetTreePoints(tree, (int)_slider.Value + delta);
    }

    public void Render()
    {
        // The picture is as tall as the tree on show needs (set before the size is worked out).
        _canvas.Height = Current() is { Nodes.Count: > DenseNodes } ? DenseCanvasHeight : CanvasHeight;
        double scale = Scale();
        if (_root.LayoutTransform is not ScaleTransform { ScaleX: var current } || Math.Abs(current - scale) > 0.001)
            _root.LayoutTransform = scale < 0.999 ? new ScaleTransform(scale, scale) : Transform.Identity;

        var build = _session.Tree;
        var tree = Current();
        if (build is null || tree is null)
        {
            _signature = null;
            _tabs.Children.Clear();
            _canvas.Children.Clear();
            _tabs.Children.Add(new TextBlock { Text = "Build tree", Foreground = Gold, FontWeight = FontWeights.SemiBold });
            Canvas.SetLeft(_empty, (CanvasWidth - _empty.Width) / 2);
            Canvas.SetTop(_empty, _canvas.Height / 2 - 40);
            _canvas.Children.Add(_empty);
            _next.Text = "";
            _status.Text = "";
            _back.Visibility = Visibility.Collapsed;
            _sliderRow.Visibility = Visibility.Collapsed;
            return;
        }

        // Rebuilding the picture makes it blink, and this is called for every change anywhere in the
        // overlay - so first see whether anything this window shows is different from last time.
        var state = _session.TreeState(tree);
        var tabs = VisibleTrees(build, tree);
        string signature = string.Join("|", build.Name, tree.Name, string.Join(",", tabs.Select(t => t.Name)),
            state.Points, state.StagePoints, state.Stage, state.FromGame,
            string.Join(",", state.Allocated.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}")),
            string.Join(",", state.Target.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}")),
            string.Join(",", state.Next.Select(n => $"{n.Node}+{n.Count}")), string.Join(",", state.OffPlan.OrderBy(n => n)),
            MarkersOn(tree), AmountsOn(tree), _session.HasActual(tree), _session.ActualPoints(tree),
            _lookAhead, _session.IsPreviewing(tree), ReadTrouble(tree), _session.Settings.ReadPointsFromScreen,
            _session.ActualUpdated is { } read ? Age(read) : "", _session.Profile.Level, _session.Rewards().Passive, _atlas is null,
            _session.Profile.StagePin, build.Variant, _session.Stage?.Name, _session.Settings.TreeMini, _session.WeaverChoice, _session.WeaverChoices.Count, _session.InEndgame);
        bool iconRetryDue = _atlas is null && _atlasTried && DateTime.UtcNow >= _atlasRetryAt;
        if (signature == _signature && !iconRetryDue) return;
        _signature = signature;
        _tabs.Children.Clear();
        _canvas.Children.Clear();

        string? previousKind = null;
        foreach (var tab in tabs)
        {
            // A divider between the passive tabs and the skill tabs.
            if (previousKind is not null && previousKind != tab.Kind)
                _tabs.Children.Add(new TextBlock { Text = "│", Foreground = Dim, Margin = new Thickness(2, 1, 5, 0) });
            previousKind = tab.Kind;
            var captured = tab;
            if (tab.Kind != TreeDef.PassiveKind)
            {
                _tabs.Children.Add(HeaderButton(tab.Name, () => Select(captured), tab == tree));
                continue;
            }
            // Every passive tab says how many points the build has there by now, so the class tree and
            // the other masteries can be followed while one of them is showing.
            var (now, byEnd, stageName) = _session.PlannedInTab(tab);
            var button = HeaderButton(tab.Name, () => Select(captured), tab == tree, now);
            button.ToolTip = $"The build has {now} point{(now == 1 ? "" : "s")} in {tab.Name} by now"
                             + (byEnd != now ? $", {byEnd} by the end of {(stageName.Length > 0 ? $"'{stageName}'" : "this stage")}." : ".");
            _tabs.Children.Add(button);
        }

        _sliderRow.Visibility = Visibility.Visible;
        DrawTree(build, tree, state);

        bool passive = tree.Kind == TreeDef.PassiveKind;
        ShowStageChip(build, tree);
        ShowStatus(tree, state, passive);
        // The same colours as the markers on the nodes: blue for the order, orange for the points.
        _next.Inlines.Clear();
        if (state.Next.Count == 0)
            _next.Inlines.Add(new Run(state.StagePoints == 0 ? "No points planned here yet." : "Nothing more planned in this tree.") { Foreground = Muted });
        else
        {
            _next.Inlines.Add(new Run("NEXT   ") { Foreground = Muted, FontSize = 11, FontWeight = FontWeights.SemiBold });
            for (int i = 0; i < state.Next.Count; i++)
            {
                var (id, count) = state.Next[i];
                _next.Inlines.Add(new Run($"{i + 1}  ") { Foreground = OrderBlue, FontWeight = FontWeights.Bold });
                _next.Inlines.Add(new Run(build.NodeName(id, passive, tree.Name)));
                _next.Inlines.Add(new Run($"  +{count}") { Foreground = AmountOrange, FontWeight = FontWeights.Bold });
                if (i < state.Next.Count - 1) _next.Inlines.Add(new Run("       "));
            }
        }

        // Mini: just the tabs, the status, the "next points" line and the switches.
        bool mini = _session.Settings.TreeMini;
        var whole = mini ? Visibility.Collapsed : Visibility.Visible;
        _canvasFrame.Visibility = whole;
        bool looking = (_lookAhead || _session.IsPreviewing(tree)) && !mini;
        foreach (var part in new UIElement[] { _sliderLabel, _minus, _slider, _plus, _sliderValue }) part.Visibility = looking ? Visibility.Visible : Visibility.Collapsed;
        Chip(_lookChip, looking, Gold, Frozen("#33C9A85C"));
        _lookChip.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
        _lookChip.ToolTip = looking
            ? "Put the slider away and show your character again."
            : "Take out a slider to see the guide's plan at any number of points - ahead of where you are, or back.";
        ((TextBlock)_mini.Child).Text = mini ? "▣ Full" : "▁ Mini";
        Chip(_mini, mini, Gold, Frozen("#33C9A85C"));
        _mini.ToolTip = mini ? "Show the tree again." : "Hide the picture and keep only the tabs and the next points - for a small screen.";

        // The slider always shows the number of points the tree is drawn with.
        int byLevel = _session.PassivePointsByLevel();
        _settingSlider = true;
        _slider.Maximum = Math.Max(Math.Max(state.StagePoints, state.Points), passive ? build.Stages.Max(s => s.Passives.Count) : 20);
        _slider.Value = Math.Min(state.Points, _slider.Maximum);
        _settingSlider = false;
        _sliderLabel.Text = passive ? "Passive points" : tree.Kind == TreeDef.WeaverKind ? "Weaver points" : "Skill level";
        _sliderValue.Text = state.Points.ToString();
        _sliderRow.ToolTip = null;
        _slider.ToolTip = (passive
            ? $"How many passive points the tree is drawn with. Level {_session.Profile.Level} with your quest rewards gives {byLevel}."
            : "This skill's level = the points it has to spend.")
            + "\nMove it to see the build's plan at that many points.\nClick a node to add a point, right-click to remove one."
            + $"\nBuild stage: {state.Stage} ({state.StagePoints} points)";
    }

    /// <summary>
    /// The line at the top: what the picture shows. The player's own points read from the game, the
    /// guide's plan (because the points could not be read, or have not been yet), or a look ahead.
    /// </summary>
    private void ShowStatus(TreeDef tree, TreeState state, bool passive)
    {
        _status.Inlines.Clear();
        bool previewing = _session.IsPreviewing(tree);
        bool weaver = tree.Kind == TreeDef.WeaverKind;
        _back.Visibility = previewing ? Visibility.Visible : Visibility.Collapsed;
        Chip(_back, true, Green, Frozen("#227BE06A"));
        string what = passive ? "passive points" : weaver ? "Weaver points" : "points";
        string tips = "";
        if (previewing)
        {
            _status.Inlines.Add(new Run("◐ Looking ahead: ") { Foreground = Gold, FontWeight = FontWeights.SemiBold });
            _status.Inlines.Add(new Run($"the guide's plan at {state.Points} {what}."));
        }
        else if (state.FromGame)
        {
            _status.Inlines.Add(new Run("● Your character: ") { Foreground = Green, FontWeight = FontWeights.SemiBold });
            _status.Inlines.Add(new Run($"{_session.ActualPoints(tree)} {what}, read from the game"
                                        + (_session.ActualUpdated is { } when ? $" {Age(when)}" : "") + "."));
            if (state.OffPlan.Count > 0)
                _status.Inlines.Add(new Run($"  {state.OffPlan.Count} outside the build, ringed red.") { Foreground = Red });
        }
        else if (ReadTrouble(tree) && _session.Settings.ReadPointsFromScreen)
        {
            _status.Inlines.Add(new Run("○ The guide's plan ") { Foreground = Amber, FontWeight = FontWeights.SemiBold });
            _status.Inlines.Add(new Run($"{(passive || weaver ? $"at {state.Points} {what}" : $"at skill level {state.Points}")}: your own points on this tree could not be read. "));
            _status.Inlines.Add(new Run("Check that the game is in Borderless Windowed, in English, and that no overlay window covers the game's tree.") { Foreground = Muted, FontSize = 12 });
            tips = "\nOnce the points can be read, the tree shows your character instead.";
        }
        else
        {
            _status.Inlines.Add(new Run("○ The guide's plan ") { Foreground = Muted, FontWeight = FontWeights.SemiBold });
            _status.Inlines.Add(new Run(passive ? $"for level {_session.Profile.Level}: {state.Points} {what}."
                : weaver ? $"at {state.Points} {what}." : $"at skill level {state.Points}."));
            if (_session.Settings.ReadPointsFromScreen)
                _status.Inlines.Add(new Run("  Open this tree in the game to see your own points here.") { Foreground = Muted, FontSize = 12 });
        }
        // The passive total from the level and the quests, for checking against the game's panel.
        string check = "";
        if (passive)
        {
            int level = _session.Profile.Level, quest = _session.Rewards().Passive, fromLevel = Math.Max(0, level - 2);
            check = $"\nLevel {level} gives {fromLevel} points + {quest} from quests = {fromLevel + quest} passive points in total (spent + unspent in the game).";
        }
        _status.ToolTip = (state.FromGame && !previewing
            ? "Drawn from the points your character has in the game. Click a node to correct it (right-click removes a point)."
            : $"Drawn from the guide: {state.Stage} ({state.StagePoints} points by its end).") + tips + check;
    }

    private void ShowStageChip(BuildTree build, TreeDef tree)
    {
        // On the Weaver tab the box chooses the way of filling the Weaver tree instead.
        if (tree.Kind == TreeDef.WeaverKind && _session.WeaverChoices.Count > 0)
        {
            _stage.Visibility = Visibility.Visible;
            ((TextBlock)_stage.Child).Text = _session.WeaverChoice + (_session.WeaverChoices.Count > 1 ? "  ▾" : "");
            Chip(_stage, true, Gold, Frozen("#33C9A85C"));
            _stage.ToolTip = "The Weaver tree is the same for every build; this is which of Maxroll's ways of filling it is shown. Click to choose another.";
            return;
        }
        bool versions = build.Variants.Count > 1, stages = !versions && build.Stages.Count > 1;
        _stage.Visibility = versions || stages ? Visibility.Visible : Visibility.Collapsed;
        if (!versions && !stages) return;
        bool pinned = _session.PinnedStage is not null;
        string name = versions ? build.Variants[Math.Clamp(build.Variant, 0, build.Variants.Count - 1)] : _session.Stage?.Name ?? "";
        ((TextBlock)_stage.Child).Text = (versions ? "" : pinned ? "📌 " : "Auto · ") + name + "  ▾";
        Chip(_stage, true, Gold, Frozen("#33C9A85C"));
        _stage.ToolTip = versions
            ? "This guide has several versions of the build. Click to use another one."
            : pinned ? "You chose this stage of the guide by hand. Click to choose another, or to follow your level again."
            : "The stage of the guide for your level - the overlay moves on by itself as you level. Click to choose one by hand.";
    }

    /// <summary>The list of the guide's stages (or versions) to choose from, opened from the chip at the bottom.</summary>
    private void ChooseStage()
    {
        if (_session.Tree is not { } build) return;
        var menu = new ContextMenu { PlacementTarget = _stage, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        if (Current()?.Kind == TreeDef.WeaverKind && _session.WeaverChoices.Count > 0)
        {
            foreach (string choice in _session.WeaverChoices)
            {
                string chosen = choice;
                var item = new MenuItem { Header = choice, IsChecked = choice == _session.WeaverChoice };
                item.Click += (_, _) => _session.SetWeaverStrategy(chosen);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
            return;
        }
        if (build.Variants.Count > 1)
        {
            for (int i = 0; i < build.Variants.Count; i++)
            {
                int index = i;
                var item = new MenuItem { Header = build.Variants[i], IsChecked = i == build.Variant };
                item.Click += (_, _) =>
                {
                    if (index != build.Variant && !_session.SwitchVariant(index))
                        _session.ShowAlert("That version was not saved with this build. Import the guide again (Settings) to get all of them.");
                };
                menu.Items.Add(item);
            }
        }
        else
        {
            var auto = new MenuItem { Header = $"Follow my level (level {_session.Profile.Level})", IsChecked = _session.PinnedStage is null };
            auto.Click += (_, _) => _session.SetStage(null);
            menu.Items.Add(auto);
            menu.Items.Add(new Separator());
            foreach (var stage in build.Stages)
            {
                var chosen = stage;
                var item = new MenuItem { Header = stage.Name, IsChecked = _session.PinnedStage == stage };
                item.Click += (_, _) => _session.SetStage(chosen);
                menu.Items.Add(item);
            }
        }
        menu.IsOpen = true;
    }

    private static void Chip(Border chip, bool on, Brush colour, Brush tint)
    {
        ((TextBlock)chip.Child).Foreground = on ? colour : Muted;
        ((TextBlock)chip.Child).FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        chip.Background = on ? tint : Brushes.Transparent;
        chip.BorderBrush = on ? colour : Dim;
    }

    /// <summary>
    /// Under the passive tree: how many passive points the character has in total, from two things
    /// the game itself reports - the level (its log) and the quest rewards (its map). Spent plus
    /// unspent points in the game's passive panel should add up to this.
    /// </summary>


    private static string Age(DateTime when)
    {
        var age = DateTime.Now - when;
        return age.TotalMinutes < 2 ? "just now" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min ago" : age.TotalDays < 1 ? $"{(int)age.TotalHours} h ago" : $"{(int)age.TotalDays} d ago";
    }

    private void DrawTree(BuildTree build, TreeDef tree, TreeState state)
    {
        if (tree.Nodes.Count == 0) return;
        double minX = tree.Nodes.Min(n => n.X), maxX = tree.Nodes.Max(n => n.X);
        double minY = tree.Nodes.Min(n => n.Y), maxY = tree.Nodes.Max(n => n.Y);
        bool dense = tree.Nodes.Count > DenseNodes;
        double canvasHeight = _canvas.Height, big = dense ? DenseNodeSize : NodeSize;
        double scale = Math.Min((CanvasWidth - 2 * Inset) / Math.Max(1, maxX - minX), (canvasHeight - 2 * Inset) / Math.Max(1, maxY - minY));
        double offsetX = (CanvasWidth - (maxX - minX) * scale) / 2, offsetY = (canvasHeight - (maxY - minY) * scale) / 2 - 10;
        // In a dense tree the next steps are drawn last, so their names and markers lie on top of their neighbours.
        var onTop = new List<(UIElement Element, double Left, double Top)>();
        Point At(TreeNode n) => new(offsetX + (n.X - minX) * scale, offsetY + (n.Y - minY) * scale);
        var byId = tree.Nodes.ToDictionary(n => n.Id);

        foreach (var node in tree.Nodes)
            foreach (int required in node.Requires)
            {
                if (!byId.TryGetValue(required, out var parent)) continue;
                Point a = At(parent), b = At(node);
                bool live = state.Allocated.ContainsKey(node.Id) && state.Allocated.ContainsKey(required);
                _canvas.Children.Add(new Line
                {
                    X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                    Stroke = live ? Gold : Line, StrokeThickness = live ? 2 : 1,
                });
            }

        foreach (var node in tree.Nodes)
        {
            Point p = At(node);
            int have = state.Allocated.GetValueOrDefault(node.Id);
            int target = state.Target.GetValueOrDefault(node.Id);
            int run = state.Next.ToList().FindIndex(r => r.Node == node.Id);
            // The node is ringed green if either marker is on; which labels it gets depends on each.
            bool showOrder = MarkersOn(tree), showAmount = AmountsOn(tree);
            int order = showOrder || showAmount ? run : -1;
            bool offPlan = state.OffPlan.Contains(node.Id);
            // A node the character has points in is drawn full size even if the build never takes it.
            bool planned = target > 0 || have > 0;
            double size = planned ? big : dense ? DenseSmallNodeSize : SmallNodeSize;

            var icon = IconBrush(node, build);
            bool lit = have > 0 || order >= 0;
            var circle = new Ellipse
            {
                Width = size, Height = size,
                Fill = icon ?? (order >= 0 ? GreenFill : have > 0 ? GoldFill : planned ? PlannedFill : Dim),
                Stroke = offPlan ? Red : order >= 0 ? Green : have > 0 ? Gold : planned ? Planned : icon is null ? null : Dim,
                StrokeThickness = order >= 0 ? 3 : planned ? 2 : 1,
                // Nodes the build never takes stay in the picture for orientation, but faded.
                Opacity = lit ? 1 : planned ? 0.75 : 0.3,
                ToolTip = $"{node.Name}  ({have}/{node.Max}{(planned ? $", build takes {target}" : ", not in this build")})"
                          + (node.Description.Length > 0 ? "\n" + node.Description : ""),
            };
            if (planned && !lit) circle.StrokeDashArray = new DoubleCollection { 2, 2 };
            // Correct a node to what the game really shows: click adds a point, right-click removes one.
            var clicked = node;
            circle.Cursor = Cursors.Hand;
            circle.MouseLeftButtonDown += (_, e) => { _session.AdjustNode(tree, clicked, +1); e.Handled = true; };
            circle.MouseRightButtonDown += (_, e) => { _session.AdjustNode(tree, clicked, -1); e.Handled = true; };
            Place(circle, p.X - size / 2, p.Y - size / 2);
            if (!planned) continue;

            // Points as a small plate under the icon, then the name.
            var plate = new Border
            {
                Background = Frozen("#E60E0F14"), CornerRadius = new CornerRadius(3), Width = 30, Height = 15, IsHitTestVisible = false,
                Child = Label($"{have}/{target}", lit ? Brushes.White : Muted, 10.5, FontWeights.SemiBold, 30),
            };
            Place(plate, p.X - 15, p.Y + big / 2 - 9);
            // A dense tree only names the nodes that are next (every node's name is in its tooltip).
            if (!dense) Place(Label(node.Name, lit ? Text : Muted, 10, FontWeights.Normal, 92), p.X - 46, p.Y + big / 2 + 6);
            else if (order >= 0)
                onTop.Add((new Border
                {
                    Background = Frozen("#E60E0F14"), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 1), IsHitTestVisible = false,
                    Child = new TextBlock { Text = node.Name, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold },
                }, p.X - 46, p.Y + big / 2 + 7));

            if (order < 0) continue;
            // One tag at the top: which step this is and how many points it puts here ("1 · +2").
            var tag = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            if (showOrder) tag.Inlines.Add(new Run((order + 1).ToString()) { Foreground = OrderBlue });
            if (showOrder && showAmount) tag.Inlines.Add(new Run(" · ") { Foreground = Muted });
            if (showAmount) tag.Inlines.Add(new Run($"+{state.Next[order].Count}") { Foreground = AmountOrange });
            var badge = new Border
            {
                Background = Plate, CornerRadius = new CornerRadius(9), Padding = new Thickness(6, 0, 6, 1), Height = 19,
                BorderBrush = Green, BorderThickness = new Thickness(1.5), IsHitTestVisible = false, Child = tag,
            };
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double left = p.X - badge.DesiredSize.Width / 2, top = p.Y - big / 2 - 21;
            if (dense) onTop.Add((badge, left, top));
            else Place(badge, left, top);
        }
        foreach (var (element, left, top) in onTop) Place(element, left, top);
    }

    private static TextBlock Label(string text, Brush brush, double size, FontWeight weight, double width) => new()
    {
        Text = text, Foreground = brush, FontSize = size, FontWeight = weight, Width = width,
        TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false,
    };

    private void Place(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        _canvas.Children.Add(element);
    }
}
