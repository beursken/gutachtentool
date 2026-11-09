using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gutachten2025.Models;
using Gutachten2025.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Gutachten2025.ViewModels;

/// <summary>
/// Korrekturbegleitung: Bearbeitungszeit je Arbeit und Restzeitprognose, Kennziffern als Bereich oder Liste,
/// Zweitgutachten (Zustimmung, Abweichung), Bausteinstatistik mit Rückmeldung an das KI-Portal,
/// Export für das Prüfungsportal und Hinweis auf neue Versionen.
/// </summary>
public sealed partial class MainViewModel
{
    // ---------- Bearbeitungszeit ----------

    DateTime? _arbeitBeginn;
    double _arbeitSekunden;
    DateTime _letzteAktivitaet;

    /// <summary>Eingabe zur aktuellen Arbeit (Editor, Note, Rohpunkte): Zeit fortschreiben; Pausen über drei Minuten zählen nicht.</summary>
    public void Aktivitaet()
    {
        if (!_initialisiert) return;
        DateTime jetzt = DateTime.Now;
        if (_arbeitBeginn is null)
        {
            _arbeitBeginn = jetzt;
        }
        else
        {
            TimeSpan abstand = jetzt - _letzteAktivitaet;
            if (abstand > TimeSpan.Zero && abstand <= Korrekturzeiten.Leerlauf) _arbeitSekunden += abstand.TotalSeconds;
        }
        _letzteAktivitaet = jetzt;
    }

    void ZeitZuruecksetzen()
    {
        _arbeitBeginn = null;
        _arbeitSekunden = 0;
    }

    /// <summary>Laufende Arbeit, z. B. „seit 14:03 · 12 Min.“ (für die Statuszeile).</summary>
    public string LaufendeZeit => _arbeitBeginn is DateTime b
        ? $"seit {b:HH:mm} · {Korrekturzeiten.Text(TimeSpan.FromSeconds(_arbeitSekunden))}"
        : string.Empty;

    void ZeitEintragen(string ordner, string kennung)
    {
        try
        {
            DateTime ende = DateTime.Now;
            Korrekturzeiten.Eintragen(ordner, kennung, _arbeitBeginn ?? ende, ende, _arbeitSekunden);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EinstellungsSpeicher.Protokollieren(ex); // die Zeiterfassung ist verzichtbar
        }
    }

    static string ZeitText(Korrekturzeit z) =>
        $"{z.Beginn:dd.MM. HH:mm}–{(z.Ende.Date == z.Beginn.Date ? z.Ende.ToString("HH:mm") : z.Ende.ToString("dd.MM. HH:mm"))} · {Korrekturzeiten.Text(z.Dauer)}";

    /// <summary>Kennzahlen zu Zeit und Zweitgutachten für die Übersicht.</summary>
    IEnumerable<Kennzahl> ZusatzKennzahlen(IReadOnlyDictionary<string, int> noten, IReadOnlyDictionary<string, Korrekturzeit> zeiten, int offen)
    {
        List<Korrekturzeit> erfasst = zeiten.Values.Where(z => noten.ContainsKey(z.Kennung)).ToList();
        if (Korrekturzeiten.Median(erfasst) is TimeSpan median)
        {
            yield return new Kennzahl("Bearbeitungszeit (Median)", Korrekturzeiten.Text(median));
            yield return new Kennzahl("Bisher insgesamt", Korrekturzeiten.Text(TimeSpan.FromSeconds(erfasst.Sum(z => z.Sekunden))));
            if (offen > 0)
                yield return new Kennzahl("Restzeit (Prognose)", $"ca. {Korrekturzeiten.Text(TimeSpan.FromSeconds(median.TotalSeconds * offen))} für {offen} Arbeiten");
        }
        if (HatErstnoten)
            foreach (Kennzahl k in ZweitkorrekturKennzahlen(noten)) yield return k;
    }

    // ---------- Kennziffern ----------

