namespace LastEpochHelper.Core;

/// <summary>
/// Matches the developer-facing trigger names the game logs ("Storerooms Sidequest Turn In Speak with
/// Heoborean Soldier") to guide task lines ("Storeroom Saboteurs: Return to Heoborean Soldier").
/// The names are free text, so this is a conservative word-overlap match: no match beats a wrong one.
/// </summary>
public static class QuestMatcher
{
    // Words that appear in almost every trigger name or objective and so identify nothing.
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "first", "second", "third", "fourth", "fifth", "main", "side", "quest", "sidequest", "step", "objective",
        "speak", "with", "talk", "enter", "find", "start", "turn", "return", "into", "from", "your", "that", "this",
        "scene", "the", "and", "for", "defeat", "kill", "slay", "reach", "take", "complete", "completed",
    };

    public static HashSet<string> Words(string text)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split(new[] { ' ', ':', ',', '.', '!', '\'', '-', '(', ')', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4 || Noise.Contains(raw)) continue;
            // Crude plural stemming so "Storerooms" meets "Storeroom".
            words.Add(raw.EndsWith('s') && raw.Length > 4 ? raw[..^1] : raw);
        }
        return words;
    }

    /// <summary>Returns the key of the single best matching candidate, or null when unsure.</summary>
    public static string? Match(string trigger, IEnumerable<(string Key, GuideTask Task)> candidates)
    {
        var words = Words(trigger);
        if (words.Count == 0) return null;
        bool isStart = trigger.Contains(" Start", StringComparison.OrdinalIgnoreCase);
        bool isTurnIn = trigger.Contains("Turn In", StringComparison.OrdinalIgnoreCase)
                        || trigger.Contains("Complete", StringComparison.OrdinalIgnoreCase);

        string? best = null;
        int bestScore = 0;
        bool tie = false;
        foreach (var (key, task) in candidates)
        {
            if (task.Type is "tip" or "boss" or "skip" or "go" or "res") continue;
            // A merged line covers several objectives; one trigger must not tick them all.
            if (task.Text.Contains('\u2192')) continue;
            bool isAccept = task.Text.StartsWith("Accept:", StringComparison.Ordinal);
            // "... Sidequest Start ..." is the pickup; anything else must not tick the pickup line.
            if (isStart != isAccept) continue;
            // A reward line is only ticked by an explicit hand-in: it drives the passive/idol counters.
            if (task.HasReward && !isTurnIn) continue;

            int score = Words(task.Text).Count(words.Contains);
            int needed = isAccept || words.Count == 1 ? 1 : 2;
            if (score < needed) continue;
            if (score > bestScore) { best = key; bestScore = score; tie = false; }
            else if (score == bestScore) tie = true;
        }
        return tie ? null : best;
    }
}
