namespace Infrastructure.Mongo;

/// <summary>
/// Ustawienia połączenia z MongoDB (sekcja "Mongo"). Adres połączenia zawiera hasło, więc trzymamy go
/// w user-secrets albo w zmiennej środowiskowej Mongo__ConnectionString, nigdy w repozytorium.
/// </summary>
public sealed class MongoOptions
{
    public const string Section = "Mongo";

    public string? ConnectionString { get; set; }
    public string Database { get; set; } = "krakow-bez-barier";

    /// <summary>Jak długo czekamy na serwer, zanim uznamy bazę za niedostępną.</summary>
    public int ServerSelectionTimeoutSeconds { get; set; } = 5;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}
