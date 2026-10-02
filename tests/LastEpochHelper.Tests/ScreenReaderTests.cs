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
            var points = LastEpochHelper.Core.TreeReader.Read(tokens2, skill);
            File.WriteAllLines(output, new[]
            {
                $"ms={(DateTime.UtcNow - started2).TotalMilliseconds:0} per-read labels={string.Join("/", reads.Select(x => LastEpochHelper.Core.TreeReader.Tokens(x).Count))} merged={tokens2.Count} nodes={skill.Nodes.Count(n => n.Max >= 1)}",
                points is null ? "NO FIT" : $"matched={points.Count}: " + string.Join(", ", points.Where(kv => kv.Value > 0).Select(kv => $"{skill.Nodes.First(n => n.Id == kv.Key).Name}={kv.Value}")),
                "labels: " + string.Join("  ", tokens2.Select(t => $"{t.Have}/{t.Max}@{t.X:0},{t.Y:0}")),
            });
            return;
        }

        // LEH_OCR_DETECT: which panel (and skill or tab) the detector sees in a saved picture.
        if (Environment.GetEnvironmentVariable("LEH_OCR_DETECT") is not null && Environment.GetEnvironmentVariable("LEH_OCR_FILE") is { } panelShot
            && Environment.GetEnvironmentVariable("LEH_TREE_SAMPLE") is { } buildFile && LastEpochHelper.Core.BuildTree.Load(buildFile) is { } shownBuild)
        {
            var read = await new ScreenReader().ReadFileAsync(panelShot);
            var skillNames = shownBuild.Trees.Where(t => t.Kind == LastEpochHelper.Core.TreeDef.SkillKind).Select(t => t.Name).ToList();
            var seen = LastEpochHelper.Core.PanelDetector.Detect(read, shownBuild.PassiveTabNames, skillNames);
            var strict = LastEpochHelper.Core.PanelDetector.Detect(read, shownBuild.PassiveTabNames, skillNames, strict: true);
            File.WriteAllLines(output, new[] { $"panel={seen.Panel} skill={seen.Skill} tab={seen.Tab} | strict: panel={strict.Panel} skill={strict.Skill} tab={strict.Tab}" });
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
}
