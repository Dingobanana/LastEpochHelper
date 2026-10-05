using System.IO;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public class LootFilterTests
{
    private static BuildTree Build() => new()
    {
        Name = "Test build",
        Stages =
        {
            new TreeStage
            {
                Name = "Early", Level = 20,
                Gear =
                {
                    new GearItem { Slot = "Helmet", Name = "Iron Casque", AffixIds = { 25, 7 } },
                    new GearItem { Slot = "Body", Name = "Calamity", Rarity = "unique", UniqueId = 9, AffixIds = { 25 } },
                },
                Idols = { new GearItem { Name = "Small Idol", AffixIds = { 800 } } },
            },
        },
    };

    [Fact]
    public void Generate_WritesTheGamesFormat_WithTheHideAllRuleAsLowestPriority()
    {
        string xml = LootFilters.Generate(Build(), "LEH Test build")!;
        var root = XDocument.Parse(xml).Root!;
        var rules = root.Element("rules")!.Elements("Rule").ToList();

        Assert.Equal("ItemFilter", root.Name.LocalName);
        Assert.Equal("LEH Test build", (string?)root.Element("name"));
        Assert.Equal("9", (string?)root.Element("lootFilterVersion"));

        // The game checks Order 0 first; the file lists the lowest priority first.
        Assert.Equal(Enumerable.Range(0, rules.Count).Reverse(), rules.Select(r => (int)r.Element("Order")!));
        Assert.Equal("HIDE", (string?)rules[0].Element("type"));
        Assert.Equal("NORMAL MAGIC RARE", rules[0].Descendants("rarity").Single().Value);
        Assert.All(rules.Skip(1), r => Assert.Equal("SHOW", (string?)r.Element("type")));

        var top = rules[^1];
        Assert.Equal("Uniques the build uses", (string?)top.Element("nameOverride"));
        Assert.Equal(new[] { 9 }, top.Descendants("UniqueId").Select(e => (int)e));

        // Gear affixes and idol affixes go to different rules; each affix id is listed once.
        var twoAffixes = rules.Single(r => (string?)r.Element("nameOverride") == "Two or more build affixes");
        Assert.Equal(new[] { 7, 25 }, twoAffixes.Descendants("affixes").Elements("int").Select(e => (int)e));
        Assert.Equal("2", twoAffixes.Descendants("minOnTheSameItem").Single().Value);
        var idols = rules.Single(r => (string?)r.Element("nameOverride") == "Idols with a build affix");
        Assert.Equal(new[] { 800 }, idols.Descendants("affixes").Elements("int").Select(e => (int)e));
        Assert.Contains(idols.Descendants("EquipmentType"), e => e.Value == "IDOL_1x1_ETERRA");

        // Conditions carry the i:type attribute the game's reader expects.
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        Assert.All(root.Descendants("Condition"), c => Assert.NotNull(c.Attribute(xsi + "type")));
        Assert.Contains("i:type=\"RarityCondition\"", xml);
    }

    [Fact]
    public void Generate_NeedsGearToWorkFrom()
    {
        Assert.Null(LootFilters.Generate(new BuildTree { Stages = { new TreeStage { Name = "Bare" } } }, "x"));
    }

    [Fact]
    public void Inspect_ReportsWhatIsNewerThanTheFilter()
    {
        // A filter that lists uniques 0..29 and affixes 0..59; the game also has 30, 31 and 60.
        string uniques = string.Concat(Enumerable.Range(0, 30).Select(i => $"<Uniques><UniqueId>{i}</UniqueId><Rolls /></Uniques>"));
        string affixes = string.Concat(Enumerable.Range(0, 60).Select(i => $"<int>{i}</int>"));
        string xml = $"""
            <ItemFilter xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><name>Old</name><lootFilterVersion>3</lootFilterVersion><rules>
              <Rule><type>SHOW</type><conditions><Condition i:type="UniqueModifiersCondition">{uniques}</Condition></conditions><isEnabled>true</isEnabled></Rule>
              <Rule><type>SHOW</type><conditions><Condition i:type="AffixCondition"><affixes>{affixes}</affixes></Condition></conditions><isEnabled>false</isEnabled></Rule>
            </rules></ItemFilter>
            """;
        var game = JsonNode.Parse("""
            { "uniques": [ { "uniqueID": 5, "name": "Old Thing" }, { "uniqueID": 30, "name": "Frostborn Crown" }, { "uniqueID": 31, "name": "x", "displayName": "Morditas' Fang" } ],
              "affixes": [ { "affixId": 3, "affixName": "Old Affix" }, { "affixId": 60, "affixName": "Bloodrage Duration" } ] }
            """);

        var report = LootFilters.Inspect(xml, game);

        Assert.Equal((3, 2, 1), (report.Version, report.Rules, report.Disabled));
        Assert.Equal(new[] { "Frostborn Crown", "Morditas' Fang" }, report.NewerUniques);
        Assert.Equal(new[] { "Bloodrage Duration" }, report.NewerAffixes);
        Assert.Contains(report.Notes, n => n.Contains("format 3"));
    }

    [Fact]
    public void Inspect_DoesNotCallANarrowFilterOutdated()
    {
        string xml = """
            <ItemFilter><lootFilterVersion>9</lootFilterVersion><rules>
              <Rule><type>SHOW</type><conditions><Condition><Uniques><UniqueId>4</UniqueId></Uniques></Condition></conditions><isEnabled>true</isEnabled></Rule>
            </rules></ItemFilter>
            """;
        var game = JsonNode.Parse("""{ "uniques": [ { "uniqueID": 400, "name": "New" } ], "affixes": [] }""");

        var report = LootFilters.Inspect(xml, game);

        Assert.Equal(0, report.NewerUniqueCount);
        Assert.Empty(report.Notes);
    }

    [Fact]
    public void Session_WritesTheGeneratedFilterWhereTheGameLooks()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"leh-filter-{Guid.NewGuid():N}");
        try
        {
            var storage = new Storage(dir);
            var session = new Session(storage, new Guide { PassiveCap = 15, IdolCap = 8, Routes = { TrackerTests.MakeRoute("A") } },
                new SceneMap(), null, Path.Combine(dir, "Filters"));
            Assert.Null(session.GenerateFilter()); // no build yet

            string plan = Path.Combine(session.BuildsDir, "b.txt");
            File.WriteAllText(plan, "name: B\n");
            Build().Save(BuildTree.PathFor(plan));
            session.Profile.BuildPlan = "b.txt";
            session.ReloadPlan();

            string name = session.GenerateFilter()!;

            Assert.Equal("LEH Test build", name);
            Assert.Contains(name, session.InstalledFilters());
            byte[] bytes = File.ReadAllBytes(Path.Combine(dir, "Filters", name + ".xml"));
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3)); // byte order mark, as the game writes
            Assert.Equal(8, session.InspectFilter(name)!.Rules);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

