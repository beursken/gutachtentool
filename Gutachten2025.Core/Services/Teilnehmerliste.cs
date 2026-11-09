using Gutachten2025.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gutachten2025.Services;

/// <summary>
/// Teilnehmendenliste einer Klausur (&lt;Klausur&gt;\Teilnehmende.json). Import aus .xlsx, .csv oder .txt:
/// Spalten werden über Überschriften erkannt (Kennziffer/Matrikel/Kennung, Name bzw. Vorname/Nachname);
/// ohne Überschrift gilt Spalte 1 = Kennung, Spalte 2 ff. = Name.
/// </summary>
public static class Teilnehmerliste
{
    public const string Datei = "Teilnehmende.json";

    public static List<Teilnehmer> Laden(string klausurOrdner)
    {
        string pfad = Path.Combine(klausurOrdner, Datei);
        if (!File.Exists(pfad)) return new();
        return JsonSerializer.Deserialize<List<Teilnehmer>>(File.ReadAllText(pfad, Encoding.UTF8), KlausurKonfigDatei.Json) ?? new();
    }

    public static void Speichern(string klausurOrdner, IEnumerable<Teilnehmer> liste)
    {
        string pfad = Path.Combine(klausurOrdner, Datei);
        string tmp = pfad + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(liste.ToList(), KlausurKonfigDatei.Json), new UTF8Encoding(false));
        File.Move(tmp, pfad, overwrite: true);
    }

    public static List<Teilnehmer> Importieren(string quelle) => ImportierenMitNoten(quelle).Teilnehmende;

    /// <summary>
    /// Import samt Noten des Erstgutachtens, falls die Datei eine Notenspalte hat (Überschrift „Note“, „Punkte“,
    /// „Erstnote“, „Erstgutachten“ … bzw. ohne Überschrift eine Spalte nur mit Punktwerten 0–18).
    /// </summary>
    public static Teilnehmerimport ImportierenMitNoten(string quelle)
    {
        string endung = Path.GetExtension(quelle).ToLowerInvariant();
        List<string[]> zeilen = endung switch
        {
            ".xlsx" or ".xlsm" => AusExcel(quelle),
            ".csv" or ".txt" or ".tsv" => AusText(quelle),
            _ => throw new InvalidDataException("Bitte eine Excel-Datei (.xlsx) oder eine Textdatei (.csv, .txt) wählen."),
        };
        return Auswerten(zeilen);
    }

    /// <summary>„12“, „12,0“, „12 P.“, „12 Punkte“ → 12; sonst null.</summary>
    public static int? Punktwert(string s)
    {
        Match m = Regex.Match(s ?? string.Empty, @"^\s*(\d{1,2})(?:[.,]0+)?\s*(?:P\.?|Pkt\.?|Punkte?)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return m.Success && int.TryParse(m.Groups[1].Value, out int p) && Notenskala.IstGueltig(p) ? p : null;
    }

    static List<string[]> AusExcel(string pfad)
    {
        using var fs = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using SpreadsheetDocument doc = SpreadsheetDocument.Open(fs, false);
        WorkbookPart wbp = doc.WorkbookPart ?? throw new InvalidDataException("Die Excel-Datei ist beschädigt.");
        if (Xlsx.BlattAnzahl(wbp) == 0) throw new InvalidDataException("Die Excel-Datei enthält kein Tabellenblatt.");
        SheetData? sd = Xlsx.Blatt(wbp, 0).Worksheet.GetFirstChild<SheetData>();
        var zeilen = new List<string[]>();
        if (sd is null) return zeilen;
        foreach (Row r in sd.Elements<Row>())
        {
            var werte = new string[30];
            for (int i = 0; i < werte.Length; i++) werte[i] = string.Empty;
            foreach (Cell c in r.Elements<Cell>())
            {
                int spalte = Xlsx.SpaltenNummer(c.CellReference?.Value ?? string.Empty);
                if (spalte is >= 1 and <= 30) werte[spalte - 1] = Xlsx.Text(c, wbp).Trim();
            }
            int letzte = Array.FindLastIndex(werte, w => w.Length > 0);
            if (letzte >= 0) zeilen.Add(werte[..(letzte + 1)]);
        }
        return zeilen;
    }

    static List<string[]> AusText(string pfad)
    {
        string[] roh = Formulierungsdatei.TextLesen(pfad).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string probe = roh.FirstOrDefault(z => z.Trim().Length > 0) ?? string.Empty;
        char trenner = new[] { '\t', ';', ',' }.OrderByDescending(c => probe.Count(x => x == c)).First();
        if (probe.IndexOf(trenner) < 0) trenner = '\0';
        return roh.Where(z => z.Trim().Length > 0)
            .Select(z => (trenner == '\0' ? new[] { z } : z.Split(trenner)).Select(Bereinigen).ToArray())
            .ToList();
    }

    static string Bereinigen(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1].Replace("\"\"", "\"");
        return s.Trim();
    }

    static Teilnehmerimport Auswerten(List<string[]> zeilen)
    {
        int kopf = zeilen.FindIndex(z => z.Any(IstKennungsKopf) || z.Any(IstNamensKopf));
        int kennung = 0, name = -1, vorname = -1, nachname = -1, note = -1, start = 0;
        if (kopf >= 0 && kopf < 5)
        {
            string[] k = zeilen[kopf].Select(s => s.ToLowerInvariant()).ToArray();
            kennung = Array.FindIndex(k, s => s.Contains("kennziffer") || s.Contains("kennung") || s.Contains("matrikel") || s.Contains("matr."));
            if (kennung < 0) kennung = Array.FindIndex(k, IstKennungsKopf);
            vorname = Array.FindIndex(k, s => s.Contains("vorname"));
            nachname = Array.FindIndex(k, s => s.Contains("nachname") || s.Contains("familienname"));
            name = Array.FindIndex(k, s => IstNamensKopf(s) && !s.Contains("vorname") && !s.Contains("nachname") && !s.Contains("familienname"));
            note = Array.FindIndex(k, IstNotenKopf);
            if (kennung < 0) throw new InvalidDataException("Keine Spalte mit Kennziffer bzw. Matrikelnummer gefunden (Überschrift z. B. „Kennziffer“ oder „Matrikel“).");
            start = kopf + 1;
        }

        // Ohne Überschrift: letzte Spalte (nach der Kennung), deren Einträge durchweg Punktwerte sind.
        if (start == 0 && note < 0) note = NotenspalteErkennen(zeilen, kennung);

        var liste = new List<Teilnehmer>();
        var noten = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] z in zeilen.Skip(start))
        {
            string k = Spalte(z, kennung);
            if (k.Length == 0 || !gesehen.Add(k)) continue;
            string n;
            if (vorname >= 0 || nachname >= 0) n = $"{Spalte(z, vorname)} {Spalte(z, nachname)}".Trim();
            else if (name >= 0) n = Spalte(z, name);
            else n = start > 0 ? string.Empty : string.Join(" ", z.Skip(kennung + 1).Where((s, i) => s.Length > 0 && i + kennung + 1 != note));
            liste.Add(new Teilnehmer { Kennung = k, Name = n });
            if (note >= 0 && Punktwert(Spalte(z, note)) is int p) noten[k] = p;
        }
        if (liste.Count == 0) throw new InvalidDataException("Die Datei enthält keine Kennungen.");
        return new Teilnehmerimport(liste, noten);
    }

    static int NotenspalteErkennen(List<string[]> zeilen, int kennung)
    {
        int breite = zeilen.Count == 0 ? 0 : zeilen.Max(z => z.Length);
        for (int s = breite - 1; s >= 0; s--)
        {
            if (s == kennung) continue;
            var werte = zeilen.Select(z => Spalte(z, s)).Where(w => w.Length > 0).ToList();
            if (werte.Count > 0 && werte.Count(w => Punktwert(w) is not null) >= Math.Max(1, (int)Math.Ceiling(werte.Count * 0.9))) return s;
        }
        return -1;
    }

    static bool IstNotenKopf(string s)
    {
        s = s.ToLowerInvariant().Trim();
        return Regex.IsMatch(s, @"^(erst)?(note|noten|punkte|punktzahl|bewertung|ergebnis)\b|erstgutachten|erstkorrektur|erstvotum|erstnote", RegexOptions.CultureInvariant);
    }

    static string Spalte(string[] z, int i) => i >= 0 && i < z.Length ? z[i].Trim() : string.Empty;

    static bool IstKennungsKopf(string s)
    {
        s = s.ToLowerInvariant();
        return s.Contains("kennziffer") || s.Contains("kennung") || s.Contains("matrikel") || s.Contains("matr.") || s == "nr" || s == "id";
    }

    static bool IstNamensKopf(string s) =>
        Regex.IsMatch(s, @"^\s*(vor|nach|familien|ruf)?namen?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

/// <summary>Ergebnis des Imports: Teilnehmende und – falls vorhanden – die Noten des Erstgutachtens je Kennung.</summary>
public sealed record Teilnehmerimport(List<Teilnehmer> Teilnehmende, Dictionary<string, int> Noten);
