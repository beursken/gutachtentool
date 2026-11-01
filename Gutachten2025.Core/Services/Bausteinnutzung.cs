using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Gutachten2025.Services;

/// <summary>
/// Welche Bausteine in welchen Gutachten verwendet wurden (&lt;Klausur&gt;\Bausteinnutzung.json, je Kennung).
/// Nur lokal; an das KI-Portal gehen allein die Zählungen je Baustein (ohne Kennziffern).
/// </summary>
public static class Bausteinnutzung
{
    public const string Datei = "Bausteinnutzung.json";

    static readonly Regex Leerraum = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>Vergleichsschlüssel eines Bausteins (Text ohne Leerraumunterschiede).</summary>
    public static string Schluessel(string text) => Leerraum.Replace(text, " ").Trim();

    public static Dictionary<string, List<string>> Laden(string klausurOrdner)
    {
        string pfad = Path.Combine(klausurOrdner, Datei);
        if (!File.Exists(pfad)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(pfad, Encoding.UTF8), KlausurKonfigDatei.Json);
            return new(d ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Ersetzt die Nutzung einer Arbeit (erneutes Erstellen zählt nicht doppelt).</summary>
    public static void Eintragen(string klausurOrdner, string kennung, IEnumerable<Baustein> verwendet)
    {
        Dictionary<string, List<string>> d = Laden(klausurOrdner);
        d[kennung] = verwendet.Select(b => Schluessel(b.Text)).Distinct().ToList();
        string pfad = Path.Combine(klausurOrdner, Datei);
        string tmp = pfad + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(d, KlausurKonfigDatei.Json), new UTF8Encoding(false));
        File.Move(tmp, pfad, overwrite: true);
    }

    /// <summary>Anzahl der Arbeiten je Bausteinschlüssel.</summary>
    public static Dictionary<string, int> Zaehlen(Dictionary<string, List<string>> nutzung)
    {
        var z = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (List<string> liste in nutzung.Values)
            foreach (string s in liste)
                z[s] = z.TryGetValue(s, out int n) ? n + 1 : 1;
        return z;
    }
}

/// <summary>Adresse und Schlüssel für Rückmeldungen an das KI-Portal (aus dem Paket; &lt;Klausur&gt;\Rueckmeldung.json).</summary>
public sealed class PaketRueckmeldung
{
    public string Adresse { get; set; } = string.Empty;
    public string Schluessel { get; set; } = string.Empty;

    public const string Datei = "Rueckmeldung.json";

    public bool Gueltig =>
        Uri.TryCreate(Adresse, UriKind.Absolute, out Uri? u) && u.Scheme == Uri.UriSchemeHttps && Schluessel.Length is >= 16 and <= 200;

    public static PaketRueckmeldung? Laden(string klausurOrdner)
    {
        string pfad = Path.Combine(klausurOrdner, Datei);
        if (!File.Exists(pfad)) return null;
        try
        {
            PaketRueckmeldung? r = JsonSerializer.Deserialize<PaketRueckmeldung>(File.ReadAllText(pfad, Encoding.UTF8), KlausurKonfigDatei.Json);
            return r is { Gueltig: true } ? r : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Speichern(string klausurOrdner) =>
        File.WriteAllText(Path.Combine(klausurOrdner, Datei), JsonSerializer.Serialize(this, KlausurKonfigDatei.Json), new UTF8Encoding(false));

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Sendet die Zählungen (Baustein, Gliederungspfad, Anzahl der Arbeiten) an das KI-Portal. Als text/plain,
    /// damit die Browser-Fassung ohne CORS-Vorabanfrage senden kann. Rückgabe: Fehlermeldung oder null.
    /// </summary>
    public async Task<string?> SendenAsync(int arbeiten, IEnumerable<(string Pfad, string Text, int Anzahl)> bausteine, string? programmversion)
    {
        var daten = new
        {
            Schluessel,
            Arbeiten = arbeiten,
            Programmversion = programmversion,
            Bausteine = bausteine.Select(b => new { b.Pfad, b.Text, b.Anzahl }).ToList(),
        };
        try
        {
            using var inhalt = new StringContent(JsonSerializer.Serialize(daten), Encoding.UTF8, "text/plain");
            using HttpResponseMessage antwort = await Http.PostAsync(Adresse, inhalt);
            if (antwort.IsSuccessStatusCode) return null;
            string text = await antwort.Content.ReadAsStringAsync();
            return $"Das KI-Portal hat die Rückmeldung abgelehnt ({(int)antwort.StatusCode}){(text.Length is > 0 and < 300 ? ": " + text : ".")}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Das KI-Portal war nicht erreichbar: " + ex.Message;
        }
    }
}
