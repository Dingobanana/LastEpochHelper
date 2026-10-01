using System.IO;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public sealed class SessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"leh-session-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static Guide MakeGuide()
    {
        GuideStep Step(string zone, params GuideTask[] tasks) => new() { Zone = zone, Tasks = tasks.ToList() };
        var full = new GuideRoute
        {
            Id = "full", Name = "Full",
            Chapters =
            {
                new GuideChapter
                {
                    Id = 1, Title = "Chapter 1", Steps =
                    {
                        Step("A", new GuideTask { Text = "Walk" }),
                        Step("B",
                            new GuideTask { Type = "side", Text = "Accept: Storeroom Saboteurs", Quest = "Storeroom Saboteurs" },
                            new GuideTask { Type = "side", Text = "Storeroom Saboteurs: Return to Heoborean Soldier", Quest = "Storeroom Saboteurs", Passive = 1 }),
                        Step("C", new GuideTask { Text = "Enter the Vault (completes The Keepers)", Passive = 1 }),
                    },
                },
                new GuideChapter { Id = 2, Title = "Chapter 2", Steps = { Step("D", new GuideTask { Text = "Go" }), Step("E", new GuideTask { Text = "Go" }) } },
            },
        };
        var skip = new GuideRoute
        {
            Id = "skip", Name = "Skip",
            Chapters = { new GuideChapter { Id = 1, Title = "Chapter 1", Steps = { Step("A", new GuideTask { Text = "Walk" }), Step("E", new GuideTask { Text = "Go" }) } } },
        };
        return new Guide { PassiveCap = 15, IdolCap = 8, Routes = { full, skip } };
    }

    private Session MakeSession() => new(new Storage(_dir), MakeGuide(), new SceneMap(new Dictionary<string, string>
    {
        ["ZA"] = "A", ["ZB"] = "B", ["ZC"] = "C", ["ZD"] = "D", ["ZE"] = "E",
    }));

    private static void EnterWorld(Session session, string scene, int level, int classId, int mastery = 0, string? created = null, bool live = true)
    {
        session.Handle(new CharacterSelectEvent(), live);
        if (created is not null) session.Handle(new CharacterCreatedEvent(created, classId), live);
        session.Handle(new SceneLoadEvent(scene), live);
        session.Handle(new CharacterLevelEvent(level, classId, mastery), live);
    }

    [Fact]
    public void FirstCharacter_AdoptsTheDefaultProfile_AndTakesItsName()
    {
        var session = MakeSession();

        EnterWorld(session, "ZA", level: 1, classId: 0, created: "Hero");

        Assert.Single(session.Store.Profiles);
        Assert.Equal("Hero", session.Profile.Name);
        Assert.Equal(0, session.Profile.ClassId);
        Assert.Equal("A", session.Tracker.Step.Zone);
    }

    [Fact]
    public void SecondCharacter_GetsItsOwnProfile_AndSwitchingBackRestoresProgress()
    {
        var session = MakeSession();
        EnterWorld(session, "ZA", 1, classId: 0, created: "Hero");
        session.Handle(new SceneLoadEvent("ZB"), true);
        session.Handle(new SceneLoadEvent("ZC"), true);
        session.Handle(new CharacterLevelEvent(9, 0, 0), true);

        EnterWorld(session, "ZA", 1, classId: 1, created: "Alt");
        Assert.Equal("Alt", session.Profile.Name);
        Assert.Equal(0, session.Tracker.Index);

        // No name in the log this time: class and level identify the first character.
        EnterWorld(session, "ZC", 9, classId: 0);
        Assert.Equal("Hero", session.Profile.Name);
        Assert.Equal("C", session.Tracker.Step.Zone);
        Assert.Equal(2, session.Store.Profiles.Count);
    }

    [Fact]
    public void SameClassCharacters_AreToldApartByLevel()
    {
        var session = MakeSession();
        EnterWorld(session, "ZA", 1, classId: 0, created: "High");
        session.Handle(new CharacterLevelEvent(30, 0, 2), true);
        EnterWorld(session, "ZA", 1, classId: 0, created: "Low");
        session.Handle(new CharacterLevelEvent(5, 0, 0), true);

        EnterWorld(session, "ZA", 31, classId: 0, mastery: 2);
        Assert.Equal("High", session.Profile.Name);

        EnterWorld(session, "ZA", 6, classId: 0);
        Assert.Equal("Low", session.Profile.Name);
    }

    [Fact]
    public void UnknownExistingCharacter_GetsANewProfile_ThatCatchesUpToItsZone()
    {
        var session = MakeSession();
        EnterWorld(session, "ZA", 1, classId: 0, created: "Hero");

        EnterWorld(session, "ZE", 40, classId: 3, mastery: 1);

        Assert.Equal("Necromancer", session.Profile.Name);
        Assert.Equal("E", session.Tracker.Step.Zone);
    }

    [Fact]
    public void Rewards_MainCountsWhenPassed_SideIsPendingUntilTickedOrSkipped()
    {
        var session = MakeSession();
        EnterWorld(session, "ZA", 1, 0, created: "Hero");
        session.Tracker.JumpTo(3);

        var (passive, _, pending) = session.Rewards();
        Assert.Equal(1, passive); // the main quest reward in C
        Assert.Equal("Storeroom Saboteurs", Assert.Single(pending).Task.Quest);

        session.ToggleDone(pending[0].Key);
        Assert.Equal(2, session.Rewards().Passive);
        Assert.Empty(session.Rewards().Pending);

        session.SkipReward(pending[0].Key);
        Assert.Equal(1, session.Rewards().Passive);
        Assert.Empty(session.Rewards().Pending);
    }

    [Fact]
    public void QuestTrigger_TicksTheMatchingLine_AndOnlyAHandInTicksAReward()
    {
        var session = MakeSession();
        EnterWorld(session, "ZA", 1, 0, created: "Hero");
        session.Handle(new SceneLoadEvent("ZB"), true);

        // Replayed history must not tick anything.
        session.Handle(new QuestTriggerEvent("Storerooms Sidequest Turn In Speak with Heoborean Soldier"), false);
        Assert.Empty(session.Profile.Done);

        session.Handle(new QuestTriggerEvent("Storerooms Sidequest Start Speak with Heoborean Soldier"), true);
        Assert.True(session.IsDone(Session.Key(1, 0)));
        Assert.False(session.IsDone(Session.Key(1, 1)));

        session.Handle(new QuestTriggerEvent("Storerooms Sidequest Turn In Speak with Heoborean Soldier"), true);
        Assert.True(session.IsDone(Session.Key(1, 1)));
    }

    [Fact]
    public void Timer_RunsOnlyInGame_AndRecordsChapterSplits()
    {
        var session = MakeSession();
        session.Tick(10);
        Assert.Equal(0, session.Profile.PlaySeconds);

        EnterWorld(session, "ZA", 1, 0, created: "Hero");
        session.Tick(60);
        session.Handle(new SceneLoadEvent("ZB"), true);
        session.Handle(new SceneLoadEvent("ZC"), true);
        session.Tick(40);
        session.Handle(new SceneLoadEvent("ZD"), true);
        session.Tick(5);

        Assert.Equal(105, session.Profile.PlaySeconds);
        Assert.Equal(100, session.Profile.Splits[3]);
        Assert.Equal(5, session.ChapterTimes().Current);

        // A second character sees the first one's chapter 1 as the time to beat.
        EnterWorld(session, "ZA", 1, classId: 1, created: "Alt");
        Assert.Equal(100, session.ChapterTimes().Best);

        session.Handle(new CharacterSelectEvent(), true);
        session.Tick(30);
        Assert.Equal(0, session.Profile.PlaySeconds);
    }

    [Fact]
    public void Death_GoesInTheJournal_WithZoneAndLevel_AndRaisesAnAlert()
    {
        var session = MakeSession();
        EnterWorld(session, "ZA", 12, 0, created: "Hero");
        session.Handle(new SceneLoadEvent("ZB"), true);

        session.Handle(new PlayerDiedEvent(), true);

        var death = Assert.Single(session.Profile.DeathLog);
        Assert.Equal(("B", 12), (death.Zone, death.Level));
        Assert.Contains("Died in B", session.Alert);

        session.SetDeathCause(death, "One-shot");
        Assert.Equal("One-shot", MakeSession().Profile.DeathLog[0].Cause);
    }

    [Fact]
    public void AccountName_IsPickedUpFromTheLog()
    {
        var session = MakeSession();

        session.Handle(new AccountEvent("SomeAccount"), live: false);

        Assert.Equal("SomeAccount", MakeSession().Settings.AccountName);
    }

    [Fact]
    public void Death_IsCounted_AndStateSurvivesARestart()
    {
        var session = MakeSession();
        EnterWorld(session, "ZA", 1, 0, created: "Hero");
        session.Handle(new SceneLoadEvent("ZB"), true);
        session.Handle(new PlayerDiedEvent(), true);
        session.ToggleDone(Session.Key(1, 0));

        var restarted = MakeSession();

        Assert.Equal("Hero", restarted.Profile.Name);
        Assert.Equal(1, restarted.Profile.Deaths);
        Assert.Equal(1, restarted.Tracker.Index);
        Assert.True(restarted.IsDone(Session.Key(1, 0)));
    }

    [Fact]
    public void ChangingRoute_RestartsProgress_AndResyncsToTheCurrentZone()
    {
        var session = MakeSession();
        EnterWorld(session, "ZA", 1, 0, created: "Hero");
        session.Handle(new SceneLoadEvent("ZB"), true);
        session.ToggleDone(Session.Key(1, 0));
        session.Tracker.JumpTo(4);
        session.Handle(new SceneLoadEvent("ZE"), true);

        session.SetRoute("skip");

        Assert.Equal("skip", session.Profile.RouteId);
        Assert.Empty(session.Profile.Done);
        Assert.Equal("E", session.Tracker.Step.Zone);
    }

    [Fact]
    public void TickPlanThrough_ClearsEverythingUpToThatLevel()
    {
        var session = MakeSession();
        session.TickPlanThrough(8);

        var (due, next) = BuildPlan.View(null, level: 25, session.Profile.PlanDone);

        Assert.Equal(new[] { 10, 20, 25 }, due.Select(e => e.Level)); // the loot filter, skill slot and resistance reminders
        Assert.Equal(35, next!.Level);
    }

    private static EndgameData MakeEndgame() => new()
    {
        Timelines =
        {
            new Timeline { Name = "Outcasts", Knowledge = 1, Blessings = { new Blessing { Name = "Winds of Fortune" } } },
            new Timeline { Name = "Dragons", Knowledge = 2 },
            new Timeline { Name = "Winter", Knowledge = 0 },
        },
    };

    [Fact]
    public void Knowledge_CountsTickedTimelines_AndIsKeptPerCharacter()
    {
        var session = new Session(new Storage(_dir), MakeGuide(), new SceneMap(), MakeEndgame(), Path.Combine(_dir, "Filters"));
        Assert.Equal(0, session.Knowledge());

        session.UpdateTimeline("Outcasts", p => p.Normal = true);
        session.UpdateTimeline("Dragons", p => { p.Normal = true; p.Blessing = "X"; p.Corruption = 40; });
        session.UpdateTimeline("Winter", p => p.Empowered = true);

        Assert.Equal(3, session.Knowledge());
        Assert.Equal(40, session.TimelineProgress("Dragons").Corruption);
        Assert.Equal("Outcasts", session.Endgame.TimelineOf("Grand Winds of Fortune")!.Name);

        var restarted = new Session(new Storage(_dir), MakeGuide(), new SceneMap(), MakeEndgame(), Path.Combine(_dir, "Filters"));
        Assert.Equal(3, restarted.Knowledge());
        restarted.Activate(restarted.CreateProfile("Alt"));
        Assert.Equal(0, restarted.Knowledge());
    }

    [Fact]
    public void LootFilters_AreListedFromTheGameFolder_AndBecomeBuildLinesAtTheirLevel()
    {
        string filters = Path.Combine(_dir, "Filters");
        Directory.CreateDirectory(filters);
        File.WriteAllText(Path.Combine(filters, "Leveling.xml"), "<ItemFilter/>");
        File.WriteAllText(Path.Combine(filters, "Strict.xml"), "<ItemFilter/>");
        File.WriteAllText(Path.Combine(filters, "notes.txt"), "");
        var session = new Session(new Storage(_dir), MakeGuide(), new SceneMap(), MakeEndgame(), filters);

        Assert.Equal(new[] { "Leveling", "Strict" }, session.InstalledFilters());

        session.SetFilterLevel("Strict", 50);
        session.SetFilterLevel("Leveling", 5);
        session.SetFilterLevel("Leveling", 0); // 0 = not used

        var (due, _) = BuildPlan.View(null, level: 50, new HashSet<string>(), maxDue: 10, extra: session.FilterEntries());
        Assert.Contains(due, e => e.Level == 50 && e.Text.Contains("\"Strict\""));
        Assert.DoesNotContain(due, e => e.Text.Contains("\"Leveling\""));
    }

    [Fact]
    public void DungeonKeysAndGearTicks_AreRemembered()
    {
        var session = MakeSession();
        session.UpdateDungeon("Temporal Sanctum", p => { p.Keys = 2; p.FirstClear = true; });
        session.ToggleGearDone("Early|Helmet");

        var restarted = MakeSession();

        Assert.Equal(2, restarted.DungeonProgress("Temporal Sanctum").Keys);
        Assert.True(restarted.DungeonProgress("Temporal Sanctum").FirstClear);
        Assert.Contains("Early|Helmet", restarted.Profile.GearDone);
        Assert.Equal(0, restarted.DungeonProgress("Lightless Arbor").Keys);
    }

    [Fact]
    public void FreshSettings_SurviveASaveAndLoad()
    {
        // Regression: a default that JSON cannot store (NaN) once made every settings save fail silently.
        var storage = new Storage(_dir);
        var settings = new Settings { LastRunVersion = "9.9.9", Left = 123 };

        storage.Save(Session.SettingsFile, settings);
        var loaded = storage.Load<Settings>(Session.SettingsFile);

        Assert.Equal("9.9.9", loaded.LastRunVersion);
        Assert.Equal(123, loaded.Left);
        Assert.Null(loaded.TreeLeft);
        Assert.Null(loaded.PlannerLeft);
    }

    [Fact]
    public void SettingsFileWithNaN_FromAnOlderVersion_StillLoads()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, Session.SettingsFile), "{ \"left\": 77, \"treeLeft\": \"NaN\", \"treeTop\": \"NaN\" }");

        var loaded = new Storage(_dir).Load<Settings>(Session.SettingsFile);

        Assert.Equal(77, loaded.Left);
    }

    [Fact]
    public void LegacyStateFile_IsMigratedIntoTheFirstProfile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "state.json"), "{ \"index\": 2, \"done\": [\"1:0\"] }");

        var session = MakeSession();

        Assert.Equal(2, session.Tracker.Index);
        Assert.True(session.IsDone("1:0"));
    }
}

