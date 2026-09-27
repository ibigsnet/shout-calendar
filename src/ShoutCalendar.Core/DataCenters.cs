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
}
