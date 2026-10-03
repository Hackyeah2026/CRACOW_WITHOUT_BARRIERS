using Domain.Assessments;
using Domain.Needs;
using Domain.Places;

namespace Tests;

public class AssessmentEngineTests
{
    private static readonly NeedsProfile Marta = NeedsProfilePresets.Build([NeedsProfilePresets.ElectricWheelchair]);
    private static readonly NeedsProfile Kuba = NeedsProfilePresets.Build([NeedsProfilePresets.Autism]);
    private static readonly NeedsProfile Zofia = NeedsProfilePresets.Build([NeedsProfilePresets.Senior]);

    private static Place PlaceWith(params AccessibilityFeature[] features) =>
        new("p1", "krakow", "Miejsce testowe", PlaceCategory.Museum, 50.06, 19.94, null, null, features);

    private static AccessibilityFeature Feature(FeatureKey key, FeatureState state, double? value = null) =>
        new(key, state, value, "test", null, false);

    [Fact]
    public void Missing_data_is_never_accessible()
    {
        var place = PlaceWith();

        Assert.Equal(AssessmentStatus.Unknown, AssessmentEngine.Assess(Marta, place).Status);
        Assert.Equal(AssessmentStatus.Unknown, AssessmentEngine.Assess(Kuba, place).Status);
        Assert.Equal(AssessmentStatus.Unknown, AssessmentEngine.Assess(Zofia, place).Status);
    }

    [Fact]
    public void Empty_profile_gives_no_assessment()
    {
        var assessment = AssessmentEngine.Assess(NeedsProfile.Empty, PlaceWith(Feature(FeatureKey.WheelchairAccess, FeatureState.Yes)));

        Assert.Equal(AssessmentStatus.Unknown, assessment.Status);
        Assert.Empty(assessment.Reasons);
    }

    [Fact]
    public void Wheelchair_accessible_place_is_accessible_for_Marta()
    {
        var assessment = AssessmentEngine.Assess(Marta, PlaceWith(
            Feature(FeatureKey.WheelchairAccess, FeatureState.Yes), Feature(FeatureKey.AccessibleToilet, FeatureState.Yes)));

        Assert.Equal(AssessmentStatus.Accessible, assessment.Status);
        Assert.Empty(assessment.Barriers);
    }

    [Fact]
    public void Steps_at_entrance_block_Marta_but_only_limit_Zofia()
    {
        var place = PlaceWith(Feature(FeatureKey.WheelchairAccess, FeatureState.No));

        Assert.Equal(AssessmentStatus.Inaccessible, AssessmentEngine.Assess(Marta, place).Status);
        Assert.Equal(AssessmentStatus.Limited, AssessmentEngine.Assess(Zofia, place).Status);
    }

    [Fact]
    public void Stairs_with_elevator_are_not_a_barrier()
    {
        var assessment = AssessmentEngine.Assess(Marta, PlaceWith(
            Feature(FeatureKey.StepFreeEntrance, FeatureState.Yes), Feature(FeatureKey.Stairs, FeatureState.Yes),
            Feature(FeatureKey.Elevator, FeatureState.Yes)));

        Assert.Equal(AssessmentStatus.Accessible, assessment.Status);
    }

    [Fact]
    public void Threshold_above_limit_blocks_wheelchair()
    {
        var assessment = AssessmentEngine.Assess(Marta, PlaceWith(
            Feature(FeatureKey.WheelchairAccess, FeatureState.Yes), Feature(FeatureKey.ThresholdHeightCm, FeatureState.Yes, 8)));

        Assert.Equal(AssessmentStatus.Inaccessible, assessment.Status);
        Assert.Contains(assessment.Barriers, r => r.Key == FeatureKey.ThresholdHeightCm);
    }

