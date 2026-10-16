# Dostępność aplikacji (WCAG 2.2 AA)

Stan na 17.10.2026. Cel: WCAG 2.2 na poziomie AA.

## Jak sprawdzamy

Automatyczny audyt [`tools/a11y/audit.js`](../tools/a11y/audit.js): axe-core z regułami WCAG 2.0, 2.1 i 2.2 na poziomach A i AA, w dwóch rozmiarach ekranu (telefon 390 × 844, komputer 1280 × 900).

```bash
dotnet run --project src/Web --launch-profile http
```

```bash
cd tools/a11y && npm install && npx playwright install chromium && npm run audit
```

Audyt obejmuje 13 widoków na każdym ekranie: start, profil, miejsca, karta miejsca, plan, zgłoś na mapie, konto, panel urzędnika, a także widoki z danymi (ustawiony profil z dwoma potrzebami, lista z ocenami, ułożony plan z trasą na mapie, karta miejsca z oceną, otwarty samouczek). Do tego test klawiatury: fokus na nagłówku po wejściu na stronę, pierwszy Tab na link „Przejdź do treści”, link widoczny po fokusie i prowadzący do treści bieżącej strony.

**Wynik: 0 problemów** (26 audytów i 2 testy klawiatury).

## Co poprawiliśmy po audycie

| Problem | Kryterium | Poprawka |
|---|---|---|
| Link „Przejdź do treści” na każdej podstronie prowadził na stronę główną (`/#tresc` przy `<base href="/">`) | 2.4.1 Pomijanie bloków | link przenosi fokus do `<main>` bieżącej strony |
| Przycisk pełnego ekranu mapy: nazwa dla czytnika („Pokaż mapę na pełnym ekranie”) nie zawierała widocznego tekstu, a przy przełączaniu zmieniała się | 2.5.3 Etykieta w nazwie, 4.1.2 | stała nazwa „Pełny ekran mapy”, stan w `aria-pressed` |
| Pinezki planu: nazwa bez widocznego numeru przystanku | 2.5.3 | nazwa „1. Nazwa miejsca”, ikony z `aria-hidden` |
| Puste dymki mapy z rolą `tooltip` zostające po przestawianiu etykiet | 4.1.2 Nazwa, rola, wartość | mapa bez animacji zanikania, dymki usuwane od razu |
| Animacje mimo ustawienia systemu „ogranicz ruch” | 2.3.3 (AAA), dobra praktyka | `prefers-reduced-motion`: bez animacji, przejść i animowanego powiększania mapy |

## Co już było w aplikacji

- Status oceny zawsze jako kolor, znak i tekst (1.4.1 Użycie koloru).
- Fokus przenoszony na nagłówek strony po nawigacji, widoczny fokus (2.4.3, 2.4.7), odstęp przewijania pod przyklejonym paskiem i dolną nawigacją, żeby fokus nie był zasłonięty (2.4.11).
- Mapa ma odpowiednik w postaci listy; pinezki obsługiwane klawiaturą.
- Pola logowania z `autocomplete` (`username`, `current-password`, `new-password`), więc działają menedżery haseł (1.3.5, 3.3.8).
- Czytanie planu i karty miejsca na głos (syntezator mowy przeglądarki).

## Czego automat nie sprawdzi

Automatyczne narzędzia wykrywają część problemów. Przed wdrożeniem produkcyjnym potrzebny jest test z udziałem ludzi: czytnik ekranu (VoiceOver na iOS i macOS, TalkBack, NVDA), obsługa samą klawiaturą całych ścieżek (zgłoszenie z mapy, panel urzędnika), powiększenie do 400% i test z użytkownikami z grup docelowych.
