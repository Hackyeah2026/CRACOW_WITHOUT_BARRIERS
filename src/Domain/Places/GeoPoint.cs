namespace Domain.Places;

public readonly record struct GeoPoint(double Lat, double Lon)
{
    private const double EarthRadiusM = 6_371_000;

    /// <summary>Odległość w linii prostej (haversine), w metrach.</summary>
    public double DistanceTo(GeoPoint other)
    {
        var dLat = ToRad(other.Lat - Lat);
        var dLon = ToRad(other.Lon - Lon);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
                + Math.Cos(ToRad(Lat)) * Math.Cos(ToRad(other.Lat)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusM * Math.Asin(Math.Sqrt(a));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180;
}

/// <summary>Prostokątny obszar mapy (np. aktualnie widoczny fragment).</summary>
public readonly record struct GeoBounds(double South, double West, double North, double East)
{
    public bool Contains(GeoPoint point) =>
        point.Lat >= South && point.Lat <= North && point.Lon >= West && point.Lon <= East;
}
