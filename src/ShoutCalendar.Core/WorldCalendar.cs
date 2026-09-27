namespace ShoutCalendar.Core;

/// <summary>Playable worlds from <see cref="ServerNames"/>, without data-center names.</summary>
public static class PlayableWorlds
{
    private static readonly HashSet<string> DataCenters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Aether", "Primal", "Crystal", "Dynamis", "Chaos", "Light",
        "Elemental", "Gaia", "Mana", "Meteor", "Materia",
    };

    public static IReadOnlyList<string> All { get; } =
        ServerNames.All.Where(name => !DataCenters.Contains(name)).ToArray();

    public static bool TryCanonical(string? name, out string canonical)
    {
        canonical = "";
        if (string.IsNullOrWhiteSpace(name))
            return false;
        foreach (var world in All)
        {
            if (!world.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            canonical = world;
            return true;
        }

        return false;
    }
}

/// <summary>
/// The world the client is on is always selected at first and always fetched.
/// Other worlds are fetched only after they are checked, and only a checked world can be shown.
/// </summary>
public sealed class WorldCalendar
{
    private readonly HashSet<string> extras = new(StringComparer.Ordinal);

    public WorldCalendar(string currentWorld)
    {
        if (!PlayableWorlds.TryCanonical(currentWorld, out var home))
            throw new ArgumentException("The current world is not a playable world.", nameof(currentWorld));
        this.Home = home;
        this.Selected = home;
    }

    public string Home { get; }

    public string Selected { get; private set; }

    public bool IsChecked(string world)
    {
        if (!PlayableWorlds.TryCanonical(world, out var canonical))
            return false;
        return canonical == this.Home || this.extras.Contains(canonical);
    }

    public void SetChecked(string world, bool on)
    {
        if (!PlayableWorlds.TryCanonical(world, out var canonical) || canonical == this.Home)
            return;
        if (on)
        {
            this.extras.Add(canonical);
            return;
        }

        this.extras.Remove(canonical);
        if (this.Selected == canonical)
            this.Selected = this.Home;
    }

    public IReadOnlyList<string> Extras()
    {
        var list = new List<string>();
        foreach (var world in PlayableWorlds.All)
        {
            if (this.extras.Contains(world))
                list.Add(world);
        }

        return list;
    }

    public IReadOnlyList<string> Selectable()
    {
        var list = new List<string> { this.Home };
        list.AddRange(this.Extras());
        return list;
    }

    public IReadOnlyList<string> Fetched() => this.Selectable();

    public bool Select(string world)
    {
        if (!PlayableWorlds.TryCanonical(world, out var canonical))
            return false;
        if (!this.Selectable().Contains(canonical, StringComparer.Ordinal))
            return false;
        this.Selected = canonical;
        return true;
    }

    public IReadOnlyList<SyncAnnouncement> Visible(IEnumerable<SyncAnnouncement> events) =>
        events.Where(item => item.World.Equals(this.Selected, StringComparison.OrdinalIgnoreCase)).ToArray();

    public void ClearExtras()
    {
        this.extras.Clear();
        this.Selected = this.Home;
    }
}
