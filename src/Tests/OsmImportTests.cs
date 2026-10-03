using Domain.Places;
using Tools;

namespace Tests;

public class OsmImportTests
{
    private static Dictionary<string, string> Tags(params (string Key, string Value)[] tags) => tags.ToDictionary(t => t.Key, t => t.Value);

    private static readonly DateOnly Edited = new(2026, 10, 1);

    [Theory]
    [InlineData("amenity", "pharmacy", PlaceCategory.Pharmacy)]
    [InlineData("amenity", "doctors", PlaceCategory.Clinic)]
    [InlineData("healthcare", "physiotherapist", PlaceCategory.Clinic)]
    [InlineData("amenity", "post_office", PlaceCategory.Service)]
    [InlineData("amenity", "townhall", PlaceCategory.Office)]
    [InlineData("amenity", "fast_food", PlaceCategory.Food)]
    [InlineData("amenity", "place_of_worship", PlaceCategory.Worship)]
    [InlineData("amenity", "bench", PlaceCategory.Bench)]
    [InlineData("tourism", "viewpoint", PlaceCategory.Attraction)]
    [InlineData("historic", "castle", PlaceCategory.Attraction)]
    [InlineData("leisure", "park", PlaceCategory.Park)]
    [InlineData("shop", "supermarket", PlaceCategory.Shop)]
    [InlineData("highway", "bus_stop", PlaceCategory.Stop)]
    public void Tag_gives_the_category(string key, string value, PlaceCategory expected) =>
        Assert.Equal(expected, OsmTags.Category(Tags((key, value))));

    [Fact]
    public void Church_is_an_attraction_only_when_tagged_so_and_a_street_never_is()
    {
        Assert.Equal(PlaceCategory.Worship, OsmTags.Category(Tags(("amenity", "place_of_worship"), ("historic", "church"))));
        Assert.Equal(PlaceCategory.Attraction, OsmTags.Category(Tags(("amenity", "place_of_worship"), ("tourism", "attraction"))));
        Assert.Null(OsmTags.Category(Tags(("highway", "pedestrian"), ("tourism", "attraction"))));
        Assert.Null(OsmTags.Category(Tags(("building", "yes"))));
    }

    [Fact]
    public void Private_toilet_is_skipped_and_a_customers_only_one_says_so()
    {
        Assert.Null(OsmTags.Category(Tags(("amenity", "toilets"), ("access", "private"))));

        var customers = Tags(("amenity", "toilets"), ("access", "customers"), ("fee", "yes"));
        Assert.Equal(PlaceCategory.Toilet, OsmTags.Category(customers));
        Assert.Equal("Toaleta dla klientów", OsmTags.Name(customers, PlaceCategory.Toilet));
        Assert.Equal("Tylko dla klientów. Płatna.", OsmTags.Description(customers, PlaceCategory.Toilet));
    }

    [Fact]
    public void Only_parking_with_disabled_spaces_is_imported()
    {
        Assert.Null(OsmTags.Category(Tags(("amenity", "parking"))));
        Assert.Null(OsmTags.Category(Tags(("amenity", "parking"), ("capacity:disabled", "0"))));
        Assert.Null(OsmTags.Category(Tags(("amenity", "parking_space"))));

        var parking = Tags(("amenity", "parking"), ("capacity:disabled", "3"));
        Assert.Equal(PlaceCategory.DisabledParking, OsmTags.Category(parking));
        Assert.Contains("(3)", OsmTags.Name(parking, PlaceCategory.DisabledParking));
        Assert.Equal(PlaceCategory.DisabledParking, OsmTags.Category(Tags(("amenity", "parking_space"), ("parking_space", "disabled"))));
    }

    [Fact]
    public void Unnamed_place_is_kept_only_for_categories_that_have_no_own_names()
    {
        Assert.Equal("Ławka", OsmTags.Name(Tags(("amenity", "bench")), PlaceCategory.Bench));
        Assert.Null(OsmTags.Name(Tags(("amenity", "restaurant")), PlaceCategory.Food));
        Assert.Equal("Bar Mleczny", OsmTags.Name(Tags(("name", "Bar Mleczny")), PlaceCategory.Food));
    }

    [Fact]
    public void Missing_tag_gives_no_feature_and_wheelchair_values_keep_their_meaning()
    {
        Assert.Empty(OsmTags.Features(Tags(("amenity", "cafe")), PlaceCategory.Food, Edited));

        var yes = OsmTags.Features(Tags(("wheelchair", "yes"), ("toilets:wheelchair", "no")), PlaceCategory.Food, Edited);
        Assert.Contains(yes, f => f is { Key: FeatureKey.WheelchairAccess, State: FeatureState.Yes, Source: "OpenStreetMap", IsDemoData: false });
        Assert.Contains(yes, f => f is { Key: FeatureKey.AccessibleToilet, State: FeatureState.No });
        Assert.All(yes, f => Assert.Equal(Edited, f.CheckedOn));

        var limited = Assert.Single(OsmTags.Features(Tags(("wheelchair", "limited")), PlaceCategory.Museum, Edited));
        Assert.Equal(FeatureKey.WheelchairLimited, limited.Key);
    }

    [Fact]
    public void Toilet_wheelchair_tag_describes_the_toilet_and_a_bench_always_has_a_bench()
    {
        var toilet = OsmTags.Features(Tags(("amenity", "toilets"), ("wheelchair", "yes")), PlaceCategory.Toilet, Edited);
        Assert.Contains(toilet, f => f is { Key: FeatureKey.AccessibleToilet, State: FeatureState.Yes });

        var bench = Assert.Single(OsmTags.Features(Tags(("amenity", "bench")), PlaceCategory.Bench, Edited));
        Assert.Equal((FeatureKey.Benches, FeatureState.Yes), (bench.Key, bench.State));
    }

    [Fact]
    public void Pet_ban_is_not_read_as_a_ban_on_assistance_dogs()
    {
        Assert.Empty(OsmTags.Features(Tags(("dog", "no")), PlaceCategory.Food, Edited));
        Assert.Single(OsmTags.Features(Tags(("dog", "yes")), PlaceCategory.Food, Edited));
    }

    [Fact]
    public void Survey_date_is_used_when_osm_has_one()
    {
        Assert.Equal(new DateOnly(2025, 6, 14), OsmTags.CheckedOn(Tags(("check_date", "2025-06-14"))));
        Assert.Equal(new DateOnly(2024, 1, 2), OsmTags.CheckedOn(Tags(("check_date", "2025-06-14"), ("check_date:wheelchair", "2024-01-02"))));
        Assert.Null(OsmTags.CheckedOn(Tags(("check_date", "wiosna 2025"))));
        Assert.Null(OsmTags.CheckedOn(Tags(("name", "x"))));
    }

    [Fact]
    public void Address_falls_back_to_place_name_for_addresses_without_a_street()
    {
        Assert.Equal("Rynek Główny 3", OsmTags.Address(Tags(("addr:street", "Rynek Główny"), ("addr:housenumber", "3"))));
        Assert.Equal("Osiedle Teatralne 12", OsmTags.Address(Tags(("addr:place", "Osiedle Teatralne"), ("addr:housenumber", "12"))));
        Assert.Null(OsmTags.Address(Tags(("addr:housenumber", "12"))));
    }
}
