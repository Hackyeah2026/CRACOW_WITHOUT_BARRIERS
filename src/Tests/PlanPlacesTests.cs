using System.Text.Json;
using Application;
using Application.Abstractions;
using Application.Places;
using Domain;
using Domain.Accounts;
using Domain.Needs;
using Domain.Places;
using Domain.Trips;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Web.Client.Services;

namespace Tests;

/// <summary>Plan z miejscami, których nie ma już w katalogu: identyfikatory zostały w nim sprzed ponownego importu.</summary>
public class PlanPlacesTests
{
    private const string Removed = "osm-node-3518909828";
    private const string AlsoRemoved = "osm-node-3223742386";
    private const string Owner = "ania";

    private static readonly DateTime Now = new(2026, 10, 4, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Place[] Places = [Place("a"), Place("b")];

    private static Place Place(string id) => new(id, "krakow", id.ToUpperInvariant(), PlaceCategory.Museum, 50.06, 19.94, null, null, []);

    /// <summary>Aplikacja po otwarciu w przeglądarce: z planami konta, gdy są podane, a bez nich bez konta.</summary>
    private static async Task<(AppState State, ISender Sender)> OpenAsync(ILocalStore store, FakePlans? plans = null)
    {
        var sender = new ServiceCollection()
            .AddApplication()
            .AddSingleton<IPlaceCatalog, FakeCatalog>()
            .AddSingleton(store)
            .AddSingleton<IAccountClient>(new FakeAccount(plans is null ? null : new UserProfile(Owner)))
            .AddSingleton<ISavedPlansClient>(plans ?? new FakePlans([]))
            .BuildServiceProvider()
            .GetRequiredService<ISender>();

        var state = new AppState(sender, store);
        await state.EnsureLoadedAsync();
        return (state, sender);
    }

    private static async Task<PlanPlaces> PlanPlacesAsync(AppState state, ISender sender) =>
        (await sender.Send(new GetPlanPlacesQuery(state.CityId, state.PlanPlaceIds, state.Profile))).Value;

    [Fact]
    public async Task Places_missing_from_the_catalog_come_back_apart_from_the_found_ones()
    {
        var (_, sender) = await OpenAsync(new JsonStore());

        var places = (await sender.Send(new GetPlanPlacesQuery("krakow", ["b", Removed, "a", AlsoRemoved], NeedsProfile.Empty))).Value;

        Assert.Equal(["b", "a"], places.Places.Select(p => p.Place.Id));
        Assert.Equal([Removed, AlsoRemoved], places.MissingIds);
    }

    [Fact]
    public async Task Guest_plan_forgets_missing_places_on_the_device()
    {
        var store = new JsonStore();
        var (state, sender) = await OpenAsync(store);
        foreach (var id in new[] { "b", Removed, "a", AlsoRemoved })
            await state.TogglePlaceAsync(id);

        var removed = await state.RemovePlacesAsync((await PlanPlacesAsync(state, sender)).MissingIds);

        Assert.True(removed.IsSuccess);
        Assert.Equal(["b", "a"], state.PlanPlaceIds);
        // Wpis sesji na urządzeniu: po ponownym otwarciu aplikacji plan nie pyta już o te identyfikatory.
        var (reopened, reopenedSender) = await OpenAsync(store);
        Assert.Equal(["b", "a"], reopened.PlanPlaceIds);
        Assert.Empty((await PlanPlacesAsync(reopened, reopenedSender)).MissingIds);
    }

    [Fact]
    public async Task Account_plan_is_saved_on_the_host_without_missing_places()
    {
        var plans = new FakePlans(["b", Removed, "a"]);
        var (state, sender) = await OpenAsync(new JsonStore(), plans);

        var removed = await state.RemovePlacesAsync((await PlanPlacesAsync(state, sender)).MissingIds);

        Assert.True(removed.IsSuccess);
        Assert.Equal(["b", "a"], plans.Plan.PlaceIds);
        Assert.Equal(["b", "a"], state.PlanPlaceIds);
        Assert.Equal(1, plans.Updates);
    }

    [Fact]
    public async Task Account_plan_stays_as_it_was_when_the_host_does_not_save_it()
    {
        var plans = new FakePlans(["b", Removed, "a"]) { Offline = true };
        var (state, sender) = await OpenAsync(new JsonStore(), plans);

        var removed = await state.RemovePlacesAsync((await PlanPlacesAsync(state, sender)).MissingIds);

        Assert.True(removed.IsFailure);
        Assert.Equal(["b", Removed, "a"], state.PlanPlaceIds);
    }

    [Fact]
    public async Task Plan_without_missing_places_is_not_saved_again()
    {
        var plans = new FakePlans(["b", "a"]);
        var (state, sender) = await OpenAsync(new JsonStore(), plans);

        var removed = await state.RemovePlacesAsync((await PlanPlacesAsync(state, sender)).MissingIds);

        Assert.True(removed.IsSuccess);
        Assert.Equal(0, plans.Updates);
    }

    private sealed class FakeCatalog : IPlaceCatalog
    {
        public Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<City>>([new City("krakow", "Kraków", 50.0614, 19.9366, 14, CityCoverage.Full, [])]);

        public Task<IReadOnlyList<PlaceCategoryCount>> GetCategoriesAsync(string cityId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PlaceCategoryCount>>([new(PlaceCategory.Museum, Places.Length)]);

        public Task<IReadOnlyList<Place>> GetAsync(string cityId, IReadOnlyCollection<PlaceCategory> categories, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Place>>(Places.Where(p => categories.Contains(p.Category)).ToList());

        public Task<Place?> FindAsync(string cityId, string placeId, CancellationToken ct) =>
            Task.FromResult(Places.FirstOrDefault(p => p.Id == placeId));
    }

    private sealed class FakeAccount(UserProfile? user) : IAccountClient
    {
        public Task<Result<UserProfile>> RegisterAsync(UserCredentials credentials, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<UserProfile>> LoginAsync(UserCredentials credentials, CancellationToken ct) => throw new NotSupportedException();

        public Task LogoutAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<UserProfile?> GetCurrentAsync(CancellationToken ct) => Task.FromResult(user);
    }

    /// <summary>Host z jednym planem konta w przygotowaniu.</summary>
    private sealed class FakePlans(IReadOnlyList<string> placeIds) : ISavedPlansClient
    {
        /// <summary>Host nie odpowiada na zapis planu.</summary>
        public bool Offline { get; init; }

        public SavedPlan Plan { get; private set; } = new("plan-1", Owner, "krakow", "Sobota", placeIds, SavedPlanStatus.Draft, Now, Now);

        public int Updates { get; private set; }

        public Task<Result<IReadOnlyList<SavedPlan>>> GetMineAsync(CancellationToken ct) =>
            Task.FromResult(Result.Success<IReadOnlyList<SavedPlan>>([Plan]));

        public Task<Result<SavedPlan>> UpdateAsync(string id, SavedPlanDraft draft, CancellationToken ct)
        {
            if (Offline)
                return Task.FromResult(Result.Failure<SavedPlan>("Brak połączenia z serwerem."));

            Updates++;
            Plan = Plan with { PlaceIds = draft.PlaceIds };
            return Task.FromResult(Result.Success(Plan));
        }

        public Task<Result<SavedPlan>> GetAsync(string id, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<SavedPlan>> CreateAsync(SavedPlanDraft draft, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<SavedPlan>> CloseAsync(string id, SavedPlanRoute route, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result> DeleteAsync(string id, CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>Magazyn urządzenia w pamięci; wartości są zapisane jako JSON, tak jak w IndexedDB.</summary>
    private sealed class JsonStore : ILocalStore
    {
        private readonly Dictionary<(string, string), string> _items = [];

        public Task<T?> GetAsync<T>(string store, string key) => Task.FromResult(
            _items.TryGetValue((store, key), out var json) ? JsonSerializer.Deserialize<T>(json, DomainJson.Options) : default);

        public Task PutAsync<T>(string store, string key, T value)
        {
            _items[(store, key)] = JsonSerializer.Serialize(value, DomainJson.Options);
            return Task.CompletedTask;
        }
    }
}
