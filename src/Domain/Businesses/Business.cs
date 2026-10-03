using Domain.Places;

namespace Domain.Businesses;

/// <summary>Etap wniosku o konto firmowe. Tylko <see cref="Approved"/> daje oznaczenia, certyfikat i wyróżnienie na mapie.</summary>
public enum BusinessStatus { Pending, Approved, Rejected, Revoked }

/// <summary>Udogodnienie zadeklarowane przez firmę: jest albo go nie ma. Brak wpisu oznacza "nie podano".</summary>
public sealed record BusinessFeature(FeatureKey Key, bool Available);

/// <summary>
/// Wniosek o konto firmowe dla miejsca z katalogu. Kto składa, ustala host na podstawie sesji, nie treść żądania.
/// </summary>
/// <param name="TaxId">NIP; widzi go tylko urząd i właściciel.</param>
/// <param name="Contact">E-mail albo telefon dla urzędu; nie jest publikowany.</param>
public sealed record BusinessApplicationDraft(
    string CityId, string PlaceId, string PlaceName, PlaceCategory Category, double Lat, double Lon,
    string BusinessName, string TaxId, string Contact, string Note)
{
    public const int MaxBusinessNameLength = 80;
    public const int MaxContactLength = 100;
    public const int MaxNoteLength = 500;

    private static readonly int[] TaxIdWeights = [6, 5, 7, 2, 3, 4, 5, 6, 7];

    /// <summary>Firmę prowadzi się w obiekcie z wnętrzem, a nie w punkcie w terenie.</summary>
    public static bool IsEligible(PlaceCategory category) => category is not
        (PlaceCategory.Stop or PlaceCategory.Bench or PlaceCategory.DisabledParking or PlaceCategory.Park
            or PlaceCategory.Toilet or PlaceCategory.QuietSpot);

    /// <summary>NIP jako same cyfry: bez spacji, kresek i przedrostka "PL".</summary>
    public static string NormalizeTaxId(string? taxId)
    {
        var compact = new string((taxId ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        return compact.StartsWith("PL", StringComparison.OrdinalIgnoreCase) ? compact[2..] : compact;
    }

    /// <summary>Dziesięć cyfr z poprawną cyfrą kontrolną.</summary>
    public static bool IsValidTaxId(string? taxId)
    {
        var digits = NormalizeTaxId(taxId);
        if (digits.Length != 10 || !digits.All(char.IsAsciiDigit))
            return false;

        var sum = TaxIdWeights.Select((weight, i) => weight * (digits[i] - '0')).Sum();
        return sum % 11 == digits[9] - '0';
    }

    /// <summary>Błędy do pokazania użytkownikowi; pusta lista oznacza poprawny wniosek.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(CityId) || string.IsNullOrWhiteSpace(PlaceId) || string.IsNullOrWhiteSpace(PlaceName))
            errors.Add("Wybierz miejsce z katalogu, w którym działa firma.");
        else if (!IsEligible(Category))
            errors.Add("Konto firmowe można założyć tylko dla obiektu, np. lokalu, sklepu albo hotelu.");
        if (Lat is < -90 or > 90 || Lon is < -180 or > 180)
            errors.Add("Nieprawidłowe położenie miejsca.");
        if (string.IsNullOrWhiteSpace(BusinessName))
            errors.Add("Podaj nazwę firmy.");
        else if (BusinessName.Trim().Length > MaxBusinessNameLength || BusinessName.Any(char.IsControl))
            errors.Add($"Nazwa firmy może mieć najwyżej {MaxBusinessNameLength} znaków.");
        if (!IsValidTaxId(TaxId))
            errors.Add("Podaj poprawny NIP (10 cyfr).");
        if (string.IsNullOrWhiteSpace(Contact))
            errors.Add("Podaj e-mail albo telefon, pod którym urząd może potwierdzić wniosek.");
        else if (Contact.Trim().Length > MaxContactLength)
            errors.Add($"Kontakt może mieć najwyżej {MaxContactLength} znaków.");
        if (Note is { Length: > MaxNoteLength })
            errors.Add($"Uwagi mogą mieć najwyżej {MaxNoteLength} znaków.");
        return errors;
    }
}

/// <summary>Decyzja urzędnika o wniosku albo o zatwierdzonym koncie.</summary>
public sealed record BusinessReview(BusinessStatus Status, string? Note)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Status == BusinessStatus.Pending)
            errors.Add("Wybierz decyzję: zatwierdzenie, odrzucenie albo cofnięcie zatwierdzenia.");
        if (Status is BusinessStatus.Rejected or BusinessStatus.Revoked && string.IsNullOrWhiteSpace(Note))
            errors.Add("Napisz, dlaczego wniosek jest odrzucony albo zatwierdzenie cofnięte: firma zobaczy to uzasadnienie.");
        if (Note is { Length: > BusinessAccount.MaxNoteLength })
            errors.Add($"Uzasadnienie może mieć najwyżej {BusinessAccount.MaxNoteLength} znaków.");
        return errors;
    }
}

