# Übernimmt die Vorlage Werkzeuge\build.yml als GitHub-Actions-Ablauf (.github\workflows\build.yml).
$ziel = Join-Path $PSScriptRoot '..\.github\workflows'
New-Item -ItemType Directory -Force $ziel | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'build.yml') $ziel -Force
Write-Host 'Übernommen. Jetzt Commit und Push.'
