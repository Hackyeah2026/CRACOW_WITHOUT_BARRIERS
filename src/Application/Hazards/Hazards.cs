using Application.Abstractions;
using Domain.Hazards;

namespace Application.Hazards;

/// <summary>Wysyła punkt z utrudnieniem i zapamiętuje jego identyfikator na urządzeniu, żeby można było sprawdzić decyzję urzędu.</summary>
public sealed record SubmitHazardCommand(HazardDraft Draft) : ICommand<HazardReceipt>;

internal sealed class SubmitHazardCommandHandler(IHazardsClient client, ILocalStore store)
    : ICommandHandler<SubmitHazardCommand, HazardReceipt>
{
    public async Task<Result<HazardReceipt>> Handle(SubmitHazardCommand command, CancellationToken ct)
    {
        var errors = command.Draft.Validate();
        if (errors.Count > 0)
            return Result.Failure<HazardReceipt>(string.Join(" ", errors));

        var result = await client.SubmitAsync(command.Draft, ct);
        if (result.IsSuccess)
        {
            // Punkt już jest w bazie; brak lokalnej kopii nie może wyglądać na nieudaną wysyłkę.
            try { await store.PutAsync(LocalStores.Hazards, result.Value.Id, result.Value); }
            catch (Exception) { }
        }
        return result;
    }
}

/// <param name="Current">Stan z bazy; null, gdy host nie odpowiedział albo punktu już nie ma.</param>
public sealed record MyHazard(HazardReceipt Receipt, HazardStatusView? Current);

/// <summary>Punkty wysłane z tego urządzenia, od najnowszych, ze stanem weryfikacji.</summary>
public sealed record GetMyHazardsQuery : IQuery<IReadOnlyList<MyHazard>>;

internal sealed class GetMyHazardsQueryHandler(IHazardsClient client, ILocalStore store)
    : IQueryHandler<GetMyHazardsQuery, IReadOnlyList<MyHazard>>
{
    public async Task<Result<IReadOnlyList<MyHazard>>> Handle(GetMyHazardsQuery query, CancellationToken ct)
    {
        var receipts = (await store.ListAsync<HazardReceipt>(LocalStores.Hazards))
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
        if (receipts.Count == 0)
            return Result.Success<IReadOnlyList<MyHazard>>([]);

        var statuses = await client.GetStatusesAsync(receipts.Select(r => r.Id).ToList(), ct);
        var byId = statuses.IsSuccess ? statuses.Value.ToDictionary(s => s.Id) : [];

        IReadOnlyList<MyHazard> result = receipts.Select(r => new MyHazard(r, byId.GetValueOrDefault(r.Id))).ToList();
        return Result.Success(result);
    }
}

/// <summary>Punkty potwierdzone przez urząd, widoczne dla wszystkich.</summary>
public sealed record GetVerifiedHazardsQuery(string CityId) : IQuery<IReadOnlyList<VerifiedHazard>>;

internal sealed class GetVerifiedHazardsQueryHandler(IHazardsClient client)
    : IQueryHandler<GetVerifiedHazardsQuery, IReadOnlyList<VerifiedHazard>>
{
    public Task<Result<IReadOnlyList<VerifiedHazard>>> Handle(GetVerifiedHazardsQuery query, CancellationToken ct) =>
        client.GetVerifiedAsync(query.CityId, ct);
}

/// <summary>Wszystkie punkty w mieście dla panelu urzędnika.</summary>
public sealed record GetHazardsQuery(string? CityId) : IQuery<IReadOnlyList<Hazard>>;

internal sealed class GetHazardsQueryHandler(IOfficialClient client) : IQueryHandler<GetHazardsQuery, IReadOnlyList<Hazard>>
{
    public Task<Result<IReadOnlyList<Hazard>>> Handle(GetHazardsQuery query, CancellationToken ct) =>
        client.GetHazardsAsync(query.CityId, ct);
}

public sealed record ReviewHazardCommand(string HazardId, HazardReview Review) : ICommand<Hazard>;

internal sealed class ReviewHazardCommandHandler(IOfficialClient client) : ICommandHandler<ReviewHazardCommand, Hazard>
{
    public Task<Result<Hazard>> Handle(ReviewHazardCommand command, CancellationToken ct)
    {
        var errors = command.Review.Validate();
        return errors.Count > 0
            ? Task.FromResult(Result.Failure<Hazard>(string.Join(" ", errors)))
            : client.ReviewHazardAsync(command.HazardId, command.Review, ct);
    }
}
