using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Gutachten2025.Services;

/// <summary>
/// Die früher mitgelieferten Beispielbausteine (Vorlagen\Texte-Beispiele.txt). Die Programmvorlage Texte.txt ist jetzt leer;
/// Klausuren und Vorlagenordner, die noch die Beispiele enthalten, werden erkannt, damit sie sich mit einem Klick leeren lassen.
/// </summary>
public static class Beispielbausteine
{
    public const string Datei = "Texte-Beispiele.txt";

    static HashSet<string>? _texte;

    static HashSet<string> Texte => _texte ??= Programmvorlagen.Vorhanden(Datei)
        ? new HashSet<string>(Formulierungsdatei.Alle(Formulierungsdatei.Parsen(Encoding.UTF8.GetString(Programmvorlagen.Lesen(Datei)).TrimStart('﻿')))
            .Where(b => !b.IstGruppe).Select(b => b.Text.Trim()), StringComparer.Ordinal)
        : new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Bestehen die Bausteine (fast) nur aus den Beispielen?</summary>
    public static bool SindBeispiele(IEnumerable<Baustein> bausteine)
    {
        List<string> t = Formulierungsdatei.Alle(bausteine).Where(b => !b.IstGruppe).Select(b => b.Text.Trim()).ToList();
        return t.Count >= 10 && Texte.Count > 0 && t.Count(Texte.Contains) >= t.Count * 0.9;
    }

    /// <summary>Ersetzt eine Formulierungsdatei mit den Beispielen durch die leere Programmvorlage (Sicherung bleibt im Ordner).</summary>
    public static void Leeren(string pfad)
    {
        string ordner = Path.GetDirectoryName(pfad)!;
        if (File.Exists(pfad)) File.Copy(pfad, KlausurAblage.Sicherungsname(ordner, Path.GetFileName(pfad)));
        Programmvorlagen.Schreiben(KlausurAblage.Formulierungen, pfad);
    }

    /// <summary>Vorlagenordner beim Start: alte Beispiel-Vorlage durch die leere ersetzen (nur wenn unverändert).</summary>
    public static void VorlageAktualisieren(string vorlagenOrdner)
    {
        try
        {
            string pfad = Path.Combine(vorlagenOrdner, KlausurAblage.Formulierungen);
            if (File.Exists(pfad) && SindBeispiele(Formulierungsdatei.Laden(pfad)))
                Leeren(pfad);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EinstellungsSpeicher.Protokollieren(ex);
        }
    }
}
