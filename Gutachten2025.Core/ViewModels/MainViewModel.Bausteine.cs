using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Gutachten2025.Models;
using Gutachten2025.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Gutachten2025.ViewModels;

/// <summary>Bausteinbaum: Laden/Speichern, Suche, Kürzel, Punktebereich, Bearbeiten und Rückgängig.</summary>
public sealed partial class MainViewModel
{
    const int VerlaufMax = 50;

    readonly List<string> _verlauf = new();
    string? _vorZiehen;
    string _bausteinPfad = string.Empty;
    DateTime _bausteinStand;
    bool _bausteineGeladen, _bausteineGeaendert;

    public ObservableCollection<Baustein> Bausteine { get; } = new();
    public ObservableCollection<Baustein> Treffer { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BaumQuelle), nameof(Umsortierbar))]
    public partial string Suchtext { get; set; }

    /// <summary>Quelle des Baums: alle Bausteine oder – bei aktiver Suche – die Treffer.</summary>
    public object BaumQuelle => Umsortierbar ? Bausteine : Treffer;

    public bool Umsortierbar => Suchtext.Trim().Length == 0;

    public bool KannRueckgaengig => _verlauf.Count > 0;

    /// <summary>Leerer Zustand: Klausur ohne Bausteine.</summary>
    public bool KeineBausteine => Umsortierbar && Bausteine.Count == 0 && !KeineKlausur;

    /// <summary>Suche ohne Treffer.</summary>
    public bool KeineTreffer => !Umsortierbar && Treffer.Count == 0;

    partial void OnSuchtextChanged(string value) => TrefferAktualisieren();

    // ---------- Laden und Speichern ----------

    void BausteineLaden()
    {
        _bausteineGeladen = false;
        _bausteinPfad = _ablage.TextePfad(KlausurName);
        Bausteine.Clear();
        _verlauf.Clear();
        OnPropertyChanged(nameof(KannRueckgaengig));
        if (File.Exists(_bausteinPfad))
            foreach (Baustein b in Formulierungsdatei.Laden(_bausteinPfad)) Bausteine.Add(b);
        _bausteinStand = File.Exists(_bausteinPfad) ? File.GetLastWriteTimeUtc(_bausteinPfad) : DateTime.MinValue;
        _bausteineGeladen = true;
        _bausteineGeaendert = false;
        PassendAktualisieren();
        TrefferAktualisieren();
        NutzungAnwenden();
    }

    /// <summary>Schreibt nur, wenn erfolgreich geladen und tatsächlich geändert – nie über eine fremde Datei.</summary>
    void BausteineSpeichern()
    {
        if (!_bausteineGeladen || !_bausteineGeaendert || _bausteinPfad.Length == 0) return;
        Formulierungsdatei.Nummerieren(Bausteine);
        Formulierungsdatei.Speichern(_bausteinPfad, Bausteine);
        _bausteinStand = File.GetLastWriteTimeUtc(_bausteinPfad);
        _bausteineGeaendert = false;
    }

    void BausteineGeaendert()
    {
        _bausteineGeaendert = true;
        Formulierungsdatei.Nummerieren(Bausteine);
        PassendAktualisieren();
        TrefferAktualisieren();
        try { BausteineSpeichern(); }
        catch (Exception ex) { Fehler(ex, "Formulierungsdatei nicht gespeichert", _bausteinPfad); }
    }

    /// <summary>Lädt die Formulierungsdatei neu, wenn sie extern (z. B. im Editor) geändert wurde.</summary>
    public void ExterneAenderungenPruefen()
    {
        if (_initialisiert)
        {
            try { SchemaExternPruefen(); }
            catch (Exception ex) { Fehler(ex, "Rohpunkteschema nicht lesbar"); }
        }
        if (!_initialisiert || _bausteinPfad.Length == 0 || !File.Exists(_bausteinPfad)) return;
        try
        {
            if (File.GetLastWriteTimeUtc(_bausteinPfad) <= _bausteinStand) return;
            if (_bausteineGeaendert)
            {
                _ui.Melden(Meldungsart.Warnung, "Formulierungsdatei extern geändert",
                    "Die Datei wurde außerhalb des Programms geändert; ungespeicherte Änderungen im Baum bleiben erhalten.");
                _bausteinStand = File.GetLastWriteTimeUtc(_bausteinPfad);
                return;
            }
            BausteineLaden();
            _ui.Melden(Meldungsart.Information, "Formulierungen neu geladen", "Die extern geänderte Formulierungsdatei wurde übernommen.");
        }
        catch (Exception ex)
        {
            Fehler(ex, "Formulierungsdatei nicht lesbar", _bausteinPfad);
        }
    }

    // ---------- Rückgängig ----------

    void Merken()
    {
        _verlauf.Add(Formulierungsdatei.Serialisieren(Bausteine));
        if (_verlauf.Count > VerlaufMax) _verlauf.RemoveAt(0);
        OnPropertyChanged(nameof(KannRueckgaengig));
    }

    [RelayCommand]
    void BausteinRueckgaengig()
    {
        if (_verlauf.Count == 0)
        {
            _ui.Melden(Meldungsart.Information, "Nichts rückgängig zu machen", "Es gibt keine weiteren Änderungen an den Bausteinen.");
            return;
        }
        string stand = _verlauf[^1];
        _verlauf.RemoveAt(_verlauf.Count - 1);
        OnPropertyChanged(nameof(KannRueckgaengig));
        Bausteine.Clear();
        foreach (Baustein b in Formulierungsdatei.Parsen(stand)) Bausteine.Add(b);
        BausteineGeaendert();
        _ui.Melden(Meldungsart.Information, "Rückgängig", "Die letzte Änderung an den Bausteinen wurde zurückgenommen.");
    }

    /// <summary>Vor dem Ziehen im Baum aufgerufen.</summary>
    public void VorUmsortieren() => _vorZiehen = Formulierungsdatei.Serialisieren(Bausteine);

    /// <summary>Nach dem Ziehen: auf Bausteine gezogene Elemente einsortieren, bei Änderung speichern.</summary>
    public void NachUmsortieren()
    {
        string? vorher = _vorZiehen;
        _vorZiehen = null;
        Formulierungsdatei.Normalisieren(Bausteine);
        if (vorher is null || Formulierungsdatei.Serialisieren(Bausteine) == vorher) return;
        _verlauf.Add(vorher);
        if (_verlauf.Count > VerlaufMax) _verlauf.RemoveAt(0);
        OnPropertyChanged(nameof(KannRueckgaengig));
        BausteineGeaendert();
    }

    // ---------- Verwenden ----------

    /// <summary>Fügt einen Baustein ein bzw. klappt eine Gruppe auf/zu.</summary>
    public void BausteinVerwenden(Baustein b)
    {
        if (b.IstGruppe) b.IsExpanded = !b.IsExpanded;
        else _editor.Einfuegen(b.Text);
    }

    /// <summary>Alt+Nummer: fügt den Baustein ein; bei einer Gruppe wird diese geliefert, damit die View sie auswählt.</summary>
    public Baustein? BausteinNachNummer(int nr)
    {
        Baustein? b = Formulierungsdatei.Alle(Bausteine).FirstOrDefault(x => x.Nr == nr);
        if (b is null) return null;
        if (!b.IstGruppe)
        {
            _editor.Einfuegen(b.Text);
            return null;
        }
        b.IsExpanded = true;
        return b;
    }

    /// <summary>Baustein zum festen Kürzel (ohne Beachtung von Groß-/Kleinschreibung).</summary>
    public Baustein? BausteinNachKuerzel(string kuerzel)
    {
        string k = kuerzel.Trim().TrimStart('#');
        return k.Length == 0 ? null
            : Formulierungsdatei.Alle(Bausteine).FirstOrDefault(b => !b.IstGruppe && Gleich(b.Kuerzel, k));
    }

    public Baustein? ErsterTreffer => Treffer.FirstOrDefault();

    void TrefferAktualisieren()
    {
        try
        {
            TrefferSuchen();
        }
        finally
        {
            OnPropertyChanged(nameof(KeineBausteine));
            OnPropertyChanged(nameof(KeineTreffer));
        }
    }

    void TrefferSuchen()
    {
        Treffer.Clear();
        string[] woerter = Suchtext.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? begriffe = woerter.Length == 0 ? null : string.Join(' ', woerter.Select(w => w.TrimStart('#')));
        foreach (Baustein b in Formulierungsdatei.Alle(Bausteine)) b.Suchbegriffe = begriffe;
        if (woerter.Length == 0) return;
        foreach (Baustein b in Formulierungsdatei.Alle(Bausteine))
        {
            if (b.IstGruppe) continue;
            bool passt = woerter.All(w =>
                b.Text.Contains(w, StringComparison.CurrentCultureIgnoreCase) ||
                ("#" + b.Kuerzel).Contains(w, StringComparison.CurrentCultureIgnoreCase) ||
                b.Nr.ToString() == w);
            if (passt) Treffer.Add(b);
        }
    }

    /// <summary>
    /// Satzvorschläge zum begonnenen Satz: zuerst Bausteine, die mit dem Getippten beginnen (Fortsetzung),
    /// sonst Bausteine, die alle getippten Wörter enthalten (das letzte als Wortanfang). Passende Punktebereiche zuerst.
    /// </summary>
    public IReadOnlyList<Satzvorschlag> SatzvorschlaegeFinden(string fragment)
    {
        string f = Leerraum.Replace(fragment, " ").TrimStart();
        if (f.Trim().Length < 3) return Array.Empty<Satzvorschlag>();
        var bausteine = Formulierungsdatei.Alle(Bausteine).Where(b => !b.IstGruppe && b.Text.Trim().Length > 0)
            .OrderByDescending(b => b.Passend).ToList();

        var liste = new List<Satzvorschlag>();
        var gesehen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (Baustein b in bausteine)
        {
            string t = Leerraum.Replace(b.Text, " ").Trim();
            if (t.Length > f.Length && t.StartsWith(f, StringComparison.CurrentCultureIgnoreCase) && gesehen.Add(t))
                liste.Add(new Satzvorschlag(b, t[f.Length..], Fortsetzung: true));
        }
        if (liste.Count > 0) return liste.Take(10).ToList();

        string[] woerter = f.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool genug = woerter.Length >= 2 ? woerter.Sum(w => w.Length) >= 6 : woerter.Length == 1 && woerter[0].Length >= 6;
        if (!genug) return Array.Empty<Satzvorschlag>();
        string letztes = woerter[^1];
        bool letztesVollstaendig = fragment.EndsWith(' ');
        foreach (Baustein b in bausteine)
        {
            string t = Leerraum.Replace(b.Text, " ").Trim();
            string[] tw = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool passt = woerter[..^1].All(w => t.Contains(w, StringComparison.CurrentCultureIgnoreCase))
                && (letztesVollstaendig
                    ? t.Contains(letztes, StringComparison.CurrentCultureIgnoreCase)
                    : tw.Any(x => x.TrimStart('(', '„', '"').StartsWith(letztes, StringComparison.CurrentCultureIgnoreCase)));
            if (passt && gesehen.Add(t)) liste.Add(new Satzvorschlag(b, t, Fortsetzung: false));
            if (liste.Count >= 10) break;
        }
        return liste;
    }

    void PassendAktualisieren()
    {
        foreach (Baustein b in Formulierungsdatei.Alle(Bausteine))
            b.Passend = b.PasstZu(NotenIndex);
    }

    /// <summary>Bausteine mit Punktebereich, deren Text im Entwurf vorkommt.</summary>
    List<Baustein> VerwendeteBausteine(string entwurf)
    {
        string text = Normal(entwurf);
        return Formulierungsdatei.Alle(Bausteine)
            .Where(b => !b.IstGruppe && b.HatBereich)
            .Where(b => Normal(b.Text) is { Length: >= 8 } t && text.Contains(t, StringComparison.Ordinal))
            .ToList();
    }

    // ---------- Bearbeiten ----------

    string? BausteinPruefen(BausteinDaten d, Baustein? ausser)
    {
        string? k = Formulierungsdatei.KuerzelNormalisieren(d.Kuerzel);
        if (k is null) return "Das Kürzel darf nur Buchstaben, Ziffern, „_“ und „-“ enthalten (höchstens 24 Zeichen).";
        if (k.Length > 0 && Formulierungsdatei.Alle(Bausteine).Any(b => !ReferenceEquals(b, ausser) && Gleich(b.Kuerzel, k)))
            return $"Das Kürzel „{k}“ ist bereits vergeben.";
        if (d.Von is int v && d.Bis is int b2 && v > b2) return "„ab Punkte“ darf nicht größer sein als „bis Punkte“.";
        return null;
    }

    static void Uebernehmen(Baustein b, BausteinDaten d)
    {
        b.Text = d.Text.Trim();
        b.Kuerzel = Formulierungsdatei.KuerzelNormalisieren(d.Kuerzel) ?? string.Empty;
        b.PunkteVon = d.Von;
        b.PunkteBis = d.Bis;
    }

    /// <summary>Fügt einen Eintrag ein: in eine gewählte Gruppe, sonst hinter den gewählten Baustein, sonst ans Ende.</summary>
    void Einordnen(Baustein neu, Baustein? bezug)
    {
        if (bezug is { IstGruppe: true })
        {
            bezug.Children.Add(neu);
            bezug.IsExpanded = true;
        }
        else if (bezug is not null && Formulierungsdatei.Finden(Bausteine, bezug) is { } ort)
            ort.Liste.Insert(ort.Index + 1, neu);
        else
            Bausteine.Add(neu);
    }

    /// <summary>Strg+S mit Markierung oder Kontextmenü: neuer Baustein.</summary>
    public async Task NeuerBausteinAsync(string vorgabe, Baustein? bezug)
    {
        if (!Umsortierbar) Suchtext = string.Empty;
        BausteinDaten? d = await _ui.BausteinBearbeitenAsync("Neuer Textbaustein",
            new BausteinDaten(vorgabe.Trim(), string.Empty, null, null), x => BausteinPruefen(x, null));
        if (d is null) return;
        Merken();
        var b = new Baustein();
        Uebernehmen(b, d);
        Einordnen(b, bezug);
        BausteineGeaendert();
    }

    public async Task NeueGruppeAsync(Baustein? bezug)
    {
        if (!Umsortierbar) Suchtext = string.Empty;
        string? name = await _ui.TextEingebenAsync("Neue Gruppe", string.Empty, "Bezeichnung der Gruppe");
        if (string.IsNullOrWhiteSpace(name)) return;
        Merken();
        Einordnen(new Baustein(name.Trim(), gruppe: true), bezug);
        BausteineGeaendert();
    }

    [RelayCommand]
    Task NeuerBausteinOhneBezugAsync() => NeuerBausteinAsync(string.Empty, null);

    [RelayCommand]
    Task NeueGruppeOhneBezugAsync() => NeueGruppeAsync(null);

    public async Task BausteinBearbeitenAsync(Baustein b)
    {
        if (b.IstGruppe)
        {
            string? text = await _ui.TextEingebenAsync("Gruppe umbenennen", b.Text, "Bezeichnung der Gruppe");
            if (string.IsNullOrWhiteSpace(text) || text.Trim() == b.Text) return;
            Merken();
            b.Text = text.Trim();
            BausteineGeaendert();
            return;
        }
        BausteinDaten? d = await _ui.BausteinBearbeitenAsync("Textbaustein bearbeiten",
            new BausteinDaten(b.Text, b.Kuerzel, b.PunkteVon, b.PunkteBis), x => BausteinPruefen(x, b));
        if (d is null) return;
        if (d.Text.Trim() == b.Text && (Formulierungsdatei.KuerzelNormalisieren(d.Kuerzel) ?? "") == b.Kuerzel
            && d.Von == b.PunkteVon && d.Bis == b.PunkteBis) return;
        Merken();
        Uebernehmen(b, d);
        BausteineGeaendert();
    }

    public async Task BausteinLoeschenAsync(Baustein b)
    {
        if (b.IstGruppe && b.Children.Count > 0 &&
            !await _ui.BestaetigenAsync("Gruppe löschen",
                $"„{b.Text}“ und alle {Formulierungsdatei.Nachfahren(b).Count()} enthaltenen Einträge löschen?", "Löschen", "Abbrechen"))
            return;
        if (Formulierungsdatei.Finden(Bausteine, b) is not { } ort) return;
        Merken();
        ort.Liste.RemoveAt(ort.Index);
        BausteineGeaendert();
        _ui.Melden(Meldungsart.Information, "Gelöscht", $"„{Kurz(b.Text)}“ wurde gelöscht.", "Rückgängig", BausteinRueckgaengig);
    }

    [RelayCommand]
    void FormulierungenBearbeiten()
    {
        try
        {
            BausteineSpeichern();
            if (!File.Exists(_bausteinPfad)) Formulierungsdatei.Speichern(_bausteinPfad, Bausteine);
            _ui.Oeffnen(_bausteinPfad);
            _ui.Melden(Meldungsart.Information, "Formulierungsdatei geöffnet",
                "Änderungen werden übernommen, sobald Sie nach dem Speichern zum Gutachtentool zurückkehren.");
        }
        catch (Exception ex)
        {
            Fehler(ex, "Formulierungsdatei nicht geöffnet", _bausteinPfad);
        }
    }

    [RelayCommand]
    async Task FormulierungenImportierenAsync()
    {
        try
        {
            BausteineSpeichern();
            string? quelle = await _ui.DateiWaehlenAsync(".txt");
            if (quelle is null || KlausurAblage.GleicherPfad(quelle, _bausteinPfad)) return;

            string ordner = Path.GetDirectoryName(_bausteinPfad)!;
            Directory.CreateDirectory(ordner);
            if (File.Exists(_bausteinPfad)) File.Copy(_bausteinPfad, KlausurAblage.Sicherungsname(ordner, KlausurAblage.Formulierungen));
            File.Copy(quelle, _bausteinPfad, overwrite: true);
            BausteineLaden();
            _ui.Melden(Meldungsart.Erfolg, "Formulierungen importiert",
                $"{Formulierungsdatei.Alle(Bausteine).Count(b => !b.IstGruppe)} Bausteine geladen; die bisherige Datei wurde gesichert.");
        }
        catch (Exception ex)
        {
            Fehler(ex, "Formulierungen nicht importiert", _bausteinPfad);
        }
    }
}
