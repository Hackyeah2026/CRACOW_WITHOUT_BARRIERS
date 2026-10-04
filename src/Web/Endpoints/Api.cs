using Application.Abstractions;

namespace Web.Endpoints;

/// <summary>Wspólne elementy endpointów API: odpowiedzi z błędem, limity i obsługa niedostępnej bazy.</summary>
internal static class Api
{
    /// <summary>Limit wysyłki zgłoszeń, punktów na mapie i wniosków firmowych z jednego adresu IP.</summary>
    public const string SubmitLimit = "report-submit";

    /// <summary>Limit prób logowania (mieszkańca i urzędnika) z jednego adresu IP.</summary>
    public const string LoginLimit = "login";

    /// <summary>Błędy walidacji jako jedna odpowiedź 400 z komunikatem dla użytkownika.</summary>
    public static IResult Invalid(IReadOnlyList<string> errors) => Invalid(string.Join(" ", errors));

    public static IResult Invalid(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status400BadRequest);

    public static IResult Conflict(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status409Conflict);

    /// <summary>Identyfikatory nadawane przez host (zgłoszenie, punkt, zdjęcie, plan) to 32 znaki szesnastkowe (Guid "N").</summary>
    public static bool IsId(string id) => id.Length == 32 && id.All(char.IsAsciiHexDigitLower);

    /// <summary>Brak bazy albo połączenia z nią staje się odpowiedzią 503, a szczegóły trafiają tylko do logów hosta.</summary>
    public static async ValueTask<object?> DatabaseUnavailableFilter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (DatabaseUnavailableException ex)
        {
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Mongo")
                .LogWarning(ex.InnerException, "Baza danych: {Error}", ex.Message);
            return Results.Problem("Baza danych jest chwilowo niedostępna.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
