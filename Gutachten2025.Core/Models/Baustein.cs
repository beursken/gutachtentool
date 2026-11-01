using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;

namespace Gutachten2025.Models;

/// <summary>
/// Ein Textbaustein (Formulierung) oder eine Gruppe im Baum. Gruppen können beliebig tief geschachtelt werden.
/// Bausteine können ein festes Kürzel (Einfügen mit Kürzel + Tab) und einen Punktebereich tragen.
/// </summary>
public sealed partial class Baustein : ObservableObject
{
    public Baustein()
    {
        Text = string.Empty;
        Kuerzel = string.Empty;
        IsExpanded = true;
        Passend = true;
    }

    public Baustein(string text, bool gruppe = false) : this()
    {
        Text = text;
        IstGruppe = gruppe;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Anzeige), nameof(NrText))]
    public partial int Nr { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Anzeige), nameof(Hinweis))]
    public partial string Text { get; set; }

    /// <summary>Kürzel ohne „#“; leer = keines.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Kennzeichen), nameof(HatKennzeichen), nameof(Hinweis), nameof(KuerzelText), nameof(HatKuerzel))]
    public partial string Kuerzel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Kennzeichen), nameof(HatKennzeichen), nameof(Hinweis), nameof(HatBereich), nameof(Bereich), nameof(BereichArt), nameof(KuerzelText), nameof(HatKuerzel))]
    public partial int? PunkteVon { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Kennzeichen), nameof(HatKennzeichen), nameof(Hinweis), nameof(HatBereich), nameof(Bereich), nameof(BereichArt))]
    public partial int? PunkteBis { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hinweis), nameof(NrText), nameof(AnzahlText), nameof(HatBereich), nameof(Bereich), nameof(BereichArt))]
    public partial bool IstGruppe { get; set; }

    /// <summary>False, wenn der Punktebereich nicht zur gewählten Note passt (Baustein wird abgeblendet).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Deckkraft))]
    public partial bool Passend { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Anzahl der Bausteine in einer Gruppe (einschließlich Untergruppen).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnzahlText))]
    public partial int Anzahl { get; set; }

    /// <summary>In wie vielen erstellten Gutachten der Baustein vorkommt (Bausteinstatistik).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NutzungText), nameof(Hinweis), nameof(HatNutzungstext))]
    public partial int Nutzung { get; set; }

    /// <summary>0 = keine Statistik, 1 = bisher nie verwendet, 2 = verwendet, 3 = häufig verwendet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NutzungText), nameof(Haeufig), nameof(Unbenutzt), nameof(HatNutzungstext))]
    public partial int NutzungStufe { get; set; }

    public bool Haeufig => NutzungStufe == 3;
    public bool Unbenutzt => NutzungStufe == 1;
    public string NutzungText => IstGruppe || NutzungStufe == 0 ? string.Empty : Nutzung == 0 ? "nie" : $"{Nutzung}×";
    public bool HatNutzungstext => NutzungText.Length > 0;

    /// <summary>Suchbegriffe zum Hervorheben (nur in der Trefferliste gesetzt).</summary>
    [ObservableProperty]
    public partial string? Suchbegriffe { get; set; }

    public ObservableCollection<Baustein> Children { get; } = new();

    public bool IstUeberschrift => IstGruppe;

    public bool HatBereich => !IstGruppe && (PunkteVon is not null || PunkteBis is not null);

    public int Von => PunkteVon ?? Notenskala.Minimum;

    public int Bis => PunkteBis ?? Notenskala.Maximum;

    public bool PasstZu(int punkte) => !HatBereich || (Von <= punkte && punkte <= Bis);

    public string Bereich => !HatBereich ? string.Empty
        : Von == Bis ? $"{Von} P."
        : $"{Von}–{Bis} P.";

    public string Anzeige => $"{Nr} – {Text}";

    public string Kennzeichen =>
        string.Join(" · ", new[] { Kuerzel.Length > 0 ? "#" + Kuerzel : string.Empty, Bereich }.Where(s => s.Length > 0));

    public bool HatKennzeichen => Kennzeichen.Length > 0;

    public string Hinweis
    {
        get
        {
            if (IstGruppe) return $"{Nr} – {Text} ({Anzahl} Bausteine)\nKlick: auf-/zuklappen · Rechtsklick: bearbeiten, neue Bausteine";
            string s = Text;
            if (Kuerzel.Length > 0) s += $"\nKürzel: {Kuerzel} (im Editor „{Kuerzel}“ + Tab)";
            if (HatBereich) s += $"\nPassend für {Bereich.Replace(" P.", " Punkte")}";
            if (NutzungStufe > 0) s += Nutzung == 0 ? "\nBisher in keinem Gutachten verwendet" : $"\nIn {Nutzung} Gutachten verwendet";
            return s + "\nKlick oder Ziehen in den Editor: einfügen · Rechtsklick: bearbeiten";
        }
    }

    public double Deckkraft => Passend ? 1.0 : 0.45;

    /// <summary>Nummer für Alt+Nummer (bei Gruppen leer, die Nummer steht dann im Tooltip).</summary>
    public string NrText => IstGruppe ? string.Empty : Nr.ToString();

    public string AnzahlText => IstGruppe && Anzahl > 0 ? Anzahl.ToString() : string.Empty;

    public string KuerzelText => Kuerzel.Length > 0 ? "#" + Kuerzel : string.Empty;

    public bool HatKuerzel => Kuerzel.Length > 0;

    /// <summary>0 = kein Bereich, 1 = nicht bestanden (bis 3 P.), 2 = gut (ab 10 P.), 3 = mittlerer Bereich.</summary>
    public int BereichArt => !HatBereich ? 0 : Bis < 4 ? 1 : Von >= 10 ? 2 : 3;

    // Schriftstärke: in der Oberfläche aus IstGruppe (WinUI: Darstellung.Gewicht).
}

/// <summary>Vorschlag beim Tippen: Fortsetzung des begonnenen Satzes oder ganzer Baustein als Ersatz für das Getippte.</summary>
public sealed record Satzvorschlag(Baustein Baustein, string Text, bool Fortsetzung);

/// <summary>Eingabewerte des Bearbeitungsdialogs.</summary>
public sealed record BausteinDaten(string Text, string Kuerzel, int? Von, int? Bis);
