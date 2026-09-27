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
