using System.IO;

namespace LastEpochHelper.Core;

/// <summary>A reward-bearing side quest line in a step the player has already left without ticking it.</summary>
public sealed record PendingReward(string Key, GuideTask Task, string Zone);

/// <summary>
/// The overlay's state without any UI: which character is being played, where it is in its route,
/// what it has earned, and how long it has taken. Fed with log events; raises <see cref="Changed"/>.
/// </summary>
public sealed class Session
{
    public const string SettingsFile = "settings.json";
    public const string ProfilesFile = "profiles.json";
    public const string LearnedScenesFile = "learned_scenes.json";
    public const string UnknownScenesFile = "unknown_scenes.txt";
    private const string LegacyStateFile = "state.json";

    private readonly Storage _storage;
    private readonly SceneMap _scenes;
    private bool _awaitingCharacter;
    private CharacterCreatedEvent? _created;
    private (string Scene, bool Live)? _pendingScene;
    private double _unsavedSeconds;

    public Settings Settings { get; }
    public Guide Guide { get; }
    public ProfileStore Store { get; }
    public Profile Profile { get; private set; } = null!;
    public GuideRoute Route { get; private set; } = null!;
    public Tracker Tracker { get; private set; } = null!;
    public BuildPlan? Plan { get; private set; }
    /// <summary>Tree layouts and point order of the imported build; null for hand-written plans.</summary>
    public BuildTree? Tree { get; private set; }
    /// <summary>True between entering the world and returning to character select.</summary>
    public bool InGame { get; private set; }
    /// <summary>Short note about the last automatic action (profile switch, auto-tick), for the status line.</summary>
    public string? Notice { get; private set; }

    public event Action? Changed;

    public EndgameData Endgame { get; }
    /// <summary>The game's loot filter folder.</summary>
    public string FiltersDir { get; }

    public Session(Storage storage, Guide guide, SceneMap scenes, EndgameData? endgame = null, string? filtersDir = null)
    {
        _storage = storage;
        _scenes = scenes;
        Guide = guide;
        Endgame = endgame ?? new EndgameData();
        FiltersDir = filtersDir ?? Path.Combine(Path.GetDirectoryName(LogWatcher.DefaultPath)!, "Filters");
        Settings = storage.Load<Settings>(SettingsFile);
        Store = storage.Load<ProfileStore>(ProfilesFile);

        if (Store.Profiles.Count == 0)
        {
            var first = new Profile { Name = "Character 1" };
            if (storage.Exists(LegacyStateFile))
            {
                var old = storage.Load<ProgressState>(LegacyStateFile);
                first.Index = old.Index;
                first.Done = old.Done;
            }
            Store.Profiles.Add(first);
        }
        Activate(Store.Profiles.FirstOrDefault(p => p.Id == Store.ActiveId) ?? Store.Profiles[0]);
    }

    public string BuildsDir => _storage.Folder("builds");
    public string MapsDir => _storage.Folder("maps");
    public string DataDir => _storage.Dir;

    // ------------------------------------------------------------------ profiles

    public void Activate(Profile profile)
    {
        if (Tracker is not null)
        {
            Tracker.Changed -= OnTrackerChanged;
            Tracker.ScenesChanged -= OnScenesChanged;
            Tracker.UnknownScene -= OnUnknownScene;
        }
        string? scene = Tracker?.CurrentScene;

        Profile = profile;
        Store.ActiveId = profile.Id;
        Route = Guide.Route(profile.RouteId);
        Tracker = new Tracker(Route, _scenes, profile.Index)
        {
            AutoLearn = Settings.AutoLearn,
            Lookahead = Settings.Lookahead,
            Lookbehind = Settings.Lookbehind,
        };
        Tracker.Changed += OnTrackerChanged;
        Tracker.ScenesChanged += OnScenesChanged;
        Tracker.UnknownScene += OnUnknownScene;
        ReloadPlan();
        // Keep showing where the game currently is, without letting it move the new profile's position.
        if (scene is not null && profile.Index > 0) Tracker.OnSceneLoaded(scene, live: false);
        Save();
        Changed?.Invoke();
    }

    public Profile CreateProfile(string name, int classId = -1)
    {
        var profile = new Profile { Name = UniqueName(name), ClassId = classId };
        Store.Profiles.Add(profile);
        return profile;
    }

    public void DeleteProfile(Profile profile)
    {
        if (Store.Profiles.Count <= 1) return;
        Store.Profiles.Remove(profile);
        if (profile == Profile) Activate(Store.Profiles[0]);
        else Save();
    }

