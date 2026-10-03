using Application.Abstractions;
using Domain.Businesses;
using Domain.Places;

namespace Infrastructure.Catalog;

/// <summary>
/// Katalog uzupełniony o certyfikaty i deklaracje zatwierdzonych firm. Gdy host nie odpowiada,
/// zwraca miejsca tak, jak są w plikach: brak listy firm nie może zatrzymać listy miejsc ani planu.
/// </summary>
public sealed class CertifiedPlaceCatalog(IPlaceCatalog inner, IBusinessClient businesses) : IPlaceCatalog
{
    public Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct) => inner.GetCitiesAsync(ct);

    public Task<IReadOnlyList<PlaceCategoryCount>> GetCategoriesAsync(string cityId, CancellationToken ct) =>
        inner.GetCategoriesAsync(cityId, ct);

    public async Task<IReadOnlyList<Place>> GetAsync(string cityId, IReadOnlyCollection<PlaceCategory> categories, CancellationToken ct)
    {
        var places = await inner.GetAsync(cityId, categories, ct);
        var certified = await CertifiedAsync(cityId, ct);
        return certified.Count == 0 ? places : places.Select(p => certified.TryGetValue(p.Id, out var c) ? c.ApplyTo(p) : p).ToList();
    }

    public async Task<Place?> FindAsync(string cityId, string placeId, CancellationToken ct)
    {
        var place = await inner.FindAsync(cityId, placeId, ct);
        return place is not null && (await CertifiedAsync(cityId, ct)).TryGetValue(placeId, out var certified)
            ? certified.ApplyTo(place)
            : place;
    }

    private async Task<Dictionary<string, CertifiedPlace>> CertifiedAsync(string cityId, CancellationToken ct)
    {
        var result = await businesses.GetCertifiedAsync(cityId, ct);
        return result.IsSuccess ? result.Value.ToDictionary(c => c.PlaceId) : [];
    }
}
