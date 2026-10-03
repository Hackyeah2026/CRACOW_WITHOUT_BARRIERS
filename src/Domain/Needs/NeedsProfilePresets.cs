namespace Domain.Needs;

public sealed record NeedsPreset(string Id, string Area, string Name, string Description, NeedsProfile Profile);

/// <summary>Gotowe profile: skrót, który wstępnie ustawia parametry. Użytkownik może je potem zmienić.</summary>
public static class NeedsProfilePresets
{
    public const string ElectricWheelchair = "wheelchair-electric";
    public const string ManualWheelchair = "wheelchair-manual";
    public const string WalkingAid = "walking-aid";
    public const string Stroller = "stroller";
    public const string Autism = "autism";
    public const string SensorySensitivity = "sensory";
    public const string Senior = "senior";
    public const string Blind = "blind";
    public const string Deaf = "deaf";
    public const string HardOfHearing = "hard-of-hearing";
    public const string EasyRead = "easy-read";

    public static IReadOnlyList<NeedsPreset> All { get; } =
    [
        new(ElectricWheelchair, "Ruch", "Wózek elektryczny", "Wejścia bez stopni, szerokie drzwi, toaleta dostosowana, bez bruku.",
            new NeedsProfile
            {
                StepFreeRequired = true, AvoidStairs = true, MaxThresholdCm = 3, MinDoorWidthCm = 85,
                AvoidCobblestone = true, NeedsAccessibleToilet = true, WalkingSpeedKmh = 5
            }),
        new(ManualWheelchair, "Ruch", "Wózek ręczny", "Wejścia bez stopni, niskie progi, krótsze odcinki.",
            new NeedsProfile
            {
                StepFreeRequired = true, AvoidStairs = true, MaxThresholdCm = 2, MinDoorWidthCm = 80,
                AvoidCobblestone = true, NeedsAccessibleToilet = true, MaxDistanceWithoutRestM = 800, WalkingSpeedKmh = 3.5
            }),
        new(WalkingAid, "Ruch", "Kule lub balkonik", "Schody są utrudnieniem, potrzebne miejsca do siedzenia.",
            new NeedsProfile { AvoidStairs = true, NeedsSeating = true, MaxDistanceWithoutRestM = 400, WalkingSpeedKmh = 3 }),
        new(Blind, "Wzrok", "Osoba niewidoma / słabowidząca", "Ścieżki dotykowe, audiodeskrypcja, pies asystujący.",
            new NeedsProfile { NeedsTactilePaving = true, NeedsAudioDescription = true, NeedsAssistanceDog = true }),
        new(Deaf, "Słuch", "Osoba głucha", "Obsługa w Polskim Języku Migowym (PJM), informacja wizualna.",
            new NeedsProfile { NeedsSignLanguage = true, NeedsVisualInformation = true }),
        new(HardOfHearing, "Słuch", "Osoba słabosłysząca", "Pętla indukcyjna na stanowisku obsługi.",
            new NeedsProfile { NeedsInductionLoop = true }),
        new(Autism, "Sensoryka", "Spektrum autyzmu", "Cicho i bez tłumu, ciche miejsca na przerwę.",
            new NeedsProfile { MaxNoiseLevel = 1, MaxCrowdLevel = 1, PrefersQuietRoom = true }),
        new(SensorySensitivity, "Sensoryka", "Nadwrażliwość sensoryczna", "Unikanie dużego hałasu i tłumu.",
            new NeedsProfile { MaxNoiseLevel = 2, MaxCrowdLevel = 2 }),
        new(EasyRead, "Poznawcze", "Niepełnosprawność intelektualna", "Tekst łatwy do czytania (ETR) i piktogramy.",
            new NeedsProfile { NeedsEasyToRead = true, NeedsPictograms = true }),
        new(Senior, "Kondycja", "Senior", "Krótkie odcinki, ławki po drodze, wolniejsze tempo.",
            new NeedsProfile { AvoidStairs = true, NeedsSeating = true, MaxDistanceWithoutRestM = 500, WalkingSpeedKmh = 3 }),
        new(Stroller, "Towarzyszące", "Wózek dziecięcy", "Schody i bruk są utrudnieniem.",
            new NeedsProfile { AvoidStairs = true, AvoidCobblestone = true, WalkingSpeedKmh = 4 })
    ];

    public static NeedsProfile Build(IEnumerable<string> presetIds)
    {
        var ids = presetIds.ToList();
        var profile = All.Where(p => ids.Contains(p.Id))
            .Select(p => p.Profile)
            .Aggregate((NeedsProfile?)null, (acc, p) => acc is null ? p : acc.CombineWith(p));
        return (profile ?? NeedsProfile.Empty) with { Presets = ids };
    }
}
