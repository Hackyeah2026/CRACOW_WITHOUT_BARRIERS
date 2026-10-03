using System.Text.Json.Serialization;

namespace Domain.Needs;

/// <summary>
/// Potrzeby funkcjonalne użytkownika. Poziomy hałasu i tłumu: 1 = niski, 2 = umiarkowany, 3 = wysoki.
/// </summary>
public sealed record NeedsProfile
{
    public static NeedsProfile Empty { get; } = new();

    public IReadOnlyList<string> Presets { get; init; } = [];

    /// <summary>Wejście bez stopni jest warunkiem koniecznym (np. wózek).</summary>
    public bool StepFreeRequired { get; init; }

    /// <summary>Schody są barierą; twardą przy <see cref="StepFreeRequired"/>, w innym wypadku utrudnieniem.</summary>
    public bool AvoidStairs { get; init; }

    public double? MaxThresholdCm { get; init; }
    public double? MinDoorWidthCm { get; init; }
    public bool AvoidCobblestone { get; init; }
    public bool NeedsAccessibleToilet { get; init; }
    public int? MaxNoiseLevel { get; init; }
    public int? MaxCrowdLevel { get; init; }
    public bool PrefersQuietRoom { get; init; }
    public bool NeedsSeating { get; init; }
    public int? MaxDistanceWithoutRestM { get; init; }
    public double WalkingSpeedKmh { get; init; } = 4.5;

    [JsonIgnore]
    public bool IsEmpty => !StepFreeRequired && !AvoidStairs && MaxThresholdCm is null && MinDoorWidthCm is null
                           && !AvoidCobblestone && !NeedsAccessibleToilet && MaxNoiseLevel is null
                           && MaxCrowdLevel is null && !PrefersQuietRoom && !NeedsSeating
                           && MaxDistanceWithoutRestM is null;

    /// <summary>Łączy dwa profile, biorąc w każdym parametrze wartość bardziej restrykcyjną.</summary>
    public NeedsProfile CombineWith(NeedsProfile other) => new()
    {
        Presets = Presets.Union(other.Presets).ToList(),
        StepFreeRequired = StepFreeRequired || other.StepFreeRequired,
        AvoidStairs = AvoidStairs || other.AvoidStairs,
        MaxThresholdCm = Min(MaxThresholdCm, other.MaxThresholdCm),
        MinDoorWidthCm = Max(MinDoorWidthCm, other.MinDoorWidthCm),
        AvoidCobblestone = AvoidCobblestone || other.AvoidCobblestone,
        NeedsAccessibleToilet = NeedsAccessibleToilet || other.NeedsAccessibleToilet,
        MaxNoiseLevel = (int?)Min(MaxNoiseLevel, other.MaxNoiseLevel),
        MaxCrowdLevel = (int?)Min(MaxCrowdLevel, other.MaxCrowdLevel),
        PrefersQuietRoom = PrefersQuietRoom || other.PrefersQuietRoom,
        NeedsSeating = NeedsSeating || other.NeedsSeating,
        MaxDistanceWithoutRestM = (int?)Min(MaxDistanceWithoutRestM, other.MaxDistanceWithoutRestM),
        WalkingSpeedKmh = Math.Min(WalkingSpeedKmh, other.WalkingSpeedKmh)
    };

    private static double? Min(double? a, double? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);
    private static double? Max(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
