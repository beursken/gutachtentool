using System.IO.Compression;
using Gutachten2025.Services;
using Gutachtentool.Oberflaeche.Dienste;
using Microsoft.JSInterop;

namespace Gutachtentool.Web.Dienste;

/// <summary>
/// Dauerhafte Ablage der Browser-Fassung. Der Kern arbeitet unverändert mit Dateien im virtuellen
/// Dateisystem des Browsers (unter <see cref="Wurzel"/>); dieser Dienst spiegelt es in die IndexedDB
/// des Browsers: beim Start vollständig laden, danach geänderte und gelöschte Dateien nachtragen.
/// Es werden keine Daten an einen Server übertragen. „Öffnen“ und „Speichern unter“ werden zu Downloads.
/// </summary>
public sealed class WebPlattform : IPlattform, IBrowserSicherung
{
    public const string Wurzel = "/gt";
    public const string Datenordner = Wurzel + "/daten";
    public const string Klausurordner = Wurzel + "/Klausuren";

    /// <summary>Übergabeordner für Dateien, die heruntergeladen werden (nicht gesichert).</summary>
    public const string Ausgang = "/tmp/gt-ausgang";

    readonly IJSRuntime _js;
    readonly Dictionary<string, (long Laenge, DateTime Stand)> _bekannt = new(StringComparer.Ordinal);
    readonly SemaphoreSlim _sperre = new(1, 1);
    readonly List<string> _ausstehend = new();

    public WebPlattform(IJSRuntime js) => _js = js;

    public bool ImBrowser => true;

    /// <summary>Zeitpunkt der letzten erfolgreichen Sicherung.</summary>
    public DateTime? Gesichert { get; private set; }

    public string? Stand => Gesichert is { } g ? $"Im Browser gespeichert {g:HH:mm:ss}" : null;

    /// <summary>Letzter Fehler beim Sichern (z. B. Speicher voll); null = keiner.</summary>
    public string? Fehler { get; private set; }

    /// <summary>Lädt alle gesicherten Dateien in das virtuelle Dateisystem.</summary>
    public async Task StartAsync()
    {
        Directory.CreateDirectory(Wurzel);
        Directory.CreateDirectory(UiDienst.Eingang);
        Directory.CreateDirectory(Ausgang);
        string[] pfade = await _js.InvokeAsync<string[]>("gutachtentool.speicher.pfade");
        foreach (string pfad in pfade)
        {
            if (!pfad.StartsWith(Wurzel + "/", StringComparison.Ordinal)) continue;
            byte[]? daten = await _js.InvokeAsync<byte[]?>("gutachtentool.speicher.lesen", pfad);
            if (daten is null) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.WriteAllBytes(pfad, daten);
            var info = new FileInfo(pfad);
            _bekannt[pfad] = (info.Length, info.LastWriteTimeUtc);
        }
        // Browser bitten, die Daten nicht bei Speicherknappheit zu verwerfen
        try { await _js.InvokeAsync<bool>("gutachtentool.speicher.dauerhaft"); }
        catch (JSException) { }
    }

