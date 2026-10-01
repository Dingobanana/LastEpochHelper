using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LastEpochHelper.Core;

/// <summary>
/// Turns a Maxroll Last Epoch planner into a build plan file. The planner stores every tree as a
/// click history, so the ORDER of points is exact; the level attached to each point is an estimate
/// (interpolated between the planner's profiles, e.g. "lvl 5 - 26"), because quest passives and skill
/// XP do not arrive at fixed levels.
/// </summary>
public static partial class MaxrollImporter
{
    private const string ProfileUrl = "https://planners.maxroll.gg/profiles/le/";
    private const string GameDataUrl = "https://assets-ng.maxroll.gg/leplanner/game/data.json";
    private const string PlannerCssUrl = "https://assets-ng.maxroll.gg/leplanner/static/css/planner.css";
    private const string AssetHost = "https://assets-ng.maxroll.gg/";
    private static readonly int[] SpecSlotLevels = { 4, 8, 20, 35, 50 };

    [GeneratedRegex(@"last-epoch/planner/([a-z0-9]{6,12})")]
    private static partial Regex PlannerLink();

    [GeneratedRegex(@"^[a-z0-9]{6,12}$")]
    private static partial Regex BareId();

    [GeneratedRegex("data-le-profile=\"([a-z0-9]+)\"")]
    private static partial Regex EmbeddedPlanner();

    [GeneratedRegex(@"leplanner/static/media/treeAtlas\.[0-9a-f]+\.webp")]
    private static partial Regex AtlasPath();

    /// <param name="Text">The per-level plan in <see cref="BuildPlan"/> format.</param>
    /// <param name="Tree">Tree layouts and point order, for the tree view.</param>
    public sealed record Result(string Name, string Text, int Steps, BuildTree Tree);

    private sealed record Step(int Level, string Kind, string Text, bool Exact, int Seq, string? Group = null, string? Item = null);

    /// <param name="input">Planner URL, bare planner id, or a Maxroll build-guide URL.</param>
    /// <param name="cacheDir">Where the (large) id-to-name table is kept between imports.</param>
    public static async Task<Result> ImportAsync(string input, string cacheDir, HttpClient http)
    {
        string id = await ResolveIdAsync(input.Trim(), http);
        var raw = JsonNode.Parse(await http.GetStringAsync(ProfileUrl + id))
                  ?? throw new InvalidDataException("Empty answer from Maxroll.");
        if (raw["data"]?.GetValue<string>() is not { } data)
            throw new InvalidDataException($"Planner '{id}' was not found or is not public.");
        var game = await LoadGameDataAsync(cacheDir, http);
        var result = Convert(id, raw["name"]?.GetValue<string>() ?? id, JsonNode.Parse(data)!, game);
        await DownloadAtlasAsync(cacheDir, http);
        return result;
    }

    /// <summary>
    /// Fetches the icon sheet the planner itself uses. Its file name carries a build hash, so the
    /// current name is read from the planner's stylesheet. Icons are a nicety: failure is not an error.
    /// </summary>
    private static async Task DownloadAtlasAsync(string cacheDir, HttpClient http)
    {
        string target = Path.Combine(cacheDir, BuildTree.AtlasFile);
        try
        {
            if (File.Exists(target) && DateTime.UtcNow - File.GetLastWriteTimeUtc(target) < TimeSpan.FromHours(24)) return;
            var match = AtlasPath().Match(await http.GetStringAsync(PlannerCssUrl));
            if (!match.Success) return;
            await File.WriteAllBytesAsync(target, await http.GetByteArrayAsync(AssetHost + match.Value));
        }
        catch (HttpRequestException) { }
        catch (IOException) { }
        catch (TaskCanceledException) { }
    }

    private static async Task<string> ResolveIdAsync(string input, HttpClient http)
    {
        var m = PlannerLink().Match(input);
        if (m.Success) return m.Groups[1].Value;
        if (BareId().IsMatch(input)) return input;
        if (input.Contains("maxroll.gg/last-epoch/", StringComparison.OrdinalIgnoreCase))
        {
            string html = await http.GetStringAsync(input);
            m = EmbeddedPlanner().Match(html);
            if (!m.Success) m = PlannerLink().Match(html);
            if (m.Success) return m.Groups[1].Value;
        }
        throw new InvalidDataException("That does not look like a Maxroll Last Epoch planner or build guide link.");
    }

