using System.Xml.Linq;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public class FilterToolTests
{
    private static BuildTree Build(string name, int affix, int? unique = null) => new()
    {
        Name = name,
        Stages =
        {
            new TreeStage
            {
                Name = "Only", Level = 50,
                Gear = { new GearItem { Slot = "Helmet", Name = "Thing", Rarity = unique is null ? "" : "unique", UniqueId = unique, AffixIds = { affix, 25 }, Affixes = { "Added Health T5", "Void Resistance T3" } } },
            },
        },
    };

    [Fact]
    public void OneFilter_ForTwoBuilds_ShowsWhatEitherWants()
    {
        string xml = LootFilters.Generate(new[] { Build("Main", 7, unique: 9), Build("Alt", 300, unique: 12) }, "LEH Main + Alt")!;
        var rules = XDocument.Parse(xml).Root!.Element("rules")!.Elements("Rule").ToList();

        var top = rules[^1];
        Assert.Equal(new[] { 9, 12 }, top.Descendants("UniqueId").Select(e => (int)e));
        var twoAffixes = rules.Single(r => (string?)r.Element("nameOverride") == "Two or more build affixes");
        Assert.Equal(new[] { 7, 25, 300 }, twoAffixes.Descendants("affixes").Elements("int").Select(e => (int)e));
        Assert.Contains("Main + Alt", XDocument.Parse(xml).Root!.Element("description")!.Value);
    }

    [Fact]
    public void KeepRules_GoOnTopOfAnExistingFilter_WithoutDisturbingItsOrder()
    {
        // As the game writes it: lowest priority first, Order counting down to 0.
        string existing = """
            <ItemFilter xmlns:i="http://www.w3.org/2001/XMLSchema-instance"><name>Strict</name><lootFilterVersion>9</lootFilterVersion><rules>
              <Rule><type>HIDE</type><conditions /><nameOverride>All</nameOverride><Order>2</Order></Rule>
              <Rule><type>SHOW</type><conditions /><nameOverride>Exalted</nameOverride><Order>1</Order></Rule>
              <Rule><type>SHOW</type><conditions /><nameOverride>Best uniques</nameOverride><Order>0</Order></Rule>
            </rules></ItemFilter>
            """;

        string combined = LootFilters.AddKeepRules(existing, new[] { Build("Main", 7, unique: 9) }, "Strict + build")!;
        var root = XDocument.Parse(combined).Root!;
        var rules = root.Element("rules")!.Elements("Rule").ToList();

        Assert.Equal("Strict + build", (string?)root.Element("name"));
        Assert.Equal(new[] { "All", "Exalted", "Best uniques", "Two or more build affixes", "Uniques the build uses" },
            rules.Select(r => (string?)r.Element("nameOverride")));
        // Still one unbroken sequence, the new rules ahead of everything that was there.
        Assert.Equal(new[] { 4, 3, 2, 1, 0 }, rules.Select(r => (int)r.Element("Order")!));
    }

    [Fact]
    public void KeepRules_HaveNothingToAdd_ForABuildWithoutGear()
    {
        Assert.Null(LootFilters.AddKeepRules("<ItemFilter><rules /></ItemFilter>", new[] { new BuildTree { Stages = { new TreeStage() } } }, "x"));
    }

    [Fact]
    public void SearchStrings_UseTheGamesSyntax_AndTheWordsOnTheTooltip()
    {
        var stage = Build("Main", 7, unique: 9).Stages[0];
        stage.Gear[0].Name = "Flames of Midnight";

        var strings = StashSearch.For(stage);

        Assert.Equal("/health|void resistance/", strings.Single(s => s.Label == "Anything with a build affix").Text);
        Assert.Equal("exalted&/health|void resistance/", strings.Single(s => s.Label == "Exalted items with a build affix").Text);
        Assert.Contains(strings, s => s.Text == "/flames of midnight/");
        Assert.Contains(strings, s => s.Text == "LP1+");
        Assert.Contains(strings, s => s.Text == "FP20+&!Corrupted");
    }
}
