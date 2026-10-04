using System.Security.Claims;
using System.Text.Json;
using Application.Abstractions;
using Domain;
using Domain.Trips;

namespace Web.Endpoints;

public static class PlanEndpoints
{
    /// <summary>Plany konta mieszkańca: każdy widzi i zmienia tylko swoje. Zapis trasy zamyka plan.</summary>
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var plans = app.MapGroup("/api/plans")
            .AddEndpointFilter(Api.DatabaseUnavailableFilter)
            .RequireAuthorization(AccountEndpoints.UserPolicy);

        plans.MapGet("/", async (ClaimsPrincipal user, ISavedPlanRepository repository, CancellationToken ct) =>
            Results.Ok(await repository.ListAsync(AccountEndpoints.LoginOf(user), SavedPlan.MaxPerAccount, ct)));

        plans.MapGet("/{id}", async (string id, ClaimsPrincipal user, ISavedPlanRepository repository, CancellationToken ct) =>
            Api.IsId(id) && await repository.FindAsync(AccountEndpoints.LoginOf(user), id, ct) is { } plan
                ? Results.Ok(plan)
                : Results.NotFound());

        plans.MapPost("/", async (SavedPlanDraft draft, ClaimsPrincipal user, ISavedPlanRepository repository, CancellationToken ct) =>
        {
            var errors = draft.Validate();
            if (errors.Count > 0)
                return Api.Invalid(errors);

            var login = AccountEndpoints.LoginOf(user);
            if (await repository.CountAsync(login, ct) >= SavedPlan.MaxPerAccount)
                return Api.Conflict($"Możesz mieć najwyżej {SavedPlan.MaxPerAccount} planów. Usuń któryś, żeby dodać nowy.");

            var plan = SavedPlan.Create(draft, login, DateTime.UtcNow);
            await repository.AddAsync(plan, ct);
            return Results.Created($"/api/plans/{plan.Id}", plan);
        });

        plans.MapPut("/{id}", async (string id, SavedPlanDraft draft, ClaimsPrincipal user, ISavedPlanRepository repository, CancellationToken ct) =>
        {
            var errors = draft.Validate();
            if (errors.Count > 0)
                return Api.Invalid(errors);
            if (!Api.IsId(id))
                return Results.NotFound();

            var login = AccountEndpoints.LoginOf(user);
            return await repository.UpdateDraftAsync(login, id, draft, DateTime.UtcNow, ct) is { } updated
                ? Results.Ok(updated)
                : await ClosedOrMissingAsync(repository, login, id, ct);
        });

        plans.MapPost("/{id}/close", async (string id, SavedPlanRoute route, ClaimsPrincipal user, ISavedPlanRepository repository, CancellationToken ct) =>
        {
            if (!IsRoute(route.RouteJson))
                return Api.Invalid("Nieprawidłowa trasa.");
            if (!Api.IsId(id))
                return Results.NotFound();

            var login = AccountEndpoints.LoginOf(user);
            return await repository.CloseAsync(login, id, route.RouteJson, DateTime.UtcNow, ct) is { } closed
                ? Results.Ok(closed)
                : await ClosedOrMissingAsync(repository, login, id, ct);
        });

        plans.MapDelete("/{id}", async (string id, ClaimsPrincipal user, ISavedPlanRepository repository, CancellationToken ct) =>
            Api.IsId(id) && await repository.DeleteAsync(AccountEndpoints.LoginOf(user), id, ct)
                ? Results.NoContent()
                : Results.NotFound());

        return app;
    }

    private static async Task<IResult> ClosedOrMissingAsync(ISavedPlanRepository repository, string login, string id, CancellationToken ct) =>
        await repository.FindAsync(login, id, ct) is null
            ? Results.NotFound()
            : Api.Conflict("Ten plan jest zamknięty: trasa została zapisana i nie można go już zmienić.");

    /// <summary>Host przechowuje trasę jako tekst, ale przyjmuje tylko taki, który da się odczytać jako plan z przystankami.</summary>
    private static bool IsRoute(string? json)
    {
        if (json is not { Length: > 0 and <= SavedPlanRoute.MaxRouteLength })
            return false;
        try
        {
            return JsonSerializer.Deserialize<TripPlan>(json, DomainJson.Options) is { Stops.Count: >= 2, Legs: not null };
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
