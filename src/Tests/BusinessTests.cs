using System.Xml.Linq;
using Application;
using Application.Abstractions;
using Application.Businesses;
using Domain.Assessments;
using Domain.Businesses;
using Domain.Needs;
using Domain.Places;
using Infrastructure.Catalog;
using Infrastructure.Mongo;
using Infrastructure.Mongo.Reports;
using Infrastructure.Server;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Tests;

public class BusinessTests
{
    private const string ValidTaxId = "1234563218";
    private static readonly DateTime Now = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddDays(2);

    private static readonly Place Cafe = new("osm-node-7", "krakow", "Cafe Lipa", PlaceCategory.Food, 50.0617, 19.9373, "Lipowa 3", null,
    [
        new AccessibilityFeature(FeatureKey.WheelchairAccess, FeatureState.No, null, "OpenStreetMap", new DateOnly(2026, 10, 1), false),
        new AccessibilityFeature(FeatureKey.NoiseLevel, FeatureState.Yes, 2, "OpenStreetMap", new DateOnly(2026, 10, 1), false)
    ]);

    private static readonly Place Museum = Cafe with { Id = "osm-node-8", Name = "Muzeum", Category = PlaceCategory.Museum };

    private static BusinessApplicationDraft Draft() =>
        new("krakow", Cafe.Id, Cafe.Name, Cafe.Category, Cafe.Lat, Cafe.Lon, "  Kawiarnia Lipa sp. z o.o. ", "123-456-32-18", " biuro@lipa.example ", " Prowadzimy lokal od 2019 r. ");

    private static BusinessAccount Pending() => BusinessAccount.Create(Draft(), "lipa", Now);

    private static BusinessAccount Approved() => Pending().Review(new BusinessReview(BusinessStatus.Approved, null), "jan", Later);

    public BusinessTests() => MongoConventions.Register();

