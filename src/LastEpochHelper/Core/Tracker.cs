namespace LastEpochHelper.Core;

/// <summary>
/// Keeps track of which guide step the player is on. Steps are advanced manually (hotkeys) or
/// automatically when the game loads a scene that maps to an upcoming step.
/// </summary>
public sealed class Tracker
{
    private readonly GuideRoute _guide;
    private readonly SceneMap _scenes;
    private (string Scene, int FromIndex)? _lastAutoLearn;

    public Tracker(GuideRoute guide, SceneMap scenes, int index = 0)
    {
        _guide = guide;
        _scenes = scenes;
        Index = Math.Clamp(index, 0, guide.Flat.Count - 1);
    }

    /// <summary>How many steps ahead a known scene may jump the tracker.</summary>
    public int Lookahead { get; set; } = 4;
    /// <summary>How many steps back it may fall when the scene matches nothing ahead (e.g. after a wrong jump).</summary>
    public int Lookbehind { get; set; } = 4;
    /// <summary>Bind unknown scenes to the next unmapped step as the player progresses.</summary>
    public bool AutoLearn { get; set; } = true;

    public int Index { get; private set; }
    public int Count => _guide.Flat.Count;
    public GuideChapter Chapter => _guide.Flat[Index].Chapter;
    public GuideStep Step => _guide.Flat[Index].Step;
    public int IndexInChapter => _guide.Flat[Index].IndexInChapter;
    public GuideStep? NextStep => Index + 1 < Count ? _guide.Flat[Index + 1].Step : null;

    /// <summary>Last scene id seen in the log, and the zone it maps to (null when unknown).</summary>
    public string? CurrentScene { get; private set; }
    public string? CurrentSceneZone { get; private set; }
    /// <summary>Human readable note about the last automatic action, for the status line.</summary>
    public string? LastEvent { get; private set; }

    public event Action? Changed;
    /// <summary>Raised when the learned scene mappings changed and should be saved.</summary>
    public event Action? ScenesChanged;
    /// <summary>Raised with a scene id that could not be mapped.</summary>
    public event Action<string>? UnknownScene;

    public void Next()
    {
        _lastAutoLearn = null;
        if (Index + 1 >= Count) return;
        Index++;
        LastEvent = null;
        Changed?.Invoke();
    }

    public void Prev()
    {
        // Stepping back right after an automatic "learn + advance" means the guess was wrong: forget it.
        if (_lastAutoLearn is { } learn && learn.FromIndex == Index - 1)
        {
            _scenes.Unlearn(learn.Scene);
            CurrentSceneZone = null;
            LastEvent = $"Forgot {learn.Scene}";
            ScenesChanged?.Invoke();
        }
        else LastEvent = null;
        _lastAutoLearn = null;
        if (Index == 0) { Changed?.Invoke(); return; }
        Index--;
        Changed?.Invoke();
    }

    public void JumpTo(int index)
    {
        _lastAutoLearn = null;
        Index = Math.Clamp(index, 0, Count - 1);
        LastEvent = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// Call when the game starts loading a scene. <paramref name="live"/> is false when replaying
    /// log history at startup; history may sync to a known zone but never teaches new mappings.
    /// <paramref name="catchUp"/> lets a tracker that is still on the first step leap any distance,
    /// for a character that is already part-way through the campaign.
    /// </summary>
    public void OnSceneLoaded(string scene, bool live = true, bool catchUp = false)
    {
        CurrentScene = scene;
        _lastAutoLearn = null;

        if (_scenes.TryGetZone(scene, out var zone))
        {
            CurrentSceneZone = zone;
            if (!SameKey(Step.Key, zone))
            {
                // Prefer the nearest upcoming visit; otherwise the nearest earlier one, which undoes
                // a jump caused by e.g. a town portal trip to a zone the route returns to later.
                int target = FindStep(zone, Index + 1, Math.Min(Count - 1, Index + Lookahead), +1);
                if (target < 0) target = FindStep(zone, Index - 1, Math.Max(0, Index - Lookbehind), -1);
                // Started mid-campaign with no saved progress: catch up to wherever the player is.
                if (target < 0 && Index == 0 && (catchUp || !live)) target = FindStep(zone, 1, Count - 1, +1);
                if (target >= 0)
                {
                    Index = target;
                    LastEvent = null;
                }
            }
            Changed?.Invoke();
            return;
        }

        CurrentSceneZone = null;
        if (live && AutoLearn)
        {
            if (!_scenes.HasSceneFor(Step.Key))
            {
                Learn(scene, Step.Key);
            }
            else if (NextStep is { } next && !_scenes.HasSceneFor(next.Key))
            {
                _lastAutoLearn = (scene, Index);
                Index++;
                Learn(scene, next.Key);
            }
        }
        if (CurrentSceneZone is null) UnknownScene?.Invoke(scene);
        Changed?.Invoke();
    }

    private int FindStep(string zone, int from, int to, int direction)
    {
        for (int i = from; direction > 0 ? i <= to : i >= to; i += direction)
            if (SameKey(_guide.Flat[i].Step.Key, zone)) return i;
        return -1;
    }

    private void Learn(string scene, string zoneKey)
    {
        _scenes.Learn(scene, zoneKey);
        CurrentSceneZone = zoneKey;
        LastEvent = $"Learned {scene} = {zoneKey}";
        ScenesChanged?.Invoke();
    }

    /// <summary>Passive points / idol slots granted by all steps before the current one.</summary>
    public (int Passive, int Idol) RewardsBeforeCurrent()
    {
        int p = 0, s = 0;
        for (int i = 0; i < Index; i++)
            foreach (var t in _guide.Flat[i].Step.Tasks) { p += t.Passive; s += t.Idol; }
        return (p, s);
    }

    private static bool SameKey(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
