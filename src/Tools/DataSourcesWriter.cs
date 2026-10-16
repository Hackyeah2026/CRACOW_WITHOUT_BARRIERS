using System.Text;
using System.Text.Json;
using Domain;
using Domain.Places;
using Domain.Transit;

namespace Tools;

/// <summary>
/// Spis źródeł danych po imporcie: data/{miasto}/sources.json dla strony "Źródła danych" i doc/DATA_SOURCES.md dla
/// dokumentacji. Oba z tych samych liczb, więc się nie rozjeżdżają.
/// </summary>
public static class DataSourcesWriter
{
    private const string FileName = "sources.json";
    private const string OsmName = "OpenStreetMap";

    /// <summary>Kiedy pobrano OSM: przy poleceniu "city" katalog OSM jest z wcześniejszego importu, więc bierzemy datę z poprzedniego spisu.</summary>
    public static DateOnly OsmRetrievedOn(string dataDir, bool freshImport)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (freshImport)
            return today;

        var sourcesPath = Path.Combine(dataDir, FileName);
        if (File.Exists(sourcesPath) &&
            JsonSerializer.Deserialize<DataSourcesReport>(File.ReadAllText(sourcesPath), DomainJson.Options)?.Sources
                .FirstOrDefault(s => s.Name == OsmName) is { } previous)
            return previous.RetrievedOn;

        var indexPath = Path.Combine(dataDir, "places", "index.json");
        return File.Exists(indexPath) && JsonSerializer.Deserialize<PlaceIndex>(File.ReadAllText(indexPath), DomainJson.Options) is { } index
            ? index.GeneratedOn
            : today;
    }

    public static void Write(string dataDir, string docPath, IReadOnlyList<Place> places, DateOnly osmRetrievedOn, IEnumerable<DataSource> citySources)
    {
        var sources = new List<DataSource>
        {
            new(OsmName, "Autorzy OpenStreetMap (Overpass API)", "https://www.openstreetmap.org/copyright", "ODbL 1.0", "na bieżąco (wersje obiektów z datami edycji)",
                osmRetrievedOn, places.Count(p => p.Id.StartsWith("osm-")),
                "Katalog miejsc w 18 kategoriach i ich tagi dostępności (wheelchair, toilets:wheelchair, tactile_paving, bench, shelter, hearing_loop).", false)
        };
        sources.AddRange(citySources);

        var transitPath = Path.Combine(dataDir, "transit.json");
        if (File.Exists(transitPath) && JsonSerializer.Deserialize<TransitNetwork>(File.ReadAllText(transitPath), DomainJson.Options) is { } network)
            sources.Add(new DataSource("Rozkład jazdy KMK (GTFS)", network.Source, "https://gtfs.ztp.krakow.pl/", "Zasady udostępniania danych ZTP",
                $"ważny {network.ValidFrom:dd.MM.yyyy}–{network.ValidTo:dd.MM.yyyy}", network.GeneratedOn, network.Stops.Count,
                $"Przejazdy z przesiadką dla odcinków dłuższych niż limit marszu; {network.Lines.Count} linii, liczba rekordów to przystanki.", false));

        sources.Add(new DataSource("OpenRouteService", "Heidelberg Institute for Geoinformation Technology", "https://openrouteservice.org/",
            "Regulamin usługi", "na bieżąco (na danych OSM)", DateOnly.FromDateTime(DateTime.UtcNow), null,
            "Trasy piesze i wózkowe z ograniczeniami z profilu; do usługi trafiają tylko punkty trasy i 3 parametry.", false));

        var report = new DataSourcesReport(DateOnly.FromDateTime(DateTime.UtcNow), sources);
        File.WriteAllText(Path.Combine(dataDir, FileName), JsonSerializer.Serialize(report, DomainJson.Indented));
        File.WriteAllText(docPath, Markdown(report));
        Console.WriteLine($"Źródła danych: {sources.Count} zbiorów, w tym {sources.Count(s => s.CityPortal)} z portali miasta");
    }

    public static string Markdown(DataSourcesReport report)
    {
        static string Cell(string text) => text.Replace("|", "\\|").Replace("\n", " ");
        var md = new StringBuilder();
        md.AppendLine("# Źródła danych");
        md.AppendLine();
        md.AppendLine($"Plik generuje import (`dotnet run --project src/Tools -- city krakow`), stan na {report.GeneratedOn:dd.MM.yyyy}. Te same dane pokazuje w aplikacji strona „Źródła danych” (`/zrodla-danych`).");
        md.AppendLine();
        md.AppendLine("Każda cecha miejsca ma w karcie miejsca swoje źródło i datę. Gdy źródła podają co innego, karta pokazuje obie wartości. Brak danych zawsze daje ocenę „brak danych”, nigdy „dostępne”.");
        md.AppendLine();
        md.AppendLine("| Zbiór | Dostawca | Portal miasta | Licencja | Data danych | Pobrano | Rekordów | Do czego używamy | Adres |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var s in report.Sources)
            md.AppendLine($"| {Cell(s.Name)} | {Cell(s.Provider)} | {(s.CityPortal ? "tak" : "nie")} | {Cell(s.License)} | {Cell(s.DataDate)} | {s.RetrievedOn:dd.MM.yyyy} | " +
                          $"{(s.Records is { } n ? n.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pl-PL")) : "usługa")} | {Cell(s.Usage)} | {Cell(s.Url)} |");
        md.AppendLine();
        md.AppendLine("Dane demonstracyjne zespołu (ręczne uzupełnienia w `src/Tools/overrides/krakow.json`) są oznaczone w aplikacji przy każdej cesze i nie są przedstawiane jako zweryfikowane.");
        return md.ToString();
    }
}
