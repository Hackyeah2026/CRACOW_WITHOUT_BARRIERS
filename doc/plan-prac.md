# Plan prac: Kraków bez barier – planer dostępnego miasta

**Zespół:** 3 osoby · **Czas:** 3.10 godz. 11:00 → 4.10 godz. 11:00 · **Stack:** Blazor Web App (host + klient WebAssembly z jednego szablonu), bez bazy danych po stronie serwera

## 1. Koncepcja

Aplikacja, która planuje poruszanie się po Krakowie dla osób z różnymi niepełnosprawnościami i zaburzeniami. Użytkownik opisuje swoje potrzeby, a aplikacja dobiera miejsca, podpowiada, na co uważać, i układa trasę, którą da się faktycznie pokonać.

**Wyróżnik:** to samo miejsce i ta sama trasa dostają inną ocenę dla każdego profilu, zawsze z uzasadnieniem (konkretne bariery i udogodnienia zamiast "dostępne: tak/nie").

**Zmiana względem pierwotnego pomysłu:** zadanie wymaga obsługi **mieszkańców i turystów** oraz roli **źródła wiedzy o usługach miasta**. Sam planer zwiedzania pokrywa tylko turystów. Dlatego ten sam silnik dostaje dwa tryby:

- **Zwiedzam**: atrakcje, podpowiedzi, trasa dnia (Wasz pierwotny pomysł).
- **Załatwiam sprawę**: urzędy, przychodnie, biblioteki, kultura, przystanki; ocena miejsca i trasa dojścia.

Koszt jest mały (dodatkowe kategorie miejsc i przełącznik trybu), a bez tego tracimy punkty w trzech kryteriach.

## 2. Pokrycie wymagań zadania

| Wymaganie z zadania | Jak je spełniamy | Gdzie to widać |
|---|---|---|
| Mieszkańcy i turyści | dwa tryby: "Zwiedzam" i "Załatwiam sprawę" | ekran startowy |
| Ocena miejsc wg indywidualnych potrzeb | profil potrzeb + silnik oceny | mapa, karta miejsca |
| Ocena tras wg indywidualnych potrzeb | planer trasy z parametrami profilu, lista barier na odcinkach | widok trasy |
| Źródło informacji o usługach i ofercie miasta | katalog miejsc i usług z kartami dostępności | lista, wyszukiwarka |
| Konkretne informacje o barierach i udogodnieniach | cechy z wartością, źródłem i datą | karta miejsca |
| Potencjał rozwoju | warstwowa architektura, otwarte API, roadmapa | sekcja 11, slajd 9 |

## 3. Plan pod kryteria oceny

| Kryterium | Waga | Co robimy, żeby zdobyć punkty |
|---|---|---|
| Pomysł | 30% | trzy persony z realnym problemem, personalizacja z uzasadnieniem, zasada "brak danych to nie dostępność" |
| Aspekty techniczne | 30% | silnik oceny z testami, planer trasy (kolejność + routing pod profil), czyste repozytorium z README |
| Projekt | 20% | diagram architektury, otwarte API z dokumentacją OpenAPI, plan wdrożenia, dostępny interfejs |
| Związek z kategorią | 10% | tabela z sekcji 2 wprost na slajdzie i w opisie |
| Efekt WOW | 10% | zmiana profilu na żywo przebudowuje plan; opis trasy w prostym języku; zgłoszenie bariery zmienia trasę; katalog i zapisane plany działają offline |

Próg nagrody to 50% punktów, więc priorytetem jest działający pełny scenariusz, a nie liczba funkcji.

## 4. Persony do demo

- **Marta, turystka na wózku elektrycznym.** Wejścia bez progów, windy, toaleta dostosowana, trasy bez bruku i stromych podjazdów.
- **Kuba, turysta w spektrum autyzmu.** Unika tłumu i hałasu, potrzebuje przewidywalnego planu i cichych miejsc na przerwę.
- **Pani Zofia, mieszkanka, seniorka.** Idzie do urzędu i przychodni: krótkie odcinki, ławki po drodze, toalety, wolniejsze tempo.

## 5. Model potrzeb

Zasada: pytamy o **potrzeby funkcjonalne**, nie o diagnozy. Rodzaj niepełnosprawności to skrót, który wstępnie ustawia parametry; użytkownik może potem zmienić każdy z nich. Profile można łączyć.

