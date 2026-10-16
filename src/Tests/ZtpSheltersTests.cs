using Domain.Assessments;
using Domain.Needs;
using Domain.Places;
using Tools;

namespace Tests;

public class ZtpSheltersTests
{
    private static readonly NeedsProfile Zofia = NeedsProfilePresets.Build([NeedsProfilePresets.Senior]);

    private static Place Stop(string id, string name, double lat, double lon, params AccessibilityFeature[] features) =>
        new(id, "krakow", name, PlaceCategory.Stop, lat, lon, null, null, features);

    [Theory]
    [InlineData("Agatowa 01", "agatowa 01")]
    [InlineData("Cracovia Stadion 02-02", "cracovia stadion 02")]
    [InlineData(" Rondo Mogilskie 03 ", "rondo mogilskie 03")]
    public void Extra_shelter_suffix_points_to_the_stop_pole(string name, string key) => Assert.Equal(key, ZtpSheltersSource.StopKey(name));

    [Fact]
    public void Shelter_joins_the_stop_with_the_same_name_or_the_nearest_one_and_is_counted_once()
    {
        var places = new List<Place>
        {
            Stop("s1", "Agatowa 01", 50.0300, 20.0000),
            Stop("s2", "Agatowa 02", 50.0302, 20.0003),
            Stop("s3", "Bez nazwy w rejestrze", 50.0500, 20.0500)
        };
        StopShelter[] shelters =
        [
            new("Agatowa 01", 50.0302, 20.0003), // bliżej słupka 02, ale nazwa wskazuje słupek 01
            new("Agatowa 01-02", 50.0300, 20.0001),
            new("Inna nazwa", 50.05001, 20.05001),
            new("Daleko", 50.10, 20.10)
        ];

        var result = ZtpSheltersSource.Apply(places, shelters, out var unmatched);

        Assert.Equal(FeatureState.Yes, result[0].StateOf(FeatureKey.StopShelter));
        Assert.Single(result[0].Features);
        Assert.Equal(FeatureState.Unknown, result[1].StateOf(FeatureKey.StopShelter));
        Assert.Equal(FeatureState.Yes, result[2].StateOf(FeatureKey.StopShelter));
        Assert.Equal(1, unmatched);
    }

    [Fact]
    public void Registry_shelter_is_first_and_the_contradicting_map_stays_visible_and_reruns_change_nothing()
    {
        var osmNo = new AccessibilityFeature(FeatureKey.StopShelter, FeatureState.No, null, "OpenStreetMap", null, false);
        StopShelter[] shelters = [new("Agatowa 01", 50.03, 20.0)];

        var once = ZtpSheltersSource.Apply([Stop("s1", "Agatowa 01", 50.03, 20.0, osmNo)], shelters, out _);
        var twice = ZtpSheltersSource.Apply(once, shelters, out _);

        Assert.Equal(ZtpSheltersSource.Source, once[0].Feature(FeatureKey.StopShelter)!.Source);
        Assert.Single(once[0].ConflictsOn(FeatureKey.StopShelter));
        Assert.Equal(once[0].Features.Count, twice[0].Features.Count);
    }

    [Fact]
    public void Shelter_is_an_amenity_for_someone_who_needs_to_rest_while_waiting()
    {
        var stop = Stop("s1", "Agatowa 01", 50.03, 20.0,
            new AccessibilityFeature(FeatureKey.StopShelter, FeatureState.Yes, null, ZtpSheltersSource.Source, null, false));

        var assessment = AssessmentEngine.Assess(Zofia, stop);

        Assert.Contains(assessment.Reasons, r => r.Key == FeatureKey.StopShelter && r.Kind == ReasonKind.Amenity && r.Source == ZtpSheltersSource.Source);
    }
}
