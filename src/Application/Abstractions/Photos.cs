using Domain.Photos;

namespace Application.Abstractions;

/// <summary>Model AI oceniający zdjęcie po stronie hosta (klucz API nie trafia do przeglądarki).</summary>
public interface IObstacleDetector
{
    Task<Result<ObstacleAnalysis>> AnalyzeAsync(PhotoUpload photo, CancellationToken ct);
}

/// <summary>Wysyłka zdjęcia z przeglądarki do analizy na hoście; wymaga zalogowanego mieszkańca.</summary>
public interface IPhotoAnalysisClient
{
    Task<Result<ObstacleAnalysis>> AnalyzeAsync(PhotoUpload photo, CancellationToken ct);
}
