using System.Globalization;
using System.IO.Compression;
using System.Text;
using Domain.Transit;

namespace Tools;

/// <summary>
/// Buduje sieć komunikacji z rozkładu GTFS: przebiegi linii, wszystkie kursy z godzinami i dni kursowania.
/// Kursy zapisujemy zwięźle (start + wariant czasów przejazdu), żeby plik dało się wczytać w przeglądarce.
/// </summary>
public sealed class GtfsTransitSource
{
    public async Task<TransitNetwork> GetNetworkAsync(string gtfsUrl, string sourceName)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"gtfs-{Guid.NewGuid():N}.zip");
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            await using (var file = File.Create(zipPath))
                await (await http.GetStreamAsync(gtfsUrl)).CopyToAsync(file);

            using var zip = ZipFile.OpenRead(zipPath);
            return Build(zip, sourceName);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    private static TransitNetwork Build(ZipArchive zip, string sourceName)
    {
        var routes = Rows(zip, "routes.txt")
            .Where(r => r["route_type"] is "0" or "3")
            .ToDictionary(r => r["route_id"], r => (Name: r["route_short_name"], Kind: r["route_type"] == "0" ? TransitKind.Tram : TransitKind.Bus));

        var (services, serviceIndex) = Services(zip);

        var trips = Rows(zip, "trips.txt")
            .Where(t => routes.ContainsKey(t["route_id"]) && serviceIndex.ContainsKey(t["service_id"]))
            .ToDictionary(t => t["trip_id"], t => (Route: t["route_id"], Service: serviceIndex[t["service_id"]], Headsign: t["trip_headsign"]));

        // Przebieg każdego kursu: (kolejność, przystanek, minuta od północy).
        var tripStops = new Dictionary<string, List<(int Sequence, string Stop, int Minute)>>();
        foreach (var row in Rows(zip, "stop_times.txt"))
        {
            if (!trips.ContainsKey(row["trip_id"]))
                continue;
            if (!tripStops.TryGetValue(row["trip_id"], out var list))
                tripStops[row["trip_id"]] = list = [];
            list.Add((int.Parse(row["stop_sequence"]), row["stop_id"], ParseMinutes(row["departure_time"])));
        }

        var stopRows = Rows(zip, "stops.txt").ToDictionary(s => s["stop_id"]);
        var stops = new List<TransitStop>();
        var stopIndex = new Dictionary<string, int>();
        int IndexOf(string stopId)
        {
            if (stopIndex.TryGetValue(stopId, out var index))
                return index;
            var row = stopRows[stopId];
            stops.Add(new TransitStop(row["stop_name"],
                Math.Round(double.Parse(row["stop_lat"], CultureInfo.InvariantCulture), 5),
                Math.Round(double.Parse(row["stop_lon"], CultureInfo.InvariantCulture), 5)));
            return stopIndex[stopId] = stops.Count - 1;
        }

        var lines = new List<TransitLine>();
        foreach (var route in tripStops.GroupBy(t => trips[t.Key].Route))
        {
            // Jeden przebieg = jedna kolejność przystanków. Kursy różnią się startem i wariantem czasów przejazdu.
            var patterns = new List<TransitPattern>();
            foreach (var sameStops in route
                         .Select(t => (Trip: trips[t.Key], Stops: t.Value.OrderBy(s => s.Sequence).ToList()))
                         .Where(t => t.Stops.Count >= 2)
                         .GroupBy(t => string.Join(',', t.Stops.Select(s => s.Stop)))
                         .OrderByDescending(g => g.Count()))
            {
                var profiles = new List<IReadOnlyList<int>>();
                var profileIndex = new Dictionary<string, int>();
                var flat = new List<int>();

                foreach (var trip in sameStops.OrderBy(t => t.Stops[0].Minute))
                {
                    var start = trip.Stops[0].Minute;
                    var offsets = trip.Stops.Select(s => s.Minute - start).ToList();
                    var key = string.Join(',', offsets);
                    if (!profileIndex.TryGetValue(key, out var profile))
                    {
                        profileIndex[key] = profile = profiles.Count;
                        profiles.Add(offsets);
                    }
                    flat.AddRange([start, profile, trip.Trip.Service]);
                }

                var headsign = sameStops.GroupBy(t => t.Trip.Headsign).MaxBy(g => g.Count())!.Key;
                patterns.Add(new TransitPattern(headsign, sameStops.First().Stops.Select(s => IndexOf(s.Stop)).ToList(), profiles, flat));
            }

            if (patterns.Count > 0)
                lines.Add(new TransitLine(routes[route.Key].Name, routes[route.Key].Kind, patterns));
        }

        var feed = Rows(zip, "feed_info.txt").FirstOrDefault();
        var today = DateOnly.FromDateTime(DateTime.Now);
        DateOnly FeedDate(string column, DateOnly fallback) =>
            feed is not null && DateOnly.TryParseExact(feed.GetValueOrDefault(column), "yyyyMMdd", out var date) ? date : fallback;

        return new TransitNetwork(sourceName, FeedDate("feed_start_date", today), FeedDate("feed_end_date", today), today, stops,
            lines.OrderBy(l => l.Kind).ThenBy(l => l.Name.PadLeft(4, '0')).ToList(), services);
    }

    private static (List<TransitService> Services, Dictionary<string, int> Index) Services(ZipArchive zip)
    {
        static DateOnly Date(string value) => DateOnly.ParseExact(value, "yyyyMMdd");
        string[] days = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

        var exceptions = Rows(zip, "calendar_dates.txt").ToLookup(r => r["service_id"]);
        var regular = Rows(zip, "calendar.txt").ToDictionary(r => r["service_id"]);

        var services = new List<TransitService>();
        var index = new Dictionary<string, int>();
        foreach (var id in regular.Keys.Concat(exceptions.Select(e => e.Key)).Distinct())
        {
            var weekdays = 0;
            DateOnly? start = null, end = null;
            if (regular.TryGetValue(id, out var row))
            {
                for (var d = 0; d < days.Length; d++)
                    if (row[days[d]] == "1")
                        weekdays |= 1 << d;
                start = Date(row["start_date"]);
                end = Date(row["end_date"]);
            }

            index[id] = services.Count;
            services.Add(new TransitService(weekdays, start, end,
                exceptions[id].Where(e => e["exception_type"] == "1").Select(e => Date(e["date"])).Order().ToList(),
                exceptions[id].Where(e => e["exception_type"] == "2").Select(e => Date(e["date"])).Order().ToList()));
        }
        return (services, index);
    }

    private static int ParseMinutes(string time)
    {
        var parts = time.Split(':');
        return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
    }

    private static IEnumerable<Dictionary<string, string>> Rows(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry is null)
            yield break;

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var header = SplitCsv(reader.ReadLine() ?? "").Select(h => h.Trim('﻿')).ToArray();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
                continue;
            var values = SplitCsv(line);
            var row = new Dictionary<string, string>(header.Length);
            for (var i = 0; i < header.Length; i++)
                row[header[i]] = i < values.Count ? values[i] : "";
            yield return row;
        }
    }

    private static List<string> SplitCsv(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else current.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { values.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        values.Add(current.ToString());
        return values;
    }
}
