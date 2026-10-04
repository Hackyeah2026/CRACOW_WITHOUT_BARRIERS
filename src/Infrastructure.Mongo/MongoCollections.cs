using Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Infrastructure.Mongo;

/// <summary>
/// Dostęp do kolekcji z jednolitą obsługą braku bazy: brak adresu połączenia i błędy połączenia
/// stają się <see cref="DatabaseUnavailableException"/>, które host zamienia na odpowiedź 503.
/// </summary>
public sealed class MongoCollections(MongoOptions options, IServiceProvider services)
{
    public const string Reports = "reports";
    public const string Officials = "officials";
    public const string Hazards = "hazards";
    public const string Users = "users";
    public const string Businesses = "businesses";
    public const string Photos = "photos";
    public const string Plans = "plans";

    public bool IsConfigured => options.IsConfigured;

    public IMongoCollection<T> Get<T>(string name)
    {
        if (!options.IsConfigured)
            throw new DatabaseUnavailableException("Baza danych nie jest skonfigurowana.");

        // Bazę pobieramy dopiero tutaj, bo klient powstaje przy pierwszym użyciu.
        return services.GetRequiredService<IMongoDatabase>().GetCollection<T>(name);
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            throw new DatabaseUnavailableException("Baza danych nie odpowiada.", ex);
        }
    }
}
