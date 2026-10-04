using Application;
using Application.Abstractions;
using Application.Trips;
using Domain.Hazards;
using Domain.Needs;
using Domain.Places;
using Domain.Transit;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class BuildTripPlanTests
{
    // Zygzak północ-południe: optymalizator odwiedza inaczej niż kolejność z listy.
    private static readonly Place[] Places =
    [
        Place("a", 50.070, 19.930), Place("b", 50.045, 19.932), Place("c", 50.069, 19.934), Place("d", 50.046, 19.936)
    ];

    // Ławka w połowie drogi a → c (ok. 300 m) i druga daleko od tras.
    private static readonly Place[] Benches =
    [
        new("bench-ac", "krakow", "Ławka", PlaceCategory.Bench, 50.0695, 19.932, null, null, []),
        new("bench-far", "krakow", "Ławka", PlaceCategory.Bench, 50.0600, 19.950, null, null, [])
    ];

    private static readonly GeoPoint NearD = new(50.0461, 19.9361);

    private static Place Place(string id, double lat, double lon) =>
        new(id, "krakow", id.ToUpperInvariant(), PlaceCategory.Museum, lat, lon, null, null, []);

    private static async Task<Result<Domain.Trips.TripPlan>> BuildAsync(
        IReadOnlyList<string> ids, GeoPoint? start = null, bool optimize = true,
        NeedsProfile? profile = null, IReadOnlyList<VerifiedHazard>? hazards = null, ITransitCatalog? transit = null,
        IRoutingClient? routing = null)
    {
        var services = new ServiceCollection()
            .AddApplication()
            .AddSingleton<IPlaceCatalog, FakeCatalog>()
            .AddSingleton(routing ?? new FakeRouting())
            .AddSingleton(transit ?? new NoTransit())
            .AddSingleton<IHazardsClient>(new FakeHazards(hazards))
            .BuildServiceProvider();

        return await services.GetRequiredService<ISender>().Send(
            new BuildTripPlanCommand("krakow", ids, profile ?? NeedsProfile.Empty, AppMode.Sightseeing, new DateTime(2026, 10, 2, 12, 0, 0), start, optimize));
    }

    private static IEnumerable<string> Ids(Domain.Trips.TripPlan plan) => plan.Stops.Select(s => s.Place.Id);

    [Fact]
    public async Task Optimized_plan_keeps_first_place_as_start_and_shortens_the_route()
    {
        var result = await BuildAsync(["a", "b", "c", "d"]);

        Assert.Equal(["a", "c", "d", "b"], Ids(result.Value));
        Assert.DoesNotContain(result.Value.Stops, s => s.IsUserLocation);
    }

    [Fact]
    public async Task Manual_order_is_kept_when_optimization_is_off()
    {
        var result = await BuildAsync(["b", "a", "d", "c"], optimize: false);

        Assert.Equal(["b", "a", "d", "c"], Ids(result.Value));
        Assert.Equal([1, 2, 3, 4], result.Value.Stops.Select(s => s.Order));
    }

    [Fact]
    public async Task User_location_becomes_first_stop_and_route_starts_there()
    {
        var result = await BuildAsync(["a", "b", "c", "d"], NearD);

        var plan = result.Value;
        Assert.Equal(5, plan.Stops.Count);
        Assert.True(plan.Stops[0].IsUserLocation);
        Assert.Equal(StartLocationRules.PlaceId, plan.Stops[0].Place.Id);
        Assert.Equal("d", plan.Stops[1].Place.Id);
        Assert.Equal(StartLocationRules.PlaceId, plan.Legs[0].FromPlaceId);
        Assert.Equal(NearD, plan.Legs[0].Geometry[0]);
        Assert.Single(plan.Stops, s => s.IsUserLocation);
    }

    [Fact]
    public async Task User_location_with_manual_order_keeps_the_list_order()
    {
        var result = await BuildAsync(["a", "b", "c"], NearD, optimize: false);

        Assert.Equal([StartLocationRules.PlaceId, "a", "b", "c"], Ids(result.Value));
    }

    [Fact]
    public async Task One_place_is_enough_when_starting_from_user_location()
    {
        var result = await BuildAsync(["a"], NearD);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Legs);
    }

    [Fact]
    public async Task One_place_without_user_location_fails()
    {
        var result = await BuildAsync(["a"]);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task User_location_far_from_the_city_fails()
    {
        var warsaw = new GeoPoint(52.2297, 21.0122);

        var result = await BuildAsync(["a", "b"], warsaw);

        Assert.True(result.IsFailure);
        Assert.Contains("Kraków", result.Error);
    }

    [Fact]
    public async Task Verified_hazard_near_the_route_is_attached_to_its_leg()
    {
        // Schody w połowie drogi a → c i hałas daleko od trasy.
        var stairs = new VerifiedHazard("h1", HazardKind.Stairs, 50.0695, 19.9321, "", new DateOnly(2026, 10, 3));
        var noise = new VerifiedHazard("h2", HazardKind.Noise, 50.0600, 19.9500, "", new DateOnly(2026, 10, 3));

        var result = await BuildAsync(["a", "c"], profile: new NeedsProfile { StepFreeRequired = true }, hazards: [stairs, noise]);

        var found = Assert.Single(result.Value.Legs[0].Hazards!);
        Assert.Equal("h1", found.Hazard.Id);
        Assert.True(found.ConcernsProfile);
    }

    [Fact]
    public async Task Plan_is_built_when_hazards_are_unavailable()
    {
        var result = await BuildAsync(["a", "c"], hazards: null);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Legs[0].Hazards!);
    }

    // Schody w połowie drogi a → c, kilka metrów od linii prostej między nimi.
    private static readonly VerifiedHazard StairsOnAc = new("h1", HazardKind.Stairs, 50.0695, 19.9321, "", new DateOnly(2026, 10, 3));

    [Fact]
    public async Task Route_goes_around_a_verified_hazard_that_concerns_the_profile()
    {
        var routing = new DetourRouting(new GeoPoint(50.0705, 19.9320));

        var result = await BuildAsync(["a", "c"], profile: new NeedsProfile { StepFreeRequired = true }, hazards: [StairsOnAc], routing: routing);

        var leg = result.Value.Legs[0];
        Assert.Equal([StairsOnAc], leg.Detour!.Avoided);
        Assert.Equal(3, leg.Geometry.Count);
        Assert.Equal(2, leg.Detour.DirectGeometry.Count);
        Assert.InRange(leg.Detour.ExtraDistanceM, 1, 200);
        // Ominięty punkt nie jest już ostrzeżeniem przy trasie, a zapytanie o objazd niosło jego położenie.
        Assert.Empty(leg.Hazards!);
        Assert.DoesNotContain(leg.Warnings, w => w.Contains("omija"));
        Assert.Equal([StairsOnAc.Location], routing.Avoided);
    }

    [Fact]
    public async Task Route_is_not_changed_for_a_profile_the_hazard_does_not_concern()
    {
        var routing = new DetourRouting(new GeoPoint(50.0705, 19.9320));

        var result = await BuildAsync(["a", "c"], profile: new NeedsProfile { MaxNoiseLevel = 1 }, hazards: [StairsOnAc], routing: routing);

        var leg = result.Value.Legs[0];
        Assert.Null(leg.Detour);
        Assert.Null(routing.Avoided);
        Assert.False(Assert.Single(leg.Hazards!).ConcernsProfile);
    }

    [Fact]
    public async Task Without_a_way_around_the_shortest_route_stays_and_the_leg_warns()
    {
        // FakeRouting zwraca tę samą linię prostą także dla zapytania o objazd.
        var result = await BuildAsync(["a", "c"], profile: new NeedsProfile { StepFreeRequired = true }, hazards: [StairsOnAc]);

        var leg = result.Value.Legs[0];
        Assert.Null(leg.Detour);
        Assert.Single(leg.Hazards!);
        Assert.Contains(leg.Warnings, w => w.Contains("omija"));
    }

    [Fact]
    public async Task Detour_much_longer_than_the_shortest_route_is_not_used()
    {
        // Objazd przez punkt ok. 1,5 km na północ.
        var routing = new DetourRouting(new GeoPoint(50.0830, 19.9320));

        var result = await BuildAsync(["a", "c"], profile: new NeedsProfile { StepFreeRequired = true }, hazards: [StairsOnAc], routing: routing);

        Assert.Null(result.Value.Legs[0].Detour);
        Assert.Equal(2, result.Value.Legs[0].Geometry.Count);
    }

    [Fact]
    public async Task Leg_longer_than_the_walking_limit_gets_a_bench_to_rest_on()
    {
        var result = await BuildAsync(["a", "c"], profile: new NeedsProfile { MaxDistanceWithoutRestM = 200 });

        var leg = result.Value.Legs[0];
        var rest = Assert.Single(leg.RestStops!);
        Assert.Equal("bench-ac", rest.PlaceId);
        Assert.InRange(rest.DistanceFromStartM, 130, 180);
        Assert.Contains(leg.Warnings, w => w.Contains("ławki"));
    }

    [Fact]
    public async Task Leg_within_the_walking_limit_gets_no_benches()
    {
        var result = await BuildAsync(["a", "c"], profile: new NeedsProfile { MaxDistanceWithoutRestM = 500 });

        Assert.Empty(result.Value.Legs[0].RestStops!);
    }

    [Fact]
    public async Task Without_a_walking_limit_no_benches_are_suggested()
    {
        var result = await BuildAsync(["a", "c"]);

        Assert.Empty(result.Value.Legs[0].RestStops!);
    }

    [Fact]
    public async Task Timetable_is_loaded_only_when_a_leg_is_long_enough_to_ride_and_then_only_once()
    {
        var transit = new NoTransit();

        // a → c to ok. 300 m: poniżej progu, od którego proponujemy przejazd.
        await BuildAsync(["a", "c"], optimize: false, transit: transit);
        Assert.Equal(0, transit.Loads);

        // a → b → c: dwa odcinki po blisko 3 km.
        await BuildAsync(["a", "b", "c"], optimize: false, transit: transit);
        Assert.Equal(1, transit.Loads);
    }

    /// <summary>Bez listy punktów udaje host, który nie odpowiada.</summary>
    private sealed class FakeHazards(IReadOnlyList<VerifiedHazard>? verified) : IHazardsClient
    {
        public Task<Result<HazardReceipt>> SubmitAsync(HazardDraft draft, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<HazardStatusView>>> GetMineAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<VerifiedHazard>>> GetVerifiedAsync(string cityId, CancellationToken ct) => Task.FromResult(
            verified is null ? Result.Failure<IReadOnlyList<VerifiedHazard>>("Brak połączenia z serwerem.") : Result.Success(verified));
    }

    private sealed class FakeCatalog : IPlaceCatalog
    {
        public Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<City>>([new City("krakow", "Kraków", 50.0614, 19.9366, 14, CityCoverage.Full, [])]);

        public Task<IReadOnlyList<PlaceCategoryCount>> GetCategoriesAsync(string cityId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PlaceCategoryCount>>([new(PlaceCategory.Museum, Places.Length)]);

        public Task<IReadOnlyList<Place>> GetAsync(string cityId, IReadOnlyCollection<PlaceCategory> categories, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Place>>(Places.Concat(Benches).Where(p => categories.Contains(p.Category)).ToList());

        public Task<Place?> FindAsync(string cityId, string placeId, CancellationToken ct) =>
            Task.FromResult(Places.FirstOrDefault(p => p.Id == placeId));
    }

    private sealed class FakeRouting : IRoutingClient
    {
        public Task<Result<RouteLeg>> GetRouteAsync(RouteRequest request, CancellationToken ct) =>
            Task.FromResult(Result.Success(new RouteLeg(request.From.DistanceTo(request.To), 1, [request.From, request.To], false, [])));
    }

    /// <summary>Silnik tras, który dla zapytania z punktami do ominięcia prowadzi przez podany punkt pośredni.</summary>
    private sealed class DetourRouting(GeoPoint via) : IRoutingClient
    {
        public IReadOnlyList<GeoPoint>? Avoided { get; private set; }

        public Task<Result<RouteLeg>> GetRouteAsync(RouteRequest request, CancellationToken ct)
        {
            if (request.Avoid is null)
                return new FakeRouting().GetRouteAsync(request, ct);

            Avoided = request.Avoid;
            return Task.FromResult(Result.Success(new RouteLeg(
                request.From.DistanceTo(via) + via.DistanceTo(request.To), 1, [request.From, via, request.To], false, [])));
        }
    }

    private sealed class NoTransit : ITransitCatalog
    {
        public int Loads { get; private set; }

        public Task<TransitNetwork?> GetNetworkAsync(string cityId, CancellationToken ct)
        {
            Loads++;
            return Task.FromResult<TransitNetwork?>(null);
        }
    }
}
