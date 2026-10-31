# Gutachtentool

Klausurgutachten mit Textbausteinen, Rohpunkteschema, Word-Gutachten aus Vorlage und Notenliste in Excel –
für Windows, macOS und im Browser. Entwickelt an der Universität Passau (Prof. Dr. Michael Beurskens).

- **Windows** (WinUI 3): Projekt `Gutachten2025`, Installation ohne Administratorrechte.
- **macOS** (Photino/WebKit): Projekt `Gutachtentool.Mac`, signiert und notarisiert.
- **Browser** (Blazor WebAssembly): Projekt `Gutachtentool.Web`; alle Daten bleiben im Browser.
- Gemeinsamer Kern ohne Oberfläche: `Gutachten2025.Core`; gemeinsame Oberfläche von Mac und Browser: `Gutachtentool.Oberflaeche`.

Pakete (`.gtpaket`) mit Vorlage, Textbausteinen, Formalkriterien, Rohpunkteschema und Erwartungshorizont lassen sich
im KI-Portal erzeugen und zwischen allen Fassungen austauschen.

## Download

https://vote.jura.uni-passau.de/download

## Erstellen

- GitHub Actions: `.github/workflows/build.yml` (alle Fassungen bei jedem Push), `release.yml` (signierte Windows-Fassung bei Tag `v*`).
- Lokal (Windows): `Installer\Erstellen.ps1`; Browser: `bash Werkzeuge/web-erstellen.sh`; Mac: `bash Werkzeuge/mac-erstellen.sh` (nur auf macOS).
- Verteilung auf die Download-Seite: `Werkzeuge\Verteilen.ps1`.

## Codesignatur

Siehe [CODE_SIGNING.md](CODE_SIGNING.md).

## Lizenz

Copyright © 2025–2026 Michael Beurskens.

Dieses Programm ist freie Software: Sie können es unter den Bedingungen der GNU Affero General Public License,
Version 3, weitergeben und/oder ändern ([LICENSE](LICENSE)). Es wird ohne jede Gewährleistung bereitgestellt.
Bibliotheken Dritter: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
