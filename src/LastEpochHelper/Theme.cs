using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace LastEpochHelper;

/// <summary>
/// The look the overlay's windows share, after Last Epoch's own panels: a dark plate that is a shade
/// lighter at the top, a bronze edge that catches light at the top and falls away below, rules that
/// fade out towards the sides, and a serif face for titles. Nothing here grows a window beyond its
/// border - the overlay blacks its windows out of what it reads from the screen, so no shadows.
/// </summary>
internal static class Theme
{
    public const double Radius = 9;
    public static readonly CornerRadius Corners = new(Radius);
    public static readonly Thickness Edge = new(1.5);

    /// <summary>Titles: the zone, the chapter. Falls back through faces every Windows has.</summary>
    public static readonly FontFamily TitleFont = new("Palatino Linotype, Cambria, Georgia, Segoe UI");

    public static readonly Brush Gold = Solid("#C9A85C");
    public static readonly Brush GoldTint = Solid("#33C9A85C");
    /// <summary>The bronze edge of a window.</summary>
    public static readonly Brush Border = Gradient(vertical: true, ("#A88B4C", 0), ("#5F4F2C", 0.35), ("#3A301A", 1));
    /// <summary>A rule between sections: gold in the middle, gone at the ends.</summary>
    public static readonly Brush Rule = Gradient(vertical: false, ("#00C9A85C", 0), ("#66C9A85C", 0.18), ("#66C9A85C", 0.82), ("#00C9A85C", 1));
    /// <summary>The plate of windows that are not see-through by setting.</summary>
    public static readonly Brush PlateBrush = Plate(0xF2);

