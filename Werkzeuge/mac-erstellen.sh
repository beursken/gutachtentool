#!/usr/bin/env bash
# Erstellt die Mac-Fassung als Gutachtentool.app in einem .dmg – je eines für Apple Silicon (arm64) und Intel (x64).
# Mit Zertifikat (Secrets) wird signiert und notarisiert, sonst entsteht eine unsignierte Fassung zum Testen.
#
# Secrets im GitHub-Repository (Settings › Secrets and variables › Actions):
#   MAC_ZERTIFIKAT_P12       Zertifikat „Developer ID Application“ samt privatem Schlüssel als .p12, base64-kodiert
#   MAC_ZERTIFIKAT_PASSWORT  Kennwort der .p12-Datei
#   MAC_NOTAR_SCHLUESSEL     App-Store-Connect-API-Schlüssel (AuthKey_XXXX.p8), base64-kodiert
#   MAC_NOTAR_SCHLUESSEL_ID  Schlüssel-ID (Key ID)
#   MAC_NOTAR_AUSSTELLER     Aussteller-ID (Issuer ID)
set -euo pipefail
cd "$(dirname "$0")/.."
WURZEL="$(pwd)"
TEMPORAER="${RUNNER_TEMP:-${TMPDIR:-/tmp}}"

# Ausgabe mitschreiben; bei einem Fehler die letzten Zeilen als Annotation melden (im Lauf unter „Annotations“ sichtbar).
PROTOKOLL="$TEMPORAER/mac-protokoll.txt"
exec 3>&1
exec > >(tee "$PROTOKOLL") 2>&1
PHASE="Start"
phase() { PHASE="$1"; echo "::notice title=Mac-Build::$1" >&3; }
amEnde() {
  local code=$?
  [ $code -eq 0 ] && return 0
  sleep 1
  local ende
  ende=$(tail -n 25 "$PROTOKOLL" | cut -c1-200 | sed -e 's/%/%25/g' -e 's/\r//g' | awk 'BEGIN{ORS="%0A"} {print}' | cut -c1-3500)
  echo "::error title=Mac-Build gescheitert (${PHASE})::${ende}" >&3
  sleep 1
}
set -E
trap amEnde EXIT

if [ ! -d Gutachtentool.Mac ]; then
  echo "Gutachtentool.Mac fehlt – übersprungen."
  exit 0
fi

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Gutachtentool.Mac/Gutachtentool.Mac.csproj | head -1)
AUSGABE="$WURZEL/publish/mac"
rm -rf "$AUSGABE"
mkdir -p "$AUSGABE"

# ---------- Programmsymbol (.icns) aus icon-512.png ----------
phase "Programmsymbol"
ICONSET="$AUSGABE/Gutachtentool.iconset"
mkdir -p "$ICONSET"
QUELLE="Gutachtentool.Web/wwwroot/icon-512.png"
for g in 16 32 128 256 512; do
  sips -z $g $g "$QUELLE" --out "$ICONSET/icon_${g}x${g}.png" >/dev/null
  d=$((g * 2))
  sips -z $d $d "$QUELLE" --out "$ICONSET/icon_${g}x${g}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$AUSGABE/Gutachtentool.icns"

# ---------- Signierung vorbereiten ----------
phase "Zertifikat einrichten"
IDENTITAET=""
if [ -n "${MAC_ZERTIFIKAT_P12:-}" ]; then
  SCHLUESSELBUND="$TEMPORAER/gutachtentool.keychain-db"
  KENNWORT="$(uuidgen)"
  security create-keychain -p "$KENNWORT" "$SCHLUESSELBUND"
  security set-keychain-settings -lut 21600 "$SCHLUESSELBUND"
  security unlock-keychain -p "$KENNWORT" "$SCHLUESSELBUND"
  echo "$MAC_ZERTIFIKAT_P12" | base64 --decode > "$TEMPORAER/zertifikat.p12"
  security import "$TEMPORAER/zertifikat.p12" -P "${MAC_ZERTIFIKAT_PASSWORT:-}" -A -t cert -f pkcs12 -k "$SCHLUESSELBUND"
  rm -f "$TEMPORAER/zertifikat.p12"
  security set-key-partition-list -S apple-tool:,apple: -s -k "$KENNWORT" "$SCHLUESSELBUND" >/dev/null
  security list-keychains -d user -s "$SCHLUESSELBUND" $(security list-keychains -d user | tr -d '"')
  IDENTITAET=$(security find-identity -v -p codesigning "$SCHLUESSELBUND" | grep "Developer ID Application" | head -1 | sed -E 's/.*"(.*)".*/\1/')
  if [ -z "$IDENTITAET" ]; then echo "::error::Kein Zertifikat „Developer ID Application“ in MAC_ZERTIFIKAT_P12."; exit 1; fi
  echo "Signiere mit: $IDENTITAET"
