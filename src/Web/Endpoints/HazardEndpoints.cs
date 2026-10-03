using System.Security.Claims;
using Application.Abstractions;
using Domain.Hazards;

namespace Web.Endpoints;

public static class HazardEndpoints
{
    private const int MaxStatusIds = 100;
    private const int MaxListed = 500;

    /// <summary>Publiczna lista potwierdzonych punktów jest krótka i rzadko się zmienia, a czyta ją każde ułożenie planu.</summary>
    private const int MaxVerified = 2000;

    /// <summary>
    /// Punkty z utrudnieniami zaznaczane na mapie (anonimowe, bez danych osobowych). Publicznie widać tylko punkty
    /// potwierdzone przez urzędnika; weryfikacja wymaga sesji urzędnika.
    /// </summary>
    public static IEndpointRouteBuilder MapHazardEndpoints(this IEndpointRouteBuilder app)
    {
        var hazards = app.MapGroup("/api/hazards").AddEndpointFilter(ReportEndpoints.DatabaseUnavailableFilter);

        hazards.MapGet("/", async (string cityId, IHazardRepository repository, CancellationToken ct) =>
            Results.Ok((await repository.ListAsync(cityId, HazardStatus.Verified, MaxVerified, ct)).Select(h => h.ToVerified())));

        hazards.MapPost("/", async (HazardDraft draft, IHazardRepository repository, CancellationToken ct) =>
        {
            var errors = draft.Validate();
            if (errors.Count > 0)
                return Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);

            var hazard = Hazard.Create(draft, DateTime.UtcNow);
            await repository.AddAsync(hazard, ct);
            return Results.Created($"/api/hazards/{hazard.Id}", hazard.ToReceipt());
        }).RequireRateLimiting(ReportEndpoints.SubmitLimit);

        // Identyfikatory w treści żądania, nie w adresie: nie trafiają do logów serwerów pośrednich.
        hazards.MapPost("/status", async (StatusRequest request, IHazardRepository repository, CancellationToken ct) =>
        {
            var ids = request.Ids.Where(ReportEndpoints.IsReportId).Distinct().Take(MaxStatusIds).ToList();
            var found = ids.Count == 0 ? [] : await repository.GetByIdsAsync(ids, ct);
            return Results.Ok(found.Select(h => h.ToStatusView()));
        });

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

    private sealed record StatusRequest(IReadOnlyList<string> Ids);
}
