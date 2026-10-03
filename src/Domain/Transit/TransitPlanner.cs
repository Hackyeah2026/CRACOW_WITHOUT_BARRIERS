using Domain.Places;

namespace Domain.Transit;

/// <summary>
/// Wyszukiwarka połączeń według rozkładu: najwcześniejszy przyjazd, najwyżej jedna przesiadka.
/// Algorytm rundowy (jak RAPTOR): runda 1 to przejazdy bezpośrednie, runda 2 to przejazdy po przesiadce.
/// Kursy rozpoczęte przed północą poprzedniego dnia rozkładowego nie są uwzględniane.
/// </summary>
public sealed class TransitPlanner
{
    private const double DetourFactor = 1.3;          // poprawka odległości w linii prostej na układ ulic
    private const double TransferRadiusM = 200;       // jak daleko można przejść między przystankami przy przesiadce
    private const int TransferBufferMin = 2;          // zapas na przesiadkę
    private const int SearchWindowMin = 120;          // nie proponujemy odjazdów późniejszych niż za 2 godziny
    private const int Never = int.MaxValue;

    private readonly TransitNetwork _network;
    private readonly List<(TransitLine Line, TransitPattern Pattern)> _patterns;
    private readonly List<(int Pattern, int Position)>[] _patternsAtStop;
    private readonly Dictionary<(int, int), List<int>> _grid = [];
    private readonly Dictionary<DateOnly, bool[]> _activeServices = [];

    public TransitPlanner(TransitNetwork network)
    {
        _network = network;
        _patterns = network.Lines.SelectMany(l => l.Patterns.Select(p => (l, p))).ToList();

        _patternsAtStop = new List<(int, int)>[network.Stops.Count];
        for (var s = 0; s < _patternsAtStop.Length; s++)
            _patternsAtStop[s] = [];
        for (var p = 0; p < _patterns.Count; p++)
        {
            var stops = _patterns[p].Pattern.Stops;
            for (var i = 0; i < stops.Count; i++)
                _patternsAtStop[stops[i]].Add((p, i));
        }

        for (var s = 0; s < network.Stops.Count; s++)
        {
            var cell = Cell(network.Stops[s].Location);
            if (!_grid.TryGetValue(cell, out var list))
                _grid[cell] = list = [];
            list.Add(s);
        }
    }

