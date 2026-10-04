using Domain.Needs;
using Domain.Places;
using Domain.Transit;

namespace Application.Abstractions;

public interface IPlaceCatalog
{
    Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct);
    /// <summary>Kategorie, dla których miasto ma dane, z liczbą miejsc.</summary>
    Task<IReadOnlyList<PlaceCategoryCount>> GetCategoriesAsync(string cityId, CancellationToken ct);

    /// <summary>Miejsca z podanych kategorii. Katalog jest podzielony na kategorie, żeby widok nie pobierał całego miasta.</summary>
    Task<IReadOnlyList<Place>> GetAsync(string cityId, IReadOnlyCollection<PlaceCategory> categories, CancellationToken ct);

    /// <summary>Miejsce o podanym identyfikatorze z dowolnej kategorii; null, gdy go nie ma.</summary>
    Task<Place?> FindAsync(string cityId, string placeId, CancellationToken ct);
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
}

/// <summary>Magazyny w IndexedDB; te same nazwy są w wwwroot/js/localStore.js.</summary>
public static class LocalStores
{
    /// <summary>Profil potrzeb (tymczasowy i profile kont) oraz stan sesji: tryb, plan bez konta, otwarty plan.</summary>
    public const string Profile = "profile";
    public const string RouteCache = "routeCache";
    public const string PlaceCache = "placeCache";
}

/// <param name="Avoid">Punkty, których trasa ma nie przecinać: utrudnienia potwierdzone przez urząd.</param>
public sealed record RouteRequest(GeoPoint From, GeoPoint To, NeedsProfile Profile, IReadOnlyList<GeoPoint>? Avoid = null);

public sealed record RouteLeg(
    double DistanceM, double DurationMin, IReadOnlyList<GeoPoint> Geometry, bool IsEstimated, IReadOnlyList<string> Warnings);

public interface IRoutingClient
{
    Task<Result<RouteLeg>> GetRouteAsync(RouteRequest request, CancellationToken ct);
}

/// <summary>
/// Parametry trasy wysyłane poza urządzenie. Celowo nie zawierają całego profilu potrzeb,
/// tylko to, czego wymaga silnik routingu. Punkty do ominięcia to położenia publicznych, potwierdzonych utrudnień.
/// </summary>
public sealed record RouteQuery(
    GeoPoint From, GeoPoint To, bool Wheelchair, bool AvoidSteps, double? MaxKerbCm, IReadOnlyList<GeoPoint>? Avoid = null)
{
    public static RouteQuery FromRequest(RouteRequest request) => new(
        request.From, request.To, request.Profile.StepFreeRequired, request.Profile.AvoidStairs, request.Profile.MaxThresholdCm,
        request.Avoid is { Count: > 0 } ? request.Avoid : null);
}

public sealed record RouteResponse(double DistanceM, IReadOnlyList<GeoPoint> Geometry, IReadOnlyList<string> Warnings);

/// <summary>Silnik routingu po stronie hosta (klucz API nie trafia do przeglądarki).</summary>
public interface IRouteProvider
{
    Task<Result<RouteResponse>> GetRouteAsync(RouteQuery query, CancellationToken ct);
}
