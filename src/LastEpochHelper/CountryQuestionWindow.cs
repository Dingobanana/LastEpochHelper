using System.Windows;
using System.Windows.Controls;
using LastEpochHelper.Core;

namespace LastEpochHelper;

/// <summary>
/// Asks once whether the overlay may send which country it is used in (see <see cref="UsagePing"/>).
/// Nothing is sent before a yes. Closing the window is no answer: it is asked again after an update.
/// </summary>
internal sealed class CountryQuestionWindow : Window
{
    /// <param name="answer">True for yes, false for no; not called when the window is closed without an answer.</param>
    public CountryQuestionWindow(string country, string version, Action<bool> answer)
    {
        Title = "Last Epoch Helper";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowActivated = false; // appears at start-up: must not pull focus out of the game
        Theme.Dialog(this);

        var heading = new TextBlock { Text = UsagePing.Question, FontFamily = Theme.TitleFont, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Theme.Gold, TextWrapping = TextWrapping.Wrap };
        string example = UsagePing.Message(country, version);
        var body = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), LineHeight = 20,
            Text = "May the overlay tell me which country it is used in? Once per version it would send two things: "
                   + $"the country Windows is set to and the overlay's version - for you, \"{example}\".\n\n"
                   + "No name, no id, nothing about you or your machine; two players in the same country look exactly the same. "
                   + "You can change your answer any time under Settings → Version.",
        };

        var yes = new Button { Content = "Yes, send it", Padding = new Thickness(16, 3, 16, 3), FontWeight = FontWeights.SemiBold, IsDefault = true };
        var no = new Button { Content = "No thanks", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0) };
        yes.Click += (_, _) => { answer(true); Close(); };
        no.Click += (_, _) => { answer(false); Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);

        var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        panel.Children.Add(heading);
        panel.Children.Add(body);
        panel.Children.Add(buttons);
        Content = panel;
    }
}
