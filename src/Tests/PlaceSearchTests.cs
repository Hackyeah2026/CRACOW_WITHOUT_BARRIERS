using Application;
using Application.Abstractions;
using Application.Places;
using Domain.Businesses;
using Domain.Needs;
using Domain.Places;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class PlaceSearchTests
{
    private static readonly Place[] Places =
    [
        Place("pod-roza", "Hotel Pod Różą", PlaceCategory.Hotel) with { Certificate = new PlaceCertificate("Pod Różą sp. z o.o.", "KBB-2026-0000AAAA", CertifiedOn) },
        Place("hostel", "Mundo Hostel", PlaceCategory.Hotel),
        Place("restauracja", "Restauracja Hotel Stary", PlaceCategory.Food),
        Place("pizzeria", "Pizzeria Roma", PlaceCategory.Food),
        Place("mariacki", "Kościół Mariacki", PlaceCategory.Worship, "plac Mariacki 5"),
        Place("lawka", "Ławka hotelowa", PlaceCategory.Bench),
        Place("urzad", "Urząd Miasta", PlaceCategory.Office)
    ];

    private static Place Place(string id, string name, PlaceCategory category, string? address = null) =>
        new(id, "krakow", name, category, 50.06, 19.94, address, null, []);

    private static readonly ISender Sender = SenderWith(new FakeBusinesses(hostAnswers: true));

    private static ISender SenderWith(IBusinessClient businesses) => new ServiceCollection()
        .AddApplication()
        .AddSingleton<IPlaceCatalog, FakeCatalog>()
        .AddSingleton(businesses)
        .BuildServiceProvider()
        .GetRequiredService<ISender>();

    private static async Task<string[]> SearchAsync(string? search, PlaceCategory? category = null, AppMode mode = AppMode.Sightseeing,
        bool onlyCertified = false, ISender? sender = null) =>
        (await (sender ?? Sender).Send(new GetPlacesQuery("krakow", mode, NeedsProfile.Empty, category, search, onlyCertified)))
            .Value.Select(p => p.Place.Id).ToArray();

    [Fact]
    public async Task Without_search_all_categories_means_mode_categories() =>
        Assert.Equal(["mariacki", "pizzeria", "restauracja"], (await SearchAsync(null)).Order());

    [Fact]
    public async Task Search_without_category_covers_support_categories_and_puts_name_matches_first()
    {
        var found = await SearchAsync("hotel");

        Assert.Equal(["pod-roza", "restauracja"], found.Take(2).Order());
        Assert.Equal(["hostel"], found.Skip(2));
    }

    [Fact]
    public async Task Search_without_category_finds_everything_the_category_filter_finds()
    {
        var inCategory = await SearchAsync("hotel", PlaceCategory.Hotel);

        Assert.Equal(["hostel", "pod-roza"], inCategory.Order());
        Assert.Subset((await SearchAsync("hotel")).ToHashSet(), inCategory.ToHashSet());
    }

    [Fact]
    public async Task Benches_are_searched_only_in_their_own_category()
    {
        Assert.DoesNotContain("lawka", await SearchAsync("hotelowa"));
        Assert.Equal(["lawka"], await SearchAsync("hotelowa", PlaceCategory.Bench));
    }

    [Theory]
    [InlineData("kosciol")]
    [InlineData("KOŚCIOŁY")]
    [InlineData("mariacki 5")]
    public async Task Search_ignores_case_and_polish_letters_and_reads_the_address(string search) =>
        Assert.Equal(["mariacki"], await SearchAsync(search));

    [Fact]
    public async Task Every_word_has_to_match() =>
        Assert.Equal(["pod-roza"], await SearchAsync("hotel roza"));

    [Fact]
    public async Task Search_stays_within_the_mode() =>
        Assert.Equal(["urzad"], await SearchAsync("urząd", mode: AppMode.Errand));

    [Fact]
    public async Task Certified_filter_reaches_outside_the_mode_and_keeps_only_certified_places()
    {
        // Hotel nie należy do trybu "Zwiedzam", ale ma certyfikat.
        Assert.Equal(["pod-roza"], await SearchAsync(null, onlyCertified: true));
        Assert.Equal(["pod-roza"], await SearchAsync(null, PlaceCategory.Hotel, onlyCertified: true));
        Assert.Empty(await SearchAsync(null, PlaceCategory.Food, onlyCertified: true));
        Assert.Empty(await SearchAsync("mundo", onlyCertified: true));
    }

    [Fact]
    public async Task Certified_filter_is_empty_when_the_host_does_not_answer() =>
        Assert.Empty(await SearchAsync(null, onlyCertified: true, sender: SenderWith(new FakeBusinesses(hostAnswers: false))));

    [Fact]
    public async Task Counts_follow_the_search()
    {
        var counts = (await Sender.Send(new GetPlaceSearchCountsQuery("krakow", AppMode.Sightseeing, "hotel"))).Value
            .ToDictionary(c => c.Category, c => c.Count);

        Assert.Equal(2, counts[PlaceCategory.Hotel]);
        Assert.Equal(1, counts[PlaceCategory.Food]);
        Assert.Equal(0, counts[PlaceCategory.Worship]);
        Assert.DoesNotContain(PlaceCategory.Bench, counts.Keys);
    }

    private static readonly DateTime CertifiedOn = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);

    /// <summary>Publiczna lista firm z hosta: jeden certyfikowany hotel albo brak odpowiedzi.</summary>
    private sealed class FakeBusinesses(bool hostAnswers) : IBusinessClient
    {
        public Task<Result<BusinessAccountView>> ApplyAsync(BusinessApplicationDraft draft, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<MyBusiness>> GetMineAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<BusinessAccountView>> SaveFeaturesAsync(BusinessFeaturesUpdate update, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<CertifiedPlace>>> GetCertifiedAsync(string cityId, CancellationToken ct) => Task.FromResult(hostAnswers
            ? Result.Success<IReadOnlyList<CertifiedPlace>>(
                [new CertifiedPlace("krakow", "pod-roza", "Hotel Pod Różą", PlaceCategory.Hotel, 50.06, 19.94, "Pod Różą sp. z o.o.", "KBB-2026-0000AAAA", CertifiedOn, [], null)])
            : Result.Failure<IReadOnlyList<CertifiedPlace>>("Brak połączenia z serwerem."));
    }

    private sealed class FakeCatalog : IPlaceCatalog
    {
        public Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<City>>([new City("krakow", "Kraków", 50.0614, 19.9366, 14, CityCoverage.Full, [])]);

        public Task<IReadOnlyList<PlaceCategoryCount>> GetCategoriesAsync(string cityId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PlaceCategoryCount>>(
                Places.GroupBy(p => p.Category).Select(g => new PlaceCategoryCount(g.Key, g.Count())).ToList());

        public Task<IReadOnlyList<Place>> GetAsync(string cityId, IReadOnlyCollection<PlaceCategory> categories, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Place>>(Places.Where(p => categories.Contains(p.Category)).ToList());

        public Task<Place?> FindAsync(string cityId, string placeId, CancellationToken ct) =>
            Task.FromResult(Places.FirstOrDefault(p => p.Id == placeId));
    }
}
