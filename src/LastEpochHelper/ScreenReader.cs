using System.IO;
using LastEpochHelper.Core;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Drawing = System.Drawing;

namespace LastEpochHelper;

/// <summary>
/// Reads the text the game is showing, using Windows' built-in OCR. This is how the tree view
/// knows which skill tree is open: the game log says nothing about its panels.
/// </summary>
internal sealed class ScreenReader
{
    private readonly OcrEngine? _engine;

    public ScreenReader()
    {
        try
        {
            // The game's text is English, so English recognition comes first, whatever language
            // Windows itself is in; a Chinese or Russian recognizer reads English text badly.
            var languages = OcrEngine.AvailableRecognizerLanguages;
            var english = languages.FirstOrDefault(l => l.LanguageTag.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                          ?? languages.FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            _engine = (english is null ? null : OcrEngine.TryCreateFromLanguage(english)) ?? OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch (Exception) { _engine = null; } // no OCR language pack installed
    }

    public bool Available => _engine is not null;

    /// <summary>The language the text is read in ("en-US"), or null when Windows has no text recognition at all.</summary>
    public string? Language => _engine?.RecognizerLanguage.LanguageTag;

    /// <summary>Reading in English, as the game's text needs.</summary>
    public bool English => Language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Reads small boxed labels (the "2/4" under skill nodes), which the plain read mostly misses:
    /// once enlarged, and once enlarged as black-on-white. Positions are screen pixels.
    /// </summary>
    public async Task<List<ScreenLine>[]> ReadLabelsAsync(Native.RECT bounds, IReadOnlyList<Native.RECT> masks) =>
        (await ReadLabelsAndPictureAsync(bounds, masks)).Reads;

    /// <summary>The same reads, and the picture they were made from (masks blacked out), for a closer look at single labels.</summary>
    /// <param name="single">Only the high-contrast read: about 40% quicker, for when the picture fills in the labels missed.</param>
    public async Task<(List<ScreenLine>[] Reads, ScreenPicture? Picture)> ReadLabelsAndPictureAsync(Native.RECT bounds, IReadOnlyList<Native.RECT> masks, bool single = false)
    {
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (_engine is null || width < 200 || height < 200) return (Array.Empty<List<ScreenLine>>(), null);
        try
        {
            using var shot = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Drawing.Graphics.FromImage(shot))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, shot.Size);
            }
            Levels.Restore(shot);
            using (var g = Drawing.Graphics.FromImage(shot))
                foreach (var mask in masks)
                    g.FillRectangle(Drawing.Brushes.Black, mask.Left - bounds.Left, mask.Top - bounds.Top, mask.Right - mask.Left, mask.Bottom - mask.Top);
            return (await ReadLabelsAsync(shot, bounds.Left, bounds.Top, single), Picture(shot, bounds.Left, bounds.Top));
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception
                                      or System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        {
            return (Array.Empty<List<ScreenLine>>(), null);
        }
    }

