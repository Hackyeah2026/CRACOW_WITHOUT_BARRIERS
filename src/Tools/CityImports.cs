namespace Tools;

/// <param name="South">Zasięg importu miejsc (bbox): południe, zachód, północ, wschód. Używany, gdy nie ma <paramref name="OsmRelationId"/>.</param>
/// <param name="OsmRelationId">Relacja OSM z granicą administracyjną miasta; import obejmuje wtedy dokładnie obszar miasta.</param>
/// <param name="GtfsUrl">Rozkład komunikacji miejskiej w formacie GTFS; null, gdy miasto go nie udostępnia.</param>
public sealed record CityImport(double South, double West, double North, double East, string? GtfsUrl = null, string? GtfsSource = null,
    long? OsmRelationId = null);

public static class CityImports
{
    public static IReadOnlyDictionary<string, CityImport> All { get; } = new Dictionary<string, CityImport>
    {
        // Cały Kraków w granicach administracyjnych (relacja 449696); bbox jest tylko zapasem.
        ["krakow"] = new(49.967, 19.792, 50.127, 20.218,
            "https://gtfs.ztp.krakow.pl/GTFS_KRK.zip", "Zarząd Transportu Publicznego w Krakowie (GTFS)",
            OsmRelationId: 449696)
    };
}
