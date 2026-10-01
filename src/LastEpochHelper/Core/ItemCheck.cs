namespace LastEpochHelper.Core;

/// <summary>
/// Judges an item from the text of its tooltip (as read off the screen): is it one of the build's
/// named items, and which of the build's affixes does it carry.
/// </summary>
public static class ItemCheck
{
    public static string Describe(IReadOnlyList<ScreenLine> lines, TreeStage stage)
    {
        if (lines.Count == 0) return "Item check: no text found near the cursor - hover the item so its tooltip shows.";
        var text = lines.Select(l => Letters(l.Text)).ToList();
        // Tooltips print "+50 Health", not the affix's catalogue name "Added Health".
        static string Spoken(string name) => name.StartsWith("Added ", StringComparison.OrdinalIgnoreCase) ? name[6..] : name;
        bool Shows(string name) => Letters(Spoken(name)) is { Length: >= 4 } key && text.Any(t => t.Contains(key, StringComparison.Ordinal));

        var parts = new List<string>();
        // Named items first: a unique or a specific base the build wears.
        foreach (var item in stage.Gear.Concat(stage.Idols).Where(g => g.Name.Length > 0 && Shows(g.Name)).DistinctBy(g => g.Name).Take(2))
            parts.Add(item.Rarity.Length > 0
                ? $"this is the build's {item.Name}{(item.Slot.Length > 0 ? $" ({item.Slot})" : "")}"
                : $"right base: {item.Name}{(item.Slot.Length > 0 ? $" ({item.Slot})" : "")}");

        var wanted = stage.Gear.Concat(stage.Idols).SelectMany(g => g.Affixes)
            .Select(a => a[..Math.Max(0, a.LastIndexOf(" T", StringComparison.Ordinal))])
            .Where(a => a.Length > 0).Distinct().ToList();
        var found = wanted.Where(Shows).ToList();
        // "Health" is inside "Hybrid Health": keep the most specific name of any nested pair.
        found = found.Where(a => !found.Any(b => b != a && Letters(b).Contains(Letters(a), StringComparison.Ordinal))).ToList();

        if (found.Count > 0) parts.Add($"{found.Count} build affix{(found.Count == 1 ? "" : "es")}: {string.Join(", ", found)}");
        return parts.Count > 0
            ? "Item check: " + string.Join("; ", parts) + "."
            : "Item check: none of the build's affixes or items on this one.";
    }

    private static string Letters(string text) => new(text.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
}
