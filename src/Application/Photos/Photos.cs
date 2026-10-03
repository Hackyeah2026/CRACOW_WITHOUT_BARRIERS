using Application.Abstractions;
using Domain.Photos;

namespace Application.Photos;

/// <summary>Ocenia zdjęcie dołączane do zgłoszenia: czy widać na nim przeszkodę.</summary>
public sealed record AnalyzePhotoCommand(PhotoUpload Photo) : ICommand<ObstacleAnalysis>;

internal sealed class AnalyzePhotoCommandHandler(IPhotoAnalysisClient client) : ICommandHandler<AnalyzePhotoCommand, ObstacleAnalysis>
{
    public Task<Result<ObstacleAnalysis>> Handle(AnalyzePhotoCommand command, CancellationToken ct)
    {
        var errors = command.Photo.Validate();
        return errors.Count > 0
            ? Task.FromResult(Result.Failure<ObstacleAnalysis>(string.Join(" ", errors)))
            : client.AnalyzeAsync(command.Photo, ct);
    }
}
