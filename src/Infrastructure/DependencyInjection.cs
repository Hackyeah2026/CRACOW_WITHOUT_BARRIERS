using Application.Abstractions;
using Infrastructure.Browser;
using Infrastructure.Catalog;
using Infrastructure.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    /// <summary>Usługi działające w przeglądarce. Wymaga zarejestrowanego HttpClient z adresem bazowym aplikacji.</summary>
    public static IServiceCollection AddBrowserInfrastructure(this IServiceCollection services) => services
        .AddScoped<IPlaceCatalog, HttpPlaceCatalog>()
        .AddScoped<ILocalStore, IndexedDbLocalStore>()
        .AddScoped<IRoutingClient, StraightLineRoutingClient>();
}
