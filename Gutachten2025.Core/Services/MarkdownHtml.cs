using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Gutachten2025.Services;

/// <summary>
/// Kleine, sichere Markdown-Darstellung für Sachverhalt und Lösung (Seitenleiste).
/// Unterstützt Überschriften (# und unterstrichen), Absätze, Zeilenumbrüche, Listen (verschachtelt, nummeriert),
/// Zitate, Tabellen, Code, Trennlinien, **fett**, *kursiv*, ~~durchgestrichen~~ und Verweise (nur http/https/mailto).
/// HTML im Text wird nie übernommen, sondern als Text angezeigt; Bilder erscheinen nur als Alternativtext.
/// </summary>
public static class MarkdownHtml
{
    /// <summary>
    /// HTML-Ausschnitt (ohne &lt;html&gt;/&lt;body&gt;), zugleich wohlgeformtes XML (leere Elemente als &lt;br/&gt;),
    /// damit die Windows-Fassung ihn ohne Browser-Komponente in eigene Steuerelemente übertragen kann.
    /// </summary>
    public static string Umwandeln(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        string text = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").TrimStart('\uFEFF');
        text = Steuerzeichen.Replace(text, string.Empty); // in XML unzulässig (z. B. Seitenvorschub aus PDF-Umwandlungen)
        var html = new StringBuilder(text.Length * 2);
        Bloecke(text.Split('\n').ToList(), html, eng: false);
        return html.ToString();
    }

    static readonly Regex Steuerzeichen = new("[\u0000-\u0008\u000B\u000C\u000E-\u001F\uFFFE\uFFFF]", RegexOptions.CultureInvariant);

    // ---------- Blöcke ----------

    static readonly Regex Ueberschrift = new(@"^ {0,3}(#{1,6})(?:[ ]+(.*?))?(?:[ ]+#+)?[ ]*$", RegexOptions.CultureInvariant);
    static readonly Regex Trennlinie = new(@"^ {0,3}(?:(?:\*[ ]*){3,}|(?:-[ ]*){3,}|(?:_[ ]*){3,})$", RegexOptions.CultureInvariant);
    static readonly Regex Zaun = new(@"^ {0,3}(`{3,}|~{3,})", RegexOptions.CultureInvariant);
    static readonly Regex Zitat = new(@"^ {0,3}> ?", RegexOptions.CultureInvariant);
    static readonly Regex Listenpunkt = new(@"^( {0,12})([-*+]|(\d{1,9})[.)])( +|$)(.*)$", RegexOptions.CultureInvariant);
    static readonly Regex Unterstrichen = new(@"^ {0,3}(=+|-+)[ ]*$", RegexOptions.CultureInvariant);
    static readonly Regex Tabellentrenner = new(@"^ *\|? *:?-+:? *(\| *:?-+:? *)*\|? *$", RegexOptions.CultureInvariant);

    static bool Leer(string z) => z.Trim().Length == 0;

    static int Einzug(string z)
    {
        int n = 0;
        while (n < z.Length && z[n] == ' ') n++;
        return n;
    }

    /// <summary>Beginnt die Zeile einen neuen Block, der einen Absatz unterbricht?</summary>
    static bool Unterbricht(string z)
    {
        if (Ueberschrift.IsMatch(z) || Trennlinie.IsMatch(z) || Zaun.IsMatch(z) || Zitat.IsMatch(z)) return true;
        Match m = Listenpunkt.Match(z);
        // Wie CommonMark: nummerierte Listen unterbrechen nur mit „1.“, Aufzählungen nur mit Inhalt
        // (sonst würde z. B. eine umbrochene Zeile „2. Alt. …“ zur Liste).
        return m.Success && m.Groups[1].Length <= 3 && m.Groups[5].Value.Trim().Length > 0
               && (!m.Groups[3].Success || m.Groups[3].Value == "1");
    }

