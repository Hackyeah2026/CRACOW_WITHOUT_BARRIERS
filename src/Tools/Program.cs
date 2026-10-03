using System.Text.Json;
using Domain;
using Tools;

// Użycie (z katalogu repozytorium):
//   dotnet run --project src/Tools -- import krakow     miejsca z OpenStreetMap + ręczne uzupełnienia
//   dotnet run --project src/Tools -- transit krakow    sieć komunikacji miejskiej z GTFS
if (args is not [var command and ("import" or "transit"), var cityId] || !CityImports.All.TryGetValue(cityId, out var city))
{
    Console.Error.WriteLine($"Użycie: <import|transit> <miasto>. Dostępne miasta: {string.Join(", ", CityImports.All.Keys)}");
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

var output = Path.Combine(dataDir, "places.json");
var overrides = Path.Combine(root, "src", "Tools", "overrides", $"{cityId}.json");

var places = await new OsmOverpassSource().GetPlacesAsync(cityId, city);
Console.WriteLine($"OSM: {places.Count} miejsc");

places = OverridesApplier.Apply(places, overrides);

await File.WriteAllTextAsync(output, JsonSerializer.Serialize(places, DomainJson.Options));
Console.WriteLine($"Zapisano {places.Count} miejsc do {output}");
foreach (var group in places.GroupBy(p => p.Category).OrderByDescending(g => g.Count()))
    Console.WriteLine($"  {group.Key}: {group.Count()}, z cechami: {group.Count(p => p.Features.Count > 0)}");
return 0;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Uruchom z katalogu repozytorium.");
}
