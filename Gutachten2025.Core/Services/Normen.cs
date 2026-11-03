using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Gutachten2025.Services;

/// <summary>Eine Vorschrift (Paragraph oder Artikel) mit Text; Absätze durch Zeilenumbruch getrennt.</summary>
public sealed class Norm
{
    /// <summary>Gesetzeskürzel wie zitiert, z. B. „BGB“, „GmbHG“, „GG“.</summary>
    public string Gesetz { get; set; } = string.Empty;
    /// <summary>Bezeichnung wie in der Quelle, z. B. „§ 433“, „Art 6“.</summary>
    public string Nr { get; set; } = string.Empty;
    public string Titel { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    /// <summary>Herkunft (z. B. Adresse bei gesetze-im-internet.de) und Stand.</summary>
    public string Quelle { get; set; } = string.Empty;

    public string Schluessel => Normverweise.Schluessel(Gesetz, Nr);

    /// <summary>„§ 433 BGB“</summary>
    public string Zitat => $"{Normverweise.NrAnzeige(Nr)} {Gesetz}";
}

/// <summary>Fundstelle eines Normzitats im Text.</summary>
public sealed record NormVerweis(int Start, int Laenge, string Gesetz, string Nr, NormTeil? Teil = null)
{
    public string Schluessel => Normverweise.Schluessel(Gesetz, Nr);
    public string Zitat => $"{Normverweise.NrAnzeige(Nr)} {Gesetz}";
}

/// <summary>Zitierter Teil einer Norm: Absatz, Satz, Nummer, Buchstabe (jeweils ohne Zusatz, z. B. „2“, „1“, „3“, „f“).</summary>
public sealed record NormTeil(string? Abs, string? Satz, string? Nr, string? Lit)
{
    public bool Leer => Abs is null && Satz is null && Nr is null && Lit is null;

    /// <summary>„Abs. 1 S. 2 Nr. 3 lit. a“</summary>
    public override string ToString() => string.Join(" ", new[]
    {
        Abs is null ? null : "Abs. " + Abs, Satz is null ? null : "S. " + Satz, Nr is null ? null : "Nr. " + Nr, Lit is null ? null : "lit. " + Lit,
    }.Where(x => x is not null));
}

/// <summary>
/// Erkennt Normzitate wie „§ 433 Abs. 2 BGB“, „§§ 280 Abs. 1, 281 BGB“, „§ 823 I BGB“, „Art. 6 Abs. 1 lit. f DSGVO“,
/// „§ 280 Abs. 1, § 281 BGB“. Ohne Gesetzesangabe gilt das zuletzt zitierte Gesetz (im selben Text).
/// </summary>
public static class Normverweise
{
    static readonly Regex Zeichen = new(
        @"§§|§|Artt?\.|Art\b|\d+[a-z]?(?!\d)|\b[IVX]{1,5}\b|[\p{L}][\p{L}\-]*\.?|[,;()]",
        RegexOptions.CultureInvariant);

    static readonly HashSet<string> Zusatz = new(StringComparer.OrdinalIgnoreCase)
    {
        "Abs.", "Absatz", "Abs", "S.", "Satz", "Nr.", "Nummer", "Alt.", "Alternative", "Var.", "Variante", "lit.", "Buchst.", "Hs.", "Halbs.",
        "Halbsatz", "UAbs.", "Unterabs.", "Spiegelstrich", "Uabs.",
    };

    static readonly HashSet<string> Verbindung = new(StringComparer.OrdinalIgnoreCase)
    {
        "und", "oder", "bis", "sowie", "i.", "V.", "m.", "iVm", "ivm", "f.", "ff.", "a.", "F.", "n.", "entspr.", "analog", "auch",
    };

    /// <summary>Bekannte Kürzel, die die Regel „mindestens zwei Großbuchstaben“ nicht erfüllen.</summary>
    static readonly HashSet<string> Kuerzel = new(StringComparer.Ordinal) { "AO", "GG" };

    /// <summary>Sieht das Wort wie ein Gesetzeskürzel aus („BGB“, „GmbHG“, „AO“)?</summary>
    public static bool IstGesetzkuerzel(string w) => IstGesetz(w) || EurLex.Celex(w) is not null;

    static bool IstGesetz(string w)
    {
        if (w.EndsWith('.') || w.Length is < 2 or > 16 || !char.IsUpper(w[0])) return false;
        if (Kuerzel.Contains(w)) return true;
        int gross = w.Count(char.IsUpper);
        return gross >= 2 && !w.All(c => c is 'I' or 'V' or 'X');
    }

