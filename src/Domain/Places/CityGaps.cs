using System.Text.RegularExpressions;
using Domain.Hazards;
using Domain.Transit;

namespace Domain.Places;

/// <summary>Pokrycie danymi o dostępności grupy miejsc. Liczby, nie odsetki: odsetki liczy <see cref="Share"/>.</summary>
/// <param name="WithWheelchair">Z informacją o wejściu dla wózków (dostępne, ograniczenie, wejście bez stopni).</param>
/// <param name="WithoutAny">Bez żadnej cechy dostępności; hałas otoczenia z mapy się nie liczy, bo mapa obejmuje wszystkich.</param>
/// <param name="WithToilet">Z informacją o toalecie dostosowanej (jest albo jej nie ma).</param>
/// <param name="WithAmbientNoise">Z hałasem otoczenia z miejskiej mapy hałasu.</param>
/// <param name="WithOnSiteNoise">Z hałasem w samym miejscu (w środku).</param>
public sealed record CoverageStats(int Places, int WithWheelchair, int WithoutAny, int WithToilet, int WithAmbientNoise, int WithOnSiteNoise)
{
    public double Share(int count) => Places == 0 ? 0 : (double)count / Places;

    public static CoverageStats Of(IReadOnlyCollection<Place> places) => new(
        places.Count,
        places.Count(CityGaps.HasWheelchairInfo),
        places.Count(p => p.Features.All(f => f.Key == FeatureKey.AmbientNoiseDb)),
        places.Count(p => p.Feature(FeatureKey.AccessibleToilet) is not null),
        places.Count(p => p.Feature(FeatureKey.AmbientNoiseDb) is not null),
        places.Count(p => p.Feature(FeatureKey.NoiseLevel) is not null));
}

/// <param name="Hazards">Bariery zgłoszone przez mieszkańców i potwierdzone przez urząd, leżące w dzielnicy.</param>
public sealed record DistrictCoverage(int Number, string Roman, string Name, CoverageStats Stats, int Hazards);

public sealed record GroupCoverage(string Group, CoverageStats Stats);

/// <param name="DeparturesNearby">Odjazdy komunikacji miejskiej w dniu odniesienia z przystanków w promieniu <see cref="CityGaps.TrafficRadiusM"/>.</param>
/// <param name="Weight">Waga kategorii w rankingu.</param>
public sealed record PriorityPlace(Place Place, string? District, int DeparturesNearby, double Weight, double Score);

/// <param name="TrafficDay">Dzień powszedni z rozkładu, dla którego liczymy odjazdy; null bez rozkładu.</param>
public sealed record CityGapsReport(
    CoverageStats Total, IReadOnlyList<DistrictCoverage> Districts, int OutsideDistricts, IReadOnlyList<GroupCoverage> Groups,
    IReadOnlyList<PriorityPlace> Priorities, int PriorityCandidates, DateOnly? TrafficDay, int Hazards);

/// <summary>
/// Raport "Luki w danych i bariery": gdzie miasto powinno najpierw zebrać dane albo poprawić infrastrukturę.
/// Liczony wyłącznie z katalogu aplikacji (OSM uzupełniony danymi miasta), rozkładu GTFS i potwierdzonych zgłoszeń.
/// </summary>
public static class CityGaps
{
    /// <summary>Punkty infrastruktury (ławki, przystanki, koperty) to nie cele wizyt, więc nie wchodzą do wskaźników.</summary>
    public static bool IsDestination(Place place) => place.Category is not (PlaceCategory.Bench or PlaceCategory.Stop or PlaceCategory.DisabledParking);

    public static readonly IReadOnlyList<(string Group, PlaceCategory[] Categories)> Groups =
    [
        ("Urzędy", [PlaceCategory.Office]),
        ("Kultura", [PlaceCategory.Museum, PlaceCategory.Culture, PlaceCategory.Library, PlaceCategory.Attraction, PlaceCategory.Worship]),
        ("Zdrowie", [PlaceCategory.Clinic, PlaceCategory.Pharmacy]),
        ("Edukacja", [PlaceCategory.Education]),
        ("Gastronomia", [PlaceCategory.Food]),
        ("Pozostałe", [PlaceCategory.Shop, PlaceCategory.Service, PlaceCategory.Hotel, PlaceCategory.Park, PlaceCategory.Toilet])
    ];

    /// <summary>Obiekty publiczne, za których dostępność odpowiada miasto albo które odwiedza najwięcej mieszkańców w sprawach koniecznych.</summary>
    public static readonly IReadOnlyDictionary<PlaceCategory, double> PriorityWeights = new Dictionary<PlaceCategory, double>
    {
        [PlaceCategory.Office] = 2, [PlaceCategory.Clinic] = 2,
        [PlaceCategory.Library] = 1, [PlaceCategory.Museum] = 1, [PlaceCategory.Culture] = 1, [PlaceCategory.Education] = 1
    };

    public const double TrafficRadiusM = 300;
    public const int PriorityCount = 20;

