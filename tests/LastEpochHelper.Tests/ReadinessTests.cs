using System.IO;
using LastEpochHelper;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public class ReadinessTests
{
    [Theory]
    [InlineData("Last Epoch on GeForce NOW", true)]   // the title a player saw in the taskbar
    [InlineData("LAST EPOCH on GeForce NOW", true)]
    [InlineData("GeForce NOW", false)]                 // the app's library, not the game
    [InlineData("Path of Exile 2 on GeForce NOW", false)]
    [InlineData("Last Epoch", false)]                  // the game itself is found by its process
    [InlineData("", false)]
    public void AGameStreamedFromGeForceNow_IsKnownByItsWindowTitle(string title, bool streamed)
    {
        Assert.Equal(streamed, GameWatcher.IsStreamedGame(title));
    }

    [Fact]
    public void TheGamesLanguage_IsReadFromItsSavedPreference()
    {
        Assert.Equal("en", GameLanguage.Parse(new byte[] { 0x65, 0x6E, 0x00 })); // as the game stores it
        Assert.Equal("es-ES", GameLanguage.Parse(System.Text.Encoding.ASCII.GetBytes("es-ES\0")));
        Assert.Equal("de", GameLanguage.Parse("de"));
        Assert.Null(GameLanguage.Parse(new byte[] { 0 }));
        Assert.Null(GameLanguage.Parse(new byte[] { 0xFF, 0x01 }));
        Assert.Null(GameLanguage.Parse(42));
        Assert.True(GameLanguage.IsEnglish("en"));
        Assert.False(GameLanguage.IsEnglish("de"));
        Assert.False(GameLanguage.IsEnglish(null));
        Assert.Equal("German", GameLanguage.Name("de"));
        Assert.Equal("Japanese", GameLanguage.Name("jp")); // the game's code, not an ISO one
    }

    [Fact]
    public void EnglishEverywhere_IsNoProblem()
    {
        Assert.Empty(Readiness.Problems(true, "en-US", "en"));
        Assert.Empty(Readiness.Problems(true, "en-GB", null)); // game language not known: nothing to say
    }

    [Fact]
    public void EachThingThatStopsReading_IsNamedWithWhatToDo()
    {
        var missing = Assert.Single(Readiness.Problems(false, null, "en"));
        Assert.Equal("ocr-missing", missing.Key);
        Assert.Contains("English (United States)", missing.Long);

        var russian = Assert.Single(Readiness.Problems(true, "ru-RU", "en"));
        Assert.Contains("ru-RU", russian.Short);

        var german = Assert.Single(Readiness.Problems(true, "en-US", "de"));
        Assert.Equal("game-de", german.Key);
        Assert.Contains("German", german.Short);
        Assert.Contains("any language", german.Long); // the campaign guide still works

        Assert.Equal(2, Readiness.Problems(false, null, "pl").Count);
    }

    [Fact]
    public void ANewerGameThanTheGuide_IsSaidOnce_APatchWithinTheVersionIsNot()
    {
        Assert.Null(Readiness.GuideBehind("1.5.1.2", "1.5"));
        Assert.Null(Readiness.GuideBehind("1.4.3", "1.5"));
        Assert.Null(Readiness.GuideBehind(null, "1.5"));
        Assert.Null(Readiness.GuideBehind("garbage", "1.5"));
        var behind = Readiness.GuideBehind("1.6.0.1", "1.5")!;
        Assert.Equal("guide-for-1.6", behind.Key);
        Assert.Contains("1.6", behind.Long);
        Assert.NotNull(Readiness.GuideBehind("2.0", "1.5"));

        // The real start-up line, through the parser and a session: one alert per game version.
        string line = "2026-10-06T08:40:26.3599716+00:00	Log	game version: 1.6.0.1. internal version: 1.6.0.3";
        Assert.Equal(new GameVersionEvent("1.6.0.1"), LogParser.Parse(line));
        var (guide, scenes) = ShippedDataTests.Load();
        var dir = Path.Combine(Path.GetTempPath(), $"leh-version-{Guid.NewGuid():N}");
        try
        {
            var session = new Session(new Storage(dir), guide, scenes);
            session.Handle(LogParser.Parse(line)!, false);
            Assert.Equal("1.6.0.1", session.GameVersion);
            Assert.Contains("bigger update", session.Alert);
            Assert.Contains("guide-for-1.6", session.Settings.ReadinessWarned);

            var again = new Session(new Storage(dir), guide, scenes);
            again.Handle(LogParser.Parse(line)!, false);
            Assert.Null(again.Alert);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
