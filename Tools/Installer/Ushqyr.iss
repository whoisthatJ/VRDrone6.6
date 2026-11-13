; Ushqyr Windows installer (Inno Setup 6).
; Build: "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" Tools\Installer\Ushqyr.iss
; Packs Builds\Windows_Fullscreen (BuildScript.BuildWindowsTo) into Builds\Installer\Ushqyr_Setup_<version>.exe.
; Installs per user (no administrator rights needed); an administrator can choose "all users" in the wizard.

#define AppName "Ushqyr"
#ifndef AppVersion
  ; Version.txt at the repository root, the same number as the game build.
  #define AppVersion Trim(FileRead(FileOpen(AddBackslash(SourcePath) + "..\..\Version.txt")))
#endif
#define AppPublisher "Multimedia Lab, SDU University"
#define AppExe "Ushqyr.exe"
#define BuildDir "..\..\Builds\Windows_Fullscreen"

[Setup]
; Keep this AppId: upgrades and the uninstaller find earlier installs by it.
AppId={{6E0C5B6A-3F7D-4C1B-9A55-0F2E8C3D7A41}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppComments=Виртуальный тренажёр для подготовки операторов БПЛА
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\..\Builds\Installer
OutputBaseFilename=Ushqyr_Setup_{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=4
WizardStyle=modern
SetupIconFile=..\Icon\Ushqyr.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
CloseApplications=yes
SetupLogging=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; The whole player except Unity's debug-symbols backup folder (not for shipping).
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "Ushqyr_BackUpThisFolder_ButDontShipItWithYourGame,Ushqyr_BurstDebugInformation_DoNotShip"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autoprograms}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Saved settings, records and garage builds live in %USERPROFILE%\AppData\LocalLow\Ushqyr and are kept on uninstall.