    static void Bloecke(List<string> zeilen, StringBuilder html, bool eng)
    {
        int i = 0;
        while (i < zeilen.Count)
        {
            string z = zeilen[i];
            if (Leer(z)) { i++; continue; }

            // Code mit Zaun
            Match zaun = Zaun.Match(z);
            if (zaun.Success)
            {
                string marke = zaun.Groups[1].Value;
                var code = new List<string>();
                i++;
                while (i < zeilen.Count && !zeilen[i].TrimStart().StartsWith(marke, StringComparison.Ordinal))
                    code.Add(zeilen[i++]);
                i++; // schließender Zaun (falls vorhanden)
                html.Append("<pre><code>").Append(Esc(string.Join("\n", code))).Append("</code></pre>\n");
                continue;
            }

            Match u = Ueberschrift.Match(z);
            if (u.Success)
            {
                int ebene = u.Groups[1].Length;
                html.Append("<h").Append(ebene).Append('>').Append(Inline(u.Groups[2].Value)).Append("</h").Append(ebene).Append(">\n");
                i++;
                continue;
            }

            if (Trennlinie.IsMatch(z))
            {
                html.Append("<hr/>\n");
                i++;
                continue;
            }

            if (Zitat.IsMatch(z))
            {
                var innen = new List<string>();
                while (i < zeilen.Count && !Leer(zeilen[i]))
                {
                    string w = zeilen[i];
                    Match q = Zitat.Match(w);
                    if (q.Success) innen.Add(w[q.Length..]);
                    else if (innen.Count > 0 && !Unterbricht(w)) innen.Add(w); // Fortsetzungszeile
                    else break;
                    i++;
                }
                html.Append("<blockquote>\n");
                Bloecke(innen, html, eng: false);
                html.Append("</blockquote>\n");
                continue;
            }

            Match l = Listenpunkt.Match(z);
            if (l.Success)
            {
                i = Liste(zeilen, i, html);
                continue;
            }

            if (i + 1 < zeilen.Count && z.Contains('|') && Tabellentrenner.IsMatch(zeilen[i + 1]) && zeilen[i + 1].Contains('-'))
            {
                List<string> kopf = Zellen(z);
                List<string> trenner = Zellen(zeilen[i + 1]);
                if (kopf.Count == trenner.Count || trenner.Count > 1)
                {
                    i = Tabelle(zeilen, i, kopf, trenner, html);
                    continue;
                }
            }

            // Absatz (ggf. unterstrichene Überschrift)
            var absatz = new List<string> { z.TrimStart() };
            i++;
            while (i < zeilen.Count && !Leer(zeilen[i]))
            {
                string w = zeilen[i];
                Match setext = Unterstrichen.Match(w);
                if (setext.Success)
                {
                    int ebene = setext.Groups[1].Value[0] == '=' ? 1 : 2;
                    html.Append("<h").Append(ebene).Append('>').Append(Inline(string.Join("\n", absatz).Trim())).Append("</h").Append(ebene).Append(">\n");
                    absatz.Clear();
                    i++;
                    break;
                }
                if (Unterbricht(w)) break;
                absatz.Add(w.TrimStart());
                i++;
            }
            if (absatz.Count == 0) continue;
            string inhalt = Inline(string.Join("\n", absatz).TrimEnd());
            if (eng) html.Append(inhalt).Append('\n');
            else html.Append("<p>").Append(inhalt).Append("</p>\n");
        }
    }