    /// <param name="maxWalkM">Najdłuższe dojście do przystanku i z przystanku.</param>
    /// <param name="directDistanceM">Długość odcinka pieszo; połączenie musi dawać mniej chodzenia.</param>
    public TransitAdvice Plan(
        GeoPoint from, GeoPoint to, DateOnly date, int nowMinute, double maxWalkM, double walkSpeedKmh, double directDistanceM)
    {
        TransitAdvice Advice(TransitStatus status, IReadOnlyList<TransitJourney>? journeys = null) => new(
            status, journeys ?? [], _network.Source, _network.ValidFrom, _network.ValidTo, _network.GeneratedOn, nowMinute);

        var services = ActiveServices(date);
        if (!services.Any(active => active))
            return Advice(TransitStatus.NoTimetable);

        var access = StopsWithin(from, maxWalkM);
        var egress = StopsWithin(to, maxWalkM);
        if (access.Count == 0 || egress.Count == 0)
            return Advice(TransitStatus.NoStopsNearby);

        int WalkMin(double meters) => (int)Math.Ceiling(meters / 1000 / walkSpeedKmh * 60);
        var deadline = nowMinute + SearchWindowMin;
        var stopCount = _network.Stops.Count;

        // labels[k][s]: najwcześniejszy czas na przystanku s po k przejazdach.
        var labels = new int[3][];
        var parents = new Parent?[3][];
        for (var k = 0; k < 3; k++)
        {
            labels[k] = Enumerable.Repeat(Never, stopCount).ToArray();
            parents[k] = new Parent?[stopCount];
        }

        var marked = new HashSet<int>();
        foreach (var (stop, walk) in access)
        {
            labels[0][stop] = nowMinute + WalkMin(walk);
            marked.Add(stop);
        }

        for (var k = 1; k <= 2; k++)
        {
            var improved = new HashSet<int>();
            var buffer = k == 1 ? 0 : TransferBufferMin;

            foreach (var group in marked.SelectMany(s => _patternsAtStop[s]).GroupBy(x => x.Pattern))
            {
                var pattern = _patterns[group.Key].Pattern;
                int? trip = null;
                var boardPosition = 0;

                for (var position = group.Min(x => x.Position); position < pattern.Stops.Count; position++)
                {
                    var stop = pattern.Stops[position];

                    if (trip is { } t)
                    {
                        var arrival = TimeAt(pattern, t, position);
                        if (arrival < labels[k][stop])
                        {
                            labels[k][stop] = arrival;
                            parents[k][stop] = new Parent(group.Key, t, boardPosition, position, null, 0);
                            improved.Add(stop);
                        }
                    }

                    // Czy z tego przystanku da się złapać wcześniejszy kurs?
                    var ready = labels[k - 1][stop];
                    if (ready == Never)
                        continue;
                    var candidate = EarliestTrip(pattern, position, ready + buffer, deadline, services);
                    if (candidate is { } c && (trip is null || TimeAt(pattern, c, position) < TimeAt(pattern, trip.Value, position)))
                    {
                        trip = c;
                        boardPosition = position;
                    }
                }
            }

            // Przejście pieszo do sąsiednich przystanków, żeby w następnej rundzie dało się tam przesiąść.
            foreach (var stop in improved.ToList())
            {
                foreach (var (neighbour, distance) in StopsWithin(_network.Stops[stop].Location, TransferRadiusM))
                {
                    var arrival = labels[k][stop] + WalkMin(distance);
                    if (neighbour != stop && arrival < labels[k][neighbour])
                    {
                        labels[k][neighbour] = arrival;
                        parents[k][neighbour] = new Parent(0, 0, 0, 0, stop, distance);
                        improved.Add(neighbour);
                    }
                }
            }

            marked = improved;
        }

        var journeys = new List<TransitJourney>();
        for (var k = 1; k <= 2; k++)
        {
            var best = egress
                .Where(e => labels[k][e.Stop] != Never)
                .Select(e => (e.Stop, e.Walk, Arrival: labels[k][e.Stop] + WalkMin(e.Walk)))
                .OrderBy(e => e.Arrival)
                .Select(e => Build(k, e.Stop, e.Walk, e.Arrival, access, parents, services, WalkMin))
                .FirstOrDefault(j => j is not null && j.TotalWalkM < directDistanceM);

            // Przesiadkę pokazujemy tylko wtedy, gdy daje wyraźnie wcześniejszy przyjazd niż przejazd bezpośredni.
            if (best is not null && (journeys.Count == 0 || best.ArriveMinute < journeys[0].ArriveMinute - 3))
                journeys.Add(best);
        }

        return journeys.Count > 0
            ? Advice(TransitStatus.Found, journeys.OrderBy(j => j.ArriveMinute).ToList())
            : Advice(TransitStatus.NoConnection);
    }

    private TransitJourney? Build(
        int round, int lastStop, double egressWalk, int arrival, List<(int Stop, double Walk)> access,
        Parent?[][] parents, bool[] services, Func<double, int> walkMin)
    {
        var rides = new List<TransitRide>();
        double transferWalk = 0;
        var stop = lastStop;
        var k = round;

        while (k > 0)
        {
            if (parents[k][stop] is not { } parent)
                return null;

            if (parent.WalkFromStop is { } previous)
            {
                // Przejście po ostatniej jeździe to część dojścia do celu, a nie przesiadka.
                if (rides.Count == 0)
                    egressWalk += parent.WalkM;
                else
                    transferWalk += parent.WalkM;
                stop = previous;
                continue;
            }

            var (line, pattern) = _patterns[parent.Pattern];
            var board = pattern.Stops[parent.BoardPosition];
            var depart = TimeAt(pattern, parent.Trip, parent.BoardPosition);
            rides.Add(new TransitRide(line.Name, line.Kind, pattern.Headsign,
                _network.Stops[board].Name, _network.Stops[stop].Name,
                depart, TimeAt(pattern, parent.Trip, parent.AlightPosition), parent.AlightPosition - parent.BoardPosition,
                NextDepartures(line, board, stop, depart, services),
                pattern.Stops.Skip(parent.BoardPosition).Take(parent.AlightPosition - parent.BoardPosition + 1)
                    .Select(s => _network.Stops[s].Location).ToList()));
            stop = board;
            k--;
        }

        // Pierwsza jazda zawsze zaczyna się na przystanku, do którego da się dojść z punktu startu.
        var accessWalk = access.Where(a => a.Stop == stop).Select(a => (double?)a.Walk).FirstOrDefault();
        if (accessWalk is null || rides.Count == 0)
            return null;

        rides.Reverse();
        return new TransitJourney(rides[0].DepartMinute - walkMin(accessWalk.Value), arrival,
            accessWalk.Value, transferWalk, egressWalk, rides);
    }