    /// <param name="standardGesetz">Gesetz für Zitate ohne Gesetzesangabe, solange keines genannt wurde (z. B. im Normtext selbst).</param>
    public static List<NormVerweis> Finden(string? text, string? standardGesetz = null)
    {
        var liste = new List<NormVerweis>();
        if (string.IsNullOrEmpty(text)) return liste;
        string? letztesGesetz = standardGesetz;
        List<Match> t = Zeichen.Matches(text).ToList();
        for (int i = 0; i < t.Count; i++)
        {
            string v = t[i].Value;
            if (v is not ("§" or "§§" or "Art." or "Artt." or "Art")) continue;
            bool mehrere = v is "§§" or "Artt.";
            string praefix = v.StartsWith('§') ? "§" : "Art";
            int kettenAnfang = t[i].Index;
            var nummern = new List<(int Start, string Nr, string?[] Teil)>();
            bool normModus = true;
            string? erwartet = null; // nächster Wert gehört zu: abs, satz, nr, lit, x (übergehen)
            string? gesetz = null;
            int ende = t[i].Index + t[i].Length;
            int j = i + 1;
            for (; j < t.Count; j++)
            {
                Match m = t[j];
                // Kette endet bei großen Lücken (anderer Satzteil).
                if (m.Index - ende > 3 && text.AsSpan(ende, m.Index - ende).Trim().Length > 0) break;
                string w = m.Value;
                if (w is "§" or "§§" or "Art." or "Artt." or "Art")
                {
                    if (w is "§§" or "Artt.") mehrere = true;
                    praefix = w.StartsWith('§') ? "§" : "Art";
                    normModus = true;
                }
                else if (char.IsDigit(w[0]))
                {
                    if (normModus)
                    {
                        nummern.Add((m.Index, $"{praefix} {w}", new string?[4]));
                        normModus = false;
                        erwartet = null;
                    }
                    else if (nummern.Count > 0)
                    {
                        string?[] teil = nummern[^1].Teil;
                        int platz = erwartet switch { "abs" => 0, "satz" => 1, "nr" => 2, _ => -1 };
                        // „§ 812 I 1“: Zahl nach römischem Absatz ist der Satz.
                        if (erwartet is null && teil[0] is not null && teil[1] is null && teil[2] is null) platz = 1;
                        if (platz >= 0 && teil[platz] is null) teil[platz] = w;
                        erwartet = null;
                    }
                }
                else if (w is "," or "und" or "sowie" or "bis")
                {
                    if (mehrere) normModus = true;
                }
                else if (w is ";" or "(" or ")") break;
                else if (Zusatz.Contains(w))
                {
                    erwartet = w.TrimEnd('.').ToLowerInvariant() switch
                    {
                        "abs" or "absatz" => "abs",
                        "s" or "satz" => "satz",
                        "nr" or "nummer" => "nr",
                        "lit" or "buchst" => "lit",
                        _ => "x",
                    };
                }
                else if (w.All(c => c is 'I' or 'V' or 'X'))
                {
                    // Römischer Absatz: „§ 823 I BGB“, „Abs. II“.
                    if (nummern.Count > 0 && (erwartet is null or "abs") && nummern[^1].Teil[0] is null) nummern[^1].Teil[0] = Roemisch(w).ToString(CultureInfo.InvariantCulture);
                    erwartet = null;
                }
                else if (w.Length == 1 && char.IsLower(w[0]))
                {
                    // lit. f; „§ 812 I 1 Alt. 1“
                    if (erwartet == "lit" && nummern.Count > 0) nummern[^1].Teil[3] ??= w;
                    erwartet = null;
                }
                else if (Verbindung.Contains(w)) { }
                else if (IstGesetz(w) || (w.Length > 2 && w.EndsWith('.') && IstGesetz(w[..^1])))
                {
                    // „BGB.“ am Satzende: Der Punkt gehört nicht zum Kürzel.
                    gesetz = w.TrimEnd('.');
                    ende = m.Index + gesetz.Length;
                    j++;
                    break;
                }
                else break;
                ende = m.Index + m.Length;
            }
            if (nummern.Count == 0) continue;
            gesetz ??= letztesGesetz;
            if (gesetz is null) continue;
            letztesGesetz = gesetz;
            for (int k = 0; k < nummern.Count; k++)
            {
                int start = k == 0 ? kettenAnfang : nummern[k].Start;
                int bis = k + 1 < nummern.Count ? nummern[k + 1].Start : ende;
                string?[] t2 = nummern[k].Teil;
                var teil = new NormTeil(t2[0], t2[1], t2[2], t2[3]);
                liste.Add(new NormVerweis(start, Math.Max(1, bis - start), gesetz, nummern[k].Nr, teil.Leer ? null : teil));
            }
            i = Math.Max(i, j - 1);
        }
        return liste;
    }

    static int Roemisch(string w)
    {
        int summe = 0;
        for (int i = 0; i < w.Length; i++)
        {
            int a = W(w[i]), b = i + 1 < w.Length ? W(w[i + 1]) : 0;
            summe += a < b ? -a : a;
        }
        return summe;
        static int W(char c) => c switch { 'I' => 1, 'V' => 5, 'X' => 10, _ => 0 };
    }

    /// <summary>
    /// Kennzeichnet Normzitate in einem (wohlgeformten) HTML-Ausschnitt von <see cref="MarkdownHtml"/> als
    /// &lt;span class="norm" data-gesetz="BGB" data-nr="§ 433" title="…"&gt;. Überschriften, Verweise und Code bleiben unverändert.
    /// </summary>
    public static string Auszeichnen(string html, Func<NormVerweis, string?> titel, string? standardGesetz = null)
    {
        if (string.IsNullOrWhiteSpace(html) || (!html.Contains('§') && !html.Contains("Art", StringComparison.Ordinal))) return html;
        XElement wurzel;
        try
        {
            wurzel = XElement.Parse("<div>" + html + "</div>", LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return html;
        }
        foreach (XText t in wurzel.DescendantNodes().OfType<XText>().ToList())
        {
            if (t.Ancestors().Any(a => a.Name.LocalName is "a" or "code" or "pre" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
                                       || (string?)a.Attribute("class") == "norm"))
                continue;
            List<NormVerweis> verweise = Finden(t.Value, standardGesetz);
            if (verweise.Count == 0) continue;
            var teile = new List<XNode>();
            int pos = 0;
            foreach (NormVerweis v in verweise.Where(v => v.Start >= 0 && v.Start + v.Laenge <= t.Value.Length))
            {
                if (v.Start < pos) continue;
                if (v.Start > pos) teile.Add(new XText(t.Value[pos..v.Start]));
                var span = new XElement("span", new XAttribute("class", "norm"), new XAttribute("data-gesetz", v.Gesetz),
                    new XAttribute("data-nr", v.Teil is null ? v.Nr : v.Nr + "|" + v.Teil), t.Value.Substring(v.Start, v.Laenge));
                if (titel(v) is { Length: > 0 } hinweis) span.SetAttributeValue("title", hinweis);
                teile.Add(span);
                pos = v.Start + v.Laenge;
            }
            if (pos < t.Value.Length) teile.Add(new XText(t.Value[pos..]));
            t.ReplaceWith(teile);
        }
        return string.Concat(wurzel.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));
    }

    /// <summary>Verweis an einer Textstelle (Mauszeiger, Klick); null = keiner.</summary>
    public static NormVerweis? An(string text, int stelle) =>
        Finden(text).FirstOrDefault(v => stelle >= v.Start && stelle <= v.Start + v.Laenge);

    /// <summary>Vergleichsschlüssel: „BGB|§433“, „DSGVO|ART6“ (Bindestriche, Punkte, Leerzeichen entfallen).</summary>
    public static string Schluessel(string gesetz, string nr) =>
        Kurz(gesetz) + "|" + Kurz(nr).Replace("ARTT", "ART");

    static string Kurz(string s) => new string(s.Where(c => !char.IsWhiteSpace(c) && c is not '.' and not '-').ToArray()).ToUpperInvariant();

    /// <summary>Zitat aus den Angaben eines angeklickten Verweises („§ 433|Abs. 2 S. 1“) mit Teil zurückgewinnen.</summary>
    public static NormVerweis AusAngabe(string gesetz, string angabe)
    {
        int k = angabe.IndexOf('|');
        if (k < 0) return new NormVerweis(0, 0, gesetz, angabe);
        string nr = angabe[..k];
        NormTeil? teil = Finden(nr + " " + angabe[(k + 1)..], gesetz).FirstOrDefault()?.Teil;
        return new NormVerweis(0, 0, gesetz, nr, teil);
    }

    /// <summary>„Art 6“ → „Art. 6“ (Zitierweise); Paragraphen unverändert.</summary>
    public static string NrAnzeige(string nr) => Regex.Replace(nr.Trim(), @"^Art\.?\s*", "Art. ");
}

/// <summary>
/// Normtexte der Klausur (Normen.json aus dem Paket) und bereits geladene Gesetze (Vorlagen\Gesetze\*.json, für alle Klausuren).
/// </summary>
public sealed class Normensammlung
{
    public const string Datei = "Normen.json";
    public const string Cacheordner = "Gesetze";

