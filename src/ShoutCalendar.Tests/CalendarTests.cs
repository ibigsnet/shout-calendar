using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class CalendarTests
{
    [Fact]
    public void PagingShowsTheEntryOnItsDayAndNotOnAnotherDay()
    {
        var shoutAt = new DateTimeOffset(2026, 9, 26, 18, 0, 0, TimeSpan.Zero);
        var session = new CalendarSession(new DateOnly(2026, 9, 1));
        Assert.True(session.TryAddShout("8:00pm ward 13 on Faerie", ShoutHarvest.ShoutChannel, shoutAt));

        var september = session.CurrentMonth();
        Assert.Equal(2026, september.Year);
        Assert.Equal(9, september.Month);
        Assert.Single(september.OnDay(26));
        Assert.Empty(september.OnDay(27));
        Assert.Empty(september.OnDay(1));

        var dayCell = Assert.Single(september.Cells, cell => cell.Day == 26);
        Assert.Single(dayCell.Entries);
        var otherCell = Assert.Single(september.Cells, cell => cell.Day == 27);
        Assert.Empty(otherCell.Entries);

        Assert.False(september.OnDay(26)[0].Accepted);

        var october = session.Page(1);
        Assert.Equal(2026, october.Year);
        Assert.Equal(10, october.Month);
        Assert.Empty(october.OnDay(26));
        Assert.Empty(october.Cells.Where(cell => cell.Day == 26).SelectMany(cell => cell.Entries));
    }

    [Fact]
    public void MonthGridUsesTheCalendarLengthAndSundayLead()
    {
        var month = CalendarMonth.Create(2026, 9, Array.Empty<CalendarEntry>());
        var dayCells = month.Cells.Where(cell => cell.Day is not null).ToList();

        Assert.Equal(DateTime.DaysInMonth(2026, 9), dayCells.Count);
        Assert.Equal(1, dayCells[0].Day);
        Assert.Equal(30, dayCells[^1].Day);
        Assert.Equal((int)new DateOnly(2026, 9, 1).DayOfWeek, month.Cells.TakeWhile(cell => cell.Day is null).Count());
        Assert.Equal(0, month.Cells.Count % 7);
    }

    [Fact]
    public void PagingRollsTheYear()
    {
        var month = CalendarMonth.Create(2026, 12, Array.Empty<CalendarEntry>());
        var next = month.Page(1);
        var previous = month.Page(-1);

        Assert.Equal(new DateOnly(2027, 1, 1), new DateOnly(next.Year, next.Month, 1));
        Assert.Equal(new DateOnly(2026, 11, 1), new DateOnly(previous.Year, previous.Month, 1));
    }
}