    /// <summary>Najbliższe odjazdy danej linii z przystanku wsiadania, które jadą dalej przez przystanek wysiadania.</summary>
    private List<int> NextDepartures(TransitLine line, int boardStop, int alightStop, int fromMinute, bool[] services)
    {
        var departures = new SortedSet<int>();
        foreach (var pattern in line.Patterns)
        {
            var board = IndexOf(pattern.Stops, boardStop);
            if (board < 0 || !pattern.Stops.Skip(board + 1).Contains(alightStop))
                continue;

            for (var t = 0; t < pattern.Trips.Count; t += 3)
            {
                if (!services[pattern.Trips[t + 2]])
                    continue;
                var departure = TimeAt(pattern, t, board);
                if (departure >= fromMinute)
                    departures.Add(departure);
            }
        }
        return departures.Take(3).ToList();
    }

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == value)
                return i;
        return -1;
    }

    private static int TimeAt(TransitPattern pattern, int trip, int position) =>
        pattern.Trips[trip] + pattern.TimeProfiles[pattern.Trips[trip + 1]][position];

    private static int? EarliestTrip(TransitPattern pattern, int position, int notBefore, int deadline, bool[] services)
    {
        int? best = null;
        var bestTime = Never;
        for (var t = 0; t < pattern.Trips.Count; t += 3)
        {
            if (!services[pattern.Trips[t + 2]])
                continue;
            var time = TimeAt(pattern, t, position);
            if (time >= notBefore && time <= deadline && time < bestTime)
            {
                best = t;
                bestTime = time;
            }
        }
        return best;
    }

    private bool[] ActiveServices(DateOnly date)
    {
        if (!_activeServices.TryGetValue(date, out var active))
            _activeServices[date] = active = _network.Services.Select(s => s.RunsOn(date)).ToArray();
        return active;
    }

    private List<(int Stop, double Walk)> StopsWithin(GeoPoint point, double maxWalkM)
    {
        var result = new List<(int, double)>();
        var (x, y) = Cell(point);
        var reach = (int)Math.Ceiling(maxWalkM / DetourFactor / CellSizeM) + 1;

        for (var dx = -reach; dx <= reach; dx++)
        for (var dy = -reach; dy <= reach; dy++)
        {
            if (!_grid.TryGetValue((x + dx, y + dy), out var stops))
                continue;
            foreach (var stop in stops)
            {
                var walk = point.DistanceTo(_network.Stops[stop].Location) * DetourFactor;
                if (walk <= maxWalkM)
                    result.Add((stop, walk));
            }
        }
        return result;
    }

    // Siatka do szybkiego szukania przystanków w pobliżu; komórka ma ok. 250 m.
    private const double CellSizeM = 250;

    private static (int, int) Cell(GeoPoint point) => (
        (int)Math.Floor(point.Lon * 111_320 * Math.Cos(point.Lat * Math.PI / 180) / CellSizeM),
        (int)Math.Floor(point.Lat * 110_540 / CellSizeM));

    /// <summary>Skąd wziął się czas na przystanku: z przejazdu albo z przejścia z innego przystanku.</summary>
    private readonly record struct Parent(int Pattern, int Trip, int BoardPosition, int AlightPosition, int? WalkFromStop, double WalkM);
}
