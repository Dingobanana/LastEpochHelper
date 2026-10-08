namespace LastEpochHelper.Core;

/// <summary>A picture of the screen as plain pixels, three bytes each (blue, green, red), and where it lies on screen.</summary>
public sealed class ScreenPicture(int width, int height, int stride, byte[] pixels, double left, double top)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public double Left { get; } = left;
    public double Top { get; } = top;

    /// <summary>
    /// White print: bright in all three colours. The gold frame around a label and the lit paths between
    /// nodes are bright too, but short of blue.
    /// </summary>
    public bool White(int x, int y)
    {
        int i = y * stride + x * 3;
        return pixels[i] > 150 && pixels[i + 1] > 150 && pixels[i + 2] > 150;
    }
}

/// <summary>
/// Reads the "have" digit of node labels ("1/3") that the text reader missed. Windows' reader takes
/// many of the Weaver tree's small labels for the word "on" or skips them, though the print is sharp.
/// Once the labels it did read have placed the tree, every node's label is known to sit at one spot,
/// with the node's maximum after the slash. So the digits are learnt from the picture itself - each
/// maximum digit shows what that digit looks like in this font at this size - and the first digit of
/// every other label is compared with them.
/// </summary>
public static class LabelGlyphs
{
    /// <summary>Where a node's label should be on screen (its centre), and the node's maximum.</summary>
    public sealed record Spot(int Node, int Max, double X, double Y);

    private const int Rows = 9, Columns = 6;
    // Measured on the game's Weaver labels: a digit compared with its own kind scores up to about 7,
    // with any other digit 13 or more.
    private const double Close = 9, Margin = 4;

    private sealed record Glyph(double[] Shape);

    /// <summary>The points of the nodes whose first digit could be told, by node id.</summary>
    /// <param name="known">Points the text reader did read, by node id: their first digits are learnt too.</param>
    /// <param name="textHeight">Height of the labels' print on screen, as the text reader measured it.</param>
    public static Dictionary<int, int> Read(ScreenPicture picture, IReadOnlyList<Spot> spots, IReadOnlyDictionary<int, int> known, double textHeight)
    {
        var read = new Dictionary<int, int>();
        if (textHeight < 6) return read;
        var labels = new List<(Spot Spot, List<Glyph> Glyphs)>();
        foreach (var spot in spots)
            if (Glyphs(picture, spot, textHeight) is { } glyphs) labels.Add((spot, glyphs));

        var samples = new List<(int Digit, double[] Shape)>();
        foreach (var (spot, glyphs) in labels)
        {
            // "0", "/", "3": the last one is the node's maximum.
            if (glyphs.Count == 3 && spot.Max <= 9) samples.Add((spot.Max, glyphs[2].Shape));
            if (known.TryGetValue(spot.Node, out int have) && have <= 9) samples.Add((have, glyphs[0].Shape));
        }
        if (samples.Count == 0) return read;

        foreach (var (spot, glyphs) in labels)
        {
            var nearest = samples.GroupBy(s => s.Digit)
                .Select(g => (Digit: g.Key, Distance: g.Min(s => Distance(s.Shape, glyphs[0].Shape))))
                .OrderBy(d => d.Distance).ToList();
            if (nearest[0].Distance > Close || nearest.Count > 1 && nearest[1].Distance - nearest[0].Distance < Margin) continue;
            if (nearest[0].Digit > spot.Max) continue;
            read[spot.Node] = nearest[0].Digit;
        }
        return read;
    }

    /// <summary>
    /// The separate shapes of white print in the label's plate, left to right: "0", "/", "3" - or two,
    /// when the slash runs into the digit after it. Null when this is not a one-digit label.
    /// </summary>
    private static List<Glyph>? Glyphs(ScreenPicture picture, Spot spot, double height)
    {
        int centreX = (int)Math.Round(spot.X - picture.Left), centreY = (int)Math.Round(spot.Y - picture.Top);
        // The plate around the print, and not the node's icon just above it.
        int halfWidth = (int)Math.Ceiling(height * 1.6), halfHeight = (int)Math.Ceiling(height * 0.65);
        int left = centreX - halfWidth, right = centreX + halfWidth, top = centreY - halfHeight, bottom = centreY + halfHeight;
        if (left < 0 || top < 0 || right >= picture.Width || bottom >= picture.Height) return null;

        var runs = new List<(int From, int To)>();
        int? start = null;
        for (int x = left; x <= right + 1; x++)
        {
            bool ink = false;
            if (x <= right)
                for (int y = top; y <= bottom && !ink; y++) ink = picture.White(x, y);
            if (ink) start ??= x;
            else if (start is { } from) { runs.Add((from, x)); start = null; }
        }
        int thinnest = Math.Max(2, (int)(height * 0.2));
        runs.RemoveAll(r => r.To - r.From < thinnest);
        if (runs.Count is < 2 or > 3) return null;
        // A first shape wider than a digit is two run together; a label is a few digits wide.
        if (runs[0].To - runs[0].From > height * 0.95) return null;
        double span = runs[^1].To - runs[0].From;
        if (span < height * 1.3 || span > height * 3.2) return null;

        var glyphs = new List<Glyph>();
        foreach (var (from, to) in runs)
        {
            if (Shape(picture, from, to, top, bottom, height) is not { } shape) return null;
            glyphs.Add(new Glyph(shape));
        }
        return glyphs;
    }

    /// <summary>The shape squeezed onto a small grid (how much of each cell is white), plus how wide it is for its height.</summary>
    private static double[]? Shape(ScreenPicture picture, int from, int to, int top, int bottom, double height)
    {
        int first = -1, last = -1;
        for (int y = top; y <= bottom; y++)
            for (int x = from; x < to; x++)
                if (picture.White(x, y)) { if (first < 0) first = y; last = y; break; }
        if (first < 0 || last - first + 1 < height * 0.5) return null;
        int rows = last - first + 1, columns = to - from;
        var shape = new double[Rows * Columns + 1];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Columns; j++)
            {
                int y0 = first + i * rows / Rows, y1 = Math.Max(y0 + 1, first + (i + 1) * rows / Rows);
                int x0 = from + j * columns / Columns, x1 = Math.Max(x0 + 1, from + (j + 1) * columns / Columns);
                int white = 0, all = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++, all++)
                        if (picture.White(x, y)) white++;
                shape[i * Columns + j] = (double)white / all;
            }
        // A "1" is narrow; stretched onto the grid it would look like a block.
        shape[^1] = 4.0 * columns / rows;
        return shape;
    }

    private static double Distance(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++) sum += (a[i] - b[i]) * (a[i] - b[i]);
        return sum;
    }
}