public class BuildPlanTests
{
    [Fact]
    public void Parses_Levels_Ranges_NameAndComments()
    {
        var plan = BuildPlan.Parse("name: Tornado Shaman\n# comment\n10-14: Passives: Natural Attunement 5/8\n5: Specialize Gathering Storm\nnot an entry\nlvl 20) Take Tornado\n");

        Assert.Equal("Tornado Shaman", plan.Name);
        Assert.Equal(new[] { 5, 10, 20 }, plan.Entries.Select(e => e.Level));
        Assert.Equal("Lvl 10-14: Passives: Natural Attunement 5/8", plan.Entries[1].Text);
        Assert.Equal("Take Tornado", plan.Entries[2].Text);
    }

    [Fact]
    public void View_ShowsDueUntickedEntries_MergedWithMilestones_AndWhatComesNext()
    {
        var plan = BuildPlan.Parse("2: First point\n6: Second thing\n12: Later");
        var done = new HashSet<string> { plan.Entries[0].Key };

        var (due, next) = BuildPlan.View(plan, level: 7, done);

        Assert.Equal(new[] { "1st skill specialization slot unlocked - specialize your main skill", "Second thing" }, due.Select(e => e.Text));
        Assert.Equal(8, next!.Level);
    }

    [Fact]
    public void View_WithoutAPlan_StillShowsMilestones()
    {
        var (due, next) = BuildPlan.View(null, level: 3, new HashSet<string>());

        Assert.Empty(due);
        Assert.Equal(4, next!.Level);
    }
}