| Obszar | Gotowe profile | Parametry do personalizacji |
|---|---|---|
| Ruch | wózek ręczny, wózek elektryczny, kule lub balkonik | maks. próg i krawężnik, maks. nachylenie, schody, nawierzchnia, szerokość drzwi, winda |
| Wzrok | osoba niewidoma, słabowidząca | ścieżki dotykowe, audiodeskrypcja, pies asystujący |
| Słuch | osoba głucha, słabosłysząca | pętla indukcyjna, tłumacz PJM, informacja wizualna |
| Sensoryka i neuroróżnorodność | spektrum autyzmu, ADHD, nadwrażliwość sensoryczna | tolerancja hałasu, tłumu i światła, ciche godziny, pokój wyciszenia |
| Poznawcze | niepełnosprawność intelektualna | tekst łatwy do czytania (ETR), piktogramy, opiekun |
| Kondycja i zdrowie | senior, choroba przewlekła | dystans bez odpoczynku, tempo, przerwy i toaleta co X minut, ławki |
| Psychiczne | lęk przed tłumem, zamkniętą przestrzenią | unikanie tłumu, wind, tuneli |
| Towarzyszące | wózek dziecięcy, opiekun | jak w obszarze "Ruch" |

**Zakres na 24 h:** pełne reguły dla trzech person (ruch, sensoryka, kondycja). Pozostałe obszary jako parametry w kreatorze z prostymi regułami.

**Prywatność:** to dane o zdrowiu. Profil zostaje w przeglądarce (IndexedDB), a ocena miejsc liczy się na urządzeniu użytkownika. Bez konta; poza urządzenie wychodzą tylko parametry trasy potrzebne do routingu.

## 6. Zakres funkcjonalny

### Musi być

1. **Ekran startowy** z wyborem trybu: "Zwiedzam" / "Załatwiam sprawę".
2. **Kreator profilu**: gotowe profile, potem dostrajanie parametrów.
3. **Katalog miejsc i usług**: mapa i lista, kategorie, ocena pod profil (zielony / żółty / czerwony / brak danych).
4. **Karta miejsca**: bariery, udogodnienia, źródło i data danych, uzasadnienie oceny.
5. **Podpowiedzi**: proponowane miejsca dopasowane do profilu.
6. **Planer trasy**: wybór miejsc, optymalna kolejność, przebieg omijający bariery, lista barier na odcinkach.

### Powinno być

7. Przerwy na trasie wstawiane wg profilu: toalety dostosowane, ławki, ciche miejsca.
8. Opis planu w prostym języku generowany przez AI.
9. Publiczne API z dokumentacją OpenAPI.

### Jeśli zostanie czas

10. Zgłoszenie bariery przez użytkownika (prosty formularz), które wpływa na ocenę i trasę. Zapis lokalny na urządzeniu.
11. Harmonogram godzinowy dnia.
12. Tryb offline (PWA): katalog miejsc i zapisane plany dostępne bez sieci.

**Kolejność cięć:** 12 → 11 → 10 → 8 → 7. Nietykalne: 1-6.

## 7. Dane

| Źródło | Co daje | Uwagi |
|---|---|---|
| OpenStreetMap (Overpass API) | atrakcje, urzędy, przychodnie, toalety, ławki, tagi `wheelchair`, `kerb`, `incline`, `surface`, `tactile_paving` | jednorazowy import dla centrum Krakowa do pliku `places.json`; wymagane podanie źródła (licencja ODbL) |
| Otwarte dane miasta | obiekty publiczne, przystanki | sprawdzić, co udostępnia miasto i organizator |
| Deklaracje dostępności instytucji publicznych | szczegóły dla urzędów, muzeów, bibliotek | ręcznie dla ok. 10 obiektów |
| Uzupełnienie ręczne | cechy sensoryczne (hałas, tłum, ciche godziny) | tylko dla miejsc ze ścieżki demo, oznaczone jako dane demonstracyjne |

**Zasady:** brak danych pokazujemy jako "nieznane", nigdy jako "dostępne". Każda cecha ma źródło i datę. Danych demonstracyjnych nie przedstawiamy jako zweryfikowanych.

### Gdzie trzymamy dane (bez bazy na serwerze)

| Dane | Miejsce | Uwagi |
|---|---|---|
| Miejsca i cechy dostępności | statyczny plik `places.json` dołączony do aplikacji | generowany narzędziem importu, wersjonowany w repozytorium |
| Profil potrzeb | IndexedDB | nie opuszcza urządzenia |
| Zapisane plany i trasy | IndexedDB | dostępne także offline |
| Zgłoszenia barier | IndexedDB | widoczne tylko na danym urządzeniu; współdzielenie to krok z roadmapy |
| Cache odpowiedzi routingu | IndexedDB | oszczędza limit API, zabezpiecza demo |

