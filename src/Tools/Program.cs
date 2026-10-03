using System.Text.Json;
using Domain;
using Domain.Places;
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

var output = Path.Combine(dataDir, "places");
var overrides = Path.Combine(root, "src", "Tools", "overrides", $"{cityId}.json");

var places = await new OsmOverpassSource().GetPlacesAsync(cityId, city);
Console.WriteLine($"OSM: {places.Count} miejsc");
if (places.Count == 0)
{
    Console.Error.WriteLine("Import nie zwrócił żadnego miejsca. Plik z danymi zostaje bez zmian.");
    return 1;
}

places = OverridesApplier.Apply(places, overrides);

// Jeden plik na kategorię: aplikacja pobiera tylko kategorie potrzebne w danym widoku.
if (Directory.Exists(output))
    Directory.Delete(output, recursive: true);
Directory.CreateDirectory(output);

var groups = places.GroupBy(p => p.Category).OrderBy(g => g.Key).ToList();
foreach (var group in groups)
    await File.WriteAllTextAsync(Path.Combine(output, $"{group.Key}.json"), JsonSerializer.Serialize(group.ToList(), DomainJson.Options));

var index = new PlaceIndex(DateOnly.FromDateTime(DateTime.UtcNow), groups.Select(g => new PlaceCategoryCount(g.Key, g.Count())).ToList());
await File.WriteAllTextAsync(Path.Combine(output, "index.json"), JsonSerializer.Serialize(index, DomainJson.Indented));

Console.WriteLine($"Zapisano {places.Count} miejsc do {output}");
foreach (var group in groups.OrderByDescending(g => g.Count()))
    Console.WriteLine($"  {group.Key}: {group.Count()}, z cechami: {group.Count(p => p.Features.Count > 0)}, " +
                      $"plik {new FileInfo(Path.Combine(output, $"{group.Key}.json")).Length / 1024} KB");
return 0;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Uruchom z katalogu repozytorium.");
}
