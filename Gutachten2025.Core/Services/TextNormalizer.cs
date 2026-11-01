using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Gutachten2025.Models;

namespace Gutachten2025.Services;

/// <summary>
/// Bereinigt den Gutachtentext für die Ausgabe: Leerzeichen nach Satzzeichen, keine Leerzeichen davor,
/// geschützte Leerzeichen in Normzitaten. Zahlen (1.000, 0,5, 1.1.2026), URLs und E-Mail-Adressen bleiben unberührt.
/// </summary>
public static class TextNormalizer
{
    const char Nbsp = ' ';
    const RegexOptions O = RegexOptions.CultureInvariant;

    static readonly Regex Geschuetzt = new(
        @"https?://\S+|www\.\S+|[\w.+-]+@[\w-]+(?:\.[\w-]+)+|\b[\w-]+(?:\.[\w-]+)*\.(?:de|com|org|net|eu|info|at|ch|int)\b",
        O | RegexOptions.IgnoreCase);
    static readonly Regex Platzhalter = new("(\\d+)", O);

    static readonly Regex Auslassung = new(@"\.{3,}", O);
    static readonly Regex NachKomma = new(@"([,;])(?=[^\s\d)\]])", O);
    static readonly Regex NachSatzende = new(@"(?<=[.!?:])(?=[\p{L}§(„])", O);
    static readonly Regex VorSatzzeichen = new(@"[  ]+(?=[.,;:!?)\]])", O);
    static readonly Regex NachKlammer = new(@"([(\[])[  ]+", O);
    static readonly Regex Mehrfach = new(@" {2,}", O);

    static readonly Regex Paragraph = new(@"(§§?)[  ]*(?=\d)", O);
    static readonly Regex Abkuerzung = new(@"\b(Art|Artt|Abs|UAbs|Nr|Rn|Rdnr|Var|Alt|Hs|Halbs|Bd|Aufl|Kap|Anh|S)\.[  ]*(?=\d)", O);
    static readonly Regex Litera = new(@"\blit\.[  ]*(?=[a-z]\b)", O);
    static readonly Regex Ausgeschrieben = new(@"\b(Satz|Absatz|Nummer|Artikel)[ ]+(?=\d)", O);
    static readonly Regex Gesetz = new(@"(?<=\d)[ ]+(?=[A-ZÄÖÜ]\p{L}*[A-ZÄÖÜ]\b)", O);
    static readonly Regex Folgende = new(@"(?<=\d)[ ]+(?=ff?\.)", O);

    /// <summary>Liefert die bereinigten, nichtleeren Absätze.</summary>
    public static IReadOnlyList<string> Absaetze(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return Normalisieren(text).Split('\n').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
    }

    /// <summary>Bereinigter Gesamttext; Absätze durch '\n' getrennt.</summary>
    static string Normalisieren(string text)
    {
        string t = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');

        var schutz = new List<string>();
        t = Geschuetzt.Replace(t, m => { schutz.Add(m.Value); return $"{schutz.Count - 1}"; });

        t = Auslassung.Replace(t, "…");
        t = NachKomma.Replace(t, "$1 ");
        t = NachSatzende.Replace(t, " ");
        t = VorSatzzeichen.Replace(t, string.Empty);
        t = NachKlammer.Replace(t, "$1");
        t = Mehrfach.Replace(t, " ");

        t = Paragraph.Replace(t, "$1" + Nbsp);
        t = Abkuerzung.Replace(t, "$1." + Nbsp);
        t = Litera.Replace(t, "lit." + Nbsp);
        t = Ausgeschrieben.Replace(t, "$1" + Nbsp);
        t = Gesetz.Replace(t, Nbsp.ToString());
        t = Folgende.Replace(t, Nbsp.ToString());

        t = Platzhalter.Replace(t, m => schutz[int.Parse(m.Groups[1].Value)]);

        return t;
    }

