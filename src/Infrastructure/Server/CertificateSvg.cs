using System.Globalization;
using System.Security;
using System.Text;
using QRCoder;

namespace Infrastructure.Server;

/// <param name="Url">Adres karty miejsca zapisany w kodzie QR.</param>
public sealed record CertificateData(string BusinessName, string PlaceName, string CertificateId, DateTime IssuedAt, string Url);

/// <summary>
/// Certyfikat konta firmowego jako SVG w formacie A4 poziomo, z kodem QR prowadzącym do karty miejsca.
/// Wektorowy, więc drukuje się ostro w każdym rozmiarze; przeglądarka zapisuje go też jako PDF.
/// </summary>
public static class CertificateSvg
{
    private const int Width = 1123;
    private const int Height = 794;
    private const int QrSize = 210;
    private const string Navy = "#0b3d91";
    private const string Gold = "#b8860b";

    /// <summary>Adres karty miejsca; miasto w adresie, bo skanujący może mieć w aplikacji wybrane inne.</summary>
    public static string PlaceUrl(string baseUrl, string cityId, string placeId) =>
        $"{baseUrl.TrimEnd('/')}/miejsca/{Uri.EscapeDataString(placeId)}?miasto={Uri.EscapeDataString(cityId)}";

    public static string Render(CertificateData data)
    {
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="297mm" height="210mm" viewBox="0 0 {Width} {Height}" role="img" aria-labelledby="t d" font-family="Georgia, 'Times New Roman', serif" text-anchor="middle">
            <title id="t">Certyfikat Kraków bez barier: {Esc(data.BusinessName)}</title>
            <desc id="d">Certyfikat numer {Esc(data.CertificateId)}. Kod QR prowadzi do informacji o dostępności miejsca: {Esc(data.Url)}</desc>
            <rect width="{Width}" height="{Height}" fill="#ffffff"/>
            <rect x="24" y="24" width="{Width - 48}" height="{Height - 48}" fill="none" stroke="{Navy}" stroke-width="6"/>
            <rect x="38" y="38" width="{Width - 76}" height="{Height - 76}" fill="none" stroke="{Gold}" stroke-width="2"/>
            <text x="{Width / 2}" y="112" font-size="24" letter-spacing="8" fill="{Navy}" font-family="Arial, Helvetica, sans-serif" font-weight="700">KRAKÓW BEZ BARIER</text>
            <text x="{Width / 2}" y="198" font-size="72" fill="#1b1b1b">Certyfikat</text>
            <text x="{Width / 2}" y="240" font-size="24" fill="#1b1b1b" font-style="italic">miejsca otwartego na potrzeby wszystkich gości</text>
            <line x1="{Width / 2 - 140}" y1="268" x2="{Width / 2 + 140}" y2="268" stroke="{Gold}" stroke-width="2"/>
            <text x="{Width / 2}" y="312" font-size="20" fill="#444444" font-family="Arial, Helvetica, sans-serif">otrzymuje</text>
            <text x="{Width / 2}" y="372" font-size="{NameSize(data.BusinessName)}" font-weight="700" fill="{Navy}">{Esc(data.BusinessName)}</text>

            """);

        if (!string.Equals(data.PlaceName.Trim(), data.BusinessName.Trim(), StringComparison.OrdinalIgnoreCase))
            svg.Append(CultureInfo.InvariantCulture, $"""
                <text x="{Width / 2}" y="412" font-size="20" fill="#444444" font-family="Arial, Helvetica, sans-serif">miejsce w aplikacji: {Esc(data.PlaceName)}</text>

                """);

        svg.Append(CultureInfo.InvariantCulture, $"""
            <g font-family="Arial, Helvetica, sans-serif" text-anchor="start" fill="#1b1b1b">
            <text x="90" y="500" font-size="20">Firma została zweryfikowana przez urząd i sama publikuje w aplikacji</text>
            <text x="90" y="528" font-size="20">informacje o udogodnieniach dla osób z niepełnosprawnościami,</text>
            <text x="90" y="556" font-size="20">seniorów i osób wrażliwych na bodźce.</text>
            <text x="90" y="622" font-size="18"><tspan font-weight="700">Numer certyfikatu:</tspan> {Esc(data.CertificateId)}</text>
            <text x="90" y="650" font-size="18"><tspan font-weight="700">Data wydania:</tspan> {LocalDate(data.IssuedAt):dd.MM.yyyy}</text>
            <text x="90" y="700" font-size="14" fill="#444444">Certyfikat jest ważny, dopóki karta miejsca w aplikacji pokazuje go pod tym numerem.</text>
            </g>
            <g transform="translate({Width - 90 - QrSize} 470)">
            {Qr(data.Url)}
            </g>
            <g font-family="Arial, Helvetica, sans-serif" fill="#1b1b1b">
            <text x="{Width - 90 - QrSize / 2}" y="706" font-size="15" font-weight="700">Zeskanuj: aktualne udogodnienia</text>
            <text x="{Width - 90 - QrSize / 2}" y="726" font-size="11" fill="#444444">{Esc(data.Url)}</text>
            </g>
            </svg>
            """);
        return svg.ToString();
    }

    /// <summary>Kod QR jako jedna ścieżka: sąsiednie ciemne pola w wierszu łączą się w prostokąt.</summary>
    private static string Qr(string url)
    {
        // Poziom M (15% nadmiaru) wystarcza na druk i zostawia pola na tyle duże, żeby telefon czytał kod z kartki.
        using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var modules = data.ModuleMatrix;
        var path = new StringBuilder();
        for (var y = 0; y < modules.Count; y++)
        {
            for (var x = 0; x < modules.Count; x++)
            {
                if (!modules[y][x])
                    continue;
                var start = x;
                while (x + 1 < modules.Count && modules[y][x + 1])
                    x++;
                path.Append(CultureInfo.InvariantCulture, $"M{start} {y}h{x - start + 1}v1h-{x - start + 1}z");
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"""
            <svg width="{QrSize}" height="{QrSize}" viewBox="0 0 {modules.Count} {modules.Count}" shape-rendering="crispEdges"><rect width="{modules.Count}" height="{modules.Count}" fill="#ffffff"/><path fill="#000000" d="{path}"/></svg>
            """);
    }

    /// <summary>Długa nazwa dostaje mniejszą czcionkę, żeby zmieścić się w ramce.</summary>
    private static int NameSize(string name) => name.Length switch { <= 28 => 52, <= 44 => 38, <= 62 => 28, _ => 22 };

    private static DateTime LocalDate(DateTime utc)
    {
        try
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return utc;
        }
    }

    private static string Esc(string text) => SecurityElement.Escape(text) ?? "";
}