/// <summary>Oznaczenia udogodnień zapisywane przez zatwierdzoną firmę; zastępują poprzednią listę w całości.</summary>
public sealed record BusinessFeaturesUpdate(IReadOnlyList<BusinessFeature> Features)
{
    /// <summary>Udogodnienia, które firma może zadeklarować: tylko cechy tak/nie dotyczące obiektu.</summary>
    public static IReadOnlyList<FeatureKey> Declarable { get; } =
    [
        FeatureKey.StepFreeEntrance, FeatureKey.WheelchairAccess, FeatureKey.Elevator, FeatureKey.AccessibleToilet,
        FeatureKey.TactilePaving, FeatureKey.AudioDescription, FeatureKey.AssistanceDogAllowed, FeatureKey.InductionLoop,
        FeatureKey.SignLanguage, FeatureKey.VisualInformation, FeatureKey.QuietHours, FeatureKey.QuietRoom,
        FeatureKey.EasyToReadText, FeatureKey.Pictograms, FeatureKey.SeatingInside
    ];

    public IReadOnlyList<string> Validate() =>
        Features is null || Features.Any(f => !Declarable.Contains(f.Key)) || Features.Select(f => f.Key).Distinct().Count() != Features.Count
            ? ["Nieprawidłowa lista udogodnień."]
            : [];
}