public class MaxrollImporterTests
{
    // A cut-down planner and game-data table in the shapes Maxroll serves.
    private const string Game = """
    {
      "classes": [ {
        "className": "Sentinel", "treeID": "kn",
        "knownAbilities": ["a_lunge"],
        "unlockableAbilities": [ { "ability": "a_rive", "level": 5 } ],
        "masteries": [
          { "name": "Sentinel", "abilities": [ { "ability": "a_multi", "level": 3 } ] },
          { "name": "Void Knight", "abilities": [] },
          { "name": "Forge Guard", "abilities": [] },
          { "name": "Paladin", "abilities": [], "masteryAbility": "a_hands" }
        ]
      } ],
      "itemTypes": [
        { "baseTypeID": 0, "displayName": "Helmet", "subItems": [ { "subTypeID": 3, "name": "Iron Casque", "displayName": "" } ] },
        { "baseTypeID": 34, "displayName": "Blessing", "subItems": [ { "subTypeID": 37, "name": "Grand Hunger of the Void", "displayName": "" } ] }
      ],
      "affixes": [ { "affixId": 25, "affixName": "Added Health", "affixDisplayName": "Health" }, { "affixId": 7, "affixName": "Void Resistance", "affixDisplayName": "" } ],
      "uniques": [ { "uniqueID": 9, "name": "Calamity", "displayName": "", "isSetItem": false } ],
      "abilities": {
        "a_lunge": { "abilityName": "Lunge", "playerAbilityID": "lu" },
        "a_rive": { "abilityName": "Rive", "playerAbilityID": "rv" },
        "a_multi": { "abilityName": "Multistrike", "playerAbilityID": "ms" },
        "a_hands": { "abilityName": "Healing Hands", "playerAbilityID": "hh" }
      },
      "skillTrees": {
        "kn": { "nodes": {
          "1": { "nodeName": "Juggernaut", "maxPoints": 8, "mastery": 0 },
          "2": { "nodeName": "Armour Clad", "maxPoints": 5, "mastery": 0 },
          "9": { "nodeName": "Defiance", "maxPoints": 8, "mastery": 3 } } },
        "rv": { "nodes": { "4": { "nodeName": "Champion", "maxPoints": 4 }, "5": { "nodeName": "Flurry", "maxPoints": 4 } } }
      }
    }
    """;

