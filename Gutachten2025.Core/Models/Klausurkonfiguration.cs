using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Gutachten2025.Models;

/// <summary>Ein Formalkriterium (z. B. Gliederung, Schrift) mit Satzvorlage und Stufen.</summary>
public sealed class Formalkriterium
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Satzvorlage; {subjekt} = „Die 12-seitige Arbeit“ bzw. „Sie“, {wert} = gewählte Stufe, {seiten} = Seitenzahl.</summary>
    public string Vorlage { get; set; } = "{subjekt} {wert}.";

    public List<string> Stufen { get; set; } = new();
}

/// <summary>Eine Aufgabe mit Höchstpunktzahl (Rohpunkte) für die Teilpunktevergabe.</summary>
public sealed class Aufgabe
{
    public string Name { get; set; } = string.Empty;
    public double Hoechstpunkte { get; set; }
}

/// <summary>Klausur.json: Formalkriterien, Aufgaben/Teilpunkte und optionaler Notenschlüssel einer Klausur.</summary>
public sealed class Klausurkonfiguration
{
    public int Formatversion { get; set; } = 1;

    /// <summary>Teilpunkte im Gutachten ausgeben (Platzhalter ###TEILPUNKTE### oder Absatz nach dem Freitext).</summary>
    public bool TeilpunkteAusgeben { get; set; }

    /// <summary>
    /// Bewertungsbogen (Rohpunkteschema) mit Punkten je Gliederungspunkt, Zwischen- und Gesamtsumme.
    /// false = nur die Anmerkungen zu den Gliederungspunkten, ohne Punkte. Fehlt der Wert in Klausur.json: true.
    /// </summary>
    public bool BogenPunkteAusgeben { get; set; } = true;

    public List<Aufgabe> Aufgaben { get; set; } = new();

    /// <summary>Optional: Mindest-Rohpunkte für 1, 2, … 18 Punkte (18 aufsteigende Werte). Leer = linear.</summary>
    public List<double>? Notenschluessel { get; set; }

    public List<Formalkriterium> Formalkriterien { get; set; } = new();

    [JsonIgnore]
    public double Hoechstsumme => Aufgaben.Sum(a => Math.Max(0, a.Hoechstpunkte));

    /// <summary>Notenvorschlag aus der Rohpunktsumme; null, wenn weder Schlüssel noch Höchstpunkte vorliegen.</summary>
    public int? Notenvorschlag(double rohpunkte)
    {
        if (Notenschluessel is { Count: 18 } schluessel)
            return schluessel.Count(grenze => rohpunkte + 1e-9 >= grenze);
        double max = Hoechstsumme;
        if (max <= 0) return null;
        return Math.Clamp((int)Math.Floor(Notenskala.Maximum * rohpunkte / max + 1e-9), Notenskala.Minimum, Notenskala.Maximum);
    }

    /// <summary>Entspricht den bis Version 1.0 fest eingebauten Auswahllisten.</summary>
    public static Klausurkonfiguration Standard() => new()
    {
        Formalkriterien = new()
        {
            new Formalkriterium
            {
                Name = "Gliederung",
                Vorlage = "{subjekt} {wert}.",
                Stufen = new()
                {
                    "ist klar durch Freizeilen und eine Gliederung strukturiert",
                    "ist durch (m.E. unnötige) Zwischenüberschriften klar gegliedert",
                    "hätte von zusätzlichen Gliederungsebenen profitiert",
                    "ist nicht durch die übliche juristische Gliederung (A - I - 1 - a - aa) geordnet",
                    "hätte von mehr Freizeilen profitiert",
                    "ist zwar durch Freizeilen strukturiert, hätte aber auch von einer Gliederung (A - I - 1 - a - aa) profitiert",
                    "ist leider nicht durch Gliederungspunkte und Freizeilen strukturiert",
                },
            },
            new Formalkriterium
            {
                Name = "Schrift",
                Vorlage = "{subjekt} ist {wert}.",
                Stufen = new()
                {
                    "sehr gut lesbar",
                    "gut lesbar",
                    "lesbar",
                    "noch lesbar",
                    "mitunter etwas schwer zu lesen",
                    "nur mühsam zu lesen",
                    "teilweise nicht zu entziffern",
                },
            },
        },
    };
}
