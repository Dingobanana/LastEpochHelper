using System.IO;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

/// <summary>
/// Sends a real bug report and a country ping through the relay (relay/), the way the overlay does.
/// Only on request: set LEH_RELAY_URL to the relay's address, e.g. http://127.0.0.1:8787 for
/// `npx wrangler dev`, with its webhooks pointed at something harmless.
/// </summary>
public sealed class RelayTests
{
    [Fact]
    public async Task TheRelayTakesWhatTheOverlaySends()
    {
        if (Environment.GetEnvironmentVariable("LEH_RELAY_URL") is not { Length: > 0 } relay) return;
        string dir = Path.Combine(Path.GetTempPath(), "leh-relay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "errors.log"), "nothing");
            string zip = BugReport.Create(new BugReportInput(dir, "", new[] { "Version: 0.7.20" }, Path.Combine(dir, "none.log"), null, Array.Empty<string>()), dir);
            using var http = ReportSender.CreateClient(TimeSpan.FromSeconds(30));
            // An empty description too: the summary is trimmed down to the heading alone.
            Assert.Null(await ReportSender.SendAsync(relay.TrimEnd('/') + "/report", zip, ReportSender.Summary("0.7.20", ""), http));
            Assert.True(await UsagePing.SendAsync(relay.TrimEnd('/') + "/ping", UsagePing.Message("DK", "0.7.20"), http));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
