using System.Text.Json;
using Domain.Hazards;
using Domain.Places;
using Domain.Transit;

namespace Tests;

public class CityGapsTests
{
    private static AccessibilityFeature F(FeatureKey key, double? value = null) => new(key, FeatureState.Yes, value, "test", null, false);

    private static Place P(string id, PlaceCategory category, double lat, double lon, string? name = null, params AccessibilityFeature[] features) =>
        new(id, "krakow", name ?? id, category, lat, lon, null, null, features);

    // Dwie dzielnice: zachodnia (I) i wschodnia (II), każda 0,01° x 0,01°.
    private static readonly DistrictMap Districts = new("test", null,
    [
        new District(1, "I", "Zachód", [[50.00, 19.90, 50.00, 19.91, 50.01, 19.91, 50.01, 19.90, 50.00, 19.90]]),
        new District(2, "II", "Wschód", [[50.00, 19.91, 50.00, 19.92, 50.01, 19.92, 50.01, 19.91, 50.00, 19.91]])
    ]);

    private static readonly Place[] Catalog =
    [
        P("office-known", PlaceCategory.Office, 50.005, 19.905, "Urząd A", F(FeatureKey.WheelchairAccess), F(FeatureKey.AmbientNoiseDb, 60)),
        P("office-unknown", PlaceCategory.Office, 50.005, 19.906, "Urząd B", F(FeatureKey.AmbientNoiseDb, 60)),
        P("dentist", PlaceCategory.Clinic, 50.005, 19.915, "Gabinet stomatologiczny"),
        P("nzoz", PlaceCategory.Clinic, 50.005, 19.915, "NZOZ Ortodoncja"),
        P("hospital", PlaceCategory.Clinic, 50.005, 19.915, "Szpital Miejski", F(FeatureKey.AccessibleToilet)),
        P("cafe", PlaceCategory.Food, 50.005, 19.915, "Kawiarnia", F(FeatureKey.NoiseLevel, 1)),
        P("bench", PlaceCategory.Bench, 50.005, 19.905),
        P("far", PlaceCategory.Museum, 50.2, 20.2, "Muzeum poza dzielnicami")
    ];

    /// <summary>Jeden przystanek przy urzędach (2 kursy w środę), drugi przy szpitalu (1 kurs); kurs w niedzielę się nie liczy.</summary>
    private static TransitNetwork Network() => new("test", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 3),
        [new("Przy urzędach", 50.0051, 19.9055), new("Przy szpitalu", 50.0051, 19.9151), new("Pętla", 50.3, 20.3)],
        [new TransitLine("1", TransitKind.Tram,
        [
            new TransitPattern("Pętla", [0, 2], [[0, 10]], [480, 0, 0, 540, 0, 0, 600, 0, 1]),
            new TransitPattern("Pętla", [1, 2], [[0, 10]], [480, 0, 0])
        ])],
        [new TransitService(0b0011111, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), [], []),
         new TransitService(0b1000000, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), [], [])]);

    [Fact]
    public void Totals_count_only_destinations_and_ignore_ambient_noise_when_judging_missing_data()
    {
        var report = CityGaps.Build(Catalog, Districts, null, []);

        Assert.Equal(7, report.Total.Places);
        Assert.Equal(1, report.Total.WithWheelchair);
        Assert.Equal(4, report.Total.WithoutAny);
        Assert.Equal(1, report.Total.WithToilet);
        Assert.Equal(2, report.Total.WithAmbientNoise);
        Assert.Equal(1, report.Total.WithOnSiteNoise);
        Assert.Equal(1, report.OutsideDistricts);
    }

    [Fact]
    public void Districts_and_groups_split_the_same_places_and_count_confirmed_barriers()
    {
        VerifiedHazard[] hazards = [new("h1", HazardKind.Stairs, 50.005, 19.915, "Schody", new DateOnly(2026, 10, 10))];

        var report = CityGaps.Build(Catalog, Districts, null, hazards);

        Assert.Equal([2, 4], report.Districts.Select(d => d.Stats.Places));
        Assert.Equal([0, 1], report.Districts.Select(d => d.Hazards));
        Assert.Equal(2, report.Groups.Single(g => g.Group == "Urzędy").Stats.Places);
        Assert.Equal(3, report.Groups.Single(g => g.Group == "Zdrowie").Stats.Places);
    }

    [Fact]
    public void Weekday_departures_skip_sunday_services_and_the_last_stop_of_a_trip()
    {
        var network = Network();

        var (departures, day) = CityGaps.DeparturesPerStop(network);

        Assert.Equal(new DateOnly(2026, 10, 7), day);
        Assert.Equal([2, 1, 0], departures);
    }

    [Fact]
    public void Priorities_take_public_places_without_wheelchair_info_and_rank_by_weighted_departures()
    {
        var report = CityGaps.Build(Catalog, Districts, Network(), []);

        // Urząd B: 2 odjazdy × 2; szpital: 1 odjazd × 2. Urząd A ma dane, gabinet i NZOZ to nie usługi publiczne, muzeum leży daleko.
        Assert.Equal(["office-unknown", "hospital", "far"], report.Priorities.Select(p => p.Place.Id));
        Assert.Equal([4d, 2d, 0d], report.Priorities.Select(p => p.Score));
        Assert.Equal("Zachód", report.Priorities[0].District);
        Assert.Equal(3, report.PriorityCandidates);
    }

    [Fact]
    public void District_test_handles_points_on_both_sides_and_outside()
    {
        Assert.Equal("Zachód", Districts.Find(new GeoPoint(50.005, 19.905))?.Name);
        Assert.Equal("Wschód", Districts.Find(new GeoPoint(50.005, 19.915))?.Name);
        Assert.Null(Districts.Find(new GeoPoint(50.5, 19.905)));
    }

    [Fact]
    public void Csv_uses_semicolons_decimal_commas_and_quotes_when_needed()
    {
        var report = CityGaps.Build(Catalog, Districts, Network(), []);

        var districts = CityGapsExport.DistrictsCsv(report).Split("\r\n");
        var priorities = CityGapsExport.PrioritiesCsv(report);

        Assert.StartsWith("nr;dzielnica;miejsca;", districts[0]);
        Assert.Equal("I;Zachód;2;1;50,0;1;50,0;0;0,0;2;100,0;0;0", districts[1]);
        Assert.Contains("1;office-unknown;Urząd B;Office;;Zachód;50,005000;19,906000;2;2;4", priorities);
    }

    [Fact]
    public void GeoJson_has_districts_priorities_and_barriers_with_lon_lat_order()
    {
        VerifiedHazard[] hazards = [new("h1", HazardKind.Stairs, 50.005, 19.915, "Schody", new DateOnly(2026, 10, 10))];
        var report = CityGaps.Build(Catalog, Districts, Network(), hazards);

        using var doc = JsonDocument.Parse(CityGapsExport.GeoJson(report, Districts, hazards));
        var features = doc.RootElement.GetProperty("features").EnumerateArray().ToList();

        Assert.Equal(2, features.Count(f => f.GetProperty("properties").GetProperty("rodzaj").GetString() == "dzielnica"));
        Assert.Equal(3, features.Count(f => f.GetProperty("properties").GetProperty("rodzaj").GetString() == "priorytet"));
        var barrier = features.Single(f => f.GetProperty("properties").GetProperty("rodzaj").GetString() == "bariera");
        Assert.Equal(19.915, barrier.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble());
        Assert.Contains("odjazdów", doc.RootElement.GetProperty("metadata").GetProperty("regula_rankingu").GetString());
    }
}
