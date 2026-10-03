using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Abstractions;
using Domain.Places;

namespace Infrastructure.Server;

public sealed class OpenRouteServiceOptions
{
    public const string Section = "OpenRouteService";

    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://api.openrouteservice.org/";
}

/// <summary>
/// Trasy piesze i wózkowe z OpenRouteService. Jeśli zapytanie z ograniczeniami profilu się nie powiedzie,
/// ponawia je bez ograniczeń i dodaje ostrzeżenie.
/// </summary>
internal sealed class OpenRouteServiceClient(HttpClient http, OpenRouteServiceOptions options) : IRouteProvider
{
    public async Task<Result<RouteResponse>> GetRouteAsync(RouteQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return Result.Failure<RouteResponse>("Brak klucza OpenRouteService w konfiguracji hosta.");

        var profile = query.Wheelchair ? "wheelchair" : "foot-walking";
        var restrictions = OpenRouteServiceRequest.Options(query);

        var first = await SendAsync(profile, query, restrictions, ct);
        if (first.IsSuccess || restrictions is null)
            return first;

        var retry = await SendAsync(profile, query, null, ct);
        return retry.IsFailure
            ? first
            : Result.Success(retry.Value with
            {
                Warnings = [.. retry.Value.Warnings, "Nie udało się uwzględnić wszystkich ograniczeń profilu na tym odcinku. Trasa może zawierać krawężniki lub schody."]
            });
    }

    private async Task<Result<RouteResponse>> SendAsync(string profile, RouteQuery query, JsonObject? routeOptions, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v2/directions/{profile}/geojson")
        {
            Content = JsonContent.Create(OpenRouteServiceRequest.Body(query, routeOptions))
        };
        request.Headers.TryAddWithoutValidation("Authorization", options.ApiKey);

        try
        {
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return response.IsSuccessStatusCode
                ? OpenRouteServiceResponse.Parse(body, query.Wheelchair)
                : Result.Failure<RouteResponse>($"OpenRouteService: {(int)response.StatusCode} {OpenRouteServiceResponse.ErrorMessage(body)}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result.Failure<RouteResponse>($"OpenRouteService nie odpowiada: {ex.Message}");
        }
    }
}

public static class OpenRouteServiceRequest
{
    public static JsonObject Body(RouteQuery query, JsonObject? options)
    {
        var body = new JsonObject
        {
            ["coordinates"] = new JsonArray(
                new JsonArray(query.From.Lon, query.From.Lat),
                new JsonArray(query.To.Lon, query.To.Lat)),
            ["instructions"] = false,
            ["elevation"] = true,
            ["extra_info"] = new JsonArray("steepness")
        };
        if (options is not null)
            body["options"] = options;
        return body;
    }

    /// <summary>Ograniczenia wynikające z profilu potrzeb; null, gdy nie ma żadnych.</summary>
    public static JsonObject? Options(RouteQuery query)
    {
        if (query.Wheelchair)
        {
            if (query.MaxKerbCm is not { } kerb)
                return null;

            // ORS przyjmuje tylko trzy wysokości krawężnika (w metrach).
            var allowed = kerb <= 3 ? 0.03 : kerb <= 6 ? 0.06 : 0.1;
            return new JsonObject
            {
                ["profile_params"] = new JsonObject { ["restrictions"] = new JsonObject { ["maximum_sloped_kerb"] = allowed } }
            };
        }

        return query.AvoidSteps ? new JsonObject { ["avoid_features"] = new JsonArray("steps") } : null;
    }
}

public static class OpenRouteServiceResponse
{
    public static Result<RouteResponse> Parse(string json, bool wheelchair)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var feature = doc.RootElement.GetProperty("features")[0];
            var properties = feature.GetProperty("properties");

            var geometry = feature.GetProperty("geometry").GetProperty("coordinates").EnumerateArray()
                .Select(c => new GeoPoint(c[1].GetDouble(), c[0].GetDouble()))
                .ToList();
            var distance = properties.GetProperty("summary").TryGetProperty("distance", out var d) ? d.GetDouble() : 0;

            return Result.Success(new RouteResponse(distance, geometry, SteepnessWarnings(properties, wheelchair)));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return Result.Failure<RouteResponse>($"Nieoczekiwana odpowiedź OpenRouteService: {ex.Message}");
        }
    }

    public static string ErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var error = doc.RootElement.GetProperty("error");
            return error.ValueKind == JsonValueKind.Object ? error.GetProperty("message").GetString() ?? "" : error.ToString();
        }
        catch (Exception)
        {
            return "";
        }
    }

    // Kategorie stromości ORS: |2| to 4-6%, |3| to 7-11%, |4| i |5| to 12% i więcej.
    private static List<string> SteepnessWarnings(JsonElement properties, bool wheelchair)
    {
        var warnings = new List<string>();
        if (!properties.TryGetProperty("extras", out var extras) || !extras.TryGetProperty("steepness", out var steepness)
            || !steepness.TryGetProperty("summary", out var summary))
            return warnings;

        double moderate = 0, steep = 0;
        foreach (var item in summary.EnumerateArray())
        {
            var category = Math.Abs(item.GetProperty("value").GetDouble());
            var length = item.GetProperty("distance").GetDouble();
            if (category >= 3) steep += length;
            else if (category >= 2) moderate += length;
        }

        if (steep >= 10)
            warnings.Add($"Około {Math.Round(steep / 10) * 10:0} m stromego odcinka (nachylenie 7% lub więcej).");
        if (wheelchair && moderate >= 10)
            warnings.Add($"Około {Math.Round(moderate / 10) * 10:0} m odcinka o nachyleniu 4-6%.");
        return warnings;
    }
}
