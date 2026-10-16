using Domain.Photos;
using Domain.Places;

namespace Domain.Reports;

public enum ReportKind { MissingAmenity, Barrier, WrongData }

/// <summary>Etap obsługi zgłoszenia przez urząd. Nowe zgłoszenie ma zawsze <see cref="New"/>.</summary>
public enum ReportStatus { New, InReview, Planned, Resolved, Rejected }

/// <summary>
/// Zgłoszenie wysyłane przez zalogowanego mieszkańca. Celowo bez profilu potrzeb: dotyczy miejsca.
/// Kto zgłasza, ustala host na podstawie sesji, nie treść żądania.
/// </summary>
public sealed record ReportDraft(
    string CityId, string PlaceId, string PlaceName, PlaceCategory Category, double Lat, double Lon,
    ReportKind Kind, IReadOnlyList<FeatureKey> Features, string Description)
{
    public const int MaxDescriptionLength = 1000;
    public const int MaxFeatures = 10;

    /// <summary>Zmniejszone zdjęcie z formularza; host zapisuje je w bazie i dopina do zgłoszenia.</summary>
    public PhotoAttachment? Photo { get; init; }

    /// <summary>
    /// Błędy do pokazania użytkownikowi; pusta lista oznacza poprawne zgłoszenie. Żądanie spoza aplikacji może
    /// pominąć pola albo podać nieznane wartości: to też ma być błąd danych, a nie awaria hosta.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(CityId) || string.IsNullOrWhiteSpace(PlaceId) || string.IsNullOrWhiteSpace(PlaceName)
            || !Enum.IsDefined(Category))
            errors.Add("Zgłoszenie musi dotyczyć miejsca z katalogu.");
        if (Lat is < -90 or > 90 || Lon is < -180 or > 180)
            errors.Add("Nieprawidłowe położenie miejsca.");
        if (!Enum.IsDefined(Kind))
            errors.Add("Nieznany rodzaj zgłoszenia.");
        if (Features is null || Features.Count > MaxFeatures || Features.Distinct().Count() != Features.Count
            || Features.Any(f => !Enum.IsDefined(f)))
            errors.Add("Nieprawidłowa lista udogodnień.");
        else if (Kind == ReportKind.MissingAmenity && Features.Count == 0)
            errors.Add("Zaznacz, jakiego udogodnienia brakuje.");
        if (Kind != ReportKind.MissingAmenity && string.IsNullOrWhiteSpace(Description))
            errors.Add("Opisz krótko, co jest nie tak.");
        if (Description is { Length: > MaxDescriptionLength })
            errors.Add($"Opis może mieć najwyżej {MaxDescriptionLength} znaków.");
        if (Photo is not null)
            errors.AddRange(Photo.Validate());
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

    /// <summary>Login konta, z którego wysłano zgłoszenie; null w zgłoszeniach sprzed wprowadzenia kont. Widzi go tylko urząd.</summary>
    public string? ReportedBy { get; init; }

    /// <summary>Zdjęcie dołączone przez zgłaszającego; sam plik jest w kolekcji zdjęć.</summary>
    public ReportPhoto? Photo { get; init; }

    /// <summary>Kiedy urzędnik potwierdził, że zaznaczonych udogodnień brakuje; null, gdy nie potwierdził.</summary>
    public DateTime? ConfirmedAt { get; init; }

    /// <summary>Potwierdzić można tylko zgłoszenie, które wskazuje konkretne udogodnienia: tylko wtedy wiadomo, co zmienić w ocenie.</summary>
    public bool CanBeConfirmed => Kind is ReportKind.MissingAmenity or ReportKind.Barrier && Features.Count > 0;

    /// <summary>
    /// Potwierdzony brak wpływa na ocenę, dopóki zgłoszenie nie jest rozwiązane ani odrzucone. Po rozwiązaniu nie
    /// zakładamy, że udogodnienie już jest: ocena wraca do pozostałych źródeł.
    /// </summary>
    public bool AffectsAssessment => ConfirmedAt is not null && CanBeConfirmed && Status is not (ReportStatus.Resolved or ReportStatus.Rejected);

    public ConfirmedGap ToConfirmedGap() => new(CityId, PlaceId, Kind, Features, ConfirmedAt ?? UpdatedAt);

    public static Report Create(ReportDraft draft, DateTime now) => new(
        Guid.NewGuid().ToString("N"), draft.CityId, draft.PlaceId, draft.PlaceName.Trim(), draft.Category, draft.Lat, draft.Lon,
        draft.Kind, draft.Features, (draft.Description ?? "").Trim(), ReportStatus.New, now, now, null, null);

    public ReportReceipt ToReceipt() => new(Id, PlaceId, PlaceName, CreatedAt);

    /// <summary>Widok dla zgłaszającego: bez loginu urzędnika.</summary>
    public ReportStatusView ToStatusView() =>
        new(Id, PlaceId, PlaceName, Kind, Features, Status, CreatedAt, UpdatedAt, OfficialNote) { Photo = Photo, ConfirmedAt = ConfirmedAt };
}

