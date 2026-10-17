# Luki w danych i bariery: liczby z raportu

Generuje: `dotnet run --project src/Tools -- report krakow` (z plików katalogu w repozytorium). Te same liczby pokazuje strona `/miasto`; na stronie dochodzą bariery potwierdzone przez urząd z bazy hosta.

Miejsca to cele wizyt: wszystkie kategorie katalogu poza ławkami, przystankami i kopertami. „Bez danych” nie liczy hałasu otoczenia z mapy, bo mapa obejmuje każde miejsce.

| Grupa | Miejsca | Z wejściem dla wózków | Bez danych o dostępności | Z danymi o toalecie | Z hałasem otoczenia | Z hałasem w miejscu |
|---|---|---|---|---|---|---|
| **Razem** | 12589 | 2194 (17,4%) | 10388 (82,5%) | 205 (1,6%) | 12588 (100,0%) | 10 |
| Urzędy | 175 | 26 (14,9%) | 149 (85,1%) | 0 (0,0%) | 175 (100,0%) | 1 |
| Kultura | 929 | 143 (15,4%) | 785 (84,5%) | 5 (0,5%) | 929 (100,0%) | 8 |
| Zdrowie | 1041 | 191 (18,3%) | 850 (81,7%) | 4 (0,4%) | 1040 (99,9%) | 1 |
| Edukacja | 448 | 17 (3,8%) | 431 (96,2%) | 1 (0,2%) | 448 (100,0%) | 0 |
| Gastronomia | 2459 | 431 (17,5%) | 2024 (82,3%) | 41 (1,7%) | 2459 (100,0%) | 0 |
| Pozostałe | 7537 | 1386 (18,4%) | 6149 (81,6%) | 154 (2,0%) | 7537 (100,0%) | 0 |

## Dzielnice (poza granicami dzielnic: 8 miejsc)

| Dzielnica | Miejsca | Z wejściem dla wózków | Bez danych o dostępności | Z danymi o toalecie | Z hałasem otoczenia | Z hałasem w miejscu |
|---|---|---|---|---|---|---|
| I Stare Miasto | 3485 | 485 (13,9%) | 2998 (86,0%) | 46 (1,3%) | 3485 (100,0%) | 5 |
| II Grzegórzki | 733 | 169 (23,1%) | 563 (76,8%) | 15 (2,0%) | 733 (100,0%) | 2 |
| III Prądnik Czerwony | 438 | 99 (22,6%) | 338 (77,2%) | 9 (2,1%) | 438 (100,0%) | 0 |
| IV Prądnik Biały | 854 | 123 (14,4%) | 731 (85,6%) | 4 (0,5%) | 854 (100,0%) | 0 |
| V Krowodrza | 764 | 268 (35,1%) | 495 (64,8%) | 17 (2,2%) | 764 (100,0%) | 1 |
| VI Bronowice | 355 | 103 (29,0%) | 252 (71,0%) | 7 (2,0%) | 355 (100,0%) | 0 |
| VII Zwierzyniec | 437 | 71 (16,2%) | 366 (83,8%) | 12 (2,7%) | 437 (100,0%) | 0 |
| VIII Dębniki | 807 | 171 (21,2%) | 635 (78,7%) | 22 (2,7%) | 807 (100,0%) | 1 |
| IX Łagiewniki-Borek Fałęcki | 264 | 27 (10,2%) | 237 (89,8%) | 5 (1,9%) | 264 (100,0%) | 0 |
| X Swoszowice | 205 | 22 (10,7%) | 183 (89,3%) | 1 (0,5%) | 205 (100,0%) | 0 |
| XI Podgórze Duchackie | 534 | 21 (3,9%) | 513 (96,1%) | 2 (0,4%) | 534 (100,0%) | 0 |
| XII Bieżanów-Prokocim | 571 | 73 (12,8%) | 498 (87,2%) | 7 (1,2%) | 571 (100,0%) | 0 |
| XIII Podgórze | 1060 | 157 (14,8%) | 903 (85,2%) | 23 (2,2%) | 1060 (100,0%) | 1 |
| XIV Czyżyny | 754 | 59 (7,8%) | 695 (92,2%) | 13 (1,7%) | 754 (100,0%) | 0 |
| XV Mistrzejowice | 352 | 20 (5,7%) | 332 (94,3%) | 1 (0,3%) | 352 (100,0%) | 0 |
| XVI Bieńczyce | 337 | 66 (19,6%) | 271 (80,4%) | 8 (2,4%) | 337 (100,0%) | 0 |
| XVII Wzgórza Krzesławickie | 108 | 4 (3,7%) | 104 (96,3%) | 1 (0,9%) | 108 (100,0%) | 0 |
| XVIII Nowa Huta | 523 | 252 (48,2%) | 270 (51,6%) | 12 (2,3%) | 523 (100,0%) | 0 |

