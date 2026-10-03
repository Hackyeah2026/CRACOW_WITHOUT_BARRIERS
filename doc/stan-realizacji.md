# Stan realizacji: Kraków bez barier

Stan na **3.10.2026, ok. 18:00** (po commicie `8046399`). Punkt odniesienia: [plan-prac.md](plan-prac.md) i [plan-implementacji.md](plan-implementacji.md).

**W skrócie:** działa lokalnie pełna ścieżka profil → miejsca → karta miejsca → plan, na danych z OpenStreetMap, z trasami po ulicach (OpenRouteService) i przejazdami komunikacją miejską według rozkładu ZTP. Brakuje wdrożenia na serwer, publicznego API, opisu AI i danych z MSIP. Największe ryzyka: nietestowana wersja opublikowana, niestabilne połączenie z OpenRouteService i wymyślone dane demonstracyjne.

**Co doszło od poprzedniej wersji tego dokumentu (13:00):** routing po ulicach, komunikacja miejska z rozkładem i przesiadkami, nowy wygląd interfejsu (Łukasz).

**Co doszło 3.10.2026 wieczorem:** przygotowane połączenie hosta z MongoDB (klaster w MongoDB Atlas), a na nim zgłoszenia mieszkańców (brakujące udogodnienia, bariery, błędne dane) i panel urzędnika do ich obsługi; szczegóły w sekcjach "Baza danych (MongoDB)" i "Zgłoszenia i panel urzędnika".

**Co doszło 3.10.2026 późnym wieczorem:** punkty z utrudnieniami zaznaczane na mapie (przeszkody terenowe, hałas, tłum), zakładka "Zgłoś na mapie", weryfikacja punktów i statystyki w panelu urzędnika; punkty potwierdzone przez urzędnika są widoczne na mapie i ostrzegają w planie trasy. Szczegóły w sekcji "Punkty z utrudnieniami na mapie".

**Co doszło 3.10.2026 ok. 20:30:** konta mieszkańców (rejestracja i logowanie samym loginem i hasłem). Wysłanie zgłoszenia miejsca albo punktu na mapie wymaga konta, a urzędnik widzi login zgłaszającego. Szczegóły w sekcji "Konta mieszkańców".

**Co doszło 3.10.2026 ok. 21:00:** strona "Konto" łączy konto, zgłoszenia i profil potrzeb; profil jest zapisywany osobno dla konta (bez konta: konfiguracja tymczasowa); strona główna pokazuje pulpit zalogowanego albo skrócone informacje z zachętą do założenia konta. Szczegóły w sekcji "Konto, profil i strona główna".

**Co doszło 3.10.2026 ok. 22:30:** katalog miejsc rozszerzony z 4 171 do 28 971 miejsc i podzielony na pliki per kategoria, pobierane dopiero wtedy, gdy widok ich potrzebuje. Szczegóły w sekcji "Katalog miejsc: zakres i podział na kategorie".

**Co doszło 3.10.2026 ok. 23:30:** konta firmowe: wniosek dla miejsca z katalogu, zatwierdzenie przez urzędnika, własne oznaczenia udogodnień, certyfikat z kodem QR do pobrania i wyróżnienie na mapie. Szczegóły w sekcji "Konta firmowe i certyfikat", plan w [plan-konto-firmowe.md](plan-konto-firmowe.md).

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
| 7 | Przerwy na trasie | zrobione (ławki) | odcinek dłuższy niż limit marszu z profilu dostaje ławki przy trasie (do 30 m, `RestStopRules.AlongRoute`) jako alternatywę dla przejazdu komunikacją: lista z odległością od początku odcinka i symbole na mapie planu; gdy ławek nie ma na całej trasie, plan podaje najdłuższy fragment bez przerwy; toalety nie są wstawiane |
| – | **Komunikacja miejska (poza pierwotnym planem)** | zrobione | patrz niżej |
| 8 | Opis planu przez AI | brak | |
| 9 | Publiczne API z OpenAPI | brak | jest tylko wewnętrzny `POST /api/route` |
| 10 | Zgłoszenie bariery | zrobione, inaczej niż w planie | zgłoszenia miejsc i punkty na mapie trafiają do bazy na hoście i obsługuje je urzędnik w panelu; punkty potwierdzone przez urzędnika ostrzegają w planie trasy; ocena miejsc się nie zmienia |
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

Stan: **połączenie działa, sześć kolekcji: `reports` (zgłoszenia miejsc), `hazards` (punkty z utrudnieniami), `photos` (zdjęcia dołączone do zgłoszeń), `users` (konta mieszkańców), `businesses` (wnioski i konta firmowe) i `officials` (konta urzędników).** Katalog miejsc i rozkład nadal są plikami statycznymi, a profil zostaje na urządzeniu.

