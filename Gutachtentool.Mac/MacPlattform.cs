using System.Diagnostics;
using System.Text;
using Gutachten2025.Services;
using Gutachtentool.Oberflaeche.Dienste;

namespace Gutachtentool.Mac;

/// <summary>
/// Mac: echte Dateien; Dateidialoge über AppleScript (osascript), Öffnen über „open“
/// (Word, Excel, Finder). Keine Sicherung nötig – der Kern schreibt direkt in den Ablageordner.
/// </summary>
public sealed class MacPlattform : IPlattform
{
    public bool ImBrowser => false;

    /// <summary>Die Fußzeile zeigt auf dem Mac den Ablageordner (aus dem ViewModel).</summary>
    public string? Stand => null;

    public string? Fehler => null;

    public Task StartAsync() => Task.CompletedTask;

    public Task NachlaufAsync() => Task.CompletedTask;

    /// <summary>Doppelklick auf .gtpaket im Finder (MacDokumente); richtet die Übergabe erneut ein, falls Photino den Delegate erst spät setzt.</summary>
    public void DateienAbonnieren(Func<string, Task> oeffnen)
    {
        MacDokumente.Einrichten();
        MacDokumente.Abonnieren(oeffnen);
    }

    public async Task<string?> DateiWaehlenAsync(IReadOnlyList<string> endungen)
    {
        string typen = endungen.Count == 0
            ? string.Empty
            : " of type {" + string.Join(", ", endungen.Select(e => Text(e.TrimStart('.')))) + "}";
        return await SkriptAsync($"POSIX path of (choose file with prompt {Text("Datei wählen")}{typen})");
    }

    public async Task<string?> SpeicherortWaehlenAsync(string vorschlag, string endung, string bezeichnung)
    {
        string? pfad = await SkriptAsync(
            $"POSIX path of (choose file name with prompt {Text(bezeichnung + " speichern")} default name {Text(KlausurAblage.SicherName(vorschlag) + endung)})");
        if (pfad is null) return null;
        return pfad.EndsWith(endung, StringComparison.OrdinalIgnoreCase) ? pfad : pfad + endung;
    }

    public async Task<string?> OrdnerWaehlenAsync()
    {
        string? pfad = await SkriptAsync($"POSIX path of (choose folder with prompt {Text("Ablageordner wählen")})");
        return pfad?.TrimEnd('/');
    }

    public async Task OeffnenAsync(string pfad)
    {
        if (IstVerweis(pfad))
        {
            await AusfuehrenAsync("/usr/bin/open", pfad); // Standardbrowser
            return;
        }
        if (!File.Exists(pfad) && !Directory.Exists(pfad))
            throw new FileNotFoundException("Nicht gefunden: " + pfad);
        await AusfuehrenAsync("/usr/bin/open", pfad);
    }

    public async Task AusgebenAsync(string name, byte[] daten)
    {
        string endung = Path.GetExtension(name);
        string? ziel = await SpeicherortWaehlenAsync(Path.GetFileNameWithoutExtension(name), endung, "Datei");
        if (ziel is not null) await File.WriteAllBytesAsync(ziel, daten);
    }

    // ---------- Hilfen ----------

    /// <summary>AppleScript-Zeichenkette mit maskierten Anführungszeichen.</summary>
    static string Text(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>Führt AppleScript aus; Ergebnis ohne Zeilenende, null bei Abbruch durch den Nutzer.</summary>
    static async Task<string?> SkriptAsync(string skript)
    {
        (int code, string aus) = await AusfuehrenAsync("/usr/bin/osascript", "-e", skript);
        if (code != 0) return null;
        string ergebnis = aus.TrimEnd('\r', '\n');
        return ergebnis.Length > 0 ? ergebnis : null;
    }

    static async Task<(int Code, string Ausgabe)> AusfuehrenAsync(string programm, params string[] argumente)
    {
        var start = new ProcessStartInfo(programm)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (string a in argumente) start.ArgumentList.Add(a);
        using Process p = Process.Start(start) ?? throw new InvalidOperationException(programm + " nicht gestartet.");
        string aus = await p.StandardOutput.ReadToEndAsync();
        await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, aus);
    }

    const string LibreOffice = "/Applications/LibreOffice.app/Contents/MacOS/soffice";

    /// <summary>PDF mit LibreOffice (falls installiert); Word für Mac bietet keine zuverlässige Umwandlung ohne Rückfrage.</summary>
    public async Task<bool> AlsPdfSpeichernAsync(string docx, string pdf)
    {
        if (!File.Exists(LibreOffice)) return false;
        string ordner = Path.GetDirectoryName(pdf)!;
        (int code, _) = await AusfuehrenAsync(LibreOffice, "--headless", "--convert-to", "pdf", "--outdir", ordner, docx);
        string erzeugt = Path.Combine(ordner, Path.GetFileNameWithoutExtension(docx) + ".pdf");
        if (code != 0 || !File.Exists(erzeugt)) return false;
        if (!string.Equals(erzeugt, pdf, StringComparison.Ordinal)) File.Move(erzeugt, pdf, overwrite: true);
        return true;
    }

    static bool IstVerweis(string pfad) =>
        pfad.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || pfad.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || pfad.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
}
