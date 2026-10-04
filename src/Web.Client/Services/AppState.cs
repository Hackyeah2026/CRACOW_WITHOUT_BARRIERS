using Application.Abstractions;
using Application.Accounts;
using Application.Places;
using Application.Profile;
using Application.Trips;
using Domain.Accounts;
using Domain.Needs;
using Domain.Places;
using Domain.Trips;
using MediatR;

namespace Web.Client.Services;

/// <summary>
/// Stan sesji: tryb, profil i miejsca wybrane do planu. Profil zapisuje się na urządzeniu. Plan bez konta też;
/// zalogowany ma nazwane plany w bazie hosta i jeden z nich otwarty.
/// </summary>
public sealed class AppState(ISender sender, ILocalStore store)
{
    private const string SessionKey = "session";
    private Task? _loading;

    public const string DefaultCityId = "krakow";
    public const int DefaultZoom = 14;

    /// <summary>Środek mapy, zanim wczyta się lista miast: Rynek Główny w Krakowie.</summary>
    public static readonly GeoPoint DefaultCenter = new(50.0614, 19.9366);

    public string CityId { get; private set; } = DefaultCityId;
    public IReadOnlyList<City> Cities { get; private set; } = [];

    /// <summary>Wybrane miasto; null do czasu wczytania listy miast.</summary>
    public City? City => Cities.FirstOrDefault(c => c.Id == CityId);

    /// <summary>Początkowy widok map dla wybranego miasta.</summary>
    public GeoPoint CityCenter => City is { } city ? new GeoPoint(city.Lat, city.Lon) : DefaultCenter;
    public int CityZoom => City?.Zoom ?? DefaultZoom;
    public AppMode Mode { get; private set; } = AppMode.Sightseeing;
    /// <summary>Profil zalogowanego konta albo, bez konta, konfiguracja tymczasowa tej przeglądarki. Zawsze tylko na urządzeniu.</summary>
    public NeedsProfile Profile { get; private set; } = NeedsProfile.Empty;
    /// <summary>Miejsca planu bez konta, zapisane na urządzeniu.</summary>
    private List<string> _guestPlaceIds = [];

    /// <summary>Miejsca bieżącego planu: bez konta wybór z urządzenia, z kontem miejsca otwartego planu.</summary>
    public IReadOnlyList<string> PlanPlaceIds => User is null ? _guestPlaceIds : ActivePlan?.PlaceIds ?? [];

    /// <summary>Plany zalogowanego konta, od ostatnio zmienianych; puste bez konta.</summary>
    public IReadOnlyList<SavedPlan> Plans { get; private set; } = [];

    /// <summary>Dlaczego nie udało się wczytać planów konta; null, gdy są wczytane.</summary>
    public string? PlansError { get; private set; }

    private string? _activePlanId;

    /// <summary>Plan konta otwarty na stronie Plan; null bez konta albo gdy konto nie ma planów.</summary>
    public SavedPlan? ActivePlan => User is null ? null : Plans.FirstOrDefault(p => p.Id == _activePlanId);

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
        // Najpierw konto: od niego zależy, czy czytamy profil konta, czy konfigurację tymczasową.
        var user = await sender.Send(new GetCurrentUserQuery());
        User = user.IsSuccess ? user.Value : null;
        await LoadProfileAsync();

        if (await store.GetAsync<Session>(LocalStores.Profile, SessionKey) is { } session)
        {
            Mode = session.Mode;
            _guestPlaceIds = session.PlanPlaceIds;
            CityId = session.CityId;
            OptimizeOrder = session.OptimizeOrder;
            _activePlanId = session.ActivePlanId;
        }
        await LoadPlansAsync();

        var cities = await sender.Send(new GetCitiesQuery());
        if (cities.IsSuccess)
            Cities = cities.Value;
        if (Cities.Count > 0 && City is null)
            CityId = DefaultCityId;

