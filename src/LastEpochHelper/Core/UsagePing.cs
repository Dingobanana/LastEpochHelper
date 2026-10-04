using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace LastEpochHelper.Core;

/// <summary>
/// Tells the people who maintain the overlay which country it is used in - and nothing else. Once per
/// installed version it sends two things: the country Windows is set to ("DK") and the overlay's
/// version. No name, no id, no machine details; two people in one country cannot be told apart.
/// Nothing is sent until the player said yes to the question (<see cref="Question"/>); the answer
/// can be changed in the settings. Like the bug report address, the address is not in the
/// source: a build from the public code alone has nowhere to send to and sends nothing.
/// </summary>
public static class UsagePing
{
    /// <summary>How long the overlay runs before it sends, so someone who changes their mind at once never sends.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(5);

    /// <summary>Where it goes, or null when this build was made without an address.</summary>
    public static string? Endpoint { get; } = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "UsageEndpoint")?.Value is { } value && ReportSender.Usable(value) ? value.Trim() : null;

    /// <summary>Is there something to send: the player said yes, and it was not sent for this version yet?</summary>
    public static bool Due(Settings settings, string version) =>
        settings.CountryAsked && settings.ShareCountry && version.Length > 0 && !string.Equals(settings.CountrySentFor, version, StringComparison.Ordinal);

    /// <summary>Ask once (again after an update if the window was closed without an answer)?</summary>
    public static bool ShouldAsk(Settings settings, string version) =>
        !settings.CountryAsked && !string.Equals(settings.CountryAskedFor, version, StringComparison.Ordinal);

    public const string Question = "Help me understand where Last Epoch Helper is used";

    /// <summary>The whole message: "DK 0.7.17". Anything that is not a plain country code becomes "??".</summary>
    public static string Message(string? country, string version)
    {
        country = country?.Trim().ToUpperInvariant() ?? "";
        if (country.Length != 2 || !country.All(c => c is >= 'A' and <= 'Z')) country = "??";
        string plain = new(version.TakeWhile(c => char.IsDigit(c) || c == '.').Take(20).ToArray());
        return $"{country} {plain}";
    }

    /// <summary>The country set under Windows' "Country or region" - not a position, and not looked up from the network.</summary>
    public static string Country()
    {
        try
        {
            var name = new StringBuilder(16);
            if (GetUserDefaultGeoName(name, name.Capacity) > 0 && name.Length == 2) return name.ToString();
        }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
        try { return RegionInfo.CurrentRegion.TwoLetterISORegionName; }
        catch (ArgumentException) { return ""; }
    }

    /// <returns>True when it arrived.</returns>
    public static async Task<bool> SendAsync(string endpoint, string message, HttpClient http)
    {
        if (!ReportSender.Usable(endpoint)) return false;
        try
        {
            string payload = JsonSerializer.Serialize(new { content = message, allowed_mentions = new { parse = Array.Empty<string>() } });
            using var response = await http.PostAsync(endpoint.Trim(), new StringContent(payload, Encoding.UTF8, "application/json"));
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetUserDefaultGeoName(StringBuilder geoName, int geoNameCount);
}