/// <summary>Wniosek i konto firmowe w bazie. Kluczem jest login konta, więc jedno konto prowadzi jedną firmę.</summary>
/// <param name="HandledBy">Login urzędnika, który podjął ostatnią decyzję; nie trafia do firmy.</param>
/// <param name="CertificateId">Numer certyfikatu nadany przy pierwszym zatwierdzeniu.</param>
public sealed record BusinessAccount(
    string Id, string CityId, string PlaceId, string PlaceName, PlaceCategory Category, double Lat, double Lon,
    string BusinessName, string TaxId, string Contact, string Note,
    BusinessStatus Status, DateTime CreatedAt, DateTime UpdatedAt, string? OfficialNote, string? HandledBy,
    string? CertificateId, DateTime? CertifiedAt, IReadOnlyList<BusinessFeature> Features, DateTime? FeaturesUpdatedAt)
{
    public const int MaxNoteLength = 1000;

    public static BusinessAccount Create(BusinessApplicationDraft draft, string login, DateTime now) => new(
        login, draft.CityId, draft.PlaceId, draft.PlaceName.Trim(), draft.Category, draft.Lat, draft.Lon,
        draft.BusinessName.Trim(), BusinessApplicationDraft.NormalizeTaxId(draft.TaxId), draft.Contact.Trim(), (draft.Note ?? "").Trim(),
        BusinessStatus.Pending, now, now, null, null, null, null, [], null);

    /// <summary>Dlaczego tej decyzji nie da się podjąć przy obecnym statusie; null, gdy decyzja jest dozwolona.</summary>
    public string? TransitionError(BusinessReview review) => review.Status switch
    {
        BusinessStatus.Revoked when Status != BusinessStatus.Approved => "Cofnąć można tylko zatwierdzone konto firmowe.",
        BusinessStatus.Rejected when Status == BusinessStatus.Approved => "Zatwierdzonego konta nie odrzuca się: cofnij zatwierdzenie.",
        _ => null
    };

    /// <summary>Zapisuje decyzję. Zatwierdzenie nadaje numer certyfikatu; ponowne zatwierdzenie zostawia dotychczasowy.</summary>
    public BusinessAccount Review(BusinessReview review, string officialLogin, DateTime now)
    {
        var approved = review.Status == BusinessStatus.Approved;
        return this with
        {
            Status = review.Status,
            OfficialNote = string.IsNullOrWhiteSpace(review.Note) ? null : review.Note.Trim(),
            HandledBy = officialLogin,
            UpdatedAt = now,
            CertificateId = approved ? CertificateId ?? NewCertificateId(now) : CertificateId,
            CertifiedAt = approved ? CertifiedAt ?? now : CertifiedAt
        };
    }

    public bool IsApproved => Status == BusinessStatus.Approved;

    public BusinessAccount WithFeatures(BusinessFeaturesUpdate update, DateTime now) =>
        this with { Features = update.Features, FeaturesUpdatedAt = now, UpdatedAt = now };

    /// <summary>Widok dla właściciela: bez loginu urzędnika.</summary>
    public BusinessAccountView ToView() => new(
        CityId, PlaceId, PlaceName, Category, BusinessName, TaxId, Contact, Note, Status, CreatedAt, UpdatedAt,
        OfficialNote, CertificateId, CertifiedAt, Features, FeaturesUpdatedAt);

    /// <summary>Widok publiczny zatwierdzonego konta: bez loginu, NIP, kontaktu i uwag.</summary>
    public CertifiedPlace ToCertified() => new(
        CityId, PlaceId, PlaceName, Category, Lat, Lon, BusinessName, CertificateId ?? "", CertifiedAt ?? UpdatedAt,
        Features, FeaturesUpdatedAt);

    private static string NewCertificateId(DateTime now) => $"KBB-{now:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}

/// <summary>Co właściciel widzi o swoim wniosku i koncie firmowym.</summary>
public sealed record BusinessAccountView(
    string CityId, string PlaceId, string PlaceName, PlaceCategory Category, string BusinessName, string TaxId, string Contact, string Note,
    BusinessStatus Status, DateTime CreatedAt, DateTime UpdatedAt, string? OfficialNote,
    string? CertificateId, DateTime? CertifiedAt, IReadOnlyList<BusinessFeature> Features, DateTime? FeaturesUpdatedAt);

/// <summary>Odpowiedź na pytanie o własne konto firmowe; <see cref="Business"/> jest null, gdy konto nie złożyło wniosku.</summary>
public sealed record MyBusiness(BusinessAccountView? Business);

/// <summary>Miejsce z certyfikatem: to, co o zatwierdzonej firmie widzi każdy.</summary>
public sealed record CertifiedPlace(
    string CityId, string PlaceId, string PlaceName, PlaceCategory Category, double Lat, double Lon,
    string BusinessName, string CertificateId, DateTime CertifiedAt,
    IReadOnlyList<BusinessFeature> Features, DateTime? FeaturesUpdatedAt)
{
    /// <summary>Źródło cech podanych przez firmę; odróżnia je od danych z OpenStreetMap.</summary>
    public const string FeatureSource = "Deklaracja firmy (konto firmowe)";

    /// <summary>
    /// Miejsce z katalogu uzupełnione o certyfikat i deklaracje firmy. Deklaracja zastępuje cechę o tym samym kluczu,
    /// bo właściciel zna obiekt lepiej niż mapa; pozostałe cechy zostają.
    /// </summary>
    public Place ApplyTo(Place place)
    {
        var declared = Features.Select(f => f.Key).ToHashSet();
        // "Ograniczona dostępność" to w danych z mapy ta sama informacja co "dostępność dla wózków", tylko pod drugim kluczem.
        if (declared.Contains(FeatureKey.WheelchairAccess))
            declared.Add(FeatureKey.WheelchairLimited);
        var checkedOn = DateOnly.FromDateTime(FeaturesUpdatedAt ?? CertifiedAt);
        return place with
        {
            Certificate = new PlaceCertificate(BusinessName, CertificateId, CertifiedAt),
            Features =
            [
                .. place.Features.Where(f => !declared.Contains(f.Key)),
                .. Features.Select(f => new AccessibilityFeature(
                    f.Key, f.Available ? FeatureState.Yes : FeatureState.No, null, FeatureSource, checkedOn, false))
            ]
        };
    }
}
