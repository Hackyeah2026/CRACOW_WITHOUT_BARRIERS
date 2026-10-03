using Application.Abstractions;
using Application.Profile;
using Domain.Needs;
using Domain.Places;
using MediatR;

namespace Web.Client.Services;

/// <summary>Stan sesji: tryb, profil i miejsca wybrane do planu. Profil i wybór zapisują się na urządzeniu.</summary>
public sealed class AppState(ISender sender, ILocalStore store)
{
    private const string SessionKey = "session";
    private Task? _loading;

    public string CityId { get; private set; } = "krakow";
    public AppMode Mode { get; private set; } = AppMode.Sightseeing;
    public NeedsProfile Profile { get; private set; } = NeedsProfile.Empty;
    public List<string> PlanPlaceIds { get; private set; } = [];

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
        }

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

    public async Task ClearPlanAsync()
    {
        PlanPlaceIds.Clear();
        await SaveSessionAsync();
    }

    private async Task SaveSessionAsync()
    {
        await store.PutAsync(LocalStores.Profile, SessionKey, new Session(Mode, PlanPlaceIds));
        Changed?.Invoke();
    }

    private sealed record Session(AppMode Mode, List<string> PlanPlaceIds);
}
