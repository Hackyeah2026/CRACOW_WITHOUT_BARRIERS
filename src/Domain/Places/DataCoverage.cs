using Domain.Reports;

namespace Domain.Places;

/// <param name="WithWheelchairInfo">Miejsca z jakąkolwiek informacją o dostępności dla wózków (wejście, ograniczona dostępność).</param>
/// <param name="WithAccessibilityInfo">Miejsca z co najmniej jedną cechą dostępności poza hałasem otoczenia (ten jest z mapy dla wszystkich).</param>
/// <param name="WithCityData">Miejsca z cechą dostępności z danych miasta (MSIP, rejestry ZTP, zgłoszenie potwierdzone przez urząd), bez hałasu otoczenia.</param>
/// <param name="WithConflicts">Miejsca, w których źródła podają co innego dla tej samej cechy.</param>
public sealed record CategoryCoverage(
    PlaceCategory Category, int Places, int WithWheelchairInfo, int WithAccessibilityInfo, int WithCityData, int WithConflicts)
{
    public double WheelchairShare => Places == 0 ? 0 : (double)WithWheelchairInfo / Places;
    public double AccessibilityShare => Places == 0 ? 0 : (double)WithAccessibilityInfo / Places;
}

/// <summary>Miejsce, w którym źródła się różnią: kandydat do sprawdzenia w terenie.</summary>
public sealed record ConflictingPlace(string PlaceId, string Name, PlaceCategory Category, IReadOnlyList<FeatureKey> Features);

/// <summary>
/// Pokrycie katalogu danymi o dostępności: pokazuje urzędowi, gdzie brakuje informacji i gdzie źródła sobie przeczą,
/// czyli gdzie zbieranie danych (wizja lokalna, deklaracje dostępności) da najwięcej.
/// </summary>
public static class DataCoverage
{
    /// <summary>Kategorie, za których dostępność odpowiada miasto albo które są celem wizyt mieszkańców i turystów.</summary>
    public static IReadOnlyList<PlaceCategory> PublicCategories { get; } =
    [
        PlaceCategory.Office, PlaceCategory.Clinic, PlaceCategory.Pharmacy, PlaceCategory.Library, PlaceCategory.Museum,
        PlaceCategory.Culture, PlaceCategory.Attraction, PlaceCategory.Park, PlaceCategory.Education, PlaceCategory.Toilet, PlaceCategory.Stop
    ];

    private static readonly Dictionary<PlaceCategory, int> CategoryOrder =
        PublicCategories.Select((category, index) => (category, index)).ToDictionary(x => x.category, x => x.index);

    private static readonly FeatureKey[] WheelchairKeys = [FeatureKey.WheelchairAccess, FeatureKey.WheelchairLimited, FeatureKey.StepFreeEntrance];

    public static bool IsCitySource(string source) =>
        source.StartsWith("MSIP", StringComparison.Ordinal) || source.StartsWith("ZTP", StringComparison.Ordinal) || source == ConfirmedGap.Source;

    /// <summary>Kategorie od najsłabiej opisanych dla osób na wózku.</summary>
    public static IReadOnlyList<CategoryCoverage> Summarize(IEnumerable<Place> places) => places
        .GroupBy(p => p.Category)
        .Select(g => new CategoryCoverage(
            g.Key, g.Count(),
            g.Count(p => p.Features.Any(f => WheelchairKeys.Contains(f.Key))),
            g.Count(p => p.Features.Any(f => f.Key != FeatureKey.AmbientNoiseDb)),
            g.Count(p => p.Features.Any(f => f.Key != FeatureKey.AmbientNoiseDb && IsCitySource(f.Source))),
            g.Count(HasConflicts)))
        .OrderBy(c => c.WheelchairShare)
        .ThenByDescending(c => c.Places)
        .ToList();

    public static IReadOnlyList<ConflictingPlace> Conflicts(IEnumerable<Place> places, int limit) => places
        .Select(p => (place: p, keys: ConflictingKeys(p)))
        .Where(x => x.keys.Count > 0)
        .OrderBy(x => CategoryOrder.GetValueOrDefault(x.place.Category, int.MaxValue))
        .ThenBy(x => x.place.Name, StringComparer.CurrentCulture)
        .Take(limit)
        .Select(x => new ConflictingPlace(x.place.Id, x.place.Name, x.place.Category, x.keys))
        .ToList();

    private static bool HasConflicts(Place place) => ConflictingKeys(place).Count > 0;

    private static IReadOnlyList<FeatureKey> ConflictingKeys(Place place) =>
        place.Features.Select(f => f.Key).Distinct().Where(k => place.ConflictsOn(k).Count > 0).ToList();
}
