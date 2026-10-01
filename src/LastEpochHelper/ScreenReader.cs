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

    /// <param name="bounds">Screen rectangle to read, in physical pixels.</param>
    /// <param name="masks">Rectangles to blank out first (our own windows, which list every skill name).</param>
    public async Task<List<ScreenLine>> ReadAsync(Native.RECT bounds, IReadOnlyList<Native.RECT> masks)
    {
        var lines = new List<ScreenLine>();
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (_engine is null || width < 200 || height < 200) return lines;

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
                    double scale = Math.Min((double)limit / width, (double)limit / height);
                    using var small = new Drawing.Bitmap(shot, (int)(width * scale), (int)(height * scale));
                    small.Save(stream, Drawing.Imaging.ImageFormat.Bmp);
                }
                else shot.Save(stream, Drawing.Imaging.ImageFormat.Bmp);
            }
            stream.Position = 0;

            var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var result = await _engine.RecognizeAsync(bitmap);
            foreach (var line in result.Lines)
                lines.Add(new ScreenLine(line.Text, line.Words.Count > 0 ? line.Words.Max(w => w.BoundingRect.Height) : 0));
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception
                                      or System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        {
            // A failed read just means no tab switch this time.
        }
        return lines;
    }
}
