using Domain.Needs;
using Domain.Places;

namespace Application.Abstractions;

public interface IPlaceCatalog
{
    Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct);
    Task<IReadOnlyList<Place>> GetAllAsync(string cityId, CancellationToken ct);
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

public sealed record RouteLeg(double DistanceM, double DurationMin, IReadOnlyList<GeoPoint> Geometry, bool IsEstimated);

public interface IRoutingClient
{
    Task<Result<RouteLeg>> GetRouteAsync(RouteRequest request, CancellationToken ct);
}
