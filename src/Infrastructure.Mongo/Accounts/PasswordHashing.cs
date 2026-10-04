using System.Security.Cryptography;

namespace Infrastructure.Mongo.Accounts;

/// <summary>Hasła urzędników i mieszkańców jako PBKDF2-SHA256 z losową solą; zapis "pbkdf2-sha256$iteracje$sól$skrót" (Base64).</summary>
public static class PasswordHashing
{
    private const string Scheme = "pbkdf2-sha256";
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    // Porównanie z hasłem do nieistniejącego konta trwa tyle samo, więc czas odpowiedzi nie zdradza, które loginy istnieją.
    private static readonly string Dummy = Hash(Guid.NewGuid().ToString());

    /// <summary>Sprawdza hasło konta odczytanego z bazy; gdy konta nie ma (null), liczy skrót tak samo długo i zwraca false.</summary>
    public static bool VerifyAccount(string password, string? storedHash) => Verify(password, storedHash ?? Dummy) && storedHash is not null;

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
