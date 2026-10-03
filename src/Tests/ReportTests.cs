using Application;
using Application.Abstractions;
using Application.Reports;
using Domain.Places;
using Domain.Reports;
using Infrastructure.Mongo;
using Infrastructure.Mongo.Reports;
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
        var services = new ServiceCollection().AddMongo(new MongoOptions()).AddMongoReports(new OfficialsOptions())
            .AddLogging().BuildServiceProvider();

        var repository = services.GetRequiredService<IReportRepository>();
        var officials = services.GetRequiredService<IOfficialDirectory>();

        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => repository.AddAsync(Sample("rynek", ReportStatus.New), CancellationToken.None));
        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => officials.VerifyAsync(new OfficialLogin("jan", "x"), CancellationToken.None));
    }

    [Fact]
    public async Task Submitting_stores_the_receipt_on_the_device()
    {
        var store = new MemoryStore();

        var result = await Send(new SubmitReportCommand(Draft()), new FakeReportsClient(), store);

        Assert.True(result.IsSuccess);
        Assert.Single(await store.ListAsync<ReportReceipt>(LocalStores.Reports));
    }

    [Fact]
    public async Task Invalid_report_is_not_sent()
    {
        var client = new FakeReportsClient();

        var result = await Send(new SubmitReportCommand(Draft(features: [])), client, new MemoryStore());

        Assert.True(result.IsFailure);
        Assert.Equal(0, client.Submitted);
    }

    private static Task<Result<ReportReceipt>> Send(SubmitReportCommand command, IReportsClient client, ILocalStore store) =>
        new ServiceCollection().AddApplication().AddSingleton(client).AddSingleton(store).BuildServiceProvider()
            .GetRequiredService<ISender>().Send(command);

    private sealed class FakeReportsClient : IReportsClient
    {
        public int Submitted { get; private set; }

        public Task<Result<ReportReceipt>> SubmitAsync(ReportDraft draft, CancellationToken ct)
        {
            Submitted++;
            return Task.FromResult(Result.Success(Report.Create(draft, Now).ToReceipt()));
        }

        public Task<Result<IReadOnlyList<ReportStatusView>>> GetStatusesAsync(IReadOnlyList<string> ids, CancellationToken ct) =>
            Task.FromResult(Result.Success<IReadOnlyList<ReportStatusView>>([]));
    }

    private sealed class MemoryStore : ILocalStore
    {
        private readonly Dictionary<(string, string), object> _items = [];

        public Task<T?> GetAsync<T>(string store, string key) => Task.FromResult(_items.TryGetValue((store, key), out var v) ? (T?)v : default);

        public Task PutAsync<T>(string store, string key, T value)
        {
            _items[(store, key)] = value!;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string store, string key)
        {
            _items.Remove((store, key));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<T>> ListAsync<T>(string store) =>
            Task.FromResult<IReadOnlyList<T>>(_items.Where(i => i.Key.Item1 == store).Select(i => (T)i.Value).ToList());
    }
}
