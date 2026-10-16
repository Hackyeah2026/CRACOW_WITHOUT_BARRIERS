namespace Tools;

/// <param name="South">Zasięg importu miejsc (bbox): południe, zachód, północ, wschód. Używany, gdy nie ma <paramref name="OsmRelationId"/>.</param>
/// <param name="OsmRelationId">Relacja OSM z granicą administracyjną miasta; import obejmuje wtedy dokładnie obszar miasta.</param>
/// <param name="GtfsUrl">Rozkład komunikacji miejskiej w formacie GTFS; null, gdy miasto go nie udostępnia.</param>
/// <param name="NoiseLayers">Warstwy poligonowe miejskiej mapy hałasu (ArcGIS REST) z polem ISOV1; puste, gdy miasto ich nie udostępnia.</param>
/// <param name="PublicToiletsLayer">Warstwa punktowa toalet publicznych w formacie rejestru ISDP Krakowa (ArcGIS REST).</param>
/// <param name="StopSheltersLayer">Warstwa punktowa wiat przystankowych z polem nazwa_przystanku_nr (ArcGIS REST).</param>
/// <param name="DisabledParkingLayer">Warstwa punktowa kopert z polami ID_MIEJSCA i punkt_adresowy (ArcGIS REST).</param>
/// <param name="StopsLayer">Warstwa punktowa przystanków z polami nazwa_przystanku_nr i na_żądanie (ArcGIS REST).</param>
/// <param name="ServicePointsLayer">Warstwa punktowa punktów obsługi mieszkańców z polami Adres i Godziny otwarcia (ArcGIS REST).</param>
public sealed record CityImport(double South, double West, double North, double East, string? GtfsUrl = null, string? GtfsSource = null,
    long? OsmRelationId = null, IReadOnlyList<string>? NoiseLayers = null, string? PublicToiletsLayer = null, string? StopSheltersLayer = null,
    string? DisabledParkingLayer = null, string? StopsLayer = null, string? ServicePointsLayer = null);

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
            ],
            PublicToiletsLayer: "https://msip.um.krakow.pl/arcgis/rest/services/Obserwatorium/WT_WC_2023/MapServer/0",
            StopSheltersLayer: "https://msip.um.krakow.pl/arcgis/rest/services/Obserwatorium/ZTP_Komunikacja_Miejska_i_Rowerowa/MapServer/9",
            DisabledParkingLayer: "https://services-eu1.arcgis.com/svTzSt3AvH7sK6q9/arcgis/rest/services/Miejsca_postojowe_OZN/FeatureServer/0",
            StopsLayer: "https://msip.um.krakow.pl/arcgis/rest/services/Obserwatorium/ZTP_Komunikacja_Miejska_i_Rowerowa/MapServer/10",
            ServicePointsLayer: "https://msip.um.krakow.pl/arcgis/rest/services/Obserwatorium/K07_Admini_i_Bezpiecz/MapServer/1")
    };
}
