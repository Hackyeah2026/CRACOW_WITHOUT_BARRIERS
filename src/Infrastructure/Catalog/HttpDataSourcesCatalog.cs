using System.Net;
using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Places;

namespace Infrastructure.Catalog;

/// <summary>Spis źródeł danych ze statycznego pliku data/{miasto}/sources.json, zapisywanego przez import.</summary>
internal sealed class HttpDataSourcesCatalog(HttpClient http) : IDataSourcesCatalog
{
    public async Task<DataSourcesReport?> GetAsync(string cityId, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync($"data/{Uri.EscapeDataString(cityId)}/sources.json", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<DataSourcesReport>(DomainJson.Options, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
