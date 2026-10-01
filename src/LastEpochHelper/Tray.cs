using System.IO;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace LastEpochHelper;

/// <summary>Notification-area icon: the way back to the overlay when it is hidden or locked.</summary>
internal sealed class Tray : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _image;

    public Tray(Action toggleVisible, Action toggleLock, Action toggleCompact, Action settings, Action quit)
    {
        _image = CreateIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show / hide", null, (_, _) => toggleVisible());
        menu.Items.Add("Lock / unlock", null, (_, _) => toggleLock());
        menu.Items.Add("Compact mode", null, (_, _) => toggleCompact());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => settings());
        menu.Items.Add("Quit", null, (_, _) => quit());

        _icon = new Forms.NotifyIcon { Icon = _image, Text = "Last Epoch Helper", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) toggleVisible(); };
    }

    private static Drawing.Icon CreateIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x0E, 0x0F, 0x14));
            using var gold = new Drawing.SolidBrush(Drawing.Color.FromArgb(0xC9, 0xA8, 0x5C));
            using var border = new Drawing.Pen(Drawing.Color.FromArgb(0xC9, 0xA8, 0x5C), 2);
            g.FillRectangle(background, 0, 0, 32, 32);
            g.DrawRectangle(border, 1, 1, 29, 29);
            using var font = new Drawing.Font("Segoe UI", 13, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            var format = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center };
            g.DrawString("LE", font, gold, new Drawing.RectangleF(0, 0, 32, 32), format);
        }
        IntPtr handle = bitmap.GetHicon();
        try { return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone(); }
        finally { Native.DestroyIcon(handle); }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}

/// <summary>Saves what the game is showing, used to build a personal library of zone maps.</summary>
internal static class ScreenCapture
{
    private const int MaxWidth = 1600;

    public static bool Save(Native.RECT bounds, string path)
    {
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (width < 200 || height < 200) return false;
        try
        {
            using var shot = new Drawing.Bitmap(width, height);
            using (var g = Drawing.Graphics.FromImage(shot))
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, shot.Size);

            // Full-resolution screenshots of an ultrawide are needlessly large for a reference picture.
            if (width > MaxWidth)
            {
                int scaledHeight = height * MaxWidth / width;
                using var scaled = new Drawing.Bitmap(MaxWidth, scaledHeight);
                using (var g = Drawing.Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(shot, 0, 0, MaxWidth, scaledHeight);
                }
                scaled.Save(path, Drawing.Imaging.ImageFormat.Png);
            }
            else shot.Save(path, Drawing.Imaging.ImageFormat.Png);
            return true;
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }
}
