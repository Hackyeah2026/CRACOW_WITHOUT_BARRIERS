using Domain.Places;
using Infrastructure.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Tests;

public class MongoSetupTests
{
    private static readonly Place Sample = new("osm-node-1", "krakow", "Sukiennice", PlaceCategory.Museum, 50.0617, 19.9373,
        "Rynek Główny 1", null,
        [new AccessibilityFeature(FeatureKey.StepFreeEntrance, FeatureState.Yes, null, "OpenStreetMap", new DateOnly(2026, 10, 1), false)]);

    public MongoSetupTests() => MongoConventions.Register();

    [Fact]
    public void Documents_use_camel_case_names_string_enums_and_place_id_as_key()
    {
        var document = Sample.ToBsonDocument();

        Assert.Equal("osm-node-1", document["_id"].AsString);
        Assert.Equal("Museum", document["category"].AsString);
        Assert.Equal("Sukiennice", document["name"].AsString);
        Assert.Equal("Yes", document["features"][0]["state"].AsString);
    }

    [Fact]
    public void Place_survives_a_round_trip_through_bson()
    {
        var restored = BsonSerializer.Deserialize<Place>(Sample.ToBsonDocument());

        Assert.Equal(Sample.Id, restored.Id);
        Assert.Equal(Sample.Location, restored.Location);
        Assert.Equal(Sample.Features, restored.Features);
    }

    [Fact]
    public async Task Health_reports_not_configured_without_connection_string()
    {
        var services = new ServiceCollection().AddMongo(new MongoOptions()).BuildServiceProvider();

        var result = await services.GetRequiredService<MongoHealth>().CheckAsync(CancellationToken.None);

        Assert.Equal(MongoHealthStatus.NotConfigured, result.Status);
        Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<IMongoClient>());
    }

    [Fact]
    public async Task Health_reports_unreachable_when_server_does_not_answer()
    {
        // Port 1 na localhost: nikt nie nasłuchuje, więc wybór serwera kończy się po czasie.
        var options = new MongoOptions { ConnectionString = "mongodb://127.0.0.1:1", ServerSelectionTimeoutSeconds = 1 };
        var services = new ServiceCollection().AddMongo(options).BuildServiceProvider();

        var result = await services.GetRequiredService<MongoHealth>().CheckAsync(CancellationToken.None);

        Assert.Equal(MongoHealthStatus.Unreachable, result.Status);
        Assert.NotNull(result.Error);
    }
}
