# Stan realizacji: Kraków bez barier

Stan na **3.10.2026, ok. 13:00**. Punkt odniesienia: [plan-prac.md](plan-prac.md) i [plan-implementacji.md](plan-implementacji.md).

**W skrócie:** działa lokalnie pełna ścieżka profil → miejsca → karta miejsca → plan, na prawdziwych danych z OpenStreetMap. Brakuje routingu po ulicach, danych miejskich (ZTP, MSIP), opisu AI, publicznego API i wdrożenia na serwer. Kod MVP nie jest jeszcze zacommitowany.

## 1. Co zostało zrealizowane

### Zakres funkcjonalny (sekcja 6 planu prac)

| Nr | Funkcja | Stan | Uwagi |
|---|---|---|---|
| 1 | Ekran startowy z wyborem trybu | zrobione | "Zwiedzam" / "Załatwiam sprawę" |
| 2 | Kreator profilu | zrobione | 7 gotowych profili (można łączyć) i dostrajanie parametrów |
| 3 | Katalog miejsc: mapa i lista | zrobione | ocena pod profil, wyszukiwarka, filtr kategorii |
| 4 | Karta miejsca | zrobione | bariery, udogodnienia, braki danych, źródło i data każdej cechy |
| 5 | Podpowiedzi | częściowo | lista jest sortowana rankingiem pod profil; nie ma osobnego widoku podpowiedzi ani punktu startu użytkownika |
| 6 | Planer trasy | częściowo | kolejność przystanków i dystanse są; przebieg ulicami i bariery na odcinkach nie |
| 7 | Przerwy na trasie | częściowo | tylko ostrzeżenie o zbyt długim odcinku; przerwy nie są wstawiane |
| 8 | Opis planu przez AI | brak | |
| 9 | Publiczne API z OpenAPI | brak | |
| 10 | Zgłoszenie bariery | brak | |
| 11 | Harmonogram godzinowy | brak | |
| 12 | Tryb offline (PWA) | brak | |

### Warstwy

| Projekt | Co zawiera |
|---|---|
| `Domain` | model miejsc i cech, profil potrzeb z gotowymi profilami, silnik oceny (reguły: ruch, sensoryka, kondycja), model planu |
| `Application` | `Result`, `ICommand` / `IQuery` nad MediatR, zapytania o miejsca i kartę miejsca, zapis i odczyt profilu, komenda układania planu, optymalizacja kolejności (najbliższy sąsiad + 2-opt), ranking podpowiedzi |
| `Infrastructure` | katalog miejsc z plików statycznych, magazyn IndexedDB, zaślepka routingu (linia prosta) |
| `Web.Client` | strony: start, profil, miejsca, karta miejsca, plan; komponent mapy Leaflet; stan sesji |
| `Web` | host serwujący aplikację; bez własnych endpointów |
| `Tools` | import z OpenStreetMap (Overpass) do `places.json`, nakładanie ręcznych uzupełnień |
| `Tests` | 11 testów: silnik oceny dla trzech person, łączenie profili, optymalizacja kolejności |

### Dane

- **858 miejsc z OpenStreetMap** dla centrum Krakowa (Stare Miasto, Kazimierz, Wawel i okolice): przystanki 216, zdrowie 145, jedzenie 132, muzea 99, urzędy 70, atrakcje 61, toalety 57, kultura 52, biblioteki 26.
- Każda cecha ma źródło i datę (dla OSM: data ostatniej edycji obiektu).
- 10 miejsc ma ręczne uzupełnienia w `src/Tools/overrides/krakow.json`.

### Sprawdzone

- Build bez ostrzeżeń, 11 testów przechodzi.
- Ścieżka w przeglądarce dla dwóch profili (wózek elektryczny, spektrum autyzmu): profil zapisuje się i przeżywa odświeżenie strony, zmiana profilu zmienia oceny na liście, mapie i w planie.
- Przykład wyróżnika: Sukiennice są dla wózka elektrycznego "z ograniczeniami" (bruk), a dla spektrum autyzmu "niedostępne" (hałas i tłum).

### Nie sprawdzone

- Wersja opublikowana (`dotnet publish`) i wdrożenie na serwer.
- Profil "senior" i tryb "Załatwiam sprawę" w przeglądarce (są pokryte tylko testami silnika).
- Urządzenia mobilne, czytnik ekranu, obsługa samą klawiaturą.

## 2. Problemy

### Dane

