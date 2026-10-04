using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using LastEpochHelper.Core;

namespace LastEpochHelper;

/// <summary>"What's new": the changelog sections for every version newer than the one last run.</summary>
internal sealed class ChangelogWindow : Window
{
    public ChangelogWindow(IReadOnlyList<ChangelogEntry> entries, string heading)
    {
        Title = "Last Epoch Helper - What's new";
        Width = 520;
        MaxHeight = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowActivated = false; // appears at start-up; must not pull focus out of the game
        Theme.Dialog(this);

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 12, 16, 4) };
        text.Inlines.Add(new Run(heading + "\n") { FontFamily = Theme.TitleFont, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Theme.Gold });
        foreach (var entry in entries)
        {
            string title = entry.Title.Length > 0 ? $"  -  {entry.Title}" : "";
            text.Inlines.Add(new Run($"\n{Updater.Display(entry.Version)}{title}\n") { FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Theme.Gold });
            foreach (string line in entry.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // "- item" and nested "  - item" both become bullets, the nested ones indented.
                string trimmed = line.TrimStart();
                string indent = new(' ', 2 + (line.Length - trimmed.Length) * 2);
                text.Inlines.Add(new Run((trimmed.StartsWith("- ") ? indent + "•  " + trimmed[2..] : line) + "\n"));
            }
        }
        if (entries.Count == 0) text.Inlines.Add(new Run("\nNo notes for this version."));

        var close = new Button { Content = "Close", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(16, 6, 16, 14), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();

        var panel = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        panel.Children.Add(close);
        panel.Children.Add(new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = panel;
    }
}
