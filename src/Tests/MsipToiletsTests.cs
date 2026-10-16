using Domain.Assessments;
using Domain.Needs;
using Domain.Places;
using Tools;

namespace Tests;

public class MsipToiletsTests
{
    private static readonly NeedsProfile Marta = NeedsProfilePresets.Build([NeedsProfilePresets.ElectricWheelchair]);

    private static CityToilet Toilet(string? accessible = "Tak", string? accessType = "wjazd z poziomu 0", bool open = true,
        double lat = 50.0577, double lon = 19.9457) =>
        new(1, lat, lon, "ul. Dietla/ul. Starowiślna", "wolnostojąca", "całodobowo", "Codziennie", false,
            accessible, accessType, open, ChangingTable: true, Notes: open ? null : "remont do 16.06.2023");

    private static Place OsmToilet(double lat, double lon, params AccessibilityFeature[] features) =>
        new("osm-node-1", "krakow", "Toaleta publiczna", PlaceCategory.Toilet, lat, lon, null, null, features);

    private static AccessibilityFeature Osm(FeatureKey key, FeatureState state) => new(key, state, null, "OpenStreetMap", null, false);

    [Fact]
    public void Level_entrance_gives_step_free_access_and_an_accessible_toilet()
    {
        var features = MsipToiletsSource.Features(Toilet());

        Assert.All(features, f => Assert.Equal(MsipToiletsSource.Source, f.Source));
        Assert.Contains(features, f => f is { Key: FeatureKey.AccessibleToilet, State: FeatureState.Yes });
        Assert.Contains(features, f => f is { Key: FeatureKey.StepFreeEntrance, State: FeatureState.Yes });
    }

    [Fact]
    public void Platform_to_an_underground_toilet_means_stairs_with_a_lift()
    {
        var place = OsmToilet(50, 20) with { Features = MsipToiletsSource.Features(Toilet(accessType: "platforma")) };

        var assessment = AssessmentEngine.Assess(Marta, place);

        Assert.Equal(AssessmentStatus.Accessible, assessment.Status);
        Assert.Contains(assessment.Reasons, r => r.Key == FeatureKey.Elevator && r.Kind == ReasonKind.Amenity);
    }

    [Fact]
    public void Stair_climber_is_not_a_lift_for_an_electric_wheelchair()
    {
        var place = OsmToilet(50, 20) with { Features = MsipToiletsSource.Features(Toilet(accessType: "schodołaz")) };

        Assert.Equal(AssessmentStatus.Inaccessible, AssessmentEngine.Assess(Marta, place).Status);
    }

    [Fact]
    public void Toilet_without_adaptation_and_unknown_access_type_says_only_what_is_known()
    {
        var features = MsipToiletsSource.Features(Toilet(accessible: "nie", accessType: null));

        var feature = Assert.Single(features);
        Assert.Equal((FeatureKey.AccessibleToilet, FeatureState.No), (feature.Key, feature.State));
    }

    [Fact]
    public void Closed_toilet_is_described_as_closed_with_the_year_of_the_data()
    {
        var text = MsipToiletsSource.Describe(Toilet(open: false));

        Assert.Contains("nieczynna", text);
        Assert.Contains("2023", text);
        Assert.Contains("remont do 16.06.2023", text);
        Assert.Contains("Jest przewijak.", text);
    }

    [Fact]
    public void City_toilet_joins_the_nearby_osm_toilet_and_a_distant_one_is_added()
    {
        var near = OsmToilet(50.0577, 19.9458, Osm(FeatureKey.TactilePaving, FeatureState.Yes));
        var cityNear = Toilet();
        var cityFar = Toilet(lat: 50.07, lon: 19.96) with { Id = 2 };

        var result = MsipToiletsSource.Apply([near], [cityNear, cityFar], "krakow");

        Assert.Equal(2, result.Count);
        var merged = result.Single(p => p.Id == "osm-node-1");
        Assert.Equal("ul. Dietla/ul. Starowiślna", merged.Address);
        Assert.Equal(MsipToiletsSource.Source, merged.Features[0].Source);
        Assert.Contains(merged.Features, f => f.Key == FeatureKey.TactilePaving);
        Assert.Contains(result, p => p.Id == $"{MsipToiletsSource.IdPrefix}2");
    }

    [Fact]
    public void Applying_the_city_data_twice_gives_the_same_catalog()
    {
        var places = new List<Place> { OsmToilet(50.0577, 19.9458, Osm(FeatureKey.AccessibleToilet, FeatureState.No)) };
        CityToilet[] toilets = [Toilet(), Toilet(lat: 50.07, lon: 19.96) with { Id = 2 }];

        var once = MsipToiletsSource.Apply(places, toilets, "krakow");
        var twice = MsipToiletsSource.Apply(once, toilets, "krakow");

        Assert.Equal(once.Count, twice.Count);
        Assert.Equal(once.Select(p => p.Features.Count), twice.Select(p => p.Features.Count));
    }

    [Fact]
    public void Conflicting_sources_are_kept_and_the_reason_says_so()
    {
        var place = MsipToiletsSource.Apply([OsmToilet(50.0577, 19.9458, Osm(FeatureKey.AccessibleToilet, FeatureState.No))], [Toilet()], "krakow")[0];

        var conflict = Assert.Single(place.ConflictsOn(FeatureKey.AccessibleToilet));
        Assert.Equal("OpenStreetMap", conflict.Source);
        Assert.Equal(FeatureState.Yes, place.StateOf(FeatureKey.AccessibleToilet));

        var reason = AssessmentEngine.Assess(Marta, place).Reasons.Single(r => r.Key == FeatureKey.AccessibleToilet);
        Assert.Contains("źródła się różnią", reason.Message);
        Assert.Contains("OpenStreetMap", reason.Message);
    }

    [Fact]
    public void Agreeing_sources_are_not_a_conflict()
    {
        var place = OsmToilet(50, 20,
            new AccessibilityFeature(FeatureKey.AccessibleToilet, FeatureState.Yes, null, MsipToiletsSource.Source, null, false),
            Osm(FeatureKey.AccessibleToilet, FeatureState.Yes));

        Assert.Empty(place.ConflictsOn(FeatureKey.AccessibleToilet));
    }
}
