using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Gutachten2025.Services;

/// <summary>Kleine Hilfen für Excel-Dateien mit dem Open XML SDK (Zellen lesen/schreiben, Reihenfolge wahren).</summary>
internal static class Xlsx
{
    static readonly Regex Bezug = new(@"^(?<s>[A-Z]+)(?<z>\d+)$", RegexOptions.CultureInvariant);

    public static WorksheetPart Blatt(WorkbookPart wbp, int index)
    {
        Sheet blatt = wbp.Workbook.Sheets?.Elements<Sheet>().ElementAtOrDefault(index)
            ?? throw new InvalidOperationException($"Die Excel-Datei hat kein Tabellenblatt Nr. {index + 1}.");
        return (WorksheetPart)wbp.GetPartById(blatt.Id!.Value!);
    }

    public static int BlattAnzahl(WorkbookPart wbp) => wbp.Workbook.Sheets?.Elements<Sheet>().Count() ?? 0;

    public static string Spalte(int nummer)
    {
        string s = string.Empty;
        while (nummer > 0)
        {
            int r = (nummer - 1) % 26;
            s = (char)('A' + r) + s;
            nummer = (nummer - 1) / 26;
        }
        return s;
    }

    public static int SpaltenNummer(string bezug)
    {
        int n = 0;
        foreach (char c in bezug)
        {
            if (c < 'A' || c > 'Z') break;
            n = n * 26 + (c - 'A' + 1);
        }
        return n;
    }

    public static Row? Zeile(SheetData sd, uint nummer) => sd.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == nummer);

    /// <summary>Liefert die Zeile; legt sie bei Bedarf an der richtigen Stelle an.</summary>
    public static Row ZeileHolen(SheetData sd, uint nummer)
    {
        Row? zeile = Zeile(sd, nummer);
        if (zeile is not null) return zeile;
        zeile = new Row { RowIndex = nummer };
        Row? danach = sd.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value > nummer);
        if (danach is not null) sd.InsertBefore(zeile, danach);
        else sd.Append(zeile);
        return zeile;
    }

    public static Cell? Zelle(SheetData sd, int spalte, uint zeile) =>
        Zeile(sd, zeile)?.Elements<Cell>().FirstOrDefault(c => c.CellReference?.Value == Spalte(spalte) + zeile);

    /// <summary>Liefert die Zelle; legt sie bei Bedarf in Spaltenreihenfolge an.</summary>
    public static Cell ZelleHolen(SheetData sd, int spalte, uint zeile)
    {
        Row r = ZeileHolen(sd, zeile);
        string bezug = Spalte(spalte) + zeile;
        Cell? zelle = r.Elements<Cell>().FirstOrDefault(c => c.CellReference?.Value == bezug);
        if (zelle is not null) return zelle;
        zelle = new Cell { CellReference = bezug };
        Cell? danach = r.Elements<Cell>().FirstOrDefault(c => SpaltenNummer(c.CellReference?.Value ?? "") > spalte);
        if (danach is not null) r.InsertBefore(zelle, danach);
        else r.Append(zelle);
        return zelle;
    }

    public static string Text(Cell? c, WorkbookPart wbp)
    {
        if (c is null) return string.Empty;
        if (c.DataType?.Value == CellValues.InlineString) return c.InlineString?.InnerText ?? string.Empty;
        string roh = c.CellValue?.Text ?? string.Empty;
        if (c.DataType?.Value == CellValues.SharedString)
        {
            if (!int.TryParse(roh, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return string.Empty;
            return wbp.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().ElementAtOrDefault(i)?.InnerText ?? string.Empty;
        }
        if (c.DataType?.Value == CellValues.Boolean) return roh == "1" ? "WAHR" : "FALSCH";
        if (c.DataType is null || c.DataType.Value == CellValues.Number)
        {
            // Zahlen ohne Exponentialschreibweise (z. B. Matrikelnummern)
            if (double.TryParse(roh, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15
                    ? ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture)
                    : d.ToString("0.###############", CultureInfo.GetCultureInfo("de-DE"));
        }
        return roh;
    }

    public static double? Zahl(Cell? c, WorkbookPart wbp)
    {
        if (c is null) return null;
        if (c.DataType is null || c.DataType.Value == CellValues.Number)
            return double.TryParse(c.CellValue?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
        return Models.Zahl.Lesen(Text(c, wbp), out double w) ? w : null;
    }

    public static void TextSetzen(Cell c, string text)
    {
        c.CellFormula = null;
        c.CellValue = null;
        c.DataType = CellValues.InlineString;
        c.InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    public static void ZahlSetzen(Cell c, double? wert)
    {
        c.CellFormula = null;
        c.InlineString = null;
        if (wert is null)
        {
            c.DataType = null;
            c.CellValue = null;
            return;
        }
        c.DataType = CellValues.Number;
        c.CellValue = new CellValue(wert.Value.ToString("R", CultureInfo.InvariantCulture));
    }

    public static void FormelSetzen(Cell c, string formel)
    {
        c.InlineString = null;
        c.DataType = null;
        c.CellValue = null;
        c.CellFormula = new CellFormula(formel);
    }

    /// <summary>Excel soll beim Öffnen alles neu berechnen (das SDK rechnet nicht); die Berechnungskette wird verworfen.</summary>
    public static void NeuberechnungErzwingen(WorkbookPart wbp)
    {
        if (wbp.CalculationChainPart is { } kette) wbp.DeletePart(kette);
        CalculationProperties eig = wbp.Workbook.CalculationProperties ??= new CalculationProperties();
        eig.FullCalculationOnLoad = true;
        eig.ForceFullCalculation = true;
    }

    public static (string Spalte, uint Zeile) Zerlegen(string bezug)
    {
        Match m = Bezug.Match(bezug);
        return m.Success ? (m.Groups["s"].Value, uint.Parse(m.Groups["z"].Value, CultureInfo.InvariantCulture)) : (bezug, 0);
    }
}
