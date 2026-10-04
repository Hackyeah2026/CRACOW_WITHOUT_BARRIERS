using Domain.Places;
using Domain.Trips;

namespace Tests;

public class RestStopRulesTests
{
    // Prosta trasa na wschód o długości 1000 m.
    private const double Lat = 50.06;
    private const double StartLon = 19.93;
    private const double RouteM = 1000;
    private static readonly double MetersPerLonDegree = 111_320 * Math.Cos(Lat * Math.PI / 180);
    private static readonly GeoPoint[] Route = [new(Lat, StartLon), new(Lat, StartLon + 500 / MetersPerLonDegree), new(Lat, StartLon + RouteM / MetersPerLonDegree)];

    private static Place Bench(double alongM, double northM = 0) =>
        new($"bench-{alongM}", "krakow", "Ławka", PlaceCategory.Bench, Lat + northM / 111_320, StartLon + alongM / MetersPerLonDegree, null, null, []);

    private static double[] Stops(int maxM, params Place[] benches) =>
        RestStopRules.AlongRoute(benches, Route, RouteM, maxM).Select(r => Math.Round(r.DistanceFromStartM / 10) * 10).ToArray();

    [Fact]
    public void Picks_the_farthest_bench_within_the_limit_until_the_rest_of_the_leg_fits() =>
        Assert.Equal([280, 500, 700], Stops(300, Bench(100), Bench(250), Bench(280), Bench(500), Bench(700), Bench(900)));

    [Fact]
    public void Ignores_benches_away_from_the_route_and_next_to_its_ends() =>
        Assert.Equal([500], Stops(600, Bench(20), Bench(450, northM: 100), Bench(500, northM: 10), Bench(980)));

    [Fact]
    public void Takes_the_next_bench_when_none_is_within_the_limit()
    {
        var stops = RestStopRules.AlongRoute([Bench(100), Bench(900)], Route, RouteM, 300);

        Assert.Equal(2, stops.Count);
        Assert.InRange(RestStopRules.LongestStretchM(stops, RouteM), 790, 810);
    }

    [Fact]
    public void Leg_within_the_limit_needs_no_rest() =>
        Assert.Empty(Stops(1000, Bench(500)));

    [Fact]
    public void Positions_follow_the_distance_reported_by_routing()
    {
        // Silnik tras podaje 2000 m dla tej samej geometrii: ławka w połowie wypada po 1000 m.
        var stop = Assert.Single(RestStopRules.AlongRoute([Bench(500)], Route, 2000, 1200));

        Assert.InRange(stop.DistanceFromStartM, 990, 1010);
    }
}
