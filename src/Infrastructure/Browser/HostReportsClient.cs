using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Reports;

namespace Infrastructure.Browser;

/// <summary>Zgłoszenia zalogowanego mieszkańca: wysyłka (POST api/reports), lista własnych ze stanem obsługi i publiczna lista potwierdzonych braków.</summary>
internal sealed class HostReportsClient(HttpClient http) : IReportsClient
{
    // Listę czyta każde zapytanie o miejsca, więc jedna odpowiedź (także błąd) wystarcza na krótki czas.
    private static readonly TimeSpan ConfirmedFreshness = TimeSpan.FromSeconds(30);
    private readonly Dictionary<string, (DateTime LoadedAt, Task<Result<IReadOnlyList<ConfirmedGap>>> Gaps)> _confirmed = [];

    public Task<Result<ReportReceipt>> SubmitAsync(ReportDraft draft, CancellationToken ct) =>
        HostApi.SendAsync<ReportReceipt>(() => http.PostAsJsonAsync("api/reports", draft, DomainJson.Options, ct), ct);

    public Task<Result<IReadOnlyList<ReportStatusView>>> GetMineAsync(CancellationToken ct) =>
        HostApi.SendAsync<IReadOnlyList<ReportStatusView>>(() => http.GetAsync("api/reports/mine", ct), ct);

    public Task<Result<IReadOnlyList<ConfirmedGap>>> GetConfirmedAsync(string cityId, CancellationToken ct)
    {
        if (_confirmed.TryGetValue(cityId, out var cached) && DateTime.UtcNow - cached.LoadedAt < ConfirmedFreshness)
            return cached.Gaps;

        var gaps = HostApi.SendAsync<IReadOnlyList<ConfirmedGap>>(
            () => http.GetAsync($"api/reports/confirmed?cityId={Uri.EscapeDataString(cityId)}", CancellationToken.None), CancellationToken.None);
        _confirmed[cityId] = (DateTime.UtcNow, gaps);
        return gaps;
    }
}
