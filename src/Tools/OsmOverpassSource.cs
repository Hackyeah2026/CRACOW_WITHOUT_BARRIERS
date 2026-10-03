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
        "https://overpass-api.de/api/interpreter",
        "https://overpass.private.coffee/api/interpreter",
        "https://overpass.openstreetmap.fr/api/interpreter"
    ];

    private const int Rounds = 4;
    private static readonly TimeSpan RoundDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxDataAge = TimeSpan.FromDays(2);

    /// <summary>
    /// Zapytania w osobnych grupach: każde mieści się w limicie czasu instancji, a awaria jednej grupy
    /// przerywa import, zamiast po cichu zostawić katalog bez części kategorii.
    /// </summary>
    private static readonly (string Name, string[] Filters)[] Groups =
    [
        ("miejsca",
        [
            """nwr["tourism"~"^(attraction|museum|gallery|viewpoint|zoo|theme_park|aquarium|hotel|hostel|guest_house|motel)$"]["name"]""",
            """nwr["historic"~"^(castle|monument|ruins|fort|city_gate|archaeological_site|manor|palace|tower|church|monastery)$"]["name"]""",
            """nwr["amenity"~"^(townhall|courthouse|police|post_office|bank|social_facility|library|clinic|doctors|hospital|dentist|pharmacy|theatre|cinema|arts_centre|community_centre|place_of_worship|restaurant|cafe|fast_food|bar|pub|ice_cream|food_court|biergarten|university|college|school)$"]["name"]""",
            """nwr["office"="government"]["name"]""",
            """nwr["healthcare"]["name"]""",
            """nwr["leisure"~"^(park|garden)$"]["name"]"""
        ]),
        ("sklepy", ["""nwr["shop"]["name"]"""]),
        ("toalety i przystanki",
        [
            """nwr["amenity"="toilets"]""",
            """node["railway"="tram_stop"]["name"]""",
            """node["highway"="bus_stop"]["name"]""",
            """nwr["public_transport"="platform"]["name"]"""
        ]),
        ("ławki i miejsca parkingowe",
        [
            """nwr["amenity"="bench"]""",
            """nwr["amenity"="parking_space"]["parking_space"="disabled"]""",
            """nwr["amenity"="parking"]["capacity:disabled"]"""
        ])
    ];

    public async Task<List<Place>> GetPlacesAsync(string cityId, CityImport city)
    {
        // Obszar: granica administracyjna miasta (identyfikator obszaru = 3600000000 + numer relacji) albo prostokąt.
        var area = city.OsmRelationId is { } relation ? $"area(id:{3_600_000_000 + relation})->.city;" : "";
        var within = city.OsmRelationId is not null
            ? "(area.city)"
            : string.Create(CultureInfo.InvariantCulture, $"({city.South},{city.West},{city.North},{city.East})");

        var places = new Dictionary<string, Place>();
        var stopsByName = new Dictionary<string, string>();
        var platforms = new List<(string Name, List<AccessibilityFeature> Features)>();

        foreach (var (groupName, filters) in Groups)
        {
            var query = $"""
                [out:json][timeout:900];
                {area}
                (
                {string.Join("\n", filters.Select(f => $"  {f}{within};"))}
                );
                out center meta;
                """;

            using var doc = JsonDocument.Parse(await DownloadAsync(query));
            var before = places.Count;
            foreach (var element in doc.RootElement.GetProperty("elements").EnumerateArray())
            {
                if (!element.TryGetProperty("tags", out var tagsElement))
                    continue;
                var tags = tagsElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
                if (OsmTags.Category(tags) is not { } category || OsmTags.Name(tags, category) is not { } name)
                    continue;

                var position = element.TryGetProperty("center", out var center) ? center : element;
                if (!position.TryGetProperty("lat", out var lat))
                    continue;

                DateOnly? edited = element.TryGetProperty("timestamp", out var ts) && ts.TryGetDateTimeOffset(out var dt)
                    ? DateOnly.FromDateTime(dt.UtcDateTime) : null;
                var features = OsmTags.Features(tags, category, OsmTags.CheckedOn(tags) ?? edited);

                // Peron nie jest osobnym miejscem: uzupełnia cechy przystanku o tej samej nazwie (nazwa zawiera numer słupka).
                if (tags.GetValueOrDefault("public_transport") == "platform" && !OsmTags.IsStopNode(tags))
                {
                    platforms.Add((name, features));
                    continue;
                }

                var id = $"osm-{element.GetProperty("type").GetString()}-{element.GetProperty("id").GetInt64()}";
                if (places.ContainsKey(id) || (category == PlaceCategory.Stop && !stopsByName.TryAdd(name, id)))
                    continue;

                places[id] = new Place(id, cityId, name, category, lat.GetDouble(), position.GetProperty("lon").GetDouble(),
                    OsmTags.Address(tags), OsmTags.Description(tags, category), features);
            }
            Console.WriteLine($"OSM, {groupName}: {places.Count - before} miejsc");
        }

        var enriched = 0;
        foreach (var (name, features) in platforms)
        {
            if (!stopsByName.TryGetValue(name, out var id))
                continue;
            var stop = places[id];
            var missing = features.Where(f => stop.Feature(f.Key) is null).ToList();
            if (missing.Count == 0)
                continue;
            places[id] = stop with { Features = [.. stop.Features, .. missing] };
            enriched++;
        }
        Console.WriteLine($"OSM, perony: cechy uzupełnione dla {enriched} przystanków");

        return places.Values
            .OrderBy(p => p.Category).ThenBy(p => p.Name, StringComparer.Create(new CultureInfo("pl-PL"), false)).ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<string> DownloadAsync(string query)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(240) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KrakowBezBarier-Import/0.2 (HackYeah 2026)");

        // Instancje publiczne odrzucają zapytania przy obciążeniu, więc po nieudanej rundzie czekamy i próbujemy ponownie.
        for (var round = 0; round < Rounds; round++)
        {
            if (round > 0)
                await Task.Delay(RoundDelay);

            foreach (var endpoint in Endpoints)
            {
                try
                {
                    using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query });
                    using var response = await http.PostAsync(endpoint, content);
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsStringAsync();

                    // Błąd wykonania zapytania (np. instancja bez bazy obszarów) przychodzi z kodem 200 i polem "remark".
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("remark", out var remark) && remark.GetString() is { } message
                        && message.Contains("error", StringComparison.OrdinalIgnoreCase))
                        throw new HttpRequestException(message);

                    // Instancje publiczne bywają opóźnione o tygodnie; stare dane po cichu gubiłyby nowe miejsca.
                    if (doc.RootElement.TryGetProperty("osm3s", out var meta) && meta.TryGetProperty("timestamp_osm_base", out var stamp)
                        && stamp.TryGetDateTimeOffset(out var dataFrom))
                    {
                        if (DateTimeOffset.UtcNow - dataFrom > MaxDataAge)
                            throw new HttpRequestException($"dane instancji są z {dataFrom:dd.MM.yyyy}, starsze niż {MaxDataAge.TotalDays:0} dni");
                        Console.WriteLine($"Overpass: {new Uri(endpoint).Host}, stan danych OSM {dataFrom:dd.MM.yyyy HH:mm} UTC");
                    }
                    return body;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    Console.Error.WriteLine($"Overpass niedostępny ({endpoint}): {ex.Message}");
                }
            }
        }

        throw new InvalidOperationException("Żadna instancja Overpass API nie odpowiedziała.");
    }
}

