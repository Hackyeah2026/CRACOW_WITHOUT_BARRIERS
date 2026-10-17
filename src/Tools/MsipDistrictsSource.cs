using System.Text.Json;
using Domain.Places;

namespace Tools;

/// <summary>Granice dzielnic administracyjnych Krakowa z MSIP, uproszczone do ok. 20 m (wystarcza do przypisania miejsc i kartogramu).</summary>
public sealed class MsipDistrictsSource(HttpClient http)
{
    public const string Source = "MSIP Kraków, dzielnice administracyjne";

    public async Task<DistrictMap> LoadAsync(string layerUrl, CancellationToken ct = default)
    {
        var url = $"{layerUrl}/query?where=1%3D1&outFields=id_dzielni,nr_dzielni,nazwa,data_aktua&outSR=4326&geometryPrecision=5" +
                  "&maxAllowableOffset=0.0002&f=geojson";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"MSIP zwrócił błąd dla {layerUrl}: {error}");

        var map = Parse(doc.RootElement.GetProperty("features"));
        if (map.Districts.Count == 0)
            throw new InvalidOperationException("Warstwa dzielnic nie zwróciła żadnej dzielnicy.");
        Console.WriteLine($"MSIP: {map.Districts.Count} dzielnic z {layerUrl}");
        return map;
    }

    public static DistrictMap Parse(JsonElement features)
    {
        var districts = new List<District>();
        DateOnly? dataDate = null;
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object)
                continue;
            var p = feature.GetProperty("properties");
            if (p.TryGetProperty("data_aktua", out var updated) && updated.ValueKind == JsonValueKind.Number)
            {
                var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(updated.GetInt64()).UtcDateTime);
                dataDate = dataDate is { } known && known > date ? known : date;
            }

            var coordinates = geometry.GetProperty("coordinates");
            var polygons = geometry.GetProperty("type").GetString() == "MultiPolygon" ? coordinates.EnumerateArray().ToList() : [coordinates];
            var rings = polygons.SelectMany(poly => poly.EnumerateArray())
                .Select(ring => ring.EnumerateArray().SelectMany(pt => new[] { pt[1].GetDouble(), pt[0].GetDouble() }).ToArray())
                .Where(ring => ring.Length >= 6)
                .ToList();
            districts.Add(new District(p.GetProperty("id_dzielni").GetInt32(), p.GetProperty("nr_dzielni").GetString()!.Trim(),
                p.GetProperty("nazwa").GetString()!.Trim(), rings));
        }
        return new DistrictMap(Source, dataDate, districts.OrderBy(d => d.Number).ToList());
    }
}
