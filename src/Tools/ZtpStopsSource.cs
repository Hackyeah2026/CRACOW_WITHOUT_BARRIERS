using System.Text.Json;
using Domain.Places;

namespace Tools;

/// <summary>Przystanek KMK z rejestru ZTP: nazwa ze słupkiem, położenie i czy jest na żądanie.</summary>
public sealed record RegistryStop(string Name, double Lat, double Lon, bool? OnRequest);

/// <summary>
/// Przystanki KMK z rejestru ZTP w MSIP Kraków. Bierzemy z nich informację "na żądanie": osoba niewidoma musi wtedy
/// dać znak kierowcy, choć może nie widzieć nadjeżdżającego pojazdu. Rejestr obejmuje wszystkie przystanki KMK,
/// więc "nie" też jest informacją.
/// </summary>
public sealed class ZtpStopsSource(HttpClient http)
{
    public const string Source = "ZTP Kraków (MSIP), rejestr przystanków KMK";

    public async Task<List<RegistryStop>> LoadAsync(string layerUrl, CancellationToken ct = default)
    {
        var stops = new List<RegistryStop>();
        for (var offset = 0; ; offset += 2000)
        {
            var url = $"{layerUrl}/query?where=1%3D1&outFields=*&outSR=4326&orderByFields=objectid&resultOffset={offset}&resultRecordCount=2000&f=geojson";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
            if (doc.RootElement.TryGetProperty("error", out var error))
                throw new InvalidOperationException($"MSIP zwrócił błąd dla {layerUrl}: {error}");
            var features = doc.RootElement.GetProperty("features");
            stops.AddRange(Parse(features));
            if (features.GetArrayLength() < 2000)
                break;
        }

        if (stops.Count == 0)
            throw new InvalidOperationException("Rejestr przystanków nie zwrócił żadnego obiektu.");
        Console.WriteLine($"MSIP: {stops.Count} przystanków KMK z {layerUrl}");
        return stops;
    }

    public static IEnumerable<RegistryStop> Parse(JsonElement features)
    {
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object)
                continue;
            var p = feature.GetProperty("properties");
            if (!p.TryGetProperty("nazwa_przystanku_nr", out var name) || name.ValueKind != JsonValueKind.String)
                continue;
            bool? onRequest = p.TryGetProperty("na_żądanie", out var flag) && flag.ValueKind == JsonValueKind.String
                ? flag.GetString()!.Trim().ToLowerInvariant() switch { "tak" => true, "nie" => false, _ => null }
                : null;
            var point = geometry.GetProperty("coordinates");
            yield return new RegistryStop(name.GetString()!, point[1].GetDouble(), point[0].GetDouble(), onRequest);
        }
    }

    /// <summary>Dopasowanie jak przy wiatach: po nazwie ze słupkiem, a bez zgodnej nazwy do najbliższego przystanku w 30 m.</summary>
    public static List<Place> Apply(IEnumerable<Place> places, IReadOnlyList<RegistryStop> stops, out int matched)
    {
        var result = places.Select(p => p with { Features = p.Features.Where(f => f.Source != Source).ToList() }).ToList();
        var catalogStops = result.Select((place, index) => (place, index)).Where(x => x.place.Category == PlaceCategory.Stop).ToList();
        var byName = catalogStops.GroupBy(x => ZtpSheltersSource.StopKey(x.place.Name)).ToDictionary(g => g.Key, g => g.Select(x => x.index).ToList());

        var flags = new Dictionary<int, bool>();
        foreach (var stop in stops.Where(s => s.OnRequest is not null))
        {
            var location = new GeoPoint(stop.Lat, stop.Lon);
            var named = byName.GetValueOrDefault(ZtpSheltersSource.StopKey(stop.Name));
            var match = (named ?? catalogStops.Select(x => x.index))
                .Select(index => (index, distance: result[index].Location.DistanceTo(location)))
                .Where(x => named is not null || x.distance <= ZtpSheltersSource.MatchRadiusM)
                .OrderBy(x => x.distance)
                .Select(x => (int?)x.index)
                .FirstOrDefault();
            if (match is { } index && !flags.ContainsKey(index))
                flags[index] = stop.OnRequest!.Value;
        }

        foreach (var (index, onRequest) in flags)
            result[index] = result[index] with
            {
                Features = [.. result[index].Features,
                    new AccessibilityFeature(FeatureKey.RequestStop, onRequest ? FeatureState.Yes : FeatureState.No, null, Source, null, false)]
            };
        matched = flags.Count;
        return result;
    }
}
