using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gutachten2025.Models;
using Gutachten2025.Rohpunkte;
using Gutachten2025.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Gutachten2025.ViewModels;

/// <summary>
/// Kern: Start, Klausurwahl, Generieren, Speichern. Weitere Teile: Bausteine, Bewertung (Formalkriterien/Teilpunkte),
/// Übersicht (Seitenleiste, Teilnehmende, Entwürfe) und Pakete.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    readonly IUiService _ui;
    readonly IEntwurfEditor _editor;
    readonly EinstellungsSpeicher _speicher = new();
    readonly NotenlisteService _notenliste = new();
    readonly GutachtenService _gutachten = new();

    KlausurAblage _ablage = new(KlausurAblage.StandardWurzel);
    Einstellungen _einstellungen = new();
    bool _initialisiert, _einstellungenGeaendert, _entwurfGeaendert, _autosaveFehlerGemeldet;

    public MainViewModel(IUiService ui, IEntwurfEditor editor)
    {
        _ui = ui;
        _editor = editor;
        // Startwerte hier statt als Initialisierer (partielle Properties)
        Suchtext = string.Empty;
        BearbeiteteKennung = string.Empty;
        PunkteText = string.Empty;
        KlausurName = string.Empty;
        Anonym = true;
        Student = string.Empty;
        Matrikel = string.Empty;
        NotenIndex = Notenskala.Vorgabe;
        UnterlagenHtml = string.Empty;
        UnterlagenEntwurf = string.Empty;
        UnterlagenWoerter = Array.Empty<string>();
        NormenSuchtext = string.Empty;
        KennungHinweis = string.Empty;
        AngabenHervorheben = true;
    }

    // ---------- Bindbare Daten ----------

    public ObservableCollection<string> Klausuren { get; } = new();
    public IReadOnlyList<string> NotenAuswahl => Notenskala.Auswahl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeineKlausur))]
    public partial string KlausurName { get; set; }

    public bool KeineKlausur => KlausurName.Length == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatrikelSichtbar), nameof(KennungsBezeichnung))]
    public partial bool Anonym { get; set; }

    [ObservableProperty] public partial string Student { get; set; }
    [ObservableProperty] public partial string Matrikel { get; set; }
    [ObservableProperty] public partial int NotenIndex { get; set; }
    [ObservableProperty] public partial double Seiten { get; set; }

    /// <summary>Kennung eines zum Ändern geöffneten Gutachtens; Generieren überschreibt es ohne Rückfrage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BearbeitungsHinweis), nameof(InBearbeitung))]
    public partial string BearbeiteteKennung { get; set; }

    public bool InBearbeitung => BearbeiteteKennung.Length > 0;

    public string BearbeitungsHinweis => InBearbeitung
        ? $"Änderung des Gutachtens „{BearbeiteteKennung}“ – Generieren ersetzt Gutachten und Note."
        : string.Empty;

    public bool MatrikelSichtbar => !Anonym;
    public string KennungsBezeichnung => Anonym ? "Kennziffer" : "Name";
    public string Ablageordner => _ablage.Wurzel;

    string Kennung => (Anonym ? Student : Matrikel).Trim();

    int SeitenZahl => double.IsNaN(Seiten) || Seiten < 0 ? 0 : (int)Math.Round(Seiten);

    partial void OnSeitenChanged(double value)
    {
        if (double.IsNaN(value) || value < 0) Seiten = 0;
        OnPropertyChanged(nameof(SeitenOffen));
    }

    partial void OnNotenIndexChanged(int value)
    {
        if (!_noteAutomatisch) NoteGesetzt = true;
        PassendAktualisieren();
        NoteGeaendert(value);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_initialisiert) _einstellungenGeaendert = true;
        if (e.PropertyName is nameof(Student) or nameof(Matrikel) or nameof(Anonym) or nameof(NotenIndex))
        {
            ErstnoteAktualisieren();
            Aktivitaet();
        }
        if (e.PropertyName is nameof(Student) or nameof(Matrikel) or nameof(Anonym) or nameof(BearbeiteteKennung) or nameof(KlausurName))
            KennungPruefen();
    }

    // ---------- Darstellung (von der View gelesen und geschrieben) ----------

    [ObservableProperty] public partial bool SeitenleisteOffen { get; set; }

    /// <summary>Generiertes Gutachten anschließend in Word öffnen.</summary>
    [ObservableProperty] public partial bool WordOeffnen { get; set; }

    /// <summary>Beim Tippen Sätze aus den Bausteinen vorschlagen.</summary>
    [ObservableProperty] public partial bool Satzvorschlaege { get; set; }

    partial void OnSeitenleisteOffenChanged(bool value)
    {
        if (value && _initialisiert) UebersichtAktualisieren();
    }

    /// <summary>Bausteinliste eingeblendet (F6).</summary>
    [ObservableProperty] public partial bool BausteineOffen { get; set; }

    /// <summary>Bausteinliste über statt links neben dem Editor.</summary>
    [ObservableProperty] public partial bool BausteineOben { get; set; }

    /// <summary>Formalkriterien im Kopfbereich aufgeklappt.</summary>
    [ObservableProperty] public partial bool KriterienAufgeklappt { get; set; }

    public double BausteinBreite
    {
        get => _einstellungen.BausteinBreite;
        set { _einstellungen.BausteinBreite = value; _einstellungenGeaendert = true; }
    }

    public double UnterlagenBreite
    {
        get => _einstellungen.UnterlagenBreite;
        set { _einstellungen.UnterlagenBreite = value; _einstellungenGeaendert = true; }
    }

    public double BausteinHoehe
    {
        get => _einstellungen.BausteinHoehe;
        set { _einstellungen.BausteinHoehe = value; _einstellungenGeaendert = true; }
    }

    /// <summary>0 = wie Windows, 1 = hell, 2 = dunkel.</summary>
    public int Darstellung
    {
        get => _einstellungen.Darstellung;
        set { _einstellungen.Darstellung = value; _einstellungenGeaendert = true; }
    }

    public double Oberflaechenskala
    {
        get => _einstellungen.Oberflaechenskala is >= 0.8 and <= 1.6 ? _einstellungen.Oberflaechenskala : 1.0;
        set { _einstellungen.Oberflaechenskala = value; _einstellungenGeaendert = true; }
    }

    public string EditorSchrift
    {
        get => _einstellungen.EditorSchrift ?? string.Empty;
        set { _einstellungen.EditorSchrift = value; _einstellungenGeaendert = true; }
    }

    public bool ZeilenbreiteBegrenzt
    {
        get => _einstellungen.ZeilenbreiteBegrenzt;
        set { _einstellungen.ZeilenbreiteBegrenzt = value; _einstellungenGeaendert = true; }
    }

    /// <summary>Gespeicherte Fensterlage (physische Pixel); X = int.MinValue: noch keine.</summary>
    public (int X, int Y, int Breite, int Hoehe, bool Maximiert) Fenster
    {
        get => (_einstellungen.FensterX, _einstellungen.FensterY, _einstellungen.FensterBreite, _einstellungen.FensterHoehe, _einstellungen.FensterMaximiert);
        set
        {
            (_einstellungen.FensterX, _einstellungen.FensterY, _einstellungen.FensterBreite, _einstellungen.FensterHoehe, _einstellungen.FensterMaximiert) = value;
            _einstellungenGeaendert = true;
        }
    }

    /// <summary>Schrift der Word-Vorlage der aktuellen Klausur (für die Editorschrift).</summary>
    public string? VorlagenSchrift() =>
        GutachtenService.VorlagenSchrift(KlausurName.Length > 0
            ? Path.Combine(_ablage.KlausurOrdner(KlausurName), KlausurAblage.WordVorlage)
            : Path.Combine(_ablage.VorlagenOrdner, KlausurAblage.WordVorlage));

    /// <summary>Ablageordner für die Anzeige im Einstellungsdialog.</summary>
    public string AblageordnerText => _ablage.Wurzel;

    public double Zoom
    {
        get => _einstellungen.Zoom;
        set { _einstellungen.Zoom = value; _einstellungenGeaendert = true; }
    }

    public bool EinfuehrungGezeigt
    {
        get => _einstellungen.EinfuehrungGezeigt;
        set { _einstellungen.EinfuehrungGezeigt = value; _einstellungenGeaendert = true; }
    }

    // ---------- Start ----------

    public void Initialisieren()
    {
        try
        {
            _einstellungen = _speicher.Laden(out string? alterEntwurf);
            _ablage = new KlausurAblage(string.IsNullOrWhiteSpace(_einstellungen.Ablageordner)
                ? KlausurAblage.StandardWurzel
                : _einstellungen.Ablageordner);
            IReadOnlyList<string> fehlend = _ablage.Einrichten();

            KlausurenNeuLaden();
            Anonym = _einstellungen.Anonym;
            Student = _einstellungen.Name;
            Matrikel = _einstellungen.Matrikel;
            NotenIndex = Notenskala.IstGueltig(_einstellungen.Note) ? _einstellungen.Note : Notenskala.Vorgabe;
            Seiten = Math.Max(0, _einstellungen.Seiten);
            AngabenHervorheben = _einstellungen.AngabenHervorheben;
            SeitenleisteOffen = _einstellungen.SeitenleisteOffen;
            WordOeffnen = _einstellungen.WordOeffnen;
            BausteineOffen = _einstellungen.BausteineOffen;
            BausteineOben = _einstellungen.BausteineOben;
            Leiste = Enum.IsDefined((Leistenanzeige)_einstellungen.Leiste) ? (Leistenanzeige)_einstellungen.Leiste : Leistenanzeige.Bausteine;
            KriterienAufgeklappt = _einstellungen.KriterienAufgeklappt;
            Satzvorschlaege = _einstellungen.Satzvorschlaege;
            BearbeiteteKennung = _einstellungen.BearbeiteteKennung ?? string.Empty;

            string klausur = Klausuren.FirstOrDefault(k => Gleich(k, _einstellungen.Klausur))
                             ?? (_einstellungen.Klausur.Length > 0 ? _einstellungen.Klausur : Klausuren.FirstOrDefault() ?? string.Empty);
            KlausurAktivieren(klausur);
            AuswahlWiederherstellen(AlteKriterien(_einstellungen), _einstellungen.Teilpunkte);
            if (HatSchema)
            {
                int note = NotenIndex;
                RohpunkteWiederherstellen(_einstellungen.Rohpunkte, _einstellungen.Rohkommentare, melden: true);
                NotenIndex = note;
            }

            NoteGesetzt = _einstellungen.NoteGesetzt ?? NotenIndex != Notenskala.Vorgabe;
            string? rtf = _speicher.EntwurfLaden();
            if (rtf is not null) _editor.RtfSetzen(rtf);
            else if (!string.IsNullOrEmpty(alterEntwurf)) _editor.TextSetzen(alterEntwurf);

            if (fehlend.Count > 0)
                _ui.Melden(Meldungsart.Fehler, "Programmvorlagen fehlen",
                    $"Nicht gefunden: {string.Join(", ", fehlend)}. Bitte das Gutachtenprogramm neu installieren.");
            else if (CloudHinweis() is { } hinweis)
                _ui.Melden(Meldungsart.Warnung, "Ablage in Cloud-Ordner", hinweis);
        }
        catch (Exception ex)
        {
            Fehler(ex, "Start nicht vollständig möglich");
        }
        finally
        {
            _arbeitBeginn = _einstellungen.ArbeitBeginn;
            _arbeitSekunden = _einstellungen.ArbeitSekunden;
            _letzteAktivitaet = DateTime.Now;
            _initialisiert = true;
            KennungPruefen();
            _einstellungenGeaendert = _entwurfGeaendert = false;
        }
        _ = AktualisierungPruefenAsync();
    }

    /// <summary>Übernimmt die Auswahl aus Version 1.0 (Index der festen Listen Gliederung/Schrift).</summary>
    static Dictionary<string, string> AlteKriterien(Einstellungen e)
    {
        var d = new Dictionary<string, string>(e.Kriterien ?? new(), StringComparer.OrdinalIgnoreCase);
        if (d.Count > 0) return d;
        Klausurkonfiguration standard = Klausurkonfiguration.Standard();
        void Uebernehmen(string name, int index)
        {
            Formalkriterium? k = standard.Formalkriterien.FirstOrDefault(f => f.Name == name);
            if (k is not null && index > 0 && index <= k.Stufen.Count) d[name] = k.Stufen[index - 1];
        }
        Uebernehmen("Gliederung", e.Gliederung);
        Uebernehmen("Schrift", e.Schrift);
        return d;
    }

    // ---------- Klausur ----------

    /// <summary>Wechselt die Klausur; die Bausteine der bisherigen Klausur werden zuvor in deren Datei gesichert.</summary>
    public void KlausurWechseln(string? neu)
    {
        neu = (neu ?? string.Empty).Trim();
        if (neu.Length > 0 && Gleich(KlausurAblage.SicherName(neu), KlausurName)) return;
        if (neu.Length == 0 && KlausurName.Length == 0) return;
        try
        {
            BausteineSpeichern();
            var kriterien = AktuelleKriterien();
            var teilpunkte = AktuelleTeilpunkte();
            KlausurAktivieren(neu);
            AuswahlWiederherstellen(kriterien, teilpunkte);
            EinstellungenSpeichern();
        }
        catch (Exception ex)
        {
            Fehler(ex, "Klausur konnte nicht gewechselt werden");
        }
    }

    void KlausurAktivieren(string anzeigename)
    {
        if (anzeigename.Length > 0)
        {
            string ordner = _ablage.KlausurEinrichten(anzeigename, _notenliste);
            if (!Klausuren.Any(k => Gleich(k, ordner))) KlausurenNeuLaden();
            KlausurName = Klausuren.FirstOrDefault(k => Gleich(k, ordner)) ?? ordner;
        }
        else
        {
            KlausurName = string.Empty;
        }
        KonfigurationLaden();
        _noteManuell = false;
        SchemaLaden();
        TeilnehmendeLaden();
        ErstnotenLaden();
        BausteineLaden();
        NormensammlungLaden();
        UnterlagenLaden();
        UebersichtAktualisieren();
    }

    void KlausurenNeuLaden()
    {
        Klausuren.Clear();
        foreach (string k in _ablage.Klausuren()) Klausuren.Add(k);
    }

    string? KlausurOrdnerOderMeldung()
    {
        if (KlausurName.Length > 0) return _ablage.KlausurOrdner(KlausurName);
        _ui.Melden(Meldungsart.Fehler, "Keine Klausur ausgewählt", "Bitte zuerst eine Klausur wählen oder einen Namen eingeben.");
        _ui.Fokussieren(Eingabefeld.Klausur);
        return null;
    }

    // ---------- Gutachten ----------

    [RelayCommand]
    async Task GenerierenAsync()
    {
        try
        {
            if (!EingabenPruefen()) return;
            BausteineSpeichern();
            EinstellungenSpeichern();

            string ordner = Path.Combine(_ablage.Wurzel, _ablage.KlausurEinrichten(KlausurName, _notenliste));
            string kennung = Kennung;
            string ziel = Path.Combine(ordner, KlausurAblage.SicherName(kennung) + ".docx");
            string liste = Path.Combine(ordner, KlausurAblage.Notenliste);
            string vorlage = Path.Combine(ordner, KlausurAblage.WordVorlage);
            int punkte = NotenIndex;
            FormatierterText format = _editor.FormatLesen();
            string entwurfstext = format.Text;

            string[] gesperrt = new[] { liste, vorlage, ziel }.Where(KlausurAblage.IstGesperrt).Select(Path.GetFileName).OfType<string>().ToArray();
            if (gesperrt.Length > 0)
            {
                _ui.Melden(Meldungsart.Fehler, "Datei geöffnet",
                    $"Bitte zuerst schließen: {string.Join(", ", gesperrt.Select(g => $"„{g}“"))}.");
                return;
            }

            if (_teilnehmende.Count > 0 && !_teilnehmende.Any(t => Gleich(t.Kennung, kennung)) &&
                !await _ui.BestaetigenAsync("Nicht in der Teilnehmendenliste",
                    $"„{kennung}“ steht nicht in der Teilnehmendenliste dieser Klausur.\n\nTrotzdem erstellen?",
                    "Trotzdem erstellen", "Abbrechen (Tippfehler?)"))
            {
                _ui.Fokussieren(Eingabefeld.Student);
                return;
            }

            if (!await AngabenVollstaendigAsync()) return;

            if (Erstnote is int erst && ErstnoteAbweichend &&
                !await _ui.BestaetigenAsync("Abweichung vom Erstgutachten",
                    $"Erstgutachten: {Notenskala.MitPunkten(erst)}\nZweitgutachten: {Notenskala.MitPunkten(punkte)}\n\n" +
                    $"Die Noten weichen um {Math.Abs(punkte - erst)} Punkte voneinander ab.\n\nTrotzdem erstellen?",
                    "Trotzdem erstellen", "Zurück zur Note"))
            {
                _ui.Fokussieren(Eingabefeld.Note);
                return;
            }

            List<Baustein> unpassend = VerwendeteBausteine(entwurfstext).Where(b => !b.PasstZu(punkte)).ToList();
            if (unpassend.Count > 0)
            {
                string liste5 = string.Join("\n", unpassend.Take(5).Select(b => $"• „{Kurz(b.Text)}“ ({b.Bereich})"));
                string mehr = unpassend.Count > 5 ? $"\n… und {unpassend.Count - 5} weitere" : string.Empty;
                if (!await _ui.BestaetigenAsync("Bausteine passen nicht zur Note",
                        $"Für {Notenskala.MitPunkten(punkte)} sind folgende Bausteine nicht vorgesehen:\n\n{liste5}{mehr}\n\nTrotzdem erstellen?",
                        "Trotzdem erstellen", "Zurück zum Entwurf"))
                {
                    _ui.Fokussieren(Eingabefeld.Editor);
                    return;
                }
            }

            if (HatSchema && Rohpunkteschema.Berechnen(Rohpunktzeilen) is { HatEingaben: true, Offen: > 0 } summe &&
                !await _ui.BestaetigenAsync("Rohpunkte unvollständig",
                    $"{summe.Offen} Position(en) des Rohpunkteschemas sind noch ohne Punkte; sie werden mit 0 gewertet.\n\nTrotzdem erstellen?",
                    "Trotzdem erstellen", "Zurück zu den Rohpunkten"))
            {
                _ui.RohpunkteZeigen();
                return;
            }

            bool ueberarbeitung = Gleich(BearbeiteteKennung, kennung);
            bool inListe = _notenliste.Enthaelt(liste, kennung);
            bool docxVorhanden = File.Exists(ziel);
            if ((inListe || docxVorhanden) && !ueberarbeitung)
            {
                string was = inListe && docxVorhanden ? "in der Notenliste und als Gutachten"
                           : inListe ? "in der Notenliste" : "als Gutachten";
                bool ok = await _ui.BestaetigenAsync("Bereits vorhanden",
                    $"Für „{kennung}“ gibt es bereits einen Eintrag {was}.\n\nSoll er überschrieben werden?",
                    "Überschreiben", "Abbrechen (Tippfehler?)");
                if (!ok)
                {
                    _ui.Melden(Meldungsart.Information, "Nicht erstellt", "Bitte Name bzw. Kennziffer prüfen.");
                    _ui.Fokussieren(Eingabefeld.Student);
                    return;
                }
            }

            // Erst Word, dann Excel: schlägt Word fehl, bleibt die Notenliste unverändert.
            string teilpunkteText = HatSchema ? string.Empty : TeilpunkteText();
            List<List<TextLauf>> absaetze = TextNormalizer.FormatierteAbsaetze(format);
            if (_konfiguration.GliederungFett) absaetze = TextNormalizer.GliederungFett(absaetze);
            _gutachten.Erzeugen(vorlage, ziel, absaetze, Platzhalter(punkte, teilpunkteText),
                !HatSchema && _konfiguration.TeilpunkteAusgeben ? teilpunkteText : null,
                Bewertungsbogen(), _konfiguration.BogenPunkteAusgeben);

            GutachtenEntwurf stand = ZustandErfassen();
            try
            {
                EntwurfArchiv.Speichern(ordner, stand);
            }
            catch (Exception ex)
            {
                EinstellungsSpeicher.Protokollieren(ex);
            }

            try
            {
                List<(string Kopf, double? Wert)> zusatz = HatSchema
                    ? RohpunkteSpalten()
                    : Aufgaben.Select(a => (Kopf: a.Name, Wert: a.Wert)).ToList();
                zusatz.AddRange(ErstnotenSpalten(punkte)); // am Ende, damit vorhandene Spalten ihre Bedeutung behalten
                _notenliste.EintragSetzen(liste, kennung, punkte, zusatz.Count > 0 ? zusatz : null);
            }
            catch (Exception ex)
            {
                Fehler(ex, "Gutachten erstellt, Note NICHT eingetragen", liste);
                if (WordOeffnen) _ui.Oeffnen(ziel);
                return; // Entwurf bleibt erhalten, damit erneut generiert werden kann.
            }

            ZeitEintragen(ordner, kennung);
            NutzungEintragen(ordner, kennung, entwurfstext);
            if (WordOeffnen) _ui.Oeffnen(ziel);
            NaechsteArbeit();
            UebersichtAktualisieren();
            _ui.Melden(Meldungsart.Erfolg, "Gutachten erstellt", $"{Path.GetFileName(ziel)}: {Notenskala.MitPunkten(punkte)}",
                "Entwurf zurückholen", () => ZustandSetzen(stand, alsUeberarbeitung: true));
        }
        catch (Exception ex)
        {
            Fehler(ex, "Gutachten konnte nicht erstellt werden");
        }
    }

    bool EingabenPruefen()
    {
        if (KlausurName.Trim().Length < 2)
            return Ungueltig(Eingabefeld.Klausur, "Keine Klausur ausgewählt", "Sie müssen eine Klausur auswählen!");
        if (!Anonym && Matrikel.Trim().Length < 2)
            return Ungueltig(Eingabefeld.Matrikel, "Keine Matrikelnummer angegeben", "Sie müssen eine Matrikelnummer eingeben!");
        if (Student.Trim().Length < 2)
            return Anonym
                ? Ungueltig(Eingabefeld.Student, "Keine Kennziffer angegeben", "Sie müssen eine Kennziffer eingeben!")
                : Ungueltig(Eingabefeld.Student, "Kein Name angegeben", "Sie müssen einen Namen eingeben!");
        if (!Notenskala.IstGueltig(NotenIndex))
            return Ungueltig(Eingabefeld.Note, "Keine Note gewählt", "Bitte eine Note auswählen.");
        return true;
    }

    bool Ungueltig(Eingabefeld feld, string titel, string text)
    {
        _ui.Melden(Meldungsart.Fehler, titel, text);
        _ui.Fokussieren(feld);
        return false;
    }

    Dictionary<string, string> Platzhalter(int punkte, string teilpunkteText) => new()
    {
        ["###DEFINITION###"] = Notenskala.Definition(punkte),
        ["###NOTE###"] = Notenskala.MitPunkten(punkte),
        ["###FORM###"] = Formalsatz(),
        ["###LOB###"] = " Daher ist die Arbeit insgesamt mit der Note",
        ["###STUDENT###"] = Anonym ? Student.Trim() : $"{Student.Trim()} (Matr. Nr. {Matrikel.Trim()})",
        ["###NAME###"] = Student.Trim(),
        ["###MATRIKEL###"] = Matrikel.Trim(),
        [GutachtenService.TeilpunkteMarke] = !HatSchema && _konfiguration.TeilpunkteAusgeben ? teilpunkteText : string.Empty,
        ["###ROHPUNKTE###"] = HatSchema
            ? (Rohpunkteschema.Berechnen(Rohpunktzeilen) is { HatEingaben: true } s ? Zahl.Text(s.Gesamt) : string.Empty)
            : Aufgaben.Any(a => a.Wert is not null) ? Zahl.Text(Aufgaben.Sum(a => a.Wert ?? 0)) : string.Empty,
    };

    /// <summary>Leert die Eingaben für die nächste Arbeit.</summary>
    void NaechsteArbeit()
    {
        Student = string.Empty;
        Matrikel = string.Empty;
        Seiten = 0;
        foreach (KriteriumAuswahl k in Kriterien) k.Index = 0;
        foreach (AufgabenPunkte a in Aufgaben) a.Punkte = double.NaN;
        RohpunkteLeeren();
        NoteVorgeben(Notenskala.Vorgabe);
        BearbeiteteKennung = string.Empty;
        _editor.Leeren();
        _speicher.EntwurfLoeschen();
        _entwurfGeaendert = false;
        ZeitZuruecksetzen();
        EinstellungenSpeichern();
        _ui.Fokussieren(Eingabefeld.Student);
    }

    /// <summary>Strg+N: neue Arbeit beginnen; der bisherige Stand lässt sich zurückholen.</summary>
    public void NeueArbeit()
    {
        GutachtenEntwurf vorher = ZustandErfassen();
        bool leer = string.IsNullOrWhiteSpace(_editor.TextLesen()) && vorher.Kennung.Length == 0;
        string bearbeitet = BearbeiteteKennung;
        NaechsteArbeit();
        if (!leer)
            _ui.Melden(Meldungsart.Information, "Neue Arbeit", "Eingaben und Entwurf wurden geleert.",
                "Rückgängig", () => ZustandSetzen(vorher, alsUeberarbeitung: bearbeitet.Length > 0));
    }

    /// <summary>Erfasst Eingaben und Entwurf (für das Archiv und zum Rückgängigmachen).</summary>
    GutachtenEntwurf ZustandErfassen() => new()
    {
        Kennung = Kennung,
        Name = Student.Trim(),
        Matrikel = Matrikel.Trim(),
        Anonym = Anonym,
        Note = NotenIndex,
        Seiten = SeitenZahl,
        Kriterien = AktuelleKriterien(),
        Teilpunkte = AktuelleTeilpunkte(),
        Rohpunkte = AktuelleRohpunkte(),
        Rohkommentare = AktuelleRohkommentare(),
        Schema = _schemaKennung,
        Rtf = _editor.RtfLesen(),
        Stand = DateTime.Now,
    };

    void ZustandSetzen(GutachtenEntwurf e, bool alsUeberarbeitung)
    {
        Anonym = e.Anonym;
        Student = e.Name;
        Matrikel = e.Matrikel;
        _noteManuell = false;
        RohpunkteWiederherstellen(e.Rohpunkte, e.Rohkommentare, melden: e.Schema is not null && e.Schema != _schemaKennung);
        NotenIndex = Notenskala.IstGueltig(e.Note) ? e.Note : Notenskala.Vorgabe;
        Seiten = Math.Max(0, e.Seiten);
        AuswahlWiederherstellen(e.Kriterien, e.Teilpunkte, alleZuruecksetzen: true);
        NoteGesetzt = true; // Note eines vorhandenen Gutachtens gilt als gewählt
        if (string.IsNullOrEmpty(e.Rtf)) _editor.Leeren();
        else _editor.RtfSetzen(e.Rtf);
        BearbeiteteKennung = alsUeberarbeitung ? e.Kennung : string.Empty;
        _entwurfGeaendert = true;
        ZeitZuruecksetzen();
        EinstellungenSpeichern();
        _ui.Fokussieren(Eingabefeld.Editor);
    }

    // ---------- Ordner-Menü ----------

    [RelayCommand]
    void OrdnerOeffnen() => MitKlausur(ordner => _ui.Oeffnen(ordner));

    [RelayCommand]
    void NotenlisteOeffnen() => MitKlausur(ordner => _ui.Oeffnen(Path.Combine(ordner, KlausurAblage.Notenliste)));

    [RelayCommand]
    void VorlageBearbeiten() => MitKlausur(ordner => _ui.Oeffnen(Path.Combine(ordner, KlausurAblage.WordVorlage)));

    [RelayCommand]
    void ErwartungshorizontOeffnen() => MitKlausur(ordner =>
    {
        string? datei = PaketService.Inhalte.Where(n => n.StartsWith("Erwartungshorizont", StringComparison.OrdinalIgnoreCase))
            .Select(n => Path.Combine(ordner, n)).FirstOrDefault(File.Exists);
        if (datei is not null) _ui.Oeffnen(datei);
        else _ui.Melden(Meldungsart.Information, "Kein Erwartungshorizont",
            "Im Klausurordner liegt keine Datei „Erwartungshorizont.md/.txt/.docx/.pdf“. Sie wird z. B. mit einem Paket aus dem KI-Portal geliefert.");
    });

    [RelayCommand]
    async Task VorlageImportierenAsync()
    {
        try
        {
            if (KlausurOrdnerOderMeldung() is null) return;
            string ordner = Path.Combine(_ablage.Wurzel, _ablage.KlausurEinrichten(KlausurName, _notenliste));
            string? quelle = await _ui.DateiWaehlenAsync(".docx", ".dotx");
            if (quelle is null) return;

            string ziel = Path.Combine(ordner, KlausurAblage.WordVorlage);
            if (!KlausurAblage.GleicherPfad(quelle, ziel))
            {
                if (File.Exists(ziel)) File.Copy(ziel, KlausurAblage.Sicherungsname(ordner, KlausurAblage.WordVorlage));
                _gutachten.VorlageImportieren(quelle, ziel);
            }
            _ui.Oeffnen(ziel);
        }
        catch (Exception ex)
        {
            Fehler(ex, "Vorlage nicht importiert", KlausurAblage.WordVorlage);
        }
    }

    [RelayCommand]
    async Task AblageordnerWaehlenAsync()
    {
        try
        {
            string? neu = await _ui.OrdnerWaehlenAsync();
            if (neu is null || KlausurAblage.GleicherPfad(neu, _ablage.Wurzel)) return;

            BausteineSpeichern();
            string bisher = KlausurName;
            _ablage = new KlausurAblage(neu);
            IReadOnlyList<string> fehlend = _ablage.Einrichten();
            _einstellungen.Ablageordner = _ablage.Wurzel;
            OnPropertyChanged(nameof(Ablageordner));

            KlausurenNeuLaden();
            KlausurAktivieren(Klausuren.FirstOrDefault(k => Gleich(k, bisher)) ?? string.Empty);
            OnPropertyChanged(nameof(KlausurName)); // Auswahl in der View wiederherstellen, auch bei gleichem Namen
            EinstellungenSpeichern();

            string text = $"Neue Gutachten werden in „{_ablage.Wurzel}“ gespeichert. Bestehende Klausuren wurden nicht verschoben.";
            if (fehlend.Count > 0)
                _ui.Melden(Meldungsart.Fehler, "Programmvorlagen fehlen", $"{text} Nicht kopiert: {string.Join(", ", fehlend)}.");
            else if (CloudHinweis() is { } hinweis)
                _ui.Melden(Meldungsart.Warnung, "Ablage in Cloud-Ordner", $"{text} {hinweis}");
            else
                _ui.Melden(Meldungsart.Erfolg, "Ablageordner geändert", text);
        }
        catch (Exception ex)
        {
            Fehler(ex, "Ablageordner nicht geändert");
        }
    }

    void MitKlausur(Action<string> aktion)
    {
        try
        {
            if (KlausurOrdnerOderMeldung() is null) return;
            BausteineSpeichern();
            aktion(Path.Combine(_ablage.Wurzel, _ablage.KlausurEinrichten(KlausurName, _notenliste)));
        }
        catch (Exception ex)
        {
            Fehler(ex, "Aktion nicht möglich");
        }
    }

    string? CloudHinweis() =>
        _ablage.CloudAnbieter() is { } anbieter
            ? $"Der Ablageordner liegt in einem synchronisierten Ordner ({anbieter}). Gutachten und Notenlisten mit Namen und Matrikelnummern werden dorthin übertragen. Bitte prüfen, ob das zulässig ist, oder unter „Ordner › Ablageordner wählen…“ einen lokalen Ordner einstellen."
            : null;

    // ---------- Speichern ----------

    public void EntwurfGeaendert()
    {
        _entwurfGeaendert = true;
        Aktivitaet();
    }

    /// <summary>Vom Timer aufgerufen; schreibt nur bei Änderungen.</summary>
    public void Autosave(bool erzwingen = false)
    {
        if (!_initialisiert) return;
        try
        {
            if (_einstellungenGeaendert || erzwingen) EinstellungenSpeichern();
            if (_entwurfGeaendert || erzwingen)
            {
                if (string.IsNullOrWhiteSpace(_editor.TextLesen())) _speicher.EntwurfLoeschen();
                else _speicher.EntwurfSpeichern(_editor.RtfLesen());
                _entwurfGeaendert = false;
            }
            if (erzwingen)
            {
                BausteineSpeichern();
                UnterlageSichern(beenden: false);
            }
        }
        catch (Exception ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            if (!_autosaveFehlerGemeldet)
            {
                _autosaveFehlerGemeldet = true;
                _ui.Melden(Meldungsart.Warnung, "Automatisches Speichern fehlgeschlagen", ex.Message);
            }
        }
    }

    void EinstellungenSpeichern()
    {
        _einstellungen.Klausur = KlausurName;
        _einstellungen.Name = Student.Trim();
        _einstellungen.Matrikel = Matrikel.Trim();
        _einstellungen.Note = NotenIndex;
        _einstellungen.Anonym = Anonym;
        _einstellungen.Seiten = SeitenZahl;
        _einstellungen.Kriterien = AktuelleKriterien();
        _einstellungen.Teilpunkte = AktuelleTeilpunkte();
        _einstellungen.Rohpunkte = AktuelleRohpunkte();
        _einstellungen.Rohkommentare = AktuelleRohkommentare();
        _einstellungen.BearbeiteteKennung = BearbeiteteKennung;
        _einstellungen.SeitenleisteOffen = SeitenleisteOffen;
        _einstellungen.WordOeffnen = WordOeffnen;
        _einstellungen.BausteineOffen = BausteineOffen;
        _einstellungen.BausteineOben = BausteineOben;
        _einstellungen.ArbeitBeginn = _arbeitBeginn;
        _einstellungen.ArbeitSekunden = _arbeitSekunden;
        _einstellungen.Leiste = (int)Leiste;
        _einstellungen.KriterienAufgeklappt = KriterienAufgeklappt;
        _einstellungen.Satzvorschlaege = Satzvorschlaege;
        _einstellungen.AngabenHervorheben = AngabenHervorheben;
        _einstellungen.NoteGesetzt = NoteGesetzt;
        _speicher.Speichern(_einstellungen);
        _einstellungenGeaendert = false;
    }

    // ---------- Hilfen ----------

    void Fehler(Exception ex, string titel, string? datei = null)
    {
        EinstellungsSpeicher.Protokollieren(ex);
        string name = datei is null ? "eine benötigte Datei" : $"„{Path.GetFileName(datei)}“";
        string text = ex switch
        {
            FileNotFoundException fnf => fnf.Message,
            DirectoryNotFoundException => $"Der Ordner für {name} existiert nicht.",
            UnauthorizedAccessException => $"Keine Schreibberechtigung für {name}. Bitte Zugriffsrechte bzw. den Virenschutz (überwachter Ordnerzugriff) prüfen.",
            IOException io when IstSperrfehler(io) => $"{(datei is null ? "Eine Datei" : name)} ist in einem anderen Programm geöffnet. Bitte schließen und erneut versuchen.",
            _ => ex.Message,
        };
        _ui.Melden(Meldungsart.Fehler, titel, text);
    }

    static bool IstSperrfehler(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    static bool Gleich(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static string Kurz(string s, int max = 70) => s.Length <= max ? s : s[..(max - 1)] + "…";

    static readonly Regex Leerraum = new(@"\s+", RegexOptions.CultureInvariant);

    static string Normal(string s) => Leerraum.Replace(s, " ").Trim().TrimEnd('.', '!', '?', ':', ' ');
}
