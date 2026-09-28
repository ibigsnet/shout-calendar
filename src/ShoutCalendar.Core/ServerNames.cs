using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

/// <summary>
/// English world and data-center names used as a place. Chinese and Korean
/// worlds are not in this list; that choice is an open question.
/// </summary>
public static class ServerNames
{
    public static readonly IReadOnlyList<string> All =
    [
        "Adamantoise", "Cactuar", "Faerie", "Gilgamesh", "Jenova", "Midgardsormr", "Sargatanas", "Siren",
        "Behemoth", "Excalibur", "Exodus", "Famfrit", "Hyperion", "Lamia", "Leviathan", "Ultros",
        "Balmung", "Brynhildr", "Coeurl", "Diabolos", "Goblin", "Malboro", "Mateus", "Zalera",
        "Cuchulainn", "Golem", "Halicarnassus", "Kraken", "Maduin", "Marilith", "Rafflesia", "Seraph",
        "Cerberus", "Louisoix", "Moogle", "Omega", "Phantom", "Ragnarok", "Sagittarius", "Spriggan",
        "Alpha", "Lich", "Odin", "Phoenix", "Raiden", "Shiva", "Twintania", "Zodiark",
        "Aegis", "Atomos", "Carbuncle", "Garuda", "Gungnir", "Kujata", "Tonberry", "Typhon",
        "Alexander", "Bahamut", "Durandal", "Fenrir", "Ifrit", "Ridill", "Tiamat", "Ultima",
        "Anima", "Asura", "Chocobo", "Hades", "Ixion", "Masamune", "Pandaemonium", "Titan",
        "Belias", "Mandragora", "Ramuh", "Shinryu", "Unicorn", "Valefor", "Yojimbo", "Zeromus",
        "Bismarck", "Ravana", "Sephirot", "Sophia", "Zurvan",
        "Aether", "Primal", "Crystal", "Dynamis", "Chaos", "Light", "Elemental", "Gaia", "Mana", "Meteor", "Materia",
    ];

    private static readonly (string Alias, string Name)[] ShortNames =
    [
        ("Dyn", "Dynamis"),
        ("Krak", "Kraken"),
        ("Bryn", "Brynhildr"),
        ("Crys", "Crystal"),
        ("Raff", "Rafflesia"),
    ];

    private static readonly Dictionary<string, string> CanonicalByFold = BuildCanonical();

    private static readonly Regex Pattern = new(
        @"\b(?:" + string.Join("|", Tokens().OrderByDescending(name => name.Length).Select(Regex.Escape)) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<string> Match(string text)
    {
        var found = new List<string>();
        foreach (Match match in Pattern.Matches(text))
        {
            if (!CanonicalByFold.TryGetValue(match.Value, out var canonical))
                continue;
            if (!found.Contains(canonical))
                found.Add(canonical);
        }

        return found;
    }

    private static Dictionary<string, string> BuildCanonical()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in All)
            map[name] = name;
        foreach (var (alias, name) in ShortNames)
            map[alias] = name;
        return map;
    }

    private static IEnumerable<string> Tokens()
    {
        foreach (var name in All)
            yield return name;
        foreach (var (alias, _) in ShortNames)
            yield return alias;
    }
}
