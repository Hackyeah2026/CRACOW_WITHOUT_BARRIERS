using Application.Abstractions;
using Domain.Places;
using Infrastructure.Server;

namespace Tests;

public class OpenRouteServiceTests
{
    private static readonly GeoPoint From = new(50.0617, 19.9373);
    private static readonly GeoPoint To = new(50.0540, 19.9355);

    private const string Sample = """
        {
          "type": "FeatureCollection",
          "features": [{
            "type": "Feature",
            "properties": {
              "summary": { "distance": 1042.5, "duration": 750.6 },
              "extras": { "steepness": { "summary": [
                { "value": 0, "distance": 900.0, "amount": 86.3 },
                { "value": -2, "distance": 80.0, "amount": 7.7 },
                { "value": 3, "distance": 62.5, "amount": 6.0 }
              ] } }
            },
            "geometry": { "type": "LineString", "coordinates": [[19.9373, 50.0617, 212.0], [19.9360, 50.0580, 210.5], [19.9355, 50.0540, 225.0]] }
          }]
        }
        """;

    [Fact]
    public void Parse_reads_distance_and_geometry_in_lat_lon_order()
    {
        var result = OpenRouteServiceResponse.Parse(Sample, wheelchair: false);

        Assert.True(result.IsSuccess);
        Assert.Equal(1042.5, result.Value.DistanceM);
        Assert.Equal(3, result.Value.Geometry.Count);
        Assert.Equal(new GeoPoint(50.0617, 19.9373), result.Value.Geometry[0]);
    }

    [Fact]
    public void Parse_warns_about_steep_sections_and_moderate_ones_only_for_wheelchair()
    {
        var walking = OpenRouteServiceResponse.Parse(Sample, wheelchair: false).Value.Warnings;
        var wheelchair = OpenRouteServiceResponse.Parse(Sample, wheelchair: true).Value.Warnings;

        Assert.Single(walking);
        Assert.Contains("7%", walking[0]);
        Assert.Equal(2, wheelchair.Count);
    }

    [Fact]
    public void Parse_fails_cleanly_on_unexpected_response()
    {
        Assert.True(OpenRouteServiceResponse.Parse("""{ "features": [] }""", false).IsFailure);
        Assert.True(OpenRouteServiceResponse.Parse("not json", false).IsFailure);
    }

    [Fact]
    public void Wheelchair_kerb_limit_maps_to_allowed_values()
    {
        var options = OpenRouteServiceRequest.Options(new RouteQuery(From, To, Wheelchair: true, AvoidSteps: true, MaxKerbCm: 3));

        Assert.Equal(0.03, (double)options!["profile_params"]!["restrictions"]!["maximum_sloped_kerb"]!);
    }

    [Fact]
    public void Walking_profile_avoids_steps_only_when_asked()
    {
        Assert.Null(OpenRouteServiceRequest.Options(new RouteQuery(From, To, false, false, null)));
        Assert.Equal("steps", (string)OpenRouteServiceRequest.Options(new RouteQuery(From, To, false, true, null))!["avoid_features"]![0]!);
    }

    [Fact]
    public void Body_sends_coordinates_as_lon_lat()
    {
        var body = OpenRouteServiceRequest.Body(new RouteQuery(From, To, false, false, null), null);

        Assert.Equal(19.9373, (double)body["coordinates"]![0]![0]!);
        Assert.Equal(50.0617, (double)body["coordinates"]![0]![1]!);
    }
}
