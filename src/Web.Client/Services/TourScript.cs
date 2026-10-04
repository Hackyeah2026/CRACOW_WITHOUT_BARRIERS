namespace Web.Client.Services;

/// <summary>Krok samouczka: element strony i dymek z opisem. Krok opcjonalny znika, gdy elementu nie ma na stronie.</summary>
public sealed record TourStep(string Selector, string Title, string Description, bool Optional = false);

/// <summary>Kroki pokazywane na jednej stronie; <see cref="Path"/> null zostawia użytkownika na bieżącej stronie.</summary>
public sealed record TourSegment(string? Path, IReadOnlyList<TourStep> Steps);

/// <summary>Scenariusz samouczka: profil potrzeb, plan i zgłoszenia, osobno dla gościa i dla zalogowanego.</summary>
public static class TourScript
{
    public static IReadOnlyList<TourSegment> For(bool loggedIn) =>
    [
        new("",
        [
            loggedIn
                ? new(Anchor("home-dashboard"), "Twój pulpit",
                    "Tutaj widzisz swój profil potrzeb, plan i wysłane zgłoszenia. Samouczek pokaże po kolei, jak z nich korzystać. Niczego nie zapisuje ani nie wysyła.")
                : new(Anchor("home-intro"), "Kraków bez barier",
                    "Aplikacja ocenia miejsca i trasy pod Twoje potrzeby. Samouczek pokaże po kolei profil potrzeb, plan i zgłoszenia. Niczego nie zapisuje ani nie wysyła."),
            new(Anchor("nav"), "Nawigacja",
                "Stąd przejdziesz do każdej części aplikacji. Samouczek zamkniesz w każdej chwili klawiszem Esc albo przyciskiem ×."),
        ]),
        new(loggedIn ? "konto/profil" : "profil",
        [
            new(Anchor("profile-presets"), "Profil potrzeb: gotowe profile",
                "Zaznacz profile, które do Ciebie pasują. Możesz wybrać kilka naraz. Pytamy o potrzeby, a nie o diagnozę."),
            new(Anchor("profile-params"), "Dostosuj parametry",
                "Każdy parametr zmienisz osobno: schody, bruk, hałas, tłum albo najdłuższy odcinek pieszo."),
            new(Anchor("profile-save"), "Zapisz profil",
                loggedIn
                    ? "Profil jest przypisany do Twojego konta i zostaje na tym urządzeniu. Według niego oceniamy miejsca i układamy trasy."
                    : "Bez konta to konfiguracja tymczasowa: działa tylko w tej przeglądarce. Po założeniu konta ustawienia przejdą na konto."),
        ]),
        new("miejsca",
        [
            new(Anchor("place-card"), "Miejsca ocenione pod Twój profil",
                "Przy każdym miejscu jest ocena dostępności i jej powód. Brak danych nigdy nie udaje dostępności.", Optional: true),
            new(Anchor("place-add"), "Dodaj miejsce do planu",
                loggedIn
                    ? "Tym przyciskiem dodajesz miejsce do planu: wybierasz, do którego, albo tworzysz nowy i go nazywasz."
                    : "Tym przyciskiem dodajesz miejsce do planu. Drugie kliknięcie je usuwa.", Optional: true),
            new(Anchor("nav-plan"), "Plan",
                "Liczba przy ikonie pokazuje, ile miejsc masz w planie. Teraz przejdziemy do planu."),
        ]),
        new("plan",
        [
            new(Anchor("plan-intro"), "Twój plan",
                "Tutaj trafiają wybrane miejsca. Gdy plan jest pusty, najpierw dodaj miejsca z listy Miejsca."),
            new(Anchor("plan-list"), "Kolejność miejsc",
                "Pierwsze miejsce jest punktem startu. Strzałkami zmienisz kolejność albo zostawisz ją do ułożenia automatycznie.", Optional: true),
            new(Anchor("plan-actions"), "Ułóż plan",
                "„Ułóż plan” wyznacza trasę po ulicach dobraną do profilu, z komunikacją miejską i ostrzeżeniami o utrudnieniach. Startem może być też Twoja lokalizacja albo punkt z mapy.", Optional: true),
        ]),
        new("zglos",
        [
            new(Anchor("report-target"), "Co chcesz zgłosić",
                "Zgłosisz utrudnienie w terenie, na przykład przeszkodę, hałas albo tłum, albo brak udogodnienia w miejscu z katalogu."),
            new("#tresc .map-frame", "Wskaż punkt na mapie",
                "Kliknij miejsce z utrudnieniem albo użyj swojej lokalizacji. Pinezkę przesuniesz też strzałkami na klawiaturze."),
            new(Anchor("report-form"), loggedIn ? "Opisz utrudnienie" : "Do wysłania potrzebne jest konto",
                loggedIn
                    ? "Wybierz rodzaj utrudnienia, dodaj zdjęcie i opis, a potem wyślij. Po potwierdzeniu przez urzędnika punkt pojawi się na mapie i w planach innych osób."
                    : "Mapę i potwierdzone utrudnienia zobaczysz bez konta. Żeby wysłać zgłoszenie, zaloguj się albo załóż konto: wystarczy login i hasło."),
        ]),
        new(null,
        [
            new(Anchor("nav-account"), loggedIn ? "Twoje zgłoszenia" : "Załóż konto",
                loggedIn
                    ? "W zakładce Konto sprawdzisz decyzje i odpowiedzi urzędu. To koniec samouczka. Uruchomisz go ponownie przyciskiem Samouczek u góry strony."
                    : "W zakładce Konto założysz konto albo się zalogujesz. Z kontem wysyłasz zgłoszenia i śledzisz odpowiedzi urzędu. To koniec samouczka. Uruchomisz go ponownie przyciskiem Samouczek u góry strony."),
        ]),
    ];

    private static string Anchor(string name) => $"[data-tour=\"{name}\"]";
}
