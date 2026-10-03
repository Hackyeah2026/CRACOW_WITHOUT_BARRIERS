using Application.Abstractions;
using Domain.Businesses;
using MongoDB.Driver;

namespace Infrastructure.Mongo.Businesses;

/// <summary>Konta firmowe w kolekcji "businesses"; dokument to <see cref="BusinessAccount"/>, z loginem konta jako kluczem _id.</summary>
internal sealed class MongoBusinessRepository(MongoCollections collections) : IBusinessRepository
{
    private static FilterDefinitionBuilder<BusinessAccount> Where => Builders<BusinessAccount>.Filter;

    private IMongoCollection<BusinessAccount> Businesses => collections.Get<BusinessAccount>(MongoCollections.Businesses);

    public Task<BusinessAccount?> FindAsync(string login, CancellationToken ct) =>
        MongoCollections.RunAsync<BusinessAccount?>(async () =>
            await Businesses.Find(Where.Eq(b => b.Id, login)).FirstOrDefaultAsync(ct));

    public Task<bool> SubmitAsync(BusinessAccount account, CancellationToken ct) =>
        MongoCollections.RunAsync(async () =>
        {
            try
            {
                // Zatwierdzone konto nie pasuje do filtra, więc zapis próbuje wstawić drugi dokument z tym samym kluczem i kończy się błędem.
                await Businesses.ReplaceOneAsync(
                    Where.And(Where.Eq(b => b.Id, account.Id), Where.Ne(b => b.Status, BusinessStatus.Approved)),
                    account, new ReplaceOptions { IsUpsert = true }, ct);
                return true;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                return false;
            }
        });

    public Task<bool> IsPlaceTakenAsync(string cityId, string placeId, string exceptLogin, CancellationToken ct) =>
        MongoCollections.RunAsync(async () => await Businesses
            .Find(Where.And(
                Where.Eq(b => b.CityId, cityId), Where.Eq(b => b.PlaceId, placeId),
                Where.Eq(b => b.Status, BusinessStatus.Approved), Where.Ne(b => b.Id, exceptLogin)))
            .AnyAsync(ct));

    public Task<IReadOnlyList<BusinessAccount>> ListAsync(string? cityId, BusinessStatus? status, int limit, CancellationToken ct) =>
        MongoCollections.RunAsync<IReadOnlyList<BusinessAccount>>(async () =>
        {
            var conditions = new List<FilterDefinition<BusinessAccount>>();
            if (!string.IsNullOrEmpty(cityId))
                conditions.Add(Where.Eq(b => b.CityId, cityId));
            if (status is { } wanted)
                conditions.Add(Where.Eq(b => b.Status, wanted));

            return await Businesses
                .Find(conditions.Count == 0 ? Where.Empty : Where.And(conditions))
                .SortByDescending(b => b.CreatedAt)
                .Limit(limit)
                .ToListAsync(ct);
        });

    public Task<BusinessSaveResult> ReplaceAsync(BusinessAccount account, DateTime previousUpdatedAt, CancellationToken ct) =>
        MongoCollections.RunAsync(async () =>
        {
            try
            {
                var result = await Businesses.ReplaceOneAsync(
                    Where.And(Where.Eq(b => b.Id, account.Id), Where.Eq(b => b.UpdatedAt, previousUpdatedAt)),
                    account, cancellationToken: ct);
                return result.MatchedCount == 1 ? BusinessSaveResult.Saved : BusinessSaveResult.Stale;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                return BusinessSaveResult.PlaceTaken;
            }
        });

    /// <summary>
    /// Indeks pod listy (miasto + status, od najnowszych) i unikalny indeks częściowy: jedno miejsce ma najwyżej
    /// jedno zatwierdzone konto firmowe, także gdy dwie decyzje zapadają równocześnie.
    /// </summary>
    public static Task EnsureIndexesAsync(IMongoCollection<BusinessAccount> businesses, CancellationToken ct)
    {
        var keys = Builders<BusinessAccount>.IndexKeys;
        return businesses.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<BusinessAccount>(keys.Ascending(b => b.CityId).Ascending(b => b.Status).Descending(b => b.CreatedAt)),
            new CreateIndexModel<BusinessAccount>(keys.Ascending(b => b.CityId).Ascending(b => b.PlaceId),
                new CreateIndexOptions<BusinessAccount>
                {
                    Name = "approved_place_unique",
                    Unique = true,
                    PartialFilterExpression = Where.Eq(b => b.Status, BusinessStatus.Approved)
                })
        ], ct);
    }
}
