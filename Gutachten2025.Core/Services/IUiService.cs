using Gutachten2025.Models;
using System;
using System.Threading.Tasks;

namespace Gutachten2025.Services;

public enum Meldungsart { Information, Erfolg, Warnung, Fehler }

public enum Eingabefeld { Klausur, Student, Matrikel, Note, Editor, Suche, Seiten, Kriterien }

/// <summary>Dialoge, Meldungen und Shell-Aufrufe; implementiert von der View.</summary>
public interface IUiService
{
    /// <summary>Zeigt eine Meldung; optional mit Schaltfläche (z. B. „Rückgängig“).</summary>
    void Melden(Meldungsart art, string titel, string text, string? aktion = null, Action? beiAktion = null);

    Task<bool> BestaetigenAsync(string titel, string text, string ja, string nein);

    /// <summary>Drei Möglichkeiten: 1 = erste, 2 = zweite, 0 = Abbrechen.</summary>
    Task<int> AuswahlAsync(string titel, string text, string erste, string zweite, string abbrechen);

    Task<string?> TextEingebenAsync(string titel, string vorgabe, string kopf = "Textbaustein");

    /// <summary>Dialog für Text, Kürzel und Punktebereich; <paramref name="pruefen"/> liefert eine Fehlermeldung oder null.</summary>
    Task<BausteinDaten?> BausteinBearbeitenAsync(string titel, BausteinDaten vorgabe, Func<BausteinDaten, string?> pruefen);

    Task<Klausurkonfiguration?> KonfigurationBearbeitenAsync(string klausur, Klausurkonfiguration vorgabe);

    Task<string?> DateiWaehlenAsync(params string[] endungen);

    Task<string?> SpeicherortWaehlenAsync(string vorschlag, string endung, string bezeichnung);

    Task<string?> OrdnerWaehlenAsync();

    void Oeffnen(string pfad);

    /// <summary>Druckt eine Datei über das zugeordnete Programm (Word) auf dem Standarddrucker; false bei Fehler.</summary>
    bool Drucken(string pfad);

    /// <summary>Zustand der Warteschlange des Standarddruckers; null = nicht abfragbar (dann ohne Überwachung drucken).</summary>
    Task<Druckstand?> DruckerAbfragenAsync();

    /// <summary>Startet die Scan-App des Systems (Windows Scan bzw. Windows-Fax und -Scan); false = nicht verfügbar.</summary>
    bool ScannenStarten();

    void Fokussieren(Eingabefeld feld);

    /// <summary>Wechselt zum Reiter „Rohpunkte“.</summary>
    void RohpunkteZeigen();

    /// <summary>Wandelt ein Gutachten in PDF um (Windows: Word; Mac: LibreOffice, falls vorhanden); false = nicht möglich.</summary>
    Task<bool> AlsPdfSpeichernAsync(string docx, string pdf);
}

/// <summary>Zugriff auf den Gutachtenentwurf im RichEditBox; implementiert von der View.</summary>
public interface IEntwurfEditor
{
    string TextLesen();

    /// <summary>Text mit Fett/Kursiv je Zeichen (für die Word-Ausgabe).</summary>
    FormatierterText FormatLesen();
    string RtfLesen();
    void RtfSetzen(string rtf);
    void TextSetzen(string text);
    void Einfuegen(string baustein);
    void Leeren();
}
