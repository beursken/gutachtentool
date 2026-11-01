namespace Gutachtentool.Oberflaeche.Dienste;

/// <summary>
/// Unterschiede zwischen Browser (WebAssembly, Daten in IndexedDB) und Mac (echtes Dateisystem).
/// Alle Aufrufe kommen aus der Oberfläche; Dateipfade sind Pfade im (virtuellen) Dateisystem des Kerns.
/// </summary>
public interface IPlattform
{
    /// <summary>true = Browser: Dateien werden hoch- und heruntergeladen, Daten liegen im Browserspeicher.</summary>
    bool ImBrowser { get; }

    /// <summary>Vor dem Anlegen des ViewModels (Browser: gespeicherte Dateien laden).</summary>
    Task StartAsync();

    /// <summary>Nach jeder Aktion und regelmäßig (Browser: Änderungen sichern, Downloads ausliefern).</summary>
    Task NachlaufAsync();

    /// <summary>Kurzer Stand für die Fußzeile (z. B. „Im Browser gespeichert 10:31:02“); null = keiner.</summary>
    string? Stand { get; }

    /// <summary>Fehlerhinweis für die Fußzeile; null = keiner.</summary>
    string? Fehler { get; }

    /// <summary>Datei wählen; null = abgebrochen. Im Browser zeigt die Oberfläche stattdessen den Hochladedialog.</summary>
    Task<string?> DateiWaehlenAsync(IReadOnlyList<string> endungen);

    Task<string?> SpeicherortWaehlenAsync(string vorschlag, string endung, string bezeichnung);

    Task<string?> OrdnerWaehlenAsync();

    /// <summary>Datei oder Ordner öffnen (Mac: mit dem zugeordneten Programm; Browser: herunterladen).</summary>
    Task OeffnenAsync(string pfad);

    /// <summary>Beliebige Daten als Datei ausgeben (Browser: herunterladen; Mac: Speichern-Dialog).</summary>
    Task AusgebenAsync(string name, byte[] daten);

    /// <summary>
    /// Von außen geöffnete Pakete (Mac: Doppelklick im Finder) an <paramref name="oeffnen"/> übergeben;
    /// vor dem Aufruf eingegangene Dateien werden nachgereicht. Browser: keine.
    /// </summary>
    void DateienAbonnieren(Func<string, Task> oeffnen);

    /// <summary>Word-Datei als PDF speichern (Mac: mit LibreOffice, falls installiert); false = nicht möglich.</summary>
    Task<bool> AlsPdfSpeichernAsync(string docx, string pdf);
}
