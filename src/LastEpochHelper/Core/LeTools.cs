using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LastEpochHelper.Core;

/// <summary>
/// Builds from Last Epoch Tools planners. Its owner allowed this overlay to read the public build address
/// (fetched only when a player imports a link, never in the background). The trees there use the same ids
/// as Maxroll's game data - class and mastery numbers, passive node ids, skill tree ids - so a build is
/// turned into a one-profile Maxroll planner and converted like any other. Its items use ids of their own
/// and are left out.
/// </summary>
public static partial class LeTools
{
    public const string PlannerUrl = "https://www.lastepochtools.com/planner/";
    private const string BuildDataUrl = "https://www.lastepochtools.com/api/public/build_data/";
    /// <summary>Marks a build's source id as a Last Epoch Tools planner (Maxroll's ids are bare).</summary>
    public const string SourcePrefix = "letools:";

    // Ids are case-sensitive ("BGz40mgV"); the address may carry a language ("/de/planner/...").
    [GeneratedRegex(@"lastepochtools\.com/(?:[a-z]{2}/)?planner/([A-Za-z0-9]{4,16})", RegexOptions.IgnoreCase)]
    private static partial Regex PlannerLink();

    /// <summary>The planner id in a Last Epoch Tools planner link, or null.</summary>
    public static string? PlannerId(string text) => PlannerLink().Match(text) is { Success: true } link ? link.Groups[1].Value : null;

    private const string BlockedKey = "LeToolsDataUrl";

    /// <summary>
    /// When the site's bot check stopped the overlay: the build's data address. The player's own browser
    /// gets through the check, and what it shows there can be pasted into the import box instead.
    /// </summary>
    public static string? BlockedDataUrl(Exception error) => error.Data[BlockedKey] as string;

    public static bool IsSource(string? sourceId) => sourceId?.StartsWith(SourcePrefix, StringComparison.Ordinal) == true;

    /// <summary>The address to show for a build's source id: its Last Epoch Tools or its Maxroll planner.</summary>
    public static string SourceUrl(string sourceId) => IsSource(sourceId)
        ? PlannerUrl + sourceId[SourcePrefix.Length..]
        : "https://maxroll.gg/last-epoch/planner/" + sourceId;