    /// <summary>Eigene Normen (Markdown): je Klausur und für alle Klausuren (Vorlagen\Gesetze).</summary>
    public const string EigeneDatei = "EigeneNormen.md";
    /// <summary>Einzeln über kvasir geladene Normen (Landesrecht u. a.).</summary>
    public const string EinzelnDatei = "einzeln.json";

    readonly Dictionary<string, Norm> _normen = new(StringComparer.Ordinal);
    readonly HashSet<string> _gesetze = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Vollständig geladene Gesetze in amtlicher Reihenfolge (zum Durchblättern).</summary>
    readonly Dictionary<string, List<Norm>> _folge = new(StringComparer.Ordinal);
    readonly List<Norm> _einzeln = new();

    public int Anzahl => _normen.Count;

    /// <summary>Normen aus dem Paket (in Reihenfolge), für das Verzeichnis.</summary>
    public List<Norm> Paketnormen { get; } = new();

    public IEnumerable<Norm> Alle => _normen.Values;

    /// <summary>Eigene Normen dieser Klausur bzw. aller Klausuren.</summary>
    public List<Norm> Eigene { get; } = new();

    public static Normensammlung Laden(string? klausurOrdner, string vorlagenOrdner)
    {
        var s = new Normensammlung();
        string cache = Path.Combine(vorlagenOrdner, Cacheordner);
        if (Directory.Exists(cache))
            foreach (string f in Directory.EnumerateFiles(cache, "*.json"))
            {
                List<Norm> normen = LesenDatei(f);
                s.Aufnehmen(normen, paket: false);
                if (Path.GetFileName(f).Equals(EinzelnDatei, StringComparison.OrdinalIgnoreCase)) s._einzeln.AddRange(normen);
                else if (normen.Count > 0) s._folge[GesetzSchluessel(normen[0].Gesetz)] = normen;
            }
        if (klausurOrdner is not null)
        {
            string p = Path.Combine(klausurOrdner, Datei);
            if (File.Exists(p)) s.Aufnehmen(LesenDatei(p), paket: true);
        }
        // Eigene Normen zuletzt: Sie gehen allen anderen Fassungen vor (Klausur vor allgemein).
        foreach (string pfad in new[] { Path.Combine(cache, EigeneDatei), klausurOrdner is null ? null : Path.Combine(klausurOrdner, EigeneDatei) }.OfType<string>())
            if (File.Exists(pfad))
            {
                try
                {
                    List<Norm> eigene = EigeneNormen.AusMarkdown(File.ReadAllText(pfad, Encoding.UTF8));
                    s.Aufnehmen(eigene, paket: false);
                    s.Eigene.AddRange(eigene);
                }
                catch (IOException ex)
                {
                    EinstellungsSpeicher.Protokollieren(ex);
                }
            }
        return s;
    }

    static string GesetzSchluessel(string gesetz) => Normverweise.Schluessel(gesetz, string.Empty).TrimEnd('|');

    /// <summary>Alle Normen eines vollständig geladenen Gesetzes in Reihenfolge; null = nur einzelne Normen bekannt.</summary>
    public List<Norm>? Folge(string gesetz) => _folge.GetValueOrDefault(GesetzSchluessel(gesetz));

    /// <summary>Einzeln geladene Norm (kvasir) in den Zwischenspeicher aufnehmen.</summary>
    public void EinzelnSpeichern(string vorlagenOrdner, Norm n)
    {
        _einzeln.RemoveAll(x => x.Schluessel == n.Schluessel);
        _einzeln.Add(n);
        string ordner = Path.Combine(vorlagenOrdner, Cacheordner);
        Directory.CreateDirectory(ordner);
        File.WriteAllText(Path.Combine(ordner, EinzelnDatei), JsonSerializer.Serialize(_einzeln, KlausurKonfigDatei.Json), new UTF8Encoding(false));
        Aufnehmen(new[] { n }, paket: false);
    }

    static List<Norm> LesenDatei(string pfad)
    {
        try
        {
            return AusBytes(File.ReadAllBytes(pfad));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            return new();
        }
    }

