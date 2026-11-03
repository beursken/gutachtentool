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
/// Hinweise auf fehlende oder vergessene Angaben: bereits begutachtete Kennziffer (sofort bei der Eingabe), unveränderte Note,
/// fehlende Seitenzahl, keine Angabe zur Gliederung. Felder ohne Angabe werden hervorgehoben; vor dem Erstellen fragt das
/// Programm nach (abschaltbar über „Pflichtangaben“).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Wahr, solange das Programm die Note selbst setzt (neue Arbeit), nicht die Nutzerin bzw. der Nutzer.</summary>
    bool _noteAutomatisch;

    /// <summary>Note seit Beginn der Arbeit gewählt (auch über Rohpunkte, Erstnote oder ein geöffnetes Gutachten).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteOffen))]
    public partial bool NoteGesetzt { get; set; }

    /// <summary>Fehlende Angaben hervorheben und vor dem Erstellen nachfragen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteOffen), nameof(SeitenOffen), nameof(GliederungOffen))]
    public partial bool AngabenHervorheben { get; set; }

    /// <summary>Hinweis, dass es zur eingegebenen Kennung schon ein Gutachten gibt; leer = keiner.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KennungVorhanden))]
    public partial string KennungHinweis { get; set; }

    public bool KennungVorhanden => KennungHinweis.Length > 0;

    public bool NoteOffen => AngabenHervorheben && !NoteGesetzt;

    public bool SeitenOffen => AngabenHervorheben && SeitenZahl == 0;

    public bool GliederungOffen => AngabenHervorheben && Gliederungskriterium is { Index: 0 };

    KriteriumAuswahl? Gliederungskriterium =>
        Kriterien.FirstOrDefault(k => k.Name.Contains("Gliederung", StringComparison.CurrentCultureIgnoreCase));

    partial void OnAngabenHervorhebenChanged(bool value) => KriterienHervorheben();

    /// <summary>Hervorhebung der Formalkriterien (Gliederung ohne Angabe) und Zusammenfassung aktualisieren.</summary>
    void KriterienHervorheben()
    {
        KriteriumAuswahl? g = Gliederungskriterium;
        foreach (KriteriumAuswahl k in Kriterien) k.Hervorgehoben = AngabenHervorheben && ReferenceEquals(k, g) && k.Index == 0;
        OnPropertyChanged(nameof(GliederungOffen));
    }

    /// <summary>Note programmseitig setzen, ohne dass sie als „gewählt“ gilt.</summary>
    void NoteVorgeben(int note)
    {
        _noteAutomatisch = true;
        try { NotenIndex = note; }
        finally { _noteAutomatisch = false; }
        NoteGesetzt = false;
    }

    /// <summary>Sofortprüfung bei der Eingabe: Gibt es zur Kennung schon ein Gutachten (Notenliste bzw. Word-Datei)?</summary>
    void KennungPruefen()
    {
        if (!_initialisiert) return; // während des Starts sind Eingaben und Ablage noch nicht gesetzt
        string k = Kennung;
        if (k.Length < 2 || KlausurName.Length == 0 || Gleich(BearbeiteteKennung, k))
        {
            KennungHinweis = string.Empty;
            return;
        }
        UebersichtEintrag? e = _alleEintraege.FirstOrDefault(x => Gleich(x.Kennung, k));
        bool docx = false;
        try
        {
            docx = File.Exists(Path.Combine(_ablage.KlausurOrdner(KlausurName), KlausurAblage.SicherName(k) + ".docx"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            EinstellungsSpeicher.Protokollieren(ex);
        }
        KennungHinweis = e?.Punkte is int p
            ? $"Zu „{k}“ gibt es bereits ein Gutachten ({Notenskala.MitPunkten(p)}). Erstellen überschreibt es; zum Ändern „Öffnen“ wählen."
            : docx
                ? $"Zu „{k}“ gibt es bereits eine Word-Datei im Klausurordner. Erstellen überschreibt sie."
                : string.Empty;
    }

    /// <summary>„Öffnen“ beim Hinweis: vorhandenes Gutachten zum Ändern laden.</summary>
    [RelayCommand]
    async Task VorhandenesOeffnenAsync()
    {
        string k = Kennung;
        if (_alleEintraege.FirstOrDefault(x => Gleich(x.Kennung, k) && x.Punkte is not null) is { } e) await EintragOeffnenAsync(e);
        else _ui.Melden(Meldungsart.Information, "Kein Entwurf gespeichert", "Bitte die Word-Datei im Klausurordner direkt bearbeiten.");
    }

    /// <summary>Vor dem Erstellen: vergessene Angaben (Note, Seiten, Gliederung) gesammelt nachfragen; false = zurück zur Eingabe.</summary>
    async Task<bool> AngabenVollstaendigAsync()
    {
        if (!AngabenHervorheben) return true;
        var fehlt = new List<(string Text, Action Fokus)>();
        if (!NoteGesetzt)
            fehlt.Add(($"Die Note steht unverändert auf der Vorgabe ({Notenskala.MitPunkten(NotenIndex)}).", () => _ui.Fokussieren(Eingabefeld.Note)));
        if (SeitenZahl == 0)
            fehlt.Add(("Es ist keine Seitenzahl angegeben.", () => _ui.Fokussieren(Eingabefeld.Seiten)));
        if (Gliederungskriterium is { Index: 0 } g)
            fehlt.Add(($"Zum Formalkriterium „{g.Name}“ ist nichts angegeben.", () =>
            {
                KriterienAufgeklappt = true;
                _ui.Fokussieren(Eingabefeld.Kriterien);
            }));
        if (fehlt.Count == 0) return true;
        bool weiter = await _ui.BestaetigenAsync("Angaben prüfen",
            string.Join("\n", fehlt.Select(f => "• " + f.Text)) + "\n\nTrotzdem erstellen?\n\n(Abschalten: Schaltfläche „Pflichtangaben“ in der Werkzeugleiste.)",
            "Trotzdem erstellen", "Zurück zur Eingabe");
        if (!weiter) fehlt[0].Fokus();
        return weiter;
    }
}
