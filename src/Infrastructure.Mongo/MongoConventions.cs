using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;

namespace Infrastructure.Mongo;

/// <summary>
/// Wspólny zapis dokumentów: pola camelCase i enumy jako tekst, czyli tak samo jak w plikach JSON katalogu.
/// Nieznane pola są pomijane, żeby starszy kod czytał dokumenty zapisane przez nowszy.
/// </summary>
public static class MongoConventions
{
    private static readonly Lock Gate = new();
    private static bool _registered;

    /// <summary>Rejestruje konwencje raz na proces; musi się wykonać przed pierwszym użyciem kolekcji.</summary>
    public static void Register()
    {
        lock (Gate)
        {
            if (_registered)
                return;

            ConventionRegistry.Register("kbb", new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                // topLevelOnly: false, żeby także listy enumów (np. udogodnienia w zgłoszeniu) były tekstem.
                new EnumRepresentationConvention(BsonType.String, topLevelOnly: false),
                new IgnoreExtraElementsConvention(true)
            }, _ => true);
            _registered = true;
        }
    }
}