- **Gdzie działa baza:** klaster w MongoDB Atlas (cloud.mongodb.com). Łączy się z nim wyłącznie host (`Web`); przeglądarka nigdy nie dostaje adresu połączenia.
- **Osobny projekt `Infrastructure.Mongo`**, podpięty tylko do hosta. Sterownik (`MongoDB.Driver` 3.12.0, licencja Apache-2.0) nie trafia do `Infrastructure`, bo ten projekt jest też częścią aplikacji w przeglądarce.
- **Konfiguracja (sekcja `Mongo`):** `ConnectionString` (sekret), `Database` (domyślnie `krakow-bez-barier`, wpisane w `appsettings.json`), `ServerSelectionTimeoutSeconds` (5).
- **Rejestracja:** `AddMongo` udostępnia `IMongoClient` i `IMongoDatabase` jako singletony. Klient powstaje przy pierwszym użyciu, więc aplikacja uruchamia się także bez skonfigurowanej bazy.
- **Zapis dokumentów:** pola camelCase, enumy jako tekst, nieznane pola pomijane, czyli tak samo jak w plikach JSON katalogu. `Place.Id` staje się kluczem `_id`.
- **Sprawdzenie połączenia:** `GET /api/health/db` zwraca `ok` z czasem odpowiedzi, `not-configured` (503), gdy brakuje adresu, albo `unreachable` (503), gdy baza nie odpowiada. Szczegóły błędu trafiają tylko do logów hosta.

- **Brak bazy:** repozytoria rzucają `DatabaseUnavailableException`, a endpointy zgłoszeń zamieniają go na 503 z komunikatem dla użytkownika (`MongoCollections`).

Jak dodać kolejną kolekcję: interfejs repozytorium w `Application/Abstractions`, implementacja w `Infrastructure.Mongo` na `MongoCollections`, endpoint w `Web/Endpoints`, klient HTTP w `Infrastructure/Browser` (wzór: zgłoszenia).

### Konta mieszkańców

- **Rejestracja i logowanie** na stronie "Konto" (`/konto`, w menu "Zaloguj" albo login): tylko login (3-30 znaków: litery bez polskich znaków, cyfry, `.`, `-`, `_`; wielkość liter bez znaczenia) i hasło (co najmniej 8 znaków). Bez e-maila, więc **hasła nie da się odzyskać**.
- **Konto jest potrzebne tylko do zgłoszeń.** Miejsca, profil i plan działają bez logowania; profil potrzeb nadal nie opuszcza urządzenia i nie jest częścią konta.
- **Sesja** to ciasteczko hosta `kbb.user` (HttpOnly, SameSite=Strict, 30 dni), osobne od sesji urzędnika `kbb.official`, więc obie mogą działać w jednej przeglądarce i żadna nie daje uprawnień drugiej.
- **Zgłaszający:** host zapisuje login z sesji w polu `reportedBy` zgłoszenia i punktu (nie bierze go z treści żądania). Widzi go tylko urzędnik w panelu; nie ma go w widoku zgłaszającego ani na publicznej liście potwierdzonych punktów. Zgłoszenia sprzed wprowadzenia kont nie mają zgłaszającego.
- **Zakładka "Moje zgłoszenia" na stronie Konto** pokazuje zgłoszenia konta (`GET /api/reports/mine`, `GET /api/hazards/mine`), więc działają na każdym urządzeniu po zalogowaniu. Zgłoszenia wysłane wcześniej anonimowo nie są już widoczne na tej liście.
- **Hasła** jak u urzędników: PBKDF2-SHA256. Rejestracja: limit 5 kont na 10 minut z adresu IP; logowanie: 5 prób na minutę; zajęty login → 409.

**Endpointy:** `POST /api/account/register`, `POST /api/account/login`, `POST /api/account/logout`, `GET /api/account/me`.

### Konto, profil i strona główna