    /// <summary>Kennziffern als Bereich (mit Ausnahmen für nicht vergebene) oder als Liste eingeben.</summary>
    [RelayCommand]
    async Task KennziffernEingebenAsync()
    {
        try
        {
            if (KlausurOrdnerOderMeldung() is null) return;
            string? eingabe = await _ui.TextEingebenAsync("Kennziffern eingeben", string.Empty,
                "Bereiche und einzelne Kennziffern, getrennt durch Komma oder Zeilenumbruch – z. B. „1001-1060, 1075“ oder „K1-K40“");
            if (string.IsNullOrWhiteSpace(eingabe)) return;
            List<string> alle = Kennziffern.Lesen(eingabe);
            if (alle.Count == 0) return;

            string? ohne = await _ui.TextEingebenAsync("Nicht vergebene Kennziffern", string.Empty,
                $"{alle.Count} Kennziffern. Nicht vergebene entfernen (z. B. „1007, 1012-1014“) – leer lassen, wenn alle vergeben sind");
            if (ohne is null) return;
            int vorher = alle.Count;
            if (ohne.Trim().Length > 0) alle = Kennziffern.Ohne(alle, Kennziffern.Lesen(ohne));

            List<Teilnehmer> ergebnis = alle.Select(k => new Teilnehmer { Kennung = k }).ToList();
            if (_teilnehmende.Count > 0)
            {
                int wahl = await _ui.AuswahlAsync("Teilnehmendenliste vorhanden",
                    $"Es gibt bereits {_teilnehmende.Count} Einträge; neu sind {ergebnis.Count} Kennziffern.", "Ersetzen", "Ergänzen", "Abbrechen");
                if (wahl == 0) return;
                if (wahl == 2)
                {
                    var vorhanden = new HashSet<string>(_teilnehmende.Select(t => t.Kennung), StringComparer.OrdinalIgnoreCase);
                    ergebnis = _teilnehmende.Concat(ergebnis.Where(t => vorhanden.Add(t.Kennung))).ToList();
                }
            }
            string ordner = Path.Combine(_ablage.Wurzel, _ablage.KlausurEinrichten(KlausurName, _notenliste));
            Teilnehmerliste.Speichern(ordner, ergebnis);
            _teilnehmende = ergebnis;
            SeitenleisteOffen = true;
            UebersichtAktualisieren();
            _ui.Melden(Meldungsart.Erfolg, "Kennziffern übernommen",
                $"{ergebnis.Count} Kennziffern{(vorher != alle.Count ? $" ({vorher - alle.Count} nicht vergebene entfernt)" : string.Empty)}. Einzelne lassen sich in der Übersicht entfernen.");
        }
        catch (FormatException ex)
        {
            _ui.Melden(Meldungsart.Fehler, "Kennziffern nicht verstanden", ex.Message);
        }
        catch (Exception ex)
        {
            Fehler(ex, "Kennziffern nicht übernommen", Teilnehmerliste.Datei);
        }
    }

    /// <summary>Offene Kennziffer aus der Teilnehmendenliste entfernen (z. B. nicht vergeben oder nicht abgegeben).</summary>
    public void TeilnehmerEntfernen(UebersichtEintrag eintrag)
    {
        try
        {
            if (eintrag.Punkte is not null || KlausurName.Length == 0) return;
            int index = _teilnehmende.FindIndex(t => Gleich(t.Kennung, eintrag.Kennung));
            if (index < 0) return;
            Teilnehmer weg = _teilnehmende[index];
            string ordner = _ablage.KlausurOrdner(KlausurName);
            _teilnehmende.RemoveAt(index);
            Teilnehmerliste.Speichern(ordner, _teilnehmende);
            UebersichtAktualisieren();
            _ui.Melden(Meldungsart.Information, "Kennziffer entfernt", $"„{weg.Kennung}“ steht nicht mehr in der Teilnehmendenliste.", "Rückgängig", () =>
            {
                _teilnehmende.Insert(Math.Min(index, _teilnehmende.Count), weg);
                Teilnehmerliste.Speichern(ordner, _teilnehmende);
                UebersichtAktualisieren();
            });
        }
        catch (Exception ex)
        {
            Fehler(ex, "Kennziffer nicht entfernt", Teilnehmerliste.Datei);
        }
    }

    // ---------- Zweitgutachten ----------

    public const string Zustimmungssatz = "Dem Erstgutachten schließe ich mich in Begründung und Ergebnis an.";

    /// <summary>„Einverstanden“: Erstnote übernehmen und den Zustimmungssatz einfügen.</summary>
    [RelayCommand]
    void ErstgutachtenZustimmen()
    {
        if (Erstnote is not int e) return;
        NotenIndex = e;
        _editor.Einfuegen(Zustimmungssatz);
        _ui.Fokussieren(Eingabefeld.Editor);
    }

    // ---------- Bausteinstatistik ----------

    int _nutzungArbeiten;

    /// <summary>Alle Bausteine, deren Text im Entwurf vorkommt (für die Statistik, auch ohne Punktebereich).</summary>
    List<Baustein> GenutzteBausteine(string entwurf)
    {
        string text = Normal(entwurf);
        return Formulierungsdatei.Alle(Bausteine)
            .Where(b => !b.IstGruppe && Normal(b.Text) is { Length: >= 8 } t && text.Contains(t, StringComparison.Ordinal))
            .ToList();
    }

