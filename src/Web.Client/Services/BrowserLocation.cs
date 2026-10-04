using Domain.Places;
using Microsoft.JSInterop;

namespace Web.Client.Services;

/// <summary>Dlaczego przeglądarka nie podała lokalizacji.</summary>
public enum LocationFailure { Denied, Timeout, Unsupported, Unavailable }

/// <summary>Wynik pytania o lokalizację: punkt albo powód jego braku.</summary>
public sealed record LocationResult(GeoPoint? Point, LocationFailure? Failure);

/// <summary>
/// Bieżąca lokalizacja z przeglądarki (wwwroot/js/geolocation.js). Strony używają jej tylko w pamięci:
/// jako punkt startu planu albo położenie zgłaszanego utrudnienia.
/// </summary>
public sealed class BrowserLocation(IJSRuntime js)
{
    public async Task<LocationResult> GetAsync()
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/geolocation.js");
        var position = await module.InvokeAsync<Position>("getCurrentPosition");
        if (position.Ok)
            return new LocationResult(new GeoPoint(position.Lat, position.Lon), null);

        return new LocationResult(null, position.Reason switch
        {
            "denied" => LocationFailure.Denied,
            "timeout" => LocationFailure.Timeout,
            "unsupported" => LocationFailure.Unsupported,
            _ => LocationFailure.Unavailable
        });
    }

    private sealed record Position(bool Ok, double Lat, double Lon, string? Reason);
}
