using Domain.Assessments;
using Domain.Places;
using Domain.Transit;

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
        _ => category.ToString()
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
}
