namespace ShoutCalendar.Core;

/// <summary>Turns a chat timestamp into a real instant. A missing or zero stamp is not 1970.</summary>
public static class ChatTime
{
    public static DateTimeOffset FromUnixOrNow(long timestamp, DateTimeOffset now)
    {
        try
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(timestamp);
            if (when.UtcDateTime.Year is >= 2000 and <= 2100)
                return when;
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return now;
    }
}