public class ProfileSyncTests
{
    private const string Profile = """
        { "charInfo": { "characterName": "Hero", "lastUpdated": "2026-10-01T20:00:00Z" },
          "buildInfo": { "level": 22, "class": 2, "mastery": 3, "data": {
            "charTree": { "treeID": "", "selected": { "1": 3, "2": 1, "7": 2 } },
            "skillTrees": [ { "treeID": "rv", "slotNumber": 0, "level": 6, "selected": { "4": 2, "6": 1 } } ] } } }
        """;

    private static BuildTree Make() => new()
    {
        Trees =
        {
            new TreeDef { Name = "Base", Nodes = { new TreeNode { Id = 1, Name = "A", Max = 8 }, new TreeNode { Id = 2, Name = "B", Max = 5 }, new TreeNode { Id = 7, Name = "Stray", Max = 5 } } },
            new TreeDef { Name = "Rive", Kind = TreeDef.SkillKind, TreeId = "rv", Nodes = { new TreeNode { Id = 4, Name = "Champion", Max = 4 }, new TreeNode { Id = 5, Name = "Flurry", Max = 4 }, new TreeNode { Id = 6, Name = "Other", Max = 1 } } },
            new TreeDef { Name = "Lunge", Kind = TreeDef.SkillKind, TreeId = "lu" },
        },
        Stages = { new TreeStage { Name = "Early", Level = 30, Passives = { 1, 1, 2, 2, 1, 1, 2 }, Skills = { ["Rive"] = new() { 4, 4, 5, 5 } } } },
    };

