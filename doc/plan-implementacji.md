# Plan implementacji: Kraków bez barier

Uszczegółowienie [plan-prac.md](plan-prac.md) do poziomu plików, kontraktów i kolejności prac. Wymagania konkursu: [WYMAGANIA_KONKURSU.md](WYMAGANIA_KONKURSU.md).

**Stan wyjściowy (3.10, 12:00):** solution `CracowWithoutBarriers.slnx` z siedmioma projektami w `src/`, szablon Blazor Web App na .NET 10, prerendering wyłączony, MediatR 12.4.1 w `Application`. Projekty `Domain`, `Application`, `Infrastructure` są puste.

**Osoby:** A = interfejs, B = domena i logika, C = dane, mapa, integracje (jak w sekcji 9 planu prac).

## 1. Decyzje techniczne do przyjęcia na starcie

| Temat | Decyzja | Powód |
|---|---|---|
| Gdzie leżą dane miejsc | `src/Web.Client/wwwroot/data/{miasto}/places.json` (na start `krakow`) plus `data/cities.json` | klient pobiera plik statyczny, host czyta ten sam plik dla API; kolejne miasto to kolejny katalog |
| Zasięg | Kraków w pełnym zakresie (OSM + dane miejskie + ręczne uzupełnienia), inne miasta w zakresie ograniczonym (tylko OSM) | aplikacja jest przede wszystkim dla Krakowa; drugie miasto pokazuje skalowalność |
| Hosting | VPS Mikrus, aplikacja jako usługa `systemd` (Kestrel) na porcie przydzielonym w panelu | jeden proces, bez bazy; publikacja budowana lokalnie, bo VPS ma mało pamięci |
| LLM | OpenAI, wywoływany wyłącznie przez host | klucz nie trafia do klienta |
| Podział `Infrastructure` | foldery `Browser/` (IndexedDB, klienci HTTP do hosta) i `Server/` (OpenRouteService, LLM) | jeden projekt, ale klient rejestruje tylko `Browser`, host tylko `Server` + katalog |
| Rejestracja usług | `AddApplication()`, `AddBrowserInfrastructure()`, `AddServerInfrastructure()` | `Program.cs` w obu projektach zostaje krótki |
| Wartość cechy | trójstanowa: `Yes` / `No` / `Unknown` plus opcjonalna liczba | zasada "brak danych to nie dostępność" wymuszona typem |
| Kolejność przystanków | najpierw odległość w linii prostej (haversine), macierz z routingu jako ulepszenie | planer działa bez sieci i bez limitu API; przy 5-8 punktach różnica jest mała |
| Wynik handlerów | `Result` / `Result<T>` przez własne `ICommand` / `IQuery` nad MediatR | błędy biznesowe bez wyjątków |
| IndexedDB | własna cienka nakładka JS (`wwwroot/js/localStore.js`) z operacjami `get`, `put`, `delete`, `list` | cztery funkcje, bez zależności i pytań o licencję |
| Mapa | Leaflet z plików lokalnych w `wwwroot/lib/leaflet`, kafelki OSM | bez CDN, działa też w trybie offline |
| Klucze API | `dotnet user-secrets` lokalnie, zmienne środowiskowe na serwerze | nic nie trafia do repozytorium |

## 2. Kontrakty (ustalić 11:00-12:00, potem nie zmieniać bez uzgodnienia)

Kontrakty pozwalają trzem osobom pracować równolegle: A i C mogą budować na zaślepkach, zanim B skończy logikę.

### Domain

```csharp
public enum PlaceCategory { Attraction, Museum, Office, Clinic, Library, Culture, Stop, Toilet, Bench, QuietSpot, Food, DisabledParking }
public enum AppMode { Sightseeing, Errand }
public enum FeatureState { Unknown, Yes, No }
public enum AssessmentStatus { Unknown, Accessible, Limited, Inaccessible }

public sealed record AccessibilityFeature(
    FeatureKey Key, FeatureState State, double? Value, string Source, DateOnly? CheckedOn, bool IsDemoData);

public sealed record Place(
    string Id, string CityId, string Name, PlaceCategory Category, double Lat, double Lon,
    string? Address, string? Description, IReadOnlyList<AccessibilityFeature> Features);

public sealed record Assessment(AssessmentStatus Status, IReadOnlyList<AssessmentReason> Reasons);
public sealed record AssessmentReason(FeatureKey Key, ReasonKind Kind, string Message);   // Kind: Barrier, Amenity, Missing
```

