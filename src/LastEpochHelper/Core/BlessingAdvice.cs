namespace LastEpochHelper.Core;

/// <summary>One blessing the build wants, with the roll ranges from the timeline data.</summary>
public sealed record WantedBlessing(string Name, string Timeline, string Effect, string Grand);

/// <summary>
/// What to pick when the game offers blessings after a timeline boss ("TIMELINE STABILIZED -
/// ACTIVATE BLESSING - Choose a Blessing:"). The offer names no blessings until each icon is
/// hovered, so the advice is the build's own choice for the timeline the boss echo belongs to.
/// When the build takes none from that timeline, <see cref="General"/> holds Maxroll's general picks
/// for it - generic advice, not the build's, and shown as such.
/// </summary>
public sealed record BlessingAdvice(string? Timeline, IReadOnlyList<WantedBlessing> Wanted, IReadOnlyList<WantedBlessing> General)
{
    /// <summary>
    /// The offer's heading, when the screen shows the blessing choice: "TIMELINE STABILIZED" with
    /// a "Blessing" line under it, or "Choose a Blessing" on its own. Null otherwise.
    /// </summary>
    public static ScreenLine? FindOffer(IReadOnlyList<ScreenLine> lines)
    {
        var text = lines.Select(l => (Line: l, Letters: Letters(l.Text))).ToList();
        var heading = text.FirstOrDefault(t => t.Letters.Contains("timelinestabili", StringComparison.Ordinal) && t.Letters.Length <= 24).Line;
        var choose = text.FirstOrDefault(t => t.Letters.Contains("chooseablessin", StringComparison.Ordinal) && t.Letters.Length <= 20).Line;
        if (choose is not null) return heading is not null && heading.Y < choose.Y ? heading : choose;
        if (heading is null) return null;
        // The heading alone could be a banner; the choice has its "ACTIVATE BLESSING" under it.
        return text.Any(t => t.Line.Y > heading.Y && t.Letters.Contains("blessin", StringComparison.Ordinal)) ? heading : null;
    }

    /// <summary>
    /// The blessings to suggest: those of the current stage, or - since leveling stages rarely list
    /// any - those of the last stage that does. Empty when the build lists none at all.
    /// </summary>
    public static IReadOnlyList<string> BuildBlessings(BuildTree? tree, TreeStage? stage)
    {
        if (stage is { Blessings.Count: > 0 }) return stage.Blessings;
        return tree?.Stages.LastOrDefault(s => s.Blessings.Count > 0)?.Blessings ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    /// <summary>
    /// The advice for a boss of <paramref name="timeline"/> (null when unknown: then every blessing
    /// the build lists, each with its timeline). Null when the build lists no blessings.
    /// </summary>
    public static BlessingAdvice? For(EndgameData endgame, IReadOnlyList<string> buildBlessings, string? timeline)
    {
        if (buildBlessings.Count == 0) return null;
        var known = timeline is null ? null : endgame.Timelines.FirstOrDefault(t => t.Name.Equals(timeline, StringComparison.OrdinalIgnoreCase));
        var wanted = new List<WantedBlessing>();
        foreach (string name in buildBlessings)
        {
            var owner = endgame.TimelineOf(name);
            if (known is not null && owner != known) continue;
            string plain = name.StartsWith("Grand ", StringComparison.OrdinalIgnoreCase) ? name[6..] : name;
            var data = owner?.Blessings.FirstOrDefault(b => b.Name.Equals(plain, StringComparison.OrdinalIgnoreCase));
            wanted.Add(new WantedBlessing(plain, owner?.Name ?? "", data?.Effect ?? "", data?.Grand ?? ""));
        }
        var general = known is not null && wanted.Count == 0
            ? known.Blessings.Where(b => b.Recommended).Select(b => new WantedBlessing(b.Name, known.Name, b.Effect, b.Grand)).ToList()
            : new List<WantedBlessing>();
        return new BlessingAdvice(known?.Name, wanted.DistinctBy(w => w.Name).ToList(), general);
    }

    private static string Letters(string text) => new(text.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
}
