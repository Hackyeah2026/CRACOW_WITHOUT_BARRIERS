using Domain.Places;
using Domain.Transit;

namespace Tests;

public class TransitPlannerTests
{
    private static readonly DateOnly Friday = new(2026, 10, 2);
    private static readonly DateOnly Saturday = new(2026, 10, 3);

    // Linia 4 biegnie z zachodu na wschód (A-B-C-D), linia 100 z C na północ (C-E-F). Przystanki co ok. 700 m.
    private static readonly TransitNetwork Network = new("test", Friday, new DateOnly(2027, 1, 31), Saturday,
        [
            new TransitStop("A", 50.0600, 19.9200), new TransitStop("B", 50.0600, 19.9300),
            new TransitStop("C", 50.0600, 19.9400), new TransitStop("D", 50.0600, 19.9500),
            new TransitStop("E", 50.0680, 19.9400), new TransitStop("F", 50.0760, 19.9400)
        ],
        [
            new TransitLine("4", TransitKind.Tram,
            [
                // Kursy o 12:00, 12:10 i 12:20 w piątek (usługa 0), 12:05 w sobotę (usługa 1).
                new TransitPattern("D", [0, 1, 2, 3], [[0, 2, 4, 6]], [720, 0, 0, 730, 0, 0, 740, 0, 0, 725, 0, 1]),
                new TransitPattern("A", [3, 2, 1, 0], [[0, 2, 4, 6]], [721, 0, 0])
            ]),
            new TransitLine("100", TransitKind.Bus,
            [
                new TransitPattern("F", [2, 4, 5], [[0, 3, 6]], [726, 0, 0, 746, 0, 0])
            ])
        ],
        [
            new TransitService(0, null, null, [Friday], []),
            new TransitService(0, null, null, [Saturday], [])
        ]);

    private static readonly GeoPoint NearA = new(50.0603, 19.9202);
    private static readonly GeoPoint NearD = new(50.0597, 19.9498);
    private static readonly GeoPoint NearF = new(50.0762, 19.9402);

    private static TransitAdvice Plan(GeoPoint from, GeoPoint to, DateOnly date, int now, double maxWalk = 300) =>
        new TransitPlanner(Network).Plan(from, to, date, now, maxWalk, walkSpeedKmh: 4.5, directDistanceM: from.DistanceTo(to) * 1.3);

    [Fact]
    public void Direct_ride_takes_the_next_departure_and_lists_following_ones()
    {
        var advice = Plan(NearA, NearD, Friday, now: 11 * 60 + 55);

        Assert.Equal(TransitStatus.Found, advice.Status);
        var journey = Assert.Single(advice.Journeys);
        var ride = Assert.Single(journey.Rides);
        Assert.Equal(("4", "A", "D"), (ride.LineName, ride.BoardStop, ride.AlightStop));
        Assert.Equal(720, ride.DepartMinute);
        Assert.Equal(726, ride.ArriveMinute);
        Assert.Equal(3, ride.StopsCount);
        Assert.Equal([720, 730, 740], ride.NextDepartures);
        Assert.True(journey.LeaveMinute <= 720 && journey.ArriveMinute >= 726);
    }

    [Fact]
    public void Missed_departure_moves_to_the_following_one()
    {
        var ride = Plan(NearA, NearD, Friday, now: 12 * 60 + 5).Journeys[0].Rides[0];

        Assert.Equal(730, ride.DepartMinute);
    }

    [Fact]
    public void Journey_with_one_transfer_waits_for_the_connecting_line()
    {
        var advice = Plan(NearA, NearF, Friday, now: 11 * 60 + 55);

        var journey = Assert.Single(advice.Journeys);
        Assert.Equal(1, journey.Transfers);
        Assert.Equal(["4", "100"], journey.Rides.Select(r => r.LineName));
        Assert.Equal("C", journey.Rides[0].AlightStop);
        Assert.Equal("C", journey.Rides[1].BoardStop);
        // Tramwaj jest na C o 12:04, autobus odjeżdża o 12:06.
        Assert.Equal(726, journey.Rides[1].DepartMinute);
        Assert.Equal(732, journey.Rides[1].ArriveMinute);
    }

    [Fact]
    public void Timetable_depends_on_the_day()
    {
        var ride = Plan(NearA, NearD, Saturday, now: 11 * 60 + 55).Journeys[0].Rides[0];

        Assert.Equal(725, ride.DepartMinute);
        Assert.Equal([725], ride.NextDepartures);
    }

    [Fact]
    public void Reports_when_there_is_no_stop_in_walking_range()
    {
        var farFromStops = new GeoPoint(50.0900, 19.9000);

        Assert.Equal(TransitStatus.NoStopsNearby, Plan(farFromStops, NearD, Friday, 715).Status);
    }

    [Fact]
    public void Reports_when_no_departure_is_left_in_the_search_window()
    {
        var advice = Plan(NearA, NearD, Friday, now: 20 * 60);

        Assert.Equal(TransitStatus.NoConnection, advice.Status);
        Assert.Empty(advice.Journeys);
    }

    [Fact]
    public void Reports_when_timetable_does_not_cover_the_day()
    {
        Assert.Equal(TransitStatus.NoTimetable, Plan(NearA, NearD, new DateOnly(2027, 6, 1), 715).Status);
    }

    [Fact]
    public void Service_follows_weekdays_and_exceptions()
    {
        var weekdays = new TransitService(0b0011111, new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31), [Saturday], [Friday]);

        Assert.True(weekdays.RunsOn(new DateOnly(2026, 10, 5)));   // poniedziałek
        Assert.False(weekdays.RunsOn(new DateOnly(2026, 10, 4)));  // niedziela
        Assert.True(weekdays.RunsOn(Saturday));                    // dodany wyjątkiem
        Assert.False(weekdays.RunsOn(Friday));                     // odwołany wyjątkiem
        Assert.False(weekdays.RunsOn(new DateOnly(2027, 1, 4)));   // poza zakresem
    }
}
