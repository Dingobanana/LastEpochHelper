using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LastEpochHelper.Core;

namespace LastEpochHelper;

/// <summary>
/// The reference pages that do not belong to a single zone: what gear and idols the build wants,
/// which loot filter to use when, the Monolith progress and the dungeons. Like the overlay it never
/// takes focus, so everything is done with clicks.
/// </summary>
internal sealed class PlannerWindow : Window
{
    private static readonly string[] Tabs = { "Gear", "Idols", "Loot filter", "Monolith", "Dungeons" };

    private static readonly Brush Gold = Frozen("#C9A85C");
    private static readonly Brush Green = Frozen("#7BE06A");
    private static readonly Brush Text = Frozen("#E8E4D8");
    private static readonly Brush Muted = Frozen("#A9A493");
    private static readonly Brush Dim = Frozen("#77736A");
    private static readonly Brush Unique = Frozen("#F0A35C");
    private static readonly Brush Set = Frozen("#6FD98B");
    private static readonly Brush Passive = Frozen("#FFD35C");
    private static readonly Brush Idol = Frozen("#C79BFF");

    private readonly Session _session;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _body = new() { Width = 560 };

    public event Action? Moved;
    public event Action? CloseRequested;

    public PlannerWindow(Session session)
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
        Foreground = Text;

        var close = Button("✕", () => CloseRequested?.Invoke());
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

        var layout = new StackPanel();
        layout.Children.Add(header);
        layout.Children.Add(new ScrollViewer
        {
            Content = _body, MaxHeight = 600, Margin = new Thickness(0, 6, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false,
        });
        Content = new Border
        {
            Background = Frozen("#F00E0F14"), BorderBrush = Frozen("#5A4B2A"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 10), Child = layout,
        };
        SourceInitialized += (_, _) => Native.ApplyOverlayStyle(new WindowInteropHelper(this).Handle, clickThrough: false);
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    // ------------------------------------------------------------------ building blocks

    private static Border Button(string text, Action onClick, bool selected = false, Brush? color = null)
    {
        var button = new Border
        {
            Child = new TextBlock { Text = text, Foreground = color ?? (selected ? Brushes.White : Muted), FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal },
            Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 3, 0), CornerRadius = new CornerRadius(3),
            Background = selected ? Frozen("#33C9A85C") : Frozen("#14FFFFFF"), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
        };
        button.MouseLeftButtonDown += (_, e) => { onClick(); e.Handled = true; };
        return button;
    }

