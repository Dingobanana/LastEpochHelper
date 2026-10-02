using System.IO;
using System.Text.Json;

namespace LastEpochHelper.Core;

public sealed class TreeNode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Max { get; set; }
    /// <summary>Position as laid out in the game's tree panel (y grows downwards here).</summary>
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Nodes that must have points before this one opens; drawn as connecting lines.</summary>
    public List<int> Requires { get; set; } = new();
    public string Description { get; set; } = "";
    /// <summary>Cell in the icon atlas (row-major, <see cref="BuildTree.AtlasColumns"/> per row); -1 = no icon.</summary>
    public int IconIndex { get; set; } = -1;
}

/// <summary>One tab of the tree view: a class/mastery passive tree, or one skill's tree.</summary>
public sealed class TreeDef
{
    public const string PassiveKind = "passive";
    public const string SkillKind = "skill";
    /// <summary>The Weaver tree: laid out and ordered like a skill tree, but not one of the game's skills.</summary>
    public const string WeaverKind = "weaver";
    public const string WeaverName = "Weaver";

    public string Name { get; set; } = "";
    public string Kind { get; set; } = PassiveKind;
    /// <summary>The game's id of a skill tree ("" for passive tabs); matches profiles read from the game.</summary>
    public string TreeId { get; set; } = "";
    public List<TreeNode> Nodes { get; set; } = new();
}

/// <summary>One piece of equipment (or idol) the build wears at a stage.</summary>
public sealed class GearItem
{
    public string Slot { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>"" for a normal base, "unique" or "set".</summary>
    public string Rarity { get; set; } = "";
    /// <summary>Affix names with tier, e.g. "Health T5".</summary>
    public List<string> Affixes { get; set; } = new();
    public int Count { get; set; } = 1;
    /// <summary>The game's ids for the affixes and the unique, as loot filters use them.</summary>
    public List<int> AffixIds { get; set; } = new();
    public int? UniqueId { get; set; }
    /// <summary>Kind of item, e.g. "Helmet" - decides which timeline drops it as an echo reward.</summary>
    public string Type { get; set; } = "";
}

/// <summary>A planner profile ("lvl 5 - 26"): the click order of every tree up to that level.</summary>
public sealed class TreeStage
{
    public string Name { get; set; } = "";
    public int Level { get; set; }
    /// <summary>One entry per passive point, in order, across the base and mastery trees.</summary>
    public List<int> Passives { get; set; } = new();
    /// <summary>Skill name -> one entry per skill point, in order.</summary>
    public Dictionary<string, List<int>> Skills { get; set; } = new();
    public List<GearItem> Gear { get; set; } = new();
    public List<GearItem> Idols { get; set; } = new();
    public List<string> Blessings { get; set; } = new();

    /// <summary>Affixes the stage's gear repeats most: what to look for on drops and at the forge.</summary>
    public List<(string Affix, int Count)> WantedAffixes(int max = 8) =>
        Gear.SelectMany(g => g.Affixes)
            .Select(a => a[..Math.Max(0, a.LastIndexOf(" T", StringComparison.Ordinal))])
            .Where(a => a.Length > 0)
            .GroupBy(a => a).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Take(max).Select(g => (g.Key, g.Count())).ToList();
}

/// <summary>The next thing to do in a tree: put <see cref="Count"/> points in a row into one node.</summary>
public sealed record NextRun(int Node, int Count);

/// <summary>What a tree tab should look like right now.</summary>
public sealed record TreeState(
    IReadOnlyDictionary<int, int> Allocated,
    IReadOnlyDictionary<int, int> Target,
    IReadOnlyList<NextRun> Next,
    int Points,
    int StagePoints,
    string Stage)
{
    /// <summary>True when <see cref="Allocated"/> is what the character really has, not what the plan assumes.</summary>
    public bool FromGame { get; init; }
    /// <summary>Nodes with more points than the build gives them at this stage.</summary>
    public IReadOnlySet<int> OffPlan { get; init; } = new HashSet<int>();
}

/// <summary>The structured half of an imported build: tree layouts plus point order, for the tree view.</summary>
public sealed class BuildTree
{
    public const string AtlasFile = "maxroll_tree_atlas.webp";
    public const int AtlasColumns = 16, AtlasCell = 64;

