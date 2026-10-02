using System.IO;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

/// <summary>
/// Things people feed the overlay that nobody planned for: hand-written plans in odd shapes, build
/// files from an older version, a build folder with stray files in it. None of it may stop the overlay.
/// </summary>
public sealed class RobustnessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"leh-robust-{Guid.NewGuid():N}");

    public RobustnessTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("")]
    [InlineData("\n\n\n")]
    [InlineData("name:")]
    [InlineData("name: only a name")]
    [InlineData("# only a comment")]
    [InlineData("no colon anywhere\njust words")]
    [InlineData("0: level zero\n101: level too high\n-5: negative\n999999999999: huge")]
    [InlineData("5: \n6:\n7:    ")]
    [InlineData("10-5: range backwards\n3-3: same\n2 - 9 : spaced")]
    [InlineData("lvl 4: with prefix\nLevel 12) other separator\n8. dot")]
    [InlineData("4: Specialize\n4: Specialize\n4: Specialize")]
    [InlineData("4: Passives [Unclosed: Node (1/8\n5: Passives []: (0/0)\n6: Passives [X]: A (x/y), B (9999999999/1)")]
    [InlineData("3: Choose mastery: \n3: Specialize \n3: Specialize A (replaces )\n3: Respec")]
    [InlineData("﻿name: with a byte order mark\r\n4: windows line ends\r\n")]
    [InlineData("4: emoji 🔥 and ünïcödé and 中文 and \t tabs")]
    public void AnyPlanText_ParsesAndSummarises_WithoutFailing(string text)
    {
        var plan = BuildPlan.Parse(text, "fallback");
        Assert.NotNull(plan.Name);
        // Whatever the levels say, looking at the plan for any character level works.
        foreach (int level in new[] { -1, 0, 1, 50, 100, 1000 })
        {
            var (due, next) = BuildPlan.View(plan, level, new HashSet<string>());
            Assert.True(due.Count <= 4);
            _ = next;
        }
        var summary = BuildSummary.From(plan);
        Assert.NotEmpty(summary.Headlines);
        Assert.All(summary.Steps, s => Assert.True(s.ToLevel >= s.Level));
    }

    [Fact]
    public void AVeryLongPlan_IsHandled()
    {
        string text = "name: Long\n" + string.Join("\n", Enumerable.Range(0, 20000).Select(i => $"{1 + i % 100}: Passives [Tree {i % 7}]: Node {i % 50} ({1 + i % 8}/8)"));
        var plan = BuildPlan.Parse(text);
        Assert.Equal(20000, plan.Entries.Count);
        Assert.NotEmpty(BuildSummary.From(plan).Steps);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"trees\": null, \"stages\": null}")]
    [InlineData("{\"trees\": [{}], \"stages\": [{}]}")]
    [InlineData("{\"trees\": [{\"name\": \"A\", \"nodes\": [{\"id\": 1, \"requires\": [99]}]}], \"stages\": [{\"passives\": [5, 5, 5], \"skills\": {\"Ghost\": [1]}}]}")]
    [InlineData("{\"name\": 5}")]
    public void ABuildFileInAnyState_LoadsAsNothingOrAsSomethingUsable(string content)
    {
        string file = Path.Combine(_dir, "x.tree.json");
        File.WriteAllText(file, content);
        var build = BuildTree.Load(file);
        if (build is null) return;

        // Whatever loaded has to survive being looked at.
        Assert.NotNull(build.Trees);
        Assert.NotNull(build.Stages);
        _ = build.StageFor(10);
        foreach (var tree in build.Trees)
        {
            var state = build.State(tree, 5, 10);
            Assert.NotNull(state.Allocated);
            _ = build.NodeName(1, passive: true);
        }
    }

    [Fact]
    public void ASessionWithABrokenOrMissingBuild_StillStarts_AndShowsTheGuide()
    {
        var storage = new Storage(_dir);
        string builds = storage.Folder("builds");
        File.WriteAllText(Path.Combine(builds, "broken.txt"), "name: Broken\n4: Specialize Rive\n");
        File.WriteAllText(Path.Combine(builds, "broken.tree.json"), "{ this is not json");
        File.WriteAllText(Path.Combine(builds, "half.txt"), "name: Half\n");
        File.WriteAllText(Path.Combine(builds, "half.tree.json"), "{\"trees\": [{\"name\": \"Only\", \"kind\": \"passive\", \"nodes\": []}], \"stages\": []}");

        var route = TrackerTests.MakeRoute("A", "B");
        foreach (string plan in new[] { "broken.txt", "half.txt", "does-not-exist.txt", "", "..\\outside.txt" })
        {
            var session = new Session(new Storage(_dir), new Guide { PassiveCap = 15, IdolCap = 8, Routes = { route } }, new SceneMap());
            session.Profile.BuildPlan = plan;
            session.ReloadPlan();
            session.Handle(new CharacterLevelEvent(20, 0, 0), live: true);

            Assert.NotNull(session.Tracker.Step);
            _ = session.Rewards();
            _ = session.Stage;
            _ = session.PinnedStage;
            session.SetStage(null);
            Assert.False(session.SwitchVariant(3));
            if (session.Tree is { } tree)
                foreach (var tab in tree.Trees) _ = session.TreeState(tab);
        }
    }
}
