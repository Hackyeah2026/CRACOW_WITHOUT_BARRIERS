using System.Text.Json;
using Domain;
using Domain.Places;
using Domain.Trips;
using Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Tests;

public class RestStopToiletTests
{
    [Fact]
    public void Toilet_along_the_route_is_a_rest_stop_and_says_so()
    {
        GeoPoint[] route = [new(50.0500, 19.9300), new(50.0500, 19.9440)];
        Place[] places = [new("wc", "krakow", "Toaleta publiczna", PlaceCategory.Toilet, 50.0501, 19.9350, null, null, [])];

        var rest = Assert.Single(RestStopRules.AlongRoute(places, route, 1000, maxWithoutRestM: 500));

        Assert.Equal(("wc", PlaceCategory.Toilet), (rest.PlaceId, rest.Kind));
    }

    [Fact]
    public void Rest_stop_saved_before_toilets_reads_as_a_bench()
    {
        var old = JsonSerializer.Deserialize<RestStop>("""{"placeId":"b1","lat":50,"lon":20,"distanceFromStartM":300}""", DomainJson.Options)!;

        Assert.Equal(PlaceCategory.Bench, old.Kind);
    }

    [Fact]
    public void Rest_stop_in_a_plan_saved_in_the_database_before_toilets_reads_as_a_bench()
    {
        MongoConventions.Register();
        var document = new RestStop("b1", 50, 20, 300, PlaceCategory.Toilet).ToBsonDocument();
        document.Remove("category");

        Assert.Equal(PlaceCategory.Bench, BsonSerializer.Deserialize<RestStop>(document).Kind);
    }
}
