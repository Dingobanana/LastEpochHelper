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

    /// <summary>A message worth interrupting for (a death, an item check), shown for a short while.</summary>
    public string? Alert => _alertUntil > DateTime.UtcNow ? _alert : null;
    private string? _alert;
    private DateTime _alertUntil;

    public void ShowAlert(string text, int seconds = 15)
    {
        _alert = text;
        _alertUntil = DateTime.UtcNow.AddSeconds(seconds);
        Changed?.Invoke();
    }

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
        ActivityLog.Write($"profile: class {profile.ClassId}, mastery {profile.Mastery}, level {profile.Level}, step {profile.Index}, {profile.PlanDone.Count} reminders ticked");
        if (Tracker is not null)
        {
            Tracker.Changed -= OnTrackerChanged;
            Tracker.ScenesChanged -= OnScenesChanged;
            Tracker.UnknownScene -= OnUnknownScene;
        }
        string? scene = Tracker?.CurrentScene;

        Profile = profile;
        Store.ActiveId = profile.Id;
        // An empty record says "this skill has no points", which no reading should have concluded.
        if (profile.Actual is { } stored)
            foreach (string empty in stored.Skills.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList())
                stored.Skills.Remove(empty);
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
                Profile.DeathLog.Add(new DeathEntry
                {
                    When = DateTime.Now, Zone = Tracker.Step.Zone, Level = Profile.Level,
                    ZoneLevel = Tracker.Step.Level, PlaySeconds = Profile.PlaySeconds,
                });
                Save();
                ShowAlert($"Died in {Tracker.Step.Zone} (level {Profile.Level}, zone {Tracker.Step.Level})."
                          + (DamageAdvice() is { } advice ? $"  {advice}." : "") + "  Note the cause in the planner: Deaths.", 25);
                break;

            case AccountEvent account:
                if (Settings.AccountName != account.Name)
                {
                    Settings.AccountName = account.Name;
                    SaveSettings();
                }
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

    /// <summary>Names of the builds imported on this machine (those with tree data).</summary>
    public List<string> ImportedBuilds()
    {
        try
        {
            return Directory.GetFiles(BuildsDir, "*.tree.json")
                .Select(f => Path.GetFileName(f)[..^".tree.json".Length]).OrderBy(n => n).ToList();
        }
        catch (IOException) { return new List<string>(); }
    }

    private List<BuildTree> LoadBuilds(IEnumerable<string>? names)
    {
        if (names is null) return Tree is null ? new List<BuildTree>() : new List<BuildTree> { Tree };
        return names.Select(n => BuildTree.Load(Path.Combine(BuildsDir, n + ".tree.json"))).Where(b => b is not null).Select(b => b!).ToList();
    }

    private void WriteFilter(string name, string xml)
    {
        Directory.CreateDirectory(FiltersDir);
        // The game writes its filters as UTF-8 with a byte order mark.
        File.WriteAllText(Path.Combine(FiltersDir, name + ".xml"), xml, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Changed?.Invoke();
    }

    /// <summary>
    /// Writes a loot filter for the given imported builds (default: this character's build) into the
    /// game's folder. Returns the filter's name, or null when no build lists gear to derive one from.
    /// </summary>
    public string? GenerateFilter(IEnumerable<string>? builds = null)
    {
        var loaded = LoadBuilds(builds);
        if (loaded.Count == 0) return null;
        string name = LootFilters.FileNameFor(string.Join(" + ", loaded.Select(b => b.Name)));
        if (LootFilters.Generate(loaded, name) is not { } xml) return null;
        WriteFilter(name, xml);
        return name;
    }

    /// <summary>
    /// Writes a copy of an installed filter with the builds' "never hide" rules on top. The original
    /// file is left untouched. Returns the new filter's name, or null if there was nothing to add.
    /// </summary>
    public string? AddBuildRulesTo(string filter, IEnumerable<string>? builds = null)
    {
        var loaded = LoadBuilds(builds);
        if (loaded.Count == 0) return null;
        try
        {
            string name = LootFilters.FileNameFor(filter)[4..] + " + build";
            string xml = File.ReadAllText(Path.Combine(FiltersDir, filter + ".xml"));
            if (LootFilters.AddKeepRules(xml, loaded, name) is not { } combined) return null;
            WriteFilter(name, combined);
            return name;
        }
        catch (IOException) { return null; }
        catch (System.Xml.XmlException) { return null; }
        catch (FormatException) { return null; }
    }

    /// <summary>Checks an installed filter for signs of age. Null if the file cannot be read as a filter.</summary>
    public FilterReport? InspectFilter(string name)
    {
        try
        {
            string xml = File.ReadAllText(Path.Combine(FiltersDir, name + ".xml"));
            return LootFilters.Inspect(xml, MaxrollImporter.CachedGameData(DataDir));
        }
        catch (IOException) { return null; }
        catch (System.Xml.XmlException) { return null; }
        catch (FormatException) { return null; }
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

    /// <summary>The most recent "damage to expect" line at or before the current step.</summary>
    public string? DamageAdvice()
    {
        for (int i = Tracker.Index; i >= 0; i--)
            if (Route.Flat[i].Step.Tasks.LastOrDefault(t => t.Type == "res") is { } task) return task.Text;
        return null;
    }

    public void SetDeathCause(DeathEntry entry, string cause)
    {
        entry.Cause = cause;
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Records that a node really has one point more or fewer than shown. The first correction takes
    /// over that tree from the plan: it starts from what the plan assumed and is the truth from then on.
    /// </summary>
    public void AdjustNode(TreeDef tree, TreeNode node, int delta)
    {
        if (Tree is null) return;
        var actual = Profile.Actual ??= new ActualTrees { Fetched = DateTime.Now, Level = Profile.Level };
        Dictionary<int, int> points;
        if (tree.Kind == TreeDef.PassiveKind)
        {
            if (actual.Passives.Count == 0 && !_passivesTaken)
            {
                // Passive points are one pool across all tabs, so seed every tab from the plan.
                int have = BuildTree.PassivePoints(Profile.Level, Rewards().Passive) + Profile.PassiveOffset;
                foreach (var tab in Tree.Trees.Where(t => t.Kind == TreeDef.PassiveKind))
                    foreach (var (id, count) in Tree.State(tab, have, Profile.Level).Allocated)
                        actual.Passives[id] = count;
                _passivesTaken = true;
            }
            points = actual.Passives;
        }
        else
        {
            string key = BuildTree.SkillKey(tree);
            if (!actual.Skills.TryGetValue(key, out points!))
                actual.Skills[key] = points = new Dictionary<int, int>(
                    Tree.State(tree, Profile.SkillPoints.GetValueOrDefault(tree.Name), Profile.Level).Allocated);
        }

        Profile.PlanViewTrees.Remove(ViewKey(tree)); // correcting a node means looking at the real points
        // Gear can put a node over its limit, and some builds plan for that ("5/4"): go as far as the build does.
        int planned = Tree.Stages.Max(s => (tree.Kind == TreeDef.PassiveKind ? s.Passives : s.Skills.GetValueOrDefault(tree.Name) ?? new List<int>()).Count(n => n == node.Id));
        int next = Math.Clamp(points.GetValueOrDefault(node.Id) + delta, 0, Math.Max(Math.Max(node.Max, 1), planned));
        if (next == 0) points.Remove(node.Id); else points[node.Id] = next;
        actual.Fetched = DateTime.Now;
        Save();
        Changed?.Invoke();
    }
    private bool _passivesTaken;

    /// <summary>
    /// Takes over the points the game shows for one tree. Only nodes that were read are changed:
    /// a node hidden behind a tooltip keeps what it had.
    /// </summary>
    /// <returns>True if anything changed.</returns>
    public bool SetReadPoints(TreeDef tree, IReadOnlyDictionary<int, int> read)
    {
        if (Tree is null || read.Count == 0) return false;
        var actual = Profile.Actual ??= new ActualTrees { Fetched = DateTime.Now, Level = Profile.Level };
        Dictionary<int, int> points;
        if (tree.Kind == TreeDef.PassiveKind) { points = actual.Passives; _passivesTaken = true; }
        else if (!actual.Skills.TryGetValue(BuildTree.SkillKey(tree), out points!))
            actual.Skills[BuildTree.SkillKey(tree)] = points = new Dictionary<int, int>();

        // A skill that has points according to the player, read as having none at all, is far more
        // likely a misread than a respec; a respec is one click on a node away.
        if (tree.Kind != TreeDef.PassiveKind && points.Count == 0 && read.Values.All(v => v == 0)
            && Profile.SkillPoints.GetValueOrDefault(tree.Name) > 0)
        {
            actual.Skills.Remove(BuildTree.SkillKey(tree));
            return false;
        }

        bool changed = false;
        foreach (var (node, have) in read)
        {
            if (points.GetValueOrDefault(node) == have) continue;
            if (have == 0) points.Remove(node); else points[node] = have;
            changed = true;
        }
        if (!changed) return false;
        actual.Fetched = DateTime.Now;
        TickSpecializationReminders(actual);
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// "Nth specialization slot unlocked" is the one kind of reminder the overlay can check: once it
    /// has seen N skills with points in the game, the first N of those reminders are evidently done.
    /// </summary>
    private void TickSpecializationReminders(ActualTrees actual)
    {
        int specialized = actual.Skills.Count(kv => kv.Value.Values.Sum() > 0);
        foreach (int level in new[] { 4, 8, 20, 35, 50 }.Take(specialized))
            Profile.PlanDone.Add($"milestone:{level}");
    }

    /// <summary>
    /// Ends every plan preview: trees whose real points are known show those again. A preview is for
    /// looking ahead while the tree is open - left on, the tree would silently stop mirroring the game.
    /// </summary>
    public void ClearPlanViews()
    {
        if (Profile.PlanViewTrees.Count == 0) return;
        Profile.PlanViewTrees.Clear();
        Save();
        Changed?.Invoke();
    }

    /// <summary>Sets how many points a tree has to place when it follows the plan (the slider in the tree view).</summary>
    public void SetTreePoints(TreeDef tree, int points)
    {
        points = Math.Max(0, points);
        // Choosing a number is asking for the plan at that many points, whatever the game shows.
        Profile.PlanViewTrees.Add(ViewKey(tree));
        if (tree.Kind == TreeDef.PassiveKind)
            Profile.PassiveOffset = points - BuildTree.PassivePoints(Profile.Level, Rewards().Passive);
        else Profile.SkillPoints[tree.Name] = points;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Passive points the character's level and quest rewards give, before any correction.</summary>
    public int PassivePointsByLevel() => BuildTree.PassivePoints(Profile.Level, Rewards().Passive);

    /// <summary>True when this tree shows hand-set (or imported) points instead of the plan's assumption.</summary>
    public bool HasActual(TreeDef tree) => Profile.Actual is { } actual &&
        (tree.Kind == TreeDef.PassiveKind ? actual.Passives.Count > 0 || _passivesTaken : actual.Skills.ContainsKey(BuildTree.SkillKey(tree)));

    private static string ViewKey(TreeDef tree) => tree.Kind == TreeDef.PassiveKind ? "passives" : BuildTree.SkillKey(tree);

    /// <summary>
    /// True when the tree is drawn from the character's real points. Real points may be known and
    /// still not shown: moving the slider switches that tree to the plan view until told otherwise.
    /// </summary>
    public bool ShowsActual(TreeDef tree) => HasActual(tree) && !Profile.PlanViewTrees.Contains(ViewKey(tree));

    /// <summary>Chooses between the game's points and the plan view for one tree.</summary>
    public void SetPlanView(TreeDef tree, bool planView)
    {
        bool changed = planView ? Profile.PlanViewTrees.Add(ViewKey(tree)) : Profile.PlanViewTrees.Remove(ViewKey(tree));
        if (!changed) return;
        Save();
        Changed?.Invoke();
    }

    /// <summary>When the points shown for the character were last read or set; null if never.</summary>
    public DateTime? ActualUpdated => Profile.Actual?.Fetched;

    /// <summary>Total points the game was last seen to have in this kind of tree.</summary>
    public int ActualPoints(TreeDef tree) => Profile.Actual is not { } actual ? 0
        : tree.Kind == TreeDef.PassiveKind ? actual.Passives.Values.Sum()
        : actual.Skills.GetValueOrDefault(BuildTree.SkillKey(tree))?.Values.Sum() ?? 0;

    /// <summary>Goes back to following the plan for one tree.</summary>
    public void ResetActual(TreeDef tree)
    {
        if (Profile.Actual is not { } actual) return;
        if (tree.Kind == TreeDef.PassiveKind) { actual.Passives.Clear(); _passivesTaken = false; }
        else actual.Skills.Remove(BuildTree.SkillKey(tree));
        Profile.PlanViewTrees.Remove(ViewKey(tree));
        if (actual.Passives.Count == 0 && actual.Skills.Count == 0) Profile.Actual = null;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Stores (or with null, forgets) the tree points read from the character's public profile.</summary>
    public void SetActual(ActualTrees? actual)
    {
        Profile.Actual = actual;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Puts the campaign guide away for this character, or brings it back.</summary>
    public void SetGuideHidden(bool hidden)
    {
        Profile.HideGuide = hidden;
        Profile.HideGuideOffered = true;
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// True the first time the character stands in the route's last chapter (the Monolith) with the
    /// guide still showing: the moment to mention that it can be hidden. Asking marks it as mentioned.
    /// </summary>
    public bool ShouldOfferHidingGuide()
    {
        if (Profile.HideGuide || Profile.HideGuideOffered || Route.Chapters.Count < 2 || Tracker.Chapter != Route.Chapters[^1]) return false;
        Profile.HideGuideOffered = true;
        Save();
        return true;
    }

    /// <summary>The stage of the guide chosen by hand, if any (and if the build still has it).</summary>
    public TreeStage? PinnedStage => Profile.StagePin.Length == 0 ? null : Tree?.Stages.FirstOrDefault(s => s.Name == Profile.StagePin);

    /// <summary>The stage of the guide in use: the one chosen by hand, else the one for the character's level.</summary>
    public TreeStage? Stage => PinnedStage ?? Tree?.StageFor(Profile.Level);

    /// <summary>Chooses a stage of the guide by hand; null goes back to following the character's level.</summary>
    public void SetStage(TreeStage? stage)
    {
        Profile.StagePin = stage?.Name ?? "";
        Save();
        Changed?.Invoke();
    }

    /// <summary>Switches to another version of the build (Starter / Endgame / ...), imported alongside this one.</summary>
    /// <returns>False if that version's files are not there (an import from before versions were kept).</returns>
    public bool SwitchVariant(int index)
    {
        if (Tree is not { } build || index < 0 || index >= build.VariantFiles.Count) return false;
        string file = build.VariantFiles[index];
        if (!File.Exists(Path.Combine(BuildsDir, file))) return false;
        Profile.BuildPlan = file;
        // Ticks on the other version's plan lines mean nothing here; everything else carries over.
        Profile.PlanDone.RemoveWhere(key => key.StartsWith("plan:", StringComparison.Ordinal));
        Profile.StagePin = "";
        ReloadPlan();
        if (Tree is { } now && now.Trees.All(t => t.Name != Profile.TreeTab)) Profile.TreeTab = "";
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>How a tab of the tree view should look for this character right now.</summary>
    public TreeState TreeState(TreeDef tree)
    {
        if (ShowsActual(tree) && Tree!.State(tree, Profile.Actual!, Profile.Level, pin: PinnedStage) is { } real) return real;

        int points = tree.Kind == TreeDef.PassiveKind
            ? BuildTree.PassivePoints(Profile.Level, Rewards().Passive) + Profile.PassiveOffset
            : Profile.SkillPoints.GetValueOrDefault(tree.Name);
        return Tree!.State(tree, points, Profile.Level, pin: PinnedStage);
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
        var (passive, idol, pending) = OwnRewards();
        // What the game's map said, plus what has been earned since. Ticking an older quest later does
        // not add to it: the map had already counted that one.
        if (Profile.MapPassives is { } seenPassives && Profile.MapIdols is { } seenIdols)
        {
            var (sincePassive, sinceIdol, _) = OwnRewards(Profile.MapIndex);
            passive = seenPassives + Math.Max(0, sincePassive - Profile.MapBasePassives);
            idol = seenIdols + Math.Max(0, sinceIdol - Profile.MapBaseIdols);
        }
        return (Math.Min(passive, Guide.PassiveCap), Math.Min(idol, Guide.IdolCap), pending);
    }

    /// <summary>
    /// Takes the counters read from the game's map as the truth. If they equal the overlay's count
    /// plus every side-quest reward still listed as unclaimed, those are evidently done and are ticked.
    /// </summary>
    public void SetMapCounters(int passive, int idol)
    {
        var (ownPassive, ownIdol, pending) = OwnRewards();
        if (pending.Count > 0 && passive == ownPassive + pending.Sum(p => p.Task.Passive) && idol == ownIdol + pending.Sum(p => p.Task.Idol))
        {
            foreach (var reward in pending) Profile.Done.Add(reward.Key);
        }
        Profile.MapPassives = passive;
        Profile.MapIdols = idol;
        Profile.MapIndex = Tracker.Index;
        (Profile.MapBasePassives, Profile.MapBaseIdols, _) = OwnRewards(Tracker.Index);
        Save();
        Changed?.Invoke();
    }

    /// <summary>The overlay's own bookkeeping, before the map's counters are applied.</summary>
    /// <param name="from">Only steps from this one onwards.</param>
    private (int Passive, int Idol, List<PendingReward> Pending) OwnRewards(int from = 0)
    {
        int passive = 0, idol = 0;
        var pending = new List<PendingReward>();
        for (int i = Math.Max(0, from); i <= Tracker.Index; i++)
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
