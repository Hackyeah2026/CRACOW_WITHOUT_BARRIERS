using Application.Abstractions;
using Infrastructure.Mongo.Accounts;
using Infrastructure.Mongo.Businesses;
using Infrastructure.Mongo.Reports;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Infrastructure.Mongo;

public static class DependencyInjection
{
    /// <summary>
    /// Połączenie z MongoDB dla hosta. Klient powstaje przy pierwszym użyciu, więc aplikacja uruchamia się
    /// także bez skonfigurowanej bazy; funkcje, które jej potrzebują, dostają wtedy czytelny błąd.
    /// </summary>
    public static IServiceCollection AddMongo(this IServiceCollection services, MongoOptions options)
    {
        MongoConventions.Register();

        services.AddSingleton(options);

        // Jeden klient na proces: jest bezpieczny wątkowo i sam zarządza pulą połączeń.
        services.AddSingleton<IMongoClient>(_ =>
        {
            if (!options.IsConfigured)
                throw new InvalidOperationException(
                    "Brak adresu połączenia z MongoDB. Ustaw go: dotnet user-secrets set \"Mongo:ConnectionString\" \"...\" --project src/Web");

            var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
            settings.ApplicationName = "krakow-bez-barier";
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(options.ServerSelectionTimeoutSeconds);
            return new MongoClient(settings);
        });
        services.AddSingleton(provider => provider.GetRequiredService<IMongoClient>().GetDatabase(options.Database));
        services.AddSingleton<MongoHealth>();
        services.AddSingleton<MongoCollections>();
        return services;
    }

    /// <summary>Zgłoszenia mieszkańców, punkty z utrudnieniami, konta mieszkańców, firm i urzędników. Wymaga <see cref="AddMongo"/>.</summary>
    public static IServiceCollection AddMongoReports(this IServiceCollection services, OfficialsOptions officials) => services
        .AddSingleton(officials)
        .AddSingleton<IReportRepository, MongoReportRepository>()
        .AddSingleton<IHazardRepository, MongoHazardRepository>()
        .AddSingleton<IOfficialDirectory, MongoOfficialDirectory>()
        .AddSingleton<IUserDirectory, MongoUserDirectory>()
        .AddSingleton<IBusinessRepository, MongoBusinessRepository>()
        .AddHostedService<ReportsStartup>();
}