/// <summary>Co zgłaszający widzi o swoim zgłoszeniu.</summary>
public sealed record ReportStatusView(
    string Id, string PlaceId, string PlaceName, ReportKind Kind, IReadOnlyList<FeatureKey> Features,
    ReportStatus Status, DateTime CreatedAt, DateTime UpdatedAt, string? OfficialNote)
{
    public ReportPhoto? Photo { get; init; }

    public DateTime? ConfirmedAt { get; init; }
}

/// <summary>Potwierdzenie przyjęcia zgłoszenia.</summary>
public sealed record ReportReceipt(string Id, string PlaceId, string PlaceName, DateTime CreatedAt);

public sealed record ReportFilter(string? CityId = null, ReportStatus? Status = null, string? PlaceId = null);

/// <param name="Confirmed">Urzędnik potwierdza, że zaznaczonych udogodnień brakuje (np. po wizji lokalnej).</param>
public sealed record ReportStatusChange(ReportStatus Status, string? Note, bool Confirmed = false)
{
    public IReadOnlyList<string> Validate() =>
        Note is { Length: > Report.MaxNoteLength } ? [$"Odpowiedź może mieć najwyżej {Report.MaxNoteLength} znaków."]
        : !Enum.IsDefined(Status) ? ["Nieznany status."]
        : Confirmed && Status == ReportStatus.Rejected ? ["Odrzuconego zgłoszenia nie można jednocześnie potwierdzić."]
        : [];
}

/// <summary>
/// Brak potwierdzony przez urząd w widoku publicznym: bez treści zgłoszenia, zgłaszającego i urzędnika.
/// Aplikacja dopisuje go do oceny miejsca jako osobne źródło.
/// </summary>
public sealed record ConfirmedGap(string CityId, string PlaceId, ReportKind Kind, IReadOnlyList<FeatureKey> Features, DateTime ConfirmedAt)
{
    public const string Source = "Zgłoszenie mieszkańca potwierdzone przez urząd";

    /// <summary>
    /// Potwierdzone braki trafiają przed pozostałe cechy, więc ocena z nich korzysta, a wcześniejsze dane (np. "jest winda"
    /// z OpenStreetMap) zostają obok i karta miejsca pokazuje, że źródła się różnią. Przy kilku potwierdzeniach tej samej
    /// cechy liczy się najnowsze.
    /// </summary>
    public static Place ApplyTo(Place place, IEnumerable<ConfirmedGap> gaps)
    {
        var confirmed = gaps.Where(g => g.PlaceId == place.Id)
            .OrderByDescending(g => g.ConfirmedAt)
            .SelectMany(g => g.Features.Select(key => (key, g.ConfirmedAt)))
            .DistinctBy(x => x.key)
            .Select(x => new AccessibilityFeature(x.key, FeatureState.No, null, Source, DateOnly.FromDateTime(x.ConfirmedAt), false))
            .ToList();
        return confirmed.Count == 0 ? place : place with { Features = [.. confirmed, .. place.Features.Where(f => f.Source != Source)] };
    }
}
