using Domain.Hazards;
using Domain.Places;

namespace Domain.Trips;

/// <summary>Dobiera ławki przy trasie tak, żeby marsz bez przerwy nie przekraczał limitu z profilu.</summary>
public static class RestStopRules
{
    /// <summary>Jak blisko trasy musi stać ławka, żeby nie trzeba było do niej zbaczać.</summary>
    public const double RouteCorridorM = 30;

    /// <summary>Ławka tuż przy początku albo końcu odcinka nie jest przerwą w marszu.</summary>
    public const double MinSpacingM = 50;

    /// <summary>
    /// Ławki w kolejności marszu. Każda kolejna to najdalsza, do której da się dojść w limicie; gdy w limicie nie ma
    /// żadnej, bierzemy najbliższą następną, a zbyt długi fragment pokazuje <see cref="LongestStretchM"/>.
    /// </summary>
    /// <param name="routeDistanceM">Długość odcinka według silnika tras; do niej skalujemy położenie ławek.</param>
    public static IReadOnlyList<RestStop> AlongRoute(
        IReadOnlyList<Place> benches, IReadOnlyList<GeoPoint> route, double routeDistanceM, int maxWithoutRestM)
    {
        if (benches.Count == 0 || route.Count < 2 || maxWithoutRestM <= 0 || routeDistanceM <= maxWithoutRestM)
            return [];

        var starts = new double[route.Count];
        for (var i = 1; i < route.Count; i++)
            starts[i] = starts[i - 1] + route[i - 1].DistanceTo(route[i]);
        var length = starts[^1];
        if (length <= 0)
            return [];

        // W mieście są tysiące ławek, więc dokładnie liczymy tylko te z okolicy trasy.
        const double marginDeg = 0.001;
        var (minLat, maxLat) = (route.Min(p => p.Lat) - marginDeg, route.Max(p => p.Lat) + marginDeg);
        var (minLon, maxLon) = (route.Min(p => p.Lon) - marginDeg, route.Max(p => p.Lon) + marginDeg);

        var candidates = benches
            .Where(b => b.Lat >= minLat && b.Lat <= maxLat && b.Lon >= minLon && b.Lon <= maxLon)
            .Select(b => (Bench: b, At: HazardRules.Locate(b.Location, route)))
            .Where(x => x.At.DistanceM <= RouteCorridorM)
            .Select(x => (x.Bench, WalkedM: (starts[x.At.Segment] + x.At.Along * (starts[x.At.Segment + 1] - starts[x.At.Segment])) / length * routeDistanceM))
            .Where(x => x.WalkedM < routeDistanceM - MinSpacingM)
            .OrderBy(x => x.WalkedM)
            .ToList();

        var stops = new List<RestStop>();
        var walked = 0.0;
        while (routeDistanceM - walked > maxWithoutRestM)
        {
            var ahead = candidates.Where(c => c.WalkedM > walked + MinSpacingM).ToList();
            if (ahead.Count == 0)
                break;

            var reachable = ahead.Where(c => c.WalkedM <= walked + maxWithoutRestM).ToList();
            var next = reachable.Count > 0 ? reachable[^1] : ahead[0];
            stops.Add(new RestStop(next.Bench.Id, next.Bench.Lat, next.Bench.Lon, Math.Round(next.WalkedM)));
            walked = next.WalkedM;
        }
        return stops;
    }

    /// <summary>Najdłuższy fragment odcinka bez przerwy: od startu, między ławkami albo do celu.</summary>
    public static double LongestStretchM(IReadOnlyList<RestStop> stops, double routeDistanceM)
    {
        var longest = 0.0;
        var previous = 0.0;
        foreach (var stop in stops)
        {
            longest = Math.Max(longest, stop.DistanceFromStartM - previous);
            previous = stop.DistanceFromStartM;
        }
        return Math.Max(longest, routeDistanceM - previous);
    }
}
