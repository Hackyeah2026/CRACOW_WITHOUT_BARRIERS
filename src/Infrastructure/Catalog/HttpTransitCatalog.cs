using System.Net;
using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Transit;

namespace Infrastructure.Catalog;

/// <summary>Sieć komunikacji ze statycznego pliku data/{miasto}/transit.json. Brak pliku oznacza brak danych dla miasta.</summary>
internal sealed class HttpTransitCatalog(HttpClient http) : ITransitCatalog
{
    private readonly Dictionary<string, TransitNetwork?> _networks = [];

    public async Task<TransitNetwork?> GetNetworkAsync(string cityId, CancellationToken ct)
    {
        if (_networks.TryGetValue(cityId, out var cached))
            return cached;

        try
        {
            using var response = await http.GetAsync($"data/{Uri.EscapeDataString(cityId)}/transit.json", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return _networks[cityId] = null;

            response.EnsureSuccessStatusCode();
            return _networks[cityId] = await response.Content.ReadFromJsonAsync<TransitNetwork>(DomainJson.Options, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            // Podpowiedzi komunikacji są dodatkiem: bez nich plan nadal się układa.
            return null;
        }
    }
}
