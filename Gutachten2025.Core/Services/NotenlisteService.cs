using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Gutachten2025.Services;

/// <summary>
/// Liste.xlsx (Open XML SDK): Blatt 1 „Noten“ (Kopf in Zeilen 1–2, Daten ab Zeile 3; A Kennung, B Punkte, C Note,
/// ab D Zusatzspalten wie Rohpunkte oder Teilpunkte), Blatt 2 „Statistik“ (Zeile 4 Anzahl, Zeile 5 Anteil, A–S für 0–18, T4 Summe).
/// Formeln rechnet Excel beim Öffnen neu; eigene Werte werden als Zahlen geschrieben.
/// </summary>
public sealed class NotenlisteService
{
    public const int ErsteDatenzeile = 3;
    public const int KopfZeile = 2;
    public const int ErsteZusatzspalte = 4;

    /// <summary>Zeile der Vorlage, deren Formate neue Zeilen übernehmen (mittlere Datenzeile ohne Abschlussrand).</summary>
    const uint MusterZeile = 4;

    public void Initialisieren(string pfad, string klausur)
    {
        using SpreadsheetDocument doc = SpreadsheetDocument.Open(pfad, true);
        WorkbookPart wbp = doc.WorkbookPart!;
        Xlsx.TextSetzen(Xlsx.ZelleHolen(Daten(Xlsx.Blatt(wbp, 0)), 1, 1), $"Notenliste für {klausur}");
        if (Xlsx.BlattAnzahl(wbp) > 1)
            Xlsx.TextSetzen(Xlsx.ZelleHolen(Daten(Xlsx.Blatt(wbp, 1)), 1, 1), $"Statistik für {klausur}");
        wbp.Workbook.Save();
    }

    static SheetData Daten(WorksheetPart wsp) => wsp.Worksheet.GetFirstChild<SheetData>()
        ?? throw new InvalidDataException("Die Notenliste ist beschädigt (Tabellenblatt ohne Daten).");

