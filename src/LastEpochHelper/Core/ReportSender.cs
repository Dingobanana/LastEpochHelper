using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace LastEpochHelper.Core;

/// <summary>
/// Sends a bug report zip to the people who maintain the overlay. The address is not in the source:
/// it is put into the program when a release is built (see the project file), so a build from the
/// public code alone has nowhere to send to and offers the zip on the desktop instead.
/// </summary>
public static class ReportSender
{
    /// <summary>Largest file the receiving side takes; a report with a big screenshot is re-made without it.</summary>
    public const long MaxBytes = 9_500_000;
    private const int MaxSummary = 1500;

    /// <summary>Where reports go, or null when this build was made without an address.</summary>
    public static string? Endpoint { get; } = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "ReportEndpoint")?.Value is { } value && Usable(value) ? value.Trim() : null;

    public static bool Usable(string? endpoint) =>
        Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <returns>Null when the report arrived; otherwise why it did not.</returns>
    public static async Task<string?> SendAsync(string endpoint, string zipPath, string summary, HttpClient http)
    {
        if (!Usable(endpoint)) return "no address to send to";
        try
        {
            var file = new FileInfo(zipPath);
            if (!file.Exists) return "the report file is missing";
            if (file.Length > MaxBytes) return "the report is too large to send";

            summary = summary.Trim();
            if (summary.Length > MaxSummary) summary = summary[..MaxSummary] + "…";
            // "allowed_mentions" with nothing in it: text in a description must not ping anyone.
            string payload = JsonSerializer.Serialize(new { content = summary, allowed_mentions = new { parse = Array.Empty<string>() } });

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(payload, Encoding.UTF8, "application/json"), "payload_json");
            var zip = new ByteArrayContent(await File.ReadAllBytesAsync(zipPath));
            zip.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(zip, "files[0]", file.Name);

            using var response = await http.PostAsync(endpoint.Trim(), form);
            return response.IsSuccessStatusCode ? null : $"the server answered {(int)response.StatusCode}";
        }
        catch (HttpRequestException e) { return "no connection (" + e.Message + ")"; }
        catch (TaskCanceledException) { return "it took too long"; }
        catch (IOException e) { return e.Message; }
    }
}
