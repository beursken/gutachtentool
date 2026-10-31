# Gutachtentool – Browser-Fassung

Blazor WebAssembly. Das Programm läuft vollständig im Browser; Klausuren, Gutachten, Entwürfe und
Einstellungen liegen in der IndexedDB des Browsers auf dem jeweiligen Rechner. Es werden **keine Daten
an den Server übertragen** – der Server liefert nur die Programmdateien aus.

## Erstellen

- GitHub Actions: Artefakt **Gutachtentool-Web** (Inhalt = Webordner).
- Lokal: `bash Werkzeuge/web-erstellen.sh` bzw. `dotnet publish Gutachtentool.Web -c Release -o publish/web`
  → `publish/web/wwwroot`.
- Ausprobieren: `dotnet run --project Gutachtentool.Web` → Adresse aus der Ausgabe öffnen.

## Bereitstellen (z. B. https://vote.jura.uni-passau.de/gutachtentool/)

1. Inhalt des Artefakts in einen Unterordner des Webservers kopieren, z. B. WebVote `wwwroot/gutachtentool/`,
   danach WebVote neu veröffentlichen (MapStaticAssets kennt nur Dateien, die beim Build vorhanden waren).
2. Aufruf **mit abschließendem Schrägstrich** (`…/gutachtentool/`); `<base href="./">` macht jeden Unterordner möglich.
3. Der Server muss `.wasm`, `.dat`, `.js`, `.json` und `.webmanifest` ausliefern. Fehlt `.dat` (Fehler 404 beim Start),
   in WebVote ergänzen:
   `new FileExtensionContentTypeProvider { Mappings = { [".dat"] = "application/octet-stream", [".webmanifest"] = "application/manifest+json" } }`.
   Unter IIS übernimmt das die mitveröffentlichte `web.config`.
4. Nur über HTTPS: Service Worker (Offline-Betrieb, Installation als App) und dauerhafter Speicher setzen HTTPS voraus.

## Bedienung – Unterschiede zur Windows-Fassung

| Windows | Browser |
|---|---|
| Ablageordner auf der Festplatte | Browserspeicher; **Daten › Sicherung herunterladen** (ZIP) und **Sicherung einspielen** |
| Word öffnet das Gutachten | Gutachten (.docx) wird heruntergeladen |
| Notenliste, Vorlage, Texte.txt im Ordner bearbeiten | Menü **Klausur**: herunterladen, bearbeiten, wieder hochladen |
| Drucken über Word | entfällt (heruntergeladene Datei drucken) |
| Bausteine per Ziehen umsortieren | entfällt (Texte.txt bearbeiten und hochladen) |
| Anmerkungen im Reiter Rohpunkte mit Fett/Kursiv | reiner Text |

Pakete (.gtpaket) aus dem KI-Portal und von der Windows-Fassung lassen sich öffnen und erstellen; Entwürfe sind
zwischen den Fassungen austauschbar (RTF).

**Wichtig:** Wer die Browserdaten löscht (oder im privaten Fenster arbeitet), verliert alle Klausuren. Regelmäßig sichern.
