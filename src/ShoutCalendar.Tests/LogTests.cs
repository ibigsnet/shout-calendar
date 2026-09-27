using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class LogTests
{
    [Fact]
    public void ShoutLineInABinaryLogIsHarvestedAndOtherChannelsAreNot()
    {
        var shoutAt = new DateTimeOffset(2026, 9, 26, 16, 0, 0, TimeSpan.Zero);
        var unix = (uint)shoutAt.ToUnixTimeSeconds();
        var token = new byte[] { 0x02, 0x13, 0x02, 0xEC, 0x03 };
        var message = LogFixture.Utf8("S rank ").Concat(token).Concat(LogFixture.Utf8("at 8:00pm ward 13")).ToArray();
        var say = LogFixture.Entry(unix, 0x01, 14, "Tester", LogFixture.Utf8("Maps at 8:00pm ward 13"));
        var shout = LogFixture.Entry(unix, 0x02, 0x0B, "Tester", message);
        var noPlace = LogFixture.Entry(unix, 0x02, 0x0B, "", LogFixture.Utf8("hello there"));
        var file = LogFixture.File(0, say, shout, noPlace);

        var lines = ChatLogReader.Read(file);
        Assert.Equal(3, lines.Count);
        Assert.Equal(unix, lines[1].TimestampUnix);
        Assert.Equal(0x02, lines[1].Filter);
        Assert.Equal(0x0B, lines[1].Channel);
        Assert.Equal("Tester", lines[1].Sender);
        Assert.Equal("S rank at 8:00pm ward 13", lines[1].Message);
        Assert.Equal("", lines[2].Sender);

        var harvested = ShoutHarvest.HarvestLog(file);
        var entry = Assert.Single(harvested);
        Assert.Equal(new DateOnly(2026, 9, 26), entry.Date);
        Assert.Equal(13, entry.Ward);
        Assert.Equal(new TimeOnly(20, 0), entry.Time);
        Assert.Equal("S rank at 8:00pm ward 13", entry.EventText);
        Assert.Null(entry.End);
        Assert.False(OngoingCheck.IsOngoing(entry, shoutAt));
    }

    [Fact]
    public void HeaderBeginAndEndCountLinesWhenBeginIsNotZero()
    {
        var shoutAt = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var entry = LogFixture.Entry((uint)shoutAt.ToUnixTimeSeconds(), 0x00, 0x0B, "Tester", LogFixture.Utf8("19:45 Faerie"));
        var lines = ChatLogReader.Read(LogFixture.File(5, entry));

        var line = Assert.Single(lines);
        Assert.Equal("19:45 Faerie", line.Message);
        var harvested = Assert.Single(ShoutHarvest.HarvestLog(LogFixture.File(5, entry)));
        Assert.Equal("Faerie", harvested.Server);
        Assert.Equal(new TimeOnly(19, 45), harvested.Time);
    }

    [Fact]
    public void EmptyAndTruncatedLogsAddNothing()
    {
        Assert.Empty(ChatLogReader.Read(ReadOnlySpan<byte>.Empty));
        Assert.Empty(ChatLogReader.Read(new byte[] { 1, 0, 0, 0 }));
        Assert.Empty(ShoutHarvest.HarvestLog(ReadOnlySpan<byte>.Empty));
    }
}
