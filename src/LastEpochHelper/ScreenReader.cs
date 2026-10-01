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
            _engine = OcrEngine.TryCreateFromUserProfileLanguages()
                      ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
        }
        catch (Exception) { _engine = null; } // no OCR language pack installed
    }

    public bool Available => _engine is not null;

    /// <summary>Reads a saved picture instead of the screen; positions are pixels in the picture.</summary>
    public async Task<List<ScreenLine>> ReadFileAsync(string path, bool words = false)
    {
        if (_engine is null) return new List<ScreenLine>();
        using var image = new Drawing.Bitmap(path);
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
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0) continue;
            if (words || _keepWords is not null)
            {
                foreach (var word in line.Words)
                    (words ? lines : _keepWords!).Add(new ScreenLine(word.Text, word.BoundingRect.Height / scale,
                        offsetX + word.BoundingRect.Left / scale, offsetY + word.BoundingRect.Top / scale, word.BoundingRect.Width / scale));
                if (words) continue;
            }
            // Back to screen coordinates, whatever the capture was scaled by.
            double left = line.Words.Min(w => w.BoundingRect.Left), right = line.Words.Max(w => w.BoundingRect.Right);
            double top = line.Words.Min(w => w.BoundingRect.Top);
            lines.Add(new ScreenLine(line.Text, line.Words.Max(w => w.BoundingRect.Height) / scale,
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
                    foreach (var mask in masks)
                        g.FillRectangle(Drawing.Brushes.Black, mask.Left - bounds.Left, mask.Top - bounds.Top, mask.Right - mask.Left, mask.Bottom - mask.Top);
                }
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
