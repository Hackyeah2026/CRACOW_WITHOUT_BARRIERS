namespace Domain.Places;

/// <summary>
/// Mapa hałasu miasta jako siatka komórek (ok. 25 m) z przedziałem hałasu w każdej. Mała na tyle, że przeglądarka może
/// ją wczytać i sprawdzać hałas wzdłuż dowolnej trasy bez pytania serwera.
/// </summary>
/// <param name="Bands">Bajt na komórkę, wiersze od południa, w Base64: 0 to brak danych, 1 to poniżej 55 dB, 2-7 to przedziały od 55 dB co 5 dB.</param>
/// <param name="Source">Źródło do pokazania przy ostrzeżeniu, np. "MSIP Kraków, mapa hałasu 2022".</param>
public sealed record NoiseGrid(double South, double West, double CellLat, double CellLon, int Rows, int Cols, string Bands, string Source)
{
    private byte[]? _cells;

    public static byte Encode(double? bandFromDb) => bandFromDb switch
    {
        null => 0,
        < AmbientNoise.ModerateFromDb => 1,
        var db => (byte)Math.Min(7, 2 + (int)((db.Value - AmbientNoise.ModerateFromDb) / 5))
    };

    public static double? Decode(byte code) => code switch
    {
        0 => null,
        1 => 0,
        _ => AmbientNoise.ModerateFromDb + (code - 2) * 5
    };

    /// <summary>Dolna granica przedziału hałasu w punkcie (0 dla przedziału poniżej 55 dB); null poza mapą.</summary>
    public double? BandAt(GeoPoint point)
    {
        var row = (int)Math.Floor((point.Lat - South) / CellLat);
        var col = (int)Math.Floor((point.Lon - West) / CellLon);
        if (row < 0 || row >= Rows || col < 0 || col >= Cols)
            return null;
        _cells ??= Convert.FromBase64String(Bands);
        return Decode(_cells[row * Cols + col]);
    }
}

/// <summary>Głośne fragmenty trasy: ile metrów prowadzi przez hałas powyżej tolerancji z profilu i jak głośno jest najbardziej.</summary>
/// <param name="LoudestDb">Dolna granica najgłośniejszego przedziału na trasie.</param>
public sealed record RouteNoise(double LoudM, double LoudestDb);

public static class RouteNoiseRules
{
    /// <summary>Co tyle metrów sprawdzamy trasę; komórka siatki ma ok. 25 m.</summary>
    public const double SampleEveryM = 20;

    /// <summary>Krótszy głośny fragment (np. przejście przez jedną ulicę) nie jest wart ostrzeżenia.</summary>
    public const double MinLoudM = 60;

    /// <summary>Fragmenty trasy z poziomem hałasu powyżej <paramref name="maxLevel"/>; null, gdy takich nie ma albo są krótkie.</summary>
    public static RouteNoise? Above(NoiseGrid grid, IReadOnlyList<GeoPoint> route, int maxLevel)
    {
        var (loudM, loudest) = (0.0, double.MinValue);
        for (var i = 0; i + 1 < route.Count; i++)
        {
            var (a, b) = (route[i], route[i + 1]);
            var length = a.DistanceTo(b);
            var samples = Math.Max(1, (int)Math.Ceiling(length / SampleEveryM));
            for (var s = 0; s < samples; s++)
            {
                var t = (s + 0.5) / samples;
                if (grid.BandAt(new GeoPoint(a.Lat + (b.Lat - a.Lat) * t, a.Lon + (b.Lon - a.Lon) * t)) is { } db && AmbientNoise.Level(db) > maxLevel)
                {
                    loudM += length / samples;
                    loudest = Math.Max(loudest, db);
                }
            }
        }
        return loudM >= MinLoudM ? new RouteNoise(loudM, loudest) : null;
    }
}
