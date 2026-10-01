using System.IO;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public class LogTests
{
    // Lines copied from a real 1.5 Player.log.
    internal const string ZoneLoad = "2026-10-01T17:05:49.1234567+00:00\tLog\tScene Z12 load started: load mode: Single";
    private const string UiLoad = "2026-10-01T16:59:06.1176097+00:00\tLog\tScene PersistentUI load started: load mode: Additive";
    internal const string TreeData = "2026-10-01T17:05:51.0000000+00:00\tLog\t<color=#3399FF>Received Local Tree Data from server | characterLevel | 7 | classID | 0 | chosenMastery | 0</color>";
    private const string TreeDataEcho = "<color=#3399FF>Received Local Tree Data from server | characterLevel | 7 | classID | 0 | chosenMastery | 0</color>";
    private const string CharSelect = "2026-10-01T17:00:47.6465801+00:00\tLog\tClientStateManager: Application state changed to CharacterSelect.";
    internal const string Created = "2026-10-01T17:05:37.0000000+00:00\tLog\tCharacter Created: Testchar 0123456789ABCDEF isOnline: True, cycle: Swordfish, class: 0";
    private const string Trigger = "2026-10-01T17:52:03.0000000+00:00\tWarning\tStateTransitionInteraction shouldn't be called from client, update the condition handler in Storerooms Sidequest Turn In Speak with Heoborean Soldier";
    private const string Died = "2026-10-01T18:10:00.0000000+00:00\tLog\tPlayer died: -IsLocalPlayer: True -LocalPlayer: Local Player(Clone) (LocalPlayer)";

    [Fact]
    public void Parses_ZoneLoad() => Assert.Equal(new SceneLoadEvent("Z12"), LogParser.Parse(ZoneLoad));

    [Fact]
    public void Ignores_AdditiveLoads() => Assert.Null(LogParser.Parse(UiLoad));

    [Fact]
    public void Parses_CharacterLevel_WithClassAndMastery() =>
        Assert.Equal(new CharacterLevelEvent(7, 0, 0), LogParser.Parse(TreeData));

    [Fact]
    public void Ignores_EchoedCopyWithoutTimestamp() => Assert.Null(LogParser.Parse(TreeDataEcho));

    [Fact]
    public void Parses_CharacterSelect() => Assert.IsType<CharacterSelectEvent>(LogParser.Parse(CharSelect));

    [Fact]
    public void Parses_CharacterCreated() =>
        Assert.Equal(new CharacterCreatedEvent("Testchar", 0), LogParser.Parse(Created));

    [Fact]
    public void Parses_QuestTrigger() =>
        Assert.Equal(new QuestTriggerEvent("Storerooms Sidequest Turn In Speak with Heoborean Soldier"), LogParser.Parse(Trigger));

    [Fact]
    public void Parses_AccountName() => Assert.Equal(new AccountEvent("SomeAccount"),
        LogParser.Parse("2026-10-01T17:00:01.0000000+00:00\tLog\tConnected to chat as 'SomeAccount' (ID: '0123456789ABCDEF')"));

    [Fact]
    public void Parses_PlayerDeath() => Assert.IsType<PlayerDiedEvent>(LogParser.Parse(Died));

    [Fact]
    public void Ignores_StackTraceLines() => Assert.Null(LogParser.Parse("UnityEngine.Debug:Log(Object)"));

    [Fact]
    public async Task Watcher_SummarisesHistoryThenReportsLiveLines_AndSurvivesTruncation()
    {
        string path = Path.Combine(Path.GetTempPath(), $"leh-test-{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(path, string.Join("\n", CharSelect, Created, ZoneLoad, TreeData, ZoneLoad.Replace("Z12", "Z13"), ""));
            var events = new List<(LogEvent, bool)>();
            using var watcher = new LogWatcher(path, TimeSpan.FromMilliseconds(20));
            watcher.Event += (e, live) => { lock (events) events.Add((e, live)); };
            watcher.Start();

            // History collapses to: character select, who was created, where they are, what level.
            await WaitFor(() => events.Count == 4);
            Assert.IsType<CharacterSelectEvent>(events[0].Item1);
            Assert.Equal((new CharacterCreatedEvent("Testchar", 0), false), events[1]);
            Assert.Equal((new SceneLoadEvent("Z13"), false), events[2]);
            Assert.Equal((new CharacterLevelEvent(7, 0, 0), false), events[3]);

            // A line arriving in two writes must be delivered once, whole.
            string line = ZoneLoad.Replace("Z12", "Z14");
            File.AppendAllText(path, line[..40]);
            await Task.Delay(80);
            File.AppendAllText(path, line[40..] + "\r\n");
            await WaitFor(() => events.Count == 5);
            Assert.Equal((new SceneLoadEvent("Z14"), true), events[4]);

            // Game restart: the log is truncated and written from the top.
            File.WriteAllText(path, ZoneLoad.Replace("Z12", "Z15") + "\n");
            await WaitFor(() => events.Count == 6);
            Assert.Equal((new SceneLoadEvent("Z15"), true), events[5]);
        }
        finally { File.Delete(path); }

        static async Task WaitFor(Func<bool> condition)
        {
            for (int i = 0; i < 150 && !condition(); i++) await Task.Delay(20);
            Assert.True(condition(), "Timed out waiting for log events.");
        }
    }

    [Theory]
    [InlineData("Ctrl+Shift+Right", true)]
    [InlineData("alt + F9", true)]
    [InlineData("Ctrl+Shift", false)]
    [InlineData("Ctrl+NotAKey", false)]
    public void Hotkey_Parsing(string combo, bool valid) =>
        Assert.Equal(valid, HotkeyManager.TryParse(combo, out _, out _));
}

