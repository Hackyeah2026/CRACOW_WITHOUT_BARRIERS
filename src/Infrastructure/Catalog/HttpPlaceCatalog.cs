using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Places;

namespace Infrastructure.Catalog;

/// <summary>Katalog czytany ze statycznych plików aplikacji (data/cities.json, data/{miasto}/places.json).</summary>
internal sealed class HttpPlaceCatalog(HttpClient http) : IPlaceCatalog
{
    private IReadOnlyList<City>? _cities;
    private readonly Dictionary<string, IReadOnlyList<Place>> _places = [];

    public async Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct) =>
        _cities ??= await http.GetFromJsonAsync<List<City>>("data/cities.json", DomainJson.Options, ct) ?? [];

    public async Task<IReadOnlyList<Place>> GetAllAsync(string cityId, CancellationToken ct)
    {
        if (_places.TryGetValue(cityId, out var cached))
            return cached;

        var places = await http.GetFromJsonAsync<List<Place>>($"data/{Uri.EscapeDataString(cityId)}/places.json", DomainJson.Options, ct) ?? [];
        return _places[cityId] = places;
    }
}