    /// <summary>Liste ab Zeile <paramref name="start"/>; Rückgabe: erste Zeile danach.</summary>
    static int Liste(List<string> zeilen, int start, StringBuilder html)
    {
        Match erste = Listenpunkt.Match(zeilen[start]);
        bool nummeriert = erste.Groups[3].Success;
        char art = nummeriert ? erste.Groups[2].Value[^1] : erste.Groups[2].Value[0];
        int basis = erste.Groups[1].Length;

        var punkte = new List<List<string>>();
        bool locker = false;
        int i = start;
        while (i < zeilen.Count)
        {
            Match m = Listenpunkt.Match(zeilen[i]);
            if (!m.Success || m.Groups[1].Length > basis + 3) break;
            bool gleicheArt = m.Groups[3].Success == nummeriert
                              && (nummeriert ? m.Groups[2].Value[^1] : m.Groups[2].Value[0]) == art;
            if (!gleicheArt) break;

            // Inhalt beginnt nach Marke und Leerraum (bei mehr als vier Leerzeichen zählt nur eines).
            int abstand = m.Groups[4].Length is >= 1 and <= 4 ? m.Groups[4].Length : 1;
            int inhaltEinzug = m.Groups[1].Length + m.Groups[2].Length + abstand;
            var punkt = new List<string> { m.Groups[5].Value };
            i++;
            bool vorherLeer = false;
            while (i < zeilen.Count)
            {
                string w = zeilen[i];
                if (Leer(w))
                {
                    punkt.Add(string.Empty);
                    vorherLeer = true;
                    i++;
                    continue;
                }
                int einzug = Einzug(w);
                if (einzug >= inhaltEinzug)
                {
                    punkt.Add(w[inhaltEinzug..]);
                }
                else if (einzug > basis && Listenpunkt.IsMatch(w))
                {
                    punkt.Add(w[Math.Min(einzug, inhaltEinzug)..]); // eingerückte Unterliste mit knappem Einzug
                }
                else if (!vorherLeer && !Unterbricht(w) && !Listenpunkt.IsMatch(w))
                {
                    punkt.Add(w.TrimStart()); // Fortsetzung des Absatzes ohne Einzug
                }
                else break;
                if (vorherLeer) locker = true;
                vorherLeer = false;
                i++;
            }
            while (punkt.Count > 0 && Leer(punkt[^1])) punkt.RemoveAt(punkt.Count - 1);
            punkte.Add(punkt);

            // Leerzeile zwischen zwei Punkten macht die Liste „locker“ (Absätze statt enger Zeilen).
            if (i < zeilen.Count && i > 0 && Leer(zeilen[i - 1]) && Listenpunkt.IsMatch(zeilen[i])) locker = true;
        }

        if (nummeriert)
        {
            int beginn = int.TryParse(erste.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 1;
            html.Append(beginn == 1 ? "<ol>\n" : $"<ol start=\"{beginn.ToString(CultureInfo.InvariantCulture)}\">\n");
        }
        else html.Append("<ul>\n");
        foreach (List<string> punkt in punkte)
        {
            html.Append("<li>");
            Bloecke(punkt, html, eng: !locker);
            html.Append("</li>\n");
        }
        html.Append(nummeriert ? "</ol>\n" : "</ul>\n");
        return i;
    }

    static int Tabelle(List<string> zeilen, int start, List<string> kopf, List<string> trenner, StringBuilder html)
    {
        string[] ausrichtung = trenner.Select(t =>
        {
            t = t.Trim();
            bool links = t.StartsWith(':'), rechts = t.EndsWith(':');
            return links && rechts ? "center" : rechts ? "right" : links ? "left" : string.Empty;
        }).ToArray();
        int spalten = kopf.Count;

        void Zeile(List<string> zellen, string tag)
        {
            html.Append("<tr>");
            for (int s = 0; s < spalten; s++)
            {
                string a = s < ausrichtung.Length && ausrichtung[s].Length > 0 ? $" style=\"text-align:{ausrichtung[s]}\"" : string.Empty;
                html.Append('<').Append(tag).Append(a).Append('>')
                    .Append(s < zellen.Count ? Inline(zellen[s].Trim()) : string.Empty)
                    .Append("</").Append(tag).Append('>');
            }
            html.Append("</tr>\n");
        }

        html.Append("<table>\n<thead>\n");
        Zeile(kopf, "th");
        html.Append("</thead>\n<tbody>\n");
        int i = start + 2;
        while (i < zeilen.Count && !Leer(zeilen[i]) && zeilen[i].Contains('|'))
            Zeile(Zellen(zeilen[i++]), "td");
        html.Append("</tbody>\n</table>\n");
        return i;
    }

    /// <summary>Zellen einer Tabellenzeile (Trenner „|“, „\|“ bleibt Text).</summary>
    static List<string> Zellen(string zeile)
    {
        string z = zeile.Trim();
        if (z.StartsWith('|')) z = z[1..];
        if (z.EndsWith('|') && !z.EndsWith("\\|", StringComparison.Ordinal)) z = z[..^1];
        var zellen = new List<string>();
        var aktuell = new StringBuilder();
        for (int k = 0; k < z.Length; k++)
        {
            if (z[k] == '\\' && k + 1 < z.Length && z[k + 1] == '|') { aktuell.Append('|'); k++; }
            else if (z[k] == '|') { zellen.Add(aktuell.ToString()); aktuell.Clear(); }
            else aktuell.Append(z[k]);
        }
        zellen.Add(aktuell.ToString());
        return zellen;
    }

    // ---------- Inline ----------

    const char PlatzAuf = '\uE000', PlatzZu = '\uE001';
    static readonly Regex Platz = new("\uE000(\\d+)\uE001", RegexOptions.CultureInvariant);
    static readonly Regex Autolink = new(@"\G<((?:https?://|mailto:)[^\s<>]+)>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    static readonly Regex FettKursiv = new(@"\*\*\*(?=\S)(.+?)(?<=\S)\*\*\*", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    static readonly Regex FettStern = new(@"\*\*(?=\S)(.+?)(?<=\S)\*\*", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    static readonly Regex FettStrich = new(@"(?<![\p{L}\p{N}_])__(?=\S)(.+?)(?<=\S)__(?![\p{L}\p{N}_])", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    static readonly Regex KursivStern = new(@"\*(?=[^\s*])(.+?)(?<=[^\s*])\*", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    static readonly Regex KursivStrich = new(@"(?<![\p{L}\p{N}_])_(?=[^\s_])(.+?)(?<=[^\s_])_(?![\p{L}\p{N}_])", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    static readonly Regex Durch = new(@"~~(?=\S)(.+?)(?<=\S)~~", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    static readonly Regex HarterUmbruch = new(@" {2,}\n", RegexOptions.CultureInvariant);

    static string Inline(string text)
    {
        var platz = new List<string>();
        string P(string h)
        {
            platz.Add(h);
            return PlatzAuf + (platz.Count - 1).ToString(CultureInfo.InvariantCulture) + PlatzZu;
        }

        text = text.Replace(PlatzAuf, ' ').Replace(PlatzZu, ' ');
        var sb = new StringBuilder(text.Length + 16);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                char n = text[i + 1];
                if (n == '\n') { sb.Append(P("<br/>")).Append('\n'); i++; continue; }
                if (char.IsAsciiLetterOrDigit(n) || n > 127 || char.IsWhiteSpace(n)) { sb.Append(c); continue; }
                sb.Append(P(Esc(n.ToString()))); i++;
                continue;
            }
            if (c == '`')
            {
                int lauf = 1;
                while (i + lauf < text.Length && text[i + lauf] == '`') lauf++;
                string marke = new('`', lauf);
                int ende = SucheLauf(text, marke, i + lauf);
                if (ende >= 0)
                {
                    string code = text[(i + lauf)..ende].Replace('\n', ' ');
                    if (code.Length > 2 && code[0] == ' ' && code[^1] == ' ' && code.Trim().Length > 0) code = code[1..^1];
                    sb.Append(P("<code>" + Esc(code) + "</code>"));
                    i = ende + lauf - 1;
                }
                else
                {
                    sb.Append(marke);
                    i += lauf - 1;
                }
                continue;
            }
            if ((c == '[' || (c == '!' && i + 1 < text.Length && text[i + 1] == '['))
                && Verweis(text, c == '!' ? i + 1 : i, out int bis, out string beschriftung, out string ziel))
            {
                if (c == '!')
                    sb.Append(P(Esc(beschriftung))); // keine Bilder nachladen
                else if (ZielErlaubt(ziel))
                    sb.Append(P($"<a href=\"{Esc(ziel)}\" target=\"_blank\" rel=\"noopener noreferrer\">{Inline(beschriftung)}</a>"));
                else
                    sb.Append(P(Inline(beschriftung)));
                i = bis;
                continue;
            }
            if (c == '<')
            {
                Match a = Autolink.Match(text, i);
                if (a.Success)
                {
                    string adresse = a.Groups[1].Value;
                    sb.Append(P($"<a href=\"{Esc(adresse)}\" target=\"_blank\" rel=\"noopener noreferrer\">{Esc(adresse)}</a>"));
                    i += a.Length - 1;
                    continue;
                }
            }
            sb.Append(c);
        }

        string s = Esc(sb.ToString());
        s = FettKursiv.Replace(s, "<strong><em>$1</em></strong>");
        s = FettStern.Replace(s, "<strong>$1</strong>");
        s = FettStrich.Replace(s, "<strong>$1</strong>");
        s = KursivStern.Replace(s, "<em>$1</em>");
        s = KursivStrich.Replace(s, "<em>$1</em>");
        s = Durch.Replace(s, "<del>$1</del>");
        s = HarterUmbruch.Replace(s, "<br/>\n");
        s = Ausgleichen(s);
        return Platz.Replace(s, m => platz[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]);
    }

