using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Gutachten2025.Services;

/// <summary>
/// Zweitkorrektur: Klausurordner „&lt;Klausur&gt; - Zweitkorrektur“ aus einer bestehenden Klausur anlegen (Vorlage,
/// Bausteine, Einstellungen, Unterlagen – ohne Noten, Gutachten, Entwürfe und Teilnehmende) und die Word-Vorlage auf
/// Zweitgutachten umstellen; Schlusssatz je nach Vergleich der Notenstufen.
/// </summary>
public static class Zweitkorrektur
{
    public const string Zusatz = " - Zweitkorrektur";

    /// <summary>Platzhalter für den Schlusssatz („Insgesamt …“ bzw. im Zweitgutachten je nach Notenstufe).</summary>
    public const string Schlussmarke = "###SCHLUSS###";

    /// <summary>Platzhalter für die Note des Erstgutachtens („gut (14 Punkte)“; leer ohne Erstnote).</summary>
    public const string Erstnotenmarke = "###ERSTNOTE###";

    public const string Zustimmungstext =
        "Ich stimme der Beurteilung der Stärken und Schwächen der Arbeit uneingeschränkt zu. Die Ausführungen des Erstgutachtens mache ich mir zu eigen.";

    /// <summary>Übliche Schlusssätze der Vorlagen, die im Zweitgutachten durch den Vergleichssatz ersetzt werden.</summary>
    public static readonly string[] Schlusswendungen =
    {
        "Insgesamt handelt es sich daher um ###DEFINITION###",
        "Insgesamt handelt es sich um ###DEFINITION###",
        "Es handelt sich daher um ###DEFINITION###",
        "Es handelt sich um ###DEFINITION###",
    };

    /// <summary>Umstellung der Word-Vorlage (Schlusssatz zuerst, damit er vor dem Wortersatz erkannt wird).</summary>
    public static IReadOnlyList<(string Alt, string Neu)> Vorlagenersetzungen { get; } =
        Schlusswendungen.Select(w => (w, Schlussmarke))
            .Concat(new[]
            {
                ("Erstgutachten", "Zweitgutachten"), ("ERSTGUTACHTEN", "ZWEITGUTACHTEN"),
                ("Erstvotum", "Zweitvotum"), ("ERSTVOTUM", "ZWEITVOTUM"),
            })
            .ToList();

    /// <summary>
    /// Schlusssatz: ohne Erstnote „Insgesamt handelt es sich daher um …“; sonst je nach Notenstufe übereinstimmend,
    /// schlechter („Leider … nur um …“) oder besser („… bereits um …“). Endet mit der Notendefinition.
    /// </summary>
    public static string Schlusssatz(int punkte, int? erstnote)
    {
        string definition = Notenskala.Definition(punkte);
        if (erstnote is not int e || !Notenskala.IstGueltig(e)) return "Insgesamt handelt es sich daher um " + definition;
        int vergleich = Notenskala.Stufe(punkte).CompareTo(Notenskala.Stufe(e));
        return vergleich switch
        {
            0 => "Insgesamt sehe ich die Arbeit übereinstimmend als " + definition,
            < 0 => "Leider handelt es sich aus meiner Sicht nur um " + definition,
            _ => "Es handelt sich aus meiner Sicht bereits um " + definition,
        };
    }

    /// <summary>Name der Zweitkorrektur zu einer Klausur („Klausur - Zweitkorrektur“, bei Bedarf mit „_(2)“).</summary>
    public static string Name(KlausurAblage ablage, string klausur) => ablage.FreierName(klausur.Trim() + Zusatz);

    /// <summary>
    /// Legt die Zweitkorrektur an und liefert ihren Ordnernamen sowie die Zahl der Ersetzungen in der Word-Vorlage.
    /// Übernommen werden nur die Inhalte eines Pakets (ohne Erstnoten); Notenliste und Teilnehmende beginnen leer.
    /// </summary>
    public static (string Name, Dictionary<string, int> Ersetzungen) Anlegen(KlausurAblage ablage, string klausur,
        NotenlisteService notenliste, GutachtenService gutachten)
    {
        string quelle = ablage.KlausurOrdner(klausur);
        if (!Directory.Exists(quelle)) throw new DirectoryNotFoundException($"Der Ordner der Klausur „{klausur}“ fehlt.");
        string name = Name(ablage, klausur);
        string ziel = ablage.KlausurOrdner(name);
        Directory.CreateDirectory(ziel);
        foreach (string datei in PaketService.Inhalte)
        {
            if (datei == Erstnoten.Datei) continue;
            string von = Path.Combine(quelle, datei);
            if (File.Exists(von)) File.Copy(von, Path.Combine(ziel, datei), overwrite: false);
        }
        name = ablage.KlausurEinrichten(name, notenliste); // Notenliste neu, fehlende Vorlagen ergänzen

        string konfigPfad = ablage.KonfigurationsPfad(name);
        Klausurkonfiguration k = KlausurKonfigDatei.Laden(konfigPfad);
        k.Zweitkorrektur = true;
        KlausurKonfigDatei.Speichern(konfigPfad, k);

        Dictionary<string, int> ersetzt = gutachten.TexteErsetzen(Path.Combine(ziel, KlausurAblage.WordVorlage), Vorlagenersetzungen);
        return (name, ersetzt);
    }

    /// <summary>Kurzbericht über die Umstellung der Vorlage.</summary>
    public static string Bericht(Dictionary<string, int> ersetzt)
    {
        int schluss = Schlusswendungen.Sum(w => ersetzt.GetValueOrDefault(w));
        int worte = ersetzt.Where(e => !Schlusswendungen.Contains(e.Key)).Sum(e => e.Value);
        var teile = new List<string>();
        if (worte > 0) teile.Add($"{worte}× „Erstgutachten/Erstvotum“ → „Zweitgutachten/Zweitvotum“");
        teile.Add(schluss > 0
            ? "Schlusssatz durch ###SCHLUSS### ersetzt (übereinstimmend/besser/schlechter)"
            : "kein Schlusssatz „Insgesamt handelt es sich daher um ###DEFINITION###“ gefunden – ggf. ###SCHLUSS### selbst einsetzen");
        return string.Join("; ", teile) + ".";
    }
}
