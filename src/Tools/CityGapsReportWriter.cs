using System.Globalization;
using System.Text;
using System.Text.Json;
using Domain;
using Domain.Places;
using Domain.Transit;

namespace Tools;

/// <summary>
/// Polecenie "report": te same liczby co strona /miasto, policzone z plików katalogu, żeby dało się je odtworzyć
/// bez przeglądarki. Bariery zgłoszone przez mieszkańców są w bazie hosta, więc tu ich nie liczymy.
/// </summary>
public static class CityGapsReportWriter
{
    public static CityGapsReport Build(string dataDir, out DistrictMap? districts)
    {
        var places = PlaceFiles.Read(Path.Combine(dataDir, "places"));
        var districtsPath = Path.Combine(dataDir, "districts.json");
        districts = File.Exists(districtsPath) ? JsonSerializer.Deserialize<DistrictMap>(File.ReadAllText(districtsPath), DomainJson.Options) : null;
        var transitPath = Path.Combine(dataDir, "transit.json");
        var transit = File.Exists(transitPath) ? JsonSerializer.Deserialize<TransitNetwork>(File.ReadAllText(transitPath), DomainJson.Options) : null;
        return CityGaps.Build(places, districts, transit, []);
    }

    public static string Markdown(CityGapsReport r)
    {
        static string P(CoverageStats s, int n) => (s.Share(n) * 100).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + "%";
        static string Row(string name, CoverageStats s) =>
            $"| {name} | {s.Places} | {s.WithWheelchair} ({P(s, s.WithWheelchair)}) | {s.WithoutAny} ({P(s, s.WithoutAny)}) | {s.WithToilet} ({P(s, s.WithToilet)}) | {s.WithAmbientNoise} ({P(s, s.WithAmbientNoise)}) | {s.WithOnSiteNoise} |";

        var md = new StringBuilder();
        md.AppendLine("# Luki w danych i bariery: liczby z raportu");
        md.AppendLine();
        md.AppendLine("Generuje: `dotnet run --project src/Tools -- report krakow` (z plików katalogu w repozytorium). Te same liczby pokazuje strona `/miasto`; na stronie dochodzą bariery potwierdzone przez urząd z bazy hosta.");
        md.AppendLine();
        md.AppendLine("Miejsca to cele wizyt: wszystkie kategorie katalogu poza ławkami, przystankami i kopertami. „Bez danych” nie liczy hałasu otoczenia z mapy, bo mapa obejmuje każde miejsce.");
        md.AppendLine();
        md.AppendLine("| Grupa | Miejsca | Z wejściem dla wózków | Bez danych o dostępności | Z danymi o toalecie | Z hałasem otoczenia | Z hałasem w miejscu |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        md.AppendLine(Row("**Razem**", r.Total));
        foreach (var g in r.Groups)
            md.AppendLine(Row(g.Group, g.Stats));
        md.AppendLine();
        md.AppendLine($"## Dzielnice (poza granicami dzielnic: {r.OutsideDistricts} miejsc)");
        md.AppendLine();
        md.AppendLine("| Dzielnica | Miejsca | Z wejściem dla wózków | Bez danych o dostępności | Z danymi o toalecie | Z hałasem otoczenia | Z hałasem w miejscu |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var d in r.Districts)
            md.AppendLine(Row($"{d.Roman} {d.Name}", d.Stats));
        md.AppendLine();
        md.AppendLine($"## 20 miejsc do sprawdzenia w pierwszej kolejności (z {r.PriorityCandidates} kandydatów, rozkład z dnia {r.TrafficDay:dd.MM.yyyy})");
        md.AppendLine();
        md.AppendLine(CityGaps.RankingRule);
        md.AppendLine();
        md.AppendLine("| # | Miejsce | Kategoria | Dzielnica | Odjazdy w 300 m | Wynik |");
        md.AppendLine("|---|---|---|---|---|---|");
        var rank = 1;
        foreach (var p in r.Priorities)
            md.AppendLine($"| {rank++} | {p.Place.Name}{(p.Place.Address is { } a ? $", {a}" : "")} | {p.Place.Category} | {p.District ?? "–"} | {p.DeparturesNearby} | {p.Score:0} |");
        return md.ToString();
    }
}
