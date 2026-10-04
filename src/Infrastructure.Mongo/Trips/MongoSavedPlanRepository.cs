using Application.Abstractions;
using Domain.Trips;
using MongoDB.Driver;

namespace Infrastructure.Mongo.Trips;

/// <summary>Plany kont w kolekcji "plans"; dokument to <see cref="SavedPlan"/>, z identyfikatorem jako kluczem _id.</summary>
internal sealed class MongoSavedPlanRepository(MongoCollections collections) : ISavedPlanRepository
{
    private static readonly FindOneAndUpdateOptions<SavedPlan> ReturnUpdated = new()
    {
        ReturnDocument = ReturnDocument.After,
        Projection = Builders<SavedPlan>.Projection.Exclude(p => p.RouteJson)
    };

    private IMongoCollection<SavedPlan> Plans => collections.Get<SavedPlan>(MongoCollections.Plans);

    public Task<IReadOnlyList<SavedPlan>> ListAsync(string owner, int limit, CancellationToken ct) =>
        MongoCollections.RunAsync<IReadOnlyList<SavedPlan>>(async () => await Plans
            .Find(p => p.Owner == owner)
            .Project<SavedPlan>(Builders<SavedPlan>.Projection.Exclude(p => p.RouteJson))
            .SortByDescending(p => p.UpdatedAt)
            .Limit(limit)
            .ToListAsync(ct));

    public Task<long> CountAsync(string owner, CancellationToken ct) =>
        MongoCollections.RunAsync(() => Plans.CountDocumentsAsync(p => p.Owner == owner, cancellationToken: ct));

    public Task<SavedPlan?> FindAsync(string owner, string id, CancellationToken ct) =>
        MongoCollections.RunAsync<SavedPlan?>(async () => await Plans.Find(p => p.Id == id && p.Owner == owner).FirstOrDefaultAsync(ct));

    public Task AddAsync(SavedPlan plan, CancellationToken ct) =>
        MongoCollections.RunAsync(() => Plans.InsertOneAsync(plan, cancellationToken: ct));

    public Task<SavedPlan?> UpdateDraftAsync(string owner, string id, SavedPlanDraft draft, DateTime now, CancellationToken ct) =>
        MongoCollections.RunAsync<SavedPlan?>(async () => await Plans.FindOneAndUpdateAsync<SavedPlan>(
            p => p.Id == id && p.Owner == owner && p.Status == SavedPlanStatus.Draft,
            Builders<SavedPlan>.Update
                .Set(p => p.Name, draft.Name.Trim())
                .Set(p => p.PlaceIds, draft.PlaceIds)
                .Set(p => p.UpdatedAt, now),
            ReturnUpdated, ct));

    public Task<SavedPlan?> CloseAsync(string owner, string id, string routeJson, DateTime now, CancellationToken ct) =>
        MongoCollections.RunAsync<SavedPlan?>(async () => await Plans.FindOneAndUpdateAsync<SavedPlan>(
            p => p.Id == id && p.Owner == owner && p.Status == SavedPlanStatus.Draft,
            Builders<SavedPlan>.Update
                .Set(p => p.Status, SavedPlanStatus.Closed)
                .Set(p => p.RouteJson, routeJson)
                .Set(p => p.UpdatedAt, now),
            ReturnUpdated, ct));

    public Task<bool> DeleteAsync(string owner, string id, CancellationToken ct) =>
        MongoCollections.RunAsync(async () =>
            (await Plans.DeleteOneAsync(p => p.Id == id && p.Owner == owner, ct)).DeletedCount > 0);

    /// <summary>Indeks pod listę planów jednego konta.</summary>
    public static Task EnsureIndexesAsync(IMongoCollection<SavedPlan> plans, CancellationToken ct) =>
        plans.Indexes.CreateOneAsync(new CreateIndexModel<SavedPlan>(
            Builders<SavedPlan>.IndexKeys.Ascending(p => p.Owner).Descending(p => p.UpdatedAt)), cancellationToken: ct);
}
