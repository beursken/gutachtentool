# Entfernt Dateien, die in die gemeinsame Oberfläche (Gutachtentool.Oberflaeche) verschoben wurden
# und nur noch als leere Platzhalter vorhanden sind. Danach Commit und Push.
$web = Join-Path $PSScriptRoot '..\Gutachtentool.Web'
foreach ($datei in 'App.razor', 'Komponenten', 'Dienste\Ablagespeicher.cs', 'Dienste\WebUi.cs', 'Dienste\WebEditor.cs') {
    $pfad = Join-Path $web $datei
    if (Test-Path $pfad) { Remove-Item $pfad -Recurse -Force; Write-Host "Entfernt: Gutachtentool.Web\$datei" }
}
