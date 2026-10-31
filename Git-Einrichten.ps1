<#
  Einmalig: räumt die nach Gutachten2025.Core verschobenen Dateien auf, legt das Git-Repository an
  und überträgt es nach GitHub (dort vorher ein leeres, privates Repository anlegen, ohne README).
  Aufruf im Ordner Gutachten2025:   .\Git-Einrichten.ps1 -Url https://github.com/beursken/Gutachtentool.git
#>
param([Parameter(Mandatory)][string]$Url)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# 1. Alte Kopien im WinUI-Projekt entfernen (die Fassungen in Gutachten2025.Core gelten).
$kern = Join-Path $PSScriptRoot 'Gutachten2025.Core'
if (-not (Test-Path (Join-Path $kern 'ViewModels\MainViewModel.cs'))) { throw 'Gutachten2025.Core fehlt – Abbruch.' }
foreach ($alt in 'Models', 'Services', 'ViewModels', 'Rohpunkte', 'MUSTER.docx', 'MUSTER.xlsx', 'Texte.txt', 'Rohpunkte-Muster.xlsx') {
    $pfad = Join-Path $PSScriptRoot "Gutachten2025\$alt"
    if (Test-Path $pfad) { Remove-Item $pfad -Recurse -Force; Write-Host "Entfernt: Gutachten2025\$alt" }
}

# 2. GitHub-Actions-Ablauf an seinen Platz (.github\workflows) kopieren.
$ablauf = Join-Path $PSScriptRoot '.github\workflows'
New-Item -ItemType Directory -Force $ablauf | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'Werkzeuge\build.yml') $ablauf -Force

# 3. Repository anlegen und hochladen.
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Git fehlt. Installation: winget install Git.Git' }
if (-not (Test-Path '.git')) { git init -b main | Out-Null }
if (-not (git config user.name)) { throw 'Git kennt Ihren Namen noch nicht: git config --global user.name "…" und git config --global user.email "…"' }
git add -A
git commit -m 'Gutachtentool 1.3: gemeinsamer Kern (Gutachten2025.Core), GitHub Actions'
if (-not ((git remote) -contains 'origin')) { git remote add origin $Url }
git push -u origin main
Write-Host 'Fertig. Den Build-Lauf zeigt GitHub unter Actions.'