    public const string RankingRule =
        "Kandydaci: urzędy, biblioteki, muzea, instytucje kultury, szkoły oraz szpitale, przychodnie, poradnie, SP ZOZ, pogotowie i hospicja " +
        "(rozpoznawane po nazwie; prywatne gabinety pomijamy), bez żadnej informacji o wejściu dla wózków. " +
        "Natężenie ruchu przybliżamy liczbą odjazdów komunikacji miejskiej w dzień powszedni z przystanków w promieniu 300 m (rozkład GTFS ZTP); " +
        "nie mamy danych o liczbie odwiedzających. Wynik = odjazdy × waga kategorii: urzędy i placówki zdrowia 2 (sprawy, których nie da się odłożyć), pozostałe 1. " +
        "Przy równym wyniku decyduje nazwa.";

    /// <summary>
    /// W katalogu "zdrowie" z OSM są też prywatne gabinety; do rankingu bierzemy placówki, które po nazwie są usługami zdrowia
    /// dla wszystkich (szpital, przychodnia, poradnia, SP ZOZ, pogotowie, hospicjum). NZOZ to placówka niepubliczna, więc go pomijamy.
    /// </summary>
    private static readonly Regex PublicHealthcare = new(@"szpital|przychodni|poradni|\bsp\s?zoz|pogotowi|hospicj", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsPriorityCandidate(Place place) =>
        PriorityWeights.ContainsKey(place.Category) && (place.Category != PlaceCategory.Clinic || PublicHealthcare.IsMatch(place.Name));

    private static readonly FeatureKey[] WheelchairKeys = [FeatureKey.WheelchairAccess, FeatureKey.WheelchairLimited, FeatureKey.StepFreeEntrance];

    public static bool HasWheelchairInfo(Place place) => place.Features.Any(f => WheelchairKeys.Contains(f.Key));

    public static CityGapsReport Build(
        IReadOnlyList<Place> catalog, DistrictMap? districts, TransitNetwork? transit, IReadOnlyList<VerifiedHazard> hazards)
    {
        var places = catalog.Where(IsDestination).ToList();
        var districtOf = places.ToDictionary(p => p, p => districts?.Find(p.Location));
        var hazardDistricts = hazards.Select(h => districts?.Find(h.Location)?.Number).ToList();

        var byDistrict = (districts?.Districts ?? [])
            .Select(d => new DistrictCoverage(d.Number, d.Roman, d.Name,
                CoverageStats.Of(places.Where(p => districtOf[p]?.Number == d.Number).ToList()),
                hazardDistricts.Count(n => n == d.Number)))
            .ToList();

        var groups = Groups.Select(g => new GroupCoverage(g.Group, CoverageStats.Of(places.Where(p => g.Categories.Contains(p.Category)).ToList()))).ToList();

        var (departures, day) = transit is null ? (Array.Empty<int>(), (DateOnly?)null) : DeparturesPerStop(transit);
        var candidates = places.Where(p => IsPriorityCandidate(p) && !HasWheelchairInfo(p)).ToList();
        var priorities = candidates
            .Select(p =>
            {
                var nearby = transit is null ? 0 : DeparturesNear(transit, departures, p.Location);
                var weight = PriorityWeights[p.Category];
                return new PriorityPlace(p, districtOf[p]?.Name, nearby, weight, nearby * weight);
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Place.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Place.Id, StringComparer.Ordinal)
            .Take(PriorityCount)
            .ToList();

        return new CityGapsReport(CoverageStats.Of(places), byDistrict, places.Count(p => districtOf[p] is null), groups,
            priorities, candidates.Count, day, hazards.Count);
    }

    /// <summary>Dzień odniesienia: pierwsza środa w okresie ważności rozkładu (zwykły dzień powszedni, wynik zawsze ten sam).</summary>
    public static DateOnly ReferenceDay(TransitNetwork network)
    {
        var day = network.ValidFrom;
        while (day.DayOfWeek != DayOfWeek.Wednesday)
            day = day.AddDays(1);
        return day;
    }

    /// <summary>Liczba odjazdów z każdego przystanku w dniu odniesienia (ostatni przystanek kursu to przyjazd, nie odjazd).</summary>
    public static (int[] Departures, DateOnly? Day) DeparturesPerStop(TransitNetwork network)
    {
        var day = ReferenceDay(network);
        var active = network.Services.Select(s => s.RunsOn(day)).ToArray();
        var departures = new int[network.Stops.Count];
        foreach (var pattern in network.Lines.SelectMany(l => l.Patterns))
        {
            var trips = 0;
            for (var t = 2; t < pattern.Trips.Count; t += 3)
                if (active[pattern.Trips[t]])
                    trips++;
            if (trips == 0)
                continue;
            for (var s = 0; s + 1 < pattern.Stops.Count; s++)
                departures[pattern.Stops[s]] += trips;
        }
        return (departures, day);
    }

    private static int DeparturesNear(TransitNetwork network, int[] departures, GeoPoint point)
    {
        // Ok. 300 m w stopniach; dokładną odległość liczymy tylko dla przystanków z tego prostokąta.
        const double latDeg = TrafficRadiusM / 111_320.0;
        var lonDeg = TrafficRadiusM / (111_320.0 * Math.Cos(point.Lat * Math.PI / 180));
        var sum = 0;
        for (var i = 0; i < network.Stops.Count; i++)
        {
            var stop = network.Stops[i];
            if (Math.Abs(stop.Lat - point.Lat) <= latDeg && Math.Abs(stop.Lon - point.Lon) <= lonDeg && stop.Location.DistanceTo(point) <= TrafficRadiusM)
                sum += departures[i];
        }
        return sum;
    }
}
