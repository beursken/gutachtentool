using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gutachten2025.Models;
using Gutachten2025.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Gutachten2025.ViewModels;

/// <summary>Formalkriterien, Teilpunkte je Aufgabe, Notenvorschlag und Klausureinstellungen (Klausur.json).</summary>
public sealed partial class MainViewModel
{
    Klausurkonfiguration _konfiguration = Klausurkonfiguration.Standard();

    public ObservableCollection<KriteriumAuswahl> Kriterien { get; } = new();
    public ObservableCollection<AufgabenPunkte> Aufgaben { get; } = new();

    [ObservableProperty] public partial string PunkteText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatNotenvorschlag))]
    [NotifyCanExecuteChangedFor(nameof(NotenvorschlagUebernehmenCommand))]
    public partial int? Notenvorschlag { get; set; }

    public bool HatNotenvorschlag => Notenvorschlag is not null;

    public bool HatAufgaben => Aufgaben.Count > 0;

    public bool HatKriterien => Kriterien.Count > 0;

    /// <summary>Kurzfassung für den zugeklappten Bereich „Formalkriterien“.</summary>
    public string KriterienZusammenfassung
    {
        get
        {
            var gesetzt = Kriterien.Where(k => k.Wert is not null).ToList();
            if (Kriterien.Count == 0) return "keine eingerichtet (Ordner › Klausureinstellungen)";
            if (gesetzt.Count == 0) return $"keine Angabe ({Kriterien.Count} verfügbar)";
            string text = string.Join(" · ", gesetzt.Select(k => $"{k.Name}: {k.Wert}"));
            return $"{gesetzt.Count} von {Kriterien.Count} – {(text.Length > 90 ? text[..89] + "…" : text)}";
        }
    }

    public bool TeilpunkteWerdenAusgegeben => _konfiguration.TeilpunkteAusgeben;

    // ---------- Laden ----------

    void KonfigurationLaden()
    {
        try
        {
            _konfiguration = KlausurKonfigDatei.Laden(_ablage.KonfigurationsPfad(KlausurName));
        }
        catch (Exception ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            _konfiguration = Klausurkonfiguration.Standard();
            _ui.Melden(Meldungsart.Warnung, "Klausur.json nicht lesbar",
                $"Es gelten die Standard-Formalkriterien; die Datei bleibt unverändert. ({ex.Message})");
        }
        BewertungAufbauen();
    }

    void BewertungAufbauen()
    {
        foreach (KriteriumAuswahl k in Kriterien) k.PropertyChanged -= Bewertung_PropertyChanged;
        foreach (AufgabenPunkte a in Aufgaben) a.PropertyChanged -= Bewertung_PropertyChanged;
        Kriterien.Clear();
        Aufgaben.Clear();
        foreach (Formalkriterium f in _konfiguration.Formalkriterien)
        {
            var k = new KriteriumAuswahl(f);
            k.PropertyChanged += Bewertung_PropertyChanged;
            Kriterien.Add(k);
        }
        foreach (Aufgabe a in _konfiguration.Aufgaben)
        {
            var p = new AufgabenPunkte(a);
            p.PropertyChanged += Bewertung_PropertyChanged;
            Aufgaben.Add(p);
        }
        OnPropertyChanged(nameof(HatAufgaben));
        OnPropertyChanged(nameof(TeilpunkteSichtbar));
        OnPropertyChanged(nameof(HatKriterien));
        OnPropertyChanged(nameof(KriterienZusammenfassung));
        OnPropertyChanged(nameof(TeilpunkteWerdenAusgegeben));
        PunkteAktualisieren();
    }

    void Bewertung_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_initialisiert) _einstellungenGeaendert = true;
        if (sender is AufgabenPunkte) PunkteAktualisieren();
        else OnPropertyChanged(nameof(KriterienZusammenfassung));
    }

    Dictionary<string, string> AktuelleKriterien()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KriteriumAuswahl k in Kriterien)
            if (k.Wert is string w) d[k.Name] = w;
        return d;
    }

    Dictionary<string, double> AktuelleTeilpunkte()
    {
        var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (AufgabenPunkte a in Aufgaben)
            if (a.Wert is double w) d[a.Name] = w;
        return d;
    }

    /// <summary>Setzt die Auswahl nach Namen; ohne <paramref name="alleZuruecksetzen"/> bleiben nicht genannte Werte erhalten.</summary>
    void AuswahlWiederherstellen(IReadOnlyDictionary<string, string>? kriterien, IReadOnlyDictionary<string, double>? teilpunkte, bool alleZuruecksetzen = false)
    {
        foreach (KriteriumAuswahl k in Kriterien)
        {
            string? wert = kriterien?.FirstOrDefault(p => Gleich(p.Key, k.Name)).Value;
            if (wert is not null) k.WertSetzen(wert);
            else if (alleZuruecksetzen) k.Index = 0;
        }
        foreach (AufgabenPunkte a in Aufgaben)
        {
            var treffer = teilpunkte?.Where(p => Gleich(p.Key, a.Name)).Select(p => (double?)p.Value).FirstOrDefault();
            if (treffer is double w) a.Punkte = w;
            else if (alleZuruecksetzen) a.Punkte = double.NaN;
        }
        PunkteAktualisieren();
    }

    // ---------- Formalsatz und Teilpunkte ----------

    string Formalsatz()
    {
        int seiten = SeitenZahl;
        string subjekt = seiten > 0 ? $"Die {seiten}-seitige Arbeit" : "Die Arbeit";
        bool subjektVerwendet = false;
        var saetze = new List<string>();
        foreach (KriteriumAuswahl k in Kriterien)
        {
            if (k.Wert is not string wert) continue;
            string vorlage = k.Kriterium.Vorlage;
            bool mitSubjekt = vorlage.Contains("{subjekt}", StringComparison.Ordinal);
            string satz = vorlage
                .Replace("{subjekt}", subjektVerwendet ? "Sie" : subjekt)
                .Replace("{wert}", wert)
                .Replace("{seiten}", seiten.ToString())
                .Trim();
            if (satz.Length == 0) continue;
            subjektVerwendet |= mitSubjekt;
            satz = char.ToUpper(satz[0]) + satz[1..];
            if (satz[^1] is not ('.' or '!' or '?')) satz += ".";
            saetze.Add(satz);
        }
        if (saetze.Count == 0)
            return seiten > 0 ? $"Die Arbeit hat {seiten} {(seiten == 1 ? "Seite" : "Seiten")}. " : string.Empty;
        if (!subjektVerwendet && seiten > 0) saetze.Insert(0, $"Die Arbeit hat {seiten} Seiten.");
        return string.Join(" ", saetze) + " ";
    }

    string TeilpunkteText()
    {
        var eingegeben = Aufgaben.Where(a => a.Wert is not null).ToList();
        if (eingegeben.Count == 0) return string.Empty;
        string teile = string.Join("; ", eingegeben.Select(a => a.Hoechstpunkte > 0
            ? $"{a.Name}: {Zahl.Text(a.Wert!.Value)} von {Zahl.Text(a.Hoechstpunkte)} Punkten"
            : $"{a.Name}: {Zahl.Text(a.Wert!.Value)} Punkte"));
        double summe = eingegeben.Sum(a => a.Wert!.Value);
        double max = _konfiguration.Hoechstsumme;
        string gesamt = eingegeben.Count > 1
            ? max > 0 ? $" (insgesamt {Zahl.Text(summe)} von {Zahl.Text(max)} Punkten)" : $" (insgesamt {Zahl.Text(summe)} Punkte)"
            : string.Empty;
        return $"Bewertung der Aufgaben: {teile}{gesamt}.";
    }

    void PunkteAktualisieren()
    {
        if (Aufgaben.Count == 0)
        {
            PunkteText = string.Empty;
            Notenvorschlag = null;
            return;
        }
        var werte = Aufgaben.Select(a => a.Wert).ToList();
        double summe = werte.Sum(w => w ?? 0);
        double max = _konfiguration.Hoechstsumme;
        bool vollstaendig = werte.All(w => w is not null);
        int? vorschlag = vollstaendig ? _konfiguration.Notenvorschlag(summe) : null;
        Notenvorschlag = vorschlag;

        string text = max > 0 ? $"Summe {Zahl.Text(summe)} von {Zahl.Text(max)}" : $"Summe {Zahl.Text(summe)}";
        if (vorschlag is int p) text += $" · Vorschlag: {Notenskala.MitPunkten(p)}";
        else if (!vollstaendig) text += " · Vorschlag nach Eingabe aller Aufgaben";
        if (_konfiguration.TeilpunkteAusgeben) text += " · wird im Gutachten ausgegeben";
        PunkteText = text;
    }

    [RelayCommand(CanExecute = nameof(HatNotenvorschlag))]
    void NotenvorschlagUebernehmen()
    {
        if (Notenvorschlag is int p) NotenIndex = p;
    }

    // ---------- Klausureinstellungen ----------

    [RelayCommand]
    async Task KonfigurationBearbeitenAsync()
    {
        try
        {
            if (KlausurOrdnerOderMeldung() is null) return;
            _ablage.KlausurEinrichten(KlausurName, _notenliste);
            Klausurkonfiguration? neu = await _ui.KonfigurationBearbeitenAsync(KlausurName.Replace('_', ' '), _konfiguration);
            if (neu is null) return;
            var kriterien = AktuelleKriterien();
            var teilpunkte = AktuelleTeilpunkte();
            KlausurKonfigDatei.Speichern(_ablage.KonfigurationsPfad(KlausurName), neu);
            KonfigurationLaden();
            AuswahlWiederherstellen(kriterien, teilpunkte);
            EinstellungenSpeichern();
            _ui.Melden(Meldungsart.Erfolg, "Klausureinstellungen gespeichert",
                $"{_konfiguration.Formalkriterien.Count} Formalkriterien, {_konfiguration.Aufgaben.Count} Aufgaben.");
        }
        catch (Exception ex)
        {
            Fehler(ex, "Klausureinstellungen nicht gespeichert", KlausurAblage.Konfiguration);
        }
    }
}
