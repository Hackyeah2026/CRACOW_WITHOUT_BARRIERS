using Application;
using Application.Abstractions;
using Application.Profile;
using Domain.Needs;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class ProfileTests
{
    private static readonly NeedsProfile Wheelchair = NeedsProfilePresets.Build([NeedsProfilePresets.ElectricWheelchair]);
    private static readonly NeedsProfile Senior = NeedsProfilePresets.Build([NeedsProfilePresets.Senior]);

    private readonly ISender _sender = new ServiceCollection().AddApplication()
        .AddSingleton<ILocalStore, MemoryStore>().BuildServiceProvider().GetRequiredService<ISender>();

    private async Task<NeedsProfile> GetAsync(string? login = null) => (await _sender.Send(new GetProfileQuery(login))).Value;

    [Fact]
    public async Task Without_an_account_the_profile_is_the_temporary_one()
    {
        Assert.True((await GetAsync()).IsEmpty);

        await _sender.Send(new SaveProfileCommand(Wheelchair));

        Assert.Equal(Wheelchair.Presets, (await GetAsync()).Presets);
    }

    [Fact]
    public async Task New_account_takes_over_the_temporary_profile_and_then_keeps_its_own()
    {
        await _sender.Send(new SaveProfileCommand(Wheelchair));

        Assert.Equal(Wheelchair.Presets, (await GetAsync("ania")).Presets);

        // Późniejsza zmiana konfiguracji tymczasowej nie rusza profilu konta.
        await _sender.Send(new SaveProfileCommand(Senior));
        Assert.Equal(Wheelchair.Presets, (await GetAsync("ania")).Presets);
    }

    [Fact]
    public async Task Account_profiles_are_separate_from_each_other_and_from_the_temporary_one()
    {
        await _sender.Send(new SaveProfileCommand(Wheelchair, "ania"));
        await _sender.Send(new SaveProfileCommand(Senior, "zofia"));

        Assert.Equal(Wheelchair.Presets, (await GetAsync("ania")).Presets);
        Assert.Equal(Senior.Presets, (await GetAsync("zofia")).Presets);
        Assert.True((await GetAsync()).IsEmpty);
    }

    [Fact]
    public async Task Cleared_account_profile_stays_empty_instead_of_taking_the_temporary_one_again()
    {
        await _sender.Send(new SaveProfileCommand(Wheelchair));
        await _sender.Send(new SaveProfileCommand(NeedsProfile.Empty, "ania"));

        Assert.True((await GetAsync("ania")).IsEmpty);
    }

    private sealed class MemoryStore : ILocalStore
    {
        private readonly Dictionary<(string, string), object> _items = [];

        public Task<T?> GetAsync<T>(string store, string key) => Task.FromResult(_items.TryGetValue((store, key), out var v) ? (T?)v : default);

        public Task PutAsync<T>(string store, string key, T value)
        {
            _items[(store, key)] = value!;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string store, string key)
        {
            _items.Remove((store, key));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<T>> ListAsync<T>(string store) =>
            Task.FromResult<IReadOnlyList<T>>(_items.Where(i => i.Key.Item1 == store).Select(i => (T)i.Value).ToList());
    }
}
