using System.Net;
using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Businesses;
using Domain.Hazards;
using Domain.Reports;

namespace Infrastructure.Browser;

/// <summary>Zgłoszenia zalogowanego mieszkańca: wysyłka (POST api/reports) i lista własnych ze stanem obsługi.</summary>
internal sealed class HostReportsClient(HttpClient http) : IReportsClient
{
    public Task<Result<ReportReceipt>> SubmitAsync(ReportDraft draft, CancellationToken ct) =>
        HostApi.SendAsync<ReportReceipt>(() => http.PostAsJsonAsync("api/reports", draft, DomainJson.Options, ct), ct);

    public Task<Result<IReadOnlyList<ReportStatusView>>> GetMineAsync(CancellationToken ct) =>
        HostApi.SendAsync<IReadOnlyList<ReportStatusView>>(() => http.GetAsync("api/reports/mine", ct), ct);
}

/// <summary>Punkty z utrudnieniami (api/hazards): wysyłka, stan własnych punktów i lista potwierdzonych przez urząd.</summary>
internal sealed class HostHazardsClient(HttpClient http) : IHazardsClient
{
    public Task<Result<HazardReceipt>> SubmitAsync(HazardDraft draft, CancellationToken ct) =>
        HostApi.SendAsync<HazardReceipt>(() => http.PostAsJsonAsync("api/hazards", draft, DomainJson.Options, ct), ct);

    public Task<Result<IReadOnlyList<HazardStatusView>>> GetMineAsync(CancellationToken ct) =>
        HostApi.SendAsync<IReadOnlyList<HazardStatusView>>(() => http.GetAsync("api/hazards/mine", ct), ct);

    public Task<Result<IReadOnlyList<VerifiedHazard>>> GetVerifiedAsync(string cityId, CancellationToken ct) =>
        HostApi.SendAsync<IReadOnlyList<VerifiedHazard>>(() => http.GetAsync($"api/hazards?cityId={Uri.EscapeDataString(cityId)}", ct), ct);
}

/// <summary>Panel urzędnika. Ciasteczko sesji wysyła przeglądarka (to samo pochodzenie), kod go nie widzi.</summary>
internal sealed class HostOfficialClient(HttpClient http) : IOfficialClient
{
    public Task<Result<OfficialProfile>> LoginAsync(OfficialLogin login, CancellationToken ct) =>
        HostApi.SendAsync<OfficialProfile>(() => http.PostAsJsonAsync("api/official/login", login, DomainJson.Options, ct), ct,
            unauthorized: "Nieprawidłowy login lub hasło.");

    public async Task LogoutAsync(CancellationToken ct)
    {
        try { (await http.PostAsync("api/official/logout", null, ct)).Dispose(); }
        catch (HttpRequestException) { }
    }

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

/// <summary>Zamienia odpowiedzi hosta na <see cref="Result"/> z komunikatem zrozumiałym dla użytkownika.</summary>
internal static class HostApi
{
    public static async Task<Result<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send, CancellationToken ct,
        string unauthorized = "Nie jesteś zalogowany albo sesja wygasła.") where T : notnull
    {
        try
        {
            using var response = await send();
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(DomainJson.Options, ct);
                return value is null ? Result.Failure<T>("Pusta odpowiedź serwera.") : Result.Success(value);
            }

            return Result.Failure<T>(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => unauthorized,
                HttpStatusCode.Forbidden => "Brak uprawnień.",
                HttpStatusCode.NotFound => "Nie znaleziono.",
                HttpStatusCode.TooManyRequests => "Za dużo prób w krótkim czasie. Spróbuj ponownie za kilka minut.",
                HttpStatusCode.ServiceUnavailable => "Baza zgłoszeń jest chwilowo niedostępna. Spróbuj później.",
                HttpStatusCode.BadRequest or HttpStatusCode.Conflict => await ProblemDetailAsync(response, ct) ?? "Nieprawidłowe dane.",
                _ => "Serwer nie przyjął żądania. Spróbuj ponownie."
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return Result.Failure<T>("Brak połączenia z serwerem.");
        }
    }

    private static async Task<string?> ProblemDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<Problem>(DomainJson.Options, ct);
            return problem?.Detail;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private sealed record Problem(string? Detail);
}
