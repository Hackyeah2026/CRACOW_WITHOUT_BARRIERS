using Domain.Places;

namespace Domain.Reports;

public enum ReportKind { MissingAmenity, Barrier, WrongData }

/// <summary>Etap obsługi zgłoszenia przez urząd. Nowe zgłoszenie ma zawsze <see cref="New"/>.</summary>
public enum ReportStatus { New, InReview, Planned, Resolved, Rejected }

/// <summary>
/// Zgłoszenie wysyłane przez mieszkańca. Celowo bez danych osobowych i bez profilu potrzeb:
/// dotyczy miejsca, a nie osoby, która je zgłasza.
/// </summary>
public sealed record ReportDraft(
    string CityId, string PlaceId, string PlaceName, PlaceCategory Category, double Lat, double Lon,
    ReportKind Kind, IReadOnlyList<FeatureKey> Features, string Description)
{
    public const int MaxDescriptionLength = 1000;
    public const int MaxFeatures = 10;

    /// <summary>Błędy do pokazania użytkownikowi; pusta lista oznacza poprawne zgłoszenie.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(CityId) || string.IsNullOrWhiteSpace(PlaceId) || string.IsNullOrWhiteSpace(PlaceName))
            errors.Add("Zgłoszenie musi dotyczyć miejsca z katalogu.");
        if (Lat is < -90 or > 90 || Lon is < -180 or > 180)
            errors.Add("Nieprawidłowe położenie miejsca.");
        if (Features.Count > MaxFeatures || Features.Distinct().Count() != Features.Count)
            errors.Add("Nieprawidłowa lista udogodnień.");
        if (Kind == ReportKind.MissingAmenity && Features.Count == 0)
            errors.Add("Zaznacz, jakiego udogodnienia brakuje.");
        if (Kind != ReportKind.MissingAmenity && string.IsNullOrWhiteSpace(Description))
            errors.Add("Opisz krótko, co jest nie tak.");
        if (Description.Length > MaxDescriptionLength)
            errors.Add($"Opis może mieć najwyżej {MaxDescriptionLength} znaków.");
        return errors;
    }

    /// <summary>Udogodnienia, których można brakować w miejscu. Pomija cechy liczbowe i opisujące bariery.</summary>
    public static IReadOnlyList<FeatureKey> RequestableFeatures { get; } =
    [
        FeatureKey.StepFreeEntrance, FeatureKey.Elevator, FeatureKey.AccessibleToilet, FeatureKey.TactilePaving,
        FeatureKey.AudioDescription, FeatureKey.InductionLoop, FeatureKey.SignLanguage, FeatureKey.VisualInformation,
        FeatureKey.QuietHours, FeatureKey.QuietRoom, FeatureKey.EasyToReadText, FeatureKey.Pictograms,
        FeatureKey.Benches, FeatureKey.SeatingInside, FeatureKey.StopShelter
    ];
}

/// <summary>Zgłoszenie zapisane w bazie, z etapem obsługi i odpowiedzią urzędu.</summary>
/// <param name="HandledBy">Login urzędnika, który ostatnio zmienił status; nie trafia do zgłaszającego.</param>
public sealed record Report(
    string Id, string CityId, string PlaceId, string PlaceName, PlaceCategory Category, double Lat, double Lon,
    ReportKind Kind, IReadOnlyList<FeatureKey> Features, string Description,
    ReportStatus Status, DateTime CreatedAt, DateTime UpdatedAt, string? OfficialNote, string? HandledBy)
{
    public const int MaxNoteLength = 1000;

    public static Report Create(ReportDraft draft, DateTime now) => new(
        Guid.NewGuid().ToString("N"), draft.CityId, draft.PlaceId, draft.PlaceName.Trim(), draft.Category, draft.Lat, draft.Lon,
        draft.Kind, draft.Features, draft.Description.Trim(), ReportStatus.New, now, now, null, null);

    public ReportReceipt ToReceipt() => new(Id, PlaceId, PlaceName, CreatedAt);

    /// <summary>Widok dla zgłaszającego: bez loginu urzędnika.</summary>
    public ReportStatusView ToStatusView() => new(Id, PlaceId, PlaceName, Kind, Features, Status, CreatedAt, UpdatedAt, OfficialNote);
}

/// <summary>Co zgłaszający widzi o swoim zgłoszeniu.</summary>
public sealed record ReportStatusView(
    string Id, string PlaceId, string PlaceName, ReportKind Kind, IReadOnlyList<FeatureKey> Features,
    ReportStatus Status, DateTime CreatedAt, DateTime UpdatedAt, string? OfficialNote);

/// <summary>Potwierdzenie przyjęcia zgłoszenia; identyfikator zostaje na urządzeniu zgłaszającego.</summary>
public sealed record ReportReceipt(string Id, string PlaceId, string PlaceName, DateTime CreatedAt);

public sealed record ReportFilter(string? CityId = null, ReportStatus? Status = null, string? PlaceId = null);

public sealed record ReportStatusChange(ReportStatus Status, string? Note)
{
    public IReadOnlyList<string> Validate() =>
        Note is { Length: > Report.MaxNoteLength } ? [$"Odpowiedź może mieć najwyżej {Report.MaxNoteLength} znaków."] : [];
}
