using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace Gutachten2025.Services;

/// <summary>
/// Bearbeitungen und Erstvoten je Kennziffer (PDF oder DOCX) im Klausurordner: „Bearbeitungen\1017.pdf“, „Erstvoten\1017.docx“.
/// Der Dateiname beginnt mit der Kennziffer; danach darf ein Zusatz folgen („1017_Erstvotum.pdf“, „1017 - Müller.docx“).
/// </summary>
public static class Arbeitsdokumente
{
    public const string Bearbeitungen = "Bearbeitungen";
    public const string Erstvoten = "Erstvoten";

    static readonly string[] Endungen = { ".pdf", ".docx" };

    public static bool IstDokument(string pfad) =>
        Endungen.Contains(Path.GetExtension(pfad), StringComparer.OrdinalIgnoreCase) && !Path.GetFileName(pfad).StartsWith("~$", StringComparison.Ordinal);

    /// <summary>Datei zur Kennung (PDF vor DOCX, genauer Name vor Name mit Zusatz); null = keine.</summary>
    public static string? Finden(string klausurOrdner, string unterordner, string kennung)
    {
        kennung = kennung.Trim();
        string ordner = Path.Combine(klausurOrdner, unterordner);
        if (kennung.Length == 0 || !Directory.Exists(ordner)) return null;
        string sicher = KlausurAblage.SicherName(kennung);
        return Directory.EnumerateFiles(ordner)
            .Where(IstDokument)
            .Select(p => (Pfad: p, Stamm: Path.GetFileNameWithoutExtension(p)))
            .Where(d => Passt(d.Stamm, kennung) || Passt(d.Stamm, sicher))
            .OrderBy(d => d.Stamm.Length)
            .ThenBy(d => Path.GetExtension(d.Pfad).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(d => d.Pfad)
            .FirstOrDefault();
    }

    static bool Passt(string stamm, string kennung) =>
        stamm.Equals(kennung, StringComparison.OrdinalIgnoreCase)
        || (stamm.Length > kennung.Length && stamm.StartsWith(kennung, StringComparison.OrdinalIgnoreCase)
            && !char.IsLetterOrDigit(stamm[kennung.Length]));

    /// <summary>Kopiert PDF/DOCX-Dateien in den Unterordner; liefert (neu, ersetzt).</summary>
    public static (int Neu, int Ersetzt) Importieren(string klausurOrdner, string unterordner, IEnumerable<string> dateien)
    {
        string ziel = Path.Combine(klausurOrdner, unterordner);
        Directory.CreateDirectory(ziel);
        int neu = 0, ersetzt = 0;
        foreach (string quelle in dateien.Where(IstDokument))
        {
            string pfad = Path.Combine(ziel, Path.GetFileName(quelle));
            if (KlausurAblage.GleicherPfad(quelle, pfad)) continue;
            if (File.Exists(pfad)) ersetzt++;
            else neu++;
            File.Copy(quelle, pfad, overwrite: true);
        }
        return (neu, ersetzt);
    }

    const long MaxEintrag = 200L << 20;

    /// <summary>
    /// ZIP mit Bearbeitungen und/oder Erstvoten entpacken: Einträge, deren Pfad „Erstvot…“, „Erstgutacht…“, „Erstkorrektur“
    /// oder „Votum“ enthält, gelten als Erstvoten, solche mit „Bearbeit…“ als Bearbeitungen, die übrigen als <paramref name="standard"/>.
    /// Ordnerstrukturen im ZIP werden nicht übernommen (nur Dateinamen).
    /// </summary>
    public static (int Bearbeitungen, int Erstvoten, int Uebergangen) AusZip(string klausurOrdner, string zip, string standard)
    {
        int bearbeitungen = 0, erstvoten = 0, uebergangen = 0;
        using ZipArchive archiv = ZipFile.OpenRead(zip);
        foreach (ZipArchiveEntry e in archiv.Entries)
        {
            string name = Path.GetFileName(e.FullName);
            if (name.Length == 0) continue; // Ordner
            if (!IstDokument(name) || name.StartsWith("._", StringComparison.Ordinal) || e.FullName.Contains("__MACOSX", StringComparison.Ordinal)
                || e.Length > MaxEintrag)
            {
                uebergangen++;
                continue;
            }
            string art = Art(e.FullName, standard);
            string ziel = Path.Combine(klausurOrdner, art);
            Directory.CreateDirectory(ziel);
            e.ExtractToFile(Path.Combine(ziel, name), overwrite: true);
            if (art == Erstvoten) erstvoten++;
            else bearbeitungen++;
        }
        return (bearbeitungen, erstvoten, uebergangen);
    }

    static readonly Regex ErstvotumMuster = new(@"erst\s*-?\s*(vot|gutacht|korrektur)|votum", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex BearbeitungMuster = new(@"bearbeit", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Art(string pfad, string standard) =>
        ErstvotumMuster.IsMatch(pfad) ? Erstvoten : BearbeitungMuster.IsMatch(pfad) ? Bearbeitungen : standard;

    /// <summary>Einzelne Datei als Dokument zur Kennung ablegen („1017.pdf“); liefert den Zielpfad.</summary>
    public static string FuerKennung(string klausurOrdner, string unterordner, string kennung, string quelle)
    {
        string ordner = Path.Combine(klausurOrdner, unterordner);
        Directory.CreateDirectory(ordner);
        // Andere Dateien derselben Kennung (z. B. DOCX statt PDF) bleiben; gezeigt wird die PDF-Fassung zuerst.
        string ziel = Path.Combine(ordner, KlausurAblage.SicherName(kennung) + Path.GetExtension(quelle).ToLowerInvariant());
        if (!KlausurAblage.GleicherPfad(quelle, ziel)) File.Copy(quelle, ziel, overwrite: true);
        return ziel;
    }
}
