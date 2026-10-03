using Application;
using Application.Abstractions;
using Application.Trips;
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

    private static readonly GeoPoint NearD = new(50.0461, 19.9361);

    private static Place Place(string id, double lat, double lon) =>
        new(id, "krakow", id.ToUpperInvariant(), PlaceCategory.Museum, lat, lon, null, null, []);

    private static async Task<Result<Domain.Trips.TripPlan>> BuildAsync(
        IReadOnlyList<string> ids, GeoPoint? start = null, bool optimize = true)
    {
        var services = new ServiceCollection()
            .AddApplication()
            .AddSingleton<IPlaceCatalog, FakeCatalog>()
            .AddSingleton<IRoutingClient, FakeRouting>()
            .AddSingleton<ITransitCatalog, NoTransit>()
            .BuildServiceProvider();

        return await services.GetRequiredService<ISender>().Send(
            new BuildTripPlanCommand("krakow", ids, NeedsProfile.Empty, AppMode.Sightseeing, new DateTime(2026, 10, 2, 12, 0, 0), start, optimize));
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

    private sealed class FakeCatalog : IPlaceCatalog
    {
        public Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<City>>([new City("krakow", "Kraków", 50.0614, 19.9366, 14, CityCoverage.Full, [])]);

        public Task<IReadOnlyList<Place>> GetAllAsync(string cityId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Place>>(Places);
    }

    private sealed class FakeRouting : IRoutingClient
    {
        public Task<Result<RouteLeg>> GetRouteAsync(RouteRequest request, CancellationToken ct) =>
            Task.FromResult(Result.Success(new RouteLeg(request.From.DistanceTo(request.To), 1, [request.From, request.To], false, [])));
    }

    private sealed class NoTransit : ITransitCatalog
    {
        public Task<TransitNetwork?> GetNetworkAsync(string cityId, CancellationToken ct) => Task.FromResult<TransitNetwork?>(null);
    }
}
