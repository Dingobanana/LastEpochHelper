using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public class ReadinessTests
{
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
}
