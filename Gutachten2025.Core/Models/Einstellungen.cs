using System;
using System.Collections.Generic;

namespace Gutachten2025.Models;

/// <summary>Persistierter Zustand (einstellungen.json). Der Entwurfstext liegt separat als RTF.</summary>
public sealed class Einstellungen
{
    public string? Ablageordner { get; set; }
    public string Klausur { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Matrikel { get; set; } = string.Empty;
    public int Note { get; set; } = Notenskala.Vorgabe;
    public bool Anonym { get; set; } = true;
    public int Seiten { get; set; }

    /// <summary>Nur zur Übernahme älterer Einstellungen (Index der früher festen Auswahllisten).</summary>
    public int Gliederung { get; set; }
    public int Schrift { get; set; }

    /// <summary>Gewählte Stufe je Formalkriterium (Name → Text der Stufe).</summary>
    public Dictionary<string, string> Kriterien { get; set; } = new();

    /// <summary>Eingegebene Teilpunkte je Aufgabe (Name → Punkte).</summary>
    public Dictionary<string, double> Teilpunkte { get; set; } = new();

    /// <summary>Kennung eines erneut geöffneten Gutachtens (leer = neue Arbeit).</summary>
    public string BearbeiteteKennung { get; set; } = string.Empty;

    // Darstellung
    public bool Nebeneinander { get; set; }
    public double Bausteinanteil { get; set; } = 0.25;
    public double Zoom { get; set; } = 14;
    public bool SeitenleisteOffen { get; set; } = true;
    public bool EinfuehrungGezeigt { get; set; }

    public bool BausteineOffen { get; set; } = true;
    public bool BausteineOben { get; set; }
    /// <summary>Linke Seitenleiste: 0 = Bausteine, 1 = Sachverhalt, 2 = Lösung, 3 = beides (siehe Leistenanzeige).</summary>
    public int Leiste { get; set; }
    public double UnterlagenBreite { get; set; } = 380;

    // Bearbeitungszeit der aktuellen Arbeit (übersteht einen Neustart)
    public DateTime? ArbeitBeginn { get; set; }
    public double ArbeitSekunden { get; set; }
    /// <summary>Letzte Suche nach einer neuen Version (höchstens einmal am Tag).</summary>
    public DateTime UpdateGeprueft { get; set; }
    public double BausteinBreite { get; set; } = 340;
    public double BausteinHoehe { get; set; } = 200;
    public bool KriterienAufgeklappt { get; set; } = true;
    /// <summary>0 = wie Windows, 1 = hell, 2 = dunkel.</summary>
    public int Darstellung { get; set; }
    public double Oberflaechenskala { get; set; } = 1.0;
    public string EditorSchrift { get; set; } = string.Empty;
    public bool ZeilenbreiteBegrenzt { get; set; } = true;

    // Fenster (physische Pixel)
    public int FensterX { get; set; } = int.MinValue;
    public int FensterY { get; set; } = int.MinValue;
    public int FensterBreite { get; set; }
    public int FensterHoehe { get; set; }
    public bool FensterMaximiert { get; set; }

    // Rohpunkte der aktuellen Arbeit
    public Dictionary<string, double> Rohpunkte { get; set; } = new();
    public Dictionary<string, List<TextLauf>> Rohkommentare { get; set; } = new();

    // Verhalten
    public bool WordOeffnen { get; set; } = true;
    public bool Satzvorschlaege { get; set; } = true;
}
