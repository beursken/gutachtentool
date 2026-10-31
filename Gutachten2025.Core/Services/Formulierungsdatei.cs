using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Gutachten2025.Services;

/// <summary>
/// Liest und schreibt Texte.txt.
/// <para>Format (ab Version 1.1): Einrückung mit Tabulator = Unterebene, „+“ = Gruppe,
/// „[#kürzel 10-18]“ vor einem Baustein = Kürzel und Punktebereich, Zeilen mit „//“ = Kommentar.</para>
/// <para>Altformat (ohne Einrückung): „+“-Zeilen sind Gruppen der obersten Ebene, die folgenden Zeilen deren Bausteine.</para>
/// Gelesen wird UTF-8/UTF-16 (mit oder ohne BOM) oder ersatzweise Windows-1252; geschrieben wird UTF-8 mit BOM.
/// </summary>
public static class Formulierungsdatei
{
    const string Kopfzeile =
        "// Gutachtentool – Formulierungen. Tabulator-Einrückung = Unterebene, „+“ = Gruppe, " +
        "„[#kürzel 10-18]“ = Kürzel (Einfügen mit Kürzel + Tab) und passender Punktebereich.";

    static readonly Regex Meta = new(@"^\[(?<inhalt>[^\]]*)\]\s*", RegexOptions.CultureInvariant);
    static readonly Regex KuerzelMuster = new(@"^#(?<k>[\p{L}\d_\-]{1,24})$", RegexOptions.CultureInvariant);
    static readonly Regex BereichMuster = new(@"^(?<von>\d{1,2})(?:\s*[-–]\s*(?<bis>\d{1,2}))?$", RegexOptions.CultureInvariant);
    static readonly Regex GueltigesKuerzel = new(@"^[\p{L}\d_\-]{1,24}$", RegexOptions.CultureInvariant);

    static Formulierungsdatei() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static List<Baustein> Laden(string pfad) => Parsen(TextLesen(pfad));

    public static List<Baustein> Parsen(string inhalt)
    {
        string[] zeilen = inhalt.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        // Neues Format: Kommentarkopf oder Tabulator-Einrückung. Vereinzelte Leerzeichen am Zeilenanfang alter Dateien zählen nicht.
        bool neuesFormat = zeilen.Any(z => z.TrimStart().StartsWith("//", StringComparison.Ordinal))
                           || zeilen.Any(z => z.StartsWith('\t') && z.Trim().Length > 0);

        var wurzeln = new List<Baustein>();
        var gruppen = new List<Baustein>(); // gruppen[i] = offene Gruppe der Tiefe i

        foreach (string roh in zeilen)
        {
            string zeile = roh.Trim();
            if (zeile.Length == 0 || zeile.StartsWith("//", StringComparison.Ordinal)) continue;

            bool gruppe = zeile.StartsWith('+');
            // Metadaten „[#kürzel 10-18]“ nur im neuen Format – in alten Dateien gehört eine Klammer zum Text.
            Baustein b = gruppe ? new Baustein(zeile.TrimStart('+').Trim(), gruppe: true)
                       : neuesFormat ? BausteinAus(zeile) : new Baustein(zeile);
            if (b.Text.Length == 0) continue;

            if (!neuesFormat)
            {
                if (gruppe)
                {
                    wurzeln.Add(b);
                    gruppen.Clear();
                    gruppen.Add(b);
                }
                else if (gruppen.Count > 0) gruppen[0].Children.Add(b);
                else wurzeln.Add(b);
                continue;
            }

            int tiefe = Math.Min(Tiefe(roh), gruppen.Count); // nie tiefer als die offene Gruppenkette
            if (gruppen.Count > tiefe) gruppen.RemoveRange(tiefe, gruppen.Count - tiefe);
            if (tiefe == 0) wurzeln.Add(b);
            else gruppen[tiefe - 1].Children.Add(b);
            if (gruppe) gruppen.Add(b);
        }
        // Version 1.0 schrieb jeden Eintrag der obersten Ebene mit „+“: „+“-Zeilen ohne Kinder waren dort einfügbare Bausteine.
        if (!neuesFormat)
            foreach (Baustein w in wurzeln)
                if (w.IstGruppe && w.Children.Count == 0) w.IstGruppe = false;
        Nummerieren(wurzeln);
        return wurzeln;
    }

