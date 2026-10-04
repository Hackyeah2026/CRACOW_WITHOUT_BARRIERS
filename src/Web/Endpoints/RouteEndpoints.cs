using Application.Abstractions;
using Domain.Hazards;

namespace Web.Endpoints;

public static class RouteEndpoints
{
    /// <summary>Pośrednik do silnika routingu. Klucz API zostaje na hoście.</summary>
    public static IEndpointRouteBuilder MapRouteEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/route", async (RouteQuery query, IRouteProvider routing, ILoggerFactory loggers, CancellationToken ct) =>
        {
            if (!IsValid(query.From.Lat, query.From.Lon) || !IsValid(query.To.Lat, query.To.Lon)
                || query.Avoid is { } avoid && avoid.Any(p => !IsValid(p.Lat, p.Lon)))
                return Api.Invalid("Nieprawidłowe współrzędne.");
            if (query.Avoid is { Count: > HazardRules.MaxAvoided })
                return Api.Invalid($"Trasa może omijać najwyżej {HazardRules.MaxAvoided} punktów.");

            var result = await routing.GetRouteAsync(query, ct);
            if (result.IsSuccess)
                return Results.Ok(result.Value);

            loggers.CreateLogger("Routing").LogWarning("Routing nieudany: {Error}", result.Error);
            return Results.Problem("Silnik routingu jest niedostępny.", statusCode: StatusCodes.Status502BadGateway);
        });
        return app;
    }

    private static bool IsValid(double lat, double lon) => lat is >= -90 and <= 90 && lon is >= -180 and <= 180;
}
