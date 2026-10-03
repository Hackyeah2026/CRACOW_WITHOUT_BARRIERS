using Domain.Places;

namespace Application.Trips;

/// <summary>
/// Układa kolejność przystanków: najbliższy sąsiad, potem poprawka 2-opt. Pierwszy punkt jest startem i zostaje na miejscu.
/// </summary>
public static class StopOrderOptimizer
{
    public static IReadOnlyList<int> Order(IReadOnlyList<GeoPoint> points)
    {
        if (points.Count <= 2)
            return Enumerable.Range(0, points.Count).ToList();

        var order = NearestNeighbour(points);
        TwoOpt(points, order);
        return order;
    }

    public static double Length(IReadOnlyList<GeoPoint> points, IReadOnlyList<int> order)
    {
        double total = 0;
        for (var i = 0; i < order.Count - 1; i++)
            total += points[order[i]].DistanceTo(points[order[i + 1]]);
        return total;
    }

    private static List<int> NearestNeighbour(IReadOnlyList<GeoPoint> points)
    {
        var order = new List<int> { 0 };
        var left = Enumerable.Range(1, points.Count - 1).ToHashSet();
        while (left.Count > 0)
        {
            var current = points[order[^1]];
            var next = left.MinBy(i => current.DistanceTo(points[i]));
            order.Add(next);
            left.Remove(next);
        }
        return order;
    }

    private static void TwoOpt(IReadOnlyList<GeoPoint> points, List<int> order)
    {
        var improved = true;
        while (improved)
        {
            improved = false;
            for (var i = 1; i < order.Count - 1; i++)
            for (var j = i + 1; j < order.Count; j++)
            {
                var before = Length(points, order);
                order.Reverse(i, j - i + 1);
                if (Length(points, order) < before - 0.01)
                    improved = true;
                else
                    order.Reverse(i, j - i + 1);
            }
        }
    }
}