    /// <summary>Liest und prüft eine Normenliste (Paket oder Zwischenspeicher).</summary>
    public static List<Norm> AusBytes(byte[] daten)
    {
        List<Norm>? liste;
        try
        {
            liste = JsonSerializer.Deserialize<List<Norm>>(KlausurKonfigDatei.OhneBom(daten), KlausurKonfigDatei.Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Die Normensammlung ist ungültig: " + ex.Message, ex);
        }
        if (liste is null) throw new InvalidDataException("Die Normensammlung ist leer.");
        if (liste.Count > 20000) throw new InvalidDataException("Die Normensammlung ist zu groß.");
        return liste.Where(n => n is not null && n.Gesetz.Trim().Length > 0 && n.Nr.Trim().Length > 0).ToList();
    }

    public void Aufnehmen(IEnumerable<Norm> normen, bool paket)
    {
        foreach (Norm n in normen)
        {
            _normen[n.Schluessel] = n; // Paketfassung geht der allgemeinen vor (später geladen)
            _gesetze.Add(Normverweise.Schluessel(n.Gesetz, string.Empty).TrimEnd('|'));
            if (paket) Paketnormen.Add(n);
        }
    }

    public Norm? Finden(string gesetz, string nr) => _normen.GetValueOrDefault(Normverweise.Schluessel(gesetz, nr));

    /// <summary>Ist das Gesetz vollständig geladen (nicht nur einzelne Normen aus dem Paket)?</summary>
    public bool GesetzGeladen(string gesetz, string vorlagenOrdner) =>
        File.Exists(Path.Combine(vorlagenOrdner, Cacheordner, Gesetzesquelle.Pfadname(gesetz) + ".json"));

    /// <summary>Volltextsuche (alle Wörter, ohne Groß-/Kleinschreibung); Treffer im Titel zuerst.</summary>
    public List<Norm> Suchen(string begriffe, int hoechstens = 40)
    {
        string[] w = begriffe.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (w.Length == 0) return new();
        return _normen.Values
            .Select(n => (n, titel: w.All(x => (n.Titel + " " + n.Nr + " " + n.Gesetz).Contains(x, StringComparison.CurrentCultureIgnoreCase)),
                            text: w.All(x => (n.Titel + " " + n.Text).Contains(x, StringComparison.CurrentCultureIgnoreCase))))
            .Where(x => x.titel || x.text)
            .OrderByDescending(x => x.titel)
            .ThenBy(x => x.n.Gesetz, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => Sortierzahl(x.n.Nr))
            .Take(hoechstens)
            .Select(x => x.n)
            .ToList();
    }

    public static double Sortierzahl(string nr)
    {
        Match m = Regex.Match(nr, @"(\d+)([a-z]?)");
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + (m.Groups[2].Length > 0 ? (m.Groups[2].Value[0] - 'a' + 1) / 100.0 : 0) : 0;
    }

    /// <summary>Speichert ein vollständig geladenes Gesetz im Zwischenspeicher und nimmt es auf.</summary>
    public void GesetzSpeichern(string vorlagenOrdner, string gesetz, List<Norm> normen)
    {
        string ordner = Path.Combine(vorlagenOrdner, Cacheordner);
        Directory.CreateDirectory(ordner);
        string pfad = Path.Combine(ordner, Gesetzesquelle.Pfadname(gesetz) + ".json");
        File.WriteAllText(pfad, JsonSerializer.Serialize(normen, KlausurKonfigDatei.Json), new UTF8Encoding(false));
        Aufnehmen(normen, paket: false);
        if (normen.Count > 0) _folge[GesetzSchluessel(gesetz)] = normen;
        foreach (Norm e in Eigene) _normen[e.Schluessel] = e; // eigene Fassungen behalten Vorrang
    }

    /// <summary>Kurztext für die Anzeige beim Überfahren mit der Maus.</summary>
    public static string Kurztext(Norm n, int hoechstens = 900, NormTeil? teil = null)
    {
        string kopf = n.Zitat + (n.Titel.Length > 0 ? " – " + n.Titel : string.Empty);
        string text = n.Text.Trim();
        // Zitierter Teil („Abs. 2 S. 1“): nur dieser, sofern er sich bestimmen lässt.
        if (teil is not null && Normstelle.Ausschnitt(n, teil) is { Length: > 0 } ausschnitt)
        {
            kopf = $"{Normverweise.NrAnzeige(n.Nr)} {teil} {n.Gesetz}" + (n.Titel.Length > 0 ? " – " + n.Titel : string.Empty);
            text = ausschnitt;
        }
        if (text.Length > hoechstens) text = text[..hoechstens].TrimEnd() + " …";
        return kopf + "\n\n" + text;
    }

    /// <summary>Normtext als Markdown für die Seitenleiste (Absätze, Aufzählungen bleiben erhalten).</summary>
    public static string AlsMarkdown(Norm n, NormTeil? teil = null)
    {
        var sb = new StringBuilder();
        sb.Append("### ").Append(MarkdownHtml.Maskieren(n.Zitat));
        if (n.Titel.Length > 0) sb.Append(" – ").Append(MarkdownHtml.Maskieren(n.Titel));
        sb.Append("\n\n");
        string[] zeilen = Normstelle.Zeilen(n);
        Dictionary<int, (int Start, int Laenge)> hervor = teil is null ? new() : Normstelle.Bereiche(zeilen, teil);
        for (int i = 0; i < zeilen.Length; i++)
        {
            string z = zeilen[i];
            if (hervor.TryGetValue(i, out var b))
                sb.Append(MarkdownHtml.Maskieren(z[..b.Start])).Append(Normstelle.Anfang)
                  .Append(MarkdownHtml.Maskieren(z.Substring(b.Start, b.Laenge))).Append(Normstelle.Ende)
                  .Append(MarkdownHtml.Maskieren(z[(b.Start + b.Laenge)..]));
            else sb.Append(MarkdownHtml.Maskieren(z));
            sb.Append("\n\n");
        }
        if (n.Quelle.Length > 0) sb.Append("*Quelle: ").Append(MarkdownHtml.Maskieren(n.Quelle)).Append("*\n");
        return sb.ToString();
    }
}

/// <summary>
/// Gesetzestexte von gesetze-im-internet.de (amtliche XML-Fassung des Bundesjustizministeriums, je Gesetz eine ZIP-Datei).
/// Die Browser-Fassung lädt über das KI-Portal (dort zwischengespeichert, mit CORS-Freigabe).
/// EU-Recht kommt von EUR-Lex (<see cref="EurLex"/>), Landesrecht und Übriges einzeln über <see cref="Kvasir"/>.
/// </summary>
public static class Gesetzesquelle
{
    public const string Basis = "https://www.gesetze-im-internet.de/";
    public const string PortalBasis = "https://learn.jura.uni-passau.de/ki/gutachtenpaket/";
    public const string Portal = PortalBasis + "gesetz/";

    /// <summary>Kürzel, deren Pfad bei gesetze-im-internet.de vom Kürzel abweicht.</summary>
    static readonly Dictionary<string, string> Pfade = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BDSG"] = "bdsg_2018", ["UWG"] = "uwg_2004", ["StVO"] = "stvo_2013", ["EGBGB"] = "bgbeg", ["WEG"] = "woeigg",
        ["BauGB"] = "bbaug", ["UmwG"] = "umwg_1995", ["VVG"] = "vvg_2008", ["KWG"] = "kredwg", ["GKG"] = "gkg_2004",
        ["OWiG"] = "owig_1968", ["TKG"] = "tkg_2021", ["VwZG"] = "vwzg_2005", ["PAngV"] = "pangv_2022", ["BNatSchG"] = "bnatschg_2009",
        ["AO"] = "ao_1977", ["UStG"] = "ustg_1980", ["StVZO"] = "stvzo_2012", ["FeV"] = "fev_2010", ["AufenthG"] = "aufenthg_2004",
        ["BtMG"] = "btmg_1981", ["WaffG"] = "waffg_2002", ["TDDDG"] = "ttdsg", ["SGB I"] = "sgb_1", ["SGB V"] = "sgb_5",
        ["EStG"] = "estg", ["ZPO"] = "zpo", ["BGB"] = "bgb",
    };