    [Fact]
    public void Same_place_gets_different_assessment_per_profile()
    {
        // Dostępne dla wózka, ale głośne i zatłoczone.
        var place = PlaceWith(
            Feature(FeatureKey.WheelchairAccess, FeatureState.Yes),
            Feature(FeatureKey.NoiseLevel, FeatureState.Yes, 3), Feature(FeatureKey.CrowdLevel, FeatureState.Yes, 3));

        Assert.Equal(AssessmentStatus.Accessible, AssessmentEngine.Assess(Marta, place).Status);
        Assert.Equal(AssessmentStatus.Inaccessible, AssessmentEngine.Assess(Kuba, place).Status);
        Assert.Equal(AssessmentStatus.Accessible, AssessmentEngine.Assess(Zofia, place).Status);
    }

    [Fact]
    public void Quiet_place_is_accessible_for_Kuba_and_moderate_is_limited()
    {
        var quiet = PlaceWith(Feature(FeatureKey.NoiseLevel, FeatureState.Yes, 1), Feature(FeatureKey.CrowdLevel, FeatureState.Yes, 1));
        var moderate = PlaceWith(Feature(FeatureKey.NoiseLevel, FeatureState.Yes, 1), Feature(FeatureKey.CrowdLevel, FeatureState.Yes, 2));

        Assert.Equal(AssessmentStatus.Accessible, AssessmentEngine.Assess(Kuba, quiet).Status);
        Assert.Equal(AssessmentStatus.Limited, AssessmentEngine.Assess(Kuba, moderate).Status);
    }

    [Fact]
    public void Combined_profile_takes_stricter_values()
    {
        var combined = NeedsProfilePresets.Build([NeedsProfilePresets.ManualWheelchair, NeedsProfilePresets.SensorySensitivity, NeedsProfilePresets.Senior]);

        Assert.True(combined.StepFreeRequired);
        Assert.Equal(2, combined.MaxNoiseLevel);
        Assert.Equal(500, combined.MaxDistanceWithoutRestM);
        Assert.Equal(3, combined.WalkingSpeedKmh);
    }

    [Fact]
    public void Deaf_profile_requires_sign_language_interpreter_in_public_office()
    {
        var deaf = NeedsProfilePresets.Build([NeedsProfilePresets.Deaf]);
        var officeWithSignLanguage = new Place("office1", "krakow", "Urząd", PlaceCategory.Office, 50.06, 19.94, null, null,
            [Feature(FeatureKey.SignLanguage, FeatureState.Yes)]);
        var officeNoSignLanguage = new Place("office2", "krakow", "Urząd 2", PlaceCategory.Office, 50.06, 19.94, null, null,
            [Feature(FeatureKey.SignLanguage, FeatureState.No)]);

        Assert.Equal(AssessmentStatus.Accessible, AssessmentEngine.Assess(deaf, officeWithSignLanguage).Status);
        Assert.Equal(AssessmentStatus.Inaccessible, AssessmentEngine.Assess(deaf, officeNoSignLanguage).Status);
    }

    [Fact]
    public void Blind_profile_requires_tactile_paving_and_assistance_dog_approval()
    {
        var blind = NeedsProfilePresets.Build([NeedsProfilePresets.Blind]);
        var accessibleMuseum = PlaceWith(
            Feature(FeatureKey.TactilePaving, FeatureState.Yes),
            Feature(FeatureKey.AudioDescription, FeatureState.Yes),
            Feature(FeatureKey.AssistanceDogAllowed, FeatureState.Yes));

        var forbiddenDogMuseum = PlaceWith(
            Feature(FeatureKey.TactilePaving, FeatureState.Yes),
            Feature(FeatureKey.AssistanceDogAllowed, FeatureState.No));

        Assert.Equal(AssessmentStatus.Accessible, AssessmentEngine.Assess(blind, accessibleMuseum).Status);
        Assert.Equal(AssessmentStatus.Inaccessible, AssessmentEngine.Assess(blind, forbiddenDogMuseum).Status);
    }
}
