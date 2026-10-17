using Application.Abstractions;
using Domain.Assessments;
using Domain.Hazards;
using Domain.Needs;
using Domain.Places;

namespace Application.Places;

public sealed record AssessedPlace(Place Place, Assessment Assessment, double Score);

public static class ModeCategories
{
    /// <summary>Kategorie, które tworzą listę i podpowiedzi w danym trybie.</summary>
    public static IReadOnlyList<PlaceCategory> For(AppMode mode) => mode switch
    {
        AppMode.Sightseeing =>
            [PlaceCategory.Attraction, PlaceCategory.Museum, PlaceCategory.Culture, PlaceCategory.Worship, PlaceCategory.Park, PlaceCategory.Food],
        _ =>
        [
            PlaceCategory.Office, PlaceCategory.Service, PlaceCategory.Clinic, PlaceCategory.Pharmacy, PlaceCategory.Library,
            PlaceCategory.Culture, PlaceCategory.Stop
        ]
    };

    /// <summary>
    /// Miejsca pomocnicze: dostępne przez filtr kategorii, ale nie mieszane z podpowiedziami. Ich pliki są pobierane
    /// dopiero po wybraniu kategorii, więc duże zbiory (ławki, sklepy) nie spowalniają zwykłej listy.
    /// </summary>
    public static IReadOnlyList<PlaceCategory> Support { get; } =
    [
        PlaceCategory.Toilet, PlaceCategory.Bench, PlaceCategory.DisabledParking, PlaceCategory.Shop, PlaceCategory.Hotel,
        PlaceCategory.Education
    ];

    /// <summary>
    /// Kategorie pomocnicze objęte wyszukiwaniem tekstowym. Ławki i koperty to punkty bez nazw: służą do planu trasy,
    /// a nie do szukania, więc ich dużych plików wyszukiwanie nie pobiera.
    /// </summary>
    public static IReadOnlyList<PlaceCategory> Searchable { get; } =
        Support.Except([PlaceCategory.Bench, PlaceCategory.DisabledParking]).ToList();

    /// <summary>
    /// Zakres zapytania: wybrana kategoria albo, bez niej, kategorie trybu. Wpisana fraza rozszerza "wszystkie kategorie"
    /// o przeszukiwalne miejsca pomocnicze, żeby np. "hotel" dawał te same wyniki bez wybierania kategorii.
    /// </summary>
    public static IReadOnlyList<PlaceCategory> Scope(AppMode mode, PlaceCategory? category, PlaceSearch search) =>
        category is { } selected ? [selected]
        : search.IsEmpty ? For(mode)
        : For(mode).Concat(Searchable).Distinct().ToList();
}

/// <summary>
/// Fraza wyszukiwania. Każde słowo musi pasować do nazwy, adresu albo kategorii miejsca; wielkość liter
/// i polskie znaki nie mają znaczenia ("kosciol" znajduje "Kościół", "hotel" całą kategorię noclegów).
/// </summary>
public sealed class PlaceSearch(string? text)
{
    private const int MaxInflectionLength = 2;

    // Rdzenie słów, którymi użytkownik nazywa kategorię (bez polskich znaków).
    private static readonly Dictionary<PlaceCategory, string[]> CategoryStems = new()
    {
        [PlaceCategory.Attraction] = ["atrakcj", "zabyt"],
        [PlaceCategory.Museum] = ["muze"],
        [PlaceCategory.Office] = ["urzad", "urzed"],
        [PlaceCategory.Clinic] = ["zdrowi", "przychodni", "lekarz"],
        [PlaceCategory.Library] = ["bibliotek"],
        [PlaceCategory.Culture] = ["kultur"],
        [PlaceCategory.Stop] = ["przystan"],
        [PlaceCategory.Toilet] = ["toalet", "wc"],
        [PlaceCategory.Bench] = ["lawk"],
        [PlaceCategory.Food] = ["jedzeni", "restauracj", "kawiarni", "gastronomi"],
        [PlaceCategory.DisabledParking] = ["kopert"],
        [PlaceCategory.Pharmacy] = ["aptek"],
        [PlaceCategory.Worship] = ["kosciol", "swiatyni"],
        [PlaceCategory.Park] = ["park"],
        [PlaceCategory.Shop] = ["sklep"],
        [PlaceCategory.Hotel] = ["hotel", "nocleg"],
        [PlaceCategory.Service] = ["poczt", "bank"],
        [PlaceCategory.Education] = ["szkol", "uczelni"]
    };

    private readonly string[] _words = Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public bool IsEmpty => _words.Length == 0;

    public bool Matches(Place place)
    {
        if (IsEmpty)
            return true;
        var content = Content(place);
        return _words.All(w => content.Contains(w) || NamesCategory(w, place.Category));
    }

