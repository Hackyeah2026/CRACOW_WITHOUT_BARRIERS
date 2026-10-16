namespace Tools;

/// <param name="South">Zasięg importu miejsc (bbox): południe, zachód, północ, wschód. Używany, gdy nie ma <paramref name="OsmRelationId"/>.</param>
/// <param name="OsmRelationId">Relacja OSM z granicą administracyjną miasta; import obejmuje wtedy dokładnie obszar miasta.</param>
/// <param name="GtfsUrl">Rozkład komunikacji miejskiej w formacie GTFS; null, gdy miasto go nie udostępnia.</param>
/// <param name="NoiseLayers">Warstwy poligonowe miejskiej mapy hałasu (ArcGIS REST) z polem ISOV1; puste, gdy miasto ich nie udostępnia.</param>
public sealed record CityImport(double South, double West, double North, double East, string? GtfsUrl = null, string? GtfsSource = null,
    long? OsmRelationId = null, IReadOnlyList<string>? NoiseLayers = null);

public static class CityImports
{
    public static IReadOnlyDictionary<string, CityImport> All { get; } = new Dictionary<string, CityImport>
    {
        // Cały Kraków w granicach administracyjnych (relacja 449696); bbox jest tylko zapasem.
        ["krakow"] = new(49.967, 19.792, 50.127, 20.218,
            "https://gtfs.ztp.krakow.pl/GTFS_KRK.zip", "Zarząd Transportu Publicznego w Krakowie (GTFS)",
            OsmRelationId: 449696,
            NoiseLayers:
            [
                // Mapa hałasu 2022, wskaźnik LDWN: hałas drogowy (1) i szynowy, czyli tramwaje i kolej (5).
                "https://msip.um.krakow.pl/arcgis/rest/services/Mapa_halasu_2022/8_2_MH_2022_IMISJA_5/MapServer/1",
                "https://msip.um.krakow.pl/arcgis/rest/services/Mapa_halasu_2022/8_2_MH_2022_IMISJA_5/MapServer/5"
            ])
    };
}
