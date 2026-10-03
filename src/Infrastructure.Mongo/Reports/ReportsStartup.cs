using Application.Abstractions;
using Domain.Hazards;
using Domain.Reports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Infrastructure.Mongo.Reports;

/// <summary>
/// Przy starcie hosta zakłada indeksy zgłoszeń i konta urzędników z konfiguracji. Działa w tle:
/// niedostępna baza kończy się wpisem w logach, a nie zatrzymaniem aplikacji.
/// </summary>
internal sealed class ReportsStartup(MongoCollections collections, OfficialsOptions officials, ILogger<ReportsStartup> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!collections.IsConfigured)
            return;

        try
        {
            await MongoCollections.RunAsync(async () =>
            {
                await MongoReportRepository.EnsureIndexesAsync(collections.Get<Report>(MongoCollections.Reports), ct);
                await MongoHazardRepository.EnsureIndexesAsync(collections.Get<Hazard>(MongoCollections.Hazards), ct);
                await SeedOfficialsAsync(ct);
                return true;
            });
        }
        catch (DatabaseUnavailableException ex)
        {
            logger.LogWarning(ex.InnerException, "Nie udało się przygotować kolekcji zgłoszeń: {Error}", ex.Message);
        }
    }

    /// <summary>Zakłada brakujące konta; istniejącemu zmienia hasło, jeśli w konfiguracji jest inne.</summary>
    private async Task SeedOfficialsAsync(CancellationToken ct)
    {
        var accounts = collections.Get<OfficialAccount>(MongoCollections.Officials);
        foreach (var seed in officials.Seed)
        {
            if (string.IsNullOrWhiteSpace(seed.Login) || string.IsNullOrEmpty(seed.Password))
            {
                logger.LogWarning("Pominięto konto urzędnika bez loginu albo hasła.");
                continue;
            }

            var id = OfficialAccount.NormalizeLogin(seed.Login);
            var existing = await accounts.Find(a => a.Id == id).FirstOrDefaultAsync(ct);
            var displayName = string.IsNullOrWhiteSpace(seed.DisplayName) ? id : seed.DisplayName.Trim();
            var unit = seed.Unit.Trim();

            var passwordMatches = existing is not null && PasswordHashing.Verify(seed.Password, existing.PasswordHash);
            if (passwordMatches && existing!.DisplayName == displayName && existing.Unit == unit)
                continue;

            var account = new OfficialAccount(id, displayName, unit,
                passwordMatches ? existing!.PasswordHash : PasswordHashing.Hash(seed.Password),
                existing?.CreatedAt ?? DateTime.UtcNow);
            await accounts.ReplaceOneAsync(a => a.Id == id, account, new ReplaceOptions { IsUpsert = true }, ct);
            logger.LogInformation("Konto urzędnika {Login} {Action}.", id, existing is null ? "utworzone" : "zaktualizowane");
        }
    }
}
