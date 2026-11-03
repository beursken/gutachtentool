using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gutachten2025.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Gutachten2025.ViewModels;

/// <summary>Was die Normenansicht der Leiste zeigt (ein Eintrag im Verlauf Zurück/Vor).</summary>
public enum NormenArt { Verzeichnis, Normen, Treffer, Gesetz }

public sealed record NormenAnsicht(NormenArt Art, IReadOnlyList<NormVerweis> Verweise, string Suchtext = "", string Gesetz = "",
    int Von = 0, int Bis = 0, string Ziel = "", bool NurUeberschrift = false);

/// <summary>
/// Normen in der linken Leiste: Normensammlung aus dem Paket (Normen.json), eigene Normen (EigeneNormen.md) und nachgeladene
/// Gesetze (Bundesrecht: gesetze-im-internet.de, EU-Recht: EUR-Lex, ganz; sonst einzelne Normen über kvasir), zwischengespeichert
/// unter Vorlagen\Gesetze. Hinweis beim Überfahren (zitierter Absatz/Satz/Nummer/Buchstabe), Volltext per (Strg+)Klick mit
/// Hervorhebung, Durchblättern des Gesetzes, Suche, Verlauf Zurück/Vor.
/// Außerdem Gliederungsnavigation: Strg+Klick auf „Frage 1“ springt zur passenden Überschrift der Lösung.
/// </summary>
public sealed partial class MainViewModel
{
    const int FensterVor = 4, FensterNach = 25, FensterSchritt = 30, NormenVerlaufMax = 60;
    const string Steuerung = "#fenster", Laden = "#laden";

    Normensammlung _normen = new();
    NormenAnsicht _ansicht = new(NormenArt.Verzeichnis, Array.Empty<NormVerweis>());
    readonly List<NormenAnsicht> _normenVerlauf = new();
    int _normenVerlaufPos = -1;
    string _normenKopf = string.Empty;

    [ObservableProperty] public partial string NormenSuchtext { get; set; }

    [ObservableProperty] public partial bool NormenBeschaeftigt { get; set; }

    /// <summary>Die Oberfläche springt zur Überschrift der Lösung, die den Begriff enthält (Gliederungsnavigation).</summary>
    public event Action<string>? UeberschriftAnspringen;

    /// <summary>
    /// Die Oberfläche bringt die gezeigte Norm in den Blick: zuerst die hervorgehobene Stelle (&lt;mark class="hervor"&gt;),
    /// sonst die Überschrift, die mit dem Zitat beginnt (zweiter Wert: nur die Überschrift).
    /// </summary>
    public event Action<string, bool>? NormAnspringen;

    public int NormenAnzahl => _normen.Anzahl;

    public bool KannNormenZurueck => _normenVerlaufPos > 0;

    public bool KannNormenVor => _normenVerlaufPos >= 0 && _normenVerlaufPos < _normenVerlauf.Count - 1;

    void NormensammlungLaden()
    {
        try
        {
            _normen = Normensammlung.Laden(KlausurName.Length > 0 ? _ablage.KlausurOrdner(KlausurName) : null, _ablage.VorlagenOrdner);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            _normen = new();
        }
        _ansicht = new(NormenArt.Verzeichnis, Array.Empty<NormVerweis>());
        _normenVerlauf.Clear();
        _normenVerlaufPos = -1;
        _normenKopf = string.Empty;
        VerlaufGeaendert();
        OnPropertyChanged(nameof(NormenAnzahl));
    }

    /// <summary>Hinweistext zu einem Zitat (für title-Attribut bzw. Tooltip); bei „Abs. 2 S. 1“ nur dieser Teil.</summary>
    string? NormHinweis(NormVerweis v) =>
        _normen.Finden(v.Gesetz, v.Nr) is { } n
            ? Normensammlung.Kurztext(n, 700, v.Teil)
            : $"{v.Zitat}: Text noch nicht geladen – (Strg+)Klick lädt ihn (gesetze-im-internet.de, EUR-Lex oder kvasir).";

    /// <summary>Zitat an einer Stelle des Entwurfs (Mauszeiger); null = keines.</summary>
    public NormVerweis? NormVerweisAn(string text, int stelle) => Normverweise.An(text, stelle);

