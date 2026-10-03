using System.Diagnostics;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Infrastructure.Mongo;

public enum MongoHealthStatus { Ok, NotConfigured, Unreachable }

/// <param name="Error">Opis błędu do logów hosta; może zawierać adresy serwerów, więc nie wysyłamy go do przeglądarki.</param>
public sealed record MongoHealthResult(MongoHealthStatus Status, string Database, long? LatencyMs = null, string? Error = null);

/// <summary>Sprawdza, czy host łączy się z bazą (polecenie ping).</summary>
public sealed class MongoHealth(MongoOptions options, IServiceProvider services)
{
    public async Task<MongoHealthResult> CheckAsync(CancellationToken ct)
    {
        if (!options.IsConfigured)
            return new MongoHealthResult(MongoHealthStatus.NotConfigured, options.Database);

        try
        {
            // Bazę pobieramy dopiero tutaj: błędny adres połączenia ma dać czytelny wynik, a nie wyjątek przy starcie.
            var database = (IMongoDatabase)services.GetService(typeof(IMongoDatabase))!;
            var watch = Stopwatch.StartNew();
            await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
            return new MongoHealthResult(MongoHealthStatus.Ok, options.Database, watch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException or FormatException or MongoConfigurationException)
        {
            return new MongoHealthResult(MongoHealthStatus.Unreachable, options.Database, Error: ex.Message);
        }
    }
}
