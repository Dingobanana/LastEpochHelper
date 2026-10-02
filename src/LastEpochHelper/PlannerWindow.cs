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
    public const string SummaryTab = "TL;DR";
    private static readonly string[] Tabs = { SummaryTab, "Gear", "Idols", "Targets", "Search", "Loot filter", "Monolith", "Morditas", "Prophecies", "Dungeons", "Deaths" };

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
    private readonly WrapPanel _tabs = new() { MaxWidth = 530 };
    private readonly StackPanel _body = new() { Width = 560 };
    private readonly Dictionary<string, FilterReport?> _reports = new();
    private string _filterMessage = "";
    private HashSet<string>? _filterBuilds;

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
            case SummaryTab: RenderSummary(); break;
            case "Gear": RenderGear(); break;
            case "Idols": RenderIdols(); break;
            case "Targets": RenderTargets(); break;
            case "Search": RenderSearch(); break;
            case "Loot filter": RenderFilters(); break;
            case "Monolith": RenderMonolith(); break;
            case "Morditas" or "Prophecies": RenderReference(current); break;
            case "Dungeons": RenderDungeons(); break;
            case "Deaths": RenderDeaths(); break;
        }
    }

    /// <summary>
    /// A guide's "tabs": either steps by level, of which the overlay follows the one for the
    /// character's level, or alternative versions of the build, of which one was imported.
    /// </summary>
    private void RenderStages()
    {
        if (_session.Tree is not { } build) return;
        if (build.Variants.Count > 1)
        {
            Heading("Versions in this guide");
            for (int i = 0; i < build.Variants.Count; i++)
            {
                int index = i;
                _body.Children.Add(Choice($"{(i == build.Variant ? "▶" : "   ")}  {build.Variants[i]}{(i == build.Variant ? "  -  in use" : "")}",
                    i == build.Variant, () => _session.SwitchVariant(index)));
            }
            Note("These are alternatives, not steps: gear, idols and sometimes skills differ. Click one to use it (also at the bottom of the build tree).");
            return;
        }
        if (build.Stages.Count < 2) return;
        Heading("Stages in this guide");
        var now = _session.Stage;
        bool pinned = _session.PinnedStage is not null;
        _body.Children.Add(Choice($"{(pinned ? "   " : "▶")}  Follow my level{(pinned ? "" : $"  -  now: {now?.Name}")}", !pinned, () => _session.SetStage(null)));
        foreach (var stage in build.Stages)
        {
            var chosen = stage;
            bool current = pinned && stage == now;
            _body.Children.Add(Choice($"{(current ? "▶" : "   ")}  {stage.Name}{(current ? "  -  chosen by hand" : "")}", current, () => _session.SetStage(chosen)));
        }
        Note("By default the overlay moves to the next stage by itself as you level - gear, idols and trees follow. Click a stage to stay on it (also at the bottom of the build tree).");
    }

    /// <summary>A line that can be clicked to choose it; the one in use is green.</summary>
    private static TextBlock Choice(string text, bool selected, Action choose)
    {
        var line = Line(text, selected ? Green : Text, top: 3, weight: selected ? FontWeights.SemiBold : FontWeights.Normal);
        line.Cursor = Cursors.Hand;
        line.MouseLeftButtonDown += (_, e) => { choose(); e.Handled = true; };
        return line;
    }

    /// <summary>The imported build boiled down: mastery, the order of the passive trees, the skills.</summary>
    private void RenderSummary()
    {
        if (_session.Plan is not { } plan)
        {
            Note("No build imported for this character. Open settings (the gear on the overlay), paste a Maxroll planner or build guide link and press 'Import from Maxroll'.");
            return;
        }
        var summary = BuildSummary.From(plan);
        Heading($"{plan.Name}  ·  the short version");
        foreach (string headline in summary.Headlines) _body.Children.Add(Line(headline, Text, top: 5));
        RenderStages();
        if (summary.Steps.Count == 0) return;

        Heading("What to do, in order");
        int level = _session.Profile.Level;
        bool nextMarked = false;
        foreach (var step in summary.Steps)
        {
            bool past = step.ToLevel < level, current = !past && step.Level <= level;
            string mark = past ? "✓" : current ? "▶" : !nextMarked ? "→" : " ";
            if (!past && !current) nextMarked = true;
            string when = step.ToLevel > step.Level ? $"Lvl {step.Level}-{step.ToLevel}" : $"Lvl {step.Level}";
            var brush = past ? Muted : step.Kind == "mastery" ? Gold : current ? Green : Text;
            _body.Children.Add(Line($"{mark}  {when}:  {step.Text}", brush, top: 4,
                weight: step.Kind == "mastery" || current ? FontWeights.SemiBold : FontWeights.Normal));
        }
        Note($"✓ behind you  ·  ▶ where a level {level} character is  ·  → next. Levels are the build's estimates - the order is what counts. "
             + "The node-by-node order is in the build tree (P or S in the game).");
    }

    private TreeStage? Stage()
    {
        if (_session.Stage is { } stage) return stage;
        Note("No build imported for this character. Open settings (the gear on the overlay), paste a Maxroll planner or build guide link and press 'Import from Maxroll'.");
        return null;
    }

    private void RenderGear()
    {
        if (Stage() is not { } stage) return;
        Heading($"{stage.Name}  ·  gear the build wears by level {stage.Level}");
        if (stage.Gear.Count == 0) { Note("This stage of the build lists no gear."); return; }

        var wanted = stage.WantedAffixes(10);
        if (wanted.Count > 0)
        {
            // Each affix is a button that puts its name on the clipboard, ready for the stash search box.
            var look = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            look.Children.Add(new TextBlock { Text = "Look for:", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 3) });
            foreach (var (affix, count) in wanted)
            {
                string search = affix.StartsWith("Added ", StringComparison.OrdinalIgnoreCase) ? affix[6..] : affix;
                var chip = Button(count > 1 ? $"{affix} ×{count}" : affix, () =>
                {
                    try { Clipboard.SetText(search); _session.ShowAlert($"Copied \"{search}\" - paste it into the stash search (Ctrl+V).", 8); }
                    catch (System.Runtime.InteropServices.ExternalException) { }
                }, color: Green);
                chip.Margin = new Thickness(0, 0, 4, 3);
                chip.ToolTip = "Click to copy for the stash search box";
                look.Children.Add(chip);
            }
            _body.Children.Add(look);
        }
        Note("Click an affix to copy it for the stash search. Tiers are what the build ends the stage with - any tier of the right affix is an upgrade on the way. Click a slot when you have it covered.");

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

    private void RenderTargets()
    {
        if (Stage() is not { } stage) return;
        Heading($"{stage.Name}  ·  where to get what the build wants");

        var uniques = stage.Gear.Concat(stage.Idols).Where(g => g.Rarity.Length > 0).DistinctBy(g => g.Name).ToList();
        if (uniques.Count == 0) Note("This stage of the build uses no unique or set items.");
        foreach (var item in uniques)
        {
            var timeline = _session.Endgame.TimelineForItemType(item.Type);
            var row = Line("", top: 5);
            row.Inlines.Add(new Run(item.Name) { Foreground = item.Rarity == "set" ? Set : Unique, FontWeight = FontWeights.SemiBold });
            row.Inlines.Add(new Run($"  ({item.Type})") { Foreground = Dim });
            row.Inlines.Add(new Run(timeline is null
                ? "\n      no timeline gives this item type as an echo reward - check its drop source"
                : $"\n      echo rewards for this item type: {timeline.Name} (level {timeline.Level})") { Foreground = Muted, FontSize = 12 });
            _body.Children.Add(row);
        }
        if (uniques.Count > 0) Note("Echo rewards narrow the item type, not the exact unique; boss-only uniques drop from their boss instead.");

        Heading("Blessings");
        if (stage.Blessings.Count == 0) Note("This stage of the build lists no blessings.");
        foreach (string blessing in stage.Blessings)
        {
            string plain = blessing.StartsWith("Grand ", StringComparison.OrdinalIgnoreCase) ? blessing[6..] : blessing;
            var timeline = _session.Endgame.TimelineOf(blessing);
            var data = timeline?.Blessings.FirstOrDefault(b => b.Name.Equals(plain, StringComparison.OrdinalIgnoreCase));
            bool taken = timeline is not null && _session.TimelineProgress(timeline.Name).Blessing.Equals(plain, StringComparison.OrdinalIgnoreCase);
            var row = Line("", top: 5);
            row.Inlines.Add(new Run(taken ? "✔ " : "☐ ") { Foreground = taken ? Green : Muted });
            row.Inlines.Add(new Run(blessing) { Foreground = taken ? Dim : Idol, FontWeight = FontWeights.SemiBold });
            if (timeline is not null)
                row.Inlines.Add(new Run($"   {timeline.Name} - kill {timeline.Boss}") { Foreground = Muted });
            if (data is not null)
                row.Inlines.Add(new Run($"\n      normal {data.Effect}" + (data.Grand.Length > 0 ? $"   ·   grand {data.Grand}" : "")) { Foreground = Muted, FontSize = 12 });
            _body.Children.Add(row);
        }
        if (stage.Blessings.Count > 0) Note("Ticked when the same blessing is chosen on the Monolith page. The top of each range is a good roll; re-kill the boss for a new offer.");
    }

    private void RenderSearch()
    {
        if (Stage() is not { } stage) return;
        Heading($"{stage.Name}  ·  stash search strings");
        Note("Click a line to copy it, then paste it into the stash search box (Ctrl+V).");
        foreach (var search in StashSearch.For(stage))
        {
            var captured = search;
            var row = Line("", top: 6);
            row.Cursor = Cursors.Hand;
            row.Inlines.Add(new Run(search.Label + "\n") { Foreground = Muted, FontSize = 12 });
            row.Inlines.Add(new Run("   " + search.Text) { Foreground = Green, FontFamily = new FontFamily("Consolas") });
            row.MouseLeftButtonDown += (_, e) =>
            {
                try { Clipboard.SetText(captured.Text); _session.ShowAlert($"Copied: {captured.Text}", 6); }
                catch (System.Runtime.InteropServices.ExternalException) { }
                e.Handled = true;
            };
            _body.Children.Add(row);
        }
        Note("Affix names are matched against the tooltip text, so an affix the game words differently will not be found. Combine your own with & (and), | (or) and ! (not).");
    }

    private void RenderReference(string title)
    {
        var page = _session.Endgame.Reference.FirstOrDefault(p => p.Title == title);
        Heading(title);
        if (page is null) { Note("No reference text in this version."); return; }
        foreach (string line in page.Lines)
        {
            bool key = line.StartsWith('!');
            _body.Children.Add(Line(key ? line[1..] : line, key ? Text : Muted, top: key ? 7 : 2, weight: key ? FontWeights.SemiBold : FontWeights.Normal));
        }
        Note("From Maxroll's copy of the 1.5 patch notes; not checked in game.");
    }

    private void RenderDeaths()
    {
        var deaths = _session.Profile.DeathLog;
        Heading($"Deaths  ·  {deaths.Count}");
        if (deaths.Count == 0) { Note("None recorded for this character since the journal was added."); return; }
        Note("Click a death to note why. Patterns show quickly: the same cause three times is the thing to fix.");

        foreach (var group in deaths.Where(d => d.Cause.Length > 0).GroupBy(d => d.Cause).OrderByDescending(g => g.Count()))
            _body.Children.Add(Line($"{group.Count()}×  {group.Key}", Passive, top: 3));

        foreach (var death in deaths.AsEnumerable().Reverse().Take(40))
        {
            var entry = death;
            var row = Line("", top: 6);
            row.Cursor = Cursors.Hand;
            row.Inlines.Add(new Run($"{entry.When:dd MMM HH:mm}  ") { Foreground = Dim });
            row.Inlines.Add(new Run(entry.Zone) { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
            bool under = entry.ZoneLevel > 0 && entry.Level < entry.ZoneLevel - 2;
            row.Inlines.Add(new Run($"   level {entry.Level}" + (entry.ZoneLevel > 0 ? $" in a level {entry.ZoneLevel} zone" : "")) { Foreground = under ? Frozen("#FF7A6B") : Muted });
            row.Inlines.Add(new Run("\n      " + (entry.Cause.Length > 0 ? entry.Cause : "cause: click to set")) { Foreground = entry.Cause.Length > 0 ? Passive : Dim, FontSize = 12 });
            row.MouseLeftButtonDown += (_, e) =>
            {
                int next = (Array.IndexOf(DeathEntry.Causes, entry.Cause) + 1) % DeathEntry.Causes.Length;
                _session.SetDeathCause(entry, DeathEntry.Causes[next]);
                e.Handled = true;
            };
            _body.Children.Add(row);
        }
    }

    private void RenderFilters()
    {
        Heading("Loot filter from your builds");
        Note("Writes a filter into the game's folder that shows everything at first, then only rares, then only items with the affixes of the chosen builds. Uniques, sets and exalted items always show. Pick more than one build to share a filter between characters. Select it in game with Shift+F.");

        // Which imported builds the filter tools work from; this character's own by default.
        var imported = _session.ImportedBuilds();
        string own = Path.GetFileNameWithoutExtension(_session.Profile.BuildPlan);
        _filterBuilds ??= imported.Contains(own) ? new HashSet<string> { own } : new HashSet<string>();
        _filterBuilds.IntersectWith(imported);
        if (imported.Count > 0)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            foreach (string build in imported)
            {
                string captured = build;
                bool on = _filterBuilds.Contains(build);
                var chip = Check(build, on, () => { if (!_filterBuilds.Remove(captured)) _filterBuilds.Add(captured); Render(); });
                chip.Margin = new Thickness(0, 0, 4, 3);
                chips.Children.Add(chip);
            }
            _body.Children.Add(chips);
        }

        var generate = Button("Generate filter", () =>
        {
            string? name = _filterBuilds.Count == 0 ? null : _session.GenerateFilter(_filterBuilds);
            _filterMessage = name is null
                ? "No build with gear to generate from - import a Maxroll build first (and re-import builds imported before 0.7)."
                : $"Wrote \"{name}\". In game: Shift+F, then pick it from the list.";
            Render();
        }, color: Green);
        generate.HorizontalAlignment = HorizontalAlignment.Left;
        generate.Margin = new Thickness(0, 5, 0, 0);
        _body.Children.Add(generate);
        if (_filterMessage.Length > 0) _body.Children.Add(Line(_filterMessage, Passive, 12, top: 3));

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
            var keep = Button("+ build", () =>
            {
                string? created = _filterBuilds is { Count: > 0 } ? _session.AddBuildRulesTo(name, _filterBuilds) : null;
                _filterMessage = created is null
                    ? "Nothing to add: tick a build with gear above first."
                    : $"Wrote \"{created}\": your filter with the builds' uniques, 2+ affix items and idols always shown on top.";
                Render();
            });
            keep.ToolTip = "Make a copy of this filter that never hides what the ticked builds want";
            keep.Margin = new Thickness(0, 0, 4, 0);
            DockPanel.SetDock(keep, Dock.Right);
            var check = Button("check", () => { _reports[name] = _session.InspectFilter(name); Render(); });
            check.ToolTip = "Look for signs that this filter is older than the game";
            check.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(check, Dock.Right);
            row.Children.Add(check);
            row.Children.Add(keep);
            row.Children.Add(new TextBlock { Text = name, Foreground = level > 0 ? Brushes.White : Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            _body.Children.Add(row);
            if (_reports.TryGetValue(name, out var report)) RenderReport(report);
        }
        var open = Button("Open Filters folder", () => Process.Start(new ProcessStartInfo(_session.FiltersDir) { UseShellExecute = true }));
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, 10, 0, 0);
        _body.Children.Add(open);
    }

    private void RenderReport(FilterReport? report)
    {
        if (report is null) { Note("      Could not read this file as a loot filter."); return; }
        var lines = new List<string> { $"format {report.Version}, {report.Rules} rules" };
        lines.AddRange(report.Notes);
        if (report.NewerUniqueCount > 0)
            lines.Add($"{report.NewerUniqueCount} uniques are newer than anything this filter lists: {string.Join(", ", report.NewerUniques)}{(report.NewerUniqueCount > report.NewerUniques.Count ? ", ..." : "")}");
        if (report.NewerAffixCount > 0)
            lines.Add($"{report.NewerAffixCount} affixes are newer than anything this filter lists: {string.Join(", ", report.NewerAffixes)}{(report.NewerAffixCount > report.NewerAffixes.Count ? ", ..." : "")}");
        if (report.NewerUniqueCount == 0 && report.NewerAffixCount == 0 && report.Notes.Count == 0)
            lines.Add("nothing newer than this filter was found");
        foreach (string line in lines)
            _body.Children.Add(Line("      " + line, report.NewerUniqueCount + report.NewerAffixCount > 0 ? Passive : Muted, 12, top: 1));
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