    public string Name { get; set; } = "";
    /// <summary>Number of cells the atlas had at import time; icons are positional, so a different atlas must not be used.</summary>
    public int AtlasCells { get; set; }
    /// <summary>
    /// File name (in the data folder) of the icon sheet this build was imported with. Maxroll renames
    /// the sheet whenever its contents move; "" = an import from before sheets were kept by name.
    /// </summary>
    public string AtlasName { get; set; } = "";
    /// <summary>The Maxroll planner this was imported from, so another version of it can be fetched.</summary>
    public string SourceId { get; set; } = "";
    /// <summary>
    /// Names of the planner's versions when they are alternatives ("Starter", "Endgame", "Aspirational")
    /// rather than steps by level; empty for a leveling planner. <see cref="Variant"/> is the one imported.
    /// </summary>
    public List<string> Variants { get; set; } = new();
    public int Variant { get; set; }
    /// <summary>Plan file of each version (same order as <see cref="Variants"/>): switching is just opening another file.</summary>
    public List<string> VariantFiles { get; set; } = new();
    public List<TreeDef> Trees { get; set; } = new();
    public List<TreeStage> Stages { get; set; } = new();
    /// <summary>The class and all its masteries, as the game names the tabs of its passive panel.</summary>
    public List<string> PassiveTabNames { get; set; } = new();

    /// <summary>Key under which a skill tree's real points are kept: the game's id, or the name for older imports.</summary>
    public static string SkillKey(TreeDef tree) => tree.TreeId.Length > 0 ? tree.TreeId : tree.Name;

    /// <summary>The stage a character of this level is working towards: the first one not yet outgrown.</summary>
    public TreeStage? StageFor(int level) => Stages.FirstOrDefault(s => s.Level >= level) ?? Stages.LastOrDefault();

    public static string PathFor(string planPath) => Path.ChangeExtension(planPath, ".tree.json");

    public static BuildTree? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<BuildTree>(File.ReadAllText(path), Guide.JsonOptions)?.Mended() : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// A file can say "trees": null, leave lists out, or name a tab twice (hand-edited, cut short, from
    /// another version). The rest of the overlay expects lists that are there and tabs it can tell apart.
    /// </summary>
    private BuildTree Mended()
    {
        Name ??= "";
        AtlasName ??= "";
        SourceId ??= "";
        Variants ??= new();
        VariantFiles ??= new();
        PassiveTabNames ??= new();
        Trees = (Trees ?? new()).Where(t => t is not null).ToList();
        Stages = (Stages ?? new()).Where(s => s is not null).ToList();
        var seen = new HashSet<string>();
        foreach (var tree in Trees)
        {
            tree.Name ??= "";
            tree.Kind = tree.Kind is TreeDef.PassiveKind or TreeDef.SkillKind or TreeDef.WeaverKind ? tree.Kind : TreeDef.PassiveKind;
            tree.TreeId ??= "";
            tree.Nodes = (tree.Nodes ?? new()).Where(n => n is not null).ToList();
            foreach (var node in tree.Nodes)
            {
                node.Name ??= "";
                node.Description ??= "";
                node.Requires ??= new();
            }
        }
        // Two tabs with one name could not be told apart; the first one wins.
        Trees = Trees.Where(t => seen.Add(t.Name)).ToList();
        foreach (var stage in Stages)
        {
            stage.Name ??= "";
            stage.Passives ??= new();
            stage.Skills = (stage.Skills ?? new()).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
            stage.Gear = (stage.Gear ?? new()).Where(g => g is not null).ToList();
            stage.Idols = (stage.Idols ?? new()).Where(g => g is not null).ToList();
            stage.Blessings = (stage.Blessings ?? new()).Where(b => b is not null).ToList();
            foreach (var item in stage.Gear.Concat(stage.Idols))
            {
                item.Slot ??= "";
                item.Name ??= "";
                item.Rarity ??= "";
                item.Type ??= "";
                item.Affixes = (item.Affixes ?? new()).Where(a => a is not null).ToList();
                item.AffixIds ??= new();
            }
        }
        if (Variant < 0 || Variant >= Math.Max(1, Variants.Count)) Variant = 0;
        return this;
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Guide.JsonOptions));

    /// <summary>
    /// Passive points a character has: one per level from level 3, plus quest rewards.
    /// </summary>
    public static int PassivePoints(int level, int questPassives) => Math.Max(0, level - 2) + questPassives;

