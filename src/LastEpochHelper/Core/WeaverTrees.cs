using System.IO;
using System.Text.Json;

namespace LastEpochHelper.Core;

/// <summary>One way of filling the Weaver tree: a name ("Nemesis Tree") and the order of its points.</summary>
public sealed class WeaverStrategy
{
    public string Name { get; set; } = "";
    /// <summary>One entry per point, in the order the author placed them.</summary>
    public List<int> History { get; set; } = new();
    /// <summary>The Maxroll planner it came from.</summary>
    public string SourceId { get; set; } = "";
}

/// <summary>
/// The Weaver tree - the endgame tree of the Woven faction, the same for every class - with the
/// ready-made ways of filling it that Maxroll publishes. It does not belong to a build guide, so it is
/// fetched and kept on its own and shown as a tab beside whatever build is imported.
/// </summary>
public sealed class WeaverSet
{
    public const string FileName = "weaver.json";

    public TreeDef Tree { get; set; } = new() { Name = TreeDef.WeaverName, Kind = TreeDef.WeaverKind, TreeId = "weaver" };
    public List<WeaverStrategy> Strategies { get; set; } = new();
    /// <summary>The icon sheet the tree's icons point into; another sheet means no icons.</summary>
    public string AtlasName { get; set; } = "";
    public int AtlasCells { get; set; }
    public DateTime Fetched { get; set; }

    public static WeaverSet? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var set = JsonSerializer.Deserialize<WeaverSet>(File.ReadAllText(path), Guide.JsonOptions);
            if (set?.Tree?.Nodes is null || set.Strategies is null) return null;
            set.Strategies = set.Strategies.Where(s => s is { History: not null } && !string.IsNullOrWhiteSpace(s.Name)).ToList();
            set.AtlasName ??= "";
            foreach (var node in set.Tree.Nodes)
            {
                node.Name ??= "";
                node.Description ??= "";
                node.Requires ??= new();
            }
            return set.Strategies.Count > 0 && set.Tree.Nodes.Count > 0 ? set : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Guide.JsonOptions));

    /// <summary>The tree's layout as a tab for a build; without icons when the build uses another icon sheet.</summary>
    public TreeDef TabFor(BuildTree build) => new()
    {
        Name = TreeDef.WeaverName, Kind = TreeDef.WeaverKind, TreeId = "weaver",
        Nodes = Tree.Nodes.Select(n => new TreeNode
        {
            Id = n.Id, Name = n.Name, Max = n.Max, X = n.X, Y = n.Y, Requires = n.Requires.ToList(), Description = n.Description,
            IconIndex = build.AtlasName == AtlasName && AtlasName.Length > 0 ? n.IconIndex : -1,
        }).ToList(),
    };
}
