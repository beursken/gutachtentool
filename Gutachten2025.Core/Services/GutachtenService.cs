using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Gutachten2025.Services;

/// <summary>Zeile des Anhangs „Bewertungsbogen“.</summary>
public sealed record BogenZeile(BogenArt Art, string Titel, int Stufe, string Punkte, IReadOnlyList<IReadOnlyList<TextLauf>> Kommentar);

public enum BogenArt { Aufgabe, Ueberschrift, Position, Zuschlag, Summe }

/// <summary>Erzeugt das Gutachten aus MUSTER.docx (Open XML SDK) und importiert Word-Vorlagen.</summary>
public sealed class GutachtenService
{
    public const string FreitextMarke = "###FREITEXT###";
    public const string TeilpunkteMarke = "###TEILPUNKTE###";
    public const string BogenMarke = "###BEWERTUNGSBOGEN###";

    const string TypVorlage = "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml";
    const string TypDokument = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

    /// <param name="absaetze">Gutachtentext je Absatz mit Fett/Kursiv.</param>
    /// <param name="anhang">Absatz nach dem Freitext, wenn die Vorlage keinen Platzhalter ###TEILPUNKTE### enthält.</param>
    /// <param name="bogen">Anhang „Bewertungsbogen“ (am Platzhalter ###BEWERTUNGSBOGEN### oder am Ende); null = keiner.</param>
    public void Erzeugen(string vorlage, string ziel, IReadOnlyList<IReadOnlyList<TextLauf>> absaetze,
        IReadOnlyDictionary<string, string> platzhalter, string? anhang = null, IReadOnlyList<BogenZeile>? bogen = null)
    {
        string tmp = ziel + ".tmp";
        File.Copy(vorlage, tmp, overwrite: true);
        File.SetAttributes(tmp, FileAttributes.Normal);
        try
        {
            using (WordprocessingDocument doc = WordprocessingDocument.Open(tmp, true))
            {
                MainDocumentPart main = doc.MainDocumentPart ?? throw new InvalidDataException("Die Word-Vorlage enthält kein Dokument.");
                Body body = main.Document?.Body ?? throw new InvalidDataException("Die Word-Vorlage enthält keinen Textkörper.");

                var text = absaetze.ToList();
                bool teilpunkteMarke = Absaetze(body).Any(p => Inhalt(p).Contains(TeilpunkteMarke, StringComparison.Ordinal));
                if (!teilpunkteMarke && !string.IsNullOrWhiteSpace(anhang))
                    text.Add(new[] { new TextLauf(anhang) });

                FreitextEinsetzen(body, text);
                BogenEinsetzen(body, bogen);

                var alle = new Dictionary<string, string>(platzhalter);
                alle.TryAdd(TeilpunkteMarke, string.Empty);
                alle.TryAdd(BogenMarke, string.Empty);
                foreach (OpenXmlElement wurzel in Bereiche(main))
                    foreach (var (marke, wert) in alle)
                        Ersetzen(wurzel, marke, wert);

                main.Document.Save();
            }
            File.Move(tmp, ziel, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    static IEnumerable<OpenXmlElement> Bereiche(MainDocumentPart main)
    {
        if (main.Document?.Body is { } b) yield return b;
        foreach (HeaderPart h in main.HeaderParts) if (h.Header is { } kopf) yield return kopf;
        foreach (FooterPart f in main.FooterParts) if (f.Footer is { } fuss) yield return fuss;
    }

    static IEnumerable<Paragraph> Absaetze(OpenXmlElement wurzel) => wurzel.Descendants<Paragraph>();

    static string Inhalt(Paragraph p) => string.Concat(p.Descendants<Text>().Select(t => t.Text));

    /// <summary>Ersetzt eine Marke auch dann, wenn Word sie auf mehrere Runs verteilt hat; Format des ersten Runs bleibt.</summary>
    static void Ersetzen(OpenXmlElement wurzel, string marke, string wert)
    {
        foreach (Paragraph p in Absaetze(wurzel).ToList())
        {
            for (int sicherung = 0; sicherung < 50; sicherung++)
            {
                List<Text> texte = p.Descendants<Text>().ToList();
                string ganz = string.Concat(texte.Select(t => t.Text));
                int pos = ganz.IndexOf(marke, StringComparison.Ordinal);
                if (pos < 0) break;
                int ende = pos + marke.Length, versatz = 0;
                bool eingesetzt = false;
                foreach (Text t in texte)
                {
                    int a = versatz, b = versatz + t.Text.Length;
                    versatz = b;
                    if (b <= pos || a >= ende) continue;
                    int von = Math.Max(pos, a) - a, bis = Math.Min(ende, b) - a;
                    t.Text = t.Text[..von] + (eingesetzt ? string.Empty : wert) + t.Text[bis..];
                    t.Space = SpaceProcessingModeValues.Preserve;
                    eingesetzt = true;
                }
            }
        }
    }

    /// <summary>Ersetzt den Absatz mit ###FREITEXT### durch die Absätze des Gutachtens (Format des Absatzes und seines ersten Runs).</summary>
    static void FreitextEinsetzen(Body body, IReadOnlyList<IReadOnlyList<TextLauf>> absaetze)
    {
        Paragraph? ort = Absaetze(body).FirstOrDefault(p => Inhalt(p).Contains(FreitextMarke, StringComparison.Ordinal));
        if (ort is null) return;

        string ganz = Inhalt(ort);
        int pos = ganz.IndexOf(FreitextMarke, StringComparison.Ordinal);
        string vor = ganz[..pos], nach = ganz[(pos + FreitextMarke.Length)..];
        RunProperties? basis = ort.Descendants<Run>().FirstOrDefault(r => r.Descendants<Text>().Any())?.RunProperties;

        var liste = absaetze.Select(a => a.ToList()).ToList();
        if (liste.Count == 0) liste.Add(new List<TextLauf>());
        if (vor.Length > 0) liste[0].Insert(0, new TextLauf(vor));
        if (nach.Length > 0) liste[^1].Add(new TextLauf(nach));

        foreach (List<TextLauf> absatz in liste)
        {
            var p = new Paragraph();
            if (ort.ParagraphProperties is { } pp) p.Append(pp.CloneNode(true));
            foreach (TextLauf l in absatz) p.Append(Lauf(l, basis));
            ort.InsertBeforeSelf(p);
        }
        ort.Remove();
    }

    static Run Lauf(TextLauf l, RunProperties? basis, bool? fett = null, string? groesse = null)
    {
        var rp = basis?.CloneNode(true) as RunProperties ?? new RunProperties();
        if (l.F || fett == true) rp.Bold = new Bold();
        if (l.K) rp.Italic = new Italic();
        if (groesse is not null)
        {
            rp.FontSize = new FontSize { Val = groesse };
            rp.FontSizeComplexScript = new FontSizeComplexScript { Val = groesse };
        }
        var r = new Run();
        if (rp.HasChildren) r.Append(rp);
        r.Append(new Text(l.T) { Space = SpaceProcessingModeValues.Preserve });
        return r;
    }

    // ---------- Anhang „Bewertungsbogen“ ----------

    const string Tabellenschrift = "20"; // 10 pt
    const string Randfarbe = "A6A6A6";

    static void BogenEinsetzen(Body body, IReadOnlyList<BogenZeile>? bogen)
    {
        Paragraph? marke = Absaetze(body).FirstOrDefault(p => Inhalt(p).Contains(BogenMarke, StringComparison.Ordinal));
        if (bogen is not { Count: > 0 })
        {
            return; // Marke wird später durch leeren Text ersetzt
        }

        var elemente = new List<OpenXmlElement>
        {
            new Paragraph(new Run(new Break { Type = BreakValues.Page })),
            new Paragraph(
                new ParagraphProperties(new KeepNext(), new SpacingBetweenLines { After = "160" }),
                new Run(new RunProperties(new Bold(), new FontSize { Val = "28" }, new FontSizeComplexScript { Val = "28" }),
                    new Text("Anhang: Bewertungsbogen"))),
            Tabelle(bogen),
        };

        if (marke is not null)
        {
            foreach (OpenXmlElement e in elemente) marke.InsertBeforeSelf(e);
            marke.Remove();
        }
        else
        {
            OpenXmlElement? sektion = body.Elements<SectionProperties>().LastOrDefault();
            foreach (OpenXmlElement e in elemente)
            {
                if (sektion is not null) body.InsertBefore(e, sektion);
                else body.Append(e);
            }
        }
    }

    static Table Tabelle(IReadOnlyList<BogenZeile> zeilen)
    {
        BorderType Rand<T>() where T : BorderType, new() => new T { Val = BorderValues.Single, Size = 4U, Color = Randfarbe };
        var tabelle = new Table(
            new TableProperties(
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableBorders(Rand<TopBorder>(), Rand<LeftBorder>(), Rand<BottomBorder>(), Rand<RightBorder>(),
                                 Rand<InsideHorizontalBorder>(), Rand<InsideVerticalBorder>()),
                new TableLayout { Type = TableLayoutValues.Fixed },
                new TableCellMarginDefault(
                    new TopMargin { Width = "40", Type = TableWidthUnitValues.Dxa },
                    new TableCellLeftMargin { Width = (short)100, Type = TableWidthValues.Dxa },
                    new BottomMargin { Width = "40", Type = TableWidthUnitValues.Dxa },
                    new TableCellRightMargin { Width = (short)100, Type = TableWidthValues.Dxa })),
            new TableGrid(new GridColumn { Width = "3500" }, new GridColumn { Width = "6100" }));

        // Kopfzeile, auf jeder Seite wiederholt
        tabelle.Append(Zeile(kopf: true, schattierung: "D9D9D9",
            Zelle(1800, Absatz(new[] { new TextLauf("Gliederungspunkt", true) })),
            Zelle(3200, Absatz(new[] { new TextLauf("Bewertung", true) }))));

        foreach (BogenZeile z in zeilen)
        {
            bool fett = z.Art is BogenArt.Aufgabe or BogenArt.Ueberschrift or BogenArt.Summe or BogenArt.Zuschlag;
            string? schattierung = z.Art switch { BogenArt.Aufgabe => "F2F2F2", BogenArt.Summe => "D9D9D9", _ => null };

            var links = Absatz(new[] { new TextLauf(z.Titel, fett) }, einzug: Math.Max(0, z.Stufe - 1) * 180);
            var rechts = new List<Paragraph>();
            foreach (var absatz in z.Kommentar) rechts.Add(Absatz(absatz));
            if (z.Punkte.Length > 0) rechts.Add(Absatz(new[] { new TextLauf(z.Punkte, true) }, rechtsbuendig: true));
            if (rechts.Count == 0) rechts.Add(Absatz(Array.Empty<TextLauf>()));

            tabelle.Append(Zeile(kopf: false, schattierung, Zelle(1800, links), Zelle(3200, rechts.ToArray())));
        }
        return tabelle;
    }

    static TableRow Zeile(bool kopf, string? schattierung, params TableCell[] zellen)
    {
        var zeile = new TableRow();
        var eigenschaften = new TableRowProperties(new CantSplit());
        if (kopf) eigenschaften.Append(new TableHeader());
        zeile.Append(eigenschaften);
        foreach (TableCell z in zellen)
        {
            if (schattierung is not null)
                z.TableCellProperties!.Append(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = schattierung });
            zeile.Append(z);
        }
        return zeile;
    }

    static TableCell Zelle(int fuenfzigstelProzent, params Paragraph[] absaetze)
    {
        var zelle = new TableCell(new TableCellProperties(
            new TableCellWidth { Width = fuenfzigstelProzent.ToString(), Type = TableWidthUnitValues.Pct }));
        foreach (Paragraph p in absaetze) zelle.Append(p);
        return zelle;
    }

    static Paragraph Absatz(IEnumerable<TextLauf> laeufe, int einzug = 0, bool rechtsbuendig = false)
    {
        var pp = new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" });
        if (einzug > 0) pp.Append(new Indentation { Left = einzug.ToString() });
        if (rechtsbuendig) pp.Append(new Justification { Val = JustificationValues.Right });
        var p = new Paragraph(pp);
        foreach (TextLauf l in laeufe) p.Append(Lauf(l, null, groesse: Tabellenschrift));
        return p;
    }

    // ---------- Vorlagen ----------

    /// <summary>Kopiert eine .docx/.dotx als Vorlage; bei .dotx wird der Inhaltstyp auf „Dokument“ umgestellt.</summary>
    public void VorlageImportieren(string quelle, string ziel)
    {
        string tmp = ziel + ".tmp";
        File.Copy(quelle, tmp, overwrite: true);
        if (Path.GetExtension(quelle).Equals(".dotx", StringComparison.OrdinalIgnoreCase))
            InhaltstypAufDokument(tmp);
        File.Move(tmp, ziel, overwrite: true);
    }

    static void InhaltstypAufDokument(string pfad)
    {
        using ZipArchive zip = ZipFile.Open(pfad, ZipArchiveMode.Update);
        ZipArchiveEntry eintrag = zip.GetEntry("[Content_Types].xml")
            ?? throw new InvalidDataException("Die Vorlage ist keine gültige Word-Datei.");
        string xml;
        using (var r = new StreamReader(eintrag.Open(), Encoding.UTF8))
            xml = r.ReadToEnd();
        eintrag.Delete();
        ZipArchiveEntry neu = zip.CreateEntry("[Content_Types].xml");
        using var w = new StreamWriter(neu.Open(), new UTF8Encoding(false));
        w.Write(xml.Replace(TypVorlage, TypDokument));
    }

    /// <summary>Standardschrift einer Word-Datei (docDefaults bzw. Designschrift „minor“); null, wenn nicht ermittelbar.</summary>
    public static string? VorlagenSchrift(string pfad)
    {
        try
        {
            if (!File.Exists(pfad)) return null;
            using var fs = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            string Lesen(string name)
            {
                ZipArchiveEntry? e = zip.GetEntry(name);
                if (e is null) return string.Empty;
                using var r = new StreamReader(e.Open(), Encoding.UTF8);
                return r.ReadToEnd();
            }
            string styles = Lesen("word/styles.xml");
            int defaults = styles.IndexOf("<w:docDefaults", StringComparison.Ordinal);
            string bereich = defaults >= 0 ? styles[defaults..] : styles;
            var direkt = Regex.Match(bereich, "<w:rFonts[^>]*w:ascii=\"([^\"]+)\"");
            if (direkt.Success) return direkt.Groups[1].Value;
            var minor = Regex.Match(Lesen("word/theme/theme1.xml"), "<a:minorFont>\\s*<a:latin typeface=\"([^\"]+)\"");
            return minor.Success ? minor.Groups[1].Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
