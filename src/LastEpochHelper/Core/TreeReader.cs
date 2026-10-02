using System.Text.RegularExpressions;

namespace LastEpochHelper.Core;

/// <summary>
/// Reads how many points each node has from the game's own tree panel. The game prints "2/6" under
/// every node; those labels form the same pattern as the node layout the build was imported with,
/// just scaled and moved. Finding that scale and shift tells which label belongs to which node.
/// </summary>
public static partial class TreeReader
{
    /// <summary>A "have/max" label and the centre of where it was on screen.</summary>
    public sealed record Token(double X, double Y, int Have, int Max);

    private const double PairTolerance = 10, MatchTolerance = 14;
    private const int MinMatches = 4;

    [GeneratedRegex(@"^(\d{1,2})/(\d{1,2})$")]
    private static partial Regex Label();

    /// <summary>The node-count labels among words read off the screen.</summary>
    public static List<Token> Tokens(IEnumerable<ScreenLine> words)
    {
        var tokens = new List<Token>();
        foreach (var word in words)
        {
            // The reader confuses a few look-alikes in these short labels.
            string text = word.Text.Replace('O', '0').Replace('o', '0').Replace('l', '1').Replace('I', '1').Replace('|', '1').Trim();
            var m = Label().Match(text);
            if (!m.Success) continue;
            int have = int.Parse(m.Groups[1].Value), max = int.Parse(m.Groups[2].Value);
            if (max is < 1 or > 15 || have > max) continue; // "78/78" is the mana globe, not a node
            tokens.Add(new Token(word.X + word.Width / 2, word.Y + word.Height / 2, have, max));
        }
        return tokens;
    }

    /// <summary>
    /// Combines the labels of several reads of the same picture: each read misses different ones.
    /// Where two reads see a label in the same place, the earlier list wins.
    /// </summary>
    public static List<Token> Merge(params IEnumerable<Token>[] reads)
    {
        var merged = new List<Token>();
        foreach (var read in reads)
            foreach (var token in read)
                if (!merged.Any(m => Math.Abs(m.X - token.X) < 25 && Math.Abs(m.Y - token.Y) < 18))
                    merged.Add(token);
        return merged;
    }

    /// <summary>
    /// Points per node id for the nodes whose label could be placed. Null when the labels do not
    /// fit this tree (another tab is showing, or too little of it is visible).
    /// </summary>
    public static Dictionary<int, int>? Read(IReadOnlyList<Token> tokens, TreeDef tree) => Fit(tokens, tree)?.Points;

    /// <summary>The tree among <paramref name="candidates"/> that the labels fit best, with its points.</summary>
    public static (TreeDef Tree, Dictionary<int, int> Points)? ReadBest(IReadOnlyList<Token> tokens, IEnumerable<TreeDef> candidates)
    {
        (TreeDef Tree, Dictionary<int, int> Points)? best = null;
        foreach (var tree in candidates)
            if (Fit(tokens, tree) is { } fit && (best is null || fit.Points.Count > best.Value.Points.Count))
                best = (tree, fit.Points);
        return best;
    }

    private sealed record Match(Dictionary<int, int> Points);

    private static Match? Fit(IReadOnlyList<Token> tokens, TreeDef tree)
    {
        var nodes = tree.Nodes.Where(n => n.Max >= 1).ToList();
        if (tokens.Count < MinMatches || nodes.Count < MinMatches) return null;

        Dictionary<int, int>? best = null;
        for (int i = 0; i < tokens.Count; i++)
        for (int j = 0; j < tokens.Count; j++)
        {
            if (i == j) continue;
            double tx = tokens[j].X - tokens[i].X, ty = tokens[j].Y - tokens[i].Y;
            double tokenDistance = Math.Sqrt(tx * tx + ty * ty);
            if (tokenDistance < 60) continue;

            foreach (var p in nodes)
            {
                if (p.Max != tokens[i].Max) continue;
                foreach (var q in nodes)
                {
                    if (q == p || q.Max != tokens[j].Max) continue;
                    double nx = q.X - p.X, ny = q.Y - p.Y;
                    double nodeDistance = Math.Sqrt(nx * nx + ny * ny);
                    if (nodeDistance < 1) continue;
                    double scale = tokenDistance / nodeDistance;
                    if (scale is < 0.3 or > 5) continue;
                    // Same direction as well as same length: the panel is neither rotated nor stretched.
                    if (Math.Abs(nx * scale - tx) > PairTolerance || Math.Abs(ny * scale - ty) > PairTolerance) continue;

                    double shiftX = tokens[i].X - p.X * scale, shiftY = tokens[i].Y - p.Y * scale;
                    var points = Assign(tokens, nodes, scale, shiftX, shiftY);
                    if (best is null || points.Count > best.Count) best = points;
                }
            }
        }
        // Most labels must fall on a node, and a fair share of the tree must be accounted for:
        // a handful of coincidences is not a tree, and a wrong fit would report its points as zero.
        return best is not null && best.Count >= MinMatches && best.Count * 10 >= tokens.Count * 6 && best.Count * 5 >= nodes.Count * 2
            ? new Match(best) : null;
    }

    private static Dictionary<int, int> Assign(IReadOnlyList<Token> tokens, List<TreeNode> nodes, double scale, double shiftX, double shiftY)
    {
        var points = new Dictionary<int, int>();
        foreach (var token in tokens)
        {
            TreeNode? nearest = null;
            double nearestDistance = MatchTolerance;
            foreach (var node in nodes)
            {
                if (node.Max != token.Max || points.ContainsKey(node.Id)) continue;
                double dx = node.X * scale + shiftX - token.X, dy = node.Y * scale + shiftY - token.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < nearestDistance) { nearest = node; nearestDistance = distance; }
            }
            if (nearest is not null) points[nearest.Id] = token.Have;
        }
        return points;
    }
}
