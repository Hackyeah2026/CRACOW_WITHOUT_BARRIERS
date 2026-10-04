using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Hazards;

namespace Infrastructure.Browser;

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