    /// <summary>Pfadname bei gesetze-im-internet.de (Kleinbuchstaben, ohne Sonderzeichen).</summary>
    public static string Pfadname(string gesetz)
    {
        if (Pfade.TryGetValue(gesetz.Trim(), out string? p)) return p;
        var sb = new StringBuilder();
        foreach (char c in gesetz.Trim().ToLowerInvariant())
            sb.Append(c switch { 'ä' => "ae", 'ö' => "oe", 'ü' => "ue", 'ß' => "ss", _ => char.IsLetterOrDigit(c) ? c.ToString() : c == ' ' ? "_" : string.Empty });
        return sb.ToString();
    }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Lädt ein ganzes Gesetz; <see cref="InvalidDataException"/> mit verständlicher Meldung bei Fehlern.</summary>
    public static async Task<List<Norm>> LadenAsync(string gesetz)
    {
        if (EurLex.Celex(gesetz) is { } celex) return await EuLadenAsync(gesetz, celex);
        string pfad = Pfadname(gesetz);
        if (!Regex.IsMatch(pfad, "^[a-z0-9_]{1,40}$")) throw new InvalidDataException($"„{gesetz}“ ist kein gültiges Gesetzeskürzel.");
        Exception? letzter = null;
        // Browser: nur über das Portal (gesetze-im-internet.de erlaubt keine Abrufe aus Webseiten). Sonst direkt, ersatzweise über das Portal.
        if (!OperatingSystem.IsBrowser())
        {
            try
            {
                using HttpResponseMessage a = await Http.GetAsync(Basis + pfad + "/xml.zip");
                if (a.IsSuccessStatusCode)
                {
                    await using Stream s = await a.Content.ReadAsStreamAsync();
                    using var ms = new MemoryStream();
                    await s.CopyToAsync(ms);
                    return AusZip(ms.ToArray(), gesetz, Basis + pfad + "/");
                }
                letzter = new InvalidDataException($"gesetze-im-internet.de kennt „{gesetz}“ nicht (Pfad „{pfad}“).");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                letzter = ex;
            }
        }
        try
        {
            using HttpResponseMessage a = await Http.GetAsync(Portal + pfad);
            if (a.IsSuccessStatusCode)
            {
                List<Norm> liste = AusBytesOderLeer(await a.Content.ReadAsByteArrayAsync());
                if (liste.Count > 0)
                {
                    foreach (Norm n in liste) n.Gesetz = gesetz;
                    return liste;
                }
            }
            letzter ??= new InvalidDataException($"„{gesetz}“ wurde nicht gefunden (Pfad „{pfad}“).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            letzter ??= ex;
        }
        throw letzter as InvalidDataException
              ?? new InvalidDataException($"„{gesetz}“ ließ sich nicht laden: {letzter?.Message}", letzter);
    }

    /// <summary>EU-Rechtsakt von EUR-Lex (Browser: über das Portal; sonst direkt, ersatzweise über das Portal).</summary>
    static async Task<List<Norm>> EuLadenAsync(string gesetz, string celex)
    {
        Exception? letzter = null;
        if (!OperatingSystem.IsBrowser())
        {
            try
            {
                using var anfrage = new HttpRequestMessage(HttpMethod.Get, EurLex.Basis + celex);
                anfrage.Headers.AcceptLanguage.ParseAdd("de");
                using HttpResponseMessage a = await Http.SendAsync(anfrage);
                if (a.IsSuccessStatusCode) return EurLex.AusHtml(await a.Content.ReadAsStringAsync(), gesetz, EurLex.Basis + celex);
                letzter = new InvalidDataException($"EUR-Lex antwortet nicht ({(int)a.StatusCode}).");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                letzter = ex;
            }
        }
        try
        {
            using HttpResponseMessage a = await Http.GetAsync(PortalBasis + "eurlex?celex=" + Uri.EscapeDataString(celex));
            if (a.IsSuccessStatusCode && AusBytesOderLeer(await a.Content.ReadAsByteArrayAsync()) is { Count: > 0 } liste)
            {
                foreach (Norm n in liste) n.Gesetz = gesetz;
                return liste;
            }
            letzter ??= new InvalidDataException($"{gesetz} ließ sich von EUR-Lex nicht laden.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            letzter ??= ex;
        }
        throw letzter as InvalidDataException ?? new InvalidDataException($"{gesetz} ließ sich nicht laden: {letzter?.Message}", letzter);
    }

    static List<Norm> AusBytesOderLeer(byte[] daten)
    {
        try { return Normensammlung.AusBytes(daten); }
        catch (InvalidDataException) { return new(); }
    }

    public static List<Norm> AusZip(byte[] zip, string gesetz, string quelle)
    {
        using var ms = new MemoryStream(zip);
        using var archiv = new ZipArchive(ms, ZipArchiveMode.Read);
        ZipArchiveEntry eintrag = archiv.Entries.FirstOrDefault(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                                  ?? throw new InvalidDataException("Die Datei von gesetze-im-internet.de enthält kein XML.");
        if (eintrag.Length > 80L << 20) throw new InvalidDataException("Das Gesetz ist zu groß.");
        using Stream s = eintrag.Open();
        return AusXml(s, gesetz, quelle);
    }

    /// <summary>Wandelt die XML-Fassung (gii-norm) in Normen um; Gliederungseinheiten und Fußnoten entfallen.</summary>
    public static List<Norm> AusXml(Stream xml, string gesetz, string quelle)
    {
        var einstellungen = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using XmlReader leser = XmlReader.Create(xml, einstellungen);
        XDocument doc = XDocument.Load(leser);
        string stand = DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var liste = new List<Norm>();
        foreach (XElement norm in doc.Descendants("norm"))
        {
            XElement? meta = norm.Element("metadaten");
            string nr = Leer(meta?.Element("enbez")?.Value);
            if (!(nr.StartsWith('§') || nr.StartsWith("Art", StringComparison.Ordinal))) continue;
            XElement? inhalt = norm.Element("textdaten")?.Element("text")?.Element("Content");
            var absaetze = new List<string>();
            if (inhalt is not null)
                foreach (XElement p in inhalt.Elements())
                {
                    string t = Klartext(p).Trim();
                    if (t.Length > 0) absaetze.Add(t);
                }
            liste.Add(new Norm
            {
                Gesetz = gesetz,
                Nr = nr,
                Titel = Leer(meta?.Element("titel")?.Value),
                Text = string.Join("\n", absaetze),
                Quelle = $"{quelle} (abgerufen {stand})",
            });
        }
        if (liste.Count == 0) throw new InvalidDataException("Im Gesetz wurden keine Paragraphen oder Artikel gefunden.");
        return liste;
    }

    /// <summary>Text eines Absatzes; Aufzählungen (DL/DT/DD) als „1. …“ in eigenen Zeilen, Fußnotenverweise entfallen.</summary>
    static string Klartext(XElement e)
    {
        var sb = new StringBuilder();
        void Gehe(XNode n)
        {
            switch (n)
            {
                case XText t:
                    sb.Append(t.Value);
                    break;
                case XElement x when x.Name.LocalName is "FnR" or "Footnotes" or "Footnote" or "FnArea":
                    break;
                case XElement x when x.Name.LocalName == "BR":
                    sb.Append('\n');
                    break;
                case XElement x when x.Name.LocalName == "DT":
                    sb.Append('\n').Append(Leer(x.Value)).Append(' ');
                    break;
                case XElement x when x.Name.LocalName is "DD" or "LA":
                    foreach (XNode k in x.Nodes()) Gehe(k);
                    break;
                case XElement x:
                    foreach (XNode k in x.Nodes()) Gehe(k);
                    break;
            }
        }
        Gehe(e);
        return Regex.Replace(Regex.Replace(sb.ToString(), @"[ \t ]+", " "), @" ?\n ?", "\n").Replace("\n\n", "\n");
    }

    static string Leer(string? s) => Regex.Replace(s ?? string.Empty, @"\s+", " ").Trim();
}

/// <summary>
/// Zitierte Stelle im Normtext (Absatz, Satz, Nummer, Buchstabe) für Hervorhebung und Hinweis. Absätze beginnen mit „(1)“,
/// Nummern mit „1.“, Buchstaben mit „a)“ in eigenen Zeilen (so liefern gesetze-im-internet.de, EUR-Lex und kvasir die Texte).
/// Sätze werden nach Satzzeichen getrennt; Abkürzungen wie „Abs.“ oder „z. B.“ beenden keinen Satz.
/// </summary>
public static class Normstelle
{
    /// <summary>Marken im Markdown, die nach der Umwandlung zu &lt;mark class="hervor"&gt; werden.</summary>
    public const char Anfang = '', Ende = '';

    static readonly Regex AbsatzAnfang = new(@"^\((\d+[a-z]?)\)\s*", RegexOptions.CultureInvariant);
    static readonly Regex NummerAnfang = new(@"^(\d+[a-z]?)\.\s", RegexOptions.CultureInvariant);
    static readonly Regex BuchstabeAnfang = new(@"^([a-z]{1,2})\)\s", RegexOptions.CultureInvariant);
    static readonly HashSet<string> Abkuerzungen = new(StringComparer.Ordinal)
    {
        "Abs", "Art", "Nr", "Buchst", "bzw", "ggf", "usw", "vgl", "Ziff", "gem", "Rn", "St", "Bd", "Anh", "Anl", "Hs", "Halbs", "lit", "sog",
        "ff", "Var", "Alt", "Unterabs", "UAbs", "Ges", "Verf", "Mio", "Mrd", "Std", "Tel", "einschl", "insb", "zzgl", "abzgl",
    };

    public static string[] Zeilen(Norm n) =>
        n.Text.Replace("\r\n", "\n").Split('\n').Select(z => z.Trim()).Where(z => z.Length > 0).ToArray();

    /// <summary>Hervorzuhebende Stellen je Zeile (Zeilenindex → Bereich); leer, wenn sich die Stelle nicht bestimmen lässt.</summary>
    public static Dictionary<int, (int Start, int Laenge)> Bereiche(string[] zeilen, NormTeil teil)
    {
        var ergebnis = new Dictionary<int, (int, int)>();
        if (teil.Leer || zeilen.Length == 0) return ergebnis;
        // Absatz bestimmen (ohne Angabe: ganze Norm).
        int von = 0, bis = zeilen.Length;
        if (teil.Abs is { } abs)
        {
            int a = Array.FindIndex(zeilen, z => AbsatzAnfang.Match(z) is { Success: true } m && m.Groups[1].Value == abs);
            if (a < 0)
            {
                // Norm ohne Absatzzählung: „Abs. 1“ ist der ganze Text.
                if (abs != "1" || zeilen.Any(z => AbsatzAnfang.IsMatch(z))) return ergebnis;
            }
            else
            {
                von = a;
                bis = a + 1;
                while (bis < zeilen.Length && !AbsatzAnfang.IsMatch(zeilen[bis])) bis++;
            }
        }
        if (teil.Nr is not null || teil.Lit is not null)
        {
            if (teil.Nr is { } nr)
            {
                int k = Array.FindIndex(zeilen, von, bis - von, z => NummerAnfang.Match(z) is { Success: true } m && m.Groups[1].Value == nr);
                if (k < 0) return ergebnis;
                von = k;
                int e = k + 1;
                while (e < bis && !NummerAnfang.IsMatch(zeilen[e]) && !AbsatzAnfang.IsMatch(zeilen[e]) && BuchstabeAnfang.IsMatch(zeilen[e])) e++;
                bis = e;
            }
            if (teil.Lit is { } lit)
            {
                int k = Array.FindIndex(zeilen, von, bis - von, z => BuchstabeAnfang.Match(z) is { Success: true } m && m.Groups[1].Value == lit);
                if (k < 0) return ergebnis;
                von = k;
                bis = k + 1;
            }
            for (int i = von; i < bis; i++) ergebnis[i] = (0, zeilen[i].Length);
            return ergebnis;
        }
        if (teil.Satz is { } satzText && int.TryParse(satzText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int satz) && satz > 0)
        {
            // Sätze nur in Fließtextzeilen des Absatzes zählen (nicht in Aufzählungen).
            int zaehler = 0;
            for (int i = von; i < bis; i++)
            {
                string z = zeilen[i];
                if (i > von && (NummerAnfang.IsMatch(z) || BuchstabeAnfang.IsMatch(z))) continue;
                int start = i == von && AbsatzAnfang.Match(z) is { Success: true } m ? m.Length : 0;
                foreach (var (s, l) in Saetze(z, start))
                    if (++zaehler == satz)
                    {
                        ergebnis[i] = (s, l);
                        return ergebnis;
                    }
            }
            return ergebnis;
        }
        for (int i = von; i < bis; i++) ergebnis[i] = (0, zeilen[i].Length);
        return ergebnis;
    }

    /// <summary>Sätze einer Zeile ab <paramref name="start"/> (Beginn, Länge).</summary>
    public static List<(int Start, int Laenge)> Saetze(string z, int start = 0)
    {
        var liste = new List<(int, int)>();
        int anfang = start;
        for (int i = start; i < z.Length; i++)
        {
            if (z[i] is not ('.' or '?' or '!')) continue;
            int n = i + 1;
            if (n < z.Length && z[n] is ')' or '“' or '"') n++;
            if (n >= z.Length) break;
            if (z[n] != ' ') continue;
            int w = n;
            while (w < z.Length && z[w] == ' ') w++;
            if (w >= z.Length || !(char.IsUpper(z[w]) || z[w] is '„' or '§')) continue;
            if (z[i] == '.' && IstAbkuerzung(z, i)) continue;
            liste.Add((anfang, n - anfang));
            anfang = w;
            i = w - 1;
        }
        if (anfang < z.Length && z[anfang..].Trim().Length > 0) liste.Add((anfang, z.Length - anfang));
        return liste;
    }

    static bool IstAbkuerzung(string z, int punkt)
    {
        int k = punkt;
        while (k > 0 && char.IsLetter(z[k - 1])) k--;
        string wort = z[k..punkt];
        if (wort.Length == 0) return false; // Zahl oder Klammer vor dem Punkt: Satzende
        if (wort.Length == 1) return true;  // z. B., i. S. d., S. 1
        return Abkuerzungen.Contains(wort);
    }

    /// <summary>Text der zitierten Stelle (für den Hinweis beim Überfahren); null = nicht bestimmbar.</summary>
    public static string? Ausschnitt(Norm n, NormTeil teil)
    {
        string[] zeilen = Zeilen(n);
        var b = Bereiche(zeilen, teil);
        if (b.Count == 0) return null;
        return string.Join("\n", b.OrderBy(x => x.Key).Select(x => zeilen[x.Key].Substring(x.Value.Start, x.Value.Laenge)));
    }

    /// <summary>Marken im fertigen HTML in &lt;mark class="hervor"&gt; umsetzen (die Marken stehen stets innerhalb einer Zeile).</summary>
    public static string Hervorheben(string html) =>
        html.Replace(Anfang.ToString(), "<mark class=\"hervor\">", StringComparison.Ordinal).Replace(Ende.ToString(), "</mark>", StringComparison.Ordinal);
}

/// <summary>
/// Eigene Normen als Markdown, z. B. Prüfungsordnungen oder Satzungen:
/// <code>
/// # PrüfO
/// ## § 12 Wiederholung
/// (1) Text …
/// (2) Text …
/// </code>
/// „# Kürzel“ setzt das Gesetz, „## § 12 Titel“ bzw. „## Art. 3 Titel“ beginnt eine Vorschrift; Zeilen mit „//“ sind Anmerkungen.
/// </summary>
public static class EigeneNormen
{
    static readonly Regex Kopf = new(@"^(§|Art\.?|Artikel)\s*(\d+[a-z]?)\s*(?:[–—:-]\s*)?(.*)$", RegexOptions.CultureInvariant);

    public const string Vorlage =
        "// Eigene Normen dieser Klausur (z. B. Prüfungsordnung, Satzung, Landesrecht). Aufbau:\n" +
        "// # Kürzel des Gesetzes, darunter je Vorschrift „## § 1 Titel“ bzw. „## Art. 1 Titel“, dann die Absätze „(1) …“.\n" +
        "// Eigene Normen gehen anderen Fassungen vor und sind Teil exportierter Pakete.\n\n" +
        "# PrüfO\n\n## § 1 Geltungsbereich\n(1) Text des ersten Absatzes.\n(2) Text des zweiten Absatzes.\n";

    public static List<Norm> AusMarkdown(string text)
    {
        var liste = new List<Norm>();
        string? gesetz = null;
        Norm? aktuell = null;
        var zeilen = new List<string>();
        void Abschliessen()
        {
            if (aktuell is null) return;
            aktuell.Text = string.Join("\n", zeilen.Select(z => z.Trim()).Where(z => z.Length > 0));
            liste.Add(aktuell);
            aktuell = null;
            zeilen.Clear();
        }
        foreach (string roh in text.Replace("\r\n", "\n").Split('\n'))
        {
            string z = roh.TrimEnd();
            if (z.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
            if (z.StartsWith("## ", StringComparison.Ordinal))
            {
                Abschliessen();
                Match m = Kopf.Match(z[3..].Trim());
                if (m.Success && gesetz is not null)
                    aktuell = new Norm
                    {
                        Gesetz = gesetz,
                        Nr = (m.Groups[1].Value == "§" ? "§ " : "Art ") + m.Groups[2].Value,
                        Titel = m.Groups[3].Value.Trim(),
                        Quelle = "eigene Norm",
                    };
                continue;
            }
            if (z.StartsWith("# ", StringComparison.Ordinal))
            {
                Abschliessen();
                gesetz = z[2..].Trim();
                if (gesetz.Length == 0) gesetz = null;
                continue;
            }
            if (aktuell is not null) zeilen.Add(z);
        }
        Abschliessen();
        return liste;
    }
}

/// <summary>
/// EU-Recht von EUR-Lex (amtliche HTML-Fassung, deutsch). Die Browser-Fassung lädt über das KI-Portal.
/// </summary>
public static class EurLex
{
    public const string Basis = "https://eur-lex.europa.eu/legal-content/DE/TXT/HTML/?uri=CELEX:";

    /// <summary>Kürzel → CELEX-Nummer (häufig zitierte Rechtsakte; Vergleich ohne Bindestriche und Leerzeichen).</summary>
    static readonly Dictionary<string, string> Rechtsakte = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DSGVO"] = "32016R0679", ["GDPR"] = "32016R0679", ["KIVO"] = "32024R1689", ["KIVerordnung"] = "32024R1689", ["AIAct"] = "32024R1689",
        ["AIVO"] = "32024R1689", ["DSA"] = "32022R2065", ["DMA"] = "32022R1925", ["DGA"] = "32022R0868", ["DataAct"] = "32023R2854",
        ["DORA"] = "32022R2554", ["eIDASVO"] = "32014R0910", ["MiCAR"] = "32023R1114", ["MiCA"] = "32023R1114", ["MAR"] = "32014R0596",
        ["EuGVVO"] = "32012R1215", ["BrüsselIaVO"] = "32012R1215", ["EuInsVO"] = "32015R0848", ["EuErbVO"] = "32012R0650",
        ["RomIVO"] = "32008R0593", ["RomIIVO"] = "32007R0864", ["SEVO"] = "32001R2157", ["ProspektVO"] = "32017R1129",
        ["AEUV"] = "12016E/TXT", ["EUV"] = "12016M/TXT", ["GRCh"] = "12016P/TXT", ["GRC"] = "12016P/TXT",
        ["VRRL"] = "32011L0083", ["WKRL"] = "32019L0771", ["DIRL"] = "32019L0770", ["KlauselRL"] = "31993L0013", ["UGPRL"] = "32005L0029",
        ["ProdHaftRL"] = "32024L2853", ["NIS2RL"] = "32022L2555", ["ePrivacyRL"] = "32002L0058", ["DSRL"] = "31995L0046",
        ["UrhDaGRL"] = "32019L0790", ["DSMRL"] = "32019L0790", ["InfoSocRL"] = "32001L0029", ["DigiRL"] = "32019L1151",
    };

    static string Kurz(string gesetz) => new(gesetz.Where(char.IsLetterOrDigit).ToArray());

    public static string? Celex(string gesetz) => Rechtsakte.GetValueOrDefault(Kurz(gesetz));

    static readonly Regex Artikelkopf = new(@"<p[^>]*class=""(?:oj-)?ti-art""[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    static readonly Regex Untertitel = new(@"<p[^>]*class=""(?:oj-)?sti-art""[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    static readonly Regex Zeile = new(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    static readonly Regex Zelle = new(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    static readonly Regex Fussnote = new(@"<(span|a)[^>]*class=""(?:oj-)?(?:super|note)[^""]*""[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.CultureInvariant);

    /// <summary>Artikel aus der HTML-Fassung; Erwägungsgründe und Anhänge entfallen.</summary>
    public static List<Norm> AusHtml(string html, string gesetz, string quelle)
    {
        string stand = DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var liste = new List<Norm>();
        MatchCollection koepfe = Artikelkopf.Matches(html);
        var gesehen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < koepfe.Count; i++)
        {
            Match kopf = koepfe[i];
            Match nr = Regex.Match(Klartext(kopf.Groups[1].Value), @"^Artikel\s+(\d+[a-z]?)", RegexOptions.CultureInvariant);
            if (!nr.Success) continue;
            int ende = i + 1 < koepfe.Count ? koepfe[i + 1].Index : Math.Min(html.Length, AbschnittEnde(html, kopf.Index));
            ende = Math.Max(ende, kopf.Index + kopf.Length);
            string teil = html[(kopf.Index + kopf.Length)..ende];
            string titel = string.Empty;
            if (Untertitel.Match(teil) is { Success: true } u && u.Index < 400)
            {
                titel = Klartext(u.Groups[1].Value);
                teil = teil[(u.Index + u.Length)..];
            }
            if (!gesehen.Add(nr.Groups[1].Value)) continue; // Änderungsrechtsakte zitieren Artikel anderer Rechtsakte
            liste.Add(new Norm
            {
                Gesetz = gesetz,
                Nr = "Art " + nr.Groups[1].Value,
                Titel = titel,
                Text = Text(teil),
                Quelle = $"{quelle} (abgerufen {stand})",
            });
        }
        if (liste.Count == 0) throw new InvalidDataException("In der Fassung von EUR-Lex wurden keine Artikel gefunden.");
        return liste;
    }

    static int AbschnittEnde(string html, int ab)
    {
        int k = html.IndexOf("class=\"oj-doc-ti\"", ab, StringComparison.Ordinal); // Anhänge
        return k < 0 ? html.Length : html.LastIndexOf('<', k);
    }

    /// <summary>HTML eines Artikels in Zeilen: Absätze „(1) …“, Aufzählungen „a) …“ (Tabellen mit Buchstabe und Text).</summary>
    public static string Text(string html)
    {
        html = Fussnote.Replace(html, string.Empty);
        html = Zeile.Replace(html, m => "\n" + string.Join(" ", Zelle.Matches(m.Groups[1].Value).Select(z => Klartext(z.Groups[1].Value))) + "\n");
        html = Regex.Replace(html, @"<(p|div|br)\b", "\n<$1", RegexOptions.CultureInvariant);
        return string.Join("\n", Klartext(html, zeilen: true).Split('\n').Select(z => z.Trim()).Where(z => z.Length > 0));
    }

    static string Klartext(string html, bool zeilen = false)
    {
        string t = System.Net.WebUtility.HtmlDecode(Tag.Replace(html, string.Empty));
        t = Regex.Replace(t, @"[ \t    ]+", " ");
        return zeilen ? t : Regex.Replace(t, @"\s+", " ").Trim();
    }
}

/// <summary>
/// Einzelne Normen über kvasir (kvasir.legal: Bundes-, Landes- und EU-Recht), stets über das KI-Portal: Der Zugangsschlüssel
/// bleibt dort, Antworten werden zwischengespeichert.
/// </summary>
public static class Kvasir
{
    public const string Portal = Gesetzesquelle.PortalBasis + "kvasir?cite=";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };

    /// <summary>Norm zum Zitat; null = nicht gefunden. <see cref="InvalidDataException"/> bei Verbindungsproblemen.</summary>
    public static async Task<Norm?> NormAsync(string gesetz, string nr)
    {
        string zitat = $"{Normverweise.NrAnzeige(nr)} {gesetz}";
        try
        {
            using HttpResponseMessage a = await Http.GetAsync(Portal + Uri.EscapeDataString(zitat));
            if (a.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if ((int)a.StatusCode == 429) throw new InvalidDataException("Das Tageskontingent für kvasir ist erschöpft; bitte später erneut versuchen.");
            if (!a.IsSuccessStatusCode) throw new InvalidDataException($"kvasir ist nicht erreichbar ({(int)a.StatusCode}).");
            return AusJson(await a.Content.ReadAsStringAsync(), gesetz, nr);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidDataException("kvasir ist nicht erreichbar: " + ex.Message, ex);
        }
    }

    /// <summary>Antwort von kvasir (/api/v1/resolve) in eine Norm umsetzen.</summary>
    public static Norm? AusJson(string json, string gesetz, string nr)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement w = doc.RootElement;
        if (!w.TryGetProperty("exists", out JsonElement e) || e.ValueKind != JsonValueKind.True) return null;
        if (!w.TryGetProperty("object", out JsonElement o)) return null;
        string titel = Text(o, "content", "label_de") ?? Text(o, "content", "label") ?? Text(o, "identity", "title") ?? string.Empty;
        // „Artikel 6 – Rechtmäßigkeit …“ → „Rechtmäßigkeit …“
        titel = Regex.Replace(titel, @"^(§+|Art\.?|Artikel)\s*\d+[a-z]?\s*[–—:-]?\s*", string.Empty).Trim();
        string text = Text(o, "content", "html") is { Length: > 0 } html
            ? EurLex.Text(html)
            : Absaetze(Text(o, "content", "plain_text") ?? string.Empty);
        // Überschrift aus dem Text entfernen („Artikel 6“, Titel).
        var zeilen = text.Split('\n').ToList();
        while (zeilen.Count > 1 && ((Regex.IsMatch(zeilen[0], @"^(§+|Art\.?|Artikel)\s*\d+[a-z]?(\s|$)") && (titel.Length == 0 || zeilen[0].EndsWith(titel, StringComparison.Ordinal)
                                       || Regex.IsMatch(zeilen[0], @"^(§+|Art\.?|Artikel)\s*\d+[a-z]?$")))
                                     || (titel.Length > 0 && zeilen[0] == titel)))
            zeilen.RemoveAt(0);
        string quelle = (Text(o, "provenance", "source") ?? "kvasir") + " über kvasir.legal";
        if (Text(o, "provenance", "retrieved_at") is { Length: >= 10 } stand) quelle += $" (Stand {stand[8..10]}.{stand[5..7]}.{stand[..4]})";
        return new Norm { Gesetz = gesetz, Nr = nr, Titel = titel, Text = string.Join("\n", zeilen), Quelle = quelle };
    }

    static string? Text(JsonElement o, string a, string b) =>
        o.TryGetProperty(a, out JsonElement x) && x.ValueKind == JsonValueKind.Object && x.TryGetProperty(b, out JsonElement y) && y.ValueKind == JsonValueKind.String
            ? y.GetString()
            : null;

    /// <summary>Fließtext ohne Zeilen: vor „(2)“, „1.“ und „a)“ umbrechen.</summary>
    static string Absaetze(string t) =>
        Regex.Replace(t, @"\s+(?=\(\d+[a-z]?\)\s|\b\d+[a-z]?\.\s+[A-ZÄÖÜa-zäöü]|\b[a-z]\)\s)", "\n").Trim();
}
