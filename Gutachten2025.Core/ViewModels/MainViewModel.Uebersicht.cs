using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gutachten2025.Models;
using Gutachten2025.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Gutachten2025.ViewModels;

/// <summary>Seitenleiste (Kennzahlen, Verteilung, bewertete/offene Kennungen), Teilnehmendenliste, erneutes Öffnen und Pakete.</summary>
public sealed partial class MainViewModel
{
    const double BalkenHoehe = 56;

    List<Teilnehmer> _teilnehmende = new();
    string _uebersichtStand = string.Empty;

    public ObservableCollection<Kennzahl> Kennzahlen { get; } = new();
    public ObservableCollection<VerteilungsBalken> Verteilung { get; } = new();
    public ObservableCollection<UebersichtEintrag> Eintraege { get; } = new();

    readonly List<UebersichtEintrag> _alleEintraege = new();

    /// <summary>0 = alle, 1 = offen, 2 = bewertet.</summary>
    [ObservableProperty] public partial int UebersichtFilter { get; set; }

    partial void OnUebersichtFilterChanged(int value) => FilterAnwenden();

    [ObservableProperty] public partial double Fortschritt { get; set; }
    [ObservableProperty] public partial double FortschrittMax { get; set; }
    [ObservableProperty] public partial string? FortschrittText { get; set; }
    [ObservableProperty] public partial bool HatTeilnehmende { get; set; }

    void FilterAnwenden()
    {
        Eintraege.Clear();
        foreach (UebersichtEintrag e in _alleEintraege)
            if (UebersichtFilter == 0 || (UebersichtFilter == 1) == (e.Punkte is null))
                Eintraege.Add(e);
    }

    public string TeilnehmendeHinweis => _teilnehmende.Count > 0
        ? $"Teilnehmendenliste: {_teilnehmende.Count} Einträge"
        : "Keine Teilnehmendenliste importiert";

    // ---------- Übersicht ----------

    [RelayCommand]
    public void UebersichtAktualisieren()
    {
        try
        {
            UebersichtAufbauen();
        }
        finally
        {
            KennungPruefen(); // Hinweis „bereits begutachtet“ auf dem neuen Stand
        }
    }

