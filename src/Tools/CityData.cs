using System.Text.Json;
using Domain;
using Domain.Places;

namespace Tools;

/// <summary>Uzupełnienie katalogu o dane miejskie (MSIP). Każde źródło jest opcjonalne i zależy od konfiguracji miasta.</summary>
public static class CityData
{
    /// <summary>Warunki ponownego wykorzystania danych portali miasta (MSIP jest częścią portalu Otwarte Dane).</summary>
    public const string CityPortalLicense =
        "Warunki portalu Otwarte Dane Krakowa: swobodne ponowne wykorzystanie z podaniem źródła (Gmina Miejska Kraków) oraz czasu wytworzenia i pozyskania danych";

    /// <param name="dataDir">Katalog danych miasta: trafia tam siatka hałasu (noise.json) do sprawdzania tras w przeglądarce.</param>
    /// <param name="sources">Tu trafia opis każdego użytego zbioru: liczba rekordów, daty, zastosowanie.</param>
    public static async Task<List<Place>> ApplyAsync(List<Place> places, string cityId, CityImport city, HttpClient http, string dataDir,
        List<DataSource> sources)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Najpierw toalety: nowe obiekty z rejestru miasta dostają potem hałas otoczenia jak pozostałe miejsca.
        if (city.PublicToiletsLayer is { } toiletsLayer)
        {
            var toilets = await new MsipToiletsSource(http).LoadAsync(toiletsLayer);
            places = MsipToiletsSource.Apply(places, toilets, cityId);
            var added = places.Count(p => p.Id.StartsWith(MsipToiletsSource.IdPrefix));
            Console.WriteLine($"Toalety publiczne: {toilets.Count - added} połączone z OSM, {added} nowych");
            sources.Add(new DataSource("Toalety publiczne (rejestr ISDP)", "Gmina Miejska Kraków, MSIP", toiletsLayer, CityPortalLicense,
                "stan 2023", today, toilets.Count,
                $"Dostosowanie dla osób z niepełnosprawnościami i jego rodzaj, przewijak, godziny; toalety jako miejsca przerwy na trasie. {toilets.Count - added} połączone z toaletami z OSM.", true));
        }

        if (city.StopSheltersLayer is { } sheltersLayer)
        {
            var shelters = await new ZtpSheltersSource(http).LoadAsync(sheltersLayer);
            places = ZtpSheltersSource.Apply(places, shelters, out var unmatched);
            var withShelter = places.Count(p => p.Features.Any(f => f.Source == ZtpSheltersSource.Source));
            var conflicts = places.Count(p => p.Category == PlaceCategory.Stop && p.ConflictsOn(FeatureKey.StopShelter).Count > 0);
            Console.WriteLine($"Wiaty: {withShelter} przystanków z wiatą z rejestru ZTP, {unmatched} wiat bez przystanku w katalogu, " +
                              $"{conflicts} przystanków, przy których OSM podaje inaczej");
            sources.Add(new DataSource("Wiaty przystankowe KMK", "Zarząd Transportu Publicznego w Krakowie, MSIP", sheltersLayer, CityPortalLicense,
                "import do MSIP 10.10.2026", today, shelters.Count,
                $"Wiata na przystanku (schronienie w czasie czekania); {withShelter} przystanków, przy {conflicts} OSM podaje inaczej.", true));
        }

        if (city.StopsLayer is { } stopsLayer)
        {
            var registry = await new ZtpStopsSource(http).LoadAsync(stopsLayer);
            places = ZtpStopsSource.Apply(places, registry, out var matched);
            var onRequest = places.Count(p => p.Features.Any(f => f.Key == FeatureKey.RequestStop && f.State == FeatureState.Yes));
            Console.WriteLine($"Przystanki na żądanie: {matched} przystanków z informacją z rejestru ZTP, w tym {onRequest} na żądanie");
            sources.Add(new DataSource("Przystanki KMK", "Zarząd Transportu Publicznego w Krakowie, MSIP", stopsLayer, CityPortalLicense,
                "import do MSIP 10.10.2026", today, registry.Count,
                $"Przystanek na żądanie (ważne dla osób niewidomych); informacja dla {matched} przystanków, {onRequest} na żądanie.", true));
        }

