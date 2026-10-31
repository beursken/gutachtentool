; Installationsprogramm für das Gutachtentool (Inno Setup 6.3 oder neuer).
; Installiert ohne Administratorrechte nach %LOCALAPPDATA%\Programs\Gutachtentool,
; Startmenüeintrag und Deinstallation nur für den angemeldeten Benutzer (HKCU).
; Erzeugen: Erstellen.ps1 (veröffentlicht die App und ruft ISCC mit Version und Quellordner auf).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Quelle
  #define Quelle "publish\x64"
#endif
#define AppExe "Gutachten2025.exe"

[Setup]
; AppId nie ändern: daran erkennt Windows Updates und Deinstallation.
AppId={{EBA0688A-A3EA-422E-BE19-3878FF2691FF}
AppName=Gutachtentool
AppVersion={#AppVersion}
AppVerName=Gutachtentool {#AppVersion}
AppPublisher=Prof. Dr. Michael Beurskens, Universität Passau
VersionInfoVersion={#AppVersion}
; Standard: nur für mich (keine Adminrechte). Wer Adminrechte hat, darf im Dialog „für alle“ wählen.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
DefaultDirName={autopf}\Gutachtentool
DisableProgramGroupPage=yes
; Fester Zielordner (wegen [InstallDelete] kein frei wählbares Verzeichnis).
DisableDirPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile=..\Gutachten2025\gutachten.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=Gutachtentool
OutputDir=Ausgabe
OutputBaseFilename=Gutachtentool-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
; Laufendes Gutachtentool vor dem Update schließen lassen.
CloseApplications=yes
RestartApplications=no
; Dateizuordnung .gtpaket (unter HKCU bzw. bei Installation für alle unter HKLM) – Explorer danach aktualisieren.
ChangesAssociations=yes

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Bei Updates alte Programmdateien entfernen (Einstellungen liegen in %LOCALAPPDATA%\Gutachtentool,
; Klausuren und Vorlagen im gewählten Ablageordner – beides bleibt unberührt).
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#Quelle}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; Doppelklick auf *.gtpaket öffnet das Gutachtentool (HKA = HKCU ohne, HKLM mit Administratorrechten).
Root: HKA; Subkey: "Software\Classes\.gtpaket"; ValueType: string; ValueName: ""; ValueData: "Gutachtentool.Paket"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.gtpaket"; ValueType: string; ValueName: "Content Type"; ValueData: "application/x-gutachtentool-paket"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.gtpaket\OpenWithProgids"; ValueType: string; ValueName: "Gutachtentool.Paket"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Gutachtentool.Paket"; ValueType: string; ValueName: ""; ValueData: "Gutachtentool-Paket"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Gutachtentool.Paket\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"
Root: HKA; Subkey: "Software\Classes\Gutachtentool.Paket\shell\open"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Gutachtentool"
Root: HKA; Subkey: "Software\Classes\Gutachtentool.Paket\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".gtpaket"; ValueData: ""; Flags: uninsdeletekey

[Icons]
Name: "{autoprograms}\Gutachtentool"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\Gutachtentool"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,Gutachtentool}"; Flags: nowait postinstall skipifsilent
