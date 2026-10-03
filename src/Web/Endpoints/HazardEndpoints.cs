using System.Security.Claims;
using Application.Abstractions;
using Domain.Hazards;

namespace Web.Endpoints;

public static class HazardEndpoints
{
    private const int MaxMine = 100;
    private const int MaxListed = 500;

    /// <summary>Publiczna lista potwierdzonych punktów jest krótka i rzadko się zmienia, a czyta ją każde ułożenie planu.</summary>
    private const int MaxVerified = 2000;

    /// <summary>
    /// Punkty z utrudnieniami zaznaczane na mapie (wymagają konta). Publicznie widać tylko punkty potwierdzone
    /// przez urzędnika, bez zgłaszającego; weryfikacja wymaga sesji urzędnika.
    /// </summary>
    public static IEndpointRouteBuilder MapHazardEndpoints(this IEndpointRouteBuilder app)
    {
        var hazards = app.MapGroup("/api/hazards").AddEndpointFilter(ReportEndpoints.DatabaseUnavailableFilter);

        hazards.MapGet("/", async (string cityId, IHazardRepository repository, CancellationToken ct) =>
            Results.Ok((await repository.ListAsync(cityId, HazardStatus.Verified, MaxVerified, ct)).Select(h => h.ToVerified())));

        hazards.MapPost("/", async (HazardDraft draft, ClaimsPrincipal user, IHazardRepository repository, IPhotoStore photos, CancellationToken ct) =>
        {
            var errors = draft.Validate();
            if (errors.Count > 0)
                return Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);

            var (now, login) = (DateTime.UtcNow, AccountEndpoints.LoginOf(user));
            var hazard = Hazard.Create(draft, now) with
            {
                ReportedBy = login,
                Photo = await PhotoEndpoints.StoreAsync(draft.Photo, login, photos, now, ct)
            };
            await repository.AddAsync(hazard, ct);
            return Results.Created($"/api/hazards/{hazard.Id}", hazard.ToReceipt());
        }).RequireAuthorization(AccountEndpoints.UserPolicy).RequireRateLimiting(ReportEndpoints.SubmitLimit);

        hazards.MapGet("/mine", async (ClaimsPrincipal user, IHazardRepository repository, CancellationToken ct) =>
            Results.Ok((await repository.ListByReporterAsync(AccountEndpoints.LoginOf(user), MaxMine, ct)).Select(h => h.ToStatusView())))
            .RequireAuthorization(AccountEndpoints.UserPolicy);

        var official = app.MapGroup("/api/official/hazards")
            .AddEndpointFilter(ReportEndpoints.DatabaseUnavailableFilter)
            .RequireAuthorization(ReportEndpoints.OfficialPolicy);

        official.MapGet("/", async (string? cityId, IHazardRepository repository, CancellationToken ct) =>
            Results.Ok(await repository.ListAsync(cityId, null, MaxListed, ct)));

        official.MapPatch("/{id}", async (string id, HazardReview review, ClaimsPrincipal user,
            IHazardRepository repository, CancellationToken ct) =>
        {
            var errors = review.Validate();
            if (errors.Count > 0)
                return Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
            if (!ReportEndpoints.IsReportId(id))
                return Results.NotFound();

            var updated = await repository.ReviewAsync(id, review, ReportEndpoints.ProfileOf(user).Login, DateTime.UtcNow, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        return app;
    }
}