    [Fact]
    public void Parse_ReadsTreePoints_AndRejectsTheNoDataAnswer()
    {
        var actual = LetProfile.Parse(Profile)!;

        Assert.Equal(22, actual.Level);
        Assert.Equal(3, actual.Passives[1]);
        Assert.Equal(2, actual.Skills["rv"][4]);
        Assert.Null(LetProfile.Parse("""{"error":"No character data","code":"NO_CHARACTER_DATA"}"""));
        Assert.Null(LetProfile.Parse("<html>blocked</html>"));
    }

    [Fact]
    public void ActualPoints_AreShownAsTaken_AndNextSkipsWhatIsAlreadyThere()
    {
        var build = Make();
        var actual = LetProfile.Parse(Profile)!;

        var state = build.State(build.Trees[0], actual, level: 22)!;

        Assert.True(state.FromGame);
        Assert.Equal(6, state.Points);
        Assert.Equal(3, state.Allocated[1]);
        // The plan wants A,A,B,B,A,A,B. The character has A×3 and B×1, taken in its own order:
        // what is left is B, then A, then B.
        Assert.Equal(new[] { new NextRun(2, 1), new NextRun(1, 1), new NextRun(2, 1) }, state.Next);
        // Node 7 is not in the plan at all.
        Assert.Equal(new[] { 7 }, state.OffPlan);
    }

    [Fact]
    public void SkillTrees_AreMatchedByTheGamesTreeId()
    {
        var build = Make();
        var actual = LetProfile.Parse(Profile)!;

        var rive = build.State(build.Trees[1], actual, 22)!;
        Assert.Equal(new[] { new NextRun(5, 2) }, rive.Next); // both Champion points are in; Flurry ×2 remains
        Assert.Contains(6, rive.OffPlan);

        Assert.Null(build.State(build.Trees[2], actual, 22)); // the profile has nothing for Lunge: fall back to the plan
    }
}

public class EndgameDataTests
{
    [Fact]
    public void ShippedData_MapsItemTypesAndBlessingsToTimelines_AndHasTheReferencePages()
    {
        var endgame = EndgameData.LoadBundled();

        Assert.Equal(10, endgame.Timelines.Count);
        Assert.Equal(3, endgame.Dungeons.Count);
        Assert.Equal("The Black Sun", endgame.TimelineForItemType("Helmet")!.Name);
        Assert.Equal("Reign of Dragons", endgame.TimelineForItemType("2H Axes")!.Name);
        Assert.Equal("Spirits of Fire", endgame.TimelineForItemType("Boots")!.Name);
        Assert.Null(endgame.TimelineForItemType("Small Idol"));
        Assert.Equal("Reign of Dragons", endgame.TimelineOf("Grand Survival of Might")!.Name);
        Assert.Equal(new[] { "Morditas", "Prophecies" }, endgame.Reference.Select(p => p.Title));
    }
}

public class BuildSummaryTests
{
    private const string Plan = """
        name: Leveling
        2: Gathering Storm unlocks
        2: Passives [Primalist]: Natural Attunement (2/8)
        4: Specialize Gathering Storm
        5: Passives [Primalist]: Natural Attunement (8/8)
        8: Specialize Summon Thorn Totem
        9: Gathering Storm: Thunderous Strikes (4/4)
        10: Respec passives (same nodes, new order)
        10: Passives [Primalist]: Hunter's Restoration (3/5), Natural Attunement (8/8)
        14: Passives [Druid]: Chitinous Plating (1/7)
        17: Passives [Druid]: Chitinous Plating (5/7)
        18: Choose mastery: Shaman
        18: Passives [Shaman]: Shamanic Infusion (1/8)
        20: Specialize Summon Storm Totem (replaces Gathering Storm)
        """;

    [Fact]
    public void SaysWhichMasteryToChoose_AndThatPointsInAnotherTreeAreOnPurpose()
    {
        var summary = BuildSummary.From(BuildPlan.Parse(Plan));

        Assert.Contains(summary.Headlines, h => h.Contains("Mastery to choose: Shaman") && h.Contains("level 18"));
        Assert.Contains(summary.Headlines, h => h.Contains("5 points into the Druid tree") && h.Contains("does not make you a Druid"));
        Assert.Contains(summary.Headlines, h => h.Contains("Summon Thorn Totem, Summon Storm Totem") && !h.Contains("Gathering Storm"));
    }

