using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class ScheduleTests
{
    private static readonly TimeZoneInfo Central =
        TimeZoneInfo.CreateCustomTimeZone("test-ct", TimeSpan.FromHours(-5), "CT", "CT");

    [Fact]
    public void NorthAmericaCactpotLandsOnSaturdayEvening()
    {
        var marks = GameSchedule.InMonth(2026, 9, Central, "na", new HashSet<string> { GameSchedule.Cactpot });
        var drawing = marks.Single(mark => mark.LocalStart == new DateTime(2026, 9, 26, 21, 0, 0));

        Assert.Equal(ResetTone.Cactus, drawing.Tone);
        Assert.Equal(drawing.StartDate, drawing.EndDate);
    }

    [Fact]
    public void EuropeCactpotUsesTheEuropeClock()
    {
        var marks = GameSchedule.InMonth(2026, 9, Central, "eu", new HashSet<string> { GameSchedule.Cactpot });

        Assert.Contains(marks, mark => mark.LocalStart == new DateTime(2026, 9, 26, 14, 0, 0));
    }

    [Fact]
    public void WeeklyResetIsTuesdayMorningLocal()
    {
        var marks = GameSchedule.InMonth(2026, 9, Central, "na", new HashSet<string> { GameSchedule.Weekly });
        var reset = marks.Single(mark => mark.LocalStart.Day == 29);

        Assert.Equal(new DateTime(2026, 9, 29, 3, 0, 0), reset.LocalStart);
        Assert.Equal(ResetTone.Crystal, reset.Tone);
    }

    [Fact]
    public void NocturneSpansTheLocalEventWindow()
    {
        var marks = GameSchedule.InMonth(2026, 9, Central, "na", new HashSet<string> { GameSchedule.Nocturne });
        var nocturne = Assert.Single(marks);

        Assert.Equal(new DateTime(2026, 9, 24, 3, 0, 0), nocturne.LocalStart);
        Assert.Equal(new DateOnly(2026, 10, 13), nocturne.EndDate);
        Assert.Equal(ResetTone.Event, nocturne.Tone);

        var month = CalendarMonth.Create(2026, 9, Array.Empty<CalendarEntry>());
        var segments = GameSchedule.Segments(month, nocturne.StartDate, nocturne.EndDate);
        Assert.True(segments.Count >= 2);
        Assert.Equal(6, segments[0].LastColumn);
        Assert.Equal(0, segments[^1].FirstColumn);
    }

    [Fact]
    public void WondrousTailsUsesTheTuesdayReset()
    {
        var marks = GameSchedule.InMonth(2026, 9, Central, "na", new HashSet<string> { GameSchedule.Tails });
        var tails = marks.Single(mark => mark.LocalStart.Day == 29);

        Assert.Equal(new DateTime(2026, 9, 29, 3, 0, 0), tails.LocalStart);
        Assert.Contains("Khloe", tails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOldDefaultListGainsWondrousTails()
    {
        var saved = new[] { GameSchedule.Cactpot, GameSchedule.Weekly, GameSchedule.Nocturne };
        var merged = GameSchedule.MergeSaved(saved).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(GameSchedule.Tails, merged);
    }

    [Fact]
    public void ATurnedOffResetIsAbsent()
    {
        var marks = GameSchedule.InMonth(2026, 9, Central, "na", new HashSet<string> { GameSchedule.Weekly });

        Assert.DoesNotContain(marks, mark => mark.Id == GameSchedule.Daily);
        Assert.DoesNotContain(marks, mark => mark.Id == GameSchedule.Cactpot);
    }
}