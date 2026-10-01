using System.Net.Http;
using System.Text.Json.Nodes;

namespace LastEpochHelper.Core;

/// <summary>The points a character really has in its trees, as last seen by Last Epoch Tools.</summary>
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
/// Reads an online character's public profile from Last Epoch Tools, the only place an online
/// character's trees can be seen from outside the game. Only ever called when the user asks: the
/// endpoint is undocumented, and the site refreshes a profile at most every couple of hours.
/// </summary>
public static class LetProfile
{
    public static string ProfilePage(string account) => $"https://www.lastepochtools.com/profile/{Uri.EscapeDataString(account)}";

    private static string Endpoint(string account, string character) =>
        $"https://www.lastepochtools.com/api/public/account/{Uri.EscapeDataString(account)}/character/{Uri.EscapeDataString(character)}";

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

    public static async Task<ActualTrees?> FetchAsync(string account, string character, HttpClient http)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(account, character));
        // The site turns away clients that do not look like a browser.
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) LastEpochHelper");
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync());
    }
}
