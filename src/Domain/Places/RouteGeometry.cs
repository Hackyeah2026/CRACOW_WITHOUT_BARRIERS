namespace Domain.Places;

/// <summary>Gdzie na trasie punkt jest do niej najbliżej.</summary>
/// <param name="DistanceM">Odległość punktu od trasy.</param>
/// <param name="Segment">Indeks odcinka łamanej: od punktu <c>Segment</c> do <c>Segment + 1</c>.</param>
/// <param name="Along">Która część tego odcinka leży przed punktem, od 0 do 1.</param>
public readonly record struct RoutePosition(double DistanceM, int Segment, double Along);

/// <summary>Geometria trasy (łamanej) wspólna dla utrudnień i ławek przy trasie.</summary>
public static class RouteGeometry
{
    /// <summary>Rzut punktu na trasę. Trasa musi mieć co najmniej jeden punkt.</summary>
    public static RoutePosition Locate(GeoPoint point, IReadOnlyList<GeoPoint> route)
    {
        if (route.Count == 1)
            return new RoutePosition(point.DistanceTo(route[0]), 0, 0);

        // Na odległościach rzędu kilometra wystarcza płaskie przybliżenie wokół badanego punktu.
        const double metersPerDegree = 111_320;
        var lonScale = Math.Cos(point.Lat * Math.PI / 180) * metersPerDegree;
        (double X, double Y) Local(GeoPoint p) => ((p.Lon - point.Lon) * lonScale, (p.Lat - point.Lat) * metersPerDegree);

        var best = new RoutePosition(double.MaxValue, 0, 0);
        for (var i = 0; i < route.Count - 1; i++)
        {
            var a = Local(route[i]);
            var b = Local(route[i + 1]);
            var (dx, dy) = (b.X - a.X, b.Y - a.Y);
            var length2 = dx * dx + dy * dy;
            var t = length2 == 0 ? 0 : Math.Clamp(-(a.X * dx + a.Y * dy) / length2, 0, 1);
            var distance = Math.Sqrt(Math.Pow(a.X + t * dx, 2) + Math.Pow(a.Y + t * dy, 2));
            if (distance < best.DistanceM)
                best = new RoutePosition(distance, i, t);
        }
        return best;
    }
}
