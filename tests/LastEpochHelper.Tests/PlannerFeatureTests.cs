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

public class ItemCheckTests
{
    private static TreeStage Stage() => new()
    {
        Gear =
        {
            new GearItem { Slot = "Ring 1", Name = "Flames of Midnight", Rarity = "unique", Affixes = { "Added Health T5", "Hybrid Health T5" } },
            new GearItem { Slot = "Helmet", Name = "Augury Helm", Affixes = { "Void Resistance T3", "Increased Melee Attack Speed T5" } },
        },
    };

    [Fact]
    public void FindsBuildAffixes_InTooltipText()
    {
        var lines = new[] { new ScreenLine("Rusted Coif", 20), new ScreenLine("+45 Health", 14), new ScreenLine("+12% Void Resistance", 14), new ScreenLine("+3 Mana", 14) };

        string result = ItemCheck.Describe(lines, Stage());

        Assert.Contains("2 build affixes", result);
        Assert.Contains("Added Health", result);
        Assert.Contains("Void Resistance", result);
    }

    [Fact]
    public void RecognisesANamedItem_AndSaysSoWhenNothingMatches()
    {
        Assert.Contains("the build's Flames of Midnight (Ring 1)", ItemCheck.Describe(new[] { new ScreenLine("FLAMES OF MIDNIGHT", 22) }, Stage()));
        Assert.Contains("right base: Augury Helm", ItemCheck.Describe(new[] { new ScreenLine("Augury Helm", 22) }, Stage()));
        Assert.Contains("none of the build's", ItemCheck.Describe(new[] { new ScreenLine("+3 Mana", 14) }, Stage()));
        Assert.Contains("no text found", ItemCheck.Describe(Array.Empty<ScreenLine>(), Stage()));
    }
}

public class EndgameDataTests
{
    [Fact]
    public void ShippedData_MapsItemTypesAndBlessingsToTimelines_AndHasTheReferencePages()
    {
        var endgame = EndgameData.Load(Path.Combine(AppContext.BaseDirectory, "Data", "endgame.json"));

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
