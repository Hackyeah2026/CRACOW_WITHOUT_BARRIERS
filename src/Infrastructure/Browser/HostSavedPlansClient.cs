using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Trips;

namespace Infrastructure.Browser;

/// <summary>Plany zalogowanego mieszkańca (api/plans).</summary>
internal sealed class HostSavedPlansClient(HttpClient http) : ISavedPlansClient
{
    private const string Unavailable = "Baza planów jest chwilowo niedostępna. Spróbuj później.";

    public Task<Result<IReadOnlyList<SavedPlan>>> GetMineAsync(CancellationToken ct) =>
        HostApi.SendAsync<IReadOnlyList<SavedPlan>>(() => http.GetAsync("api/plans", ct), ct, unavailable: Unavailable);

    public Task<Result<SavedPlan>> GetAsync(string id, CancellationToken ct) =>
        HostApi.SendAsync<SavedPlan>(() => http.GetAsync(Url(id), ct), ct, unavailable: Unavailable);

    public Task<Result<SavedPlan>> CreateAsync(SavedPlanDraft draft, CancellationToken ct) =>
        HostApi.SendAsync<SavedPlan>(() => http.PostAsJsonAsync("api/plans", draft, DomainJson.Options, ct), ct, unavailable: Unavailable);

    public Task<Result<SavedPlan>> UpdateAsync(string id, SavedPlanDraft draft, CancellationToken ct) =>
        HostApi.SendAsync<SavedPlan>(() => http.PutAsJsonAsync(Url(id), draft, DomainJson.Options, ct), ct, unavailable: Unavailable);

    public Task<Result<SavedPlan>> CloseAsync(string id, SavedPlanRoute route, CancellationToken ct) =>
        HostApi.SendAsync<SavedPlan>(() => http.PostAsJsonAsync($"{Url(id)}/close", route, DomainJson.Options, ct), ct, unavailable: Unavailable);

    public async Task<Result> DeleteAsync(string id, CancellationToken ct)
    {
        try
        {
            using var response = await http.DeleteAsync(Url(id), ct);
            // Plan usunięty wcześniej (np. na innym urządzeniu) też uznajemy za usunięty.
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? Result.Success()
                : Result.Failure(response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable
                    ? Unavailable
                    : "Nie udało się usunąć planu. Spróbuj ponownie.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result.Failure("Brak połączenia z serwerem.");
        }
    }

    private static string Url(string id) => $"api/plans/{Uri.EscapeDataString(id)}";
}
