using Domain.Needs;
using Domain.Places;

namespace Domain.Assessments;

/// <summary>
/// Ocenia miejsce pod konkretny profil potrzeb. Status końcowy to najgorszy z wpływów cząstkowych.
/// "Dostępne" wymaga co najmniej jednego potwierdzonego udogodnienia: brak danych nigdy nie daje statusu "dostępne".
/// </summary>
public static class AssessmentEngine
{
    private static readonly IAssessmentRule[] Rules = [new MobilityRule(), new SensoryRule(), new StaminaRule()];

    public static Assessment Assess(NeedsProfile profile, Place place)
    {
        var reasons = Rules.SelectMany(rule => rule.Evaluate(profile, place)).ToList();
        if (reasons.Count == 0)
            return new Assessment(AssessmentStatus.Unknown, reasons);

        var worst = reasons.MaxBy(r => Severity(r.Impact))!.Impact;
        if (worst == AssessmentStatus.Accessible && reasons.All(r => r.Kind != ReasonKind.Amenity))
            worst = AssessmentStatus.Unknown;

        return new Assessment(worst, reasons);
    }

    private static int Severity(AssessmentStatus status) => status switch
    {
        AssessmentStatus.Inaccessible => 3,
        AssessmentStatus.Limited => 2,
        AssessmentStatus.Unknown => 1,
        _ => 0
    };
}
