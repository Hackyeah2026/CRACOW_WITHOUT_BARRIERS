using Application;
using Application.Abstractions;
using Application.Reports;
using Domain.Accounts;
using Domain.Places;
using Domain.Reports;
using System.Text.Json;
using Domain;
using Domain.Hazards;
using Infrastructure.Mongo;
using Infrastructure.Mongo.Accounts;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MediatR;
using MongoDB.Bson.Serialization;

namespace Tests;

public class ReportTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);

    private static ReportDraft Draft(ReportKind kind = ReportKind.MissingAmenity, IReadOnlyList<FeatureKey>? features = null, string description = "") =>
        new("krakow", "osm-node-1", "Sukiennice", PlaceCategory.Museum, 50.0617, 19.9373, kind,
            features ?? [FeatureKey.Elevator], description);

    private static Report Sample(string placeId, ReportStatus status, params FeatureKey[] features) =>
        Report.Create(Draft(features: features) with { PlaceId = placeId, PlaceName = placeId }, Now) with { Status = status };

    public ReportTests() => MongoConventions.Register();

    [Fact]
    public void Missing_amenity_needs_at_least_one_feature()
    {
        Assert.Empty(Draft().Validate());
        Assert.NotEmpty(Draft(features: []).Validate());
    }

    [Fact]
    public void Barrier_needs_a_description_and_limits_its_length()
    {
        Assert.NotEmpty(Draft(ReportKind.Barrier, [], "  ").Validate());
        Assert.Empty(Draft(ReportKind.Barrier, [], "Trzy stopnie przy wejściu").Validate());
        Assert.NotEmpty(Draft(ReportKind.Barrier, [], new string('x', ReportDraft.MaxDescriptionLength + 1)).Validate());
    }

    [Fact]
    public void Duplicate_features_and_missing_place_are_rejected()
    {
        Assert.NotEmpty(Draft(features: [FeatureKey.Elevator, FeatureKey.Elevator]).Validate());
        Assert.NotEmpty((Draft() with { PlaceId = "" }).Validate());
    }

    [Fact]
    public void Request_with_missing_fields_or_unknown_values_is_invalid_instead_of_crashing_the_host()
    {
        // Tak wygląda żądanie spoza aplikacji: bez listy udogodnień i bez opisu.
        var bare = JsonSerializer.Deserialize<ReportDraft>(
            """{"cityId":"krakow","placeId":"osm-node-1","placeName":"Sukiennice","category":"Museum","lat":50.06,"lon":19.93,"kind":"Barrier"}""",
            DomainJson.Options)!;

        Assert.NotEmpty(bare.Validate());
        Assert.NotEmpty((Draft() with { Kind = (ReportKind)99 }).Validate());
        Assert.NotEmpty((Draft() with { Category = (PlaceCategory)99 }).Validate());
        Assert.NotEmpty(Draft(features: [(FeatureKey)99]).Validate());
        Assert.NotEmpty(new ReportStatusChange((ReportStatus)99, null).Validate());
        Assert.Empty(new ReportStatusChange(ReportStatus.Planned, "Remont w 2027 r.").Validate());
    }

    [Fact]
    public void Description_is_optional_in_requests_from_outside_the_app()
    {
        var report = JsonSerializer.Deserialize<ReportDraft>(
            """{"cityId":"krakow","placeId":"osm-node-1","placeName":"Sukiennice","category":"Museum","lat":50.06,"lon":19.93,"kind":"MissingAmenity","features":["Elevator"]}""",
            DomainJson.Options)!;
        var hazard = JsonSerializer.Deserialize<HazardDraft>(
            """{"cityId":"krakow","lat":50.06,"lon":19.93,"kind":"Stairs"}""", DomainJson.Options)!;

        Assert.Empty(report.Validate());
        Assert.Empty(hazard.Validate());
        Assert.Equal("", Report.Create(report, Now).Description);
        Assert.Equal("", Hazard.Create(hazard, Now).Description);
    }

    [Fact]
    public void My_reports_summary_counts_places_and_map_points_by_stage()
    {
        ReportStatusView Place(ReportStatus status, string? note = null) =>
            new("r", "p", "Miejsce", ReportKind.Barrier, [], status, Now, Now, note);
        HazardStatusView Point(HazardStatus status, string? note = null) =>
            new("h", HazardKind.Stairs, 50.06, 19.93, "", status, Now, Now, note);

        var summary = MyReportsSummary.From(
            [Place(ReportStatus.New), Place(ReportStatus.InReview), Place(ReportStatus.Planned, "W planie"), Place(ReportStatus.Resolved), Place(ReportStatus.Rejected, "Nie nasz teren")],
            [Point(HazardStatus.Pending), Point(HazardStatus.Verified), Point(HazardStatus.Removed, "Naprawione")]);

        Assert.Equal(new MyReportsSummary(Total: 8, Waiting: 2, InProgress: 2, Done: 3, Answered: 3), summary);
    }

    [Fact]
    public void New_report_starts_as_new_and_status_view_hides_the_official()
    {
        var report = Report.Create(Draft(description: "  winda nie działa  "), Now) with { HandledBy = "jan" };

        Assert.Equal(ReportStatus.New, report.Status);
        Assert.Equal(32, report.Id.Length);
        Assert.Equal("winda nie działa", report.Description);
        Assert.DoesNotContain(typeof(ReportStatusView).GetProperties(), p => p.Name == nameof(Report.HandledBy));
    }

    [Fact]
    public void Report_is_stored_with_string_enums_and_survives_bson_round_trip()
    {
        var report = Report.Create(Draft(features: [FeatureKey.Elevator, FeatureKey.InductionLoop]), Now);

        var document = report.ToBsonDocument();
        var restored = BsonSerializer.Deserialize<Report>(document);

        Assert.Equal(report.Id, document["_id"].AsString);
        Assert.Equal("New", document["status"].AsString);
        Assert.Equal("InductionLoop", document["features"][1].AsString);
        Assert.Equal(report.Features, restored.Features);
        Assert.Equal(report.CreatedAt, restored.CreatedAt);
    }

    [Fact]
    public void Password_hash_verifies_only_the_right_password()
    {
        var hash = PasswordHashing.Hash("tajne-haslo");

        Assert.True(PasswordHashing.Verify("tajne-haslo", hash));
        Assert.False(PasswordHashing.Verify("inne-haslo", hash));
        Assert.False(PasswordHashing.Verify("tajne-haslo", "zepsuty$zapis"));
        Assert.NotEqual(hash, PasswordHashing.Hash("tajne-haslo"));

        // Konto odczytane z bazy: brak konta nigdy nie daje dostępu, niezależnie od hasła.
        Assert.True(PasswordHashing.VerifyAccount("tajne-haslo", hash));
        Assert.False(PasswordHashing.VerifyAccount("inne-haslo", hash));
        Assert.False(PasswordHashing.VerifyAccount("tajne-haslo", null));
    }

    [Fact]
    public void Summary_counts_only_open_reports()
    {
        IReadOnlyList<Report> reports =
        [
            Sample("rynek", ReportStatus.New, FeatureKey.Elevator, FeatureKey.AccessibleToilet),
            Sample("rynek", ReportStatus.InReview, FeatureKey.Elevator),
            Sample("wawel", ReportStatus.Planned, FeatureKey.InductionLoop),
            Sample("wawel", ReportStatus.Resolved, FeatureKey.InductionLoop),
            Sample("wawel", ReportStatus.Rejected, FeatureKey.InductionLoop)
        ];

        var summary = ReportStatistics.Summarize(reports);

        Assert.Equal(3, summary.Open);
        Assert.Equal(5, summary.Total);
        Assert.Equal(new FeatureCount(FeatureKey.Elevator, 2), summary.TopFeatures[0]);
        Assert.Equal("rynek", summary.TopPlaces[0].PlaceId);
        Assert.Equal(2, summary.TopPlaces[0].Count);
        Assert.Equal(1, summary.TopPlaces[1].Count);
    }

    [Fact]
    public async Task Repository_reports_database_unavailable_without_connection_string()
    {
        var services = new ServiceCollection().AddMongo(new MongoOptions()).AddMongoRepositories(new OfficialsOptions())
            .AddLogging().BuildServiceProvider();

        var repository = services.GetRequiredService<IReportRepository>();
        var officials = services.GetRequiredService<IOfficialDirectory>();

        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => repository.AddAsync(Sample("rynek", ReportStatus.New), CancellationToken.None));
        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => officials.VerifyAsync(new OfficialLogin("jan", "x"), CancellationToken.None));
        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => services.GetRequiredService<IUserDirectory>()
            .RegisterAsync(new UserCredentials("ania", "haslo-123"), Now, CancellationToken.None));
    }

    [Fact]
    public async Task Valid_report_is_sent()
    {
        var client = new FakeReportsClient();

        var result = await Send(new SubmitReportCommand(Draft()), client);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, client.Submitted);
    }

    [Fact]
    public void Reporter_is_stored_but_hidden_from_the_status_view_and_old_reports_have_none()
    {
        var report = Report.Create(Draft(), Now) with { ReportedBy = "ania" };

        var document = report.ToBsonDocument();
        Assert.Equal("ania", document["reportedBy"].AsString);
        Assert.Equal("ania", BsonSerializer.Deserialize<Report>(document).ReportedBy);
        Assert.DoesNotContain(typeof(ReportStatusView).GetProperties(), p => p.Name == nameof(Report.ReportedBy));

        document.Remove("reportedBy");
        Assert.Null(BsonSerializer.Deserialize<Report>(document).ReportedBy);
    }

    [Fact]
    public void Registration_requires_a_simple_login_and_a_password_of_eight_characters()
    {
        Assert.Empty(new UserCredentials("Ania_K", "haslo-123").Validate());
        Assert.Equal("ania_k", UserCredentials.NormalizeLogin("  Ania_K "));
        Assert.NotEmpty(new UserCredentials("an", "haslo-123").Validate());
        Assert.NotEmpty(new UserCredentials("ania kowalska", "haslo-123").Validate());
        Assert.NotEmpty(new UserCredentials("ania", "krotkie").Validate());
        Assert.NotEmpty(new UserCredentials(null!, null!).Validate());
    }

    [Fact]
    public async Task Invalid_report_is_not_sent()
    {
        var client = new FakeReportsClient();

        var result = await Send(new SubmitReportCommand(Draft(features: [])), client);

        Assert.True(result.IsFailure);
        Assert.Equal(0, client.Submitted);
    }

    private static Task<Result<ReportReceipt>> Send(SubmitReportCommand command, IReportsClient client) =>
        new ServiceCollection().AddApplication().AddSingleton(client).BuildServiceProvider()
            .GetRequiredService<ISender>().Send(command);

    private sealed class FakeReportsClient : IReportsClient
    {
        public int Submitted { get; private set; }

        public Task<Result<ReportReceipt>> SubmitAsync(ReportDraft draft, CancellationToken ct)
        {
            Submitted++;
            return Task.FromResult(Result.Success(Report.Create(draft, Now).ToReceipt()));
        }

        public Task<Result<IReadOnlyList<ReportStatusView>>> GetMineAsync(CancellationToken ct) =>
            Task.FromResult(Result.Success<IReadOnlyList<ReportStatusView>>([]));
    }

}
