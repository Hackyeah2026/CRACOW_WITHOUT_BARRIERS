using Domain.Needs;
using Domain.Places;

namespace Domain.Assessments;

public interface IAssessmentRule
{
    IEnumerable<AssessmentReason> Evaluate(NeedsProfile profile, Place place);
}

internal static class Reasons
{
    public static AssessmentReason Barrier(Place place, FeatureKey key, AssessmentStatus impact, string message) =>
        Create(place, key, ReasonKind.Barrier, impact, message);

    public static AssessmentReason Amenity(Place place, FeatureKey key, string message) =>
        Create(place, key, ReasonKind.Amenity, AssessmentStatus.Accessible, message);

    /// <param name="blocking">Brak tej informacji uniemożliwia ocenę (status "brak danych").</param>
    public static AssessmentReason Missing(FeatureKey key, string message, bool blocking) =>
        new(key, ReasonKind.Missing, blocking ? AssessmentStatus.Unknown : AssessmentStatus.Accessible, message);

    private static AssessmentReason Create(Place place, FeatureKey key, ReasonKind kind, AssessmentStatus impact, string message)
    {
        var feature = place.Feature(key);
        return new AssessmentReason(key, kind, impact, message, feature?.Source, feature?.IsDemoData ?? false);
    }
}

/// <summary>Wejście, schody, progi, drzwi, nawierzchnia, toaleta.</summary>
public sealed class MobilityRule : IAssessmentRule
{
    public IEnumerable<AssessmentReason> Evaluate(NeedsProfile profile, Place place)
    {
        if (profile.StepFreeRequired || profile.AvoidStairs)
        {
            // Bez wózka stopnie są utrudnieniem, a nie barierą nie do pokonania.
            var hard = profile.StepFreeRequired ? AssessmentStatus.Inaccessible : AssessmentStatus.Limited;

            if (place.StateOf(FeatureKey.WheelchairAccess) == FeatureState.No)
                yield return Reasons.Barrier(place, FeatureKey.WheelchairAccess, hard, "Miejsce oznaczone jako niedostępne dla wózków (stopnie przy wejściu lub wewnątrz).");
            else if (place.StateOf(FeatureKey.StepFreeEntrance) == FeatureState.No)
                yield return Reasons.Barrier(place, FeatureKey.StepFreeEntrance, hard, "Wejście ze stopniami.");
            else if (place.StateOf(FeatureKey.WheelchairLimited) == FeatureState.Yes)
                yield return Reasons.Barrier(place, FeatureKey.WheelchairLimited, AssessmentStatus.Limited, "Dostępność dla wózków ograniczona: część miejsca może być niedostępna lub potrzebna jest pomoc.");
            else if (place.StateOf(FeatureKey.WheelchairAccess) == FeatureState.Yes)
                yield return Reasons.Amenity(place, FeatureKey.WheelchairAccess, "Miejsce oznaczone jako dostępne dla wózków.");
            else if (place.StateOf(FeatureKey.StepFreeEntrance) == FeatureState.Yes)
                yield return Reasons.Amenity(place, FeatureKey.StepFreeEntrance, "Wejście bez stopni.");
            else
                yield return Reasons.Missing(FeatureKey.WheelchairAccess, "Brak informacji, czy wejście jest bez stopni.", blocking: true);

            if (place.StateOf(FeatureKey.Stairs) == FeatureState.Yes)
            {
                if (place.StateOf(FeatureKey.Elevator) == FeatureState.Yes)
                    yield return Reasons.Amenity(place, FeatureKey.Elevator, "Są schody, ale jest też winda.");
                else
                    yield return Reasons.Barrier(place, FeatureKey.Stairs, hard, "Schody bez potwierdzonej windy.");
            }
        }

        if (profile.MaxThresholdCm is { } maxThreshold && place.ValueOf(FeatureKey.ThresholdHeightCm) is { } threshold)
        {
            if (threshold > maxThreshold)
                yield return Reasons.Barrier(place, FeatureKey.ThresholdHeightCm, AssessmentStatus.Inaccessible, $"Próg {threshold:0.#} cm (Twój limit: {maxThreshold:0.#} cm).");
            else
                yield return Reasons.Amenity(place, FeatureKey.ThresholdHeightCm, $"Próg {threshold:0.#} cm mieści się w Twoim limicie.");
        }

        if (profile.MinDoorWidthCm is { } minDoor && place.ValueOf(FeatureKey.DoorWidthCm) is { } door)
        {
            if (door < minDoor)
                yield return Reasons.Barrier(place, FeatureKey.DoorWidthCm, AssessmentStatus.Inaccessible, $"Drzwi o szerokości {door:0} cm (potrzebujesz {minDoor:0} cm).");
            else
                yield return Reasons.Amenity(place, FeatureKey.DoorWidthCm, $"Drzwi o szerokości {door:0} cm.");
        }

        if (profile.AvoidCobblestone && place.StateOf(FeatureKey.SurfaceCobblestone) == FeatureState.Yes)
            yield return Reasons.Barrier(place, FeatureKey.SurfaceCobblestone, AssessmentStatus.Limited, "Nawierzchnia z bruku w otoczeniu miejsca.");

        if (profile.NeedsAccessibleToilet && place.Category != PlaceCategory.Stop)
        {
            switch (place.StateOf(FeatureKey.AccessibleToilet))
            {
                case FeatureState.Yes:
                    yield return Reasons.Amenity(place, FeatureKey.AccessibleToilet, "Toaleta dostosowana.");
                    break;
                case FeatureState.No:
                    yield return Reasons.Barrier(place, FeatureKey.AccessibleToilet, AssessmentStatus.Limited, "Brak toalety dostosowanej.");
                    break;
                default:
                    yield return Reasons.Missing(FeatureKey.AccessibleToilet, "Brak informacji o toalecie dostosowanej.", blocking: false);
                    break;
            }
        }
    }
}

