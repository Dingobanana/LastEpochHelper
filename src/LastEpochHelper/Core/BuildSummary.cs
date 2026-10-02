using System.Text.RegularExpressions;

namespace LastEpochHelper.Core;

/// <param name="Kind">"passive", "mastery", "specialize" or "respec".</param>
public sealed record SummaryStep(int Level, int ToLevel, string Text, string Kind);

/// <summary>
/// The short version of an imported build: which mastery it ends up choosing, which passive trees it
/// fills in which order, and which skills it specializes when - without the node-by-node detail.
/// Made from the plan's own lines, so it also works for builds imported by earlier versions.
/// </summary>
public sealed partial record BuildSummary(IReadOnlyList<string> Headlines, IReadOnlyList<SummaryStep> Steps)
{
    [GeneratedRegex(@"^(?:Lvl \d+-\d+: )?Passives \[(?<tree>[^\]]+)\]: (?<nodes>.+)$")]
    private static partial Regex PassiveLine();

    [GeneratedRegex(@"(?<node>[^,(]+?) \((?<have>\d+)/\d+\)")]
    private static partial Regex NodePoints();

    [GeneratedRegex(@"^Choose mastery: (?<name>.+)$")]
    private static partial Regex MasteryLine();

    [GeneratedRegex(@"^Specialize (?<skill>.+?)(?: \(replaces (?<old>.+)\))?$")]
    private static partial Regex SpecializeLine();

    [GeneratedRegex(@"^plan:(\d+):")]
    private static partial Regex Sequence();

    private sealed class Phase
    {
        public string Tree = "";
        public int From, To, Points, Order;
    }

    public static BuildSummary From(BuildPlan plan)
    {
        // The plan is sorted by level only; the number in each key is the order the build gives.
        var entries = plan.Entries.OrderBy(e => e.Level)
            .ThenBy(e => Sequence().Match(e.Key) is { Success: true } m && int.TryParse(m.Groups[1].Value, out int n) ? n : 0).ToList();

        var steps = new List<(int Order, SummaryStep Step)>();
        var phases = new List<Phase>();
        var have = new Dictionary<string, int>();
        var perTree = new Dictionary<string, int>();
        string? mastery = null;
        int masteryLevel = 0, order = 0;
        var specialized = new List<string>();

        foreach (var entry in entries)
        {
            order++;
            string text = entry.Text;
            if (PassiveLine().Match(text) is { Success: true } passive)
            {
                string tree = passive.Groups["tree"].Value;
                int added = 0;
                foreach (Match node in NodePoints().Matches(passive.Groups["nodes"].Value))
                {
                    string key = tree + "/" + node.Groups["node"].Value.Trim();
                    // (A number no node can hold is someone's typo in a hand-written plan, not points.)
                    if (!int.TryParse(node.Groups["have"].Value, out int now) || now > 100) continue;
                    // The plan gives running totals per node; a respec can lower one, which adds nothing.
                    int before = have.GetValueOrDefault(key);
                    added += now > before ? now - before : 0;
                    have[key] = now;
                }
                perTree[tree] = perTree.GetValueOrDefault(tree) + added;
                if (phases.Count > 0 && phases[^1].Tree == tree) { phases[^1].To = entry.Level; phases[^1].Points += added; }
                else phases.Add(new Phase { Tree = tree, From = entry.Level, To = entry.Level, Points = added, Order = order });
            }
            else if (MasteryLine().Match(text) is { Success: true } chosen)
            {
                mastery = chosen.Groups["name"].Value.Trim();
                masteryLevel = entry.Level;
                steps.Add((order, new SummaryStep(entry.Level, entry.Level, $"Choose your mastery: {mastery}", "mastery")));
            }
            else if (SpecializeLine().Match(text) is { Success: true } spec)
            {
                string skill = spec.Groups["skill"].Value.Trim();
                specialized.Remove(spec.Groups["old"].Value.Trim());
                specialized.Add(skill);
                steps.Add((order, new SummaryStep(entry.Level, entry.Level, spec.Groups["old"].Success
                    ? $"Specialize {skill} - it takes the slot of {spec.Groups["old"].Value.Trim()}"
                    : $"Specialize {skill}", "specialize")));
            }
            else if (text.StartsWith("Respec", StringComparison.Ordinal))
            {
                steps.Add((order, new SummaryStep(entry.Level, entry.Level, text, "respec")));
            }
        }

        foreach (var phase in phases.Where(p => p.Points > 0))
            steps.Add((phase.Order, new SummaryStep(phase.From, phase.To,
                $"Passive points into the {phase.Tree} tree: {phase.Points} point{(phase.Points == 1 ? "" : "s")}", "passive")));

        var headlines = new List<string>();
        string? baseTree = phases.FirstOrDefault()?.Tree;
        if (mastery is not null)
        {
            // The planner can have the mastery set from its first profile; the game offers it at the End of Time.
            headlines.Add(masteryLevel < 10
                ? $"Mastery to choose: {mastery} (as soon as the game offers the choice)."
                : $"Mastery to choose: {mastery} (the build takes it around level {masteryLevel}).");
            // The thing that trips people up: points in another mastery's tree are not a choice of mastery.
            var others = perTree.Where(kv => kv.Value > 0 && kv.Key != mastery && kv.Key != baseTree).ToList();
            if (others.Count > 0)
                headlines.Add("It also puts " + string.Join(" and ", others.Select(o => $"{o.Value} point{(o.Value == 1 ? "" : "s")} into the {o.Key} tree"))
                              + $" - that is on purpose and does not make you a {others[0].Key}: the first half of every mastery tree is open to all, "
                              + $"and you still pick {mastery} when the game asks.");
        }
        else if (phases.Count > 0) headlines.Add("This build's plan never names a mastery - check the guide it came from before you choose one.");
        if (specialized.Count > 0) headlines.Add("Skills it ends up with: " + string.Join(", ", specialized) + ".");
        if (steps.Count == 0) headlines.Add("Nothing to summarise: this plan has no passive, mastery or specialization lines the overlay recognises.");

        return new BuildSummary(headlines, steps.OrderBy(s => s.Step.Level).ThenBy(s => s.Order).Select(s => s.Step).ToList());
    }
}