    /// <summary>A 24-bit picture's pixels, copied out.</summary>
    private static ScreenPicture Picture(Drawing.Bitmap shot, double left, double top)
    {
        var area = new Drawing.Rectangle(0, 0, shot.Width, shot.Height);
        var data = shot.LockBits(area, Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format24bppRgb);
        try
        {
            var pixels = new byte[data.Stride * shot.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new ScreenPicture(shot.Width, shot.Height, data.Stride, pixels, left, top);
        }
        finally { shot.UnlockBits(data); }
    }

    /// <summary>The same two reads on a saved picture, for testing against real screenshots.</summary>
    public async Task<List<ScreenLine>[]> ReadLabelsFromFileAsync(string path, Drawing.Rectangle region) =>
        (await ReadLabelsAndPictureFromFileAsync(path, region)).Reads;

    public async Task<(List<ScreenLine>[] Reads, ScreenPicture? Picture)> ReadLabelsAndPictureFromFileAsync(string path, Drawing.Rectangle region, bool single = false)
    {
        if (_engine is null) return (Array.Empty<List<ScreenLine>>(), null);
        using var image = new Drawing.Bitmap(path);
        using var crop = image.Clone(region, Drawing.Imaging.PixelFormat.Format24bppRgb);
        return (await ReadLabelsAsync(crop, region.Left, region.Top, single), Picture(crop, region.Left, region.Top));
    }

    private async Task<List<ScreenLine>[]> ReadLabelsAsync(Drawing.Bitmap shot, double offsetX, double offsetY, bool single = false)
    {
        int limit = (int)OcrEngine.MaxImageDimension;
        double Factor(double wanted) => Math.Max(1, Math.Min(wanted, Math.Min((double)limit / shot.Width, (double)limit / shot.Height)));

        // White text on a dark plate -> black on white, the form the reader copes with best.
        using var contrast = await Task.Run(() => Threshold(shot, 190));
        var reads = new List<List<ScreenLine>>();
        // The high-contrast read alone catches nearly as many as both (measured on Weaver panels); the
        // plain one adds a few more labels on a skill tree, where every label has to come from the reader.
        var passes = single ? new[] { (contrast, Factor(3)) } : new[] { (contrast, Factor(3)), (shot, Factor(2)) };
        foreach (var (image, factor) in passes)
        {
            // Enlarging a picture this size takes a while: keep it off the thread that draws the overlay.
            using var stream = await Task.Run(() =>
            {
                using var large = new Drawing.Bitmap((int)(image.Width * factor), (int)(image.Height * factor));
                using (var g = Drawing.Graphics.FromImage(large))
                {
                    g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(image, 0, 0, large.Width, large.Height);
                }
                var memory = new MemoryStream();
                large.Save(memory, Drawing.Imaging.ImageFormat.Bmp);
                memory.Position = 0;
                return memory;
            });
            reads.Add(await RecognizeAsync(stream, offsetX, offsetY, factor, words: true));
        }
        return reads.ToArray();
    }

    private static Drawing.Bitmap Threshold(Drawing.Bitmap source, int cut)
    {
        var result = new Drawing.Bitmap(source.Width, source.Height, Drawing.Imaging.PixelFormat.Format24bppRgb);
        var area = new Drawing.Rectangle(0, 0, source.Width, source.Height);
        var from = source.LockBits(area, Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format24bppRgb);
        var to = result.LockBits(area, Drawing.Imaging.ImageLockMode.WriteOnly, Drawing.Imaging.PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[from.Stride];
            for (int y = 0; y < source.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(from.Scan0 + y * from.Stride, row, 0, row.Length);
                for (int x = 0; x < source.Width; x++)
                {
                    int i = x * 3;
                    int light = (row[i] * 11 + row[i + 1] * 59 + row[i + 2] * 30) / 100; // blue, green, red
                    row[i] = row[i + 1] = row[i + 2] = (byte)(light > cut ? 0 : 255);
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, to.Scan0 + y * to.Stride, row.Length);
            }
        }
        finally
        {
            source.UnlockBits(from);
            result.UnlockBits(to);
        }
        return result;
    }

    /// <summary>Reads a saved picture instead of the screen; positions are pixels in the picture.</summary>
    /// <param name="restore">Put the contrast back first if the picture is washed out, as a read of the screen does.</param>
    public async Task<List<ScreenLine>> ReadFileAsync(string path, bool words = false, bool restore = true)
    {
        if (_engine is null) return new List<ScreenLine>();
        using var loaded = new Drawing.Bitmap(path);
        using var image = loaded.Clone(new Drawing.Rectangle(0, 0, loaded.Width, loaded.Height), Drawing.Imaging.PixelFormat.Format32bppArgb);
        if (restore) Levels.Restore(image);
        using var stream = new MemoryStream();
        double scale = 1;
        int limit = (int)OcrEngine.MaxImageDimension;
        if (image.Width > limit || image.Height > limit)
        {
            scale = Math.Min((double)limit / image.Width, (double)limit / image.Height);
            using var small = new Drawing.Bitmap(image, (int)(image.Width * scale), (int)(image.Height * scale));
            small.Save(stream, Drawing.Imaging.ImageFormat.Bmp);
        }
        else image.Save(stream, Drawing.Imaging.ImageFormat.Bmp);
        stream.Position = 0;
        return await RecognizeAsync(stream, 0, 0, scale, words);
    }

    private async Task<List<ScreenLine>> RecognizeAsync(Stream stream, double offsetX, double offsetY, double scale, bool words)
    {
        var lines = new List<ScreenLine>();
        var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await _engine!.RecognizeAsync(bitmap);
        // When the reader takes the text for tilted, it reports positions in the straightened picture,
        // turned about its centre; turn them back, or labels far from the centre land tens of pixels off.
        double angle = (result.TextAngle ?? 0) * Math.PI / 180, cos = Math.Cos(angle), sin = Math.Sin(angle);
        double centreX = bitmap.PixelWidth / 2.0, centreY = bitmap.PixelHeight / 2.0;
        Windows.Foundation.Rect Upright(Windows.Foundation.Rect r)
        {
            if (angle == 0) return r;
            double x = r.X + r.Width / 2 - centreX, y = r.Y + r.Height / 2 - centreY;
            return new Windows.Foundation.Rect(centreX + x * cos - y * sin - r.Width / 2, centreY + x * sin + y * cos - r.Height / 2, r.Width, r.Height);
        }
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0) continue;
            var rects = line.Words.Select(w => Upright(w.BoundingRect)).ToList();
            if (words || _keepWords is not null)
            {
                for (int i = 0; i < rects.Count; i++)
                    (words ? lines : _keepWords!).Add(new ScreenLine(line.Words[i].Text, rects[i].Height / scale,
                        offsetX + rects[i].Left / scale, offsetY + rects[i].Top / scale, rects[i].Width / scale));
                if (words) continue;
            }
            // Back to screen coordinates, whatever the capture was scaled by.
            double left = rects.Min(r => r.Left), right = rects.Max(r => r.Right);
            double top = rects.Min(r => r.Top);
            lines.Add(new ScreenLine(line.Text, rects.Max(r => r.Height) / scale,
                offsetX + left / scale, offsetY + top / scale, (right - left) / scale));
        }
        return lines;
    }

    /// <param name="bounds">Screen rectangle to read, in physical pixels.</param>
    /// <param name="masks">Rectangles to blank out first (our own windows, which list every skill name).</param>
    /// <summary>One read giving both the lines (for headings and names) and the single words (for node labels).</summary>
    public async Task<(List<ScreenLine> Lines, List<ScreenLine> Words)> ReadBothAsync(Native.RECT bounds, IReadOnlyList<Native.RECT> masks)
    {
        _keepWords = new List<ScreenLine>();
        try { return (await ReadAsync(bounds, masks), _keepWords); }
        finally { _keepWords = null; }
    }
    private List<ScreenLine>? _keepWords;

    /// <param name="words">Report every word on its own (with its own position) instead of whole lines.</param>
    public async Task<List<ScreenLine>> ReadAsync(Native.RECT bounds, IReadOnlyList<Native.RECT> masks, bool words = false)
    {
        var lines = new List<ScreenLine>();
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (_engine is null || width < 200 || height < 200) return lines;

        double scale = 1;
        try
        {
            using var stream = new MemoryStream();
            using (var shot = new Drawing.Bitmap(width, height))
            {
                using (var g = Drawing.Graphics.FromImage(shot))
                {
                    g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, shot.Size);
                }
                Levels.Restore(shot);
                using (var g = Drawing.Graphics.FromImage(shot))
                    foreach (var mask in masks)
                        g.FillRectangle(Drawing.Brushes.Black, mask.Left - bounds.Left, mask.Top - bounds.Top, mask.Right - mask.Left, mask.Bottom - mask.Top);
                // The engine refuses images beyond its limit; a 2x smaller ultrawide still has readable headings.
                int limit = (int)OcrEngine.MaxImageDimension;
                if (width > limit || height > limit)
                {
                    scale = Math.Min((double)limit / width, (double)limit / height);
                    using var small = new Drawing.Bitmap(shot, (int)(width * scale), (int)(height * scale));
                    small.Save(stream, Drawing.Imaging.ImageFormat.Bmp);
                }
                else shot.Save(stream, Drawing.Imaging.ImageFormat.Bmp);
            }
            stream.Position = 0;
            lines = await RecognizeAsync(stream, bounds.Left, bounds.Top, scale, words);
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception
                                      or System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        {
            // A failed read just means no tab switch this time.
        }
        return lines;
    }
}

