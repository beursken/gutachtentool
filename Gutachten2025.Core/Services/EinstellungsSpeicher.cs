using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Gutachten2025.Services;

/// <summary>
/// Speichert Einstellungen (JSON) und Entwurf (RTF) im Datenordner des Programms
/// (Windows %LOCALAPPDATA%\Gutachtentool, macOS ~/Library/Application Support/Gutachtentool).
/// Ersetzt ApplicationData.LocalSettings (8-KB-Grenze je Wert, nur in gepackten Apps verfügbar).
/// </summary>
public sealed class EinstellungsSpeicher
{
    /// <summary>Vor dem ersten Zugriff änderbar (Browser-Fassung: virtuelles Dateisystem).</summary>
    public static string Datenordner { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gutachtentool");

    /// <summary>Altbestand aus früheren Programmfassungen (Windows: ApplicationData.LocalSettings); null = keiner.</summary>
    public static Func<IDictionary<string, object>?>? Altbestand { get; set; }

    static string EinstellungsDatei => Path.Combine(Datenordner, "einstellungen.json");
    static string EntwurfDatei => Path.Combine(Datenordner, "entwurf.rtf");
    static string Protokolldatei => Path.Combine(Datenordner, "fehler.log");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static readonly object ProtokollSperre = new();

    public EinstellungsSpeicher() => Directory.CreateDirectory(Datenordner);

    /// <summary>Lädt die Einstellungen; beim ersten Start werden alte LocalSettings übernommen.</summary>
    public Einstellungen Laden(out string? alterEntwurf)
    {
        alterEntwurf = null;
        if (File.Exists(EinstellungsDatei))
        {
            try
            {
                return JsonSerializer.Deserialize<Einstellungen>(File.ReadAllText(EinstellungsDatei), Json) ?? new();
            }
            catch (Exception ex)
            {
                Protokollieren(ex);
                return new();
            }
        }
        return AusLocalSettingsUebernehmen(out alterEntwurf);
    }

    /// <summary>Liest nur die gewählte Darstellung (für App.RequestedTheme vor dem Anlegen des Fensters).</summary>
    public static int DarstellungLesen()
    {
        try
        {
            if (!File.Exists(EinstellungsDatei)) return 0;
            using JsonDocument d = JsonDocument.Parse(File.ReadAllText(EinstellungsDatei));
            return d.RootElement.TryGetProperty("Darstellung", out JsonElement w) && w.TryGetInt32(out int i) ? i : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public void Speichern(Einstellungen e) =>
        AtomarSchreiben(EinstellungsDatei, JsonSerializer.Serialize(e, Json));

    public string? EntwurfLaden() => File.Exists(EntwurfDatei) ? File.ReadAllText(EntwurfDatei, Encoding.UTF8) : null;

    public void EntwurfSpeichern(string rtf) => AtomarSchreiben(EntwurfDatei, rtf);

    public void EntwurfLoeschen()
    {
        if (File.Exists(EntwurfDatei)) File.Delete(EntwurfDatei);
    }

    public static void Protokollieren(Exception ex)
    {
        try
        {
            lock (ProtokollSperre)
            {
                Directory.CreateDirectory(Datenordner);
                File.AppendAllText(Protokolldatei, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Protokollierung darf nie selbst einen Fehler auslösen.
        }
    }

    static void AtomarSchreiben(string pfad, string inhalt)
    {
        string tmp = pfad + ".tmp";
        File.WriteAllText(tmp, inhalt, new UTF8Encoding(false));
        File.Move(tmp, pfad, overwrite: true);
    }

    static Einstellungen AusLocalSettingsUebernehmen(out string? alterEntwurf)
    {
        alterEntwurf = null;
        var e = new Einstellungen();
        try
        {
            IDictionary<string, object>? v = Altbestand?.Invoke();
            if (v is null) return e;
            object? Wert(params string[] schluessel)
            {
                foreach (string s in schluessel)
                    if (v.TryGetValue(s, out object? o) && o is not null) return o;
                return null;
            }
            e.Klausur = (Wert("Klausur") as string)?.Trim() ?? string.Empty;
            e.Name = (Wert("Name") as string)?.Trim() ?? string.Empty;
            e.Matrikel = (Wert("Matrikel") as string)?.Trim() ?? string.Empty;
            if (Wert("Note") is { } n) e.Note = Convert.ToInt32(n);
            if (Wert("Anonym") is bool a) e.Anonym = a;
            if (Wert("Seiten", "seiten") is { } s) e.Seiten = Convert.ToInt32(s);
            if (Wert("Struktur") is { } g) e.Gliederung = Convert.ToInt32(g);
            if (Wert("Schrift") is { } f) e.Schrift = Convert.ToInt32(f);
            alterEntwurf = Wert("Text") as string;
        }
        catch
        {
            // Ungepackte App oder keine Altdaten: mit Standardwerten starten.
        }
        return e;
    }
}