- **Strona "Konto"** (`/konto`; stary adres `/zgloszenia` prowadzi w to samo miejsce) zastąpiła osobne strony konta i zgłoszeń. Bez logowania: formularz logowania i rejestracji (`/konto?nowe=true` otwiera rejestrację). Po zalogowaniu dwie zakładki: "Moje zgłoszenia" i "Profil potrzeb" (`/konto/profil`) z pełnym edytorem profilu.
- **Profil potrzeb a konto.** Profil nadal jest tylko w IndexedDB na urządzeniu, ale ma dwa rodzaje wpisów: konfiguracja tymczasowa (bez konta, klucz `current`) i profil konta (klucz `account:{login}`). Aplikacja używa profilu konta, gdy ktoś jest zalogowany, a po wylogowaniu wraca do konfiguracji tymczasowej. Konto bez profilu przejmuje konfigurację tymczasową przy pierwszym logowaniu na danym urządzeniu.
- **Strona "Profil"** (`/profil`): dla zalogowanych podsumowanie zapisanego profilu z przejściem do edycji na stronie Konto; dla niezalogowanych edytor konfiguracji tymczasowej z informacją, że konto pozwala ją zapisać.
- **Strona główna:** dla zalogowanych pulpit (profil, liczba miejsc w planie, zgłoszenia według etapu obsługi) i wybór trybu; dla niezalogowanych wybór trybu, trzy krótkie opisy funkcji i zachęta do założenia konta. Sekcja "3 osoby, 3 podróże" została usunięta.
- Edytor profilu jest wspólnym komponentem (`ProfileEditor`); podgląd pokazuje niezapisane jeszcze zmiany.

### Zgłoszenia i panel urzędnika

**Mieszkaniec (zalogowany, patrz "Konta mieszkańców"):**

- Na karcie miejsca przycisk "Zgłoś": rodzaj (brakuje udogodnienia / bariera / błędne dane w aplikacji), lista udogodnień do zaznaczenia (winda, toaleta, pętla indukcyjna, PJM, ławki itd.), opis do 1000 znaków.
- Wysyłane jest miejsce, udogodnienia i opis; host dopisuje login konta. **Profil potrzeb nie trafia na serwer**; formularz mówi to wprost i prosi, żeby nie wpisywać danych osobowych.
- Strona **"Konto"** pokazuje status i odpowiedź urzędu dla zgłoszeń konta.
- `POST /api/reports` ma limit 10 zgłoszeń na 10 minut z jednego adresu IP.

**Urzędnik (`/urzednik`, link w stopce):**

- Logowanie loginem i hasłem. Sesja to ciasteczko hosta `kbb.official` (HttpOnly, SameSite=Strict, 8 godzin); kod w przeglądarce go nie widzi. Logowanie: limit 5 prób na minutę z adresu IP; odpowiedź dla nieistniejącego loginu trwa tyle samo co dla złego hasła.
- Zestawienie otwartych zgłoszeń: liczba, czego najczęściej brakuje, miejsca z największą liczbą zgłoszeń; mapa miejsc z otwartymi zgłoszeniami (kliknięcie filtruje listę).
- Lista z filtrem statusu; zmiana statusu (nowe → sprawdzane → zaplanowane → rozwiązane / odrzucone) z odpowiedzią widoczną dla zgłaszającego. Zapisywany jest login urzędnika, ale zgłaszający go nie widzi.

**Konta urzędników** zakłada host przy starcie z sekcji `Officials:Seed` (hasła jako PBKDF2-SHA256). Lokalnie:

```bash
dotnet user-secrets set "Officials:Seed:0:Login" "urzednik" --project src/Web
dotnet user-secrets set "Officials:Seed:0:Password" "<hasło>" --project src/Web
dotnet user-secrets set "Officials:Seed:0:DisplayName" "Jan Kowalski" --project src/Web
dotnet user-secrets set "Officials:Seed:0:Unit" "Pełnomocnik ds. osób z niepełnosprawnościami" --project src/Web
```

Na serwerze: zmienne `Officials__Seed__0__Login`, `Officials__Seed__0__Password` itd. Zmiana hasła w konfiguracji zmienia je w bazie przy następnym starcie.

**Endpointy:** `POST /api/reports` i `GET /api/reports/mine` (wymagają konta mieszkańca), `POST /api/official/login`, `POST /api/official/logout`, `GET /api/official/me`, `GET /api/official/reports?cityId=&status=&placeId=`, `PATCH /api/official/reports/{id}`.

### Konta firmowe i certyfikat

**Właściciel firmy (zakładka "Konto firmowe" na stronie Konto, `/konto/firma`; link w stopce i na karcie miejsca):**

- Konto firmowe to zwykłe konto (login i hasło) z wnioskiem zatwierdzonym przez urząd. Jedno konto prowadzi jedną firmę w jednym miejscu z katalogu; punkty w terenie (przystanek, ławka, park, koperta, toaleta) nie wchodzą w grę.
- **Wniosek:** miejsce wybrane z katalogu (rodzaj + nazwa, albo podpowiedziane z karty miejsca), nazwa firmy, NIP (sprawdzana cyfra kontrolna), kontakt dla urzędu, uwagi. NIP i kontakt widzi tylko urzędnik i właściciel. Po odrzuceniu albo cofnięciu zatwierdzenia wniosek można złożyć ponownie.
- **Po zatwierdzeniu:** lista 15 udogodnień z wyborem "nie podano / jest / nie ma" i certyfikat: podgląd, pobranie pliku SVG, otwarcie w nowej karcie do druku albo zapisu jako PDF.