`City`: `Id`, `Name`, środek i zasięg mapy, `Coverage` (`Full` / `OsmOnly`), lista źródeł danych do wyświetlenia w stopce.

`FeatureKey` (enum): `StepFreeEntrance`, `ThresholdHeightCm`, `DoorWidthCm`, `Elevator`, `AccessibleToilet`, `SurfaceCobblestone`, `InclinePercent`, `Stairs`, `TactilePaving`, `AudioDescription`, `AssistanceDogAllowed`, `InductionLoop`, `SignLanguage`, `VisualInformation`, `NoiseLevel`, `CrowdLevel`, `BrightLight`, `LowFloorStop`, `StopShelter`, `QuietHours`, `QuietRoom`, `EasyToReadText`, `Pictograms`, `Benches`, `SeatingInside`.

`NeedsProfile`: lista wybranych presetów plus parametry z sekcji 5 planu prac (m.in. `MaxThresholdCm`, `MaxInclinePercent`, `AvoidStairs`, `AvoidCobblestone`, `MinDoorWidthCm`, `NeedsElevator`, `NeedsAccessibleToilet`, `NoiseTolerance`, `CrowdTolerance`, `MaxDistanceWithoutRestM`, `WalkingSpeedKmh`, `BreakEveryMin`, `NeedsBenches`). Presety to statyczne fabryki w `NeedsProfilePresets`, łączenie profili bierze wartość bardziej restrykcyjną.

`TripPlan`: `Id`, `Mode`, `CreatedAt`, lista `TripStop` (miejsce, kolejność, ocena, czy przerwa) i lista `TripLeg` (dystans, czas, geometria, bariery na odcinku).

### Application: interfejsy

```csharp
public interface IPlaceCatalog   { Task<IReadOnlyList<City>> GetCitiesAsync(CancellationToken ct);
                                   Task<IReadOnlyList<Place>> GetAllAsync(string cityId, CancellationToken ct); }
public interface ILocalStore     { Task<T?> GetAsync<T>(string store, string key); Task PutAsync<T>(string store, string key, T value);
                                   Task DeleteAsync(string store, string key); Task<IReadOnlyList<T>> ListAsync<T>(string store); }
public interface IRoutingClient  { Task<Result<RouteLeg>> GetRouteAsync(RouteRequest request, CancellationToken ct); }
public interface IPlanDescriber  { Task<Result<string>> DescribeAsync(TripPlan plan, NeedsProfile profile, CancellationToken ct); }
```

Magazyny IndexedDB: `profile`, `plans`, `reports`, `routeCache`.

### Application: zapytania i komendy

| Nazwa | Zwraca | Kto używa |
|---|---|---|
| `GetPlacesQuery(mode, kategorie, fraza)` | miejsca z oceną pod bieżący profil | lista, mapa |
| `GetPlaceDetailsQuery(id)` | miejsce, ocena, powody | karta miejsca |
| `GetSuggestionsQuery(mode, punkt startu, limit)` | ranking miejsc | podpowiedzi |
| `GetProfileQuery` / `SaveProfileCommand` | profil z IndexedDB | kreator |
| `BuildTripPlanCommand(idMiejsc, start)` | `TripPlan` | planer |
| `SaveTripPlanCommand` / `GetSavedPlansQuery` | plany z IndexedDB | widok planu |
| `DescribePlanQuery(plan)` | opis w prostym języku | widok planu |
| `ReportBarrierCommand` | zapis zgłoszenia | karta miejsca (zakres opcjonalny) |

### Endpointy hosta

| Metoda i ścieżka | Cel | Typ |
|---|---|---|
| `POST /api/route` | pośrednik do OpenRouteService | wewnętrzny |
| `POST /api/describe` | pośrednik do LLM | wewnętrzny |
| `GET /api/v1/places` | katalog z filtrami | publiczny, OpenAPI |
| `GET /api/v1/places/{id}` | szczegóły miejsca | publiczny, OpenAPI |
| `POST /api/v1/places/{id}/assessment` | ocena miejsca dla profilu z treści żądania | publiczny, OpenAPI |

## 3. Źródła danych

Import działa poza aplikacją (projekt `Tools`), a wynik jest plikiem w repozytorium. Aplikacja w trakcie działania nie odpytuje żadnego z tych serwisów, więc ich awaria nie psuje demo.

