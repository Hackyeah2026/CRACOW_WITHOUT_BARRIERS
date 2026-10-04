namespace Infrastructure.Mongo.Accounts;

/// <summary>
/// Konta urzędników zakładane przy starcie hosta (sekcja "Officials"). Hasła trzymamy w user-secrets
/// albo w zmiennych środowiskowych (Officials__Seed__0__Password), nigdy w repozytorium.
/// </summary>
public sealed class OfficialsOptions
{
    public const string Section = "Officials";

    public List<OfficialSeed> Seed { get; set; } = [];
}

public sealed class OfficialSeed
{
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Unit { get; set; } = "";
}
