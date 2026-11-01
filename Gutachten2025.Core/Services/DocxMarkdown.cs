using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Gutachten2025.Services;

/// <summary>
/// Word-Datei → Markdown für Sachverhalt und Lösung: Überschriften (Formatvorlagen/Gliederungsebene),
/// Absätze, Listen (Einrückung je Ebene), Tabellen, fett und kursiv. Bilder, Fußnoten und Felder entfallen.
/// </summary>
public static class DocxMarkdown
{
    public static string Umwandeln(string pfad)
    {
        using var fs = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using WordprocessingDocument d = WordprocessingDocument.Open(fs, false);
        Body body = d.MainDocumentPart?.Document?.Body ?? throw new InvalidDataException("Die Word-Datei enthält keinen Text.");
        Styles? vorlagen = d.MainDocumentPart.StyleDefinitionsPart?.Styles;

        var md = new StringBuilder();
        foreach (OpenXmlElement e in body.ChildElements)
        {
            if (e is Paragraph p) Absatz(p, vorlagen, md);
            else if (e is Table t) Tabelle(t, md);
        }
        return md.ToString().Trim() + "\n";
    }

    static void Absatz(Paragraph p, Styles? vorlagen, StringBuilder md)
    {
        string text = Laeufe(p).Trim();
        if (text.Length == 0) return;

        int ebene = Ueberschriftsebene(p, vorlagen);
        if (ebene > 0)
        {
            md.Append('\n').Append(new string('#', Math.Min(ebene, 6))).Append(' ').Append(text.Replace("**", string.Empty)).Append("\n\n");
            return;
        }
        NumberingProperties? liste = p.ParagraphProperties?.NumberingProperties;
        if (liste?.NumberingId?.Val?.Value is int id && id != 0)
        {
            int stufe = liste.NumberingLevelReference?.Val?.Value ?? 0;
            md.Append(new string(' ', Math.Clamp(stufe, 0, 8) * 2)).Append("- ").Append(text).Append('\n');
            return;
        }
        if (md.Length > 0 && !(md.Length >= 2 && md[md.Length - 1] == '\n' && md[md.Length - 2] == '\n')) md.Append('\n');
        md.Append(text).Append("\n\n");
    }

    /// <summary>Text eines Absatzes mit **fett** und *kursiv*; Zeilenumbrüche als harte Umbrüche.</summary>
    static string Laeufe(OpenXmlElement absatz)
    {
        var sb = new StringBuilder();
        foreach (Run r in absatz.Descendants<Run>())
        {
            if (r.Ancestors<DeletedRun>().Any()) continue;
            var t = new StringBuilder();
            foreach (OpenXmlElement k in r.ChildElements)
            {
                switch (k)
                {
                    case Text x: t.Append(Escape(x.Text)); break;
                    case TabChar: t.Append(' '); break;
                    case Break: t.Append("  \n"); break;
                    case NoBreakHyphen: t.Append('-'); break;
                }
            }
            if (t.Length == 0) continue;
            string s = t.ToString();
            bool fett = An(r.RunProperties?.Bold), kursiv = An(r.RunProperties?.Italic);
            if ((fett || kursiv) && s.Trim().Length > 0)
            {
                // Markierung ohne umgebenden Leerraum, sonst erkennt Markdown sie nicht.
                string vorne = s[..(s.Length - s.TrimStart().Length)], hinten = s[s.TrimEnd().Length..];
                string marke = fett && kursiv ? "***" : fett ? "**" : "*";
                s = vorne + marke + s.Trim() + marke + hinten;
            }
            sb.Append(s);
        }
        // Aneinandergrenzende gleiche Markierungen zusammenfassen („**a****b**“ → „**ab**“).
        return sb.ToString().Replace("****", string.Empty).Replace("**  **", " ");
    }

    static bool An(OnOffType? o) => o is not null && (o.Val is null || o.Val.Value);

    static int Ueberschriftsebene(Paragraph p, Styles? vorlagen)
    {
        ParagraphProperties? pp = p.ParagraphProperties;
        if (pp?.OutlineLevel?.Val?.Value is int direkt && direkt < 9) return direkt + 1;
        string? id = pp?.ParagraphStyleId?.Val?.Value;
        for (int tiefe = 0; id is not null && tiefe < 10; tiefe++)
        {
            Style? s = vorlagen?.Elements<Style>().FirstOrDefault(x => x.StyleId?.Value == id);
            string name = s?.StyleName?.Val?.Value ?? id;
            if (s?.StyleParagraphProperties?.OutlineLevel?.Val?.Value is int ol && ol < 9) return ol + 1;
            foreach (string vorsilbe in new[] { "heading", "überschrift", "berschrift" })
            {
                int k = name.IndexOf(vorsilbe, StringComparison.OrdinalIgnoreCase);
                if (k >= 0 && int.TryParse(name[(k + vorsilbe.Length)..].Trim(), out int n) && n is >= 1 and <= 9) return n;
            }
            if (name.Equals("Title", StringComparison.OrdinalIgnoreCase) || name.Equals("Titel", StringComparison.OrdinalIgnoreCase)) return 1;
            id = s?.BasedOn?.Val?.Value;
        }
        return 0;
    }

    static void Tabelle(Table t, StringBuilder md)
    {
        List<List<string>> zeilen = t.Elements<TableRow>()
            .Select(r => r.Elements<TableCell>()
                .Select(c => string.Join(" ", c.Elements<Paragraph>().Select(Laeufe).Where(s => s.Trim().Length > 0))
                    .Replace("  \n", " ").Replace("\n", " ").Replace("|", "\\|").Trim())
                .ToList())
            .Where(z => z.Count > 0)
            .ToList();
        if (zeilen.Count == 0) return;
        int spalten = zeilen.Max(z => z.Count);
        md.Append('\n');
        for (int i = 0; i < zeilen.Count; i++)
        {
            List<string> z = zeilen[i];
            md.Append("| ").Append(string.Join(" | ", Enumerable.Range(0, spalten).Select(k => k < z.Count ? z[k] : string.Empty))).Append(" |\n");
            if (i == 0) md.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", spalten))).Append('\n');
        }
        md.Append('\n');
    }

    /// <summary>Zeichen, die Markdown sonst als Formatierung läse.</summary>
    static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c is '\\' or '*' or '_' or '`' or '[' or ']' or '<' or '~') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
