using Domain.Places;

namespace Tools;

/// <summary>Uzupełnienie katalogu o dane miejskie (MSIP). Każde źródło jest opcjonalne i zależy od konfiguracji miasta.</summary>
public static class CityData
{
    public static async Task<List<Place>> ApplyAsync(List<Place> places, string cityId, CityImport city, HttpClient http)
    {
        // Najpierw toalety: nowe obiekty z rejestru miasta dostają potem hałas otoczenia jak pozostałe miejsca.
        if (city.PublicToiletsLayer is { } toiletsLayer)
        {
            var toilets = await new MsipToiletsSource(http).LoadAsync(toiletsLayer);
            places = MsipToiletsSource.Apply(places, toilets, cityId);
            var added = places.Count(p => p.Id.StartsWith(MsipToiletsSource.IdPrefix));
            Console.WriteLine($"Toalety publiczne: {toilets.Count - added} połączone z OSM, {added} nowych");
        }

        if (city.StopSheltersLayer is { } sheltersLayer)
        {
            var shelters = await new ZtpSheltersSource(http).LoadAsync(sheltersLayer);
            places = ZtpSheltersSource.Apply(places, shelters, out var unmatched);
            var withShelter = places.Count(p => p.Features.Any(f => f.Source == ZtpSheltersSource.Source));
            var conflicts = places.Count(p => p.Category == PlaceCategory.Stop && p.ConflictsOn(FeatureKey.StopShelter).Count > 0);
            Console.WriteLine($"Wiaty: {withShelter} przystanków z wiatą z rejestru ZTP, {unmatched} wiat bez przystanku w katalogu, " +
                              $"{conflicts} przystanków, przy których OSM podaje inaczej");
        }

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
