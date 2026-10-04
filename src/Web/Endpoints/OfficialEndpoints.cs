using System.Security.Claims;
using Application.Abstractions;
using Domain.Reports;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Web.Endpoints;

public static class OfficialEndpoints
{
    /// <summary>Polityka i rola urzędnika; sesja to domyślny schemat ciasteczka, osobny od sesji mieszkańca.</summary>
    public const string OfficialPolicy = "official";

    private const string UnitClaim = "unit";

    /// <summary>Sesja urzędnika: logowanie, wylogowanie i profil zalogowanego.</summary>
    public static IEndpointRouteBuilder MapOfficialEndpoints(this IEndpointRouteBuilder app)
    {
        var official = app.MapGroup("/api/official").AddEndpointFilter(Api.DatabaseUnavailableFilter);

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
        }).RequireRateLimiting(Api.LoginLimit);

        official.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        official.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(ProfileOf(user)))
            .RequireAuthorization(OfficialPolicy);

        return app;
    }

    /// <summary>Profil zalogowanego urzędnika; tylko w endpointach z polityką <see cref="OfficialPolicy"/>.</summary>
    internal static OfficialProfile ProfileOf(ClaimsPrincipal user) => new(
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
        user.FindFirstValue(ClaimTypes.Name) ?? "",
        user.FindFirstValue(UnitClaim) ?? "");
}
