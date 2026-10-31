<#
  Erzeugt Installer\Ausgabe\Gutachtentool-Setup-<Version>.exe
  1. veröffentlicht Gutachten2025 ungepackt (ohne MSIX) und eigenständig (.NET und Windows App SDK enthalten),
  2. kompiliert Gutachtentool.iss mit Inno Setup 6.
  Aufruf im Ordner Installer:  .\Erstellen.ps1      (Version aus <Version> der csproj)
                               .\Erstellen.ps1 -Kompakt   (zusätzlich Trimmen der .NET-/Windows-Bibliotheken – danach gründlich testen)
#>
param([string]$Version, [switch]$Kompakt)
$ErrorActionPreference = 'Stop'

$projekt = Join-Path $PSScriptRoot '..\Gutachten2025\Gutachten2025.csproj'
$quelle  = Join-Path $PSScriptRoot 'publish\x64'

if (Test-Path $quelle) { Remove-Item $quelle -Recurse -Force }
dotnet publish $projekt -c Release -r win-x64 -p:Platform=x64 `
    -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfContained=true `
    -p:PublishSingleFile=false -p:PublishTrimmed=$($Kompakt.IsPresent) -p:TrimMode=partial `
    -p:JsonSerializerIsReflectionEnabledByDefault=true `
    -p:DebugType=none -p:DebugSymbols=false -o $quelle
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish fehlgeschlagen.' }
$mb = [math]::Round(((Get-ChildItem $quelle -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "Veröffentlicht: $mb MB in $quelle"
if (Test-Path (Join-Path $quelle 'onnxruntime.dll')) {
    Write-Warning 'KI-Bestandteile des Windows App SDK sind noch enthalten (onnxruntime.dll) – Ausschluss in der csproj greift nicht.'
}

foreach ($datei in 'Gutachten2025.exe', 'MUSTER.docx', 'MUSTER.xlsx', 'Texte.txt', 'Rohpunkte-Muster.xlsx') {
    if (-not (Test-Path (Join-Path $quelle $datei))) { throw "$datei fehlt im Veröffentlichungsordner." }
}

if (-not $Version) {
    $Version = (Get-Item (Join-Path $quelle 'Gutachten2025.exe')).VersionInfo.ProductVersion -replace '\+.*$', ''
}

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 nicht gefunden. Installation: winget install JRSoftware.InnoSetup' }

& $iscc "/DAppVersion=$Version" "/DQuelle=$quelle" (Join-Path $PSScriptRoot 'Gutachtentool.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup fehlgeschlagen.' }
$setup = Join-Path $PSScriptRoot "Ausgabe\Gutachtentool-Setup-$Version.exe"
Write-Host "Fertig: $setup"

# Verteilung über die Download-Seite: nicht dieses Inno-Setup, sondern das gemeinsame Lehre-Setup
# (InteractX\Lehre.Setup\Veroeffentlichen.ps1, aufgerufen von Werkzeuge\Verteilen.ps1). Dieses Setup bleibt für Tests und GitHub Actions.
