# Plan: konto firmowe, certyfikat z kodem QR i wyróżnienie na mapie

Stan na 3.10.2026. Punkt odniesienia: [stan-realizacji.md](stan-realizacji.md), wzór warstw: zgłoszenia i punkty z utrudnieniami.

## 1. Zakres

1. Właściciel firmy (kawiarnia, hotel, restauracja, sklep…) składa **wniosek o konto firmowe** dla miejsca z katalogu.
2. **Urzędnik zatwierdza albo odrzuca** wniosek w panelu; może też cofnąć zatwierdzenie.
3. Zatwierdzone konto **samo oznacza udogodnienia** swojego miejsca; trafiają one na kartę miejsca i do oceny dostępności.
4. Zatwierdzone konto **pobiera certyfikat** (plik SVG, do druku także jako PDF) z **kodem QR prowadzącym do karty miejsca**.
5. Miejsca z certyfikatem są **wyróżnione na mapie** i na liście.

## 2. Decyzje

| Kwestia | Decyzja | Dlaczego |
|---|---|---|
| Czym jest konto firmowe | zwykłe konto (login + hasło, sesja `kbb.user`) z zatwierdzonym wnioskiem; jedno konto = jedna firma = jedno miejsce | nie powstaje trzeci rodzaj sesji; rejestracja i logowanie już działają |
| Jakie miejsce | tylko miejsce z katalogu (identyfikator OSM); bez punktów w terenie (przystanek, ławka, park, koperta, toaleta) | kod QR ma dokąd prowadzić, a oznaczenia mają się do czego dopisać |
| Dane we wniosku | nazwa firmy, NIP (z sumą kontrolną), kontakt dla urzędu, uwagi | urzędnik musi móc sprawdzić, kto składa wniosek; NIP i kontakt widzi tylko urząd i właściciel |
| Oznaczenia | lista udogodnień tak/nie (wejście bez stopni, winda, toaleta, pętla, PJM, ciche godziny…); bez cech liczbowych | proste do wypełnienia i jednoznaczne w ocenie |
| Skąd widać, kto podał cechę | źródło „Deklaracja firmy (konto firmowe)” i data aktualizacji przy każdej cesze | dane od właściciela nie mogą udawać danych z OSM ani urzędu |
| Gdzie łączymy oznaczenia z katalogiem | dekorator `IPlaceCatalog` w przeglądarce | lista, karta miejsca i plan dostają te same dane bez zmian w zapytaniach |
| Certyfikat | SVG generowany przez host na żądanie, numer `KBB-rok-XXXXXXXX` nadawany przy zatwierdzeniu | bez przechowywania plików; cofnięcie zatwierdzenia od razu odbiera pobieranie |
| Kod QR | biblioteka QRCoder (MIT), rysowany jako ścieżka wektorowa; adres `…/miejsca/{id}?miasto={miasto}` | karta miejsca pokazuje certyfikat, więc skan jest też weryfikacją |
| Dwa konta na jedno miejsce | unikalny indeks częściowy (miasto, miejsce) dla statusu „zatwierdzone” | baza pilnuje tego także przy równoległych decyzjach |

## 3. Implementacja

| Warstwa | Zmiana |
|---|---|
| `Domain/Businesses` | `BusinessApplicationDraft` (walidacja, NIP), `BusinessAccount` (statusy `Pending / Approved / Rejected / Revoked`, decyzja, numer certyfikatu, oznaczenia), `BusinessReview`, `BusinessFeaturesUpdate`, widoki: `BusinessAccountView` (właściciel, bez loginu urzędnika) i `CertifiedPlace` (publiczny, bez NIP, kontaktu i loginu) z `ApplyTo(Place)` |
| `Domain/Places` | `Place.Certificate` (firma, numer, data) |
| `Application` | `IBusinessRepository`, `IBusinessClient`, rozszerzenie `IOfficialClient`; komendy i zapytania: wniosek, moje konto firmowe, zapis oznaczeń, lista certyfikowanych, lista i decyzja urzędnika |
| `Infrastructure.Mongo` | kolekcja `businesses` (kluczem login), indeksy, zapis z kontrolą wersji |
| `Infrastructure` | `HostBusinessClient`, `CertifiedPlaceCatalog` (dekorator katalogu), `CertificateSvg` (certyfikat z kodem QR) |
| `Web` | `BusinessEndpoints` (niżej) |
| `Web.Client` | zakładka „Konto firmowe” na stronie Konto, zakładka „Konta firmowe” w panelu urzędnika, certyfikat na karcie miejsca, wyróżnione pinezki i filtr „tylko z certyfikatem” na liście miejsc |