    /// <summary>Fraza jest w nazwie albo adresie, a nie tylko w nazwie kategorii: takie wyniki idą na początek listy.</summary>
    public bool MatchesText(Place place)
    {
        // Bez frazy lista ma tysiące miejsc: nie ma czego porównywać, więc nie normalizujemy ich nazw.
        if (IsEmpty)
            return true;
        var content = Content(place);
        return _words.All(content.Contains);
    }

    private static string Content(Place place) => Normalize($"{place.Name} {place.Address}");

    private static bool NamesCategory(string word, PlaceCategory category) =>
        CategoryStems.TryGetValue(category, out var stems) && stems.Any(stem =>
            word.StartsWith(stem, StringComparison.Ordinal)
                ? word.Length - stem.Length <= MaxInflectionLength
                : word.Length >= 3 && stem.StartsWith(word, StringComparison.Ordinal));

    // Własna tabela zamiast string.Normalize: działa tak samo w przeglądarce (WebAssembly) i na serwerze.
    private static string Normalize(string? text) => string.Create(text?.Length ?? 0, text ?? "", (span, source) =>
    {
        for (var i = 0; i < span.Length; i++)
        {
            span[i] = char.ToLowerInvariant(source[i]) switch
            {
                'ą' => 'a', 'ć' => 'c', 'ę' => 'e', 'ł' => 'l', 'ń' => 'n', 'ó' => 'o', 'ś' => 's', 'ź' or 'ż' => 'z',
                var c when char.IsWhiteSpace(c) => ' ',
                var c => c
            };
        }
    });
}

/// <summary>Ranking podpowiedzi: ocena dostępności + potwierdzone udogodnienia - odległość od punktu odniesienia.</summary>
public static class SuggestionRanker
{
    public static double Score(Place place, Assessment assessment, GeoPoint origin)
    {
        var status = assessment.Status switch
        {
            AssessmentStatus.Accessible => 3.0,
            AssessmentStatus.Limited => 1.5,
            AssessmentStatus.Unknown => 1.0,
            _ => -5.0
        };
        var amenities = Math.Min(assessment.Amenities.Count(), 5) * 0.2;
        var distanceKm = origin.DistanceTo(place.Location) / 1000;
        return status + amenities - distanceKm * 0.3;
    }
}

/// <summary>Miejsca z katalogu z oceną pod podany profil, posortowane od najlepiej dopasowanych.</summary>
/// <param name="OnlyCertified">
/// Tylko miejsca z certyfikatem konta firmowego. Bez wybranej kategorii obejmuje wszystkie kategorie, w których są
/// takie miejsca, także spoza bieżącego trybu: hotel z certyfikatem ma być na liście również w trybie "Zwiedzam".
/// </param>
public sealed record GetPlacesQuery(
    string CityId, AppMode Mode, NeedsProfile Profile, PlaceCategory? Category = null, string? Search = null,
    bool OnlyCertified = false)
    : IQuery<IReadOnlyList<AssessedPlace>>;

internal sealed class GetPlacesQueryHandler(IPlaceCatalog catalog, IBusinessClient businesses)
    : IQueryHandler<GetPlacesQuery, IReadOnlyList<AssessedPlace>>
{
    public async Task<Result<IReadOnlyList<AssessedPlace>>> Handle(GetPlacesQuery query, CancellationToken ct)
    {
        var cities = await catalog.GetCitiesAsync(ct);
        var city = cities.FirstOrDefault(c => c.Id == query.CityId);
        if (city is null)
            return Result.Failure<IReadOnlyList<AssessedPlace>>($"Nieznane miasto: {query.CityId}.");

        var search = new PlaceSearch(query.Search);
        var places = await catalog.GetAsync(query.CityId, await ScopeAsync(query, search, ct), ct);
        var origin = new GeoPoint(city.Lat, city.Lon);

        IReadOnlyList<AssessedPlace> result = places
            .Where(p => !query.OnlyCertified || p.Certificate is not null)
            .Where(search.Matches)
            .Select(p =>
            {
                var assessment = AssessmentEngine.Assess(query.Profile, p);
                return new AssessedPlace(p, assessment, SuggestionRanker.Score(p, assessment, origin));
            })
            .OrderByDescending(p => search.MatchesText(p.Place))
            .ThenByDescending(p => p.Score)
            .ToList();

        return Result.Success(result);
    }

    private async Task<IReadOnlyList<PlaceCategory>> ScopeAsync(GetPlacesQuery query, PlaceSearch search, CancellationToken ct)
    {
        if (!query.OnlyCertified || query.Category is not null)
            return ModeCategories.Scope(query.Mode, query.Category, search);

        // Bez listy firm z hosta nie wiadomo, które miejsca mają certyfikat: lista jest wtedy pusta.
        var certified = await businesses.GetCertifiedAsync(query.CityId, ct);
        return certified.IsSuccess ? certified.Value.Select(c => c.Category).Distinct().ToList() : [];
    }
}

