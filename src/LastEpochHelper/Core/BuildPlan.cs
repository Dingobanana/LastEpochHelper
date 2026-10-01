using System.IO;
using System.Text.RegularExpressions;

namespace LastEpochHelper.Core;

/// <summary>Something to do when the character reaches <see cref="Level"/>.</summary>
public sealed record PlanEntry(int Level, string Text, string Key);

/// <summary>
/// A per-level to-do list for a character. Plain text, one entry per line:
/// <code>
/// name: Tornado Shaman
/// # comment
/// 5: Specialize Gathering Storm
/// 10-14: Passives: Natural Attunement 5/8
/// </code>
/// A range ("10-14") becomes due at its first level.
/// </summary>
public sealed partial class BuildPlan
{
    public string Name { get; private set; } = "";
    public List<PlanEntry> Entries { get; } = new();

    [GeneratedRegex(@"^\s*(?:lvl|level)?\s*(\d{1,3})\s*(?:-\s*(\d{1,3}))?\s*[:.)]\s*(.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EntryLine();

    /// <summary>Milestones every character hits, shown even without a build plan.</summary>
    public static readonly PlanEntry[] Milestones =
    {
        new(4, "1st skill specialization slot unlocked - specialize your main skill", "milestone:4"),
        new(8, "2nd skill specialization slot unlocked", "milestone:8"),
        new(20, "3rd skill specialization slot unlocked", "milestone:20"),
        new(35, "4th skill specialization slot unlocked", "milestone:35"),
        new(50, "5th (last) skill specialization slot unlocked", "milestone:50"),
    };

    public static BuildPlan Parse(string text, string fallbackName = "")
    {
        var plan = new BuildPlan { Name = fallbackName };
        int n = 0;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
            {
                plan.Name = line[5..].Trim();
                continue;
            }
            var m = EntryLine().Match(line);
            if (!m.Success) continue;
            int level = int.Parse(m.Groups[1].Value);
            string prefix = m.Groups[2].Success ? $"Lvl {level}-{m.Groups[2].Value}: " : "";
            plan.Entries.Add(new PlanEntry(level, prefix + m.Groups[3].Value, $"plan:{n++}:{level}"));
        }
        plan.Entries.Sort((a, b) => a.Level.CompareTo(b.Level));
        return plan;
    }

    public static BuildPlan? Load(string path)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path)) : null; }
        catch (IOException) { return null; }
    }

    public const string Template =
        "name: My build\n" +
        "# One line per step: <level>: <what to do>. A range like 10-14 is shown from level 10.\n" +
        "# Click a line in the overlay to tick it off.\n" +
        "2: First passive point - put it in ...\n" +
        "4: Specialize your main skill\n" +
        "10-14: Passives: ...\n";

    /// <summary>Entries that are due at <paramref name="level"/> and not ticked, plus the next one coming up.</summary>
    public static (List<PlanEntry> Due, PlanEntry? Next) View(BuildPlan? plan, int level, ISet<string> done, int maxDue = 4)
    {
        var all = (plan?.Entries ?? Enumerable.Empty<PlanEntry>()).Concat(Milestones).OrderBy(e => e.Level).ToList();
        var due = all.Where(e => e.Level <= level && !done.Contains(e.Key)).ToList();
        // The newest entries are the relevant ones if a backlog built up.
        if (due.Count > maxDue) due = due.GetRange(due.Count - maxDue, maxDue);
        return (due, all.FirstOrDefault(e => e.Level > level));
    }
}
