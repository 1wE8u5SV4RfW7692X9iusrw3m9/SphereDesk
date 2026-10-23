; Per-user installer for the win-x64 self-contained publish directory.
; The wizard uses Inno Setup's default language because ChineseSimplified.isl is not installed.

[Setup]
AppId={{6C505493-D53C-4797-BCFB-5440BBC58BCB}
AppName=SphereDesk
AppVersion=1.0.0
AppMutex=Local\SphereDesk.SingleInstance.v1
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\SphereDesk
DefaultGroupName=SphereDesk
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\SphereDesk.exe
OutputDir=..\artifacts
OutputBaseFilename=SphereDesk-Setup-1.0.0
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dynamic
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

[Files]
Source: "..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SphereDesk"; Filename: "{app}\SphereDesk.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SphereDesk"; ValueData: """{app}\SphereDesk.exe"""; Flags: uninsdeletevalue
