using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class PlaceTests
{
    private static readonly DateTimeOffset ShoutAt = new(2026, 9, 26, 18, 0, 0, TimeSpan.Zero);
    private static readonly PlaceCatalog Places = new(
    [
        "Limsa Lominsa",
        "Ul'dah",
        "The Goblet",
        "The Lavender Beds",
        "Western Thanalan",
        "Silver Bazaar",
    ]);

    [Fact]
    public void AZoneNameIsAPlaceWithoutAWard()
    {
        var entry = ShoutHarvest.TryHarvest("8:00pm at Limsa Lominsa", ShoutHarvest.ShoutChannel, ShoutAt, Places);

        Assert.NotNull(entry);
        Assert.Null(entry.Ward);
        Assert.Contains("Limsa Lominsa", entry.Place);
    }

    [Fact]
    public void ApostropheNamesMatch()
    {
        var entry = ShoutHarvest.TryHarvest("9pm Ul'dah", ShoutHarvest.ShoutChannel, ShoutAt, Places);

        Assert.NotNull(entry);
        Assert.Contains("Ul'dah", entry.Place);
    }

    [Fact]
    public void MapCoordinatesAreAPlace()
    {
        var entry = ShoutHarvest.TryHarvest("meet at 8pm (12.4, 8.1)", ShoutHarvest.ShoutChannel, ShoutAt, Places);

        Assert.NotNull(entry);
        Assert.Equal("x 12.4, y 8.1", entry.Place);
    }

    [Fact]
    public void WardAndPlotShorthandAreKeptWithTheZone()
    {
        var entry = ShoutHarvest.TryHarvest("10pm The Goblet W22 P35", ShoutHarvest.ShoutChannel, ShoutAt, Places);

        Assert.NotNull(entry);
        Assert.Equal(22, entry.Ward);
        Assert.Contains("plot 35", entry.Place);
        Assert.Contains("The Goblet", entry.Place);
    }

    [Fact]
    public void FreeCompanyDateAndWardShorthandAreKept()
    {
        const string text = "ASDFASDFASDF host  ing and NOT FREE COMPANY Brings you Chaseh On W3 Plot 27 on 10/13/26 bring your glmaour and.   gambleing,  death rool bring ayour friends";
        var entry = ShoutHarvest.TryHarvest(text, ShoutHarvest.FreeCompanyChannel, ShoutAt);

        Assert.NotNull(entry);
        Assert.Equal(new DateOnly(2026, 10, 13), entry.Date);
        Assert.Equal(3, entry.Ward);
        Assert.Contains("plot 27", entry.Place);
        Assert.Null(entry.Time);
        Assert.False(entry.Accepted);
    }

    [Fact]
    public void AMissingTimestampIsNot1970AndTomorrowIsTheNextDay()
    {
        var now = new DateTimeOffset(2026, 9, 27, 18, 0, 0, TimeSpan.Zero);
        Assert.Equal(now, ChatTime.FromUnixOrNow(0, now));
        Assert.Equal(now, ChatTime.FromUnixOrNow(23_400, now));

        var entry = ShoutHarvest.TryHarvest("tomorrow at 6:30pm W3", ShoutHarvest.ShoutChannel, now);
        Assert.NotNull(entry);
        Assert.Equal(new DateOnly(2026, 9, 28), entry.Date);
        Assert.Equal(new TimeOnly(18, 30), entry.Time);
        Assert.Equal(3, entry.Ward);
    }

    [Fact]
    public void PartyStaysOffUnlessChecked()
    {
        Assert.Null(ShoutHarvest.TryHarvest("W3 Plot 27 on 10/13/26", 14, ShoutAt));
    }

    [Fact]
    public void InstallDefaultsMatchTheCheckedChatsAndACheckedChannelIsListenedTo()
    {
        Assert.Equal([10, 11, 13, 24, 27, 30, 69], ChatChannels.DefaultIds.Order());
        Assert.Null(ShoutHarvest.TryHarvest("8pm W3 Plot 27", 14, ShoutAt));

        var party = new HashSet<int> { 14 };
        var entry = ShoutHarvest.TryHarvest("8pm W3 Plot 27", 14, ShoutAt, channels: party);

        Assert.NotNull(entry);
        Assert.Equal(3, entry.Ward);
    }

    [Fact]
    public void AShorterNameDoesNotStealTheLongerOne()
    {
        var catalog = new PlaceCatalog(["The Lavender Beds", "Lavender"]);
        var found = catalog.Match("party at The Lavender Beds");

        Assert.Equal(["The Lavender Beds"], found);
    }
}
