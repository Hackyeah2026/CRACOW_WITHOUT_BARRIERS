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

        var categories = query.Category is { } category ? [category] : ModeCategories.For(query.Mode);
        var places = await catalog.GetAsync(query.CityId, categories, ct);
        var search = query.Search?.Trim();
        var origin = new GeoPoint(city.Lat, city.Lon);

        IReadOnlyList<AssessedPlace> result = places
            .Where(p => string.IsNullOrEmpty(search) || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                var assessment = AssessmentEngine.Assess(query.Profile, p);
                return new AssessedPlace(p, assessment, SuggestionRanker.Score(p, assessment, origin));
            })
            .OrderByDescending(p => p.Score)
            .ToList();

        return Result.Success(result);
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
