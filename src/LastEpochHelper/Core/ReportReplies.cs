using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace LastEpochHelper.Core;

/// <summary>A bug report this installation sent, by its reply id.</summary>
public sealed class SentReport
{
    public string Id { get; set; } = "";
    public DateTime Sent { get; set; }
}

/// <summary>An answer to a report, as written in the repository's replies.json.</summary>
public sealed record ReportReply(string Id, string Date, string Text)
{
    /// <summary>One answer is shown once; a later answer to the same report has another date.</summary>
    public string Key => Id + "|" + Date;
}

/// <summary>
/// Answers to bug reports without knowing who sent them. Every report carries a random reply id; the
/// maintainers answer in replies.json in the public repository, under that id. The overlay remembers the
/// ids it sent and looks there now and then: only the computer that sent a report knows the id is its own.
/// The answers are public, so they must hold nothing private - but nothing in them points to anyone.
/// </summary>
public static class ReportReplies
{
    public const string Address = "https://raw.githubusercontent.com/" + Updater.Repository + "/main/replies.json";
    /// <summary>How long after sending a report the overlay keeps looking for an answer.</summary>
    public static readonly TimeSpan LookFor = TimeSpan.FromDays(60);

    // No 0/O, 1/I/L, U: an id read aloud or typed from a screenshot comes out right.
    private const string Letters = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    /// <summary>A new id such as "K7Q2-9XMB": 40 bits of chance, nothing about the machine or the player.</summary>
    public static string NewId()
    {
        Span<char> id = stackalloc char[9];
        for (int i = 0; i < id.Length; i++) id[i] = i == 4 ? '-' : Letters[RandomNumberGenerator.GetInt32(Letters.Length)];
        return new string(id);
    }

    /// <summary>
    /// Reads replies.json: <c>{"replies": [{"id": "K7Q2-9XMB", "date": "2026-10-08", "text": "..."}]}</c>.
    /// Anything it does not understand is skipped.
    /// </summary>
    public static List<ReportReply> Parse(string json)
    {
        var replies = new List<ReportReply>();
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["replies"] is not JsonArray list) return replies;
            foreach (var node in list)
                if (node is JsonObject item && item["id"].StrOrNull() is { Length: > 0 } id && item["text"].StrOrNull() is { Length: > 0 } text)
                    replies.Add(new ReportReply(id.Trim().ToUpperInvariant(), item["date"].StrOrNull() ?? "", text.Trim()));
        }
        catch (System.Text.Json.JsonException) { }
        return replies;
    }

    /// <summary>Is there a recent report an answer could still come for?</summary>
    public static bool Waiting(IEnumerable<SentReport> sent, DateTime now) => sent.Any(s => now - s.Sent < LookFor);

    /// <summary>The answers to reports sent from here that have not been shown yet, oldest report first.</summary>
    public static List<ReportReply> New(IEnumerable<SentReport> sent, IEnumerable<ReportReply> replies, IReadOnlySet<string> shown)
    {
        var mine = sent.OrderBy(s => s.Sent).Select(s => s.Id.ToUpperInvariant()).ToList();
        return replies.Where(r => mine.Contains(r.Id) && !shown.Contains(r.Key)).OrderBy(r => mine.IndexOf(r.Id)).ThenBy(r => r.Date).ToList();
    }

    /// <returns>The replies, or null when the list could not be fetched (offline, GitHub down).</returns>
    public static async Task<List<ReportReply>?> FetchAsync(HttpClient http)
    {
        try
        {
            using var response = await http.GetAsync(Address);
            if (!response.IsSuccessStatusCode) return response.StatusCode == System.Net.HttpStatusCode.NotFound ? new() : null;
            return Parse(await response.Content.ReadAsStringAsync());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return null; }
    }
}
