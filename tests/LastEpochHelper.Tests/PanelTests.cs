using System.IO;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public class PanelDetectorTests
{
    private static readonly string[] Tabs = { "Primalist", "Beastmaster", "Shaman", "Druid" };
    private static readonly string[] Skills = { "Gathering Storm", "Summon Thorn Totem", "Spriggan Form" };

    [Fact]
    public void PassivePanel_IsRecognisedByItsTabs_NotByOneStrayName()
    {
        var panel = new[] { new ScreenLine("PRIMALIST 13", 20, 300, 90, 120), new ScreenLine("Beastmaster 0", 20, 500, 92, 140), new ScreenLine("Shaman 0", 20, 700, 92, 90) };
        var reading = PanelDetector.Detect(panel, Tabs, Skills);
        Assert.Equal(GamePanel.Passives, reading.Panel);
        Assert.Equal(90, reading.Anchor!.Y);

        var chat = new[] { new ScreenLine("[Global] any shaman builds?", 16) };
        Assert.Equal(GamePanel.None, PanelDetector.Detect(chat, Tabs, Skills).Panel);

        var withWord = new[] { new ScreenLine("Druid", 20), new ScreenLine("Passive Points: 2", 18) };
        Assert.Equal(GamePanel.Passives, PanelDetector.Detect(withWord, Tabs, Skills).Panel);
    }

    [Fact]
    public void SkillPanel_GivesTheOpenSkill_OrJustThePanelWhenSeveralAreListed()
    {
        var tree = new[] { new ScreenLine("GATHERING STORM", 34, 2000, 140, 380), new ScreenLine("Level 6", 18) };
        var reading = PanelDetector.Detect(tree, Tabs, Skills);
        Assert.Equal((GamePanel.Skills, "Gathering Storm"), (reading.Panel, reading.Skill));

        var overview = new[] { new ScreenLine("Gathering Storm", 18), new ScreenLine("Spriggan Form", 18) };
        var list = PanelDetector.Detect(overview, Tabs, Skills);
        Assert.Equal(GamePanel.Skills, list.Panel);
        Assert.Null(list.Skill);
    }

    [Fact]
    public void NothingOnScreen_MeansNoPanel_AndTheHintBreaksATie()
    {
        Assert.Equal(GamePanel.None, PanelDetector.Detect(new[] { new ScreenLine("Inventory", 30) }, Tabs, Skills).Panel);

        var both = new[] { new ScreenLine("Primalist", 20), new ScreenLine("Druid", 20), new ScreenLine("Gathering Storm", 18), new ScreenLine("Spriggan Form", 18) };
        Assert.Equal(GamePanel.Passives, PanelDetector.Detect(both, Tabs, Skills, GamePanel.Passives).Panel);
        Assert.Equal(GamePanel.Skills, PanelDetector.Detect(both, Tabs, Skills, GamePanel.Skills).Panel);
    }
}

public sealed class HandSetPointsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"leh-hand-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private Session Make()
    {
        var session = new Session(new Storage(_dir), new Guide { PassiveCap = 15, IdolCap = 8, Routes = { TrackerTests.MakeRoute("A") } },
            new SceneMap(), null, Path.Combine(_dir, "Filters"));
        string plan = Path.Combine(session.BuildsDir, "b.txt");
        File.WriteAllText(plan, "name: B\n");
        new BuildTree
        {
            Trees =
            {
                new TreeDef { Name = "Base", Nodes = { new TreeNode { Id = 1, Name = "A", Max = 8 }, new TreeNode { Id = 2, Name = "B", Max = 5 }, new TreeNode { Id = 7, Name = "Stray", Max = 2 } } },
                new TreeDef { Name = "Rive", Kind = TreeDef.SkillKind, TreeId = "rv", Nodes = { new TreeNode { Id = 4, Name = "Champion", Max = 4 }, new TreeNode { Id = 5, Name = "Flurry", Max = 4 } } },
            },
            Stages = { new TreeStage { Name = "Early", Level = 30, Passives = { 1, 1, 2, 2, 1 }, Skills = { ["Rive"] = new() { 4, 4, 5 } } } },
        }.Save(BuildTree.PathFor(plan));
        session.Profile.BuildPlan = "b.txt";
        session.ReloadPlan();
        session.Handle(new CharacterLevelEvent(5, 2, 0), live: false); // level 5 = 3 passive points
        return session;
    }

    [Fact]
    public void ClickingANode_StartsFromThePlan_ThenTheHandSetPointsAreTheTruth()
    {
        var session = Make();
        var passives = session.Tree!.Trees[0];
        Assert.False(session.TreeState(passives).FromGame);
        Assert.Equal(2, session.TreeState(passives).Allocated[1]); // plan: A, A, B

        // In the game the third point went into "Stray" instead of B.
        session.AdjustNode(passives, passives.Nodes[1], -1);
        session.AdjustNode(passives, passives.Nodes[2], +1);

        var state = session.TreeState(passives);
        Assert.True(state.FromGame);
        Assert.Equal(3, state.Points);
        Assert.False(state.Allocated.ContainsKey(2));
        Assert.Equal(new[] { 7 }, state.OffPlan);
        Assert.Equal(new NextRun(2, 2), state.Next[0]); // B is still owed, twice

        // Points never go above the node's maximum, and survive a restart.
        session.AdjustNode(passives, passives.Nodes[2], +5);
        Assert.Equal(2, session.TreeState(passives).Allocated[7]);
        var restarted = Make();
        Assert.Equal(2, restarted.TreeState(restarted.Tree!.Trees[0]).Allocated[7]);
    }

    [Fact]
    public void SkillTrees_AreSetIndependently_AndCanGoBackToThePlan()
    {
        var session = Make();
        var rive = session.Tree!.Trees[1];

        session.AdjustNode(rive, rive.Nodes[1], +1);
        Assert.True(session.TreeState(rive).FromGame);
        Assert.Equal(1, session.TreeState(rive).Allocated[5]);
        Assert.False(session.TreeState(session.Tree.Trees[0]).FromGame); // passives untouched

        session.ResetActual(rive);
        Assert.False(session.TreeState(rive).FromGame);
        Assert.Null(session.Profile.Actual);
    }
}
