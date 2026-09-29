using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class LinkTests
{
    [Fact]
    public void DiscordAndWebAddressesBecomeLinks()
    {
        var text = "7 PM EST/ Crystal Zalera Goblet W7 P5/ discord.gg/moonlitkissclub and https://example.com/night.";
        var links = LinkFinder.Find(text);
        Assert.Equal(2, links.Count);
        Assert.Equal("https://discord.gg/moonlitkissclub", links[0].Url);
        Assert.Equal("discord.gg/moonlitkissclub", links[0].Label);
        Assert.Equal("https://example.com/night", links[1].Url);
        Assert.False(LinkFinder.IsHttp("javascript:alert(1)"));
        Assert.Empty(LinkFinder.Find("no address here"));
        var stream = LinkFinder.Find("drop by twitch.com/examplecaster");
        var twitch = Assert.Single(stream);
        Assert.Equal("https://twitch.tv/examplecaster", twitch.Url);
        Assert.Equal("twitch.tv/examplecaster", twitch.Label);
        var full = LinkFinder.Find("https://www.twitch.tv/examplecaster and twitch.com/examplecaster");
        var once = Assert.Single(full);
        Assert.Contains("examplecaster", once.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GobletWardAndPlotAreATravelSpotOnZalera()
    {
        var spot = HousingTravel.Find("ward 7, plot 5, The Goblet, Crystal, Zalera", "7 PM EST", 7, "Crystal, Zalera");
        Assert.Equal(9u, spot!.Value.CityAetheryteId);
        Assert.NotNull(spot);
        Assert.Equal("The Goblet", spot.Value.District);
        Assert.Equal(7, spot.Value.Ward);
        Assert.Equal(5, spot.Value.Plot);
        Assert.Equal("Zalera", spot.Value.World);
        Assert.Null(HousingTravel.Find("Limsa Lominsa", "8pm", null, null));
    }

    [Fact]
    public void FeedbackLinksPrefillAGitHubIssueWithoutLabels()
    {
        var feature = GitHubFeedback.FeatureUrl("0.1.24");
        var error = GitHubFeedback.ErrorUrl("0.1.24");
        Assert.StartsWith("https://github.com/ibigsnet/shout-calendar/issues/new?", feature, StringComparison.Ordinal);
        Assert.Contains("title=Feature%20request", feature, StringComparison.Ordinal);
        Assert.Contains("Shout%20Calendar%200.1.24", feature, StringComparison.Ordinal);
        Assert.Contains("title=Error%20report", error, StringComparison.Ordinal);
        Assert.Contains("What%20happened", error, StringComparison.Ordinal);
        Assert.DoesNotContain("labels=", feature, StringComparison.Ordinal);
        Assert.DoesNotContain("labels=", error, StringComparison.Ordinal);
        Assert.True(LinkFinder.IsHttp(feature));
        Assert.True(LinkFinder.IsHttp(error));
    }

    [Fact]
    public void NearbyPlayersSkipTheTeleportAndFarOnesDoNot()
    {
        Assert.True(TravelNear.Skip(true, playerToTarget: 1f, aetheryteToTarget: 10f));
        Assert.False(TravelNear.Skip(true, playerToTarget: 20f, aetheryteToTarget: 10f));
        Assert.False(TravelNear.Skip(false, playerToTarget: 1f, aetheryteToTarget: 10f));
        Assert.True(TravelNear.Skip(true, playerToTarget: 4f, aetheryteToTarget: float.PositiveInfinity));
        Assert.Equal(341u, HousingTravel.Find(null, "Goblet W21 P35", 21, "Golem")!.Value.WardTerritoryId);
        Assert.Equal(130u, HousingTravel.Find(null, "Goblet W21 P35", 21, "Golem")!.Value.CityTerritoryId);
        Assert.Contains("Visit Another World Server", DataCenters.TravelLine("Zalera", "Diabolos"), StringComparison.Ordinal);
        Assert.Contains("Visit Another Data Center", DataCenters.TravelLine("Cuchulainn", "Diabolos"), StringComparison.Ordinal);
        Assert.DoesNotContain("Foundation", DataCenters.TravelLine("Golem", "Rafflesia"), StringComparison.Ordinal);
    }
}
