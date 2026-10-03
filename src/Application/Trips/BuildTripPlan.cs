using Application.Abstractions;
using Domain.Assessments;
using Domain.Needs;
using Domain.Places;
using Domain.Trips;

namespace Application.Trips;

/// <summary>Układa plan z wybranych miejsc. Pierwsze miejsce na liście jest punktem startu.</summary>
public sealed record BuildTripPlanCommand(string CityId, IReadOnlyList<string> PlaceIds, NeedsProfile Profile, AppMode Mode)
    : ICommand<TripPlan>;

internal sealed class BuildTripPlanCommandHandler(IPlaceCatalog catalog, IRoutingClient routing)
    : ICommandHandler<BuildTripPlanCommand, TripPlan>
{
    public async Task<Result<TripPlan>> Handle(BuildTripPlanCommand command, CancellationToken ct)
    {
        var all = await catalog.GetAllAsync(command.CityId, ct);
        var places = command.PlaceIds.Distinct()
            .Select(id => all.FirstOrDefault(p => p.Id == id))
            .OfType<Place>()
            .ToList();

        if (places.Count < 2)
            return Result.Failure<TripPlan>("Wybierz co najmniej dwa miejsca, żeby ułożyć plan.");

        var order = StopOrderOptimizer.Order(places.Select(p => p.Location).ToList());
        var ordered = order.Select(i => places[i]).ToList();

        var stops = ordered
            .Select((place, index) => new TripStop(index + 1, place, AssessmentEngine.Assess(command.Profile, place)))
            .ToList();

        var legs = new List<TripLeg>();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var route = await routing.GetRouteAsync(new RouteRequest(ordered[i].Location, ordered[i + 1].Location, command.Profile), ct);
            if (route.IsFailure)
                return Result.Failure<TripPlan>(route.Error!);

            legs.Add(new TripLeg(ordered[i].Id, ordered[i + 1].Id, route.Value.DistanceM, route.Value.DurationMin,
                route.Value.Geometry, route.Value.IsEstimated, LegWarnings(route.Value, command.Profile)));
        }

        return Result.Success(new TripPlan(Guid.NewGuid().ToString("N"), command.Mode, DateTimeOffset.UtcNow, stops, legs));
    }

    private static List<string> LegWarnings(RouteLeg leg, NeedsProfile profile)
    {
        var warnings = new List<string>();
        if (profile.MaxDistanceWithoutRestM is { } max && leg.DistanceM > max)
            warnings.Add($"Odcinek dłuższy niż {max} m bez odpoczynku. Zaplanuj przerwę po drodze.");
        if (leg.IsEstimated)
            warnings.Add("Dystans szacowany w linii prostej. Bariery na trasie (krawężniki, bruk, nachylenie) nie są jeszcze sprawdzane.");
        return warnings;
    }
}
