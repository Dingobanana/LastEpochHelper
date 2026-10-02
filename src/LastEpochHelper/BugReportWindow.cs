using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace LastEpochHelper;

/// <summary>
/// "Report a bug": the player describes what went wrong, and the overlay packs that together with
/// its logs into one zip file on the desktop for them to pass on.
/// </summary>
internal sealed class BugReportWindow : Window
{
    private const string IssuesUrl = "https://github.com/Dingobanana/LastEpochHelper/issues/new";

    /// <param name="create">Builds the report from (description, include screenshot) and returns the zip's path.</param>
    public BugReportWindow(Func<string, bool, string> create, bool screenshotAvailable, string version)
    {
        Title = "Last Epoch Helper - Report a bug";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        var intro = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = "What went wrong? Say what you did, what you expected and what happened instead.",
        };
        var description = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var screenshot = new CheckBox
        {
            Content = "Include the picture of the game taken when this window opened",
            IsChecked = screenshotAvailable, IsEnabled = screenshotAvailable, Margin = new Thickness(0, 8, 0, 0),
        };
        var note = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = SystemColors.GrayTextBrush,
            Text = "The report is a zip file saved to your desktop - nothing is sent automatically. It holds your description, the overlay's "
                   + "error and activity logs, what it last read off the screen, its settings and the end of the game's log. Your account and "
                   + "character names are removed, but chat that was on screen may be in it, so send it privately to whoever shared the overlay with you.",
        };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.SemiBold };

        var make = new Button { Content = "Create report", Padding = new Thickness(14, 3, 14, 3), IsDefault = false };
        make.Click += (_, _) =>
        {
            try
            {
                string zip = create(description.Text, screenshot.IsChecked == true);
                status.Text = $"Saved: {zip}\nSend that file to whoever shared the overlay with you.";
                // Show the file, selected, so it can be dragged straight into a chat.
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = true });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidDataException)
            {
                status.Text = "Could not create the report: " + e.Message;
            }
        };
        var issue = new Button { Content = "Open a GitHub issue instead...", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
        issue.ToolTip = "Opens the public issue page in your browser with your description filled in (needs a GitHub account; no logs are attached).";
        issue.Click += (_, _) =>
        {
            string body = Uri.EscapeDataString($"{description.Text.Trim()}\n\nVersion: {version}");
            try { Process.Start(new ProcessStartInfo($"{IssuesUrl}?body={body}") { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { status.Text = "Could not open the browser."; }
        };
        var close = new Button { Content = "Close", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(make);
        buttons.Children.Add(issue);
        buttons.Children.Add(close);

        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
        foreach (var element in new UIElement[] { intro, description, screenshot, note, status, buttons }) panel.Children.Add(element);
        Content = panel;
        Loaded += (_, _) => description.Focus();
    }
}