/// <summary>Liczba miejsc pasujących do frazy w każdej kategorii z zakresu wyszukiwania (do filtra kategorii).</summary>
public sealed record GetPlaceSearchCountsQuery(string CityId, AppMode Mode, string? Search) : IQuery<IReadOnlyList<PlaceCategoryCount>>;

internal sealed class GetPlaceSearchCountsQueryHandler(IPlaceCatalog catalog)
    : IQueryHandler<GetPlaceSearchCountsQuery, IReadOnlyList<PlaceCategoryCount>>
{
    public async Task<Result<IReadOnlyList<PlaceCategoryCount>>> Handle(GetPlaceSearchCountsQuery query, CancellationToken ct)
    {
        var search = new PlaceSearch(query.Search);
        var scope = ModeCategories.Scope(query.Mode, null, search);
        var matches = (await catalog.GetAsync(query.CityId, scope, ct)).Where(search.Matches).ToLookup(p => p.Category);
        return Result.Success<IReadOnlyList<PlaceCategoryCount>>(scope.Select(c => new PlaceCategoryCount(c, matches[c].Count())).ToList());
    }
}

public sealed record GetPlaceDetailsQuery(string CityId, string PlaceId, NeedsProfile Profile) : IQuery<AssessedPlace>;

internal sealed class GetPlaceDetailsQueryHandler(IPlaceCatalog catalog) : IQueryHandler<GetPlaceDetailsQuery, AssessedPlace>
{
    public async Task<Result<AssessedPlace>> Handle(GetPlaceDetailsQuery query, CancellationToken ct)
    {
        var place = await catalog.FindAsync(query.CityId, query.PlaceId, ct);
        return place is null
            ? Result.Failure<AssessedPlace>("Nie znaleziono miejsca.")
            : Result.Success(new AssessedPlace(place, AssessmentEngine.Assess(query.Profile, place), 0));
    }
}

/// <param name="MissingIds">Identyfikatory z planu, których nie ma już w katalogu.</param>
public sealed record PlanPlaces(IReadOnlyList<AssessedPlace> Places, IReadOnlyList<string> MissingIds);

/// <summary>
/// Miejsca wybrane do planu, w kolejności z planu. Identyfikator zapisany w planie może zniknąć z katalogu
/// po ponownym imporcie: taki wraca osobno, żeby strona usunęła go z planu i powiedziała o tym użytkownikowi.
/// </summary>
public sealed record GetPlanPlacesQuery(string CityId, IReadOnlyList<string> PlaceIds, NeedsProfile Profile) : IQuery<PlanPlaces>;

internal sealed class GetPlanPlacesQueryHandler(IPlaceCatalog catalog) : IQueryHandler<GetPlanPlacesQuery, PlanPlaces>
{
    public async Task<Result<PlanPlaces>> Handle(GetPlanPlacesQuery query, CancellationToken ct)
    {
        var places = new List<AssessedPlace>();
        var missing = new List<string>();
        foreach (var id in query.PlaceIds)
        {
            if (await catalog.FindAsync(query.CityId, id, ct) is { } place)
                places.Add(new AssessedPlace(place, AssessmentEngine.Assess(query.Profile, place), 0));
            else
                missing.Add(id);
        }
        return Result.Success(new PlanPlaces(places, missing));
    }
}

/// <summary>Kategorie, które mają dane w tym mieście, z liczbą miejsc (do filtra kategorii).</summary>
public sealed record GetPlaceCategoriesQuery(string CityId) : IQuery<IReadOnlyList<PlaceCategoryCount>>;

internal sealed class GetPlaceCategoriesQueryHandler(IPlaceCatalog catalog)
    : IQueryHandler<GetPlaceCategoriesQuery, IReadOnlyList<PlaceCategoryCount>>
{
    public async Task<Result<IReadOnlyList<PlaceCategoryCount>>> Handle(GetPlaceCategoriesQuery query, CancellationToken ct) =>
        Result.Success(await catalog.GetCategoriesAsync(query.CityId, ct));
}

public sealed record GetCitiesQuery : IQuery<IReadOnlyList<City>>;

internal sealed class GetCitiesQueryHandler(IPlaceCatalog catalog) : IQueryHandler<GetCitiesQuery, IReadOnlyList<City>>
{
    public async Task<Result<IReadOnlyList<City>>> Handle(GetCitiesQuery query, CancellationToken ct) =>
        Result.Success(await catalog.GetCitiesAsync(ct));
}

