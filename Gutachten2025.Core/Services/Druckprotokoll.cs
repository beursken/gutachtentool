using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Gutachten2025.Services;

/// <summary>Druckstatus eines Gutachtens.</summary>
public sealed class Druckeintrag
{
    public DateTime Stand { get; set; }
    /// <summary>true = an den Drucker übergeben (und ohne gemeldeten Fehler); false = fehlgeschlagen.</summary>
    public bool Ok { get; set; }
    public string? Meldung { get; set; }
}

/// <summary>Zustand der Druckwarteschlange (Standarddrucker); <see cref="Problem"/> = Drucker offline, Papierstau usw.</summary>
public sealed record Druckstand(int Auftraege, string? Problem, IReadOnlyList<string> FehlerhafteDokumente);

/// <summary>
/// Druckprotokoll.json im Klausurordner: welche Gutachten mit „Alle Gutachten drucken“ übergeben wurden und welche
/// fehlschlugen – damit sich nach einem Druckerproblem nur die fehlenden erneut drucken lassen.
/// </summary>
public static class Druckprotokoll
{
    public const string Datei = "Druckprotokoll.json";

    public static Dictionary<string, Druckeintrag> Laden(string klausurOrdner)
    {
        try
        {
            string pfad = Path.Combine(klausurOrdner, Datei);
            if (!File.Exists(pfad)) return new(StringComparer.OrdinalIgnoreCase);
            var d = JsonSerializer.Deserialize<Dictionary<string, Druckeintrag>>(File.ReadAllText(pfad, Encoding.UTF8), KlausurKonfigDatei.Json);
            return d is null ? new(StringComparer.OrdinalIgnoreCase) : new(d, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void Speichern(string klausurOrdner, Dictionary<string, Druckeintrag> protokoll)
    {
        string pfad = Path.Combine(klausurOrdner, Datei);
        string tmp = pfad + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(protokoll, KlausurKonfigDatei.Json), new UTF8Encoding(false));
        File.Move(tmp, pfad, overwrite: true);
    }

    /// <summary>Nach dem erneuten Erstellen gilt ein Gutachten wieder als nicht gedruckt.</summary>
    public static void Entfernen(string klausurOrdner, string kennung)
    {
        var p = Laden(klausurOrdner);
        if (!p.Remove(KlausurAblage.SicherName(kennung))) return;
        try { Speichern(klausurOrdner, p); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { EinstellungsSpeicher.Protokollieren(ex); }
    }

    /// <summary>Kurztext für die Übersicht („gedruckt 09.11. 13:05“ bzw. „Druckfehler“); leer = nicht gedruckt.</summary>
    public static string Text(Dictionary<string, Druckeintrag> protokoll, string kennung) =>
        protokoll.TryGetValue(KlausurAblage.SicherName(kennung), out Druckeintrag? e)
            ? e.Ok ? $"gedruckt {e.Stand:dd.MM. HH:mm}" : "⚠ Druckfehler"
            : string.Empty;

    /// <summary>Ordnet einen Dokumentnamen der Warteschlange („Microsoft Word - 1017.docx“) einem Dateistamm zu.</summary>
    public static string? Zuordnen(string dokument, IEnumerable<string> staemme) =>
        staemme.Where(s => dokument.Contains(s + ".docx", StringComparison.OrdinalIgnoreCase)
                           || dokument.EndsWith(" - " + s, StringComparison.OrdinalIgnoreCase)
                           || dokument.Equals(s, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Length)
            .FirstOrDefault();
}
