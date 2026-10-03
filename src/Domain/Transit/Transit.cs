using System.Text.Json.Serialization;
using Domain.Places;

namespace Domain.Transit;

public enum TransitKind { Tram, Bus }

public sealed record TransitStop(string Name, double Lat, double Lon)
{
    [JsonIgnore]
    public GeoPoint Location => new(Lat, Lon);
}

/// <summary>Przebieg linii: jedna kolejność przystanków wraz z kursami, które nią jeżdżą.</summary>
/// <param name="Stops">Indeksy przystanków w <see cref="TransitNetwork.Stops"/>, w kolejności jazdy.</param>
/// <param name="TimeProfiles">Warianty czasów przejazdu: minuty od startu kursu dla każdego przystanku.</param>
/// <param name="Trips">
/// Kursy zapisane płasko po trzy liczby: minuta startu (od północy dnia rozkładowego, może przekroczyć 1440),
/// indeks w <paramref name="TimeProfiles"/>, indeks w <see cref="TransitNetwork.Services"/>.
/// </param>
public sealed record TransitPattern(
    string Headsign, IReadOnlyList<int> Stops, IReadOnlyList<IReadOnlyList<int>> TimeProfiles, IReadOnlyList<int> Trips);

public sealed record TransitLine(string Name, TransitKind Kind, IReadOnlyList<TransitPattern> Patterns);

/// <summary>Dni kursowania: dni tygodnia w zakresie dat (bit 0 = poniedziałek) oraz wyjątki.</summary>
public sealed record TransitService(
    int Weekdays, DateOnly? Start, DateOnly? End, IReadOnlyList<DateOnly> Added, IReadOnlyList<DateOnly> Removed)
{
    public bool RunsOn(DateOnly date)
    {
        if (Removed.Contains(date))
            return false;
        if (Added.Contains(date))
            return true;
        var bit = 1 << (((int)date.DayOfWeek + 6) % 7);
        return (Weekdays & bit) != 0 && Start <= date && date <= End;
    }
}

/// <summary>Sieć komunikacji miejskiej z rozkładem jazdy.</summary>
/// <param name="ValidFrom">Początek ważności rozkładu według wydawcy.</param>
/// <param name="GeneratedOn">Dzień pobrania danych.</param>
public sealed record TransitNetwork(
    string Source, DateOnly ValidFrom, DateOnly ValidTo, DateOnly GeneratedOn,
    IReadOnlyList<TransitStop> Stops, IReadOnlyList<TransitLine> Lines, IReadOnlyList<TransitService> Services);

public enum TransitStatus
{
    /// <summary>Znaleziono połączenie.</summary>
    Found,
    /// <summary>W zasięgu dojścia nie ma przystanku na początku lub na końcu odcinka.</summary>
    NoStopsNearby,
    /// <summary>Są przystanki, ale nie ma połączenia z najwyżej jedną przesiadką w najbliższym czasie.</summary>
    NoConnection,
    /// <summary>Rozkład nie obejmuje tego dnia.</summary>
    NoTimetable
}

/// <param name="DepartMinute">Minuta odjazdu z przystanku (od północy).</param>
/// <param name="NextDepartures">Najbliższe odjazdy tej linii z tego przystanku w stronę przystanku docelowego.</param>
public sealed record TransitRide(
    string LineName, TransitKind Kind, string Headsign, string BoardStop, string AlightStop,
    int DepartMinute, int ArriveMinute, int StopsCount, IReadOnlyList<int> NextDepartures);

/// <param name="LeaveMinute">O której trzeba wyjść, żeby zdążyć na pierwszy odjazd.</param>
/// <param name="ArriveMinute">O której jest się u celu, z dojściem z ostatniego przystanku.</param>
public sealed record TransitJourney(
    int LeaveMinute, int ArriveMinute, double WalkToStopM, double TransferWalkM, double WalkFromStopM,
    IReadOnlyList<TransitRide> Rides)
{
    public int Transfers => Rides.Count - 1;
    public double TotalWalkM => WalkToStopM + TransferWalkM + WalkFromStopM;
}

/// <summary>Odpowiedź na pytanie "czy ten odcinek da się przejechać komunikacją".</summary>
public sealed record TransitAdvice(
    TransitStatus Status, IReadOnlyList<TransitJourney> Journeys,
    string Source, DateOnly ValidFrom, DateOnly ValidTo, DateOnly GeneratedOn, int SearchedFromMinute);