    private const string Planner = """
    {
      "items": {
        "11": { "itemType": 0, "subType": 3, "affixes": [ { "id": 25, "tier": 3, "roll": 1 }, { "id": 7, "tier": 2, "roll": 1 } ] },
        "12": { "itemType": 0, "subType": 3, "uniqueID": 9, "affixes": [ { "id": 25, "tier": 5, "roll": 1 } ] }
      },
      "profiles": [
        { "name": "Early", "level": 10, "class": 0, "mastery": 0, "items": { "head": 11 },
          "passives": { "history": [1, 1, {"2": 2}, 1], "position": 3 },
          "specializedSkills": ["a_rive"], "activeSkills": ["a_rive", "a_multi"],
          "skillTrees": { "rv": { "history": [4, 4], "position": 2 } } },
        { "name": "Mid", "level": 20, "class": 0, "mastery": 3,
          "items": { "head": 12 }, "idols": [null, 11, 11], "blessings": [null, { "itemType": 34, "subType": 37 }],
          "passives": { "history": [1, 1, {"2": 2}, 9, 9], "position": 5 },
          "specializedSkills": ["a_rive"], "activeSkills": ["a_rive"],
          "skillTrees": { "rv": { "history": [4, 4, 5], "position": 3 } } }
      ]
    }
    """;