    /// <summary>
    /// The tree after <paramref name="points"/> points. Passive points are shared by all passive tabs,
    /// so the count is over the whole passive history; a skill has its own history.
    /// </summary>
    /// <param name="pin">A stage chosen by hand, instead of the one the points and level point to.</param>
    public TreeState State(TreeDef tree, int points, int level, int nextCount = 3, TreeStage? pin = null)
    {
        if (Stages.Count == 0) return new TreeState(new Dictionary<int, int>(), new Dictionary<int, int>(), Array.Empty<NextRun>(), 0, 0, "");
        points = Math.Max(0, points);
        bool passive = tree.Kind == TreeDef.PassiveKind;
        List<int> History(TreeStage s) => passive ? s.Passives : s.Skills.GetValueOrDefault(tree.Name) ?? new List<int>();

        // Passives: the stage is the first one long enough to hold the points the character has.
        // Skills: skill points do not follow character level closely, so go by the level bracket,
        // but move on once the pointer has run past the end of that stage's order.
        TreeStage stage = pin ?? (passive
            ? Stages.FirstOrDefault(s => s.Passives.Count > points) ?? Stages[^1]
            : Stages.FirstOrDefault(s => s.Level >= level && History(s).Count > points)
              ?? Stages.FirstOrDefault(s => History(s).Count > points)
              ?? Stages.LastOrDefault(s => History(s).Count > 0) ?? Stages[^1]);

        var history = History(stage);
        int taken = Math.Min(points, history.Count);
        var mine = tree.Nodes.Select(n => n.Id).ToHashSet();
        var allocated = Count(history.Take(taken).Where(mine.Contains));
        var target = Count(history.Where(mine.Contains));
        // "Next" is in terms of the whole history, so the user sees when the next point belongs to another
        // tab. Consecutive points into the same node are one step: "put 3 here".
        var next = new List<NextRun>();
        foreach (int node in history.Skip(taken))
        {
            if (next.Count > 0 && next[^1].Node == node) next[^1] = next[^1] with { Count = next[^1].Count + 1 };
            else if (next.Count == nextCount) break;
            else next.Add(new NextRun(node, 1));
        }
        return new TreeState(allocated, target, next, taken, history.Count, stage.Name);
    }

    /// <summary>
    /// The same view built from the points the character really has: what is taken is the truth, and
    /// "next" is the first planned points not yet covered - so points taken out of order are fine.
    /// Returns null when the profile has nothing for this tree.
    /// </summary>
    public TreeState? State(TreeDef tree, ActualTrees actual, int level, int nextCount = 3, TreeStage? pin = null)
    {
        if (Stages.Count == 0) return null;
        bool passive = tree.Kind == TreeDef.PassiveKind;
        Dictionary<int, int>? have = passive ? actual.Passives : actual.Skills.GetValueOrDefault(SkillKey(tree));
        if (have is null) return null;
        int total = have.Values.Sum();
        List<int> History(TreeStage s) => passive ? s.Passives : s.Skills.GetValueOrDefault(tree.Name) ?? new List<int>();

        TreeStage stage = pin ?? Stages.FirstOrDefault(s => (passive || s.Level >= level) && History(s).Count > total)
                          ?? Stages.FirstOrDefault(s => History(s).Count > total)
                          ?? Stages.LastOrDefault(s => History(s).Count > 0) ?? Stages[^1];
        var history = History(stage);
        var mine = tree.Nodes.Select(n => n.Id).ToHashSet();
        var allocated = have.Where(kv => kv.Value > 0 && mine.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var target = Count(history.Where(mine.Contains));
        var wholeTarget = Count(history);

        var remaining = new Dictionary<int, int>(have);
        var next = new List<NextRun>();
        foreach (int node in history)
        {
            if (remaining.GetValueOrDefault(node) > 0) { remaining[node]--; continue; }
            if (next.Count > 0 && next[^1].Node == node) next[^1] = next[^1] with { Count = next[^1].Count + 1 };
            else if (next.Count == nextCount) break;
            else next.Add(new NextRun(node, 1));
        }
        var offPlan = allocated.Where(kv => kv.Value > wholeTarget.GetValueOrDefault(kv.Key)).Select(kv => kv.Key).ToHashSet();
        return new TreeState(allocated, target, next, total, history.Count, stage.Name) { FromGame = true, OffPlan = offPlan };
    }

    public string NodeName(int id, bool passive, string? skill = null)
    {
        foreach (var tree in Trees)
        {
            if (passive != (tree.Kind == TreeDef.PassiveKind)) continue;
            if (!passive && tree.Name != skill) continue;
            if (tree.Nodes.FirstOrDefault(n => n.Id == id) is { } node)
                return passive && Trees.Count(t => t.Kind == TreeDef.PassiveKind) > 1 ? $"{node.Name} [{tree.Name}]" : node.Name;
        }
        return $"node {id}";
    }

    private static Dictionary<int, int> Count(IEnumerable<int> nodes)
    {
        var counts = new Dictionary<int, int>();
        foreach (int node in nodes) counts[node] = counts.GetValueOrDefault(node) + 1;
        return counts;
    }
}
