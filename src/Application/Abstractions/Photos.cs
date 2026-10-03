using Domain.Photos;

namespace Application.Abstractions;

/// <summary>Model AI oceniający zdjęcie po stronie hosta (klucz API nie trafia do przeglądarki).</summary>
public interface IObstacleDetector
{
    Task<Result<ObstacleAnalysis>> AnalyzeAsync(PhotoUpload photo, CancellationToken ct);
}

/// <summary>Zdjęcia dołączone do zgłoszeń w bazie hosta; osobno od zgłoszeń, żeby listy nie pobierały plików.</summary>
public interface IPhotoStore
{
    Task SaveAsync(StoredPhoto photo, CancellationToken ct);
    Task<StoredPhoto?> FindAsync(string id, CancellationToken ct);
}

/// <summary>Wysyłka zdjęcia z przeglądarki do analizy na hoście; wymaga zalogowanego mieszkańca.</summary>
public interface IPhotoAnalysisClient
{
    Task<Result<ObstacleAnalysis>> AnalyzeAsync(PhotoUpload photo, CancellationToken ct);
}
