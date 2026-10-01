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

        // Passive panel: its tabs name the class and the masteries side by side. One name alone can
        // be anywhere (the character sheet, chat), so ask for two - or the word "passive" with one.
        var tabLines = passiveTabs.Select(LineWith).Where(l => l is not null).Select(l => l!).ToList();
        bool saysPassive = text.Any(t => t.Letters.Contains("passive", StringComparison.Ordinal));
        PanelReading? passives = tabLines.Count >= 2 || (tabLines.Count == 1 && saysPassive)
            ? new PanelReading(GamePanel.Passives, Anchor: tabLines.OrderBy(l => l.Y).First())
            : null;

        // Skill panel: an open tree has the skill's name as a heading; the overview lists several.
        PanelReading? skillPanel = null;
        if (SkillTitleMatcher.PickLine(lines, skills) is { } heading)
            skillPanel = new PanelReading(GamePanel.Skills, heading.Skill, heading.Line);
        else
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
