using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;

namespace LastEpochHelper.Core;

/// <summary>
/// Reading Maxroll's data without caring whether a value came as text or as a number. The same field
/// has been "12" in one version of their data and 12 in another, and a reader that insists on one of
/// the two turns every import away the day they switch.
/// </summary>
public static class JsonRead
{
    /// <summary>The value as text ("12" for 12, "1.5" for 1.5), or null when it is missing or not a plain value.</summary>
    public static string? StrOrNull(this JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue(out string? text)) return text;
        if (value.TryGetValue(out long whole)) return whole.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue(out double number)) return number.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue(out bool flag)) return flag ? "true" : "false";
        return null;
    }

    /// <summary>The value as text; one that has to be there.</summary>
    public static string Str(this JsonNode? node) =>
        node.StrOrNull() ?? throw new InvalidDataException($"Expected a value, found {Describe(node)}.");

    /// <summary>The value as a whole number (12, 12.0 and "12" all count), or null.</summary>
    public static int? IntOrNull(this JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue(out int whole)) return whole;
        if (value.TryGetValue(out double number) && number == Math.Floor(number) && number is >= int.MinValue and <= int.MaxValue) return (int)number;
        if (value.TryGetValue(out string? text) && int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return parsed;
        return null;
    }

    /// <summary>The value as a whole number; one that has to be there.</summary>
    public static int Int(this JsonNode? node) =>
        node.IntOrNull() ?? throw new InvalidDataException($"Expected a whole number, found {Describe(node)}.");

    /// <summary>The value as a number (1.5 and "1.5" both count), or null.</summary>
    public static double? DoubleOrNull(this JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue(out double number)) return number;
        if (value.TryGetValue(out string? text) && double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) return parsed;
        return null;
    }

    /// <summary>The value as yes / no (true, 1 and "true" all count as yes), or null.</summary>
    public static bool? BoolOrNull(this JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue(out bool flag)) return flag;
        if (node.IntOrNull() is { } whole) return whole != 0;
        if (value.TryGetValue(out string? text) && bool.TryParse(text?.Trim(), out bool parsed)) return parsed;
        return null;
    }

    private static string Describe(JsonNode? node) => node switch
    {
        null => "nothing",
        JsonValue value => value.GetValueKind().ToString().ToLowerInvariant(),
        JsonArray => "a list",
        _ => "an object",
    };
}
