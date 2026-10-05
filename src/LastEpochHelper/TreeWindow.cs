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
    private static readonly Brush OrderTint = Frozen("#335BB8FF");
    private static readonly Brush AmountOrange = Frozen("#FF9A3D");
    private static readonly Brush AmountTint = Frozen("#33FF9A3D");
    private static readonly Brush Amber = Frozen("#FFC857");
    private static readonly Brush Plate = Frozen("#F00E0F14");

    private readonly Session _session;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly Canvas _canvas = new() { Width = CanvasWidth, Height = CanvasHeight, ClipToBounds = true };
    private readonly TextBlock _next = new() { TextWrapping = TextWrapping.Wrap, Foreground = Text, Margin = new Thickness(2, 10, 2, 0), LineHeight = 20 };
    private readonly TextBlock _check = new() { TextWrapping = TextWrapping.Wrap, Foreground = Muted, Margin = new Thickness(2, 8, 2, 0), FontSize = 12, MaxWidth = CanvasWidth, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _points = new() { Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
    private Border _markers = null!;
    private Border _amounts = null!;
    private Border _reset = null!;
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
        _markers = HeaderButton("① Order", ToggleMarkers);
        _markers.ToolTip = "Show or hide the numbers that mark the order of the next points (remembered separately for passives and skills)";
        _amounts = HeaderButton("+N Points", ToggleAmounts);
        _amounts.ToolTip = "Show or hide how many points the next step puts into a node (remembered separately for passives and skills)";
        // Only there when the game's own points are known for this tree: switches between them and the plan.
        _reset = HeaderButton("game", () => { if (Current() is { } tree) _session.SetPlanView(tree, _session.ShowsActual(tree)); });
        // Which part of the guide the tree follows: a stage by level, or a version of the build.
        _stage = HeaderButton("stage", ChooseStage);
        var adjust = new StackPanel { Orientation = Orientation.Horizontal };
        adjust.Children.Add(_stage);
        adjust.Children.Add(_reset);
        adjust.Children.Add(_markers);
        adjust.Children.Add(_amounts);
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
        foreach (var chip in new[] { _stage, _reset, _markers, _amounts, _mini })
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
        _canvasFrame = new Border { Child = _canvas, Margin = new Thickness(0, 4, 0, 0), Background = Frozen("#12FFFFFF"), CornerRadius = new CornerRadius(7) };
        body.Children.Add(_canvasFrame);
        body.Children.Add(_next);
        body.Children.Add(_check);
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
        double fit = Math.Min((area.Width - 16) / (CanvasWidth + 50), (area.Height - 16) / (_canvas.Height + 170));
        return Math.Clamp(Math.Min(_session.Settings.TreeScale, fit), 0.4, 1);
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static Border HeaderButton(string text, Action onClick, bool selected = false)
    {
        var label = new TextBlock { Text = text, Foreground = selected ? Brushes.White : Muted, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal };
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

    private void ToggleAmounts()
    {
        if (Current() is not { } tree) return;
        if (tree.Kind == TreeDef.PassiveKind) _session.Settings.ShowAmountPassives = !_session.Settings.ShowAmountPassives;
        else _session.Settings.ShowAmountSkills = !_session.Settings.ShowAmountSkills;
        _session.SaveSettings();
        Render();
    }

    private void ToggleMarkers()
    {
        if (Current() is not { } tree) return;
        if (tree.Kind == TreeDef.PassiveKind) _session.Settings.ShowOrderPassives = !_session.Settings.ShowOrderPassives;
        else _session.Settings.ShowOrderSkills = !_session.Settings.ShowOrderSkills;
        _session.SaveSettings();
        Render();
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
            _check.Visibility = Visibility.Collapsed;
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
            _tabs.Children.Add(HeaderButton(tab.Name, () => Select(captured), tab == tree));
        }

        _sliderRow.Visibility = Visibility.Visible;
        DrawTree(build, tree, state);

        bool passive = tree.Kind == TreeDef.PassiveKind;
        ShowStageChip(build, tree);
        Chip(_markers, MarkersOn(tree), OrderBlue, OrderTint);
        Chip(_amounts, AmountsOn(tree), AmountOrange, AmountTint);
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
        ShowCheck(passive);

        // Mini: just the tabs, the "next points" line and the switches.
        bool mini = _session.Settings.TreeMini;
        var whole = mini ? Visibility.Collapsed : Visibility.Visible;
        _canvasFrame.Visibility = whole;
        if (mini) _check.Visibility = Visibility.Collapsed;
        foreach (var part in new UIElement[] { _sliderLabel, _minus, _slider, _plus, _sliderValue }) part.Visibility = whole;
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

        // "game" = drawn from the points read off the game's panel; click to flip to the plan view and back.
        bool known = _session.HasActual(tree);
        _reset.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
        ((TextBlock)_reset.Child).Text = state.FromGame
            ? "● game" + (_session.ActualUpdated is { } when ? $" · {Age(when)}" : "")
            : "○ plan";
        Chip(_reset, state.FromGame, Green, Frozen("#227BE06A"));
        _reset.ToolTip = state.FromGame
            ? "Showing the points your character has in the game" + (state.OffPlan.Count > 0 ? $" ({state.OffPlan.Count} node(s) outside the build, ringed red)" : "") + ".\nClick to see the build's plan instead."
            : $"Showing the build's plan at {state.Points} points. Click to go back to the points read from the game ({_session.ActualPoints(tree)}).";
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
    private void ShowCheck(bool passive)
    {
        _check.Visibility = passive ? Visibility.Visible : Visibility.Collapsed;
        if (!passive) return;
        int level = _session.Profile.Level, quest = _session.Rewards().Passive;
        int fromLevel = Math.Max(0, level - 2);
        _check.Text = $"Level {level} gives {fromLevel} points + {quest} from quests = {fromLevel + quest} passive points in total (spent + unspent in the game).";
    }

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
            if (showOrder)
            {
            // The order: a blue disc at the top left.
            var badge = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = OrderBlue,
                BorderBrush = Plate, BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = (order + 1).ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
                IsHitTestVisible = false,
            };
            if (dense) onTop.Add((badge, p.X - big / 2 - 7, p.Y - big / 2 - 7));
            else Place(badge, p.X - big / 2 - 7, p.Y - big / 2 - 7);
            }
            if (!showAmount) continue;

            // How many points this step puts here: an orange tag at the top right.
            var add = new Border
            {
                Background = AmountOrange, CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 1), Height = 18, IsHitTestVisible = false,
                BorderBrush = Plate, BorderThickness = new Thickness(2),
                Child = new TextBlock { Text = $"+{state.Next[order].Count}", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center },
            };
            if (dense) onTop.Add((add, p.X + big / 2 - 10, p.Y - big / 2 - 7));
            else Place(add, p.X + big / 2 - 10, p.Y - big / 2 - 7);
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
