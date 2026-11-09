using System.Collections.Generic;
using System.Linq;

namespace Gutachten2025.Models;

/// <summary>Notenstufen und Definitionen nach § 1 JurPrNotSkV (0–18 Punkte).</summary>
public static class Notenskala
{
    public const int Minimum = 0;
    public const int Maximum = 18;
    public const int Vorgabe = 8;

    public static bool IstGueltig(int punkte) => punkte is >= Minimum and <= Maximum;

    public static string Bezeichnung(int punkte) => punkte switch
    {
        >= 16 => "sehr gut",
        >= 13 => "gut",
        >= 10 => "vollbefriedigend",
        >= 7 => "befriedigend",
        >= 4 => "ausreichend",
        >= 1 => "mangelhaft",
        _ => "ungenügend",
    };

    /// <summary>Notenstufe 0 (ungenügend) bis 6 (sehr gut) – zum Vergleich von Erst- und Zweitgutachten.</summary>
    public static int Stufe(int punkte) => punkte switch
    {
        >= 16 => 6,
        >= 13 => 5,
        >= 10 => 4,
        >= 7 => 3,
        >= 4 => 2,
        >= 1 => 1,
        _ => 0,
    };

    public static string Definition(int punkte) => punkte switch
    {
        >= 16 => "eine besonders hervorragende Leistung.",
        >= 13 => "eine erheblich über den durchschnittlichen Anforderungen liegende Leistung.",
        >= 10 => "eine über den durchschnittlichen Anforderungen liegende Leistung.",
        >= 7 => "eine Leistung, die in jeder Hinsicht durchschnittlichen Anforderungen entspricht.",
        >= 4 => "eine Leistung, die trotz ihrer Mängel durchschnittlichen Anforderungen noch entspricht.",
        >= 1 => "eine an erheblichen Mängeln leidende, im ganzen nicht mehr brauchbare Leistung.",
        _ => "eine völlig unbrauchbare Leistung.",
    };

    /// <summary>z. B. „gut (14 Punkte)“, „mangelhaft (1 Punkt)“.</summary>
    public static string MitPunkten(int punkte) =>
        $"{Bezeichnung(punkte)} ({punkte} {(punkte == 1 ? "Punkt" : "Punkte")})";

    /// <summary>Einträge der Notenauswahl; der Index entspricht der Punktzahl.</summary>
    public static IReadOnlyList<string> Auswahl { get; } =
        Enumerable.Range(Minimum, Maximum + 1).Select(p => $"{p} – {Bezeichnung(p)}").ToList();
}
