using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public class BlessingTests
{
    // As Windows read the offer after Fall of the Outcasts' boss (screenshot 2026-10-06 112129).
    private static readonly List<ScreenLine> Offer = new()
    {
        new("25", 17, 1, 135, 25),
        new("TIMELINE STABILIZED", 16, 228, 106, 243),
        new("CTIVATE BLESSIN", 18, 228, 176, 244),
        new("Choose a Blessing:", 18, 279, 209, 141),
        new("SELECT", 11, 328, 636, 53),
    };

    [Fact]
    public void TheOffer_IsFoundByItsHeading()
    {
        var heading = BlessingAdvice.FindOffer(Offer);

        Assert.NotNull(heading);
        Assert.Equal("TIMELINE STABILIZED", heading!.Text);
    }

    [Fact]
    public void TheHeadingAlone_OrOtherPanels_AreNotTheOffer()
    {
        Assert.Null(BlessingAdvice.FindOffer(new List<ScreenLine> { new("TIMELINE STABILIZED", 16, 228, 106, 243) }));
        Assert.Null(BlessingAdvice.FindOffer(new List<ScreenLine> { new("PASSIVES", 20, 100, 50, 120), new("Primalist", 14, 300, 52, 90) }));
        // Half of it covered or misread still counts when the other half says it.
        Assert.NotNull(BlessingAdvice.FindOffer(new List<ScreenLine> { new("Choose a Blessing:", 18, 279, 209, 141) }));
    }

    [Fact]
    public void Advice_IsTheBuildsBlessingForThisTimeline_WithItsRolls()
    {
        var endgame = EndgameData.LoadBundled();
        var build = new[] { "Grand Winds of Fortune", "Grand Resolve of Grael", "Grand Protection of Heorot" };

        var outcasts = BlessingAdvice.For(endgame, build, "Fall of the Outcasts")!;
        var winter = BlessingAdvice.For(endgame, build, "The Age of Winter")!;
        var lance = BlessingAdvice.For(endgame, build, "The Stolen Lance")!;
        var unknown = BlessingAdvice.For(endgame, build, null)!;

        var wanted = Assert.Single(outcasts.Wanted);
        Assert.Equal("Winds of Fortune", wanted.Name);
        Assert.Contains("Unique Drop Rate", wanted.Effect);
        Assert.NotEmpty(wanted.Grand);
        Assert.Equal(new[] { "Resolve of Grael", "Protection of Heorot" }, winter.Wanted.Select(w => w.Name));
        Assert.Empty(lance.Wanted);
        Assert.Equal("The Stolen Lance", lance.Timeline);
        // Nothing from the build there: Maxroll's general picks for that timeline instead, and only then.
        Assert.Equal(endgame.Timelines.First(t => t.Name == "The Stolen Lance").Blessings.Where(b => b.Recommended).Select(b => b.Name),
            lance.General.Select(g => g.Name));
        Assert.NotEmpty(lance.General);
        Assert.Empty(outcasts.General);
        Assert.Empty(unknown.General);
        Assert.Equal(3, unknown.Wanted.Count);
        Assert.Null(unknown.Timeline);
        Assert.Null(BlessingAdvice.For(endgame, Array.Empty<string>(), "Fall of the Outcasts"));
    }

    [Fact]
    public void ALevelingStageWithoutBlessings_BorrowsThoseOfTheLastStageThatHasThem()
    {
        var leveling = new TreeStage { Name = "Leveling", Level = 70 };
        var endgame = new TreeStage { Name = "Endgame", Level = 100, Blessings = { "Grand Winds of Fortune" } };
        var tree = new BuildTree { Stages = { leveling, endgame } };

        Assert.Equal(new[] { "Grand Winds of Fortune" }, BlessingAdvice.BuildBlessings(tree, leveling));
        Assert.Empty(BlessingAdvice.BuildBlessings(new BuildTree { Stages = { leveling } }, leveling));
    }
}
