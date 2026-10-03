using Tools;

// Użycie: dotnet run --project src/Tools -- import krakow
if (args is not ["import", var cityId] || !CityImports.All.TryGetValue(cityId, out var city))
{
    Console.Error.WriteLine($"Użycie: import <miasto>. Dostępne: {string.Join(", ", CityImports.All.Keys)}");
    return 1;
}

var root = FindRepoRoot();
var output = Path.Combine(root, "src", "Web.Client", "wwwroot", "data", cityId, "places.json");
var overrides = Path.Combine(root, "src", "Tools", "overrides", $"{cityId}.json");

var places = await new OsmOverpassSource().GetPlacesAsync(cityId, city);
Console.WriteLine($"OSM: {places.Count} miejsc");

places = OverridesApplier.Apply(places, overrides);

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, System.Text.Json.JsonSerializer.Serialize(places, Domain.DomainJson.Options));
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
