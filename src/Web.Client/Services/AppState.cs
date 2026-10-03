using Application.Abstractions;
using Application.Accounts;
using Application.Places;
using Application.Profile;
using Domain.Accounts;
using Domain.Needs;
using Domain.Places;
using MediatR;

namespace Web.Client.Services;

/// <summary>Stan sesji: tryb, profil i miejsca wybrane do planu. Profil i wybór zapisują się na urządzeniu.</summary>
public sealed class AppState(ISender sender, ILocalStore store)
{
    private const string SessionKey = "session";
    private Task? _loading;

    public const string DefaultCityId = "krakow";

    public string CityId { get; private set; } = DefaultCityId;
    public IReadOnlyList<City> Cities { get; private set; } = [];

    /// <summary>Wybrane miasto; null do czasu wczytania listy miast.</summary>
    public City? City => Cities.FirstOrDefault(c => c.Id == CityId);
    public AppMode Mode { get; private set; } = AppMode.Sightseeing;
    public NeedsProfile Profile { get; private set; } = NeedsProfile.Empty;
    public List<string> PlanPlaceIds { get; private set; } = [];

    /// <summary>Czy plan ma sam ułożyć kolejność miejsc po starcie; gdy nie, zostaje kolejność z listy.</summary>
    public bool OptimizeOrder { get; private set; } = true;

    /// <summary>Start planu spoza katalogu (lokalizacja albo punkt z mapy). Celowo tylko w pamięci: nie zapisujemy go na urządzeniu.</summary>
    public GeoPoint? StartLocation { get; private set; }

    /// <summary>Filtry listy miejsc, żeby po powrocie ze szczegółów lista wyglądała tak samo.</summary>
    public string? PlacesSearch { get; set; }
    public string PlacesCategory { get; set; } = "";

    /// <summary>Zalogowany mieszkaniec; null bez konta. Sesję trzyma ciasteczko hosta, tu jest tylko login do wyświetlenia.</summary>
    public UserProfile? User { get; private set; }

    public event Action? Changed;

    /// <summary>Wczytuje stan z urządzenia. Równoległe wywołania czekają na to samo wczytanie.</summary>
    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        var profile = await sender.Send(new GetProfileQuery());
        if (profile.IsSuccess)
            Profile = profile.Value;

        if (await store.GetAsync<Session>(LocalStores.Profile, SessionKey) is { } session)
        {
            Mode = session.Mode;
            PlanPlaceIds = session.PlanPlaceIds;
            CityId = session.CityId;
            OptimizeOrder = session.OptimizeOrder;
        }

        var cities = await sender.Send(new GetCitiesQuery());
        if (cities.IsSuccess)
            Cities = cities.Value;
        if (Cities.Count > 0 && City is null)
            CityId = DefaultCityId;

        var user = await sender.Send(new GetCurrentUserQuery());
        User = user.IsSuccess ? user.Value : null;

        Changed?.Invoke();
    }

    public void SetUser(UserProfile? user)
    {
        User = user;
        Changed?.Invoke();
    }

    public async Task SetModeAsync(AppMode mode)
    {
        Mode = mode;
        await SaveSessionAsync();
    }

    public async Task SetProfileAsync(NeedsProfile profile)
    {
        Profile = profile;
        await sender.Send(new SaveProfileCommand(profile));
        Changed?.Invoke();
    }

    public bool IsInPlan(string placeId) => PlanPlaceIds.Contains(placeId);

    public async Task TogglePlaceAsync(string placeId)
    {
        if (!PlanPlaceIds.Remove(placeId))
            PlanPlaceIds.Add(placeId);
        await SaveSessionAsync();
    }

    public Task MovePlaceUpAsync(string placeId) => MovePlaceAsync(placeId, -1);

    public Task MovePlaceDownAsync(string placeId) => MovePlaceAsync(placeId, 1);

    private async Task MovePlaceAsync(string placeId, int offset)
    {
        var from = PlanPlaceIds.IndexOf(placeId);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= PlanPlaceIds.Count)
            return;

        (PlanPlaceIds[from], PlanPlaceIds[to]) = (PlanPlaceIds[to], PlanPlaceIds[from]);
        await SaveSessionAsync();
    }

    public async Task SetOptimizeOrderAsync(bool optimize)
    {
        OptimizeOrder = optimize;
        await SaveSessionAsync();
    }

    public void SetStartLocation(GeoPoint? location)
    {
        StartLocation = location;
        Changed?.Invoke();
    }

    public async Task ClearPlanAsync()
    {
        PlanPlaceIds.Clear();
        StartLocation = null;
        await SaveSessionAsync();
    }

    private async Task SaveSessionAsync()
    {
        await store.PutAsync(LocalStores.Profile, SessionKey, new Session(Mode, PlanPlaceIds, OptimizeOrder, CityId));
        Changed?.Invoke();
    }

    private sealed record Session(AppMode Mode, List<string> PlanPlaceIds, bool OptimizeOrder = true, string CityId = DefaultCityId);
}
