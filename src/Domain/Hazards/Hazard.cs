using Domain.Needs;
using Domain.Photos;
using Domain.Places;

namespace Domain.Hazards;

/// <summary>Rodzaj utrudnienia w terenie zaznaczonego na mapie, poza katalogiem miejsc.</summary>
public enum HazardKind { Stairs, HighKerb, UnevenSurface, SteepSlope, NarrowPassage, Roadworks, Noise, Crowd, BrightLight, Other }

/// <summary>
/// Etap weryfikacji punktu przez urząd. Tylko <see cref="Verified"/> jest widoczny dla innych i brany pod uwagę w planie;
/// <see cref="Removed"/> oznacza, że utrudnienie było potwierdzone, ale już go nie ma.
/// </summary>
public enum HazardStatus { Pending, Verified, Rejected, Removed }

/// <summary>
/// Punkt z utrudnieniem wskazany na mapie przez zalogowanego mieszkańca. Tak jak zgłoszenie miejsca: bez profilu
/// potrzeb, a zgłaszającego ustala host na podstawie sesji.
/// </summary>
public sealed record HazardDraft(string CityId, double Lat, double Lon, HazardKind Kind, string Description)
{
    public const int MaxDescriptionLength = 500;

    /// <summary>Zmniejszone zdjęcie z formularza; host zapisuje je w bazie i dopina do punktu.</summary>
    public PhotoAttachment? Photo { get; init; }

    /// <summary>Błędy do pokazania użytkownikowi; pusta lista oznacza poprawne zgłoszenie.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(CityId))
            errors.Add("Zgłoszenie musi dotyczyć miasta z aplikacji.");
        if (Lat is < -90 or > 90 || Lon is < -180 or > 180 || (Lat == 0 && Lon == 0))
            errors.Add("Wskaż punkt na mapie.");
        if (!Enum.IsDefined(Kind))
            errors.Add("Nieznany rodzaj utrudnienia.");
        if (Kind == HazardKind.Other && string.IsNullOrWhiteSpace(Description))
            errors.Add("Opisz krótko, na czym polega utrudnienie.");
        // Opis jest opcjonalny, więc żądanie spoza aplikacji może go pominąć.
        if (Description is { Length: > MaxDescriptionLength })
            errors.Add($"Opis może mieć najwyżej {MaxDescriptionLength} znaków.");
        if (Photo is not null)
            errors.AddRange(Photo.Validate());
        return errors;
    }
}

/// <summary>Punkt zapisany w bazie, z etapem weryfikacji.</summary>
/// <param name="OfficialNote">Odpowiedź urzędu dla zgłaszającego.</param>
/// <param name="HandledBy">Login urzędnika, który ostatnio zmienił status; nie trafia poza panel.</param>
public sealed record Hazard(
    string Id, string CityId, double Lat, double Lon, HazardKind Kind, string Description,
    HazardStatus Status, DateTime CreatedAt, DateTime UpdatedAt, string? OfficialNote, string? HandledBy)
{
    public const int MaxNoteLength = 1000;

    /// <summary>Login konta, z którego zgłoszono punkt; null w punktach sprzed wprowadzenia kont. Widzi go tylko urząd.</summary>
    public string? ReportedBy { get; init; }

    /// <summary>Zdjęcie dołączone przez zgłaszającego; widzi je urząd i zgłaszający, nie ma go w widoku publicznym.</summary>
    public ReportPhoto? Photo { get; init; }

    public GeoPoint Location => new(Lat, Lon);

    public static Hazard Create(HazardDraft draft, DateTime now) => new(
        Guid.NewGuid().ToString("N"), draft.CityId, draft.Lat, draft.Lon, draft.Kind, (draft.Description ?? "").Trim(),
        HazardStatus.Pending, now, now, null, null);

    public HazardReceipt ToReceipt() => new(Id, Kind, Lat, Lon, CreatedAt);

    /// <summary>Widok dla zgłaszającego: bez loginu urzędnika.</summary>
    public HazardStatusView ToStatusView() =>
        new(Id, Kind, Lat, Lon, Description, Status, CreatedAt, UpdatedAt, OfficialNote) { Photo = Photo };

    /// <summary>Widok publiczny potwierdzonego punktu: bez zgłaszającego i bez urzędnika.</summary>
    public VerifiedHazard ToVerified() => new(Id, Kind, Lat, Lon, Description, DateOnly.FromDateTime(UpdatedAt));
}

/// <summary>Punkt potwierdzony przez urząd: to widzą wszyscy na mapie i w planie.</summary>
public sealed record VerifiedHazard(string Id, HazardKind Kind, double Lat, double Lon, string Description, DateOnly VerifiedOn)
{
    public GeoPoint Location => new(Lat, Lon);
}

/// <summary>Co zgłaszający widzi o swoim punkcie.</summary>
public sealed record HazardStatusView(
    string Id, HazardKind Kind, double Lat, double Lon, string Description,
    HazardStatus Status, DateTime CreatedAt, DateTime UpdatedAt, string? OfficialNote)
{
    public ReportPhoto? Photo { get; init; }
}

