using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace LastEpochHelper;

/// <summary>
/// A short greeting at start: a rune circle opening like a rift in time, embers and the name in gold.
/// Under two seconds, click-through, never takes focus, and closes itself; the overlay does not wait for it.
/// </summary>
internal sealed class SplashWindow : Window
{
    private const double WindowWidth = 640, WindowHeight = 360;
    /// <summary>Everything happens within this; the last part of it is the fade.</summary>
    private static readonly TimeSpan Length = TimeSpan.FromSeconds(1.75), FadeFrom = TimeSpan.FromSeconds(1.3);

    private static readonly Color Ember = Color.FromRgb(0xE8, 0x9A, 0x3C), GoldColor = Color.FromRgb(0xC9, 0xA8, 0x5C);

    /// <summary>Shows the greeting; anything going wrong only means there is none.</summary>
    public static void Play()
    {
        try { new SplashWindow().Show(); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception) { }
    }

    private SplashWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        Width = WindowWidth;
        Height = WindowHeight;
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - WindowWidth) / 2;
        Top = area.Top + (area.Height - WindowHeight) / 2;
        SourceInitialized += (_, _) => Native.ApplyOverlayStyle(new WindowInteropHelper(this).Handle, clickThrough: true);

        var stage = new Canvas { Width = WindowWidth, Height = WindowHeight };
        Content = stage;
        double cx = WindowWidth / 2, cy = 140;

        // Smoke: a dark cloud that fades out to nothing at its edges, so no box is ever seen.
        var smoke = new Ellipse
        {
            Width = WindowWidth, Height = WindowHeight,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xFA, 0x0A, 0x0B, 0x0F), 0),
                    new GradientStop(Color.FromArgb(0xF6, 0x0C, 0x0D, 0x12), 0.6),
                    new GradientStop(Color.FromArgb(0xB4, 0x0A, 0x0B, 0x0F), 0.82),
                    new GradientStop(Color.FromArgb(0x00, 0x0A, 0x0B, 0x0F), 1),
                },
            },
        };
        stage.Children.Add(smoke);
        Fade(smoke, 0, 1, 0, 0.25);

        // The rift's light, breathing once.
        var glow = Centred(new Ellipse
        {
            Width = 190, Height = 190,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x70, 0xE8, 0xB8, 0x6A), 0),
                    new GradientStop(Color.FromArgb(0x28, 0xC9, 0xA8, 0x5C), 0.45),
                    new GradientStop(Color.FromArgb(0x00, 0xC9, 0xA8, 0x5C), 1),
                },
            },
        }, cx, cy);
        stage.Children.Add(glow);
        Fade(glow, 0, 1, 0.1, 0.45);
        Scale(glow, 0.3, 1.0, 0.1, 0.6);

        // Two rune circles, turning against each other while they open.
        stage.Children.Add(Ring(cx, cy, 128, 1.4, new DoubleCollection { 1, 3, 10, 3 }, 0.9, turn: 50, delay: 0.05));
        stage.Children.Add(Ring(cx, cy, 98, 1.0, new DoubleCollection { 6, 4 }, 0.6, turn: -70, delay: 0.15));
        stage.Children.Add(Ring(cx, cy, 150, 0.8, new DoubleCollection { 0.5, 7 }, 0.45, turn: 25, delay: 0.0));

        // The rift itself: a crack of light that tears open down the middle and settles.
        var rift = Centred(new Ellipse
        {
            Width = 7, Height = 118,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xF4, 0xD8), 0),
                    new GradientStop(Color.FromArgb(0xC0, 0xE8, 0xB8, 0x6A), 0.45),
                    new GradientStop(Color.FromArgb(0x00, 0xC9, 0xA8, 0x5C), 1),
                },
            },
            Effect = new BlurEffect { Radius = 3 },
        }, cx, cy);
        stage.Children.Add(rift);
        var tear = new ScaleTransform(0.2, 0);
        rift.RenderTransform = tear;
        tear.BeginAnimation(ScaleTransform.ScaleYProperty, Animate(0, 1, 0.12, 0.35));
        var widen = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(0.12) };
        widen.KeyFrames.Add(new EasingDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        widen.KeyFrames.Add(new EasingDoubleKeyFrame(2.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        widen.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8)), new CubicEase { EasingMode = EasingMode.EaseInOut }));
        tear.BeginAnimation(ScaleTransform.ScaleXProperty, widen);

        // Embers drifting up out of the rift.
        var random = new Random();
        for (int i = 0; i < 22; i++)
        {
            double size = 1.6 + random.NextDouble() * 2.4;
            var ember = new Ellipse
            {
                Width = size, Height = size, Opacity = 0,
                Fill = new SolidColorBrush(random.Next(3) == 0 ? GoldColor : Ember),
                Effect = new BlurEffect { Radius = 1.5 },
            };
            double x = cx + (random.NextDouble() - 0.5) * 200, y = cy + 20 + random.NextDouble() * 60;
            Canvas.SetLeft(ember, x);
            Canvas.SetTop(ember, y);
            stage.Children.Add(ember);
            double start = random.NextDouble() * 0.7, life = 0.6 + random.NextDouble() * 0.5;
            var rise = new TranslateTransform();
            ember.RenderTransform = rise;
            rise.BeginAnimation(TranslateTransform.YProperty, Animate(0, -(50 + random.NextDouble() * 70), start, life, ease: false));
            rise.BeginAnimation(TranslateTransform.XProperty, Animate(0, (random.NextDouble() - 0.5) * 30, start, life, ease: false));
            var flicker = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(start) };
            flicker.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            flicker.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(life * 0.25))));
            flicker.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(life))));
            ember.BeginAnimation(OpacityProperty, flicker);
        }

        // The name, coming out of a blur.
        var title = new TextBlock
        {
            Text = Spaced("LAST EPOCH HELPER"), FontFamily = Theme.TitleFont, FontSize = 30, FontWeight = FontWeights.SemiBold,
            Foreground = new LinearGradientBrush(Color.FromRgb(0xF0, 0xD9, 0x9A), Color.FromRgb(0xA8, 0x86, 0x42), 90),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 0, Opacity = 1 },
        };
        Place(title, cx, 262);
        stage.Children.Add(title);
        Fade(title, 0, 1, 0.3, 0.5);
        var lift = new TranslateTransform();
        title.RenderTransform = lift;
        lift.BeginAnimation(TranslateTransform.YProperty, Animate(10, 0, 0.3, 0.55));

        // A gold rule drawn out from the middle under the name.
        var rule = new Rectangle { Width = 360, Height = 1.5, Fill = Theme.Rule, RenderTransformOrigin = new Point(0.5, 0.5) };
        Canvas.SetLeft(rule, cx - 180);
        Canvas.SetTop(rule, 290);
        stage.Children.Add(rule);
        var draw = new ScaleTransform(0, 1);
        rule.RenderTransform = draw;
        draw.BeginAnimation(ScaleTransform.ScaleXProperty, Animate(0, 1, 0.45, 0.5));

        var line = new TextBlock
        {
            Text = Spaced("THE TIMELINES AWAIT"), FontFamily = Theme.TitleFont, FontSize = 11, Foreground = Theme.Muted, Opacity = 0,
        };
        Place(line, cx, 304);
        stage.Children.Add(line);
        Fade(line, 0, 0.85, 0.6, 0.4);

        // Then everything goes back into the dark, and the window closes itself.
        var fade = Animate(1, 0, FadeFrom.TotalSeconds, (Length - FadeFrom).TotalSeconds);
        fade.Completed += (_, _) => Close();
        stage.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Letters set apart, as engraved titles are.</summary>
    private static string Spaced(string text) => string.Join(" ", text.ToCharArray());

    private static UIElement Ring(double cx, double cy, double diameter, double thickness, DoubleCollection dashes, double opacity, double turn, double delay)
    {
        var ring = Centred(new Ellipse
        {
            Width = diameter, Height = diameter, Stroke = new SolidColorBrush(GoldColor), StrokeThickness = thickness,
            StrokeDashArray = dashes, Opacity = 0, Effect = new DropShadowEffect { Color = GoldColor, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.8 },
        }, cx, cy);
        Fade(ring, 0, opacity, delay, 0.4);
        var group = new TransformGroup();
        var scale = new ScaleTransform(0.55, 0.55);
        var rotate = new RotateTransform(0);
        group.Children.Add(scale);
        group.Children.Add(rotate);
        ring.RenderTransform = group;
        ring.RenderTransformOrigin = new Point(0.5, 0.5);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Animate(0.55, 1, delay, 0.65));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Animate(0.55, 1, delay, 0.65));
        rotate.BeginAnimation(RotateTransform.AngleProperty, Animate(0, turn, delay, Length.TotalSeconds - delay, ease: false));
        return ring;
    }

    private static T Centred<T>(T shape, double cx, double cy) where T : FrameworkElement
    {
        Canvas.SetLeft(shape, cx - shape.Width / 2);
        Canvas.SetTop(shape, cy - shape.Height / 2);
        shape.RenderTransformOrigin = new Point(0.5, 0.5);
        return shape;
    }

    /// <summary>Centres a text on a point, once it knows its own size.</summary>
    private static void Place(TextBlock text, double cx, double cy)
    {
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(text, cx - text.DesiredSize.Width / 2);
        Canvas.SetTop(text, cy - text.DesiredSize.Height / 2);
    }

    private static void Fade(UIElement element, double from, double to, double delay, double seconds)
    {
        element.Opacity = from;
        element.BeginAnimation(OpacityProperty, Animate(from, to, delay, seconds));
    }

    private static void Scale(UIElement element, double from, double to, double delay, double seconds)
    {
        var scale = new ScaleTransform(from, from);
        element.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Animate(from, to, delay, seconds));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Animate(from, to, delay, seconds));
    }

    private static DoubleAnimation Animate(double from, double to, double delay, double seconds, bool ease = true) => new(from, to, TimeSpan.FromSeconds(seconds))
    {
        BeginTime = TimeSpan.FromSeconds(delay),
        FillBehavior = FillBehavior.HoldEnd,
        EasingFunction = ease ? new CubicEase { EasingMode = EasingMode.EaseOut } : null,
    };
}
