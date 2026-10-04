using Application.Reports;
using Domain.Hazards;
using Domain.Needs;
using Domain.Places;
using Domain.Reports;
using Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Tests;

public class HazardTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);

    // Odcinek ok. 700 m wzdłuż południka.
    private static readonly GeoPoint[] Route = [new(50.0600, 19.9400), new(50.0663, 19.9400)];

    private static HazardDraft Draft(HazardKind kind = HazardKind.Stairs, string description = "") =>
        new("krakow", 50.0617, 19.9373, kind, description);

    private static VerifiedHazard Verified(string id, HazardKind kind, double lat, double lon) =>
        new(id, kind, lat, lon, "", new DateOnly(2026, 10, 3));

    public HazardTests() => MongoConventions.Register();

    [Fact]
    public void Draft_needs_a_point_and_other_kind_needs_a_description()
    {
        Assert.Empty(Draft().Validate());
        Assert.NotEmpty((Draft() with { Lat = 0, Lon = 0 }).Validate());
        Assert.NotEmpty(Draft(HazardKind.Other).Validate());
        Assert.Empty(Draft(HazardKind.Other, "Zepsuta sygnalizacja dźwiękowa").Validate());
        Assert.NotEmpty(Draft(description: new string('x', HazardDraft.MaxDescriptionLength + 1)).Validate());
        Assert.NotEmpty(Draft((HazardKind)99).Validate());
    }

    [Fact]
    public void New_hazard_waits_for_verification_and_status_view_hides_the_official()
    {
        var hazard = Hazard.Create(Draft(description: "  trzy stopnie  "), Now) with { HandledBy = "jan" };

        Assert.Equal(HazardStatus.Pending, hazard.Status);
        Assert.Equal("trzy stopnie", hazard.Description);
        Assert.DoesNotContain(typeof(HazardStatusView).GetProperties(), p => p.Name == nameof(Hazard.HandledBy));
        Assert.DoesNotContain(typeof(HazardStatusView).GetProperties(), p => p.Name == nameof(Hazard.ReportedBy));
        Assert.DoesNotContain(typeof(VerifiedHazard).GetProperties(),
            p => p.Name is nameof(Hazard.HandledBy) or nameof(Hazard.OfficialNote) or nameof(Hazard.ReportedBy));
    }

    [Fact]
    public void Hazard_is_stored_with_string_enums_and_survives_bson_round_trip()
    {
        var hazard = Hazard.Create(Draft(HazardKind.Noise, "głośny węzeł"), Now);

        var document = hazard.ToBsonDocument();
        var restored = BsonSerializer.Deserialize<Hazard>(document);

        Assert.Equal(hazard.Id, document["_id"].AsString);
        Assert.Equal("Pending", document["status"].AsString);
        Assert.Equal("Noise", document["kind"].AsString);
        Assert.Equal(hazard, restored);
    }

    [Fact]
    public void Only_hazards_inside_the_corridor_are_on_the_route_in_walking_order()
    {
        var far = Verified("far", HazardKind.Stairs, 50.0630, 19.9420);      // ok. 140 m w bok
        var later = Verified("later", HazardKind.Crowd, 50.0650, 19.9402);   // ok. 15 m w bok
        var sooner = Verified("sooner", HazardKind.Stairs, 50.0610, 19.9400);
        var behind = Verified("behind", HazardKind.Stairs, 50.0590, 19.9400); // ok. 110 m przed startem

        var found = HazardRules.AlongRoute([far, later, sooner, behind], Route, NeedsProfile.Empty);

        Assert.Equal(["sooner", "later"], found.Select(h => h.Hazard.Id));
        Assert.InRange(found[1].DistanceM, 10, 20);
        Assert.Equal(0, found[0].DistanceM);
    }

    [Fact]
    public void Hazard_concerns_only_profiles_it_affects()
    {
        var wheelchair = new NeedsProfile { StepFreeRequired = true };
        var sensory = new NeedsProfile { MaxNoiseLevel = 1 };

        Assert.True(HazardRules.Concerns(HazardKind.Stairs, wheelchair));
        Assert.False(HazardRules.Concerns(HazardKind.Stairs, sensory));
        Assert.True(HazardRules.Concerns(HazardKind.Noise, sensory));
        Assert.False(HazardRules.Concerns(HazardKind.Noise, wheelchair));
        Assert.False(HazardRules.Concerns(HazardKind.Stairs, NeedsProfile.Empty));
        Assert.True(HazardRules.Concerns(HazardKind.Roadworks, NeedsProfile.Empty));
    }

    [Fact]
    public void Route_is_blocked_only_by_physical_hazards_on_it_that_concern_the_profile()
    {
        var wheelchair = new NeedsProfile { StepFreeRequired = true };
        var onRoute = Verified("on", HazardKind.Stairs, 50.0630, 19.94005);      // kilka metrów od trasy
        var beside = Verified("beside", HazardKind.Stairs, 50.0640, 19.9404);    // ok. 30 m w bok: tylko ostrzeżenie
        var noise = Verified("noise", HazardKind.Noise, 50.0635, 19.9400);       // obszar, a nie punkt do obejścia
        var atStart = Verified("start", HazardKind.Stairs, 50.0602, 19.9400);    // ok. 20 m od początku odcinka
        var atEnd = Verified("end", HazardKind.HighKerb, 50.0661, 19.9400);

        var blocking = HazardRules.Blocking([onRoute, beside, noise, atStart, atEnd], Route, wheelchair);

        Assert.Equal(["on"], blocking.Select(h => h.Id));
        Assert.Empty(HazardRules.Blocking([onRoute], Route, new NeedsProfile { MaxNoiseLevel = 1 }));
    }

    [Fact]
    public void Analytics_cover_closed_reports_decision_time_and_daily_counts()
    {
        Report Sample(ReportStatus status, int createdDaysAgo, int decidedAfterHours, ReportKind kind = ReportKind.MissingAmenity) =>
            Report.Create(new ReportDraft("krakow", "p", "P", PlaceCategory.Museum, 50, 19, kind, [FeatureKey.Elevator], "opis"),
                Now.AddDays(-createdDaysAgo)) with { Status = status, UpdatedAt = Now.AddDays(-createdDaysAgo).AddHours(decidedAfterHours) };

        IReadOnlyList<Report> reports =
        [
            Sample(ReportStatus.New, 10, 0),
            Sample(ReportStatus.New, 0, 0),
            Sample(ReportStatus.Resolved, 2, 4),
            Sample(ReportStatus.Rejected, 2, 10, ReportKind.Barrier)
        ];
        IReadOnlyList<Hazard> hazards =
        [
            Hazard.Create(Draft(), Now.AddDays(-8)),
            Hazard.Create(Draft(HazardKind.Noise), Now) with { Status = HazardStatus.Verified, UpdatedAt = Now.AddHours(6) }
        ];

        var stats = ReportAnalytics.Analyze(reports, hazards, Now, DateOnly.FromDateTime(Now), utc => utc);

        Assert.Equal(0.5, stats.ClosedShare);
        Assert.Equal(6, stats.MedianHoursToDecision);
        Assert.Equal(2, stats.WaitingOverWeek);
        Assert.Equal(2, stats.ReportsByStatus.Single(c => c.Key == ReportStatus.New).Value);
        Assert.Equal(0, stats.ReportsByStatus.Single(c => c.Key == ReportStatus.Planned).Value);
        Assert.Equal(new Count<FeatureKey>(FeatureKey.Elevator, 3), Assert.Single(stats.MissingFeatures));
        Assert.Equal(1, stats.HazardsByStatus.Single(c => c.Key == HazardStatus.Verified).Value);
        Assert.Equal(ReportAnalytics.DailyWindow, stats.Daily.Count);
        Assert.Equal(new DayCount(DateOnly.FromDateTime(Now), 1, 1), stats.Daily[^1]);
        Assert.Equal(new DayCount(DateOnly.FromDateTime(Now).AddDays(-2), 2, 0), stats.Daily[^3]);
    }

    [Fact]
    public void Analytics_of_nothing_have_no_shares()
    {
        var stats = ReportAnalytics.Analyze([], [], Now, DateOnly.FromDateTime(Now), utc => utc);

        Assert.Null(stats.ClosedShare);
        Assert.Null(stats.MedianHoursToDecision);
        Assert.Equal(0, stats.WaitingOverWeek);
    }
}