    public void SetRoute(string routeId)
    {
        if (Profile.RouteId.Equals(routeId, StringComparison.OrdinalIgnoreCase)) return;
        string? scene = Tracker.CurrentScene;
        Profile.RouteId = Guide.Route(routeId).Id;
        // Step indexes mean something else on another route.
        Profile.ResetProgress();
        Activate(Profile);
        if (scene is not null) Tracker.OnSceneLoaded(scene, live: false, catchUp: true);
    }

    public void ResetProgress()
    {
        Profile.ResetProgress();
        Profile.PlanDone.Clear();
        Activate(Profile);
    }

    public void ReloadPlan()
    {
        string? path = string.IsNullOrEmpty(Profile.BuildPlan) ? null : Path.Combine(BuildsDir, Profile.BuildPlan);
        Plan = path is null ? null : BuildPlan.Load(path);
        Tree = path is null ? null : BuildTree.Load(BuildTree.PathFor(path));
    }

    private string UniqueName(string name)
    {
        string candidate = name;
        for (int n = 2; Store.Profiles.Any(p => p.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); n++)
            candidate = $"{name} {n}";
        return candidate;
    }

    /// <summary>
    /// Works out which profile the character entering the world belongs to. The log names a character
    /// only when it is created; otherwise class, mastery and level have to identify it.
    /// </summary>
    private void ResolveProfile(CharacterLevelEvent info)
    {
        Profile? target;
        if (_created is { } created)
        {
            target = Store.Profiles.FirstOrDefault(p => p.Name.Equals(created.Name, StringComparison.OrdinalIgnoreCase));
            if (target is null && Profile.ClassId < 0)
            {
                target = Profile;
                target.Name = UniqueName(created.Name);
            }
            target ??= CreateProfile(created.Name, created.ClassId);
        }
        else
        {
            bool Fits(Profile p) => p.ClassId == info.ClassId && (p.Mastery == 0 || p.Mastery == info.Mastery) && info.Level >= p.Level;
            // Levels only go up, so the profile whose last known level is closest below is the best guess.
            target = Store.Profiles.Where(Fits)
                .OrderBy(p => info.Level - p.Level).ThenBy(p => p == Profile ? 0 : 1).FirstOrDefault();
            // An untouched profile adopts whatever character shows up first.
            if (target is null && Profile.ClassId < 0) target = Profile;
            target ??= CreateProfile(Profile.ClassName(info.ClassId, info.Mastery), info.ClassId);
        }

        _created = null;
        if (target != Profile)
        {
            Activate(target);
            Notice = $"Profile: {target.Name}";
        }
    }

    // ------------------------------------------------------------------ log events

    public void Handle(LogEvent e, bool live)
    {
        switch (e)
        {
            case CharacterSelectEvent:
                InGame = false;
                _awaitingCharacter = true;
                _created = null;
                _pendingScene = null;
                Save();
                break;

            case CharacterCreatedEvent created:
                _created = created;
                _awaitingCharacter = true;
                break;

            case SceneLoadEvent scene:
                InGame = true;
                // The scene is logged a moment before the character data that tells us whose it is.
                if (_awaitingCharacter && _pendingScene is null) _pendingScene = (scene.Scene, live);
                else
                {
                    FlushPendingScene();
                    ApplyScene(scene.Scene, live);
                }
                break;

            case CharacterLevelEvent level:
                if (_awaitingCharacter || (Profile.ClassId >= 0 && Profile.ClassId != level.ClassId))
                {
                    ResolveProfile(level);
                    _awaitingCharacter = false;
                }
                bool leveledUp = live && Profile.Level > 0 && level.Level > Profile.Level;
                Profile.Level = level.Level;
                Profile.ClassId = level.ClassId;
                Profile.Mastery = level.Mastery;
                Profile.LastPlayed = DateTime.Now;
                if (leveledUp) Notice = $"Level {level.Level}!";
                FlushPendingScene();
                Save();
                break;

            case QuestTriggerEvent trigger when live && Settings.AutoTick:
                AutoTick(trigger.Name);
                break;

            case PlayerDiedEvent when live:
                Profile.Deaths++;
                Save();
                break;
        }
        Changed?.Invoke();
    }

    private void FlushPendingScene()
    {
        if (_pendingScene is not { } pending) return;
        _pendingScene = null;
        _awaitingCharacter = false;
        ApplyScene(pending.Scene, pending.Live);
    }

