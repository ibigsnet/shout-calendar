namespace ShoutCalendar.Core;

/// <summary>
/// Place names supplied by the caller. The plugin fills this from the game's PlaceName rows
/// that are referenced by zones, maps, and aetherytes.
/// </summary>
public sealed class PlaceCatalog
{
    public static PlaceCatalog Empty { get; } = new([]);

    private readonly string[] namesLongestFirst;

    public PlaceCatalog(IEnumerable<string> names)
    {
        this.namesLongestFirst = names
            .Select(name => name.Trim())
            .Where(name => name.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(name => name.Length)
            .ToArray();
    }

    public int Count => this.namesLongestFirst.Length;

    public IReadOnlyList<string> Match(string text)
    {
        var found = new List<string>();
        var taken = new List<(int Start, int End)>();
        foreach (var name in this.namesLongestFirst)
        {
            var index = 0;
            while (index < text.Length)
            {
                var at = text.IndexOf(name, index, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                    break;
                var end = at + name.Length;
                var leftFree = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
                var rightFree = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                var overlaps = taken.Any(span => at < span.End && end > span.Start);
                if (leftFree && rightFree && !overlaps)
                {
                    found.Add(name);
                    taken.Add((at, end));
                    break;
                }

                index = at + 1;
            }
        }

        return found;
    }
}