    /// <summary>The dark plate behind everything, at this much opacity.</summary>
    public static Brush Plate(byte alpha)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(alpha, 0x17, 0x18, 0x21), 0),
                new GradientStop(Color.FromArgb(alpha, 0x0E, 0x0F, 0x14), 0.3),
                new GradientStop(Color.FromArgb(alpha, 0x0A, 0x0B, 0x0F), 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    private static Brush Solid(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static Brush Gradient(bool vertical, params (string Hex, double At)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = vertical ? new Point(0, 1) : new Point(1, 0) };
        foreach (var (hex, at) in stops) brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hex), at));
        brush.Freeze();
        return brush;
    }

    // ------------------------------------------------------------------ menus

    private static ResourceDictionary? _menu;

    /// <summary>Gives a menu the overlay's look instead of Windows' white one.</summary>
    public static ContextMenu Styled(ContextMenu menu)
    {
        _menu ??= (ResourceDictionary)XamlReader.Parse(MenuXaml);
        menu.Resources = _menu;
        menu.Style = (Style)_menu[typeof(ContextMenu)];
        return menu;
    }

    // ------------------------------------------------------------------ sliders and scroll bars

    private static ResourceDictionary? _controls;

    /// <summary>Makes the sliders and scroll bars inside a dark window dark too, instead of Windows' light ones.</summary>
    public static void Controls(FrameworkElement root)
    {
        _controls ??= (ResourceDictionary)XamlReader.Parse(ControlsXaml);
        root.Resources.MergedDictionaries.Add(_controls);
    }

    private const string ControlsXaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <Style TargetType="Slider">
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="Slider">
                            <Grid Height="20" Background="Transparent">
                                <Border Height="4" CornerRadius="2" Background="#2EFFFFFF" VerticalAlignment="Center" Margin="2,0,2,0"/>
                                <Track x:Name="PART_Track">
                                    <Track.DecreaseRepeatButton>
                                        <RepeatButton Command="Slider.DecreaseLarge" Focusable="False">
                                            <RepeatButton.Template>
                                                <ControlTemplate TargetType="RepeatButton">
                                                    <Grid Background="Transparent">
                                                        <Border Height="4" CornerRadius="2" Background="#B8964E" VerticalAlignment="Center" Margin="2,0,-2,0"/>
                                                    </Grid>
                                                </ControlTemplate>
                                            </RepeatButton.Template>
                                        </RepeatButton>
                                    </Track.DecreaseRepeatButton>
                                    <Track.IncreaseRepeatButton>
                                        <RepeatButton Command="Slider.IncreaseLarge" Focusable="False">
                                            <RepeatButton.Template>
                                                <ControlTemplate TargetType="RepeatButton">
                                                    <Grid Background="Transparent"/>
                                                </ControlTemplate>
                                            </RepeatButton.Template>
                                        </RepeatButton>
                                    </Track.IncreaseRepeatButton>
                                    <Track.Thumb>
                                        <Thumb Focusable="False" Cursor="Hand">
                                            <Thumb.Template>
                                                <ControlTemplate TargetType="Thumb">
                                                    <Ellipse Width="15" Height="15" Fill="#E6CC8C" Stroke="#2A2212" StrokeThickness="2"/>
                                                </ControlTemplate>
                                            </Thumb.Template>
                                        </Thumb>
                                    </Track.Thumb>
                                </Track>
                            </Grid>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>

            <Style TargetType="ScrollBar">
                <Setter Property="Width" Value="8"/>
                <Setter Property="MinWidth" Value="8"/>
                <Setter Property="Margin" Value="4,0,0,0"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ScrollBar">
                            <Grid Background="Transparent">
                                <Border CornerRadius="4" Background="#14FFFFFF"/>
                                <Track x:Name="PART_Track" IsDirectionReversed="True">
                                    <Track.DecreaseRepeatButton>
                                        <RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/>
                                    </Track.DecreaseRepeatButton>
                                    <Track.IncreaseRepeatButton>
                                        <RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/>
                                    </Track.IncreaseRepeatButton>
                                    <Track.Thumb>
                                        <Thumb Focusable="False">
                                            <Thumb.Template>
                                                <ControlTemplate TargetType="Thumb">
                                                    <Border CornerRadius="4" Background="#80C9A85C"/>
                                                </ControlTemplate>
                                            </Thumb.Template>
                                        </Thumb>
                                    </Track.Thumb>
                                </Track>
                            </Grid>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
        </ResourceDictionary>
        """;

    private const string MenuXaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <LinearGradientBrush x:Key="MenuPlate" StartPoint="0,0" EndPoint="0,1">
                <GradientStop Color="#F717181F" Offset="0"/>
                <GradientStop Color="#F70C0D12" Offset="1"/>
            </LinearGradientBrush>
            <LinearGradientBrush x:Key="MenuEdge" StartPoint="0,0" EndPoint="0,1">
                <GradientStop Color="#A88B4C" Offset="0"/>
                <GradientStop Color="#4A3D22" Offset="1"/>
            </LinearGradientBrush>
            <LinearGradientBrush x:Key="MenuRule" StartPoint="0,0" EndPoint="1,0">
                <GradientStop Color="#00C9A85C" Offset="0"/>
                <GradientStop Color="#66C9A85C" Offset="0.2"/>
                <GradientStop Color="#66C9A85C" Offset="0.8"/>
                <GradientStop Color="#00C9A85C" Offset="1"/>
            </LinearGradientBrush>

            <Style TargetType="ContextMenu">
                <Setter Property="Foreground" Value="#E8E4D8"/>
                <Setter Property="FontFamily" Value="Segoe UI"/>
                <Setter Property="FontSize" Value="13"/>
                <Setter Property="HasDropShadow" Value="False"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ContextMenu">
                            <Border Background="{StaticResource MenuPlate}" BorderBrush="{StaticResource MenuEdge}" BorderThickness="1.5" CornerRadius="8" Padding="4">
                                <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
                            </Border>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>

            <Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator">
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="Separator">
                            <Rectangle Height="1" Margin="6,4,6,4" Fill="{StaticResource MenuRule}"/>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>

            <Style TargetType="MenuItem">
                <Setter Property="Foreground" Value="#E8E4D8"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="MenuItem">
                            <Border x:Name="Row" Background="Transparent" CornerRadius="5" Padding="6,4,10,5">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="20"/>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBlock x:Name="Check" Text="✓" Foreground="#C9A85C" FontWeight="Bold" VerticalAlignment="Center" Visibility="Collapsed"/>
                                    <ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center"/>
                                    <TextBlock x:Name="Arrow" Grid.Column="2" Text="›" FontSize="15" Margin="14,-3,0,0" Foreground="#A9A493" VerticalAlignment="Center" Visibility="Collapsed"/>
                                    <Popup x:Name="PART_Popup" Placement="Right" HorizontalOffset="6" VerticalOffset="-6" AllowsTransparency="True" Focusable="False"
                                           PopupAnimation="Fade" IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}">
                                        <Border Background="{StaticResource MenuPlate}" BorderBrush="{StaticResource MenuEdge}" BorderThickness="1.5" CornerRadius="8" Padding="4">
                                            <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
                                        </Border>
                                    </Popup>
                                </Grid>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsHighlighted" Value="True">
                                    <Setter TargetName="Row" Property="Background" Value="#33C9A85C"/>
                                    <Setter Property="Foreground" Value="White"/>
                                </Trigger>
                                <Trigger Property="IsChecked" Value="True">
                                    <Setter TargetName="Check" Property="Visibility" Value="Visible"/>
                                </Trigger>
                                <Trigger Property="HasItems" Value="True">
                                    <Setter TargetName="Arrow" Property="Visibility" Value="Visible"/>
                                </Trigger>
                                <Trigger Property="IsEnabled" Value="False">
                                    <Setter Property="Foreground" Value="#77736A"/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
        </ResourceDictionary>
        """;
}
