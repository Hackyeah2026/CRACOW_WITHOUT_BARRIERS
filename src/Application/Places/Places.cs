using Application.Abstractions;
using Domain.Assessments;
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
public sealed record GetPlacesQuery(
    string CityId, AppMode Mode, NeedsProfile Profile, PlaceCategory? Category = null, string? Search = null)
    : IQuery<IReadOnlyList<AssessedPlace>>;

internal sealed class GetPlacesQueryHandler(IPlaceCatalog catalog)
    : IQueryHandler<GetPlacesQuery, IReadOnlyList<AssessedPlace>>
{
    public async Task<Result<IReadOnlyList<AssessedPlace>>> Handle(GetPlacesQuery query, CancellationToken ct)
    {
        var cities = await catalog.GetCitiesAsync(ct);
        var city = cities.FirstOrDefault(c => c.Id == query.CityId);
        if (city is null)
            return Result.Failure<IReadOnlyList<AssessedPlace>>($"Nieznane miasto: {query.CityId}.");

        var search = new PlaceSearch(query.Search);
        var places = await catalog.GetAsync(query.CityId, ModeCategories.Scope(query.Mode, query.Category, search), ct);
        var origin = new GeoPoint(city.Lat, city.Lon);

        IReadOnlyList<AssessedPlace> result = places
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
