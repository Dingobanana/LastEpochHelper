namespace LastEpochHelper.Core;

/// <summary>A line of text read off the screen, with the pixel height of its letters and where it was (screen pixels).</summary>
public sealed record ScreenLine(string Text, double Height, double X = 0, double Y = 0, double Width = 0);

/// <summary>
/// Decides which skill's tree the game is showing from the text on screen. The open tree prints
/// its skill name as a heading, so the match in the biggest letters wins; if several skill names
/// are on screen at about the same size (a list of skills, a tooltip) nothing is picked.
/// </summary>
public static class SkillTitleMatcher
{
    private const double ClearlyBigger = 1.2;

    /// <summary>The line the picked skill name was read from, so the next look can go straight there.</summary>
    public static (string Skill, ScreenLine Line)? PickLine(IReadOnlyList<ScreenLine> lines, IReadOnlyList<string> skillNames)
    {
        if (Pick(lines, skillNames) is not { } skill) return null;
        string key = Letters(skill);
        var line = lines.Where(l => Letters(l.Text).Contains(key, StringComparison.Ordinal)).OrderByDescending(l => l.Height).First();
        return (skill, line);
    }

    /// <summary>
    /// For a heading the reader got slightly wrong: the skill whose name is nearest to some line,
    /// allowing about one wrong letter in six. Only for use when a skill tree is known to be open.
    /// </summary>
    public static (string Skill, ScreenLine Line)? PickClosest(IReadOnlyList<ScreenLine> lines, IReadOnlyList<string> skillNames)
    {
        (string Skill, ScreenLine Line, int Distance)? best = null;
        foreach (var line in lines)
        {
            string text = Letters(line.Text);
            if (text.Length is < 4 or > 40) continue;
            foreach (string name in skillNames)
            {
                string key = Letters(name);
                if (key.Length < 5 || Math.Abs(key.Length - text.Length) > 3) continue;
                int distance = Distance(text, key);
                if (distance == 0 || distance > Math.Max(1, key.Length / 6)) continue;
                if (best is null || distance < best.Value.Distance || (distance == best.Value.Distance && line.Height > best.Value.Line.Height))
                    best = (name, line, distance);
            }
        }
        return best is { } found ? (found.Skill, found.Line) : null;
    }

    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            var row = new int[b.Length + 1];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
                row[j] = Math.Min(Math.Min(row[j - 1], previous[j]) + 1, previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = row;
        }
        return previous[b.Length];
    }

    public static string? Pick(IEnumerable<ScreenLine> lines, IEnumerable<string> skillNames)
    {
        var best = new Dictionary<string, double>();
        var names = skillNames.Select(n => (Name: n, Key: Letters(n))).Where(n => n.Key.Length >= 4).ToList();
        foreach (var line in lines)
        {
            string text = Letters(line.Text);
            foreach (var (name, key) in names)
                if (text.Contains(key, StringComparison.Ordinal) && line.Height > best.GetValueOrDefault(name))
                    best[name] = line.Height;
        }
        if (best.Count == 0) return null;
        var ranked = best.OrderByDescending(kv => kv.Value).ToList();
        // "Tornado" is also inside other names' lines only if those are on screen too; a longer
        // name that matched the same line is the more specific reading.
        if (ranked.Count > 1 && ranked[0].Value < ranked[1].Value * ClearlyBigger)
        {
            var tied = ranked.Where(r => r.Value * ClearlyBigger > ranked[0].Value).ToList();
            var longest = tied.OrderByDescending(r => r.Key.Length).First();
            bool contained = tied.All(r => Letters(longest.Key).Contains(Letters(r.Key), StringComparison.Ordinal));
            return contained ? longest.Key : null;
        }
        return ranked[0].Key;
    }

    /// <summary>Lower-case letters only: OCR is unreliable about spaces, apostrophes and case.</summary>
    private static string Letters(string text) => new(text.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
}
