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

    public string Name { get; set; } = "";
    public string Kind { get; set; } = PassiveKind;
    public List<TreeNode> Nodes { get; set; } = new();
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
    string Stage);

/// <summary>The structured half of an imported build: tree layouts plus point order, for the tree view.</summary>
public sealed class BuildTree
{
    public const string AtlasFile = "maxroll_tree_atlas.webp";
    public const int AtlasColumns = 16, AtlasCell = 64;

    public string Name { get; set; } = "";
    /// <summary>Number of cells the atlas had at import time; icons are positional, so a different atlas must not be used.</summary>
    public int AtlasCells { get; set; }
    public List<TreeDef> Trees { get; set; } = new();
    public List<TreeStage> Stages { get; set; } = new();

    public static string PathFor(string planPath) => Path.ChangeExtension(planPath, ".tree.json");

    public static BuildTree? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<BuildTree>(File.ReadAllText(path), Guide.JsonOptions) : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
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
    public TreeState State(TreeDef tree, int points, int level, int nextCount = 3)
    {
        if (Stages.Count == 0) return new TreeState(new Dictionary<int, int>(), new Dictionary<int, int>(), Array.Empty<NextRun>(), 0, 0, "");
        points = Math.Max(0, points);
        bool passive = tree.Kind == TreeDef.PassiveKind;
        List<int> History(TreeStage s) => passive ? s.Passives : s.Skills.GetValueOrDefault(tree.Name) ?? new List<int>();

        // Passives: the stage is the first one long enough to hold the points the character has.
        // Skills: skill points do not follow character level closely, so go by the level bracket,
        // but move on once the pointer has run past the end of that stage's order.
        TreeStage stage = passive
            ? Stages.FirstOrDefault(s => s.Passives.Count > points) ?? Stages[^1]
            : Stages.FirstOrDefault(s => s.Level >= level && History(s).Count > points)
              ?? Stages.FirstOrDefault(s => History(s).Count > points)
              ?? Stages.LastOrDefault(s => History(s).Count > 0) ?? Stages[^1];

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