    void UebersichtAufbauen()
    {
        OnPropertyChanged(nameof(TeilnehmendeHinweis));
        if (KlausurName.Length == 0)
        {
            Kennzahlen.Clear();
            Verteilung.Clear();
            AbweichungenAktualisieren(new Dictionary<string, int>());
            Eintraege.Clear();
            _alleEintraege.Clear();
            _uebersichtStand = string.Empty;
            HatTeilnehmende = false;
            FortschrittText = null;
            return;
        }

        string ordner = _ablage.KlausurOrdner(KlausurName);
        List<(string Kennung, int Punkte)> noten = new();
        try
        {
            string liste = Path.Combine(ordner, KlausurAblage.Notenliste);
            if (File.Exists(liste)) noten = _notenliste.Lesen(liste);
        }
        catch (Exception ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            noten = new();
        }

        var nachKennung = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, p) in noten) nachKennung[k] = p;
        Dictionary<string, Korrekturzeit> zeiten = Korrekturzeiten.Laden(ordner);
        string Zeit(string k) => zeiten.TryGetValue(k, out Korrekturzeit? z) && z.Beginn != default ? ZeitText(z) : string.Empty;
        string Erst(string k) => _erstnoten.TryGetValue(k, out int e) ? $"Erstgutachten {e}" : string.Empty;
        Dictionary<string, Druckeintrag> druck = Druckprotokoll.Laden(ordner);

        var eintraege = new List<UebersichtEintrag>();
        var bekannt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Teilnehmer t in _teilnehmende)
        {
            if (!bekannt.Add(t.Kennung)) continue;
            eintraege.Add(new UebersichtEintrag
            {
                Kennung = t.Kennung,
                Name = t.Name,
                Punkte = nachKennung.TryGetValue(t.Kennung, out int p) ? p : null,
                HatEntwurf = EntwurfArchiv.Existiert(ordner, t.Kennung),
                SchemaGeaendert = SchemaAbweichend(ordner, t.Kennung),
                Zeit = Zeit(t.Kennung),
                Erstnote = Erst(t.Kennung),
                Druck = nachKennung.ContainsKey(t.Kennung) ? Druckprotokoll.Text(druck, t.Kennung) : string.Empty,
            });
        }
        foreach (var (k, p) in nachKennung)
        {
            if (!bekannt.Add(k)) continue;
            eintraege.Add(new UebersichtEintrag
            {
                Kennung = k, Punkte = p, HatEntwurf = EntwurfArchiv.Existiert(ordner, k), SchemaGeaendert = SchemaAbweichend(ordner, k),
                Zeit = Zeit(k), Erstnote = Erst(k), Druck = Druckprotokoll.Text(druck, k),
            });
        }

        // Nur bei Änderungen neu aufbauen (die Übersicht wird auch bei jeder Fensteraktivierung aktualisiert).
        string stand = KlausurName + "|" + string.Join(";", eintraege.Select(e => $"{e.Kennung}:{e.Name}:{e.Punkte}:{e.HatEntwurf}:{e.SchemaGeaendert}:{e.Zusatz}"));
        if (stand == _uebersichtStand) return;
        _uebersichtStand = stand;
        Kennzahlen.Clear();
        Verteilung.Clear();
        _alleEintraege.Clear();
        _alleEintraege.AddRange(eintraege);
        FilterAnwenden();

        int bewertet = _teilnehmende.Count(t => nachKennung.ContainsKey(t.Kennung));
        HatTeilnehmende = _teilnehmende.Count > 0;
        FortschrittMax = Math.Max(1, _teilnehmende.Count);
        Fortschritt = bewertet;
        FortschrittText = HatTeilnehmende
            ? $"{bewertet} von {_teilnehmende.Count} bewertet ({Zahl.Prozent((double)bewertet / _teilnehmende.Count)})"
            : $"{nachKennung.Count} bewertet";

        List<int> punkte = nachKennung.Values.ToList();
        foreach (Kennzahl z in Statistik.Kennzahlen(punkte, _teilnehmende.Count)) Kennzahlen.Add(z);
        int offen = HatTeilnehmende ? Math.Max(0, _teilnehmende.Count - bewertet) : 0;
        foreach (Kennzahl z in ZusatzKennzahlen(nachKennung, zeiten, offen)) Kennzahlen.Add(z);
        if (punkte.Count > 0)
            foreach (VerteilungsBalken b in Statistik.Verteilung(punkte, BalkenHoehe)) Verteilung.Add(b);
        AbweichungenAktualisieren(nachKennung);
    }

    /// <summary>Klick in der Übersicht: bewertete Arbeit zum Ändern öffnen bzw. offene Kennung übernehmen.</summary>
    public async Task EintragOeffnenAsync(UebersichtEintrag eintrag)
    {
        try
        {
            if (eintrag.Punkte is null)
            {
                TeilnehmerWaehlen(new Teilnehmer { Kennung = eintrag.Kennung, Name = eintrag.Name });
                _ui.Fokussieren(Eingabefeld.Editor);
                return;
            }
            if (Gleich(BearbeiteteKennung, eintrag.Kennung)) return;

            string ordner = _ablage.KlausurOrdner(KlausurName);
            GutachtenEntwurf? entwurf = EntwurfArchiv.Laden(ordner, eintrag.Kennung);
            if (entwurf is null)
            {
                _ui.Melden(Meldungsart.Information, "Kein gespeicherter Entwurf",
                    $"Das Gutachten zu „{eintrag.Kennung}“ wurde mit einer älteren Programmversion erstellt. Bitte die Word-Datei direkt bearbeiten.");
                return;
            }

            bool entwurfVorhanden = !string.IsNullOrWhiteSpace(_editor.TextLesen());
            if (entwurfVorhanden && !await _ui.BestaetigenAsync("Entwurf ersetzen?",
                    $"Im Editor steht ein Entwurf{(Kennung.Length > 0 ? $" zu „{Kennung}“" : string.Empty)}. Er wird durch das Gutachten zu „{eintrag.Kennung}“ ersetzt (über die Meldung danach zurückholbar).",
                    "Öffnen", "Abbrechen"))
                return;

            GutachtenEntwurf vorher = ZustandErfassen();
            string vorherBearbeitet = BearbeiteteKennung;
            ZustandSetzen(entwurf, alsUeberarbeitung: true);
            _ui.Melden(Meldungsart.Information, "Gutachten zum Ändern geöffnet",
                $"„{eintrag.Kennung}“ ({Notenskala.MitPunkten(entwurf.Note)}, Stand {entwurf.Stand:dd.MM.yyyy HH:mm}). Generieren ersetzt Gutachten und Note.",
                entwurfVorhanden ? "Vorigen Entwurf zurückholen" : null,
                entwurfVorhanden ? () => ZustandSetzen(vorher, alsUeberarbeitung: vorherBearbeitet.Length > 0) : null);
        }
        catch (Exception ex)
        {
            Fehler(ex, "Entwurf nicht geöffnet");
        }
    }

    // ---------- Teilnehmende ----------

    void TeilnehmendeLaden()
    {
        _teilnehmende = new();
        if (KlausurName.Length == 0) return;
        try
        {
            _teilnehmende = Teilnehmerliste.Laden(_ablage.KlausurOrdner(KlausurName));
        }
        catch (Exception ex)
        {
            Fehler(ex, "Teilnehmendenliste nicht lesbar", Teilnehmerliste.Datei);
        }
    }

    [RelayCommand]
    async Task TeilnehmendeImportierenAsync()
    {
        try
        {
            if (KlausurOrdnerOderMeldung() is null) return;
            string ordner = Path.Combine(_ablage.Wurzel, _ablage.KlausurEinrichten(KlausurName, _notenliste));
            string? quelle = await _ui.DateiWaehlenAsync(".xlsx", ".csv", ".txt");
            if (quelle is null) return;

            Teilnehmerimport import = Teilnehmerliste.ImportierenMitNoten(quelle);
            List<Teilnehmer> neu = import.Teilnehmende;
            List<Teilnehmer> ergebnis = neu;
            bool ergaenzen = false;
            if (_teilnehmende.Count > 0)
            {
                int wahl = await _ui.AuswahlAsync("Teilnehmendenliste vorhanden",
                    $"Es gibt bereits {_teilnehmende.Count} Teilnehmende. Die Datei enthält {neu.Count}.",
                    "Ersetzen", "Ergänzen", "Abbrechen");
                if (wahl == 0) return;
                if (wahl == 2)
                {
                    ergaenzen = true;
                    ergebnis = _teilnehmende.ToList();
                    foreach (Teilnehmer t in neu)
                    {
                        Teilnehmer? alt = ergebnis.FirstOrDefault(x => Gleich(x.Kennung, t.Kennung));
                        if (alt is null) ergebnis.Add(t);
                        else if (alt.Name.Length == 0) alt.Name = t.Name;
                    }
                }
            }
            Teilnehmerliste.Speichern(ordner, ergebnis);
            _teilnehmende = ergebnis;

            // Noten des Erstgutachtens → Erstnoten.json (Zweitkorrekturmodus).
            string notenText = string.Empty;
            bool neuerModus = false;
            if (import.Noten.Count > 0)
            {
                bool vorher = Zweitkorrekturmodus;
                var noten = new Dictionary<string, int>(ergaenzen ? _erstnoten : new(), StringComparer.OrdinalIgnoreCase);
                foreach (var (kz, pt) in import.Noten) noten[kz] = pt;
                int gespeichert = Erstnoten.Speichern(ordner, noten);
                ErstnotenLaden();
                neuerModus = !vorher && Zweitkorrekturmodus;
                int uebergangen = import.Noten.Count - import.Noten.Keys.Count(Erstnoten.IstKennziffer);
                notenText = $" {gespeichert} Erstnoten übernommen – Zweitkorrekturmodus." +
                            (uebergangen > 0 ? $" {uebergangen} Einträge ohne Kennziffer/Matrikelnummer ohne Note übernommen." : string.Empty);
            }
            SeitenleisteOffen = true;
            UebersichtAktualisieren();
            _ui.Melden(Meldungsart.Erfolg, "Teilnehmendenliste importiert",
                $"{ergebnis.Count} Teilnehmende. Beim Generieren wird geprüft, ob die Kennung in der Liste steht.{notenText}");
            if (neuerModus) await VorlageAufZweitgutachtenAnbietenAsync(ordner);
        }
        catch (Exception ex)
        {
            Fehler(ex, "Teilnehmendenliste nicht importiert");
        }
    }

    /// <summary>Vorschläge für das Kennungsfeld (Kennziffer, Matrikelnummer oder Name).</summary>
    public IReadOnlyList<Teilnehmer> TeilnehmerVorschlaege(string eingabe)
    {
        string e = eingabe.Trim();
        if (e.Length == 0 || _teilnehmende.Count == 0) return Array.Empty<Teilnehmer>();
        return _teilnehmende
            .Where(t => t.Kennung.Contains(e, StringComparison.CurrentCultureIgnoreCase) || t.Name.Contains(e, StringComparison.CurrentCultureIgnoreCase))
            .Take(12)
            .ToList();
    }

    public void TeilnehmerWaehlen(Teilnehmer t)
    {
        if (Anonym)
        {
            Student = t.Kennung;
        }
        else
        {
            Matrikel = t.Kennung;
            if (t.Name.Length > 0) Student = t.Name;
        }
    }

    // ---------- Drucken ----------

    bool _druckAbbruch;

    /// <summary>Höchstens so viele Aufträge dürfen in der Warteschlange stehen, bevor das nächste Gutachten folgt.</summary>
    const int DruckWarteschlangeMax = 3;

    /// <summary>Wartet, bis die Warteschlange abgearbeitet ist; liefert ein Druckerproblem (dann anhalten) oder null.</summary>
    async Task<string?> WarteschlangeAbwartenAsync()
    {
        DateTime bis = DateTime.Now.AddMinutes(3);
        while (!_druckAbbruch)
        {
            Druckstand? s = await _ui.DruckerAbfragenAsync();
            if (s is null) return null; // nicht abfragbar: wie bisher ohne Überwachung
            if (s.Problem is not null) return s.Problem;
            if (s.Auftraege < DruckWarteschlangeMax) return null;
            if (DateTime.Now > bis) return $"Die Druckwarteschlange leert sich nicht ({s.Auftraege} Aufträge seit 3 Minuten).";
            await Task.Delay(2000);
        }
        return null;
    }

    /// <summary>Druckt alle Gutachten der Klausur nacheinander (Reihenfolge wie im Explorer) auf dem Standarddrucker.</summary>
    [RelayCommand]
    async Task AlleDruckenAsync()
    {
        try
        {
            if (KlausurOrdnerOderMeldung() is not string ordner) return;
            BausteineSpeichern();

            // Bevorzugt die Gutachten zu den Einträgen der Notenliste; sonst alle Word-Dateien außer Vorlagen und Sicherungen.
            var dateien = new List<string>();
            string liste = Path.Combine(ordner, KlausurAblage.Notenliste);
            if (File.Exists(liste))
                foreach (var (k, _) in _notenliste.Lesen(liste))
                {
                    string d = Path.Combine(ordner, KlausurAblage.SicherName(k) + ".docx");
                    if (File.Exists(d) && !dateien.Contains(d, StringComparer.OrdinalIgnoreCase)) dateien.Add(d);
                }
            if (dateien.Count == 0)
                dateien = Directory.GetFiles(ordner, "*.docx")
                    .Where(d => Path.GetFileName(d) is var n && !n.StartsWith("~$") && !n.StartsWith("MUSTER", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            dateien = dateien.OrderBy(d => Path.GetFileNameWithoutExtension(d), KlausurAblage.NatuerlicheReihenfolge).ToList();

            if (dateien.Count == 0)
            {
                _ui.Melden(Meldungsart.Information, "Nichts zu drucken", "Im Klausurordner liegen noch keine Gutachten.");
                return;
            }
            Dictionary<string, Druckeintrag> protokoll = Druckprotokoll.Laden(ordner);
            string Stamm(string d) => Path.GetFileNameWithoutExtension(d);
            List<string> offen = dateien.Where(d => !(protokoll.TryGetValue(Stamm(d), out Druckeintrag? e) && e.Ok)).ToList();
            int gedrucktVorher = dateien.Count - offen.Count, fehlerVorher = dateien.Count(d => protokoll.TryGetValue(Stamm(d), out Druckeintrag? e) && !e.Ok);
            if (gedrucktVorher > 0)
            {
                int wahl = await _ui.AuswahlAsync("Alle Gutachten drucken",
                    $"Laut Druckprotokoll wurden {gedrucktVorher} von {dateien.Count} Gutachten bereits gedruckt" +
                    (fehlerVorher > 0 ? $", bei {fehlerVorher} trat ein Fehler auf" : string.Empty) + ".\n\nBitte den Standarddrucker prüfen.",
                    $"Nur noch nicht gedruckte ({offen.Count})", $"Alle erneut ({dateien.Count})", "Abbrechen");
                if (wahl == 0) return;
                if (wahl == 1) dateien = offen;
                if (dateien.Count == 0)
                {
                    _ui.Melden(Meldungsart.Information, "Nichts zu drucken", "Alle Gutachten sind laut Druckprotokoll gedruckt.");
                    return;
                }
            }
            else
            {
                string von = Stamm(dateien[0]), bis = Stamm(dateien[^1]);
                if (!await _ui.BestaetigenAsync("Alle Gutachten drucken",
                        $"{dateien.Count} Gutachten ({von} bis {bis}) werden nacheinander auf dem Windows-Standarddrucker gedruckt. " +
                        "Word öffnet sich dafür jeweils kurz. Das Programm wartet, solange sich in der Druckwarteschlange Aufträge stauen, und hält bei Druckerproblemen an; " +
                        "das Druckprotokoll hält fest, was gedruckt wurde.\n\nBitte den Standarddrucker vorher prüfen und während des Druckens keine dieser Dateien bearbeiten.",
                        "Drucken", "Abbrechen"))
                    return;
            }

            _druckAbbruch = false;
            int gedruckt = 0, fehler = 0;
            string? halt = null;
            for (int i = 0; i < dateien.Count && !_druckAbbruch; i++)
            {
                string stamm = Stamm(dateien[i]);
                _ui.Melden(Meldungsart.Information, "Drucken …",
                    $"{i + 1} von {dateien.Count}: {Path.GetFileName(dateien[i])}", "Abbrechen", () => _druckAbbruch = true);
                halt = await WarteschlangeAbwartenAsync();
                if (halt is not null) break;
                bool ok = _ui.Drucken(dateien[i]);
                protokoll[stamm] = new Druckeintrag { Stand = DateTime.Now, Ok = ok, Meldung = ok ? null : "Word ließ sich nicht starten (Details in fehler.log)." };
                if (ok) gedruckt++;
                else fehler++;
                SpeichernStill(ordner, protokoll);
                // Word braucht Zeit zum Öffnen und Übergeben an die Druckwarteschlange; beim ersten Mal länger.
                await Task.Delay(i == 0 ? 5000 : 2500);
            }

            // Aufträge, die der Spooler als fehlerhaft meldet, im Protokoll als Fehler vermerken.
            if (halt is null && !_druckAbbruch) await Task.Delay(3000);
            var dieseRunde = dateien.Select(Stamm).ToList();
            if (await _ui.DruckerAbfragenAsync() is { } stand)
                foreach (string dok in stand.FehlerhafteDokumente)
                    if (Druckprotokoll.Zuordnen(dok, dieseRunde) is string s && protokoll.TryGetValue(s, out Druckeintrag? alt) && alt.Ok)
                    {
                        protokoll[s] = new Druckeintrag { Stand = DateTime.Now, Ok = false, Meldung = "Druckauftrag in der Warteschlange fehlerhaft: " + dok };
                        gedruckt--;
                        fehler++;
                    }
            SpeichernStill(ordner, protokoll);
            UebersichtAktualisieren();

            int rest = dateien.Count - gedruckt - fehler;
            string grund = halt is not null ? $" Angehalten: {halt}" : _druckAbbruch ? " Abgebrochen." : string.Empty;
            _ui.Melden(fehler > 0 || halt is not null ? Meldungsart.Warnung : Meldungsart.Erfolg, "Drucken beendet",
                $"{gedruckt} Gutachten an den Drucker übergeben{(fehler > 0 ? $", {fehler} fehlgeschlagen" : string.Empty)}" +
                $"{(rest > 0 ? $", {rest} nicht gedruckt" : string.Empty)}.{grund}" +
                (fehler > 0 || rest > 0 ? " Erneut „Alle Gutachten drucken“ → „Nur noch nicht gedruckte“ druckt den Rest." : string.Empty));

            static void SpeichernStill(string o, Dictionary<string, Druckeintrag> p)
            {
                try { Druckprotokoll.Speichern(o, p); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { EinstellungsSpeicher.Protokollieren(ex); }
            }
        }
        catch (Exception ex)
        {
            Fehler(ex, "Drucken nicht möglich");
        }
    }

    // ---------- Pakete ----------

    static string? Programmversion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3);

    [RelayCommand]
    async Task PaketExportierenAsync()
    {
        try
        {
            if (KlausurOrdnerOderMeldung() is null) return;
            BausteineSpeichern();
            SchemaExternPruefen(); // in Excel geänderte rohpunkte.xlsx zuerst nach rohpunkte.json übernehmen
            string ordner = Path.Combine(_ablage.Wurzel, _ablage.KlausurEinrichten(KlausurName, _notenliste));
            string? ziel = await _ui.SpeicherortWaehlenAsync(KlausurName, PaketService.Endung, "Gutachtentool-Paket");
            if (ziel is null) return;
            var manifest = new PaketManifest
            {
                Titel = KlausurName.Replace('_', ' '),
                Programmversion = Programmversion,
                Quelle = "Gutachtentool",
            };
            PaketService.Exportieren(ordner, manifest, ziel);
            _ui.Melden(Meldungsart.Erfolg, "Paket gespeichert",
                $"{Path.GetFileName(ziel)} enthält Formulierungen, Word-Vorlage, Klausureinstellungen sowie ggf. Rohpunkteschema, Sachverhalt und Lösung – keine Noten, Gutachten, Entwürfe oder Teilnehmenden.");
        }
        catch (Exception ex)
        {
            Fehler(ex, "Paket nicht gespeichert");
        }
    }

    [RelayCommand]
    async Task PaketImportierenWaehlenAsync()
    {
        string? pfad = await _ui.DateiWaehlenAsync(PaketService.Endung);
        if (pfad is not null) await PaketImportierenAsync(pfad);
    }

    /// <summary>Öffnet im KI-Portal die Seite zum Erzeugen eines Pakets (Klausurinformationen vorbereiten).</summary>
    [RelayCommand]
    void KlausurinformationenVorbereiten() => _ui.Oeffnen(Verweise.Gutachtenpaket);

    /// <summary>Importiert ein Paket (auch per Doppelklick); vorhandene Vorlagen werden gesichert, Noten bleiben unberührt.</summary>
    public async Task PaketImportierenAsync(string pfad)
    {
        try
        {
            PaketInhalt inhalt = PaketService.Pruefen(pfad);
            PaketManifest m = inhalt.Manifest;
            string titel = string.IsNullOrWhiteSpace(m.Titel) ? Path.GetFileNameWithoutExtension(pfad) : m.Titel.Trim();
            string name = KlausurAblage.SicherName(titel);
            string herkunft = string.Join(", ", new[] { m.Autor, m.Quelle, m.Erstellt == default ? null : m.Erstellt.ToString("dd.MM.yyyy") }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            string info = $"„{titel}“{(herkunft.Length > 0 ? $" ({herkunft})" : string.Empty)}\nInhalt: {inhalt.Beschreibung()}"
                          + (string.IsNullOrWhiteSpace(m.Beschreibung) ? string.Empty : $"\n\n{m.Beschreibung}");

            if (_ablage.Existiert(name))
            {
                int wahl = await _ui.AuswahlAsync("Klausur bereits vorhanden",
                    $"{info}\n\nDie Klausur „{name}“ gibt es schon. Noten, Gutachten und Entwürfe bleiben in jedem Fall unberührt; ersetzte Vorlagen werden gesichert.",
                    "Vorlagen ersetzen", "Als neue Klausur", "Abbrechen");
                if (wahl == 0) return;
                if (wahl == 2) name = _ablage.FreierName(name);
            }
            else if (!await _ui.BestaetigenAsync("Paket importieren", $"{info}\n\nAls neue Klausur „{name}“ anlegen?", "Importieren", "Abbrechen"))
                return;

            BausteineSpeichern();
            string ordner = Path.Combine(_ablage.Wurzel, _ablage.KlausurEinrichten(name, _notenliste));
            int gesichert = PaketService.Entpacken(inhalt, ordner);
            if (m.Rueckmeldung is { Gueltig: true } rueck) rueck.Speichern(ordner); // für die Bausteinstatistik

            var kriterien = AktuelleKriterien();
            var teilpunkte = AktuelleTeilpunkte();
            KlausurenNeuLaden();
            KlausurAktivieren(name);
            AuswahlWiederherstellen(kriterien, teilpunkte);
            OnPropertyChanged(nameof(KlausurName));
            EinstellungenSpeichern();
            _ui.Melden(Meldungsart.Erfolg, "Paket importiert",
                $"Klausur „{KlausurName}“: {inhalt.Beschreibung()}{(gesichert > 0 ? $"; {gesichert} vorhandene Datei(en) gesichert" : string.Empty)}.");
        }
        catch (Exception ex)
        {
            Fehler(ex, "Paket nicht importiert", pfad);
        }
    }

    /// <summary>Nur für bewertete Arbeiten mit Schema-Fingerabdruck: wurde das Schema seither geändert?</summary>
    bool SchemaAbweichend(string ordner, string kennung) =>
        _schemaKennung is not null && EntwurfArchiv.Schema(ordner, kennung) is string s && s != _schemaKennung;
}
