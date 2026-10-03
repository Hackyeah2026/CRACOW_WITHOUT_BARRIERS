using System.Text.RegularExpressions;

namespace Domain.Accounts;

/// <summary>Konto mieszkańca: sam login. Profil potrzeb nie jest częścią konta i zostaje na urządzeniu.</summary>
public sealed record UserProfile(string Login);

public sealed partial record UserCredentials(string Login, string Password)
{
    public const int MinLoginLength = 3;
    public const int MaxLoginLength = 30;
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 200;

    /// <summary>Login bez rozróżniania wielkości liter i spacji na brzegach; w tej postaci jest kluczem konta.</summary>
    public static string NormalizeLogin(string login) => login.Trim().ToLowerInvariant();

    /// <summary>Błędy rejestracji do pokazania użytkownikowi; pusta lista oznacza poprawne dane.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!LoginPattern().IsMatch(NormalizeLogin(Login ?? "")))
            errors.Add($"Login musi mieć od {MinLoginLength} do {MaxLoginLength} znaków: litery bez polskich znaków, cyfry, kropka, myślnik lub podkreślenie.");
        if (Password is not { Length: >= MinPasswordLength and <= MaxPasswordLength })
            errors.Add($"Hasło musi mieć co najmniej {MinPasswordLength} znaków.");
        return errors;
    }

    [GeneratedRegex("^[a-z0-9._-]{3,30}$")]
    private static partial Regex LoginPattern();
}
