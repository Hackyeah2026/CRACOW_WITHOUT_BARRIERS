# Stan realizacji: Kraków bez barier

Stan na **3.10.2026, ok. 14:00** (commit `7bf1ae4`). Punkt odniesienia: [plan-prac.md](plan-prac.md) i [plan-implementacji.md](plan-implementacji.md).

**W skrócie:** działa lokalnie pełna ścieżka profil → miejsca → karta miejsca → plan, na danych z OpenStreetMap, z trasami po ulicach (OpenRouteService) i przejazdami komunikacją miejską według rozkładu ZTP. Brakuje wdrożenia na serwer, publicznego API, opisu AI i danych z MSIP. Największe ryzyka: nietestowana wersja opublikowana, niestabilne połączenie z OpenRouteService i wymyślone dane demonstracyjne.

**Co doszło od poprzedniej wersji tego dokumentu (13:00):** routing po ulicach, komunikacja miejska z rozkładem i przesiadkami, nowy wygląd interfejsu (Łukasz).

**Co doszło 3.10.2026 wieczorem:** przygotowane połączenie hosta z MongoDB (klaster w MongoDB Atlas). Na razie sama infrastruktura, bez kolekcji z danymi; szczegóły w sekcji "Baza danych (MongoDB)".

## 1. Co zostało zrealizowane

### Zakres funkcjonalny (sekcja 6 planu prac)

| Nr | Funkcja | Stan | Uwagi |
|---|---|---|---|
| 1 | Ekran startowy z wyborem trybu | zrobione | "Zwiedzam" / "Załatwiam sprawę" |
| 2 | Kreator profilu | zrobione | 7 gotowych profili (można łączyć) i dostrajanie parametrów |
| 3 | Katalog miejsc: mapa i lista | zrobione | ocena pod profil, wyszukiwarka, filtr kategorii |
| 4 | Karta miejsca | zrobione | bariery, udogodnienia, braki danych, źródło i data każdej cechy |
| 5 | Podpowiedzi | częściowo | lista jest sortowana rankingiem pod profil; nie ma osobnego widoku ani punktu startu użytkownika |
| 6 | Planer trasy | zrobione w podstawowym zakresie | kolejność przystanków, trasa po ulicach dobrana do profilu, ostrzeżenia o stromych odcinkach; bez listy pojedynczych barier (krawężniki, bruk) |
| 7 | Przerwy na trasie | częściowo | ostrzeżenie o zbyt długim odcinku i propozycja przejazdu komunikacją; przerwy (ławki, toalety) nie są wstawiane |
| – | **Komunikacja miejska (poza pierwotnym planem)** | zrobione | patrz niżej |
| 8 | Opis planu przez AI | brak | |
| 9 | Publiczne API z OpenAPI | brak | jest tylko wewnętrzny `POST /api/route` |
| 10 | Zgłoszenie bariery | brak | |
| 11 | Harmonogram godzinowy | brak | |
| 12 | Tryb offline (PWA) | brak | |

### Trasy po ulicach (OpenRouteService)

- Przeglądarka pyta host (`POST /api/route`), host pyta OpenRouteService. Klucz API zostaje na hoście.
- Do hosta idą tylko trzy parametry z profilu: czy wózek, czy unikać schodów, maksymalna wysokość krawężnika. Reszta profilu nie opuszcza urządzenia.
- Wózek → profil `wheelchair` z limitem krawężnika; pozostali → `foot-walking`, przy "unikam schodów" z pominięciem schodów.
- Ostrzeżenia o stromych odcinkach (7% i więcej; dla wózka także 4-6%).
- Trzy poziomy awaryjne: zapytanie z ograniczeniami → bez ograniczeń (z ostrzeżeniem) → linia prosta. Plan układa się zawsze.
- Odpowiedzi są zapamiętywane w IndexedDB, więc ten sam odcinek nie zużywa limitu drugi raz.

### Komunikacja miejska