**Urzędnik (`/urzednik`, zakładka "Konta firmowe" z licznikiem wniosków czekających na decyzję):**

- Widzi nazwę firmy, miejsce, NIP, kontakt, login konta, uwagi i deklaracje. Decyzje: zatwierdzenie, odrzucenie wniosku i cofnięcie zatwierdzenia; dwie ostatnie wymagają uzasadnienia, które widzi firma.
- Jedno miejsce ma najwyżej jedno zatwierdzone konto firmowe: pilnuje tego unikalny indeks częściowy w bazie, więc drugi wniosek kończy się odpowiedzią 409 także przy równoległych decyzjach.

**Co dzieje się po zatwierdzeniu:**

- **Oznaczenia** trafiają na kartę miejsca ze źródłem "Deklaracja firmy (konto firmowe)" i datą zmiany, i wchodzą do oceny dostępności na liście, karcie i w planie. Deklaracja zastępuje cechę z mapy o tym samym kluczu; deklaracja dostępności dla wózków zastępuje też wpis "ograniczona dostępność" z OpenStreetMap. Łączenie robi dekorator katalogu w przeglądarce (`CertifiedPlaceCatalog`); gdy host nie odpowiada, katalog działa jak dotąd.
- **Certyfikat** (`CertificateSvg`): A4 poziomo, nazwa firmy, numer `KBB-rok-XXXXXXXX` nadany przy pierwszym zatwierdzeniu, data, kod QR (QRCoder 1.8.0, licencja MIT) z adresem `/miejsca/{id}?miasto={miasto}`. Host generuje go na żądanie, niczego nie przechowuje. Karta miejsca pokazuje firmę, numer i datę certyfikatu, więc skan kodu jest też sprawdzeniem, czy certyfikat jest aktualny.
- **Wyróżnienie:** większa pinezka z gwiazdką w złotej obwódce (wypełnienie nadal w kolorze oceny), dopisek "certyfikat Kraków bez barier" w nazwie pinezki, znaczek "Certyfikat" na liście, filtr "Tylko z certyfikatem". Miejsca z certyfikatem spoza kategorii bieżącego trybu (np. hotel w trybie "Zwiedzam") są na mapie także bez wybrania kategorii.
- **Cofnięcie zatwierdzenia** od razu zdejmuje wyróżnienie i deklaracje z katalogu i blokuje pobieranie certyfikatu. Ponowne zatwierdzenie przywraca ten sam numer.

**Adres w kodzie QR** to adres, pod którym host dostał żądanie. Za pośrednikiem (reverse proxy) trzeba ustawić `Certificates:PublicBaseUrl` (zmienna `Certificates__PublicBaseUrl`), inaczej kod poprowadzi na adres wewnętrzny.

**Endpointy:** `GET /api/businesses?cityId=` (publiczny), `GET /api/business/mine`, `POST /api/business/application`, `PUT /api/business/features`, `GET /api/business/certificate` (konto; dwa ostatnie tylko po zatwierdzeniu, inaczej 409), `GET /api/official/businesses?cityId=`, `PATCH /api/official/businesses/{login}` (urzędnik).

### Zdjęcia w zgłoszeniach

- **Zdjęcie dodane w formularzu jest częścią zgłoszenia** (miejsca i punktu na mapie), razem z oceną AI z chwili wysłania: prawdopodobieństwem przeszkody, rodzajem i opisem. Gdy analiza się nie uda, zdjęcie trafia do zgłoszenia bez oceny.
- **Zmniejszanie przed zapisem** robi przeglądarka (`js/photo.js`): do analizy idzie wersja do 1600 px, do bazy osobna kopia JPEG do 1024 px na dłuższym boku. Gdy plik jest większy niż 300 KB, kolejne próby zmniejszają bok i jakość. Zdjęcie testowe 4000×3000 px (10 MB) zajęło w bazie 156 KB. Host nie ufa przeglądarce: zgłoszenie ze zdjęciem ponad 300 KB albo w innym formacie niż JPEG dostaje 400.
- **Gdzie leży:** plik w osobnej kolekcji `photos`, a w zgłoszeniu tylko odnośnik (`photo`: identyfikator, rozmiar, ocena AI). Dzięki temu lista zgłoszeń w panelu nie pobiera plików.
- **Kto widzi:** urzędnik (każde zdjęcie) i konto, które je wysłało (`GET /api/photos/{id}`; bez sesji 401, cudze zdjęcie 404). Zdjęcia nie ma w publicznym widoku potwierdzonego punktu, bo mogą być na nim ludzie i tablice rejestracyjne.
- **Ograniczenia:** ocenę AI odsyła przeglądarka zgłaszającego, więc host tylko doprowadza ją do dozwolonych zakresów i nie może sprawdzić, czy pochodzi z modelu; urzędnik widzi ją jako podpowiedź. Zdjęć nie da się jeszcze usunąć z bazy z poziomu aplikacji.

