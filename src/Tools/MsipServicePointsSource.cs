using System.Text.Json;
using Domain.Places;

namespace Tools;

/// <summary>Punkt obsługi mieszkańców Urzędu Miasta: adres i link do godzin otwarcia w BIP.</summary>
public sealed record ServicePoint(int Id, string Address, string? HoursUrl, double Lat, double Lon);

/// <summary>
/// Dzienniki podawcze i punkty obsługi mieszkańców Urzędu Miasta Krakowa z MSIP. Trafiają do katalogu jako urzędy
/// (tryb "Załatwiam sprawę"); urząd z OSM w tym samym miejscu dostaje adres i opis z danych miasta. Rejestr nie mówi
/// nic o dostępności, więc nie dopisujemy cech: ocena zostaje taka, jak wynika z pozostałych źródeł.
/// </summary>
public sealed class MsipServicePointsSource(HttpClient http)
{
    public const string Source = "MSIP Kraków, punkty obsługi mieszkańców UMK";
    public const string IdPrefix = "msip-pom-";
    public const double MatchRadiusM = 40;
    private const string Name = "Punkt Obsługi Mieszkańców Urzędu Miasta Krakowa";

    public async Task<List<ServicePoint>> LoadAsync(string layerUrl, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"{layerUrl}/query?where=1%3D1&outFields=*&outSR=4326&f=geojson", ct));
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"MSIP zwrócił błąd dla {layerUrl}: {error}");
        var points = Parse(doc.RootElement.GetProperty("features")).ToList();
        Console.WriteLine($"MSIP: {points.Count} punktów obsługi mieszkańców z {layerUrl}");
        return points;
    }

    public static IEnumerable<ServicePoint> Parse(JsonElement features)
    {
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object)
                continue;
            var p = feature.GetProperty("properties");
            var point = geometry.GetProperty("coordinates");
            var hours = p.TryGetProperty("godziny_otwarcia", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
            yield return new ServicePoint(p.GetProperty("objectid").GetInt32(), p.GetProperty("adres").GetString()!.Trim(), hours,
                point[1].GetDouble(), point[0].GetDouble());
        }
    }

    public static string Describe(ServicePoint point) =>
        $"Punkt obsługi mieszkańców Urzędu Miasta Krakowa (źródło: {Source})." +
        (point.HoursUrl is { } url ? $" Godziny otwarcia: {url}" : "");

    /// <summary>Ponowne uruchomienie daje ten sam katalog: wcześniej dodane punkty są najpierw usuwane.</summary>
    public static List<Place> Apply(IEnumerable<Place> places, IReadOnlyList<ServicePoint> points, string cityId, out int matched)
    {
        var result = places.Where(p => !p.Id.StartsWith(IdPrefix)).ToList();
        var offices = result.Select((place, index) => (place, index)).Where(x => x.place.Category == PlaceCategory.Office).ToList();
        var taken = new HashSet<int>();
        matched = 0;
        foreach (var point in points)
        {
            var location = new GeoPoint(point.Lat, point.Lon);
            var match = offices.Where(x => !taken.Contains(x.index))
                .Select(x => (x.index, distance: x.place.Location.DistanceTo(location)))
                .Where(x => x.distance <= MatchRadiusM)
                .OrderBy(x => x.distance)
                .Select(x => (int?)x.index)
                .FirstOrDefault();
            if (match is { } index)
            {
                taken.Add(index);
                matched++;
                result[index] = result[index] with { Address = point.Address, Description = Describe(point) };
            }
            else
                result.Add(new Place($"{IdPrefix}{point.Id}", cityId, Name, PlaceCategory.Office, point.Lat, point.Lon,
                    point.Address, Describe(point), []));
        }
        return result;
    }
}
