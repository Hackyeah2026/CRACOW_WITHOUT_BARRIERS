using Application.Abstractions;
using Domain.Accounts;
using MongoDB.Driver;

namespace Infrastructure.Mongo.Accounts;

/// <summary>Konto mieszkańca w kolekcji "users". Kluczem _id jest login zapisany małymi literami, więc baza pilnuje jego unikalności.</summary>
public sealed record UserAccount(string Id, string PasswordHash, DateTime CreatedAt)
{
    public UserProfile ToProfile() => new(Id);
}

internal sealed class MongoUserDirectory(MongoCollections collections) : IUserDirectory
{
    private IMongoCollection<UserAccount> Users => collections.Get<UserAccount>(MongoCollections.Users);

    public Task<UserProfile?> RegisterAsync(UserCredentials credentials, DateTime now, CancellationToken ct) =>
        MongoCollections.RunAsync(async () =>
        {
            var account = new UserAccount(UserCredentials.NormalizeLogin(credentials.Login), PasswordHashing.Hash(credentials.Password), now);
            try
            {
                await Users.InsertOneAsync(account, cancellationToken: ct);
                return account.ToProfile();
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                return null;
            }
        });

    public Task<UserProfile?> VerifyAsync(UserCredentials credentials, CancellationToken ct) =>
        MongoCollections.RunAsync(async () =>
        {
            var id = UserCredentials.NormalizeLogin(credentials.Login);
            var account = await Users.Find(a => a.Id == id).FirstOrDefaultAsync(ct);

            return PasswordHashing.VerifyAccount(credentials.Password, account?.PasswordHash) ? account?.ToProfile() : null;
        });
}