    /// <summary>Hinweistext zum Zitat an einer Stelle; null = keines.</summary>
    public string? NormHinweisAn(string text, int stelle) => NormVerweisAn(text, stelle) is { } v ? NormHinweis(v) : null;

    /// <summary>Gliederungsbegriff („Frage 1“) an einer Stelle; null = keiner.</summary>
    public string? GliederungsbegriffAn(string text, int stelle) => TextNormalizer.GliederungsbegriffAn(text, stelle);

    /// <summary>Strg+Klick auf „Frage 1“ usw.: Lösung in der Leiste zeigen und zur passenden Überschrift springen.</summary>
    public void GliederungZeigen(string begriff)
    {
        Leiste = HatLoesung ? Leistenanzeige.Loesung : HatSachverhalt ? Leistenanzeige.Sachverhalt : Leistenanzeige.Loesung;
        if (!BausteineOben) BausteineOffen = true;
        UeberschriftAnspringen?.Invoke(begriff);
    }

    /// <summary>Strg+Klick im Entwurf: Zitat (mit Absatz/Satz …) in der Leiste zeigen.</summary>
    public Task NormZeigenAsync(NormVerweis v) => NormenZeigenAsync(new List<NormVerweis> { v });

    /// <summary>
    /// Klick auf ein Zitat bzw. eine Steuerverknüpfung in der Leiste. <paramref name="angabe"/> ist das data-nr-Attribut
    /// („§ 433“ oder „§ 433|Abs. 2 S. 1“); Steuerverknüpfungen: „#fenster“ (Gesetz|von|bis|Ziel) und „#laden“ (Gesetz|Nr).
    /// </summary>
    public async Task NormZeigenAsync(string gesetz, string angabe)
    {
        string[] t = angabe.Split('|');
        if (gesetz == Steuerung && t.Length >= 4
            && int.TryParse(t[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int von)
            && int.TryParse(t[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bis))
        {
            Gehe(new NormenAnsicht(NormenArt.Gesetz, Array.Empty<NormVerweis>(), Gesetz: t[0], Von: von, Bis: bis, Ziel: t[3], NurUeberschrift: true));
            return;
        }
        if (gesetz == Laden && t.Length >= 2)
        {
            if (await GesetzLadenAsync(t[0], erzwingen: true)) await NormenZeigenAsync(new List<NormVerweis> { new(0, 0, t[0], t[1]) });
            return;
        }
        await NormenZeigenAsync(new List<NormVerweis> { Normverweise.AusAngabe(gesetz, angabe) });
    }

    async Task NormenZeigenAsync(List<NormVerweis> verweise)
    {
        foreach (var gruppe in verweise.GroupBy(v => v.Gesetz, StringComparer.OrdinalIgnoreCase))
            await BeschaffenAsync(gruppe.Key, gruppe.Where(v => _normen.Finden(v.Gesetz, v.Nr) is null).ToList());
        var fehlend = verweise.Where(v => _normen.Finden(v.Gesetz, v.Nr) is null).Select(v => v.Zitat).Distinct().ToList();
        _normenKopf = fehlend.Count > 0 ? $"Nicht gefunden: {string.Join(", ", fehlend)}." : string.Empty;
        var gefunden = verweise.Where(v => _normen.Finden(v.Gesetz, v.Nr) is not null).DistinctBy(v => v.Schluessel).ToList();
        // Eine Norm eines vollständig geladenen Gesetzes: im Zusammenhang zeigen (durchblätterbar).
        if (gefunden.Count == 1 && _normen.Folge(gefunden[0].Gesetz) is { } folge
            && folge.FindIndex(n => n.Schluessel == gefunden[0].Schluessel) is var k and >= 0)
        {
            Gehe(new NormenAnsicht(NormenArt.Gesetz, gefunden, Gesetz: gefunden[0].Gesetz,
                Von: Math.Max(0, k - FensterVor), Bis: Math.Min(folge.Count, k + FensterNach), Ziel: folge[k].Zitat));
            return;
        }
        Gehe(new NormenAnsicht(NormenArt.Normen, gefunden, Ziel: gefunden.Count > 0 ? gefunden[0].Zitat : string.Empty));
    }

    /// <summary>Fehlende Normen eines Gesetzes besorgen: ganzes Gesetz (Bund, EU), sonst einzeln über kvasir.</summary>
    async Task BeschaffenAsync(string gesetz, List<NormVerweis> fehlend)
    {
        if (fehlend.Count == 0) return;
        if (!_normen.GesetzGeladen(gesetz, _ablage.VorlagenOrdner) && await GesetzLadenAsync(gesetz, stumm: true)
            && fehlend.All(v => _normen.Finden(v.Gesetz, v.Nr) is not null))
            return;
        NormenBeschaeftigt = true;
        try
        {
            foreach (NormVerweis v in fehlend.Where(v => _normen.Finden(v.Gesetz, v.Nr) is null).Take(10))
            {
                Norm? n = await Kvasir.NormAsync(v.Gesetz, v.Nr);
                if (n is not null) _normen.EinzelnSpeichern(_ablage.VorlagenOrdner, n);
            }
            OnPropertyChanged(nameof(NormenAnzahl));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _ui.Melden(Meldungsart.Warnung, "Norm nicht geladen", ex.Message);
        }
        finally
        {
            NormenBeschaeftigt = false;
        }
    }

    /// <summary>Lädt ein ganzes Gesetz (gesetze-im-internet.de bzw. EUR-Lex) in den Zwischenspeicher (für alle Klausuren).</summary>
    async Task<bool> GesetzLadenAsync(string gesetz, bool stumm = false, bool erzwingen = false)
    {
        if (!erzwingen && _normen.GesetzGeladen(gesetz, _ablage.VorlagenOrdner)) return true;
        NormenBeschaeftigt = true;
        string quelle = EurLex.Celex(gesetz) is null ? "gesetze-im-internet.de" : "EUR-Lex";
        try
        {
            _ui.Melden(Meldungsart.Information, "Gesetz wird geladen", $"{gesetz} von {quelle} …");
            List<Norm> normen = await Gesetzesquelle.LadenAsync(gesetz);
            _normen.GesetzSpeichern(_ablage.VorlagenOrdner, gesetz, normen);
            OnPropertyChanged(nameof(NormenAnzahl));
            _ui.Melden(Meldungsart.Erfolg, "Gesetz geladen", $"{gesetz}: {normen.Count} Vorschriften; steht künftig auch ohne Internet zur Verfügung.");
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            if (!stumm)
                _ui.Melden(Meldungsart.Warnung, "Gesetz nicht geladen",
                    ex.Message + " Landesrecht und andere Normen lassen sich einzeln (über kvasir) nachschlagen oder als eigene Normen ergänzen.");
            return false;
        }
        finally
        {
            NormenBeschaeftigt = false;
        }
    }

    /// <summary>Suche: Zitate („§ 433 BGB“, „Art. 6 Abs. 1 lit. f DSGVO“) zeigen den Volltext, sonst Volltextsuche in den geladenen Normen.</summary>
    [RelayCommand]
    async Task NormenSuchenAsync()
    {
        string s = (NormenSuchtext ?? string.Empty).Trim();
        if (s.Length == 0)
        {
            NormenVerzeichnis();
            return;
        }
        List<NormVerweis> verweise = Normverweise.Finden(s);
        if (verweise.Count > 0)
        {
            await NormenZeigenAsync(verweise);
            return;
        }
        // „BGB“ allein: Gesetz von vorn durchblättern.
        if (!s.Contains(' ') && Normverweise.IstGesetzkuerzel(s) && (_normen.Folge(s) is not null || await GesetzLadenAsync(s, stumm: true)) && _normen.Folge(s) is { Count: > 0 } folge)
        {
            _normenKopf = string.Empty;
            Gehe(new NormenAnsicht(NormenArt.Gesetz, Array.Empty<NormVerweis>(), Gesetz: s, Von: 0, Bis: Math.Min(folge.Count, FensterSchritt), Ziel: folge[0].Zitat));
            return;
        }
        _normenKopf = string.Empty;
        Gehe(new NormenAnsicht(NormenArt.Treffer, Array.Empty<NormVerweis>(), Suchtext: s));
    }

    /// <summary>Verzeichnis der Normensammlung (Ausgangsansicht).</summary>
    [RelayCommand]
    void NormenVerzeichnis()
    {
        _normenKopf = string.Empty;
        Gehe(new NormenAnsicht(NormenArt.Verzeichnis, Array.Empty<NormVerweis>()));
    }

    [RelayCommand(CanExecute = nameof(KannNormenZurueck))]
    void NormenZurueck()
    {
        if (!KannNormenZurueck) return;
        _normenVerlaufPos--;
        _normenKopf = string.Empty;
        Zeigen(_normenVerlauf[_normenVerlaufPos]);
    }

    [RelayCommand(CanExecute = nameof(KannNormenVor))]
    void NormenVor()
    {
        if (!KannNormenVor) return;
        _normenVerlaufPos++;
        _normenKopf = string.Empty;
        Zeigen(_normenVerlauf[_normenVerlaufPos]);
    }

    void Gehe(NormenAnsicht ansicht)
    {
        if (_normenVerlaufPos >= 0 && _normenVerlaufPos < _normenVerlauf.Count && _normenVerlauf[_normenVerlaufPos] == ansicht)
        {
            Zeigen(ansicht);
            return;
        }
        if (_normenVerlaufPos < _normenVerlauf.Count - 1) _normenVerlauf.RemoveRange(_normenVerlaufPos + 1, _normenVerlauf.Count - _normenVerlaufPos - 1);
        _normenVerlauf.Add(ansicht);
        if (_normenVerlauf.Count > NormenVerlaufMax) _normenVerlauf.RemoveAt(0);
        _normenVerlaufPos = _normenVerlauf.Count - 1;
        Zeigen(ansicht);
    }

    void Zeigen(NormenAnsicht ansicht)
    {
        _ansicht = ansicht;
        VerlaufGeaendert();
        if (Leiste != Leistenanzeige.Normen) Leiste = Leistenanzeige.Normen; // lädt und zeigt
        else UnterlagenLaden();
        if (!BausteineOben) BausteineOffen = true;
        if (ansicht.Ziel.Length > 0) NormAnspringen?.Invoke(ansicht.Ziel, ansicht.NurUeberschrift);
    }

    void VerlaufGeaendert()
    {
        OnPropertyChanged(nameof(KannNormenZurueck));
        OnPropertyChanged(nameof(KannNormenVor));
        NormenZurueckCommand.NotifyCanExecuteChanged();
        NormenVorCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Steuerverknüpfung als HTML (wird nicht nochmals als Zitat ausgezeichnet).</summary>
    static string Verknuepfung(string art, string angabe, string text, string hinweis) =>
        $"<p><span class=\"norm\" data-gesetz=\"{WebUtility.HtmlEncode(art)}\" data-nr=\"{WebUtility.HtmlEncode(angabe)}\" " +
        $"title=\"{WebUtility.HtmlEncode(hinweis)}\">{WebUtility.HtmlEncode(text)}</span></p>\n";

    static string NormFenster(string gesetz, int von, int bis, string ziel) =>
        string.Join("|", gesetz, von.ToString(CultureInfo.InvariantCulture), bis.ToString(CultureInfo.InvariantCulture), ziel);

    string NormenHtml()
    {
        var md = new StringBuilder();
        if (_normenKopf.Length > 0) md.Append(MarkdownHtml.Maskieren(_normenKopf)).Append("\n\n");
        switch (_ansicht.Art)
        {
            case NormenArt.Gesetz when _normen.Folge(_ansicht.Gesetz) is { Count: > 0 } folge:
            {
                int von = Math.Clamp(_ansicht.Von, 0, folge.Count - 1), bis = Math.Clamp(_ansicht.Bis, von + 1, folge.Count);
                NormVerweis? ziel = _ansicht.Verweise.FirstOrDefault();
                string kopf = Normverweise.NrAnzeige(folge[von].Nr) + (bis - 1 > von ? " bis " + Normverweise.NrAnzeige(folge[bis - 1].Nr) : string.Empty);
                md.Append("**").Append(MarkdownHtml.Maskieren(folge[0].Gesetz)).Append("** – ").Append(MarkdownHtml.Maskieren(kopf))
                  .Append(" (von ").Append(folge.Count).Append(" Vorschriften)\n\n");
                string oben = von > 0
                    ? Verknuepfung(Steuerung, NormFenster(_ansicht.Gesetz, Math.Max(0, von - FensterSchritt), bis, folge[von].Zitat),
                        "▲ frühere Vorschriften", $"{Normverweise.NrAnzeige(folge[Math.Max(0, von - FensterSchritt)].Nr)} bis {Normverweise.NrAnzeige(folge[von - 1].Nr)} einblenden")
                    : string.Empty;
                for (int i = von; i < bis; i++)
                {
                    NormTeil? teil = ziel is not null && folge[i].Schluessel == ziel.Schluessel ? ziel.Teil : null;
                    md.Append(Normensammlung.AlsMarkdown(folge[i], teil)).Append("\n---\n\n");
                }
                string unten = bis < folge.Count
                    ? Verknuepfung(Steuerung, NormFenster(_ansicht.Gesetz, von, Math.Min(folge.Count, bis + FensterSchritt), folge[bis - 1].Zitat),
                        "▼ weitere Vorschriften", $"{Normverweise.NrAnzeige(folge[bis].Nr)} bis {Normverweise.NrAnzeige(folge[Math.Min(folge.Count, bis + FensterSchritt) - 1].Nr)} einblenden")
                    : string.Empty;
                return oben + Gesetzestext(md.ToString(), _ansicht.Gesetz) + unten;
            }
            case NormenArt.Normen or NormenArt.Gesetz:
            {
                var html = new StringBuilder();
                md.Clear();
                foreach (NormVerweis v in _ansicht.Verweise)
                {
                    if (_normen.Finden(v.Gesetz, v.Nr) is not { } n) continue;
                    md.Append(Normensammlung.AlsMarkdown(n, v.Teil)).Append('\n');
                    html.Append(Gesetzestext(md.ToString(), n.Gesetz));
                    md.Clear();
                    // Im Gesetz blättern: geladen → Ausschnitt, sonst laden (nur Bundes- und EU-Recht).
                    if (_normen.Folge(n.Gesetz) is { } folge && folge.FindIndex(x => x.Schluessel == n.Schluessel) is var k and >= 0)
                        html.Append(Verknuepfung(Steuerung, NormFenster(n.Gesetz, Math.Max(0, k - FensterVor), Math.Min(folge.Count, k + FensterNach), n.Zitat),
                            "↕ im Gesetz blättern", "Vorschriften davor und danach zeigen"));
                    else if (n.Quelle != "eigene Norm")
                        html.Append(Verknuepfung(Laden, n.Gesetz + "|" + n.Nr, "↕ ganzes Gesetz laden und blättern",
                            $"{n.Gesetz} vollständig laden (gesetze-im-internet.de bzw. EUR-Lex); danach auch ohne Internet verfügbar"));
                    html.Append("<hr/>\n");
                }
                return (_normenKopf.Length > 0 ? MarkdownHtml.Umwandeln(MarkdownHtml.Maskieren(_normenKopf)) : string.Empty) + html;
            }
            case NormenArt.Treffer:
            {
                List<Norm> treffer = _normen.Suchen(_ansicht.Suchtext);
                md.Append(treffer.Count == 0
                    ? $"Keine Treffer für „{MarkdownHtml.Maskieren(_ansicht.Suchtext)}“ in {_normen.Anzahl} geladenen Vorschriften. Normen eines anderen Gesetzes: Zitat eingeben, z. B. „§ 1004 BGB“; ganzes Gesetz: Kürzel eingeben, z. B. „GmbHG“.\n\n"
                    : $"{treffer.Count} Treffer für „{MarkdownHtml.Maskieren(_ansicht.Suchtext)}“" + (treffer.Count >= 40 ? " (die ersten 40)" : string.Empty) + ":\n\n");
                foreach (Norm n in treffer)
                {
                    string auszug = n.Text.Replace('\n', ' ');
                    if (auszug.Length > 220) auszug = auszug[..220].TrimEnd() + " …";
                    md.Append("- **").Append(MarkdownHtml.Maskieren(n.Zitat)).Append("** ").Append(MarkdownHtml.Maskieren(n.Titel)).Append("  \n")
                      .Append(MarkdownHtml.Maskieren(auszug)).Append('\n');
                }
                return MarkdownHtml.Umwandeln(md.ToString());
            }
        }
        // Verzeichnis
        if (_normen.Paketnormen.Count > 0) Liste("Normensammlung der Klausur", _normen.Paketnormen);
        if (_normen.Eigene.Count > 0) Liste("Eigene Normen", _normen.Eigene);
        if (_normen.Paketnormen.Count == 0 && _normen.Eigene.Count == 0)
            md.Append("Diese Klausur hat keine Normensammlung (Pakete aus dem KI-Portal bringen die zitierten Normen mit).\n\n");
        md.Append("Suche: Zitat wie „§ 433 Abs. 2 BGB“ oder „Art. 6 Abs. 1 lit. f DSGVO“ zeigt die Norm mit hervorgehobener Stelle; ")
          .Append("ein Kürzel wie „GmbHG“ zeigt das Gesetz zum Durchblättern; sonst werden die geladenen Normen durchsucht. ")
          .Append("Bundesrecht lädt das Programm von gesetze-im-internet.de, EU-Recht von EUR-Lex, Übriges (z. B. Landesrecht) einzeln über kvasir. ")
          .Append("Im Gutachtentext: Maus auf ein Zitat zeigt den Text, Strg+Klick öffnet ihn hier; Strg+Klick auf „Frage 1“, „Tatkomplex 2“ usw. springt zur Stelle in der Lösung. ")
          .Append("„Bearbeiten“ ergänzt eigene Normen (Markdown, z. B. Prüfungsordnung).\n\n");
        string html2 = MarkdownHtml.Umwandeln(md.ToString());
        var geladen = _normen.Alle.Select(n => n.Gesetz).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(g => _normen.Folge(g) is not null).OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (geladen.Count > 0)
        {
            html2 += "<h4>Geladene Gesetze (durchblättern)</h4>\n";
            foreach (string g in geladen)
            {
                List<Norm> folge = _normen.Folge(g)!;
                html2 += Verknuepfung(Steuerung, NormFenster(g, 0, Math.Min(folge.Count, FensterSchritt), folge[0].Zitat), g, $"{folge.Count} Vorschriften");
            }
        }
        return html2;

        void Liste(string titel, List<Norm> normen)
        {
            md.Append("**").Append(titel).Append("** (").Append(normen.Count).Append(" Vorschriften; Klick zeigt den Text)\n\n");
            foreach (var gruppe in normen.GroupBy(n => n.Gesetz, StringComparer.OrdinalIgnoreCase))
            {
                md.Append("#### ").Append(MarkdownHtml.Maskieren(gruppe.Key)).Append("\n\n");
                foreach (Norm n in gruppe.OrderBy(n => Normensammlung.Sortierzahl(n.Nr)))
                    md.Append("- ").Append(MarkdownHtml.Maskieren(n.Zitat)).Append(n.Titel.Length > 0 ? " – " + MarkdownHtml.Maskieren(n.Titel) : string.Empty).Append('\n');
                md.Append('\n');
            }
        }
    }

    /// <summary>Normtext: Markdown → HTML, Zitate im Text (ohne Gesetz: dasselbe Gesetz) auszeichnen, Fundstelle hervorheben.</summary>
    string Gesetzestext(string md, string gesetz)
    {
        string html = Normverweise.Auszeichnen(MarkdownHtml.Umwandeln(md), NormHinweis, gesetz);
        string mit = Normstelle.Hervorheben(html);
        try
        {
            _ = System.Xml.Linq.XElement.Parse("<div>" + mit + "</div>");
            return mit;
        }
        catch (System.Xml.XmlException)
        {
            // Hervorhebung überschneidet sich mit einem Zitat: ohne Hervorhebung zeigen.
            return html.Replace(Normstelle.Anfang.ToString(), string.Empty).Replace(Normstelle.Ende.ToString(), string.Empty);
        }
    }
}
