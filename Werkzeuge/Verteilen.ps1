<#
.SYNOPSIS
  Schritt 4: verteilt alle Fassungen des Gutachtentools auf die Download-Seite von WebVote.

.DESCRIPTION
  1. lädt aus dem letzten erfolgreichen GitHub-Lauf (beursken/gutachtentool, Ablauf „Build“, Zweig main)
     die Artefakte Gutachtentool-Web und Gutachtentool-Mac,
  2. legt die Browser-Fassung nach WebVote2026\wwwroot\gutachtentool\ (alter Inhalt wird ersetzt),
  3. legt die Mac-Fassungen unter festen Namen nach WebVote2026\wwwroot\Content\
     (Gutachtentool-Mac-AppleSilicon.dmg, Gutachtentool-Mac-Intel.dmg),
  4. erstellt die Windows-Fassung über das gemeinsame Lehre-Setup
     (InteractX\Lehre.Setup\Veroeffentlichen.ps1 -Ohne InteractX, OutlineProgress),
  5. ergänzt SHA256SUMS.txt und gutachtentool.json (Versionen für Download-Seiten).

  Danach WebVote2026 neu veröffentlichen (MapStaticAssets kennt nur Dateien, die beim Build vorhanden waren).

  Voraussetzung (einmalig): GitHub CLI – winget install GitHub.cli, danach gh auth login.

.EXAMPLE
  .\Werkzeuge\Verteilen.ps1
  .\Werkzeuge\Verteilen.ps1 -OhneWindows          # nur Browser und Mac
  .\Werkzeuge\Verteilen.ps1 -Lauf 41193882705     # bestimmter Lauf statt des letzten erfolgreichen