    static readonly Regex Auszeichnung = new("<(/?)(strong|em|del)>", RegexOptions.CultureInvariant);

    /// <summary>Schachtelt fett/kursiv/durchgestrichen korrekt (überlappende Markierungen werden geschlossen und neu geöffnet).</summary>
    static string Ausgleichen(string s)
    {
        var offen = new List<string>();
        var sb = new StringBuilder(s.Length + 16);
        int pos = 0;
        foreach (Match m in Auszeichnung.Matches(s))
        {
            sb.Append(s, pos, m.Index - pos);
            pos = m.Index + m.Length;
            string tag = m.Groups[2].Value;
            if (m.Groups[1].Length == 0)
            {
                offen.Add(tag);
                sb.Append('<').Append(tag).Append('>');
                continue;
            }
            int k = offen.LastIndexOf(tag);
            if (k < 0) continue; // schließt nichts: entfällt
            for (int j = offen.Count - 1; j > k; j--) sb.Append("</").Append(offen[j]).Append('>');
            sb.Append("</").Append(tag).Append('>');
            List<string> wieder = offen.GetRange(k + 1, offen.Count - k - 1);
            offen.RemoveRange(k, offen.Count - k);
            foreach (string w in wieder)
            {
                sb.Append('<').Append(w).Append('>');
                offen.Add(w);
            }
        }
        sb.Append(s, pos, s.Length - pos);
        for (int j = offen.Count - 1; j >= 0; j--) sb.Append("</").Append(offen[j]).Append('>');
        return sb.ToString();
    }

