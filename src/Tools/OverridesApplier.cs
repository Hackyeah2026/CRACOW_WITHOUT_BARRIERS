using System.Text.Json;
using Domain;
using Domain.Places;

namespace Tools;

public sealed record PlaceOverride(string Id, string? Note, List<FeatureOverride> Features);

public sealed record FeatureOverride(FeatureKey Key, FeatureState State, double? Value);

/// <summary>
/// Ręczne uzupełnienia cech dla miejsc demo. Zawsze oznaczane jako dane demonstracyjne,
/// żeby nie były przedstawiane jako zweryfikowane.
/// </summary>
public static class OverridesApplier
{
    private const string Source = "Dane demonstracyjne zespołu";

    public static List<Place> Apply(List<Place> places, string overridesPath)
    {
        if (!File.Exists(overridesPath))
            return places;

        var overrides = JsonSerializer.Deserialize<List<PlaceOverride>>(File.ReadAllText(overridesPath), DomainJson.Options) ?? [];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var item in overrides)
        {
            var index = places.FindIndex(p => p.Id == item.Id);
            if (index < 0)
            {
                Console.Error.WriteLine($"Uzupełnienie pominięte, brak miejsca: {item.Id} ({item.Note})");
                continue;
            }

            var keys = item.Features.Select(f => f.Key).ToHashSet();
            var features = places[index].Features.Where(f => !keys.Contains(f.Key))
                .Concat(item.Features.Select(f => new AccessibilityFeature(f.Key, f.State, f.Value, Source, today, true)))
                .ToList();
            places[index] = places[index] with { Features = features };
        }

        Console.WriteLine($"Uzupełnienia ręczne: {overrides.Count}");
        return places;
    }
}