    [Theory]
    [InlineData(ValidTaxId, true)]
    [InlineData("123-456-32-18", true)]
    [InlineData("PL 123 456 32 18", true)]
    [InlineData("1234563219", false)]
    [InlineData("123456321", false)]
    [InlineData("12345632180", false)]
    [InlineData("12345632a8", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Tax_id_needs_ten_digits_and_a_valid_check_digit(string? taxId, bool valid) =>
        Assert.Equal(valid, BusinessApplicationDraft.IsValidTaxId(taxId));

    [Fact]
    public void Application_needs_a_place_a_name_a_tax_id_and_a_contact()
    {
        Assert.Empty(Draft().Validate());
        Assert.NotEmpty((Draft() with { PlaceId = "" }).Validate());
        Assert.NotEmpty((Draft() with { BusinessName = "  " }).Validate());
        Assert.NotEmpty((Draft() with { BusinessName = new string('x', BusinessApplicationDraft.MaxBusinessNameLength + 1) }).Validate());
        Assert.NotEmpty((Draft() with { TaxId = "1234567890" }).Validate());
        Assert.NotEmpty((Draft() with { Contact = "" }).Validate());
        Assert.NotEmpty((Draft() with { Note = new string('x', BusinessApplicationDraft.MaxNoteLength + 1) }).Validate());
        Assert.NotEmpty((Draft() with { Lat = 91 }).Validate());
    }

    [Theory]
    [InlineData(PlaceCategory.Food, true)]
    [InlineData(PlaceCategory.Hotel, true)]
    [InlineData(PlaceCategory.Shop, true)]
    [InlineData(PlaceCategory.Stop, false)]
    [InlineData(PlaceCategory.Bench, false)]
    [InlineData(PlaceCategory.Park, false)]
    public void Business_account_is_only_for_places_with_an_interior(PlaceCategory category, bool eligible) =>
        Assert.Equal(eligible, (Draft() with { Category = category }).Validate().Count == 0);

    [Fact]
    public void New_application_waits_for_a_decision_and_has_no_certificate()
    {
        var account = Pending();

        Assert.Equal("lipa", account.Id);
        Assert.Equal(BusinessStatus.Pending, account.Status);
        Assert.False(account.IsApproved);
        Assert.Null(account.CertificateId);
        Assert.Empty(account.Features);
        Assert.Equal("Kawiarnia Lipa sp. z o.o.", account.BusinessName);
        Assert.Equal(ValidTaxId, account.TaxId);
        Assert.Equal("biuro@lipa.example", account.Contact);
    }

    [Fact]
    public void Approval_issues_a_certificate_and_records_the_official()
    {
        var account = Approved();

        Assert.True(account.IsApproved);
        Assert.Matches("^KBB-2026-[0-9A-F]{8}$", account.CertificateId);
        Assert.Equal(Later, account.CertifiedAt);
        Assert.Equal("jan", account.HandledBy);
        Assert.NotEqual(account.CertificateId, Approved().CertificateId);
    }

    [Fact]
    public void Rejection_and_revocation_need_a_reason_and_pending_is_not_a_decision()
    {
        Assert.Empty(new BusinessReview(BusinessStatus.Approved, null).Validate());
        Assert.NotEmpty(new BusinessReview(BusinessStatus.Pending, null).Validate());
        Assert.NotEmpty(new BusinessReview(BusinessStatus.Rejected, " ").Validate());
        Assert.NotEmpty(new BusinessReview(BusinessStatus.Revoked, null).Validate());
        Assert.Empty(new BusinessReview(BusinessStatus.Rejected, "NIP należy do innej firmy.").Validate());
        Assert.NotEmpty(new BusinessReview(BusinessStatus.Approved, new string('x', BusinessAccount.MaxNoteLength + 1)).Validate());
    }

    [Fact]
    public void Only_an_approved_account_can_be_revoked_and_it_cannot_be_rejected()
    {
        var revoke = new BusinessReview(BusinessStatus.Revoked, "Lokal zamknięty.");
        var reject = new BusinessReview(BusinessStatus.Rejected, "Brak potwierdzenia.");

        Assert.NotNull(Pending().TransitionError(revoke));
        Assert.Null(Pending().TransitionError(reject));
        Assert.Null(Approved().TransitionError(revoke));
        Assert.NotNull(Approved().TransitionError(reject));
    }

    [Fact]
    public void Revoked_account_loses_approval_and_keeps_its_certificate_number_when_approved_again()
    {
        var approved = Approved();
        var revoked = approved.Review(new BusinessReview(BusinessStatus.Revoked, " Lokal zamknięty. "), "ewa", Later.AddDays(1));

        Assert.False(revoked.IsApproved);
        Assert.Equal("Lokal zamknięty.", revoked.OfficialNote);
        Assert.Equal("ewa", revoked.HandledBy);

        var again = revoked.Review(new BusinessReview(BusinessStatus.Approved, null), "jan", Later.AddDays(5));
        Assert.Equal(approved.CertificateId, again.CertificateId);
        Assert.Equal(approved.CertifiedAt, again.CertifiedAt);
        Assert.Null(again.OfficialNote);
    }

    [Fact]
    public void Declared_features_must_come_from_the_list_without_repeats()
    {
        Assert.Empty(new BusinessFeaturesUpdate([new(FeatureKey.Elevator, true), new(FeatureKey.QuietRoom, false)]).Validate());
        Assert.Empty(new BusinessFeaturesUpdate([]).Validate());
        Assert.NotEmpty(new BusinessFeaturesUpdate([new(FeatureKey.NoiseLevel, true)]).Validate());
        Assert.NotEmpty(new BusinessFeaturesUpdate([new(FeatureKey.Elevator, true), new(FeatureKey.Elevator, false)]).Validate());
        Assert.NotEmpty(new BusinessFeaturesUpdate(null!).Validate());
    }

    [Fact]
    public void Owner_view_hides_the_official_and_public_view_hides_tax_id_contact_and_login()
    {
        var account = Approved();

        Assert.DoesNotContain(typeof(BusinessAccountView).GetProperties(), p => p.Name == nameof(BusinessAccount.HandledBy));
        var hidden = new[] { nameof(BusinessAccount.Id), nameof(BusinessAccount.TaxId), nameof(BusinessAccount.Contact), nameof(BusinessAccount.Note), nameof(BusinessAccount.HandledBy), nameof(BusinessAccount.OfficialNote) };
        Assert.DoesNotContain(typeof(CertifiedPlace).GetProperties(), p => hidden.Contains(p.Name));

        var json = System.Text.Json.JsonSerializer.Serialize(account.ToCertified(), Domain.DomainJson.Options);
        Assert.DoesNotContain(ValidTaxId, json);
        Assert.DoesNotContain("biuro@lipa.example", json);
        Assert.Contains(account.CertificateId!, json);
    }

    [Fact]
    public void Declaration_replaces_the_same_feature_adds_new_ones_and_marks_the_place_as_certified()
    {
        var account = Approved().WithFeatures(
            new BusinessFeaturesUpdate([new(FeatureKey.WheelchairAccess, true), new(FeatureKey.AccessibleToilet, false)]), Later.AddDays(3));

        var place = account.ToCertified().ApplyTo(Cafe);

        Assert.Equal(new PlaceCertificate("Kawiarnia Lipa sp. z o.o.", account.CertificateId!, Later), place.Certificate);
        Assert.Equal(3, place.Features.Count);
        Assert.Equal(FeatureState.Yes, place.StateOf(FeatureKey.WheelchairAccess));
        Assert.Equal(FeatureState.No, place.StateOf(FeatureKey.AccessibleToilet));
        Assert.Equal(2, place.ValueOf(FeatureKey.NoiseLevel));
        Assert.Equal("OpenStreetMap", place.Feature(FeatureKey.NoiseLevel)!.Source);

        var declared = place.Feature(FeatureKey.WheelchairAccess)!;
        Assert.Equal(CertifiedPlace.FeatureSource, declared.Source);
        Assert.Equal(DateOnly.FromDateTime(Later.AddDays(3)), declared.CheckedOn);
        Assert.False(declared.IsDemoData);
        Assert.Null(Cafe.Certificate);
    }

    [Fact]
    public void Declared_wheelchair_access_replaces_limited_access_from_the_map()
    {
        var limited = Cafe with
        {
            Features = [new AccessibilityFeature(FeatureKey.WheelchairLimited, FeatureState.Yes, null, "OpenStreetMap", null, false)]
        };

        var declared = Approved().WithFeatures(new BusinessFeaturesUpdate([new(FeatureKey.WheelchairAccess, true)]), Later).ToCertified().ApplyTo(limited);
        var untouched = Approved().WithFeatures(new BusinessFeaturesUpdate([new(FeatureKey.Elevator, true)]), Later).ToCertified().ApplyTo(limited);

        Assert.Equal(FeatureState.Unknown, declared.StateOf(FeatureKey.WheelchairLimited));
        Assert.Equal(FeatureState.Yes, declared.StateOf(FeatureKey.WheelchairAccess));
        Assert.Equal(FeatureState.Yes, untouched.StateOf(FeatureKey.WheelchairLimited));
    }

    [Fact]
    public void Assessment_uses_the_declaration_of_the_business()
    {
        var wheelchair = NeedsProfilePresets.Build([NeedsProfilePresets.ElectricWheelchair]);
        var account = Approved().WithFeatures(new BusinessFeaturesUpdate(
            [new(FeatureKey.WheelchairAccess, true), new(FeatureKey.AccessibleToilet, true)]), Later);

        Assert.Equal(AssessmentStatus.Inaccessible, AssessmentEngine.Assess(wheelchair, Cafe).Status);

        var assessment = AssessmentEngine.Assess(wheelchair, account.ToCertified().ApplyTo(Cafe));
        Assert.NotEqual(AssessmentStatus.Inaccessible, assessment.Status);
        Assert.Contains(assessment.Reasons, r => r.Source == CertifiedPlace.FeatureSource);
    }

    [Fact]
    public void Account_is_stored_with_the_login_as_key_string_enums_and_survives_bson_round_trip()
    {
        var account = Approved().WithFeatures(new BusinessFeaturesUpdate([new(FeatureKey.InductionLoop, true)]), Later);

        var document = account.ToBsonDocument();
        var restored = BsonSerializer.Deserialize<BusinessAccount>(document);

        Assert.Equal("lipa", document["_id"].AsString);
        Assert.Equal("Approved", document["status"].AsString);
        Assert.Equal("Food", document["category"].AsString);
        Assert.Equal("InductionLoop", document["features"][0]["key"].AsString);
        Assert.Equal(account.Features, restored.Features);
        Assert.Equal(account.CertificateId, restored.CertificateId);
        Assert.Equal(account.CertifiedAt, restored.CertifiedAt);
        Assert.Equal(account.UpdatedAt, restored.UpdatedAt);
        Assert.True(restored.IsApproved);
    }

    [Fact]
    public void Certificate_is_well_formed_svg_with_escaped_name_number_address_and_qr_code()
    {
        var url = CertificateSvg.PlaceUrl("https://example.org/", "krakow", "osm-node-7");
        var svg = CertificateSvg.Render(new CertificateData("Bar \"U <Zosi>\" & Syn", "Cafe Lipa", "KBB-2026-0A1B2C3D", Now, url));

        Assert.Equal("https://example.org/miejsca/osm-node-7?miasto=krakow", url);

        var document = XDocument.Parse(svg);
        XNamespace ns = "http://www.w3.org/2000/svg";
        var texts = document.Descendants(ns + "text").Select(t => t.Value).ToList();

        Assert.Contains("Bar \"U <Zosi>\" & Syn", texts);
        Assert.Contains(texts, t => t.Contains("KBB-2026-0A1B2C3D"));
        Assert.Contains(texts, t => t.Contains("03.10.2026"));
        Assert.Contains(url, texts);
        Assert.Contains(texts, t => t.Contains("Cafe Lipa"));

        // Kod QR: zagnieżdżony rysunek z jedną ścieżką; rozmiar w modułach to 21 + 4n plus margines 4 pól z każdej strony.
        var qr = document.Descendants(ns + "svg").Single(e => e.Parent is not null);
        var modules = int.Parse(qr.Attribute("viewBox")!.Value.Split(' ')[2]);
        Assert.Equal(0, (modules - 8 - 21) % 4);
        Assert.StartsWith("M", qr.Element(ns + "path")!.Attribute("d")!.Value);
    }

    [Fact]
    public void Certificate_skips_the_place_line_when_the_business_has_the_same_name()
    {
        var svg = CertificateSvg.Render(new CertificateData("Cafe Lipa", "cafe lipa", "KBB-2026-0A1B2C3D", Now, "https://example.org/miejsca/x"));

        Assert.DoesNotContain("miejsce w aplikacji", svg);
    }

    [Fact]
    public async Task Catalog_adds_certificates_to_matching_places_only()
    {
        var certified = Approved().WithFeatures(new BusinessFeaturesUpdate([new(FeatureKey.Elevator, true)]), Later).ToCertified();
        var catalog = new CertifiedPlaceCatalog(new FakeCatalog(), new FakeBusinessClient([certified]));

        var places = await catalog.GetAsync("krakow", [PlaceCategory.Food, PlaceCategory.Museum], CancellationToken.None);

        Assert.NotNull(places.Single(p => p.Id == Cafe.Id).Certificate);
        Assert.Equal(FeatureState.Yes, places.Single(p => p.Id == Cafe.Id).StateOf(FeatureKey.Elevator));
        Assert.Same(Museum, places.Single(p => p.Id == Museum.Id));
        Assert.NotNull((await catalog.FindAsync("krakow", Cafe.Id, CancellationToken.None))!.Certificate);
        Assert.Same(Museum, await catalog.FindAsync("krakow", Museum.Id, CancellationToken.None));
        Assert.Null(await catalog.FindAsync("krakow", "nie-ma", CancellationToken.None));
    }

    [Fact]
    public async Task Catalog_works_without_the_list_of_businesses()
    {
        var catalog = new CertifiedPlaceCatalog(new FakeCatalog(), new FakeBusinessClient(null));

        var places = await catalog.GetAsync("krakow", [PlaceCategory.Food], CancellationToken.None);

        Assert.Same(Cafe, Assert.Single(places));
        Assert.Same(Cafe, await catalog.FindAsync("krakow", Cafe.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_application_and_invalid_features_are_not_sent()
    {
        var client = new FakeBusinessClient([]);
        var sender = new ServiceCollection().AddApplication().AddSingleton<IBusinessClient>(client).BuildServiceProvider()
            .GetRequiredService<ISender>();

        Assert.True((await sender.Send(new SubmitBusinessApplicationCommand(Draft() with { TaxId = "123" }))).IsFailure);
        Assert.True((await sender.Send(new SaveBusinessFeaturesCommand(new BusinessFeaturesUpdate([new(FeatureKey.Stairs, true)])))).IsFailure);
        Assert.Equal(0, client.Sent);

        Assert.True((await sender.Send(new SubmitBusinessApplicationCommand(Draft()))).IsSuccess);
        Assert.True((await sender.Send(new SaveBusinessFeaturesCommand(new BusinessFeaturesUpdate([new(FeatureKey.Elevator, true)])))).IsSuccess);
        Assert.Equal(2, client.Sent);
    }

    [Fact]
    public async Task Repository_reports_database_unavailable_without_connection_string()
    {
        var repository = new ServiceCollection().AddMongo(new MongoOptions()).AddMongoReports(new OfficialsOptions())
            .AddLogging().BuildServiceProvider().GetRequiredService<IBusinessRepository>();

        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => repository.FindAsync("lipa", CancellationToken.None));
        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => repository.SubmitAsync(Pending(), CancellationToken.None));
    }

    /// <summary>Bez listy udaje host, który nie odpowiada.</summary>
    private sealed class FakeBusinessClient(IReadOnlyList<CertifiedPlace>? certified) : IBusinessClient
    {
        public int Sent { get; private set; }

        public Task<Result<BusinessAccountView>> ApplyAsync(BusinessApplicationDraft draft, CancellationToken ct)
        {
            Sent++;
            return Task.FromResult(Result.Success(BusinessAccount.Create(draft, "lipa", Now).ToView()));
        }

        public Task<Result<MyBusiness>> GetMineAsync(CancellationToken ct) => Task.FromResult(Result.Success(new MyBusiness(null)));

        public Task<Result<BusinessAccountView>> SaveFeaturesAsync(BusinessFeaturesUpdate update, CancellationToken ct)
        {
            Sent++;
            return Task.FromResult(Result.Success(Approved().WithFeatures(update, Later).ToView()));
        }

        public Task<Result<IReadOnlyList<CertifiedPlace>>> GetCertifiedAsync(string cityId, CancellationToken ct) => Task.FromResult(
            certified is null ? Result.Failure<IReadOnlyList<CertifiedPlace>>("Brak połączenia z serwerem.") : Result.Success(certified));
    }

    private sealed class FakeCatalog : IPlaceCatalog
    {
        private static readonly Place[] Places = [Cafe, Museum];

        public Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<City>>([]);

        public Task<IReadOnlyList<PlaceCategoryCount>> GetCategoriesAsync(string cityId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PlaceCategoryCount>>([]);

        public Task<IReadOnlyList<Place>> GetAsync(string cityId, IReadOnlyCollection<PlaceCategory> categories, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Place>>(Places.Where(p => categories.Contains(p.Category)).ToList());

        public Task<Place?> FindAsync(string cityId, string placeId, CancellationToken ct) =>
            Task.FromResult(Places.FirstOrDefault(p => p.Id == placeId));
    }
}
