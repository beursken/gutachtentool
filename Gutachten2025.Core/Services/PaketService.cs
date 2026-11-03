using DocumentFormat.OpenXml.Packaging;
using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gutachten2025.Services;

/// <summary>Verständliche Fehlermeldung beim Prüfen eines Pakets.</summary>
public sealed class PaketFehler : Exception
{
    public PaketFehler(string meldung, Exception? innere = null) : base(meldung, innere) { }
}

/// <summary>Kopfdaten eines Gutachtentool-Pakets (manifest.json).</summary>
public sealed class PaketManifest
{
    public int Formatversion { get; set; } = PaketService.Formatversion;
    public string Programm { get; set; } = "Gutachtentool";
    public string? Programmversion { get; set; }
    public string Titel { get; set; } = string.Empty;
    public string? Beschreibung { get; set; }
    public string? Rechtsgebiet { get; set; }
    public string? Autor { get; set; }
    /// <summary>Herkunft, z. B. „KIPortal“.</summary>
    public string? Quelle { get; set; }
    public DateTime Erstellt { get; set; } = DateTime.Now;
    /// <summary>Nur Pakete des KI-Portals: Adresse und Schlüssel für die Bausteinstatistik (wird nie weitergegeben).</summary>
    public PaketRueckmeldung? Rueckmeldung { get; set; }
    /// <summary>SHA-256 (hex, Kleinbuchstaben) je enthaltener Datei.</summary>
    public Dictionary<string, string> Pruefsummen { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Geprüfter Inhalt eines Pakets (nur bekannte Dateien).</summary>
public sealed class PaketInhalt
{
    public required PaketManifest Manifest { get; init; }
    public required Dictionary<string, byte[]> Dateien { get; init; }

    public string Beschreibung()
    {
        var teile = new List<string>();
        if (Dateien.ContainsKey(KlausurAblage.Formulierungen)) teile.Add("Formulierungen");
        if (Dateien.ContainsKey(KlausurAblage.WordVorlage)) teile.Add("Word-Vorlage");
        if (Dateien.ContainsKey(KlausurAblage.Konfiguration)) teile.Add("Formalkriterien/Aufgaben");
        if (Dateien.ContainsKey(Rohpunkte.Rohpunkteschema.Dateiname) || Dateien.ContainsKey(Rohpunkte.Rohpunkteschema.Exceldatei)) teile.Add("Rohpunkteschema");
        if (Dateien.TryGetValue(Erstnoten.Datei, out byte[]? erst)) teile.Add($"Erstnoten ({Erstnoten.AusBytes(erst).Count} Kennziffern)");
        if (Dateien.ContainsKey(KlausurAblage.Sachverhalt)) teile.Add("Sachverhalt");
        if (Dateien.ContainsKey(KlausurAblage.Loesung)) teile.Add("Lösung");
        if (Dateien.TryGetValue(Normensammlung.Datei, out byte[]? normen)) teile.Add($"Normensammlung ({Normensammlung.AusBytes(normen).Count} Vorschriften)");
        if (Dateien.ContainsKey(Normensammlung.EigeneDatei)) teile.Add("eigene Normen");
        if (Dateien.Keys.Any(k => k.StartsWith("Erwartungshorizont", StringComparison.OrdinalIgnoreCase))) teile.Add("Erwartungshorizont");
        return string.Join(", ", teile);
    }
}

/// <summary>
/// Gutachtentool-Paket (*.gtpaket): ZIP mit manifest.json, Texte.txt, MUSTER.docx, Klausur.json und optional Rohpunkteschema
/// (rohpunkte.json/rohpunkte.xlsx wie im KI-Portal), Sachverhalt und Lösung (Markdown) sowie Erwartungshorizont.
/// Ältere Programmversionen übergehen ihnen unbekannte Dateien; die Formatversion bleibt daher 1.
/// Enthält nie Notenlisten, Gutachten, Entwürfe oder Teilnehmendenlisten. Einzige Ausnahme: Pakete des KI-Portals für ein
/// Zweitgutachten dürfen Erstnoten.json (nur Kennziffern und Punkte) enthalten; der Export des Gutachtentools gibt sie nie weiter.
/// </summary>
public static class PaketService
{
    public const string Endung = ".gtpaket";
    public const int Formatversion = 1;
    public const string ManifestName = "manifest.json";

    /// <summary>Zulässige Dateien (ohne Unterordner). Andere Einträge werden beim Import ignoriert.</summary>
    public static readonly string[] Inhalte =
    {
        KlausurAblage.Formulierungen, KlausurAblage.WordVorlage, KlausurAblage.Konfiguration,
        Rohpunkte.Rohpunkteschema.Dateiname, Rohpunkte.Rohpunkteschema.Exceldatei,
        KlausurAblage.Sachverhalt, KlausurAblage.Loesung, Erstnoten.Datei, Normensammlung.Datei, Normensammlung.EigeneDatei,
        "Erwartungshorizont.md", "Erwartungshorizont.txt", "Erwartungshorizont.docx", "Erwartungshorizont.pdf",
    };

    const long MaxDatei = 25L << 20;
    const long MaxGesamt = 60L << 20;
    const int MaxEintraege = 64;

    public static void Exportieren(string klausurOrdner, PaketManifest manifest, string ziel)
    {
        string tmp = ziel + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (ZipArchive zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (string name in Inhalte)
            {
                if (name == Erstnoten.Datei) continue; // Noten verlassen das Gutachtentool nicht
                string pfad = Path.Combine(klausurOrdner, name);
                if (!File.Exists(pfad)) continue;
                byte[] daten = GeteiltLesen(pfad);
                manifest.Pruefsummen[name] = Pruefsumme(daten);
                ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
                using Stream s = e.Open();
                s.Write(daten);
            }
            ZipArchiveEntry m = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
            using Stream ms = m.Open();
            JsonSerializer.Serialize(ms, manifest, KlausurKonfigDatei.Json);
        }
        File.Move(tmp, ziel, overwrite: true);
    }

    /// <summary>Liest und prüft ein Paket vollständig, ohne etwas zu schreiben.</summary>
    public static PaketInhalt Pruefen(string paket)
    {
        var dateien = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        byte[]? manifestDaten = null;
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(paket);
            if (zip.Entries.Count > MaxEintraege) throw new PaketFehler("Das Paket enthält zu viele Einträge.");
            long gesamt = 0;
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                // Nur Dateien der obersten Ebene mit bekanntem Namen; Pfade aus dem Archiv werden nie als Zielpfad verwendet.
                string? bekannt = e.FullName.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)
                    ? ManifestName
                    : Inhalte.FirstOrDefault(n => n.Equals(e.FullName, StringComparison.OrdinalIgnoreCase));
                if (bekannt is null) continue;
                if (e.Length > MaxDatei) throw new PaketFehler($"„{bekannt}“ ist zu groß.");
                gesamt += e.Length;
                if (gesamt > MaxGesamt) throw new PaketFehler("Das Paket ist zu groß.");
                byte[] daten = Lesen(e);
                if (bekannt == ManifestName) manifestDaten = daten;
                else dateien[bekannt] = daten;
            }
        }
        catch (InvalidDataException ex)
        {
            throw new PaketFehler("Die Datei ist kein gültiges Gutachtentool-Paket (ZIP beschädigt).", ex);
        }

