using ShoutCalendar.Core;

var shoutAt = new DateTimeOffset(2026, 9, 26, 15, 0, 0, TimeSpan.Zero);
const string text = "Hunt train 8:00pm to 10:00pm ward 18 plot 5 on Faerie";

string? previous = null;
for (var run = 1; run <= 2; run++)
{
    var session = new CalendarSession(DateOnly.FromDateTime(shoutAt.UtcDateTime));
    var added = session.TryAddShout(text, ShoutHarvest.ShoutChannel, shoutAt);
    var day = session.CurrentMonth().OnDay(shoutAt.UtcDateTime.Day);
    if (!added || day.Count != 1)
    {
        Console.Error.WriteLine($"run {run} did not harvest one entry");
        return 1;
    }

    var entry = day[0];
    var end = entry.End is TimeOnly clock ? clock.ToString("HH:mm") : "";
    var block = string.Join(
        '\n',
        $"run: {run}",
        $"time: {entry.Time?.ToString("HH:mm")}-{end}",
        $"place: {entry.Place}",
        $"event: {entry.EventText}",
        $"date: {entry.Date?.ToString("yyyy-MM-dd")}",
        $"ward: {entry.Ward}",
        $"server: {entry.Server}");
    Console.WriteLine(block);
    if (previous is not null && previous != block.Replace("run: 2", "run: 1"))
    {
        Console.Error.WriteLine("runs differ");
        return 1;
    }

    previous = block;
}

Console.WriteLine("identical: true");
return 0;