- **Kiedy:** dla każdego odcinka dłuższego niż limit z profilu, a gdy profil limitu nie ma, dla odcinków powyżej 1 km (dojście do przystanku do 600 m).
- **Co pokazuje:** o której wyjść, linia i kierunek, przystanek i godzina odjazdu, przystanek i godzina przyjazdu, trzy najbliższe odjazdy, dojścia pieszo.
- **Przesiadki:** najwyżej jedna, z 2 minutami zapasu i przejściem do 200 m między przystankami.
- **Brak połączenia** jest oznaczony czerwoną ramką z powodem: brak przystanku w zasięgu, brak połączenia w ciągu 2 godzin albo rozkład nie obejmuje danego dnia.
- **Data danych** jest pod każdą podpowiedzią: źródło, zakres ważności rozkładu, dzień pobrania.
- Wyszukiwanie działa w przeglądarce, na pliku z rozkładem; host nie bierze w nim udziału.

### Baza danych (MongoDB)

Stan: **przygotowane połączenie, bez danych.** Żadna funkcja aplikacji nie korzysta jeszcze z bazy; katalog miejsc i rozkład nadal są plikami statycznymi, a profil zostaje na urządzeniu.

- **Gdzie działa baza:** klaster w MongoDB Atlas (cloud.mongodb.com). Łączy się z nim wyłącznie host (`Web`); przeglądarka nigdy nie dostaje adresu połączenia.
- **Osobny projekt `Infrastructure.Mongo`**, podpięty tylko do hosta. Sterownik (`MongoDB.Driver` 3.12.0, licencja Apache-2.0) nie trafia do `Infrastructure`, bo ten projekt jest też częścią aplikacji w przeglądarce.
- **Konfiguracja (sekcja `Mongo`):** `ConnectionString` (sekret), `Database` (domyślnie `krakow-bez-barier`, wpisane w `appsettings.json`), `ServerSelectionTimeoutSeconds` (5).
- **Rejestracja:** `AddMongo` udostępnia `IMongoClient` i `IMongoDatabase` jako singletony. Klient powstaje przy pierwszym użyciu, więc aplikacja uruchamia się także bez skonfigurowanej bazy.
- **Zapis dokumentów:** pola camelCase, enumy jako tekst, nieznane pola pomijane, czyli tak samo jak w plikach JSON katalogu. `Place.Id` staje się kluczem `_id`.
- **Sprawdzenie połączenia:** `GET /api/health/db` zwraca `ok` z czasem odpowiedzi, `not-configured` (503), gdy brakuje adresu, albo `unreachable` (503), gdy baza nie odpowiada. Szczegóły błędu trafiają tylko do logów hosta.

Jak dodać pierwszą kolekcję: interfejs repozytorium w `Application/Abstractions`, implementacja w `Infrastructure.Mongo` na `IMongoDatabase`, endpoint w `Web/Endpoints`, klient HTTP w `Infrastructure/Browser`.

### Warstwy

| Projekt | Co zawiera |
|---|---|
| `Domain` | model miejsc i cech, profil potrzeb z gotowymi profilami, silnik oceny (ruch, sensoryka, kondycja), model planu, sieć komunikacji i wyszukiwarka połączeń |
| `Application` | `Result`, `ICommand` / `IQuery` nad MediatR, zapytania o miejsca i kartę miejsca, zapis i odczyt profilu, układanie planu (kolejność, odcinki, komunikacja), ranking podpowiedzi |
| `Infrastructure` | katalog miejsc i sieć komunikacji z plików statycznych, magazyn IndexedDB, klient routingu z cache i wariantem awaryjnym, klient OpenRouteService po stronie hosta |
| `Web.Client` | strony: start, profil, miejsca, karta miejsca, plan; mapa Leaflet; panel komunikacji; stan sesji |
| `Infrastructure.Mongo` | połączenie hosta z MongoDB: ustawienia, rejestracja klienta, konwencje zapisu, sprawdzenie połączenia |
| `Web` | host: serwuje aplikację, pośredniczy w routingu, sprawdza połączenie z bazą (`GET /api/health/db`) |
| `Tools` | `import <miasto>`: miejsca z OpenStreetMap + ręczne uzupełnienia; `transit <miasto>`: rozkład z GTFS |
| `Tests` | 44 testy: silnik oceny, łączenie profili, kolejność przystanków, układanie planu, zapytania i odpowiedzi OpenRouteService, wyszukiwarka połączeń, obszar mapy, zapis dokumentów MongoDB i zachowanie bez bazy |

### Dane

