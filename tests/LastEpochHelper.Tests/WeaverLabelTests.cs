using LastEpochHelper.Core;
using D = System.Drawing;

namespace LastEpochHelper.Tests;

/// <summary>
/// The Weaver tree's labels: the text reader catches about half of them (it takes many "0/2" for the
/// word "on"), so the rest are read digit by digit, with digits learnt from the same picture.
/// </summary>
public class WeaverLabelTests
{
    // Node id, max, have, position in the planner's layout.
    private static readonly (int Id, int Max, int Have, double X, double Y)[] Nodes =
    {
        (1, 3, 0, 0, 0), (2, 2, 1, 120, 0), (3, 1, 1, 240, 0), (4, 3, 1, 360, 0),
        (5, 2, 0, 0, 100), (6, 3, 2, 120, 100), (7, 5, 3, 240, 100), (8, 1, 0, 360, 100),
        (9, 4, 0, 0, 200), (10, 2, 2, 120, 200), (11, 3, 0, 240, 200), (12, 1, 1, 360, 200),
    };

    private const double Scale = 1.5, ShiftX = 150, ShiftY = 120;

    [Fact]
    public void LabelsTheReaderMissed_AreReadFromThePicture()
    {
        var tree = new TreeDef
        {
            Name = TreeDef.WeaverName, Kind = TreeDef.WeaverKind,
            Nodes = Nodes.Select(n => new TreeNode { Id = n.Id, Name = $"Node {n.Id}", Max = n.Max, X = n.X, Y = n.Y }).ToList(),
        };
        using var shot = Draw();
        var picture = PictureOf(shot);

        // What the text reader made of it: five labels, slightly off centre as its boxes are.
        var read = new[] { 1, 2, 5, 8, 11 }.Select(id => Nodes.First(n => n.Id == id))
            .Select(n => new TreeReader.Token(n.X * Scale + ShiftX + 1.5, n.Y * Scale + ShiftY - 1, n.Have, n.Max, 13)).ToList();

        var points = TreeReader.ReadClosely(read, picture, tree, known: true);

        Assert.NotNull(points);
        foreach (var node in Nodes) Assert.Equal((node.Id, node.Have), (node.Id, points!.GetValueOrDefault(node.Id, -1)));
        // Without the picture only the labels the reader caught are known.
        Assert.Equal(5, TreeReader.ReadClosely(read, null, tree, known: true)!.Count);
    }

    [Fact]
    public void ADigitNotSeenAnywhere_IsLeftUnread_NotGuessed()
    {
        // No 3 anywhere else on this picture: the 3/5 label is left out.
        var tree = new TreeDef
        {
            Name = TreeDef.WeaverName, Kind = TreeDef.WeaverKind,
            Nodes = Nodes.Where(n => n.Id is 2 or 3 or 5 or 7 or 8 or 12)
                .Select(n => new TreeNode { Id = n.Id, Name = $"Node {n.Id}", Max = n.Max, X = n.X, Y = n.Y }).ToList(),
        };
        using var shot = Draw(Nodes.Where(n => n.Id is 2 or 3 or 5 or 7 or 8 or 12));
        var read = new[] { 3, 5, 8, 12 }.Select(id => Nodes.First(n => n.Id == id))
            .Select(n => new TreeReader.Token(n.X * Scale + ShiftX, n.Y * Scale + ShiftY, n.Have, n.Max, 13)).ToList();

        var points = TreeReader.ReadClosely(read, PictureOf(shot), tree, known: true)!;

        Assert.Equal(1, points[2]);
        Assert.False(points.ContainsKey(7));
    }

    /// <summary>The game's look: dark plates in gold frames under bright node icons, white print.</summary>
    private static D.Bitmap Draw(IEnumerable<(int Id, int Max, int Have, double X, double Y)>? nodes = null)
    {
        var shot = new D.Bitmap(900, 600, D.Imaging.PixelFormat.Format24bppRgb);
        using var g = D.Graphics.FromImage(shot);
        g.Clear(D.Color.FromArgb(48, 58, 50));
        g.TextRenderingHint = D.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new D.Font("Arial", 17, D.FontStyle.Bold, D.GraphicsUnit.Pixel);
        using var gold = new D.Pen(D.Color.FromArgb(210, 170, 70), 2);
        using var plate = new D.SolidBrush(D.Color.FromArgb(14, 14, 16));
        using var icon = new D.SolidBrush(D.Color.FromArgb(235, 235, 240));
        var centre = new D.StringFormat { Alignment = D.StringAlignment.Center, LineAlignment = D.StringAlignment.Center };
        foreach (var (_, max, have, x, y) in nodes ?? Nodes)
        {
            float cx = (float)(x * Scale + ShiftX), cy = (float)(y * Scale + ShiftY);
            g.FillEllipse(icon, cx - 18, cy - 50, 36, 36);
            g.FillRectangle(plate, cx - 24, cy - 12, 48, 24);
            g.DrawRectangle(gold, cx - 24, cy - 12, 48, 24);
            // Set one character at a time, as the game's font keeps them apart.
            g.DrawString(have.ToString(), font, D.Brushes.White, cx - 11, cy + 1, centre);
            g.DrawString("/", font, D.Brushes.White, cx, cy + 1, centre);
            g.DrawString(max.ToString(), font, D.Brushes.White, cx + 11, cy + 1, centre);
        }
        return shot;
    }

    private static ScreenPicture PictureOf(D.Bitmap shot)
    {
        var data = shot.LockBits(new D.Rectangle(0, 0, shot.Width, shot.Height), D.Imaging.ImageLockMode.ReadOnly, D.Imaging.PixelFormat.Format24bppRgb);
        try
        {
            var pixels = new byte[data.Stride * shot.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new ScreenPicture(shot.Width, shot.Height, data.Stride, pixels, 0, 0);
        }
        finally { shot.UnlockBits(data); }
    }
}
