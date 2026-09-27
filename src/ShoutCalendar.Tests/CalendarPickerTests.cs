using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class CalendarPickerTests
{
    [Fact]
    public void TheYearListCentersOnTheCurrentYear()
    {
        var years = CalendarPicker.Years(2026, 2026);
        Assert.Equal(2026, years[years.Count / 2]);
        Assert.Equal(years.Count / 2, CalendarPicker.ScrollIndex(years, 2026));

        var wide = CalendarPicker.Years(2026, 1800);
        Assert.Contains(1800, wide);
        Assert.Equal(2026, wide[CalendarPicker.ScrollIndex(wide, 2026)]);
    }
}
