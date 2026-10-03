using Application.Abstractions;
using Domain.Assessments;
using Domain.Needs;
using Domain.Places;
using Domain.Transit;
using Domain.Trips;

namespace Application.Trips;

/// <summary>Układa plan z wybranych miejsc. Pierwsze miejsce na liście jest punktem startu.</summary>
/// <param name="Now">Czas lokalny użytkownika; od niego szukamy odjazdów komunikacji.</param>
public sealed record BuildTripPlanCommand(
    string CityId, IReadOnlyList<string> PlaceIds, NeedsProfile Profile, AppMode Mode, DateTime Now)
    : ICommand<TripPlan>;

public static class TransitRules
{
    /// <summary>Od jakiej długości odcinka proponujemy komunikację, gdy profil nie ma własnego limitu.</summary>
    public const int DefaultLegThresholdM = 1000;

    /// <summary>Najdłuższe dojście do przystanku, gdy profil nie ma własnego limitu.</summary>
    public const int DefaultMaxWalkToStopM = 600;
}

internal sealed class BuildTripPlanCommandHandler(IPlaceCatalog catalog, IRoutingClient routing, ITransitCatalog transit)
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

        var network = await transit.GetNetworkAsync(command.CityId, ct);
        var planner = network is null ? null : new TransitPlanner(network);

        var legs = new List<TripLeg>();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var route = await routing.GetRouteAsync(new RouteRequest(ordered[i].Location, ordered[i + 1].Location, command.Profile), ct);
            if (route.IsFailure)
                return Result.Failure<TripPlan>(route.Error!);

            var advice = TransitFor(planner, command, route.Value, ordered[i].Location, ordered[i + 1].Location);
            legs.Add(new TripLeg(ordered[i].Id, ordered[i + 1].Id, route.Value.DistanceM, route.Value.DurationMin,
                route.Value.Geometry, route.Value.IsEstimated, LegWarnings(route.Value, command.Profile, advice?.Status == TransitStatus.Found), advice));
        }

        return Result.Success(new TripPlan(Guid.NewGuid().ToString("N"), command.Mode, DateTimeOffset.UtcNow, stops, legs));
    }

    /// <summary>Komunikację sprawdzamy dla każdego odcinka dłuższego niż limit z profilu albo próg domyślny.</summary>
    private static TransitAdvice? TransitFor(TransitPlanner? planner, BuildTripPlanCommand command, RouteLeg leg, GeoPoint from, GeoPoint to)
    {
        var profile = command.Profile;
        if (planner is null || leg.DistanceM <= (profile.MaxDistanceWithoutRestM ?? TransitRules.DefaultLegThresholdM))
            return null;

        return planner.Plan(from, to, DateOnly.FromDateTime(command.Now), command.Now.Hour * 60 + command.Now.Minute,
            profile.MaxDistanceWithoutRestM ?? TransitRules.DefaultMaxWalkToStopM, profile.WalkingSpeedKmh, leg.DistanceM);
    }

    private static List<string> LegWarnings(RouteLeg leg, NeedsProfile profile, bool hasTransit)
    {
        var warnings = new List<string>(leg.Warnings);
        if (profile.MaxDistanceWithoutRestM is { } max && leg.DistanceM > max)
            warnings.Add(hasTransit
                ? $"Odcinek dłuższy niż {max} m. Możesz podjechać komunikacją miejską."
                : $"Odcinek dłuższy niż {max} m bez odpoczynku. Zaplanuj przerwę po drodze.");
        if (leg.IsEstimated)
            warnings.Add("Dystans szacowany w linii prostej. Bariery na tym odcinku nie są sprawdzone.");
        return warnings;
    }
}
