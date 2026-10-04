using System.Globalization;
using Application.Places;
using Domain.Assessments;
using Domain.Transit;
using Domain.Trips;

namespace Web.Client.Services;

/// <summary>
/// Tekst do czytania na głos: te same informacje co na ekranie, ułożone w pełne zdania. Każdy element listy
/// to osobny fragment dla syntezatora mowy. Jednostki są zapisane słowami, a daty i źródła danych pominięte.
/// </summary>
public static class Narration
{
    /// <summary>Plan trasy: podsumowanie, a potem na zmianę przystanek i odcinek do następnego.</summary>
    public static IReadOnlyList<string> Of(TripPlan plan)
    {
        var hazards = plan.Legs.SelectMany(l => l.Hazards ?? []).DistinctBy(h => h.Hazard.Id).ToList();
        var avoided = plan.Legs.SelectMany(l => l.Detour?.Avoided ?? []).DistinctBy(h => h.Id).Count();

        var summary = new List<string>
        {
            $"Plan trasy: {Labels.Stops(plan.Stops.Count)}, {About(plan.TotalDistanceM)} drogi, {AboutMinutes(plan.TotalDurationMin)} w ruchu."
        };
        if (avoided > 0)
            summary.Add($"Trasa omija {avoided} {ConfirmedHazards(avoided)} przez urząd.");
        if (hazards.Count > 0)
            summary.Add($"Przy trasie: {hazards.Count} {ConfirmedHazards(hazards.Count)} przez urząd, z tego dla Twojego profilu: {hazards.Count(h => h.ConcernsProfile)}.");

        var parts = new List<string> { string.Join(' ', summary) };
        for (var i = 0; i < plan.Stops.Count; i++)
        {
            parts.Add(Stop(plan.Stops[i], i, plan.Stops.Count));
            if (i < plan.Legs.Count)
                parts.Add(Leg(plan.Legs[i]));
        }
        return parts;
    }

    /// <summary>Karta miejsca: nazwa, ocena dla profilu i powody w tej samej kolejności co na ekranie.</summary>
    public static IReadOnlyList<string> Of(AssessedPlace item)
    {
        var (place, assessment) = (item.Place, item.Assessment);
        var parts = new List<string>
        {
            Sentence(place.Name) + " " + Sentence(place.Address is null ? Labels.Of(place.Category) : $"{Labels.Of(place.Category)}, {place.Address}")
        };
        if (place.Certificate is { } certificate)
            parts.Add($"To miejsce ma certyfikat Kraków bez barier. Prowadzi je firma {Sentence(certificate.BusinessName)}");

        if (assessment.Reasons.Count == 0)
        {
            parts.Add("Ustaw profil potrzeb, żeby usłyszeć ocenę dopasowaną do Ciebie.");
            return parts;
        }

        parts.Add($"Ocena dla Twojego profilu: {Labels.Of(assessment.Status).ToLowerInvariant()}.");
        AddReasons(parts, "Bariery", assessment.Barriers);
        AddReasons(parts, "Udogodnienia", assessment.Amenities);
        AddReasons(parts, "Czego nie wiemy", assessment.Missing);
        return parts;
    }

    private static void AddReasons(List<string> parts, string title, IEnumerable<AssessmentReason> reasons)
    {
        var messages = reasons.Select(r => Sentence(r.Message)).ToList();
        if (messages.Count > 0)
            parts.Add($"{title}. {string.Join(' ', messages)}");
    }

    private static string Stop(TripStop stop, int index, int count)
    {
        var role = index == 0 ? ", start" : index == count - 1 ? ", cel" : "";
        var text = new List<string> { $"Przystanek {stop.Order}{role}: {Sentence(stop.Place.Name)}" };
        if (stop.IsUserLocation)
            return text[0];

        text.Add($"Ocena: {Labels.Of(stop.Assessment.Status).ToLowerInvariant()}.");
        text.AddRange(stop.Assessment.Barriers.Select(b => Sentence(b.Message)));
        if (stop.Assessment.Status == AssessmentStatus.Unknown)
            text.Add("Nie mamy danych, żeby ocenić to miejsce dla Twojego profilu.");
        return string.Join(' ', text);
    }

