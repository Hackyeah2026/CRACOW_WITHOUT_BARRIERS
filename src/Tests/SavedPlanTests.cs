using System.Text.Json;
using Application.Trips;
using Domain;
using Domain.Assessments;
using Domain.Hazards;
using Domain.Needs;
using Domain.Places;
using Domain.Transit;
using Domain.Trips;
using Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Tests;

public class SavedPlanTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Place Museum = new("osm-node-1", "krakow", "Sukiennice", PlaceCategory.Museum, 50.0617, 19.9373, null, null,
        [new AccessibilityFeature(FeatureKey.WheelchairAccess, FeatureState.No, null, "OpenStreetMap", new DateOnly(2026, 10, 1), false)]);

    private static readonly Place Castle = new("osm-node-2", "krakow", "Wawel", PlaceCategory.Attraction, 50.0540, 19.9354, null, null, []);

    public SavedPlanTests() => MongoConventions.Register();

    private static SavedPlanDraft Draft(string name = "Sobota na Kazimierzu", params string[] placeIds) => new("krakow", name, placeIds);

    private static TripPlan Route(NeedsProfile profile)
    {
        GeoPoint[] geometry = [Museum.Location, Castle.Location];
        var ride = new TransitRide("8", TransitKind.Tram, "Borek Fałęcki", "Plac Wszystkich Świętych", "Wawel", 553, 556, 1, [553, 563], geometry);
        var advice = new TransitAdvice(TransitStatus.Found, [new TransitJourney(550, 560, 120, 0, 90, [ride])],
            "GTFS ZTP Kraków", new DateOnly(2026, 10, 2), new DateOnly(2027, 1, 31), new DateOnly(2026, 10, 3), 540);
        var hazard = new HazardOnRoute(new VerifiedHazard("h1", HazardKind.Stairs, 50.058, 19.936, "Schody", new DateOnly(2026, 10, 3)), 12, true);
        var leg = new TripLeg(Museum.Id, Castle.Id, 900, 12, geometry, false, ["Odcinek dłuższy niż 500 m."], advice,
            [hazard], [new RestStop("bench-1", 50.057, 19.936, 400)]);
        return new TripPlan("plan-1", AppMode.Sightseeing, new DateTimeOffset(Now),
            [new TripStop(1, Museum, AssessmentEngine.Assess(profile, Museum)), new TripStop(2, Castle, AssessmentEngine.Assess(profile, Castle))],
            [leg]);
    }

    [Fact]
    public void Draft_needs_a_name_and_a_sane_list_of_places()
    {
        Assert.Empty(Draft().Validate());
        Assert.Empty(Draft(placeIds: ["a", "b"]).Validate());
        Assert.NotEmpty(Draft(name: "  ").Validate());
        Assert.NotEmpty(Draft(name: new string('x', SavedPlanDraft.MaxNameLength + 1)).Validate());
        Assert.NotEmpty(Draft(placeIds: ["a", "a"]).Validate());
        Assert.NotEmpty(Draft(placeIds: Enumerable.Range(0, SavedPlanDraft.MaxPlaces + 1).Select(i => $"p{i}").ToArray()).Validate());
        Assert.NotEmpty(new SavedPlanDraft("", "Plan", []).Validate());
        // Żądanie spoza aplikacji bez listy miejsc.
        Assert.NotEmpty(JsonSerializer.Deserialize<SavedPlanDraft>("""{"cityId":"krakow","name":"Plan"}""", DomainJson.Options)!.Validate());
    }

    [Fact]
    public void New_plan_is_a_draft_owned_by_the_account_with_a_trimmed_name()
    {
        var plan = SavedPlan.Create(Draft("  Weekend  ", "a"), "ania", Now);

        Assert.Equal("Weekend", plan.Name);
        Assert.Equal("ania", plan.Owner);
        Assert.Equal(SavedPlanStatus.Draft, plan.Status);
        Assert.False(plan.IsClosed);
        Assert.Null(plan.RouteJson);
        Assert.Equal(32, plan.Id.Length);
    }

    [Fact]
    public void Plan_survives_a_round_trip_through_bson_with_and_without_the_route()
    {
        var closed = SavedPlan.Create(Draft(placeIds: ["a", "b"]), "ania", Now) with { Status = SavedPlanStatus.Closed, RouteJson = "{}" };
        var document = closed.ToBsonDocument();

        Assert.Equal(closed.Id, document["_id"].AsString);
        Assert.Equal("Closed", document["status"].AsString);

        var restored = BsonSerializer.Deserialize<SavedPlan>(document);
        Assert.Equal(closed.PlaceIds, restored.PlaceIds);
        Assert.Equal("{}", restored.RouteJson);
        Assert.True(restored.IsClosed);

        // Lista planów pomija trasę (projekcja bez pola routeJson).
        document.Remove("routeJson");
        Assert.Null(BsonSerializer.Deserialize<SavedPlan>(document).RouteJson);
    }

    [Fact]
    public void Saved_route_does_not_carry_the_assessment_and_gets_it_back_on_the_device()
    {
        var wheelchair = NeedsProfilePresets.Build([NeedsProfilePresets.ElectricWheelchair]);
        var route = Route(wheelchair);
        Assert.Equal(AssessmentStatus.Inaccessible, route.Stops[0].Assessment.Status);

        var json = SavedRoutes.Write(route);
        Assert.DoesNotContain("Inaccessible", json);

        var restored = SavedRoutes.Read(json, wheelchair)!;
        Assert.Equal(AssessmentStatus.Inaccessible, restored.Stops[0].Assessment.Status);
        Assert.Equal(route.Stops.Select(s => s.Place.Id), restored.Stops.Select(s => s.Place.Id));

        var (leg, saved) = (route.Legs[0], restored.Legs[0]);
        Assert.Equal(leg.DistanceM, saved.DistanceM);
        Assert.Equal(leg.Geometry, saved.Geometry);
        Assert.Equal(leg.Warnings, saved.Warnings);
        Assert.Equal(leg.Hazards, saved.Hazards);
        Assert.Equal(leg.RestStops, saved.RestStops);
        Assert.Equal("8", saved.Transit!.Journeys[0].Rides[0].LineName);
        Assert.Equal(TransitStatus.Found, saved.Transit.Status);

        // Ta sama trasa otwarta z innym profilem dostaje ocenę dla tego profilu.
        Assert.NotEqual(AssessmentStatus.Inaccessible, SavedRoutes.Read(json, NeedsProfile.Empty)!.Stops[0].Assessment.Status);
    }

    [Fact]
    public void Saved_route_keeps_the_detour_around_a_verified_hazard()
    {
        var route = Route(NeedsProfile.Empty);
        var stairs = new VerifiedHazard("h1", HazardKind.Stairs, 50.057, 19.938, "trzy stopnie", new DateOnly(2026, 10, 3));
        var detour = new RouteDetour([stairs], 104.5, [new GeoPoint(50.061, 19.937), new GeoPoint(50.054, 19.935)]);
        route = route with { Legs = [route.Legs[0] with { Detour = detour }, .. route.Legs.Skip(1)] };

        var saved = SavedRoutes.Read(SavedRoutes.Write(route), NeedsProfile.Empty)!.Legs[0].Detour!;

        Assert.Equal(detour.Avoided, saved.Avoided);
        Assert.Equal(detour.ExtraDistanceM, saved.ExtraDistanceM);
        Assert.Equal(detour.DirectGeometry, saved.DirectGeometry);
    }

    [Fact]
    public void Broken_or_missing_route_is_not_read()
    {
        Assert.Null(SavedRoutes.Read(null, NeedsProfile.Empty));
        Assert.Null(SavedRoutes.Read("", NeedsProfile.Empty));
        Assert.Null(SavedRoutes.Read("{not json", NeedsProfile.Empty));
        Assert.Null(SavedRoutes.Read("{}", NeedsProfile.Empty));
    }
}