| Plik | Zawartość | Źródło |
|---|---|---|
| `data/krakow/places.json` (250 KB) | 858 miejsc w centrum Krakowa: przystanki 216, zdrowie 145, jedzenie 132, muzea 99, urzędy 70, atrakcje 61, toalety 57, kultura 52, biblioteki 26 | OpenStreetMap, pobrane 3.10.2026 |
| `data/krakow/transit.json` (1,3 MB, ok. 330 KB po kompresji) | 218 linii, 589 przebiegów, 94 953 kursy, 3474 przystanki, cały Kraków | GTFS ZTP Kraków, rozkład ważny 2.10.2026-31.01.2027, pobrany 3.10.2026 |
| `src/Tools/overrides/krakow.json` | ręczne uzupełnienia cech dla 10 miejsc | zespół, dane demonstracyjne |

### Sprawdzone

- Build bez ostrzeżeń, 25 testów przechodzi.
- W przeglądarce: profile "wózek elektryczny", "spektrum autyzmu" i "senior"; zapis profilu i jego odczyt po odświeżeniu; zmiana profilu zmienia oceny na liście, mapie i w planie.
- Routing na żywo dla profilu pieszego z omijaniem schodów (odcinek 1,4 km zamiast szacowanych 910 m).
- Wariant awaryjny routingu: bez klucza i przy niedostępnym serwerze plan układa się z linią prostą.
- Komunikacja na prawdziwym rozkładzie (sobota 3.10, ok. 14:00):

| Odcinek | Wynik |
|---|---|
| Urząd Miasta → Przychodnia Medycyna Polska (senior, limit 500 m) | autobus 184, Rondo Mogilskie 14:11 → Hala Targowa 14:20 |
| Sukiennice → Fabryka Schindlera | tramwaj 1 do Ronda Mogilskiego, przesiadka na 7 do Zabłocia |
| Wawel → Muzeum Narodowe (9:00) | tramwaj 8, Wawel 9:13 → Muzeum Narodowe 9:21 |
| Przychodnia Medycyna Polska → Wojewódzka Biblioteka (limit 500 m) | brak połączenia, oznaczone na czerwono |

- MongoDB bez dostępu do klastra: host uruchamia się bez adresu połączenia, `GET /api/health/db` zwraca `not-configured`; zapis i odczyt miejsca przez BSON oraz zachowanie przy niedostępnym serwerze są pokryte testami.

### Nie sprawdzone

- **Połączenie z klastrem MongoDB Atlas.** Adres połączenia nie był jeszcze ustawiony; pierwsze sprawdzenie to `GET /api/health/db` po ustawieniu sekretu.
- Wersja opublikowana (`dotnet publish`) i wdrożenie na serwer.
- Routing na żywo dla profilu wózkowego z limitem krawężnika.
- Komunikacja dla profilu bez limitu odcinka (próg 1 km) w przeglądarce; logika jest pokryta testami tylko pośrednio.
- Tryb "Załatwiam sprawę" jako cała ścieżka.
- Urządzenia mobilne, czytnik ekranu, obsługa samą klawiaturą (także po zmianie wyglądu interfejsu).
- Uruchamianie z Ridera: zgłoszony biały ekran i błędy w konsoli JS, przyczyna nieustalona (patrz sekcja 2).

## 2. Problemy

### Dane

| Problem | Skutek | Co z tym zrobić |
|---|---|---|
| Cechy sensoryczne i część pozostałych dla 10 miejsc są **wymyślone na potrzeby demo**, nie zmierzone | w aplikacji są oznaczone jako "dane demonstracyjne", ale nie wolno ich przedstawiać jako faktów | zastąpić danymi z deklaracji dostępności i z wizji lokalnej albo zostawić oznaczenie i powiedzieć to wprost w prezentacji |
| OSM nie ma danych o hałasie i tłumie | dla profili sensorycznych 337 z 344 miejsc ma status "brak danych" | mapa hałasu MSIP albo ręczne uzupełnienie miejsc ze ścieżki demo |
| Większość miejsc w OSM nie ma tagu `wheelchair` | dla wózka 183 z 401 miejsc w trybie "Zwiedzam" ma "brak danych" | to uczciwy wynik; uzupełnić miejsca demo |
| **GTFS nie zawiera danych o dostępności pojazdów i przystanków** | aplikacja proponuje przejazd, ale nie wie, czy pojazd jest niskopodłogowy i czy przystanek jest dostępny; mówi to przy każdej podpowiedzi | dla osoby na wózku to istotna luka: szukać danych o taborze w ZTP albo MPK, cechy przystanków z OSM |
| MSIP nie ma danych o komunikacji | źródło nie zostało podpięte | z MSIP zostają do wzięcia budynki publiczne i mapa hałasu |
| Adresy warstw ZTP w ArcGIS Hub (wiaty, koperty) nieznane | nie podpięte | ustalić ręcznie na stronie huba |
| Adres MSIP z notatek zwraca 404 | – | działający katalog: `https://msip.um.krakow.pl/arcgis/rest/services` |
| Główny serwer Overpass nie odpowiadał | import miejsc trwał dłużej | import próbuje kolejno trzech instancji; wynik jest plikiem w repozytorium |
| Przystanki w `places.json` są deduplikowane po nazwie | jeden punkt na nazwę zamiast osobnych słupków | przystanki do planowania pochodzą już z GTFS; listę w katalogu można z nich odtworzyć |

