namespace LastEpochHelper.Core;

public enum GamePanel { None, Passives, Skills }

/// <param name="Skill">The skill whose tree is open, when one is.</param>
/// <param name="Anchor">A line that proves the panel is there; watching just that spot is cheap.</param>
public sealed record PanelReading(GamePanel Panel, string? Skill = null, ScreenLine? Anchor = null);

/// <summary>
/// Works out from the text on screen whether the game is showing its passive tree or its skill
/// trees. The game log says nothing about panels, and guessing from key presses drifts out of step
/// (a panel can be closed with the mouse), so the screen is the only reliable witness.
/// </summary>
public static class PanelDetector
{
    /// <param name="passiveTabs">Names of the class and its masteries, shown as tabs on the passive panel.</param>
    /// <param name="skills">Names of the build's skills.</param>
    /// <param name="hint">Which panel was expected; breaks a tie when both seem present.</param>
    public static PanelReading Detect(IReadOnlyList<ScreenLine> lines, IReadOnlyList<string> passiveTabs,
        IReadOnlyList<string> skills, GamePanel hint = GamePanel.None)
    {
        var text = lines.Select(l => (Line: l, Letters: Letters(l.Text))).ToList();
        ScreenLine? LineWith(string name)
        {
            string key = Letters(name);
            return key.Length < 4 ? null : text.Where(t => t.Letters.Contains(key, StringComparison.Ordinal))
                .OrderByDescending(t => t.Line.Height).Select(t => t.Line).FirstOrDefault();
        }

        // The game's own headings are the best evidence (1.5): "PASSIVES" on the passive panel,
        // "SKILLS & SPECIALIZATIONS" on the skill overview. ("N UNSPENT POINTS" is no evidence: a
        // skill tree with points left to spend says it too.)
        var passiveHeading = text.Where(t => t.Letters == "passives").Select(t => t.Line).OrderBy(l => l.Y).FirstOrDefault();
        var skillsHeading = text.Where(t => t.Letters.Contains("skillsspecializations", StringComparison.Ordinal) || t.Letters == "skillsandspecializations")
            .Select(t => t.Line).FirstOrDefault();

        // Without a heading, fall back on the tabs naming the class and its masteries - but the skill
        // overview prints those names too ("Unlocked by spending points in the Shaman passive tree").
        // "Minimum Specialized Level" is printed under the heading of every open skill tree, and nowhere else.
        bool skillTree = text.Any(t => t.Letters.Contains("specializedlevel", StringComparison.Ordinal));

        PanelReading? passives = null;
        if (passiveHeading is not null) passives = new PanelReading(GamePanel.Passives, Anchor: passiveHeading);
        else if (skillsHeading is null && !skillTree)
        {
            var tabLines = passiveTabs.Select(LineWith).Where(l => l is not null).Select(l => l!).ToList();
            if (tabLines.Count >= 2) passives = new PanelReading(GamePanel.Passives, Anchor: tabLines.OrderBy(l => l.Y).First());
        }

        // Skill panel: an open tree has the skill's name as a heading; the overview lists them all.
        PanelReading? skillPanel = null;
        // One skill name standing out in bigger letters is an open tree, with or without the overview's heading.
        var openTree = passiveHeading is null ? SkillTitleMatcher.PickLine(lines, skills) : null;
        if (openTree is { } heading) skillPanel = new PanelReading(GamePanel.Skills, heading.Skill, heading.Line);
        else if (skillsHeading is not null) skillPanel = new PanelReading(GamePanel.Skills, Anchor: skillsHeading);
        else if (passiveHeading is null)
        {
            var named = skills.Select(LineWith).Where(l => l is not null).Select(l => l!).ToList();
            if (named.Count >= 2) skillPanel = new PanelReading(GamePanel.Skills, Anchor: named.OrderBy(l => l.Y).First());
        }

        if (passives is not null && skillPanel is not null)
            return hint == GamePanel.Skills ? skillPanel : hint == GamePanel.Passives ? passives
                : skillPanel.Skill is not null ? skillPanel : passives;
        return passives ?? skillPanel ?? new PanelReading(GamePanel.None);
    }

    private static string Letters(string text) => new(text.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
}
