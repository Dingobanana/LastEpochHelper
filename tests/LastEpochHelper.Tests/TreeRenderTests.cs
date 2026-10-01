using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public sealed class TreeRenderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"leh-tree-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>
    /// Draws every tab of the tree view off-screen. Set LEH_TREE_SAMPLE to a *.tree.json and
    /// LEH_RENDER_DIR to a folder to get PNGs of a real build for a visual check.
    /// </summary>
    [Fact]
    public void EveryTab_Renders()
    {
        string? sample = Environment.GetEnvironmentVariable("LEH_TREE_SAMPLE");
        string? output = Environment.GetEnvironmentVariable("LEH_RENDER_DIR");
        int.TryParse(Environment.GetEnvironmentVariable("LEH_TREE_LEVEL"), out int level);
        Exception? failure = null;
        int rendered = 0;

        var thread = new Thread(() =>
        {
            try
            {
                var storage = new Storage(_dir);
                if (Environment.GetEnvironmentVariable("LEH_ATLAS") is { } atlas)
                    File.Copy(atlas, storage.PathOf(BuildTree.AtlasFile));
                var route = TrackerTests.MakeRoute("A", "B");
                string filters = Path.Combine(_dir, "Filters");
                Directory.CreateDirectory(filters);
                File.WriteAllText(Path.Combine(filters, "Leveling.xml"), "");
                File.WriteAllText(Path.Combine(filters, "Strict endgame.xml"), "");
                var endgame = EndgameData.Load(Path.Combine(AppContext.BaseDirectory, "Data", "endgame.json"));
                var session = new Session(storage, new Guide { PassiveCap = 15, IdolCap = 8, Routes = { route } }, new SceneMap(), endgame, filters);

                var build = sample is not null ? BuildTree.Load(sample)! : new BuildTree
                {
                    Trees =
                    {
                        new TreeDef { Name = "Base", Nodes = { new TreeNode { Id = 1, Name = "A", Max = 8 }, new TreeNode { Id = 2, Name = "B", Max = 5, X = 100, Y = 50, Requires = { 1 } } } },
                        new TreeDef { Name = "Rive", Kind = TreeDef.SkillKind, Nodes = { new TreeNode { Id = 4, Name = "Champion", Max = 4 } } },
                    },
                    Stages = { new TreeStage { Name = "Early", Level = 10, Passives = { 1, 1, 2 }, Skills = { ["Rive"] = new() { 4, 4 } } } },
                };
                string plan = Path.Combine(session.BuildsDir, "sample.txt");
                File.WriteAllText(plan, "name: Sample\n");
                build.Save(BuildTree.PathFor(plan));
                session.Profile.BuildPlan = "sample.txt";
                session.ReloadPlan();
                session.Handle(new CharacterLevelEvent(level > 0 ? level : 5, 2, 0), live: false);

                var window = new TreeWindow(session);
                foreach (var tree in session.Tree!.Trees)
                {
                    session.Profile.TreeTab = tree.Name;
                    if (tree.Kind == TreeDef.SkillKind) session.Profile.SkillPoints[tree.Name] = 3;
                    window.Render();
                    var root = (FrameworkElement)window.Content;
                    root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    root.Arrange(new Rect(root.DesiredSize));
                    root.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    rendered++;
                    if (output is null) continue;
                    Directory.CreateDirectory(output);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"tree-{rendered:00}-{tree.Name.Replace(' ', '_')}.png"));
                    encoder.Save(file);
                }

                // The planner pages, with some state so every kind of row is drawn.
                session.SetFilterLevel("Strict endgame", 60);
                session.UpdateTimeline("Fall of the Outcasts", p => { p.Normal = true; p.Blessing = "Winds of Fortune"; p.Corruption = 30; });
                session.UpdateDungeon("Temporal Sanctum", p => p.Keys = 2);
                var planner = new PlannerWindow(session);
                session.Handle(new SceneLoadEvent("ZA"), live: true);
                session.Handle(new PlayerDiedEvent(), live: true);
                session.SetDeathCause(session.Profile.DeathLog[0], "Boss mechanic");
                session.Handle(new PlayerDiedEvent(), live: true);
                foreach (string tab in new[] { "Gear", "Idols", "Targets", "Loot filter", "Monolith", "Morditas", "Prophecies", "Dungeons", "Deaths" })
                {
                    session.Profile.PlannerTab = tab;
                    planner.Render();
                    var root = (FrameworkElement)planner.Content;
                    root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    root.Arrange(new Rect(root.DesiredSize));
                    root.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    rendered++;
                    if (output is null) continue;
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"planner-{tab.Replace(' ', '_')}.png"));
                    encoder.Save(file);
                }
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.True(rendered >= 2);
    }
}