    [Fact]
    public void Convert_KeepsPointOrder_MergesPerLevel_AndProducesAParsablePlan()
    {
        var result = MaxrollImporter.Convert("abc123", "Test build",
            System.Text.Json.Nodes.JsonNode.Parse(Planner)!, System.Text.Json.Nodes.JsonNode.Parse(Game)!);
        var lines = result.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("name: Test build", lines[0]);
        // "position" cuts the history: the trailing Juggernaut point of the first profile is not taken.
        Assert.DoesNotContain(lines, l => l.Contains("Juggernaut (3/8)"));
        // Order within the tree is the click order.
        int juggernaut = Array.FindIndex(lines, l => l.Contains("Juggernaut (1/8)") || l.Contains("Juggernaut (2/8)"));
        int armour = Array.FindIndex(lines, l => l.Contains("Armour Clad"));
        Assert.True(juggernaut >= 0 && juggernaut < armour);
        Assert.Contains(lines, l => l.Contains("Multistrike unlocks (3 points in the Sentinel tree)"));
        Assert.Contains(lines, l => l.StartsWith("5: Rive unlocks"));
        Assert.Contains(lines, l => l.Contains("Specialize Rive"));
        Assert.Contains(lines, l => l.Contains("Choose mastery: Paladin"));
        Assert.Contains(lines, l => l.Contains("Passives [Paladin]: Defiance (2/8)"));
        Assert.Contains(lines, l => l.Contains("Rive: Flurry (1/4)"));

        // The structured half: one tab per passive tree with points, one per specialized skill.
        var tree = result.Tree;
        Assert.Equal(new[] { "Sentinel", "Paladin", "Rive" }, tree.Trees.Select(t => t.Name));
        Assert.Equal(new[] { 4, 6 }, tree.Stages.Select(s => s.Passives.Count)); // a {node: 2} batch is two points
        var juggernautNode = tree.Trees[0].Nodes.Single(n => n.Name == "Juggernaut");
        Assert.Equal(8, juggernautNode.Max);

        // Gear, idols and blessings come out with readable names.
        var helmet = Assert.Single(tree.Stages[0].Gear);
        Assert.Equal(("Helmet", "Iron Casque", ""), (helmet.Slot, helmet.Name, helmet.Rarity));
        Assert.Equal(new[] { "Health T3", "Void Resistance T2" }, helmet.Affixes);
        Assert.Equal(("Calamity", "unique"), (tree.Stages[1].Gear[0].Name, tree.Stages[1].Gear[0].Rarity));
        Assert.Equal(2, Assert.Single(tree.Stages[1].Idols).Count);
        Assert.Equal(new[] { "Grand Hunger of the Void" }, tree.Stages[1].Blessings);
        Assert.Equal(("Health", 1), tree.Stages[0].WantedAffixes()[0]);
        Assert.Same(tree.Stages[1], tree.StageFor(15));

        var plan = BuildPlan.Parse(result.Text);
        Assert.Equal("Test build", plan.Name);
        Assert.Equal(lines.Length - 3, plan.Entries.Count); // everything but the name and the two comment lines
    }
}