### Technika

| Problem | Skutek | Co z tym zrobić |
|---|---|---|
| **OpenRouteService odpowiada niestabilnie** | w testach część zapytań kończyła się timeoutem po 15 s lub błędem DNS; plan się układa, ale wolniej i z linią prostą | przed demo ułożyć plany ze ścieżki demo, żeby trasy były w cache; rozważyć krótszy timeout |
| Parametry OpenRouteService wpisane z pamięci dokumentacji | jeśli nazwa ograniczenia jest błędna, zadziała wariant bez ograniczeń z ostrzeżeniem | sprawdzić profil wózkowy na żywo |
| "Unikam bruku" nie trafia do routingu | trasa może prowadzić po bruku | ustalić dozwolone wartości nawierzchni w OpenRouteService |
| Godziny komunikacji są rozkładowe | bez opóźnień i odwołań | ZTP udostępnia dane na żywo (GTFS-RT); do podpięcia przez host |
| Odjazdy dla każdego odcinka są liczone od chwili ułożenia planu | dla dalszych odcinków planu godziny są orientacyjne | liczyć od przewidywanego czasu dotarcia do danego miejsca |
| Wyszukiwarka wybiera najwcześniejszy przyjazd | bywa, że proponuje dłuższe dojście, choć istnieje wygodniejsze połączenie | dla profili z limitem ważyć dojście wyżej niż czas |
| Kursy nocne rozpoczęte przed północą nie są widoczne po północy | brak połączeń tuż po północy | uwzględnić poprzedni dzień rozkładowy |
| Dojście do przystanku jest szacowane w linii prostej (×1,3) | rzeczywista droga może być dłuższa i mieć bariery | policzyć dojścia routingiem |
| `transit.json` ma 1,3 MB i jest czytany w całości | ułożenie planu trwało ok. 2 s; na słabszym telefonie dłużej | wystarcza na demo; docelowo podział na obszary albo wyszukiwanie na hoście |
| Kafelki mapy z publicznego serwera OpenStreetMap | serwer nie jest przeznaczony pod duży ruch; brak sieci = brak mapy | na demo wystarczy |
| MediatR wyszukuje handlery przez refleksję | przy publikacji WebAssembly z przycinaniem kodu handlery mogą zostać usunięte | sprawdzić wersję opublikowaną przed wdrożeniem |
| Biały ekran przy uruchomieniu z Ridera | nie odtworzone: te same pliki uruchomione poleceniem `dotnet run` działają | potrzebna treść błędów z konsoli; podejrzenia: pamięć podręczna przeglądarki po przebudowie, konfiguracja `Web.Client` zamiast `Web`, certyfikat dla profilu `https` |
| Przebudowa projektu przy działającej aplikacji | host działa na starym kodzie, a pliki klienta są już nowe | po każdej przebudowie restart aplikacji |

| Brak adresu IP na liście dostępu w MongoDB Atlas | połączenie kończy się limitem czasu, a endpoint zwraca `unreachable` | dodać adresy zespołu i serwera w Network Access; przyczyna jest w logach hosta |
| Adres `mongodb+srv://` wymaga rekordów DNS SRV | w niektórych sieciach połączenie się nie uda | użyć dłuższego adresu `mongodb://` z Atlasa |

### Organizacja

