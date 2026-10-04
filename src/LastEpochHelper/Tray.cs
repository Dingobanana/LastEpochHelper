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

    /// <summary>The program's icon (app.ico, see tools/make_icon.py) at the size Windows uses beside the clock.</summary>
    private static Drawing.Icon CreateIcon()
    {
        using var stream = typeof(Tray).Assembly.GetManifestResourceStream("app.ico")
                           ?? throw new InvalidOperationException("app.ico is missing from the program.");
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
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

    public static bool Save(Native.RECT bounds, string path, int maxWidth = MaxWidth)
    {
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (width < 200 || height < 200) return false;
        try
        {
            using var shot = new Drawing.Bitmap(width, height);
            using (var g = Drawing.Graphics.FromImage(shot))
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, shot.Size);

            // Full-resolution screenshots of an ultrawide are needlessly large for a reference picture.
            if (width > maxWidth)
            {
                int scaledHeight = height * maxWidth / width;
                using var scaled = new Drawing.Bitmap(maxWidth, scaledHeight);
                using (var g = Drawing.Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(shot, 0, 0, maxWidth, scaledHeight);
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
