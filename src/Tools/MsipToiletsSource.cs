using System.Text.Json;
using Domain.Places;

namespace Tools;

/// <summary>Toaleta publiczna z rejestru miasta (ISDP) z polami opisującymi dostępność.</summary>
public sealed record CityToilet(
    int Id, double Lat, double Lon, string Location, string? Kind, string? Hours, string? Days, bool Seasonal,
    string? Accessible, string? AccessType, bool Open, bool ChangingTable, string? Notes);

/// <summary>
/// Toalety publiczne w Krakowie z MSIP (warstwa WT_WC_2023, dane z ISDP). Łączymy je z toaletami z OpenStreetMap:
/// dane miasta trafiają przed cechy z OSM, a cechy z OSM zostają obok, więc karta miejsca pokazuje oba źródła,
/// także gdy się różnią.
/// </summary>
public sealed class MsipToiletsSource(HttpClient http)
{
    public const string Source = "MSIP Kraków, toalety publiczne (ISDP), stan 2023";

    /// <summary>Toaleta z OSM bliżej niż tyle metrów to ten sam obiekt.</summary>
    public const double MatchRadiusM = 50;

    public const string IdPrefix = "msip-wc-";

    public async Task<List<CityToilet>> LoadAsync(string layerUrl, CancellationToken ct = default)
    {
        var url = $"{layerUrl}/query?where=1%3D1&outFields=*&outSR=4326&f=geojson";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"MSIP zwrócił błąd dla {layerUrl}: {error}");

        var toilets = Parse(doc.RootElement.GetProperty("features")).ToList();
        if (toilets.Count == 0)
            throw new InvalidOperationException("Warstwa toalet publicznych nie zwróciła żadnego obiektu.");
        Console.WriteLine($"MSIP: {toilets.Count} toalet publicznych z {layerUrl}");
        return toilets;
    }

    public static IEnumerable<CityToilet> Parse(JsonElement features)
    {
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object)
                continue;

            var p = feature.GetProperty("properties");
            var point = geometry.GetProperty("coordinates");
            yield return new CityToilet(
                p.GetProperty("ESRI_OID").GetInt32(), point[1].GetDouble(), point[0].GetDouble(),
                Text(p, "miejsce") ?? "bez opisu lokalizacji", Text(p, "rodz_ob"), Text(p, "godziny"), Text(p, "dni"),
                IsYes(Text(p, "sezon")), Text(p, "nplnsprw"), Text(p, "rodz_npl"),
                Text(p, "status")?.ToLowerInvariant() != "nie", IsYes(Text(p, "przewijak")), Text(p, "uwagi"));
        }
    }

    /// <summary>
    /// Cechy z pól rejestru. Platforma i winda prowadzą do toalet podziemnych, więc oznaczają schody z windą;
    /// schodołaz wymaga obsługi i nie pasuje do wielu wózków, więc oznacza schody bez windy.
    /// </summary>
    public static List<AccessibilityFeature> Features(CityToilet toilet)
    {
        var features = new List<AccessibilityFeature>();
        void Add(FeatureKey key, FeatureState state) => features.Add(new AccessibilityFeature(key, state, null, Source, null, false));

        var accessible = toilet.Accessible?.Trim().ToLowerInvariant();
        if (accessible?.StartsWith("tak") == true)
            Add(FeatureKey.AccessibleToilet, FeatureState.Yes);
        else if (accessible == "nie")
            Add(FeatureKey.AccessibleToilet, FeatureState.No);

        switch (toilet.AccessType?.Trim().ToLowerInvariant())
        {
            case "wjazd z poziomu 0" or "pochylnia":
                Add(FeatureKey.WheelchairAccess, FeatureState.Yes);
                Add(FeatureKey.StepFreeEntrance, FeatureState.Yes);
                break;
            case "platforma" or "winda":
                Add(FeatureKey.WheelchairAccess, FeatureState.Yes);
                Add(FeatureKey.Stairs, FeatureState.Yes);
                Add(FeatureKey.Elevator, FeatureState.Yes);
                break;
            case "schodołaz":
                Add(FeatureKey.WheelchairLimited, FeatureState.Yes);
                Add(FeatureKey.Stairs, FeatureState.Yes);
                Add(FeatureKey.Elevator, FeatureState.No);
                break;
        }

        return features;
    }

    public static string Describe(CityToilet toilet)
    {
        var parts = new List<string> { $"Toaleta miejska{(toilet.Kind is { } kind ? $" ({kind})" : "")}." };
        if (!toilet.Open)
            parts.Add($"W danych miasta z 2023 r. oznaczona jako nieczynna{(toilet.Notes is { } why ? $": {why}" : "")}.");
        else if (toilet.Notes is { } notes)
            parts.Add($"Uwagi: {notes}.");
        if (toilet.Hours is not null || toilet.Days is not null)
            parts.Add($"Otwarta: {string.Join(", ", new[] { toilet.Days, toilet.Hours }.OfType<string>())}{(toilet.Seasonal ? ", godziny zależą od sezonu" : "")}.");
        if (toilet.Accessible is { } accessible)
            parts.Add($"Dla osób z niepełnosprawnościami: {accessible}{(toilet.AccessType is { } how ? $", {how}" : "")}.");
        parts.Add(toilet.ChangingTable ? "Jest przewijak." : "Bez przewijaka.");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Wynik nie zależy od tego, czy dane miasta były już dodane: wcześniejsze cechy z tego źródła i dodane wcześniej
    /// toalety są najpierw usuwane.
    /// </summary>
    public static List<Place> Apply(IEnumerable<Place> places, IReadOnlyList<CityToilet> toilets, string cityId)
    {
        var result = places.Where(p => !p.Id.StartsWith(IdPrefix))
            .Select(p => p with { Features = p.Features.Where(f => f.Source != Source).ToList() })
            .ToList();

        var osmToilets = result.Select((place, index) => (place, index)).Where(x => x.place.Category == PlaceCategory.Toilet).ToList();
        var taken = new HashSet<int>();
        foreach (var toilet in toilets)
        {
            var location = new GeoPoint(toilet.Lat, toilet.Lon);
            var match = osmToilets.Where(x => !taken.Contains(x.index))
                .Select(x => (x.index, distance: x.place.Location.DistanceTo(location)))
                .Where(x => x.distance <= MatchRadiusM)
                .OrderBy(x => x.distance)
                .Select(x => (int?)x.index)
                .FirstOrDefault();

            var features = Features(toilet);
            if (match is { } index)
            {
                taken.Add(index);
                var osm = result[index];
                result[index] = osm with
                {
                    Name = "Toaleta publiczna",
                    Address = toilet.Location,
                    Description = Describe(toilet),
                    Features = [.. features, .. osm.Features]
                };
            }
            else
                result.Add(new Place($"{IdPrefix}{toilet.Id}", cityId, "Toaleta publiczna", PlaceCategory.Toilet,
                    toilet.Lat, toilet.Lon, toilet.Location, Describe(toilet), features));
        }

        return result;
    }

    private static string? Text(JsonElement properties, string name) =>
        properties.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString()!.Trim() is { Length: > 0 } text && text != "-" ? text : null;

    private static bool IsYes(string? value) => value?.ToLowerInvariant() == "tak";
}