- Deploy na Mikrusa nie zaczęty, a plan zakładał go w pierwszej godzinie.
- Brak README.
- Klucz OpenRouteService każdy ustawia u siebie (`dotnet user-secrets set "OpenRouteService:ApiKey" ... --project src/Web`); na serwerze zmienna `OpenRouteService__ApiKey`. Bez klucza trasy są liczone w linii prostej.
- Adres połączenia z MongoDB każdy ustawia u siebie (`dotnet user-secrets set "Mongo:ConnectionString" "mongodb+srv://..." --project src/Web`); na serwerze zmienna `Mongo__ConnectionString`. Zawiera hasło, więc nie trafia do repozytorium. Bez niego aplikacja działa jak dotąd.
- W MongoDB Atlas potrzebny jest użytkownik bazy z rolą `readWrite` na bazie `krakow-bez-barier` oraz adresy zespołu i serwera w Network Access.
- Kamień milowy 17:00 z planu prac (mapa z prawdziwymi danymi, wdrożona na serwerze) jest spełniony funkcjonalnie, ale nie w części "wdrożona".

## 3. Odstępstwa od planu

| Plan | Jak jest | Dlaczego |
|---|---|---|
| Cecha `StepFreeEntrance` jako podstawa oceny wejścia | doszły `WheelchairAccess` i `WheelchairLimited` | tag `wheelchair` z OSM opisuje całe miejsce, nie samo wejście; mapowanie na "wejście bez stopni" byłoby nadinterpretacją |
| `AssessmentReason(Key, Kind, Message)` | doszły pola `Impact`, `Source`, `IsDemoData` | status końcowy wynika z wpływu powodów; źródło i oznaczenie demo są potrzebne w interfejsie |
| Brak wymaganej cechy zawsze daje "brak danych" | braki dzielą się na blokujące (wejście, hałas, tłum) i informacyjne (toaleta, miejsca do siedzenia) | inaczej prawie każde miejsce miałoby "brak danych" |
| Zapytania biorą profil z magazynu | profil jest parametrem zapytania | logika zostaje czysta i testowalna |
| Osobne `GetSuggestionsQuery` | ranking jest częścią `GetPlacesQuery` | jedna lista zamiast dwóch widoków |
| Komunikacja miejska dopiero w roadmapie | zrobiona: rozkład, przesiadki, najbliższe odjazdy | decyzja w trakcie prac; pokrywa kryterium "ocena tras" dla osób z limitem dystansu |
| GTFS jako źródło przystanków do katalogu | GTFS jako osobna sieć komunikacji (`transit.json`), niezależna od katalogu miejsc | rozkład to inny rodzaj danych niż miejsca; plik wczytuje się tylko przy układaniu planu |
| Dane o komunikacji z ZTP i MSIP | tylko GTFS z ZTP | MSIP nie ma danych o komunikacji; adresy warstw ZTP nieustalone |
| Do routingu idą "parametry trasy" | idą trzy parametry: wózek, schody, krawężnik | zgodne z zasadą prywatności, doprecyzowane |
| Macierz czasów z routingu do układania kolejności | kolejność z odległości w linii prostej | planer nie zależy od limitu i dostępności API |
| `BreakInserter` wstawia przerwy | ostrzeżenie i propozycja przejazdu komunikacją | przerwy (ławki, toalety) nadal do zrobienia |
| Toalety w liście i podpowiedziach | dostępne tylko przez filtr kategorii | zajmowały górę rankingu |
| Zasięg miejsc: Kraków | centrum (prostokąt ok. 3 × 3 km); komunikacja obejmuje cały Kraków | mniejszy plik i szybszy import; zasięg jest jednym wpisem w `CityImports` |
| Bez bazy danych: pliki statyczne i IndexedDB | doszło połączenie hosta z MongoDB (na razie bez kolekcji) | przygotowanie pod dane wspólne dla użytkowników, np. zgłoszenia barier |
| Solution w formacie `.sln` | `.slnx` | domyślny format SDK .NET 10; starsze wersje IDE mogą go nie otwierać |
| Leaflet | wersja 1.9.4 wgrana do repozytorium | bez CDN; licencja BSD-2 |

## 4. Co trzeba rozważyć

### Decyzje, które wpływają na resztę prac

