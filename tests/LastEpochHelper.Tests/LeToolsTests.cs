using System.Text.Json.Nodes;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public class LeToolsTests
{
    [Theory]
    [InlineData("https://www.lastepochtools.com/planner/BGz40mgV", "BGz40mgV")]
    [InlineData("lastepochtools.com/planner/oYEqej8D?tab=skills", "oYEqej8D")]
    [InlineData("https://www.lastepochtools.com/de/planner/Q9JKG88E#passives", "Q9JKG88E")]
    [InlineData("my build: <https://www.lastepochtools.com/planner/om6wKJw1> have fun", "om6wKJw1")]
    public void APlannerLink_IsUnderstood_WithItsIdAsWritten(string pasted, string id)
    {
        var understood = MaxrollImporter.Understand(pasted);
        Assert.Equal(id, understood.LeToolsId);
        Assert.Null(understood.Problem);
        Assert.Null(understood.PlannerId);
    }

    // 1 is free, 2 needs a point in 1, 3 needs two in 2; 4 needs 3 points spent in its part of the tree;
    // 6 needs a point in 7.
    private static readonly JsonObject Nodes = (JsonObject)JsonNode.Parse("""
    { "0": { "maxPoints": 0 },
      "1": { "maxPoints": 3, "requirements": [] },
      "2": { "maxPoints": 2, "requirements": [ { "node": 1, "requirement": 1 } ] },
      "3": { "maxPoints": 1, "requirements": [ { "node": 2, "requirement": 2 } ] },
      "4": { "maxPoints": 1, "masteryRequirement": 3, "requirements": [] },
      "6": { "maxPoints": 1, "requirements": [ { "node": 7, "requirement": 1 } ] },
      "7": { "maxPoints": 1, "requirements": [] } }
    """)!;

    private static List<int> Ordered(string? progression, string selected) =>
        LeTools.Ordered(progression is null ? null : JsonNode.Parse(progression), JsonNode.Parse(selected), Nodes);

    [Fact]
    public void TheOrderGiven_IsKept_WhereItAddsUpToThePointsPlaced()
    {
        Assert.Equal(new[] { 1, 2, 2, 3 }, Ordered("[1,2,2,3]", """{"0":0,"1":1,"2":2,"3":1}"""));
    }

    [Fact]
    public void WithoutAnOrder_PointsGoWhereTheTreeAllowsThem()
    {
        Assert.Equal(new[] { 7, 6 }, Ordered(null, """{"6":1,"7":1}"""));
        Assert.Equal(new[] { 1, 7, 6, 4 }, Ordered("[]", """{"4":1,"6":1,"1":1,"7":1}""")); // 4 waits for three points
        Assert.Equal(new[] { 1, 2, 2, 3 }, Ordered(null, """{"3":1,"2":2,"1":1}"""));
    }

    [Fact]
    public void AnOrderThatDisagrees_IsCutToThePointsPlaced_AndCompleted()
    {
        // Too many points in 1, a node never placed (5), text instead of numbers, and 3 left out.
        Assert.Equal(new[] { 1, 2, 2, 3 }, Ordered("""[1,1,"2",5,2,1]""", """{"1":1,"2":"2","3":1}"""));
    }

    [Fact]
    public void PointsAboveTheMaximum_OrOnUnknownNodes_AreDropped()
    {
        Assert.Equal(new[] { 1, 1, 1 }, Ordered("[1,1,1,1,1]", """{"1":5,"99":3}"""));
    }

    [Fact]
    public void TheWholeBuild_BecomesAOneProfilePlanner_WithItsSkillsInSlotOrder()
    {
        var game = JsonNode.Parse(MaxrollImporterTests.Game)!;
        var build = (JsonObject)JsonNode.Parse(ImportFlowTests.LeToolsBuild)!;
        // A second skill in an earlier slot, with no order of its own, and one the game data does not know.
        build["data"]!["skillTrees"]!.AsArray().Add(JsonNode.Parse("""{"treeID":"lu","selected":{"0":0},"level":1,"slotNumber":1}"""));
        build["data"]!["skillTrees"]!.AsArray().Add(JsonNode.Parse("""{"treeID":"zz","selected":{"1":1},"level":1,"slotNumber":2}"""));

        var planner = LeTools.ToPlanner(build, "Name", game);

        var profile = planner["profiles"]![0]!;
        Assert.Equal(0, profile["class"]!.GetValue<int>());
        Assert.Equal(3, profile["mastery"]!.GetValue<int>());
        Assert.Equal(30, profile["level"]!.GetValue<int>());
        // Lunge has no points, so it is not specialized; "zz" is not a skill the game data knows.
        Assert.Equal(new[] { "a_rive" }, profile["specializedSkills"]!.AsArray().Select(s => s!.GetValue<string>()));
        Assert.Equal(new[] { "a_rive" }, profile["activeSkills"]!.AsArray().Select(s => s!.GetValue<string>()));
        Assert.Null(profile["weaver"]);
        Assert.Empty(planner["items"]!.AsObject());
    }
}
