namespace LastEpochHelper.Core;

/// <summary>A line of text read off the screen, with the pixel height of its letters.</summary>
public sealed record ScreenLine(string Text, double Height);

/// <summary>
/// Decides which skill's tree the game is showing from the text on screen. The open tree prints
/// its skill name as a heading, so the match in the biggest letters wins; if several skill names
/// are on screen at about the same size (a list of skills, a tooltip) nothing is picked.
/// </summary>
public static class SkillTitleMatcher
{
    private const double ClearlyBigger = 1.2;

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
