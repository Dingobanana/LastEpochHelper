using System.IO;

namespace LastEpochHelper.Tests;

public class ScreenReaderTests
{
    /// <summary>
    /// Reads the real screen, so it only runs on request: set LEH_OCR_OUT to a file path and the
    /// recognised lines (with letter heights) are written there. LEH_OCR_RECT = "left,top,right,bottom".
    /// </summary>
    [Fact]
    public async Task ReadsTheScreen_WhenAskedTo()
    {
        string? output = Environment.GetEnvironmentVariable("LEH_OCR_OUT");
        if (output is null) return;

        // LEH_OCR_LABELS = "left,top,width,height;Skill name": the enlarged label read on a saved picture.
        if (Environment.GetEnvironmentVariable("LEH_OCR_LABELS") is { } labels && Environment.GetEnvironmentVariable("LEH_OCR_FILE") is { } shot
            && Environment.GetEnvironmentVariable("LEH_TREE_SAMPLE") is { } treeFile && LastEpochHelper.Core.BuildTree.Load(treeFile) is { } tree)
        {
            var parts2 = labels.Split(';');
            var r = parts2[0].Split(',').Select(int.Parse).ToArray();
            var started2 = DateTime.UtcNow;
            var reads = await new ScreenReader().ReadLabelsFromFileAsync(shot, new System.Drawing.Rectangle(r[0], r[1], r[2], r[3]));
            var tokens2 = LastEpochHelper.Core.TreeReader.Merge(reads.Select(LastEpochHelper.Core.TreeReader.Tokens).ToArray());
            var skill = tree.Trees.First(t => t.Name == parts2[1]);
            var points = LastEpochHelper.Core.TreeReader.Read(tokens2, skill, known: true);
            File.WriteAllLines(output, new[]
            {
                $"ms={(DateTime.UtcNow - started2).TotalMilliseconds:0} per-read labels={string.Join("/", reads.Select(x => LastEpochHelper.Core.TreeReader.Tokens(x).Count))} merged={tokens2.Count} nodes={skill.Nodes.Count(n => n.Max >= 1)}",
                points is null ? "NO FIT" : $"matched={points.Count}: " + string.Join(", ", points.Where(kv => kv.Value > 0).Select(kv => $"{skill.Nodes.First(n => n.Id == kv.Key).Name}={kv.Value}")),
                "labels: " + string.Join("  ", tokens2.Select(t => $"{t.Have}/{t.Max}@{t.X:0},{t.Y:0}")),
            }.Concat(reads.Select((x, k) => $"read {k}: " + string.Join("  ", LastEpochHelper.Core.TreeReader.Tokens(x).Select(t => $"{t.Have}/{t.Max}@{t.X:0},{t.Y:0}")))));
            return;
        }

        // LEH_OCR_WEAVER = weaver.json: the Weaver tree's labels in a saved picture, as the overlay reads them.
        // LEH_OCR_MASK = "left,top,right,bottom" blacks out the overlay's own tree window in the picture first.
        if (Environment.GetEnvironmentVariable("LEH_OCR_WEAVER") is { } weaverFile && Environment.GetEnvironmentVariable("LEH_OCR_FILE") is { } weaverShot
            && LastEpochHelper.Core.WeaverSet.Load(weaverFile) is { } weaver)
        {
            string weaverCopy = Path.Combine(Path.GetTempPath(), $"leh-weaver-{Guid.NewGuid():N}.png");
            System.Drawing.Rectangle whole;
            using (var loaded = new System.Drawing.Bitmap(weaverShot))
            using (var copy = loaded.Clone(new System.Drawing.Rectangle(0, 0, loaded.Width, loaded.Height), System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                if (Environment.GetEnvironmentVariable("LEH_OCR_MASK")?.Split(',').Select(int.Parse).ToArray() is { Length: 4 } m)
                    using (var g = System.Drawing.Graphics.FromImage(copy)) g.FillRectangle(System.Drawing.Brushes.Black, m[0], m[1], m[2] - m[0], m[3] - m[1]);
                copy.Save(weaverCopy);
                whole = new System.Drawing.Rectangle(0, 0, copy.Width, copy.Height);
            }
            try
            {
                var started3 = DateTime.UtcNow;
                var (reads, shot3) = await new ScreenReader().ReadLabelsAndPictureFromFileAsync(weaverCopy, whole);
                var tokens3 = LastEpochHelper.Core.TreeReader.Merge(reads.Select(LastEpochHelper.Core.TreeReader.Tokens).ToArray());
                var byText = LastEpochHelper.Core.TreeReader.Read(tokens3, weaver.Tree, known: true) ?? new Dictionary<int, int>();
                var all = LastEpochHelper.Core.TreeReader.ReadClosely(tokens3, shot3, weaver.Tree, known: true);
                File.WriteAllLines(output, new[] { $"ms={(DateTime.UtcNow - started3).TotalMilliseconds:0} labels={tokens3.Count} by text={byText.Count} in all={all?.Count.ToString() ?? "NO FIT"}" }
                    .Concat((all ?? new Dictionary<int, int>()).OrderBy(kv => kv.Key)
                        .Select(kv => $"{weaver.Tree.Nodes.First(n => n.Id == kv.Key).Name} = {kv.Value}{(byText.ContainsKey(kv.Key) ? "" : "  (digit by digit)")}")));
            }
            finally { File.Delete(weaverCopy); }
            return;
        }

        // LEH_OCR_DETECT: which panel (and skill or tab) the detector sees in a saved picture.
        if (Environment.GetEnvironmentVariable("LEH_OCR_DETECT") is not null && Environment.GetEnvironmentVariable("LEH_OCR_FILE") is { } panelShot
            && Environment.GetEnvironmentVariable("LEH_TREE_SAMPLE") is { } buildFile && LastEpochHelper.Core.BuildTree.Load(buildFile) is { } shownBuild)
        {
            // LEH_OCR_RAW: read the picture as it is, without putting its contrast back first.
            var read = await new ScreenReader().ReadFileAsync(panelShot, restore: Environment.GetEnvironmentVariable("LEH_OCR_RAW") is null);
            var skillNames = shownBuild.Trees.Where(t => t.Kind == LastEpochHelper.Core.TreeDef.SkillKind).Select(t => t.Name).ToList();
            var seen = LastEpochHelper.Core.PanelDetector.Detect(read, shownBuild.PassiveTabNames, skillNames);
            var strict = LastEpochHelper.Core.PanelDetector.Detect(read, shownBuild.PassiveTabNames, skillNames, strict: true);
            File.WriteAllLines(output, new[] { $"panel={seen.Panel} skill={seen.Skill} tab={seen.Tab} weaverPlaced={seen.WeaverPlaced} | strict: panel={strict.Panel} skill={strict.Skill} tab={strict.Tab} | {read.Count} lines read" });
            return;
        }

        // LEH_OCR_BLESSING: is the blessing choice in a saved picture, and where is its heading.
        if (Environment.GetEnvironmentVariable("LEH_OCR_BLESSING") is not null && Environment.GetEnvironmentVariable("LEH_OCR_FILE") is { } offerShot)
        {
            var read = await new ScreenReader().ReadFileAsync(offerShot);
            var heading = LastEpochHelper.Core.BlessingAdvice.FindOffer(read);
            File.WriteAllLines(output, new[] { heading is null ? "no offer" : $"offer: '{heading.Text}' @{heading.X:0},{heading.Y:0} w{heading.Width:0}" }
                .Concat(read.Select(l => $"{l.Height,5:0} @{l.X,6:0},{l.Y,6:0} w{l.Width,4:0}  {l.Text}")));
            return;
        }

        // LEH_OCR_FILE reads a saved picture word by word instead of the live screen.
        if (Environment.GetEnvironmentVariable("LEH_OCR_FILE") is { } picture)
        {
            var found = await new ScreenReader().ReadFileAsync(picture, words: true);
            var report = found.Select(l => $"{l.Height,5:0} @{l.X,6:0},{l.Y,6:0} w{l.Width,4:0}  {l.Text}").ToList();
            if (Environment.GetEnvironmentVariable("LEH_TREE_SAMPLE") is { } sample && LastEpochHelper.Core.BuildTree.Load(sample) is { } build)
            {
                var tokens = LastEpochHelper.Core.TreeReader.Tokens(found);
                report.Insert(0, $"tokens: {string.Join("  ", tokens.Select(t => $"{t.Have}/{t.Max}@{t.X:0},{t.Y:0}"))}");
                var best = LastEpochHelper.Core.TreeReader.ReadBest(tokens, build.Trees);
                report.Insert(0, best is null ? "NO TREE FITS" : $"FITS {best.Value.Tree.Name}: " + string.Join(", ",
                    best.Value.Points.Select(kv => $"{best.Value.Tree.Nodes.First(n => n.Id == kv.Key).Name}={kv.Value}")));
            }
            File.WriteAllLines(output, report);
            return;
        }

        var parts = (Environment.GetEnvironmentVariable("LEH_OCR_RECT") ?? "0,0,1920,1080").Split(',').Select(int.Parse).ToArray();
        var reader = new ScreenReader();
        var started = DateTime.UtcNow;
        var lines = await reader.ReadAsync(
            new Native.RECT { Left = parts[0], Top = parts[1], Right = parts[2], Bottom = parts[3] }, Array.Empty<Native.RECT>());

        File.WriteAllLines(output, new[] { $"available={reader.Available} lines={lines.Count} ms={(DateTime.UtcNow - started).TotalMilliseconds:0}" }
            .Concat(lines.Select(l => $"{l.Height,5:0}  {l.Text}")));
        Assert.True(reader.Available);
    }

    /// <summary>
    /// Text the reader takes for slightly tilted comes back straightened, its positions turned about
    /// the picture's centre; they must be turned back to where the words are on the picture.
    /// </summary>
    [Fact]
    public async Task WordsOnATiltedPicture_KeepTheirPlace()
    {
        var reader = new ScreenReader();
        if (!reader.Available) return;
        var drawn = new List<(string Text, float X, float Y)>();
        string path = Path.Combine(Path.GetTempPath(), $"leh-tilt-{Guid.NewGuid():N}.png");
        using (var picture = new System.Drawing.Bitmap(1600, 900))
        using (var g = System.Drawing.Graphics.FromImage(picture))
        using (var font = new System.Drawing.Font("Arial", 22, System.Drawing.FontStyle.Bold))
        {
            g.Clear(System.Drawing.Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            string[] words = { "Thunder", "Glacier", "Harvest", "Lantern", "Meadow", "Orchard", "Quarry", "Rampart", "Saddle" };
            for (int i = 0; i < words.Length; i++)
            {
                float x = 200 + i % 3 * 600, y = 150 + i / 3 * 300;
                var size = g.MeasureString(words[i], font);
                // The whole picture is tilted by 5 degrees: each word turns about its own centre and
                // sits where the turn of the picture puts it.
                double turn = 5 * Math.PI / 180, dx = x - 800, dy = y - 450;
                float cx = (float)(800 + dx * Math.Cos(turn) - dy * Math.Sin(turn)), cy = (float)(450 + dx * Math.Sin(turn) + dy * Math.Cos(turn));
                g.ResetTransform();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(5);
                g.DrawString(words[i], font, System.Drawing.Brushes.Black, -size.Width / 2, -size.Height / 2);
                drawn.Add((words[i], cx, cy));
            }
            picture.Save(path);
        }
        try
        {
            var read = await reader.ReadFileAsync(path, words: true, restore: false);
            foreach (var (text, x, y) in drawn)
            {
                var word = read.FirstOrDefault(w => w.Text == text);
                if (word is null) continue;
                Assert.InRange(word.X + word.Width / 2, x - 12, x + 12);
                Assert.InRange(word.Y + word.Height / 2, y - 12, y + 12);
            }
            Assert.True(read.Count(w => drawn.Any(d => d.Text == w.Text)) >= 6, string.Join(", ", read.Select(w => w.Text)));
        }
        finally { File.Delete(path); }
    }
}
