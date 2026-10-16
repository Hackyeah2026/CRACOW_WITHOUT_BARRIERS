using Application.Abstractions;
using Domain.Places;
using Domain.Reports;

namespace Infrastructure.Catalog;

/// <summary>
/// Katalog uzupełniony o braki potwierdzone przez urząd na podstawie zgłoszeń mieszkańców. Gdy host nie odpowiada,
/// zwraca miejsca bez nich: brak tej listy nie może zatrzymać listy miejsc ani planu.
/// </summary>
public sealed class ConfirmedGapsPlaceCatalog(IPlaceCatalog inner, IReportsClient reports) : IPlaceCatalog
{
    public Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct) => inner.GetCitiesAsync(ct);

    public Task<IReadOnlyList<PlaceCategoryCount>> GetCategoriesAsync(string cityId, CancellationToken ct) =>
        inner.GetCategoriesAsync(cityId, ct);

    public async Task<IReadOnlyList<Place>> GetAsync(string cityId, IReadOnlyCollection<PlaceCategory> categories, CancellationToken ct)
    {
        var places = await inner.GetAsync(cityId, categories, ct);
        var gaps = await GapsAsync(cityId, ct);
        return gaps.Count == 0 ? places : places.Select(p => gaps.Contains(p.Id) ? ConfirmedGap.ApplyTo(p, gaps[p.Id]) : p).ToList();
    }

    public async Task<Place?> FindAsync(string cityId, string placeId, CancellationToken ct)
    {
        var place = await inner.FindAsync(cityId, placeId, ct);
        return place is null ? null : ConfirmedGap.ApplyTo(place, (await GapsAsync(cityId, ct))[placeId]);
    }

    private async Task<ILookup<string, ConfirmedGap>> GapsAsync(string cityId, CancellationToken ct)
    {
        var result = await reports.GetConfirmedAsync(cityId, ct);
        return (result.IsSuccess ? result.Value : []).ToLookup(g => g.PlaceId);
    }
}
