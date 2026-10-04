using System.Text.Json.Nodes;

namespace LastEpochHelper.Core;

/// <summary>The points a character really has in its trees: set by hand in the tree view, or read from a profile.</summary>
public sealed class ActualTrees
{
    public DateTime Fetched { get; set; }
    /// <summary>When the site last refreshed the character from the game (its own text).</summary>
    public string Updated { get; set; } = "";
    public int Level { get; set; }
    /// <summary>Passive node id -> points, across the base and mastery trees.</summary>
    public Dictionary<int, int> Passives { get; set; } = new();
    /// <summary>Skill tree id -> node id -> points.</summary>
    public Dictionary<string, Dictionary<int, int>> Skills { get; set; } = new();
}

/// <summary>
/// The shape of a character's trees as Last Epoch Tools publishes them. The overlay itself never
/// fetches from that site (it does not allow programs); this reads the format, for tests and for
/// files someone saved by hand.
/// </summary>
public static class LetProfile
{
    /// <summary>Null when the site has no data for that character (it has to be looked up there once).</summary>
    public static ActualTrees? Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException) { return null; }
        if (root?["buildInfo"]?["data"] is not { } data) return null;

        var trees = new ActualTrees
        {
            Fetched = DateTime.Now,
            Updated = root["charInfo"]?["lastUpdated"]?.ToString() ?? "",
            Level = root["buildInfo"]?["level"]?.GetValue<int>() ?? 0,
            Passives = Points(data["charTree"]?["selected"]),
        };
        foreach (var tree in data["skillTrees"] as JsonArray ?? new JsonArray())
            if (tree?["treeID"]?.GetValue<string>() is { Length: > 0 } id)
                trees.Skills[id] = Points(tree["selected"]);
        return trees;
    }

    private static Dictionary<int, int> Points(JsonNode? selected)
    {
        var points = new Dictionary<int, int>();
        if (selected is not JsonObject nodes) return points;
        foreach (var (node, count) in nodes)
            if (int.TryParse(node, out int id) && count is not null) points[id] = count.GetValue<int>();
        return points;
    }
}
