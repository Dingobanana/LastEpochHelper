using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public class TrackerTests
{
    internal static GuideRoute MakeRoute(params string[] zones) => new()
    {
        Chapters =
        {
            new GuideChapter
            {
                Id = 1, Title = "Chapter 1",
                Steps = zones.Select(z => new GuideStep
                {
                    Zone = z,
                    Tasks = { new GuideTask { Text = "go", Passive = z.StartsWith('P') ? 1 : 0 } },
                }).ToList(),
            },
        },
    };

    [Fact]
    public void KnownScene_JumpsForwardWithinLookahead()
    {
        var scenes = new SceneMap(new Dictionary<string, string> { ["Z1"] = "A", ["Z3"] = "C" });
        var tracker = new Tracker(MakeRoute("A", "B", "C", "D"), scenes);

        tracker.OnSceneLoaded("Z3");

        Assert.Equal("C", tracker.Step.Zone);
        Assert.Equal("C", tracker.CurrentSceneZone);
    }

    [Fact]
    public void KnownScene_BeyondLookahead_DoesNotJump()
    {
        var scenes = new SceneMap(new Dictionary<string, string> { ["Z4"] = "D" });
        var tracker = new Tracker(MakeRoute("A", "B", "C", "D"), scenes) { Lookahead = 2 };

        tracker.OnSceneLoaded("Z4");

        Assert.Equal(0, tracker.Index);
    }

    [Fact]
    public void KnownScene_FallsBackToNearestEarlierVisit_WhenNothingMatchesAhead()
    {
        var scenes = new SceneMap(new Dictionary<string, string> { ["Z1"] = "A", ["Z2"] = "B" });
        var tracker = new Tracker(MakeRoute("A", "B", "A", "C", "D"), scenes, index: 3) { Lookbehind = 2 };

        tracker.OnSceneLoaded("Z1");
        Assert.Equal(2, tracker.Index);

        // Out of reach behind: stay put.
        tracker.JumpTo(4);
        tracker.OnSceneLoaded("Z2");
        Assert.Equal(4, tracker.Index);
    }

    [Fact]
    public void TownTripThatJumpsAhead_IsUndoneOnReturn()
    {
        var scenes = new SceneMap(new Dictionary<string, string> { ["T"] = "Town", ["Z1"] = "A", ["Z2"] = "B" });
        var tracker = new Tracker(MakeRoute("Town", "A", "B", "Town"), scenes, index: 1);

        tracker.OnSceneLoaded("T");   // portal to town mid-zone: matches the later town visit
        Assert.Equal(3, tracker.Index);
        tracker.OnSceneLoaded("Z1");  // back through the portal
        Assert.Equal(1, tracker.Index);
    }

    [Fact]
    public void RevisitedZone_JumpsToNextOccurrence()
    {
        var scenes = new SceneMap(new Dictionary<string, string> { ["T"] = "Town", ["Z1"] = "A", ["Z2"] = "B" });
        var tracker = new Tracker(MakeRoute("Town", "A", "Town", "B"), scenes, index: 1);

        tracker.OnSceneLoaded("T");

        Assert.Equal(2, tracker.Index);
    }

    [Fact]
    public void UnknownScene_IsLearnedForCurrentStepFirst_ThenAdvances()
    {
        var scenes = new SceneMap();
        var tracker = new Tracker(MakeRoute("A", "B", "C"), scenes);

        tracker.OnSceneLoaded("Z12");
        Assert.Equal(0, tracker.Index);
        Assert.True(scenes.TryGetZone("Z12", out var zone) && zone == "A");

        tracker.OnSceneLoaded("Z13");
        Assert.Equal(1, tracker.Index);
        Assert.True(scenes.TryGetZone("Z13", out zone) && zone == "B");

        // Walking back into a learned zone follows the player and learns nothing new.
        tracker.OnSceneLoaded("Z12");
        Assert.Equal(0, tracker.Index);
        Assert.Equal(2, scenes.Learned.Count);
    }

    [Fact]
    public void PrevRightAfterAutoLearn_ForgetsTheMapping()
    {
        var scenes = new SceneMap(new Dictionary<string, string> { ["Z1"] = "A" });
        var tracker = new Tracker(MakeRoute("A", "B"), scenes);

        tracker.OnSceneLoaded("Dungeon");
        Assert.Equal(1, tracker.Index);

        tracker.Prev();

        Assert.Equal(0, tracker.Index);
        Assert.False(scenes.TryGetZone("Dungeon", out _));
    }

    [Fact]
    public void History_NeverLearns()
    {
        var scenes = new SceneMap();
        var tracker = new Tracker(MakeRoute("A", "B"), scenes);

        tracker.OnSceneLoaded("Z12", live: false);

        Assert.Empty(scenes.Learned);
        Assert.Equal("Z12", tracker.CurrentScene);
        Assert.Null(tracker.CurrentSceneZone);
    }

    [Fact]
    public void FreshTracker_CatchesUpAnyDistance_OnlyWhenAllowed()
    {
        var scenes = new SceneMap(new Dictionary<string, string> { ["Z9"] = "I" });
        var route = MakeRoute("A", "B", "C", "D", "E", "F", "G", "H", "I");

        var history = new Tracker(route, scenes);
        history.OnSceneLoaded("Z9", live: false);
        Assert.Equal(8, history.Index);

        var allowed = new Tracker(route, scenes);
        allowed.OnSceneLoaded("Z9", live: true, catchUp: true);
        Assert.Equal(8, allowed.Index);

        // Saved progress wins over a far-away scene, and plain live play never leaps.
        var saved = new Tracker(route, scenes, index: 2);
        saved.OnSceneLoaded("Z9", live: false, catchUp: true);
        Assert.Equal(2, saved.Index);
        var live = new Tracker(route, scenes);
        live.OnSceneLoaded("Z9");
        Assert.Equal(0, live.Index);
    }

    [Fact]
    public void AutoLearnOff_ReportsUnknownScene()
    {
        var scenes = new SceneMap();
        var tracker = new Tracker(MakeRoute("A", "B"), scenes) { AutoLearn = false };
        string? unknown = null;
        tracker.UnknownScene += s => unknown = s;

        tracker.OnSceneLoaded("Z12");

        Assert.Equal("Z12", unknown);
        Assert.Empty(scenes.Learned);
    }

    [Fact]
    public void SceneKey_SeparatesZonesSharingADisplayName()
    {
        var route = MakeRoute("Forest", "B", "Forest");
        route.Chapters[0].Steps[2].SceneKey = "Forest (Ch7)";
        var scenes = new SceneMap(new Dictionary<string, string> { ["Z1"] = "Forest", ["Z2"] = "B" });
        var tracker = new Tracker(route, scenes, index: 1);

        tracker.OnSceneLoaded("Z90");

        Assert.Equal(2, tracker.Index);
        Assert.True(scenes.TryGetZone("Z90", out var zone) && zone == "Forest (Ch7)");
    }

    [Fact]
    public void NextAndPrev_StayInRange()
    {
        var tracker = new Tracker(MakeRoute("A", "B"), new SceneMap());
        tracker.Prev();
        Assert.Equal(0, tracker.Index);
        tracker.Next();
        tracker.Next();
        Assert.Equal(1, tracker.Index);
    }
}
