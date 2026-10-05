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
                // LEH_WEAVER: a fetched weaver.json, to draw the Weaver tab too. LEH_ATLAS_NAMED: the icon sheet under its own name.
                if (Environment.GetEnvironmentVariable("LEH_WEAVER") is { } weaver)
                    File.Copy(weaver, storage.PathOf(WeaverSet.FileName));
                if (Environment.GetEnvironmentVariable("LEH_ATLAS_NAMED") is { } named)
                    File.Copy(named, storage.PathOf(Path.GetFileName(named)));
                var route = TrackerTests.MakeRoute("A", "B");
                string filters = Path.Combine(_dir, "Filters");
                Directory.CreateDirectory(filters);
                File.WriteAllText(Path.Combine(filters, "Leveling.xml"), "");
                File.WriteAllText(Path.Combine(filters, "Strict endgame.xml"), "");
                var endgame = EndgameData.LoadBundled();
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
                // With a real sample, its plan text (next to the tree file) feeds the summary page.
                string? samplePlan = sample?.Replace(".tree.json", ".txt");
                File.WriteAllText(plan, samplePlan is not null && File.Exists(samplePlan) ? File.ReadAllText(samplePlan)
                    : "name: Sample\n4: Specialize Rive\n5: Passives [Base]: A (2/8)\n9: Choose mastery: Paladin\n");
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
                foreach (string tab in new[] { PlannerWindow.SummaryTab, "Gear", "Idols", "Targets", "Search", "Loot filter", "Monolith", "Morditas", "Prophecies", "Dungeons", "Deaths" })
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
                    using var file = File.Create(Path.Combine(output, $"planner-{tab.Replace(' ', '_').Replace(';', '_')}.png"));
                    encoder.Save(file);
                }

                // The settings window: every tab has to build and lay out. (It only needs the overlay
                // window for its buttons, so an empty stand-in will do.)
                var overlay = (MainWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
                var settings = new SettingsWindow(session, overlay);
                var tabs = (System.Windows.Controls.TabControl)((System.Windows.Controls.Panel)settings.Content).Children[0];
                for (int i = 0; i < tabs.Items.Count; i++)
                {
                    // A tab control only shows the selected page inside a live window; draw the page itself.
                    var page = (FrameworkElement)((System.Windows.Controls.TabItem)tabs.Items[i]).Content;
                    tabs.SelectedIndex = i;
                    ((System.Windows.Controls.TabItem)tabs.Items[i]).Content = null;
                    var frame = new System.Windows.Controls.Border { Background = Brushes.White, Child = page, Width = 500 };
                    frame.Measure(new Size(500, double.PositiveInfinity));
                    frame.Arrange(new Rect(frame.DesiredSize));
                    frame.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(500, Math.Max(1, (int)Math.Ceiling(frame.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(frame);
                    rendered++;
                    frame.Child = null;
                    ((System.Windows.Controls.TabItem)tabs.Items[i]).Content = page;
                    if (output is null) continue;
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"settings-{i + 1}.png"));
                    encoder.Save(file);
                }
                Assert.Equal(5, tabs.Items.Count);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.True(rendered >= 2);
    }

    /// <summary>
    /// Draws the build tree and the planner for real planners saved from Maxroll (LEH_MAXROLL_DIR,
    /// LEH_MAXROLL_GAME; at most LEH_RENDER_LIMIT planners, spread over the folders), at several
    /// character levels and with each stage chosen by hand. Whatever a planner holds, the windows have to draw.
    /// </summary>
    [Fact]
    public void EverySavedPlanner_Draws_WhenAskedTo()
    {
        string? dirs = Environment.GetEnvironmentVariable("LEH_MAXROLL_DIR"), gameFile = Environment.GetEnvironmentVariable("LEH_MAXROLL_GAME"),
            output = Environment.GetEnvironmentVariable("LEH_MAXROLL_OUT");
        if (dirs is null || gameFile is null || output is null || !int.TryParse(Environment.GetEnvironmentVariable("LEH_RENDER_LIMIT"), out int limit)) return;

        var failures = new List<string>();
        int drawn = 0, builds = 0;
        var thread = new Thread(() =>
        {
            var game = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(gameFile))!;
            var files = dirs.Split(';', StringSplitOptions.RemoveEmptyEntries).SelectMany(d => Directory.GetFiles(d, "*.json")).OrderBy(f => f).ToList();
            int step = Math.Max(1, files.Count / Math.Max(1, limit));
            var endgame = EndgameData.LoadBundled();
            var route = TrackerTests.MakeRoute("A", "B");
            for (int f = 0; f < files.Count; f += step)
            {
                string id = Path.GetFileNameWithoutExtension(files[f]);
                try
                {
                    if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(files[f]))?["data"]?.GetValue<string>() is not { } data) continue;
                    var results = MaxrollImporter.ConvertAll(id, id, System.Text.Json.Nodes.JsonNode.Parse(data)!, game);
                    string home = Path.Combine(_dir, id);
                    var storage = new Storage(home);
                    var session = new Session(storage, new Guide { PassiveCap = 15, IdolCap = 8, Routes = { route } }, new SceneMap(), endgame, Path.Combine(home, "Filters"));
                    var written = BuildFiles.Write(session.BuildsDir, results);
                    var tree = new TreeWindow(session);
                    var planner = new PlannerWindow(session);
                    for (int b = 0; b < results.Count; b++)
                    {
                        builds++;
                        session.Profile.BuildPlan = written[b];
                        session.ReloadPlan();
                        foreach (int level in new[] { 1, 23, 60, 100 })
                        {
                            session.Handle(new CharacterLevelEvent(level, 0, 0), live: false);
                            foreach (var stage in session.Tree!.Stages.Cast<TreeStage?>().Prepend(null).Take(level == 60 ? 99 : 1))
                            {
                                session.SetStage(stage);
                                foreach (var tab in session.Tree.Trees)
                                {
                                    session.Profile.TreeTab = tab.Name;
                                    session.Profile.SkillPoints[tab.Name] = level / 5;
                                    tree.Render();
                                    Draw((FrameworkElement)tree.Content);
                                    drawn++;
                                }
                            }
                            foreach (string page in new[] { PlannerWindow.SummaryTab, "Gear", "Idols", "Targets", "Search", "Loot filter" })
                            {
                                session.Profile.PlannerTab = page;
                                planner.Render();
                                Draw((FrameworkElement)planner.Content);
                                drawn++;
                            }
                        }
                        if (results.Count > 1 && !session.SwitchVariant((b + 1) % results.Count)) failures.Add($"{id}: could not switch to version {(b + 1) % results.Count}");
                    }
                }
                catch (Exception e)
                {
                    failures.Add($"{id}: {e.GetType().Name}: {e.Message} @ {e.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("LastEpochHelper"))?.Trim()}");
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        File.WriteAllLines(output, new[] { $"{builds} builds, {drawn} windows drawn, {failures.Count} failures" }.Concat(failures));
        Assert.Empty(failures);

        static void Draw(FrameworkElement root)
        {
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();
            int width = Math.Clamp((int)Math.Ceiling(root.ActualWidth), 1, 4000), height = Math.Clamp((int)Math.Ceiling(root.ActualHeight), 1, 4000);
            new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32).Render(root);
        }
    }
}
