using Application.Abstractions;
using Domain.Accounts;
using Domain.Reports;
using MongoDB.Driver;

namespace Infrastructure.Mongo.Accounts;

/// <summary>Konto urzędnika w kolekcji "officials". Kluczem _id jest login zapisany małymi literami.</summary>
public sealed record OfficialAccount(string Id, string DisplayName, string Unit, string PasswordHash, DateTime CreatedAt)
{
    public OfficialProfile ToProfile() => new(Id, DisplayName, Unit);
}

internal sealed class MongoOfficialDirectory(MongoCollections collections) : IOfficialDirectory
{
    public Task<OfficialProfile?> VerifyAsync(OfficialLogin login, CancellationToken ct) =>
        MongoCollections.RunAsync(async () =>
        {
            var id = UserCredentials.NormalizeLogin(login.Login);
            var account = await collections.Get<OfficialAccount>(MongoCollections.Officials)
                .Find(a => a.Id == id).FirstOrDefaultAsync(ct);

            return PasswordHashing.VerifyAccount(login.Password, account?.PasswordHash) ? account?.ToProfile() : null;
        });
}