/// <summary>Zamiana tagów OpenStreetMap na kategorię, nazwę i cechy dostępności. Brak tagu to zawsze brak cechy, nigdy "dostępne".</summary>
public static class OsmTags
{
    private const string Source = "OpenStreetMap";

    public static PlaceCategory? Category(IReadOnlyDictionary<string, string> tags)
    {
        var amenity = tags.GetValueOrDefault("amenity");
        var tourism = tags.GetValueOrDefault("tourism");
        var healthcare = tags.GetValueOrDefault("healthcare");

        switch (amenity)
        {
            // Toaleta prywatna albo zamknięta nie jest miejscem, do którego można kogoś wysłać.
            case "toilets": return tags.GetValueOrDefault("access") is "private" or "no" ? null : PlaceCategory.Toilet;
            case "bench": return PlaceCategory.Bench;
            case "parking_space": return tags.GetValueOrDefault("parking_space") == "disabled" ? PlaceCategory.DisabledParking : null;
            case "parking": return DisabledCapacity(tags) is not null ? PlaceCategory.DisabledParking : null;
            case "pharmacy": return PlaceCategory.Pharmacy;
            case "clinic" or "doctors" or "hospital" or "dentist": return PlaceCategory.Clinic;
            case "townhall" or "courthouse" or "police": return PlaceCategory.Office;
            case "post_office" or "bank" or "social_facility": return PlaceCategory.Service;
            case "library": return PlaceCategory.Library;
            case "theatre" or "cinema" or "arts_centre" or "community_centre": return PlaceCategory.Culture;
            case "restaurant" or "cafe" or "fast_food" or "bar" or "pub" or "ice_cream" or "food_court" or "biergarten": return PlaceCategory.Food;
            case "university" or "college" or "school": return PlaceCategory.Education;
        }

        if (tags.GetValueOrDefault("office") == "government")
            return PlaceCategory.Office;
        if (healthcare is not null)
            return healthcare == "pharmacy" ? PlaceCategory.Pharmacy : PlaceCategory.Clinic;
        if (tourism is "museum" or "gallery")
            return PlaceCategory.Museum;
        // Ulica oznaczona jako atrakcja to w OSM kilka odcinków drogi, a nie miejsce z wejściem.
        // Kościół jest atrakcją tylko wtedy, gdy OSM oznacza go tak wprost; sam tag "historic" zostawia go miejscem kultu.
        if (amenity == "place_of_worship")
            return tourism == "attraction" ? PlaceCategory.Attraction : PlaceCategory.Worship;
        if ((tourism is "attraction" or "viewpoint" or "zoo" or "theme_park" or "aquarium" || tags.ContainsKey("historic"))
            && !tags.ContainsKey("highway"))
            return PlaceCategory.Attraction;
        if (tourism is "hotel" or "hostel" or "guest_house" or "motel")
            return PlaceCategory.Hotel;
        if (tags.GetValueOrDefault("leisure") is "park" or "garden")
            return PlaceCategory.Park;
        if (tags.ContainsKey("shop"))
            return PlaceCategory.Shop;
        if (IsStopNode(tags) || tags.GetValueOrDefault("public_transport") == "platform")
            return PlaceCategory.Stop;
        return null;
    }