    /// <summary>Überträgt Änderungen seit der letzten Sicherung; liefert die Zahl der Vorgänge.</summary>
    public async Task<int> SichernAsync()
    {
        await _sperre.WaitAsync();
        try
        {
            var jetzt = new Dictionary<string, (long, DateTime)>(StringComparer.Ordinal);
            if (Directory.Exists(Wurzel))
            {
                foreach (string pfad in Directory.EnumerateFiles(Wurzel, "*", SearchOption.AllDirectories))
                {
                    if (pfad.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                    var info = new FileInfo(pfad);
                    jetzt[pfad] = (info.Length, info.LastWriteTimeUtc);
                }
            }
            int vorgaenge = 0;
            foreach (var (pfad, stand) in jetzt)
            {
                if (_bekannt.TryGetValue(pfad, out var alt) && alt == stand) continue;
                await _js.InvokeVoidAsync("gutachtentool.speicher.schreiben", pfad, File.ReadAllBytes(pfad));
                _bekannt[pfad] = stand;
                vorgaenge++;
            }
            foreach (string pfad in _bekannt.Keys.Where(p => !jetzt.ContainsKey(p)).ToList())
            {
                await _js.InvokeVoidAsync("gutachtentool.speicher.loeschen", pfad);
                _bekannt.Remove(pfad);
                vorgaenge++;
            }
            Gesichert = DateTime.Now;
            Fehler = null;
            return vorgaenge;
        }
        catch (Exception ex) when (ex is JSException or IOException)
        {
            Fehler = ex.Message;
            return 0;
        }
        finally
        {
            _sperre.Release();
        }
    }

    public async Task NachlaufAsync()
    {
        List<string> fertig;
        lock (_ausstehend)
        {
            fertig = _ausstehend.Where(File.Exists).ToList();
            _ausstehend.RemoveAll(fertig.Contains);
        }
        foreach (string pfad in fertig)
        {
            await HerunterladenAsync(Path.GetFileName(pfad), File.ReadAllBytes(pfad));
            File.Delete(pfad);
        }
        await SichernAsync();
    }

    /// <summary>Im Browser wählt die Oberfläche Dateien selbst (Hochladedialog).</summary>
    public Task<string?> DateiWaehlenAsync(IReadOnlyList<string> endungen) => Task.FromResult<string?>(null);

    /// <summary>Übergabedatei; nach dem Schreiben wird sie heruntergeladen (<see cref="NachlaufAsync"/>).</summary>
    public Task<string?> SpeicherortWaehlenAsync(string vorschlag, string endung, string bezeichnung)
    {
        Directory.CreateDirectory(Ausgang);
        string pfad = Path.Combine(Ausgang, KlausurAblage.SicherName(vorschlag) + endung);
        if (File.Exists(pfad)) File.Delete(pfad);
        lock (_ausstehend) _ausstehend.Add(pfad);
        return Task.FromResult<string?>(pfad);
    }

    public Task<string?> OrdnerWaehlenAsync() => Task.FromResult<string?>(null);

    public async Task OeffnenAsync(string pfad)
    {
        if (pfad.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            await _js.InvokeVoidAsync("open", pfad, "_blank", "noopener");
            return;
        }
        if (Directory.Exists(pfad))
            throw new InvalidOperationException(
                "Im Browser gibt es keine Ordner. Einzelne Dateien stehen im Menü „Klausur“ bereit, alle Daten unter „Daten › Sicherung herunterladen“.");
        if (!File.Exists(pfad)) throw new FileNotFoundException("Datei nicht gefunden: " + Path.GetFileName(pfad));
        await HerunterladenAsync(Path.GetFileName(pfad), File.ReadAllBytes(pfad));
    }

    public Task AusgebenAsync(string name, byte[] daten) => HerunterladenAsync(name, daten);

    /// <summary>Im Browser gibt es keine von außen geöffneten Dateien.</summary>
    public void DateienAbonnieren(Func<string, Task> oeffnen) { }

    async Task HerunterladenAsync(string name, byte[] daten) =>
        await _js.InvokeVoidAsync("gutachtentool.herunterladen", name, Dateityp(name), daten);

    static string Dateityp(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".dotx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.template",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pdf" => "application/pdf",
        ".txt" or ".md" => "text/plain;charset=utf-8",
        ".json" => "application/json",
        ".zip" => "application/zip",
        _ => "application/octet-stream",
    };

    public Task NeuLadenAsync() => _js.InvokeVoidAsync("gutachtentool.neuLaden").AsTask();

    /// <summary>Alle Daten (Klausuren, Einstellungen, Entwurf) als ZIP.</summary>
    public byte[] SicherungErstellen()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string pfad in Directory.EnumerateFiles(Wurzel, "*", SearchOption.AllDirectories))
            {
                if (pfad.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetRelativePath(Wurzel, pfad).Replace('\\', '/');
                ZipArchiveEntry eintrag = zip.CreateEntry(name, CompressionLevel.Optimal);
                eintrag.LastWriteTime = File.GetLastWriteTime(pfad);
                using Stream ziel = eintrag.Open();
                using FileStream quelle = File.OpenRead(pfad);
                quelle.CopyTo(ziel);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Ersetzt alle Daten durch den Inhalt einer Sicherung (ZIP aus <see cref="SicherungErstellen"/>).</summary>
    public async Task SicherungEinspielenAsync(string zipPfad)
    {
        using (ZipArchive zip = ZipFile.OpenRead(zipPfad))
        {
            if (!zip.Entries.Any(e => e.FullName.StartsWith("Klausuren/", StringComparison.Ordinal) ||
                                      e.FullName.StartsWith("daten/", StringComparison.Ordinal)))
                throw new InvalidDataException("Die Datei ist keine Datensicherung des Gutachtentools.");
            if (Directory.Exists(Wurzel)) Directory.Delete(Wurzel, recursive: true);
            Directory.CreateDirectory(Wurzel);
            string basis = Path.GetFullPath(Wurzel) + "/";
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                if (e.FullName.EndsWith('/')) continue;
                string ziel = Path.GetFullPath(Path.Combine(Wurzel, e.FullName));
                if (!ziel.StartsWith(basis, StringComparison.Ordinal)) continue; // keine Pfade außerhalb
                Directory.CreateDirectory(Path.GetDirectoryName(ziel)!);
                e.ExtractToFile(ziel, overwrite: true);
            }
        }
        await SichernAsync();
    }

    /// <summary>Belegter und verfügbarer Browserspeicher in Bytes; null, wenn der Browser es nicht meldet.</summary>
    public async Task<(double Belegt, double Verfuegbar)?> BelegungAsync()
    {
        try
        {
            double[]? b = await _js.InvokeAsync<double[]?>("gutachtentool.speicher.belegung");
            return b is { Length: 2 } ? (b[0], b[1]) : null;
        }
        catch (JSException)
        {
            return null;
        }
    }
}
