#define MyAppName "SteerCast"
#ifndef MyAppVersion
#define MyAppVersion "0.1.8"
#endif
#define MyAppExeName "SteerCast.exe"

[Setup]
AppId={{07D3B28F-CE21-47C2-9D4D-E370A08463F5}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\SteerCast
DefaultGroupName=SteerCast
OutputDir=..\artifacts
OutputBaseFilename=SteerCast-{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\branding\app-icon.ico
LicenseFile=..\LICENSE
WizardStyle=modern

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: filesandordirs; Name: "{app}\wwwroot"

[Icons]
Name: "{group}\SteerCast"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\SteerCast"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startup"; Description: "Launch SteerCast when I sign in"; GroupDescription: "Startup:"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SteerCast"; ValueData: """{app}\{#MyAppExeName}"" --background"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch SteerCast"; Flags: nowait postinstall skipifsilent
