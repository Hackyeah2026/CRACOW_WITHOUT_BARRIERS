using System.Text.Json;
using Application.Abstractions;
using Domain;
using Domain.Assessments;
using Domain.Needs;
using Domain.Trips;

namespace Application.Trips;

/// <summary>Plany zalogowanego mieszkańca, od ostatnio zmienianych, bez zapisanych tras.</summary>
public sealed record GetMyPlansQuery : IQuery<IReadOnlyList<SavedPlan>>;

internal sealed class GetMyPlansQueryHandler(ISavedPlansClient client) : IQueryHandler<GetMyPlansQuery, IReadOnlyList<SavedPlan>>
{
    public Task<Result<IReadOnlyList<SavedPlan>>> Handle(GetMyPlansQuery query, CancellationToken ct) => client.GetMineAsync(ct);
}

/// <summary>Trasa zapisana w zamkniętym planie, z oceną miejsc policzoną na urządzeniu dla podanego profilu.</summary>
public sealed record GetSavedRouteQuery(string PlanId, NeedsProfile Profile) : IQuery<TripPlan>;

internal sealed class GetSavedRouteQueryHandler(ISavedPlansClient client) : IQueryHandler<GetSavedRouteQuery, TripPlan>
{
    public async Task<Result<TripPlan>> Handle(GetSavedRouteQuery query, CancellationToken ct)
    {
        var plan = await client.GetAsync(query.PlanId, ct);
        if (plan.IsFailure)
            return Result.Failure<TripPlan>(plan.Error!);

        return SavedRoutes.Read(plan.Value.RouteJson, query.Profile) is { } route
            ? Result.Success(route)
            : Result.Failure<TripPlan>("Nie udało się odczytać zapisanej trasy.");
    }
}

public sealed record CreatePlanCommand(SavedPlanDraft Draft) : ICommand<SavedPlan>;

internal sealed class CreatePlanCommandHandler(ISavedPlansClient client) : ICommandHandler<CreatePlanCommand, SavedPlan>
{
    public Task<Result<SavedPlan>> Handle(CreatePlanCommand command, CancellationToken ct)
    {
        var errors = command.Draft.Validate();
        return errors.Count > 0
            ? Task.FromResult(Result.Failure<SavedPlan>(string.Join(" ", errors)))
            : client.CreateAsync(command.Draft, ct);
    }
}

/// <summary>Zmienia nazwę i miejsca planu w przygotowaniu.</summary>
public sealed record UpdatePlanCommand(string PlanId, SavedPlanDraft Draft) : ICommand<SavedPlan>;

internal sealed class UpdatePlanCommandHandler(ISavedPlansClient client) : ICommandHandler<UpdatePlanCommand, SavedPlan>
{
    public Task<Result<SavedPlan>> Handle(UpdatePlanCommand command, CancellationToken ct)
    {
        var errors = command.Draft.Validate();
        return errors.Count > 0
            ? Task.FromResult(Result.Failure<SavedPlan>(string.Join(" ", errors)))
            : client.UpdateAsync(command.PlanId, command.Draft, ct);
    }
}

/// <summary>Zapisuje wyznaczoną trasę i zamyka plan: od tej chwili nie da się go edytować.</summary>
public sealed record ClosePlanCommand(string PlanId, TripPlan Route) : ICommand<SavedPlan>;

internal sealed class ClosePlanCommandHandler(ISavedPlansClient client) : ICommandHandler<ClosePlanCommand, SavedPlan>
{
    public Task<Result<SavedPlan>> Handle(ClosePlanCommand command, CancellationToken ct)
    {
        var json = SavedRoutes.Write(command.Route);
        return json.Length > SavedPlanRoute.MaxRouteLength
            ? Task.FromResult(Result.Failure<SavedPlan>("Trasa jest za długa, żeby ją zapisać. Usuń część miejsc z planu."))
            : client.CloseAsync(command.PlanId, new SavedPlanRoute(json), ct);
    }
}

public sealed record DeletePlanCommand(string PlanId) : ICommand;

internal sealed class DeletePlanCommandHandler(ISavedPlansClient client) : ICommandHandler<DeletePlanCommand>
{
    public Task<Result> Handle(DeletePlanCommand command, CancellationToken ct) => client.DeleteAsync(command.PlanId, ct);
}

/// <summary>
/// Zapis trasy do planu konta. Ocena miejsc wynika z profilu potrzeb, więc nie trafia na serwer:
/// przy odczycie liczymy ją od nowa na urządzeniu.
/// </summary>
public static class SavedRoutes
{
    private static readonly Assessment NotAssessed = new(AssessmentStatus.Unknown, []);

    public static string Write(TripPlan route) => JsonSerializer.Serialize(
        route with { Stops = route.Stops.Select(s => s with { Assessment = NotAssessed }).ToList() }, DomainJson.Options);

    /// <summary>Trasa z oceną dla profilu; null, gdy zapis jest pusty albo uszkodzony.</summary>
    public static TripPlan? Read(string? json, NeedsProfile profile)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<TripPlan>(json, DomainJson.Options) is { Stops: not null, Legs: not null } route
                ? route with { Stops = route.Stops.Select(s => s with { Assessment = AssessmentEngine.Assess(profile, s.Place) }).ToList() }
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
