using System.Net.Http;
using LastEpochHelper.Core;

namespace LastEpochHelper.Tests;

public class UsagePingTests
{
    [Fact]
    public void TheMessage_IsACountryCodeAndAVersion_AndNothingElse()
    {
        Assert.Equal("DK 0.7.17", UsagePing.Message("dk", "0.7.17"));
        Assert.Equal("US 0.8.0", UsagePing.Message(" US ", "0.8.0+7b4e43d92372")); // no build hash
        // Whatever Windows answers, nothing but two letters gets through.
        Assert.Equal("?? 0.7.17", UsagePing.Message("Denmark", "0.7.17"));
        Assert.Equal("?? 0.7.17", UsagePing.Message(null, "0.7.17"));
        Assert.Equal("?? 0.7.17", UsagePing.Message("4x", "0.7.17"));
    }

    [Fact]
    public void ItIsSentOncePerVersion_AndNotAtAllWhenSwitchedOff()
    {
        var settings = new Settings { CountryAsked = true };
        Assert.True(UsagePing.Due(settings, "0.7.17"));
        settings.CountrySentFor = "0.7.17";
        Assert.False(UsagePing.Due(settings, "0.7.17"));
        Assert.True(UsagePing.Due(settings, "0.7.18"));
        settings.ShareCountry = false;
        Assert.False(UsagePing.Due(settings, "0.7.18"));
    }

    [Fact]
    public void NothingIsSent_BeforeThePlayerSaidYes()
    {
        var settings = new Settings(); // ShareCountry is on by default, but nobody has been asked
        Assert.False(UsagePing.Due(settings, "0.7.20"));
        Assert.True(UsagePing.ShouldAsk(settings, "0.7.20"));

        settings.CountryAskedFor = "0.7.20"; // the question was closed without an answer
        Assert.False(UsagePing.ShouldAsk(settings, "0.7.20"));
        Assert.True(UsagePing.ShouldAsk(settings, "0.7.21")); // asked again after an update
        Assert.False(UsagePing.Due(settings, "0.7.21"));

        settings.CountryAsked = true; settings.ShareCountry = false; // "No thanks"
        Assert.False(UsagePing.ShouldAsk(settings, "0.7.22"));
        Assert.False(UsagePing.Due(settings, "0.7.22"));
    }

    [Fact]
    public void TheCountry_IsTwoLettersOrNothing()
    {
        string country = UsagePing.Country();
        Assert.True(country.Length is 0 or 2, country);
    }

    [Fact]
    public async Task WithoutAnAddress_NothingIsSent()
    {
        using var http = new HttpClient();
        Assert.False(await UsagePing.SendAsync("", "DK 0.7.17", http));
        Assert.False(await UsagePing.SendAsync("http://example.invalid/not-https", "DK 0.7.17", http));
    }
}
