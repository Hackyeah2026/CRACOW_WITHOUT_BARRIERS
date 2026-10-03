using Domain.Assessments;
using Domain.Places;

namespace Domain.Trips;

public sealed record TripStop(int Order, Place Place, Assessment Assessment);

/// <param name="IsEstimated">Odcinek policzony w linii prostej, bez silnika routingu.</param>
public sealed record TripLeg(
    string FromPlaceId, string ToPlaceId, double DistanceM, double DurationMin,
    IReadOnlyList<GeoPoint> Geometry, bool IsEstimated, IReadOnlyList<string> Warnings);

public sealed record TripPlan(
    string Id, AppMode Mode, DateTimeOffset CreatedAt,
    IReadOnlyList<TripStop> Stops, IReadOnlyList<TripLeg> Legs)
{
    public double TotalDistanceM => Legs.Sum(l => l.DistanceM);
    public double TotalDurationMin => Legs.Sum(l => l.DurationMin);
}
