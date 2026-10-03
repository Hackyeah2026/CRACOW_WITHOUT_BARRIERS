using Domain.Assessments;
using Domain.Hazards;
using Domain.Needs;
using Domain.Places;
using Domain.Reports;
using Domain.Transit;
using Web.Client.Components;

namespace Web.Client.Services;

/// <summary>Polskie etykiety i wygląd statusów. Status ma zawsze kolor, znak i tekst, żeby nie polegać na samym kolorze.</summary>
public static class Labels
{
    public static string Of(PlaceCategory category) => category switch
    {
        PlaceCategory.Attraction => "Atrakcja",
        PlaceCategory.Museum => "Muzeum",
        PlaceCategory.Office => "Urząd",
        PlaceCategory.Clinic => "Zdrowie",
        PlaceCategory.Library => "Biblioteka",
        PlaceCategory.Culture => "Kultura",
        PlaceCategory.Stop => "Przystanek",
        PlaceCategory.Toilet => "Toaleta",
        PlaceCategory.Bench => "Ławka",
        PlaceCategory.QuietSpot => "Ciche miejsce",
        PlaceCategory.Food => "Jedzenie",
        PlaceCategory.DisabledParking => "Koperta",
        PlaceCategory.Pharmacy => "Apteka",
        PlaceCategory.Worship => "Miejsce kultu",
        PlaceCategory.Park => "Park",
        PlaceCategory.Shop => "Sklep",
        PlaceCategory.Hotel => "Nocleg",
        PlaceCategory.Service => "Poczta, bank, pomoc",
        PlaceCategory.Education => "Szkoła, uczelnia",
        _ => category.ToString()
    };

    /// <summary>Powyżej tylu pinezek symbole (elementy strony) zastępujemy kółkami rysowanymi na jednym płótnie.</summary>
    public const int MaxIconMarkers = 600;

    /// <summary>Symbol na mapie dla punktów odpoczynku; pozostałe kategorie rysujemy kółkiem w kolorze oceny.</summary>
    public static string? MapIcon(PlaceCategory category) => category switch
    {
        PlaceCategory.Toilet => "🚽",
        PlaceCategory.Bench => "🪑",
        PlaceCategory.QuietSpot => "🤫",
        PlaceCategory.DisabledParking => "🅿️",
        _ => null
    };

    public static string Of(AppMode mode) => mode == AppMode.Sightseeing ? "Zwiedzam" : "Załatwiam sprawę";

    public static string Of(AssessmentStatus status) => status switch
    {
        AssessmentStatus.Accessible => "Dostępne",
        AssessmentStatus.Limited => "Z ograniczeniami",
        AssessmentStatus.Inaccessible => "Niedostępne",
        _ => "Brak danych"
    };

    public static string Symbol(AssessmentStatus status) => status switch
    {
        AssessmentStatus.Accessible => "✓",
        AssessmentStatus.Limited => "!",
        AssessmentStatus.Inaccessible => "✕",
        _ => "?"
    };

    public static string Css(AssessmentStatus status) => status switch
    {
        AssessmentStatus.Accessible => "status-ok",
        AssessmentStatus.Limited => "status-limited",
        AssessmentStatus.Inaccessible => "status-no",
        _ => "status-unknown"
    };

    public static string Color(AssessmentStatus status) => status switch
    {
        AssessmentStatus.Accessible => "#198754",
        AssessmentStatus.Limited => "#ffc107",
        AssessmentStatus.Inaccessible => "#dc3545",
        _ => "#adb5bd"
    };

