using System.Globalization;

namespace ShoutCalendar.Core;

public sealed record MonthCell(int? Day, IReadOnlyList<CalendarEntry> Entries);

/// <summary>One month of the calendar grid. Paging returns another month over the same entries.</summary>
public sealed class CalendarMonth
{
    private CalendarMonth(int year, int month, IReadOnlyList<CalendarEntry> entries, IReadOnlyList<MonthCell> cells)
    {
        this.Year = year;
        this.Month = month;
        this.Entries = entries;
        this.Cells = cells;
    }

    public int Year { get; }

    public int Month { get; }

    public IReadOnlyList<CalendarEntry> Entries { get; }

    public IReadOnlyList<MonthCell> Cells { get; }

    public string Title => new DateOnly(this.Year, this.Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public static CalendarMonth Create(int year, int month, IReadOnlyList<CalendarEntry> entries)
    {
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month));

        var byDay = new Dictionary<int, List<CalendarEntry>>();
        foreach (var entry in entries)
        {
            if (entry.Repeat is null)
            {
                if (entry.Date is not DateOnly date || date.Year != year || date.Month != month)
                    continue;
                Add(byDay, date.Day, entry);
                continue;
            }

            if (entry.Date is null)
                continue;
            var daysInMonth = DateTime.DaysInMonth(year, month);
            for (var dayNumber = 1; dayNumber <= daysInMonth; dayNumber++)
            {
                if (EventRepeat.FallsOn(entry, new DateOnly(year, month, dayNumber)))
                    Add(byDay, dayNumber, entry);
            }
        }

        void Add(Dictionary<int, List<CalendarEntry>> days, int dayNumber, CalendarEntry item)
        {
            if (!days.TryGetValue(dayNumber, out var list))
            {
                list = new List<CalendarEntry>();
                days[dayNumber] = list;
            }

            list.Add(item);
        }

        var first = new DateOnly(year, month, 1);
        var lead = (int)first.DayOfWeek;
        var days = DateTime.DaysInMonth(year, month);
        var cells = new List<MonthCell>(lead + days + 6);
        for (var i = 0; i < lead; i++)
            cells.Add(new MonthCell(null, Array.Empty<CalendarEntry>()));
        for (var day = 1; day <= days; day++)
        {
            IReadOnlyList<CalendarEntry> onDay = byDay.TryGetValue(day, out var list)
                ? list
                : Array.Empty<CalendarEntry>();
            cells.Add(new MonthCell(day, onDay));
        }

        while (cells.Count % 7 != 0)
            cells.Add(new MonthCell(null, Array.Empty<CalendarEntry>()));

        return new CalendarMonth(year, month, entries, cells);
    }

    public CalendarMonth Page(int monthDelta)
    {
        var shifted = new DateOnly(this.Year, this.Month, 1).AddMonths(monthDelta);
        return Create(shifted.Year, shifted.Month, this.Entries);
    }

    public IReadOnlyList<CalendarEntry> OnDay(int day)
    {
        if (day < 1 || day > DateTime.DaysInMonth(this.Year, this.Month))
            return Array.Empty<CalendarEntry>();

        foreach (var cell in this.Cells)
        {
            if (cell.Day == day)
                return cell.Entries;
        }

        return Array.Empty<CalendarEntry>();
    }
}
