using Application;
using Application.Abstractions;
using Application.Places;
using Domain.Assessments;
using Domain.Needs;
using Domain.Places;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Tools;

namespace Tests;

public class CityRegistriesTests
{
    private static readonly NeedsProfile Blind = NeedsProfilePresets.Build([NeedsProfilePresets.Blind]);
    private static readonly DateOnly Edited = new(2026, 9, 17);

    private static Place P(string id, PlaceCategory category, double lat, double lon, string? name = null, params AccessibilityFeature[] features) =>
        new(id, "krakow", name ?? id, category, lat, lon, null, null, features);

    [Fact]
    public void Neighbouring_parking_spaces_become_one_place_with_their_count()
    {
        ParkingSpace[] spaces =
        [
            new("ID_2", "Zamkowa 1", 50.053190, 19.929397),
            new("ID_3", "Zamkowa 1", 50.053207, 19.929442),
            new("ID_1", "Piltza 55", 50.015082, 19.890529)
        ];

        var groups = ZdmkParkingSource.Group(spaces);

        Assert.Equal(2, groups.Count);
        Assert.Equal((2, "Zamkowa 1"), (groups[0].Spaces, groups[0].Address));
    }

    [Fact]
    public void Parking_group_joins_the_nearby_osm_space_and_a_new_one_is_added_with_count_source_and_date()
    {
        var osm = P("osm-1", PlaceCategory.DisabledParking, 50.05320, 19.92942);
        ParkingGroup[] groups = [new("ID_2", "Zamkowa 1", 50.05320, 19.92942, 2), new("ID_1", "Piltza 55", 50.015082, 19.890529, 1)];

        var once = ZdmkParkingSource.Apply([osm], groups, Edited, "krakow", out var matched);
        var twice = ZdmkParkingSource.Apply(once, groups, Edited, "krakow", out _);

        Assert.Equal(1, matched);
        var joined = once.Single(p => p.Id == "osm-1");
        Assert.Equal("Zamkowa 1", joined.Address);
        var spaces = joined.Feature(FeatureKey.DisabledParkingSpaces)!;
        Assert.Equal((2d, ZdmkParkingSource.Source, Edited), (spaces.Value!.Value, spaces.Source, spaces.CheckedOn!.Value));
        Assert.Contains(once, p => p.Id == $"{ZdmkParkingSource.IdPrefix}ID_1" && p.Category == PlaceCategory.DisabledParking);
        Assert.Equal(once.Count, twice.Count);
        Assert.Equal(once.Sum(p => p.Features.Count), twice.Sum(p => p.Features.Count));
    }

    [Fact]
    public void Request_stop_flag_comes_from_the_registry_by_stop_name()
    {
        Place[] stops = [P("s1", PlaceCategory.Stop, 50.03, 20.00, "Agatowa 01"), P("s2", PlaceCategory.Stop, 50.04, 20.01, "Rondo 02")];
        RegistryStop[] registry = [new("Agatowa 01", 50.0301, 20.0001, true), new("Rondo 02", 50.04, 20.01, false), new("Nieznany 01", 50.2, 20.2, true)];

        var result = ZtpStopsSource.Apply(stops, registry, out var matched);

        Assert.Equal(2, matched);
        Assert.Equal(FeatureState.Yes, result[0].StateOf(FeatureKey.RequestStop));
        Assert.Equal(FeatureState.No, result[1].StateOf(FeatureKey.RequestStop));
    }

    [Fact]
    public void Request_stop_is_limited_for_a_blind_person_and_not_mentioned_for_others()
    {
        var onRequest = P("s1", PlaceCategory.Stop, 50, 20, "Agatowa 01",
            new AccessibilityFeature(FeatureKey.RequestStop, FeatureState.Yes, null, ZtpStopsSource.Source, null, false));
        var always = onRequest with
        {
            Features = [new AccessibilityFeature(FeatureKey.RequestStop, FeatureState.No, null, ZtpStopsSource.Source, null, false)]
        };

        var blind = AssessmentEngine.Assess(Blind, onRequest);

        Assert.Contains(blind.Reasons, r => r.Key == FeatureKey.RequestStop && r.Impact == AssessmentStatus.Limited && r.Source == ZtpStopsSource.Source);
        Assert.Contains(AssessmentEngine.Assess(Blind, always).Reasons, r => r.Key == FeatureKey.RequestStop && r.Kind == ReasonKind.Amenity);
        Assert.DoesNotContain(AssessmentEngine.Assess(NeedsProfilePresets.Build([NeedsProfilePresets.Senior]), onRequest).Reasons,
            r => r.Key == FeatureKey.RequestStop);
    }

    [Fact]
    public void Service_point_enriches_the_office_in_the_same_place_or_is_added_and_two_points_never_share_one_office()
    {
        var office = P("osm-office", PlaceCategory.Office, 50.09281, 19.89823);
        ServicePoint[] points =
        [
            new(11, "Galeria Handlowa, ul. Stawowa 61, lokal nr 157", "https://www.bip.krakow.pl/?dok_id=123237", 50.09281, 19.89823),
            new(12, "Galeria Handlowa, ul. Stawowa 61, lokal nr 193", "https://www.bip.krakow.pl/?dok_id=123238", 50.09281, 19.89823)
        ];

        var result = MsipServicePointsSource.Apply([office], points, "krakow", out var matched);

        Assert.Equal(1, matched);
        Assert.Contains("lokal nr 157", result[0].Address);
        Assert.Contains("bip.krakow.pl", result[0].Description);
        Assert.Contains(result, p => p.Id == $"{MsipServicePointsSource.IdPrefix}12" && p.Category == PlaceCategory.Office);
        Assert.Empty(result.Single(p => p.Id == $"{MsipServicePointsSource.IdPrefix}12").Features);
    }

    [Fact]
    public async Task Nearest_parking_is_sorted_by_distance_within_the_radius()
    {
        Place[] parking =
        [
            P("far", PlaceCategory.DisabledParking, 50.0700, 19.9400),
            P("near", PlaceCategory.DisabledParking, 50.0601, 19.9400),
            P("mid", PlaceCategory.DisabledParking, 50.0620, 19.9400)
        ];
        var sender = new ServiceCollection().AddApplication().AddSingleton<IPlaceCatalog>(new Catalog(parking)).BuildServiceProvider()
            .GetRequiredService<ISender>();

        var result = await sender.Send(new GetNearbyPlacesQuery("krakow", new GeoPoint(50.0600, 19.9400), PlaceCategory.DisabledParking, 500, 3));

        Assert.Equal(["near", "mid"], result.Value.Select(n => n.Place.Id));
    }

    private sealed class Catalog(IReadOnlyList<Place> places) : IPlaceCatalog
    {
        public Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<City>>([]);
        public Task<IReadOnlyList<PlaceCategoryCount>> GetCategoriesAsync(string cityId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PlaceCategoryCount>>([]);
        public Task<IReadOnlyList<Place>> GetAsync(string cityId, IReadOnlyCollection<PlaceCategory> categories, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Place>>(places.Where(p => categories.Contains(p.Category)).ToList());
        public Task<Place?> FindAsync(string cityId, string placeId, CancellationToken ct) =>
            Task.FromResult(places.FirstOrDefault(p => p.Id == placeId));
    }
}
