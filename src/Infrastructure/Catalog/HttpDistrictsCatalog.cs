using System.Net;
using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Places;

namespace Infrastructure.Catalog;

/// <summary>Granice dzielnic ze statycznego pliku data/{miasto}/districts.json, zapisywanego przez import.</summary>
internal sealed class HttpDistrictsCatalog(HttpClient http) : IDistrictsCatalog
{
    private readonly Dictionary<string, DistrictMap?> _maps = [];

    public async Task<DistrictMap?> GetAsync(string cityId, CancellationToken ct)
    {
        if (_maps.TryGetValue(cityId, out var cached))
            return cached;
        try
        {
            using var response = await http.GetAsync($"data/{Uri.EscapeDataString(cityId)}/districts.json", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return _maps[cityId] = null;
            response.EnsureSuccessStatusCode();
            return _maps[cityId] = await response.Content.ReadFromJsonAsync<DistrictMap>(DomainJson.Options, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