    void NutzungEintragen(string ordner, string kennung, string entwurf)
    {
        try
        {
            Bausteinnutzung.Eintragen(ordner, kennung, GenutzteBausteine(entwurf));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EinstellungsSpeicher.Protokollieren(ex);
        }
        NutzungAnwenden();
    }

    /// <summary>Zählungen auf die Bausteine übertragen: häufig (mind. ein Viertel der Arbeiten), nie verwendet (ab fünf Arbeiten).</summary>
    void NutzungAnwenden()
    {
        Dictionary<string, List<string>> nutzung = KlausurName.Length > 0
            ? Bausteinnutzung.Laden(_ablage.KlausurOrdner(KlausurName))
            : new();
        Dictionary<string, int> zahl = Bausteinnutzung.Zaehlen(nutzung);
        _nutzungArbeiten = nutzung.Count;
        int haeufig = Math.Max(2, (int)Math.Ceiling(_nutzungArbeiten * 0.25));
        foreach (Baustein b in Formulierungsdatei.Alle(Bausteine))
        {
            if (b.IstGruppe) continue;
            int n = zahl.TryGetValue(Bausteinnutzung.Schluessel(b.Text), out int z) ? z : 0;
            b.Nutzung = n;
            b.NutzungStufe = _nutzungArbeiten == 0 ? 0 : n == 0 ? (_nutzungArbeiten >= 5 ? 1 : 0) : n >= haeufig ? 3 : 2;
        }
        OnPropertyChanged(nameof(KannRueckmelden));
    }

    public bool KannRueckmelden => _nutzungArbeiten > 0 && KlausurName.Length > 0
                                   && PaketRueckmeldung.Laden(_ablage.KlausurOrdner(KlausurName)) is not null;

    /// <summary>Zählungen (ohne Kennziffern) an das KI-Portal senden, damit künftige Pakete besser werden.</summary>
    [RelayCommand]
    async Task BausteinstatistikSendenAsync()
    {
        try
        {
            if (KlausurOrdnerOderMeldung() is null) return;
            PaketRueckmeldung? r = PaketRueckmeldung.Laden(_ablage.KlausurOrdner(KlausurName));
            if (r is null)
            {
                _ui.Melden(Meldungsart.Information, "Keine Rückmeldung möglich",
                    "Rückmeldungen gibt es nur für Klausuren aus einem Paket des KI-Portals (ab Gutachtentool 1.4).");
                return;
            }
            NutzungAnwenden();
            if (_nutzungArbeiten == 0)
            {
                _ui.Melden(Meldungsart.Information, "Noch keine Statistik", "Die Statistik entsteht beim Erstellen der Gutachten.");
                return;
            }
            var zeilen = new List<(string Pfad, string Text, int Anzahl)>();
            void Sammeln(IEnumerable<Baustein> liste, string pfad)
            {
                foreach (Baustein b in liste)
                {
                    if (b.IstGruppe) Sammeln(b.Children, pfad.Length == 0 ? b.Text : pfad + " › " + b.Text);
                    else zeilen.Add((pfad, b.Text, b.Nutzung));
                }
            }
            Sammeln(Bausteine, string.Empty);
            int genutzt = zeilen.Count(z => z.Anzahl > 0);
            if (!await _ui.BestaetigenAsync("Bausteinstatistik an das KI-Portal senden",
                    $"Gesendet werden nur die Bausteine mit der Zahl der Gutachten, in denen sie vorkommen ({_nutzungArbeiten} Gutachten; " +
                    $"{genutzt} von {zeilen.Count} Bausteinen verwendet) – keine Kennziffern, Noten oder Gutachtentexte.\n\n" +
                    "Das Portal nutzt die Rückmeldung für weitere Läufe dieses Pakets und zur Verbesserung der Vorgaben.",
                    "Senden", "Abbrechen"))
                return;
            string? fehler = await r.SendenAsync(_nutzungArbeiten, zeilen, Programmversion);
            if (fehler is null) _ui.Melden(Meldungsart.Erfolg, "Rückmeldung gesendet", "Vielen Dank – das KI-Portal hat die Bausteinstatistik übernommen.");
            else _ui.Melden(Meldungsart.Fehler, "Rückmeldung nicht gesendet", fehler);
        }
        catch (Exception ex)
        {
            Fehler(ex, "Rückmeldung nicht gesendet");
        }
    }

    // ---------- Prüfungsportal ----------

