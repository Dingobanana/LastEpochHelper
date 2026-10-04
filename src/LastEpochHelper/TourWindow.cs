using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LastEpochHelper;

/// <param name="Show">Something to bring up while this step is showing (a window of the overlay), by name.</param>
internal sealed record TourStep(string Title, string Body, string? Show = null);

/// <summary>
/// "Take a tour": a handful of cards that walk through what the overlay can do, opening the window
/// each card talks about. Offered once, can be skipped at any point, and stays available in the menu.
/// </summary>
internal sealed class TourWindow : Window
{
    public static readonly TourStep[] Steps =
    {
        new("Welcome to Last Epoch Helper",
            "A two-minute tour of what the overlay does. Nothing here changes your game: the overlay only reads the game's log file and what is on screen.\n\n"
            + "Skip it whenever you like - it stays in the menu (☰) as \"Take a tour\"."),
        new("The guide follows you",
            "The box (or bar) shows what to do in the zone you are in and changes by itself when you travel - it reads zone changes from the game's log.\n\n"
            + "• Click a line to tick it off. Lines with a quest reward (passive point, idol slot) are marked.\n"
            + "• Bosses get a short tactics card.\n"
            + "• \"x/15 passives · x/8 idols\" are your quest rewards so far. Open the game's map (M) and the overlay reads the real numbers from its corner.", "overlay"),
        new("Reminders for your level",
            "The green lines are reminders that come due at a level: specialization slots, a loot filter at 10, resistances later.\n\n"
            + "• Click one to tick it off; right-click ticks it and every earlier one.\n"
            + "• Specialization reminders tick themselves once the overlay has seen that many skills with points in the game.\n"
            + "• The others are just reminders - the overlay cannot see whether you did them.", "overlay"),
        new("Import a build",
            "Settings (the gear) → paste a Maxroll planner or build-guide link → Import from Maxroll.\n\n"
            + "That gives the overlay the build's passive and skill trees with the order of every point, its gear, idols and blessings. "
            + "Each character remembers its own build.", "settings"),
        new("The build in short",
            "Planner → TL;DR is the short version of the imported build: which mastery to choose and when, which passive trees get points in which order, "
            + "and which skills to specialize.\n\nUseful when a leveling build spends points in one mastery's tree but ends in another.", "summary"),
        new("The build tree",
            "Press P or S in the game (or click the \"+\" for unspent points) and the build's tree opens next to the game's own panel, and follows the tab or skill you open.\n\n"
            + "• A blue number is the order of the next points; an orange +N is how many go in that node. Both can be switched off at the bottom.\n"
            + "• It shows the points your character really has (read off the game's panel). The slider previews the plan at any number of points while the tree is open.\n"
            + "• Click a node to correct it: left adds a point, right removes one.", "tree"),
        new("The planner",
            "Gear, idols and blessings the build wears at each stage, where its uniques drop, ready-made stash search strings (click to copy), "
            + "a loot filter generated from your build, and checklists for the Monolith and dungeons.", "planner"),
        new("Make it yours",
            "☰ → Bar layout turns the box into one line across the screen. Settings has opacity, font size, the hotkeys, the route "
            + "(full campaign or the shorter leveling routes) and what follows the game.\n\n"
            + "Lock the overlay to make it click-through; the tray icon unlocks it again.", "overlay"),
        new("Updates and problems",
            "The overlay checks for a new version by itself and tells you; Settings → Look for update does it on demand, and \"What's new\" lists the changes.\n\n"
            + "If something misbehaves: ☰ → Report a bug. Describe it, press Send, and the report with the overlay's logs goes to us.\n\n"
            + "One more thing, if you said yes to it: once per version the overlay tells me which country it is used in - the country code and the version, nothing else. Settings → Version changes your answer.\n\nThat's the tour - good luck out there."),
    };

    private readonly Action<string?> _show;
    private readonly TextBlock _title = new() { FontFamily = Theme.TitleFont, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Theme.Gold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _body = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), LineHeight = 20 };
    private readonly TextBlock _count = new() { Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _back = new() { Content = "Back", Padding = new Thickness(12, 3, 12, 3) };
    private readonly Button _next = new() { Padding = new Thickness(16, 3, 16, 3), Margin = new Thickness(8, 0, 0, 0), FontWeight = FontWeights.SemiBold };
    private readonly Button _skip = new() { Content = "Skip the tour", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0) };
    private int _index;

    /// <param name="show">Brings up what a step talks about; called with null when the tour ends.</param>
    public TourWindow(Action<string?> show)
    {
        _show = show;
        Title = "Last Epoch Helper - Tour";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowActivated = false; // offered at start-up: must not pull focus out of the game
        Theme.Dialog(this);
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Lower right of the screen, clear of the guide box and of the windows the steps open.
        Left = Math.Max(0, SystemParameters.WorkArea.Right - Width - 60);
        Top = Math.Max(0, SystemParameters.WorkArea.Bottom - 420);

        _back.Click += (_, _) => Go(_index - 1);
        _next.Click += (_, _) => { if (_index == Steps.Length - 1) Close(); else Go(_index + 1); };
        _skip.Click += (_, _) => Close();
        Closed += (_, _) => _show(null);

        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        DockPanel.SetDock(_count, Dock.Left);
        buttons.Children.Add(_count);
        foreach (var button in new[] { _skip, _next, _back })
        {
            DockPanel.SetDock(button, Dock.Right);
            buttons.Children.Add(button);
        }
        _back.Margin = new Thickness(8, 0, 0, 0);

        var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        panel.Children.Add(_title);
        panel.Children.Add(_body);
        panel.Children.Add(buttons);
        Content = panel;
        Go(0);
    }

    private void Go(int index)
    {
        _index = Math.Clamp(index, 0, Steps.Length - 1);
        var step = Steps[_index];
        _title.Text = step.Title;
        _body.Text = step.Body;
        _count.Text = $"{_index + 1} of {Steps.Length}";
        _back.IsEnabled = _index > 0;
        bool last = _index == Steps.Length - 1;
        _next.Content = _index == 0 ? "Take the tour" : last ? "Done" : "Next";
        _skip.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
        _skip.Content = _index == 0 ? "No thanks" : "Skip the tour";
        if (step.Show is not null) _show(step.Show);
    }
}
