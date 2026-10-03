using Domain.Businesses;

namespace Application.Abstractions;

public enum BusinessSaveResult { Saved, Stale, PlaceTaken }

/// <summary>Wnioski i konta firmowe w bazie hosta; kluczem jest login konta.</summary>
public interface IBusinessRepository
{
    Task<BusinessAccount?> FindAsync(string login, CancellationToken ct);

    /// <summary>Zapisuje wniosek, zastępując poprzedni niezatwierdzony; false, gdy konto jest już zatwierdzone.</summary>
    Task<bool> SubmitAsync(BusinessAccount account, CancellationToken ct);

    /// <summary>Czy miejsce ma już zatwierdzone konto firmowe innego właściciela.</summary>
    Task<bool> IsPlaceTakenAsync(string cityId, string placeId, string exceptLogin, CancellationToken ct);

    /// <summary>Najnowsze wnioski w mieście; bez statusu wszystkie.</summary>
    Task<IReadOnlyList<BusinessAccount>> ListAsync(string? cityId, BusinessStatus? status, int limit, CancellationToken ct);

    /// <summary>
    /// Zapisuje zmienione konto, o ile w bazie jest nadal wersja z <paramref name="previousUpdatedAt"/>:
    /// decyzja urzędnika i zapis oznaczeń przez firmę nie mogą się nawzajem nadpisać.
    /// </summary>
    Task<BusinessSaveResult> ReplaceAsync(BusinessAccount account, DateTime previousUpdatedAt, CancellationToken ct);
}

/// <summary>Konto firmowe w przeglądarce (wymaga zalogowanego konta) i publiczna lista miejsc z certyfikatem.</summary>
public interface IBusinessClient
{
    Task<Result<BusinessAccountView>> ApplyAsync(BusinessApplicationDraft draft, CancellationToken ct);

    /// <summary>Wniosek albo konto firmowe zalogowanego; pusty wynik, gdy wniosku nie było.</summary>
    Task<Result<MyBusiness>> GetMineAsync(CancellationToken ct);

    Task<Result<BusinessAccountView>> SaveFeaturesAsync(BusinessFeaturesUpdate update, CancellationToken ct);

    Task<Result<IReadOnlyList<CertifiedPlace>>> GetCertifiedAsync(string cityId, CancellationToken ct);
}
