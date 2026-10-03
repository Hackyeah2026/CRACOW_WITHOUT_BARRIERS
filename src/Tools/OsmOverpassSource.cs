using System.Globalization;
using System.Text.Json;
using Domain.Places;

namespace Tools;

/// <summary>Import miejsc z OpenStreetMap przez Overpass API. Dane na licencji ODbL.</summary>
public sealed class OsmOverpassSource
{
    // Publiczne instancje bywają niedostępne, więc próbujemy po kolei.
    private static readonly string[] Endpoints =
    [
        "https://overpass.openstreetmap.fr/api/interpreter",
        "https://overpass-api.de/api/interpreter",
        "https://overpass.private.coffee/api/interpreter"
    ];
    private const string Source = "OpenStreetMap";

    public async Task<List<Place>> GetPlacesAsync(string cityId, CityImport city)
    {
        var bbox = string.Create(CultureInfo.InvariantCulture, $"{city.South},{city.West},{city.North},{city.East}");
        var query = $"""
            [out:json][timeout:90];
            (
              nwr["tourism"~"^(attraction|museum|gallery)$"]["name"]({bbox});
              nwr["amenity"~"^(townhall|post_office|library|clinic|doctors|hospital|pharmacy|theatre|cinema|arts_centre|community_centre)$"]["name"]({bbox});
              nwr["office"="government"]["name"]({bbox});
              nwr["amenity"="toilets"]({bbox});
              nwr["amenity"~"^(restaurant|cafe)$"]["wheelchair"]["name"]({bbox});
              node["railway"="tram_stop"]["name"]({bbox});
              node["highway"="bus_stop"]["name"]({bbox});
            );
            out center meta;
            """;

        using var doc = JsonDocument.Parse(await DownloadAsync(query));
        var places = new List<Place>();
        var seenStops = new HashSet<string>();

        foreach (var element in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (!element.TryGetProperty("tags", out var tagsElement))
                continue;
            var tags = tagsElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            if (Category(tags) is not { } category)
                continue;

            var position = element.TryGetProperty("center", out var center) ? center : element;
            if (!position.TryGetProperty("lat", out var lat))
                continue;

            var name = tags.GetValueOrDefault("name") ?? (category == PlaceCategory.Toilet ? "Toaleta publiczna" : null);
            if (name is null || (category == PlaceCategory.Stop && !seenStops.Add(name)))
                continue;

            DateOnly? edited = element.TryGetProperty("timestamp", out var ts) && ts.TryGetDateTimeOffset(out var dt)
                ? DateOnly.FromDateTime(dt.UtcDateTime) : null;

            places.Add(new Place(
                $"osm-{element.GetProperty("type").GetString()}-{element.GetProperty("id").GetInt64()}",
                cityId, name, category, lat.GetDouble(), position.GetProperty("lon").GetDouble(),
                Address(tags), Description(tags), Features(tags, category, edited)));
        }

        return places.OrderBy(p => p.Category).ThenBy(p => p.Name, StringComparer.Create(new CultureInfo("pl-PL"), false)).ToList();
    }

    private static async Task<string> DownloadAsync(string query)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KrakowBezBarier-Import/0.1 (HackYeah 2026)");

        foreach (var endpoint in Endpoints)
        {
            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query });
                using var response = await http.PostAsync(endpoint, content);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Console.Error.WriteLine($"Overpass niedostępny ({endpoint}): {ex.Message}");
            }
        }

        throw new InvalidOperationException("Żadna instancja Overpass API nie odpowiedziała.");
    }

    private static PlaceCategory? Category(Dictionary<string, string> tags)
    {
        var amenity = tags.GetValueOrDefault("amenity");
        var tourism = tags.GetValueOrDefault("tourism");
        return (amenity, tourism) switch
        {
            ("toilets", _) => PlaceCategory.Toilet,
            ("townhall" or "post_office", _) => PlaceCategory.Office,
            ("library", _) => PlaceCategory.Library,
            ("clinic" or "doctors" or "hospital" or "pharmacy", _) => PlaceCategory.Clinic,
            ("theatre" or "cinema" or "arts_centre" or "community_centre", _) => PlaceCategory.Culture,
            ("restaurant" or "cafe", _) => PlaceCategory.Food,
            (_, "museum" or "gallery") => PlaceCategory.Museum,
            (_, "attraction") => PlaceCategory.Attraction,
            _ when tags.GetValueOrDefault("office") == "government" => PlaceCategory.Office,
            _ when tags.GetValueOrDefault("railway") == "tram_stop" || tags.GetValueOrDefault("highway") == "bus_stop" => PlaceCategory.Stop,
            _ => null
        };
    }

    private static string? Address(Dictionary<string, string> tags) =>
        tags.TryGetValue("addr:street", out var street)
            ? $"{street} {tags.GetValueOrDefault("addr:housenumber")}".Trim()
            : null;

    private static string? Description(Dictionary<string, string> tags) =>
        tags.GetValueOrDefault("wheelchair:description:pl")
        ?? tags.GetValueOrDefault("wheelchair:description")
        ?? tags.GetValueOrDefault("description:pl")
        ?? tags.GetValueOrDefault("description");

    private static List<AccessibilityFeature> Features(Dictionary<string, string> tags, PlaceCategory category, DateOnly? edited)
    {
        var features = new List<AccessibilityFeature>();
        void Add(FeatureKey key, FeatureState state) => features.Add(new(key, state, null, Source, edited, false));
        void AddYesNo(string tag, FeatureKey key)
        {
            switch (tags.GetValueOrDefault(tag))
            {
                case "yes" or "designated": Add(key, FeatureState.Yes); break;
                case "no": Add(key, FeatureState.No); break;
            }
        }

        switch (tags.GetValueOrDefault("wheelchair"))
        {
            case "yes" or "designated": Add(FeatureKey.WheelchairAccess, FeatureState.Yes); break;
            case "no": Add(FeatureKey.WheelchairAccess, FeatureState.No); break;
            case "limited": Add(FeatureKey.WheelchairLimited, FeatureState.Yes); break;
        }

        // Dla toalety publicznej tag "wheelchair" opisuje samą toaletę.
        AddYesNo(category == PlaceCategory.Toilet ? "wheelchair" : "toilets:wheelchair", FeatureKey.AccessibleToilet);
        AddYesNo("tactile_paving", FeatureKey.TactilePaving);
        AddYesNo("bench", FeatureKey.Benches);
        AddYesNo("shelter", FeatureKey.StopShelter);
        return features;
    }
}
