namespace LastEpochHelper.Core;

public enum GamePanel { None, Passives, Skills }

/// <param name="Skill">The skill whose tree is open, when one is.</param>
/// <param name="Anchor">A line that proves the panel is there; watching just that spot is cheap.</param>
/// <param name="Tab">The passive tab (class or mastery) that is showing, when its title could be read.</param>
public sealed record PanelReading(GamePanel Panel, string? Skill = null, ScreenLine? Anchor = null, string? Tab = null);

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
    /// <param name="strict">
    /// Only believe the game's own headings, not names that merely suggest a panel. For looking on
    /// spec, when nothing says a panel was opened: a wrong guess there would pop the tree up unasked.
    /// </param>
    public static PanelReading Detect(IReadOnlyList<ScreenLine> lines, IReadOnlyList<string> passiveTabs,
        IReadOnlyList<string> skills, GamePanel hint = GamePanel.None, bool strict = false)
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
        // "Minimum Specialized Level" is printed under the heading of every open skill tree, and nowhere
        // else; failing that, its BACK and RESPEC buttons together. This holds for any skill - also one
        // the imported build does not use, whose name the overlay has no way of knowing.
        var treeMarker = text.Where(t => t.Letters.Contains("specializedlevel", StringComparison.Ordinal)).Select(t => t.Line).FirstOrDefault();
        bool specialized = treeMarker is not null;
        treeMarker ??= (text.Any(t => t.Letters == "back") ? text.Where(t => t.Letters == "respec").Select(t => t.Line).FirstOrDefault() : null);
        bool skillTree = treeMarker is not null;

        PanelReading? passives = null;
        if (passiveHeading is not null)
        {
            // The tab that is showing has its name as a title on the heading's own line, to its right
            // (the list of all tabs is further left and lower). Often read a letter off: "PRIMAL 1ST".
            var beside = lines.Where(l => l != passiveHeading && l.X > passiveHeading.X
                                          && Math.Abs(l.Y - passiveHeading.Y) < passiveHeading.Height * 1.5).ToList();
            var title = SkillTitleMatcher.PickLine(beside, passiveTabs) ?? SkillTitleMatcher.PickClosest(beside, passiveTabs);
            // Watch heading and title together, so a change of tab is seen by the quick looks too.
            var anchor = title is { } found && found.Line.X + found.Line.Width > passiveHeading.X
                ? passiveHeading with { Width = found.Line.X + found.Line.Width - passiveHeading.X }
                : passiveHeading;
            passives = new PanelReading(GamePanel.Passives, Anchor: anchor, Tab: title?.Skill);
        }
        else if (skillsHeading is null && !skillTree && !strict)
        {
            var tabLines = passiveTabs.Select(LineWith).Where(l => l is not null).Select(l => l!).ToList();
            if (tabLines.Count >= 2) passives = new PanelReading(GamePanel.Passives, Anchor: tabLines.OrderBy(l => l.Y).First());
        }

        // Skill panel: an open tree has the skill's name as a heading; the overview lists them all.
        PanelReading? skillPanel = null;
        // A skill is only named when the screen shows an open tree (its marker is there). On the
        // overview every skill's name is listed at about one size, and the reader's idea of that size
        // wobbles from look to look: taking "the biggest name" there made the tree jump between skills.
        (string Skill, ScreenLine Line)? openTree = null;
        if (passiveHeading is null && treeMarker is not null && specialized)
        {
            // The open tree's heading stands straight above "Minimum Specialized Level", left edges
            // aligned. Looking only there keeps other skill names on screen - the skill bar prints
            // them in letters nearly as big - from muddying which one it is.
            var above = lines.Where(l => l.Y < treeMarker.Y && l.Y > treeMarker.Y - treeMarker.Height * 9
                                         && Math.Abs(l.X - treeMarker.X) < treeMarker.Height * 4).ToList();
            // A heading read with a letter or two wrong ("SUMM0N TH0RN TOTEM") is still that skill.
            openTree = SkillTitleMatcher.PickLine(above, skills) ?? SkillTitleMatcher.PickClosest(above, skills);
        }
        if (openTree is null && passiveHeading is null && skillTree)
            openTree = SkillTitleMatcher.PickLine(lines, skills) ?? SkillTitleMatcher.PickClosest(lines, skills);
        // Keep an eye on heading and marker together: a quick look at the heading alone could not tell an open tree from the overview.
        if (openTree is { } open && specialized && treeMarker!.Y > open.Line.Y)
            openTree = (open.Skill, open.Line with { Height = Math.Max(open.Line.Height, (treeMarker.Y + treeMarker.Height - open.Line.Y) / 3 + 4) });
        if (openTree is { } heading) skillPanel = new PanelReading(GamePanel.Skills, heading.Skill, heading.Line);
        else if (skillTree) skillPanel = new PanelReading(GamePanel.Skills, Anchor: treeMarker);
        else if (skillsHeading is not null) skillPanel = new PanelReading(GamePanel.Skills, Anchor: skillsHeading);
        else if (passiveHeading is null && !strict)
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
