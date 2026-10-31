using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Gutachten2025.Services;

/// <summary>
/// Minimales RTF für den Gutachtenentwurf: nur Text, Absätze, Fett und Kursiv.
/// Liest RTF des Windows-RichEditBox (Entwürfe aus entwurf.rtf und dem Entwurfarchiv) und schreibt RTF,
/// das RichEditBox, Word und TextEdit öffnen. Damit tauschen Windows-, Mac- und Browser-Fassung Entwürfe aus.
/// </summary>
public static class RtfText
{
    /// <summary>Gruppen, deren Inhalt kein Fließtext ist.</summary>
    static readonly HashSet<string> Ueberspringen = new(StringComparer.Ordinal)
    {
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "object", "header", "footer", "headerl", "headerr",
        "footerl", "footerr", "footnote", "listtable", "listoverridetable", "rsidtbl", "generator", "xmlnstbl",
        "themedata", "colorschememapping", "latentstyles", "datastore", "fldinst", "filetbl", "revtbl", "mmathPr",
    };

    sealed record Zustand(bool Fett, bool Kursiv, bool Weg, int Uc);

    /// <summary>Text mit Fett/Kursiv; Absätze als „\n“. Kein RTF (z. B. reiner Text) wird unverändert übernommen.</summary>
    public static FormatierterText Lesen(string? rtf)
    {
        if (string.IsNullOrEmpty(rtf)) return FormatierterText.Unformatiert(string.Empty);
        if (!rtf.TrimStart().StartsWith(@"{\rtf", StringComparison.Ordinal))
            return FormatierterText.Unformatiert(rtf.Replace("\r\n", "\n").Replace('\r', '\n'));

        var text = new StringBuilder();
        var fett = new List<bool>();
        var kursiv = new List<bool>();
        var stapel = new Stack<Zustand>();
        var z = new Zustand(false, false, false, 1);
        int auslassen = 0;          // nach \uN zu überspringende Ersatzzeichen
        var bytes = new List<byte>(); // \'hh-Folgen (Codepage 1252)
        Encoding cp = Codepage(1252);

        void Zeichen(char c)
        {
            if (z.Weg) return;
            text.Append(c);
            fett.Add(z.Fett);
            kursiv.Add(z.Kursiv);
        }

        void BytesAusgeben()
        {
            if (bytes.Count == 0) return;
            foreach (char c in cp.GetString(bytes.ToArray())) Zeichen(c);
            bytes.Clear();
        }

        int i = 0;
        while (i < rtf.Length)
        {
            char c = rtf[i];
            if (c == '\\' && i + 1 < rtf.Length && rtf[i + 1] == '\'' && i + 3 < rtf.Length)
            {
                if (auslassen > 0) { auslassen--; i += 4; continue; }
                if (byte.TryParse(rtf.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b)) bytes.Add(b);
                i += 4;
                continue;
            }
            BytesAusgeben();
            switch (c)
            {
                case '{':
                    stapel.Push(z);
                    i++;
                    // {\*\ziel …}: unbekannte Zielgruppe überspringen
                    if (string.CompareOrdinal(rtf, i, @"\*", 0, 2) == 0) z = z with { Weg = true };
                    break;
                case '}':
                    z = stapel.Count > 0 ? stapel.Pop() : z;
                    auslassen = 0;
                    i++;
                    break;
                case '\r':
                case '\n':
                    i++;
                    break;
                case '\\':
                    i++;
                    if (i >= rtf.Length) break;
                    char n = rtf[i];
                    if (!char.IsAsciiLetter(n))
                    {
                        // Steuerzeichen: \\ \{ \} \~ \- \_ \tab usw.
                        i++;
                        if (auslassen > 0) { auslassen--; break; }
                        switch (n)
                        {
                            case '\\': case '{': case '}': Zeichen(n); break;
                            case '~': Zeichen('\u00A0'); break;
                            case '_': Zeichen('\u2011'); break;
                            case '\r': case '\n': Zeichen('\n'); break;
                        }
                        break;
                    }
                    int s = i;
                    while (i < rtf.Length && char.IsAsciiLetter(rtf[i])) i++;
                    string wort = rtf[s..i];
                    int? zahl = null;
                    int zs = i;
                    if (i < rtf.Length && (rtf[i] == '-' || char.IsAsciiDigit(rtf[i])))
                    {
                        i++;
                        while (i < rtf.Length && char.IsAsciiDigit(rtf[i])) i++;
                        if (int.TryParse(rtf.AsSpan(zs, i - zs), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int w)) zahl = w;
                    }
                    if (i < rtf.Length && rtf[i] == ' ') i++;
                    Steuerwort(wort, zahl);
                    break;
                default:
                    i++;
                    if (auslassen > 0) { auslassen--; break; }
                    Zeichen(c);
                    break;
            }
        }
        BytesAusgeben();

        void Steuerwort(string wort, int? zahl)
        {
            bool an = zahl != 0;
            switch (wort)
            {
                case "b": z = z with { Fett = an }; break;
                case "i": z = z with { Kursiv = an }; break;
                case "plain": z = z with { Fett = false, Kursiv = false }; break;
                case "par": case "line": case "sect": case "page": Zeichen('\n'); break;
                case "tab": Zeichen('\t'); break;
                case "emdash": Zeichen('\u2014'); break;
                case "endash": Zeichen('\u2013'); break;
                case "lquote": Zeichen('\u2018'); break;
                case "rquote": Zeichen('\u2019'); break;
                case "ldblquote": Zeichen('\u201C'); break;
                case "rdblquote": Zeichen('\u201D'); break;
                case "bullet": Zeichen('\u2022'); break;
                case "uc": z = z with { Uc = Math.Max(0, zahl ?? 1) }; break;
                case "u":
                    if (zahl is int u)
                    {
                        Zeichen((char)(u < 0 ? u + 65536 : u));
                        auslassen = z.Uc;
                    }
                    break;
                case "ansicpg":
                    if (zahl is int p) cp = Codepage(p);
                    break;
                default:
                    if (Ueberspringen.Contains(wort)) z = z with { Weg = true };
                    break;
            }
        }

        // RichEditBox schließt das Dokument mit einem leeren Absatz ab.
        int laenge = text.Length;
        while (laenge > 0 && text[laenge - 1] == '\n') laenge--;
        return new FormatierterText(text.ToString(0, laenge), fett.GetRange(0, laenge).ToArray(), kursiv.GetRange(0, laenge).ToArray());
    }