Ciasteczek nie używamy: mają limit ok. 4 KB i są wysyłane z każdym żądaniem, więc dane o zdrowiu trafiałyby na serwer.

## 8. Architektura

### Projekty w solution

```
Domain          -> Place, AccessibilityFeature, NeedsProfile, Assessment, TripPlan; silnik oceny
Application     -> komendy i zapytania (MediatR), planer trasy, podpowiedzi,
                   interfejsy: IPlaceCatalog, ILocalStore, IRoutingClient, IPlanDescriber
Infrastructure  -> katalog miejsc z places.json, magazyn IndexedDB, klient routingu, klient LLM
Web             -> host ASP.NET Core: serwuje aplikację, pośredniczy w wywołaniach
                   routingu i LLM, wystawia publiczne endpointy REST z OpenAPI
Web.Client      -> Blazor WebAssembly: interfejs i mapa
Tools           -> konsolowy import OSM -> places.json
Tests           -> testy silnika oceny i planera
```

Cała logika (ocena, podpowiedzi, planer) działa w przeglądarce. Host nie ma bazy i nie przechowuje stanu.

### Host i klient z jednego szablonu

`Web` i `Web.Client` powstają z szablonu **Blazor Web App** z interaktywnością WebAssembly:

```
dotnet new blazor --interactivity WebAssembly --all-interactive -o Web
```

- Szablon tworzy dwa projekty (host i `.Client`), ale to jedna aplikacja: jedno uruchomienie, jeden deploy, wspólny adres dla interfejsu i API.
- Wszystkie strony i komponenty trafiają do `Web.Client`. W hoście zostają tylko endpointy i konfiguracja.
- **Wyłączamy prerendering** (`new InteractiveWebAssemblyRenderMode(prerender: false)` w `App.razor`). Przy włączonym komponenty najpierw renderują się na serwerze, gdzie nie ma IndexedDB ani Leafleta, i wywołania JS interop kończą się błędem.
- Nazwa: "Blazor Hybrid" oznacza u Microsoftu aplikacje natywne (MAUI, WPF) z osadzonym Blazorem. Tutaj budujemy Blazor Web App; wersja hybrydowa na telefon jest w roadmapie.

**Po co host, skoro nie ma bazy:** aplikacja WebAssembly jest w całości pobierana przez przeglądarkę, więc klucze do routingu i LLM wpisane w klienta byłyby jawne. Host trzyma klucze i przekazuje żądania dalej. Przy okazji wystawia katalog miejsc jako otwarte API (czyta ten sam `places.json`, a ocenę liczy tym samym silnikiem z Domain), co daje punkty w kryterium "Projekt".

**Droga do produkcji:** dostęp do danych idzie przez interfejsy `IPlaceCatalog` i `ILocalStore`. Podmiana pliku JSON na bazę i dodanie synchronizacji zgłoszeń nie wymaga zmian w Domain i Application. To pokazujemy na slajdzie architektury.

### Kluczowe moduły

1. **Silnik oceny**: `NeedsProfile` × cechy miejsca → `Assessment` (status + powody). Czysta logika w Domain, pokryta testami.
2. **Podpowiedzi**: ranking = ocena dostępności + zgodność z kategoriami + odległość (liczona w pamięci, miejsc jest kilkaset).
3. **Planer trasy**:
   - macierz czasów przejścia z silnika routingu,
   - kolejność: najbliższy sąsiad + poprawka 2-opt,
   - odcinki: routing pieszy lub wózkowy z parametrami profilu,
   - wstawianie przerw wg dystansu i czasu z profilu.
4. **Warstwa AI**: zamienia gotowy plan na opis w prostym języku. Model nie jest źródłem faktów o dostępności; dostaje wyłącznie dane z katalogu.

### Technologie

- Blazor Web App (interaktywność WebAssembly, bez prerenderingu), Bootstrap, mapa Leaflet przez JS interop.
- Tryb offline: szablon Blazor Web App nie ma opcji PWA, więc manifest i service worker trzeba dodać ręcznie. Dlatego punkt 12 jest pierwszy do wycięcia.
- IndexedDB przez JS interop (cienka własna nakładka albo gotowa biblioteka na otwartej licencji).
- Routing: OpenRouteService (profil wózkowy i pieszy) wywoływany przez host.
- Tylko komponenty na otwartych licencjach, bo prawa majątkowe do nagrodzonego rozwiązania przechodzą na fundatora. Dotyczy to też MediatR: sprawdźcie licencję wersji, którą instalujecie.

