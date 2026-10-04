using System.Net;
using System.Text;
using System.Text.Json;
using Application.Abstractions;
using Domain;
using Domain.Places;
using Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class HttpPlaceCatalogTests
{
    private static readonly Place Museum = new("osm-node-1", "krakow", "Sukiennice", PlaceCategory.Museum, 50.0617, 19.9373, null, null, []);

    /// <summary>Identyfikator z planu sprzed ponownego importu: w bieżącym katalogu go nie ma.</summary>
    private const string Removed = "osm-node-3518909828";

    /// <summary>Katalog jak po otwarciu aplikacji; ten sam magazyn w kolejnych katalogach to to samo urządzenie.</summary>
    private static IPlaceCatalog Catalog(StaticFiles files, ILocalStore? store = null) => new ServiceCollection()
        .AddSingleton(new HttpClient(files) { BaseAddress = new Uri("http://localhost/") })
        .AddBrowserInfrastructure()
        // Magazyn na urządzeniu wymaga przeglądarki; w teście zastępuje go pamięć.
        .AddSingleton(store ?? new MemoryStore())
        .BuildServiceProvider()
        .GetRequiredService<IPlaceCatalog>();

    [Fact]
    public async Task Category_file_is_downloaded_once()
    {
        var files = new StaticFiles();
        var catalog = Catalog(files);

        Assert.Equal(Museum.Id, Assert.Single(await catalog.GetAsync("krakow", [PlaceCategory.Museum], CancellationToken.None)).Id);
        Assert.Equal(Museum.Id, (await catalog.FindAsync("krakow", Museum.Id, CancellationToken.None))!.Id);

        Assert.Equal(1, files.Requests("Museum.json"));
        Assert.Equal(1, files.Requests("index.json"));
    }

    [Fact]
    public async Task Failed_download_is_retried_instead_of_being_remembered()
    {
        var files = new StaticFiles { FailuresLeft = 1 };
        var catalog = Catalog(files);

        await Assert.ThrowsAsync<HttpRequestException>(() => catalog.GetAsync("krakow", [PlaceCategory.Museum], CancellationToken.None));

        Assert.Equal(Museum.Id, Assert.Single(await catalog.GetAsync("krakow", [PlaceCategory.Museum], CancellationToken.None)).Id);
    }

    [Fact]
    public async Task Category_without_a_file_gives_no_places()
    {
        var catalog = Catalog(new StaticFiles());

        Assert.Empty(await catalog.GetAsync("krakow", [PlaceCategory.Bench], CancellationToken.None));
        Assert.Null(await catalog.FindAsync("krakow", "nie-ma", CancellationToken.None));
    }

    [Fact]
    public async Task Missing_place_is_remembered_on_the_device_so_the_catalog_is_not_read_again()
    {
        var store = new MemoryStore();
        var first = new StaticFiles();
        Assert.Null(await Catalog(first, store).FindAsync("krakow", Removed, CancellationToken.None));
        Assert.Equal(1, first.Requests("Museum.json"));

        // Kolejne otwarcie aplikacji: o braku wiadomo z urządzenia, więc pliki kategorii nie są pobierane.
        var second = new StaticFiles();
        Assert.Null(await Catalog(second, store).FindAsync("krakow", Removed, CancellationToken.None));
        Assert.Equal(0, second.Requests("Museum.json"));
    }

    [Fact]
    public async Task Missing_place_is_looked_up_again_after_the_next_import()
    {
        var store = new MemoryStore();
        Assert.Null(await Catalog(new StaticFiles(), store).FindAsync("krakow", Removed, CancellationToken.None));

        var reimported = new StaticFiles { GeneratedOn = new DateOnly(2026, 10, 10), Places = [Museum with { Id = Removed }] };

        Assert.Equal(Removed, (await Catalog(reimported, store).FindAsync("krakow", Removed, CancellationToken.None))!.Id);
    }

    [Fact]
    public async Task Place_from_a_loaded_file_wins_over_a_remembered_absence()
    {
        var store = new MemoryStore();
        Assert.Null(await Catalog(new StaticFiles(), store).FindAsync("krakow", Removed, CancellationToken.None));

        // Drugi import tego samego dnia: data się nie zmienia, ale miejsce jest na liście, z której przychodzi użytkownik.
        var catalog = Catalog(new StaticFiles { Places = [Museum with { Id = Removed }] }, store);
        await catalog.GetAsync("krakow", [PlaceCategory.Museum], CancellationToken.None);

        Assert.Equal(Removed, (await catalog.FindAsync("krakow", Removed, CancellationToken.None))!.Id);
    }

    [Fact]
    public async Task Failed_download_does_not_mark_the_place_as_missing()
    {
        var catalog = Catalog(new StaticFiles { FailuresLeft = 1 });

        await Assert.ThrowsAsync<HttpRequestException>(() => catalog.FindAsync("krakow", Museum.Id, CancellationToken.None));

        Assert.Equal(Museum.Id, (await catalog.FindAsync("krakow", Museum.Id, CancellationToken.None))!.Id);
    }

    /// <summary>Pliki katalogu serwowane z pamięci; lista firm z hosta jest niedostępna, jak bez bazy.</summary>
    private sealed class StaticFiles : HttpMessageHandler
    {
        private readonly List<string> _requests = [];

        /// <summary>Data importu z index.json: od niej zależy, czy to, co zapamiętało urządzenie, jest jeszcze ważne.</summary>
        public DateOnly GeneratedOn { get; init; } = new(2026, 10, 3);

        public Place[] Places { get; init; } = [Museum];

        /// <summary>Ile kolejnych żądań o plik kategorii ma się nie udać (chwilowy brak sieci).</summary>
        public int FailuresLeft { get; set; }

        public int Requests(string file) => _requests.Count(path => path.EndsWith(file, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            _requests.Add(path);

            if (path.EndsWith("places/index.json", StringComparison.Ordinal))
                return Json(new PlaceIndex(GeneratedOn, [new PlaceCategoryCount(PlaceCategory.Museum, Places.Length)]));
            if (path.EndsWith("places/Museum.json", StringComparison.Ordinal))
                return FailuresLeft-- > 0 ? Status(HttpStatusCode.ServiceUnavailable) : Json(Places);
            return Status(HttpStatusCode.NotFound);
        }

        private static Task<HttpResponseMessage> Json<T>(T value) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, DomainJson.Options), Encoding.UTF8, "application/json")
        });

        private static Task<HttpResponseMessage> Status(HttpStatusCode code) => Task.FromResult(new HttpResponseMessage(code));
    }

    private sealed class MemoryStore : ILocalStore
    {
        private readonly Dictionary<(string, string), object> _items = [];

        public Task<T?> GetAsync<T>(string store, string key) => Task.FromResult(_items.TryGetValue((store, key), out var v) ? (T?)v : default);

        public Task PutAsync<T>(string store, string key, T value)
        {
            _items[(store, key)] = value!;
            return Task.CompletedTask;
        }
    }
}
