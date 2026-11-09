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
/// Hinweise auf fehlende oder vergessene Angaben: bereits begutachtete oder nicht vergebene Kennziffer (sofort bei der Eingabe),
/// unveränderte Note, fehlende Seitenzahl, Pflicht-Formalkriterien ohne Angabe (Klausureinstellungen „+!“), sehr kurzer
/// Entwurf, starke Abweichung vom Notenvorschlag. Felder ohne Angabe werden hervorgehoben; vor dem Erstellen fragt das
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
    [NotifyPropertyChangedFor(nameof(NoteOffen), nameof(SeitenOffen), nameof(PflichtkriterienOffen))]
    public partial bool AngabenHervorheben { get; set; }

    /// <summary>Hinweis, dass es zur eingegebenen Kennung schon ein Gutachten gibt; leer = keiner.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KennungVorhanden))]
    public partial string KennungHinweis { get; set; }

    public bool KennungVorhanden => KennungHinweis.Length > 0;

    public bool NoteOffen => AngabenHervorheben && !NoteGesetzt;

    public bool SeitenOffen => AngabenHervorheben && SeitenZahl == 0;

    /// <summary>Mindestens ein Pflicht-Formalkriterium ohne Angabe.</summary>
    public bool PflichtkriterienOffen => AngabenHervorheben && OffenePflichtkriterien.Any();

    IEnumerable<KriteriumAuswahl> OffenePflichtkriterien => Kriterien.Where(k => k.Kriterium.IstPflicht && k.Index == 0);

    /// <summary>Unter dieser Wortzahl gilt ein Entwurf als sehr kurz.</summary>
    public const int MindestWoerter = 60;

    /// <summary>Ab dieser Abweichung vom Notenvorschlag (Rohpunkte bzw. Teilpunkte) fragt das Programm nach.</summary>
    public const int VorschlagSchwelle = 3;

    /// <summary>Die Note wurde bestätigt, ohne sie zu ändern (erneuter Klick auf denselben Eintrag, z. B. 8 Punkte).</summary>
    public void NoteBestaetigen()
    {
        if (!NoteGesetzt) NoteGesetzt = true;
    }

    /// <summary>Kennziffern der Teilnehmendenliste als Bereich („1001–1060“) bzw. Anzahl.</summary>
    string Kennzifferbereich()
    {
        var zahlen = _teilnehmende.Select(t => long.TryParse(t.Kennung, out long z) ? z : (long?)null).ToList();
        return zahlen.All(z => z is not null) && zahlen.Count > 0
            ? $"{zahlen.Min()}–{zahlen.Max()}"
            : $"{_teilnehmende.Count} Kennungen";
    }

    partial void OnAngabenHervorhebenChanged(bool value) => KriterienHervorheben();

    /// <summary>Hervorhebung der Formalkriterien (Gliederung ohne Angabe) und Zusammenfassung aktualisieren.</summary>
    void KriterienHervorheben()
    {
        foreach (KriteriumAuswahl k in Kriterien) k.Hervorgehoben = AngabenHervorheben && k.Kriterium.IstPflicht && k.Index == 0;
        OnPropertyChanged(nameof(PflichtkriterienOffen));
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
        KannVorhandenesOeffnen = false;
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
        KannVorhandenesOeffnen = e?.Punkte is not null;
        // Nicht vergeben: erst ab der Länge der kürzesten Kennung prüfen, damit der Hinweis beim Tippen nicht flackert.
        bool ausserhalb = _teilnehmende.Count > 0 && e is null
                          && k.Length >= _teilnehmende.Min(t => t.Kennung.Length)
                          && !_teilnehmende.Any(t => Gleich(t.Kennung, k));
        KennungHinweis = e?.Punkte is int p
            ? $"Zu „{k}“ gibt es bereits ein Gutachten ({Notenskala.MitPunkten(p)}). Erstellen überschreibt es; zum Ändern „Öffnen“ wählen."
            : docx
                ? $"Zu „{k}“ gibt es bereits eine Word-Datei im Klausurordner. Erstellen überschreibt sie."
                : ausserhalb
                    ? $"„{k}“ liegt außerhalb der vergebenen Kennziffern ({Kennzifferbereich()}) – Tippfehler?"
                    : string.Empty;
    }

    /// <summary>„Öffnen“ beim Hinweis sichtbar (es gibt ein bewertetes Gutachten zur Kennung).</summary>
    [ObservableProperty] public partial bool KannVorhandenesOeffnen { get; set; }

    /// <summary>„Öffnen“ beim Hinweis: vorhandenes Gutachten zum Ändern laden.</summary>
    [RelayCommand]
    async Task VorhandenesOeffnenAsync()
    {
        string k = Kennung;
        if (_alleEintraege.FirstOrDefault(x => Gleich(x.Kennung, k) && x.Punkte is not null) is { } e) await EintragOeffnenAsync(e);
        else _ui.Melden(Meldungsart.Information, "Kein Entwurf gespeichert", "Bitte die Word-Datei im Klausurordner direkt bearbeiten.");
    }

    static int Woerter(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetter));

    /// <summary>Vor dem Erstellen: vergessene Angaben gesammelt nachfragen; false = zurück zur Eingabe.</summary>
    async Task<bool> AngabenVollstaendigAsync()
    {
        if (!AngabenHervorheben) return true;
        var fehlt = new List<(string Text, Action Fokus)>();
        if (!NoteGesetzt)
            fehlt.Add(($"Die Note steht unverändert auf der Vorgabe ({Notenskala.MitPunkten(NotenIndex)}).", () => _ui.Fokussieren(Eingabefeld.Note)));
        if (SeitenZahl == 0)
            fehlt.Add(("Es ist keine Seitenzahl angegeben.", () => _ui.Fokussieren(Eingabefeld.Seiten)));
        List<KriteriumAuswahl> offen = OffenePflichtkriterien.ToList();
        if (offen.Count > 0)
            fehlt.Add(($"Zu {(offen.Count == 1 ? "dem Formalkriterium" : "den Formalkriterien")} {string.Join(", ", offen.Select(o => $"„{o.Name}“"))} ist nichts angegeben.", () =>
            {
                KriterienAufgeklappt = true;
                _ui.Fokussieren(Eingabefeld.Kriterien);
            }));
        if (!(UneingeschraenkteZustimmung && Zweitkorrekturmodus))
        {
            int woerter = Woerter(_editor.TextLesen());
            if (woerter < MindestWoerter)
                fehlt.Add(($"Der Entwurf ist sehr kurz ({woerter} Wörter).", () => _ui.Fokussieren(Eingabefeld.Editor)));
        }
        int? vorschlag = HatSchema ? ErrechneteNote : Notenvorschlag;
        if (vorschlag is int v && Math.Abs(NotenIndex - v) >= VorschlagSchwelle)
            fehlt.Add(($"Die Note weicht um {Math.Abs(NotenIndex - v)} Punkte vom Notenvorschlag nach {(HatSchema ? "Rohpunkten" : "Teilpunkten")} ab ({Notenskala.MitPunkten(v)}).",
                () => _ui.Fokussieren(Eingabefeld.Note)));
        if (fehlt.Count == 0) return true;
        bool weiter = await _ui.BestaetigenAsync("Angaben prüfen",
            string.Join("\n", fehlt.Select(f => "• " + f.Text)) + "\n\nTrotzdem erstellen?\n\n(Abschalten: Schaltfläche „Pflichtangaben“ in der Werkzeugleiste.)",
            "Trotzdem erstellen", "Zurück zur Eingabe");
        if (!weiter) fehlt[0].Fokus();
        return weiter;
    }
}