## 9. Podział ról

| Osoba | Zakres | Materiały do zgłoszenia |
|---|---|---|
| **A: interfejs** | ekran startowy, kreator profilu, lista, karta miejsca, widok planu, dostępność interfejsu | zrzuty ekranu, nagranie ekranu do filmu |
| **B: domena i logika** | model, silnik oceny, podpowiedzi, planer trasy, magazyn IndexedDB, host z API, testy | README, diagram architektury |
| **C: dane, mapa, integracje** | import OSM do `places.json`, Leaflet, routing, AI | opis projektu, prezentacja PDF, montaż filmu |

## 10. Harmonogram godzinowy

Jeśli startujecie z opóźnieniem, skracajcie fazy 2 i 3, a godzinę 08:00 traktujcie jako nieprzesuwalną.

### 11:00-12:00 · Start

- [ ] Wszyscy: zakres, ścieżka demo, kontrakty między modułami (DTO, interfejsy)
- [ ] B: solution z szablonu Blazor Web App (host + klient WebAssembly), wyłączony prerendering, pozostałe projekty, repozytorium, deploy pustej aplikacji
- [ ] C: klucze API (routing, LLM), wycinek mapy, lista 20-25 miejsc demo
- [ ] A: szkic ekranów na kartce, układ strony

### 12:00-17:00 · Fundament

- [ ] B: model domenowy, katalog miejsc ładowany z `places.json`, zapytania o miejsca
- [ ] B: magazyn IndexedDB (profil, plany), pośrednik routingu na hoście
- [ ] C: narzędzie importu OSM → `places.json`, uzupełnienie cech miejsc demo
- [ ] C: mapa Leaflet z pinezkami
- [ ] A: ekran startowy, lista miejsc, szkielet karty miejsca i kreatora profilu

**17:00 kamień milowy:** mapa Krakowa z miejscami z prawdziwych danych, wdrożona na serwerze.

### 17:00-23:00 · Personalizacja

- [ ] A: kreator profilu z zapisem w IndexedDB
- [ ] B: silnik oceny + testy dla trzech person
- [ ] B: podpowiedzi (ranking)
- [ ] A: karta miejsca z uzasadnieniem, kolory zależne od profilu
- [ ] C: integracja routingu, mapowanie profilu na parametry trasy
- [ ] C: szkic 10 slajdów i opisu projektu (same nagłówki i tezy)

**23:00 kamień milowy:** zmiana profilu zmienia oceny i podpowiedzi; pojedyncza trasa A→B rysuje się na mapie.

### 23:00-05:00 · Trasy

- [ ] B: planer (kolejność przystanków, przerwy)
- [ ] C: rysowanie trasy wieloetapowej, bariery na odcinkach
- [ ] A: widok planu dnia, tryb "Załatwiam sprawę" na tych samych komponentach
- [ ] C: opis planu przez AI
- [ ] B: zapis planów w IndexedDB, endpointy REST + OpenAPI na hoście
- [ ] Sen na zmiany, po 2-3 h na osobę; zawsze ktoś pilnuje, żeby wersja na serwerze działała

**05:00 kamień milowy:** pełny scenariusz od profilu do planu dla trzech person.

### 05:00-08:00 · Dopracowanie

- [ ] A: dostępność interfejsu: kontrast, obsługa klawiaturą, etykiety dla czytnika ekranu, duże cele dotykowe
- [ ] B: obsługa błędów, zapisane odpowiedzi routingu i AI jako wariant awaryjny, README
- [ ] C: dopracowanie danych demo, próba ścieżki demo
- [ ] Jeśli jest zapas: zgłoszenie bariery (punkt 10), tryb offline (punkt 12)

**08:00 zamrożenie kodu.** Od tej chwili tylko poprawki błędów blokujących demo.

### 08:00-10:00 · Materiały

- [ ] A: nagranie ekranu wg scenariusza, zrzuty ekranu
- [ ] C: montaż filmu (mp4, maks. 3 min), prezentacja PDF (maks. 10 slajdów)
- [ ] B: diagram architektury, opis projektu, porządek w repozytorium

### 10:00-10:30 · Zgłoszenie

- [ ] Wysyłka przez HackTribe, sprawdzenie, że pliki się otwierają

