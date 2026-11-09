using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gutachten2025.Services;

/// <summary>Note des Erstgutachtens zu einer Kennziffer.</summary>
public sealed class Erstnote
{
    public string Kennung { get; set; } = string.Empty;
    public int Punkte { get; set; }
}

/// <summary>
/// Erstnoten für das Zweitgutachten (&lt;Klausur&gt;\Erstnoten.json, z. B. aus einem Paket des KI-Portals):
/// nur Kennziffern und Punkte, keine Namen. Das Gutachtentool gibt die Datei beim Paketexport nicht weiter.
/// </summary>
public static class Erstnoten
{
    public const string Datei = "Erstnoten.json";

    /// <summary>Ab dieser Abweichung (Punkte) warnt das Programm vor dem Erstellen des Zweitgutachtens.</summary>
    public const int Warnschwelle = 4;

    /// <summary>Kennziffer: Buchstaben/Ziffern (mit . _ / -), mindestens eine Ziffer, kein Leerzeichen – Namen werden so abgewiesen.</summary>
    static readonly Regex Kennziffer = new(@"^(?=[^\d]*\d)[A-Za-z0-9][A-Za-z0-9._/\-]{0,23}$", RegexOptions.CultureInvariant);

    public static bool IstKennziffer(string s) => Kennziffer.IsMatch(s.Trim());

    public static Dictionary<string, int> Laden(string klausurOrdner)
    {
        string pfad = Path.Combine(klausurOrdner, Datei);
        if (!File.Exists(pfad)) return new(StringComparer.OrdinalIgnoreCase);
        return AusBytes(File.ReadAllBytes(pfad)).ToDictionary(e => e.Kennung, e => e.Punkte, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Liest und prüft Erstnoten.json; <see cref="InvalidDataException"/> bei Namen, ungültigen Punkten oder Widersprüchen.</summary>
    public static List<Erstnote> AusBytes(byte[] daten)
    {
        List<Erstnote>? liste;
        try
        {
            liste = JsonSerializer.Deserialize<List<Erstnote>>(KlausurKonfigDatei.OhneBom(daten), KlausurKonfigDatei.Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Erstnoten.json ist ungültig: " + ex.Message, ex);
        }
        if (liste is null || liste.Count == 0) throw new InvalidDataException("Erstnoten.json enthält keine Einträge.");
        var gesehen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Erstnote e in liste)
        {
            e.Kennung = (e.Kennung ?? string.Empty).Trim();
            if (!IstKennziffer(e.Kennung))
                throw new InvalidDataException($"Erstnoten.json: „{Kurz(e.Kennung)}“ ist keine Kennziffer (Namen werden nicht übernommen).");
            if (!Notenskala.IstGueltig(e.Punkte))
                throw new InvalidDataException($"Erstnoten.json: Punkte für „{e.Kennung}“ außerhalb von 0 bis 18.");
            if (gesehen.TryGetValue(e.Kennung, out int alt) && alt != e.Punkte)
                throw new InvalidDataException($"Erstnoten.json: „{e.Kennung}“ ist mit verschiedenen Noten aufgeführt.");
            gesehen[e.Kennung] = e.Punkte;
        }
        return liste;
    }

    /// <summary>Ergänzt die Teilnehmendenliste um die Kennziffern der Erstnoten (vorhandene Einträge bleiben unverändert).</summary>
    public static int TeilnehmendeErgaenzen(string klausurOrdner, IEnumerable<Erstnote> erstnoten)
    {
        List<Teilnehmer> liste = Teilnehmerliste.Laden(klausurOrdner);
        var vorhanden = new HashSet<string>(liste.Select(t => t.Kennung), StringComparer.OrdinalIgnoreCase);
        int neu = 0;
        foreach (Erstnote e in erstnoten)
        {
            if (!vorhanden.Add(e.Kennung)) continue;
            liste.Add(new Teilnehmer { Kennung = e.Kennung });
            neu++;
        }
        if (neu > 0) Teilnehmerliste.Speichern(klausurOrdner, liste);
        return neu;
    }

    /// <summary>
    /// Schreibt Erstnoten.json (z. B. aus einer importierten Teilnehmendenliste mit Noten); nur Kennziffern bzw.
    /// Matrikelnummern, Namen werden übergangen. Liefert die Zahl der gespeicherten Einträge.
    /// </summary>
    public static int Speichern(string klausurOrdner, IReadOnlyDictionary<string, int> noten)
    {
        List<Erstnote> liste = noten
            .Where(n => IstKennziffer(n.Key) && Notenskala.IstGueltig(n.Value))
            .Select(n => new Erstnote { Kennung = n.Key.Trim(), Punkte = n.Value })
            .OrderBy(e => e.Kennung, KlausurAblage.NatuerlicheReihenfolge)
            .ToList();
        string pfad = Path.Combine(klausurOrdner, Datei);
        if (liste.Count == 0)
        {
            if (File.Exists(pfad)) File.Delete(pfad);
            return 0;
        }
        string tmp = pfad + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(liste, KlausurKonfigDatei.Json), new UTF8Encoding(false));
        File.Move(tmp, pfad, overwrite: true);
        return liste.Count;
    }

    static string Kurz(string s) => s.Length <= 30 ? s : s[..29] + "…";
}
