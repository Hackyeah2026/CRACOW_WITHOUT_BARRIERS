using System.Globalization;
using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Infrastructure.Routing;

namespace Infrastructure.Browser;

/// <summary>
/// Pyta host o trasę po ulicach (POST api/route). Odpowiedzi trzyma w IndexedDB, żeby oszczędzać limit API.
/// Gdy host lub silnik routingu nie odpowiada, zwraca szacunek w linii prostej zamiast błędu.
/// </summary>
internal sealed class HostRoutingClient(HttpClient http, ILocalStore store) : IRoutingClient
{
    public async Task<Result<RouteLeg>> GetRouteAsync(RouteRequest request, CancellationToken ct)
    {
        var query = RouteQuery.FromRequest(request);
        var key = CacheKey(query);

        var route = await TryAsync(() => store.GetAsync<RouteResponse>(LocalStores.RouteCache, key));
        if (route is null)
        {
            route = await FetchAsync(query, ct);
            if (route is not null)
                await TryAsync(async () => { await store.PutAsync(LocalStores.RouteCache, key, route); return route; });
        }

        return Result.Success(route is null
            ? StraightLineRoute.Estimate(request)
            : new RouteLeg(route.DistanceM, Duration.Minutes(route.DistanceM, request.Profile.WalkingSpeedKmh),
                route.Geometry, IsEstimated: false, route.Warnings));
    }

    private async Task<RouteResponse?> FetchAsync(RouteQuery query, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/route", query, DomainJson.Options, ct);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<RouteResponse>(DomainJson.Options, ct)
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // Cache jest ułatwieniem: jego awaria nie może zablokować układania planu.
    private static async Task<T?> TryAsync<T>(Func<Task<T?>> action) where T : class
    {
        try { return await action(); }
        catch (Exception) { return null; }
    }

    private static string CacheKey(RouteQuery q) => string.Create(CultureInfo.InvariantCulture,
        $"{q.From.Lat:F5},{q.From.Lon:F5}>{q.To.Lat:F5},{q.To.Lon:F5}|w{(q.Wheelchair ? 1 : 0)}s{(q.AvoidSteps ? 1 : 0)}k{q.MaxKerbCm}");
}
