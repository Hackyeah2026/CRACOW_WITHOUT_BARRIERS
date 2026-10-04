namespace Domain.Photos;

/// <summary>
/// Zdjęcie dołączane do zgłoszenia razem z oceną AI, jeśli analiza się udała. Przeglądarka zmniejsza je wcześniej
/// do <see cref="MaxDimensionPx"/> i zapisuje jako JPEG; host sprawdza limit, bo nie może ufać klientowi.
/// </summary>
public sealed record PhotoAttachment(byte[] Content, ObstacleAnalysis? Analysis)
{
    /// <summary>Zdjęcie w bazie ma pokazać przeszkodę urzędnikowi, a nie nadawać się do druku.</summary>
    public const int MaxBytes = 300 * 1024;
    public const int MaxDimensionPx = 1024;
    public const string ContentType = "image/jpeg";

    public IReadOnlyList<string> Validate() =>
        Content is null || Content.Length == 0 ? ["Zdjęcie jest puste."]
        : Content.Length > MaxBytes ? [$"Zdjęcie do zgłoszenia może mieć najwyżej {MaxBytes / 1024} KB."]
        : !PhotoUpload.HasImageSignature(Content, ContentType) ? ["Zdjęcie do zgłoszenia musi być w formacie JPEG."]
        : [];
}

/// <summary>Zdjęcie jako część zgłoszenia: odnośnik do pliku w kolekcji zdjęć i ocena AI z chwili wysłania.</summary>
/// <param name="Analysis">Podpowiedź modelu przekazana przez przeglądarkę zgłaszającego; null, gdy analiza się nie udała.</param>
public sealed record ReportPhoto(string Id, int SizeBytes, ObstacleAnalysis? Analysis);

/// <summary>Plik zdjęcia w bazie. Widzi go urząd i konto, które je wysłało.</summary>
public sealed record StoredPhoto(string Id, byte[] Content, string ContentType, string OwnerLogin, DateTime CreatedAt)
{
    public static StoredPhoto Create(PhotoAttachment attachment, string ownerLogin, DateTime now) =>
        new(Guid.NewGuid().ToString("N"), attachment.Content, PhotoAttachment.ContentType, ownerLogin, now);

    public ReportPhoto ToReportPhoto(ObstacleAnalysis? analysis) => new(Id, Content.Length, analysis?.Sanitized());
}
