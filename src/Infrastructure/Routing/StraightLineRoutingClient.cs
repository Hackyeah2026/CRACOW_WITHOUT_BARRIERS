using Application.Abstractions;

namespace Infrastructure.Routing;

/// <summary>
/// Zaślepka do czasu podłączenia OpenRouteService: odcinek w linii prostej z poprawką na układ ulic.
/// Wynik jest oznaczony jako szacunkowy.
/// </summary>
internal sealed class StraightLineRoutingClient : IRoutingClient
{
    private const double DetourFactor = 1.3;

    public Task<Result<RouteLeg>> GetRouteAsync(RouteRequest request, CancellationToken ct)
    {
        var distance = request.From.DistanceTo(request.To) * DetourFactor;
        var duration = distance / 1000 / request.Profile.WalkingSpeedKmh * 60;
        return Task.FromResult(Result.Success(new RouteLeg(distance, duration, [request.From, request.To], IsEstimated: true)));
    }
}