public class BuildTreeTests
{
    private static BuildTree Make() => new()
    {
        Trees =
        {
            new TreeDef { Name = "Base", Kind = TreeDef.PassiveKind, Nodes = { new TreeNode { Id = 1, Name = "A", Max = 8 }, new TreeNode { Id = 2, Name = "B", Max = 5 } } },
            new TreeDef { Name = "Mastery", Kind = TreeDef.PassiveKind, Nodes = { new TreeNode { Id = 9, Name = "M", Max = 8 } } },
            new TreeDef { Name = "Rive", Kind = TreeDef.SkillKind, Nodes = { new TreeNode { Id = 4, Name = "Champion", Max = 4 }, new TreeNode { Id = 5, Name = "Flurry", Max = 4 } } },
        },
        Stages =
        {
            new TreeStage { Name = "Early", Level = 10, Passives = { 1, 1, 2 }, Skills = { ["Rive"] = new() { 4, 4 } } },
            new TreeStage { Name = "Late", Level = 30, Passives = { 1, 1, 2, 9, 9, 1 }, Skills = { ["Rive"] = new() { 4, 4, 5 } } },
        },
    };

    [Fact]
    public void PassivePoints_StartAtLevelThree_AndIncludeQuestRewards()
    {
        Assert.Equal(0, BuildTree.PassivePoints(level: 2, questPassives: 0));
        Assert.Equal(1, BuildTree.PassivePoints(3, 0));
        Assert.Equal(113, BuildTree.PassivePoints(100, 15));
    }

    [Fact]
    public void PassiveState_ShowsWhatIsTaken_WhatIsNext_AndTheStageTarget()
    {
        var build = Make();

        var state = build.State(build.Trees[0], points: 2, level: 4);

        Assert.Equal("Early", state.Stage);
        Assert.Equal(2, state.Allocated[1]);
        Assert.False(state.Allocated.ContainsKey(2));
        Assert.Equal(new[] { new NextRun(2, 1) }, state.Next);
        Assert.Equal(1, state.Target[2]);
    }

