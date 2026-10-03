using Application.Trips;
using Domain.Places;

namespace Tests;

public class StopOrderOptimizerTests
{
    [Fact]
    public void Order_keeps_start_and_visits_every_point_once()
    {
        GeoPoint[] points = [new(50.061, 19.937), new(50.054, 19.935), new(50.064, 19.945), new(50.051, 19.944), new(50.062, 19.939)];

        var order = StopOrderOptimizer.Order(points);

        Assert.Equal(0, order[0]);
        Assert.Equal(Enumerable.Range(0, points.Length), order.Order());
    }

    [Fact]
    public void Order_is_not_longer_than_input_order()
    {
        // Punkty na przemian z północy i południa: kolejność wejściowa to zygzak.
        GeoPoint[] points = [new(50.070, 19.930), new(50.045, 19.932), new(50.069, 19.934), new(50.046, 19.936), new(50.068, 19.938)];

        var order = StopOrderOptimizer.Order(points);

        var input = Enumerable.Range(0, points.Length).ToList();
        Assert.True(StopOrderOptimizer.Length(points, order) < StopOrderOptimizer.Length(points, input));
    }
}
