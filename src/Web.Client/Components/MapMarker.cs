namespace Web.Client.Components;

/// <param name="Label">Numer przystanku w planie; pusty dla zwykłej pinezki.</param>
public sealed record MapMarker(string Id, string Name, double Lat, double Lon, string Color, string? Label = null);