| Problem | Skutek | Co z tym zrobić |
|---|---|---|
| Cechy sensoryczne i część pozostałych dla 10 miejsc są **wymyślone na potrzeby demo**, nie zmierzone | w aplikacji są oznaczone jako "dane demonstracyjne", ale nie wolno ich przedstawiać jako faktów | zastąpić danymi z deklaracji dostępności i z wizji lokalnej albo zostawić oznaczenie i powiedzieć to wprost w prezentacji |
| OSM nie ma danych o hałasie i tłumie | dla profili sensorycznych 337 z 344 miejsc ma status "brak danych" | mapa hałasu MSIP albo ręczne uzupełnienie miejsc ze ścieżki demo |
| Większość miejsc w OSM nie ma tagu `wheelchair` | dla wózka 183 z 401 miejsc w trybie "Zwiedzam" ma "brak danych" | to uczciwy wynik; uzupełnić miejsca demo |
| GTFS ZTP nie zawiera danych o dostępności przystanków | kolumny `wheelchair_boarding` i `wheelchair_accessible` są puste lub zerowe | cechy przystanków brać z warstw ZTP i z OSM |
| Adres MSIP z notatek zwraca 404 | – | działający katalog: `https://msip.um.krakow.pl/arcgis/rest/services` |
| Listy zbiorów ZTP nie udało się pobrać automatycznie | adresy warstw (wiaty, koperty) nieznane | ustalić ręcznie na stronie huba |
| Główny serwer Overpass nie odpowiadał | import trwał dłużej | import próbuje kolejno trzech instancji; wynik jest plikiem w repozytorium, więc demo od tego nie zależy |
| Przystanki są deduplikowane po nazwie | jeden punkt na nazwę zamiast osobnych słupków | wystarcza na MVP; do poprawy przy imporcie z GTFS |

### Technika

| Problem | Skutek | Co z tym zrobić |
|---|---|---|
| Trasa to linia prosta z poprawką ×1,3 | dystans i czas są szacunkowe, a trasa nie omija barier; interfejs mówi to wprost | podłączyć OpenRouteService przez `IRoutingClient` |
| Kafelki mapy pochodzą z publicznego serwera OpenStreetMap | serwer ma zasady użycia i nie jest przeznaczony pod duży ruch; brak sieci = brak mapy | na demo wystarczy; przy większym ruchu inny dostawca kafelków |
| MediatR wyszukuje handlery przez refleksję | przy publikacji WebAssembly z przycinaniem kodu handlery mogą zostać usunięte | sprawdzić wersję opublikowaną przed wdrożeniem |
| `places.json` waży ok. 250 KB i jest wczytywany w całości | przy całym Krakowie plik urośnie kilkukrotnie | kompresja po stronie serwera, ewentualnie podział na kategorie |
| Lista miejsc pokazuje po 30 pozycji, mapa wszystkie | przy kilku tysiącach punktów mapa zwolni | grupowanie pinezek |

### Organizacja

- Kod MVP i oba nowe dokumenty nie są zacommitowane.
- Brak README.
- Deploy na Mikrusa nie zaczęty, a plan zakładał go w pierwszej godzinie.

## 3. Odstępstwa od planu

| Plan | Jak jest | Dlaczego |
|---|---|---|
| Cecha `StepFreeEntrance` jako podstawa oceny wejścia | doszły `WheelchairAccess` i `WheelchairLimited` | tag `wheelchair` z OSM (tak / nie / ograniczone) opisuje całe miejsce, nie samo wejście; mapowanie na "wejście bez stopni" byłoby nadinterpretacją |
| `AssessmentReason(Key, Kind, Message)` | doszły pola `Impact`, `Source`, `IsDemoData` | status końcowy wynika z wpływu powodów; źródło i oznaczenie demo są potrzebne w interfejsie |
| Brak wymaganej cechy zawsze daje "brak danych" | braki dzielą się na blokujące (wejście, hałas, tłum) i informacyjne (toaleta, miejsca do siedzenia) | inaczej prawie każde miejsce miałoby "brak danych" i ocena byłaby bezużyteczna |
| Zapytania biorą profil z magazynu | profil jest parametrem zapytania | logika zostaje czysta i testowalna, a to samo zapytanie obsłuży publiczne API |
| Osobne `GetSuggestionsQuery` | ranking jest częścią `GetPlacesQuery` | jedna lista zamiast dwóch widoków |
| `BreakInserter` wstawia przerwy | tylko ostrzeżenie przy odcinku dłuższym niż limit z profilu | zakres MVP |
| Toalety w liście i podpowiedziach | dostępne tylko przez filtr kategorii | zajmowały górę rankingu |
| Import: OSM + GTFS + ZTP + MSIP z łączeniem rekordów | tylko OSM i ręczne uzupełnienia | zakres MVP; adresy warstw miejskich nieustalone |
| Zasięg: Kraków | centrum (prostokąt ok. 3 × 3 km) | mniejszy plik i szybszy import; zasięg jest jednym wpisem w `CityImports` |
| Solution w formacie `.sln` | `.slnx` | domyślny format SDK .NET 10; starsze wersje IDE mogą go nie otwierać |
| Leaflet | wersja 1.9.4 wgrana do repozytorium | bez CDN; licencja BSD-2 |
| `Web.styles.css` z szablonu | usunięty | nie ma już stylów izolowanych; wszystko jest w `app.css` |