    private static string Leg(TripLeg leg)
    {
        var text = new List<string> { $"Do następnego przystanku: {About(leg.DistanceM)} pieszo, {AboutMinutes(leg.DurationMin)}." };
        if (leg.IsEstimated)
            text.Add("Dystans jest szacowany w linii prostej, a bariery po drodze nie są sprawdzone.");
        text.AddRange(leg.Warnings.Select(Sentence));

        if (leg.Detour is { } detour)
        {
            text.Add($"Trasa omija {(detour.Avoided.Count == 1 ? "utrudnienie potwierdzone" : "utrudnienia potwierdzone")} przez urząd: {Labels.Kinds(detour.Avoided)}.");
            text.Add(detour.ExtraDistanceM >= 10 ? $"Droga jest przez to dłuższa o {About(detour.ExtraDistanceM)}." : "Droga nie jest przez to dłuższa.");
        }

        foreach (var hazard in leg.Hazards ?? [])
        {
            var where = hazard.DistanceM < 10 ? "na trasie" : $"{About(hazard.DistanceM)} od trasy";
            text.Add($"Uwaga: {Labels.Verified(hazard.Hazard).ToLowerInvariant()}, {where}.{(hazard.ConcernsProfile ? " Dotyczy Twojego profilu." : "")}");
        }

        if (leg.RestStops is { Count: > 0 } rests)
        {
            text.Add(rests.Count == 1
                ? $"Przy trasie jest ławka na przerwę, {About(rests[0].DistanceFromStartM)} od początku odcinka."
                : $"Ławki na przerwę przy trasie: {rests.Count}. Pierwsza {About(rests[0].DistanceFromStartM)} od początku odcinka.");
        }

        if (leg.Transit is { } transit)
            text.Add(Transit(transit));
        return string.Join(' ', text);
    }

    /// <summary>Pierwsze, czyli najwcześniejsze połączenie; pozostałe są na ekranie.</summary>
    private static string Transit(TransitAdvice advice)
    {
        if (advice.Status != TransitStatus.Found || advice.Journeys.Count == 0)
            return "Tego odcinka nie da się teraz przejechać komunikacją miejską.";

        var journey = advice.Journeys[0];
        var text = new List<string> { $"Możesz pojechać komunikacją miejską. Wyjdź o {Labels.Time(journey.LeaveMinute)}." };
        for (var i = 0; i < journey.Rides.Count; i++)
        {
            var ride = journey.Rides[i];
            if (i > 0)
                text.Add($"Przesiadka na przystanku {Sentence(ride.BoardStop)}");
            text.Add($"{Labels.Of(ride.Kind)} {ride.LineName}, kierunek {ride.Headsign}, odjazd o {Labels.Time(ride.DepartMinute)} z przystanku {Sentence(ride.BoardStop)}");
            text.Add($"Wysiądź na przystanku {ride.AlightStop} o {Labels.Time(ride.ArriveMinute)}.");
        }
        text.Add($"Na miejscu około {Labels.Time(journey.ArriveMinute)}, pieszo razem {About(journey.TotalWalkM)}.");
        text.Add("Nie mamy danych o dostępności pojazdów i przystanków: sprawdź ją przed podróżą.");
        return string.Join(' ', text);
    }

    private static string ConfirmedHazards(int count) =>
        Labels.Plural(count, "utrudnienie potwierdzone", "utrudnienia potwierdzone", "utrudnień potwierdzonych");

    /// <summary>Odległość słowami, w formie do zdań z "około": 120 metrów, 1 kilometra, 2,4 kilometra, 3 kilometrów.</summary>
    private static string About(double meters)
    {
        if (meters < 1000)
            return $"około {Math.Round(meters / 10) * 10:0} metrów";

        var km = Math.Round(meters / 1000, 1);
        return km == Math.Floor(km)
            ? $"około {km:0} {(km == 1 ? "kilometra" : "kilometrów")}"
            : $"około {km.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')} kilometra";
    }

    private static string AboutMinutes(double minutes)
    {
        var whole = (int)Math.Ceiling(minutes);
        return $"około {whole} {(whole == 1 ? "minuty" : "minut")}";
    }

    /// <summary>Tekst zakończony kropką, żeby syntezator zrobił pauzę przed następnym zdaniem.</summary>
    private static string Sentence(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length == 0 || trimmed[^1] is '.' or '!' or '?' ? trimmed : trimmed + ".";
    }
}
