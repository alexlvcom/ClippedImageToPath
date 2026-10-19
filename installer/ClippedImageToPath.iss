#ifndef AppVersion
  #error AppVersion must be supplied by scripts/build-installer.ps1
#endif

[Setup]
AppId={{04EC45AA-557F-4EC7-9669-6134271F56CC}
AppName=Clipped Image to Path
AppVersion={#AppVersion}
AppPublisher=alexlvcom
AppPublisherURL=https://github.com/alexlvcom/ClippedImageToPath
AppSupportURL=https://github.com/alexlvcom/ClippedImageToPath/issues
DefaultDirName={autopf}\Clipped Image to Path
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=ClippedImageToPath-Setup
SetupIconFile=..\ClippedImageToPath.ico
UninstallDisplayIcon={app}\ClippedImageToPath.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked
Name: startup; Description: "Start with Windows"; Flags: unchecked

[Files]
Source: "..\publish\installer\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Clipped Image to Path"; Filename: "{app}\ClippedImageToPath.exe"
Name: "{autodesktop}\Clipped Image to Path"; Filename: "{app}\ClippedImageToPath.exe"; Tasks: desktopicon
Name: "{autostartup}\Clipped Image to Path"; Filename: "{app}\ClippedImageToPath.exe"; Tasks: startup

[Run]
Filename: "{app}\ClippedImageToPath.exe"; Description: "Start Clipped Image to Path"; Flags: nowait postinstall skipifsilent
Filename: "{app}\ClippedImageToPath.exe"; Flags: nowait; Check: RestartSilently

[Code]
var
  WasRunning: Boolean;

{ Interactive setup always offers the checked Start checkbox; silent setup restarts a previously running app. }
function RestartSilently: Boolean;
begin
  Result := WasRunning and WizardSilent;
end;

function StopApplication: Boolean;
var
  AppWindow: HWND;
  Attempt: Integer;
begin
  Result := not CheckForMutexes('ClippedImageToPath.Singleton');
  if Result then
    Exit;

  Log('Requesting application shutdown.');
  AppWindow := FindWindowByWindowName('ClippedImageToPathListener');
  if AppWindow <> 0 then
    PostMessage(AppWindow, $0010, 0, 0); { WM_CLOSE: same clean exit as the tray menu }

  for Attempt := 1 to 100 do
  begin
    if not CheckForMutexes('ClippedImageToPath.Singleton') then
    begin
      Result := True;
      Exit;
    end;
    Sleep(100);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { AppMutex would prompt before this hook. Check it ourselves after requesting exit. }
  WasRunning := WasRunning or CheckForMutexes('ClippedImageToPath.Singleton');
  Result := '';
  if not StopApplication then
    Result := 'Clipped Image to Path did not respond to the shutdown request. Setup cannot safely replace its files. Please retry setup.';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { Run after uninstall confirmation, before any files are removed. }
  if CurUninstallStep = usAppMutexCheck then
    if not StopApplication then
      RaiseException('Clipped Image to Path did not respond to the shutdown request. No application files have been removed.');
end;