/// <summary>Hałas, tłum, ciche miejsca.</summary>
public sealed class SensoryRule : IAssessmentRule
{
    public IEnumerable<AssessmentReason> Evaluate(NeedsProfile profile, Place place)
    {
        if (profile.MaxNoiseLevel is { } maxNoise)
            yield return Level(place, FeatureKey.NoiseLevel, maxNoise, "hałasu", "Brak informacji o poziomie hałasu.");

        if (profile.MaxCrowdLevel is { } maxCrowd)
            yield return Level(place, FeatureKey.CrowdLevel, maxCrowd, "tłumu", "Brak informacji o tym, jak tłoczno jest w tym miejscu.");

        if (profile.PrefersQuietRoom || profile.MaxNoiseLevel is not null)
        {
            if (place.StateOf(FeatureKey.QuietRoom) == FeatureState.Yes)
                yield return Reasons.Amenity(place, FeatureKey.QuietRoom, "Jest pokój lub strefa wyciszenia.");
            if (place.StateOf(FeatureKey.QuietHours) == FeatureState.Yes)
                yield return Reasons.Amenity(place, FeatureKey.QuietHours, "Miejsce ma ciche godziny.");
        }
    }

    private static AssessmentReason Level(Place place, FeatureKey key, int max, string what, string missing)
    {
        if (place.ValueOf(key) is not { } value)
            return Reasons.Missing(key, missing, blocking: true);

        var label = value switch { <= 1 => "niski", <= 2 => "umiarkowany", _ => "wysoki" };
        if (value <= max)
            return Reasons.Amenity(place, key, $"Poziom {what}: {label}.");

        var impact = value - max >= 2 ? AssessmentStatus.Inaccessible : AssessmentStatus.Limited;
        return Reasons.Barrier(place, key, impact, $"Poziom {what}: {label}, powyżej Twojej tolerancji.");
    }
}

/// <summary>Miejsca do siedzenia i odpoczynku.</summary>
public sealed class StaminaRule : IAssessmentRule
{
    public IEnumerable<AssessmentReason> Evaluate(NeedsProfile profile, Place place)
    {
        if (!profile.NeedsSeating)
            yield break;

        var benches = place.StateOf(FeatureKey.Benches);
        var seating = place.StateOf(FeatureKey.SeatingInside);

        if (benches == FeatureState.Yes || seating == FeatureState.Yes)
            yield return Reasons.Amenity(place, benches == FeatureState.Yes ? FeatureKey.Benches : FeatureKey.SeatingInside, "Są miejsca do siedzenia.");
        else if (benches == FeatureState.No && seating != FeatureState.Yes)
            yield return Reasons.Barrier(place, FeatureKey.Benches, AssessmentStatus.Limited, "Brak miejsc do siedzenia.");
        else
            yield return Reasons.Missing(FeatureKey.Benches, "Brak informacji o miejscach do siedzenia.", blocking: false);
    }
}
