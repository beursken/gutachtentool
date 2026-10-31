# Gutachtentool – Mac-Fassung

Natives Programmfenster (Photino, WebKit) mit derselben Oberfläche wie die Browser-Fassung
(`Gutachtentool.Oberflaeche`) und demselben Kern wie die Windows-Fassung (`Gutachten2025.Core`).

- Ablage: echte Dateien, Standard `~/Documents/Klausuren` (änderbar über „Ablageordner …“, z. B. ein OneDrive-Ordner,
  den auch die Windows-Fassung nutzt). Einstellungen und Entwurf: `~/Library/Application Support/Gutachtentool`.
- Word, Excel und Finder öffnen Gutachten, Notenliste, Vorlagen und Ordner („open“).
- Rechtschreibprüfung und Diktat von macOS im Gutachtentext und in den Anmerkungen; Fett/Kursiv mit ⌘B/⌘I bzw. Strg+B/I.
- `.gtpaket` per Doppelklick im Finder öffnen (Dateityp `de.uni-passau.jura.gutachtentool.paket`, `MacDokumente.cs`)
  oder über „Klausur › Paket öffnen …“. Noch nicht: Drucken über Word.
- Mindestens macOS 12; getrennte Fassungen für Apple Silicon und Intel.

## Erstellen

GitHub Actions (Job „macOS“) führt `Werkzeuge/mac-erstellen.sh` aus und liefert das Artefakt **Gutachtentool-Mac**
mit `Gutachtentool-<Version>-AppleSilicon.dmg` und `…-Intel.dmg`. Ohne Secrets ist die Fassung unsigniert
(macOS blockiert sie; Test nur über Systemeinstellungen › Datenschutz & Sicherheit › „Dennoch öffnen“).

## Signieren und Notarisieren einrichten (einmalig, ohne Mac möglich)

Voraussetzung: Apple Developer Program, Rolle **Account Holder** (nur diese darf Developer-ID-Zertifikate anlegen).
OpenSSL ist in Git für Windows enthalten (`C:\Program Files\Git\usr\bin\openssl.exe`).

1. **Schlüssel und Zertifikatsanforderung** (PowerShell, in einem sicheren Ordner außerhalb des Repositorys).
   Zuerst eine Minimal-Konfiguration setzen (sonst sucht OpenSSL `C:\Program Files\Common Files\ssl\openssl.cnf`):
   ```
   Set-Content openssl.cnf "[req]`ndistinguished_name = dn`n[dn]" -Encoding ascii
   $env:OPENSSL_CONF = (Resolve-Path openssl.cnf).Path
   openssl req -new -newkey rsa:2048 -nodes -keyout gutachtentool.key -out gutachtentool.csr -subj "/CN=Michael Beurskens/C=DE"
   ```
2. developer.apple.com › Certificates › „+“ › **Developer ID Application** › Profiltyp „G2 Sub-CA“ › `gutachtentool.csr` hochladen
   › Zertifikat (`developerID_application.cer`) herunterladen. Dazu von https://www.apple.com/certificateauthority/
   „Developer ID – G2“ (`DeveloperIDG2CA.cer`) herunterladen.
3. **.p12 erzeugen** (Kennwort frei wählen; ältere Verschlüsselung, damit macOS die Datei lesen kann):
   ```
   openssl x509 -inform DER -in developerID_application.cer -out zertifikat.pem
   openssl x509 -inform DER -in DeveloperIDG2CA.cer -out zwischen.pem
   openssl pkcs12 -export -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1 -inkey gutachtentool.key -in zertifikat.pem -certfile zwischen.pem -out gutachtentool.p12
   ```
4. **API-Schlüssel für die Notarisierung:** App Store Connect › Benutzer und Zugriff › Integrationen › App Store Connect API
   › Teamschlüssel „+“ (Rolle „Developer“) › `AuthKey_XXXXXXXXXX.p8` herunterladen; **Key ID** und **Issuer ID** notieren.
5. **Secrets** im Repository (GitHub › Settings › Secrets and variables › Actions › New repository secret):

   | Name | Inhalt |
   |---|---|
   | `MAC_ZERTIFIKAT_P12` | `[Convert]::ToBase64String([IO.File]::ReadAllBytes("gutachtentool.p12")) \| Set-Clipboard` |
   | `MAC_ZERTIFIKAT_PASSWORT` | Kennwort aus Schritt 3 |
   | `MAC_NOTAR_SCHLUESSEL` | `[Convert]::ToBase64String([IO.File]::ReadAllBytes("AuthKey_XXXXXXXXXX.p8")) \| Set-Clipboard` |
   | `MAC_NOTAR_SCHLUESSEL_ID` | Key ID |
   | `MAC_NOTAR_AUSSTELLER` | Issuer ID |

6. Danach den Ablauf erneut starten (Actions › Build › Run workflow). `gutachtentool.key`, `.p12` und `.p8` sicher
   aufbewahren und nicht ins Repository legen (`.gitignore` schließt `*.p12` aus).

Bündel-ID: `de.uni-passau.jura.gutachtentool` (Werkzeuge/mac/Info.plist).
