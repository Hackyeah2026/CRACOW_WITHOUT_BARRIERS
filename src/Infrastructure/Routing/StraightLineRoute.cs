using Application.Abstractions;

namespace Infrastructure.Routing;

/// <summary>
/// Wariant awaryjny, gdy silnik routingu jest niedostępny: odcinek w linii prostej z poprawką na układ ulic.
/// Wynik jest oznaczony jako szacunkowy.
/// </summary>
internal static class StraightLineRoute
{
    private const double DetourFactor = 1.3;

    public static RouteLeg Estimate(RouteRequest request)
    {
        var distance = request.From.DistanceTo(request.To) * DetourFactor;
        return new RouteLeg(distance, Duration.Minutes(distance, request.Profile.WalkingSpeedKmh), [request.From, request.To], IsEstimated: true, []);
    }
}

internal static class Duration
{
    public static double Minutes(double distanceM, double speedKmh) => distanceM / 1000 / speedKmh * 60;
}
