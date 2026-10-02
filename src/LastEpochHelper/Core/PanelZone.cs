namespace LastEpochHelper.Core;

/// <summary>
/// Where the game prints what the overlay reads to follow its panels: the headings in the upper left
/// part of the passive and skill panel ("PASSIVES" and the tab's title, a skill's name with its level
/// and "Minimum Specialized Level"). The overlay blacks its own windows out of every picture of the
/// screen, so a window of its own lying over this spot hides exactly the text it needs.
/// </summary>
public static class PanelZone
{
    /// <summary>
    /// The heading area for a game window with these bounds (screen pixels). The panel is centred and
    /// its layout scales with the window's height, so the area is given in parts of the height.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) Headings(int left, int top, int right, int bottom)
    {
        int height = bottom - top, centre = (left + right) / 2;
        return (Math.Max(left, centre - (int)(height * 0.80)), top + (int)(height * 0.04),
            Math.Min(right, centre + (int)(height * 0.40)), top + (int)(height * 0.21));
    }

    /// <summary>How much of the heading area a window covers, in pixels (0 = clear of it).</summary>
    public static long Covered((int Left, int Top, int Right, int Bottom) zone, int left, int top, int right, int bottom)
    {
        long width = Math.Max(0, Math.Min(right, zone.Right) - Math.Max(left, zone.Left));
        long height = Math.Max(0, Math.Min(bottom, zone.Bottom) - Math.Max(top, zone.Top));
        return width * height;
    }

    /// <summary>Does the window hide enough of the headings to matter? A corner just touching the area does not.</summary>
    public static bool Hides((int Left, int Top, int Right, int Bottom) zone, int left, int top, int right, int bottom) =>
        Covered(zone, left, top, right, bottom) * 20 >= (long)(zone.Right - zone.Left) * (zone.Bottom - zone.Top);
}