    [Fact]
    public void ListsTheStepsInOrder_WithPassivePointsGroupedByTree()
    {
        var steps = BuildSummary.From(BuildPlan.Parse(Plan)).Steps;

        Assert.Equal(new[] { "passive", "specialize", "specialize", "respec", "passive", "mastery", "passive", "specialize" }, steps.Select(s => s.Kind));
        // The plan's numbers are running totals per node: 8 in one node, then 3 in another.
        Assert.Equal((2, 10), (steps[0].Level, steps[0].ToLevel));
        Assert.Contains("Primalist tree: 11 points", steps[0].Text);
        Assert.Contains("Druid tree: 5 points", steps[4].Text);
        Assert.Equal((14, 17), (steps[4].Level, steps[4].ToLevel));
        Assert.Contains("Choose your mastery: Shaman", steps[5].Text);
        Assert.Contains("takes the slot of Gathering Storm", steps[7].Text);
        // Skill points and unlocks are detail, not steps.
        Assert.DoesNotContain(steps, s => s.Text.Contains("Thunderous") || s.Text.Contains("unlocks"));
    }

    [Fact]
    public void APlanWithoutBuildLines_SaysSo()
    {
        var summary = BuildSummary.From(BuildPlan.Parse("name: Mine\n5: Buy potions\n"));
        Assert.Empty(summary.Steps);
        Assert.Contains(summary.Headlines, h => h.Contains("Nothing to summarise"));
    }

    [Fact]
    public void ProfilesByLevel_AreASequence_ProfilesAtOneLevel_AreAlternatives()
    {
        static System.Text.Json.Nodes.JsonNode Planner(params (string Name, int Level)[] profiles) => System.Text.Json.Nodes.JsonNode.Parse(
            "{\"profiles\":[" + string.Join(",", profiles.Select(p => $"{{\"name\":\"{p.Name}\",\"level\":{p.Level}}}")) + "]}")!;

        Assert.True(MaxrollImporter.Shape(Planner(("Starting Setup (lvl 1 - 9)", 9), ("Early Setup (lvl 10 - 34)", 34), ("Final Setup", 79))).Sequential);
        Assert.True(MaxrollImporter.Shape(Planner(("Only", 100))).Sequential);

        var guide = MaxrollImporter.Shape(Planner(("Starter", 100), ("Endgame", 100), ("Aspirational", 100)));
        Assert.False(guide.Sequential);
        Assert.Equal(new[] { "Starter", "Endgame", "Aspirational" }, guide.Names);
        // A level or two apart is still the same build in another dress, not a later step.
        Assert.False(MaxrollImporter.Shape(Planner(("Starting Gear", 100), ("Endgame Gear", 100), ("Aspirational Gear", 99), ("HC Shield Variant", 100))).Sequential);
    }

    [Theory]
    [InlineData("https://maxroll.gg/last-epoch/planner/3k9hk0gr")]
    [InlineData("  https://maxroll.gg/last-epoch/planner/3k9hk0gr#2  ")]
    [InlineData("https://maxroll.gg/last-epoch/planner/3k9hk0gr?utm_source=share")]
    [InlineData("maxroll.gg/last-epoch/planner/3k9hk0gr")]
    [InlineData("HTTPS://MAXROLL.GG/LAST-EPOCH/PLANNER/3K9HK0GR")]
    [InlineData("check this out https://maxroll.gg/last-epoch/planner/3k9hk0gr, it is good")]
    [InlineData("<https://maxroll.gg/last-epoch/planner/3k9hk0gr>")]
    [InlineData("3k9hk0gr")]
    [InlineData("\"3K9HK0GR\"")]
    public void APlannerLink_IsUnderstood_InEveryShapeItGetsPastedIn(string pasted)
    {
        Assert.Equal("3k9hk0gr", MaxrollImporter.Understand(pasted).PlannerId);
    }

