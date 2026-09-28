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
        Assert.NotNull(spot);
        Assert.Equal("The Goblet", spot.Value.District);
        Assert.Equal(7, spot.Value.Ward);
        Assert.Equal(5, spot.Value.Plot);
        Assert.Equal("Zalera", spot.Value.World);
        Assert.Null(HousingTravel.Find("Limsa Lominsa", "8pm", null, null));
    }
}
