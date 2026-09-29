using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class AlarmTests
{
    private static readonly DateTime At = new(2026, 9, 27, 18, 30, 10);

    [Fact]
    public void TheFirstMinuteAfterStartupDoesNotRing()
    {
        var hits = EventAlarm.Due([AcceptedAt(At)], At, null, true, false, 0);

        Assert.Empty(hits);
    }

    [Fact]
    public void TheSameMinuteDoesNotRingAgain()
    {
        var hits = EventAlarm.Due([AcceptedAt(At)], At, EventAlarm.MinuteOf(At), true, false, 0);

        Assert.Empty(hits);
    }

    [Fact]
    public void AnAcceptedEventRingsAtItsClock()
    {
        var previous = EventAlarm.MinuteOf(At).AddMinutes(-1);
        var hits = EventAlarm.Due([AcceptedAt(At)], At, previous, true, false, 0);

        var hit = Assert.Single(hits);
        Assert.True(hit.Accepted);
        Assert.Equal("accepted", hit.Id);
    }

    [Fact]
    public void APendingEventStaysQuietUntilThatAlarmIsOn()
    {
        var previous = EventAlarm.MinuteOf(At).AddMinutes(-1);
        var pending = AcceptedAt(At) with { Accepted = false, Id = "pending" };

        Assert.Empty(EventAlarm.Due([pending], At, previous, true, false, 0));

        var hits = EventAlarm.Due([pending], At, previous, true, true, 0);
        var hit = Assert.Single(hits);
        Assert.False(hit.Accepted);
    }

    [Fact]
    public void MinutesBeforeMovesTheRingEarlier()
    {
        var start = At;
        var early = start.AddMinutes(-15);
        var previous = EventAlarm.MinuteOf(early).AddMinutes(-1);

        var hits = EventAlarm.Due([AcceptedAt(start)], early, previous, true, false, 15);

        Assert.Single(hits);
        Assert.Empty(EventAlarm.Due([AcceptedAt(start)], start, EventAlarm.MinuteOf(start).AddMinutes(-1), true, false, 15));
        Assert.Single(EventAlarm.Due([AcceptedAt(start)], start, EventAlarm.MinuteOf(start).AddMinutes(-1), true, false, 0));
        Assert.True(EventAlarm.IsStartMinute(AcceptedAt(start), start));
        Assert.Contains("is starting", AlarmNotice.Line(AcceptedAt(start), 0), StringComparison.Ordinal);
        var veil = AcceptedAt(start) with
        {
            Server = "Famfrit",
            Place = "The Lavender Beds ward 25 plot 36",
            Ward = 25,
            EventText = "\uE084\uE078\uE075 \uE086\uE075\uE079\uE07C tonight",
        };
        var hop = AlarmNotice.Line(veil, 15, "Diabolos");
        Assert.Contains("THE VEIL", hop, StringComparison.Ordinal);
        Assert.True(hop.IndexOf("THE VEIL", StringComparison.Ordinal) < hop.IndexOf("Server hop", StringComparison.Ordinal));
        Assert.Contains("Server hop to Famfrit (Primal) first", hop, StringComparison.Ordinal);
        Assert.Contains("Visit Another Data Center", hop, StringComparison.Ordinal);
        Assert.Contains("Teleport: New Gridania aetheryte", hop, StringComparison.Ordinal);
        Assert.Equal(
            "PURE BASSMENT",
            EventTitle.Readable("\uE080\uE085\uE082\uE075 \uE072\uE071\uE083\uE083\uE07D\uE075\uE07E\uE084\uE03C \uE06F"));
    }

    [Fact]
    public void TwoInvitesDueOnTheSameMinuteShareOneChatLine()
    {
        var veil = AcceptedAt(At) with
        {
            Id = "veil",
            Server = "Famfrit",
            Place = "The Lavender Beds ward 25 plot 36",
            Ward = 25,
            EventText = "\uE084\uE078\uE075 \uE086\uE075\uE079\uE07C tonight",
        };
        var copy = veil with { Id = "sync-copy" };
        var other = AcceptedAt(At) with
        {
            Id = "solace",
            Accepted = false,
            Server = "Mateus",
            Place = "Mist ward 18 plot 46",
            Ward = 18,
            EventText = "\u2605 SOLACE \u2605 open tonight ward 18",
        };
        var unique = AlarmNotice.Dedupe(
        [
            new AlarmNotice.Ring(veil, 15, true),
            new AlarmNotice.Ring(copy, 15, true),
            new AlarmNotice.Ring(other, 15, false),
        ]);

        Assert.Equal(2, unique.Count);
        var text = AlarmNotice.Broadcast(unique, "Diabolos");
        Assert.Equal(1, text.Split("Shout Calendar:").Length - 1);
        Assert.Contains("THE VEIL", text, StringComparison.Ordinal);
        Assert.Contains("SOLACE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("open tonight", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Server hop", AlarmNotice.Broadcast([unique[0]], "Diabolos"), StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptingACurrentOrPastEventIsAlreadyDue()
    {
        var start = At;
        var accepted = AcceptedAt(start);

        Assert.True(EventAlarm.AlreadyDue(accepted, start.AddMinutes(5), 15));
        Assert.True(EventAlarm.AlreadyDue(accepted, start.AddMinutes(-5), 15));
        Assert.False(EventAlarm.AlreadyDue(accepted, start.AddMinutes(-20), 15));
        Assert.False(EventAlarm.AlreadyDue(accepted, start.AddMinutes(20), 15));
        Assert.False(EventAlarm.AlreadyDue(accepted, start.AddDays(1), 15));
        Assert.False(EventAlarm.AlreadyDue(accepted with { Time = null }, start, 15));
    }

    [Fact]
    public void AMissingClockDoesNotRing()
    {
        var entry = AcceptedAt(At) with { Time = null };
        var hits = EventAlarm.Due([entry], At, EventAlarm.MinuteOf(At).AddMinutes(-1), true, false, 0);

        Assert.Empty(hits);
    }

    [Fact]
    public void SoundChoicesStayInsideTheChatRange()
    {
        Assert.Equal(1, EventAlarm.ClampSound(0));
        Assert.Equal(1, EventAlarm.ClampSound(1));
        Assert.Equal(16, EventAlarm.ClampSound(16));
        Assert.Equal(1, EventAlarm.ClampSound(17));
    }

    private static CalendarEntry AcceptedAt(DateTime start) => new(
        DateOnly.FromDateTime(start),
        TimeOnly.FromDateTime(start),
        null,
        3,
        null,
        "ward 3",
        "maps",
        "Host",
        true,
        "accepted",
        start);
}
