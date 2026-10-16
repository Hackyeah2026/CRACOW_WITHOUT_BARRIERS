using System.Text.Json;
using Domain.Places;

namespace Tools;

/// <summary>
/// Hałas otoczenia z mapy hałasu Krakowa 2022 z Miejskiego Systemu Informacji Przestrzennej (ArcGIS REST).
/// Dane Gminy Miejskiej Kraków do swobodnego ponownego wykorzystania z podaniem źródła i czasu wytworzenia.
/// </summary>
public sealed class MsipNoiseSource(HttpClient http)
{
    public const string Source = "MSIP Kraków, mapa hałasu 2022 (LDWN, ruch drogowy i szynowy)";

    private const int PageSize = 1000;

    public async Task<NoiseMap> LoadAsync(IReadOnlyList<string> layerUrls, CancellationToken ct = default)
    {
        var areas = new List<NoiseArea>();
        foreach (var layer in layerUrls)
        {
            var before = areas.Count;
            for (var offset = 0; ; offset += PageSize)
            {
                // Uproszczenie geometrii do ok. 2 m: przedziały mapy i tak mają rozdzielczość kilku metrów.
                var url = $"{layer}/query?where=1%3D1&outFields=ISOV1&outSR=4326&geometryPrecision=6&maxAllowableOffset=0.00002" +
                          $"&orderByFields=OBJECTID&resultOffset={offset}&resultRecordCount={PageSize}&f=geojson";
                using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
                if (doc.RootElement.TryGetProperty("error", out var error))
                    throw new InvalidOperationException($"MSIP zwrócił błąd dla {layer}: {error}");

                var features = doc.RootElement.GetProperty("features");
                areas.AddRange(ParseAreas(features));
                if (features.GetArrayLength() < PageSize)
                    break;
            }
            Console.WriteLine($"MSIP: {areas.Count - before} obszarów z {layer}");
        }

        if (areas.Count == 0)
            throw new InvalidOperationException("Mapa hałasu nie zwróciła żadnego obszaru.");
        return new NoiseMap(areas);
    }

    /// <summary>Obiekty GeoJSON z polem ISOV1 (dolna granica przedziału).</summary>
    public static IEnumerable<NoiseArea> ParseAreas(JsonElement features)
    {
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("properties", out var properties) || !properties.TryGetProperty("ISOV1", out var isov1) ||
                isov1.ValueKind != JsonValueKind.Number ||
                !feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object)
                continue;

            var fromDb = BandFrom(isov1.GetDouble());
            var coordinates = geometry.GetProperty("coordinates");
            var polygons = geometry.GetProperty("type").GetString() switch
            {
                "Polygon" => new List<JsonElement> { coordinates },
                "MultiPolygon" => coordinates.EnumerateArray().ToList(),
                _ => new List<JsonElement>()
            };

            var rings = polygons.SelectMany(p => p.EnumerateArray())
                .Select(ring => ring.EnumerateArray().SelectMany(point => new[] { point[0].GetDouble(), point[1].GetDouble() }).ToArray())
                .Where(ring => ring.Length >= 6)
                .ToList();
            if (rings.Count > 0)
                yield return new NoiseArea(fromDb, rings);
        }
    }

    /// <summary>
    /// Wspólne przedziały co 5 dB: hałas drogowy ma je w danych, szynowy ma przedziały co 1 dB (np. 56-57 to przedział 55-60).
    /// Najniższy przedział (ujemna granica w danych, czyli poniżej 55 dB) zapisujemy jako 0.
    /// </summary>
    public static double BandFrom(double isov1) => isov1 < AmbientNoise.ModerateFromDb ? 0 : Math.Floor(isov1 / 5) * 5;

    /// <summary>Siatka ok. 25 m na obszarze miasta: komórka dostaje przedział hałasu ze swojego środka.</summary>
    public static NoiseGrid BuildGrid(NoiseMap map, double south, double west, double north, double east, double cellM = 25)
    {
        var cellLat = cellM / 111_320.0;
        var cellLon = cellM / (111_320.0 * Math.Cos((south + north) / 2 * Math.PI / 180));
        var (rows, cols) = ((int)Math.Ceiling((north - south) / cellLat), (int)Math.Ceiling((east - west) / cellLon));
        var cells = new byte[rows * cols];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                cells[r * cols + c] = NoiseGrid.Encode(map.BandAt(south + (r + 0.5) * cellLat, west + (c + 0.5) * cellLon));
        return new NoiseGrid(south, west, cellLat, cellLon, rows, cols, Convert.ToBase64String(cells), Source);
    }

    /// <summary>Ławki i miejsca parkingowe to nie cele wizyt; pomijamy je, żeby nie powiększać największych plików katalogu.</summary>
    public static bool AppliesTo(Place place) => place.Category is not (PlaceCategory.Bench or PlaceCategory.DisabledParking);

    /// <summary>Zastępuje cechę hałasu otoczenia aktualnym odczytem z mapy, więc ponowne uruchomienie niczego nie dubluje.</summary>
    public static List<Place> Apply(IEnumerable<Place> places, NoiseMap map) => places.Select(place =>
    {
        var features = place.Features.Where(f => f.Key != FeatureKey.AmbientNoiseDb).ToList();
        if (AppliesTo(place) && map.BandAt(place.Lat, place.Lon) is { } band)
            features.Add(new AccessibilityFeature(FeatureKey.AmbientNoiseDb, FeatureState.Yes, band, Source, null, false));
        return place with { Features = features };
    }).ToList();
}
