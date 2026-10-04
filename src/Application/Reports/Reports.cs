using Application.Abstractions;
using Domain.Hazards;
using Domain.Places;
using Domain.Reports;

namespace Application.Reports;

/// <summary>Wysyła zgłoszenie z konta zalogowanego mieszkańca.</summary>
public sealed record SubmitReportCommand(ReportDraft Draft) : ICommand<ReportReceipt>;

internal sealed class SubmitReportCommandHandler(IReportsClient client) : ICommandHandler<SubmitReportCommand, ReportReceipt>
{
    public Task<Result<ReportReceipt>> Handle(SubmitReportCommand command, CancellationToken ct) =>
        command.Draft.Validate().IfValidAsync(() => client.SubmitAsync(command.Draft, ct));
}

/// <summary>Zgłoszenia zalogowanego mieszkańca, od najnowszych, ze stanem obsługi.</summary>
public sealed record GetMyReportsQuery : IQuery<IReadOnlyList<ReportStatusView>>;

internal sealed class GetMyReportsQueryHandler(IReportsClient client) : IQueryHandler<GetMyReportsQuery, IReadOnlyList<ReportStatusView>>
{
    public Task<Result<IReadOnlyList<ReportStatusView>>> Handle(GetMyReportsQuery query, CancellationToken ct) => client.GetMineAsync(ct);
}

public sealed record OfficialLoginCommand(OfficialLogin Login) : ICommand<OfficialProfile>;

internal sealed class OfficialLoginCommandHandler(IOfficialClient client) : ICommandHandler<OfficialLoginCommand, OfficialProfile>
{
    public Task<Result<OfficialProfile>> Handle(OfficialLoginCommand command, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(command.Login.Login) || string.IsNullOrEmpty(command.Login.Password)
            ? Task.FromResult(Result.Failure<OfficialProfile>("Podaj login i hasło."))
            : client.LoginAsync(command.Login, ct);
}

public sealed record OfficialLogoutCommand : ICommand;

internal sealed class OfficialLogoutCommandHandler(IOfficialClient client) : ICommandHandler<OfficialLogoutCommand>
{
    public async Task<Result> Handle(OfficialLogoutCommand command, CancellationToken ct)
    {
        await client.LogoutAsync(ct);
        return Result.Success();
    }
}

/// <summary>Zalogowany urzędnik; błąd, gdy nikt nie jest zalogowany.</summary>
public sealed record GetCurrentOfficialQuery : IQuery<OfficialProfile>;

internal sealed class GetCurrentOfficialQueryHandler(IOfficialClient client) : IQueryHandler<GetCurrentOfficialQuery, OfficialProfile>
{
    public async Task<Result<OfficialProfile>> Handle(GetCurrentOfficialQuery query, CancellationToken ct) =>
        await client.GetCurrentAsync(ct) is { } official
            ? Result.Success(official)
            : Result.Failure<OfficialProfile>("Nie jesteś zalogowany.");
}

public sealed record GetReportsQuery(ReportFilter Filter) : IQuery<IReadOnlyList<Report>>;

internal sealed class GetReportsQueryHandler(IOfficialClient client) : IQueryHandler<GetReportsQuery, IReadOnlyList<Report>>
{
    public Task<Result<IReadOnlyList<Report>>> Handle(GetReportsQuery query, CancellationToken ct) =>
        client.GetReportsAsync(query.Filter, ct);
}

public sealed record UpdateReportStatusCommand(string ReportId, ReportStatusChange Change) : ICommand<Report>;

internal sealed class UpdateReportStatusCommandHandler(IOfficialClient client) : ICommandHandler<UpdateReportStatusCommand, Report>
{
    public Task<Result<Report>> Handle(UpdateReportStatusCommand command, CancellationToken ct) =>
        command.Change.Validate().IfValidAsync(() => client.UpdateStatusAsync(command.ReportId, command.Change, ct));
}

/// <summary>Zgłoszenia konta na pulpicie strony głównej: zgłoszenia miejsc i punkty na mapie razem, według etapu obsługi.</summary>
/// <param name="Waiting">Bez żadnej decyzji urzędu.</param>
/// <param name="Done">Rozwiązane zgłoszenia miejsc oraz punkty potwierdzone (także te, których już nie ma).</param>
/// <param name="Answered">Z odpowiedzią urzędu dla zgłaszającego.</param>
public sealed record MyReportsSummary(int Total, int Waiting, int InProgress, int Done, int Answered)
{
    public static MyReportsSummary From(IReadOnlyList<ReportStatusView> reports, IReadOnlyList<HazardStatusView> hazards) => new(
        reports.Count + hazards.Count,
        reports.Count(r => r.Status == ReportStatus.New) + hazards.Count(h => h.Status == HazardStatus.Pending),
        reports.Count(r => r.Status is ReportStatus.InReview or ReportStatus.Planned),
        reports.Count(r => r.Status == ReportStatus.Resolved) + hazards.Count(h => h.Status is HazardStatus.Verified or HazardStatus.Removed),
        reports.Count(r => r.OfficialNote is not null) + hazards.Count(h => h.OfficialNote is not null));
}

public sealed record FeatureCount(FeatureKey Feature, int Count);

public sealed record PlaceReportCount(string PlaceId, string PlaceName, PlaceCategory Category, int Count, IReadOnlyList<FeatureKey> Features);

public sealed record ReportSummary(int Open, int Total, IReadOnlyList<FeatureCount> TopFeatures, IReadOnlyList<PlaceReportCount> TopPlaces);

/// <summary>Zestawienie dla urzędu: czego najczęściej brakuje i które miejsca wymagają działań.</summary>
public static class ReportStatistics
{
    public static bool IsOpen(ReportStatus status) => status is ReportStatus.New or ReportStatus.InReview or ReportStatus.Planned;

    /// <summary>Liczy tylko zgłoszenia otwarte: zamknięte nie wymagają już decyzji.</summary>
    public static ReportSummary Summarize(IReadOnlyList<Report> reports, int top = 5)
    {
        var open = reports.Where(r => IsOpen(r.Status)).ToList();

        var features = open
            .SelectMany(r => r.Features)
            .GroupBy(f => f)
            .Select(g => new FeatureCount(g.Key, g.Count()))
            .OrderByDescending(f => f.Count).ThenBy(f => f.Feature)
            .Take(top)
            .ToList();

        var places = open
            .GroupBy(r => r.PlaceId)
            .Select(g => new PlaceReportCount(g.Key, g.First().PlaceName, g.First().Category, g.Count(),
                g.SelectMany(r => r.Features).Distinct().Order().ToList()))
            .OrderByDescending(p => p.Count).ThenBy(p => p.PlaceName, StringComparer.CurrentCulture)
            .Take(top)
            .ToList();

        return new ReportSummary(open.Count, reports.Count, features, places);
    }
}