        if (manifestDaten is null) throw new PaketFehler("Die Datei ist kein Gutachtentool-Paket (manifest.json fehlt).");
        PaketManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PaketManifest>(KlausurKonfigDatei.OhneBom(manifestDaten), KlausurKonfigDatei.Json)
                ?? throw new PaketFehler("manifest.json ist leer.");
        }
        catch (JsonException ex)
        {
            throw new PaketFehler($"manifest.json ist ungültig: {ex.Message}", ex);
        }
        manifest.Pruefsummen = new(manifest.Pruefsummen ?? new(), StringComparer.OrdinalIgnoreCase);
        if (manifest.Formatversion > Formatversion)
            throw new PaketFehler("Das Paket stammt aus einer neueren Programmversion. Bitte das Gutachtentool aktualisieren.");
        if (dateien.Count == 0) throw new PaketFehler("Das Paket enthält weder Formulierungen noch Vorlagen.");

        foreach (var (name, daten) in dateien)
        {
            if (manifest.Pruefsummen.TryGetValue(name, out string? soll) && !string.IsNullOrWhiteSpace(soll) &&
                !string.Equals(soll.Trim(), Pruefsumme(daten), StringComparison.OrdinalIgnoreCase))
                throw new PaketFehler($"„{name}“ ist beschädigt oder wurde verändert (Prüfsumme stimmt nicht).");
            if (name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) WordPruefen(name, daten);
            if (name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) ExcelPruefen(name, daten);
        }
        if (dateien.TryGetValue(KlausurAblage.Konfiguration, out byte[]? konfig))
        {
            try { KlausurKonfigDatei.AusBytes(konfig); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                throw new PaketFehler($"Klausur.json im Paket ist ungültig: {ex.Message}");
            }
        }
        if (dateien.TryGetValue(Normensammlung.Datei, out byte[]? normen))
        {
            try { Normensammlung.AusBytes(normen); }
            catch (InvalidDataException ex) { throw new PaketFehler(ex.Message, ex); }
        }
        if (dateien.TryGetValue(Erstnoten.Datei, out byte[]? erstnoten))
        {
            try { Erstnoten.AusBytes(erstnoten); }
            catch (InvalidDataException ex) { throw new PaketFehler(ex.Message, ex); }
        }
        if (dateien.TryGetValue(Rohpunkte.Rohpunkteschema.Dateiname, out byte[]? schema))
        {
            try
            {
                if (Rohpunkte.RpsBuilder.LiesJson(Encoding.UTF8.GetString(KlausurKonfigDatei.OhneBom(schema))) is null)
                    throw new InvalidDataException("leer");
            }
            catch (Exception ex) when (ex is not PaketFehler)
            {
                throw new PaketFehler($"rohpunkte.json im Paket ist ungültig: {ex.Message}");
            }
        }
        return new PaketInhalt { Manifest = manifest, Dateien = dateien };
    }

    /// <summary>Schreibt die Paketdateien in den Klausurordner; vorhandene, abweichende Dateien werden zuvor gesichert.</summary>
    public static int Entpacken(PaketInhalt inhalt, string klausurOrdner)
    {
        Directory.CreateDirectory(klausurOrdner);
        int gesichert = 0;
        foreach (var (name, daten) in inhalt.Dateien)
        {
            string ziel = Path.Combine(klausurOrdner, name);
            if (File.Exists(ziel))
            {
                if (GeteiltLesen(ziel).AsSpan().SequenceEqual(daten)) continue;
                File.Copy(ziel, KlausurAblage.Sicherungsname(klausurOrdner, name));
                gesichert++;
            }
            string tmp = ziel + ".tmp";
            File.WriteAllBytes(tmp, daten);
            File.Move(tmp, ziel, overwrite: true);
        }

        // Kennziffern der Erstnoten als Teilnehmendenliste (Vorschläge, offene Arbeiten in der Übersicht).
        if (inhalt.Dateien.TryGetValue(Erstnoten.Datei, out byte[]? erst))
            Erstnoten.TeilnehmendeErgaenzen(klausurOrdner, Erstnoten.AusBytes(erst));

        // Rohpunkteschema: maßgeblich ist rohpunkte.json des Pakets; eine ältere Excel-Fassung im Ordner wird beiseitegelegt,
        // eine mitgelieferte gilt als gleichwertig (älterer Zeitstempel, damit sie die JSON-Datei nicht überschreibt).
        string json = Path.Combine(klausurOrdner, Rohpunkte.Rohpunkteschema.Dateiname);
        string xlsx = Path.Combine(klausurOrdner, Rohpunkte.Rohpunkteschema.Exceldatei);
        if (inhalt.Dateien.ContainsKey(Rohpunkte.Rohpunkteschema.Dateiname) && File.Exists(xlsx))
        {
            if (inhalt.Dateien.ContainsKey(Rohpunkte.Rohpunkteschema.Exceldatei))
                File.SetLastWriteTimeUtc(xlsx, File.GetLastWriteTimeUtc(json).AddSeconds(-2));
            else
            {
                File.Move(xlsx, KlausurAblage.Sicherungsname(klausurOrdner, Rohpunkte.Rohpunkteschema.Exceldatei));
                gesichert++;
            }
        }
        return gesichert;
    }

    static void ExcelPruefen(string name, byte[] daten)
    {
        try
        {
            // Hauptteil über die Beziehungen des Pakets suchen, nicht über den Teilnamen (xl/workbook.xml):
            // Das Open XML SDK und andere Programme dürfen ihn anders benennen.
            using var ms = new MemoryStream(daten);
            using SpreadsheetDocument d = SpreadsheetDocument.Open(ms, false);
            if (d.WorkbookPart?.Workbook is null)
                throw new PaketFehler($"„{name}“ ist keine Excel-Datei.");
            if (d.WorkbookPart.VbaProjectPart is not null)
                throw new PaketFehler($"„{name}“ enthält Makros und wird aus Sicherheitsgründen nicht importiert.");
        }
        catch (PaketFehler) { throw; }
        catch (Exception ex)
        {
            throw new PaketFehler($"„{name}“ ist beschädigt.", ex);
        }
    }

    static void WordPruefen(string name, byte[] daten)
    {
        try
        {
            // Hauptteil über die Beziehungen des Pakets suchen, nicht über den Teilnamen (word/document.xml):
            // Vom KI-Portal erzeugte Dateien heißen z. B. word/document2.xml.
            using var ms = new MemoryStream(daten);
            using WordprocessingDocument d = WordprocessingDocument.Open(ms, false);
            if (d.MainDocumentPart?.Document is null)
                throw new PaketFehler($"„{name}“ ist keine Word-Datei.");
            if (d.MainDocumentPart.VbaProjectPart is not null)
                throw new PaketFehler($"„{name}“ enthält Makros und wird aus Sicherheitsgründen nicht importiert.");
        }
        catch (PaketFehler) { throw; }
        catch (Exception ex)
        {
            throw new PaketFehler($"„{name}“ ist beschädigt.", ex);
        }
    }

    static byte[] Lesen(ZipArchiveEntry e)
    {
        using Stream s = e.Open();
        using var ms = new MemoryStream();
        byte[] puffer = new byte[81920];
        int n;
        while ((n = s.Read(puffer, 0, puffer.Length)) > 0)
        {
            ms.Write(puffer, 0, n);
            if (ms.Length > MaxDatei) throw new PaketFehler($"„{e.FullName}“ ist zu groß.");
        }
        return ms.ToArray();
    }

    static byte[] GeteiltLesen(string pfad)
    {
        using var fs = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    static string Pruefsumme(byte[] daten) => Convert.ToHexString(SHA256.HashData(daten)).ToLowerInvariant();
}
