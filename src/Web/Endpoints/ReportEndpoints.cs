using System.Security.Claims;
using Application.Abstractions;
using Domain.Reports;

namespace Web.Endpoints;

public static class ReportEndpoints
{
    private const int MaxMine = 100;
    private const int MaxListed = 500;

    /// <summary>
    /// Zgłoszenia miejsc: wysyła je zalogowany mieszkaniec (urząd widzi login zgłaszającego), a obsługuje urzędnik
    /// w panelu (osobna sesja).
    /// </summary>
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/api/reports")
            .AddEndpointFilter(Api.DatabaseUnavailableFilter)
            .RequireAuthorization(AccountEndpoints.UserPolicy);

        reports.MapPost("/", async (ReportDraft draft, ClaimsPrincipal user, IReportRepository repository, IPhotoStore photos, CancellationToken ct) =>
        {
            var errors = draft.Validate();
            if (errors.Count > 0)
                return Api.Invalid(errors);

            var (now, login) = (DateTime.UtcNow, AccountEndpoints.LoginOf(user));
            var report = Report.Create(draft, now) with
            {
                ReportedBy = login,
                Photo = await PhotoEndpoints.StoreAsync(draft.Photo, login, photos, now, ct)
            };
            await repository.AddAsync(report, ct);
            return Results.Created($"/api/reports/{report.Id}", report.ToReceipt());
        }).RequireRateLimiting(Api.SubmitLimit);

        reports.MapGet("/mine", async (ClaimsPrincipal user, IReportRepository repository, CancellationToken ct) =>
            Results.Ok((await repository.ListByReporterAsync(AccountEndpoints.LoginOf(user), MaxMine, ct)).Select(r => r.ToStatusView())));

        var official = app.MapGroup("/api/official/reports")
            .AddEndpointFilter(Api.DatabaseUnavailableFilter)
            .RequireAuthorization(OfficialEndpoints.OfficialPolicy);

        official.MapGet("/", async (string? cityId, ReportStatus? status, string? placeId, IReportRepository repository, CancellationToken ct) =>
            Results.Ok(await repository.ListAsync(new ReportFilter(cityId, status, placeId), MaxListed, ct)));

        official.MapPatch("/{id}", async (string id, ReportStatusChange change, ClaimsPrincipal user,
            IReportRepository repository, CancellationToken ct) =>
        {
            var errors = change.Validate();
            if (errors.Count > 0)
                return Api.Invalid(errors);
            if (!Api.IsId(id))
                return Results.NotFound();

            var updated = await repository.UpdateStatusAsync(id, change, OfficialEndpoints.ProfileOf(user).Login, DateTime.UtcNow, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        return app;
    }
}
