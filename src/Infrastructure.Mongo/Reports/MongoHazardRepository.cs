using Application.Abstractions;
using Domain.Hazards;
using MongoDB.Driver;

namespace Infrastructure.Mongo.Reports;

/// <summary>Punkty z utrudnieniami w kolekcji "hazards"; dokument to <see cref="Hazard"/>, z identyfikatorem jako kluczem _id.</summary>
internal sealed class MongoHazardRepository(MongoCollections collections) : IHazardRepository
{
    private IMongoCollection<Hazard> Hazards => collections.Get<Hazard>(MongoCollections.Hazards);

    public Task AddAsync(Hazard hazard, CancellationToken ct) =>
        MongoCollections.RunAsync(async () =>
        {
            await Hazards.InsertOneAsync(hazard, cancellationToken: ct);
            return true;
        });

    public Task<IReadOnlyList<Hazard>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
        MongoCollections.RunAsync<IReadOnlyList<Hazard>>(async () =>
            await Hazards.Find(Builders<Hazard>.Filter.In(h => h.Id, ids)).ToListAsync(ct));

    public Task<IReadOnlyList<Hazard>> ListAsync(string? cityId, HazardStatus? status, int limit, CancellationToken ct) =>
        MongoCollections.RunAsync<IReadOnlyList<Hazard>>(async () =>
        {
            var where = Builders<Hazard>.Filter;
            var conditions = new List<FilterDefinition<Hazard>>();
            if (!string.IsNullOrEmpty(cityId))
                conditions.Add(where.Eq(h => h.CityId, cityId));
            if (status is { } wanted)
                conditions.Add(where.Eq(h => h.Status, wanted));

            return await Hazards
                .Find(conditions.Count == 0 ? where.Empty : where.And(conditions))
                .SortByDescending(h => h.CreatedAt)
                .Limit(limit)
                .ToListAsync(ct);
        });

    public Task<Hazard?> ReviewAsync(string id, HazardReview review, string officialLogin, DateTime now, CancellationToken ct) =>
        MongoCollections.RunAsync<Hazard?>(async () =>
        {
            var note = string.IsNullOrWhiteSpace(review.Note) ? null : review.Note.Trim();
            var update = Builders<Hazard>.Update
                .Set(h => h.Status, review.Status)
                .Set(h => h.OfficialNote, note)
                .Set(h => h.HandledBy, officialLogin)
                .Set(h => h.UpdatedAt, now);

            return await Hazards.FindOneAndUpdateAsync(
                Builders<Hazard>.Filter.Eq(h => h.Id, id), update,
                new FindOneAndUpdateOptions<Hazard> { ReturnDocument = ReturnDocument.After }, ct);
        });

    /// <summary>Indeks pod listę potwierdzonych punktów miasta i pod listę w panelu urzędnika.</summary>
    public static Task EnsureIndexesAsync(IMongoCollection<Hazard> hazards, CancellationToken ct)
    {
        var keys = Builders<Hazard>.IndexKeys;
        return hazards.Indexes.CreateOneAsync(
            new CreateIndexModel<Hazard>(keys.Ascending(h => h.CityId).Ascending(h => h.Status).Descending(h => h.CreatedAt)),
            cancellationToken: ct);
    }
}