    [Fact]
    public void PassiveState_MovesToTheNextStage_WhenTheFirstIsUsedUp_AndNextCanPointAtAnotherTab()
    {
        var build = Make();

        var state = build.State(build.Trees[0], points: 3, level: 12);

        Assert.Equal("Late", state.Stage);
        Assert.Equal(new[] { new NextRun(9, 2), new NextRun(1, 1) }, state.Next); // two points in a row into the mastery tab's node
        Assert.Equal(3, state.Target[1]);
        Assert.Equal(2, build.State(build.Trees[1], 5, 14).Allocated[9]);
        Assert.Equal("M [Mastery]", build.NodeName(9, passive: true));
    }

    [Fact]
    public void SkillState_UsesItsOwnCounter_AndTheLevelBracket()
    {
        var build = Make();
        var rive = build.Trees[2];

        var early = build.State(rive, points: 1, level: 8);
        Assert.Equal("Early", early.Stage);
        Assert.Equal(new[] { new NextRun(4, 1) }, early.Next);

        // The early order is used up, so the later stage takes over even at a low level.
        var past = build.State(rive, points: 2, level: 8);
        Assert.Equal("Late", past.Stage);
        Assert.Equal(new[] { new NextRun(5, 1) }, past.Next);

        var done = build.State(rive, points: 9, level: 40);
        Assert.Empty(done.Next);
        Assert.Equal(3, done.Points);
    }
}

public class SkillTitleMatcherTests
{
    private static readonly string[] Skills = { "Tornado", "Gathering Storm", "Summon Spriggan", "Warcry" };

    [Fact]
    public void Picks_TheSkillNamedInTheBiggestLetters()
    {
        var lines = new[]
        {
            new ScreenLine("Gathering Storm", 14), new ScreenLine("Warcry", 14), // skill bar tooltips / list
            new ScreenLine("GATHERING  STORM", 34),                              // the open tree's heading
            new ScreenLine("Level 5", 20),
        };

        Assert.Equal("Gathering Storm", SkillTitleMatcher.Pick(lines, Skills));
    }

    [Fact]
    public void ReturnsNull_WhenNoSkillNameIsOnScreen_OrSeveralAreEquallyProminent()
    {
        Assert.Null(SkillTitleMatcher.Pick(new[] { new ScreenLine("Inventory", 30) }, Skills));
        Assert.Null(SkillTitleMatcher.Pick(new[] { new ScreenLine("Tornado", 20), new ScreenLine("Warcry", 20) }, Skills));
    }

    [Fact]
    public void PickLine_ReturnsWhereTheHeadingWas()
    {
        var lines = new[] { new ScreenLine("Warcry", 14, 100, 900, 60), new ScreenLine("GATHERING STORM", 34, 2200, 150, 400) };

        var found = SkillTitleMatcher.PickLine(lines, Skills)!.Value;

        Assert.Equal("Gathering Storm", found.Skill);
        Assert.Equal((2200, 150), (found.Line.X, found.Line.Y));
        Assert.Null(SkillTitleMatcher.PickLine(new[] { new ScreenLine("Inventory", 30) }, Skills));
    }

    [Fact]
    public void ToleratesOcrNoise_InSpacingCaseAndPunctuation()
    {
        Assert.Equal("Summon Spriggan", SkillTitleMatcher.Pick(new[] { new ScreenLine("summon-Spriggan: Lvl 3", 30) }, Skills));
    }
}

public class QuestMatcherTests
{
    private static (string, GuideTask)[] Tasks(params string[] texts) =>
        texts.Select((t, i) => (i.ToString(), new GuideTask { Type = t.Contains(':') ? "side" : "main", Text = t })).ToArray();

    [Theory]
    [InlineData("Second Main Quest Step 2 Objective Speak Leena", "1")]
    [InlineData("Second Main Quest Step 1 Objective Speak Grael", "0")]
    [InlineData("First Main Quest Start", null)]
    [InlineData("Entrance Grael Deactivator", null)] // two lines mention Grael's camp equally: unsure, so no tick
    public void Matches_RealTriggerNames_ToTheRightLine(string trigger, string? expected)
    {
        var tasks = Tasks(
            "Speak with Grael in the Keeper's Camp",
            "Speak with Keeper Leena in the Keeper's Camp",
            "Buy a weapon");

        Assert.Equal(expected, QuestMatcher.Match(trigger, tasks));
    }
}