    // catchUp only has an effect while the tracker is still on the very first step.
    private void ApplyScene(string scene, bool live) => Tracker.OnSceneLoaded(scene, live, catchUp: true);

    private void AutoTick(string trigger)
    {
        var candidates = new List<(string, GuideTask)>();
        int from = Math.Max(0, Tracker.Index - 1), to = Math.Min(Tracker.Count - 1, Tracker.Index + 2);
        for (int i = from; i <= to; i++)
        {
            var tasks = Route.Flat[i].Step.Tasks;
            for (int j = 0; j < tasks.Count; j++)
                if (!Profile.Done.Contains(Key(i, j))) candidates.Add((Key(i, j), tasks[j]));
        }
        if (QuestMatcher.Match(trigger, candidates) is not { } key) return;
        Profile.Done.Add(key);
        Save();
    }

    // ------------------------------------------------------------------ tasks and rewards

    public static string Key(int step, int task) => $"{step}:{task}";

    public bool IsDone(string key) => Profile.Done.Contains(key);

    public void ToggleDone(string key)
    {
        Profile.Skipped.Remove(key);
        if (!Profile.Done.Remove(key)) Profile.Done.Add(key);
        Save();
        Changed?.Invoke();
    }

    public void SkipReward(string key)
    {
        Profile.Done.Remove(key);
        Profile.Skipped.Add(key);
        Save();
        Changed?.Invoke();
    }

