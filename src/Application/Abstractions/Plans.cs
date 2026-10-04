using Domain.Trips;

namespace Application.Abstractions;

/// <summary>Plany kont mieszkańców w bazie hosta. Każda operacja dotyczy planów jednego właściciela.</summary>
public interface ISavedPlanRepository
{
    /// <summary>Plany konta od ostatnio zmienianych, bez zapisanych tras.</summary>
    Task<IReadOnlyList<SavedPlan>> ListAsync(string owner, int limit, CancellationToken ct);

    Task<long> CountAsync(string owner, CancellationToken ct);

    /// <summary>Plan z zapisaną trasą; null, gdy konto nie ma takiego planu.</summary>
    Task<SavedPlan?> FindAsync(string owner, string id, CancellationToken ct);

    Task AddAsync(SavedPlan plan, CancellationToken ct);

    /// <summary>Zmienia nazwę i miejsca planu w przygotowaniu; null, gdy planu nie ma albo jest zamknięty.</summary>
    Task<SavedPlan?> UpdateDraftAsync(string owner, string id, SavedPlanDraft draft, DateTime now, CancellationToken ct);

    /// <summary>Zapisuje trasę i zamyka plan; null, gdy planu nie ma albo jest już zamknięty.</summary>
    Task<SavedPlan?> CloseAsync(string owner, string id, string routeJson, DateTime now, CancellationToken ct);

    Task<bool> DeleteAsync(string owner, string id, CancellationToken ct);
}

/// <summary>Plany zalogowanego mieszkańca w przeglądarce (api/plans).</summary>
public interface ISavedPlansClient
{
    Task<Result<IReadOnlyList<SavedPlan>>> GetMineAsync(CancellationToken ct);
    Task<Result<SavedPlan>> GetAsync(string id, CancellationToken ct);
    Task<Result<SavedPlan>> CreateAsync(SavedPlanDraft draft, CancellationToken ct);
    Task<Result<SavedPlan>> UpdateAsync(string id, SavedPlanDraft draft, CancellationToken ct);
    Task<Result<SavedPlan>> CloseAsync(string id, SavedPlanRoute route, CancellationToken ct);
    Task<Result> DeleteAsync(string id, CancellationToken ct);
}
