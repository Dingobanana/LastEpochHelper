using System.IO;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

/// <summary>
/// Everything an imported build has to satisfy for the rest of the overlay to work with it, whoever
/// made the planner and however they used it. Used on hand-made fixtures here and, on request, on a
/// folder of real planners saved from Maxroll.
/// </summary>
public static class BuildChecks
{
    public static List<string> Problems(MaxrollImporter.Result result)
    {
        var problems = new List<string>();
        var build = result.Tree;
        void Check(bool ok, string what) { if (!ok) problems.Add(what); }

        Check(result.Name.Trim().Length > 0, "no name");
        Check(build.Stages.Count > 0, "no stages");
        Check(build.Trees.Any(t => t.Kind == TreeDef.PassiveKind), "no passive tab");
        Check(build.Trees.Select(t => t.Name).Distinct().Count() == build.Trees.Count, "two tabs with the same name");
        Check(build.Stages.Select(s => s.Name).Distinct().Count() == build.Stages.Count, "two stages with the same name");
        Check(build.Stages.Zip(build.Stages.Skip(1)).All(p => p.Second.Level >= p.First.Level), "stage levels go down");

        var passiveNodes = build.Trees.Where(t => t.Kind == TreeDef.PassiveKind).SelectMany(t => t.Nodes).ToList();
        Check(passiveNodes.Select(n => n.Id).Distinct().Count() == passiveNodes.Count, "a passive node id is in two tabs");
        foreach (var tree in build.Trees)
        {
            var ids = tree.Nodes.Select(n => n.Id).ToHashSet();
            Check(tree.Nodes.Count > 0, $"{tree.Name}: no nodes");
            Check(tree.Nodes.All(n => n.Requires.All(ids.Contains)), $"{tree.Name}: a node requires one that is not in the tree");
            Check(tree.Nodes.All(n => n.IconIndex < build.AtlasCells), $"{tree.Name}: an icon outside the sheet");
        }

        foreach (var stage in build.Stages)
        {
            // Every point of every stage has to land on a node the tree view can draw, within the node's limit.
            var passiveMax = passiveNodes.ToDictionary(n => n.Id, n => n.Max);
            foreach (var group in stage.Passives.GroupBy(n => n))
            {
                // (More points than the node's limit is fine: items add points to single nodes, and builds plan for it.)
                if (!passiveMax.ContainsKey(group.Key)) problems.Add($"{stage.Name}: passive node {group.Key} is in no tab");
            }
            foreach (var (skill, history) in stage.Skills)
            {
                var tree = build.Trees.FirstOrDefault(t => t.Kind != TreeDef.PassiveKind && t.Name == skill);
                if (tree is null) { problems.Add($"{stage.Name}: skill '{skill}' has points but no tab"); continue; }
                var limits = tree.Nodes.ToDictionary(n => n.Id, n => n.Max);
                foreach (var group in history.GroupBy(n => n))
                {
                    if (!limits.ContainsKey(group.Key)) problems.Add($"{stage.Name}: {skill} node {group.Key} is not in its tree");
                }
            }
            foreach (var search in StashSearch.For(stage)) Check(search.Text.Length > 0, $"{stage.Name}: empty stash search string");
        }

        // The tree view at every number of points, for every tab, with and without a stage chosen by hand.
        foreach (var tree in build.Trees)
        {
            int most = build.Stages.Max(s => tree.Kind == TreeDef.PassiveKind ? s.Passives.Count : s.Skills.GetValueOrDefault(tree.Name)?.Count ?? 0);
            foreach (var pin in build.Stages.Cast<TreeStage?>().Prepend(null))
                for (int points = 0; points <= most + 1; points += Math.Max(1, most / 12))
                {
                    var state = build.State(tree, points, level: 1 + points, pin: pin);
                    var limits = tree.Nodes.ToDictionary(n => n.Id, n => n.Max);
                    Check(state.Allocated.All(kv => limits.ContainsKey(kv.Key)), $"{tree.Name}: the view shows a node that is not in the tab");
                    Check(state.Allocated.Values.Sum() <= state.Points, $"{tree.Name}: more points drawn than taken");
                    Check(state.Next.Count <= 3, $"{tree.Name}: more than three next steps");
                    foreach (var next in state.Next) _ = build.NodeName(next.Node, tree.Kind == TreeDef.PassiveKind, tree.Name);
                }
        }

        // The plan text and what is made from it.
        var plan = BuildPlan.Parse(result.Text);
        Check(plan.Name.Length > 0, "the plan text has no name");
        Check(plan.Entries.All(e => e.Level is >= 1 and <= 100), "a plan line outside level 1-100");
        var summary = BuildSummary.From(plan);
        Check(summary.Headlines.Count > 0, "the summary has no headline");

        // A loot filter the game will read.
        if (LootFilters.Generate(build, "check") is { } filter)
        {
            try { Check(XDocument.Parse(filter).Root?.Name.LocalName == "ItemFilter", "the loot filter is not an ItemFilter"); }
            catch (System.Xml.XmlException e) { problems.Add("the loot filter is not valid XML: " + e.Message); }
        }
        Check(LootFilters.FileNameFor(result.Name).IndexOfAny(Path.GetInvalidFileNameChars()) < 0, "the filter file name has characters Windows does not allow");

        // Saved and loaded again, it is the same build.
        string file = Path.Combine(Path.GetTempPath(), $"leh-check-{Guid.NewGuid():N}.json");
        try
        {
            build.Save(file);
            var again = BuildTree.Load(file);
            Check(again is not null && again.Trees.Count == build.Trees.Count && again.Stages.Count == build.Stages.Count
                  && again.Stages.Zip(build.Stages).All(p => p.First.Passives.SequenceEqual(p.Second.Passives)), "saving and loading changed the build");
        }
        finally { File.Delete(file); }
        return problems;
    }
}

