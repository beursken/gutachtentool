using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Gutachten2025.Services;

/// <summary>
/// Export für das Prüfungsportal (Klausuren2020, „Noten/Gutachten hochladen“): ZIP mit der Notenliste (.xlsx; Blatt 1,
/// ab Zeile 3: Spalte A Kennziffer bzw. Matrikelnummer, Spalte B Note in Punkten – das Format der Liste.xlsx)
/// und je Arbeit &lt;Kennziffer&gt;.pdf. Das Portal ordnet PDFs über den Dateinamen (Code oder Matrikelnummer) zu.
/// </summary>
public static class Klausurenportal
{
    public const string Listenname = "Notenliste.xlsx";

    /// <summary>Kennungen, deren Gutachten (.docx) vorliegt, aber noch kein PDF.</summary>
    public static List<(string Kennung, string Docx, string Pdf)> FehlendePdf(string klausurOrdner, IEnumerable<string> kennungen) =>
        kennungen.Select(k => (k, Path.Combine(klausurOrdner, KlausurAblage.SicherName(k) + ".docx"), Path.Combine(klausurOrdner, KlausurAblage.SicherName(k) + ".pdf")))
            .Where(x => File.Exists(x.Item2) && (!File.Exists(x.Item3) || File.GetLastWriteTimeUtc(x.Item3) < File.GetLastWriteTimeUtc(x.Item2)))
            .ToList();

    /// <summary>Schreibt das ZIP; Rückgabe: Zahl der PDFs.</summary>
    public static int Exportieren(string klausurOrdner, IEnumerable<string> kennungen, string ziel)
    {
        string liste = Path.Combine(klausurOrdner, KlausurAblage.Notenliste);
        if (!File.Exists(liste)) throw new FileNotFoundException("Es gibt noch keine Notenliste (Liste.xlsx).");
        string tmp = ziel + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        int pdf = 0;
        using (ZipArchive zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            Hinzufuegen(zip, liste, Listenname);
            foreach (string k in kennungen.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string datei = Path.Combine(klausurOrdner, KlausurAblage.SicherName(k) + ".pdf");
                if (!File.Exists(datei)) continue;
                Hinzufuegen(zip, datei, KlausurAblage.SicherName(k) + ".pdf");
                pdf++;
            }
        }
        File.Move(tmp, ziel, overwrite: true);
        return pdf;
    }

    static void Hinzufuegen(ZipArchive zip, string pfad, string name)
    {
        ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream ziel = e.Open();
        using var quelle = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        quelle.CopyTo(ziel);
    }
}
