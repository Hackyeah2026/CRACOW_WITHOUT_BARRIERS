using Application.Places;
using Domain.Assessments;
using Domain.Hazards;
using Domain.Places;
using Domain.Transit;
using Domain.Trips;
using Web.Client.Services;

namespace Tests;

public class NarrationTests
{
    private static readonly GeoPoint[] Line = [new(50.0600, 19.9400), new(50.0663, 19.9400)];

    private static Place Place(string name) => new(name, "krakow", name, PlaceCategory.Museum, 50.06, 19.94, "Rynek Główny 1", null, []);

    private static TripStop Stop(int order, string name, Assessment assessment, bool isUserLocation = false) =>
        new(order, Place(name), assessment, isUserLocation);

    private static readonly Assessment Unknown = new(AssessmentStatus.Unknown, []);

    private static readonly Assessment Inaccessible = new(AssessmentStatus.Inaccessible,
        [new AssessmentReason(FeatureKey.Stairs, ReasonKind.Barrier, AssessmentStatus.Inaccessible, "Do wejścia prowadzą schody")]);

    private static TripLeg Leg(double distanceM, double minutes) => new("a", "b", distanceM, minutes, Line, false, [], null, [], []);

    [Fact]
    public void Plan_is_read_as_summary_then_stops_and_legs_in_walking_order()
    {
        var plan = new TripPlan("p", AppMode.Sightseeing, DateTimeOffset.UnixEpoch,
            [Stop(1, "Punkt startu", Unknown, isUserLocation: true), Stop(2, "Sukiennice", Inaccessible), Stop(3, "Wawel", Unknown)],
            [Leg(846, 11.2), Leg(2440, 1)]);

        var parts = Narration.Of(plan);

        Assert.Equal(6, parts.Count);
        Assert.Equal("Plan trasy: 3 przystanki, około 3,3 kilometra drogi, około 13 minut w ruchu.", parts[0]);
        // Punkt startu spoza katalogu nie ma oceny.
        Assert.Equal("Przystanek 1, start: Punkt startu.", parts[1]);
        Assert.Equal("Do następnego przystanku: około 850 metrów pieszo, około 12 minut.", parts[2]);
        Assert.Equal("Przystanek 2: Sukiennice. Ocena: niedostępne. Do wejścia prowadzą schody.", parts[3]);
        Assert.Equal("Do następnego przystanku: około 2,4 kilometra pieszo, około 1 minuty.", parts[4]);
        Assert.Equal("Przystanek 3, cel: Wawel. Ocena: brak danych. Nie mamy danych, żeby ocenić to miejsce dla Twojego profilu.", parts[5]);
    }

    [Fact]
    public void Leg_says_what_the_route_avoids_what_is_near_it_and_where_to_rest()
    {
        var stairs = new VerifiedHazard("h1", HazardKind.Stairs, 50.063, 19.94, "", new DateOnly(2026, 10, 3));
        var noise = new VerifiedHazard("h2", HazardKind.Noise, 50.064, 19.9403, "", new DateOnly(2026, 10, 3));
        var leg = Leg(1000, 15) with
        {
            Warnings = ["Odcinek dłuższy niż 500 m. Po drodze są ławki, na których możesz odpocząć"],
            Detour = new RouteDetour([stairs], 124, Line),
            Hazards = [new HazardOnRoute(noise, 22, ConcernsProfile: true)],
            RestStops = [new RestStop("bench", 50.062, 19.94, 304), new RestStop("bench2", 50.065, 19.94, 700)]
        };
        var plan = new TripPlan("p", AppMode.Sightseeing, DateTimeOffset.UnixEpoch, [Stop(1, "A", Unknown), Stop(2, "B", Unknown)], [leg]);

        var parts = Narration.Of(plan);

        Assert.Contains("Trasa omija 1 utrudnienie potwierdzone przez urząd.", parts[0]);
        Assert.Contains("Przy trasie: 1 utrudnienie potwierdzone przez urząd, z tego dla Twojego profilu: 1.", parts[0]);
        var read = parts[2];
        Assert.StartsWith("Do następnego przystanku: około 1 kilometra pieszo, około 15 minut.", read);
        Assert.Contains("możesz odpocząć. Trasa omija utrudnienie potwierdzone przez urząd: schody lub stopnie.", read);
        Assert.Contains("Droga jest przez to dłuższa o około 120 metrów.", read);
        Assert.Contains("hałas, około 20 metrów od trasy. Dotyczy Twojego profilu.", read);
        Assert.EndsWith("Ławki na przerwę przy trasie: 2. Pierwsza około 300 metrów od początku odcinka.", read);
    }