## 4. Co trzeba rozważyć

### Decyzje, które wpływają na resztę prac

1. **Dane demonstracyjne.** Czy pokazujemy je jury z oznaczeniem, czy zastępujemy prawdziwymi dla 20-25 miejsc ze ścieżki demo. Zgodnie z planem nie przedstawiamy ich jako zweryfikowanych.
2. **Routing.** OpenRouteService wymaga klucza i ma limity. Jeśli weryfikacja wypadnie źle, zostaje linia prosta, a kryterium "ocena tras wg indywidualnych potrzeb" trzeba pokryć inaczej, np. barierami z OSM w pobliżu odcinka.
3. **Ile danych miejskich.** GTFS i MSIP dają położenie obiektów, nie ich dostępność. Warto wziąć to, co wnosi nową informację (wiaty, koperty, mapa hałasu), a pominąć resztę.
4. **Zasięg importu.** Centrum czy cały Kraków. Większy zasięg oznacza większy plik i więcej miejsc bez danych.
5. **Drugie miasto.** Model ma już `CityId` i `Coverage`, ale interfejs nie ma wyboru miasta. Do zrobienia dopiero po pełnej ścieżce demo.

### Ryzyka przed demo

- **Wersja opublikowana może zachowywać się inaczej** niż uruchamiana lokalnie (przycinanie kodu, ścieżki plików statycznych). Deploy pustej wersji na Mikrusa warto zrobić teraz, nie nad ranem.
- **Mikrus:** nieznana ilość pamięci, port, subdomena z HTTPS i dostępność środowiska .NET 10. Bez HTTPS IndexedDB działa, ale geolokalizacja i tryb offline nie.
- **Pani Zofia** (senior, "Załatwiam sprawę") jest najmniej sprawdzoną personą, a ma własny fragment filmu.
- **Dostępność samej aplikacji** nie była testowana czytnikiem ekranu. Mapa ma odpowiednik w postaci listy, ale to założenie, nie wynik testu.
- **Licencje:** przypis OSM (ODbL) jest w stopce i na mapie; warunków wykorzystania danych ZTP i MSIP nie sprawdziliśmy. Prawa do nagrodzonego rozwiązania przechodzą na fundatora, więc każda zależność musi być na otwartej licencji (MediatR przypięty do 12.4.1, Apache-2.0).

## 5. Następne kroki

| Kolejność | Zadanie | Po co |
|---|---|---|
| 1 | Commit i push MVP | reszta zespołu pracuje na tym samym kodzie |
| 2 | Publikacja i deploy na Mikrusa | wykrycie problemów wersji opublikowanej, publiczny adres do zgłoszenia |
| 3 | Weryfikacja i podłączenie OpenRouteService | trasa po ulicach, parametry pod profil |
| 4 | Prawdziwe dane dla miejsc demo | wiarygodność przed jury |
| 5 | Scenariusz Pani Zofii w przeglądarce, poprawki trybu "Załatwiam sprawę" | trzecia persona z filmu |
| 6 | Publiczne API + OpenAPI na hoście | kryterium "Projekt" |
| 7 | Wstawianie przerw, opis planu przez OpenAI | punkty 7 i 8 zakresu |
| 8 | Import ZTP i MSIP, mapa hałasu | dane miejskie, mniej "brak danych" dla profili sensorycznych |
| 9 | README, przegląd dostępności interfejsu | materiały do zgłoszenia |
