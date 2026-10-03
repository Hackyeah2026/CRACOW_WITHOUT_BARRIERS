namespace Domain.Places;

public enum PlaceCategory { Attraction, Museum, Office, Clinic, Library, Culture, Stop, Toilet, Bench, QuietSpot, Food, DisabledParking }

public enum AppMode { Sightseeing, Errand }

public enum FeatureState { Unknown, Yes, No }

public enum CityCoverage { Full, OsmOnly }

public enum FeatureKey
{
    WheelchairAccess, WheelchairLimited, StepFreeEntrance, ThresholdHeightCm, DoorWidthCm, Elevator, AccessibleToilet,
    SurfaceCobblestone, InclinePercent, Stairs, TactilePaving, AudioDescription, AssistanceDogAllowed,
    InductionLoop, SignLanguage, VisualInformation, NoiseLevel, CrowdLevel, BrightLight, LowFloorStop,
    StopShelter, QuietHours, QuietRoom, EasyToReadText, Pictograms, Benches, SeatingInside
}

/// <summary>
/// Pojedyncza cecha dostępności. Brak wiedzy to zawsze <see cref="FeatureState.Unknown"/>, nigdy "dostępne".
/// Dla cech liczbowych (np. próg w cm, poziom hałasu 1-3) wartość jest w <see cref="Value"/>.
/// </summary>
public sealed record AccessibilityFeature(
    FeatureKey Key, FeatureState State, double? Value, string Source, DateOnly? CheckedOn, bool IsDemoData);

public sealed record Place(
    string Id, string CityId, string Name, PlaceCategory Category, double Lat, double Lon,
    string? Address, string? Description, IReadOnlyList<AccessibilityFeature> Features)
{
    public GeoPoint Location => new(Lat, Lon);

    public AccessibilityFeature? Feature(FeatureKey key) => Features.FirstOrDefault(f => f.Key == key);

    public FeatureState StateOf(FeatureKey key) => Feature(key)?.State ?? FeatureState.Unknown;

    public double? ValueOf(FeatureKey key) => Feature(key)?.Value;
}

public sealed record City(
    string Id, string Name, double Lat, double Lon, int Zoom, CityCoverage Coverage, IReadOnlyList<string> Sources);
