using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions;
using Domain;

namespace Infrastructure.Browser;

/// <summary>Zamienia odpowiedzi hosta na <see cref="Result"/> z komunikatem zrozumiałym dla użytkownika.</summary>
internal static class HostApi
{
    private const string Unauthorized = "Nie jesteś zalogowany albo sesja wygasła.";
    private const string Unavailable = "Baza zgłoszeń jest chwilowo niedostępna. Spróbuj później.";
    private const string NoConnection = "Brak połączenia z serwerem.";

    /// <summary>Żądanie, na które host odpowiada danymi.</summary>
    public static async Task<Result<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send, CancellationToken ct,
        string unauthorized = Unauthorized, string unavailable = Unavailable) where T : notnull
    {
        try
        {
            using var response = await send();
            if (!response.IsSuccessStatusCode)
                return Result.Failure<T>(await ErrorAsync(response, unauthorized, unavailable, ct));

            var value = await response.Content.ReadFromJsonAsync<T>(DomainJson.Options, ct);
            return value is null ? Result.Failure<T>("Pusta odpowiedź serwera.") : Result.Success(value);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result.Failure<T>(NoConnection);
        }
    }

    /// <summary>Żądanie bez danych w odpowiedzi, np. usunięcie albo wylogowanie.</summary>
    /// <param name="missingIsSuccess">Odpowiedź "nie znaleziono" też oznacza cel osiągnięty (np. plan usunięty wcześniej na innym urządzeniu).</param>
    public static async Task<Result> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct,
        string unavailable = Unavailable, bool missingIsSuccess = false)
    {
        try
        {
            using var response = await send();
            return response.IsSuccessStatusCode || (missingIsSuccess && response.StatusCode == HttpStatusCode.NotFound)
                ? Result.Success()
                : Result.Failure(await ErrorAsync(response, Unauthorized, unavailable, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result.Failure(NoConnection);
        }
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response, string unauthorized, string unavailable, CancellationToken ct) =>
        response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => unauthorized,
            HttpStatusCode.Forbidden => "Brak uprawnień.",
            HttpStatusCode.NotFound => "Nie znaleziono.",
            HttpStatusCode.TooManyRequests => "Za dużo prób w krótkim czasie. Spróbuj ponownie za kilka minut.",
            HttpStatusCode.ServiceUnavailable => unavailable,
            HttpStatusCode.BadRequest or HttpStatusCode.Conflict => await ProblemDetailAsync(response, ct) ?? "Nieprawidłowe dane.",
            _ => "Serwer nie przyjął żądania. Spróbuj ponownie."
        };

    private static async Task<string?> ProblemDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<Problem>(DomainJson.Options, ct);
            return problem?.Detail;
        }
        catch (JsonException) { return null; }
    }

    private sealed record Problem(string? Detail);
}
