using Domain.Assessments;
using Domain.Places;
using Domain.Transit;

namespace Domain.Trips;

public sealed record TripStop(int Order, Place Place, Assessment Assessment);

/// <param name="IsEstimated">Odcinek policzony w linii prostej, bez silnika routingu.</param>
/// <param name="Transit">Wynik szukania połączenia komunikacją dla dłuższego odcinka; null, gdy odcinek jest krótki.</param>
public sealed record TripLeg(
    string FromPlaceId, string ToPlaceId, double DistanceM, double DurationMin,
    IReadOnlyList<GeoPoint> Geometry, bool IsEstimated, IReadOnlyList<string> Warnings,
    TransitAdvice? Transit);

public sealed record TripPlan(
    string Id, AppMode Mode, DateTimeOffset CreatedAt,
    IReadOnlyList<TripStop> Stops, IReadOnlyList<TripLeg> Legs)
{
    public double TotalDistanceM => Legs.Sum(l => l.DistanceM);
    public double TotalDurationMin => Legs.Sum(l => l.DurationMin);
}
