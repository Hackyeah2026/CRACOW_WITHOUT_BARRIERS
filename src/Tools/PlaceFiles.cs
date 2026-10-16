using System.Text.Json;
using Domain;
using Domain.Places;

namespace Tools;

/// <summary>Katalog miejsc miasta na dysku: jeden plik na kategorię i spis plików (index.json).</summary>
public static class PlaceFiles
{
    private const string IndexFile = "index.json";

    public static List<Place> Read(string directory)
    {
        var index = JsonSerializer.Deserialize<PlaceIndex>(File.ReadAllText(Path.Combine(directory, IndexFile)), DomainJson.Options)
                    ?? throw new InvalidOperationException($"Pusty spis katalogu w {directory}.");
        return index.Categories
            .SelectMany(c => JsonSerializer.Deserialize<List<Place>>(File.ReadAllText(Path.Combine(directory, $"{c.Category}.json")), DomainJson.Options) ?? [])
            .ToList();
    }

    // Jeden plik na kategorię: aplikacja pobiera tylko kategorie potrzebne w danym widoku.
    public static void Write(string directory, IReadOnlyList<Place> places)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        Directory.CreateDirectory(directory);

        var groups = places.GroupBy(p => p.Category).OrderBy(g => g.Key).ToList();
        foreach (var group in groups)
            File.WriteAllText(Path.Combine(directory, $"{group.Key}.json"), JsonSerializer.Serialize(group.ToList(), DomainJson.Options));

        var index = new PlaceIndex(DateOnly.FromDateTime(DateTime.UtcNow), groups.Select(g => new PlaceCategoryCount(g.Key, g.Count())).ToList());
        File.WriteAllText(Path.Combine(directory, IndexFile), JsonSerializer.Serialize(index, DomainJson.Indented));

        Console.WriteLine($"Zapisano {places.Count} miejsc do {directory}");
        foreach (var group in groups.OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {group.Key}: {group.Count()}, z cechami: {group.Count(p => p.Features.Count > 0)}, " +
                              $"plik {new FileInfo(Path.Combine(directory, $"{group.Key}.json")).Length / 1024} KB");
    }
}
