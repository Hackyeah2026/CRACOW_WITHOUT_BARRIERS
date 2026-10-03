using Application.Abstractions;
using Domain.Reports;
using MongoDB.Driver;

namespace Infrastructure.Mongo.Reports;

/// <summary>Konto urzędnika w kolekcji "officials". Kluczem _id jest login zapisany małymi literami.</summary>
public sealed record OfficialAccount(string Id, string DisplayName, string Unit, string PasswordHash, DateTime CreatedAt)
{
    public OfficialProfile ToProfile() => new(Id, DisplayName, Unit);

    public static string NormalizeLogin(string login) => login.Trim().ToLowerInvariant();
}

internal sealed class MongoOfficialDirectory(MongoCollections collections) : IOfficialDirectory
{
    // Porównanie z hasłem do nieistniejącego konta trwa tyle samo, więc czas odpowiedzi nie zdradza, które loginy istnieją.
    private static readonly string DummyHash = PasswordHashing.Hash(Guid.NewGuid().ToString());

    public Task<OfficialProfile?> VerifyAsync(OfficialLogin login, CancellationToken ct) =>
        MongoCollections.RunAsync(async () =>
        {
            var id = OfficialAccount.NormalizeLogin(login.Login);
            var account = await collections.Get<OfficialAccount>(MongoCollections.Officials)
                .Find(a => a.Id == id).FirstOrDefaultAsync(ct);

            var valid = PasswordHashing.Verify(login.Password, account?.PasswordHash ?? DummyHash);
            return valid && account is not null ? account.ToProfile() : null;
        });
}