**Endpointy**

| Endpoint | Kto | Co |
|---|---|---|
| `GET /api/businesses?cityId=` | publiczny | miejsca z certyfikatem i ich oznaczenia |
| `GET /api/business/mine` | konto | wniosek albo konto firmowe zalogowanego |
| `POST /api/business/application` | konto | nowy wniosek (także ponowny po odrzuceniu); limit jak dla zgłoszeń |
| `PUT /api/business/features` | konto zatwierdzone | zapis oznaczeń |
| `GET /api/business/certificate` | konto zatwierdzone | certyfikat SVG (`?inline=true` do podglądu) |
| `GET /api/official/businesses?cityId=` | urzędnik | wszystkie wnioski |
| `PATCH /api/official/businesses/{login}` | urzędnik | decyzja z uzasadnieniem |

## 4. Testy

**Automatyczne (`BusinessTests`)**

- walidacja wniosku: NIP (suma kontrolna, zapis z kreskami), wymagane pola, limity długości, kategorie bez wnętrza;
- nowy wniosek ma status „czeka” i nie ma certyfikatu;
- decyzje: zatwierdzenie nadaje numer i datę certyfikatu; odrzucenie i cofnięcie wymagają uzasadnienia; nie da się wrócić do „czeka”, cofnąć niezatwierdzonego ani odrzucić zatwierdzonego; ponowne zatwierdzenie zachowuje numer;
- oznaczenia: tylko konto zatwierdzone, tylko udogodnienia z listy, bez powtórzeń;
- widoki: właściciel nie widzi loginu urzędnika; widok publiczny nie zawiera NIP, kontaktu ani loginu;
- łączenie z katalogiem: deklaracja zastępuje cechę o tym samym kluczu, dopisuje nowe, ustawia źródło, datę i certyfikat; inne miejsca bez zmian; niedostępny host nie psuje katalogu;
- ocena dostępności korzysta z deklaracji firmy;
- zapis w BSON: enumy jako tekst, kluczem login, pełny obieg;
- certyfikat: poprawny XML, nazwa z `<`, `&` i cudzysłowem zakodowana, numer, adres i kod QR obecne;
- komendy: błędny wniosek i błędne oznaczenia nie są wysyłane; brak bazy → `DatabaseUnavailableException`.

**W przeglądarce (baza `krakow-bez-barier-test`)**

1. Rejestracja konta, wniosek dla kawiarni; błędny NIP zatrzymuje formularz.
2. Przed decyzją: `PUT features` i `GET certificate` → 409; bez konta → 401; sesja mieszkańca nie zatwierdza wniosków (401).
3. Urzędnik zatwierdza wniosek; drugi wniosek na to samo miejsce z innego konta → 409.
4. Właściciel zapisuje oznaczenia; karta miejsca pokazuje je ze źródłem „Deklaracja firmy” i zmienia ocenę.
5. Certyfikat: podgląd, pobranie, kod QR odczytany z pliku prowadzi do karty miejsca.
6. Mapa i lista: wyróżniona pinezka, znaczek na karcie, filtr „tylko z certyfikatem”.
7. Cofnięcie zatwierdzenia: miejsce traci wyróżnienie i oznaczenia, certyfikat przestaje się pobierać.
