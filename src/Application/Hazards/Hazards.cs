using Application.Abstractions;
using Domain.Hazards;

namespace Application.Hazards;

/// <summary>Wysyła punkt z utrudnieniem z konta zalogowanego mieszkańca.</summary>
public sealed record SubmitHazardCommand(HazardDraft Draft) : ICommand<HazardReceipt>;

internal sealed class SubmitHazardCommandHandler(IHazardsClient client) : ICommandHandler<SubmitHazardCommand, HazardReceipt>
{
    public Task<Result<HazardReceipt>> Handle(SubmitHazardCommand command, CancellationToken ct)
    {
        var errors = command.Draft.Validate();
        return errors.Count > 0
            ? Task.FromResult(Result.Failure<HazardReceipt>(string.Join(" ", errors)))
            : client.SubmitAsync(command.Draft, ct);
    }
}

/// <summary>Punkty zalogowanego mieszkańca, od najnowszych, ze stanem weryfikacji.</summary>
public sealed record GetMyHazardsQuery : IQuery<IReadOnlyList<HazardStatusView>>;

internal sealed class GetMyHazardsQueryHandler(IHazardsClient client) : IQueryHandler<GetMyHazardsQuery, IReadOnlyList<HazardStatusView>>
{
    public Task<Result<IReadOnlyList<HazardStatusView>>> Handle(GetMyHazardsQuery query, CancellationToken ct) => client.GetMineAsync(ct);
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