    public static bool IsStopNode(IReadOnlyDictionary<string, string> tags) =>
        tags.GetValueOrDefault("railway") == "tram_stop" || tags.GetValueOrDefault("highway") == "bus_stop";

    /// <summary>Nazwa z OSM; punkty bez nazwy własnej (ławka, toaleta, miejsce parkingowe) dostają nazwę rodzaju. Null: miejsce pomijamy.</summary>
    public static string? Name(IReadOnlyDictionary<string, string> tags, PlaceCategory category)
    {
        if (tags.GetValueOrDefault("name") is { Length: > 0 } name)
            return name;

        return category switch
        {
            PlaceCategory.Toilet => tags.GetValueOrDefault("access") == "customers" ? "Toaleta dla klientów" : "Toaleta publiczna",
            PlaceCategory.Bench => "Ławka",
            PlaceCategory.DisabledParking => DisabledCapacity(tags) is { } spaces
                ? $"Parking z miejscami dla osób z niepełnosprawnościami ({spaces})"
                : "Miejsce parkingowe dla osób z niepełnosprawnościami",
            _ => null
        };
    }

    /// <summary>Liczba miejsc dla osób z niepełnosprawnościami na parkingu; "yes" bez liczby zapisujemy jako 1.</summary>
    private static int? DisabledCapacity(IReadOnlyDictionary<string, string> tags) => tags.GetValueOrDefault("capacity:disabled") switch
    {
        "yes" => 1,
        var value when int.TryParse(value, out var count) && count > 0 => count,
        _ => null
    };

    public static string? Address(IReadOnlyDictionary<string, string> tags)
    {
        var street = tags.GetValueOrDefault("addr:street") ?? tags.GetValueOrDefault("addr:place");
        return street is null ? null : $"{street} {tags.GetValueOrDefault("addr:housenumber")}".Trim();
    }

    public static string? Description(IReadOnlyDictionary<string, string> tags, PlaceCategory category)
    {
        var description = tags.GetValueOrDefault("wheelchair:description:pl")
            ?? tags.GetValueOrDefault("wheelchair:description")
            ?? tags.GetValueOrDefault("description:pl")
            ?? tags.GetValueOrDefault("description");
        if (category != PlaceCategory.Toilet)
            return description;

        var notes = new List<string>();
        if (tags.GetValueOrDefault("access") == "customers")
            notes.Add("Tylko dla klientów.");
        if (tags.GetValueOrDefault("fee") == "yes")
            notes.Add("Płatna.");
        if (description is not null)
            notes.Add(description);
        return notes.Count == 0 ? null : string.Join(" ", notes);
    }

    /// <summary>Data sprawdzenia w terenie, jeśli edytor OSM ją zapisał; w innym wypadku import używa daty ostatniej edycji obiektu.</summary>
    public static DateOnly? CheckedOn(IReadOnlyDictionary<string, string> tags)
    {
        foreach (var key in (string[])["check_date:wheelchair", "check_date", "survey:date"])
        {
            if (DateOnly.TryParseExact(tags.GetValueOrDefault(key), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
        }
        return null;
    }

    public static List<AccessibilityFeature> Features(IReadOnlyDictionary<string, string> tags, PlaceCategory category, DateOnly? checkedOn)
    {
        var features = new List<AccessibilityFeature>();
        void Add(FeatureKey key, FeatureState state)
        {
            if (features.All(f => f.Key != key))
                features.Add(new(key, state, null, Source, checkedOn, false));
        }
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
        AddYesNo("shelter", FeatureKey.StopShelter);
        AddYesNo("elevator", FeatureKey.Elevator);
        AddYesNo("hearing_loop", FeatureKey.InductionLoop);

        if (category == PlaceCategory.Bench)
            Add(FeatureKey.Benches, FeatureState.Yes);
        else
            AddYesNo("bench", FeatureKey.Benches);

        // "dog=no" dotyczy zwierząt domowych; pies asystujący ma wstęp z mocy prawa, więc z tego tagu nie wnioskujemy zakazu.
        if (tags.GetValueOrDefault("dog") == "yes")
            Add(FeatureKey.AssistanceDogAllowed, FeatureState.Yes);
        return features;
    }
}
