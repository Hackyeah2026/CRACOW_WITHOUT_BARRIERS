using System.Text.Json;
using Application.Abstractions;
using Domain;
using Microsoft.JSInterop;

namespace Infrastructure.Browser;

/// <summary>Nakładka na IndexedDB (wwwroot/js/localStore.js). Wartości trzymamy jako JSON w formacie domenowym.</summary>
internal sealed class IndexedDbLocalStore(IJSRuntime js) : ILocalStore, IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _module =
        new(() => js.InvokeAsync<IJSObjectReference>("import", "./js/localStore.js").AsTask());

    public async Task<T?> GetAsync<T>(string store, string key)
    {
        var json = await (await _module.Value).InvokeAsync<string?>("get", store, key);
        return json is null ? default : JsonSerializer.Deserialize<T>(json, DomainJson.Options);
    }

    public async Task PutAsync<T>(string store, string key, T value) =>
        await (await _module.Value).InvokeVoidAsync("put", store, key, JsonSerializer.Serialize(value, DomainJson.Options));

    public async Task DeleteAsync(string store, string key) =>
        await (await _module.Value).InvokeVoidAsync("remove", store, key);

    public async Task<IReadOnlyList<T>> ListAsync<T>(string store)
    {
        var items = await (await _module.Value).InvokeAsync<string[]>("list", store);
        return items.Select(json => JsonSerializer.Deserialize<T>(json, DomainJson.Options)!).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        if (_module.IsValueCreated)
            await (await _module.Value).DisposeAsync();
    }
}
