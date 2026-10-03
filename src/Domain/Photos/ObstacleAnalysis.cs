using Domain.Hazards;

namespace Domain.Photos;

/// <summary>Jak pewna jest ocena modelu: progi są w <see cref="ObstacleAnalysis"/>.</summary>
public enum ObstacleVerdict { Likely, Uncertain, Unlikely }

/// <summary>
/// Ocena zdjęcia przez model AI: czy widać na nim przeszkodę. To podpowiedź dla zgłaszającego,
/// a nie decyzja urzędu; zgłoszenie wysyła i opisuje zawsze człowiek.
/// </summary>
/// <param name="Probability">Prawdopodobieństwo przeszkody od 0 do 1.</param>
/// <param name="SuggestedKind">Najbardziej pasujący rodzaj utrudnienia; null, gdy model nie widzi przeszkody.</param>
/// <param name="Summary">Krótki opis tego, co widać na zdjęciu, po polsku.</param>
public sealed record ObstacleAnalysis(double Probability, HazardKind? SuggestedKind, string Summary)
{
    public const double LikelyFrom = 0.7;
    public const double UnlikelyBelow = 0.3;
    public const int MaxSummaryLength = 300;

    public ObstacleVerdict Verdict =>
        Probability >= LikelyFrom ? ObstacleVerdict.Likely
        : Probability < UnlikelyBelow ? ObstacleVerdict.Unlikely
        : ObstacleVerdict.Uncertain;

    /// <summary>Ocena doprowadzona do dozwolonych zakresów; potrzebne, gdy wraca do hosta z przeglądarki razem ze zgłoszeniem.</summary>
    public ObstacleAnalysis Sanitized() => new(
        double.IsFinite(Probability) ? Math.Clamp(Probability, 0, 1) : 0,
        SuggestedKind is { } kind && Enum.IsDefined(kind) ? kind : null,
        (Summary ?? "").Trim() is { Length: > MaxSummaryLength } text ? text[..MaxSummaryLength] : (Summary ?? "").Trim());

    public int Percent => (int)Math.Round(Math.Clamp(Probability, 0, 1) * 100);
}

/// <summary>
/// Zdjęcie do analizy. Ta wersja nie jest zapisywana: host przekazuje ją modelowi i od razu zapomina.
/// Do zgłoszenia trafia osobna, mniejsza kopia (<see cref="PhotoAttachment"/>).
/// Przeglądarka zmniejsza je wcześniej do <see cref="MaxDimensionPx"/> i zapisuje jako JPEG, co usuwa też dane EXIF (m.in. GPS).
/// </summary>
public sealed record PhotoUpload(byte[] Content, string ContentType)
{
    public const long MaxBytes = 4 * 1024 * 1024;
    public const int MaxDimensionPx = 1600;

    public static IReadOnlyList<string> ContentTypes { get; } = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>Błędy do pokazania użytkownikowi; pusta lista oznacza poprawne zdjęcie.</summary>
    public IReadOnlyList<string> Validate()
    {
        if (Content.Length == 0)
            return ["Nie wybrano zdjęcia."];
        if (Content.Length > MaxBytes)
            return [$"Zdjęcie może mieć najwyżej {MaxBytes / (1024 * 1024)} MB."];
        if (!ContentTypes.Contains(ContentType) || !HasImageSignature(Content, ContentType))
            return ["Plik nie jest zdjęciem w formacie JPEG, PNG ani WebP."];
        return [];
    }

    /// <summary>Sprawdza początek pliku, a nie tylko nagłówek Content-Type podany przez klienta.</summary>
    public static bool HasImageSignature(ReadOnlySpan<byte> content, string contentType) => contentType switch
    {
        "image/jpeg" => content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
        "image/png" => content.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        "image/webp" => content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8),
        _ => false
    };
}
