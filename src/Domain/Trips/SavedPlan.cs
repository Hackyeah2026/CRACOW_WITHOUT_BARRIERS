namespace Domain.Trips;

/// <summary>
/// Etap pracy nad planem konta. <see cref="Closed"/> oznacza zapisaną trasę: takiego planu nie da się już edytować,
/// można go tylko obejrzeć albo usunąć.
/// </summary>
public enum SavedPlanStatus { Draft, Closed }

/// <summary>Nazwa i miejsca planu wysyłane z przeglądarki. Właściciela ustala host na podstawie sesji.</summary>
public sealed record SavedPlanDraft(string CityId, string Name, IReadOnlyList<string> PlaceIds)
{
    public const int MaxNameLength = 60;
    public const int MaxPlaces = 30;
    public const int MaxPlaceIdLength = 100;

    /// <summary>Błędy do pokazania użytkownikowi; pusta lista oznacza poprawny plan.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(CityId))
            errors.Add("Plan musi dotyczyć miasta z aplikacji.");
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Podaj nazwę planu.");
        else if (Name.Trim().Length > MaxNameLength)
            errors.Add($"Nazwa planu może mieć najwyżej {MaxNameLength} znaków.");
        if (PlaceIds is { Count: > MaxPlaces })
            errors.Add($"Plan może mieć najwyżej {MaxPlaces} miejsc.");
        // Brak listy w żądaniu spoza aplikacji to błąd danych, a nie awaria hosta.
        if (PlaceIds is null || PlaceIds.Distinct().Count() != PlaceIds.Count
            || PlaceIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > MaxPlaceIdLength))
            errors.Add("Nieprawidłowa lista miejsc.");
        return errors;
    }
}

/// <summary>Wyznaczona trasa (<see cref="TripPlan"/> jako JSON), której zapis zamyka plan.</summary>
public sealed record SavedPlanRoute(string RouteJson)
{
    public const int MaxRouteLength = 2_000_000;
}

/// <summary>Plan zapisany w bazie dla konta mieszkańca.</summary>
/// <param name="Owner">Login konta; plan widzi tylko jego właściciel.</param>
public sealed record SavedPlan(
    string Id, string Owner, string CityId, string Name, IReadOnlyList<string> PlaceIds,
    SavedPlanStatus Status, DateTime CreatedAt, DateTime UpdatedAt)
{
    /// <summary>Ile planów może mieć jedno konto.</summary>
    public const int MaxPerAccount = 50;

    /// <summary>Zapisana trasa zamkniętego planu; listy planów jej nie pobierają.</summary>
    public string? RouteJson { get; init; }

    public bool IsClosed => Status == SavedPlanStatus.Closed;

    public static SavedPlan Create(SavedPlanDraft draft, string owner, DateTime now) => new(
        Guid.NewGuid().ToString("N"), owner, draft.CityId, draft.Name.Trim(), draft.PlaceIds, SavedPlanStatus.Draft, now, now);
}