    public static async Task<JsonObject> FetchAsync(string id, HttpClient http)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildDataUrl + Uri.EscapeDataString(id));
        // Says who is asking, so the site can tell this overlay's requests apart.
        request.Headers.UserAgent.ParseAdd($"LastEpochHelper/{Updater.Display(Updater.Current)} (+https://github.com/{Updater.Repository})");
        using var answer = await http.SendAsync(request);
        if (answer.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidDataException($"Last Epoch Tools has no planner '{id}' - it may have been deleted, or the link is cut short.");
        // The site sits behind Cloudflare, whose bot check can stop a program even where the site allows it.
        if (answer.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            var blocked = new InvalidDataException("Last Epoch Tools' protection turned the overlay away (we are sorting that out with the site). "
                                              + "Meanwhile your browser can fetch the build: open the link below, select all (Ctrl+A), copy (Ctrl+C), "
                                              + "paste it into the Build link box here and press Import build. In Firefox, choose 'Raw Data' first.");
            blocked.Data[BlockedKey] = BuildDataUrl + Uri.EscapeDataString(id);
            throw blocked;
        }
        answer.EnsureSuccessStatusCode();
        JsonNode? found;
        try { found = JsonNode.Parse(await answer.Content.ReadAsStringAsync()); }
        catch (System.Text.Json.JsonException) { found = null; }
        // A build that is not there still comes back as 200, with {"code": 3, "error": "Build not found"}.
        if (found is JsonObject { } failed && failed["error"] is not null)
            throw new InvalidDataException($"Last Epoch Tools has no public planner '{id}' - it may be private or deleted.");
        if (found is not JsonObject build || build["data"] is not JsonObject)
            throw new InvalidDataException($"Last Epoch Tools sent something for planner '{id}' that is not a build.");
        return build;
    }

    /// <summary>A name for a build that has none of its own: its mastery (or class) and where it came from.</summary>
    public static string Name(JsonObject build, string id, JsonNode game)
    {
        var data = build["data"];
        int cls = data?["bio"]?["characterClass"].IntOrNull() ?? build["class"].IntOrNull() ?? -1;
        int mastery = data?["bio"]?["chosenMastery"].IntOrNull() ?? build["mastery"].IntOrNull() ?? 0;
        var masteries = Class(game, cls)?["masteries"] as JsonArray;
        string who = (masteries is not null && mastery >= 0 && mastery < masteries.Count ? masteries[mastery]?["name"].StrOrNull() : null)
                     ?? Class(game, cls)?["className"].StrOrNull() ?? "Build";
        return $"{who} - Last Epoch Tools {id}".TrimEnd();
    }

    /// <summary>
    /// The build as a Maxroll planner with one profile at the build's level. Last Epoch Tools keeps the
    /// order points were placed in; where that order is missing or does not add up to the points placed
    /// (older planners), the rest are placed in an order the tree allows.
    /// </summary>
    public static JsonObject ToPlanner(JsonObject build, string name, JsonNode game)
    {
        var data = build["data"] as JsonObject ?? throw new InvalidDataException("That Last Epoch Tools planner holds no build.");
        int cls = data["bio"]?["characterClass"].IntOrNull() ?? build["class"].IntOrNull()
                  ?? throw new InvalidDataException("The Last Epoch Tools planner does not say which class the build is for.");
        int mastery = data["bio"]?["chosenMastery"].IntOrNull() ?? build["mastery"].IntOrNull() ?? 0;
        int level = Math.Clamp(data["bio"]?["level"].IntOrNull() ?? build["level"].IntOrNull() ?? 100, 1, 100);
        var trees = game["skillTrees"] as JsonObject;
        var passiveNodes = trees?[Class(game, cls)?["treeID"].StrOrNull() ?? ""]?["nodes"] as JsonObject;

        var profile = new JsonObject
        {
            ["name"] = name,
            ["class"] = cls,
            ["mastery"] = mastery,
            ["level"] = level,
            ["passives"] = History(Ordered(data["charTreeProgression"], data["charTree"]?["selected"], passiveNodes)),
        };

        // Skill trees, in the order of their specialization slots.
        var skillTrees = new JsonObject();
        var specialized = new List<string>();
        var abilityOf = (game["abilities"] as JsonObject)?
            .Where(kv => kv.Value is JsonObject a && a["playerAbilityID"].StrOrNull() is { Length: > 0 })
            .GroupBy(kv => kv.Value!["playerAbilityID"].Str())
            .ToDictionary(g => g.Key, g => g.First().Key) ?? new Dictionary<string, string>();
        var slotted = (data["skillTrees"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(t => (Tree: t, Id: t["treeID"].StrOrNull() ?? "", Slot: t["slotNumber"].IntOrNull() ?? 99))
            .Where(t => t.Id.Length > 0).OrderBy(t => t.Slot);
        foreach (var (tree, treeId, _) in slotted)
        {
            var order = Ordered(data["skillTreesProgression"]?[treeId], tree["selected"], trees?[treeId]?["nodes"] as JsonObject);
            if (order.Count == 0 || !abilityOf.TryGetValue(treeId, out string? ability) || specialized.Contains(ability)) continue;
            skillTrees[treeId] = History(order);
            specialized.Add(ability);
        }
        profile["skillTrees"] = skillTrees;
        profile["specializedSkills"] = Strings(specialized);
        // The skill bar: tree ids, or "" for an empty slot.
        var bar = (data["hud"] as JsonArray ?? new JsonArray()).Select(s => s.StrOrNull() ?? "")
            .Select(treeId => abilityOf.GetValueOrDefault(treeId)).Where(a => a is not null).Select(a => a!).Distinct().ToList();
        profile["activeSkills"] = Strings(bar.Count > 0 ? bar : specialized);

        // Its click order is not kept reliably; the points are.
        var weaver = Ordered(null, data["weaverTree"]?["selected"], trees?["weaver"]?["nodes"] as JsonObject);
        if (weaver.Count > 0) profile["weaver"] = History(weaver);

        return new JsonObject { ["profiles"] = new JsonArray(profile), ["items"] = new JsonObject() };
    }

    private static JsonNode? Class(JsonNode game, int index) =>
        game["classes"] is JsonArray classes && index >= 0 && index < classes.Count ? classes[index] : null;

    private static JsonObject History(List<int> order) =>
        new() { ["history"] = new JsonArray(order.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()) };

    private static JsonArray Strings(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    /// <summary>
    /// One entry per point, in the order given, kept only as far as it agrees with the points placed
    /// (<paramref name="selected"/>: node id -> points). Points the order leaves out follow, each taken
    /// as soon as its tree allows it. Nodes the game data does not know are dropped.
    /// </summary>
    internal static List<int> Ordered(JsonNode? progression, JsonNode? selected, JsonObject? nodes)
    {
        var order = new List<int>();
        if (nodes is null || selected is not JsonObject placed) return order;
        var want = new Dictionary<int, int>();
        foreach (var (key, value) in placed)
            if (int.TryParse(key, out int node) && nodes[key] is JsonObject info && value.IntOrNull() is > 0 and var points)
                want[node] = Math.Min(points, info["maxPoints"].IntOrNull() is > 0 and var max ? max : points);

        var have = new Dictionary<int, int>();
        foreach (var entry in progression as JsonArray ?? new JsonArray())
            if (entry.IntOrNull() is { } node && have.GetValueOrDefault(node) < want.GetValueOrDefault(node))
            {
                have[node] = have.GetValueOrDefault(node) + 1;
                order.Add(node);
            }

        // What is left: one point at a time, to the lowest node whose requirements are met.
        while (want.Any(kv => have.GetValueOrDefault(kv.Key) < kv.Value))
        {
            var open = want.Where(kv => have.GetValueOrDefault(kv.Key) < kv.Value).Select(kv => kv.Key).OrderBy(n => n).ToList();
            int next = open.FirstOrDefault(n => Allowed(n, nodes, have), -1);
            if (next < 0) next = open[0]; // the tree's rules are not what they seem: keep the points anyway
            have[next] = have.GetValueOrDefault(next) + 1;
            order.Add(next);
        }
        return order;
    }

    /// <summary>A node can take a point when one of its required nodes has enough, and its part of the tree has enough points.</summary>
    private static bool Allowed(int node, JsonObject nodes, Dictionary<int, int> have)
    {
        var info = nodes[node.ToString()];
        var requirements = (info?["requirements"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
        bool linked = requirements.Count == 0 || requirements.Any(r => have.GetValueOrDefault(r["node"].IntOrNull() ?? -1) >= (r["requirement"].IntOrNull() ?? 0));
        int section = info?["mastery"].IntOrNull() ?? 0, needed = info?["masteryRequirement"].IntOrNull() ?? 0;
        int spent = have.Where(kv => (nodes[kv.Key.ToString()]?["mastery"].IntOrNull() ?? 0) == section).Sum(kv => kv.Value);
        return linked && spent >= needed;
    }
}
