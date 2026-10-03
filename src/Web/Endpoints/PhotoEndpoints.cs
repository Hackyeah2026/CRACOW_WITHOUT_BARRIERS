using Application.Abstractions;
using Domain.Photos;

namespace Web.Endpoints;

public static class PhotoEndpoints
{
    public const string AnalyzeLimit = "photo-analyze";

    /// <summary>
    /// Ocena zdjęcia do zgłoszenia przez model AI (wymaga konta, bo każde wywołanie kosztuje).
    /// Zdjęcie nie jest zapisywane: trafia tylko do modelu, a odpowiedź wraca do przeglądarki.
    /// </summary>
    public static IEndpointRouteBuilder MapPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/photos/analyze", async (IFormFile photo, IObstacleDetector detector, ILoggerFactory loggers, CancellationToken ct) =>
        {
            if (photo.Length > PhotoUpload.MaxBytes)
                return Results.Problem($"Zdjęcie może mieć najwyżej {PhotoUpload.MaxBytes / (1024 * 1024)} MB.", statusCode: StatusCodes.Status400BadRequest);

            var content = new byte[photo.Length];
            await using (var stream = photo.OpenReadStream())
                await stream.ReadExactlyAsync(content, ct);

            var upload = new PhotoUpload(content, photo.ContentType);
            var errors = upload.Validate();
            if (errors.Count > 0)
                return Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);

            var result = await detector.AnalyzeAsync(upload, ct);
            if (result.IsFailure)
            {
                loggers.CreateLogger("OpenAI").LogWarning("Analiza zdjęcia: {Error}", result.Error);
                return Results.Problem("Analiza zdjęć jest chwilowo niedostępna.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            return Results.Ok(result.Value);
        })
        .RequireAuthorization(AccountEndpoints.UserPolicy)
        .RequireRateLimiting(AnalyzeLimit)
        // Klient WebAssembly nie ma tokenu antiforgery; przed żądaniami z innych stron chroni ciasteczko sesji
        // SameSite=Strict, tak jak w pozostałych endpointach zgłoszeń.
        .DisableAntiforgery();

        return app;
    }
}