        if (city.ServicePointsLayer is { } pointsLayer)
        {
            var points = await new MsipServicePointsSource(http).LoadAsync(pointsLayer);
            places = MsipServicePointsSource.Apply(places, points, cityId, out var matched);
            Console.WriteLine($"Punkty obsługi mieszkańców: {matched} połączone z urzędami z OSM, {points.Count - matched} nowych");
            sources.Add(new DataSource("Dzienniki podawcze i punkty obsługi mieszkańców UMK", "Gmina Miejska Kraków, MSIP", pointsLayer, CityPortalLicense,
                "brak daty w usłudze", today, points.Count, "Urzędy w trybie „Załatwiam sprawę”: adres i link do godzin otwarcia w BIP.", true));
        }

        if (city.DisabledParkingLayer is { } parkingLayer)
        {
            var snapshot = await new ZdmkParkingSource(http).LoadAsync(parkingLayer);
            var groups = ZdmkParkingSource.Group(snapshot.Spaces);
            places = ZdmkParkingSource.Apply(places, groups, snapshot.EditedOn, cityId, out var matched);
            Console.WriteLine($"Koperty ZDMK: {groups.Count} miejsc ({snapshot.Spaces.Count} stanowisk), {matched} połączonych z OSM, {groups.Count - matched} nowych");
            sources.Add(new DataSource("Miejsca postojowe dla pojazdów osób z niepełnosprawnościami", "Zarząd Dróg Miasta Krakowa (ArcGIS Online Gminy Miejskiej Kraków)",
                parkingLayer, "Dane publiczne, licencja niepodana; nie pochodzą z portali MSIP ani Otwarte Dane",
                snapshot.EditedOn?.ToString("dd.MM.yyyy") ?? "brak daty", today, snapshot.Spaces.Count,
                $"Najbliższe koperty przy miejscu i start trasy pieszej; {groups.Count} miejsc, {matched} połączonych z OSM.", false));
        }

        if (city.DistrictsLayer is { } districtsLayer)
        {
            var districts = await new MsipDistrictsSource(http).LoadAsync(districtsLayer);
            var districtsPath = Path.Combine(dataDir, "districts.json");
            await File.WriteAllTextAsync(districtsPath, JsonSerializer.Serialize(districts, DomainJson.Options));
            Console.WriteLine($"Dzielnice: {districts.Districts.Count}, {new FileInfo(districtsPath).Length / 1024} KB");
            sources.Add(new DataSource("Dzielnice administracyjne", "Gmina Miejska Kraków, MSIP", districtsLayer, CityPortalLicense,
                districts.DataDate?.ToString("dd.MM.yyyy") ?? "brak daty", today, districts.Districts.Count,
                "Rozbicie raportu „Luki w danych i bariery” na dzielnice i kartogram pokrycia danymi.", true));
        }

        if (city.NoiseLayers is { Count: > 0 } layers)
        {
            var map = await new MsipNoiseSource(http).LoadAsync(layers);
            places = MsipNoiseSource.Apply(places, map);
            var grid = MsipNoiseSource.BuildGrid(map, city.South, city.West, city.North, city.East);
            var gridPath = Path.Combine(dataDir, "noise.json");
            await File.WriteAllTextAsync(gridPath, JsonSerializer.Serialize(grid, DomainJson.Options));
            Console.WriteLine($"Siatka hałasu: {grid.Rows} × {grid.Cols} komórek, {new FileInfo(gridPath).Length / 1024} KB");
            var noisy = places.Count(p => p.Feature(FeatureKey.AmbientNoiseDb) is not null);
            sources.Add(new DataSource("Mapa hałasu 2022 (LDWN, ruch drogowy i szynowy)", "Gmina Miejska Kraków, MSIP", string.Join(" ; ", layers),
                CityPortalLicense, "2022", today, map.AreaCount,
                $"Hałas otoczenia {noisy} miejsc i odcinków trasy dla profili sensorycznych (najwyżej „z ograniczeniami”).", true));
            Console.WriteLine($"Hałas otoczenia: {places.Count(p => p.Feature(FeatureKey.AmbientNoiseDb) is not null)} miejsc " +
                              $"z {places.Count(MsipNoiseSource.AppliesTo)} sprawdzanych");
        }

        return places;
    }
}
