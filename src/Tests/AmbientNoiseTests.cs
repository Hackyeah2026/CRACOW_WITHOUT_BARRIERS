using System.Text.Json;
using Domain.Assessments;
using Domain.Needs;
using Domain.Places;
using Tools;

namespace Tests;

public class AmbientNoiseTests
{
    private static readonly NeedsProfile QuietOnly = new() { MaxNoiseLevel = 1 };
    private static readonly NeedsProfile ModerateNoise = new() { MaxNoiseLevel = 2 };

    private static Place PlaceAt(PlaceCategory category, params AccessibilityFeature[] features) =>
        new("p1", "krakow", "Miejsce testowe", category, 50.06, 19.94, null, null, features);

    private static AccessibilityFeature Ambient(double db) =>
        new(FeatureKey.AmbientNoiseDb, FeatureState.Yes, db, MsipNoiseSource.Source, null, false);

    // Kwadrat 0,01° x 0,01° z dziurą w środku.
    private static readonly double[] Outer = [19.93, 50.05, 19.94, 50.05, 19.94, 50.06, 19.93, 50.06, 19.93, 50.05];
    private static readonly double[] Hole = [19.934, 50.054, 19.936, 50.054, 19.936, 50.056, 19.934, 50.056, 19.934, 50.054];

    [Theory]
    [InlineData(0, 1)]
    [InlineData(55, 2)]
    [InlineData(60, 2)]
    [InlineData(65, 3)]
    [InlineData(80, 3)]
    public void Map_band_gives_the_noise_level(double fromDb, int level) => Assert.Equal(level, AmbientNoise.Level(fromDb));

    [Fact]
    public void Band_is_described_in_decibels()
    {
        Assert.Equal("poniżej 55 dB", AmbientNoise.Describe(0));
        Assert.Equal("60–65 dB", AmbientNoise.Describe(60));
        Assert.Equal("80 dB i więcej", AmbientNoise.Describe(80));
    }

    [Theory]
    [InlineData(-24.14, 0)]
    [InlineData(55, 55)]
    [InlineData(56, 55)]
    [InlineData(64, 60)]
    [InlineData(80, 80)]
    public void Rail_bands_of_one_decibel_fall_into_five_decibel_bands(double isov1, double band) =>
        Assert.Equal(band, MsipNoiseSource.BandFrom(isov1));

    [Fact]
    public void Point_in_a_hole_is_outside_the_area()
    {
        var area = new NoiseArea(60, [Outer, Hole]);

        Assert.True(area.Contains(50.051, 19.931));
        Assert.False(area.Contains(50.055, 19.935));
        Assert.False(area.Contains(50.07, 19.935));
    }

    [Fact]
    public void Louder_of_overlapping_areas_wins_and_a_point_off_the_map_has_no_band()
    {
        var map = new NoiseMap([new NoiseArea(0, [Outer]), new NoiseArea(70, [Hole])]);

        Assert.Equal(70, map.BandAt(50.055, 19.935));
        Assert.Equal(0, map.BandAt(50.051, 19.931));
        Assert.Null(map.BandAt(50.2, 20.1));
    }

    [Fact]
    public void GeoJson_from_the_map_service_is_read_with_bands_and_holes()
    {
        const string json = """
            [
              { "properties": { "ISOV1": -24.14 }, "geometry": { "type": "Polygon", "coordinates": [[[19.93,50.05],[19.94,50.05],[19.94,50.06],[19.93,50.05]]] } },
              { "properties": { "ISOV1": 57 }, "geometry": { "type": "MultiPolygon", "coordinates": [[[[19.93,50.05],[19.94,50.05],[19.94,50.06],[19.93,50.05]]]] } },
              { "properties": { "ISOV1": 60 }, "geometry": null }
            ]
            """;
        using var doc = JsonDocument.Parse(json);

        var areas = MsipNoiseSource.ParseAreas(doc.RootElement).ToList();

        Assert.Equal([0d, 55d], areas.Select(a => a.FromDb));
    }

    [Fact]
    public void Reading_the_map_again_replaces_the_feature_and_skips_benches()
    {
        var map = new NoiseMap([new NoiseArea(65, [Outer])]);
        var museum = PlaceAt(PlaceCategory.Museum, Ambient(55)) with { Lat = 50.051, Lon = 19.931 };
        var bench = PlaceAt(PlaceCategory.Bench) with { Id = "b1", Lat = 50.051, Lon = 19.931 };

        var result = MsipNoiseSource.Apply(MsipNoiseSource.Apply([museum, bench], map), map);

        var feature = Assert.Single(result[0].Features);
        Assert.Equal(65, feature.Value);
        Assert.Equal(MsipNoiseSource.Source, feature.Source);
        Assert.False(feature.IsDemoData);
        Assert.Empty(result[1].Features);
    }

    [Fact]
    public void Quiet_park_is_accessible_for_noise_from_the_city_map()
    {
        var assessment = AssessmentEngine.Assess(QuietOnly, PlaceAt(PlaceCategory.Park, Ambient(0)));

        Assert.Equal(AssessmentStatus.Accessible, assessment.Status);
        var reason = Assert.Single(assessment.Reasons);
        Assert.Equal(MsipNoiseSource.Source, reason.Source);
    }

    [Fact]
    public void Even_a_very_loud_stop_is_limited_and_not_inaccessible_because_the_map_is_a_daily_average()
    {
        var assessment = AssessmentEngine.Assess(QuietOnly, PlaceAt(PlaceCategory.Stop, Ambient(75)));

        Assert.Equal(AssessmentStatus.Limited, assessment.Status);
        Assert.Contains("MSIP", assessment.Reasons.Single().Source);
    }

    [Fact]
    public void Street_noise_never_tells_how_loud_it_is_inside_a_building()
    {
        var quietStreet = AssessmentEngine.Assess(QuietOnly, PlaceAt(PlaceCategory.Museum, Ambient(0)));
        var loudStreet = AssessmentEngine.Assess(ModerateNoise, PlaceAt(PlaceCategory.Museum, Ambient(80)));

        Assert.Equal(AssessmentStatus.Unknown, quietStreet.Status);
        Assert.Contains(quietStreet.Reasons, r => r.Kind == ReasonKind.Missing && r.Key == FeatureKey.NoiseLevel);
        Assert.Contains(quietStreet.Reasons, r => r.Kind == ReasonKind.Amenity && r.Key == FeatureKey.AmbientNoiseDb);

        // Głośna ulica to utrudnienie przy dojściu, ale nie przesądza o niedostępności wnętrza.
        Assert.Equal(AssessmentStatus.Limited, loudStreet.Status);
    }

    [Fact]
    public void Noise_measured_on_site_wins_over_the_map_outdoors()
    {
        var onSite = new AccessibilityFeature(FeatureKey.NoiseLevel, FeatureState.Yes, 1, "test", null, false);

        var assessment = AssessmentEngine.Assess(QuietOnly, PlaceAt(PlaceCategory.Park, onSite, Ambient(70)));

        Assert.Equal(AssessmentStatus.Accessible, assessment.Status);
        Assert.Contains(assessment.Reasons, r => r.Key == FeatureKey.NoiseLevel && r.Kind == ReasonKind.Amenity);
        Assert.DoesNotContain(assessment.Reasons, r => r.Key == FeatureKey.AmbientNoiseDb);
    }
}
