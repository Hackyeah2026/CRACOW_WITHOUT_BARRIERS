using Domain.Assessments;
using Domain.Hazards;
using Domain.Places;
using Domain.Transit;

namespace Domain.Trips;

/// <param name="IsUserLocation">Start spoza katalogu: lokalizacja użytkownika albo punkt wskazany na mapie.</param>
public sealed record TripStop(int Order, Place Place, Assessment Assessment, bool IsUserLocation = false);

/// <summary>Ławka przy trasie odcinka, proponowana na przerwę w marszu.</summary>
/// <param name="DistanceFromStartM">Ile marszu od początku odcinka do ławki.</param>
public sealed record RestStop(string PlaceId, double Lat, double Lon, double DistanceFromStartM);

/// <summary>Objazd odcinka: trasa poprowadzona inną drogą, żeby ominąć utrudnienia potwierdzone przez urząd.</summary>
/// <param name="Avoided">Punkty, przez które prowadziła najkrótsza trasa.</param>
/// <param name="ExtraDistanceM">O ile objazd jest dłuższy od najkrótszej trasy; ujemne, gdy wyszedł krótszy.</param>
/// <param name="DirectGeometry">Najkrótsza trasa, której plan nie używa; mapa pokazuje ją dla porównania.</param>
public sealed record RouteDetour(IReadOnlyList<VerifiedHazard> Avoided, double ExtraDistanceM, IReadOnlyList<GeoPoint> DirectGeometry);

/// <param name="IsEstimated">Odcinek policzony w linii prostej, bez silnika routingu.</param>
/// <param name="Transit">Wynik szukania połączenia komunikacją dla dłuższego odcinka; null, gdy odcinek jest krótki.</param>
/// <param name="Hazards">Utrudnienia potwierdzone przez urząd, leżące przy trasie odcinka; null w planach zapisanych przed ich wprowadzeniem.</param>
/// <param name="RestStops">Ławki na przerwę, gdy odcinek jest dłuższy niż limit marszu z profilu; null w planach zapisanych przed ich wprowadzeniem.</param>
/// <param name="Detour">Objazd utrudnień leżących na najkrótszej trasie; null, gdy trasa nie była zmieniana.</param>
public sealed record TripLeg(
    string FromPlaceId, string ToPlaceId, double DistanceM, double DurationMin,
    IReadOnlyList<GeoPoint> Geometry, bool IsEstimated, IReadOnlyList<string> Warnings,
    TransitAdvice? Transit, IReadOnlyList<HazardOnRoute>? Hazards = null,
    IReadOnlyList<RestStop>? RestStops = null, RouteDetour? Detour = null);

public sealed record TripPlan(
    string Id, AppMode Mode, DateTimeOffset CreatedAt,
    IReadOnlyList<TripStop> Stops, IReadOnlyList<TripLeg> Legs)
{
    public double TotalDistanceM => Legs.Sum(l => l.DistanceM);
    public double TotalDurationMin => Legs.Sum(l => l.DurationMin);
}
