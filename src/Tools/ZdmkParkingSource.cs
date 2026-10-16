using System.Text.Json;
using Domain.Places;

namespace Tools;

/// <summary>Stanowisko postojowe dla osób z niepełnosprawnościami z ewidencji ZDMK.</summary>
public sealed record ParkingSpace(string Id, string? Address, double Lat, double Lon);

/// <summary>Stanowiska stojące obok siebie (np. dwie koperty przy jednym budynku) jako jedno miejsce w katalogu.</summary>
public sealed record ParkingGroup(string Id, string? Address, double Lat, double Lon, int Spaces);

/// <summary>
/// Miejsca postojowe dla pojazdów osób z niepełnosprawnościami (koperty) z mapy Zarządu Dróg Miasta Krakowa
/// (ArcGIS Online Gminy Miejskiej Kraków). Warstwa jest publiczna, ale bez podanej licencji, więc w źródle piszemy to wprost.
/// Łączymy je z miejscami parkingowymi z OSM; koperty bez odpowiednika w OSM dodajemy do katalogu.
/// </summary>
public sealed class ZdmkParkingSource(HttpClient http)
{
    public const string Source = "ZDMK, mapa miejsc postojowych dla osób z niepełnosprawnościami (dane publiczne, licencja niepodana)";

    /// <summary>Stanowiska bliżej niż tyle metrów od siebie to jedno miejsce (np. kilka kopert przy jednym wejściu).</summary>
    public const double GroupRadiusM = 15;

    /// <summary>Miejsce parkingowe z OSM bliżej niż tyle metrów od grupy kopert to ten sam obiekt.</summary>
    public const double MatchRadiusM = 25;

    public const string IdPrefix = "zdmk-ozn-";

    public sealed record Snapshot(IReadOnlyList<ParkingSpace> Spaces, DateOnly? EditedOn);

    public async Task<Snapshot> LoadAsync(string layerUrl, CancellationToken ct = default)
    {
        // Data ostatniej zmiany danych w warstwie to "stan na" każdej koperty.
        using var info = JsonDocument.Parse(await http.GetStringAsync($"{layerUrl}?f=json", ct));
        DateOnly? editedOn = info.RootElement.TryGetProperty("editingInfo", out var editing) &&
                             editing.TryGetProperty("dataLastEditDate", out var edited) && edited.ValueKind == JsonValueKind.Number
            ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(edited.GetInt64()).UtcDateTime)
            : null;

        var spaces = new List<ParkingSpace>();
        for (var offset = 0; ; offset += 1000)
        {
            var url = $"{layerUrl}/query?where=1%3D1&outFields=ID_MIEJSCA,punkt_adresowy&outSR=4326&orderByFields=OBJECTID" +
                      $"&resultOffset={offset}&resultRecordCount=1000&f=geojson";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
            if (doc.RootElement.TryGetProperty("error", out var error))
                throw new InvalidOperationException($"ZDMK zwrócił błąd dla {layerUrl}: {error}");
            var features = doc.RootElement.GetProperty("features");
            spaces.AddRange(Parse(features));
            if (features.GetArrayLength() < 1000)
                break;
        }

        if (spaces.Count == 0)
            throw new InvalidOperationException("Mapa miejsc postojowych nie zwróciła żadnego stanowiska.");
        Console.WriteLine($"ZDMK: {spaces.Count} stanowisk postojowych, stan na {editedOn:dd.MM.yyyy}");
        return new Snapshot(spaces, editedOn);
    }

    public static IEnumerable<ParkingSpace> Parse(JsonElement features)
    {
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object)
                continue;
            var p = feature.GetProperty("properties");
            var point = geometry.GetProperty("coordinates");
            var address = p.TryGetProperty("punkt_adresowy", out var a) && a.ValueKind == JsonValueKind.String && a.GetString()!.Trim() is { Length: > 0 } text ? text : null;
            yield return new ParkingSpace(p.GetProperty("ID_MIEJSCA").GetString()!, address, point[1].GetDouble(), point[0].GetDouble());
        }
    }

    /// <summary>Zachłannie: każde stanowisko dołącza do pierwszej grupy, której pierwsze stanowisko jest w promieniu.</summary>
    public static List<ParkingGroup> Group(IEnumerable<ParkingSpace> spaces)
    {
        var groups = new List<(ParkingSpace First, List<ParkingSpace> Members)>();
        foreach (var space in spaces)
        {
            var location = new GeoPoint(space.Lat, space.Lon);
            var group = groups.FirstOrDefault(g => new GeoPoint(g.First.Lat, g.First.Lon).DistanceTo(location) <= GroupRadiusM);
            if (group.Members is null)
                groups.Add((space, [space]));
            else
                group.Members.Add(space);
        }

        return groups.Select(g => new ParkingGroup(
            g.First.Id, g.Members.Select(m => m.Address).FirstOrDefault(a => a is not null),
            g.Members.Average(m => m.Lat), g.Members.Average(m => m.Lon), g.Members.Count)).ToList();
    }

    /// <summary>Ponowne uruchomienie daje ten sam katalog: wcześniej dodane koperty i cechy z tego źródła są najpierw usuwane.</summary>
    public static List<Place> Apply(IEnumerable<Place> places, IReadOnlyList<ParkingGroup> groups, DateOnly? editedOn, string cityId, out int matched)
    {
        var result = places.Where(p => !p.Id.StartsWith(IdPrefix))
            .Select(p => p with { Features = p.Features.Where(f => f.Source != Source).ToList() })
            .ToList();
        var osm = result.Select((place, index) => (place, index)).Where(x => x.place.Category == PlaceCategory.DisabledParking).ToList();

        var taken = new HashSet<int>();
        matched = 0;
        foreach (var group in groups)
        {
            var location = new GeoPoint(group.Lat, group.Lon);
            var feature = new AccessibilityFeature(FeatureKey.DisabledParkingSpaces, FeatureState.Yes, group.Spaces, Source, editedOn, false);
            var match = osm.Where(x => !taken.Contains(x.index))
                .Select(x => (x.index, distance: x.place.Location.DistanceTo(location)))
                .Where(x => x.distance <= MatchRadiusM)
                .OrderBy(x => x.distance)
                .Select(x => (int?)x.index)
                .FirstOrDefault();

            if (match is { } index)
            {
                taken.Add(index);
                matched++;
                result[index] = result[index] with { Address = result[index].Address ?? group.Address, Features = [feature, .. result[index].Features] };
            }
            else
                result.Add(new Place($"{IdPrefix}{group.Id}", cityId, Name(group.Spaces), PlaceCategory.DisabledParking,
                    group.Lat, group.Lon, group.Address, null, [feature]));
        }

        return result;
    }

    private static string Name(int spaces) =>
        spaces == 1 ? "Miejsce postojowe dla osób z niepełnosprawnościami" : $"Miejsca postojowe dla osób z niepełnosprawnościami ({spaces})";
}