    public static string Of(FeatureKey key) => key switch
    {
        FeatureKey.WheelchairAccess => "Dostępność dla wózków",
        FeatureKey.WheelchairLimited => "Ograniczona dostępność dla wózków",
        FeatureKey.StepFreeEntrance => "Wejście bez stopni",
        FeatureKey.ThresholdHeightCm => "Wysokość progu (cm)",
        FeatureKey.DoorWidthCm => "Szerokość drzwi (cm)",
        FeatureKey.Elevator => "Winda",
        FeatureKey.AccessibleToilet => "Toaleta dostosowana",
        FeatureKey.SurfaceCobblestone => "Bruk w otoczeniu",
        FeatureKey.InclinePercent => "Nachylenie (%)",
        FeatureKey.Stairs => "Schody",
        FeatureKey.TactilePaving => "Ścieżki dotykowe",
        FeatureKey.AudioDescription => "Audiodeskrypcja",
        FeatureKey.AssistanceDogAllowed => "Wstęp z psem asystującym",
        FeatureKey.InductionLoop => "Pętla indukcyjna",
        FeatureKey.SignLanguage => "Tłumacz PJM",
        FeatureKey.VisualInformation => "Informacja wizualna",
        FeatureKey.NoiseLevel => "Poziom hałasu (1-3)",
        FeatureKey.CrowdLevel => "Poziom tłumu (1-3)",
        FeatureKey.BrightLight => "Ostre światło",
        FeatureKey.LowFloorStop => "Przystanek z wysokim peronem",
        FeatureKey.StopShelter => "Wiata",
        FeatureKey.QuietHours => "Ciche godziny",
        FeatureKey.QuietRoom => "Pokój wyciszenia",
        FeatureKey.EasyToReadText => "Tekst łatwy do czytania",
        FeatureKey.Pictograms => "Piktogramy",
        FeatureKey.Benches => "Ławki",
        FeatureKey.SeatingInside => "Miejsca do siedzenia w środku",
        _ => key.ToString()
    };

    public static string Value(AccessibilityFeature feature) => feature.Value is { } value
        ? value.ToString("0.#")
        : feature.State switch { FeatureState.Yes => "tak", FeatureState.No => "nie", _ => "nieznane" };

    public static string Of(TransitKind kind) => kind == TransitKind.Tram ? "Tramwaj" : "Autobus";

    /// <summary>Minuta od północy jako godzina; czasy po północy zawijają się do następnej doby.</summary>
    public static string Time(int minute) => $"{minute / 60 % 24:00}:{minute % 60:00}";

    public static string Stops(int count) => count == 1 ? "1 przystanek" : count is >= 2 and <= 4 ? $"{count} przystanki" : $"{count} przystanków";

    public static string Distance(double meters) => meters >= 1000 ? $"{meters / 1000:0.0} km" : $"{Math.Round(meters / 10) * 10:0} m";

    public static string Of(ReportKind kind) => kind switch
    {
        ReportKind.MissingAmenity => "Brakuje udogodnienia",
        ReportKind.Barrier => "Bariera",
        ReportKind.WrongData => "Błędne dane w aplikacji",
        _ => kind.ToString()
    };

    public static string Of(ReportStatus status) => status switch
    {
        ReportStatus.New => "Nowe",
        ReportStatus.InReview => "Sprawdzane",
        ReportStatus.Planned => "Zaplanowane",
        ReportStatus.Resolved => "Rozwiązane",
        ReportStatus.Rejected => "Odrzucone",
        _ => status.ToString()
    };

    /// <summary>Wygląd statusu zgłoszenia, zgodny z oceną miejsc: zielony = załatwione, czerwony = odrzucone.</summary>
    public static string Css(ReportStatus status) => status switch
    {
        ReportStatus.Resolved => "status-accessible",
        ReportStatus.Rejected => "status-inaccessible",
        ReportStatus.InReview or ReportStatus.Planned => "status-limited",
        _ => "status-unknown"
    };

    public static string Of(HazardKind kind) => kind switch
    {
        HazardKind.Stairs => "Schody lub stopnie",
        HazardKind.HighKerb => "Wysoki krawężnik",
        HazardKind.UnevenSurface => "Nierówna nawierzchnia",
        HazardKind.SteepSlope => "Stromy odcinek",
        HazardKind.NarrowPassage => "Wąskie przejście",
        HazardKind.Roadworks => "Roboty lub zastawiony chodnik",
        HazardKind.Noise => "Hałas",
        HazardKind.Crowd => "Tłum",
        HazardKind.BrightLight => "Ostre lub migające światło",
        HazardKind.Other => "Inne utrudnienie",
        _ => kind.ToString()
    };

    /// <summary>Symbol punktu na mapie; rodzaj jest zawsze także w nazwie pinezki.</summary>
    public static string Icon(HazardKind kind) => kind switch
    {
        HazardKind.Roadworks => "🚧",
        HazardKind.Noise => "🔊",
        HazardKind.Crowd => "👥",
        HazardKind.BrightLight => "💡",
        _ => "⚠️"
    };

