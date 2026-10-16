using Domain.Places;
using Domain.Reports;
using Tools;

namespace Tests;

public class DataCoverageTests
{
    private static AccessibilityFeature F(FeatureKey key, FeatureState state, string source = "OpenStreetMap") => new(key, state, null, source, null, false);

    private static Place P(string id, PlaceCategory category, params AccessibilityFeature[] features) =>
        new(id, "krakow", id, category, 50, 20, null, null, features);

    [Fact]
    public void Coverage_counts_wheelchair_info_city_data_and_conflicts_and_ignores_ambient_noise()
    {
        Place[] places =
        [
            P("t1", PlaceCategory.Toilet, F(FeatureKey.AccessibleToilet, FeatureState.Yes, MsipToiletsSource.Source), F(FeatureKey.AccessibleToilet, FeatureState.No)),
            P("t2", PlaceCategory.Toilet, F(FeatureKey.AmbientNoiseDb, FeatureState.Yes, MsipNoiseSource.Source)),
            P("o1", PlaceCategory.Office, F(FeatureKey.WheelchairAccess, FeatureState.Yes)),
            P("o2", PlaceCategory.Office, F(FeatureKey.Elevator, FeatureState.No, ConfirmedGap.Source))
        ];

        var coverage = DataCoverage.Summarize(places);

        var toilets = coverage.Single(c => c.Category == PlaceCategory.Toilet);
        Assert.Equal((2, 0, 1, 1, 1), (toilets.Places, toilets.WithWheelchairInfo, toilets.WithAccessibilityInfo, toilets.WithCityData, toilets.WithConflicts));
        var offices = coverage.Single(c => c.Category == PlaceCategory.Office);
        Assert.Equal((2, 1, 2, 1, 0), (offices.Places, offices.WithWheelchairInfo, offices.WithAccessibilityInfo, offices.WithCityData, offices.WithConflicts));
        Assert.Equal(PlaceCategory.Toilet, coverage[0].Category);
    }

    [Fact]
    public void Conflicts_list_names_the_disputed_features_and_respects_the_limit()
    {
        Place[] places =
        [
            P("s1", PlaceCategory.Stop, F(FeatureKey.StopShelter, FeatureState.Yes, ZtpSheltersSource.Source), F(FeatureKey.StopShelter, FeatureState.No)),
            P("o1", PlaceCategory.Office, F(FeatureKey.Elevator, FeatureState.No, ConfirmedGap.Source), F(FeatureKey.Elevator, FeatureState.Yes)),
            P("m1", PlaceCategory.Museum, F(FeatureKey.WheelchairAccess, FeatureState.Yes))
        ];

        var conflicts = DataCoverage.Conflicts(places, limit: 1);

        var first = Assert.Single(conflicts);
        Assert.Equal("o1", first.PlaceId);
        Assert.Equal([FeatureKey.Elevator], first.Features);
    }

    [Theory]
    [InlineData(MsipNoiseSource.Source, true)]
    [InlineData(ZtpSheltersSource.Source, true)]
    [InlineData(ConfirmedGap.Source, true)]
    [InlineData("OpenStreetMap", false)]
    [InlineData("Deklaracja firmy (konto firmowe)", false)]
    public void City_sources_are_recognised(string source, bool city) => Assert.Equal(city, DataCoverage.IsCitySource(source));
}