    /// <summary>ZIP mit Notenliste und Gutachten als PDF für „Noten/Gutachten hochladen“ im Prüfungsportal.</summary>
    [RelayCommand]
    async Task KlausurenportalExportierenAsync()
    {
        try
        {
            if (KlausurOrdnerOderMeldung() is null) return;
            string ordner = Path.Combine(_ablage.Wurzel, _ablage.KlausurEinrichten(KlausurName, _notenliste));
            string liste = Path.Combine(ordner, KlausurAblage.Notenliste);
            List<string> kennungen = File.Exists(liste) ? _notenliste.Lesen(liste).Select(n => n.Kennung).ToList() : new();
            if (kennungen.Count == 0)
            {
                _ui.Melden(Meldungsart.Information, "Noch keine Noten", "Die Notenliste dieser Klausur ist leer.");
                return;
            }
            if (KlausurAblage.IstGesperrt(liste))
            {
                _ui.Melden(Meldungsart.Fehler, "Datei geöffnet", $"Bitte zuerst „{KlausurAblage.Notenliste}“ schließen.");
                return;
            }

            var fehlend = Klausurenportal.FehlendePdf(ordner, kennungen);
            int umgewandelt = 0, nichtUmgewandelt = 0;
            if (fehlend.Count > 0)
            {
                int wahl = await _ui.AuswahlAsync("Gutachten als PDF",
                    $"Das Prüfungsportal erwartet die Gutachten als PDF. {fehlend.Count} Gutachten liegen nur als Word-Datei vor (bzw. das PDF ist älter).",
                    "PDFs erzeugen", "Nur die Notenliste", "Abbrechen");
                if (wahl == 0) return;
                if (wahl == 1)
                {
                    for (int i = 0; i < fehlend.Count; i++)
                    {
                        _ui.Melden(Meldungsart.Information, "PDF erzeugen …", $"{i + 1} von {fehlend.Count}: {Path.GetFileName(fehlend[i].Docx)}");
                        if (await _ui.AlsPdfSpeichernAsync(fehlend[i].Docx, fehlend[i].Pdf)) umgewandelt++;
                        else
                        {
                            nichtUmgewandelt = fehlend.Count - i;
                            break;
                        }
                    }
                }
            }

            string? ziel = await _ui.SpeicherortWaehlenAsync(KlausurName + " – Prüfungsportal", ".zip", "ZIP für das Prüfungsportal");
            if (ziel is null) return;
            int pdf = Klausurenportal.Exportieren(ordner, kennungen, ziel);
            string hinweis = nichtUmgewandelt > 0
                ? $" {nichtUmgewandelt} Gutachten ließen sich nicht umwandeln (Windows: Word erforderlich; Mac: LibreOffice) – bitte als PDF speichern und neu exportieren."
                : string.Empty;
            _ui.Melden(nichtUmgewandelt > 0 ? Meldungsart.Warnung : Meldungsart.Erfolg, "Export für das Prüfungsportal",
                $"{Path.GetFileName(ziel)}: {kennungen.Count} Noten, {pdf} Gutachten als PDF{(umgewandelt > 0 ? $" ({umgewandelt} neu erzeugt)" : string.Empty)}. " +
                "Im Prüfungsportal unter „Noten/Gutachten hochladen“ als ZIP hochladen; die Kennziffern müssen den dortigen Codes entsprechen." + hinweis);
        }
        catch (Exception ex)
        {
            Fehler(ex, "Export nicht möglich");
        }
    }

    // ---------- Neue Version ----------

    async Task AktualisierungPruefenAsync()
    {
        try
        {
            if ((DateTime.Now - _einstellungen.UpdateGeprueft).TotalHours < 20) return;
            if (Programmversion is not { } text || !Version.TryParse(text, out Version? aktuell)) return;
            _einstellungen.UpdateGeprueft = DateTime.Now;
            _einstellungenGeaendert = true;
            if (await Aktualisierung.NeuereVersionAsync(aktuell) is Version neu)
                _ui.Melden(Meldungsart.Information, "Neue Version verfügbar",
                    $"Gutachtentool {neu.ToString(3)} steht bereit (installiert: {aktuell.ToString(3)}).", "Herunterladen",
                    () => _ui.Oeffnen(Aktualisierung.Downloadseite));
        }
        catch (Exception ex)
        {
            EinstellungsSpeicher.Protokollieren(ex); // nie störend
        }
    }

    // ---------- Rechtschreibprüfung ----------

    /// <summary>Wörter aus Sachverhalt und Lösung (Namen, Fachbegriffe), die die Rechtschreibprüfung nicht anstreichen soll.</summary>
    [ObservableProperty] public partial IReadOnlyList<string> UnterlagenWoerter { get; set; }
}
