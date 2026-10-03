using System.Text.Json;
using System.Text.Json.Serialization;

namespace Domain;

/// <summary>Wspólny format JSON dla plików katalogu miejsc, IndexedDB i API.</summary>
public static class DomainJson
{
    public static JsonSerializerOptions Options { get; } = Create(indented: false);

    public static JsonSerializerOptions Indented { get; } = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented) => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = indented,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
}
