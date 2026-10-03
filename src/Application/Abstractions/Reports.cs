using Domain.Hazards;
using Domain.Reports;

namespace Application.Abstractions;

/// <summary>Zgłoszenia w bazie hosta.</summary>
public interface IReportRepository
{
    Task AddAsync(Report report, CancellationToken ct);
    /// <summary>Najnowsze zgłoszenia wysłane z danego konta.</summary>
    Task<IReadOnlyList<Report>> ListByReporterAsync(string login, int limit, CancellationToken ct);

    /// <summary>Najnowsze zgłoszenia spełniające filtr.</summary>
    Task<IReadOnlyList<Report>> ListAsync(ReportFilter filter, int limit, CancellationToken ct);

    /// <summary>Zmienia status i odpowiedź urzędu; null, gdy zgłoszenia nie ma.</summary>
    Task<Report?> UpdateStatusAsync(string id, ReportStatusChange change, string officialLogin, DateTime now, CancellationToken ct);
}

/// <summary>Punkty z utrudnieniami w bazie hosta.</summary>
public interface IHazardRepository
{
    Task AddAsync(Hazard hazard, CancellationToken ct);
    /// <summary>Najnowsze punkty zgłoszone z danego konta.</summary>
    Task<IReadOnlyList<Hazard>> ListByReporterAsync(string login, int limit, CancellationToken ct);

    /// <summary>Najnowsze punkty w mieście; bez statusu wszystkie.</summary>
    Task<IReadOnlyList<Hazard>> ListAsync(string? cityId, HazardStatus? status, int limit, CancellationToken ct);

    /// <summary>Zapisuje decyzję urzędnika; null, gdy punktu nie ma.</summary>
    Task<Hazard?> ReviewAsync(string id, HazardReview review, string officialLogin, DateTime now, CancellationToken ct);
}

/// <summary>Konta urzędników w bazie hosta.</summary>
public interface IOfficialDirectory
{
    /// <summary>Profil urzędnika, gdy login i hasło się zgadzają; w przeciwnym razie null.</summary>
    Task<OfficialProfile?> VerifyAsync(OfficialLogin login, CancellationToken ct);
}

/// <summary>Baza nie jest skonfigurowana albo nie odpowiada. Host zamienia to na odpowiedź 503.</summary>
public sealed class DatabaseUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Wysyłka zgłoszeń z przeglądarki do hosta; wymaga zalogowanego mieszkańca.</summary>
public interface IReportsClient
{
    Task<Result<ReportReceipt>> SubmitAsync(ReportDraft draft, CancellationToken ct);
    /// <summary>Zgłoszenia zalogowanego mieszkańca, od najnowszych.</summary>
    Task<Result<IReadOnlyList<ReportStatusView>>> GetMineAsync(CancellationToken ct);
}

/// <summary>Punkty z utrudnieniami: wysyłka z przeglądarki do hosta i odczyt punktów potwierdzonych przez urząd.</summary>
public interface IHazardsClient
{
    Task<Result<HazardReceipt>> SubmitAsync(HazardDraft draft, CancellationToken ct);
    /// <summary>Punkty zalogowanego mieszkańca, od najnowszych.</summary>
    Task<Result<IReadOnlyList<HazardStatusView>>> GetMineAsync(CancellationToken ct);
    Task<Result<IReadOnlyList<VerifiedHazard>>> GetVerifiedAsync(string cityId, CancellationToken ct);
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

    /// <summary>Wszystkie punkty z utrudnieniami w mieście, także niezweryfikowane.</summary>
    Task<Result<IReadOnlyList<Hazard>>> GetHazardsAsync(string? cityId, CancellationToken ct);
    Task<Result<Hazard>> ReviewHazardAsync(string id, HazardReview review, CancellationToken ct);
}
