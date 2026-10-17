using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Domain.Hazards;

namespace Domain.Places;

/// <summary>
/// Eksport raportu "Luki w danych i bariery" do plików dla systemów miasta. CSV: separator średnik, przecinek dziesiętny
/// (tak otwiera je polski Excel), UTF-8. GeoJSON: dzielnice z wskaźnikami, miejsca priorytetowe i potwierdzone bariery (WGS 84).
/// </summary>
public static class CityGapsExport
{
    // Sam format liczb, bez danych kultury: przeglądarka (WebAssembly) nie ma ich wbudowanych.
    private static readonly NumberFormatInfo Pl = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "" };

    private static readonly string[] StatsHeader =
        ["miejsca", "z_wejsciem_dla_wozkow", "proc_z_wejsciem", "bez_danych_o_dostepnosci", "proc_bez_danych",
         "z_danymi_o_toalecie", "proc_z_toaleta", "z_halasem_otoczenia", "proc_z_halasem_otoczenia", "z_halasem_w_miejscu"];

    public static string DistrictsCsv(CityGapsReport report)
    {
        var csv = Header(["nr", "dzielnica", .. StatsHeader, "potwierdzone_bariery"]);
        foreach (var d in report.Districts)
            Row(csv, [d.Roman, d.Name, .. Stats(d.Stats), d.Hazards.ToString(Pl)]);
        return csv.ToString();
    }

    public static string GroupsCsv(CityGapsReport report)
    {
        var csv = Header(["kategoria", .. StatsHeader]);
        Row(csv, ["Razem", .. Stats(report.Total)]);
        foreach (var g in report.Groups)
            Row(csv, [g.Group, .. Stats(g.Stats)]);
        return csv.ToString();
    }

    public static string PrioritiesCsv(CityGapsReport report)
    {
        var csv = Header(["pozycja", "id", "nazwa", "kategoria", "adres", "dzielnica", "szer_geogr", "dl_geogr", "odjazdy_w_300_m", "waga", "wynik"]);
        var rank = 1;
        foreach (var p in report.Priorities)
            Row(csv, [(rank++).ToString(Pl), p.Place.Id, p.Place.Name, p.Place.Category.ToString(), p.Place.Address ?? "", p.District ?? "",
                p.Place.Lat.ToString("0.000000", Pl), p.Place.Lon.ToString("0.000000", Pl), p.DeparturesNearby.ToString(Pl),
                p.Weight.ToString("0.#", Pl), p.Score.ToString("0.#", Pl)]);
        return csv.ToString();
    }

    public static string GeoJson(CityGapsReport report, DistrictMap? districts, IReadOnlyList<VerifiedHazard> hazards)
    {
        var features = new JsonArray();
        foreach (var d in report.Districts)
        {
            var district = districts?.Districts.FirstOrDefault(x => x.Number == d.Number);
            if (district is null)
                continue;
            features.Add(Feature(Polygon(district), StatsProperties(d.Stats, new JsonObject
            {
                ["rodzaj"] = "dzielnica", ["nr"] = d.Roman, ["nazwa"] = d.Name, ["potwierdzone_bariery"] = d.Hazards
            })));
        }

        var rank = 1;
        foreach (var p in report.Priorities)
            features.Add(Feature(Point(p.Place.Lat, p.Place.Lon), new JsonObject
            {
                ["rodzaj"] = "priorytet", ["pozycja"] = rank++, ["id"] = p.Place.Id, ["nazwa"] = p.Place.Name,
                ["kategoria"] = p.Place.Category.ToString(), ["adres"] = p.Place.Address, ["dzielnica"] = p.District,
                ["odjazdy_w_300_m"] = p.DeparturesNearby, ["waga"] = p.Weight, ["wynik"] = p.Score
            }));

        foreach (var h in hazards)
            features.Add(Feature(Point(h.Lat, h.Lon), new JsonObject
            {
                ["rodzaj"] = "bariera", ["typ"] = h.Kind.ToString(), ["opis"] = h.Description,
                ["potwierdzona"] = h.VerifiedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            }));

        var collection = new JsonObject
        {
            ["type"] = "FeatureCollection",
            ["metadata"] = new JsonObject
            {
                ["raport"] = "Luki w danych i bariery", ["regula_rankingu"] = CityGaps.RankingRule,
                ["dzien_rozkladu"] = report.TrafficDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            },
            ["features"] = features
        };
        return collection.ToJsonString(new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static IEnumerable<string> Stats(CoverageStats s) =>
    [
        s.Places.ToString(Pl), s.WithWheelchair.ToString(Pl), Percent(s, s.WithWheelchair), s.WithoutAny.ToString(Pl), Percent(s, s.WithoutAny),
        s.WithToilet.ToString(Pl), Percent(s, s.WithToilet), s.WithAmbientNoise.ToString(Pl), Percent(s, s.WithAmbientNoise), s.WithOnSiteNoise.ToString(Pl)
    ];

    private static JsonObject StatsProperties(CoverageStats s, JsonObject properties)
    {
        properties["miejsca"] = s.Places;
        properties["z_wejsciem_dla_wozkow"] = s.WithWheelchair;
        properties["proc_z_wejsciem"] = Math.Round(s.Share(s.WithWheelchair) * 100, 1);
        properties["bez_danych_o_dostepnosci"] = s.WithoutAny;
        properties["proc_bez_danych"] = Math.Round(s.Share(s.WithoutAny) * 100, 1);
        properties["z_danymi_o_toalecie"] = s.WithToilet;
        properties["z_halasem_otoczenia"] = s.WithAmbientNoise;
        properties["z_halasem_w_miejscu"] = s.WithOnSiteNoise;
        return properties;
    }

    private static string Percent(CoverageStats s, int count) => (s.Share(count) * 100).ToString("0.0", Pl);

    private static StringBuilder Header(IEnumerable<string> columns)
    {
        var csv = new StringBuilder();
        Row(csv, columns);
        return csv;
    }

    private static void Row(StringBuilder csv, IEnumerable<string> cells) =>
        csv.Append(string.Join(';', cells.Select(Escape))).Append("\r\n");

    private static string Escape(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static JsonObject Feature(JsonObject geometry, JsonObject properties) =>
        new() { ["type"] = "Feature", ["geometry"] = geometry, ["properties"] = properties };

    private static JsonObject Point(double lat, double lon) =>
        new() { ["type"] = "Point", ["coordinates"] = new JsonArray(Math.Round(lon, 6), Math.Round(lat, 6)) };

    /// <summary>Każdy pierścień jako osobny wielokąt MultiPolygonu. W danych MSIP każda dzielnica to jeden pierścień bez dziur.</summary>
    private static JsonObject Polygon(District district)
    {
        var polygons = new JsonArray();
        foreach (var ring in district.Rings)
        {
            var points = new JsonArray();
            for (var i = 0; i + 1 < ring.Length; i += 2)
                points.Add(new JsonArray(ring[i + 1], ring[i]));
            polygons.Add(new JsonArray(points));
        }
        return new JsonObject { ["type"] = "MultiPolygon", ["coordinates"] = polygons };
    }
}