    public void TogglePlanDone(string key)
    {
        if (!Profile.PlanDone.Remove(key)) Profile.PlanDone.Add(key);
        Save();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ planner: gear, filters, endgame

    public void ToggleGearDone(string key)
    {
        if (!Profile.GearDone.Remove(key)) Profile.GearDone.Add(key);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Names of the loot filters in the game's folder.</summary>
    public List<string> InstalledFilters()
    {
        try
        {
            return Directory.Exists(FiltersDir)
                ? Directory.GetFiles(FiltersDir, "*.xml").Select(f => Path.GetFileNameWithoutExtension(f)!).OrderBy(n => n).ToList()
                : new List<string>();
        }
        catch (IOException) { return new List<string>(); }
        catch (UnauthorizedAccessException) { return new List<string>(); }
    }

    public void SetFilterLevel(string filter, int level)
    {
        if (level <= 0) Profile.FilterStages.Remove(filter);
        else Profile.FilterStages[filter] = level;
        Save();
        Changed?.Invoke();
    }

    /// <summary>"Switch loot filter" reminders for this character, as build lines.</summary>
    public IEnumerable<PlanEntry> FilterEntries() =>
        Profile.FilterStages.Where(kv => kv.Value > 0)
            .Select(kv => new PlanEntry(kv.Value, $"Loot filter: switch to \"{kv.Key}\" (Shift+F in game)", $"filter:{kv.Key}:{kv.Value}"));

    public TimelineProgress TimelineProgress(string timeline) => Profile.Timelines.GetValueOrDefault(timeline) ?? new TimelineProgress();

    public void UpdateTimeline(string timeline, Action<TimelineProgress> change)
    {
        if (!Profile.Timelines.TryGetValue(timeline, out var progress)) Profile.Timelines[timeline] = progress = new TimelineProgress();
        change(progress);
        Save();
        Changed?.Invoke();
    }

    public DungeonProgress DungeonProgress(string dungeon) => Profile.Dungeons.GetValueOrDefault(dungeon) ?? new DungeonProgress();

    public void UpdateDungeon(string dungeon, Action<DungeonProgress> change)
    {
        if (!Profile.Dungeons.TryGetValue(dungeon, out var progress)) Profile.Dungeons[dungeon] = progress = new DungeonProgress();
        change(progress);
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Knowledge of Orobyss: from timelines ticked as completed, plus one each for finishing
    /// Chapter 9 and Chapter 10 when the route has taken the character past them.
    /// </summary>
    public int Knowledge()
    {
        int knowledge = Endgame.Timelines.Where(t => TimelineProgress(t.Name).Normal).Sum(t => t.Knowledge);
        int position = Route.Chapters.IndexOf(Tracker.Chapter);
        knowledge += Route.Chapters.Take(position).Count(c => c.Id is 9 or 10);
        return knowledge;
    }

    /// <summary>How a tab of the tree view should look for this character right now.</summary>
    public TreeState TreeState(TreeDef tree)
    {
        int points = tree.Kind == TreeDef.PassiveKind
            ? BuildTree.PassivePoints(Profile.Level, Rewards().Passive) + Profile.PassiveOffset
            : Profile.SkillPoints.GetValueOrDefault(tree.Name);
        return Tree!.State(tree, points, Profile.Level);
    }

    public void AdjustTreePoints(TreeDef tree, int delta)
    {
        if (tree.Kind == TreeDef.PassiveKind)
        {
            // Never let the correction push the total below zero.
            int computed = BuildTree.PassivePoints(Profile.Level, Rewards().Passive);
            Profile.PassiveOffset = Math.Max(-computed, Profile.PassiveOffset + delta);
        }
        else Profile.SkillPoints[tree.Name] = Math.Max(0, Profile.SkillPoints.GetValueOrDefault(tree.Name) + delta);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Ticks every plan entry up to and including the given level - for clearing a backlog in one go.</summary>
    public void TickPlanThrough(int level)
    {
        foreach (var entry in (Plan?.Entries ?? Enumerable.Empty<PlanEntry>()).Concat(BuildPlan.Milestones).Concat(FilterEntries()))
            if (entry.Level <= level) Profile.PlanDone.Add(entry.Key);
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Quest passives / idol slots earned so far. Main-quest rewards count once their step is behind
    /// the player (they cannot be missed); side-quest rewards count when ticked, and otherwise show
    /// up as pending until ticked or skipped.
    /// </summary>
    public (int Passive, int Idol, List<PendingReward> Pending) Rewards()
    {
        int passive = 0, idol = 0;
        var pending = new List<PendingReward>();
        for (int i = 0; i <= Tracker.Index; i++)
        {
            var step = Route.Flat[i].Step;
            for (int j = 0; j < step.Tasks.Count; j++)
            {
                var task = step.Tasks[j];
                if (!task.HasReward) continue;
                string key = Key(i, j);
                if (Profile.Skipped.Contains(key)) continue;
                bool passed = i < Tracker.Index;
                if (Profile.Done.Contains(key) || (passed && task.IsMain))
                {
                    passive += task.Passive;
                    idol += task.Idol;
                }
                else if (passed) pending.Add(new PendingReward(key, task, step.Zone));
            }
        }
        return (Math.Min(passive, Guide.PassiveCap), Math.Min(idol, Guide.IdolCap), pending);
    }

    // ------------------------------------------------------------------ timers

    /// <summary>Call regularly with the elapsed wall-clock time while the game is running.</summary>
    public void Tick(double seconds)
    {
        if (!InGame) return;
        Profile.PlaySeconds += seconds;
        _unsavedSeconds += seconds;
        if (_unsavedSeconds >= 30) Save();
    }

    /// <summary>Time spent in the current chapter, and the fastest finished run of it by another profile.</summary>
    public (double Current, double? Best) ChapterTimes()
    {
        int position = Route.Chapters.IndexOf(Tracker.Chapter);
        int first = Route.FirstStepOf(position), next = Route.FirstStepOf(position + 1);
        double current = Profile.Splits.TryGetValue(first, out double start) ? Profile.PlaySeconds - start : 0;

        double? best = null;
        foreach (var other in Store.Profiles)
        {
            if (other == Profile || !other.RouteId.Equals(Profile.RouteId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!other.Splits.TryGetValue(first, out double a) || !other.Splits.TryGetValue(next, out double b) || b <= a) continue;
            if (best is null || b - a < best) best = b - a;
        }
        return (current, best);
    }

    // ------------------------------------------------------------------ plumbing

    private void OnTrackerChanged()
    {
        Profile.Index = Tracker.Index;
        if (InGame && !Profile.Splits.ContainsKey(Tracker.Index)) Profile.Splits[Tracker.Index] = Profile.PlaySeconds;
        Notice = null;
        Save();
        Changed?.Invoke();
    }

    private void OnScenesChanged() => _storage.Save(LearnedScenesFile, _scenes.Learned);

    private void OnUnknownScene(string scene) => _storage.AppendLine(UnknownScenesFile,
        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{scene}\tat step: {Tracker.Step.Zone}");

    public void ApplySettings()
    {
        Tracker.AutoLearn = Settings.AutoLearn;
        Tracker.Lookahead = Settings.Lookahead;
        Tracker.Lookbehind = Settings.Lookbehind;
        SaveSettings();
        Changed?.Invoke();
    }

    public void SaveSettings() => _storage.Save(SettingsFile, Settings);

    public void Save()
    {
        _unsavedSeconds = 0;
        _storage.Save(ProfilesFile, Store);
    }
}
