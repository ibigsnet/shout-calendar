namespace ShoutCalendar.Core;

/// <summary>The readable character name from a chat sender.</summary>
public static class SenderName
{
    public static string Clean(string? sender)
    {
        if (string.IsNullOrWhiteSpace(sender))
            return "";
        var kept = new char[sender.Length];
        for (var i = 0; i < sender.Length; i++)
        {
            var ch = sender[i];
            kept[i] = char.IsLetter(ch) || ch is ' ' or '\'' or '\u2019' or '-' ? ch : ' ';
        }

        var words = new string(kept).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 4 && words.Length % 2 == 0)
        {
            var half = words.Length / 2;
            var left = string.Join(' ', words[..half]);
            var right = string.Join(' ', words[half..]);
            if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
                return left;
        }

        return string.Join(' ', words);
    }
}
