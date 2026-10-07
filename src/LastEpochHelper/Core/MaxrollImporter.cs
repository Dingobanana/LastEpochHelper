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

    /// <summary>
    /// How a planner's profiles relate. Maxroll uses them in two ways: leveling planners have one per
    /// level range ("lvl 1 - 9", "lvl 10 - 34", ...), each continuing the one before; build guides have
    /// alternatives at the same level ("Starter", "Endgame", "Aspirational", "HC Shield Variant").
    /// Only the first kind is a sequence - of the second, one is followed at a time.
    /// </summary>
    public static (bool Sequential, List<string> Names) Shape(JsonNode data)
    {
        var profiles = data["profiles"] as JsonArray ?? new JsonArray();
        var levels = profiles.Select(p => p is JsonObject && p["level"] is JsonValue value && value.TryGetValue(out int level) ? level : 0).ToList();
        var names = Names(profiles);
        // Many players leave every profile at level 100 and let the trees tell the story: "Campaign"
        // with 73 passive points, "Early endgame" with 111. Steadily more points is a sequence too.
        var points = profiles.Select(p => p is JsonObject ? Points(p["passives"]) : 0).ToList();
        // Rising levels alone do not make a sequence: a planner can hold two unrelated sets that happen to
        // differ in level. In a sequence the trees never shrink from one stage to the next.
        bool byLevel = levels.Zip(levels.Skip(1)).All(pair => pair.Second > pair.First) && points.Zip(points.Skip(1)).All(pair => pair.Second >= pair.First);
        bool byGrowth = levels.Zip(levels.Skip(1)).All(pair => pair.Second >= pair.First) && points.Zip(points.Skip(1)).All(pair => pair.Second > pair.First);
        return (levels.Count < 2 || byLevel || byGrowth, names);
    }

    /// <summary>
    /// A name for every profile: its own, tidied (one line, not endless), a stand-in where it has none,
    /// and numbered where two profiles carry the same name - a name is how a stage or version is chosen.
    /// </summary>
    private static List<string> Names(JsonArray profiles)
    {
        var names = new List<string>();
        for (int i = 0; i < profiles.Count; i++)
        {
            string name = profiles[i] is JsonObject profile && profile["name"] is JsonValue value && value.TryGetValue(out string? given) ? given ?? "" : "";
            name = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (name.Length > 60) name = name[..60].TrimEnd();
            if (name.Length == 0) name = $"Version {i + 1}";
            string unique = name;
            for (int n = 2; names.Contains(unique, StringComparer.OrdinalIgnoreCase); n++) unique = $"{name} ({n})";
            names.Add(unique);
        }
        return names;
    }

    private static int Points(JsonNode? tree) =>
        tree is JsonObject && tree["history"] is JsonArray ? History(tree).Count : 0;

    /// <summary>
    /// The level a stage ends at. The planner's own number when the profiles are levelled; when they
    /// are all left at 100 but hold ever more passive points, the level at which a character has that
    /// many (points come one per level from 3, plus up to 15 from quests).
    /// </summary>
    private static void GiveStagesLevels(JsonArray profiles)
    {
        int previous = 0;
        for (int i = 0; i < profiles.Count; i++)
        {
            if (profiles[i] is not JsonObject profile) continue;
            int level = Math.Clamp(profile["level"] is JsonValue value && value.TryGetValue(out int stated) ? stated : 100, 1, 100);
            bool last = i == profiles.Count - 1;
            int earned = Math.Clamp(Points(profile["passives"]) - 13, 1, 100);
            // Only where the stated level cannot be right: not above the stage before it.
            if (level <= previous || (!last && profiles[i + 1] is JsonObject next && next["level"] is JsonValue nv && nv.TryGetValue(out int nextLevel) && nextLevel <= level))
                level = Math.Min(100, Math.Max(Math.Min(level, earned), previous + 1));
            profile["level"] = level;
            previous = level;
        }
    }

    /// <summary>Every build a planner holds: one for a leveling planner, one per version otherwise.</summary>
    /// <param name="preferred">The version (0-based, as in the planner) to put first: the one a link or guide pointed at.</param>
    internal static List<Result> ConvertAll(string id, string name, JsonNode data, JsonNode game, int? preferred = null)
    {
        // Planners can be saved without a title; the build still needs something to be called (and filed under).
        name = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0) name = $"Maxroll build {id}";
        var (sequential, names) = Shape(data);
        var results = new List<Result>();
        Exception? firstFailure = null;
        foreach (int version in Enumerable.Range(0, sequential ? 1 : Math.Max(1, names.Count)))
        {
            // One version that cannot be read (a half-finished profile) must not cost the others.
            try { results.Add(ConvertPlanner(id, name, data, game, version)); }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or KeyNotFoundException or NullReferenceException
                                          or ArgumentException or FormatException or IndexOutOfRangeException or System.Text.Json.JsonException)
            {
                firstFailure ??= e;
            }
        }
        if (results.Count == 0)
            throw new InvalidDataException("This planner could not be read: " + (firstFailure is InvalidDataException known ? known.Message
                : "it holds something the importer does not understand (" + (firstFailure?.GetType().Name ?? "no profiles") + ")."), firstFailure);
        // Start on a version that has trees, not on a gear-only one that happens to come first.
        // ...and on the one the link or the guide page pointed at, when it said.
        int start = preferred is { } wanted ? results.FindIndex(r => r.Tree.Variant == wanted && r.Steps > 0) : -1;
        if (start < 0) start = results.FindIndex(r => r.Steps > 0);
        if (start > 0) results.Insert(0, results[start]);
        if (start > 0) results.RemoveAt(start + 1);
        // The list of versions each build carries is the list of builds that were actually made, in this order.
        if (results.Count > 1)
        {
            var made = results.Select(r => r.Tree.Variants.Count > r.Tree.Variant ? r.Tree.Variants[r.Tree.Variant] : r.Name).ToList();
            for (int i = 0; i < results.Count; i++)
            {
                results[i].Tree.Variants = made.ToList();
                results[i].Tree.Variant = i;
            }
        }
        else if (results.Count == 1) results[0].Tree.Variants = new List<string>();
        return results;
    }

    /// <param name="variant">Which alternative to import when the planner holds alternatives; null = the first.</param>
    internal static Result ConvertPlanner(string id, string name, JsonNode data, JsonNode game, int? variant = null)
    {
        var (sequential, names) = Shape(data);
        // From here on every profile carries the tidied, unique name.
        data = data.DeepClone();
        if (data["profiles"] is JsonArray named)
            for (int i = 0; i < named.Count && i < names.Count; i++)
                if (named[i] is JsonObject profile)
                {
                    profile["name"] = names[i];
                    // ...and a level that is one: characters go from 1 to 100, whatever the field holds.
                    profile["level"] = Math.Clamp(profile["level"] is JsonValue stated && stated.TryGetValue(out int level) ? level : 100, 1, 100);
                }
        Result result;
        if (sequential)
        {
            var staged = data.DeepClone();
            if (staged["profiles"] is JsonArray stages) GiveStagesLevels(stages);
            result = Convert(id, name, staged, game);
        }
        else
        {
            int index = Math.Clamp(variant ?? 0, 0, names.Count - 1);
            // The alternatives share one item list; everything else about the others is left out.
            var one = data.DeepClone();
            one["profiles"] = new JsonArray(data["profiles"]![index]!.DeepClone());
            result = Convert(id, $"{name} - {names[index]}", one, game);
            result.Tree.Variants = names;
            result.Tree.Variant = index;
        }
        result.Tree.SourceId = id;
        return result;
    }

    /// <summary>
    /// The planner a guide page is about. Its own planner is embedded several times (skill bar, trees,
    /// gear); a class overview page instead embeds many builds once each, and is not a guide to import.
    /// </summary>
    public static string? PickPlanner(string html)
    {
        var ids = EmbeddedPlanner().Matches(html).Select(m => m.Groups[1].Value).Where(id => id.Any(char.IsDigit)).ToList();
        if (ids.Count == 0)
            return PlannerLink().Matches(html).Select(m => m.Groups[1].Value).FirstOrDefault(id => id.Any(char.IsDigit));
        var counted = ids.GroupBy(i => i).Select(g => (Id: g.Key, Count: g.Count())).OrderByDescending(g => g.Count).ToList();
        if (counted.Count > 1 && counted[0].Count == 1)
            throw new InvalidDataException("That page lists several builds. Open the guide of the one you want and paste that link.");
        return counted[0].Id;
    }

    private sealed record Step(int Level, string Kind, string Text, bool Exact, int Seq, string? Group = null, string? Item = null);

    /// <param name="input">Planner URL, bare planner id, or a Maxroll build-guide URL.</param>
    /// <param name="cacheDir">Where the (large) id-to-name table is kept between imports.</param>
    /// <returns>
    /// One result for a leveling planner; for a build guide one per version (Starter, Endgame, ...),
    /// so that switching between them later needs no download.
    /// </returns>
    public static async Task<IReadOnlyList<Result>> ImportAsync(string input, string cacheDir, HttpClient http)
    {
        var pasted = Understand(input);
        // The planner's "Export data" puts one version of a build on the clipboard as JSON; that can be pasted as it is.
        if (pasted.Json is { } json)
        {
            JsonNode? document;
            try { document = JsonNode.Parse(json); }
            catch (System.Text.Json.JsonException) { document = null; }
            if (document is not JsonObject found)
                throw new InvalidDataException("That looks like data copied from a planner, but it is cut off or not complete. Copy it again with the planner's Export button.");
            return await FromJsonAsync(found, "", "Pasted build", cacheDir, http);
        }

        if (pasted.LeToolsId is { } leTools)
            return await FromJsonAsync(await LeTools.FetchAsync(leTools, http), LeTools.SourcePrefix + leTools, "", cacheDir, http);

        var (id, variant) = await ResolveAsync(pasted, http);
        using var answer = await http.GetAsync(ProfileUrl + id);
        if (answer.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidDataException($"Maxroll has no planner '{id}' - it may have been deleted, or the link is cut short.");
        answer.EnsureSuccessStatusCode();
        JsonNode? raw;
        try { raw = JsonNode.Parse(await answer.Content.ReadAsStringAsync()); }
        catch (System.Text.Json.JsonException) { raw = null; }
        if (raw is not JsonObject profile || profile["data"] is not JsonValue)
            throw new InvalidDataException($"Planner '{id}' was not found or is not public.");
        return await FromJsonAsync(profile, id, id, cacheDir, http, variant);
    }

    /// <summary>
    /// Builds from planner data in any of the shapes it travels in: Maxroll's answer for a planner
    /// ({"name", "data": "..."}), a planner's inner data ({"profiles": [...]}), one version as the
    /// planner's Export button copies it ({"class", "passives", "skillTrees", "items", ...}), or a
    /// Last Epoch Tools build ({"data": {"bio", "charTree", "skillTrees", ...}}).
    /// </summary>
    /// <param name="id">The planner's id when it is known ("" for pasted or file data).</param>
    /// <param name="preferred">The version (0-based) the link or guide page pointed at.</param>
    private static async Task<IReadOnlyList<Result>> FromJsonAsync(JsonObject found, string id, string fallbackName, string cacheDir, HttpClient http, int? preferred = null)
    {
        JsonNode? planner = found;
        string title = fallbackName;
        bool leTools = found["data"] is JsonObject build && (build["charTree"] is JsonObject || build["bio"] is JsonObject);
        if (found["data"] is JsonValue inner && inner.TryGetValue(out string? data))
        {
            try { planner = JsonNode.Parse(data ?? ""); }
            catch (System.Text.Json.JsonException) { planner = null; }
            if (id.Length == 0 && found["id"] is JsonValue idValue && idValue.TryGetValue(out string? given) && !string.IsNullOrWhiteSpace(given)) id = given;
            if (found["name"] is JsonValue nameValue && nameValue.TryGetValue(out string? named) && !string.IsNullOrWhiteSpace(named)) title = named;
        }
        bool exported = planner is JsonObject one && one["profiles"] is null
                        && (one["passives"] is JsonObject || one["skillTrees"] is JsonObject || one["items"] is JsonObject);
        if (!exported && !leTools && (planner is not JsonObject || planner["profiles"] is not JsonArray))
            throw new InvalidDataException("That is JSON, but not a build: it has neither this overlay's trees nor a Maxroll planner's profiles.");

        // Icons are positions in the sheet, listed in the game data: the two must be of the same age.
        var (atlas, isNew) = await DownloadAtlasAsync(cacheDir, http);
        var game = await LoadGameDataAsync(cacheDir, http, refresh: isNew);
        List<Result> Convert(JsonNode data)
        {
            if (!leTools)
                return ConvertAll(id.Length > 0 ? id : "pasted", title, exported ? FromExport((JsonObject)planner!, title, data) : planner!, data, preferred);
            string named = LeTools.Name(found, LeTools.IsSource(id) ? id[LeTools.SourcePrefix.Length..] : "", data);
            return ConvertAll(id.Length > 0 ? id : LeTools.SourcePrefix, named, LeTools.ToPlanner(found, named, data), data);
        }
        List<Result> results;
        try { results = Convert(game); }
        catch (InvalidDataException) when (GameDataAge(cacheDir) > TimeSpan.FromMinutes(5))
        {
            // The kept copy of the game data can be older than the planner (Maxroll updated it since),
            // and then the planner names nodes it does not know. Fetch it again, once, and try again.
            game = await LoadGameDataAsync(cacheDir, http, refresh: true);
            results = Convert(game);
        }
        foreach (var result in results)
        {
            result.Tree.AtlasName = atlas ?? "";
            if (id.Length == 0) result.Tree.SourceId = "";
        }
        return results;
    }

    /// <summary>
    /// Turns what the planner's Export button copies - one version, with its items written out in place
    /// and without a name, a level or a list of its skills - into a planner with that one profile.
    /// </summary>
    internal static JsonObject FromExport(JsonObject export, string name, JsonNode game)
    {
        if (export["class"] is not JsonValue cls || !cls.TryGetValue(out int _))
            throw new InvalidDataException("The pasted planner data has no class in it. In the planner's Export dialog, tick everything (class, passives, skills, items) and copy again.");

        var profile = (JsonObject)export.DeepClone();
        var table = new JsonObject();
        int next = 1;
        JsonNode? Listed(JsonNode? item)
        {
            if (item is not JsonObject written) return item?.DeepClone();
            string key = (next++).ToString();
            table[key] = written.DeepClone();
            return JsonValue.Create(int.Parse(key));
        }
        if (profile["items"] is JsonObject slots)
            profile["items"] = new JsonObject(slots.ToList().Select(kv => KeyValuePair.Create(kv.Key, Listed(kv.Value))));
        if (profile["idols"] is JsonArray idols)
            profile["idols"] = new JsonArray(idols.ToList().Select(Listed).ToArray());
        profile["name"] = name;
        if (profile["level"] is not JsonValue) profile["level"] = 100;

        // The skills it specializes are the ones whose trees have points in them.
        if (profile["specializedSkills"] is not JsonArray && profile["skillTrees"] is JsonObject trees && game["abilities"] is JsonObject abilities)
        {
            var used = trees.Where(kv => History(kv.Value).Count > 0).Select(kv => kv.Key).ToHashSet();
            // Several abilities can share one tree (Teleport also exists as monster and shapeshift
            // versions): take the one the class itself has, else the first.
            var own = ClassAbilities(game, cls.GetValue<int>());
            var skills = abilities.Where(kv => kv.Value is JsonObject ability && ability["playerAbilityID"].StrOrNull() is { } treeId && used.Contains(treeId))
                .GroupBy(kv => kv.Value!["playerAbilityID"].Str())
                .Select(tree => (tree.FirstOrDefault(kv => own.Contains(kv.Key)).Key ?? tree.First().Key))
                .ToList();
            profile["specializedSkills"] = new JsonArray(skills.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
            if (profile["activeSkills"] is not JsonArray)
                profile["activeSkills"] = new JsonArray(skills.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        }
        return new JsonObject { ["profiles"] = new JsonArray(profile), ["items"] = table };
    }

    /// <summary>
    /// Imports a build from a file instead of a link: a build this overlay saved (someone's
    /// "name.tree.json", with "name.txt" beside it if they sent that too), or a planner as Maxroll's
    /// own address answers it. The first needs no network at all.
    /// </summary>
    public static async Task<IReadOnlyList<Result>> ImportFileAsync(string path, string cacheDir, HttpClient http)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new InvalidDataException("There is no file at that path.");
        if (file.Length > 40_000_000) throw new InvalidDataException("That file is far too large to be a build.");
        JsonNode? document;
        try { document = JsonNode.Parse(await File.ReadAllTextAsync(path)); }
        catch (System.Text.Json.JsonException) { document = null; }
        if (document is not JsonObject found)
            throw new InvalidDataException("That file is not a build: it has to be a build file saved by this overlay (name.tree.json) or a Maxroll planner in JSON.");

        static bool Has(JsonObject o, string key) => o.Any(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        // A build saved by this overlay.
        if (Has(found, "trees") && Has(found, "stages"))
        {
            var tree = BuildTree.Load(path);
            if (tree is null || tree.Trees.Count == 0 || tree.Stages.Count == 0)
                throw new InvalidDataException("That build file is empty or damaged.");
            string name = file.Name.EndsWith(".tree.json", StringComparison.OrdinalIgnoreCase) ? file.Name[..^".tree.json".Length] : Path.GetFileNameWithoutExtension(file.Name);
            if (tree.Name.Trim().Length > 0) name = tree.Name.Trim();
            string planFile = Path.Combine(file.DirectoryName ?? "", (file.Name.EndsWith(".tree.json", StringComparison.OrdinalIgnoreCase) ? file.Name[..^".tree.json".Length] : Path.GetFileNameWithoutExtension(file.Name)) + ".txt");
            string text = File.Exists(planFile) ? await File.ReadAllTextAsync(planFile) : $"name: {name}\n# Imported from a build file without its plan text: the trees and gear are here, the per-level list is not.\n";
            tree.Name = name;
            // It arrives alone: the other versions of its guide are not in this file.
            tree.Variants = new List<string>();
            tree.VariantFiles = new List<string>();
            tree.Variant = 0;
            return new[] { new Result(name, text, BuildPlan.Parse(text).Entries.Count, tree) };
        }

        // A Maxroll planner, in any of the shapes it travels in.
        return await FromJsonAsync(found, "", Path.GetFileNameWithoutExtension(file.Name), cacheDir, http);
    }

    /// <summary>
    /// Fetches the icon sheet the planner itself uses. Its file name carries a build hash, so the
    /// current name is read from the planner's stylesheet. Icons are a nicety: failure is not an error.
    /// </summary>
    /// <returns>The sheet's file name in the cache folder (null if it could not be had), and whether it was new.</returns>
    private static async Task<(string? Name, bool IsNew)> DownloadAtlasAsync(string cacheDir, HttpClient http)
    {
        try
        {
            var match = AtlasPath().Match(await http.GetStringAsync(PlannerCssUrl));
            if (!match.Success) return (null, false);
            // Kept under its own (hashed) name: a build imported earlier goes on using the sheet it was made with.
            string name = "maxroll_" + Path.GetFileName(match.Value);
            string target = Path.Combine(cacheDir, name);
            if (File.Exists(target)) return (name, false);
            await File.WriteAllBytesAsync(target, await http.GetByteArrayAsync(AssetHost + match.Value));
            return (name, true);
        }
        catch (HttpRequestException) { }
        catch (IOException) { }
        catch (TaskCanceledException) { }
        return (null, false);
    }

    public const string WeaverPage = "https://maxroll.gg/last-epoch/resources/weaver-tree-strategies";

    /// <summary>
    /// Fetches the Weaver tree and Maxroll's ready-made ways of filling it. They are not part of any
    /// build guide: one article lists them all (starter, general endgame, Nemesis, boss farming, ...),
    /// each as a small planner that holds nothing but a Weaver tree.
    /// </summary>
    public static async Task<WeaverSet> ImportWeaverAsync(string cacheDir, HttpClient http)
    {
        using var answer = await http.GetAsync(WeaverPage);
        if (!answer.IsSuccessStatusCode)
            throw new InvalidDataException($"Maxroll's Weaver tree page could not be read (it answered {(int)answer.StatusCode}).");
        string html = await answer.Content.ReadAsStringAsync();
        var ids = Regex.Matches(html, "<[^<>]*data-le-type=\"weavertree\"[^<>]*>|<[^<>]*data-le-profile=\"[a-z0-9]+\"[^<>]*data-le-type=\"weavertree\"[^<>]*>")
            .Select(tag => EmbeddedPlanner().Match(tag.Value)).Where(m => m.Success).Select(m => m.Groups[1].Value)
            .Where(id => id.Any(char.IsDigit)).Distinct().ToList();
        if (ids.Count == 0) throw new InvalidDataException("No Weaver trees were found on Maxroll's page - it may have been moved or rebuilt.");

        var (atlas, isNew) = await DownloadAtlasAsync(cacheDir, http);
        var game = await LoadGameDataAsync(cacheDir, http, refresh: isNew);
        if (game["skillTrees"] is not JsonObject || game["skillTrees"]!["weaver"] is not JsonObject || game["skillTrees"]!["weaver"]!["nodes"] is not JsonObject nodes)
            throw new InvalidDataException("Maxroll's game data has no Weaver tree in it.");
        var icons = new Dictionary<string, int>();
        if (game["treeAtlas"] is JsonArray cells)
            for (int i = 0; i < cells.Count; i++)
                if (cells[i].StrOrNull() is { } name) icons[name] = i;

        var set = new WeaverSet { AtlasName = atlas ?? "", AtlasCells = icons.Count, Fetched = DateTime.Now };
        set.Tree.Nodes = nodes.Where(kv => int.TryParse(kv.Key, out _) && kv.Value is JsonObject).Select(kv => ToNode(kv.Key, kv.Value!, icons)).ToList();

        foreach (string id in ids)
        {
            try
            {
                using var planner = await http.GetAsync(ProfileUrl + id);
                if (!planner.IsSuccessStatusCode) continue;
                if (JsonNode.Parse(await planner.Content.ReadAsStringAsync()) is not JsonObject raw || raw["data"] is not JsonValue inner
                    || !inner.TryGetValue(out string? data) || JsonNode.Parse(data ?? "") is not JsonObject document || document["profiles"] is not JsonArray profiles) continue;
                string title = raw["name"] is JsonValue titled && titled.TryGetValue(out string? given) && !string.IsNullOrWhiteSpace(given) ? given.Trim() : id;
                var filled = profiles.OfType<JsonObject>()
                    .Select(p => (Name: p["name"] is JsonValue n && n.TryGetValue(out string? pn) ? pn ?? "" : "",
                        History: History(p["weaver"]).Where(node => nodes.ContainsKey(node.ToString())).ToList()))
                    .Where(p => p.History.Count > 0).ToList();
                foreach (var (profileName, history) in filled)
                {
                    string name = filled.Count > 1 && profileName.Length > 0 ? $"{title} - {profileName}" : title;
                    for (int n = 2; set.Strategies.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); n++) name = $"{title} ({n})";
                    set.Strategies.Add(new WeaverStrategy { Name = name, History = history, SourceId = id });
                }
            }
            catch (System.Text.Json.JsonException) { } // one unreadable strategy does not cost the others
            await Task.Delay(250);
        }
        if (set.Strategies.Count == 0) throw new InvalidDataException("Maxroll's Weaver trees could not be read - none of them had points in it.");
        return set;
    }

    /// <summary>What a pasted text turned out to be.</summary>
    /// <param name="PlannerId">Set when the text names a planner directly.</param>
    /// <param name="PageUrl">Set when it is a Maxroll page that has to be read to find its planner.</param>
    /// <param name="Problem">Set when it is neither: a sentence for the player saying why.</param>
    /// <param name="Variant">The version the link points at (0-based): the "#2" on a shared planner link.</param>
    /// <param name="Json">Set when planner data itself was pasted (the planner's Export button).</param>
    /// <param name="LeToolsId">Set when it is a Last Epoch Tools planner link: that planner's id.</param>
    public sealed record Pasted(string? PlannerId = null, string? PageUrl = null, string? Problem = null, int? Variant = null, string? Json = null,
        string? LeToolsId = null);

    [GeneratedRegex(@"last-epoch/planner/[a-z0-9]{6,12}#(\d{1,2})")]
    private static partial Regex LinkedVariant();

    [GeneratedRegex("data-le-id=\"(\\d{1,2})\"")]
    private static partial Regex EmbedVariant();

    /// <summary>
    /// The version of the build a guide page shows: its planner embeds carry the number (1-based) of the
    /// profile they open on, and a guide written around "Endgame" should not be imported as "Starter".
    /// </summary>
    public static int? PickVariant(string html, string plannerId)
    {
        var shown = new List<int>();
        foreach (Match tag in Regex.Matches(html, "<[^<>]*data-le-profile=\"" + Regex.Escape(plannerId) + "\"[^<>]*>"))
        {
            // Only the embeds that show a whole version; tree and skill-bar snippets number something else.
            if (!tag.Value.Contains("data-le-type=\"planner", StringComparison.Ordinal)) continue;
            if (EmbedVariant().Match(tag.Value) is { Success: true } number) shown.Add(int.Parse(number.Groups[1].Value) - 1);
        }
        return shown.Count == 0 ? null : shown.GroupBy(n => n).OrderByDescending(g => g.Count()).First().Key is >= 0 and var index ? index : null;
    }

    [GeneratedRegex(@"https?://[^\s<>""']+|(?:www\.)?maxroll\.gg/[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex AnyLink();

    /// <summary>
    /// Makes sense of whatever was pasted into the import box: a planner link in any of its shapes
    /// (with a #fragment, a ?query, without https, inside a sentence), a bare planner id, a Maxroll guide
    /// page - or something that cannot be imported, in which case it says what and why. No network.
    /// </summary>
    public static Pasted Understand(string? input)
    {
        string text = (input ?? "").Trim().Trim('"', '\'', '<', '>');
        if (text.Length == 0) return new Pasted(Problem: "Paste a Maxroll planner or build guide link first.");
        // Planner data itself (the planner's Export button copies JSON).
        if (text.StartsWith('{')) return new Pasted(Json: text);

        // A planner link anywhere in the text. Ids always carry a digit, which keeps words that follow
        // "planner/" in other Maxroll addresses ("community-builds") from being taken for one.
        foreach (Match link in PlannerLink().Matches(text.ToLowerInvariant()))
            if (link.Groups[1].Value.Any(char.IsDigit))
            {
                // A shared link ends in "#2" (or "#2&...") when the second version was showing.
                int? variant = LinkedVariant().Match(text.ToLowerInvariant()) is { Success: true } hash && int.Parse(hash.Groups[1].Value) >= 1
                    ? int.Parse(hash.Groups[1].Value) - 1 : null;
                return new Pasted(PlannerId: link.Groups[1].Value, Variant: variant);
            }
        if (BareId().IsMatch(text.ToLowerInvariant()) && text.Any(char.IsDigit)) return new Pasted(PlannerId: text.ToLowerInvariant());

        string lower = text.ToLowerInvariant();
        // A path on this computer (the caller imports the file if it exists; this is for when it does not).
        if (text.Length > 2 && (text[1] == ':' || text.StartsWith(@"\\\\", StringComparison.Ordinal)) || lower.EndsWith(".json") || lower.EndsWith(".txt"))
            return new Pasted(Problem: "There is no file at that path. Use 'Import from file' to pick it.");
        if (LeTools.PlannerId(text) is { } leTools) return new Pasted(LeToolsId: leTools);
        if (lower.Contains("lastepochtools.com"))
            return new Pasted(Problem: "Of Last Epoch Tools only planner links can be imported (lastepochtools.com/planner/...). Open the build in its planner and paste the link from the address bar.");
        if (lower.Contains("maxroll.gg/last-epoch/planner"))
            return new Pasted(Problem: "That is Maxroll's planner page, not one build. Open the build you want and paste the link from the address bar (it ends in a code like 3k9hk0gr).");
        if (lower.Contains("maxroll.gg/") && !lower.Contains("maxroll.gg/last-epoch"))
            return new Pasted(Problem: "That Maxroll link is for another game. Paste a Last Epoch planner or build guide link.");
        if (lower.Contains("maxroll.gg/last-epoch"))
        {
            string url = AnyLink().Match(text) is { Success: true } found ? found.Value : text;
            url = url.TrimEnd('.', ',', ')', ';');
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            return Uri.TryCreate(url, UriKind.Absolute, out var page) && page.Host.EndsWith("maxroll.gg", StringComparison.OrdinalIgnoreCase)
                ? new Pasted(PageUrl: new UriBuilder(page) { Scheme = "https", Port = -1 }.Uri.ToString())
                : new Pasted(Problem: "That does not look like a complete link. Copy it from the browser's address bar.");
        }
        if (AnyLink().IsMatch(text))
            return new Pasted(Problem: "Only builds from Maxroll and Last Epoch Tools can be imported. Paste a planner or build guide link from maxroll.gg, or a planner link from lastepochtools.com.");
        return new Pasted(Problem: "That does not look like a Maxroll or Last Epoch Tools planner link, or a Maxroll build guide link.");
    }

    private static async Task<(string Id, int? Variant)> ResolveAsync(Pasted pasted, HttpClient http)
    {
        if (pasted.PlannerId is { } id) return (id, pasted.Variant);
        if (pasted.PageUrl is { } url)
        {
            using var answer = await http.GetAsync(url);
            if (answer.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidDataException("Maxroll has no page at that address - check the link.");
            answer.EnsureSuccessStatusCode();
            string html = await answer.Content.ReadAsStringAsync();
            string planner = PickPlanner(html)
                             ?? throw new InvalidDataException("That Maxroll page has no build planner on it. Open a build guide and paste its link.");
            return (planner, PickVariant(html, planner));
        }
        throw new InvalidDataException(pasted.Problem ?? "That does not look like a Maxroll Last Epoch planner or build guide link.");
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

    /// <summary>
    /// Which copy of Maxroll's game data this machine has, for a bug report: Maxroll's servers hand out
    /// different copies in different places, and a fault in one is invisible with another.
    /// </summary>
    public static string GameDataFacts(string cacheDir)
    {
        string cache = Path.Combine(cacheDir, "maxroll_le_data.json");
        try
        {
            var file = new FileInfo(cache);
            if (!file.Exists) return "none kept";
            using var stream = file.OpenRead();
            string hash = System.Convert.ToHexString(System.Security.Cryptography.MD5.HashData(stream))[..12].ToLowerInvariant();
            return $"{file.Length} bytes, md5 {hash}, fetched {file.LastWriteTimeUtc:yyyy-MM-dd HH:mm} UTC";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "could not be read: " + e.Message; }
    }

    private static TimeSpan GameDataAge(string cacheDir)
    {
        string cache = Path.Combine(cacheDir, "maxroll_le_data.json");
        return File.Exists(cache) ? DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) : TimeSpan.Zero;
    }

    private static async Task<JsonNode> LoadGameDataAsync(string cacheDir, HttpClient http, bool refresh = false)
    {
        string cache = Path.Combine(cacheDir, "maxroll_le_data.json");
        if (refresh || !File.Exists(cache) || DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) > TimeSpan.FromHours(24))
            await File.WriteAllBytesAsync(cache, await http.GetByteArrayAsync(GameDataUrl));
        await using var stream = File.OpenRead(cache);
        return await JsonNode.ParseAsync(stream) ?? throw new InvalidDataException("Maxroll game data is empty.");
    }

    /// <summary>history[..position] as a flat list of node ids. Entries are ints, or {nodeId: count}.</summary>
    private static List<int> History(JsonNode? tree)
    {
        var result = new List<int>();
        if (tree is not JsonObject || tree["history"] is not JsonArray history) return result;
        // "position" is how far into the history the planner's undo pointer stands. Anything that is
        // not a plain entry is skipped rather than allowed to stop the import.
        int position = tree["position"] is JsonValue pointer && pointer.TryGetValue(out int at) ? Math.Clamp(at, 0, history.Count) : history.Count;
        foreach (var entry in history.Take(position))
        {
            if (entry is JsonObject batch)
            {
                foreach (var (node, count) in batch)
                    if (int.TryParse(node, out int id) && count is JsonValue times && times.TryGetValue(out int n) && n is > 0 and <= 30)
                        result.AddRange(Enumerable.Repeat(id, n));
            }
            else if (entry is JsonValue single && single.TryGetValue(out int id)) result.Add(id);
        }
        return result;
    }

    /// <summary>Level for the i-th (1-based) of n points gained while going from level lo to hi.</summary>
    private static int Interpolate(int i, int n, int lo, int hi)
    {
        if (n <= 0 || hi <= lo) return hi;
        return Math.Min(hi, lo + Math.Max(1, (int)Math.Ceiling((double)i * (hi - lo) / n)));
    }

    /// <summary>The abilities a class can have: its own, and those of its masteries' trees.</summary>
    private static HashSet<string> ClassAbilities(JsonNode game, int classIndex)
    {
        var own = new HashSet<string>();
        if (game["classes"] is not JsonArray classes || classIndex < 0 || classIndex >= classes.Count || classes[classIndex] is not JsonObject cls) return own;
        foreach (var a in cls["unlockableAbilities"] as JsonArray ?? new JsonArray()) if (a?["ability"].StrOrNull() is { } unlockable) own.Add(unlockable);
        foreach (var a in cls["knownAbilities"] as JsonArray ?? new JsonArray()) if (a.StrOrNull() is { } known) own.Add(known);
        foreach (var m in cls["masteries"] as JsonArray ?? new JsonArray())
            foreach (var a in m?["abilities"] as JsonArray ?? new JsonArray()) if (a?["ability"].StrOrNull() is { } mastery) own.Add(mastery);
        return own;
    }

    internal static Result Convert(string id, string name, JsonNode data, JsonNode game)
    {
        if (data["profiles"] is not JsonArray profiles || profiles.Count == 0 || profiles.Any(p => p is not JsonObject))
            throw new InvalidDataException("The planner has no profiles.");

        var last = profiles[^1]!;
        // The class decides which trees there are; without one there is nothing to import.
        if (last["class"] is not JsonValue classValue || !classValue.TryGetValue(out int classIndex)
            || game["classes"] is not JsonArray classes || classIndex < 0 || classIndex >= classes.Count)
            throw new InvalidDataException("The planner does not say which class the build is for (or names one this version of the game data does not have).");
        var cls = classes[classIndex]!;
        var passiveTree = game["skillTrees"]![cls["treeID"].Str()]!;
        var masteries = cls["masteries"]!.AsArray();
        string MasteryName(int i) => masteries[i]!["name"].Str();
        string SkillName(string ability) => game["abilities"]?[ability]?["abilityName"].StrOrNull() ?? ability;
        JsonNode? PassiveNode(int node) => passiveTree["nodes"]?[node.ToString()];

        // Base-class skills unlock at a character level; tree skills after N points in that tree.
        var unlock = new Dictionary<string, int>();
        foreach (var a in cls["unlockableAbilities"]!.AsArray())
            unlock[a!["ability"].Str()] = a["level"].Int();
        foreach (var a in cls["knownAbilities"]!.AsArray()) unlock[a.Str()] = 1;
        var treeUnlocks = masteries.Select(m => m!["abilities"]!.AsArray()
            .Select(a => (Need: a!["level"].Int(), Ability: a["ability"].Str()))
            .OrderBy(a => a.Need).ToList()).ToList();

        static List<string> Skills(JsonNode? profile, string key) =>
            profile is JsonObject && profile[key] is JsonArray listed
                ? listed.Select(s => s.StrOrNull()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList()
                : new();
        var used = profiles.SelectMany(p => Skills(p!, "specializedSkills").Concat(Skills(p!, "activeSkills"))).ToHashSet();

        var steps = new List<Step>();
        int seq = 0;
        void Add(int level, string kind, string text, bool exact = false, string? group = null, string? item = null) =>
            steps.Add(new Step(level, kind, text, exact, seq++, group, item));

        var atlas = new Dictionary<string, int>();
        if (game["treeAtlas"] is JsonArray cells)
            for (int i = 0; i < cells.Count; i++)
                if (cells[i].StrOrNull() is { } cell) atlas[cell] = i;
        var build = new BuildTree
        {
            Name = name, AtlasCells = atlas.Count,
            PassiveTabNames = Enumerable.Range(0, masteries.Count).Select(MasteryName).ToList(),
        };
        var skillTrees = new Dictionary<string, JsonNode>();
        var skillTreeIds = new Dictionary<string, string>();

        int prevLevel = 1, prevMastery = 0;
        var prevPassives = new List<int>();
        var prevSpec = new List<string>();
        var prevSkillHistory = new Dictionary<string, List<int>>();

        for (int index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index]!;
            int level = Math.Clamp(profile["level"] is JsonValue levelValue && levelValue.TryGetValue(out int stated) ? stated : 100, 1, 100);
            int lo = index > 0 ? prevLevel : 1;
            // A mastery that is not one of the class's three counts as "none chosen yet".
            int mastery = profile["mastery"] is JsonValue masteryValue && masteryValue.TryGetValue(out int chosen) && chosen >= 0 && chosen < masteries.Count ? chosen : 0;
            int? firstMasteryLevel = null;

            // ---------- passives
            var history = History(profile["passives"]).Where(node => PassiveNode(node) is not null).ToList();
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
                int tree = info?["mastery"].IntOrNull() ?? 0;
                int at = Interpolate(i + 1, added.Count, lo, level);
                string nodeName = info?["nodeName"].StrOrNull() ?? $"node {node}";
                Add(at, "passive", "", group: MasteryName(tree), item: $"{nodeName} ({running[node]}/{info?["maxPoints"].IntOrNull() ?? 0})|{nodeName}");
                if (firstMasteryLevel is null && mastery != 0 && tree == mastery) firstMasteryLevel = at;

                int inTree = running.Where(kv => (PassiveNode(kv.Key)?["mastery"].IntOrNull() ?? 0) == tree).Sum(kv => kv.Value);
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
                if (masteries[mastery]!["masteryAbility"].StrOrNull() is { } masteryAbility) unlock[masteryAbility] = at;
            }

            // ---------- skills
            var spec = Skills(profile, "specializedSkills");
            var skillHistory = new Dictionary<string, List<int>>();
            var dropped = prevSpec.Where(s => !spec.Contains(s)).ToList();
            var kept = prevSpec.Where(spec.Contains).ToList();
            var newly = spec.Where(s => !prevSpec.Contains(s)).ToList();
            foreach (string ability in spec)
            {
                string? treeId = game["abilities"]?[ability]?["playerAbilityID"].StrOrNull();
                var tree = treeId is null ? null : game["skillTrees"]?[treeId];
                var known = treeId is null ? null : game["skillTrees"]?[treeId]?["nodes"] as JsonObject;
                var points = treeId is null || known is null || profile["skillTrees"] is not JsonObject
                    ? new List<int>() : History(profile["skillTrees"]![treeId]).Where(node => known.ContainsKey(node.ToString())).ToList();
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
                    string nodeName = info?["nodeName"].StrOrNull() ?? $"node {node}";
                    Add(Interpolate(i + 1, fresh.Count, start, level), "skill", "", group: skill,
                        item: $"{nodeName} ({count[node]}/{info?["maxPoints"].IntOrNull() ?? 0})|{nodeName}");
                }
            }

            var stage = new TreeStage
            {
                Name = profile["name"].StrOrNull() ?? $"Level {level}",
                Level = level,
                Passives = history,
                // Two abilities with one name (a planner listing a skill twice) give that name's tree once.
                Skills = skillHistory.Where(kv => skillTrees.ContainsKey(SkillName(kv.Key)))
                    .GroupBy(kv => SkillName(kv.Key)).ToDictionary(g => g.Key, g => g.First().Value),
            };
            // The Weaver tree is stored like a skill tree: a click history.
            var weaverKnown = game["skillTrees"]?["weaver"]?["nodes"] as JsonObject;
            var weaver = History(profile["weaver"]).Where(node => weaverKnown?.ContainsKey(node.ToString()) == true).ToList();
            if (weaver.Count > 0) stage.Skills[TreeDef.WeaverName] = weaver;
            // Gear, idols and blessings are extras: a build whose item list cannot be read still has its trees.
            try { MaxrollGear.Fill(stage, profile, data, game); }
            catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or NullReferenceException or ArgumentException
                                          or FormatException or IndexOutOfRangeException or InvalidCastException)
            {
                stage.Gear.Clear();
                stage.Idols.Clear();
                stage.Blessings.Clear();
            }
            build.Stages.Add(stage);

            prevLevel = level;
            prevPassives = history;
            if (mastery != 0) prevMastery = mastery;
            prevSpec = spec;
            prevSkillHistory = skillHistory;
        }

        foreach (var a in cls["unlockableAbilities"]!.AsArray())
        {
            string ability = a!["ability"].Str();
            int level = a["level"].Int();
            if (used.Contains(ability) && level > 1) Add(level, "unlock", $"{SkillName(ability)} unlocks", exact: true);
        }

        // One passive tab per tree the build puts points in (the base class tree always).
        var passiveNodes = passiveTree["nodes"]!.AsObject();
        var usedMasteries = build.Stages.SelectMany(s => s.Passives)
            .Select(n => PassiveNode(n)?["mastery"].IntOrNull() ?? 0).Append(0).Distinct().OrderBy(m => m);
        foreach (int m in usedMasteries)
            build.Trees.Add(new TreeDef
            {
                Name = MasteryName(m),
                Kind = TreeDef.PassiveKind,
                Nodes = passiveNodes.Where(kv => (kv.Value?["mastery"].IntOrNull() ?? 0) == m).Select(kv => ToNode(kv.Key, kv.Value!, atlas)).ToList(),
            });
        foreach (var (skill, tree) in skillTrees)
        {
            // The root node of a skill tree shows the skill's own icon.
            string? skillIcon = tree["ability"].StrOrNull() is { } key ? game["abilities"]?[key]?["sprite"].StrOrNull() : null;
            build.Trees.Add(new TreeDef
            {
                Name = skill,
                Kind = TreeDef.SkillKind,
                TreeId = skillTreeIds.GetValueOrDefault(skill, ""),
                Nodes = tree["nodes"]!.AsObject().Select(kv => ToNode(kv.Key, kv.Value!, atlas, kv.Key == "0" ? skillIcon : null, skillIcon)).ToList(),
            });
        }

        if (build.Stages.Any(s => s.Skills.ContainsKey(TreeDef.WeaverName)) && game["skillTrees"]?["weaver"]?["nodes"] is JsonObject weaverNodes)
            build.Trees.Add(new TreeDef
            {
                Name = TreeDef.WeaverName,
                Kind = TreeDef.WeaverKind,
                TreeId = "weaver",
                Nodes = weaverNodes.Select(kv => ToNode(kv.Key, kv.Value!, atlas)).ToList(),
            });

        return new Result(name, Render(id, name, steps), steps.Count, build);
    }

    /// <param name="fallbackIcon">For a node the data gives no picture (a few trees' root nodes): the skill's own.</param>
    private static TreeNode ToNode(string id, JsonNode node, Dictionary<string, int> atlas, string? iconOverride = null, string? fallbackIcon = null)
    {
        string icon = iconOverride ?? node["icon"].StrOrNull() ?? fallbackIcon ?? "";
        string description = node["altText"].StrOrNull() ?? "";
        if (description.Length == 0) description = node["description"].StrOrNull() ?? "";
        var stats = (node["stats"] as JsonArray)?.Select(s => $"{s?["value"].StrOrNull()} {s?["statName"].StrOrNull()}".Trim())
            .Where(s => s.Length > 0) ?? Enumerable.Empty<string>();
        return new TreeNode
        {
            Id = int.Parse(id),
            Name = node["nodeName"].StrOrNull() ?? $"node {id}",
            Max = node["maxPoints"].IntOrNull() ?? 0,
            X = node["transform"]?["x"].DoubleOrNull() ?? 0,
            // The planner's y axis points up; screens point down.
            Y = -(node["transform"]?["y"].DoubleOrNull() ?? 0),
            Requires = (node["requirements"] as JsonArray)?.Select(r => r!["node"].Int()).ToList() ?? new List<int>(),
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
        text.Append("# Imported from ").Append(LeTools.SourceUrl(id)).Append('\n');
        if (LeTools.IsSource(id)) text.Append("# Build data by Last Epoch Tools (lastepochtools.com). Its gear and idols are not imported.\n");
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
