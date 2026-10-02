using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace LastEpochHelper;

/// <summary>Large, click-through view of the current zone's map picture, centred on the primary screen.</summary>
internal sealed class MapWindow : Window
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform };

    public MapWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0.92;
        Content = new Border
        {
            BorderBrush = Theme.Border,
            BorderThickness = Theme.Edge,
            Child = _image,
        };
        SourceInitialized += (_, _) => Native.ApplyOverlayStyle(new WindowInteropHelper(this).Handle, clickThrough: true);
    }

    public void Display(ImageSource? source)
    {
        if (source is null) { Hide(); return; }
        _image.Source = source;
        double screenWidth = SystemParameters.PrimaryScreenWidth, screenHeight = SystemParameters.PrimaryScreenHeight;
        double scale = Math.Min(screenWidth * 0.5 / source.Width, screenHeight * 0.7 / source.Height);
        Width = source.Width * scale;
        Height = source.Height * scale;
        Left = (screenWidth - Width) / 2;
        Top = (screenHeight - Height) / 2;
        if (!IsVisible) Show();
    }
}
