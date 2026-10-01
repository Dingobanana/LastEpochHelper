using System.Text.Json.Nodes;

namespace LastEpochHelper.Core;

/// <summary>Reads the gear, idols and blessings of a Maxroll planner profile into readable names.</summary>
internal static class MaxrollGear
{
    private const int BlessingItemType = 34;

    private static readonly (string Key, string Label)[] Slots =
    {
        ("weapon", "Weapon"), ("offhand", "Off-hand"), ("head", "Helmet"), ("body", "Body"), ("hands", "Gloves"),
        ("waist", "Belt"), ("feet", "Boots"), ("neck", "Amulet"), ("finger1", "Ring 1"), ("finger2", "Ring 2"), ("relic", "Relic"),
    };

    public static void Fill(TreeStage stage, JsonNode profile, JsonNode data, JsonNode game)
    {
        var items = data["items"] as JsonObject;

        if (profile["items"] is JsonObject worn && items is not null)
            foreach (var (key, label) in Slots)
                if (worn[key] is { } id && items[id.ToString()] is { } item)
                    stage.Gear.Add(Describe(label, item, game));

        if (profile["idols"] is JsonArray idols && items is not null)
        {
            // The array is the idol grid; identical idols are listed once with a count.
            foreach (var group in idols.Where(i => i is not null).GroupBy(i => i!.ToString()))
            {
                if (items[group.Key] is not { } idol) continue;
                var described = Describe("", idol, game);
                described.Count = group.Count();
                stage.Idols.Add(described);
            }
        }

        if (profile["blessings"] is JsonArray blessings)
            foreach (var blessing in blessings)
                if (blessing is not null && (blessing["itemType"]?.GetValue<int>() ?? -1) == BlessingItemType)
                    stage.Blessings.Add(BaseName(blessing, game));
    }

    private static JsonNode? Find(JsonNode? list, string key, int value) =>
        (list as JsonArray)?.FirstOrDefault(n => (n?[key]?.GetValue<int>() ?? -1) == value);

    private static string Text(JsonNode? node, params string[] keys)
    {
        foreach (string key in keys)
            if (node?[key]?.GetValue<string>() is { Length: > 0 } text) return text;
        return "";
    }

    private static string BaseName(JsonNode item, JsonNode game)
    {
        var type = Find(game["itemTypes"], "baseTypeID", item["itemType"]?.GetValue<int>() ?? -1);
        var sub = Find(type?["subItems"], "subTypeID", item["subType"]?.GetValue<int>() ?? -1);
        string name = Text(sub, "displayName", "name");
        return name.Length > 0 ? name : Text(type, "displayName", "BaseTypeName");
    }

    private static GearItem Describe(string slot, JsonNode item, JsonNode game)
    {
        var type = Find(game["itemTypes"], "baseTypeID", item["itemType"]?.GetValue<int>() ?? -1);
        var gear = new GearItem { Slot = slot, Name = BaseName(item, game), Type = Text(type, "displayName", "BaseTypeName") };
        if (item["uniqueID"] is { } uniqueId && Find(game["uniques"], "uniqueID", uniqueId.GetValue<int>()) is { } unique)
        {
            gear.Name = Text(unique, "displayName", "name");
            gear.UniqueId = uniqueId.GetValue<int>();
            gear.Rarity = unique["isSetItem"]?.GetValue<bool>() == true ? "set" : "unique";
        }

        void AddAffix(JsonNode? affix, string suffix = "")
        {
            if (affix?["id"] is not { } id) return;
            var definition = Find(game["affixes"], "affixId", id.GetValue<int>());
            string name = Text(definition, "affixDisplayName", "affixName");
            if (name.Length == 0) return;
            gear.Affixes.Add($"{name} T{affix["tier"]?.GetValue<int>() ?? 1}{suffix}");
            gear.AffixIds.Add(id.GetValue<int>());
        }
        foreach (var affix in item["affixes"] as JsonArray ?? new JsonArray()) AddAffix(affix);
        AddAffix(item["sealedAffix"], " (sealed)");
        foreach (var affix in item["corruptedAffixes"] as JsonArray ?? new JsonArray()) AddAffix(affix, " (corrupted)");
        return gear;
    }
}
