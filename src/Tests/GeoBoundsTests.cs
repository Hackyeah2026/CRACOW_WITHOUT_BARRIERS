using Domain.Places;

namespace Tests;

public class GeoBoundsTests
{
    private static readonly GeoBounds OldTown = new(South: 50.055, West: 19.930, North: 50.066, East: 19.945);

    [Fact]
    public void Contains_point_inside_and_on_the_edge()
    {
        Assert.True(OldTown.Contains(new GeoPoint(50.0617, 19.9373)));
        Assert.True(OldTown.Contains(new GeoPoint(50.055, 19.930)));
    }

    [Theory]
    [InlineData(50.070, 19.937)]    // na północ
    [InlineData(50.050, 19.937)]    // na południe
    [InlineData(50.061, 19.920)]    // na zachód
    [InlineData(50.061, 19.950)]    // na wschód
    public void Does_not_contain_point_outside(double lat, double lon)
    {
        Assert.False(OldTown.Contains(new GeoPoint(lat, lon)));
    }
}
