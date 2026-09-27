namespace ShoutCalendar.Core;

/// <summary>
/// One short name for a shout. Boxed letters win, then a marked name, then the category.
/// </summary>
public static class EventTitle
{
    public static string Choose(string? text, string? category = null)
    {
        var boxed = Boxed(text);
        if (boxed.Length > 0)
            return boxed;
        var marked = Marked(text);
        if (marked.Length > 0)
            return marked;
        return string.IsNullOrWhiteSpace(category) ? "" : category.Trim();
    }

    private static string Boxed(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var phrases = new List<string>();
        var word = new System.Text.StringBuilder();
        var words = new List<string>();
        void FlushWord()
        {
            if (word.Length == 0)
                return;
            words.Add(word.ToString());
            word.Clear();
        }

        void FlushPhrase()
        {
            FlushWord();
            if (words.Count == 0)
                return;
            phrases.Add(string.Join(' ', words));
            words.Clear();
        }

        foreach (var ch in text)
        {
            if (ch is >= '\uE071' and <= '\uE08A')
            {
                word.Append((char)('A' + (ch - '\uE071')));
                continue;
            }

            if (ch is '\'' or '\u2019')
            {
                if (word.Length > 0)
                    word.Append('\'');
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch is >= '\uE000' and <= '\uF8FF')
            {
                FlushWord();
                continue;
            }

            FlushPhrase();
        }

        FlushPhrase();
        foreach (var phrase in phrases)
        {
            if (phrase.Contains(' ', StringComparison.Ordinal))
                return Pretty(phrase);
        }

        foreach (var phrase in phrases)
        {
            if (phrase.Length >= 4)
                return Pretty(phrase);
        }

        return "";
    }

    private static string Marked(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var stars = System.Text.RegularExpressions.Regex.Match(text, @"★\s*([^★\r\n]{2,40}?)\s*★");
        if (stars.Success)
            return stars.Groups[1].Value.Trim();
        var corners = System.Text.RegularExpressions.Regex.Match(text, @"【\s*([^】\r\n]{2,40}?)\s*】");
        return corners.Success ? corners.Groups[1].Value.Trim() : "";
    }

    private static string Pretty(string upper)
    {
        var parts = upper.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var word = parts[i].ToLowerInvariant();
            if (word.Length == 0)
                continue;
            word = char.ToUpperInvariant(word[0]) + word[1..];
            var mark = word.IndexOf('\'');
            if (mark > 0 && mark < word.Length - 1)
                word = word[..(mark + 1)] + char.ToLowerInvariant(word[mark + 1]) + word[(mark + 2)..];
            parts[i] = word;
        }

        return string.Join(' ', parts);
    }
}
