using Application.Abstractions;
using Domain.Hazards;
using MongoDB.Driver;

namespace Infrastructure.Mongo.Reports;

/// <summary>Punkty z utrudnieniami w kolekcji "hazards"; dokument to <see cref="Hazard"/>, z identyfikatorem jako kluczem _id.</summary>
internal sealed class MongoHazardRepository(MongoCollections collections) : IHazardRepository
{
    private IMongoCollection<Hazard> Hazards => collections.Get<Hazard>(MongoCollections.Hazards);

    public Task AddAsync(Hazard hazard, CancellationToken ct) =>
        MongoCollections.RunAsync(() => Hazards.InsertOneAsync(hazard, cancellationToken: ct));

    public Task<IReadOnlyList<Hazard>> ListByReporterAsync(string login, int limit, CancellationToken ct) =>
        MongoCollections.RunAsync<IReadOnlyList<Hazard>>(async () =>
            await Hazards.Find(h => h.ReportedBy == login).SortByDescending(h => h.CreatedAt).Limit(limit).ToListAsync(ct));

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

    /// <summary>Indeksy pod listę potwierdzonych punktów miasta, listę w panelu urzędnika i punkty jednego konta.</summary>
    public static Task EnsureIndexesAsync(IMongoCollection<Hazard> hazards, CancellationToken ct)
    {
        var keys = Builders<Hazard>.IndexKeys;
        return hazards.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<Hazard>(keys.Ascending(h => h.CityId).Ascending(h => h.Status).Descending(h => h.CreatedAt)),
            new CreateIndexModel<Hazard>(keys.Ascending(h => h.ReportedBy).Descending(h => h.CreatedAt))
        ], ct);
    }
}
