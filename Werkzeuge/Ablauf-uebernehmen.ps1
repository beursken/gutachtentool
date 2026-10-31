# Übernimmt die Vorlagen Werkzeuge\build.yml und Werkzeuge\release.yml als GitHub-Actions-Abläufe (.github\workflows).
$ziel = Join-Path $PSScriptRoot '..\.github\workflows'
New-Item -ItemType Directory -Force $ziel | Out-Null
foreach ($datei in 'build.yml', 'release.yml') { Copy-Item (Join-Path $PSScriptRoot $datei) $ziel -Force }
Write-Host 'Übernommen. Jetzt Commit und Push.'
