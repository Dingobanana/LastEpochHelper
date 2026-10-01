namespace LastEpochHelper.Core;

/// <summary>
/// Maps the game's internal scene ids (as printed in Player.log, e.g. "Z12") to guide zone keys.
/// Shipped mappings are read-only; mappings learned while playing are kept separately so they can be saved.
/// </summary>
public sealed class SceneMap
{
    private readonly Dictionary<string, string> _shipped;
    private readonly Dictionary<string, string> _learned;

    public SceneMap(IDictionary<string, string>? shipped = null, IDictionary<string, string>? learned = null)
    {
        _shipped = new(shipped ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        _learned = new(learned ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string> Learned => _learned;

    public bool TryGetZone(string scene, out string zone)
    {
        if (_learned.TryGetValue(scene, out zone!)) return true;
        return _shipped.TryGetValue(scene, out zone!);
    }

    public bool HasSceneFor(string zoneKey) =>
        _learned.Values.Contains(zoneKey, StringComparer.OrdinalIgnoreCase) ||
        _shipped.Values.Contains(zoneKey, StringComparer.OrdinalIgnoreCase);

    public void Learn(string scene, string zoneKey) => _learned[scene] = zoneKey;

    public bool Unlearn(string scene) => _learned.Remove(scene);
}
