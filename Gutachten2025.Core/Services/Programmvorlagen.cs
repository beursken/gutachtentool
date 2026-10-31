using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Gutachten2025.Services;

/// <summary>
/// In Gutachten2025.Core eingebettete Programmvorlagen (Vorlagen\*). Alle Fassungen (Windows, macOS, Browser)
/// erhalten so dieselben Dateien, unabhängig davon, ob sie neben dem Programm liegen.
/// </summary>
public static class Programmvorlagen
{
    public const string Rohpunkte = "Rohpunkte-Muster.xlsx";

    static readonly Assembly Baugruppe = typeof(Programmvorlagen).Assembly;

    static string? Ressource(string datei) =>
        Baugruppe.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(".Vorlagen." + datei, StringComparison.OrdinalIgnoreCase));

    public static bool Vorhanden(string datei) => Ressource(datei) is not null;

    public static Stream Oeffnen(string datei) =>
        Ressource(datei) is { } r && Baugruppe.GetManifestResourceStream(r) is { } s
            ? s
            : throw new FileNotFoundException($"Die Programmvorlage „{datei}“ fehlt.", datei);

    /// <summary>Schreibt die Vorlage nach <paramref name="ziel"/> (überschreibt).</summary>
    public static void Schreiben(string datei, string ziel)
    {
        using Stream quelle = Oeffnen(datei);
        using FileStream f = File.Create(ziel);
        quelle.CopyTo(f);
    }

    public static byte[] Lesen(string datei)
    {
        using Stream quelle = Oeffnen(datei);
        using var m = new MemoryStream();
        quelle.CopyTo(m);
        return m.ToArray();
    }
}
