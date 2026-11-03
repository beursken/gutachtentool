using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Gutachten2025.Models;

/// <summary>Auswahl einer Stufe für ein Formalkriterium (Index 0 = keine Angabe).</summary>
public sealed partial class KriteriumAuswahl : ObservableObject
{
    public KriteriumAuswahl(Formalkriterium kriterium)
    {
        Kriterium = kriterium;
        Optionen = new[] { $"– {kriterium.Name}: keine Angabe –" }.Concat(kriterium.Stufen).ToList();
    }

    public Formalkriterium Kriterium { get; }
    public string Name => Kriterium.Name;
    public IReadOnlyList<string> Optionen { get; }

    [ObservableProperty]
    public partial int Index { get; set; }

    /// <summary>Hervorheben, solange keine Angabe gewählt ist (Gliederung, siehe MainViewModel.Pruefung).</summary>
    [ObservableProperty]
    public partial bool Hervorgehoben { get; set; }

    public string? Wert => Index > 0 && Index < Optionen.Count ? Optionen[Index] : null;

    public void WertSetzen(string? wert)
    {
        int i = wert is null ? 0 : Kriterium.Stufen.FindIndex(s => string.Equals(s, wert, StringComparison.Ordinal));
        Index = i < 0 ? 0 : (wert is null ? 0 : i + 1);
    }
}

/// <summary>Teilpunkte einer Aufgabe (NaN = nicht eingegeben).</summary>
public sealed partial class AufgabenPunkte : ObservableObject
{
    public AufgabenPunkte(Aufgabe aufgabe)
    {
        Aufgabe = aufgabe;
        Punkte = double.NaN;
    }

    public Aufgabe Aufgabe { get; }
    public string Name => Aufgabe.Name;
    public double Hoechstpunkte => Aufgabe.Hoechstpunkte;
    public double Maximum => Hoechstpunkte > 0 ? Hoechstpunkte : 1000;
    public string Kopf => Hoechstpunkte > 0 ? $"{Name} (max. {Zahl.Text(Hoechstpunkte)})" : Name;

    [ObservableProperty]
    public partial double Punkte { get; set; }

    public double? Wert => double.IsNaN(Punkte) ? null : Punkte;
}

/// <summary>Gespeicherter Stand eines Gutachtens (Entwürfe\&lt;Kennung&gt;.json) – zum erneuten Öffnen und Rückgängigmachen.</summary>
public sealed class GutachtenEntwurf
{
    public string Kennung { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Matrikel { get; set; } = string.Empty;
    public bool Anonym { get; set; } = true;
    public int Note { get; set; } = Notenskala.Vorgabe;
    public int Seiten { get; set; }
    public Dictionary<string, string> Kriterien { get; set; } = new();
    public Dictionary<string, double> Teilpunkte { get; set; } = new();
    /// <summary>Rohpunkte je Position (Kennung „1.6“; „+“ Bonus, „-“ Minus).</summary>
    public Dictionary<string, double> Rohpunkte { get; set; } = new();
    /// <summary>Kommentare je Position mit Fett/Kursiv.</summary>
    public Dictionary<string, List<TextLauf>> Rohkommentare { get; set; } = new();
    /// <summary>Fingerabdruck des Rohpunkteschemas bei der Bewertung.</summary>
    public string? Schema { get; set; }
    public string Rtf { get; set; } = string.Empty;
    public DateTime Stand { get; set; } = DateTime.Now;
}

/// <summary>Eintrag der Teilnehmendenliste.</summary>
public sealed class Teilnehmer
{
    public string Kennung { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public override string ToString() => Name.Length > 0 && Name != Kennung ? $"{Kennung} – {Name}" : Kennung;
}

/// <summary>Zeile der Übersicht in der Seitenleiste.</summary>
public sealed class UebersichtEintrag
{
    public string Kennung { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int? Punkte { get; init; }
    public bool HatEntwurf { get; init; }
    public bool SchemaGeaendert { get; init; }
    /// <summary>Bearbeitungszeit, z. B. „12.11. 14:03–14:31 · 24 Min.“ (leer = unbekannt).</summary>
    public string Zeit { get; init; } = string.Empty;
    /// <summary>Erstnote (Zweitgutachten), z. B. „Erstgutachten 9“.</summary>
    public string Erstnote { get; init; } = string.Empty;
    public string Zusatz => string.Join(" · ", new[] { Erstnote, Zeit }.Where(s => s.Length > 0));
    public bool HatZusatz => Zusatz.Length > 0;
    public bool IstOffen => Punkte is null;
    public string Anzeige => Name.Length > 0 && Name != Kennung ? $"{Kennung} – {Name}" : Kennung;
    public string Status => Punkte is int p ? $"{p} P.{(SchemaGeaendert ? " ⚠" : string.Empty)}" : "offen";
    public string Hinweis => Punkte is int p
        ? $"{Anzeige}: {Notenskala.MitPunkten(p)}{(SchemaGeaendert ? "\nRohpunkteschema seit der Bewertung geändert – bitte prüfen" : string.Empty)}{(HatEntwurf ? "\nKlick: Entwurf zum Ändern öffnen" : "\nKein gespeicherter Entwurf (ältere Version)")}"
        : $"{Anzeige}: noch nicht bewertet\nKlick: Kennung übernehmen";
    public double Deckkraft => Punkte is null ? 0.6 : 1.0;
}

public sealed record Kennzahl(string Bezeichnung, string Wert);

public sealed record VerteilungsBalken(int Punkte, int Anzahl, double Hoehe)
{
    public string Hinweis => $"{Punkte} {(Punkte == 1 ? "Punkt" : "Punkte")}: {Anzahl}";
    /// <summary>Beschriftet die Untergrenzen der Notenstufen.</summary>
    public string Beschriftung => Punkte is 0 or 4 or 7 or 10 or 13 or 16 ? Punkte.ToString(CultureInfo.CurrentCulture) : string.Empty;
    public bool Bestanden => Punkte >= 4;
    public bool NichtBestanden => Punkte < 4;
}

public static class Zahl
{
    static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Text(double wert) => wert.ToString("0.##", De);

    public static string Prozent(double anteil) => (anteil * 100).ToString("0.0", De) + " %";

    public static bool Lesen(string s, out double wert) =>
        double.TryParse(s.Trim(), NumberStyles.Float, De, out wert) ||
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out wert);
}
