using System.Net;
using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Places;

namespace Infrastructure.Catalog;

/// <summary>Siatka hałasu ze statycznego pliku data/{miasto}/noise.json. Brak pliku oznacza, że miasto nie udostępnia mapy hałasu.</summary>
internal sealed class HttpNoiseCatalog(HttpClient http) : INoiseCatalog
{
    private readonly Dictionary<string, NoiseGrid?> _grids = [];

    public async Task<NoiseGrid?> GetGridAsync(string cityId, CancellationToken ct)
    {
        if (_grids.TryGetValue(cityId, out var cached))
            return cached;

        try
        {
            using var response = await http.GetAsync($"data/{Uri.EscapeDataString(cityId)}/noise.json", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return _grids[cityId] = null;

            response.EnsureSuccessStatusCode();
            return _grids[cityId] = await response.Content.ReadFromJsonAsync<NoiseGrid>(DomainJson.Options, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            // Hałas na trasie to dodatek: bez siatki plan nadal się układa, tylko bez tego ostrzeżenia.
            return null;
        }
    }
}