    private static TextBlock Line(string text, Brush? brush = null, double size = 13, FontWeight? weight = null, double top = 0) => new()
    {
        Text = text, Foreground = brush ?? Text, FontSize = size, FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0),
    };

    private void Heading(string text) => _body.Children.Add(Line(text, Gold, 14, FontWeights.SemiBold, _body.Children.Count == 0 ? 0 : 10));

    private void Note(string text) => _body.Children.Add(Line(text, Muted, 12, top: 2));

    /// <summary>"label  [−] value [+]" for numbers that have to be kept by hand.</summary>
    private static StackPanel Stepper(string label, int value, Action<int> change, int step = 1)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        panel.Children.Add(Button("−", () => change(-step)));
        panel.Children.Add(new TextBlock { Text = value.ToString(), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Width = 34, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(Button("+", () => change(step)));
        return panel;
    }

    private static Border Check(string label, bool on, Action toggle) =>
        Button((on ? "✔ " : "☐ ") + label, toggle, color: on ? Green : Muted);

    // ------------------------------------------------------------------ rendering

    public void Render()
    {
        string current = Tabs.Contains(_session.Profile.PlannerTab) ? _session.Profile.PlannerTab : Tabs[0];
        _tabs.Children.Clear();
        foreach (string tab in Tabs)
        {
            string captured = tab;
            _tabs.Children.Add(Button(tab, () => { _session.Profile.PlannerTab = captured; _session.Save(); Render(); }, tab == current));
        }

        _body.Children.Clear();
        switch (current)
        {
            case "Gear": RenderGear(); break;
            case "Idols": RenderIdols(); break;
            case "Loot filter": RenderFilters(); break;
            case "Monolith": RenderMonolith(); break;
            case "Dungeons": RenderDungeons(); break;
        }
    }

    private TreeStage? Stage()
    {
        if (_session.Tree?.StageFor(_session.Profile.Level) is { } stage) return stage;
        Note("No build imported for this character. Open settings (the gear on the overlay), paste a Maxroll planner or build guide link and press 'Import from Maxroll'.");
        return null;
    }

    private void RenderGear()
    {
        if (Stage() is not { } stage) return;
        Heading($"{stage.Name}  ·  gear the build wears by level {stage.Level}");
        if (stage.Gear.Count == 0) { Note("This stage of the build lists no gear."); return; }

        var wanted = stage.WantedAffixes();
        if (wanted.Count > 0)
        {
            var look = Line("", top: 4);
            look.Inlines.Add(new Run("Look for:  ") { Foreground = Muted });
            look.Inlines.Add(new Run(string.Join(",  ", wanted.Select(w => w.Count > 1 ? $"{w.Affix} ×{w.Count}" : w.Affix))) { Foreground = Green });
            _body.Children.Add(look);
        }
        Note("Tiers are what the build ends the stage with - any tier of the right affix is an upgrade on the way. Click a slot when you have it covered.");

        foreach (var item in stage.Gear)
        {
            string key = $"{stage.Name}|{item.Slot}";
            bool done = _session.Profile.GearDone.Contains(key);
            var row = Line("", top: 6);
            row.Cursor = Cursors.Hand;
            row.Inlines.Add(new Run(done ? "✔ " : "☐ ") { Foreground = done ? Green : Muted });
            row.Inlines.Add(new Run($"{item.Slot}:  ") { Foreground = Muted });
            row.Inlines.Add(new Run(item.Name) { Foreground = done ? Dim : item.Rarity == "unique" ? Unique : item.Rarity == "set" ? Set : Brushes.White, FontWeight = FontWeights.SemiBold });
            if (item.Rarity.Length > 0) row.Inlines.Add(new Run($"  ({item.Rarity})") { Foreground = Dim });
            if (item.Affixes.Count > 0)
                row.Inlines.Add(new Run("\n      " + string.Join("  ·  ", item.Affixes)) { Foreground = done ? Dim : Muted, FontSize = 12 });
            row.MouseLeftButtonDown += (_, e) => { _session.ToggleGearDone(key); e.Handled = true; };
            _body.Children.Add(row);
        }
    }

    private void RenderIdols()
    {
        if (Stage() is not { } stage) return;
        Heading($"{stage.Name}  ·  idols");
        if (stage.Idols.Count == 0) Note("This stage of the build lists no idols (leveling builds often leave them out): use any idol with health, resistances or your skill's damage.");
        foreach (var idol in stage.Idols)
        {
            var row = Line("", top: 5);
            row.Inlines.Add(new Run($"{idol.Count}× ") { Foreground = Idol, FontWeight = FontWeights.SemiBold });
            row.Inlines.Add(new Run(idol.Name) { Foreground = idol.Rarity == "unique" ? Unique : Brushes.White, FontWeight = FontWeights.SemiBold });
            if (idol.Affixes.Count > 0) row.Inlines.Add(new Run("\n      " + string.Join("  ·  ", idol.Affixes)) { Foreground = Muted, FontSize = 12 });
            _body.Children.Add(row);
        }

        Heading("Blessings");
        if (stage.Blessings.Count == 0) Note("This stage of the build lists no blessings.");
        foreach (string blessing in stage.Blessings)
        {
            var timeline = _session.Endgame.TimelineOf(blessing);
            var row = Line("", top: 4);
            row.Inlines.Add(new Run(blessing) { Foreground = Brushes.White });
            if (timeline is not null) row.Inlines.Add(new Run($"   -  {timeline.Name} ({timeline.Boss})") { Foreground = Muted });
            _body.Children.Add(row);
        }
    }

    private void RenderFilters()
    {
        Heading("Loot filter by level");
        Note("Give each filter the level you want to start using it at. When you reach that level the overlay tells you to switch (in game: Shift+F opens the filter list). 0 = not used.");
        var files = _session.InstalledFilters();
        if (files.Count == 0) Note("No filters found in the game's Filters folder. Download one (for example from a Maxroll or Last Epoch Tools build guide) and put the .xml file there.");
        foreach (string name in files)
        {
            int level = _session.Profile.FilterStages.GetValueOrDefault(name);
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            var stepper = Stepper("from level", level, delta => _session.SetFilterLevel(name, Math.Clamp(level + delta, 0, 100)), step: 5);
            DockPanel.SetDock(stepper, Dock.Right);
            row.Children.Add(stepper);
            row.Children.Add(new TextBlock { Text = name, Foreground = level > 0 ? Brushes.White : Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            _body.Children.Add(row);
        }
        var open = Button("Open Filters folder", () => Process.Start(new ProcessStartInfo(_session.FiltersDir) { UseShellExecute = true }));
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, 10, 0, 0);
        _body.Children.Add(open);
    }

    private void RenderMonolith()
    {
        int knowledge = _session.Knowledge();
        var title = Line("", size: 14, top: 0);
        title.Inlines.Add(new Run("Monolith of Fate") { Foreground = Gold, FontWeight = FontWeights.SemiBold });
        title.Inlines.Add(new Run($"     Knowledge of Orobyss {Math.Min(knowledge, _session.Endgame.KnowledgeNeeded)}/{_session.Endgame.KnowledgeNeeded}")
            { Foreground = knowledge >= _session.Endgame.KnowledgeNeeded ? Green : Passive });
        _body.Children.Add(title);
        Note("Kept by hand - the game log does not report echoes, blessings or corruption. Level 90 timelines open at 5 Knowledge (Chapters 9 and 10 give 1 each).");

        foreach (var timeline in _session.Endgame.Timelines)
        {
            var progress = _session.TimelineProgress(timeline.Name);
            var name = Line("", top: 9);
            name.Inlines.Add(new Run(timeline.Name) { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
            name.Inlines.Add(new Run($"   lvl {timeline.Level}  ·  {timeline.Boss}" + (timeline.Knowledge > 0 ? $"  ·  +{timeline.Knowledge} Knowledge" : "")) { Foreground = Muted });
            name.ToolTip = $"Quest echoes (stability normal/empowered): {timeline.Echoes}\nHarbinger: {timeline.Harbinger}\nExclusive echo rewards: {timeline.Rewards}";
            _body.Children.Add(name);

            var row = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            row.Children.Add(Check("Normal", progress.Normal, () => _session.UpdateTimeline(timeline.Name, p => p.Normal = !p.Normal)));
            row.Children.Add(Check("Empowered", progress.Empowered, () => _session.UpdateTimeline(timeline.Name, p => p.Empowered = !p.Empowered)));
            var corruption = Stepper("  corruption", progress.Corruption, delta => _session.UpdateTimeline(timeline.Name, p => p.Corruption = Math.Max(0, p.Corruption + delta)), step: 10);
            row.Children.Add(corruption);
            _body.Children.Add(row);

            var blessing = Button(progress.Blessing.Length > 0 ? $"Blessing: {progress.Blessing}" : "Blessing: choose...",
                () => { }, color: progress.Blessing.Length > 0 ? Idol : Muted);
            blessing.HorizontalAlignment = HorizontalAlignment.Left;
            blessing.Margin = new Thickness(0, 3, 0, 0);
            blessing.MouseLeftButtonDown += (_, _) => BlessingMenu(blessing, timeline);
            _body.Children.Add(blessing);
        }
    }

    private void BlessingMenu(UIElement target, Timeline timeline)
    {
        var menu = new ContextMenu { PlacementTarget = target };
        var none = new MenuItem { Header = "(none)" };
        none.Click += (_, _) => _session.UpdateTimeline(timeline.Name, p => p.Blessing = "");
        menu.Items.Add(none);
        // Recommended ones first; the rest in the game's order.
        foreach (var blessing in timeline.Blessings.OrderByDescending(b => b.Recommended))
        {
            var item = new MenuItem
            {
                Header = $"{(blessing.Recommended ? "★ " : "")}{blessing.Name}   -   {blessing.Effect}",
                ToolTip = blessing.Grand.Length > 0 ? "Grand (empowered): " + blessing.Grand : null,
                IsChecked = blessing.Name == _session.TimelineProgress(timeline.Name).Blessing,
            };
            item.Click += (_, _) => _session.UpdateTimeline(timeline.Name, p => p.Blessing = blessing.Name);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void RenderDungeons()
    {
        foreach (var dungeon in _session.Endgame.Dungeons)
        {
            var progress = _session.DungeonProgress(dungeon.Name);
            Heading($"{dungeon.Name}  ·  level {dungeon.Level}+");

            var row = new WrapPanel { Margin = new Thickness(0, 3, 0, 2) };
            row.Children.Add(Stepper("keys", progress.Keys, delta => _session.UpdateDungeon(dungeon.Name, p => p.Keys = Math.Max(0, p.Keys + delta))));
            row.Children.Add(new TextBlock { Width = 12 });
            row.Children.Add(Check("First clear done", progress.FirstClear, () => _session.UpdateDungeon(dungeon.Name, p => p.FirstClear = !p.FirstClear)));
            _body.Children.Add(row);

            void Fact(string label, string value, Brush? brush = null)
            {
                var line = Line("", top: 2);
                line.Inlines.Add(new Run(label + "  ") { Foreground = Muted });
                line.Inlines.Add(new Run(value) { Foreground = brush ?? Text });
                _body.Children.Add(line);
            }
            Fact("Entrance", dungeon.Entrance);
            Fact("Boss", $"{dungeon.Boss} - {dungeon.Mechanic}");
            Fact("Reward", dungeon.Reward);
            Fact("First clear", dungeon.FirstClear, progress.FirstClear ? Dim : Passive);
            Fact("Skip", dungeon.Skip);
            Fact("Keys from", dungeon.Keys);
        }
    }
}
