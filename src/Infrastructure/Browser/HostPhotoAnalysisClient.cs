using System.Net.Http.Headers;
using Application.Abstractions;
using Domain.Photos;

namespace Infrastructure.Browser;

/// <summary>Analiza zdjęcia (POST api/photos/analyze, multipart). Klucz OpenAI zna tylko host.</summary>
internal sealed class HostPhotoAnalysisClient(HttpClient http) : IPhotoAnalysisClient
{
    public Task<Result<ObstacleAnalysis>> AnalyzeAsync(PhotoUpload photo, CancellationToken ct) =>
        HostApi.SendAsync<ObstacleAnalysis>(async () =>
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(photo.Content);
            file.Headers.ContentType = new MediaTypeHeaderValue(photo.ContentType);
            form.Add(file, "photo", "photo");
            return await http.PostAsync("api/photos/analyze", form, ct);
        }, ct, unavailable: "Analiza zdjęć jest chwilowo niedostępna. Możesz wysłać zgłoszenie bez niej.");
}
