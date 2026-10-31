# Installationsprogramm Gutachtentool

**Erzeugen** (einmalig Inno Setup 6 installieren: `winget install JRSoftware.InnoSetup`):

```powershell
cd Installer
.\Erstellen.ps1            # Version aus <Version> in Gutachten2025.csproj
```

Ergebnis: `Ausgabe\Gutachtentool-Setup-<Version>.exe` (eine Datei, enthält .NET und Windows App SDK).
Neue Version: `<Version>` in der csproj erhöhen, Skript erneut ausführen. Das Setup aktualisiert
eine vorhandene Installation; Einstellungen, Vorlagen und Klausuren bleiben erhalten.

**Was das Setup tut:** Installation nach `%LOCALAPPDATA%\Programs\Gutachtentool`, Startmenü
(optional Desktop), Eintrag unter *Einstellungen › Apps* zum Deinstallieren – alles ohne
Administratorrechte. Wer Adminrechte hat, kann im ersten Dialog „für alle Benutzer“ wählen.
Außerdem wird die Dateiendung **.gtpaket** (Gutachtentool-Paket) registriert: Ein Doppelklick
öffnet das Paket im laufenden bzw. neu gestarteten Gutachtentool (nur eine Instanz).

## Paketformat (.gtpaket)

ZIP-Archiv mit festen Dateinamen auf oberster Ebene (andere Einträge werden ignoriert):

| Datei | Inhalt |
|---|---|
| `manifest.json` | Pflicht: `Formatversion` (1), `Titel`, optional `Beschreibung`, `Rechtsgebiet`, `Autor`, `Quelle`, `Erstellt`, `Pruefsummen` (SHA-256 hex je Datei) |
| `Texte.txt` | Formulierungen (Tabulator-Einrückung = Unterebene, `+` = Gruppe, `[#kürzel 10-18]` = Kürzel und Punktebereich) |
| `MUSTER.docx` | Word-Vorlage (ohne Makros) |
| `Klausur.json` | `TeilpunkteAusgeben`, `Aufgaben` (`Name`, `Hoechstpunkte`), `Notenschluessel` (18 Werte oder null), `Formalkriterien` (`Name`, `Vorlage`, `Stufen`) |
| `rohpunkte.json` | optional: Rohpunkteschema wie im KI-Portal (`titel`, `aufgaben` {Nr: `t`, `m`}, `positionen` {Kennung: `t`, `m`, `h`, `a`, `v`, `k`, `b`, `w`, `x`}, `umrechnung` [[ab, Notenpunkte], …]); maßgeblich, wenn vorhanden |
| `rohpunkte.xlsx` | optional: dasselbe Schema zur Bearbeitung in Excel (Blätter „Bewertungsbogen“ und „Umrechnung“, Layout des KI-Portals; ohne Makros) |
| `Erwartungshorizont.md/.txt/.docx/.pdf` | optional |

Die Vorlage `###BEWERTUNGSBOGEN###` in `MUSTER.docx` legt fest, wo der Anhang „Bewertungsbogen“ erscheint
(sonst am Ende); `###ROHPUNKTE###` liefert die Gesamtpunktzahl.

Grenzen: höchstens 25 MB je Datei, 60 MB insgesamt. Noten, Gutachten, Entwürfe und
Teilnehmendenlisten werden nie exportiert.

## Hinweis für Nutzer (z. B. auf der Download-Seite)

Das Installationsprogramm ist nicht signiert. Windows zeigt deshalb beim ersten Start
„Der Computer wurde durch Windows geschützt“. Weiter mit **Weitere Informationen ›
Trotzdem ausführen**. Ist unter Windows 11 die *intelligente App-Steuerung* (Smart App Control)
eingeschaltet, blockiert Windows unsignierte Programme vollständig; dann ist die Installation
nur nach Abschalten dieser Funktion möglich. Auf verwalteten Dienstrechnern kann die IT
Programme in Benutzerordnern sperren (AppLocker) – dann bitte die IT ansprechen.

Zur Kontrolle der Datei die SHA-256-Prüfsumme mitveröffentlichen:
`Get-FileHash .\Ausgabe\Gutachtentool-Setup-*.exe`
