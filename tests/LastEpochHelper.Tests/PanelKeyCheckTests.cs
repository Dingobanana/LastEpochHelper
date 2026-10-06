using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public class PanelKeyCheckTests
{
    private const string Skills = "skill", Passives = "passive";

    [Fact]
    public void SMovingDown_IsIgnoredAfterAFewPresses_ThatNeverOpenedTheSkillPanel()
    {
        var check = new PanelKeyCheck();
        var ignored = Enumerable.Range(0, 4).Select(_ => check.Pressed(Skills)).ToList();
        Assert.Equal(new[] { false, false, false, true }, ignored);
    }

    [Fact]
    public void OpeningAndClosingThePanel_NeverAddsUp()
    {
        var check = new PanelKeyCheck();
        for (int i = 0; i < 10; i++)
        {
            Assert.False(check.Pressed(Skills)); // opens
            check.Seen(Skills);
            Assert.False(check.Pressed(Skills)); // closes: nothing to see afterwards
        }
    }

    [Fact]
    public void AnotherPanelKey_OrThePanelSeenInBetween_StartsTheCountAgain()
    {
        var check = new PanelKeyCheck();
        check.Pressed(Skills); check.Pressed(Skills); check.Pressed(Skills);
        check.Seen(Skills);
        Assert.False(check.Pressed(Skills));
        Assert.False(check.Pressed(Passives));
        Assert.False(check.Pressed(Skills));
        Assert.False(check.Pressed(Skills));
    }
}
