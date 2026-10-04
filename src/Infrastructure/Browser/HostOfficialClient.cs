using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Businesses;
using Domain.Hazards;
using Domain.Reports;

namespace Infrastructure.Browser;

/// <summary>Panel urzędnika (api/official). Ciasteczko sesji wysyła przeglądarka (to samo pochodzenie), kod go nie widzi.</summary>
internal sealed class HostOfficialClient(HttpClient http) : IOfficialClient
{
    public Task<Result<OfficialProfile>> LoginAsync(OfficialLogin login, CancellationToken ct) =>
        HostApi.SendAsync<OfficialProfile>(() => http.PostAsJsonAsync("api/official/login", login, DomainJson.Options, ct), ct,
            unauthorized: "Nieprawidłowy login lub hasło.");

    // Wylogowanie bez odpowiedzi hosta i tak kończy pracę w panelu: sesja wygaśnie sama.
    public Task LogoutAsync(CancellationToken ct) => HostApi.SendAsync(() => http.PostAsync("api/official/logout", null, ct), ct);

    public async Task<OfficialProfile?> GetCurrentAsync(CancellationToken ct)
    {
        var result = await HostApi.SendAsync<OfficialProfile>(() => http.GetAsync("api/official/me", ct), ct);
        return result.IsSuccess ? result.Value : null;
    }

    public Task<Result<IReadOnlyList<Report>>> GetReportsAsync(ReportFilter filter, CancellationToken ct)
    {
        var query = new List<string>();
        if (filter.CityId is { } city)
            query.Add($"cityId={Uri.EscapeDataString(city)}");
        if (filter.Status is { } status)
            query.Add($"status={status}");
        if (filter.PlaceId is { } place)
            query.Add($"placeId={Uri.EscapeDataString(place)}");
        var url = query.Count == 0 ? "api/official/reports" : $"api/official/reports?{string.Join('&', query)}";

        return HostApi.SendAsync<IReadOnlyList<Report>>(() => http.GetAsync(url, ct), ct);
    }

    public Task<Result<Report>> UpdateStatusAsync(string id, ReportStatusChange change, CancellationToken ct) =>
        HostApi.SendAsync<Report>(
            () => http.PatchAsJsonAsync($"api/official/reports/{Uri.EscapeDataString(id)}", change, DomainJson.Options, ct), ct);

    public Task<Result<IReadOnlyList<Hazard>>> GetHazardsAsync(string? cityId, CancellationToken ct) =>
        HostApi.SendAsync<IReadOnlyList<Hazard>>(() => http.GetAsync(
            cityId is null ? "api/official/hazards" : $"api/official/hazards?cityId={Uri.EscapeDataString(cityId)}", ct), ct);

    public Task<Result<Hazard>> ReviewHazardAsync(string id, HazardReview review, CancellationToken ct) =>
        HostApi.SendAsync<Hazard>(
            () => http.PatchAsJsonAsync($"api/official/hazards/{Uri.EscapeDataString(id)}", review, DomainJson.Options, ct), ct);

    public Task<Result<IReadOnlyList<BusinessAccount>>> GetBusinessesAsync(string? cityId, CancellationToken ct) =>
        HostApi.SendAsync<IReadOnlyList<BusinessAccount>>(() => http.GetAsync(
            cityId is null ? "api/official/businesses" : $"api/official/businesses?cityId={Uri.EscapeDataString(cityId)}", ct), ct);

    public Task<Result<BusinessAccount>> ReviewBusinessAsync(string login, BusinessReview review, CancellationToken ct) =>
        HostApi.SendAsync<BusinessAccount>(
            () => http.PatchAsJsonAsync($"api/official/businesses/{Uri.EscapeDataString(login)}", review, DomainJson.Options, ct), ct);
}