    /// <summary>RTF aus Text mit Fett/Kursiv („\r“, „\n“ oder „\r\n“ als Absatzende).</summary>
    public static string Schreiben(FormatierterText f, string schrift = "Segoe UI", int punkt = 11)
    {
        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0\uc1{\fonttbl{\f0\fnil ").Append(Maskieren(schrift)).Append(";}}");
        sb.Append(@"\pard\f0\fs").Append(punkt * 2).Append(' ');
        bool fett = false, kursiv = false;
        string t = f.Text;
        for (int i = 0; i < t.Length; i++)
        {
            bool fi = i < f.Fett.Length && f.Fett[i], ki = i < f.Kursiv.Length && f.Kursiv[i];
            if (fi != fett) { sb.Append(fi ? @"\b " : @"\b0 "); fett = fi; }
            if (ki != kursiv) { sb.Append(ki ? @"\i " : @"\i0 "); kursiv = ki; }
            char c = t[i];
            if (c == '\r' && i + 1 < t.Length && t[i + 1] == '\n') continue;
            sb.Append(c switch
            {
                '\r' or '\n' => "\\par\r\n",
                '\t' => @"\tab ",
                '\\' => @"\\",
                '{' => @"\{",
                '}' => @"\}",
                _ when c < 0x80 => c.ToString(),
                _ => $@"\u{(c > 32767 ? c - 65536 : c)}?",
            });
        }
        sb.Append(@"\par}");
        return sb.ToString();
    }

    static string Maskieren(string s) => s.Replace("\\", "").Replace("{", "").Replace("}", "").Replace(";", "");

    static Encoding Codepage(int nummer)
    {
        try
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(nummer) ?? Encoding.Latin1;
        }
        catch (Exception)
        {
            return Encoding.Latin1;
        }
    }
}
