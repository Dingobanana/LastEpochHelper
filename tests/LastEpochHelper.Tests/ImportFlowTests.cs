using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

/// <summary>
/// The whole import, from the pasted text to files on disk, against a stand-in for Maxroll: which
/// addresses are asked for, what happens when one of them fails, and what ends up in the builds folder.
/// </summary>
public sealed class ImportFlowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"leh-import-{Guid.NewGuid():N}");

    public ImportFlowTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Answers like Maxroll does, from a table of address -> (status, body); records what was asked.</summary>
    private sealed class FakeMaxroll : HttpMessageHandler
    {
        public readonly Dictionary<string, (HttpStatusCode Status, string Body)> Pages = new();
        public readonly List<string> Asked = new();
        public bool Offline;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string url = request.RequestUri!.ToString();
            Asked.Add(url);
            if (Offline) throw new HttpRequestException("No such host is known.");
            var (status, body) = Pages.TryGetValue(url, out var page) ? page : (HttpStatusCode.NotFound, "{\"error\":\"Profile not found\"}");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private const string PlannerUrl = "https://planners.maxroll.gg/profiles/le/";
    private const string Css = "https://assets-ng.maxroll.gg/leplanner/static/css/planner.css";
    private const string Data = "https://assets-ng.maxroll.gg/leplanner/game/data.json";

    private static string Profile(string name, string planner = MaxrollImporterTests.Planner) =>
        new JsonObject { ["id"] = "ab12cd34", ["name"] = name, ["data"] = planner }.ToJsonString();

    private static FakeMaxroll Maxroll(string name = "Test build", string planner = MaxrollImporterTests.Planner)
    {
        var maxroll = new FakeMaxroll();
        maxroll.Pages[PlannerUrl + "ab12cd34"] = (HttpStatusCode.OK, Profile(name, planner));
        maxroll.Pages[Data] = (HttpStatusCode.OK, MaxrollImporterTests.Game);
        maxroll.Pages[Css] = (HttpStatusCode.OK, ".x{background:url(/leplanner/static/media/treeAtlas.0123abcd.webp)}");
        maxroll.Pages["https://assets-ng.maxroll.gg/leplanner/static/media/treeAtlas.0123abcd.webp"] = (HttpStatusCode.OK, "not-really-a-picture");
        return maxroll;
    }

    [Fact]
    public async Task APlannerLink_IsFetched_Converted_AndCheckedClean()
    {
        var maxroll = Maxroll();
        using var http = new HttpClient(maxroll);

        var results = await MaxrollImporter.ImportAsync(" https://maxroll.gg/last-epoch/planner/AB12CD34#1 ", _dir, http);

        var result = Assert.Single(results); // two profiles with rising levels: one build with two stages
        Assert.Equal("Test build", result.Name);
        Assert.Equal(new[] { "Early", "Mid" }, result.Tree.Stages.Select(s => s.Name));
        Assert.Equal("ab12cd34", result.Tree.SourceId);
        Assert.Equal("maxroll_treeAtlas.0123abcd.webp", result.Tree.AtlasName);
        Assert.Empty(BuildChecks.Problems(result));
        Assert.True(File.Exists(Path.Combine(_dir, "maxroll_treeAtlas.0123abcd.webp")));
        Assert.Contains(PlannerUrl + "ab12cd34", maxroll.Asked);
    }

    [Fact]
    public async Task AGuidePage_LeadsToItsPlanner()
    {
        var maxroll = Maxroll();
        maxroll.Pages["https://maxroll.gg/last-epoch/build-guides/test-guide"] = (HttpStatusCode.OK,
            "<div data-le-profile=\"ab12cd34\" data-le-id=\"1\"></div><div data-le-profile=\"ab12cd34\"></div><div data-le-profile=\"zz99zz99\"></div>");
        using var http = new HttpClient(maxroll);

        var results = await MaxrollImporter.ImportAsync("maxroll.gg/last-epoch/build-guides/test-guide", _dir, http);
        Assert.Equal("ab12cd34", results[0].Tree.SourceId);

        // A page without a planner, and a page that is not there, are each refused in words.
        maxroll.Pages["https://maxroll.gg/last-epoch/resources/some-article"] = (HttpStatusCode.OK, "<p>no planner here</p>");
        var none = await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("https://maxroll.gg/last-epoch/resources/some-article", _dir, http));
        Assert.Contains("no build planner", none.Message);
        var missing = await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("https://maxroll.gg/last-epoch/build-guides/gone", _dir, http));
        Assert.Contains("no page at that address", missing.Message);
    }

    [Fact]
    public async Task WhatCannotBeImported_IsRefusedInWords_WithoutAskingTheNetworkWhenItNeedNot()
    {
        var maxroll = Maxroll();
        using var http = new HttpClient(maxroll);

        var tools = await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("https://www.lastepochtools.com/planner/AbCdEf12", _dir, http));
        Assert.Contains("Last Epoch Tools", tools.Message);
        Assert.Empty(maxroll.Asked); // refused from the text alone

        var deleted = await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("zz99zz99", _dir, http));
        Assert.Contains("no planner 'zz99zz99'", deleted.Message);

        // A planner that answers but holds nothing (private, or emptied).
        maxroll.Pages[PlannerUrl + "pr1vate0"] = (HttpStatusCode.OK, "{\"id\":\"pr1vate0\"}");
        Assert.Contains("not public", (await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("pr1vate0", _dir, http))).Message);
        maxroll.Pages[PlannerUrl + "garbage0"] = (HttpStatusCode.OK, "<html>maintenance</html>");
        Assert.Contains("not public", (await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("garbage0", _dir, http))).Message);
        maxroll.Pages[PlannerUrl + "empty000"] = (HttpStatusCode.OK, Profile("Empty", "{\"profiles\":[]}"));
        Assert.Contains("could not be read", (await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("empty000", _dir, http))).Message);
    }

    [Fact]
    public async Task WithoutTheIconSheet_TheBuildStillImports_AndOfflineIsReportedAsSuch()
    {
        var maxroll = Maxroll();
        maxroll.Pages.Remove(Css);
        using var http = new HttpClient(maxroll);
        var results = await MaxrollImporter.ImportAsync("ab12cd34", _dir, http);
        Assert.Equal("", results[0].Tree.AtlasName); // drawn without pictures

        maxroll.Offline = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => MaxrollImporter.ImportAsync("ab12cd34", _dir, http));
    }

    [Fact]
    public async Task VersionsOfOneBuild_EachGetTheirOwnFiles_EvenWithAwkwardNames()
    {
        // Three profiles at one level: versions. Two share a name, one has a name Windows cannot store.
        var planner = JsonNode.Parse(MaxrollImporterTests.Planner)!;
        var profiles = planner["profiles"]!.AsArray();
        var third = profiles[1]!.DeepClone();
        profiles.Add(third);
        profiles[0]!["level"] = 100; profiles[1]!["level"] = 100; profiles[2]!["level"] = 100;
        profiles[0]!["name"] = "Set 1"; profiles[1]!["name"] = "set 1"; profiles[2]!["name"] = "Fire/Cold: \"best\"?  ";
        using var http = new HttpClient(Maxroll("Мой билд <S5>", planner.ToJsonString()));

        var results = await MaxrollImporter.ImportAsync("ab12cd34", _dir, http);
        Assert.Equal(new[] { "Set 1", "set 1 (2)", "Fire/Cold: \"best\"?" }, results[0].Tree.Variants);
        Assert.Equal(new[] { 0, 1, 2 }, results.Select(r => r.Tree.Variant));

        string builds = Path.Combine(_dir, "builds");
        var files = BuildFiles.Write(builds, results);
        Assert.Equal(3, files.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(files, f => Assert.True(File.Exists(Path.Combine(builds, f)) && File.Exists(BuildTree.PathFor(Path.Combine(builds, f)))));
        Assert.StartsWith("Мой билд _S5_ - ", files[0]);
        // Each build knows all three files, so the tree window can switch without the network.
        var loaded = BuildTree.Load(BuildTree.PathFor(Path.Combine(builds, files[1])))!;
        Assert.Equal(files, loaded.VariantFiles);
        Assert.Equal(1, loaded.Variant);
        Assert.All(results, r => Assert.Empty(BuildChecks.Problems(r)));
    }

    private static string ThreeVersions()
    {
        var planner = JsonNode.Parse(MaxrollImporterTests.Planner)!;
        var profiles = planner["profiles"]!.AsArray();
        profiles.Add(profiles[1]!.DeepClone());
        string[] names = { "Starter", "Endgame", "Aspirational" };
        for (int i = 0; i < 3; i++) { profiles[i]!["level"] = 100; profiles[i]!["name"] = names[i]; }
        return planner.ToJsonString();
    }

    [Fact]
    public async Task TheVersionALinkOrGuidePointsAt_IsTheOneInUse()
    {
        var maxroll = Maxroll("Guide build", ThreeVersions());
        maxroll.Pages["https://maxroll.gg/last-epoch/build-guides/endgame-guide"] = (HttpStatusCode.OK,
            "<div data-le-profile=\"ab12cd34\" data-le-id=\"2\" data-le-type=\"plannerEquipment\"></div><div data-le-profile=\"ab12cd34\" data-le-id=\"2\" data-le-type=\"plannerSkills\"></div>");
        using var http = new HttpClient(maxroll);

        // No hint: the first version.
        Assert.Equal("Guide build - Starter", (await MaxrollImporter.ImportAsync("ab12cd34", _dir, http))[0].Name);
        // "#3" on a shared link: the third.
        var shared = await MaxrollImporter.ImportAsync("https://maxroll.gg/last-epoch/planner/ab12cd34#3", _dir, http);
        Assert.Equal("Guide build - Aspirational", shared[0].Name);
        Assert.Equal(new[] { "Aspirational", "Starter", "Endgame" }, shared[0].Tree.Variants);
        Assert.Equal(3, shared.Count);
        // A guide written around the second version.
        Assert.Equal("Guide build - Endgame", (await MaxrollImporter.ImportAsync("https://maxroll.gg/last-epoch/build-guides/endgame-guide", _dir, http))[0].Name);
        // A number past the end is ignored.
        Assert.Equal("Guide build - Starter", (await MaxrollImporter.ImportAsync("https://maxroll.gg/last-epoch/planner/ab12cd34#9", _dir, http))[0].Name);
    }

    [Fact]
    public async Task DataCopiedWithThePlannersExportButton_CanBePastedAsItIs()
    {
        using var http = new HttpClient(Maxroll());
        // One version: items written out in place, no name, no level, no list of its skills.
        const string exported = """
        {
          "class": 0, "mastery": 3,
          "items": { "head": { "itemType": 0, "subType": 3, "affixes": [ { "id": 25, "tier": 3, "roll": 1 } ] } },
          "idols": [ null, { "itemType": 0, "subType": 3, "affixes": [ { "id": 7, "tier": 2, "roll": 1 } ] } ],
          "passives": { "history": [1, 1, {"2": 2}, 9, 9], "position": 5 },
          "skillTrees": { "rv": { "history": [4, 4, 5], "position": 3 }, "lu": { "history": [], "position": 0 } }
        }
        """;
        var result = Assert.Single(await MaxrollImporter.ImportAsync(exported, _dir, http));

        Assert.Equal("Pasted build", result.Name);
        Assert.Equal(6, result.Tree.Stages[0].Passives.Count);
        Assert.Equal(new[] { 4, 4, 5 }, result.Tree.Stages[0].Skills["Rive"]); // the skill with points is the one specialized
        Assert.Contains(result.Tree.Stages[0].Gear, g => g.Name == "Iron Casque");
        Assert.Empty(BuildChecks.Problems(result));

        // Without a class there is nothing to hang the trees on - said in words.
        var noClass = await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("{\"passives\": {\"history\": [1], \"position\": 1}}", _dir, http));
        Assert.Contains("no class", noClass.Message);
        var cutOff = await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("{\"class\": 0, \"passives\": {\"hist", _dir, http));
        Assert.Contains("cut off", cutOff.Message);
        var other = await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("{\"hello\": 1}", _dir, http));
        Assert.Contains("not a build", other.Message);
    }

    [Fact]
    public async Task TheWeaverTrees_AreFetchedFromTheirOwnPage_AndShownBesideAnyBuild()
    {
        var maxroll = Maxroll();
        var game = JsonNode.Parse(MaxrollImporterTests.Game)!;
        game["skillTrees"]!["weaver"] = JsonNode.Parse("""
            { "nodes": { "0": { "nodeName": "Weaver Tree", "maxPoints": 0 },
                         "1": { "nodeName": "Shifted Knowledge", "maxPoints": 1, "requirements": [ { "node": 0 } ] },
                         "2": { "nodeName": "Woven Riches", "maxPoints": 3, "requirements": [ { "node": 1 } ] } } }
            """);
        maxroll.Pages[Data] = (HttpStatusCode.OK, game.ToJsonString());
        maxroll.Pages[MaxrollImporter.WeaverPage] = (HttpStatusCode.OK,
            "<h2>Starter</h2><div data-le-profile=\"st4rt3r0\" data-le-type=\"weavertree\" data-le-id=\"1\"></div>"
            + "<h2>Nemesis</h2><div data-le-type=\"weavertree\" data-le-profile=\"n3m3s1s0\"></div>"
            + "<div data-le-profile=\"ab12cd34\" data-le-type=\"plannerSkills\"></div>"
            + "<div data-le-profile=\"br0k3n00\" data-le-type=\"weavertree\"></div>");
        static string Planner(string name, string history) => new JsonObject
        {
            ["name"] = name,
            ["data"] = "{\"profiles\":[{\"name\":\"Set 1\",\"class\":0,\"weaver\":{\"history\":" + history + ",\"position\":99}}]}",
        }.ToJsonString();
        maxroll.Pages[PlannerUrl + "st4rt3r0"] = (HttpStatusCode.OK, Planner("Starter Tree", "[1, 2, 2, 77]")); // 77 is not a node of the tree
        maxroll.Pages[PlannerUrl + "n3m3s1s0"] = (HttpStatusCode.OK, Planner("Nemesis Tree", "[1, {\"2\": 3}]"));
        maxroll.Pages[PlannerUrl + "br0k3n00"] = (HttpStatusCode.OK, "<html>maintenance</html>");
        using var http = new HttpClient(maxroll);

        var set = await MaxrollImporter.ImportWeaverAsync(_dir, http);
        Assert.Equal(new[] { "Starter Tree", "Nemesis Tree" }, set.Strategies.Select(s => s.Name));
        Assert.Equal(new[] { 1, 2, 2 }, set.Strategies[0].History);
        Assert.Equal(new[] { 1, 2, 2, 2 }, set.Strategies[1].History);
        Assert.Equal(3, set.Tree.Nodes.Count);
        Assert.DoesNotContain(PlannerUrl + "ab12cd34", maxroll.Asked); // only the Weaver embeds are followed

        // A character with a build gets a Weaver tab with the chosen strategy, in every stage.
        var storage = new Storage(Path.Combine(_dir, "home"));
        var session = new Session(storage, new Guide { PassiveCap = 15, IdolCap = 8, Routes = { TrackerTests.MakeRoute("A", "B") } }, new SceneMap());
        var files = BuildFiles.Write(session.BuildsDir, await MaxrollImporter.ImportAsync("ab12cd34", _dir, http));
        session.Profile.BuildPlan = files[0];
        session.ReloadPlan();
        Assert.DoesNotContain(session.Tree!.Trees, t => t.Kind == TreeDef.WeaverKind);
        Assert.Empty(session.WeaverChoices);

        session.SetWeaver(set);
        var tab = Assert.Single(session.Tree!.Trees, t => t.Kind == TreeDef.WeaverKind);
        Assert.Equal(new[] { "Starter Tree", "Nemesis Tree" }, session.WeaverChoices);
        Assert.Equal("Starter Tree", session.WeaverChoice);
        Assert.All(session.Tree.Stages, s => Assert.Equal(new[] { 1, 2, 2 }, s.Skills[TreeDef.WeaverName]));
        session.Profile.SkillPoints[TreeDef.WeaverName] = 2;
        var state = session.TreeState(tab);
        Assert.Equal(1, state.Allocated[1]);
        Assert.Equal(1, state.Allocated[2]);
        Assert.Equal(2, state.Next[0].Node); // one more point into node 2

        // The tab is for the endgame only.
        Assert.False(session.InEndgame);
        session.Handle(new CharacterLevelEvent(60, 0, 3), live: false);
        Assert.True(session.InEndgame);

        session.SetWeaverStrategy("Nemesis Tree");
        Assert.Equal(4, session.TreeState(session.Tree!.Trees.Single(t => t.Kind == TreeDef.WeaverKind)).StagePoints);
        // It is remembered, also across a restart.
        var again = new Session(new Storage(Path.Combine(_dir, "home")), new Guide { PassiveCap = 15, IdolCap = 8, Routes = { TrackerTests.MakeRoute("A", "B") } }, new SceneMap());
        Assert.Equal("Nemesis Tree", again.WeaverChoice);
        Assert.Empty(BuildChecks.Problems(new MaxrollImporter.Result("x", "name: x\n", 0, again.Tree!)));

        // When the page is gone or empty, it is said in words.
        maxroll.Pages[MaxrollImporter.WeaverPage] = (HttpStatusCode.OK, "<p>moved</p>");
        Assert.Contains("No Weaver trees", (await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportWeaverAsync(_dir, http))).Message);
        maxroll.Pages.Remove(MaxrollImporter.WeaverPage);
        Assert.Contains("could not be read", (await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportWeaverAsync(_dir, http))).Message);
    }

    [Fact]
    public async Task ABuildSentAsAFile_Imports_WithoutTheNetwork()
    {
        // Someone imports from Maxroll and sends the two files the overlay wrote.
        var maxroll = Maxroll("Shared build");
        using var http = new HttpClient(maxroll);
        var original = await MaxrollImporter.ImportAsync("ab12cd34", _dir, http);
        string sent = Path.Combine(_dir, "sent");
        var files = BuildFiles.Write(sent, original);
        string treeFile = BuildTree.PathFor(Path.Combine(sent, files[0]));

        maxroll.Offline = true; // the receiver needs nothing from Maxroll
        maxroll.Asked.Clear();
        var received = Assert.Single(await MaxrollImporter.ImportFileAsync(treeFile, _dir, http));
        Assert.Equal("Shared build", received.Name);
        Assert.Equal(original[0].Tree.Stages.Select(s => s.Name), received.Tree.Stages.Select(s => s.Name));
        Assert.Equal(original[0].Text, received.Text);
        Assert.Empty(maxroll.Asked);
        Assert.Empty(BuildChecks.Problems(received));

        // Only the tree file, without its plan text: still a usable build.
        File.Delete(Path.Combine(sent, files[0]));
        var alone = Assert.Single(await MaxrollImporter.ImportFileAsync(treeFile, _dir, http));
        Assert.Equal("Shared build", BuildPlan.Parse(alone.Text).Name);
        Assert.Equal(2, alone.Tree.Stages.Count);
    }

    [Fact]
    public async Task APlannerSavedAsJson_Imports_InEitherShape_AndOtherFilesAreRefusedInWords()
    {
        using var http = new HttpClient(Maxroll());
        string whole = Path.Combine(_dir, "planner-answer.json"), inner = Path.Combine(_dir, "My planner.json");
        File.WriteAllText(whole, Profile("Saved planner"));
        File.WriteAllText(inner, MaxrollImporterTests.Planner);

        Assert.Equal("Saved planner", (await MaxrollImporter.ImportFileAsync(whole, _dir, http))[0].Name);
        var fromInner = (await MaxrollImporter.ImportFileAsync(inner, _dir, http))[0];
        Assert.Equal("My planner", fromInner.Name); // named after the file
        Assert.Equal("", fromInner.Tree.SourceId);
        Assert.Empty(BuildChecks.Problems(fromInner));

        foreach (var (name, content, reason) in new[]
                 {
                     ("notes.txt", "just some text", "not a build"),
                     ("list.json", "[1, 2, 3]", "not a build"),
                     ("settings.json", "{\"Opacity\": 0.9}", "neither this overlay's trees nor a Maxroll planner"),
                     ("empty.tree.json", "{\"trees\": [], \"stages\": []}", "empty or damaged"),
                     ("broken.json", "{\"data\": \"{not json\"}", "neither this overlay's trees nor a Maxroll planner"),
                 })
        {
            string file = Path.Combine(_dir, name);
            File.WriteAllText(file, content);
            var refused = await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportFileAsync(file, _dir, http));
            Assert.Contains(reason, refused.Message);
        }
        Assert.Contains("no file", (await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportFileAsync(Path.Combine(_dir, "missing.json"), _dir, http))).Message);
        Assert.Contains("no file at that path", MaxrollImporter.Understand(@"C:\Builds\mine.tree.json").Problem);
    }

    [Theory]
    [InlineData("Judgement Paladin - Starter", "Judgement Paladin - Starter")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a_b_c_d_e_f_g_h_i_j")]
    [InlineData("  spaced   out  ", "spaced out")]
    [InlineData("ends with dots...", "ends with dots")]
    [InlineData("", "build")]
    [InlineData("   ", "build")]
    [InlineData("CON", "CON_")]
    [InlineData("tab\tand\nnewline", "tab_and_newline")]
    public void BuildNames_BecomeFileNamesWindowsAccepts(string name, string file)
    {
        Assert.Equal(file, BuildFiles.SafeName(name));
        Assert.True(BuildFiles.SafeName(new string('x', 500)).Length <= 100);
    }
}
