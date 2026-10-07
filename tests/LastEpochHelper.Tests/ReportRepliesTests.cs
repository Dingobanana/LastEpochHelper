using System.Text.RegularExpressions;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public class ReportRepliesTests
{
    [Fact]
    public void AReplyId_IsRandom_AndEasyToReadBack()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => ReportReplies.NewId()).ToList();

        Assert.All(ids, id => Assert.Matches("^[A-HJKMNP-TV-Z2-9]{4}-[A-HJKMNP-TV-Z2-9]{4}$", id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void TheMessageBesideTheZip_StillPassesTheRelay_WithItsReplyId()
    {
        // relay/src/index.js: REPORT_HEADING
        var heading = new Regex(@"^\*\*Bug report\*\* - version \d{1,3}\.\d{1,3}\.\d{1,3}(\n|$)");

        string message = ReportSender.Summary("0.7.28", "The tree is wrong", "K7Q2-9XMB");

        Assert.Matches(heading, message);
        Assert.Equal("**Bug report** - version 0.7.28\nReply id: K7Q2-9XMB\nThe tree is wrong", message);
        Assert.Equal("**Bug report** - version 0.7.28\nx", ReportSender.Summary("0.7.28", "x"));
    }

    [Fact]
    public void OnlyAnswersToReportsSentFromHere_AreShown_EachOnce()
    {
        const string json = """
        { "replies": [
            { "id": "AAAA-BBBB", "date": "2026-10-08", "text": "Fixed in 0.7.28." },
            { "id": "cccc-dddd", "date": "2026-10-08", "text": "Not ours." },
            { "id": "AAAA-BBBB", "date": "2026-10-10", "text": "And a follow-up." },
            { "id": "EEEE-FFFF", "text": "" },
            { "text": "no id" },
            42
        ] }
        """;
        var replies = ReportReplies.Parse(json);
        Assert.Equal(3, replies.Count);
        Assert.Equal("CCCC-DDDD", replies[1].Id); // typed in lower case: still found

        var sent = new[] { new SentReport { Id = "AAAA-BBBB", Sent = new DateTime(2026, 10, 7) } };
        var shown = new HashSet<string>();
        var fresh = ReportReplies.New(sent, replies, shown);
        Assert.Equal(new[] { "Fixed in 0.7.28.", "And a follow-up." }, fresh.Select(r => r.Text));

        shown.Add(fresh[0].Key);
        Assert.Equal("And a follow-up.", Assert.Single(ReportReplies.New(sent, replies, shown)).Text);
    }

    [Fact]
    public void TheListIsOnlyAskedFor_WhileAReportIsRecent_AndBadDataIsIgnored()
    {
        var now = new DateTime(2026, 12, 1);
        Assert.False(ReportReplies.Waiting(Array.Empty<SentReport>(), now));
        Assert.False(ReportReplies.Waiting(new[] { new SentReport { Id = "X", Sent = now.AddDays(-61) } }, now));
        Assert.True(ReportReplies.Waiting(new[] { new SentReport { Id = "X", Sent = now.AddDays(-3) } }, now));

        Assert.Empty(ReportReplies.Parse("not json"));
        Assert.Empty(ReportReplies.Parse("""{"replies": {"AAAA-BBBB": "wrong shape"}}"""));
        Assert.Empty(ReportReplies.Parse("[]"));
    }
}
