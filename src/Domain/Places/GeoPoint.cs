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
