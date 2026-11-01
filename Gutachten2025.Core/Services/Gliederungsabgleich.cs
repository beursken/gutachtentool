using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Gutachten2025.Services;

/// <summary>Findet zu einer Überschrift der Lösung die passende Zeile des Rohpunkteschemas.</summary>
public static class Gliederungsabgleich
{
    static readonly Regex Nummer = new(@"^\s*((?:[A-Z]|[IVXLC]+|\d+|[a-z]{1,2})[.)]|\(\d+\)|\([a-z]{1,2}\))(\s|$)", RegexOptions.CultureInvariant);
    static readonly Regex Wort = new(@"[\p{L}\d§]+", RegexOptions.CultureInvariant);

    /// <summary>Index des besten Treffers in <paramref name="titel"/> oder -1 (zu unähnlich).</summary>
    public static int Finden(string ueberschrift, IReadOnlyList<string> titel)
    {
        string u = Normal(ueberschrift);
        if (u.Length == 0) return -1;
        string? nr = Gliederungsnummer(ueberschrift);
        HashSet<string> uw = Woerter(ueberschrift);
        int beste = -1;
        double bestwert = 0;
        for (int i = 0; i < titel.Count; i++)
        {
            string t = Normal(titel[i]);
            if (t.Length == 0) continue;
            double wert;
            if (t == u) wert = 10;
            else
            {
                HashSet<string> tw = Woerter(titel[i]);
                int gemeinsam = uw.Count(tw.Contains);
                wert = uw.Count == 0 || tw.Count == 0 ? 0 : (double)gemeinsam / Math.Max(uw.Count, tw.Count);
                if (t.StartsWith(u, StringComparison.Ordinal) || u.StartsWith(t, StringComparison.Ordinal)) wert += 0.5;
                if (nr is not null && nr == Gliederungsnummer(titel[i])) wert += 0.25;
            }
            if (wert > bestwert)
            {
                bestwert = wert;
                beste = i;
            }
        }
        return bestwert >= 0.45 ? beste : -1;
    }

    static string? Gliederungsnummer(string s)
    {
        Match m = Nummer.Match(s);
        return m.Success ? m.Groups[1].Value : null;
    }

    static HashSet<string> Woerter(string s) =>
        Wort.Matches(Nummer.Replace(s, string.Empty)).Select(m => m.Value.ToLowerInvariant()).Where(w => w.Length >= 3 || w == "§").ToHashSet();

    static string Normal(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.Normalize(NormalizationForm.FormC))
            if (char.IsLetterOrDigit(c) || c == '§') sb.Append(char.ToLower(c, CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}

/// <summary>Wörter aus Sachverhalt und Lösung (z. B. Namen der Beteiligten) für die Rechtschreibprüfung.</summary>
public static class Unterlagenwoerter
{
    static readonly Regex Wort = new(@"(?<![\p{L}\d])[\p{L}][\p{L}\-’']{1,39}(?![\p{L}\d])", RegexOptions.CultureInvariant);

    public static List<string> Aus(params string[] texte)
    {
        var w = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string t in texte)
            foreach (Match m in Wort.Matches(t ?? string.Empty))
            {
                string s = m.Value.Trim('-', '’', '\'');
                if (s.Length >= 2) w.Add(s);
            }
        return w.Take(20000).ToList();
    }
}
