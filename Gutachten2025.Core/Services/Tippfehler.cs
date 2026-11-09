using System;
using System.Collections.Generic;
using System.Linq;

namespace Gutachten2025.Services;

/// <summary>
/// Berichtigt beim Tippen zwei Großbuchstaben am Wortanfang („GRoßbuchstabe“ → „Großbuchstabe“, „DIe“ → „Die“).
/// Abkürzungen bleiben unberührt: nur ganze Großschreibung („BGB“), gemischte Kürzel („BGBl“, „BVerfG“, „USt“, „AGs“)
/// und Wörter aus Sachverhalt und Lösung. Wörter mit genau einem Kleinbuchstaben werden nur berichtigt, wenn sie ein
/// häufiges kurzes Wort ergeben („DIe“, „UNd“).
/// </summary>
public static class Tippfehler
{
    static readonly HashSet<string> KurzeWoerter = new(StringComparer.Ordinal)
    {
        "die", "der", "das", "den", "dem", "des", "ein", "und", "sie", "wir", "ihr", "ihm", "ihn", "mit", "von", "vom", "zum", "zur",
        "bei", "auf", "aus", "für", "ist", "war", "hat", "man", "nur", "wie", "was", "wer", "vor", "bis", "als", "nun", "gut", "neu",
        "zu", "im", "in", "an", "am", "ab", "er", "es", "so", "da", "ob", "um", "wo", "ja", "zu",
    };

    /// <summary>Gebräuchliche Ausdrücke mit zwei führenden Großbuchstaben, die richtig geschrieben sind.</summary>
    static readonly HashSet<string> Ausnahmen = new(StringComparer.Ordinal)
    {
        "ITler", "ITlerin", "ITlerinnen", "ITlern", "IHKen", "PKWen", "LKWen", "EUweit",
    };

    /// <summary>Berichtigte Schreibung oder null (nichts zu tun).</summary>
    public static string? Berichtigen(string wort, ISet<string>? bekannt = null)
    {
        if (wort.Length < 2 || !char.IsUpper(wort[0]) || !char.IsUpper(wort[1])) return null;
        string rest = wort[2..];
        if (rest.Length == 0 || !rest.All(char.IsLower)) return null;
        if (Ausnahmen.Contains(wort) || (bekannt?.Contains(wort) ?? false)) return null;
        string neu = string.Concat(wort[0].ToString(), char.ToLowerInvariant(wort[1]).ToString(), rest);
        if (rest.Length >= 2) return neu;
        return KurzeWoerter.Contains(neu.ToLowerInvariant()) ? neu : null;
    }

    /// <summary>Wort unmittelbar vor <paramref name="ende"/> (nur Buchstaben); Start = -1, wenn keines.</summary>
    public static (int Start, string Wort) WortVor(string text, int ende)
    {
        int a = ende;
        while (a > 0 && char.IsLetter(text[a - 1])) a--;
        // Teil eines Wortes mit Ziffern oder Bindestrich („EU-weit“, „IPv6“): nicht anfassen.
        if (a == ende || (a > 0 && (char.IsLetterOrDigit(text[a - 1]) || text[a - 1] is '-' or '_' or '/' or '.' or '@'))) return (-1, string.Empty);
        return (a, text[a..ende]);
    }

    /// <summary>Zeichen, nach denen ein Wort als fertig getippt gilt.</summary>
    public static bool IstWortende(char c) => char.IsWhiteSpace(c) || c is '.' or ',' or ';' or ':' or '!' or '?' or ')' or '“' or '"' or '«' or '»' or '\'';
}
