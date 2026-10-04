using Domain.Places;

namespace Web.Client.Components;

/// <param name="Label">Numer przystanku w planie; pusty dla zwykłej pinezki.</param>
/// <param name="Icon">Symbol rysowany zamiast kółka (punkty odpoczynku: toaleta, ławka, ciche miejsce).</param>
/// <param name="Certified">Miejsce z certyfikatem konta firmowego: większa pinezka z gwiazdką w kolorze oceny.</param>
public sealed record MapMarker(
    string Id, string Name, double Lat, double Lon, string Color, string? Label = null, string? Icon = null, bool Certified = false);

/// <summary>
/// Odcinek trasy na mapie: pieszo (linia przerywana), komunikacją miejską (linia ciągła z nazwą linii)
/// albo najkrótsza droga, którą trasa omija z powodu utrudnienia (cienka linia kropkowana).
/// </summary>
public sealed record MapRouteSegment(IReadOnlyList<GeoPoint> Points, bool IsTransit = false, string? Label = null, bool IsBypassed = false);
