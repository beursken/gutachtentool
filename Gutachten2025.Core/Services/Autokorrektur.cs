using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Gutachten2025.Services;

/// <summary>
/// Autokorrektur beim Tippen und Diktieren: anpassbare Liste (Vorlagen\Autokorrektur.txt, für alle Klausuren) mit
/// Wortersetzungen („dei => die“, „z.B. => z. B.“) und Diktatregeln vor Zahlen („Absatz # => Abs. #“, Zahlwörter → Ziffern),
/// dazu fest eingebaut „§433“ → „§ 433“ und „GRoß“ → „Groß“ (<see cref="Tippfehler"/>). Ausnahmen je Klausur (Ausnahmen.txt)
/// werden nie berichtigt. Doppelte Wörter („die die“) und Abkürzungen wie „StPO“ bleiben bewusst unberührt.
/// </summary>
public sealed class Autokorrektur
{
    public const string Datei = "Autokorrektur.txt";
    public const string AusnahmenDatei = "Ausnahmen.txt";

    public const string AusnahmenKopf =
        "// Ausnahmen dieser Klausur: je Zeile ein Wort, das nie automatisch berichtigt und in der Rechtschreibprüfung nicht markiert wird.\r\n";

    /// <summary>Ersetzung im übergebenen Text: ab <paramref name="Start"/> werden <paramref name="Laenge"/> Zeichen durch <paramref name="Neu"/> ersetzt.</summary>
    public sealed record Ersetzung(int Start, int Laenge, string Neu, string Alt);

    readonly Dictionary<string, string> _ersetzungen = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _vorZahl = new(StringComparer.Ordinal);
    readonly HashSet<string> _ausnahmen = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Ausnahmen => _ausnahmen;
    public int Anzahl => _ersetzungen.Count + _vorZahl.Count;

    static readonly Regex Normzitat = new(@"^(§§?|Art\.|Abs\.|Nr\.|S\.|lit\.|Rn\.|Alt\.|Var\.|Hs\.|UAbs\.)(\d+[a-z]?)$", RegexOptions.CultureInvariant);
    static readonly Regex Wort = new(@"§§?|\p{L}+\.|[\p{L}\p{N}]+", RegexOptions.CultureInvariant);

    public Autokorrektur() { }

    public Autokorrektur(string liste, IEnumerable<string>? ausnahmen = null)
    {
        Uebernehmen(liste);
        if (ausnahmen is not null) _ausnahmen.UnionWith(ausnahmen);
    }

    void Uebernehmen(string liste)
    {
        foreach (var (falsch, richtig) in Parsen(liste))
        {
            if (falsch.EndsWith(" #", StringComparison.Ordinal))
                _vorZahl[falsch[..^2].Trim()] = richtig.EndsWith(" #", StringComparison.Ordinal) ? richtig[..^2].Trim() : richtig.TrimEnd('#').Trim();
            else if (!falsch.Contains(' ')) _ersetzungen[falsch] = richtig;
        }
    }