    /// <summary>Auch bei in Excel geöffneter Datei lesbar.</summary>
    static FileStream Lesestrom(string pfad) => new(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    /// <summary>Prüft lesend, ob die Kennung schon eingetragen ist.</summary>
    public bool Enthaelt(string pfad, string kennung)
    {
        using FileStream s = Lesestrom(pfad);
        using SpreadsheetDocument doc = SpreadsheetDocument.Open(s, false);
        WorkbookPart wbp = doc.WorkbookPart!;
        return FindeZeile(Daten(Xlsx.Blatt(wbp, 0)), wbp, kennung) > 0;
    }

    /// <summary>Liest alle Einträge (Kennung, Punkte).</summary>
    public List<(string Kennung, int Punkte)> Lesen(string pfad)
    {
        var liste = new List<(string, int)>();
        using FileStream s = Lesestrom(pfad);
        using (SpreadsheetDocument doc = SpreadsheetDocument.Open(s, false))
        {
            WorkbookPart wbp = doc.WorkbookPart!;
            SheetData sd = Daten(Xlsx.Blatt(wbp, 0));
            foreach (Row r in sd.Elements<Row>().Where(r => r.RowIndex?.Value >= ErsteDatenzeile))
            {
                uint z = r.RowIndex!.Value;
                string kennung = Xlsx.Text(Xlsx.Zelle(sd, 1, z), wbp).Trim();
                if (kennung.Length == 0) continue;
                if (Xlsx.Zahl(Xlsx.Zelle(sd, 2, z), wbp) is not double punkte) continue;
                int ganz = (int)Math.Round(punkte);
                if (Notenskala.IstGueltig(ganz)) liste.Add((kennung, ganz));
            }
        }
        return liste;
    }

    /// <summary>Trägt Note und Zusatzspalten ein bzw. überschreibt einen vorhandenen Eintrag; aktualisiert Statistik, Tabelle und Druckbereich.</summary>
    public void EintragSetzen(string pfad, string kennung, int punkte, IReadOnlyList<(string Kopf, double? Wert)>? zusatz = null)
    {
        using SpreadsheetDocument doc = SpreadsheetDocument.Open(pfad, true);
        WorkbookPart wbp = doc.WorkbookPart!;
        WorksheetPart noten = Xlsx.Blatt(wbp, 0);
        SheetData sd = Daten(noten);

        uint zeile = FindeZeile(sd, wbp, kennung);
        uint letzte = LetzteDatenzeile(sd, wbp);
        if (zeile == 0)
        {
            zeile = letzte + 1;
            letzte = zeile;
        }
        FormatUebernehmen(sd, zeile, 3 + (zusatz?.Count ?? 0));

        Xlsx.TextSetzen(Xlsx.ZelleHolen(sd, 1, zeile), kennung.Trim());
        Xlsx.ZahlSetzen(Xlsx.ZelleHolen(sd, 2, zeile), punkte);
        Xlsx.TextSetzen(Xlsx.ZelleHolen(sd, 3, zeile), Notenskala.Bezeichnung(punkte));

        if (zusatz is { Count: > 0 })
        {
            uint kopfStil = Xlsx.Zelle(sd, 3, KopfZeile)?.StyleIndex?.Value ?? 0;
            for (int i = 0; i < zusatz.Count; i++)
            {
                int spalte = ErsteZusatzspalte + i;
                Cell kopf = Xlsx.ZelleHolen(sd, spalte, KopfZeile);
                if (!string.Equals(Xlsx.Text(kopf, wbp), zusatz[i].Kopf, StringComparison.Ordinal))
                {
                    Xlsx.TextSetzen(kopf, zusatz[i].Kopf);
                    kopf.StyleIndex = kopfStil;
                }
                Xlsx.ZahlSetzen(Xlsx.ZelleHolen(sd, spalte, zeile), zusatz[i].Wert);
            }
        }

        uint ende = Math.Max(letzte, ErsteDatenzeile);
        BereicheAnpassen(wbp, noten, ende);
        if (Xlsx.BlattAnzahl(wbp) > 1)
            StatistikSetzen(Daten(Xlsx.Blatt(wbp, 1)), Blattname(wbp, 0), ende);

        Xlsx.NeuberechnungErzwingen(wbp);
        noten.Worksheet.Save();
        wbp.Workbook.Save();
    }

    static string Blattname(WorkbookPart wbp, int index) =>
        wbp.Workbook.Sheets?.Elements<Sheet>().ElementAtOrDefault(index)?.Name?.Value ?? "Noten";

    /// <summary>Übernimmt für eine neue bzw. unformatierte Zeile Höhe und Zellformate der Musterzeile.</summary>
    static void FormatUebernehmen(SheetData sd, uint zeile, int spalten)
    {
        Row? muster = Xlsx.Zeile(sd, MusterZeile);
        if (muster is null || zeile == MusterZeile) return;
        Row ziel = Xlsx.ZeileHolen(sd, zeile);
        if (ziel.Height is null && muster.Height is not null)
        {
            ziel.Height = muster.Height.Value;
            ziel.CustomHeight = true;
        }
        for (int s = 1; s <= spalten; s++)
        {
            Cell z = Xlsx.ZelleHolen(sd, s, zeile);
            if (z.StyleIndex is null && Xlsx.Zelle(sd, Math.Min(s, 3), MusterZeile)?.StyleIndex is { } stil)
                z.StyleIndex = stil.Value;
        }
    }

    /// <summary>Tabellenobjekt, Filter und Druckbereich auf die letzte Datenzeile erweitern.</summary>
    static void BereicheAnpassen(WorkbookPart wbp, WorksheetPart noten, uint ende)
    {
        foreach (TableDefinitionPart tp in noten.TableDefinitionParts)
        {
            Table? t = tp.Table;
            if (t?.Reference?.Value is not string bezug) continue;
            string neu = Regex.Replace(bezug, @"(\d+)$", ende.ToString());
            t.Reference = neu;
            if (t.AutoFilter is { } f) f.Reference = neu;
            t.Save();
        }
        if (wbp.Workbook.DefinedNames is { } namen)
            foreach (DefinedName n in namen.Elements<DefinedName>())
            {
                if (n.LocalSheetId?.Value != 0 || n.Name?.Value is not ("_xlnm._FilterDatabase" or "_xlnm.Print_Area")) continue;
                n.Text = Regex.Replace(n.Text ?? string.Empty, @"(\$[A-Z]+\$)(\d+)$", m => m.Groups[1].Value + ende);
            }
    }

    static void StatistikSetzen(SheetData stat, string blatt, uint letzteZeile)
    {
        string bereich = $"'{blatt.Replace("'", "''")}'!$B${ErsteDatenzeile}:$B${letzteZeile}";
        for (int punkte = Notenskala.Minimum; punkte <= Notenskala.Maximum; punkte++)
        {
            int spalte = punkte + 1; // A = 0 Punkte … S = 18 Punkte
            Xlsx.FormelSetzen(Xlsx.ZelleHolen(stat, spalte, 4), $"COUNTIF({bereich},{punkte})");
            Xlsx.FormelSetzen(Xlsx.ZelleHolen(stat, spalte, 5), $"IF($T$4=0,0,{Xlsx.Spalte(spalte)}4/$T$4)");
        }
        Xlsx.FormelSetzen(Xlsx.ZelleHolen(stat, 20, 4), "SUM(A4:S4)");
    }

    static uint FindeZeile(SheetData sd, WorkbookPart wbp, string kennung)
    {
        foreach (Row r in sd.Elements<Row>().Where(r => r.RowIndex?.Value >= ErsteDatenzeile))
        {
            uint z = r.RowIndex!.Value;
            if (string.Equals(Xlsx.Text(Xlsx.Zelle(sd, 1, z), wbp).Trim(), kennung.Trim(), StringComparison.OrdinalIgnoreCase))
                return z;
        }
        return 0;
    }

    /// <summary>Letzte Zeile mit Kennung in Spalte A; Lücken werden übersprungen.</summary>
    static uint LetzteDatenzeile(SheetData sd, WorkbookPart wbp)
    {
        uint letzte = ErsteDatenzeile - 1;
        foreach (Row r in sd.Elements<Row>().Where(r => r.RowIndex?.Value >= ErsteDatenzeile))
        {
            uint z = r.RowIndex!.Value;
            if (Xlsx.Text(Xlsx.Zelle(sd, 1, z), wbp).Trim().Length > 0) letzte = Math.Max(letzte, z);
        }
        return letzte;
    }
}
