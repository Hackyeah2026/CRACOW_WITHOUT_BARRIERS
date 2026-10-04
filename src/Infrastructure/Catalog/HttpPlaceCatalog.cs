using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Places;
using Microsoft.JSInterop;

namespace Infrastructure.Catalog;

/// <summary>
/// Katalog czytany ze statycznych plików aplikacji: data/cities.json i po jednym pliku na kategorię
/// (data/{miasto}/places/{kategoria}.json, spis w index.json). Plik kategorii jest pobierany przy pierwszym użyciu.
/// Miejsca wyszukane po identyfikatorze są zapamiętywane na urządzeniu: plan otwarty z bezpośredniego adresu
/// nie musi wtedy pobierać i czytać plików wszystkich kategorii, co na kilka-kilkadziesiąt sekund blokowało stronę.
/// Tak samo zapamiętujemy, że identyfikatora nie ma w bieżącym imporcie (miejsce z planu sprzed ponownego importu).
/// </summary>
internal sealed class HttpPlaceCatalog(HttpClient http, ILocalStore store) : IPlaceCatalog
{
    private readonly HashSet<string> _remembered = [];
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
        var searched = new HashSet<PlaceCategory>();
        foreach (var (key, file) in _files.ToList())
        {
            if (key.CityId != cityId || !file.IsCompletedSuccessfully)
                continue;
            if (file.Result.FirstOrDefault(p => p.Id == placeId) is { } cached)
                return await RememberAsync(cached, index.GeneratedOn);
            searched.Add(key.Category);
        }

        if (await RecallAsync(cityId, placeId, index.GeneratedOn) is { } remembered)
            return remembered.Place;

        // Potem pozostałe pliki od najmniejszych, żeby wejście z bezpośredniego adresu nie zaczynało od tysięcy ławek.
        // Pomijamy tylko pliki przeszukane wyżej: plik, który doczytał się w międzyczasie, też trzeba sprawdzić.
        foreach (var category in index.Categories.OrderBy(c => c.Count).Select(c => c.Category).Where(c => !searched.Contains(c)))
        {
            if ((await FileAsync(cityId, category)).FirstOrDefault(p => p.Id == placeId) is { } found)
                return await RememberAsync(found, index.GeneratedOn);
        }

        // Cały katalog przeczytany i miejsca nie ma. Zapamiętujemy to, żeby kolejne pytanie o ten identyfikator
        // (plan sprzed ponownego importu, stary adres karty miejsca) nie czytało znowu wszystkich plików.
        await RememberAsync(cityId, placeId, new CachedPlace(index.GeneratedOn, null));
        return null;
    }

    /// <summary>
    /// Wpis zapamiętany na urządzeniu, o ile pochodzi z tego samego importu co bieżący katalog.
    /// Wpis bez miejsca oznacza, że identyfikatora w tym imporcie nie ma.
    /// </summary>
    private async Task<CachedPlace?> RecallAsync(string cityId, string placeId, DateOnly generatedOn)
    {
        try
        {
            var key = CacheKey(cityId, placeId);
            if (await store.GetAsync<CachedPlace>(LocalStores.PlaceCache, key) is not { } cached || cached.GeneratedOn != generatedOn)
                return null;

            // Wpisu o braku nie oznaczamy jako zapisanego: gdy miejsce jednak się znajdzie, ma go zastąpić.
            if (cached.Place is not null)
                _remembered.Add(key);
            return cached;
        }
        catch (Exception ex) when (ex is JSException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task<Place> RememberAsync(Place place, DateOnly generatedOn)
    {
        await RememberAsync(place.CityId, place.Id, new CachedPlace(generatedOn, place));
        return place;
    }

    private async Task RememberAsync(string cityId, string placeId, CachedPlace entry)
    {
        // Pamięć na urządzeniu tylko przyspiesza: gdy zapis się nie uda, katalog działa dalej z plików.
        try
        {
            if (_remembered.Add(CacheKey(cityId, placeId)))
                await store.PutAsync(LocalStores.PlaceCache, CacheKey(cityId, placeId), entry);
        }
        catch (JSException) { }
    }

    private static string CacheKey(string cityId, string placeId) => $"{cityId}:{placeId}";

    /// <param name="Place">Null, gdy katalog z tego importu nie ma miejsca o tym identyfikatorze.</param>
    private sealed record CachedPlace(DateOnly GeneratedOn, Place? Place);

    private Task<PlaceIndex> IndexAsync(string cityId) =>
        OnceAsync(_indexes, cityId, () => LoadAsync<PlaceIndex>(cityId, "index", new PlaceIndex(default, [])));

    private Task<IReadOnlyList<Place>> FileAsync(string cityId, PlaceCategory category) =>
        OnceAsync(_files, (cityId, category), () => LoadAsync<IReadOnlyList<Place>>(cityId, category.ToString(), []));

    /// <summary>
    /// Trzymamy zadania, a nie wyniki: równoległe zapytania o ten sam plik czekają na jedno pobranie.
    /// Nieudane pobranie nie zostaje w pamięci, żeby chwilowy brak sieci nie blokował pliku do przeładowania strony.
    /// </summary>
    private static Task<T> OnceAsync<TKey, T>(Dictionary<TKey, Task<T>> loads, TKey key, Func<Task<T>> load) where TKey : notnull
    {
        if (!loads.TryGetValue(key, out var loading) || loading.IsFaulted || loading.IsCanceled)
            loads[key] = loading = load();
        return loading;
    }

    private async Task<T> LoadAsync<T>(string cityId, string name, T empty) =>
        await http.GetFromJsonAsync<T>($"data/{Uri.EscapeDataString(cityId)}/places/{name}.json", DomainJson.Options) ?? empty;
}