    public static string Serialisieren(IEnumerable<Baustein> wurzeln)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Kopfzeile);

        void Schreiben(Baustein b, int tiefe)
        {
            string text = Einzeilig(b.Text);
            if (b.IstGruppe) text = text.TrimStart('+').Trim();
            if (text.Length == 0 && b.IstGruppe) text = "(ohne Bezeichnung)";
            if (text.Length == 0) return;
            sb.Append('\t', tiefe);
            if (b.IstGruppe)
            {
                sb.Append('+').AppendLine(text);
                foreach (Baustein k in b.Children) Schreiben(k, tiefe + 1);
            }
            else
            {
                string meta = MetaText(b);
                // Ein Baustein, der selbst mit „[“ oder „//“ beginnt, erhält ein leeres Präfix, damit er nicht als Metadaten gelesen wird.
                if (meta.Length == 0 && (text.StartsWith('[') || text.StartsWith('+') || text.StartsWith("//", StringComparison.Ordinal))) meta = "[] ";
                sb.Append(meta).AppendLine(text);
                // Kinder eines Bausteins (sollte nach Normalisieren nicht vorkommen) auf gleicher Ebene sichern.
                foreach (Baustein k in b.Children) Schreiben(k, tiefe);
            }
        }

        foreach (Baustein w in wurzeln) Schreiben(w, 0);
        return sb.ToString();
    }

    /// <summary>Speichert atomar.</summary>
    public static void Speichern(string pfad, IEnumerable<Baustein> wurzeln)
    {
        string tmp = pfad + ".tmp";
        File.WriteAllText(tmp, Serialisieren(wurzeln), new UTF8Encoding(true));
        File.Move(tmp, pfad, overwrite: true);
    }

    /// <summary>Fortlaufende Nummern in Anzeigereihenfolge (Tiefensuche), beginnend bei 0.</summary>
    public static void Nummerieren(IEnumerable<Baustein> wurzeln)
    {
        int n = 0;
        foreach (Baustein b in Alle(wurzeln))
        {
            b.Nr = n++;
            if (b.IstGruppe) b.Anzahl = Nachfahren(b).Count(k => !k.IstGruppe);
        }
    }

    public static IEnumerable<Baustein> Nachfahren(Baustein b)
    {
        foreach (Baustein k in b.Children)
        {
            yield return k;
            foreach (Baustein kk in Nachfahren(k)) yield return kk;
        }
    }

    public static IEnumerable<Baustein> Alle(IEnumerable<Baustein> wurzeln) =>
        wurzeln.SelectMany(w => new[] { w }.Concat(Nachfahren(w)));

    /// <summary>
    /// Nach dem Ziehen im Baum: Kinder, die auf einen Baustein (keine Gruppe) gezogen wurden, werden hinter ihm einsortiert.
    /// </summary>
    public static void Normalisieren(ObservableCollection<Baustein> liste)
    {
        for (int i = 0; i < liste.Count; i++)
        {
            Baustein b = liste[i];
            if (!b.IstGruppe && b.Children.Count > 0)
            {
                Baustein[] kinder = b.Children.ToArray();
                b.Children.Clear();
                for (int j = 0; j < kinder.Length; j++) liste.Insert(i + 1 + j, kinder[j]);
            }
            Normalisieren(b.Children);
        }
    }

    /// <summary>Liefert die Liste, die <paramref name="ziel"/> enthält, und dessen Index.</summary>
    public static (ObservableCollection<Baustein> Liste, int Index)? Finden(ObservableCollection<Baustein> liste, Baustein ziel)
    {
        int i = liste.IndexOf(ziel);
        if (i >= 0) return (liste, i);
        foreach (Baustein b in liste)
            if (Finden(b.Children, ziel) is { } treffer) return treffer;
        return null;
    }

    /// <summary>Normalisiert ein Kürzel (ohne „#“, ohne Leerzeichen); liefert null, wenn es ungültig ist.</summary>
    public static string? KuerzelNormalisieren(string? kuerzel)
    {
        string k = (kuerzel ?? string.Empty).Trim().TrimStart('#');
        if (k.Length == 0) return string.Empty;
        return GueltigesKuerzel.IsMatch(k) ? k : null;
    }

    static Baustein BausteinAus(string zeile)
    {
        var b = new Baustein();
        Match m = Meta.Match(zeile);
        if (m.Success && MetaLesen(m.Groups["inhalt"].Value, b))
            zeile = zeile[m.Length..];
        else
        {
            b.Kuerzel = string.Empty;
            b.PunkteVon = b.PunkteBis = null;
        }
        b.Text = zeile.Trim();
        return b;
    }

    /// <summary>Liest „#kürzel 10-18“; false, wenn ein Bestandteil nicht passt (dann gehört die Klammer zum Text).</summary>
    static bool MetaLesen(string inhalt, Baustein b)
    {
        foreach (string token in inhalt.Split(new[] { ' ', ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (KuerzelMuster.Match(token) is { Success: true } k)
            {
                b.Kuerzel = k.Groups["k"].Value;
                continue;
            }
            if (BereichMuster.Match(token) is { Success: true } r)
            {
                int von = int.Parse(r.Groups["von"].Value);
                int bis = r.Groups["bis"].Success ? int.Parse(r.Groups["bis"].Value) : von;
                if (von > bis) (von, bis) = (bis, von);
                if (bis > Notenskala.Maximum) return false;
                b.PunkteVon = von;
                b.PunkteBis = bis;
                continue;
            }
            return false;
        }
        return true;
    }

    static string MetaText(Baustein b)
    {
        var teile = new List<string>();
        if (b.Kuerzel.Length > 0) teile.Add("#" + b.Kuerzel);
        if (b.HatBereich) teile.Add(b.Von == b.Bis ? $"{b.Von}" : $"{b.Von}-{b.Bis}");
        return teile.Count == 0 ? string.Empty : $"[{string.Join(' ', teile)}] ";
    }

    static int Tiefe(string zeile)
    {
        int spalten = 0;
        foreach (char c in zeile)
        {
            if (c == '\t') spalten += 4;
            else if (c == ' ') spalten++;
            else break;
        }
        return (spalten + 2) / 4;
    }

    static string Einzeilig(string s) => s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>Liest eine Textdatei mit Erkennung von UTF-8/UTF-16 bzw. ersatzweise Windows-1252.</summary>
    public static string TextLesen(string pfad)
    {
        byte[] b;
        using (var fs = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var ms = new MemoryStream())
        {
            fs.CopyTo(ms);
            b = ms.ToArray();
        }
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(b);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(b);
        }
    }
}
