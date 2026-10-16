using System.Globalization;

namespace Domain.Places;

/// <summary>
/// Hałas otoczenia z miejskiej mapy hałasu (wskaźnik LDWN, przedziały co 5 dB). Wartość cechy
/// <see cref="FeatureKey.AmbientNoiseDb"/> to dolna granica przedziału; 0 oznacza najniższy przedział, poniżej 55 dB.
/// Mapa opisuje hałas na zewnątrz (ruch drogowy, tramwaje, kolej), nie hałas wewnątrz budynku.
/// </summary>
public static class AmbientNoise
{
    /// <summary>Od 55 dB dyrektywa 2002/49/WE wymaga map hałasu; WHO zaleca dla ruchu drogowego poniżej 53 dB.</summary>
    public const double ModerateFromDb = 55;

    /// <summary>Od 65 dB uznajemy hałas za wysoki: to dwa przedziały mapy powyżej progu dyrektywy.</summary>
    public const double HighFromDb = 65;

    /// <summary>Poziom w skali 1-3, tej samej co <see cref="FeatureKey.NoiseLevel"/>.</summary>
    public static int Level(double bandFromDb) => bandFromDb < ModerateFromDb ? 1 : bandFromDb < HighFromDb ? 2 : 3;

    public static string Describe(double bandFromDb) => bandFromDb switch
    {
        < ModerateFromDb => "poniżej 55 dB",
        >= 80 => "80 dB i więcej",
        _ => string.Create(CultureInfo.InvariantCulture, $"{bandFromDb:0}–{bandFromDb + 5:0} dB")
    };
}