    /// <summary>Maxroll's game database, if an earlier import left a copy; never downloads.</summary>
    public static JsonNode? CachedGameData(string cacheDir)
    {
        try
        {
            string cache = Path.Combine(cacheDir, "maxroll_le_data.json");
            if (!File.Exists(cache)) return null;
            using var stream = File.OpenRead(cache);
            return JsonNode.Parse(stream);
        }
        catch (IOException) { return null; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static async Task<JsonNode> LoadGameDataAsync(string cacheDir, HttpClient http)
    {
        string cache = Path.Combine(cacheDir, "maxroll_le_data.json");
        if (!File.Exists(cache) || DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) > TimeSpan.FromHours(24))
            await File.WriteAllBytesAsync(cache, await http.GetByteArrayAsync(GameDataUrl));
        await using var stream = File.OpenRead(cache);
        return await JsonNode.ParseAsync(stream) ?? throw new InvalidDataException("Maxroll game data is empty.");
    }

    /// <summary>history[..position] as a flat list of node ids. Entries are ints, or {nodeId: count}.</summary>
    private static List<int> History(JsonNode? tree)
    {
        var result = new List<int>();
        if (tree?["history"] is not JsonArray history) return result;
        int position = tree["position"]?.GetValue<int>() ?? 0;
        foreach (var entry in history.Take(position))
        {
            if (entry is JsonObject batch)
                foreach (var (node, count) in batch)
                    result.AddRange(Enumerable.Repeat(int.Parse(node), count!.GetValue<int>()));
            else if (entry is not null) result.Add(entry.GetValue<int>());
        }
        return result;
    }

    /// <summary>Level for the i-th (1-based) of n points gained while going from level lo to hi.</summary>
    private static int Interpolate(int i, int n, int lo, int hi)
    {
        if (n <= 0 || hi <= lo) return hi;
        return Math.Min(hi, lo + Math.Max(1, (int)Math.Ceiling((double)i * (hi - lo) / n)));
    }

    internal static Result Convert(string id, string name, JsonNode data, JsonNode game)
    {
        var profiles = data["profiles"]?.AsArray() ?? throw new InvalidDataException("The planner has no profiles.");
        if (profiles.Count == 0) throw new InvalidDataException("The planner has no profiles.");

        var last = profiles[^1]!;
        var cls = game["classes"]![last["class"]!.GetValue<int>()]!;
        var passiveTree = game["skillTrees"]![cls["treeID"]!.GetValue<string>()]!;
        var masteries = cls["masteries"]!.AsArray();
        string MasteryName(int i) => masteries[i]!["name"]!.GetValue<string>();
        string SkillName(string ability) => game["abilities"]?[ability]?["abilityName"]?.GetValue<string>() ?? ability;
        JsonNode? PassiveNode(int node) => passiveTree["nodes"]?[node.ToString()];

        // Base-class skills unlock at a character level; tree skills after N points in that tree.
        var unlock = new Dictionary<string, int>();
        foreach (var a in cls["unlockableAbilities"]!.AsArray())
            unlock[a!["ability"]!.GetValue<string>()] = a["level"]!.GetValue<int>();
        foreach (var a in cls["knownAbilities"]!.AsArray()) unlock[a!.GetValue<string>()] = 1;
        var treeUnlocks = masteries.Select(m => m!["abilities"]!.AsArray()
            .Select(a => (Need: a!["level"]!.GetValue<int>(), Ability: a["ability"]!.GetValue<string>()))
            .OrderBy(a => a.Need).ToList()).ToList();

        static List<string> Skills(JsonNode profile, string key) =>
            (profile[key] as JsonArray)?.Select(s => s?.GetValue<string>()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList() ?? new();
        var used = profiles.SelectMany(p => Skills(p!, "specializedSkills").Concat(Skills(p!, "activeSkills"))).ToHashSet();

        var steps = new List<Step>();
        int seq = 0;
        void Add(int level, string kind, string text, bool exact = false, string? group = null, string? item = null) =>
            steps.Add(new Step(level, kind, text, exact, seq++, group, item));

        var atlas = new Dictionary<string, int>();
        if (game["treeAtlas"] is JsonArray cells)
            for (int i = 0; i < cells.Count; i++)
                if (cells[i]?.GetValue<string>() is { } cell) atlas[cell] = i;
        var build = new BuildTree { Name = name, AtlasCells = atlas.Count };
        var skillTrees = new Dictionary<string, JsonNode>();
        var skillTreeIds = new Dictionary<string, string>();

        int prevLevel = 1, prevMastery = 0;
        var prevPassives = new List<int>();
        var prevSpec = new List<string>();
        var prevSkillHistory = new Dictionary<string, List<int>>();

        for (int index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index]!;
            int level = profile["level"]!.GetValue<int>();
            int lo = index > 0 ? prevLevel : 1;
            int mastery = profile["mastery"]?.GetValue<int>() ?? 0;
            int? firstMasteryLevel = null;

            // ---------- passives
            var history = History(profile["passives"]);
            Dictionary<int, int> running;
            List<int> added;
            if (history.Take(prevPassives.Count).SequenceEqual(prevPassives))
            {
                running = Count(prevPassives);
                added = history.Skip(prevPassives.Count).ToList();
            }
            else
            {
                // The next profile reshuffled the tree: keep what both have, add only what is new.
                var have = Count(prevPassives);
                added = new List<int>();
                foreach (int node in history)
                {
                    if (have.GetValueOrDefault(node) > 0) have[node]--;
                    else added.Add(node);
                }
                var removed = have.Where(kv => kv.Value > 0).ToList();
                Add(lo + 1, "respec", removed.Count > 0
                    ? "Respec passives: remove " + string.Join(", ", removed.Select(kv => $"{PassiveNode(kv.Key)?["nodeName"]} x{kv.Value}"))
                    : "Respec passives (same nodes, new order)");
                running = Count(prevPassives);
                foreach (var (node, count) in removed) running[node] -= count;
            }
            for (int i = 0; i < added.Count; i++)
            {
                int node = added[i];
                running[node] = running.GetValueOrDefault(node) + 1;
                var info = PassiveNode(node);
                int tree = info?["mastery"]?.GetValue<int>() ?? 0;
                int at = Interpolate(i + 1, added.Count, lo, level);
                string nodeName = info?["nodeName"]?.GetValue<string>() ?? $"node {node}";
                Add(at, "passive", "", group: MasteryName(tree), item: $"{nodeName} ({running[node]}/{info?["maxPoints"]?.GetValue<int>() ?? 0})|{nodeName}");
                if (firstMasteryLevel is null && mastery != 0 && tree == mastery) firstMasteryLevel = at;

                int inTree = running.Where(kv => (PassiveNode(kv.Key)?["mastery"]?.GetValue<int>() ?? 0) == tree).Sum(kv => kv.Value);
                foreach (var (need, ability) in treeUnlocks[tree])
                {
                    if (inTree < need || unlock.ContainsKey(ability)) continue;
                    unlock[ability] = at;
                    if (used.Contains(ability))
                        Add(at, "unlock", $"{SkillName(ability)} unlocks ({need} points in the {MasteryName(tree)} tree)");
                }
            }
            if (mastery != 0 && mastery != prevMastery)
            {
                int at = firstMasteryLevel ?? (index > 0 ? lo + 1 : lo);
                Add(at, "mastery", $"Choose mastery: {MasteryName(mastery)}");
                if (masteries[mastery]!["masteryAbility"]?.GetValue<string>() is { } masteryAbility) unlock[masteryAbility] = at;
            }

            // ---------- skills
            var spec = Skills(profile, "specializedSkills");
            var skillHistory = new Dictionary<string, List<int>>();
            var dropped = prevSpec.Where(s => !spec.Contains(s)).ToList();
            var kept = prevSpec.Where(spec.Contains).ToList();
            var newly = spec.Where(s => !prevSpec.Contains(s)).ToList();
            foreach (string ability in spec)
            {
                string? treeId = game["abilities"]?[ability]?["playerAbilityID"]?.GetValue<string>();
                var tree = treeId is null ? null : game["skillTrees"]?[treeId];
                var points = treeId is null ? new List<int>() : History(profile["skillTrees"]?[treeId]);
                skillHistory[ability] = points;
                string skill = SkillName(ability);

                int start = lo;
                if (newly.Contains(ability))
                {
                    int slot = kept.Count + newly.IndexOf(ability);
                    start = Math.Max(index > 0 ? lo + 1 : 1, unlock.GetValueOrDefault(ability, 1));
                    string text = $"Specialize {skill}";
                    if (slot < prevSpec.Count && dropped.Count > 0)
                    {
                        text += $" (replaces {SkillName(dropped[0])})";
                        dropped.RemoveAt(0);
                    }
                    else if (slot < SpecSlotLevels.Length) start = Math.Max(start, SpecSlotLevels[slot]);
                    start = Math.Min(start, level);
                    Add(start, "specialize", text);
                }
                if (tree is null) continue;
                skillTrees[skill] = tree;
                skillTreeIds[skill] = treeId!;

                var old = prevSkillHistory.GetValueOrDefault(ability) ?? new List<int>();
                List<int> fresh;
                Dictionary<int, int> count;
                if (points.Take(old.Count).SequenceEqual(old))
                {
                    fresh = points.Skip(old.Count).ToList();
                    count = Count(old);
                }
                else
                {
                    Add(start, "respec", $"Respec skill tree: {skill}");
                    fresh = points;
                    count = new Dictionary<int, int>();
                }
                for (int i = 0; i < fresh.Count; i++)
                {
                    int node = fresh[i];
                    count[node] = count.GetValueOrDefault(node) + 1;
                    var info = tree["nodes"]?[node.ToString()];
                    string nodeName = info?["nodeName"]?.GetValue<string>() ?? $"node {node}";
                    Add(Interpolate(i + 1, fresh.Count, start, level), "skill", "", group: skill,
                        item: $"{nodeName} ({count[node]}/{info?["maxPoints"]?.GetValue<int>() ?? 0})|{nodeName}");
                }
            }

            var stage = new TreeStage
            {
                Name = profile["name"]?.GetValue<string>() ?? $"Level {level}",
                Level = level,
                Passives = history,
                Skills = skillHistory.Where(kv => skillTrees.ContainsKey(SkillName(kv.Key)))
                    .ToDictionary(kv => SkillName(kv.Key), kv => kv.Value),
            };
            MaxrollGear.Fill(stage, profile, data, game);
            build.Stages.Add(stage);

            prevLevel = level;
            prevPassives = history;
            if (mastery != 0) prevMastery = mastery;
            prevSpec = spec;
            prevSkillHistory = skillHistory;
        }

        foreach (var a in cls["unlockableAbilities"]!.AsArray())
        {
            string ability = a!["ability"]!.GetValue<string>();
            int level = a["level"]!.GetValue<int>();
            if (used.Contains(ability) && level > 1) Add(level, "unlock", $"{SkillName(ability)} unlocks", exact: true);
        }

        // One passive tab per tree the build puts points in (the base class tree always).
        var passiveNodes = passiveTree["nodes"]!.AsObject();
        var usedMasteries = build.Stages.SelectMany(s => s.Passives)
            .Select(n => PassiveNode(n)?["mastery"]?.GetValue<int>() ?? 0).Append(0).Distinct().OrderBy(m => m);
        foreach (int m in usedMasteries)
            build.Trees.Add(new TreeDef
            {
                Name = MasteryName(m),
                Kind = TreeDef.PassiveKind,
                Nodes = passiveNodes.Where(kv => (kv.Value?["mastery"]?.GetValue<int>() ?? 0) == m).Select(kv => ToNode(kv.Key, kv.Value!, atlas)).ToList(),
            });
        foreach (var (skill, tree) in skillTrees)
        {
            // The root node of a skill tree shows the skill's own icon.
            string? skillIcon = tree["ability"]?.GetValue<string>() is { } key ? game["abilities"]?[key]?["sprite"]?.GetValue<string>() : null;
            build.Trees.Add(new TreeDef
            {
                Name = skill,
                Kind = TreeDef.SkillKind,
                TreeId = skillTreeIds.GetValueOrDefault(skill, ""),
                Nodes = tree["nodes"]!.AsObject().Select(kv => ToNode(kv.Key, kv.Value!, atlas, kv.Key == "0" ? skillIcon : null)).ToList(),
            });
        }

        return new Result(name, Render(id, name, steps), steps.Count, build);
    }

    private static TreeNode ToNode(string id, JsonNode node, Dictionary<string, int> atlas, string? iconOverride = null)
    {
        string icon = iconOverride ?? node["icon"]?.GetValue<string>() ?? "";
        string description = node["altText"]?.GetValue<string>() ?? "";
        if (description.Length == 0) description = node["description"]?.GetValue<string>() ?? "";
        var stats = (node["stats"] as JsonArray)?.Select(s => $"{s?["value"]?.GetValue<string>()} {s?["statName"]?.GetValue<string>()}".Trim())
            .Where(s => s.Length > 0) ?? Enumerable.Empty<string>();
        return new TreeNode
        {
            Id = int.Parse(id),
            Name = node["nodeName"]?.GetValue<string>() ?? $"node {id}",
            Max = node["maxPoints"]?.GetValue<int>() ?? 0,
            X = node["transform"]?["x"]?.GetValue<double>() ?? 0,
            // The planner's y axis points up; screens point down.
            Y = -(node["transform"]?["y"]?.GetValue<double>() ?? 0),
            Requires = (node["requirements"] as JsonArray)?.Select(r => r!["node"]!.GetValue<int>()).ToList() ?? new List<int>(),
            Description = string.Join("\n", stats.Append(description).Where(s => s.Length > 0)),
            IconIndex = atlas.GetValueOrDefault(icon, -1),
        };
    }

    private static Dictionary<int, int> Count(IEnumerable<int> nodes)
    {
        var counts = new Dictionary<int, int>();
        foreach (int node in nodes) counts[node] = counts.GetValueOrDefault(node) + 1;
        return counts;
    }

    private static readonly Dictionary<string, int> KindOrder = new()
    {
        ["mastery"] = 1, ["respec"] = 2, ["passive"] = 3, ["unlock"] = 3, ["specialize"] = 4, ["skill"] = 5,
    };

    /// <summary>
    /// One line per level and tree: several points landing on the same level are merged, showing
    /// each node once with the count it should have reached.
    /// </summary>
    private static string Render(string id, string name, List<Step> steps)
    {
        var text = new StringBuilder();
        text.Append("name: ").Append(name).Append('\n');
        text.Append("# Imported from https://maxroll.gg/last-epoch/planner/").Append(id).Append('\n');
        text.Append("# The order of points is exact. Levels are estimates: take the points in this order as you get them.\n");

        foreach (var level in steps.GroupBy(s => s.Level).OrderBy(g => g.Key))
        {
            var ordered = level.OrderBy(s => s.Exact ? 0 : KindOrder[s.Kind]).ThenBy(s => s.Seq).ToList();
            var merged = new HashSet<(string, string)>();
            foreach (var step in ordered)
            {
                if (step.Group is null)
                {
                    text.Append(level.Key).Append(": ").Append(step.Text).Append('\n');
                    continue;
                }
                if (!merged.Add((step.Kind, step.Group))) continue;
                // Keep the last (highest) count per node, in the order the nodes were first touched.
                var items = ordered.Where(s => s.Kind == step.Kind && s.Group == step.Group)
                    .Select(s => s.Item!.Split('|'))
                    .GroupBy(parts => parts[1])
                    .Select(g => g.Last()[0]);
                string prefix = step.Kind == "passive" ? $"Passives [{step.Group}]" : step.Group;
                text.Append(level.Key).Append(": ").Append(prefix).Append(": ").Append(string.Join(", ", items)).Append('\n');
            }
        }
        return text.ToString();
    }
}
