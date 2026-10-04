using Application.Abstractions;
using Domain.Assessments;
using Domain.Hazards;
using Domain.Needs;
using Domain.Places;
using Domain.Transit;
using Domain.Trips;

namespace Application.Trips;

/// <summary>Układa plan z wybranych miejsc. Startem jest <paramref name="Start"/>, a gdy go nie ma, pierwsze miejsce na liście.</summary>
/// <param name="Now">Czas lokalny użytkownika; od niego szukamy odjazdów komunikacji.</param>
/// <param name="Start">Punkt startu spoza katalogu: lokalizacja użytkownika albo miejsce wskazane na mapie.</param>
/// <param name="OptimizeOrder">Czy ułożyć kolejność miejsc po starcie tak, żeby droga była najkrótsza. Gdy nie, zostaje kolejność z listy.</param>
public sealed record BuildTripPlanCommand(
    string CityId, IReadOnlyList<string> PlaceIds, NeedsProfile Profile, AppMode Mode, DateTime Now,
    GeoPoint? Start = null, bool OptimizeOrder = true)
    : ICommand<TripPlan>;

public static class TransitRules
{
    /// <summary>Od jakiej długości odcinka proponujemy komunikację, gdy profil nie ma własnego limitu.</summary>
    public const int DefaultLegThresholdM = 1000;

    /// <summary>Najdłuższe dojście do przystanku, gdy profil nie ma własnego limitu.</summary>
    public const int DefaultMaxWalkToStopM = 600;
}

public static class DetourRules
{
    /// <summary>Ile razy pytamy o objazd: kolejna próba omija też punkty, na które trafił poprzedni objazd.</summary>
    public const int MaxAttempts = 2;

    /// <summary>Dłuższego objazdu nie proponujemy: plan zostaje przy najkrótszej trasie i ostrzega.</summary>
    public const int MaxExtraM = 1000;
}

public static class StartLocationRules
{
    public const string PlaceId = "my-location";
    public const string Name = "Punkt startu";

    /// <summary>Dalej od centrum miasta lokalizacja nie nadaje się na start pieszego planu.</summary>
    public const int MaxDistanceFromCityM = 30_000;
}