/// <param name="ConflictCount">Wszystkie miejsca ze sprzecznymi źródłami; <see cref="Conflicts"/> zawiera najwyżej kilkadziesiąt z nich.</param>
public sealed record DataGapsReport(IReadOnlyList<CategoryCoverage> Categories, IReadOnlyList<ConflictingPlace> Conflicts, int ConflictCount);

/// <summary>Pokrycie danymi o dostępności w kategoriach miejsc publicznych i miejsca ze sprzecznymi źródłami (panel urzędnika).</summary>
public sealed record GetDataGapsQuery(string CityId, int MaxConflicts = 50) : IQuery<DataGapsReport>;

internal sealed class GetDataGapsQueryHandler(IPlaceCatalog catalog) : IQueryHandler<GetDataGapsQuery, DataGapsReport>
{
    public async Task<Result<DataGapsReport>> Handle(GetDataGapsQuery query, CancellationToken ct)
    {
        var places = await catalog.GetAsync(query.CityId, DataCoverage.PublicCategories.ToList(), ct);
        var categories = DataCoverage.Summarize(places);
        return Result.Success(new DataGapsReport(categories, DataCoverage.Conflicts(places, query.MaxConflicts), categories.Sum(c => c.WithConflicts)));
    }
}

public sealed record NearbyPlace(Place Place, double DistanceM);

/// <summary>Najbliższe miejsca danej kategorii w linii prostej, np. koperty przy miejscu, do którego się jedzie.</summary>
public sealed record GetNearbyPlacesQuery(string CityId, GeoPoint Point, PlaceCategory Category, double MaxDistanceM, int Limit)
    : IQuery<IReadOnlyList<NearbyPlace>>;

internal sealed class GetNearbyPlacesQueryHandler(IPlaceCatalog catalog) : IQueryHandler<GetNearbyPlacesQuery, IReadOnlyList<NearbyPlace>>
{
    public async Task<Result<IReadOnlyList<NearbyPlace>>> Handle(GetNearbyPlacesQuery query, CancellationToken ct)
    {
        var places = await catalog.GetAsync(query.CityId, [query.Category], ct);
        IReadOnlyList<NearbyPlace> nearby = places
            .Select(p => new NearbyPlace(p, p.Location.DistanceTo(query.Point)))
            .Where(n => n.DistanceM <= query.MaxDistanceM)
            .OrderBy(n => n.DistanceM)
            .Take(query.Limit)
            .ToList();
        return Result.Success(nearby);
    }
}

/// <summary>Zbiory danych użyte w katalogu miasta, do strony "Źródła danych".</summary>
public sealed record GetDataSourcesQuery(string CityId) : IQuery<DataSourcesReport>;

internal sealed class GetDataSourcesQueryHandler(IDataSourcesCatalog catalog) : IQueryHandler<GetDataSourcesQuery, DataSourcesReport>
{
    public async Task<Result<DataSourcesReport>> Handle(GetDataSourcesQuery query, CancellationToken ct) =>
        await catalog.GetAsync(query.CityId, ct) is { } report
            ? Result.Success(report)
            : Result.Failure<DataSourcesReport>("Brak spisu źródeł danych dla tego miasta.");
}

/// <param name="HazardsAvailable">Czy udało się pobrać bariery potwierdzone przez urząd (wymagają bazy hosta).</param>
public sealed record CityGapsView(CityGapsReport Report, DistrictMap? Districts, IReadOnlyList<VerifiedHazard> Hazards, bool HazardsAvailable);

/// <summary>Raport "Luki w danych i bariery" dla całego miasta (strona /miasto).</summary>
public sealed record GetCityGapsQuery(string CityId) : IQuery<CityGapsView>;

internal sealed class GetCityGapsQueryHandler(IPlaceCatalog catalog, IDistrictsCatalog districts, ITransitCatalog transit, IHazardsClient hazards)
    : IQueryHandler<GetCityGapsQuery, CityGapsView>
{
    public async Task<Result<CityGapsView>> Handle(GetCityGapsQuery query, CancellationToken ct)
    {
        var categories = Enum.GetValues<PlaceCategory>().Where(c => c is not (PlaceCategory.Bench or PlaceCategory.Stop or PlaceCategory.DisabledParking)).ToList();
        var places = await catalog.GetAsync(query.CityId, categories, ct);
        var map = await districts.GetAsync(query.CityId, ct);
        var network = await transit.GetNetworkAsync(query.CityId, ct);
        var verified = await hazards.GetVerifiedAsync(query.CityId, ct);
        IReadOnlyList<VerifiedHazard> known = verified.IsSuccess ? verified.Value : [];
        return Result.Success(new CityGapsView(CityGaps.Build(places, map, network, known), map, known, verified.IsSuccess));
    }
}
