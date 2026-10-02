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

        // The game's own headings, as read from a real 1.5 screen.
        var real = new[] { new ScreenLine("PASSIVES", 25, 2117, 137, 185), new ScreenLine("PRIMALIST", 26, 2790, 136, 300), new ScreenLine("1 UNSPENT POINTS", 18, 2757, 231, 280) };
        var heading = PanelDetector.Detect(real, Tabs, Skills);
        Assert.Equal(GamePanel.Passives, heading.Panel);
        Assert.Equal("PASSIVES", heading.Anchor!.Text);
    }

    [Fact]
    public void SkillPanel_GivesTheOpenSkill_OrJustThePanelWhenSeveralAreListed()
    {
        var tree = new[] { new ScreenLine("GATHERING STORM", 34, 2000, 140, 380), new ScreenLine("Level 6", 18) };
        var reading = PanelDetector.Detect(tree, Tabs, Skills);
        Assert.Equal((GamePanel.Skills, "Gathering Storm"), (reading.Panel, reading.Skill));

        // The overview names every skill at one size, and every mastery too ("...in the Shaman passive tree").
        var overview = new[]
        {
            new ScreenLine("SKILLS & SPECIALIZATIONS", 32, 1435, 82, 500), new ScreenLine("PRIMALIST", 20), new ScreenLine("BEASTMASTER", 20),
            new ScreenLine("Unlocked by spending points in the Shaman passive tree.", 21),
            new ScreenLine("Gathering Storm", 19), new ScreenLine("Spriggan Form", 19),
        };
        var list = PanelDetector.Detect(overview, Tabs, Skills, GamePanel.Passives);
        Assert.Equal(GamePanel.Skills, list.Panel);
        Assert.Null(list.Skill);
        Assert.Equal(82, list.Anchor!.Y);
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

public class SkillTreeWithUnspentPointsTests
{
    [Fact]
    public void AnOpenSkillTreeWithPointsToSpend_IsNotThePassivePanel()
    {
        // As read from the game: this tree had a point left, and "UNSPENT POINT" used to mean "passives".
        var tabs = new[] { "Primalist", "Beastmaster", "Shaman", "Druid" };
        var skills = new[] { "Gathering Storm", "Summon Thorn Totem", "Summon Storm Totem", "Spriggan Form" };
        var screen = new[]
        {
            new ScreenLine("SUMMON THORN TOTEM", 21, 1435, 156, 400), new ScreenLine("Minimum Specialized Level: 3", 19, 1434, 259, 300),
            new ScreenLine("1 UNSPENT POINT", 19, 2444, 307, 200), new ScreenLine("LEVEL 5", 16, 1436, 208, 80),
            new ScreenLine("RESPEC", 15, 3487, 178, 80), new ScreenLine("BACK", 14, 1556, 101, 60), new ScreenLine("0/5", 15, 2000, 725, 30),
        };
        foreach (var hint in new[] { GamePanel.None, GamePanel.Passives, GamePanel.Skills })
        {
            var reading = PanelDetector.Detect(screen, tabs, skills, hint);
            Assert.Equal(GamePanel.Skills, reading.Panel);
            Assert.Equal("Summon Thorn Totem", reading.Skill);
        }

        // Even with the class names in view (a tooltip, chat), a skill tree is a skill tree.
        var noisy = screen.Concat(new[] { new ScreenLine("Primalist", 16), new ScreenLine("any Druid builds?", 16) }).ToArray();
        Assert.Equal(GamePanel.Skills, PanelDetector.Detect(noisy, tabs, skills).Panel);
    }

    [Fact]
    public void AnySkillTree_IsASkillPanel_EvenOneTheBuildDoesNotKnow()
    {
        var tabs = new[] { "Sentinel", "Void Knight", "Forge Guard", "Paladin" };
        var skills = new[] { "Smite", "Judgement", "Holy Aura" };
        // A skill the imported build does not use: its name means nothing to the overlay.
        var unknown = new[]
        {
            new ScreenLine("SHIELD RUSH", 21, 1435, 156, 300), new ScreenLine("Minimum Specialized Level: 1", 19, 1434, 259, 300),
            new ScreenLine("LEVEL 3", 16, 1436, 208, 80), new ScreenLine("0/5", 15, 2000, 725, 30),
        };
        var reading = PanelDetector.Detect(unknown, tabs, skills, GamePanel.Skills);
        Assert.Equal(GamePanel.Skills, reading.Panel);
        Assert.Null(reading.Skill);
        Assert.NotNull(reading.Anchor);

        // Without that line, the BACK and RESPEC buttons together say the same.
        var buttons = new[] { new ScreenLine("SHIELD RUSH", 21), new ScreenLine("BACK", 14), new ScreenLine("RESPEC", 15) };
        Assert.Equal(GamePanel.Skills, PanelDetector.Detect(buttons, tabs, skills).Panel);
        Assert.Equal(GamePanel.None, PanelDetector.Detect(new[] { new ScreenLine("BACK", 14) }, tabs, skills).Panel);

        // A heading read with a wrong letter is still matched to its skill.
        var misread = new[] { new ScreenLine("JUDGEMEMT", 21, 1435, 156, 300), new ScreenLine("Minimum Specialized Level: 4", 19) };
        Assert.Equal("Judgement", PanelDetector.Detect(misread, tabs, skills).Skill);
    }

    [Fact]
    public void LookingOnSpec_OnlyBelievesTheGamesOwnHeadings()
    {
        var tabs = new[] { "Primalist", "Beastmaster", "Shaman", "Druid" };
        var skills = new[] { "Gathering Storm", "Summon Thorn Totem" };
        PanelReading Strict(params ScreenLine[] lines) => PanelDetector.Detect(lines, tabs, skills, strict: true);

        Assert.Equal(GamePanel.Passives, Strict(new ScreenLine("PASSIVES", 25), new ScreenLine("PRIMALIST", 26)).Panel);
        Assert.Equal(GamePanel.Skills, Strict(new ScreenLine("SKILLS & SPECIALIZATIONS", 32)).Panel);
        var tree = Strict(new ScreenLine("GATHERING STORM", 21), new ScreenLine("Minimum Specialized Level: 3", 19));
        Assert.Equal((GamePanel.Skills, "Gathering Storm"), (tree.Panel, tree.Skill));

        // Names alone - a tooltip, chat, the skill bar - open nothing.
        Assert.Equal(GamePanel.None, Strict(new ScreenLine("Primalist", 20), new ScreenLine("Druid", 20)).Panel);
        Assert.Equal(GamePanel.None, Strict(new ScreenLine("GATHERING STORM", 21)).Panel);
        Assert.Equal(GamePanel.None, Strict(new ScreenLine("Gathering Storm", 16), new ScreenLine("Summon Thorn Totem", 16)).Panel);
    }

    [Fact]
    public void TheHeadingAboveTheMarker_NamesTheOpenSkill_WhateverElseIsOnScreen()
    {
        var tabs = new[] { "Primalist", "Beastmaster", "Shaman", "Druid" };
        var skills = new[] { "Gathering Storm", "Summon Thorn Totem", "Summon Storm Totem", "Spriggan Form", "Eterra's Blessing" };
        // The skill bar at the bottom prints skill names almost as large as the heading.
        var screen = new[]
        {
            new ScreenLine("SUMMON THORN TOTEM", 20, 1435, 156, 400), new ScreenLine("LEVEL 6", 16, 1436, 208, 80),
            new ScreenLine("Minimum Specialized Level: 3", 19, 1434, 259, 300), new ScreenLine("BACK", 14, 1556, 101, 60),
            new ScreenLine("ETERRA'S BLESSING", 18, 2351, 1319, 250), new ScreenLine("GATHERING STORM", 18, 2000, 1319, 250),
        };
        var reading = PanelDetector.Detect(screen, tabs, skills, GamePanel.Skills);
        Assert.Equal((GamePanel.Skills, "Summon Thorn Totem"), (reading.Panel, reading.Skill));
        Assert.Equal("Summon Thorn Totem", PanelDetector.Detect(screen, tabs, skills, strict: true).Skill);
    }

    [Fact]
    public void ThePassiveTabShowing_IsReadFromItsTitle()
    {
        var tabs = new[] { "Primalist", "Beastmaster", "Shaman", "Druid" };
        var skills = new[] { "Gathering Storm" };
        // As read from the game: the title beside the heading, the list of all tabs further left.
        ScreenLine[] Panel(string title) => new[]
        {
            new ScreenLine("PASSIVES", 25, 2117, 137, 185), new ScreenLine(title, 26, 2790, 136, 300),
            new ScreenLine("PRIMALIST", 20, 1664, 126, 150), new ScreenLine("BEASTMASTER", 15, 1513, 543, 150),
            new ScreenLine("SHAMAN", 19, 1859, 536, 100), new ScreenLine("1 UNSPENT POINTS", 21, 2757, 231, 280),
        };
        Assert.Equal("Primalist", PanelDetector.Detect(Panel("PRIMAL 1ST"), tabs, skills).Tab);
        // The spot to keep an eye on covers the heading and the title beside it.
        var anchor = PanelDetector.Detect(Panel("PRIMAL 1ST"), tabs, skills).Anchor!;
        Assert.Equal((2117, 973), (anchor.X, anchor.Width));
        Assert.Equal("Shaman", PanelDetector.Detect(Panel("SHAMAN"), tabs, skills).Tab);
        Assert.Equal("Beastmaster", PanelDetector.Detect(Panel("BEASTMASTER"), tabs, skills).Tab);
        Assert.Null(PanelDetector.Detect(Panel("~~~"), tabs, skills).Tab);
        Assert.Equal(GamePanel.Passives, PanelDetector.Detect(Panel("~~~"), tabs, skills).Panel);
    }

    [Fact]
    public void LabelsFromTwoReads_AreCombinedWithoutDoubles()
    {
        var first = new[] { new TreeReader.Token(100, 100, 1, 3), new TreeReader.Token(400, 100, 0, 4) };
        var second = new[] { new TreeReader.Token(104, 97, 7, 8), new TreeReader.Token(700, 300, 2, 4) };
        var merged = TreeReader.Merge(first, second);
        Assert.Equal(3, merged.Count);
        Assert.Contains(merged, m => m.Have == 1 && m.Max == 3); // the first read wins where both saw a label
        Assert.Contains(merged, m => m.X == 700);
    }
}

public class MapCounterTests
{
    [Fact]
    public void FindsThePairOfCounters_HoweverTheyAreSpaced()
    {
        Assert.Equal((3, 1), MapCounters.Parse(new[] { new ScreenLine("3/15", 16, 4800, 1300, 40), new ScreenLine("1/8", 16, 4900, 1300, 30) }, 15, 8));
        Assert.Equal((13, 8), MapCounters.Parse(new[] { new ScreenLine("Passive Points 13 / 15   Idol Slots 8/8", 16, 60, 1300, 500) }, 15, 8));
        Assert.Equal((0, 0), MapCounters.Parse(new[] { new ScreenLine("O/15", 16, 60, 1300, 40), new ScreenLine("o/8", 16, 60, 1330, 30) }, 15, 8));
    }

    [Fact]
    public void ReadsTheMapCorner_AsTheGameShowsIt()
    {
        // As read from a real screenshot of the map (5120x1440).
        var corner = new[]
        {
            new ScreenLine("PASSIVE POINTS REWARDS (6/15)", 20, 33, 1247, 364), new ScreenLine("IDOL SLOT REWARDS (1/8)", 20, 33, 1293, 289),
            new ScreenLine("RESET VIEW", 15, 110, 1400, 120), new ScreenLine("HIDE QUESTS", 15, 320, 1400, 130),
        };
        Assert.Equal((6, 1), MapCounters.Parse(corner, 15, 8));
    }

    [Fact]
    public void NeedsBothCounters_CloseTogether_AndWithinTheirCaps()
    {
        Assert.Null(MapCounters.Parse(new[] { new ScreenLine("3/15", 16, 100, 100, 40), new ScreenLine("78/78", 16, 150, 100, 40) }, 15, 8));
        Assert.Null(MapCounters.Parse(new[] { new ScreenLine("3/15", 16, 100, 100, 40), new ScreenLine("1/8", 16, 4000, 1300, 30) }, 15, 8));
        Assert.Null(MapCounters.Parse(new[] { new ScreenLine("16/15", 16, 100, 100, 40), new ScreenLine("9/8", 16, 150, 100, 30) }, 15, 8));
        Assert.Null(MapCounters.Parse(new[] { new ScreenLine("3/150", 16, 100, 100, 40), new ScreenLine("1/80", 16, 150, 100, 30) }, 15, 8));
        // One counter without its partner is reported, so the caller can take a closer look.
        Assert.Null(MapCounters.Parse(new[] { new ScreenLine("3/15", 16, 100, 100, 40) }, 15, 8, out bool half));
        Assert.True(half);
        Assert.Null(MapCounters.Parse(new[] { new ScreenLine("Find Artem's stash", 16, 100, 100, 40) }, 15, 8, out half));
        Assert.False(half);
        // "13/15" is thirteen, not also three.
        Assert.Equal((13, 2), MapCounters.Parse(new[] { new ScreenLine("13/15", 16, 100, 100, 40), new ScreenLine("2/8", 16, 150, 100, 30) }, 15, 8));
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
    public void PointsReadFromTheScreen_ReplaceThePlansGuess_ButLeaveUnseenNodesAlone()
    {
        var session = Make();
        var passives = session.Tree!.Trees[0];

        Assert.True(session.SetReadPoints(passives, new Dictionary<int, int> { [1] = 3, [2] = 0 }));
        Assert.Equal(3, session.TreeState(passives).Allocated[1]);
        Assert.False(session.TreeState(passives).Allocated.ContainsKey(2));

        // A later read that could not see node 1 (tooltip in the way) must not wipe it.
        Assert.True(session.SetReadPoints(passives, new Dictionary<int, int> { [7] = 1 }));
        Assert.Equal(3, session.TreeState(passives).Allocated[1]);
        Assert.False(session.SetReadPoints(passives, new Dictionary<int, int> { [7] = 1 })); // nothing new
    }

    [Fact]
    public void TheSlider_SetsHowManyPointsThePlanPlaces()
    {
        var session = Make();
        var passives = session.Tree!.Trees[0];
        var rive = session.Tree.Trees[1];
        Assert.Equal(3, session.PassivePointsByLevel());

        session.SetTreePoints(passives, 5);
        Assert.Equal(5, session.TreeState(passives).Points);
        Assert.Equal(2, session.Profile.PassiveOffset);

        session.SetTreePoints(rive, 2);
        Assert.Equal(2, session.TreeState(rive).Allocated[4]);
        Assert.Equal(new NextRun(5, 1), session.TreeState(rive).Next[0]);
    }

    [Fact]
    public void MovingTheSlider_ShowsThePlan_EvenWhenTheGamesPointsAreKnown_AndTheSwitchGoesBack()
    {
        var session = Make();
        var passives = session.Tree!.Trees[0];
        session.SetReadPoints(passives, new Dictionary<int, int> { [1] = 1, [7] = 2 });
        Assert.True(session.TreeState(passives).FromGame);
        Assert.Equal(3, session.ActualPoints(passives));

        session.SetTreePoints(passives, 5); // "show me the build at 5 points"
        var plan = session.TreeState(passives);
        Assert.False(plan.FromGame);
        Assert.Equal(5, plan.Points);

        // New readings keep arriving while the game's panel is open; the chosen view stays put.
        session.SetReadPoints(passives, new Dictionary<int, int> { [1] = 2 });
        Assert.False(session.TreeState(passives).FromGame);

        session.SetPlanView(passives, false);
        var game = session.TreeState(passives);
        Assert.True(game.FromGame);
        Assert.Equal(2, game.Allocated[1]);

        // Correcting a node by hand is about the real points, so it leaves the plan view too.
        session.SetTreePoints(passives, 4);
        session.AdjustNode(passives, passives.Nodes[0], +1);
        Assert.True(session.TreeState(passives).FromGame);

        // A preview lasts while the tree is open: closing it goes back to mirroring the game.
        session.SetTreePoints(passives, 4);
        Assert.False(session.TreeState(passives).FromGame);
        session.ClearPlanViews();
        Assert.True(session.TreeState(passives).FromGame);
    }

    [Fact]
    public void AStageChosenByHand_IsUsed_UntilLevelIsFollowedAgain()
    {
        var session = Make();
        var build = session.Tree!;
        build.Stages.Add(new TreeStage { Name = "Later", Level = 90, Passives = { 7, 7, 7, 1 }, Skills = { [build.Trees[1].Name] = new() { 5, 5, 5 } } });
        var passives = build.Trees[0];
        var first = build.Stages[0];

        Assert.Null(session.PinnedStage);
        Assert.Equal(first, session.Stage); // by level

        session.SetStage(build.Stages[1]);
        Assert.Equal("Later", session.Stage!.Name);
        Assert.Equal("Later", session.TreeState(passives).Stage);
        Assert.Equal("Later", session.TreeState(build.Trees[1]).Stage);

        session.SetStage(null);
        Assert.Equal(first, session.Stage);
        Assert.Equal(first.Name, session.TreeState(passives).Stage);
    }

    [Fact]
    public void SkillsSeenWithPointsInTheGame_TickTheSpecializationReminders()
    {
        var session = Make();
        var rive = session.Tree!.Trees[1];
        Assert.DoesNotContain("milestone:4", session.Profile.PlanDone);

        session.SetReadPoints(rive, new Dictionary<int, int> { [4] = 1 });
        Assert.Contains("milestone:4", session.Profile.PlanDone);   // one skill specialized: the first slot is used
        Assert.DoesNotContain("milestone:8", session.Profile.PlanDone);
    }

    [Fact]
    public void ASkillReadAsEmpty_WhileThePlayerSaysItHasPoints_IsNotBelieved()
    {
        var session = Make();
        var rive = session.Tree!.Trees[1];
        session.SetTreePoints(rive, 2);
        session.SetPlanView(rive, false);

        Assert.False(session.SetReadPoints(rive, new Dictionary<int, int> { [4] = 0, [5] = 0 }));
        Assert.False(session.HasActual(rive));
        Assert.Equal(2, session.TreeState(rive).Points); // still the plan at 2 points

        // A reading with points in it is taken.
        Assert.True(session.SetReadPoints(rive, new Dictionary<int, int> { [4] = 1, [5] = 0 }));
        Assert.Equal(1, session.TreeState(rive).Allocated[4]);
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

public class TreeReaderTests
{
    // A tree laid out like the game's passive grid: columns 198.6 apart, rows 92.28 apart.
    private static TreeDef Tree()
    {
        var tree = new TreeDef { Name = "Primalist" };
        (int Id, int Column, int Row, int Max)[] nodes =
        {
            (1, 0, 0, 6), (2, 0, 1, 8), (3, 1, 0, 6), (4, 2, 1, 6), (5, 2, 4, 6), (6, 3, 0, 6), (7, 3, 2, 8), (8, 3, 3, 5), (9, 4, 1, 5), (10, 4, 3, 5), (11, 5, 4, 10),
        };
        foreach (var (id, column, row, max) in nodes)
            tree.Nodes.Add(new TreeNode { Id = id, Name = $"N{id}", Max = max, X = 51.78 + column * 198.6, Y = 115.93 + row * 92.28 });
        return tree;
    }

    private static ScreenLine Label(string text, double centreX, double centreY) => new(text, 16, centreX - 18, centreY - 8, 36);

    // The labels as they sit on a real 5120x1440 screen: the same grid at 4/3 size.
    private static List<ScreenLine> Screen(TreeDef tree, IReadOnlyDictionary<int, int> have, params int[] hidden) =>
        tree.Nodes.Where(n => !hidden.Contains(n.Id))
            .Select(n => Label($"{have.GetValueOrDefault(n.Id)}/{n.Max}", 2113 + n.X * 1.3333, 371 + n.Y * 1.3333)).ToList();

    [Fact]
    public void ReadsThePointsOfEachNode_FromLabelsAtAnotherScaleAndPosition()
    {
        var tree = Tree();
        var words = Screen(tree, new Dictionary<int, int> { [2] = 8, [4] = 2 });
        words.Add(new ScreenLine("PASSIVES", 25, 2117, 137, 185));
        words.Add(Label("78/78", 3138, 1322)); // the mana globe
        words.Add(Label("3/15", 830, 424));    // some other counter elsewhere on screen

        var points = TreeReader.Read(TreeReader.Tokens(words), tree)!;

        Assert.Equal(8, points[2]);
        Assert.Equal(2, points[4]);
        Assert.Equal(0, points[1]);
        Assert.Equal(tree.Nodes.Count, points.Count);
    }

    [Fact]
    public void NodesBehindATooltip_AreSimplyLeftOut()
    {
        var tree = Tree();

        var points = TreeReader.Read(TreeReader.Tokens(Screen(tree, new Dictionary<int, int> { [2] = 8 }, hidden: new[] { 5, 7, 8 })), tree)!;

        Assert.Equal(tree.Nodes.Count - 3, points.Count);
        Assert.False(points.ContainsKey(7));
    }

    [Fact]
    public void ToleratesLookAlikeCharacters_AndRejectsWhatIsNotANodeLabel()
    {
        var tokens = TreeReader.Tokens(new[] { new ScreenLine("O/6", 16), new ScreenLine("2/lO", 16), new ScreenLine("310/310", 16), new ScreenLine("13/5", 16), new ScreenLine("Level", 16) });

        Assert.Equal(new[] { (0, 6), (2, 10) }, tokens.Select(t => (t.Have, t.Max)));
    }

    [Fact]
    public void PicksTheTabThatIsShowing_AndGivesUpWhenNothingFits()
    {
        var shown = Tree();
        var other = new TreeDef { Name = "Druid" };
        foreach (var node in shown.Nodes)
            other.Nodes.Add(new TreeNode { Id = node.Id + 100, Name = node.Name, Max = node.Max == 6 ? 5 : node.Max + 1, X = node.Y * 2.1, Y = node.X * 0.4 });
        var tokens = TreeReader.Tokens(Screen(shown, new Dictionary<int, int> { [2] = 8 }));

        Assert.Equal("Primalist", TreeReader.ReadBest(tokens, new[] { other, shown })!.Value.Tree.Name);
        Assert.Null(TreeReader.Read(tokens, other));
        Assert.Null(TreeReader.Read(TreeReader.Tokens(new[] { Label("0/6", 10, 10), Label("0/5", 300, 40) }), shown));

        // Four labels that happen to line up are not enough when the tree has many more nodes.
        var few = TreeReader.Tokens(Screen(shown, new Dictionary<int, int>(), hidden: new[] { 5, 6, 7, 8, 9, 10, 11 }));
        Assert.Equal(4, few.Count);
        Assert.Null(TreeReader.Read(few, shown));
        // ...unless the tree is known to be the one showing: then they only have to be placed.
        Assert.Equal(4, TreeReader.Read(few, shown, known: true)!.Count);
    }
}
