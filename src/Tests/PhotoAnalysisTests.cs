using System.Text.Json;
using Domain.Hazards;
using Domain.Photos;
using Infrastructure.Server;

namespace Tests;

public class PhotoAnalysisTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private static string Completion(string content) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { role = "assistant", content, refusal = (string?)null } } }
    });

    [Fact]
    public void Parse_reads_probability_kind_and_summary()
    {
        var result = OpenAiObstacleResponse.Parse(Completion(
            """{"obstacle_probability":0.92,"obstacle_kind":"Stairs","summary":"Trzy stopnie przy wejściu, brak podjazdu."}"""));

        Assert.True(result.IsSuccess);
        Assert.Equal(0.92, result.Value.Probability);
        Assert.Equal(HazardKind.Stairs, result.Value.SuggestedKind);
        Assert.Equal(ObstacleVerdict.Likely, result.Value.Verdict);
        Assert.Equal(92, result.Value.Percent);
    }

    [Fact]
    public void Parse_maps_none_to_no_kind_and_clamps_probability()
    {
        var result = OpenAiObstacleResponse.Parse(Completion(
            """{"obstacle_probability":-0.2,"obstacle_kind":"None","summary":"Równy chodnik."}"""));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.SuggestedKind);
        Assert.Equal(0, result.Value.Probability);
        Assert.Equal(ObstacleVerdict.Unlikely, result.Value.Verdict);
    }

    [Fact]
    public void Parse_shortens_long_summary()
    {
        var summary = new string('a', 500);
        var result = OpenAiObstacleResponse.Parse(Completion(
            $$"""{"obstacle_probability":0.5,"obstacle_kind":"Other","summary":"{{summary}}"}"""));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Summary.Length <= ObstacleAnalysis.MaxSummaryLength + 1);
        Assert.Equal(ObstacleVerdict.Uncertain, result.Value.Verdict);
    }

    [Fact]
    public void Parse_reports_refusal_and_malformed_answer()
    {
        var refusal = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content = (string?)null, refusal = "Nie mogę pomóc." } } }
        });

        Assert.True(OpenAiObstacleResponse.Parse(refusal).IsFailure);
        Assert.True(OpenAiObstacleResponse.Parse(Completion("to nie jest JSON")).IsFailure);
        Assert.True(OpenAiObstacleResponse.Parse("""{"choices":[]}""").IsFailure);
    }

    [Fact]
    public void ErrorMessage_reads_openai_error()
    {
        Assert.Equal("Incorrect API key provided.",
            OpenAiObstacleResponse.ErrorMessage("""{"error":{"message":"Incorrect API key provided.","type":"invalid_request_error"}}"""));
        Assert.Equal("", OpenAiObstacleResponse.ErrorMessage("<html>"));
    }

    [Fact]
    public void Request_sends_image_as_data_url_with_strict_schema()
    {
        var body = OpenAiObstacleRequest.Body("gpt-test", new PhotoUpload(Jpeg, "image/jpeg"));

        Assert.Equal("gpt-test", (string?)body["model"]);
        var image = body["messages"]![1]!["content"]![1]!["image_url"]!["url"]!.GetValue<string>();
        Assert.StartsWith("data:image/jpeg;base64,", image);

        var schema = body["response_format"]!["json_schema"]!;
        Assert.True((bool)schema["strict"]!);
        var kinds = schema["schema"]!["properties"]!["obstacle_kind"]!["enum"]!.AsArray().Select(k => (string?)k).ToList();
        Assert.Contains(OpenAiObstacleRequest.NoObstacle, kinds);
        Assert.All(Enum.GetNames<HazardKind>(), name => Assert.Contains(name, kinds));
    }

    [Fact]
    public void Photo_validation_checks_type_signature_and_size()
    {
        Assert.Empty(new PhotoUpload(Jpeg, "image/jpeg").Validate());
        Assert.Empty(new PhotoUpload([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0], "image/png").Validate());
        Assert.Empty(new PhotoUpload("RIFF\0\0\0\0WEBPVP8 "u8.ToArray(), "image/webp").Validate());

        Assert.NotEmpty(new PhotoUpload([], "image/jpeg").Validate());
        Assert.NotEmpty(new PhotoUpload(Jpeg, "image/gif").Validate());
        Assert.NotEmpty(new PhotoUpload("<svg></svg>"u8.ToArray(), "image/jpeg").Validate());
        Assert.NotEmpty(new PhotoUpload(new byte[PhotoUpload.MaxBytes + 1], "image/jpeg").Validate());
    }
}
