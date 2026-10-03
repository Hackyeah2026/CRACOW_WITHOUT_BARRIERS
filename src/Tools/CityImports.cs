namespace Tools;

/// <param name="South">Zasięg importu miejsc (bbox): południe, zachód, północ, wschód.</param>
/// <param name="GtfsUrl">Rozkład komunikacji miejskiej w formacie GTFS; null, gdy miasto go nie udostępnia.</param>
public sealed record CityImport(double South, double West, double North, double East, string? GtfsUrl = null, string? GtfsSource = null);

public static class CityImports
{
    public static IReadOnlyDictionary<string, CityImport> All { get; } = new Dictionary<string, CityImport>
    {
        // Stare Miasto, Kazimierz, Wawel i okolice.
        ["krakow"] = new(50.043, 19.915, 50.072, 19.962,
            "https://gtfs.ztp.krakow.pl/GTFS_KRK.zip", "Zarząd Transportu Publicznego w Krakowie (GTFS)")
    };
}
