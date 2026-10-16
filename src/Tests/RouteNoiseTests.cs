using Domain.Places;
using Tools;

namespace Tests;

public class RouteNoiseTests
{
    // Kwadrat 0,01° x 0,01°: zachodnia połowa cicha, wschodnia 70-75 dB.
    private static readonly double[] West = [19.930, 50.050, 19.935, 50.050, 19.935, 50.060, 19.930, 50.060, 19.930, 50.050];
    private static readonly double[] East = [19.935, 50.050, 19.940, 50.050, 19.940, 50.060, 19.935, 50.060, 19.935, 50.050];

    private static NoiseGrid Grid() =>
        MsipNoiseSource.BuildGrid(new NoiseMap([new NoiseArea(0, [West]), new NoiseArea(70, [East])]), 50.050, 19.930, 50.060, 19.940);

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(55d)]
    [InlineData(70d)]
    [InlineData(80d)]
    public void Band_survives_encoding(double? band) => Assert.Equal(band, NoiseGrid.Decode(NoiseGrid.Encode(band)));

    [Fact]
    public void Grid_answers_like_the_map_and_has_no_data_outside()
    {
        var grid = Grid();

        Assert.Equal(0, grid.BandAt(new GeoPoint(50.055, 19.932)));
        Assert.Equal(70, grid.BandAt(new GeoPoint(50.055, 19.938)));
        Assert.Null(grid.BandAt(new GeoPoint(50.2, 19.938)));
        Assert.Equal(MsipNoiseSource.Source, grid.Source);
    }

    [Fact]
    public void Loud_part_of_the_route_is_measured_only_above_the_profile_tolerance()
    {
        var grid = Grid();
        GeoPoint[] route = [new(50.055, 19.931), new(50.055, 19.939)];

        var quietOnly = RouteNoiseRules.Above(grid, route, maxLevel: 1);
        var tolerant = RouteNoiseRules.Above(grid, route, maxLevel: 3);

        Assert.NotNull(quietOnly);
        Assert.InRange(quietOnly.LoudM, 250, 330);
        Assert.Equal(70, quietOnly.LoudestDb);
        Assert.Null(tolerant);
    }

    [Fact]
    public void Crossing_a_single_loud_street_is_not_worth_a_warning()
    {
        var grid = Grid();
        GeoPoint[] route = [new(50.055, 19.9345), new(50.055, 19.9355)];

        Assert.Null(RouteNoiseRules.Above(grid, route, maxLevel: 1));
    }
}