public class ShippedDataTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "Data");

    internal static (Guide Guide, SceneMap Scenes) Load()
    {
        var guide = Guide.Load(Path.Combine(DataDir, "guide.json"));
        var scenes = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(DataDir, "scenes.json")), Guide.JsonOptions)!;
        return (guide, new SceneMap(scenes));
    }

    [Fact]
    public void EveryRoute_HasScenesAndTasksForEveryStep_AndStaysWithinTheCaps()
    {
        var (guide, scenes) = Load();

        Assert.Equal("full", guide.Routes[0].Id);
        Assert.True(guide.Routes.Count >= 3);
        Assert.All(guide.Routes, route =>
        {
            Assert.All(route.Flat, f =>
            {
                Assert.NotEmpty(f.Step.Tasks);
                // Endgame chapters are stepped through by hand and need no scene.
                if (f.Chapter.Id <= 10) Assert.True(scenes.HasSceneFor(f.Step.Key), $"No scene id for '{f.Step.Key}'");
            });
            Assert.Equal(guide.PassiveCap, route.Flat.Sum(f => f.Step.Tasks.Sum(t => t.Passive)));
            Assert.Equal(guide.IdolCap, route.Flat.Sum(f => f.Step.Tasks.Sum(t => t.Idol)));
        });
    }

    [Fact]
    public void FirstZonesFromARealLog_AdvanceTheGuide()
    {
        var (guide, scenes) = Load();
        var tracker = new Tracker(guide.Route("full"), scenes);

        tracker.OnSceneLoaded("Z12");
        Assert.Equal("The Old Road", tracker.Step.Zone);
        tracker.OnSceneLoaded("Z22");
        Assert.Equal("The Burning Forest", tracker.Step.Zone);
        Assert.Equal(1, tracker.Index);
    }

    [Fact]
    public void SkipRoute_FollowsTheDungeonIntoALaterChapter()
    {
        var (guide, scenes) = Load();
        var route = guide.Route("sanctum");
        int archive = route.Flat.ToList().FindIndex(f => f.Step.Zone == "The Sanctum Archive");
        var tracker = new Tracker(route, scenes, archive);

        tracker.OnSceneLoaded("H40");

        Assert.Equal("The Radiant Dunes", tracker.Step.Zone);
        Assert.Equal(9, tracker.Chapter.Id);
    }
}
