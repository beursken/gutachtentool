using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gutachten2025.Models;
using Gutachten2025.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Gutachten2025.ViewModels;

/// <summary>
/// Zweitkorrekturmodus: aktiv, sobald die Klausur Erstnoten hat (Paket oder Teilnehmendenliste mit Noten) oder als
/// Zweitkorrektur angelegt wurde. Zeigt die Erstnote über der Notenauswahl, wählt den Schlusssatz nach Vergleich der
/// Notenstufen und bietet „Uneingeschränkte Zustimmung“ (statt des Entwurfs der Zustimmungstext).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Zweitkorrekturmodus (Erstnoten vorhanden oder Klausur als Zweitkorrektur angelegt).</summary>
    public bool Zweitkorrekturmodus => HatErstnoten || _konfiguration.Zweitkorrektur;

    /// <summary>Überschrift der Notenauswahl, im Zweitkorrekturmodus mit der Note des Erstgutachtens.</summary>
    public string NotenKopf => !Zweitkorrekturmodus
        ? "Note"
        : Erstnote is int e
            ? $"Note · Erstgutachten: {e} – {Notenskala.Bezeichnung(e)}"
            : Kennung.Length > 0 && HatErstnoten ? "Note · keine Erstnote zu dieser Kennziffer" : "Note (Zweitgutachten)";

    /// <summary>Statt des Entwurfs erscheint im Gutachten der Zustimmungstext (der Entwurf bleibt erhalten).</summary>
    [ObservableProperty]
    public partial bool UneingeschraenkteZustimmung { get; set; }

    public string ZustimmungHinweis => $"Im Gutachten steht statt des Entwurfs: „{Zweitkorrektur.Zustimmungstext}“";

    /// <summary>Schlusssatz, wie er im Gutachten erscheint (Vorschau unter der Note).</summary>
    public string SchlusssatzVorschau => Zweitkorrekturmodus && Erstnote is int e
        ? Zweitkorrektur.Schlusssatz(NotenIndex, e)
        : string.Empty;

    /// <summary>Bei uneingeschränkter Zustimmung gilt die Erstnote; die Notenauswahl ist gesperrt.</summary>
    public bool NoteGesperrt => UneingeschraenkteZustimmung && Erstnote is not null;

    partial void OnUneingeschraenkteZustimmungChanged(bool value)
    {
        NoteAnErstnoteBinden();
        OnPropertyChanged(nameof(NoteGesperrt));
        if (_initialisiert) _entwurfGeaendert = true;
    }

    void NoteAnErstnoteBinden()
    {
        if (UneingeschraenkteZustimmung && Erstnote is int e && NotenIndex != e) NotenIndex = e; // gilt als gewählt
    }

    void ZweitkorrekturAktualisieren()
    {
        OnPropertyChanged(nameof(Zweitkorrekturmodus));
        OnPropertyChanged(nameof(NotenKopf));
        OnPropertyChanged(nameof(SchlusssatzVorschau));
        OnPropertyChanged(nameof(NoteGesperrt));
        OnPropertyChanged(nameof(DrittgutachtenErforderlich));
        OnPropertyChanged(nameof(DrittgutachtenHinweis));
        OnPropertyChanged(nameof(EinigungSichtbar));
        if (!Zweitkorrekturmodus && UneingeschraenkteZustimmung) UneingeschraenkteZustimmung = false;
        NoteAnErstnoteBinden();
    }

    // ---------- Drittgutachten / Einigung ----------

    const string Vermerkmarke = "###VERMERK###", Einigungsmarke = "###EINIGUNG###";

    /// <summary>Auswahl der Einigungsnote: Index 0 = keine Einigung, 1–19 = 0–18 Punkte.</summary>
    public IReadOnlyList<string> EinigungsAuswahl { get; } =
        new[] { "– keine Einigung –" }.Concat(Notenskala.Auswahl).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Einigungsnote), nameof(EinigungSichtbar), nameof(VermerkText))]
    public partial int EinigungsIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VermerkText))]
    public partial DateTimeOffset? Gespraechsdatum { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VermerkText))]
    public partial bool ErstgutachterZugestimmt { get; set; }

    public int? Einigungsnote => EinigungsIndex > 0 ? EinigungsIndex - 1 : null;

    /// <summary>Abweichung von mehr als 3 Punkten zur Erstnote: Drittgutachten erforderlich, sofern keine Einigung.</summary>
    public bool DrittgutachtenErforderlich => Erstnote is int e && Math.Abs(NotenIndex - e) > 3;

    public bool EinigungSichtbar => Zweitkorrekturmodus && (DrittgutachtenErforderlich || EinigungsIndex > 0);

    public bool EinigungVollstaendig => Einigungsnote is not null && Gespraechsdatum is not null && ErstgutachterZugestimmt;

    public string DrittgutachtenHinweis => Erstnote is int e && DrittgutachtenErforderlich
        ? $"Abweichung von {Math.Abs(NotenIndex - e)} Punkten zum Erstgutachten ({Notenskala.MitPunkten(e)}): Ohne Einigung ist ein Drittgutachten erforderlich. " +
          "Bitte Rücksprache mit dem Erstkorrektor halten und eine etwaige Einigung hier vermerken."
        : string.Empty;

    /// <summary>Vermerk über die Einigung (Platzhalter ###VERMERK###, sonst als letzter Absatz); leer ohne vollständige Angaben.</summary>
    public string VermerkText => EinigungVollstaendig
        ? $"Vermerk: Nach Rücksprache mit dem Erstgutachter am {Gespraechsdatum!.Value:dd.MM.yyyy} haben sich Erst- und Zweitgutachter auf die Note " +
          $"{Notenskala.MitPunkten(Einigungsnote!.Value)} geeinigt; der Erstgutachter hat der Einigung zugestimmt."
        : string.Empty;

    void EinigungLeeren()
    {
        EinigungsIndex = 0;
        Gespraechsdatum = null;
        ErstgutachterZugestimmt = false;
    }

    void EinigungSetzen(int? note, DateTime? datum, bool zugestimmt)
    {
        EinigungsIndex = note is int n && Notenskala.IstGueltig(n) ? n + 1 : 0;
        Gespraechsdatum = datum is DateTime d ? new DateTimeOffset(d) : null;
        ErstgutachterZugestimmt = zugestimmt;
    }

    /// <summary>Vor dem Erstellen: Drittgutachten bzw. unvollständiger Vermerk; false = zurück.</summary>
    async Task<bool> DrittgutachtenPruefenAsync(int punkte)
    {
        if (Erstnote is not int erst) return true;
        if (DrittgutachtenErforderlich && !EinigungVollstaendig)
        {
            bool ok = await _ui.BestaetigenAsync("Drittgutachten erforderlich",
                $"Erstgutachten: {Notenskala.MitPunkten(erst)}\nZweitgutachten: {Notenskala.MitPunkten(punkte)}\n\n" +
                $"Die Noten weichen um {Math.Abs(punkte - erst)} Punkte (mehr als 3) voneinander ab. Ohne Einigung ist ein Drittgutachten erforderlich; " +
                "bitte Rücksprache mit dem Erstkorrektor halten. Eine Einigung lässt sich unter der Note vermerken (Einigungsnote, Datum des Gesprächs, Zustimmung).\n\n" +
                "Trotzdem ohne Einigungsvermerk erstellen?",
                "Trotzdem erstellen", "Zurück zur Note");
            if (!ok) _ui.Fokussieren(Eingabefeld.Note);
            return ok;
        }
        if (EinigungsIndex > 0 && !EinigungVollstaendig)
        {
            bool ok = await _ui.BestaetigenAsync("Einigungsvermerk unvollständig",
                "Zur Einigungsnote fehlen das Datum des Gesprächs oder die Zustimmung des Erstgutachters; der Vermerk erscheint dann nicht im Gutachten.\n\nTrotzdem erstellen?",
                "Trotzdem erstellen", "Zurück");
            return ok;
        }
        return true;
    }

    // ---------- Statistik der Zweitkorrektur ----------

    public ObservableCollection<AbweichungsBalken> Abweichungen { get; } = new();

    public bool HatAbweichungen => Abweichungen.Count > 0;

    const int AbweichungsRand = 6;

    void AbweichungenAktualisieren(IReadOnlyDictionary<string, int> noten)
    {
        Abweichungen.Clear();
        List<int> diff = Differenzen(noten);
        if (diff.Count > 0)
        {
            var anzahl = new Dictionary<int, int>();
            foreach (int d in diff)
            {
                int k = Math.Clamp(d, -AbweichungsRand, AbweichungsRand);
                anzahl[k] = anzahl.GetValueOrDefault(k) + 1;
            }
            int max = Math.Max(1, anzahl.Values.Max());
            for (int d = -AbweichungsRand; d <= AbweichungsRand; d++)
            {
                int n = anzahl.GetValueOrDefault(d);
                Abweichungen.Add(new AbweichungsBalken(d, n, n == 0 ? 1 : Math.Max(3, 38.0 * n / max), Math.Abs(d) == AbweichungsRand));
            }
        }
        OnPropertyChanged(nameof(HatAbweichungen));
    }

    List<int> Differenzen(IReadOnlyDictionary<string, int> noten) =>
        noten.Where(n => _erstnoten.ContainsKey(n.Key)).Select(n => n.Value - _erstnoten[n.Key]).ToList();

    /// <summary>Kennzahlen: mittlere Abweichung, Übereinstimmungsquote (Notenstufe/Punkte), Tendenz, Drittgutachten.</summary>
    IEnumerable<Kennzahl> ZweitkorrekturKennzahlen(IReadOnlyDictionary<string, int> noten)
    {
        var paare = noten.Where(n => _erstnoten.ContainsKey(n.Key)).Select(n => (Zweit: n.Value, Erst: _erstnoten[n.Key])).ToList();
        if (paare.Count == 0) yield break;
        var de = CultureInfo.GetCultureInfo("de-DE");
        List<int> diff = paare.Select(p => p.Zweit - p.Erst).ToList();
        double mittel = diff.Average(), betrag = diff.Average(d => Math.Abs(d));
        int stufe = paare.Count(p => Notenskala.Stufe(p.Zweit) == Notenskala.Stufe(p.Erst));
        int gleich = diff.Count(d => d == 0);
        int dritt = diff.Count(d => Math.Abs(d) > 3);
        yield return new Kennzahl("Mittlere Abweichung (Betrag)", $"{betrag.ToString("0.0", de)} P.");
        yield return new Kennzahl("Übereinstimmung Notenstufe", $"{Zahl.Prozent((double)stufe / paare.Count)} ({stufe}/{paare.Count})");
        yield return new Kennzahl("Übereinstimmung Punkte", $"{Zahl.Prozent((double)gleich / paare.Count)} ({gleich}/{paare.Count})");
        yield return new Kennzahl("Tendenz",
            Math.Abs(mittel) < 0.25 ? "ausgeglichen (Ø " + mittel.ToString("+0.0;−0.0;0.0", de) + ")"
            : mittel < 0 ? $"strenger (Ø {mittel.ToString("+0.0;−0.0;0.0", de)} P.)"
            : $"milder (Ø {mittel.ToString("+0.0;−0.0;0.0", de)} P.)");
        yield return new Kennzahl("Drittgutachten (> 3 Punkte)", $"{dritt} von {paare.Count}");
    }

    /// <summary>Platzhalter des Zweitgutachtens; die üblichen Schlusswendungen alter Vorlagen werden mit ersetzt.</summary>
    void ZweitkorrekturPlatzhalter(Dictionary<string, string> p, int punkte)
    {
        string schluss = Zweitkorrekturmodus ? Zweitkorrektur.Schlusssatz(punkte, Erstnote) : Zweitkorrektur.Schlusssatz(punkte, null);
        p[Zweitkorrektur.Schlussmarke] = schluss;
        p[Zweitkorrektur.Erstnotenmarke] = Erstnote is int e ? Notenskala.MitPunkten(e) : string.Empty;
        p[Vermerkmarke] = VermerkText;
        p[Einigungsmarke] = EinigungVollstaendig ? Notenskala.MitPunkten(Einigungsnote!.Value) : string.Empty;
        if (Zweitkorrekturmodus)
            foreach (string w in Zweitkorrektur.Schlusswendungen) p[w] = schluss;
    }

    /// <summary>„Zweitkorrektur anlegen“: neuer Klausurordner, Vorlage umstellen, Teilnehmendenliste mit Noten importieren.</summary>
    [RelayCommand]
    async Task ZweitkorrekturAnlegenAsync()
    {
        try
        {
            if (KlausurOrdnerOderMeldung() is null) return;
            string quelle = KlausurName;
            string name = Zweitkorrektur.Name(_ablage, quelle);
            if (!await _ui.BestaetigenAsync("Zweitkorrektur anlegen",
                    $"Neuer Klausurordner „{name}“ mit Word-Vorlage, Textbausteinen, Klausureinstellungen, Rohpunkteschema und Unterlagen von „{quelle}“ – " +
                    "ohne Noten, Gutachten, Entwürfe und Teilnehmende.\n\n" +
                    "In der Vorlage werden „Erstgutachten“ und „Erstvotum“ zu „Zweitgutachten“ und „Zweitvotum“; der Schlusssatz richtet sich " +
                    "danach nach dem Vergleich mit der Erstnote. Anschließend können Sie die Teilnehmendenliste mit den Noten des Erstgutachtens importieren.",
                    "Anlegen", "Abbrechen"))
                return;

            BausteineSpeichern();
            (string neu, Dictionary<string, int> ersetzt) = Zweitkorrektur.Anlegen(_ablage, quelle, _notenliste, _gutachten);
            KlausurenNeuLaden();
            KlausurWechseln(neu);
            _ui.Melden(Meldungsart.Erfolg, "Zweitkorrektur angelegt", $"„{neu}“: {Zweitkorrektur.Bericht(ersetzt)}");

            if (await _ui.BestaetigenAsync("Teilnehmendenliste mit Noten",
                    "Bitte jetzt die Liste mit den Noten des Erstgutachtens wählen (Excel, CSV oder Text): " +
                    "Spalte „Kennziffer“ (bzw. Matrikelnummer) und Spalte „Note“ bzw. „Punkte“ (0–18); ohne Überschriften Spalte 1 = Kennziffer, " +
                    "eine Spalte nur mit Punktwerten = Erstnote.",
                    "Liste wählen …", "Später"))
                await TeilnehmendeImportierenAsync();

            string vorlage = Path.Combine(_ablage.KlausurOrdner(KlausurName), KlausurAblage.WordVorlage);
            _ui.Oeffnen(vorlage);
        }
        catch (Exception ex)
        {
            Fehler(ex, "Zweitkorrektur nicht angelegt");
        }
    }

    /// <summary>Nach dem Import von Erstnoten: Vorlage auf Zweitgutachten umstellen, falls sie noch „Erstgutachten“ enthält.</summary>
    async Task VorlageAufZweitgutachtenAnbietenAsync(string ordner)
    {
        string vorlage = Path.Combine(ordner, KlausurAblage.WordVorlage);
        if (!File.Exists(vorlage) || _konfiguration.Zweitkorrektur) return;
        if (!await _ui.BestaetigenAsync("Vorlage umstellen?",
                "Die Klausur hat jetzt Erstnoten (Zweitkorrekturmodus). Soll die Word-Vorlage auf das Zweitgutachten umgestellt werden " +
                "(„Erstgutachten/Erstvotum“ → „Zweitgutachten/Zweitvotum“, Schlusssatz nach Notenvergleich)? Eine Sicherung der Vorlage bleibt im Ordner.",
                "Umstellen", "Nicht umstellen"))
            return;
        if (KlausurAblage.IstGesperrt(vorlage))
        {
            _ui.Melden(Meldungsart.Fehler, "Vorlage geöffnet", $"Bitte zuerst „{KlausurAblage.WordVorlage}“ in Word schließen.");
            return;
        }
        File.Copy(vorlage, KlausurAblage.Sicherungsname(ordner, KlausurAblage.WordVorlage));
        Dictionary<string, int> ersetzt = _gutachten.TexteErsetzen(vorlage, Zweitkorrektur.Vorlagenersetzungen);
        string pfad = _ablage.KonfigurationsPfad(KlausurName);
        Klausurkonfiguration k = KlausurKonfigDatei.Laden(pfad);
        k.Zweitkorrektur = true;
        KlausurKonfigDatei.Speichern(pfad, k);
        KonfigurationLaden();
        ZweitkorrekturAktualisieren();
        _ui.Melden(Meldungsart.Erfolg, "Vorlage umgestellt", Zweitkorrektur.Bericht(ersetzt), "Vorlage öffnen", () => _ui.Oeffnen(vorlage));
    }
}