### Punkty z utrudnieniami na mapie

**Mieszkaniec (zalogowany), zakładka "Zgłoś na mapie" (`/zglos`):**

- Tryb "Utrudnienie w terenie": kliknięcie w mapę albo przycisk "Jestem tutaj" stawia przesuwalną pinezkę; do tego rodzaj (schody, wysoki krawężnik, nierówna nawierzchnia, stromy odcinek, wąskie przejście, roboty, hałas, tłum, ostre światło, inne) i opis do 500 znaków. Wysyłane jest tylko położenie, rodzaj i opis.
- Tryb "Miejsce z katalogu": wybór miejsca na mapie albo z wyszukiwarki i ten sam formularz zgłoszenia co na karcie miejsca.
- Strona "Zgłoszenia" pokazuje decyzję urzędu dla punktów konta. Mapę i potwierdzone punkty widzi każdy; konto jest potrzebne dopiero do wysłania.

**Urzędnik (`/urzednik`), trzy zakładki:**

- "Zgłoszenia miejsc": jak dotąd.
- "Punkty na mapie": lista z filtrem statusu i mapą; decyzja: czeka na weryfikację → potwierdzone / odrzucone / już nie występuje, z opcjonalną odpowiedzią dla zgłaszającego.
- "Statystyki": udział zamkniętych zgłoszeń, mediana czasu do decyzji, sprawy czekające ponad 7 dni, nowe zgłoszenia z 14 dni, rozkłady według statusu, rodzaju, kategorii miejsca, brakujących udogodnień i rodzaju utrudnienia (`ReportAnalytics`).

**Co dzieje się z punktem potwierdzonym:**

- Jest publiczny (`GET /api/hazards?cityId=`): symbol na mapie miejsc i na mapie zgłaszania. Opis zgłaszającego staje się widoczny dla wszystkich dopiero po potwierdzeniu.
- Przy układaniu planu każdy odcinek dostaje punkty leżące do 40 m od trasy (`HazardRules.AlongRoute`), w kolejności marszu: komunikat "W tym miejscu jest zweryfikowane utrudnienie dla ...", odległość od trasy, data potwierdzenia, symbol na mapie planu. Punkty istotne dla profilu (`HazardRules.Concerns`, np. schody dla wózka, hałas dla profilu sensorycznego) są wyróżnione i policzone w podsumowaniu planu.
- Trasa **nie omija** punktu, plan tylko o nim ostrzega. Gdy host nie odpowiada, plan układa się bez ostrzeżeń.

**Endpointy:** `GET /api/hazards?cityId=`, `POST /api/hazards` i `GET /api/hazards/mine` (wymagają konta mieszkańca; limit wspólny ze zgłoszeniami), `GET /api/official/hazards?cityId=`, `PATCH /api/official/hazards/{id}`.

### Katalog miejsc: zakres i podział na kategorie

**Rozważone warianty wczytywania:** jeden duży plik (kilkanaście MB przy każdym wejściu), pliki per kategoria, kafle przestrzenne (ranking całego miasta przestaje działać bez pobrania wszystkiego), miejsca w MongoDB z zapytaniami po obszarze (katalog zależny od bazy), odpytywanie OpenStreetMap na żywo (publiczne Overpass API jest zawodne i wolne: podczas importu trzy instancje na zmianę zwracały błędy, limity czasu i nieaktualne dane). **Wybrane: pliki per kategoria**, bo aplikacja i tak pracuje zestawami kategorii, a katalog zostaje statyczny i działa bez serwera.