internal sealed class BuildTripPlanCommandHandler(
    IPlaceCatalog catalog, IRoutingClient routing, ITransitCatalog transit, IHazardsClient hazards)
    : ICommandHandler<BuildTripPlanCommand, TripPlan>
{
    public async Task<Result<TripPlan>> Handle(BuildTripPlanCommand command, CancellationToken ct)
    {
        var places = new List<Place>();
        foreach (var id in command.PlaceIds.Distinct())
        {
            if (await catalog.FindAsync(command.CityId, id, ct) is { } place)
                places.Add(place);
        }

        if (command.Start is { } start)
        {
            if (places.Count < 1)
                return Result.Failure<TripPlan>("Wybierz co najmniej jedno miejsce, żeby ułożyć plan z punktu startu.");

            var city = (await catalog.GetCitiesAsync(ct)).FirstOrDefault(c => c.Id == command.CityId);
            if (city is not null && start.DistanceTo(new GeoPoint(city.Lat, city.Lon)) > StartLocationRules.MaxDistanceFromCityM)
                return Result.Failure<TripPlan>(
                    $"Punkt startu jest dalej niż {StartLocationRules.MaxDistanceFromCityM / 1000} km od centrum miasta: {city.Name}. Popraw go na mapie albo wybierz start z listy miejsc.");

            places.Insert(0, new Place(StartLocationRules.PlaceId, command.CityId, StartLocationRules.Name,
                PlaceCategory.Stop, start.Lat, start.Lon, null, null, []));
        }
        else if (places.Count < 2)
        {
            return Result.Failure<TripPlan>("Wybierz co najmniej dwa miejsca, żeby ułożyć plan.");
        }

        var ordered = command.OptimizeOrder
            ? StopOrderOptimizer.Order(places.Select(p => p.Location).ToList()).Select(i => places[i]).ToList()
            : places;

        var stops = ordered
            .Select((place, index) => new TripStop(index + 1, place, AssessmentEngine.Assess(command.Profile, place),
                IsUserLocation: command.Start is not null && index == 0))
            .ToList();

        // Bez odpowiedzi hosta plan układa się jak dotąd, tylko bez potwierdzonych utrudnień.
        var verified = await hazards.GetVerifiedAsync(command.CityId, ct);
        IReadOnlyList<VerifiedHazard> knownHazards = verified.IsSuccess ? verified.Value : [];

        // Rozkład jazdy i plik ławek są duże, więc pobieramy je dopiero, gdy któryś odcinek ich potrzebuje.
        TransitPlanner? planner = null;
        var transitLoaded = false;
        IReadOnlyList<Place>? benches = null;
        var legs = new List<TripLeg>();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var (from, to) = (ordered[i], ordered[i + 1]);
            var route = await RouteAsync(from.Location, to.Location, command.Profile, knownHazards, ct);
            if (route.IsFailure)
                return Result.Failure<TripPlan>(route.Error!);
            var (leg, detour, detourFailed) = route.Value;

            TransitAdvice? advice = null;
            if (NeedsTransit(leg, command.Profile))
            {
                if (!transitLoaded)
                {
                    planner = await transit.GetNetworkAsync(command.CityId, ct) is { } network ? new TransitPlanner(network) : null;
                    transitLoaded = true;
                }
                advice = planner is null ? null : TransitFor(planner, command, leg, from.Location, to.Location);
            }

            IReadOnlyList<RestStop> rests = [];
            if (NeedsRest(leg, command.Profile, out var maxWithoutRest))
            {
                benches ??= await catalog.GetAsync(command.CityId, [PlaceCategory.Bench], ct);
                rests = RestStopRules.AlongRoute(benches, leg.Geometry, leg.DistanceM, maxWithoutRest);
            }

            // Ominięte punkty są opisane w objeździe, więc nie powtarzamy ich jako ostrzeżeń przy trasie.
            var along = HazardRules.AlongRoute(knownHazards, leg.Geometry, command.Profile);
            if (detour is not null)
                along = along.Where(h => !detour.Avoided.Contains(h.Hazard)).ToList();

            legs.Add(new TripLeg(from.Id, to.Id, leg.DistanceM, leg.DurationMin, leg.Geometry, leg.IsEstimated,
                LegWarnings(leg, command.Profile, advice?.Status == TransitStatus.Found, rests, detourFailed), advice,
                along, rests, detour));
        }

        return Result.Success(new TripPlan(Guid.NewGuid().ToString("N"), command.Mode, DateTimeOffset.UtcNow, stops, legs));
    }

    private sealed record Routed(RouteLeg Leg, RouteDetour? Detour, bool DetourFailed);

    /// <summary>
    /// Trasa odcinka. Gdy najkrótsza prowadzi przez potwierdzone utrudnienie istotne dla profilu, pytamy o drogę,
    /// która je omija. Bez takiej drogi zostaje najkrótsza trasa, a plan ostrzega jak dotąd.
    /// </summary>
    private async Task<Result<Routed>> RouteAsync(
        GeoPoint from, GeoPoint to, NeedsProfile profile, IReadOnlyList<VerifiedHazard> hazards, CancellationToken ct)
    {
        var direct = await routing.GetRouteAsync(new RouteRequest(from, to, profile), ct);
        if (direct.IsFailure)
            return Result.Failure<Routed>(direct.Error!);

        // Trasa szacowana w linii prostej nie mówi, którędy się idzie, więc nie ma czego omijać.
        var avoid = direct.Value.IsEstimated ? [] : HazardRules.Blocking(hazards, direct.Value.Geometry, profile);
        if (avoid.Count == 0)
            return Result.Success(new Routed(direct.Value, null, false));

        for (var attempt = 0; attempt < DetourRules.MaxAttempts && avoid.Count <= HazardRules.MaxAvoided; attempt++)
        {
            var detour = await routing.GetRouteAsync(new RouteRequest(from, to, profile, avoid.Select(h => h.Location).ToList()), ct);
            if (detour.IsFailure || detour.Value.IsEstimated)
                break;

            var extra = detour.Value.DistanceM - direct.Value.DistanceM;
            var blocking = HazardRules.Blocking(hazards, detour.Value.Geometry, profile);
            if (extra > DetourRules.MaxExtraM || blocking.Any(avoid.Contains))
                break;
            if (blocking.Count == 0)
                return Result.Success(new Routed(detour.Value, new RouteDetour(avoid, extra, direct.Value.Geometry), false));

            avoid = [.. avoid, .. blocking];
        }

        return Result.Success(new Routed(direct.Value, null, DetourFailed: true));
    }

    /// <summary>Komunikację sprawdzamy dla każdego odcinka dłuższego niż limit z profilu albo próg domyślny.</summary>
    private static bool NeedsTransit(RouteLeg leg, NeedsProfile profile) =>
        leg.DistanceM > (profile.MaxDistanceWithoutRestM ?? TransitRules.DefaultLegThresholdM);

    private static TransitAdvice TransitFor(TransitPlanner planner, BuildTripPlanCommand command, RouteLeg leg, GeoPoint from, GeoPoint to) =>
        planner.Plan(from, to, DateOnly.FromDateTime(command.Now), command.Now.Hour * 60 + command.Now.Minute,
            command.Profile.MaxDistanceWithoutRestM ?? TransitRules.DefaultMaxWalkToStopM, command.Profile.WalkingSpeedKmh, leg.DistanceM);

    /// <summary>
    /// Ławek szukamy, gdy odcinek jest dłuższy niż limit marszu z profilu. Trasa szacowana w linii prostej
    /// nie mówi, którędy się idzie, więc ławek przy niej nie wskazujemy.
    /// </summary>
    private static bool NeedsRest(RouteLeg leg, NeedsProfile profile, out int maxWithoutRestM)
    {
        maxWithoutRestM = profile.MaxDistanceWithoutRestM ?? 0;
        return maxWithoutRestM > 0 && !leg.IsEstimated && leg.DistanceM > maxWithoutRestM;
    }

    private static List<string> LegWarnings(
        RouteLeg leg, NeedsProfile profile, bool hasTransit, IReadOnlyList<RestStop> rests, bool detourFailed)
    {
        var warnings = new List<string>(leg.Warnings);
        if (detourFailed)
            warnings.Add("Trasa prowadzi przez utrudnienie potwierdzone przez urząd, istotne dla Twojego profilu. Nie udało się wyznaczyć drogi, która je omija.");
        if (profile.MaxDistanceWithoutRestM is { } max && leg.DistanceM > max)
        {
            warnings.Add((hasTransit, rests.Count > 0) switch
            {
                (true, true) => $"Odcinek dłuższy niż {max} m. Możesz podjechać komunikacją miejską albo iść pieszo i odpocząć na ławce po drodze.",
                (true, false) => $"Odcinek dłuższy niż {max} m. Możesz podjechać komunikacją miejską.",
                (false, true) => $"Odcinek dłuższy niż {max} m. Po drodze są ławki, na których możesz odpocząć.",
                _ => $"Odcinek dłuższy niż {max} m bez odpoczynku. Zaplanuj przerwę po drodze."
            });
            if (rests.Count > 0 && RestStopRules.LongestStretchM(rests, leg.DistanceM) is var stretch && stretch > max)
                warnings.Add($"Ławek nie ma na całej trasie: najdłuższy fragment bez przerwy ma około {Math.Round(stretch / 10) * 10} m.");
        }
        return warnings;
    }
}