| Źródło | Co bierzemy | Jak | Stan (sprawdzone 3.10) |
|---|---|---|---|
| OpenStreetMap | miejsca wszystkich kategorii, tagi `wheelchair`, `kerb`, `incline`, `surface`, `tactile_paving`, `toilets:wheelchair`, ławki, toalety | Overpass API, zapytanie po obszarze miasta | do napisania; jedyne źródło dla innych miast |
| ZTP Kraków, GTFS | przystanki: nazwa, położenie, słupek | `https://gtfs.ztp.krakow.pl/GTFS_KRK.zip`, plik `stops.txt` | działa, ok. 17 MB, aktualizowany codziennie |
| ZTP Kraków, ArcGIS Hub | wiaty przystankowe, koperty dla osób z niepełnosprawnościami | zapytania `FeatureServer/.../query?f=geojson` | adresy warstw do ustalenia ręcznie na stronie huba |
| MSIP Kraków | budynki publiczne i jednostki miejskie, punkty adresowe, mapa hałasu 2022 | ArcGIS REST `MapServer/.../query?f=geojson` | katalog usług działa pod innym adresem, niż zakładaliśmy |
| Ręczne uzupełnienia | cechy sensoryczne i szczegóły z deklaracji dostępności dla miejsc demo | `overrides.json` | oznaczone `IsDemoData` |

Ustalenia z weryfikacji, które zmieniają plan:

- **GTFS nie zawiera danych o dostępności.** Kolumna `wheelchair_boarding` w `stops.txt` jest pusta, a `wheelchair_accessible` w `trips.txt` ma wartość 0 (brak informacji); sprawdzone na pliku tramwajowym `GTFS_KRK_T.zip`. GTFS daje więc tylko listę i położenie przystanków. Cechy przystanku (wiata, krawężnik, ścieżki dotykowe) muszą przyjść z warstw ZTP i z OSM, a tam, gdzie ich nie ma, pokazujemy "nieznane".
- **Adres MSIP z notatek zwraca 404.** `https://msip3.um.krakow.pl/server/...` nie odpowiada; działający katalog usług to `https://msip.um.krakow.pl/arcgis/rest/services` (ArcGIS Server 10.81). Przydatne foldery: `Obserwatorium` (m.in. budynki z adresami, pitniki), `ZSOZ` (adresy, BDOT), `Mapa_halasu_2022`.
- **Mapa hałasu 2022 z MSIP** może dać poziom hałasu otoczenia dla profilu sensorycznego z prawdziwych danych miejskich zamiast wpisów ręcznych. To mocny argument w kryterium "Pomysł", ale nie sprawdziliśmy jeszcze, czy warstwy da się odpytać o wartość w punkcie; robimy po podstawowym imporcie.
- **Lista zbiorów ZTP nie dała się pobrać automatycznie** (interfejs huba zwraca katalog globalny). Osoba C ustala adresy warstw `FeatureServer` ręcznie ze strony huba.
- **W przejrzanych folderach MSIP nie widać gotowych atrybutów dostępności budynków.** Dane miejskie dają wiarygodną listę i położenie obiektów; szczegóły dostępności pochodzą z OSM i z uzupełnień ręcznych.

### Łączenie źródeł

1. Każde źródło implementuje `IPlaceSource` i zwraca miejsca z cechami opisanymi źródłem i datą.
2. `PlaceMerger` łączy rekordy tego samego obiektu (odległość do 30 m i podobna nazwa); cechy z różnych źródeł się sumują.
3. Przy konflikcie wygrywa: uzupełnienie ręczne → dane miejskie → OSM. Przegrana wartość nie jest wyświetlana.
4. Karta miejsca pokazuje źródło każdej cechy; stopka aplikacji zawiera wymagane przypisy (OSM na licencji ODbL, dane ZTP i MSIP wg ich regulaminów, do sprawdzenia).

### Inne miasta

Tryb ograniczony to ten sam import z samym źródłem OSM: `import <miasto>` generuje `data/<miasto>/places.json`, a wpis w `cities.json` ma `Coverage = OsmOnly`. W interfejsie użytkownik wybiera miasto, a przy trybie ograniczonym widzi komunikat, że dane pochodzą wyłącznie z OpenStreetMap i częściej będą "nieznane". Silnik oceny, podpowiedzi i planer działają bez zmian. Na hackathon wystarczy jedno dodatkowe miasto, dodane dopiero po kamieniu milowym 05:00.

## 4. Docelowy układ plików

