using Application.Abstractions;
using Domain.Needs;

namespace Application.Profile;

/// <summary>
/// Profil potrzeb z urządzenia. Bez loginu: konfiguracja tymczasowa tej przeglądarki. Z loginem: profil zapisany
/// dla tego konta; konto, które nie ma jeszcze profilu, przejmuje konfigurację tymczasową.
/// </summary>
public sealed record GetProfileQuery(string? Login = null) : IQuery<NeedsProfile>;

internal sealed class GetProfileQueryHandler(ILocalStore store) : IQueryHandler<GetProfileQuery, NeedsProfile>
{
    public async Task<Result<NeedsProfile>> Handle(GetProfileQuery query, CancellationToken ct)
    {
        var temporary = await store.GetAsync<NeedsProfile>(LocalStores.Profile, ProfileKeys.Temporary) ?? NeedsProfile.Empty;
        if (query.Login is null)
            return Result.Success(temporary);

        var key = ProfileKeys.Account(query.Login);
        if (await store.GetAsync<NeedsProfile>(LocalStores.Profile, key) is { } saved)
            return Result.Success(saved);

        // Kto ustawił profil przed założeniem konta, nie powinien robić tego drugi raz.
        if (!temporary.IsEmpty)
            await store.PutAsync(LocalStores.Profile, key, temporary);
        return Result.Success(temporary);
    }
}

/// <summary>Zapisuje profil na urządzeniu: dla konta o podanym loginie albo jako konfigurację tymczasową.</summary>
public sealed record SaveProfileCommand(NeedsProfile Profile, string? Login = null) : ICommand;

internal sealed class SaveProfileCommandHandler(ILocalStore store) : ICommandHandler<SaveProfileCommand>
{
    public async Task<Result> Handle(SaveProfileCommand command, CancellationToken ct)
    {
        await store.PutAsync(LocalStores.Profile,
            command.Login is null ? ProfileKeys.Temporary : ProfileKeys.Account(command.Login), command.Profile);
        return Result.Success();
    }
}

internal static class ProfileKeys
{
    /// <summary>Klucz sprzed wprowadzenia kont; został, żeby ustawione już profile nie zniknęły.</summary>
    public const string Temporary = "current";

    public static string Account(string login) => $"account:{login}";
}