- **Układ:** `data/{miasto}/places/index.json` (data importu, kategorie z liczbą miejsc) i `places/{Kategoria}.json`. `IPlaceCatalog` ma `GetAsync(miasto, kategorie)`, `FindAsync(miasto, id)` i `GetCategoriesAsync`; nie ma już metody zwracającej całe miasto.
- **Co się pobiera:** lista w trybie "Załatwiam sprawę" to 7 plików (ok. 1,4 MB przed kompresją), "Zwiedzam" 6 plików (ok. 0,75 MB). Ławki (3,4 MB) i sklepy (1,3 MB) pobierają się dopiero po wybraniu kategorii w filtrze. Kompresja serwera zmniejsza pliki 6-15 razy (ławki: ok. 220 KB).
- **Karta miejsca z bezpośredniego adresu** szuka najpierw w plikach już pobranych, potem w pozostałych od najmniejszych.
- **Nowe kategorie:** apteka (wydzielona ze "Zdrowia"), miejsce kultu, park, sklep, nocleg, usługi (poczta, bank, pomoc społeczna; poczta przeszła z "Urzędu"), szkoła i uczelnia. Ławki i koperty, dotąd puste, są importowane. Tryb "Zwiedzam" obejmuje dodatkowo miejsca kultu i parki, "Załatwiam sprawę" apteki i usługi; reszta jest w filtrze kategorii z liczbą miejsc.
- **Zmiany w imporcie:** cztery osobne zapytania z ponawianiem; perony uzupełniają cechy przystanków (387 przystanków); restauracje także bez tagu `wheelchair`; toalety prywatne pominięte, "dla klientów" i płatne opisane; ulice oznaczone jako atrakcje pominięte; adres także z `addr:place`; nowe tagi `elevator`, `hearing_loop`, `dog=yes`; data cechy to data sprawdzenia z OSM (`check_date`), a gdy jej nie ma, data ostatniej edycji obiektu (kolumna na karcie miejsca nazywa się teraz "Stan na dzień"). Importer odrzuca instancję Overpass z danymi starszymi niż 2 dni.
- **Mapa:** powyżej 600 pinezek symbole są zastępowane kółkami rysowanymi na płótnie, żeby tysiące ławek nie zatrzymały przeglądarki.

| Kategoria | Miejsc | Z informacją o wózkach |
|---|---|---|
| Ławka | 12 437 | 0 |
| Sklep | 6 435 | 1 096 |
| Jedzenie | 2 400 | 407 |
| Przystanek | 2 179 | 441 |
| Koperta | 1 871 | 21 |
| Zdrowie | 746 | 96 |
| Szkoła, uczelnia | 449 | 18 |
| Nocleg | 383 | 55 |
| Atrakcja | 364 | 42 |
| Poczta, bank, pomoc | 314 | 80 |
| Apteka | 273 | 85 |
| Miejsce kultu | 229 | 27 |
| Toaleta | 226 | 135 |
| Urząd | 166 | 24 |
| Park | 163 | 5 |
| Kultura | 127 | 22 |
| Muzeum | 123 | 27 |
| Biblioteka | 86 | 21 |

**Znany brak w zapisanych danych:** pierwsza grupa zapytań (miejsca z nazwą) przyszła z instancji z nieaktualną bazą, więc brakuje ok. 25 niedawno dodanych obiektów, w tym "Przychodni Medycyna Polska" ze ścieżki demo (jej ręczne uzupełnienie nie miało się do czego przypiąć). Kontrola świeżości jest już w importerze; trzeba powtórzyć `dotnet run --project src/Tools -- import krakow`, gdy główna instancja Overpass będzie dostępna.

### Warstwy

| Projekt | Co zawiera |
|---|---|
| `Domain` | model miejsc i cech, profil potrzeb z gotowymi profilami, silnik oceny (ruch, sensoryka, kondycja), model planu, sieć komunikacji i wyszukiwarka połączeń |
| `Application` | `Result`, `ICommand` / `IQuery` nad MediatR, zapytania o miejsca i kartę miejsca, zapis i odczyt profilu, układanie planu (kolejność, odcinki, komunikacja), ranking podpowiedzi, zgłoszenia i zestawienie dla urzędu |
| `Infrastructure` | katalog miejsc i sieć komunikacji z plików statycznych, magazyn IndexedDB, klient routingu z cache i wariantem awaryjnym, klient OpenRouteService po stronie hosta |
| `Web.Client` | strony: start, profil, miejsca, karta miejsca z formularzem zgłoszenia, plan, moje zgłoszenia, panel urzędnika; mapa Leaflet; panel komunikacji; stan sesji |
| `Infrastructure.Mongo` | połączenie hosta z MongoDB: ustawienia, rejestracja klienta, konwencje zapisu, sprawdzenie połączenia; repozytorium zgłoszeń, konta urzędników, indeksy i konta zakładane przy starcie |
| `Web` | host: serwuje aplikację, pośredniczy w routingu, sprawdza połączenie z bazą (`GET /api/health/db`), przyjmuje zgłoszenia, loguje urzędników |
| `Tools` | `import <miasto>`: miejsca z OpenStreetMap + ręczne uzupełnienia; `transit <miasto>`: rozkład z GTFS |
| `Tests` | 139 testów: zdjęcia w zgłoszeniach (limit i format, odnośnik w zgłoszeniu, widoki, zapis w BSON), konta firmowe (walidacja wniosku i NIP, decyzje urzędnika, deklaracje, widoki bez danych firmy, łączenie z katalogiem, certyfikat z kodem QR), mapowanie tagów OpenStreetMap na kategorie i cechy, profil konta i konfiguracja tymczasowa, konta mieszkańców (walidacja rejestracji, zgłaszający w dokumencie i poza widokami publicznymi), punkty z utrudnieniami (walidacja, pas wokół trasy, dopasowanie do profilu, statystyki), silnik oceny, łączenie profili, kolejność przystanków, układanie planu, zapytania i odpowiedzi OpenRouteService, wyszukiwarka połączeń, obszar mapy, zapis dokumentów MongoDB i zachowanie bez bazy, walidacja i zapis zgłoszeń, hasła urzędników, zestawienie zgłoszeń |