```
src/Domain/
  Places/        Place, AccessibilityFeature, FeatureKey, PlaceCategory
  Needs/         NeedsProfile, NeedsProfilePresets
  Assessment/    AssessmentEngine, Assessment, reguły (IAssessmentRule + implementacje)
  Trips/         TripPlan, TripStop, TripLeg
src/Application/
  Abstractions/  ICommand, IQuery, Result, IPlaceCatalog, ILocalStore, IRoutingClient, IPlanDescriber
  Places/        GetPlaces, GetPlaceDetails, GetSuggestions (+ SuggestionRanker)
  Profile/       GetProfile, SaveProfile
  Trips/         BuildTripPlan (+ StopOrderOptimizer, BreakInserter), SaveTripPlan, GetSavedPlans, DescribePlan
  DependencyInjection.cs
src/Infrastructure/
  Catalog/       JsonPlaceCatalog (HTTP w kliencie, plik na hoście), PlaceDto
  Browser/       IndexedDbLocalStore, HostRoutingClient, HostPlanDescriber
  Server/        OpenRouteServiceClient, LlmPlanDescriber (OpenAI), opcje konfiguracji
  DependencyInjection.cs
src/Web/
  Endpoints/     RouteEndpoints, DescribeEndpoints, PlacesEndpoints
  Program.cs     OpenAPI, rejestracja usług, mapowanie endpointów
src/Web.Client/
  Pages/         Home (wybór trybu), Profile, Places, PlaceDetails, Plan
  Components/    MapView, PlaceCard, AssessmentBadge, ReasonList, ProfileSummary, LoadingScreen
  Services/      AppState (tryb, profil, koszyk miejsc; zdarzenie zmiany)
  wwwroot/       data/cities.json, data/krakow/places.json, js/localStore.js, js/map.js, lib/leaflet
src/Tools/
  Sources/       OsmOverpassSource, GtfsStopsSource, ZtpArcGisSource, MsipArcGisSource (wspólny IPlaceSource)
  Merge/         PlaceMerger (łączenie po odległości i nazwie), OverridesApplier (overrides.json)
  Program.cs     `import <miasto>` -> data/<miasto>/places.json
src/Tests/       AssessmentEngineTests, SuggestionRankerTests, StopOrderOptimizerTests, BreakInserterTests
```

## 5. Etapy

Każdy etap kończy się stanem, który da się pokazać. Zadania w etapie są ułożone tak, żeby nikt nie czekał na drugą osobę.

### Etap 0 · Start (do 12:00)

| Kto | Zadanie | Gotowe, gdy |
|---|---|---|
| B | solution, repozytorium, prerendering wyłączony | **zrobione** |
| B | deploy pustej aplikacji na VPS Mikrus: `dotnet publish` lokalnie, kopiowanie przez `scp`, usługa `systemd`, port i subdomena z panelu | publiczny adres zwraca stronę startową |
| Wszyscy | przyjęcie kontraktów z sekcji 2 | typy z sekcji "Domain" są w repozytorium |
| C | klucze OpenRouteService i OpenAI, lista 20-25 miejsc demo, adresy warstw ZTP i MSIP | klucze w `user-secrets`, lista miejsc i adresów w `doc/` |
| A | szkic ekranów, układ strony | ustalona nawigacja: Start → Profil → Miejsca → Plan |

### Etap 1 · Fundament (12:00-17:00)

| Kto | Zadanie | Gotowe, gdy |
|---|---|---|
| B | typy domenowe, `Result`, `ICommand` / `IQuery`, `AddApplication()` | solution się buduje, typy dostępne dla A i C |
| B | `JsonPlaceCatalog` + `GetPlacesQuery`, `GetPlaceDetailsQuery` (ocena na razie `Unknown`) | lista miejsc zwracana z pliku |
| B | `localStore.js` + `IndexedDbLocalStore`, `GetProfileQuery`, `SaveProfileCommand` | profil przeżywa odświeżenie strony |
| B | `POST /api/route` jako pośrednik (klucz po stronie hosta) | zapytanie z klienta zwraca geometrię trasy |
| C | `Tools`: `OsmOverpassSource` dla Krakowa, mapowanie tagów OSM na `FeatureKey`, zapis `data/krakow/places.json` | plik z kilkuset miejscami, każda cecha ma źródło |
| C | `GtfsStopsSource` (przystanki z `stops.txt`), potem `ZtpArcGisSource` (wiaty, koperty) i `MsipArcGisSource` (budynki publiczne) + `PlaceMerger` | przystanki i obiekty miejskie w katalogu, bez duplikatów z OSM |
| C | `overrides.json` z ręcznymi cechami miejsc demo (oznaczone `IsDemoData`) | 20-25 miejsc ma komplet cech dla trzech person |
| C | Leaflet: `map.js` + komponent `MapView` (pinezki, kliknięcie, kolor) | mapa pokazuje miejsca z katalogu |
| A | usunięcie stron szablonu (`Counter`, `Weather`), układ, nawigacja, `AppState` | cztery puste strony z nawigacją |
| A | ekran startowy z wyborem trybu, lista miejsc z filtrem kategorii, szkielet karty i kreatora | lista działa na danych z `GetPlacesQuery` |

