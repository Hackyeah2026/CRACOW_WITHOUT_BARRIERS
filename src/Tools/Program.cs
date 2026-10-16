using System.Text.Json;
using Domain;
using Tools;

// Użycie (z katalogu repozytorium):
//   dotnet run --project src/Tools -- import krakow     miejsca z OpenStreetMap + dane miejskie + ręczne uzupełnienia
//   dotnet run --project src/Tools -- city krakow       same dane miejskie (MSIP) dla istniejącego katalogu, bez pobierania OSM
//   dotnet run --project src/Tools -- transit krakow    sieć komunikacji miejskiej z GTFS
if (args is not [var command and ("import" or "city" or "transit"), var cityId] || !CityImports.All.TryGetValue(cityId, out var city))
{
    Console.Error.WriteLine($"Użycie: <import|city|transit> <miasto>. Dostępne miasta: {string.Join(", ", CityImports.All.Keys)}");
    return 1;
}

var root = FindRepoRoot();
var dataDir = Path.Combine(root, "src", "Web.Client", "wwwroot", "data", cityId);
Directory.CreateDirectory(dataDir);

if (command == "transit")
{
    if (city.GtfsUrl is null)
    {
        Console.Error.WriteLine($"Brak adresu GTFS dla miasta {cityId}.");
        return 1;
    }

    var network = await new GtfsTransitSource().GetNetworkAsync(city.GtfsUrl, city.GtfsSource ?? city.GtfsUrl);
    var transitPath = Path.Combine(dataDir, "transit.json");
    await File.WriteAllTextAsync(transitPath, JsonSerializer.Serialize(network, DomainJson.Options));
    Console.WriteLine($"Zapisano {network.Lines.Count} linii, {network.Lines.Sum(l => l.Patterns.Count)} przebiegów, " +
                      $"{network.Lines.Sum(l => l.Patterns.Sum(p => p.Trips.Count / 3))} kursów i {network.Stops.Count} przystanków do {transitPath}.");
    Console.WriteLine($"Rozkład ważny {network.ValidFrom:dd.MM.yyyy}-{network.ValidTo:dd.MM.yyyy}, pobrany {network.GeneratedOn:dd.MM.yyyy}.");
    return 0;
}

var output = Path.Combine(dataDir, "places");
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("KrakowBezBarier/1.0 (+https://github.com/Hackyeah2026/CRACOW_WITHOUT_BARRIERS)");

if (command == "city")
{
    PlaceFiles.Write(output, await CityData.ApplyAsync(PlaceFiles.Read(output), cityId, city, http, dataDir));
    return 0;
}

var overrides = Path.Combine(root, "src", "Tools", "overrides", $"{cityId}.json");

var places = await new OsmOverpassSource().GetPlacesAsync(cityId, city);
Console.WriteLine($"OSM: {places.Count} miejsc");
if (places.Count == 0)
{
    Console.Error.WriteLine("Import nie zwrócił żadnego miejsca. Plik z danymi zostaje bez zmian.");
    return 1;
}

places = await CityData.ApplyAsync(places, cityId, city, http, dataDir);
places = OverridesApplier.Apply(places, overrides);
PlaceFiles.Write(output, places);
return 0;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Uruchom z katalogu repozytorium.");
}
