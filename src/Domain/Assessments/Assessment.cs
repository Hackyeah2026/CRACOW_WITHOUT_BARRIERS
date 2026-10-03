using Domain.Places;

namespace Domain.Assessments;

public enum AssessmentStatus { Unknown, Accessible, Limited, Inaccessible }

public enum ReasonKind { Barrier, Amenity, Missing }

/// <param name="Impact">Jak ten powód wpływa na ocenę końcową (najgorszy wpływ wygrywa).</param>
public sealed record AssessmentReason(
    FeatureKey Key, ReasonKind Kind, AssessmentStatus Impact, string Message, string? Source = null, bool IsDemoData = false);

public sealed record Assessment(AssessmentStatus Status, IReadOnlyList<AssessmentReason> Reasons)
{
    public IEnumerable<AssessmentReason> Barriers => Reasons.Where(r => r.Kind == ReasonKind.Barrier);
    public IEnumerable<AssessmentReason> Amenities => Reasons.Where(r => r.Kind == ReasonKind.Amenity);
    public IEnumerable<AssessmentReason> Missing => Reasons.Where(r => r.Kind == ReasonKind.Missing);
}
