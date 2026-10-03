using System.Security.Claims;
using System.Text;
using Application.Abstractions;
using Domain.Accounts;
using Domain.Businesses;
using Infrastructure.Server;

namespace Web.Endpoints;

public static class BusinessEndpoints
{
    /// <summary>Adres aplikacji wpisywany w kod QR, gdy host stoi za pośrednikiem i nie zna swojego publicznego adresu.</summary>
    public const string PublicBaseUrlSetting = "Certificates:PublicBaseUrl";

    private const int MaxListed = 500;
    private const int MaxCertified = 2000;

    /// <summary>
    /// Konta firmowe: wniosek składa zalogowane konto, zatwierdza go urzędnik. Publicznie widać tylko zatwierdzone
    /// firmy, bez NIP, kontaktu i loginu. Oznaczenia i certyfikat są dostępne dopiero po zatwierdzeniu.
    /// </summary>
    public static IEndpointRouteBuilder MapBusinessEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/businesses", async (string cityId, IBusinessRepository repository, CancellationToken ct) =>
                Results.Ok((await repository.ListAsync(cityId, BusinessStatus.Approved, MaxCertified, ct)).Select(b => b.ToCertified())))
            .AddEndpointFilter(ReportEndpoints.DatabaseUnavailableFilter);

        var business = app.MapGroup("/api/business")
            .AddEndpointFilter(ReportEndpoints.DatabaseUnavailableFilter)
            .RequireAuthorization(AccountEndpoints.UserPolicy);

        business.MapGet("/mine", async (ClaimsPrincipal user, IBusinessRepository repository, CancellationToken ct) =>
            Results.Ok(new MyBusiness((await repository.FindAsync(AccountEndpoints.LoginOf(user), ct))?.ToView())));

        business.MapPost("/application", async (BusinessApplicationDraft draft, ClaimsPrincipal user, IBusinessRepository repository, CancellationToken ct) =>
        {
            var errors = draft.Validate();
            if (errors.Count > 0)
                return Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);

            var login = AccountEndpoints.LoginOf(user);
            if (await repository.IsPlaceTakenAsync(draft.CityId, draft.PlaceId, login, ct))
                return Conflict("To miejsce ma już zatwierdzone konto firmowe.");

            var account = BusinessAccount.Create(draft, login, DateTime.UtcNow);
            return await repository.SubmitAsync(account, ct)
                ? Results.Ok(account.ToView())
                : Conflict("Twoje konto firmowe jest już zatwierdzone.");
        }).RequireRateLimiting(ReportEndpoints.SubmitLimit);

        business.MapPut("/features", async (BusinessFeaturesUpdate update, ClaimsPrincipal user, IBusinessRepository repository, CancellationToken ct) =>
        {
            var errors = update.Validate();
            if (errors.Count > 0)
                return Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);

            var account = await repository.FindAsync(AccountEndpoints.LoginOf(user), ct);
            if (account is not { IsApproved: true })
                return Conflict(NotApproved);

            var updated = account.WithFeatures(update, DateTime.UtcNow);
            return await repository.ReplaceAsync(updated, account.UpdatedAt, ct) == BusinessSaveResult.Saved
                ? Results.Ok(updated.ToView())
                : Conflict("Konto firmowe zmieniło się w międzyczasie. Odśwież stronę i spróbuj ponownie.");
        });

        business.MapGet("/certificate", async (bool? inline, ClaimsPrincipal user, IBusinessRepository repository,
            IConfiguration configuration, HttpContext http, CancellationToken ct) =>
        {
            var account = await repository.FindAsync(AccountEndpoints.LoginOf(user), ct);
            if (account is not { IsApproved: true, CertificateId: not null, CertifiedAt: not null })
                return Conflict(NotApproved);

            var baseUrl = configuration[PublicBaseUrlSetting] is { Length: > 0 } configured
                ? configured
                : $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}";
            var svg = CertificateSvg.Render(new CertificateData(account.BusinessName, account.PlaceName, account.CertificateId,
                account.CertifiedAt.Value, CertificateSvg.PlaceUrl(baseUrl, account.CityId, account.PlaceId)));

            // Plik zawiera nazwę wpisaną przez użytkownika: otwarty wprost w karcie nie może uruchomić żadnego skryptu.
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'";
            http.Response.Headers.CacheControl = "no-store";
            return inline == true
                ? Results.Text(svg, "image/svg+xml", Encoding.UTF8)
                : Results.File(Encoding.UTF8.GetBytes(svg), "image/svg+xml", $"certyfikat-{account.CertificateId}.svg");
        });

        var official = app.MapGroup("/api/official/businesses")
            .AddEndpointFilter(ReportEndpoints.DatabaseUnavailableFilter)
            .RequireAuthorization(ReportEndpoints.OfficialPolicy);

        official.MapGet("/", async (string? cityId, IBusinessRepository repository, CancellationToken ct) =>
            Results.Ok(await repository.ListAsync(cityId, null, MaxListed, ct)));

        official.MapPatch("/{login}", async (string login, BusinessReview review, ClaimsPrincipal user,
            IBusinessRepository repository, CancellationToken ct) =>
        {
            var errors = review.Validate();
            if (errors.Count > 0)
                return Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
            if (!UserCredentials.IsValidLogin(login))
                return Results.NotFound();

            var account = await repository.FindAsync(UserCredentials.NormalizeLogin(login), ct);
            if (account is null)
                return Results.NotFound();
            if (account.TransitionError(review) is { } transition)
                return Conflict(transition);

            var updated = account.Review(review, ReportEndpoints.ProfileOf(user).Login, DateTime.UtcNow);
            return await repository.ReplaceAsync(updated, account.UpdatedAt, ct) switch
            {
                BusinessSaveResult.Saved => Results.Ok(updated),
                BusinessSaveResult.PlaceTaken => Conflict("To miejsce ma już zatwierdzone konto firmowe innego właściciela."),
                _ => Conflict("Wniosek zmienił się w międzyczasie. Odśwież listę i spróbuj ponownie.")
            };
        });

        return app;
    }

    private const string NotApproved = "Konto firmowe nie jest zatwierdzone przez urząd.";

    private static IResult Conflict(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status409Conflict);
}