    [Theory]
    [InlineData("https://maxroll.gg/last-epoch/planner/y21h9h0x#2", 1)]
    [InlineData("https://maxroll.gg/last-epoch/planner/y21h9h0x#1", 0)]
    [InlineData("https://maxroll.gg/last-epoch/planner/y21h9h0x#3&abc", 2)]
    [InlineData("https://maxroll.gg/last-epoch/planner/y21h9h0x", null)]
    [InlineData("https://maxroll.gg/last-epoch/planner/y21h9h0x#0", null)]
    [InlineData("https://maxroll.gg/last-epoch/planner/y21h9h0x#skills", null)]
    public void TheNumberOnASharedLink_IsTheVersionItShowed(string link, int? version)
    {
        Assert.Equal(version, MaxrollImporter.Understand(link).Variant);
    }

    [Fact]
    public void AGuidePage_SaysWhichVersionItIsAbout()
    {
        const string page = "<div data-le-profile=\"y21h9h0x\" data-le-type=\"skillbar\" data-le-id=\"4\"></div>"
                            + "<div data-le-profile=\"y21h9h0x\" data-le-id=\"2\" data-le-type=\"plannerEquipment\"></div>"
                            + "<div data-le-profile=\"y21h9h0x\" data-le-id=\"2\" data-le-type=\"plannerSkills\"></div>"
                            + "<div data-le-profile=\"zz99zz99\" data-le-id=\"3\" data-le-type=\"plannerPassives\"></div>";
        Assert.Equal(1, MaxrollImporter.PickVariant(page, "y21h9h0x")); // the second version, counted from 0
        Assert.Equal(2, MaxrollImporter.PickVariant(page, "zz99zz99"));
        Assert.Null(MaxrollImporter.PickVariant(page, "ab12cd34"));
        Assert.Null(MaxrollImporter.PickVariant("<p>nothing</p>", "y21h9h0x"));
    }

    [Theory]
    [InlineData("https://maxroll.gg/last-epoch/build-guides/shaman-leveling-guide", "https://maxroll.gg/last-epoch/build-guides/shaman-leveling-guide")]
    [InlineData("maxroll.gg/last-epoch/build-guides/shaman-leveling-guide#skills", "https://maxroll.gg/last-epoch/build-guides/shaman-leveling-guide#skills")]
    [InlineData("http://www.maxroll.gg/last-epoch/build-guides/x-guide.", "https://www.maxroll.gg/last-epoch/build-guides/x-guide")]
    [InlineData("guide: https://maxroll.gg/last-epoch/build-guides/x-guide?a=1 (the starter)", "https://maxroll.gg/last-epoch/build-guides/x-guide?a=1")]
    public void AGuideLink_BecomesAPageToRead(string pasted, string page)
    {
        var understood = MaxrollImporter.Understand(pasted);
        Assert.Null(understood.PlannerId);
        Assert.Equal(page, understood.PageUrl);
    }

    [Theory]
    [InlineData("", "Paste a Maxroll")]
    [InlineData("   ", "Paste a Maxroll")]
    [InlineData("https://www.lastepochtools.com/planner/AbCdEf12", "Last Epoch Tools")]
    [InlineData("https://maxroll.gg/last-epoch/planner", "not one build")]
    [InlineData("https://maxroll.gg/last-epoch/planner/community-builds", "not one build")]
    [InlineData("https://maxroll.gg/d4/planner/ab12cd34", "another game")]
    [InlineData("https://www.youtube.com/watch?v=abc", "Only Maxroll")]
    [InlineData("hello there", "does not look like")]
    [InlineData("community", "does not look like")]
    public void AnythingElse_IsRefusedWithTheReason(string pasted, string reason)
    {
        var understood = MaxrollImporter.Understand(pasted);
        Assert.Null(understood.PlannerId);
        Assert.Null(understood.PageUrl);
        Assert.Contains(reason, understood.Problem);
    }

