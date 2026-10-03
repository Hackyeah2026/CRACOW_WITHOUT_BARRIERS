using System.Security.Claims;
using Application.Abstractions;
using Domain.Accounts;
using Microsoft.AspNetCore.Authentication;

namespace Web.Endpoints;

public static class AccountEndpoints
{
    /// <summary>Osobny schemat i ciasteczko niż dla urzędnika, żeby obie sesje nie nadpisywały się w jednej przeglądarce.</summary>
    public const string UserScheme = "user";
    public const string UserPolicy = "user";
    public const string RegisterLimit = "account-register";

    /// <summary>Konta mieszkańców: sam login i hasło. Konto jest potrzebne do wysyłania zgłoszeń.</summary>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/api/account").AddEndpointFilter(ReportEndpoints.DatabaseUnavailableFilter);

        account.MapPost("/register", async (UserCredentials credentials, IUserDirectory directory, HttpContext http, CancellationToken ct) =>
        {
            var errors = credentials.Validate();
            if (errors.Count > 0)
                return Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);

            var profile = await directory.RegisterAsync(credentials, DateTime.UtcNow, ct);
            if (profile is null)
                return Results.Problem("Ten login jest już zajęty. Wybierz inny.", statusCode: StatusCodes.Status409Conflict);

            await SignInAsync(http, profile);
            return Results.Ok(profile);
        }).RequireRateLimiting(RegisterLimit);

        account.MapPost("/login", async (UserCredentials credentials, IUserDirectory directory, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(credentials.Login) || string.IsNullOrEmpty(credentials.Password)
                || credentials.Password.Length > UserCredentials.MaxPasswordLength)
                return Results.Unauthorized();

            var profile = await directory.VerifyAsync(credentials, ct);
            if (profile is null)
                return Results.Unauthorized();

            await SignInAsync(http, profile);
            return Results.Ok(profile);
        }).RequireRateLimiting(ReportEndpoints.LoginLimit);

        account.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(UserScheme);
            return Results.NoContent();
        });

        account.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new UserProfile(LoginOf(user))))
            .RequireAuthorization(UserPolicy);

        return app;
    }

    /// <summary>Login zalogowanego mieszkańca; tylko w endpointach z polityką <see cref="UserPolicy"/>.</summary>
    internal static string LoginOf(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    private static Task SignInAsync(HttpContext http, UserProfile profile) => http.SignInAsync(UserScheme,
        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, profile.Login)], UserScheme)),
        new AuthenticationProperties { IsPersistent = true });
}
