namespace LastEpochHelper.Core;

/// <summary>A ready-made string for the game's stash search box.</summary>
public sealed record SearchString(string Label, string Text);

/// <summary>
/// Builds stash search strings from a build. The syntax is the game's own (support article "Stash
/// Searching"): macros such as LP1+ or 1T7+, text between slashes as a regular expression, and
/// &amp; | ! to combine them.
/// </summary>
public static class StashSearch
{
    /// <summary>How an affix reads on a tooltip: "+50 Health", not the catalogue name "Added Health".</summary>
    private static string Spoken(string affix)
    {
        string name = affix.StartsWith("Added ", StringComparison.OrdinalIgnoreCase) ? affix[6..] : affix;
        // Regex operators inside a name would change its meaning; none occur in practice except these.
        return name.ToLowerInvariant().Replace("+", "\\+").Replace("(", "").Replace(")", "");
    }

    public static List<SearchString> For(TreeStage stage, int maxAffixes = 8)
    {
        var strings = new List<SearchString>();
        var affixes = stage.WantedAffixes(maxAffixes).Select(a => Spoken(a.Affix)).Distinct().ToList();

        if (affixes.Count > 0)
        {
            string any = "/" + string.Join("|", affixes) + "/";
            strings.Add(new SearchString("Anything with a build affix", any));
            strings.Add(new SearchString("Exalted items with a build affix", "exalted&" + any));
            strings.Add(new SearchString("Idols with a build affix", "Idol&" + any));
        }
        foreach (string affix in affixes)
            strings.Add(new SearchString($"Affix: {affix}", $"/{affix}/"));

        foreach (var item in stage.Gear.Concat(stage.Idols).Where(g => g.Rarity.Length > 0).DistinctBy(g => g.Name))
            strings.Add(new SearchString($"The build's {item.Name}", $"/{item.Name.ToLowerInvariant()}/"));

        strings.Add(new SearchString("Uniques with Legendary Potential", "LP1+"));
        strings.Add(new SearchString("Uniques with 2+ Legendary Potential", "LP2+"));
        strings.Add(new SearchString("Items with a tier 6 or better affix", "1T6+"));
        strings.Add(new SearchString("Items with two tier 6+ affixes", "2T6+"));
        strings.Add(new SearchString("Good crafting bases (20+ forging potential, not corrupted)", "FP20+&!Corrupted"));
        return strings;
    }
}