**Kamień milowy 17:00:** mapa Krakowa z miejscami z prawdziwych danych, wdrożona na serwerze.

### Etap 2 · Personalizacja (17:00-23:00)

| Kto | Zadanie | Gotowe, gdy |
|---|---|---|
| B | `AssessmentEngine`: reguły dla ruchu, sensoryki i kondycji | testy trzech person przechodzą |
| B | proste reguły dla pozostałych obszarów (wzrok, słuch, poznawcze) | parametr w profilu zmienia ocenę |
| B | `SuggestionRanker` + `GetSuggestionsQuery` | ranking różny dla Marty i Kuby |
| A | kreator profilu: wybór presetów, dostrajanie parametrów, zapis | zmiana profilu od razu odświeża listę i mapę |
| A | karta miejsca: status, bariery, udogodnienia, braki danych, źródło i data | powody czytelne bez znajomości modelu |
| A | `AssessmentBadge` z kolorem, ikoną i tekstem | status czytelny bez rozróżniania kolorów |
| C | mapowanie `NeedsProfile` na parametry OpenRouteService (profil `wheelchair` lub `foot-walking`, ograniczenia nawierzchni, nachylenia, krawężnika) | trasa Marty omija schody |
| C | rysowanie pojedynczej trasy A→B na mapie | linia i czas przejścia widoczne |
| C | szkic 10 slajdów i opisu projektu | nagłówki i tezy gotowe |

**Kamień milowy 23:00:** zmiana profilu zmienia oceny i podpowiedzi; trasa A→B rysuje się na mapie.

#### Zasady silnika oceny

- Każda reguła dostaje profil i cechy miejsca, zwraca zero lub więcej powodów.
- Bariera twarda (np. schody bez windy dla wózka) → `Inaccessible`.
- Bariera miękka lub wartość bliska progu → `Limited`.
- Cecha wymagana przez profil, ale `Unknown` → powód typu `Missing`; samo `Missing` daje `Unknown`, nigdy `Accessible`.
- `Accessible` tylko wtedy, gdy wszystkie cechy wymagane przez profil są znane i spełnione.
- Status końcowy to najgorszy ze statusów cząstkowych.

### Etap 3 · Trasy (23:00-05:00)

| Kto | Zadanie | Gotowe, gdy |
|---|---|---|
| B | `StopOrderOptimizer`: najbliższy sąsiad + 2-opt | test: kolejność nie gorsza niż wejściowa |
| B | `BreakInserter`: przerwy wg `MaxDistanceWithoutRestM` i `BreakEveryMin`, dobór toalety, ławki lub cichego miejsca | Pani Zofia dostaje przerwę z ławką |
| B | `BuildTripPlanCommand`: kolejność → odcinki z routingu → przerwy → oceny przystanków | plan dla 5 miejsc w kilka sekund |
| B | zapis planów, cache odpowiedzi routingu w IndexedDB | drugi raz ten sam plan bez wywołania API |
| B | publiczne endpointy `/api/v1/places` + OpenAPI | dokument OpenAPI dostępny pod adresem hosta |
| C | trasa wieloetapowa na mapie, znaczniki barier na odcinkach | odcinki rozróżnialne, bariery klikalne |
| C | `POST /api/describe` + `LlmPlanDescriber` na OpenAI (model dostaje wyłącznie dane planu) | opis zgodny z danymi, bez dopisanych faktów |
| A | widok planu: lista przystanków, odcinki, przerwy, ostrzeżenia, opis AI | plan zrozumiały bez mapy |
| A | tryb "Załatwiam sprawę": inne kategorie domyślne, ten sam zestaw komponentów | Pani Zofia przechodzi cały scenariusz |

