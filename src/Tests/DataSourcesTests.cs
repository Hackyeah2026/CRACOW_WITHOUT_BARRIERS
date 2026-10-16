using Domain.Places;
using Tools;

namespace Tests;

public class DataSourcesTests
{
    [Fact]
    public void Markdown_lists_every_source_with_licence_dates_count_and_city_portal_flag()
    {
        var report = new DataSourcesReport(new DateOnly(2026, 10, 17),
        [
            new("Mapa hałasu 2022", "Gmina Miejska Kraków, MSIP", "https://msip.um.krakow.pl/x", CityData.CityPortalLicense, "2022",
                new DateOnly(2026, 10, 16), 17444, "Hałas otoczenia | profile sensoryczne", true),
            new("OpenRouteService", "HeiGIT", "https://openrouteservice.org/", "Regulamin usługi", "na bieżąco", new DateOnly(2026, 10, 16), null, "Trasy", false)
        ]);

        var md = DataSourcesWriter.Markdown(report);

        Assert.Contains("| Mapa hałasu 2022 | Gmina Miejska Kraków, MSIP | tak |", md);
        Assert.Contains("17 444", md);
        Assert.Contains("Hałas otoczenia \\| profile sensoryczne", md);
        Assert.Contains("| usługa |", md);
        Assert.Contains("brak danych", md);
    }

    [Fact]
    public void Osm_date_is_kept_from_the_previous_import_when_only_city_data_is_refreshed()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var previous = new DataSourcesReport(new DateOnly(2026, 10, 3),
                [new("OpenStreetMap", "x", "u", "ODbL", "d", new DateOnly(2026, 10, 3), 1, "u", false)]);
            File.WriteAllText(Path.Combine(dir, "sources.json"), System.Text.Json.JsonSerializer.Serialize(previous, Domain.DomainJson.Options));

            Assert.Equal(new DateOnly(2026, 10, 3), DataSourcesWriter.OsmRetrievedOn(dir, freshImport: false));
            Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), DataSourcesWriter.OsmRetrievedOn(dir, freshImport: true));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