/// <summary>
/// Puts the contrast back into a picture of the screen that came out washed. With HDR switched on,
/// Windows hands programs a copy of the screen in which black is grey and everything is squeezed
/// into a narrow band of brightness; small print disappears in it. Stretching that band back out to
/// black-to-white makes the text readable again. A normal picture already spans the range and is left alone.
/// </summary>
internal static class Levels
{
    /// <summary>What the last look found: darkest and brightest level in use, and whether it was stretched. For the logs.</summary>
    public static (int Low, int High, bool Stretched) Last { get; private set; }

    public static void Restore(Drawing.Bitmap picture)
    {
        var area = new Drawing.Rectangle(0, 0, picture.Width, picture.Height);
        var data = picture.LockBits(area, Drawing.Imaging.ImageLockMode.ReadWrite, Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(data.Stride) * picture.Height;
            var pixels = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, bytes);

            // Brightness of every 16th pixel is plenty to see how much of the range is in use.
            var histogram = new int[256];
            int samples = 0;
            for (int i = 0; i + 2 < bytes; i += 64)
            {
                histogram[(pixels[i] * 11 + pixels[i + 1] * 59 + pixels[i + 2] * 30) / 100]++;
                samples++;
            }
            if (samples == 0) return;
            int low = Percentile(histogram, samples, 0.01), high = Percentile(histogram, samples, 0.995);
            // A game screen has true black and bright text somewhere. If it does not, the copy is washed out.
            bool washed = low > 24 || (high < 190 && high - low > 20);
            Last = (low, high, washed);
            if (!washed || high - low < 20) return;

            var table = new byte[256];
            for (int v = 0; v < 256; v++) table[v] = (byte)Math.Clamp((v - low) * 255 / (high - low), 0, 255);
            for (int i = 0; i + 2 < bytes; i += 4)
            {
                pixels[i] = table[pixels[i]];
                pixels[i + 1] = table[pixels[i + 1]];
                pixels[i + 2] = table[pixels[i + 2]];
            }
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, bytes);
        }
        finally { picture.UnlockBits(data); }
    }

    private static int Percentile(int[] histogram, int samples, double share)
    {
        int wanted = (int)(samples * share), seen = 0;
        for (int v = 0; v < 256; v++)
        {
            seen += histogram[v];
            if (seen > wanted) return v;
        }
        return 255;
    }
}