### Dane

| Plik | Zawartość | Źródło |
|---|---|---|
| `data/krakow/places/` (18 plików, 7,4 MB) | 28 971 miejsc w całym Krakowie, po jednym pliku na kategorię; liczby w sekcji "Katalog miejsc" | OpenStreetMap, pobrane 3.10.2026 |
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
- **Zgłoszenia na klastrze Atlas** (osobna baza `krakow-bez-barier-test`, konto urzędnika z zmiennych środowiskowych): wysłanie zgłoszenia z karty Sukiennic, walidacja pustego formularza, lista "Moje zgłoszenia", logowanie urzędnika (złe hasło i nieznany login → 401, szósta próba w minucie → 429), zestawienie i mapa w panelu, zmiana statusu z odpowiedzią widoczna u zgłaszającego, wylogowanie.

- **Punkty z utrudnieniami na bazie testowej** (`krakow-bez-barier-test`): zgłoszenie punktu z mapy, lista "Zgłoszenia", potwierdzenie w panelu, statystyki, symbol na mapie miejsc, ostrzeżenie i symbol w planie (profil "kule lub balkonik", schody ok. 40 m od trasy), zgłoszenie miejsca wybranego na mapie.

- **Konta mieszkańców na bazie testowej:** bez konta formularze pokazują prośbę o logowanie, a `POST /api/hazards` i `GET /api/reports/mine` zwracają 401; rejestracja z powrotem do formularza; zajęty login → 409; wysłanie punktu z konta i lista "Zgłoszenia"; login zgłaszającego w panelu urzędnika; sesja mieszkańca nie daje dostępu do panelu; wylogowanie, złe hasło → 401, ponowne logowanie.

- **Konto, profil i strona główna** (baza testowa): strona główna bez konta i pulpit po zalogowaniu; konfiguracja tymczasowa "Senior" przejęta przez konto przy logowaniu; podsumowanie profilu na `/profil` i edytor na `/konto/profil`; zakładka zgłoszeń pod `/konto` i `/zgloszenia`.

- **Konta firmowe na bazie testowej:** wniosek z formularza (wyszukanie lokalu, błędny NIP zatrzymany, status "czeka"); bez konta 401, sesja mieszkańca nie zatwierdza wniosków (401); przed zatwierdzeniem oznaczenia i certyfikat → 409; zatwierdzenie z panelu urzędnika; drugi wniosek i druga decyzja dla tego samego miejsca → 409; odrzucenie bez uzasadnienia → 400; zapis oznaczeń i ich widok na karcie miejsca ze zmianą oceny; podgląd certyfikatu, a kod QR odczytany z niego przez przeglądarkę (BarcodeDetector) daje adres karty miejsca; pinezki z gwiazdką, znaczek na liście i filtr "Tylko z certyfikatem"; cofnięcie zatwierdzenia zdejmuje miejsce z listy publicznej i blokuje certyfikat, ponowne zatwierdzenie zostawia numer.

### Nie sprawdzone

