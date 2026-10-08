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
        public readonly List<string> Agents = new();
        public bool Offline;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string url = request.RequestUri!.ToString();
            Asked.Add(url);
            Agents.Add(request.Headers.UserAgent.ToString());
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
    public async Task AKeptCopyOfTheGameDataOlderThanThePlanner_IsFetchedAgain()
    {
        var maxroll = Maxroll();
        using var http = new HttpClient(maxroll);
        await MaxrollImporter.ImportAsync("https://maxroll.gg/last-epoch/planner/ab12cd34", _dir, http); // icon sheet and game data kept

        // Hours later Maxroll has new game data, and the planner names nodes the kept copy does not know.
        string kept = Path.Combine(_dir, "maxroll_le_data.json");
        File.WriteAllText(kept, "{}");
        File.SetLastWriteTimeUtc(kept, DateTime.UtcNow.AddHours(-3));
        maxroll.Asked.Clear();

        var result = Assert.Single(await MaxrollImporter.ImportAsync("https://maxroll.gg/last-epoch/planner/ab12cd34", _dir, http));

        Assert.Equal("Test build", result.Name);
        Assert.Single(maxroll.Asked, url => url == Data);
        Assert.NotEqual("{}", File.ReadAllText(kept));
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

    // A Last Epoch Tools build in the shape its public address answers, for the cut-down game data:
    // a node the game data does not know (77), an empty blessing list as [], and gear in its own ids.
    internal const string LeToolsBuild = """
    {"data":{"bio":{"level":30,"characterClass":0,"chosenMastery":3},
      "charTree":{"treeID":"","selected":{"1":3,"2":2,"9":1,"77":4},"version":3},
      "charTreeProgression":[1,2,1,2,1,9],
      "skillTrees":[{"treeID":"rv","selected":{"0":0,"4":2,"5":1},"level":3,"slotNumber":0,"version":2}],
      "skillTreesProgression":{"rv":[4,5,4]},
      "weaverTree":{"treeID":"","selected":{},"version":10},"weaverTreeProgression":[],
      "hud":["rv","",""],"blessings":[],"equipment":{"head":{"id":"UAwRgrALA7CQ","affixes":[]}}},
     "level":30,"class":0,"mastery":3,"data_version":"Version 1.5.0","guide":null}
    """;

    [Fact]
    public async Task ALastEpochToolsPlannerLink_IsFetchedFromItsPublicAddress_AndConvertedInItsOwnOrder()
    {
        var maxroll = Maxroll();
        maxroll.Pages["https://www.lastepochtools.com/api/public/build_data/AbCdEf12"] = (HttpStatusCode.OK, LeToolsBuild);
        using var http = new HttpClient(maxroll);

        var results = await MaxrollImporter.ImportAsync("look: https://www.lastepochtools.com/planner/AbCdEf12", _dir, http);

        var result = Assert.Single(results);
        Assert.Equal("Paladin - Last Epoch Tools AbCdEf12", result.Name);
        Assert.Equal("letools:AbCdEf12", result.Tree.SourceId);
        Assert.Contains("# Imported from https://www.lastepochtools.com/planner/AbCdEf12", result.Text);
        Assert.Contains("gear and idols are not imported", result.Text);
        var stage = Assert.Single(result.Tree.Stages);
        Assert.Equal(30, stage.Level);
        Assert.Equal(new[] { 1, 2, 1, 2, 1, 9 }, stage.Passives); // its order; node 77 is not in the game
        Assert.Equal(new[] { 4, 5, 4 }, stage.Skills["Rive"]);
        Assert.Empty(stage.Gear);
        // Asked once, saying who asks; the id keeps its capitals.
        int index = maxroll.Asked.IndexOf("https://www.lastepochtools.com/api/public/build_data/AbCdEf12");
        Assert.True(index >= 0);
        Assert.Contains("github.com/Dingobanana/LastEpochHelper", maxroll.Agents[index]);
        Assert.Single(maxroll.Asked, url => url.Contains("lastepochtools"));
    }

    [Fact]
    public async Task ALastEpochToolsPlannerThatIsNotThere_IsSaidSo()
    {
        var maxroll = Maxroll();
        // Not found still answers 200.
        maxroll.Pages["https://www.lastepochtools.com/api/public/build_data/Gone1234"] = (HttpStatusCode.OK, "{\"code\":3,\"error\":\"Build not found\"}");
        using var http = new HttpClient(maxroll);

        var gone = await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("https://www.lastepochtools.com/planner/Gone1234", _dir, http));
        Assert.Contains("no public planner 'Gone1234'", gone.Message);
        var missing = await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("https://www.lastepochtools.com/planner/Nope5678", _dir, http));
        Assert.Contains("no planner 'Nope5678'", missing.Message);
        // Cloudflare's bot check answers 403 with a challenge page.
        maxroll.Pages["https://www.lastepochtools.com/api/public/build_data/Wall1234"] = (HttpStatusCode.Forbidden, "<html>Just a moment...</html>");
        var walled = await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("https://www.lastepochtools.com/planner/Wall1234", _dir, http));
        Assert.Contains("turned the overlay away", walled.Message);
        Assert.Equal("https://www.lastepochtools.com/api/public/build_data/Wall1234", LeTools.BlockedDataUrl(walled));
    }

    [Fact]
    public async Task WhatTheBrowserShowsAtALastEpochToolsBuildsAddress_CanBePasted()
    {
        // The way round the site's bot check: the player's browser fetches the build, the text is pasted.
        var maxroll = Maxroll();
        using var http = new HttpClient(maxroll);
        string pasted = string.Join(" ", LeToolsBuild.Split('\n', StringSplitOptions.TrimEntries)); // one line, as the browser shows it

        var result = Assert.Single(await MaxrollImporter.ImportAsync(pasted, _dir, http));

        Assert.Equal("Paladin - Last Epoch Tools", result.Name);
        Assert.Equal(new[] { 1, 2, 1, 2, 1, 9 }, result.Tree.Stages[0].Passives);
        Assert.Equal(new[] { 4, 5, 4 }, result.Tree.Stages[0].Skills["Rive"]);
        Assert.DoesNotContain(maxroll.Asked, url => url.Contains("lastepochtools"));
        Assert.Empty(BuildChecks.Problems(result));
    }

    [Fact]
    public async Task WhatCannotBeImported_IsRefusedInWords_WithoutAskingTheNetworkWhenItNeedNot()
    {
        var maxroll = Maxroll();
        using var http = new HttpClient(maxroll);

        var tools = await Assert.ThrowsAsync<InvalidDataException>(() => MaxrollImporter.ImportAsync("https://www.lastepochtools.com/profile/SomeOne/character/Two", _dir, http));
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
    public async Task AnExportedTreeSharedBySeveralAbilities_IsTheClasssOwnSkill()
    {
        // Maxroll's data has more abilities on one tree than the player can have: Teleport also exists
        // as monster and shapeshift versions, some with the same name. The export names only the tree.
        var maxroll = Maxroll();
        var game = JsonNode.Parse(MaxrollImporterTests.Game)!;
        var abilities = new JsonObject
        {
            ["Wraith Rive"] = JsonNode.Parse("""{ "abilityName": "Rive", "playerAbilityID": "rv" }"""),
            ["Bear Rive"] = JsonNode.Parse("""{ "abilityName": "Bear Swipe", "playerAbilityID": "rv" }"""),
        };
        foreach (var (key, value) in game["abilities"]!.AsObject().ToList()) abilities[key] = value!.DeepClone();
        game["abilities"] = abilities;
        maxroll.Pages[Data] = (HttpStatusCode.OK, game.ToJsonString());
        using var http = new HttpClient(maxroll);
        const string exported = """
        { "class": 0, "mastery": 3, "passives": { "history": [1, 1], "position": 2 },
          "skillTrees": { "rv": { "history": [4, 4, 5], "position": 3 } } }
        """;

        var result = Assert.Single(await MaxrollImporter.ImportAsync(exported, _dir, http));

        Assert.Equal(new[] { 4, 4, 5 }, result.Tree.Stages[0].Skills["Rive"]);
        Assert.DoesNotContain("Bear Swipe", result.Tree.Stages[0].Skills.Keys);
        Assert.Single(result.Text.Split('\n'), l => l.Contains("Specialize"));
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

        // Read off the game's panel: 3 points placed, but only node 2 was on screen. The nodes seen are
        // shown as they are, and the window can say how many of the points they account for.
        session.SetReadPoints(tab, 3);
        session.SetReadPoints(tab, new Dictionary<int, int> { [2] = 2 });
        state = session.TreeState(tab);
        Assert.True(state.FromGame);
        Assert.Equal(2, state.Allocated[2]);
        Assert.False(state.Allocated.ContainsKey(1));
        Assert.Equal((2, 3), session.WeaverSeen(tab));
        session.SetReadPoints(tab, new Dictionary<int, int> { [1] = 1 });
        Assert.Null(session.WeaverSeen(tab)); // all three accounted for
        // A respec in the game: fewer points than the nodes read hold, so those reads are let go.
        session.SetReadPoints(tab, 1);
        Assert.False(session.HasActual(tab));
        Assert.False(session.TreeState(tab).FromGame);

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
