using System.Net.Http.Json;
using Application.Abstractions;
using Domain;
using Domain.Accounts;

namespace Infrastructure.Browser;

/// <summary>Konto mieszkańca (api/account). Ciasteczko sesji wysyła przeglądarka (to samo pochodzenie), kod go nie widzi.</summary>
internal sealed class HostAccountClient(HttpClient http) : IAccountClient
{
    public Task<Result<UserProfile>> RegisterAsync(UserCredentials credentials, CancellationToken ct) =>
        HostApi.SendAsync<UserProfile>(() => http.PostAsJsonAsync("api/account/register", credentials, DomainJson.Options, ct), ct);

    public Task<Result<UserProfile>> LoginAsync(UserCredentials credentials, CancellationToken ct) =>
        HostApi.SendAsync<UserProfile>(() => http.PostAsJsonAsync("api/account/login", credentials, DomainJson.Options, ct), ct,
            unauthorized: "Nieprawidłowy login lub hasło.");

    public async Task LogoutAsync(CancellationToken ct)
    {
        try { (await http.PostAsync("api/account/logout", null, ct)).Dispose(); }
        catch (HttpRequestException) { }
    }

    public async Task<UserProfile?> GetCurrentAsync(CancellationToken ct)
    {
        var result = await HostApi.SendAsync<UserProfile>(() => http.GetAsync("api/account/me", ct), ct);
        return result.IsSuccess ? result.Value : null;
    }
}