/// <summary>Potwierdzenie przyjęcia punktu.</summary>
public sealed record HazardReceipt(string Id, HazardKind Kind, double Lat, double Lon, DateTime CreatedAt);

public sealed record HazardReview(HazardStatus Status, string? Note)
{
    public IReadOnlyList<string> Validate() =>
        Note is { Length: > Hazard.MaxNoteLength } ? [$"Odpowiedź może mieć najwyżej {Hazard.MaxNoteLength} znaków."]
        : !Enum.IsDefined(Status) ? ["Nieznany status."]
        : [];
}

/// <summary>Potwierdzone utrudnienie przy odcinku planu.</summary>
/// <param name="DistanceM">Odległość punktu od trasy odcinka.</param>
/// <param name="ConcernsProfile">Czy utrudnienie dotyczy profilu, dla którego ułożono plan.</param>
public sealed record HazardOnRoute(VerifiedHazard Hazard, double DistanceM, bool ConcernsProfile);

public static class HazardRules
{
    /// <summary>Jak blisko trasy musi leżeć punkt, żeby trafił do planu.</summary>
    public const double RouteCorridorM = 40;

    /// <summary>Czy utrudnienie tego rodzaju jest istotne dla profilu. Roboty i "inne" dotyczą każdego.</summary>
    public static bool Concerns(HazardKind kind, NeedsProfile profile) => kind switch
    {
        HazardKind.Stairs => profile.StepFreeRequired || profile.AvoidStairs,
        HazardKind.HighKerb => profile.StepFreeRequired || profile.MaxThresholdCm is not null,
        HazardKind.UnevenSurface => profile.StepFreeRequired || profile.AvoidCobblestone || profile.NeedsTactilePaving,
        HazardKind.SteepSlope => profile.StepFreeRequired || profile.MaxDistanceWithoutRestM is not null,
        HazardKind.NarrowPassage => profile.StepFreeRequired || profile.MinDoorWidthCm is not null || profile.NeedsAssistanceDog,
        HazardKind.Noise => profile.MaxNoiseLevel is not null || profile.PrefersQuietRoom,
        HazardKind.Crowd => profile.MaxCrowdLevel is not null || profile.PrefersQuietRoom,
        HazardKind.BrightLight => profile.MaxNoiseLevel is not null || profile.MaxCrowdLevel is not null || profile.PrefersQuietRoom,
        _ => true
    };

    /// <summary>Bliżej trasy punkt uznajemy za leżący na niej: wtedy szukamy drogi, która go omija.</summary>
    public const double OnRouteM = 15;

    /// <summary>Punktu tuż przy początku albo końcu odcinka nie da się ominąć, bo tam trzeba dojść.</summary>
    public const double EndpointMarginM = 40;

    /// <summary>Najwięcej punktów omijanych na jednym odcinku; tyle przyjmuje host.</summary>
    public const int MaxAvoided = 20;

    /// <summary>Przeszkoda w konkretnym miejscu, którą da się obejść inną ulicą. Hałas, tłum i światło obejmują obszar.</summary>
    public static bool IsPhysical(HazardKind kind) => kind is HazardKind.Stairs or HazardKind.HighKerb
        or HazardKind.UnevenSurface or HazardKind.SteepSlope or HazardKind.NarrowPassage or HazardKind.Roadworks;

    /// <summary>
    /// Punkty, które trasa ma ominąć: przeszkody fizyczne istotne dla profilu, leżące na trasie (do <see cref="OnRouteM"/>),
    /// poza otoczeniem początku i końca odcinka.
    /// </summary>
    public static IReadOnlyList<VerifiedHazard> Blocking(
        IReadOnlyList<VerifiedHazard> hazards, IReadOnlyList<GeoPoint> route, NeedsProfile profile)
    {
        if (hazards.Count == 0 || route.Count < 2)
            return [];

        return hazards
            .Where(h => IsPhysical(h.Kind) && Concerns(h.Kind, profile))
            .Where(h => h.Location.DistanceTo(route[0]) > EndpointMarginM && h.Location.DistanceTo(route[^1]) > EndpointMarginM)
            .Where(h => RouteGeometry.Locate(h.Location, route).DistanceM <= OnRouteM)
            .ToList();
    }

    /// <summary>
    /// Potwierdzone punkty w pasie <see cref="RouteCorridorM"/> wokół trasy, od najbliższych początku odcinka.
    /// </summary>
    public static IReadOnlyList<HazardOnRoute> AlongRoute(
        IReadOnlyList<VerifiedHazard> hazards, IReadOnlyList<GeoPoint> route, NeedsProfile profile)
    {
        if (hazards.Count == 0 || route.Count == 0)
            return [];

        return hazards
            .Select(h => (Hazard: h, At: RouteGeometry.Locate(h.Location, route)))
            .Where(x => x.At.DistanceM <= RouteCorridorM)
            .OrderBy(x => x.At.Segment).ThenBy(x => x.At.Along)
            .Select(x => new HazardOnRoute(x.Hazard, Math.Round(x.At.DistanceM), Concerns(x.Hazard.Kind, profile)))
            .ToList();
    }
}
