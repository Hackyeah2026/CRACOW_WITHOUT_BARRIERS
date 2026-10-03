using Application.Abstractions;
using Domain.Needs;

namespace Application.Profile;

public sealed record GetProfileQuery : IQuery<NeedsProfile>;

internal sealed class GetProfileQueryHandler(ILocalStore store) : IQueryHandler<GetProfileQuery, NeedsProfile>
{
    public async Task<Result<NeedsProfile>> Handle(GetProfileQuery query, CancellationToken ct) =>
        Result.Success(await store.GetAsync<NeedsProfile>(LocalStores.Profile, ProfileKeys.Current) ?? NeedsProfile.Empty);
}

public sealed record SaveProfileCommand(NeedsProfile Profile) : ICommand;

internal sealed class SaveProfileCommandHandler(ILocalStore store) : ICommandHandler<SaveProfileCommand>
{
    public async Task<Result> Handle(SaveProfileCommand command, CancellationToken ct)
    {
        await store.PutAsync(LocalStores.Profile, ProfileKeys.Current, command.Profile);
        return Result.Success();
    }
}

internal static class ProfileKeys
{
    public const string Current = "current";
}