## 20 miejsc do sprawdzenia w pierwszej kolejności (z 906 kandydatów, rozkład z dnia 07.10.2026)

Kandydaci: urzędy, biblioteki, muzea, instytucje kultury, szkoły oraz szpitale, przychodnie, poradnie, SP ZOZ, pogotowie i hospicja (rozpoznawane po nazwie; prywatne gabinety pomijamy), bez żadnej informacji o wejściu dla wózków. Natężenie ruchu przybliżamy liczbą odjazdów komunikacji miejskiej w dzień powszedni z przystanków w promieniu 300 m (rozkład GTFS ZTP); nie mamy danych o liczbie odwiedzających. Wynik = odjazdy × waga kategorii: urzędy i placówki zdrowia 2 (sprawy, których nie da się odłożyć), pozostałe 1. Przy równym wyniku decyduje nazwa.

| # | Miejsce | Kategoria | Dzielnica | Odjazdy w 300 m | Wynik |
|---|---|---|---|---|---|
| 1 | Izba Administracji Skarbowej w Krakowie, Aleja Zygmunta Krasińskiego 11b | Office | Stare Miasto | 5486 | 10972 |
| 2 | Specjalistyczna Poradnia Diagnozy i Rehabilitacji Dzieci i Młodzieży z Wadą Słuchu PZG | Clinic | Stare Miasto | 4082 | 8164 |
| 3 | Starostwo Powiatowe w Krakowie | Office | Krowodrza | 4082 | 8164 |
| 4 | ДП "Документ", Pawia 5 | Office | Stare Miasto | 3878 | 7756 |
| 5 | Komenda Regionalna Straży Ochrony Kolei w Krakowie, Lubicz 14 | Office | Stare Miasto | 3565 | 7130 |
| 6 | Komisja Nadzoru Finansowego | Office | Stare Miasto | 3552 | 7104 |
| 7 | Agencja Mienia Wojskowego | Office | Stare Miasto | 3523 | 7046 |
| 8 | Szpital Uniwersytecki CM UJ | Clinic | Grzegórzki | 3026 | 6052 |
| 9 | Wody Polskie, Marszałka Józefa Piłsudskiego 22 | Office | Stare Miasto | 3013 | 6026 |
| 10 | Sąd Apelacyjny w Krakowie | Office | Grzegórzki | 2979 | 5958 |
| 11 | Sąd Rejonowy, Mogilska 17 | Office | Grzegórzki | 2979 | 5958 |
| 12 | Najwyższa Izba Kontroli | Office | Krowodrza | 2963 | 5926 |
| 13 | Powiatowy Inspektorat Nadzoru Budowlanego w Krakowie, Aleja Juliusza Słowackiego 20 | Office | Krowodrza | 2963 | 5926 |
| 14 | Wojewódzki Inspektorat Nadzoru Budowlanego | Office | Krowodrza | 2963 | 5926 |
| 15 | Centrum Powiadamiania Ratunkowego, Szlak 73a | Office | Stare Miasto | 2789 | 5578 |
| 16 | Komisariat Policji IV w Krakowie, Królewska 4 | Office | Krowodrza | 2740 | 5480 |
| 17 | Rada i Zarząd Dzielnicy V Krowodrza | Office | Krowodrza | 2740 | 5480 |
| 18 | Punkt Obsługi Programu WFOŚiGW "Czyste Powietrze" | Office | Czyżyny | 2723 | 5446 |
| 19 | Szpital na Siemiradzkiego - Ośrodek Medycyny Matczyno-Płodowej | Clinic | Stare Miasto | 2707 | 5414 |
| 20 | Narodowe Centrum Nauki, Twardowskiego 16 | Office | Dębniki | 2664 | 5328 |
