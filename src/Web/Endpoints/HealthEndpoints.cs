using Infrastructure.Mongo;

namespace Web.Endpoints;

public static class HealthEndpoints
{
    /// <summary>Stan połączenia z bazą. Szczegóły błędu zostają w logach hosta, bo mogą zawierać adresy serwerów.</summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health/db", async (MongoHealth health, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var result = await health.CheckAsync(ct);
            switch (result.Status)
            {
                case MongoHealthStatus.Ok:
                    return Results.Ok(new { status = "ok", database = result.Database, latencyMs = result.LatencyMs });

                case MongoHealthStatus.NotConfigured:
                    return Results.Json(new { status = "not-configured", detail = "Brak ustawienia Mongo:ConnectionString." },
                        statusCode: StatusCodes.Status503ServiceUnavailable);

                default:
                    loggers.CreateLogger("Mongo").LogWarning("Baza niedostępna: {Error}", result.Error);
                    return Results.Json(new { status = "unreachable", detail = "Host nie połączył się z bazą. Szczegóły są w logach hosta." },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        return app;
    }
}
