# Code signing policy / Richtlinie zur Codesignatur

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

Kostenlose Codesignatur durch [SignPath.io](https://about.signpath.io/), Zertifikat der [SignPath Foundation](https://signpath.org/).

## Signierte Dateien

Signiert werden ausschließlich Dateien, die aus dem Quellcode dieses Repositorys in GitHub Actions erstellt werden
(Ablauf `.github/workflows/release.yml`, ausgelöst durch einen Versions-Tag `v*`):

- `Gutachten2025.exe`, `Gutachten2025.dll`, `Gutachten2025.Core.dll` (Windows-Fassung)
- `Gutachtentool-Setup-<Version>.exe` (Installationsprogramm)

Bibliotheken Dritter (.NET, Windows App SDK, Open XML SDK, CommunityToolkit) werden nicht von diesem Projekt signiert;
sie tragen, soweit vorhanden, die Signatur ihres Herausgebers. Die Mac-Fassung wird getrennt mit einer Apple Developer ID
signiert und von Apple notarisiert.

## Team roles / Rollen

| Rolle | Person |
|---|---|
| Committers and reviewers / Autoren und Prüfer | [Michael Beurskens](https://github.com/beursken) |
| Approvers / Freigabe jeder Signatur | [Michael Beurskens](https://github.com/beursken) |

Änderungen von Personen außerhalb des Teams werden vor der Übernahme geprüft. Jede Signatur wird in SignPath
einzeln freigegeben. Alle Teammitglieder nutzen Zwei-Faktor-Anmeldung für GitHub und SignPath.

## Privacy policy / Datenschutz

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it.

Das Programm überträgt keine Daten an andere Systeme, sofern Nutzerinnen und Nutzer dies nicht ausdrücklich veranlassen.
Klausuren, Gutachten, Noten und Einstellungen bleiben auf dem Rechner (Windows, Mac) bzw. im Browserspeicher
(Browser-Fassung). Das Programm sammelt keine Nutzungs- oder Fehlerdaten.

## Systemänderungen und Deinstallation

Das Installationsprogramm installiert für das angemeldete Konto ohne Administratorrechte, legt Verknüpfungen an und
ordnet die Dateiendung `.gtpaket` zu. Entfernen über *Einstellungen › Apps › Gutachtentool*; Klausuren und
Einstellungen bleiben dabei erhalten.

## Einrichtung (für Betreuer)

1. Bei der SignPath Foundation bewerben (https://signpath.org/apply) – Repository öffentlich, Lizenz AGPL-3.0.
2. In SignPath: Projekt `gutachtentool`, Signierrichtlinie `release-signing` (mit Freigabe), Trusted Build System
   „GitHub.com“ mit diesem Repository verknüpfen, Artefaktkonfigurationen `programmdateien` und `setup` aus
   `Werkzeuge/signpath/*.xml` anlegen.
3. GitHub › Settings › Secrets and variables › Actions: Secret `SIGNPATH_API_TOKEN` (CI-Benutzer in SignPath),
   Variable `SIGNPATH_ORGANIZATION_ID`.
4. Veröffentlichen: `<Version>` in den csproj-Dateien und `product-version` in `Werkzeuge/signpath/programmdateien.xml`
   anheben, Tag setzen (`git tag v1.3.1` und `git push --tags`), Signatur in SignPath freigeben.
