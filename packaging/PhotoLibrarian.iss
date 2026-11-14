#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef PublishDir
  #error PublishDir must be supplied by Build-Installer.ps1
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define MyAppName "PhotoLibrarian"
#define MyAppPublisher "Trimenor"
#define MyAppExeName "PhotoLibrarian.exe"
#define MyAppAssocName "PhotoLibrarian Album"
#define MyAppAssocExt ".pla"
#define MyAppAssocKey StringChange(MyAppAssocName, " ", "") + MyAppAssocExt

[Setup]
AppId={{B4747EB1-9710-4C66-A723-EE942659BD05}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/zafodtom/PhotoLibrarian
AppSupportURL=https://github.com/zafodtom/PhotoLibrarian/issues
AppUpdatesURL=https://github.com/zafodtom/PhotoLibrarian/releases/latest
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=PhotoLibrarian-{#MyAppVersion}-Setup-x64
SetupIconFile=..\src\PhotoLibrarian\Assets\PhotoLibrarian.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes
; Silent in-app upgrades reuse the previously selected optional shell task.
UsePreviousTasks=yes
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName} installer

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "explorercontext"; Description: "Add Create PhotoLibrarian album here to File Explorer"; GroupDescription: "Windows Explorer integration:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocExt}\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

; Folder right-click: %1 is the selected directory path.
Root: HKA; Subkey: "Software\Classes\Directory\shell\PhotoLibrarian.CreateAlbum"; ValueType: string; ValueName: ""; ValueData: "Create PhotoLibrarian album here"; Flags: uninsdeletekey; Tasks: explorercontext
Root: HKA; Subkey: "Software\Classes\Directory\shell\PhotoLibrarian.CreateAlbum"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName},0"; Tasks: explorercontext
Root: HKA; Subkey: "Software\Classes\Directory\shell\PhotoLibrarian.CreateAlbum\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --album ""%1"""; Tasks: explorercontext
; Empty background right-click: %V resolves the directory currently viewed.
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\PhotoLibrarian.CreateAlbum"; ValueType: string; ValueName: ""; ValueData: "Create PhotoLibrarian album here"; Flags: uninsdeletekey; Tasks: explorercontext
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\PhotoLibrarian.CreateAlbum"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExeName},0"; Tasks: explorercontext
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\PhotoLibrarian.CreateAlbum\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --album ""%V"""; Tasks: explorercontext


[Code]
// Removing an optional task during an upgrade must also remove registry
// entries created by earlier versions: skipped [Registry] entries alone would
// leave those previous shell commands behind.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssInstall) and not WizardIsTaskSelected('explorercontext') then
  begin
    RegDeleteKeyIncludingSubkeys(HKLM,
      'Software\Classes\Directory\shell\PhotoLibrarian.CreateAlbum');
    RegDeleteKeyIncludingSubkeys(HKLM,
      'Software\Classes\Directory\Background\shell\PhotoLibrarian.CreateAlbum');
  end;
end;

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
