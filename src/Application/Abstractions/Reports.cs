using Domain.Reports;

namespace Application.Abstractions;

/// <summary>Zgłoszenia w bazie hosta.</summary>
public interface IReportRepository
{
    Task AddAsync(Report report, CancellationToken ct);
    Task<IReadOnlyList<Report>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct);

    /// <summary>Najnowsze zgłoszenia spełniające filtr.</summary>
    Task<IReadOnlyList<Report>> ListAsync(ReportFilter filter, int limit, CancellationToken ct);

    /// <summary>Zmienia status i odpowiedź urzędu; null, gdy zgłoszenia nie ma.</summary>
    Task<Report?> UpdateStatusAsync(string id, ReportStatusChange change, string officialLogin, DateTime now, CancellationToken ct);
}

/// <summary>Konta urzędników w bazie hosta.</summary>
public interface IOfficialDirectory
{
    /// <summary>Profil urzędnika, gdy login i hasło się zgadzają; w przeciwnym razie null.</summary>
    Task<OfficialProfile?> VerifyAsync(OfficialLogin login, CancellationToken ct);
}

/// <summary>Baza nie jest skonfigurowana albo nie odpowiada. Host zamienia to na odpowiedź 503.</summary>
public sealed class DatabaseUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Wysyłka zgłoszeń z przeglądarki do hosta.</summary>
public interface IReportsClient
{
    Task<Result<ReportReceipt>> SubmitAsync(ReportDraft draft, CancellationToken ct);
    Task<Result<IReadOnlyList<ReportStatusView>>> GetStatusesAsync(IReadOnlyList<string> ids, CancellationToken ct);
}

/// <summary>Panel urzędnika w przeglądarce. Sesję trzyma ciasteczko hosta niedostępne dla skryptów.</summary>
public interface IOfficialClient
{
    Task<Result<OfficialProfile>> LoginAsync(OfficialLogin login, CancellationToken ct);
    Task LogoutAsync(CancellationToken ct);

    /// <summary>Zalogowany urzędnik albo null.</summary>
    Task<OfficialProfile?> GetCurrentAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<Report>>> GetReportsAsync(ReportFilter filter, CancellationToken ct);
    Task<Result<Report>> UpdateStatusAsync(string id, ReportStatusChange change, CancellationToken ct);
}
