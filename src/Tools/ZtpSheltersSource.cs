using System.Text.Json;
using System.Text.RegularExpressions;
using Domain.Places;

namespace Tools;

/// <summary>Wiata z rejestru ZTP: nazwa przystanku ze słupkiem (np. "Agatowa 01") i położenie.</summary>
public sealed record StopShelter(string StopName, double Lat, double Lon);

/// <summary>
/// Wiaty przystankowe KMK z rejestru Zarządu Transportu Publicznego, udostępnione w MSIP Kraków. Rejestr mówi, gdzie
/// wiata jest; nie mówi, gdzie jej nie ma (mogą być wiaty spoza rejestru), więc dopisujemy tylko "jest wiata".
/// Cecha z rejestru trafia przed cechę z OSM, a cecha z OSM zostaje obok, więc sprzeczność widać na karcie przystanku.
/// </summary>
public sealed partial class ZtpSheltersSource(HttpClient http)
{
    public const string Source = "ZTP Kraków (MSIP), rejestr wiat przystankowych";

    /// <summary>Wiata bez przystanku o tej samej nazwie należy do najbliższego przystanku w tej odległości.</summary>
    public const double MatchRadiusM = 30;

    public async Task<List<StopShelter>> LoadAsync(string layerUrl, CancellationToken ct = default)
    {
        var url = $"{layerUrl}/query?where=1%3D1&outFields=nazwa_przystanku_nr&outSR=4326&resultRecordCount=5000&f=geojson";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"MSIP zwrócił błąd dla {layerUrl}: {error}");

        var shelters = Parse(doc.RootElement.GetProperty("features")).ToList();
        if (shelters.Count == 0)
            throw new InvalidOperationException("Rejestr wiat nie zwrócił żadnego obiektu.");
        Console.WriteLine($"MSIP: {shelters.Count} wiat przystankowych z {layerUrl}");
        return shelters;
    }

    public static IEnumerable<StopShelter> Parse(JsonElement features)
    {
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object ||
                !feature.GetProperty("properties").TryGetProperty("nazwa_przystanku_nr", out var name) || name.ValueKind != JsonValueKind.String)
                continue;
            var point = geometry.GetProperty("coordinates");
            yield return new StopShelter(name.GetString()!, point[1].GetDouble(), point[0].GetDouble());
        }
    }

    /// <summary>"Cracovia Stadion 02-02" to druga wiata przy słupku 02: liczy się nazwa przystanku ze słupkiem.</summary>
    public static string StopKey(string name) => ExtraShelterSuffix().Replace(name.Trim(), "").ToLowerInvariant();

    /// <summary>Ponowne uruchomienie daje ten sam katalog: wcześniejsze cechy z rejestru są najpierw usuwane.</summary>
    public static List<Place> Apply(IEnumerable<Place> places, IReadOnlyList<StopShelter> shelters, out int unmatched)
    {
        var result = places.Select(p => p with { Features = p.Features.Where(f => f.Source != Source).ToList() }).ToList();
        var stops = result.Select((place, index) => (place, index)).Where(x => x.place.Category == PlaceCategory.Stop).ToList();
        var byName = stops.GroupBy(x => StopKey(x.place.Name)).ToDictionary(g => g.Key, g => g.Select(x => x.index).ToList());

        var sheltered = new HashSet<int>();
        unmatched = 0;
        foreach (var shelter in shelters)
        {
            var location = new GeoPoint(shelter.Lat, shelter.Lon);
            var candidates = byName.TryGetValue(StopKey(shelter.StopName), out var named) ? named : stops.Select(x => x.index);
            var match = candidates
                .Select(index => (index, distance: result[index].Location.DistanceTo(location)))
                .Where(x => named is not null || x.distance <= MatchRadiusM)
                .OrderBy(x => x.distance)
                .Select(x => (int?)x.index)
                .FirstOrDefault();

            if (match is { } index)
                sheltered.Add(index);
            else
                unmatched++;
        }

        foreach (var index in sheltered)
            result[index] = result[index] with
            {
                Features = [new AccessibilityFeature(FeatureKey.StopShelter, FeatureState.Yes, null, Source, null, false), .. result[index].Features]
            };
        return result;
    }

    [GeneratedRegex(@"-\d{2}$")]
    private static partial Regex ExtraShelterSuffix();
}
