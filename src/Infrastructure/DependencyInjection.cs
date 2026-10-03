using Application.Abstractions;
using Infrastructure.Browser;
using Infrastructure.Catalog;
using Infrastructure.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    /// <summary>Usługi działające w przeglądarce. Wymaga zarejestrowanego HttpClient z adresem bazowym aplikacji.</summary>
    public static IServiceCollection AddBrowserInfrastructure(this IServiceCollection services) => services
        .AddScoped<HttpPlaceCatalog>()
        .AddScoped<IBusinessClient, HostBusinessClient>()
        // Katalog z plików uzupełniony o certyfikaty i deklaracje zatwierdzonych firm.
        .AddScoped<IPlaceCatalog>(p => new CertifiedPlaceCatalog(p.GetRequiredService<HttpPlaceCatalog>(), p.GetRequiredService<IBusinessClient>()))
        .AddScoped<ITransitCatalog, HttpTransitCatalog>()
        .AddScoped<ILocalStore, IndexedDbLocalStore>()
        .AddScoped<IRoutingClient, HostRoutingClient>()
        .AddScoped<IReportsClient, HostReportsClient>()
        .AddScoped<IHazardsClient, HostHazardsClient>()
        .AddScoped<IOfficialClient, HostOfficialClient>()
        .AddScoped<IAccountClient, HostAccountClient>();

    /// <summary>Usługi hosta: integracje wymagające kluczy API.</summary>
    public static IServiceCollection AddServerInfrastructure(this IServiceCollection services, OpenRouteServiceOptions routing)
    {
        services.AddSingleton(routing);
        services.AddHttpClient<IRouteProvider, OpenRouteServiceClient>(client =>
        {
            client.BaseAddress = new Uri(routing.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        return services;
    }
}