        Changed?.Invoke();
    }

    /// <summary>Po zalogowaniu albo wylogowaniu: profil potrzeb przełącza się między profilem konta a konfiguracją tymczasową.</summary>
    public async Task SetUserAsync(UserProfile? user)
    {
        User = user;
        await LoadProfileAsync();
        await LoadPlansAsync();
        Changed?.Invoke();
    }

    private async Task LoadPlansAsync()
    {
        (Plans, PlansError) = ([], null);
        if (User is null)
            return;

        var plans = await sender.Send(new GetMyPlansQuery());
        if (plans.IsSuccess)
            Plans = plans.Value;
        else
            PlansError = plans.Error;

        // Otwarty zostaje plan z poprzedniej wizyty; gdy go już nie ma, pierwszy, który da się edytować.
        if (ActivePlan is null)
            _activePlanId = Plans.FirstOrDefault(p => !p.IsClosed)?.Id;
    }

    private async Task LoadProfileAsync()
    {
        var profile = await sender.Send(new GetProfileQuery(User?.Login));
        if (profile.IsSuccess)
            Profile = profile.Value;
    }

    public async Task SetModeAsync(AppMode mode)
    {
        Mode = mode;
        await SaveSessionAsync();
    }

    public async Task SetProfileAsync(NeedsProfile profile)
    {
        Profile = profile;
        await sender.Send(new SaveProfileCommand(profile, User?.Login));
        Changed?.Invoke();
    }

    /// <summary>Bez konta: czy miejsce jest w planie. Z kontem: czy jest w którymś planie w przygotowaniu.</summary>
    public bool IsInPlan(string placeId) => User is null
        ? _guestPlaceIds.Contains(placeId)
        : Plans.Any(p => !p.IsClosed && p.PlaceIds.Contains(placeId));

    /// <summary>Dodaje miejsce do bieżącego planu albo je z niego usuwa.</summary>
    public Task<Result> TogglePlaceAsync(string placeId)
    {
        var ids = PlanPlaceIds.ToList();
        if (!ids.Remove(placeId))
            ids.Add(placeId);
        return SavePlaceIdsAsync(ids);
    }

    /// <summary>
    /// Usuwa z bieżącego planu miejsca, których nie ma już w katalogu (np. po ponownym imporcie): bez konta z wyboru
    /// na urządzeniu, z kontem z planu na hoście. Gdy host nie zapisze zmiany, plan konta zostaje, jaki był.
    /// </summary>
    public Task<Result> RemovePlacesAsync(IReadOnlyCollection<string> placeIds)
    {
        var ids = PlanPlaceIds.Where(id => !placeIds.Contains(id)).ToList();
        return ids.Count == PlanPlaceIds.Count ? Task.FromResult(Result.Success()) : SavePlaceIdsAsync(ids);
    }

    public Task<Result> MovePlaceUpAsync(string placeId) => MovePlaceAsync(placeId, -1);

    public Task<Result> MovePlaceDownAsync(string placeId) => MovePlaceAsync(placeId, 1);

    private async Task<Result> MovePlaceAsync(string placeId, int offset)
    {
        var ids = PlanPlaceIds.ToList();
        var from = ids.IndexOf(placeId);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= ids.Count)
            return Result.Success();

        (ids[from], ids[to]) = (ids[to], ids[from]);
        return await SavePlaceIdsAsync(ids);
    }

    private async Task<Result> SavePlaceIdsAsync(List<string> ids)
    {
        if (User is null)
        {
            _guestPlaceIds = ids;
            await SaveSessionAsync();
            return Result.Success();
        }

        return ActivePlan is { IsClosed: false } plan
            ? await UpdatePlanAsync(plan, ids)
            : Result.Failure("Ten plan jest zamknięty. Otwórz plan w przygotowaniu albo utwórz nowy.");
    }

    private async Task<Result> UpdatePlanAsync(SavedPlan plan, IReadOnlyList<string> placeIds)
    {
        var updated = await sender.Send(new UpdatePlanCommand(plan.Id, new SavedPlanDraft(plan.CityId, plan.Name, placeIds)));
        if (updated.IsFailure)
            return Result.Failure(updated.Error!);

        ReplacePlan(updated.Value);
        return Result.Success();
    }

    /// <summary>Podmienia plan na liście i przesuwa go na początek: lista jest ułożona od ostatnio zmienianych.</summary>
    private void ReplacePlan(SavedPlan plan)
    {
        Plans = Plans.Where(p => p.Id != plan.Id).Prepend(plan).ToList();
        Changed?.Invoke();
    }

    /// <summary>Tworzy plan konta, od razu z podanym miejscem, i go otwiera.</summary>
    public async Task<Result> CreatePlanAsync(string name, string? placeId = null)
    {
        var created = await sender.Send(new CreatePlanCommand(new SavedPlanDraft(CityId, name, placeId is null ? [] : [placeId])));
        if (created.IsFailure)
            return Result.Failure(created.Error!);

        _activePlanId = created.Value.Id;
        ReplacePlan(created.Value);
        await SaveSessionAsync();
        return Result.Success();
    }

    /// <summary>Dodaje miejsce do wskazanego planu konta albo je z niego usuwa; ten plan staje się otwarty.</summary>
    public async Task<Result> TogglePlaceInPlanAsync(string planId, string placeId)
    {
        if (Plans.FirstOrDefault(p => p.Id == planId) is not { IsClosed: false } plan)
            return Result.Failure("Ten plan jest zamknięty i nie można go już zmienić.");

        var ids = plan.PlaceIds.ToList();
        if (!ids.Remove(placeId))
            ids.Add(placeId);
        var result = await UpdatePlanAsync(plan, ids);
        if (result.IsSuccess)
            await SelectPlanAsync(planId);
        return result;
    }

    public async Task SelectPlanAsync(string planId)
    {
        if (_activePlanId == planId)
            return;

        _activePlanId = planId;
        // Punkt startu wskazano dla poprzedniego planu.
        StartLocation = null;
        await SaveSessionAsync();
    }

    public async Task<Result> DeletePlanAsync(string planId)
    {
        var deleted = await sender.Send(new DeletePlanCommand(planId));
        if (deleted.IsFailure)
            return deleted;

        Plans = Plans.Where(p => p.Id != planId).ToList();
        if (_activePlanId == planId)
        {
            _activePlanId = Plans.FirstOrDefault(p => !p.IsClosed)?.Id;
            StartLocation = null;
        }
        await SaveSessionAsync();
        return Result.Success();
    }

    /// <summary>Zapisuje wyznaczoną trasę w otwartym planie i go zamyka: potem nie da się go edytować.</summary>
    public async Task<Result> ClosePlanAsync(TripPlan route)
    {
        if (ActivePlan is not { IsClosed: false } plan)
            return Result.Failure("Ten plan jest już zamknięty.");

        var closed = await sender.Send(new ClosePlanCommand(plan.Id, route));
        if (closed.IsFailure)
            return Result.Failure(closed.Error!);

        StartLocation = null;
        ReplacePlan(closed.Value);
        return Result.Success();
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

    /// <summary>Usuwa z bieżącego planu wszystkie miejsca i punkt startu; sam plan konta zostaje.</summary>
    public Task<Result> ClearPlanAsync()
    {
        StartLocation = null;
        return SavePlaceIdsAsync([]);
    }

    private async Task SaveSessionAsync()
    {
        await store.PutAsync(LocalStores.Profile, SessionKey, new Session(Mode, _guestPlaceIds, OptimizeOrder, CityId, _activePlanId));
        Changed?.Invoke();
    }

    private sealed record Session(AppMode Mode, List<string> PlanPlaceIds, bool OptimizeOrder = true, string CityId = DefaultCityId,
        string? ActivePlanId = null);
}
