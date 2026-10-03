using Domain.Accounts;

namespace Application.Abstractions;

/// <summary>Konta mieszkańców w bazie hosta.</summary>
public interface IUserDirectory
{
    /// <summary>Zakłada konto; null, gdy login jest już zajęty.</summary>
    Task<UserProfile?> RegisterAsync(UserCredentials credentials, DateTime now, CancellationToken ct);

    /// <summary>Profil, gdy login i hasło się zgadzają; w przeciwnym razie null.</summary>
    Task<UserProfile?> VerifyAsync(UserCredentials credentials, CancellationToken ct);
}

/// <summary>Konto mieszkańca w przeglądarce. Sesję trzyma ciasteczko hosta niedostępne dla skryptów.</summary>
public interface IAccountClient
{
    Task<Result<UserProfile>> RegisterAsync(UserCredentials credentials, CancellationToken ct);
    Task<Result<UserProfile>> LoginAsync(UserCredentials credentials, CancellationToken ct);
    Task LogoutAsync(CancellationToken ct);

    /// <summary>Zalogowany mieszkaniec albo null.</summary>
    Task<UserProfile?> GetCurrentAsync(CancellationToken ct);
}