#>
param(
    # wwwroot von WebVote2026
    [string]$WebVote = (Join-Path $PSScriptRoot '..\..\WebVote2026\wwwroot'),
    # Windows-Fassung nicht neu erstellen
    [switch]$OhneWindows,
    # Nummer des GitHub-Laufs (Vorgabe: letzter erfolgreicher auf main)
    [string]$Lauf
)
$ErrorActionPreference = 'Stop'
$repo = 'beursken/gutachtentool'
function Schritt($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

if (-not (Test-Path $WebVote)) { throw "WebVote nicht gefunden: $WebVote (Parameter -WebVote)." }
$WebVote = (Resolve-Path $WebVote).Path
$content = Join-Path $WebVote 'Content'
New-Item -ItemType Directory -Force $content | Out-Null

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI fehlt: winget install GitHub.cli, danach gh auth login.' }
gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI ist nicht angemeldet: gh auth login.' }

# ---------- 1. Artefakte laden ----------
Schritt 'GitHub-Lauf'
if (-not $Lauf) {
    $info = gh run list -R $repo --workflow Build --branch main --status success --limit 1 --json databaseId,headSha,createdAt,displayTitle | ConvertFrom-Json
    if (-not $info) { throw 'Kein erfolgreicher Lauf auf main gefunden.' }
    $Lauf = $info[0].databaseId
    Write-Host "   Lauf $Lauf vom $(([datetime]$info[0].createdAt).ToLocalTime().ToString('dd.MM.yyyy HH:mm')) – $($info[0].displayTitle) ($($info[0].headSha.Substring(0,7)))"
}
$temp = Join-Path ([IO.Path]::GetTempPath()) "Gutachtentool-Verteilen-$Lauf"
if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
foreach ($name in 'Gutachtentool-Web', 'Gutachtentool-Mac') {
    gh run download $Lauf -R $repo -n $name -D (Join-Path $temp $name)
    if ($LASTEXITCODE -ne 0) { throw "Artefakt $name nicht geladen (Lauf $Lauf)." }
}

# ---------- 2. Browser-Fassung ----------
Schritt 'Browser-Fassung'
$web = Join-Path $temp 'Gutachtentool-Web'
if (-not (Test-Path (Join-Path $web 'index.html'))) { throw 'Gutachtentool-Web enthält keine index.html.' }
$webZiel = Join-Path $WebVote 'gutachtentool'
if (Test-Path $webZiel) { Remove-Item $webZiel -Recurse -Force }
Copy-Item $web $webZiel -Recurse
$mb = [math]::Round(((Get-ChildItem $webZiel -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "   $webZiel ($mb MB)"

# ---------- 3. Mac-Fassungen ----------
Schritt 'Mac-Fassungen'
$macDateien = @()
foreach ($art in 'AppleSilicon', 'Intel') {
    $dmg = Get-ChildItem (Join-Path $temp 'Gutachtentool-Mac') -Filter "*-$art.dmg" | Select-Object -First 1
    if (-not $dmg) { throw "Gutachtentool-Mac enthält keine *-$art.dmg." }
    $ziel = "Gutachtentool-Mac-$art.dmg"
    Copy-Item $dmg.FullName (Join-Path $content $ziel) -Force
    $macDateien += $ziel
    $macVersion = ($dmg.BaseName -replace '^Gutachtentool-', '') -replace "-$art$", ''
    Write-Host "   $ziel (Version $macVersion, $([math]::Round($dmg.Length / 1MB)) MB)"
}

# ---------- 4. Windows (Lehre-Setup) ----------
if (-not $OhneWindows) {
    Schritt 'Windows (Lehre-Setup)'
    $lehre = Join-Path $PSScriptRoot '..\..\InteractX\Lehre.Setup\Veroeffentlichen.ps1'
    if (-not (Test-Path $lehre)) { throw "Lehre-Setup nicht gefunden: $lehre (oder -OhneWindows)." }
    & $lehre -Ziel $content -Ohne InteractX, OutlineProgress
}

# ---------- 5. Prüfsummen und Versionen ----------
Schritt 'Prüfsummen'
$summen = Join-Path $content 'SHA256SUMS.txt'
$zeilen = @()
if (Test-Path $summen) { $zeilen = @(Get-Content $summen | Where-Object { $_ -notmatch 'Gutachtentool-Mac-' }) }
foreach ($d in $macDateien) {
    $zeilen += '{0}  {1}' -f (Get-FileHash (Join-Path $content $d) -Algorithm SHA256).Hash.ToLowerInvariant(), $d
}
[IO.File]::WriteAllLines($summen, $zeilen)

$windowsVersion = $null
$katalog = Join-Path $content 'pakete.json'
if (Test-Path $katalog) {
    $windowsVersion = ((Get-Content $katalog -Raw -Encoding UTF8 | ConvertFrom-Json).pakete | Where-Object { $_.id -eq 'Gutachtentool' }).version
}
$versionen = [ordered]@{
    erstellt = (Get-Date).ToString('s')
    lauf     = "$Lauf"
    windows  = $windowsVersion
    mac      = $macVersion
    browser  = $macVersion
}
[IO.File]::WriteAllText((Join-Path $content 'gutachtentool.json'), ($versionen | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))

# ---------- Hinweise ----------
$programm = Join-Path $WebVote '..\Program.cs'
if ((Test-Path $programm) -and -not (Select-String -Path $programm -Pattern '\.dat' -Quiet)) {
    Write-Warning 'WebVote: In Program.cs fehlt vermutlich die Zuordnung für .dat/.webmanifest/.dmg (siehe Gutachtentool.Web\LIESMICH.md).'
}
Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue

Schritt 'Fertig'
Write-Host 'Jetzt WebVote2026 neu veröffentlichen. Danach erreichbar:'
Write-Host '   https://vote.jura.uni-passau.de/gutachtentool/'
Write-Host '   https://vote.jura.uni-passau.de/Content/Gutachtentool-Mac-AppleSilicon.dmg'
Write-Host '   https://vote.jura.uni-passau.de/Content/Gutachtentool-Mac-Intel.dmg'
Write-Host '   https://vote.jura.uni-passau.de/Content/Gutachtentool-Setup.exe'
