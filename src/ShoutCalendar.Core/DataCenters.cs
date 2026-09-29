namespace ShoutCalendar.Core;

/// <summary>Playable worlds grouped under their data center.</summary>
public static class DataCenters
{
    public static readonly IReadOnlyList<Group> All =
    [
        new("Aether", ["Adamantoise", "Cactuar", "Faerie", "Gilgamesh", "Jenova", "Midgardsormr", "Sargatanas", "Siren"]),
        new("Primal", ["Behemoth", "Excalibur", "Exodus", "Famfrit", "Hyperion", "Lamia", "Leviathan", "Ultros"]),
        new("Crystal", ["Balmung", "Brynhildr", "Coeurl", "Diabolos", "Goblin", "Malboro", "Mateus", "Zalera"]),
        new("Dynamis", ["Cuchulainn", "Golem", "Halicarnassus", "Kraken", "Maduin", "Marilith", "Rafflesia", "Seraph"]),
        new("Chaos", ["Cerberus", "Louisoix", "Moogle", "Omega", "Phantom", "Ragnarok", "Sagittarius", "Spriggan"]),
        new("Light", ["Alpha", "Lich", "Odin", "Phoenix", "Raiden", "Shiva", "Twintania", "Zodiark"]),
        new("Elemental", ["Aegis", "Atomos", "Carbuncle", "Garuda", "Gungnir", "Kujata", "Tonberry", "Typhon"]),
        new("Gaia", ["Alexander", "Bahamut", "Durandal", "Fenrir", "Ifrit", "Ridill", "Tiamat", "Ultima"]),
        new("Mana", ["Anima", "Asura", "Chocobo", "Hades", "Ixion", "Masamune", "Pandaemonium", "Titan"]),
        new("Meteor", ["Belias", "Mandragora", "Ramuh", "Shinryu", "Unicorn", "Valefor", "Yojimbo", "Zeromus"]),
        new("Materia", ["Bismarck", "Ravana", "Sephirot", "Sophia", "Zurvan"]),
    ];

    public readonly record struct Group(string Name, IReadOnlyList<string> Worlds);

    public static bool TryGroup(string? world, out string group)
    {
        group = "";
        if (string.IsNullOrWhiteSpace(world))
            return false;
        foreach (var center in All)
        {
            foreach (var name in center.Worlds)
            {
                if (!name.Equals(world.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                group = center.Name;
                return true;
            }
        }

        return false;
    }

    public static bool SameCenter(string? left, string? right) =>
        TryGroup(left, out var first) && TryGroup(right, out var second) && first.Equals(second, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Same data center: World Visit is the main aetheryte in Limsa Lominsa, Gridania, or Ul'dah.
    /// Another data center is the character list, not an aetheryte.
    /// </summary>
    public static string TravelLine(string needed, string current)
    {
        if (SameCenter(needed, current) && TryGroup(needed, out var center))
            return $"that plot is on {needed}. You are on {current} ({center}). Use the main aetheryte in Limsa Lominsa, Gridania, or Ul'dah and choose Visit Another World Server. City shards and other cities cannot switch worlds.";
        var from = TryGroup(current, out var here) ? $"{current} ({here})" : current;
        var to = TryGroup(needed, out var there) ? $"{needed} ({there})" : needed;
        return $"that plot is on {to}. You are on {from}. Log out to the character list and choose Visit Another Data Center.";
    }
}