**Kamień milowy 05:00:** pełny scenariusz od profilu do planu dla trzech person.

### Etap 4 · Dopracowanie (05:00-08:00)

| Kto | Zadanie |
|---|---|
| A | dostępność interfejsu: kontrast, obsługa klawiaturą, etykiety ARIA, cele dotykowe min. 44 px, widoczny fokus; mapa ma odpowiednik w postaci listy |
| A | ekran ładowania WebAssembly, komunikaty błędów zrozumiałym językiem |
| B | obsługa awarii routingu i LLM: zapisane odpowiedzi dla ścieżki demo, plan bez geometrii zamiast błędu |
| B | README: uruchomienie, architektura, źródła danych i licencje (ODbL dla OSM) |
| C | przegląd danych demo, próba ścieżki demo na wersji z serwera |
| C | hałas z mapy hałasu MSIP dla miejsc demo (jeśli warstwa da się odpytać) |
| Zapas | drugie miasto w trybie `OsmOnly`, zgłoszenie bariery (punkt 10), potem tryb offline (punkt 12) |

**08:00 zamrożenie kodu.** Dalej wg sekcji 10 planu prac: materiały, zgłoszenie, bufor.

## 6. Testy

| Zestaw | Co sprawdza |
|---|---|
| `AssessmentEngineTests` | po jednym miejscu "dobrym", "złym" i "bez danych" dla każdej z trzech person; brak danych nigdy nie daje `Accessible`; łączenie profili |
| `SuggestionRankerTests` | miejsce niedostępne nie trafia na szczyt; tryb filtruje kategorie |
| `StopOrderOptimizerTests` | wynik zawiera wszystkie punkty dokładnie raz; długość nie większa niż wejściowa |
| `BreakInserterTests` | przerwa pojawia się po przekroczeniu dystansu lub czasu; brak przerw dla profilu bez ograniczeń |

Testy silnika oceny powstają razem z regułami w etapie 2, bo to one są pokazywane jury jako dowód jakości (kryterium "Aspekty techniczne").

## 7. Ścieżka demo jako kryterium ukończenia

Kod jest gotowy, gdy na wersji z serwera przechodzą bez błędu trzy scenariusze z filmu:

1. **Marta:** tryb "Zwiedzam" → preset "wózek elektryczny" → podpowiedzi → wybór 4-5 miejsc → plan omijający bruk i progi.
2. **Kuba:** zmiana profilu na "spektrum autyzmu" → te same miejsca mają inne oceny → inna kolejność i ciche przerwy.
3. **Pani Zofia:** tryb "Załatwiam sprawę" → preset "senior" → urząd i przychodnia → karta miejsca z barierami i źródłem danych → trasa z ławkami.

## 8. Zależności blokujące

| Co | Blokuje | Jak rozładować |
|---|---|---|
| typy domenowe (B, do 13:00) | lista i karta (A), import (C) | A i C pracują na typach z sekcji 2 od razu po ich wrzuceniu, bez logiki |
| `data/krakow/places.json` (C, do 15:00) | lista, mapa, silnik | do tego czasu plik z 5 ręcznie wpisanymi miejscami |
| `POST /api/route` (B, do 17:00) | trasy (C) | C testuje parametry bezpośrednio w konsoli OpenRouteService |
| silnik oceny (B, do 21:00) | karta miejsca, kolory na mapie (A) | zapytania od początku zwracają `Assessment`, najpierw zawsze `Unknown` |
| planer (B, do 03:00) | widok planu (A), mapa trasy (C) | A buduje widok na ręcznie złożonym `TripPlan` |

## 9. Otwarte kwestie

- **OpenRouteService:** do weryfikacji później. Sprawdzić limity darmowego klucza, parametry profilu wózkowego i dostępność macierzy czasów. Do tego czasu kolejność liczymy z odległości w linii prostej, a `IRoutingClient` ma zaślepkę zwracającą odcinek prosty.
- **Adresy warstw ZTP i MSIP:** ustalić konkretne adresy `FeatureServer` / `MapServer` (wiaty, koperty, budynki publiczne, mapa hałasu) i warunki wykorzystania danych.
- **Mikrus:** potwierdzić ilość pamięci na serwerze, przydzielony port, subdomenę z HTTPS oraz czy jest tam środowisko uruchomieniowe .NET 10 (jeśli nie, publikacja `--self-contained`).
- **OpenAI:** wybór modelu i limit wydatków na czas demo.