    [Fact]
    public void Leg_reads_the_first_connection_or_says_there_is_none()
    {
        var ride = new TransitRide("8", TransitKind.Tram, "Bronowice Małe", "Wawel", "Muzeum Narodowe", 9 * 60 + 13, 9 * 60 + 21, 4, [], []);
        var found = new TransitAdvice(TransitStatus.Found, [new TransitJourney(9 * 60 + 5, 9 * 60 + 25, 200, 0, 150, [ride])],
            "ZTP", new DateOnly(2026, 10, 2), new DateOnly(2027, 1, 31), new DateOnly(2026, 10, 3), 9 * 60);
        var none = found with { Status = TransitStatus.NoStopsNearby, Journeys = [] };
        TripPlan PlanWith(TransitAdvice advice) => new("p", AppMode.Sightseeing, DateTimeOffset.UnixEpoch,
            [Stop(1, "A", Unknown), Stop(2, "B", Unknown)], [Leg(1500, 20) with { Transit = advice }]);

        var withRide = Narration.Of(PlanWith(found))[2];

        Assert.Contains("Wyjdź o 09:05. Tramwaj 8, kierunek Bronowice Małe, odjazd o 09:13 z przystanku Wawel.", withRide);
        Assert.Contains("Wysiądź na przystanku Muzeum Narodowe o 09:21. Na miejscu około 09:25, pieszo razem około 350 metrów.", withRide);
        Assert.EndsWith("Tego odcinka nie da się teraz przejechać komunikacją miejską.", Narration.Of(PlanWith(none))[2]);
    }

    [Fact]
    public void Place_is_read_with_assessment_and_reasons_grouped_like_on_the_card()
    {
        var assessment = new Assessment(AssessmentStatus.Limited,
        [
            new AssessmentReason(FeatureKey.Benches, ReasonKind.Barrier, AssessmentStatus.Limited, "Brak ławek."),
            new AssessmentReason(FeatureKey.StepFreeEntrance, ReasonKind.Amenity, AssessmentStatus.Accessible, "Wejście bez stopni"),
            new AssessmentReason(FeatureKey.AccessibleToilet, ReasonKind.Missing, AssessmentStatus.Unknown, "Nie wiemy, czy jest toaleta dostosowana")
        ]);
        var place = Place("Sukiennice") with { Certificate = new PlaceCertificate("Kawiarnia Pod Arkadami", "KBB-2026-1", DateTime.UnixEpoch) };

        var parts = Narration.Of(new AssessedPlace(place, assessment, 1));

        Assert.Equal(
        [
            "Sukiennice. Muzeum, Rynek Główny 1.",
            "To miejsce ma certyfikat Kraków bez barier. Prowadzi je firma Kawiarnia Pod Arkadami.",
            "Ocena dla Twojego profilu: z ograniczeniami.",
            "Bariery. Brak ławek.",
            "Udogodnienia. Wejście bez stopni.",
            "Czego nie wiemy. Nie wiemy, czy jest toaleta dostosowana."
        ], parts);
    }

    [Fact]
    public void Place_without_a_profile_asks_to_set_one()
    {
        var parts = Narration.Of(new AssessedPlace(Place("Wawel"), Unknown, 0));

        Assert.Equal(2, parts.Count);
        Assert.Contains("Ustaw profil potrzeb", parts[1]);
    }
}
