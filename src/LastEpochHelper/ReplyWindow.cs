using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using LastEpochHelper.Core;

namespace LastEpochHelper;

/// <summary>An answer to a bug report sent from here (see <see cref="ReportReplies"/>).</summary>
internal sealed class ReplyWindow : Window
{
    public ReplyWindow(ReportReply reply)
    {
        Title = "Last Epoch Helper - Answer to your bug report";
        Width = 480;
        MaxHeight = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Theme.Dialog(this);

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 12, 16, 4) };
        text.Inlines.Add(new Run("Answer to your bug report\n") { FontFamily = Theme.TitleFont, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Theme.Gold });
        text.Inlines.Add(new Run($"Report {reply.Id}{(reply.Date.Length > 0 ? " · answered " + reply.Date : "")}\n\n") { Foreground = Theme.Muted });
        text.Inlines.Add(new Run(reply.Text));
        text.Inlines.Add(new Run("\n\nAnswers are published under the report's number only; nobody can tell who sent it.")
            { Foreground = Theme.Muted, FontSize = 12 });

        var close = new Button { Content = "Close", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(16, 6, 16, 14), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();

        var panel = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        panel.Children.Add(close);
        panel.Children.Add(new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = panel;
    }
}
