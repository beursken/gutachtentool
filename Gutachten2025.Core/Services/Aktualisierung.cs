using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Gutachten2025.Services;

/// <summary>
/// Prüft, ob auf der Download-Seite eine neuere Version liegt (gutachtentool.json, von Werkzeuge\Verteilen.ps1 erzeugt).
/// Es wird nur die Versionsdatei abgerufen; es werden keine Daten übertragen.
/// </summary>
public static class Aktualisierung
{
    public const string Versionsdatei = "https://vote.jura.uni-passau.de/Content/gutachtentool.json";
    public const string Downloadseite = "https://vote.jura.uni-passau.de/Download";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Neuere Version für diese Plattform oder null (auch bei Netzfehlern).</summary>
    public static async Task<Version?> NeuereVersionAsync(Version aktuell)
    {
        string schluessel = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "browser";
        if (schluessel == "browser") return null; // die Browser-Fassung lädt stets die aktuelle Version
        try
        {
            using JsonDocument doc = JsonDocument.Parse(await Http.GetStringAsync(Versionsdatei));
            if (!doc.RootElement.TryGetProperty(schluessel, out JsonElement e) || e.ValueKind != JsonValueKind.String) return null;
            return Vergleichen(aktuell, e.GetString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Neuere Version oder null; vergleicht nur Haupt-, Neben- und Patch-Nummer.</summary>
    public static Version? Vergleichen(Version aktuell, string? angeboten)
    {
        if (!Version.TryParse((angeboten ?? string.Empty).Trim().TrimStart('v', 'V'), out Version? v)) return null;
        static Version Drei(Version x) => new(x.Major, Math.Max(0, x.Minor), Math.Max(0, x.Build));
        return Drei(v) > Drei(aktuell) ? Drei(v) : null;
    }
}
