using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Businesses;

namespace Infrastructure.Browser;

/// <summary>Konto firmowe (api/business) i publiczna lista miejsc z certyfikatem (api/businesses).</summary>
internal sealed class HostBusinessClient(HttpClient http) : IBusinessClient
{
    // Listę czyta każde zapytanie o miejsca, więc jedna odpowiedź (także błąd) wystarcza na krótki czas.
    private static readonly TimeSpan CertifiedFreshness = TimeSpan.FromSeconds(30);
    private readonly Dictionary<string, (DateTime LoadedAt, Task<Result<IReadOnlyList<CertifiedPlace>>> Places)> _certified = [];

    public Task<Result<BusinessAccountView>> ApplyAsync(BusinessApplicationDraft draft, CancellationToken ct) =>
        HostApi.SendAsync<BusinessAccountView>(() => http.PostAsJsonAsync("api/business/application", draft, DomainJson.Options, ct), ct);

    public Task<Result<MyBusiness>> GetMineAsync(CancellationToken ct) =>
        HostApi.SendAsync<MyBusiness>(() => http.GetAsync("api/business/mine", ct), ct);

    public async Task<Result<BusinessAccountView>> SaveFeaturesAsync(BusinessFeaturesUpdate update, CancellationToken ct)
    {
        var result = await HostApi.SendAsync<BusinessAccountView>(
            () => http.PutAsJsonAsync("api/business/features", update, DomainJson.Options, ct), ct);
        // Właściciel ma od razu zobaczyć swoje oznaczenia na karcie miejsca.
        _certified.Clear();
        return result;
    }

    public Task<Result<IReadOnlyList<CertifiedPlace>>> GetCertifiedAsync(string cityId, CancellationToken ct)
    {
        if (_certified.TryGetValue(cityId, out var cached) && DateTime.UtcNow - cached.LoadedAt < CertifiedFreshness)
            return cached.Places;

        var places = HostApi.SendAsync<IReadOnlyList<CertifiedPlace>>(
            () => http.GetAsync($"api/businesses?cityId={Uri.EscapeDataString(cityId)}", CancellationToken.None), CancellationToken.None);
        _certified[cityId] = (DateTime.UtcNow, places);
        return places;
    }
}