    /// <summary>Lädt Liste und Ausnahmen; fehlt die Liste im Vorlagenordner, wird die Programmvorlage angelegt.</summary>
    public static Autokorrektur Laden(string vorlagenOrdner, string? klausurOrdner)
    {
        var a = new Autokorrektur();
        try
        {
            string liste = ListenPfad(vorlagenOrdner);
            if (!File.Exists(liste) && Programmvorlagen.Vorhanden(Datei))
            {
                Directory.CreateDirectory(vorlagenOrdner);
                Programmvorlagen.Schreiben(Datei, liste);
            }
            if (File.Exists(liste)) a.Uebernehmen(Formulierungsdatei.TextLesen(liste));
            string? aus = klausurOrdner is null ? null : Path.Combine(klausurOrdner, AusnahmenDatei);
            if (aus is not null && File.Exists(aus)) a._ausnahmen.UnionWith(Woerter(Formulierungsdatei.TextLesen(aus)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EinstellungsSpeicher.Protokollieren(ex);
        }
        return a;
    }

    public static string ListenPfad(string vorlagenOrdner) => Path.Combine(vorlagenOrdner, Datei);

    /// <summary>„falsch => richtig“ (auch „→“ oder Tabulator); Anmerkungen mit „//“.</summary>
    public static List<(string Falsch, string Richtig)> Parsen(string text)
    {
        var liste = new List<(string, string)>();
        foreach (string roh in text.TrimStart('﻿').Replace("\r\n", "\n").Split('\n'))
        {
            string z = roh.Trim();
            if (z.Length == 0 || z.StartsWith("//", StringComparison.Ordinal)) continue;
            string[] teile = z.Split(new[] { "=>", "→", "\t" }, 2, StringSplitOptions.None);
            if (teile.Length < 2) continue;
            string falsch = teile[0].Trim(), richtig = teile[1].Trim();
            if (falsch.Length == 0 || richtig.Length == 0) continue;
            liste.Add((falsch, richtig));
        }
        return liste;
    }

    static IEnumerable<string> Woerter(string text) =>
        text.TrimStart('﻿').Replace("\r\n", "\n").Split('\n')
            .Select(z => z.Trim())
            .Where(z => z.Length > 0 && !z.StartsWith("//", StringComparison.Ordinal));

    public bool IstAusnahme(string wort) => _ausnahmen.Contains(wort);

    /// <summary>
    /// Vorschlag für den Text vor der Schreibmarke. <paramref name="eingefuegt"/>: Zahl der eben eingefügten Zeichen
    /// (1 beim Tippen, mehr bei Diktat oder Autovervollständigung); berichtigt wird nur, was an die Einfügung grenzt.
    /// Beim Tippen muss das letzte Zeichen ein Wortende sein; ein Diktatabschnitt gilt als abgeschlossen.
    /// </summary>
    public Ersetzung? Vorschlag(string davor, int eingefuegt, ISet<string>? bekannt = null)
    {
        if (davor.Length < 2 || eingefuegt < 1) return null;
        bool wortende = Tippfehler.IstWortende(davor[^1]);
        if (eingefuegt == 1 && !wortende) return null;
        int ende = wortende ? davor.Length - 1 : davor.Length;
        int grenze = Math.Max(0, davor.Length - eingefuegt - 1);
        return Diktat(davor, ende, grenze) ?? Wortkorrektur(davor, ende, bekannt);
    }

    Ersetzung? Wortkorrektur(string davor, int ende, ISet<string>? bekannt)
    {
        var (start, token) = TokenVor(davor, ende);
        if (start < 0) return null;
        bool punkt = ende < davor.Length && davor[ende] == '.';
        if (punkt && Berichtigen(token + ".", bekannt, nurListe: true) is string mitPunkt)
            return new Ersetzung(start, token.Length + 1, mitPunkt, token + ".");
        if (Berichtigen(token, bekannt) is not string neu) return null;
        if (punkt && neu.EndsWith('.') && !token.EndsWith('.')) return null; // „bzgl“ + „.“: Punkt wird gerade getippt
        return new Ersetzung(start, token.Length, neu, token);
    }

    /// <summary>Berichtigung eines abgeschlossenen Wortes (z. B. „z.B.“, „dei“, „§433“, „GRoß“) oder null.</summary>
    public string? Berichtigen(string token, ISet<string>? bekannt = null, bool nurListe = false)
    {
        if (token.Length < 2 || _ausnahmen.Contains(token)) return null;
        if (_ersetzungen.TryGetValue(token, out string? r)) return r;
        // Satzanfang: „Dei“ → „Die“, wenn „dei => die“ in der Liste steht.
        if (char.IsUpper(token[0]) && _ersetzungen.TryGetValue(char.ToLowerInvariant(token[0]) + token[1..], out r) && char.IsLower(r[0]))
            return char.ToUpperInvariant(r[0]) + r[1..];
        if (nurListe) return null;
        Match m = Normzitat.Match(token);
        if (m.Success) return m.Groups[1].Value + " " + m.Groups[2].Value;
        return token.All(char.IsLetter) ? Tippfehler.Berichtigen(token, bekannt) : null;
    }

    /// <summary>Token unmittelbar vor <paramref name="ende"/>: bis zum letzten Leerzeichen bzw. öffnenden Zeichen; Start = -1, wenn leer.</summary>
    public static (int Start, string Token) TokenVor(string text, int ende)
    {
        int a = ende;
        while (a > 0 && !char.IsWhiteSpace(text[a - 1]) && text[a - 1] is not ('(' or '„' or '"' or '[' or '‚' or '«' or '»' or '/' or '“')) a--;
        return a == ende ? (-1, string.Empty) : (a, text[a..ende]);
    }

    // ---------- Diktat: „Paragraph vier dreiunddreißig Absatz eins“ → „§ 433 Abs. 1“ ----------

    Ersetzung? Diktat(string davor, int ende, int grenze)
    {
        if (_vorZahl.Count == 0) return null;
        int von = Math.Max(0, Math.Min(grenze, ende) - 120);
        while (von > 0 && von < ende && !char.IsWhiteSpace(davor[von - 1])) von++;
        if (von > ende) return null;
        var w = new List<(int Start, int Ende, string Text)>();
        foreach (Match m in Wort.Matches(davor[..ende], von))
            if (m.Index >= von) w.Add((m.Index, m.Index + m.Length, m.Value));

        var teile = new List<(int Start, int Ende, string Neu)>();
        for (int i = 0; i < w.Count; i++)
        {
            string kw = w[i].Text;
            string? abk = Schluessel(kw);
            if (abk is null || _ausnahmen.Contains(kw)) continue;
            bool buchstabe = abk == "lit.", anhang = abk is "§" or "§§" or "Art.";
            int j = i + 1;
            var stuecke = new List<(int Start, int Ende, string Neu)>();
            while (j < w.Count && (Luecke(davor, w[j - 1].Ende, w[j].Start) || (stuecke.Count > 0 && davor[w[j - 1].Ende..w[j].Start] is ", " or ",")))
            {
                int k = j;
                string? zahl = Zahlenfolge(w, ref k, davor, buchstabe);
                if (zahl is null) break;
                // „433 a“ (diktiert) → „433a“
                if (anhang && k < w.Count && w[k].Text.Length == 1 && w[k].Text[0] is >= 'a' and <= 'z' && davor[w[k - 1].Ende..w[k].Start] == " ")
                {
                    zahl += w[k].Text;
                    k++;
                }
                stuecke.Add((w[j].Start, w[k - 1].Ende, zahl));
                j = k;
                // Aufzählung: „280 und 281“, „1, 2“, „1 bis 3“
                if (j + 1 < w.Count && w[j].Text is "und" or "oder" or "bis" && Luecke(davor, w[j - 1].Ende, w[j].Start)) { j++; continue; }
                if (j < w.Count && davor[w[j - 1].Ende..w[j].Start].Trim() == ",") continue;
                break;
            }
            if (stuecke.Count == 0) continue;
            int segEnde = stuecke[^1].Ende;
            if (segEnde < grenze) { i = j - 1; continue; }
            var sb = new StringBuilder(abk);
            int pos = w[i].Ende;
            foreach (var s in stuecke)
            {
                string zwischen = davor[pos..s.Start];
                sb.Append(pos == w[i].Ende ? " " : zwischen).Append(s.Neu);
                pos = s.Ende;
            }
            teile.Add((w[i].Start, segEnde, sb.ToString()));
            i = j - 1;
        }
        teile.RemoveAll(t => davor[t.Start..t.Ende] == t.Neu);
        if (teile.Count == 0) return null;
        int a = teile[0].Start, e = teile[^1].Ende;
        var neu = new StringBuilder();
        int p = a;
        foreach (var t in teile)
        {
            neu.Append(davor, p, t.Start - p).Append(t.Neu);
            p = t.Ende;
        }
        return new Ersetzung(a, e - a, neu.ToString(), davor[a..e]);
    }

    string? Schluessel(string wort)
    {
        if (_vorZahl.TryGetValue(wort, out string? a)) return a;
        // Satzanfang bzw. klein diktiert
        string anders = char.IsUpper(wort[0]) ? char.ToLowerInvariant(wort[0]) + wort[1..] : char.ToUpperInvariant(wort[0]) + wort[1..];
        return _vorZahl.TryGetValue(anders, out a) ? a : null;
    }

    static bool Luecke(string text, int a, int b) => b > a && b - a <= 2 && text[a..b].All(c => c == ' ' || c == ' ');

    /// <summary>Folge aus Ziffern und Zahlwörtern ab <paramref name="k"/> („vier dreiunddreißig“ → „433“); null, wenn keine.</summary>
    static string? Zahlenfolge(List<(int Start, int Ende, string Text)> w, ref int k, string text, bool buchstabe)
    {
        if (buchstabe && w[k].Text.Length == 1 && w[k].Text[0] is >= 'a' and <= 'z')
        {
            k++;
            return w[k - 1].Text;
        }
        var teile = new List<string>();
        bool wort = false;
        int start = k;
        while (k < w.Count && (k == start || Luecke(text, w[k - 1].Ende, w[k].Start)))
        {
            string t = w[k].Text;
            bool satzende = false;
            if (t.EndsWith('.'))
            {
                // „Satz zwei.“: Zahlwort am Satzende – der Punkt bleibt stehen.
                if (Zahlwort(t[..^1]) is null) break;
                t = t[..^1];
                w[k] = (w[k].Start, w[k].Ende - 1, t);
                satzende = true;
            }
            if (t.All(char.IsDigit) || (t.Length > 1 && t[..^1].All(char.IsDigit) && char.IsLower(t[^1])))
            {
                if (teile.Count > 0 && !wort) break;
                teile.Add(t);
            }
            else if (Zahlwort(t) is int z)
            {
                teile.Add(z.ToString(System.Globalization.CultureInfo.InvariantCulture));
                wort = true;
            }
            else break;
            k++;
            if (satzende) break;
        }
        if (teile.Count == 0) return null;
        // Reine Ziffernfolgen („280 281“) nicht zusammenziehen.
        if (!wort)
        {
            k = start + 1;
            return teile[0];
        }
        return string.Concat(teile);
    }

    static readonly (string Wort, int Wert)[] Einer =
    {
        ("eins", 1), ("zwei", 2), ("drei", 3), ("vier", 4), ("fünf", 5), ("sechs", 6), ("sieben", 7), ("acht", 8), ("neun", 9),
    };

    static readonly Dictionary<string, int> Besondere = new(StringComparer.Ordinal)
    {
        ["null"] = 0, ["zehn"] = 10, ["elf"] = 11, ["zwölf"] = 12, ["dreizehn"] = 13, ["vierzehn"] = 14, ["fünfzehn"] = 15,
        ["sechzehn"] = 16, ["siebzehn"] = 17, ["achtzehn"] = 18, ["neunzehn"] = 19,
        ["zwanzig"] = 20, ["dreißig"] = 30, ["dreissig"] = 30, ["vierzig"] = 40, ["fünfzig"] = 50, ["sechzig"] = 60, ["siebzig"] = 70,
        ["achtzig"] = 80, ["neunzig"] = 90,
    };

    /// <summary>Deutsches Zahlwort (0 bis 9999) als Zahl; „ein“/„eine“ allein gelten nicht als Zahl.</summary>
    public static int? Zahlwort(string wort)
    {
        string w = wort.ToLowerInvariant();
        if (w is "ein" or "eine" or "einer" or "einem" or "einen" or "eines") return null;
        return Wert(w, true);
    }

    static int? Wert(string w, bool ganz)
    {
        if (w.Length == 0) return null;
        int i = w.IndexOf("tausend", StringComparison.Ordinal);
        if (i >= 0)
        {
            int? links = i == 0 ? 1 : Wert(w[..i], false);
            string r = w[(i + 7)..];
            int? rechts = r.Length == 0 ? 0 : Wert(r, true);
            return links is >= 1 and <= 9 && rechts is int rr && rr < 1000 ? links * 1000 + rr : null;
        }
        i = w.IndexOf("hundert", StringComparison.Ordinal);
        if (i >= 0)
        {
            int? links = i == 0 ? 1 : Wert(w[..i], false);
            string r = w[(i + 7)..];
            int? rechts = r.Length == 0 ? 0 : Wert(r, true);
            return links is >= 1 and <= 9 && rechts is int rr && rr < 100 ? links * 100 + rr : null;
        }
        if (Besondere.TryGetValue(w, out int b)) return b;
        if (w == "ein") return ganz ? null : 1;
        if (w == "eins") return ganz ? 1 : null;
        foreach (var (e, v) in Einer) if (w == e) return v;
        int und = w.IndexOf("und", StringComparison.Ordinal);
        if (und > 0)
        {
            string einer = w[..und], zehner = w[(und + 3)..];
            int? ev = einer == "ein" ? 1 : einer == "eins" ? null : Wert(einer, false);
            return ev is >= 1 and <= 9 && Besondere.TryGetValue(zehner, out int zv) && zv >= 20 ? zv + ev : null;
        }
        return null;
    }

    /// <summary>Ausnahme in die Liste der Klausur aufnehmen (Datei anlegen, falls nötig); false, wenn schon vorhanden.</summary>
    public static bool AusnahmeHinzufuegen(string klausurOrdner, string wort)
    {
        string pfad = Path.Combine(klausurOrdner, AusnahmenDatei);
        if (File.Exists(pfad) && Woerter(Formulierungsdatei.TextLesen(pfad)).Contains(wort)) return false;
        if (!File.Exists(pfad)) File.WriteAllText(pfad, AusnahmenKopf, new UTF8Encoding(true));
        File.AppendAllText(pfad, wort + "\r\n", new UTF8Encoding(false));
        return true;
    }
}
