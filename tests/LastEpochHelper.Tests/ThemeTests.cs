using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LastEpochHelper.Tests;

public class ThemeTests
{
    /// <summary>The menu's look is written as markup in a string: it has to load and draw. Set LEH_RENDER_DIR for a PNG to look at.</summary>
    [Fact]
    public void TheStyledMenu_LoadsAndDraws()
    {
        string? output = Environment.GetEnvironmentVariable("LEH_RENDER_DIR");
        Exception? failure = null;
        double height = 0;
        var thread = new Thread(() =>
        {
            try
            {
                var menu = Theme.Styled(new ContextMenu());
                menu.Items.Add(new MenuItem { Header = "Chapter 1 · Divine Era", IsChecked = true });
                menu.Items.Add(new MenuItem { Header = "Chapter 2 · Ruined Era" });
                menu.Items.Add(new Separator());
                var routes = new MenuItem { Header = "Route" };
                routes.Items.Add(new MenuItem { Header = "Full" });
                menu.Items.Add(routes);
                menu.Items.Add(new MenuItem { Header = "Not available", IsEnabled = false });

                menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                menu.Arrange(new Rect(menu.DesiredSize));
                menu.UpdateLayout();
                height = menu.ActualHeight;
                if (output is null) return;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(menu);
                Directory.CreateDirectory(output);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, "menu.png"));
                encoder.Save(file);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
        Assert.True(height > 80, $"menu height {height}");
    }

    [Fact]
    public void ThePlate_TakesTheOpacityItIsGiven()
    {
        var brush = Assert.IsType<LinearGradientBrush>(Theme.Plate(128));
        Assert.All(brush.GradientStops, stop => Assert.Equal(128, stop.Color.A));
        Assert.True(brush.IsFrozen);
    }
}
