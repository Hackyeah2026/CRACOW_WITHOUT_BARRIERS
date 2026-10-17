namespace Domain.Places;

/// <summary>Dzielnica miasta z granicą: pierścienie jako płaskie tablice [lat0, lon0, lat1, lon1, ...].</summary>
public sealed record District(int Number, string Roman, string Name, IReadOnlyList<double[]> Rings)
{
    private (double MinLat, double MaxLat, double MinLon, double MaxLon)? _box;

    /// <summary>Reguła parzystości po wszystkich pierścieniach: dziury i części rozłączne działają bez rozróżniania.</summary>
    public bool Contains(GeoPoint point)
    {
        var box = _box ??= (
            Rings.Min(r => Every(r, 0).Min()), Rings.Max(r => Every(r, 0).Max()),
            Rings.Min(r => Every(r, 1).Min()), Rings.Max(r => Every(r, 1).Max()));
        if (point.Lat < box.MinLat || point.Lat > box.MaxLat || point.Lon < box.MinLon || point.Lon > box.MaxLon)
            return false;

        var inside = false;
        foreach (var ring in Rings)
            for (int i = 0, j = ring.Length - 2; i + 1 < ring.Length; j = i, i += 2)
            {
                double yi = ring[i], xi = ring[i + 1], yj = ring[j], xj = ring[j + 1];
                if ((yi > point.Lat) != (yj > point.Lat) && point.Lon < (xj - xi) * (point.Lat - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
        return inside;
    }

    private static IEnumerable<double> Every(double[] ring, int offset)
    {
        for (var i = offset; i < ring.Length; i += 2)
            yield return ring[i];
    }
}

/// <param name="DataDate">Data aktualizacji granic według dostawcy.</param>
public sealed record DistrictMap(string Source, DateOnly? DataDate, IReadOnlyList<District> Districts)
{
    public District? Find(GeoPoint point) => Districts.FirstOrDefault(d => d.Contains(point));
}