### 10:30-11:00 · Bufor

Zmiany po 11:00 nie są brane pod uwagę, więc tego czasu nie planujemy na pracę.

## 11. Potencjał rozwoju

- Kolejne miasta: import z OSM i danych miejskich jest powtarzalny.
- Aplikacja mobilna: te same komponenty Razor w .NET MAUI Blazor Hybrid (Domain i Application bez zmian).
- Automatyczne wyciąganie cech z deklaracji dostępności i zdjęć.
- Komunikacja miejska w trasie: pojazdy niskopodłogowe, status wind.
- Dane na żywo: remonty, natężenie ruchu, wydarzenia.
- Centralna baza i synchronizacja: zgłoszenia barier współdzielone między użytkownikami, aktualizacja danych bez nowego wydania.
- Społeczność: weryfikacja zgłoszeń przez organizacje pozarządowe.
- Panel dla miasta: mapa najczęściej napotykanych barier jako wsparcie decyzji inwestycyjnych.
- Otwarte API dla instytucji kultury i branży turystycznej.

## 12. Materiały do zgłoszenia

### Prezentacja PDF (10 slajdów, po polsku)

| Nr | Slajd | Kryterium |
|---|---|---|
| 1 | Tytuł, ID zespołu, jedno zdanie o produkcie | – |
| 2 | Problem i grupy docelowe (trzy persony) | Pomysł |
| 3 | Rozwiązanie: profil → ocena → podpowiedzi → trasa | Pomysł |
| 4 | Zgodność z zadaniem (tabela z sekcji 2) | Związek z kategorią |
| 5 | Personalizacja: to samo miejsce, trzy różne oceny | Pomysł, WOW |
| 6 | Jak działa silnik oceny i planer trasy | Technika |
| 7 | Architektura i stack (diagram) | Projekt, Technika |
| 8 | Dane, ich źródła i wiarygodność | Projekt |
| 9 | Roadmapa i wdrożenie produkcyjne | Projekt, rozwój |
| 10 | Podsumowanie, link do dema i repozytorium | – |

### Film (mp4, maks. 3 min)

| Czas | Treść |
|---|---|
| 0:00-0:20 | Problem: plan "dla wszystkich" nie działa dla nikogo konkretnego |
| 0:20-1:10 | Marta: profil w 20 sekund, podpowiedzi, plan dnia bez bruku i progów |
| 1:10-1:50 | Przełączenie na Kubę: te same miejsca, inne oceny, inna kolejność, ciche przerwy |
| 1:50-2:25 | Pani Zofia: tryb "Załatwiam sprawę", urząd, karta miejsca z barierami i źródłem danych |
| 2:25-2:45 | Architektura i API w jednym ujęciu |
| 2:45-3:00 | Rozwój: kolejne miasta, panel dla urzędu |

### Checklista zgłoszenia (HackTribe, po polsku)

**Obowiązkowe:**

- [ ] Tytuł projektu
- [ ] ID zespołu
- [ ] Opis projektu
- [ ] Prezentacja PDF, maks. 10 slajdów
- [ ] Film mp4, maks. 3 minuty

**Opcjonalne (dodajemy wszystkie):**

- [ ] Zrzuty ekranu
- [ ] Link do repozytorium z README
- [ ] Link do działającego dema
- [ ] Diagram architektury

## 13. Ryzyka

| Ryzyko | Zabezpieczenie |
|---|---|
| Słabe dane o dostępności w OSM | mały wycinek + ręczne uzupełnienie miejsc demo, jawnie oznaczone |
| Limity lub awaria API routingu | cache, zapisane trasy demo |
| AI zmyśla fakty o dostępności | model tylko opisuje dane z katalogu; oceny liczą reguły |
| Za szeroki zakres profili | pełne reguły dla trzech person, reszta uproszczona |
| Brak czasu na materiały | szkic slajdów już wieczorem, zamrożenie kodu o 08:00 |
| Zmęczenie nad ranem | sen na zmiany, najtrudniejsze zadania przed 02:00 |
| Klucze API widoczne w kliencie WebAssembly | wywołania routingu i LLM tylko przez host |
| Długie pierwsze ładowanie WebAssembly | ekran ładowania, mały `places.json` |
| Dane w przeglądarce znikają po wyczyszczeniu | katalog jest w pliku aplikacji; lokalnie tylko profil, plany i zgłoszenia |
| Aplikacja sama niedostępna | test klawiaturą i czytnikiem ekranu w fazie dopracowania |
