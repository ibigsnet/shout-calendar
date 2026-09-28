using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

/// <summary>Breaks a shout into short lines for reading. The stored note is unchanged.</summary>
public static class NoteLayout
{
    private static readonly Regex Sections = new(@"\s+[|/]\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Sentences = new(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<string> Lines(string? text)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return lines;
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            foreach (var section in Sections.Split(raw))
            {
                var bit = section.Trim();
                if (bit.Length == 0)
                    continue;
                if (bit.Length < 90)
                {
                    lines.Add(bit);
                    continue;
                }

                foreach (var sentence in Sentences.Split(bit))
                {
                    var line = sentence.Trim();
                    if (line.Length > 0)
                        lines.Add(line);
                }
            }
        }

        return lines;
    }
}