- Konto firmowe: kliknięcie "Pobierz certyfikat" w przeglądarce (endpoint sprawdzony osobno: plik SVG jako załącznik), wydruk i zapis jako PDF, skan kodu QR telefonem z kartki, adres w kodzie QR za pośrednikiem (`Certificates:PublicBaseUrl`), widok na telefonie i z czytnikiem ekranu.
- Zgłoszenia na bazie `krakow-bez-barier` (testy szły na osobnej bazie testowej) i na serwerze.
- Panel urzędnika i formularz zgłoszenia na telefonie i z czytnikiem ekranu.
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
| Limity zgłoszeń i logowań liczone per adres IP | za reverse proxy na serwerze wszyscy mają ten sam adres, więc limit będzie wspólny | włączyć `UseForwardedHeaders` z adresem proxy przy wdrożeniu |
| Ciasteczko urzędnika ma flagę `Secure` tylko przy żądaniu HTTPS | za proxy kończącym HTTPS host widzi HTTP | jak wyżej: nagłówki `X-Forwarded-Proto` |
| Trasa nie omija potwierdzonych utrudnień | plan ostrzega, ale prowadzi tą samą drogą | OpenRouteService przyjmuje obszary do ominięcia (`avoid_polygons`); do podpięcia dla punktów istotnych dla profilu |
| Publiczna lista potwierdzonych punktów jest pobierana w całości (do 2000) | przy dużej liczbie punktów rośnie odpowiedź | zapytanie po obszarze mapy |
| Opis punktu jest tekstem mieszkańca pokazywanym publicznie po potwierdzeniu | urzędnik musi go przeczytać przed potwierdzeniem; nie może go poprawić | edycja opisu w panelu |
| Profil konta jest zapisany na urządzeniu, nie na serwerze | po zalogowaniu na innym urządzeniu profil trzeba ustawić od nowa; na wspólnym urządzeniu profil konta zostaje w przeglądarce po wylogowaniu (nie jest używany, ale nie jest kasowany) | zgodne z zasadą, że dane o zdrowiu nie opuszczają urządzenia; synchronizacja przez serwer wymagałaby zgody użytkownika i decyzji zespołu |
| Tytuł w banerze strony głównej był granatowy na granatowym tle | nieczytelny nagłówek | poprawione: biały tekst |
| Konto mieszkańca nie ma e-maila | nie ma odzyskiwania ani zmiany hasła, nie da się też usunąć konta z aplikacji | zmiana hasła po zalogowaniu i usuwanie konta; odzyskiwanie wymaga kanału kontaktu |
| Login zgłaszającego to dane o osobie, powiązane z miejscami, które zgłasza | wchodzi w zakres RODO (informacja o przetwarzaniu, prawo do usunięcia) | krótka informacja przy rejestracji i procedura usunięcia konta przed wdrożeniem dla urzędu |
| Lista zgłoszeń w panelu ma limit 500 najnowszych | przy większej liczbie starsze nie są widoczne | stronicowanie, gdy będzie potrzebne |
| Zmienna `--neutral-400` nie była zdefiniowana w `app.css` | pola wyboru (profil, formularze) nie miały obramowania | poprawione: `#6b7280`, kontrast 4,8:1 |

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
| Bez bazy danych: pliki statyczne i IndexedDB | doszło połączenie hosta z MongoDB z kolekcjami zgłoszeń i kont urzędników | zgłoszenia mają trafiać do urzędu, a nie zostawać na urządzeniu |
| Zgłoszenie bariery zapisane lokalnie, zmienia ocenę i trasę | zgłoszenie trafia do bazy na hoście i do panelu urzędnika; na urządzeniu zostaje tylko jego identyfikator; ocena i trasa się nie zmieniają | decyzja zespołu: zgłoszenia jako źródło informacji dla miasta; wpływ na ocenę dopiero po weryfikacji przez urzędnika |
| "Bez konta" | konto ma urzędnik, a mieszkaniec zakłada je (login i hasło) dopiero, gdy chce coś zgłosić | decyzja zespołu: urząd ma wiedzieć, kto zgłasza; przeglądanie, profil i plan zostają bez konta, a profil nie opuszcza urządzenia |
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

8. **Co jeszcze trafia do MongoDB.** Zgłoszenia już tam są. Kandydaci: katalog miejsc, zapisane plany. Profil potrzeb powinien zostać na urządzeniu: aplikacja deklaruje, że dane o zdrowiu go nie opuszczają.
9. **Czy zgłoszenia mają wpływać na ocenę miejsc.** Plan zakładał, że zgłoszenie zmienia ocenę i trasę. Teraz trafia tylko do urzędu. Prosty krok: karta miejsca pokazuje zgłoszenia przyjęte przez urzędnika jako osobne źródło ("zgłoszenie mieszkańca, potwierdzone").

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
| 11 | Konta urzędników na serwerze (`Officials__Seed__...`), zgłoszenia potwierdzone przez urząd widoczne na karcie miejsca | zgłoszenia i panel działają lokalnie |
| 12 | Omijanie potwierdzonych utrudnień przez routing, punkty w pobliżu miejsca na jego karcie | teraz plan tylko ostrzega |
