using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Places;

namespace Infrastructure.Catalog;

/// <summary>
/// Katalog czytany ze statycznych plików aplikacji: data/cities.json i po jednym pliku na kategorię
/// (data/{miasto}/places/{kategoria}.json, spis w index.json). Plik kategorii jest pobierany przy pierwszym użyciu.
/// </summary>
internal sealed class HttpPlaceCatalog(HttpClient http) : IPlaceCatalog
{
    private IReadOnlyList<City>? _cities;
    private readonly Dictionary<string, Task<PlaceIndex>> _indexes = [];
    private readonly Dictionary<(string CityId, PlaceCategory Category), Task<IReadOnlyList<Place>>> _files = [];

    public async Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct) =>
        _cities ??= await http.GetFromJsonAsync<List<City>>("data/cities.json", DomainJson.Options, ct) ?? [];

    public async Task<IReadOnlyList<PlaceCategoryCount>> GetCategoriesAsync(string cityId, CancellationToken ct) =>
        (await IndexAsync(cityId)).Categories;

    public async Task<IReadOnlyList<Place>> GetAsync(string cityId, IReadOnlyCollection<PlaceCategory> categories, CancellationToken ct)
    {
        var available = (await IndexAsync(cityId)).Categories.Select(c => c.Category).ToHashSet();
        var files = await Task.WhenAll(categories.Distinct().Where(available.Contains).Select(c => FileAsync(cityId, c)));
        return files.Length == 1 ? files[0] : files.SelectMany(f => f).ToList();
    }

    public async Task<Place?> FindAsync(string cityId, string placeId, CancellationToken ct)
    {
        var index = await IndexAsync(cityId);

        // Najpierw to, co już jest w pamięci (zwykle lista, z której użytkownik przyszedł).
        foreach (var (key, file) in _files.ToList())
        {
            if (key.CityId == cityId && file.IsCompletedSuccessfully && file.Result.FirstOrDefault(p => p.Id == placeId) is { } cached)
                return cached;
        }

        // Potem brakujące pliki od najmniejszych, żeby wejście z bezpośredniego adresu nie zaczynało od tysięcy ławek.
        foreach (var category in index.Categories.OrderBy(c => c.Count).Select(c => c.Category))
        {
            if (_files.TryGetValue((cityId, category), out var loaded) && loaded.IsCompletedSuccessfully)
                continue;
            if ((await FileAsync(cityId, category)).FirstOrDefault(p => p.Id == placeId) is { } found)
                return found;
        }
        return null;
    }

    // Trzymamy zadania, a nie wyniki: równoległe zapytania o ten sam plik czekają na jedno pobranie.
    private Task<PlaceIndex> IndexAsync(string cityId)
    {
        if (!_indexes.TryGetValue(cityId, out var index))
            _indexes[cityId] = index = LoadAsync<PlaceIndex>(cityId, "index", new PlaceIndex(default, []));
        return index;
    }

    private Task<IReadOnlyList<Place>> FileAsync(string cityId, PlaceCategory category)
    {
        if (!_files.TryGetValue((cityId, category), out var file))
            _files[(cityId, category)] = file = LoadAsync<IReadOnlyList<Place>>(cityId, category.ToString(), []);
        return file;
    }

    private async Task<T> LoadAsync<T>(string cityId, string name, T empty) =>
        await http.GetFromJsonAsync<T>($"data/{Uri.EscapeDataString(cityId)}/places/{name}.json", DomainJson.Options) ?? empty;
}
