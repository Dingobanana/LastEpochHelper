using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using LastEpochHelper.Core;

namespace LastEpochHelper;

/// <summary>
/// Small click-through note beside the game's blessing choice: which blessing the build takes from
/// this timeline. Placed next to the game's window, never over it.
/// </summary>
internal sealed class BlessingWindow : Window
{
    private static readonly Brush Blessing = new SolidColorBrush(Color.FromRgb(0xC7, 0x9B, 0xFF));
    private readonly StackPanel _body = new() { Margin = new Thickness(14, 10, 14, 12) };

    public BlessingWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 420;
        Content = new Border
        {
            Background = Theme.PlateBrush,
            BorderBrush = Theme.Border,
            BorderThickness = Theme.Edge,
            CornerRadius = Theme.Corners,
            Child = _body,
        };
        SourceInitialized += (_, _) => Native.ApplyOverlayStyle(new WindowInteropHelper(this).Handle, clickThrough: true);
    }

    /// <summary>Fills the note in and shows it with its top-left corner at <paramref name="at"/> (device-independent units).</summary>
    public void Display(BlessingAdvice advice, Point at)
    {
        Fill(advice);
        Left = at.X;
        Top = at.Y;
        if (!IsVisible) Show();
    }

    public void Fill(BlessingAdvice advice)
    {
        _body.Children.Clear();
        var heading = Theme.Heading(advice.Timeline is null ? "Blessings this build takes" : "Blessing to take");
        heading.Margin = new Thickness(0, 0, 0, 4);
        _body.Children.Add(heading);

        if (advice.Wanted.Count == 0)
            _body.Children.Add(Line($"The build takes no blessing from {advice.Timeline}: pick what you like.", Theme.Muted));
        foreach (var wanted in advice.Wanted)
        {
            var row = Line("", Theme.Text);
            row.Margin = new Thickness(0, 4, 0, 0);
            row.Inlines.Add(new Run(wanted.Name) { Foreground = Blessing, FontWeight = FontWeights.SemiBold, FontSize = 15 });
            if (advice.Timeline is null && wanted.Timeline.Length > 0) row.Inlines.Add(new Run($"   {wanted.Timeline}") { Foreground = Theme.Muted });
            if (wanted.Effect.Length > 0) row.Inlines.Add(new Run($"\n{wanted.Effect}") { Foreground = Theme.Text, FontSize = 12 });
            if (wanted.Grand.Length > 0) row.Inlines.Add(new Run($"\nGrand (empowered): {wanted.Grand}") { Foreground = Theme.Muted, FontSize = 12 });
            _body.Children.Add(row);
        }
        if (advice.Wanted.Count > 0)
        {
            var hint = Line("Hover the icons to find it. Not offered? Beat the boss again for a new offer.", Theme.Muted);
            hint.Margin = new Thickness(0, 8, 0, 0);
            hint.FontSize = 12;
            _body.Children.Add(hint);
        }
    }

    private static TextBlock Line(string text, Brush brush) => new()
    {
        Text = text, Foreground = brush, FontSize = 13, TextWrapping = TextWrapping.Wrap,
    };
}
