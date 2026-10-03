using Domain.Needs;
using Domain.Places;
using Domain.Transit;

namespace Application.Abstractions;

public interface IPlaceCatalog
{
    Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct);
    Task<IReadOnlyList<Place>> GetAllAsync(string cityId, CancellationToken ct);
}

public interface ITransitCatalog
{
    /// <summary>Sieć komunikacji miejskiej albo null, gdy dla miasta nie ma takich danych.</summary>
    Task<TransitNetwork?> GetNetworkAsync(string cityId, CancellationToken ct);
}

/// <summary>Magazyn na urządzeniu użytkownika (IndexedDB). Dane o zdrowiu nie trafiają na serwer.</summary>
public interface ILocalStore
{
    Task<T?> GetAsync<T>(string store, string key);
    Task PutAsync<T>(string store, string key, T value);
    Task DeleteAsync(string store, string key);
    Task<IReadOnlyList<T>> ListAsync<T>(string store);
}

public static class LocalStores
{
    public const string Profile = "profile";
    public const string Plans = "plans";
    public const string Reports = "reports";
    public const string RouteCache = "routeCache";
}

public sealed record RouteRequest(GeoPoint From, GeoPoint To, NeedsProfile Profile);

public sealed record RouteLeg(
    double DistanceM, double DurationMin, IReadOnlyList<GeoPoint> Geometry, bool IsEstimated, IReadOnlyList<string> Warnings);

public interface IRoutingClient
{
    Task<Result<RouteLeg>> GetRouteAsync(RouteRequest request, CancellationToken ct);
}

/// <summary>
/// Parametry trasy wysyłane poza urządzenie. Celowo nie zawierają całego profilu potrzeb,
/// tylko to, czego wymaga silnik routingu.
/// </summary>
public sealed record RouteQuery(GeoPoint From, GeoPoint To, bool Wheelchair, bool AvoidSteps, double? MaxKerbCm)
{
    public static RouteQuery FromRequest(RouteRequest request) => new(
        request.From, request.To, request.Profile.StepFreeRequired, request.Profile.AvoidStairs, request.Profile.MaxThresholdCm);
}

public sealed record RouteResponse(double DistanceM, IReadOnlyList<GeoPoint> Geometry, IReadOnlyList<string> Warnings);

/// <summary>Silnik routingu po stronie hosta (klucz API nie trafia do przeglądarki).</summary>
public interface IRouteProvider
{
    Task<Result<RouteResponse>> GetRouteAsync(RouteQuery query, CancellationToken ct);
}
