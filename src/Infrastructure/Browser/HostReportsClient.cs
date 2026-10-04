using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
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
