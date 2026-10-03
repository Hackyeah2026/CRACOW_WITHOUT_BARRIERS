using Domain.Hazards;
using Domain.Places;
using Domain.Reports;

namespace Application.Reports;

public sealed record Count<T>(T Key, int Value);

public sealed record DayCount(DateOnly Day, int Reports, int Hazards);

/// <param name="ClosedShare">Jaka część zgłoszeń miejsc jest już zamknięta (0-1); null, gdy zgłoszeń nie ma.</param>
/// <param name="MedianHoursToDecision">Mediana czasu od zgłoszenia do ostatniej decyzji dla spraw zamkniętych; null, gdy żadna nie jest zamknięta.</param>
/// <param name="WaitingOverWeek">Zgłoszenia i punkty bez żadnej decyzji od ponad 7 dni.</param>
public sealed record OfficeAnalytics(
    IReadOnlyList<Count<ReportStatus>> ReportsByStatus,
    IReadOnlyList<Count<ReportKind>> ReportsByKind,
    IReadOnlyList<Count<PlaceCategory>> ReportsByCategory,
    IReadOnlyList<Count<FeatureKey>> MissingFeatures,
    IReadOnlyList<Count<HazardStatus>> HazardsByStatus,
    IReadOnlyList<Count<HazardKind>> HazardsByKind,
    IReadOnlyList<DayCount> Daily,
    double? ClosedShare, double? MedianHoursToDecision, int WaitingOverWeek);

/// <summary>Analizy dla urzędu ze wszystkich zgłoszeń, także zamkniętych: co, gdzie i jak szybko jest obsługiwane.</summary>
public static class ReportAnalytics
{
    public const int DailyWindow = 14;

    /// <param name="today">Dzień w czasie lokalnym urzędnika; <paramref name="toLocal"/> przelicza na niego daty z bazy (UTC).</param>
    public static OfficeAnalytics Analyze(
        IReadOnlyList<Report> reports, IReadOnlyList<Hazard> hazards, DateTime nowUtc, DateOnly today, Func<DateTime, DateTime> toLocal)
    {
        var closed = reports.Where(r => !ReportStatistics.IsOpen(r.Status)).ToList();
        var decided = closed.Select(r => (r.UpdatedAt - r.CreatedAt).TotalHours)
            .Concat(hazards.Where(h => h.Status != HazardStatus.Pending).Select(h => (h.UpdatedAt - h.CreatedAt).TotalHours))
            .Order().ToList();

        var weekAgo = nowUtc.AddDays(-7);
        var waiting = reports.Count(r => r.Status == ReportStatus.New && r.CreatedAt < weekAgo)
                      + hazards.Count(h => h.Status == HazardStatus.Pending && h.CreatedAt < weekAgo);

        var reportDays = reports.GroupBy(r => DateOnly.FromDateTime(toLocal(r.CreatedAt))).ToDictionary(g => g.Key, g => g.Count());
        var hazardDays = hazards.GroupBy(h => DateOnly.FromDateTime(toLocal(h.CreatedAt))).ToDictionary(g => g.Key, g => g.Count());
        var daily = Enumerable.Range(0, DailyWindow)
            .Select(i => today.AddDays(i - DailyWindow + 1))
            .Select(day => new DayCount(day, reportDays.GetValueOrDefault(day), hazardDays.GetValueOrDefault(day)))
            .ToList();

        return new OfficeAnalytics(
            Counts(reports.Select(r => r.Status), all: true),
            Counts(reports.Select(r => r.Kind), all: true),
            Counts(reports.Select(r => r.Category)),
            Counts(reports.Where(r => r.Kind == ReportKind.MissingAmenity).SelectMany(r => r.Features)),
            Counts(hazards.Select(h => h.Status), all: true),
            Counts(hazards.Select(h => h.Kind)),
            daily,
            reports.Count == 0 ? null : (double)closed.Count / reports.Count,
            decided.Count == 0 ? null : Median(decided),
            waiting);
    }

    /// <summary>Liczności malejąco; z <paramref name="all"/> także wartości bez wystąpień, w kolejności enuma.</summary>
    private static List<Count<T>> Counts<T>(IEnumerable<T> values, bool all = false) where T : struct, Enum
    {
        var counts = values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        return all
            ? Enum.GetValues<T>().Select(v => new Count<T>(v, counts.GetValueOrDefault(v))).ToList()
            : counts.Select(c => new Count<T>(c.Key, c.Value)).OrderByDescending(c => c.Value).ThenBy(c => c.Key).ToList();
    }

    private static double Median(List<double> sorted) => sorted.Count % 2 == 1
        ? sorted[sorted.Count / 2]
        : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
}