public class MaxrollSurveyTests
{
    /// <summary>
    /// Runs the importer and every check over folders of saved planner answers (LEH_MAXROLL_DIR, several
    /// separated by ';', one JSON file per planner) with saved game data (LEH_MAXROLL_GAME), writing what
    /// came out to LEH_MAXROLL_OUT. On request only: the planners are other people's work and stay off the repository.
    /// </summary>
    [Fact]
    public void EverySavedPlanner_ConvertsCleanly_WhenAskedTo()
    {
        string? dirs = Environment.GetEnvironmentVariable("LEH_MAXROLL_DIR"), gameFile = Environment.GetEnvironmentVariable("LEH_MAXROLL_GAME"),
            output = Environment.GetEnvironmentVariable("LEH_MAXROLL_OUT");
        if (dirs is null || gameFile is null || output is null) return;

        var game = JsonNode.Parse(File.ReadAllText(gameFile))!;
        var report = new List<string>();
        int failures = 0, planners = 0, builds = 0, flawed = 0;
        foreach (string file in dirs.Split(';', StringSplitOptions.RemoveEmptyEntries).SelectMany(d => Directory.GetFiles(d, "*.json")).OrderBy(f => f))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            try
            {
                var raw = JsonNode.Parse(File.ReadAllText(file));
                if (raw?["data"]?.GetValue<string>() is not { } data) { report.Add($"{id}: no planner data"); continue; }
                planners++;
                foreach (var result in MaxrollImporter.ConvertAll(id, raw["name"]?.GetValue<string>() ?? id, JsonNode.Parse(data)!, game))
                {
                    builds++;
                    var problems = BuildChecks.Problems(result);
                    if (problems.Count > 0) flawed++;
                    report.Add($"{id}: {result.Name} | {result.Tree.Stages.Count} stage(s) [{string.Join(" / ", result.Tree.Stages.Select(s => $"{s.Name} L{s.Level} {s.Passives.Count}p"))}] "
                               + $"{result.Tree.Trees.Count} tabs, {result.Steps} steps"
                               + (problems.Count > 0 ? "\n      PROBLEMS: " + string.Join("; ", problems.Distinct().Take(8)) : ""));
                }
            }
            catch (Exception e)
            {
                failures++;
                report.Add($"{id}: FAILED {e.GetType().Name}: {e.Message}\n      {e.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("LastEpochHelper"))?.Trim()}");
            }
        }
        report.Insert(0, $"{planners} planners -> {builds} builds; {failures} could not be converted, {flawed} converted with problems");
        File.WriteAllLines(output, report);
        Assert.Equal(0, failures);
        Assert.Equal(0, flawed);
    }

    /// <summary>
    /// Damages real planners in random ways (LEH_MAXROLL_FUZZ = number of damaged copies per planner) and
    /// imports each copy. Whatever is missing or wrong, the import may refuse with a message - it must
    /// not fail in any other way, and what it does produce must still pass every check.
    /// </summary>
    [Fact]
    public void DamagedPlanners_AreRefusedOrImportedCleanly_WhenAskedTo()
    {
        string? dirs = Environment.GetEnvironmentVariable("LEH_MAXROLL_DIR"), gameFile = Environment.GetEnvironmentVariable("LEH_MAXROLL_GAME"),
            output = Environment.GetEnvironmentVariable("LEH_MAXROLL_OUT");
        if (dirs is null || gameFile is null || output is null || !int.TryParse(Environment.GetEnvironmentVariable("LEH_MAXROLL_FUZZ"), out int copies)) return;

        var game = JsonNode.Parse(File.ReadAllText(gameFile))!;
        var random = new Random(20261002);
        var outcomes = new Dictionary<string, int>();
        var examples = new Dictionary<string, string>();
        void Count(string outcome, string example)
        {
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
            examples.TryAdd(outcome, example);
        }

        foreach (string file in dirs.Split(';', StringSplitOptions.RemoveEmptyEntries).SelectMany(d => Directory.GetFiles(d, "*.json")).OrderBy(f => f))
        {
            if (JsonNode.Parse(File.ReadAllText(file))?["data"]?.GetValue<string>() is not { } data) continue;
            string id = Path.GetFileNameWithoutExtension(file);
            for (int copy = 0; copy < copies; copy++)
            {
                var planner = JsonNode.Parse(data)!;
                string damage = string.Join(" + ", Enumerable.Range(0, 1 + random.Next(3)).Select(_ => Damage(planner, random)));
                try
                {
                    var results = MaxrollImporter.ConvertAll(id, "Fuzz", planner, game);
                    var problems = results.SelectMany(BuildChecks.Problems).Distinct().ToList();
                    if (problems.Count == 0) Count("imported cleanly", "");
                    else Count("imported with problems: " + System.Text.RegularExpressions.Regex.Replace(problems[0], @"\d+", "N"), $"{id}: {damage} -> {problems[0]}");
                }
                catch (InvalidDataException e)
                {
                    string site = e.InnerException?.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("LastEpochHelper"))?.Trim() ?? "";
                    site = System.Text.RegularExpressions.Regex.Match(site, @"Core\.(\w+\.\w+)").Groups[1].Value + System.Text.RegularExpressions.Regex.Match(site, @":line \d+").Value;
                    Count($"refused with a message ({e.InnerException?.GetType().Name ?? "by design"} in {site})", $"{id}: {damage} -> {e.Message}");
                }
                catch (Exception e) { Count("FAILED " + e.GetType().Name, $"{id}: {damage} -> {e.Message} @ {e.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("LastEpochHelper"))?.Trim()}"); }
            }
        }
        File.WriteAllLines(output, outcomes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value,6}  {kv.Key}\n          e.g. {examples[kv.Key]}"));
        Assert.DoesNotContain(outcomes.Keys, k => k.StartsWith("FAILED", StringComparison.Ordinal));
    }

    /// <summary>
    /// The same damage done to Maxroll's game data instead (LEH_MAXROLL_GAMEFUZZ = number of damaged
    /// copies): the table the importer leans on can change shape with a patch, and then every import
    /// must be refused in words rather than fail.
    /// </summary>
    [Fact]
    public void DamagedGameData_IsSurvived_WhenAskedTo()
    {
        string? dirs = Environment.GetEnvironmentVariable("LEH_MAXROLL_DIR"), gameFile = Environment.GetEnvironmentVariable("LEH_MAXROLL_GAME"),
            output = Environment.GetEnvironmentVariable("LEH_MAXROLL_OUT");
        if (dirs is null || gameFile is null || output is null || !int.TryParse(Environment.GetEnvironmentVariable("LEH_MAXROLL_GAMEFUZZ"), out int copies)) return;

        // The parts of the game data the importer reads; damage is aimed there, not at the megabytes it never touches.
        string[] read = { "classes", "abilities", "skillTrees", "treeAtlas", "affixes", "itemTypes", "uniques" };
        string gameText = File.ReadAllText(gameFile);
        var planners = dirs.Split(';', StringSplitOptions.RemoveEmptyEntries).SelectMany(d => Directory.GetFiles(d, "*.json")).OrderBy(f => f)
            .Select(f => (Id: Path.GetFileNameWithoutExtension(f), Data: JsonNode.Parse(File.ReadAllText(f))?["data"]?.GetValue<string>()))
            .Where(p => p.Data is not null).Take(12).ToList();
        var random = new Random(4242);
        var outcomes = new Dictionary<string, int>();
        var examples = new Dictionary<string, string>();
        for (int copy = 0; copy < copies; copy++)
        {
            var game = JsonNode.Parse(gameText)!.AsObject();
            string part = read[random.Next(read.Length)];
            string damage = part + ": " + (game[part] is { } section && random.Next(6) > 0 ? Damage(section, random) : RemoveKey(game, part));
            foreach (var (id, data) in planners)
            {
                string outcome;
                try
                {
                    var results = MaxrollImporter.ConvertAll(id, "Fuzz", JsonNode.Parse(data!)!, game);
                    outcome = results.SelectMany(BuildChecks.Problems).Any() ? "imported with problems" : "imported cleanly";
                    if (outcome != "imported cleanly") examples.TryAdd(outcome, $"{id}: {damage} -> {results.SelectMany(BuildChecks.Problems).First()}");
                }
                catch (InvalidDataException) { outcome = "refused with a message"; }
                catch (Exception e)
                {
                    outcome = "FAILED " + e.GetType().Name;
                    examples.TryAdd(outcome, $"{id}: {damage} -> {e.Message} @ {e.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("LastEpochHelper"))?.Trim()}");
                }
                outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
            }
        }
        File.WriteAllLines(output, outcomes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value,6}  {kv.Key}" + (examples.TryGetValue(kv.Key, out string? example) ? "\n          e.g. " + example : "")));
        Assert.DoesNotContain(outcomes.Keys, k => k.StartsWith("FAILED", StringComparison.Ordinal));
    }

    private static string RemoveKey(JsonObject game, string key)
    {
        game.Remove(key);
        return "removed";
    }

    /// <summary>
    /// The real thing, on request (LEH_LIVE_IMPORT = inputs separated by '|'): each input is imported from
    /// Maxroll as the settings window would, and what happened is written to LEH_MAXROLL_OUT.
    /// </summary>
    [Fact]
    public async Task LiveImports_WhenAskedTo()
    {
        string? inputs = Environment.GetEnvironmentVariable("LEH_LIVE_IMPORT"), output = Environment.GetEnvironmentVariable("LEH_MAXROLL_OUT");
        if (inputs is null || output is null) return;
        string cache = Path.Combine(Path.GetTempPath(), "leh-live-import");
        Directory.CreateDirectory(cache);
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LastEpochHelper/0.2");
        var report = new List<string>();
        foreach (string input in inputs.Split('|'))
        {
            try
            {
                var results = await MaxrollImporter.ImportAsync(input, cache, http);
                var files = BuildFiles.Write(Path.Combine(cache, "builds"), results);
                var problems = results.SelectMany(BuildChecks.Problems).Distinct().ToList();
                report.Add($"OK       {input}\n         -> {results.Count} build(s), in use: '{results[0].Name}', stages [{string.Join(" / ", results[0].Tree.Stages.Select(s => s.Name))}], "
                           + $"versions [{string.Join(", ", results[0].Tree.Variants)}], icon sheet '{results[0].Tree.AtlasName}', files {files.Count}"
                           + (problems.Count > 0 ? "\n         PROBLEMS: " + string.Join("; ", problems) : ""));
            }
            catch (InvalidDataException e) { report.Add($"REFUSED  {input}\n         -> {e.Message}"); }
            catch (Exception e) { report.Add($"FAILED   {input}\n         -> {e.GetType().Name}: {e.Message}"); }
            await Task.Delay(500);
        }
        File.WriteAllLines(output, report);
    }

    /// <summary>The real Weaver trees, on request (LEH_LIVE_WEAVER = folder to save weaver.json in; a summary goes to LEH_MAXROLL_OUT).</summary>
    [Fact]
    public async Task LiveWeaverTrees_WhenAskedTo()
    {
        string? folder = Environment.GetEnvironmentVariable("LEH_LIVE_WEAVER"), output = Environment.GetEnvironmentVariable("LEH_MAXROLL_OUT");
        if (folder is null || output is null) return;
        Directory.CreateDirectory(folder);
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LastEpochHelper/0.2");
        var set = await MaxrollImporter.ImportWeaverAsync(folder, http);
        set.Save(Path.Combine(folder, WeaverSet.FileName));
        var ids = set.Tree.Nodes.Select(n => n.Id).ToHashSet();
        File.WriteAllLines(output, new[] { $"{set.Tree.Nodes.Count} nodes ({set.Tree.Nodes.Count(n => n.IconIndex < 0)} without icon), icon sheet '{set.AtlasName}'" }
            .Concat(set.Strategies.Select(s => $"{s.Name}: {s.History.Count} points, {s.History.Distinct().Count()} nodes, all known: {s.History.All(ids.Contains)} ({s.SourceId})")));
        Assert.NotEmpty(set.Strategies);
    }

    /// <summary>One random change somewhere in the document; returns a description of it.</summary>
    private static string Damage(JsonNode root, Random random)
    {
        // Walk to a random place.
        var places = new List<(JsonNode Parent, string? Key, int Index)>();
        void Walk(JsonNode node)
        {
            if (node is JsonObject o)
                foreach (var (key, child) in o.ToList())
                {
                    places.Add((o, key, -1));
                    if (child is not null) Walk(child);
                }
            else if (node is JsonArray a)
                for (int i = 0; i < Math.Min(a.Count, 40); i++)
                {
                    places.Add((a, null, i));
                    if (a[i] is { } child) Walk(child);
                }
        }
        Walk(root);
        if (places.Count == 0) return "nothing to damage";
        var (parent, key, index) = places[random.Next(places.Count)];
        string where = key ?? $"[{index}]";
        JsonNode? Replacement(int kind) => kind switch
        {
            0 => null,
            1 => JsonValue.Create(random.Next(-5, 100000)),
            2 => JsonValue.Create("nonsense"),
            3 => new JsonArray(),
            4 => new JsonObject(),
            _ => JsonValue.Create(true),
        };
        int action = random.Next(8);
        if (action == 0)
        {
            if (parent is JsonObject o) o.Remove(key!); else ((JsonArray)parent).RemoveAt(index);
            return $"removed {where}";
        }
        var value = Replacement(random.Next(6));
        string what = value?.ToJsonString() ?? "null";
        if (parent is JsonObject obj) obj[key!] = value; else ((JsonArray)parent)[index] = value;
        return $"{where} = {what}";
    }
}
