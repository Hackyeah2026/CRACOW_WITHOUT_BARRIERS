using Application.Abstractions;
using Domain.Reports;
using MongoDB.Driver;

namespace Infrastructure.Mongo.Reports;

/// <summary>Zgłoszenia w kolekcji "reports"; dokument to <see cref="Report"/>, z identyfikatorem jako kluczem _id.</summary>
internal sealed class MongoReportRepository(MongoCollections collections) : IReportRepository
{
    private IMongoCollection<Report> Reports => collections.Get<Report>(MongoCollections.Reports);

    public Task AddAsync(Report report, CancellationToken ct) =>
        MongoCollections.RunAsync(async () =>
        {
            await Reports.InsertOneAsync(report, cancellationToken: ct);
            return true;
        });

    public Task<IReadOnlyList<Report>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
        MongoCollections.RunAsync<IReadOnlyList<Report>>(async () =>
            await Reports.Find(Builders<Report>.Filter.In(r => r.Id, ids)).ToListAsync(ct));

    public Task<IReadOnlyList<Report>> ListAsync(ReportFilter filter, int limit, CancellationToken ct) =>
        MongoCollections.RunAsync<IReadOnlyList<Report>>(async () =>
        {
            var where = Builders<Report>.Filter;
            var conditions = new List<FilterDefinition<Report>>();
            if (!string.IsNullOrEmpty(filter.CityId))
                conditions.Add(where.Eq(r => r.CityId, filter.CityId));
            if (filter.Status is { } status)
                conditions.Add(where.Eq(r => r.Status, status));
            if (!string.IsNullOrEmpty(filter.PlaceId))
                conditions.Add(where.Eq(r => r.PlaceId, filter.PlaceId));

            return await Reports
                .Find(conditions.Count == 0 ? where.Empty : where.And(conditions))
                .SortByDescending(r => r.CreatedAt)
                .Limit(limit)
                .ToListAsync(ct);
        });

    public Task<Report?> UpdateStatusAsync(string id, ReportStatusChange change, string officialLogin, DateTime now, CancellationToken ct) =>
        MongoCollections.RunAsync<Report?>(async () =>
        {
            var note = string.IsNullOrWhiteSpace(change.Note) ? null : change.Note.Trim();
            var update = Builders<Report>.Update
                .Set(r => r.Status, change.Status)
                .Set(r => r.OfficialNote, note)
                .Set(r => r.HandledBy, officialLogin)
                .Set(r => r.UpdatedAt, now);

            return await Reports.FindOneAndUpdateAsync(
                Builders<Report>.Filter.Eq(r => r.Id, id), update,
                new FindOneAndUpdateOptions<Report> { ReturnDocument = ReturnDocument.After }, ct);
        });

    /// <summary>Indeksy pod listę w panelu urzędnika (miasto + status, od najnowszych) i pod zgłoszenia jednego miejsca.</summary>
    public static Task EnsureIndexesAsync(IMongoCollection<Report> reports, CancellationToken ct)
    {
        var keys = Builders<Report>.IndexKeys;
        return reports.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<Report>(keys.Ascending(r => r.CityId).Ascending(r => r.Status).Descending(r => r.CreatedAt)),
            new CreateIndexModel<Report>(keys.Ascending(r => r.PlaceId))
        ], ct);
    }
}
