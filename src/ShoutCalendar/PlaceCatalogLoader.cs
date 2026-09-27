using Lumina.Excel;
using Lumina.Excel.Sheets;
using ShoutCalendar.Core;

namespace ShoutCalendar;

/// <summary>
/// Place names from the game's own sheets: zones, maps, and aetherytes.
/// </summary>
internal static class PlaceCatalogLoader
{
    public static PlaceCatalog Load(Dalamud.Plugin.Services.IDataManager data)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in data.GetExcelSheet<TerritoryType>())
        {
            Add(names, row.PlaceName);
            Add(names, row.PlaceNameZone);
            Add(names, row.PlaceNameRegion);
        }

        foreach (var row in data.GetExcelSheet<Aetheryte>())
        {
            Add(names, row.PlaceName);
            Add(names, row.AethernetName);
        }

        foreach (var row in data.GetExcelSheet<Map>())
        {
            Add(names, row.PlaceName);
            Add(names, row.PlaceNameRegion);
            Add(names, row.PlaceNameSub);
        }

        return new PlaceCatalog(names);
    }

    private static void Add(HashSet<string> names, RowRef<PlaceName> place)
    {
        if (place.RowId == 0)
            return;
        var text = place.Value.Name.ExtractText().Trim();
        if (text.Length >= 4)
            names.Add(text);
    }
}
