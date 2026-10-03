using Application.Abstractions;
using Domain.Photos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Web.Endpoints;

public static class PhotoEndpoints
{
    public const string AnalyzeLimit = "photo-analyze";

    /// <summary>
    /// Ocena zdjęcia do zgłoszenia przez model AI (wymaga konta, bo każde wywołanie kosztuje): wersja do analizy
    /// trafia tylko do modelu. Zmniejszona kopia jest zapisywana dopiero razem ze zgłoszeniem; potem widzi ją
    /// urząd i konto, które ją wysłało.
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

        app.MapGet("/api/photos/{id}", async (string id, IPhotoStore photos, HttpContext http, CancellationToken ct) =>
        {
            // Dwie niezależne sesje: urzędnik widzi każde zdjęcie, konto mieszkańca tylko własne.
            var official = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var user = await http.AuthenticateAsync(AccountEndpoints.UserScheme);
            var isOfficial = official.Succeeded && official.Principal.IsInRole(ReportEndpoints.OfficialPolicy);
            if (!isOfficial && !user.Succeeded)
                return Results.Unauthorized();

            var photo = ReportEndpoints.IsReportId(id) ? await photos.FindAsync(id, ct) : null;
            if (photo is null || !(isOfficial || photo.OwnerLogin == AccountEndpoints.LoginOf(user.Principal!)))
                return Results.NotFound();

            http.Response.Headers.CacheControl = "private, max-age=86400";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(photo.Content, photo.ContentType);
        }).AddEndpointFilter(ReportEndpoints.DatabaseUnavailableFilter);

        return app;
    }

    /// <summary>Zapisuje zdjęcie ze zgłoszenia i zwraca odnośnik do wpisania w zgłoszenie; null, gdy zdjęcia nie było.</summary>
    internal static async Task<ReportPhoto?> StoreAsync(PhotoAttachment? attachment, string login, IPhotoStore photos, DateTime now, CancellationToken ct)
    {
        if (attachment is null)
            return null;

        var stored = StoredPhoto.Create(attachment, login, now);
        await photos.SaveAsync(stored, ct);
        return stored.ToReportPhoto(attachment.Analysis);
    }
}
