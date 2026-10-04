using Application.Abstractions;
using Domain.Photos;
using MongoDB.Driver;

namespace Infrastructure.Mongo.Reports;

/// <summary>
/// Zdjęcia zgłoszeń w kolekcji "photos", z identyfikatorem jako kluczem _id. Osobna kolekcja, bo listy zgłoszeń
/// w panelu czytają setki dokumentów naraz i nie mogą ciągnąć za sobą plików.
/// </summary>
internal sealed class MongoPhotoStore(MongoCollections collections) : IPhotoStore
{
    private IMongoCollection<StoredPhoto> Photos => collections.Get<StoredPhoto>(MongoCollections.Photos);

    public Task SaveAsync(StoredPhoto photo, CancellationToken ct) =>
        MongoCollections.RunAsync(() => Photos.InsertOneAsync(photo, cancellationToken: ct));

    public Task<StoredPhoto?> FindAsync(string id, CancellationToken ct) =>
        MongoCollections.RunAsync<StoredPhoto?>(async () => await Photos.Find(p => p.Id == id).FirstOrDefaultAsync(ct));
}
