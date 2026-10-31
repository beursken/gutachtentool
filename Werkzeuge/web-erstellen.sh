#!/usr/bin/env bash
# Veröffentlicht die Browser-Fassung nach publish/web/wwwroot (statische Dateien, auf jedem Webserver lauffähig).
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet publish Gutachtentool.Web/Gutachtentool.Web.csproj -c Release -o publish/web
echo "Fertig: publish/web/wwwroot ($(du -sh publish/web/wwwroot | cut -f1))"