    /// <summary>
    /// Wie <see cref="Absaetze"/>, behält aber Fett/Kursiv: Der bereinigte Text wird Zeichen für Zeichen dem Original
    /// zugeordnet (die Bereinigung fügt nur Leerzeichen ein, entfernt welche, tauscht Leer- gegen geschützte Leerzeichen
    /// und „...“ gegen „…“).
    /// </summary>
    public static List<List<TextLauf>> FormatierteAbsaetze(FormatierterText quelle)
    {
        // Vorverarbeitung wie in Normalisieren (\r\n, \r → \n; Tab → Leerzeichen), Formate mitführen
        var zeichen = new StringBuilder();
        var fett = new List<bool>();
        var kursiv = new List<bool>();
        string q = quelle.Text ?? string.Empty;
        for (int i = 0; i < q.Length; i++)
        {
            char c = q[i];
            if (c == '\r' && i + 1 < q.Length && q[i + 1] == '\n') continue;
            if (c == '\r' || c == '\v') c = '\n';
            if (c == '\t') c = ' ';
            zeichen.Append(c);
            fett.Add(i < quelle.Fett.Length && quelle.Fett[i]);
            kursiv.Add(i < quelle.Kursiv.Length && quelle.Kursiv[i]);
        }
        string vor = zeichen.ToString();
        if (string.IsNullOrWhiteSpace(vor)) return new List<List<TextLauf>>();
        string nach = Normalisieren(vor);

        var f = new bool[nach.Length];
        var k = new bool[nach.Length];
        int o = 0;
        for (int j = 0; j < nach.Length;)
        {
            char c = nach[j];
            bool gleich = o < vor.Length && (vor[o] == c || (c == Nbsp && vor[o] == ' ') || (c == ' ' && vor[o] == Nbsp));
            if (gleich)
            {
                f[j] = fett[o]; k[j] = kursiv[o];
                o++; j++;
            }
            else if (c == '…' && o + 2 < vor.Length && vor[o] == '.' && vor[o + 1] == '.' && vor[o + 2] == '.')
            {
                f[j] = fett[o]; k[j] = kursiv[o];
                while (o < vor.Length && vor[o] == '.') o++;
                j++;
            }
            else if (c is ' ' or Nbsp)
            {
                // eingefügtes Leerzeichen: Format des vorigen Zeichens
                if (o > 0) { f[j] = fett[o - 1]; k[j] = kursiv[o - 1]; }
                j++;
            }
            else if (o < vor.Length)
            {
                o++; // im Original entferntes Zeichen (Leerzeichen)
            }
            else
            {
                j++;
            }
        }

        var gesamt = new FormatierterText(nach, f, k);
        var absaetze = new List<List<TextLauf>>();
        int start = 0;
        for (int i = 0; i <= nach.Length; i++)
        {
            if (i < nach.Length && nach[i] != '\n') continue;
            int a = start, e = i;
            while (a < e && char.IsWhiteSpace(nach[a])) a++;
            while (e > a && char.IsWhiteSpace(nach[e - 1])) e--;
            if (e > a) absaetze.Add(gesamt.Laeufe(a, e - a));
            start = i + 1;
        }
        return absaetze;
    }

    static readonly Regex Gliederungsbegriff = new(
        @"(?<![\p{L}\d])(?:Tatkomplex|Fallfrage|Frage|Aufgabe)\s+(?:\d{1,3}[a-z]?|[IVX]{1,5})(?![\p{L}\d])",
        RegexOptions.CultureInvariant);

    /// <summary>Setzt „Frage 1“, „Aufgabe 2“, „Tatkomplex 3“, „Fallfrage 4“ (auch „Frage 2a“, „Tatkomplex II“) fett.</summary>
    public static List<List<TextLauf>> GliederungFett(List<List<TextLauf>> absaetze)
    {
        var ergebnis = new List<List<TextLauf>>(absaetze.Count);
        foreach (List<TextLauf> absatz in absaetze)
        {
            string text = string.Concat(absatz.Select(l => l.T));
            var treffer = Gliederungsbegriff.Matches(text);
            if (treffer.Count == 0)
            {
                ergebnis.Add(absatz);
                continue;
            }
            var fett = new bool[text.Length];
            var kursiv = new bool[text.Length];
            int pos = 0;
            foreach (TextLauf l in absatz)
            {
                for (int j = 0; j < l.T.Length; j++)
                {
                    fett[pos + j] = l.F;
                    kursiv[pos + j] = l.K;
                }
                pos += l.T.Length;
            }
            foreach (Match m in treffer)
                for (int j = m.Index; j < m.Index + m.Length; j++) fett[j] = true;
            var neu = new List<TextLauf>();
            var sb = new StringBuilder();
            for (int j = 0; j < text.Length; j++)
            {
                if (j > 0 && (fett[j] != fett[j - 1] || kursiv[j] != kursiv[j - 1]))
                {
                    neu.Add(new TextLauf(sb.ToString(), fett[j - 1], kursiv[j - 1]));
                    sb.Clear();
                }
                sb.Append(text[j]);
            }
            if (sb.Length > 0) neu.Add(new TextLauf(sb.ToString(), fett[^1], kursiv[^1]));
            ergebnis.Add(neu);
        }
        return ergebnis;
    }
}