    /// <summary>Nächster Lauf aus genau <paramref name="marke"/> (gleich viele Backticks) ab <paramref name="ab"/>; -1 = keiner.</summary>
    static int SucheLauf(string text, string marke, int ab)
    {
        int k = ab;
        while ((k = text.IndexOf(marke, k, StringComparison.Ordinal)) >= 0)
        {
            bool davor = k > 0 && text[k - 1] == '`';
            bool danach = k + marke.Length < text.Length && text[k + marke.Length] == '`';
            if (!davor && !danach) return k;
            k++;
        }
        return -1;
    }

    /// <summary>[Beschriftung](Ziel "Titel") ab der öffnenden Klammer; <paramref name="bis"/> = Index der schließenden runden Klammer.</summary>
    static bool Verweis(string text, int start, out int bis, out string beschriftung, out string ziel)
    {
        bis = -1;
        beschriftung = ziel = string.Empty;
        int tiefe = 0, k = start;
        for (; k < text.Length; k++)
        {
            if (text[k] == '\\') { k++; continue; }
            if (text[k] == '[') tiefe++;
            else if (text[k] == ']' && --tiefe == 0) break;
        }
        if (k >= text.Length || k + 1 >= text.Length || text[k + 1] != '(') return false;
        beschriftung = text[(start + 1)..k];

        int j = k + 2;
        while (j < text.Length && text[j] == ' ') j++;
        var z = new StringBuilder();
        if (j < text.Length && text[j] == '<')
        {
            j++;
            while (j < text.Length && text[j] != '>' && text[j] != '\n') z.Append(text[j++]);
            if (j >= text.Length || text[j] != '>') return false;
            j++;
        }
        else
        {
            int klammern = 0;
            while (j < text.Length && !char.IsWhiteSpace(text[j]))
            {
                if (text[j] == '(') klammern++;
                else if (text[j] == ')' && klammern-- == 0) break;
                z.Append(text[j++]);
            }
        }
        while (j < text.Length && text[j] == ' ') j++;
        if (j < text.Length && text[j] is '"' or '\'')
        {
            char q = text[j];
            int e = text.IndexOf(q, j + 1);
            if (e < 0) return false;
            j = e + 1;
            while (j < text.Length && text[j] == ' ') j++;
        }
        if (j >= text.Length || text[j] != ')') return false;
        bis = j;
        ziel = z.ToString();
        return true;
    }

    static bool ZielErlaubt(string ziel) =>
        ziel.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || ziel.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || ziel.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Text so maskieren, dass Markdown ihn wörtlich wiedergibt (Zeichen für Formatierung, Verweise, HTML).</summary>
    public static string Maskieren(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            if (c is '\\' or '*' or '_' or '`' or '[' or ']' or '<' or '~' or '|' or '#') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Nur die für HTML nötigen Zeichen (Umlaute und § bleiben lesbar).</summary>
    static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
            sb.Append(c switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", '\'' => "&#39;", _ => c.ToString() });
        return sb.ToString();
    }
}
