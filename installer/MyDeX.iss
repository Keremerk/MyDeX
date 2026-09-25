; MyDeX installer (Inno Setup 6). Built by scripts\Publish.ps1, which passes /DAppVersion=x.y.z.
; Installs per user (no admin prompt) into %LocalAppData%\Programs\MyDeX.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{8F0C2E7A-6B1D-4C8E-9F3A-2D5B7E1C4A90}
AppName=MyDeX
AppVersion={#AppVersion}
AppVerName=MyDeX {#AppVersion}
AppPublisher=Keremerk
AppPublisherURL=https://github.com/Keremerk/MyDeX
DefaultDirName={localappdata}\Programs\MyDeX
DisableProgramGroupPage=yes
; Always its own folder: uninstall removes folders inside {app}, so {app} must never be a folder of the user's.
DisableDirPage=yes
UsePreviousAppDir=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=MyDeX-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\MyDeX.exe
UninstallDisplayName=MyDeX
; Closes MyDeX (and the adb it started) if it is running during an update or uninstall.
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[InstallDelete]
; MyDeX can update its own scrcpy; start every install/upgrade from the bundled copy so no stray files mix in.
Type: filesandordirs; Name: "{app}\tools\scrcpy"
Type: filesandordirs; Name: "{app}\tools\scrcpy.old"

[UninstallDelete]
; Files added by a scrcpy self-update were never installed by setup, so remove them explicitly –
; only MyDeX's own subfolders, never anything else in {app}.
Type: filesandordirs; Name: "{app}\tools\scrcpy"
Type: filesandordirs; Name: "{app}\tools\scrcpy.old"
Type: dirifempty; Name: "{app}\tools"
Type: dirifempty; Name: "{app}"

[Files]
Source: "..\dist\MyDeX\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\MyDeX"; Filename: "{app}\MyDeX.exe"
Name: "{autodesktop}\MyDeX"; Filename: "{app}\MyDeX.exe"; Tasks: desktopicon

[Registry]
; MyDeX adds this itself when "Start MyDeX with Windows" is on; make sure uninstalling removes it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "MyDeX"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\MyDeX.exe"; Description: "Start MyDeX now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; adb keeps a background server running from {app}\tools; stop it so its files can be removed.
Filename: "{app}\tools\scrcpy\adb.exe"; Parameters: "kill-server"; Flags: runhidden waituntilterminated; RunOnceId: "StopAdb"

[Code]
// Settings and the log (%AppData%\MyDeX) are kept for a reinstall unless the user wants them gone.
// In a silent uninstall the answer defaults to "No" (keep them).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\MyDeX');
    if DirExists(DataDir) then
      if SuppressibleMsgBox('Also delete your MyDeX settings and log?' + #13#10 + '(' + DataDir + ')',
                            mbConfirmation, MB_YESNO, IDNO) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
