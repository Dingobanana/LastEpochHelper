using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

/// <summary>
/// The dialogs in the overlay's look: their markup has to load and draw. Nothing is shown on screen -
/// each window's content is drawn on its own. Set LEH_RENDER_DIR for PNGs to look at.
/// </summary>
public class DialogRenderTests
{
    [Fact]
    public void TheDialogs_LoadAndDraw()
    {
        string? output = Environment.GetEnvironmentVariable("LEH_RENDER_DIR");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Draw(new CountryQuestionWindow("DK", "0.7.20", _ => { }), "dialog-country.png", output);
                Draw(new TourWindow(_ => { }), "dialog-tour.png", output);
                Draw(new ChangelogWindow(new[] { new ChangelogEntry(new Version(0, 7, 20), "Ready for everyone", "- One thing\n- Another thing\n  - nested") }, "Updated to 0.7.20"), "dialog-changelog.png", output);
                Draw(new BugReportWindow((_, _, _) => "", (_, _) => Task.FromResult<string?>(null), true, "0.7.20"), "dialog-report.png", output);
                Draw(Sample(), "dialog-controls.png", output);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }

    /// <summary>Every kind of control the settings window uses, in a themed window.</summary>
    private static Window Sample()
    {
        var window = new Window { Width = 520 };
        Theme.Dialog(window);
        var tabs = new TabControl();
        var page = new StackPanel { Margin = new Thickness(12, 6, 12, 12) };
        tabs.Items.Add(new TabItem { Header = "Character", Content = page, Padding = new Thickness(10, 4, 10, 4) });
        tabs.Items.Add(new TabItem { Header = "Following the game", Content = new StackPanel(), Padding = new Thickness(10, 4, 10, 4) });
        tabs.SelectedIndex = 0;
        page.Children.Add(Theme.Heading("Can the overlay read the game?"));
        page.Children.Add(new CheckBox { Content = "Ticked box", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) });
        page.Children.Add(new CheckBox { Content = "Unticked box", Margin = new Thickness(0, 4, 0, 0) });
        page.Children.Add(new TextBox { Text = "https://maxroll.gg/last-epoch/planner/abc", Margin = new Thickness(0, 6, 0, 0) });
        page.Children.Add(new ComboBox { ItemsSource = new[] { "Always", "Only while the mouse is over it" }, SelectedIndex = 1, Margin = new Thickness(0, 6, 0, 0) });
        page.Children.Add(new Slider { Minimum = 0, Maximum = 1, Value = 0.6, Margin = new Thickness(0, 6, 0, 0) });
        var check = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        check.Inlines.Add(new System.Windows.Documents.Run("✓  Windows reads text in English (en-US).\n") { Foreground = Theme.Good });
        check.Inlines.Add(new System.Windows.Documents.Run("✗  Last Epoch is set to German.\n") { Foreground = Theme.Bad });
        page.Children.Add(check);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(new Button { Content = "Apply", IsDefault = true });
        buttons.Children.Add(new Button { Content = "Close", Margin = new Thickness(6, 0, 0, 0) });
        buttons.Children.Add(new Button { Content = "Disabled", IsEnabled = false, Margin = new Thickness(6, 0, 0, 0) });
        page.Children.Add(buttons);
        window.Content = new StackPanel { Margin = new Thickness(12), Children = { tabs } };
        return window;
    }

    /// <summary>Draws a window's content on the window's own background and resources, without showing the window.</summary>
    private static void Draw(Window window, string file, string? output)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var plate = new Border
        {
            Background = window.Background, Child = content, Width = double.IsNaN(window.Width) ? 520 : window.Width,
        };
        plate.Resources = window.Resources;
        TextElement(plate, window);
        plate.Measure(new Size(plate.Width, double.PositiveInfinity));
        plate.Arrange(new Rect(plate.DesiredSize));
        plate.UpdateLayout();
        Assert.True(plate.ActualHeight > 40, $"{file}: height {plate.ActualHeight}");
        if (output is null) return;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(plate.ActualWidth), (int)Math.Ceiling(plate.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(plate);
        Directory.CreateDirectory(output);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, file));
        encoder.Save(stream);
    }

    private static void TextElement(Border plate, Window window)
    {
        System.Windows.Documents.TextElement.SetForeground(plate, window.Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(plate, window.FontFamily);
    }
}