    [Fact]
    public void ProfilesThatGrowInPoints_AreStagesToo_EvenWhenAllAreLeftAtLevel100()
    {
        static System.Text.Json.Nodes.JsonNode Planner(params (string Name, int Level, int Points)[] profiles) => System.Text.Json.Nodes.JsonNode.Parse(
            "{\"profiles\":[" + string.Join(",", profiles.Select(p =>
                $"{{\"name\":\"{p.Name}\",\"level\":{p.Level},\"passives\":{{\"history\":[{string.Join(",", Enumerable.Repeat("1", p.Points))}],\"position\":{p.Points}}}}}")) + "]}")!;

        // "Campaign" at 60, then two profiles both left at 100 but with more points each time.
        Assert.True(MaxrollImporter.Shape(Planner(("Campaign", 60, 73), ("Early Endgame", 100, 111), ("Endgame", 100, 113))).Sequential);
        Assert.True(MaxrollImporter.Shape(Planner(("Early", 100, 30), ("Mid", 100, 60), ("Late", 100, 113))).Sequential);
        // The same points in every profile: alternatives. So are profiles where an early stage has two alternatives.
        Assert.False(MaxrollImporter.Shape(Planner(("Starter", 100, 113), ("Endgame", 100, 113))).Sequential);
        Assert.False(MaxrollImporter.Shape(Planner(("Early", 100, 30), ("Early, low life", 100, 30), ("Mid", 100, 60))).Sequential);
        // Two unrelated sets that differ in level: the second has fewer points, so it is no continuation.
        Assert.False(MaxrollImporter.Shape(Planner(("Set 1", 86, 30), ("Set 2", 100, 0))).Sequential);
        // A gear-only profile with no trees among full ones is an alternative, not a step back.
        Assert.False(MaxrollImporter.Shape(Planner(("Leveling", 100, 113), ("Leveling uniques", 100, 0), ("Stacker", 100, 113))).Sequential);
    }

    [Fact]
    public void AGuidePage_GivesItsOwnPlanner_AnOverviewPageIsRefused()
    {
        static string Embed(string id) => $"<div data-le-profile=\"{id}\" data-le-id=\"2\"></div>";

        // The guide's planner is embedded many times; one tree is borrowed from another planner.
        Assert.Equal("kg56tr03", MaxrollImporter.PickPlanner(Embed("kg56tr03") + Embed("kg56tr03") + Embed("20tfn0go") + Embed("kg56tr03")));
        Assert.Equal("abc12345", MaxrollImporter.PickPlanner(Embed("abc12345")));
        Assert.Equal("zz99zz99", MaxrollImporter.PickPlanner("<a href=\"https://maxroll.gg/last-epoch/planner/zz99zz99\">planner</a>"));
        Assert.Null(MaxrollImporter.PickPlanner("<p>nothing here</p>"));
        var refused = Assert.Throws<InvalidDataException>(() => MaxrollImporter.PickPlanner(Embed("aaaaaa11") + Embed("bbbbbb22") + Embed("cccccc33")));
        Assert.Contains("several builds", refused.Message);
    }

    [Fact]
    public void AnAnswerCounts_OnceItHasComeTwiceInARow()
    {
        var reads = new StableReads();
        Assert.False(reads.Twice("skill", "Flame Ward"));
        Assert.False(reads.Twice("skill", "Mana Strike"));  // one odd look
        Assert.False(reads.Twice("skill", "Flame Ward"));
        Assert.True(reads.Twice("skill", "Flame Ward"));
        Assert.True(reads.Twice("skill", "Flame Ward"));
        Assert.False(reads.Twice("tab", "Flame Ward"));     // another question has its own count
        reads.ForgetAnswers();
        Assert.False(reads.Twice("skill", "Flame Ward"));
    }

    [Fact]
    public void AValueIsOnlyPassedOn_WhenTwoLooksInARowAgree()
    {
        var reads = new StableReads();
        Assert.Empty(reads.Confirm("tree", new Dictionary<int, int> { [1] = 2, [2] = 0 }));
        // Node 1 wobbles, node 2 holds, node 3 is new.
        var second = reads.Confirm("tree", new Dictionary<int, int> { [1] = 7, [2] = 0, [3] = 1 });
        Assert.Equal(new Dictionary<int, int> { [2] = 0 }, second);
        var third = reads.Confirm("tree", new Dictionary<int, int> { [1] = 7, [3] = 1 });
        Assert.Equal(new Dictionary<int, int> { [1] = 7, [3] = 1 }, third);
        Assert.Empty(reads.Confirm("other tree", new Dictionary<int, int> { [1] = 7 }));
    }
}
