using System.Windows;
using System.Windows.Controls;
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
    private const double CanvasWidth = 860, CanvasHeight = 420, Inset = 50, NodeSize = 40, SmallNodeSize = 24;

    private static readonly Brush Gold = Frozen("#C9A85C");
    private static readonly Brush GoldFill = Frozen("#4A3A14");
    private static readonly Brush Green = Frozen("#7BE06A");
    private static readonly Brush GreenFill = Frozen("#1F4A1A");
    private static readonly Brush Planned = Frozen("#7A6A3E");
    private static readonly Brush PlannedFill = Frozen("#1B1A16");
    private static readonly Brush Dim = Frozen("#33FFFFFF");
    private static readonly Brush Line = Frozen("#3AFFFFFF");
    private static readonly Brush Text = Frozen("#E8E4D8");
    private static readonly Brush Muted = Frozen("#A9A493");

    private readonly Session _session;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly Canvas _canvas = new() { Width = CanvasWidth, Height = CanvasHeight, ClipToBounds = true };
    private readonly TextBlock _next = new() { TextWrapping = TextWrapping.Wrap, Foreground = Text, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _points = new() { Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
    private Border _markers = null!;
    private readonly TextBlock _empty = new()
    {
        Foreground = Muted, TextWrapping = TextWrapping.Wrap, Width = 420, TextAlignment = TextAlignment.Center,
        Text = "No build imported for this character.\n\nOpen settings (the gear on the overlay), paste a Maxroll planner or build guide link and press 'Import from Maxroll'.",
    };

    private BitmapSource? _atlas;
    private bool _atlasTried;
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

        var minus = HeaderButton("−", () => Adjust(-1));
        var plus = HeaderButton("+", () => Adjust(+1));
        minus.ToolTip = "I have one point fewer than shown";
        plus.ToolTip = "I have one point more than shown";
        var footer = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        _markers = HeaderButton("① order", ToggleMarkers);
        _markers.ToolTip = "Show or hide the green rings that mark the next points (remembered separately for passives and skills)";
        var adjust = new StackPanel { Orientation = Orientation.Horizontal };
        adjust.Children.Add(_markers);
        adjust.Children.Add(minus);
        adjust.Children.Add(plus);
        DockPanel.SetDock(adjust, Dock.Right);
        footer.Children.Add(adjust);
        footer.Children.Add(_points);

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(new Border { Child = _canvas, Margin = new Thickness(0, 4, 0, 0), Background = Frozen("#14FFFFFF"), CornerRadius = new CornerRadius(4) });
        body.Children.Add(_next);
        body.Children.Add(footer);

        Content = new Border
        {
            Background = Frozen("#F00E0F14"),
            BorderBrush = Frozen("#5A4B2A"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 8),
            Child = body,
        };
        SourceInitialized += (_, _) => Native.ApplyOverlayStyle(new WindowInteropHelper(this).Handle, clickThrough: false);
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
            CornerRadius = new CornerRadius(3),
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
        if (!_atlasTried)
        {
            _atlasTried = true;
            try
            {
                string path = System.IO.Path.Combine(_session.DataDir, BuildTree.AtlasFile);
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
            catch (Exception e) when (e is NotSupportedException or IOException or FileFormatException or System.Runtime.InteropServices.COMException)
            {
                _atlas = null;
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
        _icons.Clear();
    }

    private TreeDef? Current()
    {
        var trees = _session.Tree?.Trees;
        if (trees is null || trees.Count == 0) return null;
        return trees.FirstOrDefault(t => t.Name == _session.Profile.TreeTab) ?? trees[0];
    }

    /// <summary>Shows the first passive tab, or the last-used (else first) skill tab.</summary>
    public void SelectKind(string kind)
    {
        var trees = _session.Tree?.Trees;
        if (trees is null) return;
        if (Current() is { } current && current.Kind == kind) return;
        string? remembered = kind == TreeDef.SkillKind ? _session.Profile.LastSkillTab : null;
        var target = trees.FirstOrDefault(t => t.Kind == kind && t.Name == remembered) ?? trees.FirstOrDefault(t => t.Kind == kind);
        if (target is not null) Select(target);
    }

    public string? CurrentKind => Current()?.Kind;

    /// <summary>Names of the skill tabs, for matching against text on screen.</summary>
    public IEnumerable<string> SkillNames =>
        _session.Tree?.Trees.Where(t => t.Kind == TreeDef.SkillKind).Select(t => t.Name) ?? Enumerable.Empty<string>();

    /// <summary>Switches to the named skill tab if it is not already showing.</summary>
    public void SelectSkill(string name)
    {
        var tree = _session.Tree?.Trees.FirstOrDefault(t => t.Kind == TreeDef.SkillKind && t.Name == name);
        if (tree is not null && tree != Current()) Select(tree);
    }

    private bool MarkersOn(TreeDef tree) =>
        tree.Kind == TreeDef.PassiveKind ? _session.Settings.ShowOrderPassives : _session.Settings.ShowOrderSkills;

    private void ToggleMarkers()
    {
        if (Current() is not { } tree) return;
        if (tree.Kind == TreeDef.PassiveKind) _session.Settings.ShowOrderPassives = !_session.Settings.ShowOrderPassives;
        else _session.Settings.ShowOrderSkills = !_session.Settings.ShowOrderSkills;
        _session.SaveSettings();
        Render();
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
        if (Current() is { } tree) _session.AdjustTreePoints(tree, delta);
    }

    public void Render()
    {
        _tabs.Children.Clear();
        _canvas.Children.Clear();
        var build = _session.Tree;
        var tree = Current();
        if (build is null || tree is null)
        {
            _tabs.Children.Add(new TextBlock { Text = "Build tree", Foreground = Gold, FontWeight = FontWeights.SemiBold });
            Canvas.SetLeft(_empty, (CanvasWidth - _empty.Width) / 2);
            Canvas.SetTop(_empty, CanvasHeight / 2 - 40);
            _canvas.Children.Add(_empty);
            _next.Text = "";
            _points.Text = "";
            return;
        }

        string? previousKind = null;
        foreach (var tab in build.Trees)
        {
            // A divider between the passive tabs and the skill tabs.
            if (previousKind is not null && previousKind != tab.Kind)
                _tabs.Children.Add(new TextBlock { Text = "│", Foreground = Dim, Margin = new Thickness(2, 1, 5, 0) });
            previousKind = tab.Kind;
            var captured = tab;
            _tabs.Children.Add(HeaderButton(tab.Name, () => Select(captured), tab == tree));
        }

        var state = _session.TreeState(tree);
        DrawTree(build, tree, state);

        bool passive = tree.Kind == TreeDef.PassiveKind;
        ((TextBlock)_markers.Child).Foreground = MarkersOn(tree) ? Green : Muted;
        var running = new Dictionary<int, int>(state.Allocated);
        var parts = new List<string>();
        for (int i = 0; i < state.Next.Count; i++)
        {
            var (id, count) = state.Next[i];
            running[id] = running.GetValueOrDefault(id) + count;
            // What to click and how many times; the bracket is where the node stands afterwards.
            bool here = tree.Nodes.Any(n => n.Id == id);
            string after = here ? $" (→ {running[id]}/{tree.Nodes.First(n => n.Id == id).Max})" : "";
            parts.Add($"{i + 1}. {build.NodeName(id, passive, tree.Name)} +{count}{after}");
        }
        _next.Text = parts.Count > 0 ? "Next:  " + string.Join("     ", parts)
            : state.StagePoints == 0 ? "This build puts no points here yet." : "Everything in this tree is taken - nothing more planned.";

        string source = passive
            ? $"{state.Points} of {state.StagePoints} passive points  (level {_session.Profile.Level}, incl. quest rewards)"
            : $"{state.Points} of {state.StagePoints} points in {tree.Name}  -  use + each time you spend a skill point";
        _points.Text = $"{state.Stage}  ·  {source}";
    }

    private void DrawTree(BuildTree build, TreeDef tree, TreeState state)
    {
        if (tree.Nodes.Count == 0) return;
        double minX = tree.Nodes.Min(n => n.X), maxX = tree.Nodes.Max(n => n.X);
        double minY = tree.Nodes.Min(n => n.Y), maxY = tree.Nodes.Max(n => n.Y);
        double scale = Math.Min((CanvasWidth - 2 * Inset) / Math.Max(1, maxX - minX), (CanvasHeight - 2 * Inset) / Math.Max(1, maxY - minY));
        double offsetX = (CanvasWidth - (maxX - minX) * scale) / 2, offsetY = (CanvasHeight - (maxY - minY) * scale) / 2 - 6;
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
            int order = MarkersOn(tree) ? run : -1;
            bool planned = target > 0;
            double size = planned ? NodeSize : SmallNodeSize;

            var icon = IconBrush(node, build);
            bool lit = have > 0 || order >= 0;
            var circle = new Ellipse
            {
                Width = size, Height = size,
                Fill = icon ?? (order >= 0 ? GreenFill : have > 0 ? GoldFill : planned ? PlannedFill : Dim),
                Stroke = order >= 0 ? Green : have > 0 ? Gold : planned ? Planned : icon is null ? null : Dim,
                StrokeThickness = order >= 0 ? 3 : planned ? 2 : 1,
                // Nodes the build never takes stay in the picture for orientation, but faded.
                Opacity = lit ? 1 : planned ? 0.75 : 0.3,
                ToolTip = $"{node.Name}  ({have}/{node.Max}{(planned ? $", build takes {target}" : ", not in this build")})"
                          + (node.Description.Length > 0 ? "\n" + node.Description : ""),
            };
            if (planned && !lit) circle.StrokeDashArray = new DoubleCollection { 2, 2 };
            Place(circle, p.X - size / 2, p.Y - size / 2);
            if (!planned) continue;

            // Points as a small plate under the icon, then the name.
            var plate = new Border
            {
                Background = Frozen("#E60E0F14"), CornerRadius = new CornerRadius(3), Width = 30, Height = 15, IsHitTestVisible = false,
                Child = Label($"{have}/{target}", lit ? Brushes.White : Muted, 10.5, FontWeights.SemiBold, 30),
            };
            Place(plate, p.X - 15, p.Y + NodeSize / 2 - 9);
            Place(Label(node.Name, lit ? Text : Muted, 10, FontWeights.Normal, 92), p.X - 46, p.Y + NodeSize / 2 + 6);

            if (order < 0) continue;
            var badge = new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Green,
                Child = new TextBlock
                {
                    Text = (order + 1).ToString(), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
                IsHitTestVisible = false,
            };
            Place(badge, p.X + NodeSize / 2 - 9, p.Y - NodeSize / 2 - 6);

            // How many points this step puts here.
            var add = new Border
            {
                Background = Green, CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 3, 0), Height = 15, IsHitTestVisible = false,
                Child = new TextBlock { Text = $"+{state.Next[order].Count}", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Brushes.Black },
            };
            Place(add, p.X + 17, p.Y + NodeSize / 2 - 9);
        }
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
