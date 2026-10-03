namespace Tools;

/// <param name="South">Zasięg importu (bbox): południe, zachód, północ, wschód.</param>
public sealed record CityImport(double South, double West, double North, double East);

public static class CityImports
{
    public static IReadOnlyDictionary<string, CityImport> All { get; } = new Dictionary<string, CityImport>
    {
        // Stare Miasto, Kazimierz, Wawel i okolice.
        ["krakow"] = new(50.043, 19.915, 50.072, 19.962)
    };
}