    /// <summary>Kogo utrudnienie dotyczy najbardziej, do zdania "utrudnienie dla ...".</summary>
    public static string AffectedGroup(HazardKind kind) => kind switch
    {
        HazardKind.Stairs or HazardKind.HighKerb or HazardKind.SteepSlope or HazardKind.NarrowPassage
            => "dla wózków i osób z ograniczoną sprawnością ruchową",
        HazardKind.UnevenSurface => "dla wózków, osób z ograniczoną sprawnością ruchową i osób niewidomych",
        HazardKind.Noise => "dla osób wrażliwych na hałas",
        HazardKind.Crowd => "dla osób wrażliwych na tłum",
        HazardKind.BrightLight => "dla osób wrażliwych na bodźce",
        _ => "dla pieszych"
    };

    /// <summary>Nazwa potwierdzonego punktu na mapie.</summary>
    public static string Verified(VerifiedHazard hazard) =>
        $"Zweryfikowane utrudnienie {AffectedGroup(hazard.Kind)}: {Of(hazard.Kind).ToLowerInvariant()}";

    public static string Of(HazardStatus status) => status switch
    {
        HazardStatus.Pending => "Czeka na weryfikację",
        HazardStatus.Verified => "Potwierdzone",
        HazardStatus.Rejected => "Odrzucone",
        HazardStatus.Removed => "Już nie występuje",
        _ => status.ToString()
    };

    public static string Css(HazardStatus status) => status switch
    {
        HazardStatus.Verified => "status-accessible",
        HazardStatus.Rejected => "status-inaccessible",
        HazardStatus.Removed => "status-limited",
        _ => "status-unknown"
    };

    public static string Color(HazardStatus status) => status switch
    {
        HazardStatus.Pending => "#d97706",
        HazardStatus.Verified => "#dc3545",
        _ => "#adb5bd"
    };

    public static MapMarker Marker(VerifiedHazard hazard, string idPrefix = "") =>
        new(idPrefix + hazard.Id, Verified(hazard), hazard.Lat, hazard.Lon, Color(HazardStatus.Verified), Icon: Icon(hazard.Kind));

    /// <summary>Ustawione potrzeby jako krótka lista do podsumowania profilu.</summary>
    public static IReadOnlyList<string> Needs(NeedsProfile profile)
    {
        var needs = new List<string>();
        void Add(bool on, string text) { if (on) needs.Add(text); }

        Add(profile.StepFreeRequired, "wejście bez stopni");
        Add(profile.AvoidStairs, "bez schodów");
        Add(profile.AvoidCobblestone, "bez bruku i nierównej nawierzchni");
        Add(profile.NeedsAccessibleToilet, "toaleta dostosowana");
        Add(profile.NeedsTactilePaving, "ścieżki dotykowe");
        Add(profile.NeedsAudioDescription, "audiodeskrypcja");
        Add(profile.NeedsAssistanceDog, "wstęp z psem asystującym");
        Add(profile.NeedsSignLanguage, "obsługa w PJM");
        Add(profile.NeedsInductionLoop, "pętla indukcyjna");
        Add(profile.NeedsVisualInformation, "informacja wizualna");
        Add(profile.MaxNoiseLevel is not null, profile.MaxNoiseLevel == 1 ? "tylko bardzo cicho" : "najwyżej umiarkowany hałas");
        Add(profile.MaxCrowdLevel is not null, profile.MaxCrowdLevel == 1 ? "tylko mało ludzi" : "najwyżej umiarkowany tłum");
        Add(profile.PrefersQuietRoom, "pokój wyciszenia");
        Add(profile.NeedsEasyToRead, "tekst łatwy do czytania");
        Add(profile.NeedsPictograms, "piktogramy");
        Add(profile.NeedsSeating, "miejsca do siedzenia");
        Add(profile.MaxDistanceWithoutRestM is not null, $"odpoczynek co {profile.MaxDistanceWithoutRestM} m");
        return needs;
    }

    /// <summary>Login zgłaszającego w panelu urzędnika; zgłoszenia sprzed wprowadzenia kont go nie mają.</summary>
    public static string Reporter(string? login) => login ?? "brak (zgłoszenie sprzed wprowadzenia kont)";

    public static string Features(IReadOnlyList<FeatureKey> features) => string.Join(", ", features.Select(Of));

    /// <summary>Data i godzina w czasie lokalnym przeglądarki.</summary>
    public static string When(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}