1. **Dane demonstracyjne.** Czy pokazujemy je jury z oznaczeniem, czy zastępujemy prawdziwymi dla 20-25 miejsc ze ścieżki demo.
2. **Dostępność komunikacji dla osób na wózku.** Bez danych o taborze i przystankach podpowiedź przejazdu jest dla Marty niepełna. Albo znajdujemy takie dane, albo w prezentacji pokazujemy komunikację na personie Pani Zofii, a dla wózka mówimy wprost, czego brakuje.
3. **Niezawodność routingu na demo.** OpenRouteService bywa niedostępny. Trasy ze ścieżki demo powinny być w cache albo zapisane jako wariant awaryjny.
4. **Dane na żywo z ZTP (GTFS-RT).** Dają opóźnienia i byłyby mocnym punktem w kryterium "WOW", ale wymagają pośrednictwa hosta i parsowania formatu protobuf.
5. **Ile danych miejskich jeszcze podpinamy.** Z MSIP realnie do wzięcia są budynki publiczne i mapa hałasu; z huba ZTP wiaty i koperty, jeśli ustalimy adresy warstw.
6. **Zasięg importu miejsc.** Centrum czy cały Kraków. Komunikacja obejmuje już całe miasto, więc różnica jest widoczna.
7. **Drugie miasto.** Model ma `CityId` i `Coverage`, import jest parametryzowany, ale interfejs nie ma wyboru miasta.

8. **Co trafia do MongoDB.** Kandydaci: zgłoszenia barier (punkt 10 zakresu), katalog miejsc, zapisane plany. Profil potrzeb powinien zostać na urządzeniu: aplikacja deklaruje, że dane o zdrowiu go nie opuszczają.

### Ryzyka przed demo

- **Wersja opublikowana może zachowywać się inaczej** niż uruchamiana lokalnie (przycinanie kodu, ścieżki plików statycznych, kompresja dużych plików danych). Deploy na Mikrusa warto zrobić teraz, nie nad ranem.
- **Mikrus:** nieznana ilość pamięci, port, subdomena z HTTPS i dostępność środowiska .NET 10.
- **Rozkład jest ważny od 2.10.2026.** Demo 4.10 mieści się w zakresie, ale nagranie filmu o innej porze niż testy da inne godziny odjazdów; scenariusz filmu nie powinien zależeć od konkretnej godziny.
- **MongoDB Atlas na serwerze:** adres IP Mikrusa musi być na liście dostępu, a darmowy klaster M0 ma limit 512 MB.
- **Dostępność samej aplikacji** nie była testowana czytnikiem ekranu, a interfejs został przebudowany.
- **Licencje:** przypis OSM (ODbL) jest w stopce i na mapie; źródło rozkładu ZTP jest podane przy podpowiedziach, ale warunków wykorzystania danych ZTP i regulaminu OpenRouteService nie sprawdziliśmy. Prawa do nagrodzonego rozwiązania przechodzą na fundatora, więc każda zależność musi być na otwartej licencji (MediatR przypięty do 12.4.1, Apache-2.0).

## 5. Następne kroki

| Kolejność | Zadanie | Po co |
|---|---|---|
| 1 | Publikacja i deploy na Mikrusa | wykrycie problemów wersji opublikowanej, publiczny adres do zgłoszenia |
| 2 | Wyjaśnienie białego ekranu w Riderze | cały zespół musi móc uruchamiać aplikację |
| 3 | Test profilu wózkowego w routingu, przekazanie "unikam bruku" | Marta jest pierwszą personą w filmie |
| 4 | Prawdziwe dane dla miejsc demo | wiarygodność przed jury |
| 5 | Scenariusz Pani Zofii w trybie "Załatwiam sprawę" z przejazdem komunikacją | trzecia persona z filmu |
| 6 | Publiczne API + OpenAPI na hoście | kryterium "Projekt" |
| 7 | Odjazdy liczone od czasu dotarcia do miejsca, przerwy na trasie | spójny plan dnia |
| 8 | Opis planu przez OpenAI | punkt 8 zakresu |
| 9 | Mapa hałasu MSIP, warstwy ZTP | mniej "brak danych" dla profili sensorycznych, cechy przystanków |
| 10 | README, przegląd dostępności interfejsu | materiały do zgłoszenia |
| 11 | MongoDB: ustawić adres połączenia, potwierdzić `GET /api/health/db`, zdecydować o pierwszej kolekcji | baza jest podłączona, ale jeszcze nieużywana |
