namespace Tools;

/// <summary>
/// Obszar jednego przedziału hałasu: dolna granica w dB i pierścienie poligonu (zewnętrzne i dziury) jako płaskie
/// tablice [lon0, lat0, lon1, lat1, ...].
/// </summary>
public sealed class NoiseArea
{
    private readonly IReadOnlyList<double[]> _rings;

    public NoiseArea(double fromDb, IReadOnlyList<double[]> rings)
    {
        FromDb = fromDb;
        _rings = rings;
        MinLon = MinLat = double.MaxValue;
        MaxLon = MaxLat = double.MinValue;
        foreach (var ring in rings)
            for (var i = 0; i + 1 < ring.Length; i += 2)
            {
                MinLon = Math.Min(MinLon, ring[i]);
                MaxLon = Math.Max(MaxLon, ring[i]);
                MinLat = Math.Min(MinLat, ring[i + 1]);
                MaxLat = Math.Max(MaxLat, ring[i + 1]);
            }
    }

    public double FromDb { get; }
    public double MinLon { get; }
    public double MaxLon { get; }
    public double MinLat { get; }
    public double MaxLat { get; }

    /// <summary>Reguła parzystości po wszystkich pierścieniach: dziury i wielokąty złożone działają bez rozróżniania pierścieni.</summary>
    public bool Contains(double lat, double lon)
    {
        if (lon < MinLon || lon > MaxLon || lat < MinLat || lat > MaxLat)
            return false;

        var inside = false;
        foreach (var ring in _rings)
        {
            for (int i = 0, j = ring.Length - 2; i + 1 < ring.Length; j = i, i += 2)
            {
                double xi = ring[i], yi = ring[i + 1], xj = ring[j], yj = ring[j + 1];
                if ((yi > lat) != (yj > lat) && lon < (xj - xi) * (lat - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
        }
        return inside;
    }
}

/// <summary>Mapa hałasu z siatką ok. 1 km, żeby punkt sprawdzać tylko z obszarami w pobliżu.</summary>
public sealed class NoiseMap
{
    private const double CellDeg = 0.01;
    private readonly Dictionary<(int, int), List<NoiseArea>> _grid = [];

    public NoiseMap(IEnumerable<NoiseArea> areas)
    {
        foreach (var area in areas)
        {
            AreaCount++;
            for (var x = Cell(area.MinLon); x <= Cell(area.MaxLon); x++)
                for (var y = Cell(area.MinLat); y <= Cell(area.MaxLat); y++)
                {
                    if (!_grid.TryGetValue((x, y), out var list))
                        _grid[(x, y)] = list = [];
                    list.Add(area);
                }
        }
    }

    public int AreaCount { get; }

    /// <summary>
    /// Najwyższa dolna granica przedziału spośród obszarów, w których leży punkt (np. hałas drogowy i szynowy razem);
    /// null, gdy punkt jest poza mapą.
    /// </summary>
    public double? BandAt(double lat, double lon)
    {
        if (!_grid.TryGetValue((Cell(lon), Cell(lat)), out var areas))
            return null;

        double? band = null;
        foreach (var area in areas)
            if (area.FromDb > (band ?? double.MinValue) && area.Contains(lat, lon))
                band = area.FromDb;
        return band;
    }

    private static int Cell(double degrees) => (int)Math.Floor(degrees / CellDeg);
}
