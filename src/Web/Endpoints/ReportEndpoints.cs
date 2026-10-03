using System.Security.Claims;
using Application.Abstractions;
using Domain.Reports;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Web.Endpoints;

public static class ReportEndpoints
{
    public const string OfficialPolicy = "official";
    public const string SubmitLimit = "report-submit";
    public const string LoginLimit = "official-login";

    private const string UnitClaim = "unit";
    private const int MaxStatusIds = 100;
    private const int MaxListed = 500;

    /// <summary>
    /// Zgłoszenia mieszkańców (anonimowe, bez danych osobowych) i panel urzędnika (logowanie ciasteczkiem hosta).
    /// </summary>
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/api/reports").AddEndpointFilter(DatabaseUnavailableFilter);

        reports.MapPost("/", async (ReportDraft draft, IReportRepository repository, CancellationToken ct) =>
        {
            var errors = draft.Validate();
            if (errors.Count > 0)
                return Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);

            var report = Report.Create(draft, DateTime.UtcNow);
            await repository.AddAsync(report, ct);
            return Results.Created($"/api/reports/{report.Id}", report.ToReceipt());
        }).RequireRateLimiting(SubmitLimit);

        // Identyfikatory w treści żądania, nie w adresie: nie trafiają do logów serwerów pośrednich.
        reports.MapPost("/status", async (StatusRequest request, IReportRepository repository, CancellationToken ct) =>
        {
            var ids = request.Ids.Where(IsReportId).Distinct().Take(MaxStatusIds).ToList();
            var found = ids.Count == 0 ? [] : await repository.GetByIdsAsync(ids, ct);
            return Results.Ok(found.Select(r => r.ToStatusView()));
        });

        var official = app.MapGroup("/api/official").AddEndpointFilter(DatabaseUnavailableFilter);

        official.MapPost("/login", async (OfficialLogin login, IOfficialDirectory directory, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(login.Login) || string.IsNullOrEmpty(login.Password) || login.Password.Length > 200)
                return Results.Unauthorized();

            var profile = await directory.VerifyAsync(login, ct);
            if (profile is null)
                return Results.Unauthorized();

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, profile.Login),
                new Claim(ClaimTypes.Name, profile.DisplayName),
                new Claim(UnitClaim, profile.Unit),
                new Claim(ClaimTypes.Role, OfficialPolicy)
            ], CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Ok(profile);
        }).RequireRateLimiting(LoginLimit);

        official.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        official.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(ProfileOf(user)))
            .RequireAuthorization(OfficialPolicy);

        official.MapGet("/reports", async (string? cityId, ReportStatus? status, string? placeId,
                IReportRepository repository, CancellationToken ct) =>
            Results.Ok(await repository.ListAsync(new ReportFilter(cityId, status, placeId), MaxListed, ct)))
            .RequireAuthorization(OfficialPolicy);

        official.MapPatch("/reports/{id}", async (string id, ReportStatusChange change, ClaimsPrincipal user,
            IReportRepository repository, CancellationToken ct) =>
        {
            var errors = change.Validate();
            if (errors.Count > 0)
                return Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
            if (!IsReportId(id))
                return Results.NotFound();

            var updated = await repository.UpdateStatusAsync(id, change, ProfileOf(user).Login, DateTime.UtcNow, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }).RequireAuthorization(OfficialPolicy);

        return app;
    }

    internal static OfficialProfile ProfileOf(ClaimsPrincipal user) => new(
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
        user.FindFirstValue(ClaimTypes.Name) ?? "",
        user.FindFirstValue(UnitClaim) ?? "");

    /// <summary>Identyfikator zgłoszenia i punktu na mapie to 32 znaki szesnastkowe (Guid "N").</summary>
    internal static bool IsReportId(string id) => id.Length == 32 && id.All(char.IsAsciiHexDigitLower);

    internal static async ValueTask<object?> DatabaseUnavailableFilter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (DatabaseUnavailableException ex)
        {
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Mongo")
                .LogWarning(ex.InnerException, "Zgłoszenia: {Error}", ex.Message);
            return Results.Problem("Baza zgłoszeń jest chwilowo niedostępna.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private sealed record StatusRequest(IReadOnlyList<string> Ids);
}