else
  echo "::warning::Keine Signatur-Secrets – die Mac-Fassung wird nicht signiert (nur zum Testen)."
fi

NOTAR=""
if [ -n "$IDENTITAET" ] && [ -n "${MAC_NOTAR_SCHLUESSEL:-}" ]; then
  echo "$MAC_NOTAR_SCHLUESSEL" | base64 --decode > "$TEMPORAER/AuthKey.p8"
  NOTAR=1
fi

signieren() {
  codesign --force --timestamp --options runtime \
    --entitlements "$WURZEL/Werkzeuge/mac/Gutachtentool.entitlements" --sign "$IDENTITAET" "$1"
}

# ---------- je Architektur: veröffentlichen, bündeln, signieren, DMG ----------
for RID in osx-arm64 osx-x64; do
  NAME=$([ "$RID" = osx-arm64 ] && echo "AppleSilicon" || echo "Intel")
  ZIEL="$AUSGABE/$RID"
  phase "Veröffentlichen $RID"
  dotnet publish Gutachtentool.Mac/Gutachtentool.Mac.csproj -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=false -p:UseAppHost=true -p:DebugType=none -p:DebugSymbols=false -o "$ZIEL/publish"

  APP="$ZIEL/Gutachtentool.app"
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
  cp -R "$ZIEL/publish/." "$APP/Contents/MacOS/"
  sed "s/@VERSION@/$VERSION/g" Werkzeuge/mac/Info.plist > "$APP/Contents/Info.plist"
  cp "$AUSGABE/Gutachtentool.icns" "$APP/Contents/Resources/Gutachtentool.icns"
  chmod +x "$APP/Contents/MacOS/Gutachtentool"

  phase "Signieren $RID"
  if [ -n "$IDENTITAET" ]; then
    # Erst alle Dateien einzeln (Bibliotheken, Programm), dann das Bündel
    while IFS= read -r datei; do
      signieren "$datei" || { echo "Signieren fehlgeschlagen: $datei"; exit 1; }
    done < <(find "$APP/Contents/MacOS" -type f)
    signieren "$APP"
    codesign --verify --deep --strict --verbose=2 "$APP" || echo "::warning title=Mac-Build::codesign --verify meldet Abweichungen ($RID)" >&3
  fi

  phase "DMG $RID"
  DMG="$AUSGABE/Gutachtentool-$VERSION-$NAME.dmg"
  INHALT="$ZIEL/dmg"
  mkdir -p "$INHALT"
  cp -R "$APP" "$INHALT/"
  ln -s /Applications "$INHALT/Programme"
  hdiutil create -volname "Gutachtentool" -srcfolder "$INHALT" -ov -format UDZO "$DMG"

  if [ -n "$IDENTITAET" ]; then
    codesign --force --timestamp --sign "$IDENTITAET" "$DMG"
  fi
  if [ -n "$NOTAR" ]; then
    phase "Notarisieren $RID"
    xcrun notarytool submit "$DMG" --key "$TEMPORAER/AuthKey.p8" \
      --key-id "${MAC_NOTAR_SCHLUESSEL_ID:-}" --issuer "${MAC_NOTAR_AUSSTELLER:-}" --wait --timeout 30m
    xcrun stapler staple "$DMG"
    spctl --assess --type open --context context:primary-signature -v "$DMG" || true
  fi
  echo "Fertig: $DMG"
done

rm -f "$TEMPORAER/AuthKey.p8" 2>/dev/null || true
