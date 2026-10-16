using Domain.Places;

namespace Tools;

/// <summary>Uzupełnienie katalogu o dane miejskie (MSIP). Każde źródło jest opcjonalne i zależy od konfiguracji miasta.</summary>
public static class CityData
{
    public static async Task<List<Place>> ApplyAsync(List<Place> places, CityImport city, HttpClient http)
    {
        if (city.NoiseLayers is { Count: > 0 } layers)
        {
            var map = await new MsipNoiseSource(http).LoadAsync(layers);
            places = MsipNoiseSource.Apply(places, map);
            Console.WriteLine($"Hałas otoczenia: {places.Count(p => p.Feature(FeatureKey.AmbientNoiseDb) is not null)} miejsc " +
                              $"z {places.Count(MsipNoiseSource.AppliesTo)} sprawdzanych");
        }

        return places;
    }
}
