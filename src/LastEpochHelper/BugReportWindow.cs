using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LastEpochHelper.Core;

namespace LastEpochHelper;

/// <summary>
/// "Report a bug": the player describes what went wrong, and the overlay packs that together with
/// its logs into one zip file - sent straight to the maintainers when this build knows where to,
/// otherwise saved to the desktop for the player to pass on.
/// </summary>
internal sealed class BugReportWindow : Window
{
    private const string IssuesUrl = "https://github.com/Dingobanana/LastEpochHelper/issues/new";

    /// <param name="create">Builds the report from (description, include screenshot, folder) and returns the zip's path.</param>
    /// <param name="send">Sends (zip, description); returns null when it arrived, else why not. Null if this build cannot send.</param>
    /// <param name="replyId">The report's reply id: an answer to it shows up in the overlay.</param>
    public BugReportWindow(Func<string, bool, string, string> create, Func<string, string, Task<string?>>? send, bool screenshotAvailable, string version, string? replyId = null)
    {
        Title = "Last Epoch Helper - Report a bug";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Theme.Dialog(this);

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
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
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Theme.Muted,
            Text = (send is not null
                       ? "Send delivers the report privately to the people who maintain the overlay. "
                       : "The report is a zip file saved to your desktop - nothing is sent automatically; pass it on privately to whoever shared the overlay with you. ")
                   + "It holds your description, the overlay's error and activity logs, what it last read off the screen, its settings and the "
                   + "end of the game's log. Your account and character names are removed, but chat that was on screen may be in it.",
        };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.SemiBold };

        void SaveToDesktop(string? because)
        {
            try
            {
                string zip = create(description.Text, screenshot.IsChecked == true, desktop);
                status.Text = (because is null ? "" : $"Could not send: {because}.\n") + $"Saved: {zip}\nSend that file to whoever shared the overlay with you.";
                // Show the file, selected, so it can be dragged straight into a chat.
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = true });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidDataException)
            {
                status.Text = "Could not create the report: " + e.Message;
            }
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (send is not null)
        {
            var sendButton = new Button { Content = "Send", Padding = new Thickness(18, 3, 18, 3), FontWeight = FontWeights.SemiBold };
            sendButton.Click += async (_, _) =>
            {
                sendButton.IsEnabled = false;
                status.Text = "Sending...";
                string? problem;
                string? zip = null;
                try
                {
                    string temp = Path.Combine(Path.GetTempPath(), "LastEpochHelper-report");
                    zip = create(description.Text, screenshot.IsChecked == true, temp);
                    // A report that is too big is nearly always the picture's doing.
                    if (new FileInfo(zip).Length > ReportSender.MaxBytes && screenshot.IsChecked == true)
                    {
                        File.Delete(zip);
                        zip = create(description.Text, false, temp);
                    }
                    problem = await send(zip, description.Text);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    problem = e.Message;
                }
                try { if (zip is not null) File.Delete(zip); }
                catch (IOException) { }

                if (problem is null)
                {
                    status.Text = replyId is null ? "Sent - thank you. You can close this window."
                        : $"Sent - thank you. If we answer, the answer shows up in the overlay within a few hours (report {replyId}). You can close this window.";
                    description.IsEnabled = false;
                    return;
                }
                SaveToDesktop(problem);
                sendButton.IsEnabled = true;
            };
            buttons.Children.Add(sendButton);
        }

        var save = new Button { Content = send is null ? "Create report" : "Save to desktop instead", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(send is null ? 0 : 8, 0, 0, 0) };
        save.Click += (_, _) => SaveToDesktop(null);
        var issue = new Button { Content = "GitHub issue...", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
        issue.ToolTip = "Opens the public issue page in your browser with your description filled in (needs a GitHub account; no logs are attached).";
        issue.Click += (_, _) =>
        {
            string body = Uri.EscapeDataString($"{description.Text.Trim()}\n\nVersion: {version}");
            try { Process.Start(new ProcessStartInfo($"{IssuesUrl}?body={body}") { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { status.Text = "Could not open the browser."; }
        };
        var close = new Button { Content = "Close", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(save);
        buttons.Children.Add(issue);
        buttons.Children.Add(close);

        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
        foreach (var element in new UIElement[] { intro, description, screenshot, note, status, buttons }) panel.Children.Add(element);
        Content = panel;
        Loaded += (_, _) => description.Focus();
    }
}
