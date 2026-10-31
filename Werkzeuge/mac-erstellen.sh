#!/usr/bin/env bash
# Erstellt die Mac-Fassung (folgt in Schritt 3). Bis dahin ohne Wirkung.
set -euo pipefail
cd "$(dirname "$0")/.."
if [ ! -d Gutachtentool.Mac ]; then
  echo "Gutachtentool.Mac ist noch nicht vorhanden – übersprungen."
  exit 0
fi
