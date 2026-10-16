using Application;
using Application.Abstractions;
using Domain.Assessments;
using Domain.Needs;
using Domain.Places;
using Domain.Reports;
using Infrastructure.Catalog;
using Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Tests;

public class ConfirmedGapTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly NeedsProfile Marta = NeedsProfilePresets.Build([NeedsProfilePresets.ElectricWheelchair]);

    public ConfirmedGapTests() => MongoConventions.Register();

    private static Report Report(ReportKind kind = ReportKind.MissingAmenity, ReportStatus status = ReportStatus.Planned,
        DateTime? confirmedAt = null, params FeatureKey[] features) =>
        Domain.Reports.Report.Create(new ReportDraft("krakow", "osm-node-1", "Muzeum", PlaceCategory.Museum, 50.06, 19.94, kind,
            features.Length == 0 ? [FeatureKey.Elevator] : features, "opis"), Now) with { Status = status, ConfirmedAt = confirmedAt };

    private static Place Museum(params AccessibilityFeature[] features) =>
        new("osm-node-1", "krakow", "Muzeum", PlaceCategory.Museum, 50.06, 19.94, null, null, features);

    private static AccessibilityFeature Osm(FeatureKey key, FeatureState state) => new(key, state, null, "OpenStreetMap", null, false);

    [Fact]
    public void Only_a_report_naming_amenities_can_be_confirmed()
    {
        Assert.True(Report().CanBeConfirmed);
        Assert.True(Report(ReportKind.Barrier).CanBeConfirmed);
        Assert.False(Report(ReportKind.WrongData).CanBeConfirmed);
        Assert.False((Report() with { Features = [] }).CanBeConfirmed);
    }

    [Fact]
    public void Confirmed_gap_affects_the_assessment_until_the_report_is_closed()
    {
        Assert.False(Report().AffectsAssessment);
        Assert.True(Report(confirmedAt: Now).AffectsAssessment);
        Assert.False(Report(status: ReportStatus.Resolved, confirmedAt: Now).AffectsAssessment);
        Assert.False(Report(status: ReportStatus.Rejected, confirmedAt: Now).AffectsAssessment);
    }

    [Fact]
    public void Rejecting_and_confirming_at_once_is_an_error()
    {
        Assert.NotEmpty(new ReportStatusChange(ReportStatus.Rejected, null, Confirmed: true).Validate());
        Assert.Empty(new ReportStatusChange(ReportStatus.Planned, "Winda w budżecie na 2027 r.", Confirmed: true).Validate());
    }

    [Fact]
    public void Public_gap_has_no_report_text_or_logins()
    {
        var properties = typeof(ConfirmedGap).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(nameof(Domain.Reports.Report.Description), properties);
        Assert.DoesNotContain(nameof(Domain.Reports.Report.ReportedBy), properties);
        Assert.DoesNotContain(nameof(Domain.Reports.Report.HandledBy), properties);
    }

    [Fact]
    public void Confirmed_missing_lift_overrides_the_map_and_both_sources_stay_visible()
    {
        var place = Museum(Osm(FeatureKey.WheelchairAccess, FeatureState.Yes), Osm(FeatureKey.Elevator, FeatureState.Yes),
            Osm(FeatureKey.Stairs, FeatureState.Yes));

        var updated = ConfirmedGap.ApplyTo(place, [Report(confirmedAt: Now).ToConfirmedGap()]);

        var lift = updated.Feature(FeatureKey.Elevator)!;
        Assert.Equal((FeatureState.No, ConfirmedGap.Source, new DateOnly(2026, 10, 16)), (lift.State, lift.Source, lift.CheckedOn));
        Assert.Single(updated.ConflictsOn(FeatureKey.Elevator));

        var assessment = AssessmentEngine.Assess(Marta, updated);
        Assert.Equal(AssessmentStatus.Inaccessible, assessment.Status);
        Assert.Contains(assessment.Reasons, r => r.Source == ConfirmedGap.Source && r.Message.Contains("źródła się różnią"));
    }

    [Fact]
    public void Newest_confirmation_wins_and_other_places_are_untouched()
    {
        var older = Report(confirmedAt: Now.AddDays(-5)).ToConfirmedGap();
        var newer = Report(confirmedAt: Now).ToConfirmedGap();
        var elsewhere = Report(confirmedAt: Now).ToConfirmedGap() with { PlaceId = "osm-node-2" };

        var updated = ConfirmedGap.ApplyTo(Museum(), [older, newer, elsewhere]);

        var lift = Assert.Single(updated.Features);
        Assert.Equal(new DateOnly(2026, 10, 16), lift.CheckedOn);
    }

    [Fact]
    public void Confirmation_date_survives_the_database_and_old_documents_read_as_unconfirmed()
    {
        var confirmed = Report(confirmedAt: Now);
        Assert.Equal(Now, BsonSerializer.Deserialize<Report>(confirmed.ToBsonDocument()).ConfirmedAt);

        var old = Report().ToBsonDocument();
        old.Remove("confirmedAt");
        Assert.Null(BsonSerializer.Deserialize<Report>(old).ConfirmedAt);
    }

    [Fact]
    public async Task Catalog_adds_confirmed_gaps_and_works_without_the_host()
    {
        var place = Museum(Osm(FeatureKey.Elevator, FeatureState.Yes));
        var gaps = new FakeReports(Result.Success<IReadOnlyList<ConfirmedGap>>([Report(confirmedAt: Now).ToConfirmedGap()]));
        var down = new FakeReports(Result.Failure<IReadOnlyList<ConfirmedGap>>("Host nie odpowiada."));

        var withGaps = await new ConfirmedGapsPlaceCatalog(new OnePlace(place), gaps).FindAsync("krakow", place.Id, default);
        var listed = await new ConfirmedGapsPlaceCatalog(new OnePlace(place), gaps).GetAsync("krakow", [PlaceCategory.Museum], default);
        var offline = await new ConfirmedGapsPlaceCatalog(new OnePlace(place), down).FindAsync("krakow", place.Id, default);

        Assert.Equal(FeatureState.No, withGaps!.StateOf(FeatureKey.Elevator));
        Assert.Equal(FeatureState.No, Assert.Single(listed).StateOf(FeatureKey.Elevator));
        Assert.Equal(FeatureState.Yes, offline!.StateOf(FeatureKey.Elevator));
    }

    private sealed class FakeReports(Result<IReadOnlyList<ConfirmedGap>> gaps) : IReportsClient
    {
        public Task<Result<ReportReceipt>> SubmitAsync(ReportDraft draft, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<ReportStatusView>>> GetMineAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<ConfirmedGap>>> GetConfirmedAsync(string cityId, CancellationToken ct) => Task.FromResult(gaps);
    }

    private sealed class OnePlace(Place place) : IPlaceCatalog
    {
        public Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<City>>([]);
        public Task<IReadOnlyList<PlaceCategoryCount>> GetCategoriesAsync(string cityId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PlaceCategoryCount>>([]);
        public Task<IReadOnlyList<Place>> GetAsync(string cityId, IReadOnlyCollection<PlaceCategory> categories, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Place>>([place]);
        public Task<Place?> FindAsync(string cityId, string placeId, CancellationToken ct) =>
            Task.FromResult<Place?>(placeId == place.Id ? place : null);
    }
}
