using System.Text.Json;
using Domain;
using Domain.Hazards;
using Domain.Photos;
using Domain.Places;
using Domain.Reports;
using Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Tests;

public class ReportPhotoTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 9, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private static readonly ObstacleAnalysis Analysis = new(0.92, HazardKind.Stairs, "Trzy stopnie przy wejściu.");

    private static ReportDraft ReportDraft(PhotoAttachment? photo) =>
        new("krakow", "osm-node-1", "Sukiennice", PlaceCategory.Museum, 50.0617, 19.9373, ReportKind.Barrier, [], "Stopnie przy wejściu") { Photo = photo };

    private static HazardDraft HazardDraft(PhotoAttachment? photo) =>
        new("krakow", 50.0617, 19.9373, HazardKind.Stairs, "") { Photo = photo };

    public ReportPhotoTests() => MongoConventions.Register();

    [Fact]
    public void Attachment_must_be_a_small_jpeg()
    {
        Assert.Empty(new PhotoAttachment(Jpeg, Analysis).Validate());
        Assert.Empty(new PhotoAttachment(Jpeg, null).Validate());
        Assert.NotEmpty(new PhotoAttachment([], null).Validate());
        Assert.NotEmpty(new PhotoAttachment(null!, null).Validate());
        Assert.NotEmpty(new PhotoAttachment([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], null).Validate());

        var tooBig = new byte[PhotoAttachment.MaxBytes + 1];
        Jpeg.CopyTo(tooBig, 0);
        Assert.NotEmpty(new PhotoAttachment(tooBig, null).Validate());
        Assert.True(PhotoAttachment.MaxBytes < PhotoUpload.MaxBytes / 10);
    }

    [Fact]
    public void Drafts_accept_no_photo_and_reject_an_invalid_one()
    {
        Assert.Empty(ReportDraft(null).Validate());
        Assert.Empty(ReportDraft(new PhotoAttachment(Jpeg, Analysis)).Validate());
        Assert.NotEmpty(ReportDraft(new PhotoAttachment([1, 2, 3], null)).Validate());
        Assert.Empty(HazardDraft(new PhotoAttachment(Jpeg, null)).Validate());
        Assert.NotEmpty(HazardDraft(new PhotoAttachment([1, 2, 3], null)).Validate());
    }

    [Fact]
    public void Draft_with_photo_survives_the_trip_to_the_host_as_json()
    {
        var json = JsonSerializer.Serialize(ReportDraft(new PhotoAttachment(Jpeg, Analysis)), DomainJson.Options);
        var restored = JsonSerializer.Deserialize<ReportDraft>(json, DomainJson.Options)!;

        Assert.Equal(Jpeg, restored.Photo!.Content);
        Assert.Equal(Analysis, restored.Photo.Analysis);
        Assert.DoesNotContain("\"photo\"", JsonSerializer.Serialize(ReportDraft(null), DomainJson.Options));
    }

    [Fact]
    public void Analysis_sent_back_by_the_browser_is_brought_into_range()
    {
        var forged = new ObstacleAnalysis(7, (HazardKind)99, new string('x', 2000)).Sanitized();

        Assert.Equal(1, forged.Probability);
        Assert.Null(forged.SuggestedKind);
        Assert.Equal(ObstacleAnalysis.MaxSummaryLength, forged.Summary.Length);
        Assert.Equal(0, new ObstacleAnalysis(double.NaN, null, null!).Sanitized().Probability);
        Assert.Equal(Analysis, Analysis.Sanitized());
    }

    [Fact]
    public void Stored_photo_belongs_to_the_reporter_and_gives_the_report_a_reference_without_the_file()
    {
        var stored = StoredPhoto.Create(new PhotoAttachment(Jpeg, Analysis), "ania", Now);
        var reference = stored.ToReportPhoto(Analysis);

        Assert.Equal(32, stored.Id.Length);
        Assert.Equal("ania", stored.OwnerLogin);
        Assert.Equal("image/jpeg", stored.ContentType);
        Assert.Equal(new ReportPhoto(stored.Id, Jpeg.Length, Analysis), reference);
        Assert.DoesNotContain(typeof(ReportPhoto).GetProperties(), p => p.PropertyType == typeof(byte[]));
    }

    [Fact]
    public void Photo_is_part_of_the_report_and_of_the_reporter_view()
    {
        var photo = new ReportPhoto("0123456789abcdef0123456789abcdef", Jpeg.Length, Analysis);
        var report = Report.Create(ReportDraft(new PhotoAttachment(Jpeg, Analysis)), Now) with { Photo = photo };
        var hazard = Hazard.Create(HazardDraft(new PhotoAttachment(Jpeg, Analysis)), Now) with { Photo = photo };

        Assert.Equal(photo, report.ToStatusView().Photo);
        Assert.Equal(photo, hazard.ToStatusView().Photo);
        Assert.Null(Report.Create(ReportDraft(null), Now).ToStatusView().Photo);
        // Publiczny widok potwierdzonego punktu nie zawiera zdjęcia: mogą być na nim ludzie i tablice rejestracyjne.
        Assert.DoesNotContain(typeof(VerifiedHazard).GetProperties(), p => p.Name == nameof(Hazard.Photo));
    }

    [Fact]
    public void Report_keeps_the_photo_reference_in_bson_and_old_reports_have_none()
    {
        var photo = new ReportPhoto("0123456789abcdef0123456789abcdef", Jpeg.Length, Analysis);
        var document = (Report.Create(ReportDraft(null), Now) with { Photo = photo }).ToBsonDocument();

        Assert.Equal(photo.Id, document["photo"]["_id"].AsString);
        Assert.Equal("Stairs", document["photo"]["analysis"]["suggestedKind"].AsString);
        Assert.Equal(photo, BsonSerializer.Deserialize<Report>(document).Photo);

        document.Remove("photo");
        Assert.Null(BsonSerializer.Deserialize<Report>(document).Photo);

        var hazard = (Hazard.Create(HazardDraft(null), Now) with { Photo = photo with { Analysis = null } }).ToBsonDocument();
        Assert.Equal(photo with { Analysis = null }, BsonSerializer.Deserialize<Hazard>(hazard).Photo);
    }

    [Fact]
    public void Stored_photo_is_binary_in_bson_and_survives_a_round_trip()
    {
        var stored = StoredPhoto.Create(new PhotoAttachment(Jpeg, null), "ania", Now);

        var document = stored.ToBsonDocument();
        var restored = BsonSerializer.Deserialize<StoredPhoto>(document);

        Assert.Equal(stored.Id, document["_id"].AsString);
        Assert.True(document["content"].IsBsonBinaryData);
        Assert.Equal(Jpeg, restored.Content);
        Assert.Equal("ania", restored.OwnerLogin);
    }
}
