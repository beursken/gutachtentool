using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Gutachten2025.Services;

/// <summary>
/// Kennziffern aus einer Eingabe wie „1001-1050, 1060; K1–K20“ (Bereiche und Einzelwerte, getrennt durch Komma,
/// Semikolon, Leerraum oder Zeilenumbruch). Führende Nullen bleiben erhalten („001-120“ → 001 … 120).
/// </summary>
public static class Kennziffern
{
    public const int Hoechstzahl = 5000;

    static readonly Regex Strich = new(@"\s*[-–—]\s*", RegexOptions.CultureInvariant);
    static readonly Regex Trenner = new(@"[,;\s]+", RegexOptions.CultureInvariant);
    static readonly Regex Bereich = new(@"^(?<p1>[^\d\s-]*)(?<z1>\d{1,9})-(?<p2>[^\d\s-]*)(?<z2>\d{1,9})$", RegexOptions.CultureInvariant);

    /// <summary>Kennziffern in Reihenfolge, ohne Doppelte; <see cref="FormatException"/> bei unzulässigen Bereichen.</summary>
    public static List<string> Lesen(string? eingabe)
    {
        var liste = new List<string>();
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string text = Strich.Replace(eingabe ?? string.Empty, "-");
        foreach (string teil in Trenner.Split(text))
        {
            if (teil.Length == 0) continue;
            Match m = Bereich.Match(teil);
            if (!m.Success)
            {
                if (gesehen.Add(teil)) liste.Add(teil);
                continue;
            }
            string praefix = m.Groups["p1"].Value, p2 = m.Groups["p2"].Value;
            if (p2.Length > 0 && !p2.Equals(praefix, StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"„{teil}“: Anfang und Ende des Bereichs haben verschiedene Vorsätze.");
            string z1 = m.Groups["z1"].Value;
            long von = long.Parse(z1, CultureInfo.InvariantCulture), bis = long.Parse(m.Groups["z2"].Value, CultureInfo.InvariantCulture);
            if (bis < von) throw new FormatException($"„{teil}“: Das Ende des Bereichs liegt vor dem Anfang.");
            if (bis - von + 1 + liste.Count > Hoechstzahl) throw new FormatException($"Höchstens {Hoechstzahl} Kennziffern.");
            int breite = z1.Length > 1 && z1[0] == '0' ? z1.Length : 0;
            for (long z = von; z <= bis; z++)
            {
                string k = praefix + (breite > 0 ? z.ToString(CultureInfo.InvariantCulture).PadLeft(breite, '0') : z.ToString(CultureInfo.InvariantCulture));
                if (gesehen.Add(k)) liste.Add(k);
            }
        }
        if (liste.Count > Hoechstzahl) throw new FormatException($"Höchstens {Hoechstzahl} Kennziffern.");
        return liste;
    }

    /// <summary>Bereich minus nicht vergebener Kennziffern (gleiche Schreibweise, Groß-/Kleinschreibung egal).</summary>
    public static List<string> Ohne(IEnumerable<string> alle, IEnumerable<string> weg)
    {
        var raus = new HashSet<string>(weg, StringComparer.OrdinalIgnoreCase);
        return alle.Where(k => !raus.Contains(k)).ToList();
    }
}
