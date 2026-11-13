; Dual Precision installer (Inno Setup 6).
; Don't run this directly - double-click build-installer.cmd in the project folder, which publishes the app first
; and passes the version in. The app itself installs per-user with no administrator rights.
; Optionally (a ticked task, only shown when VIIPER is missing) it downloads VIIPER from its official GitHub
; release, verifies the file's SHA-256, and runs VIIPER's own unattended installer. That installer adds the signed
; usbip-win2 driver, which Windows asks administrator permission for - that prompt is the user's explicit consent.

#define AppName "Dual Precision"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppPublisher "Opifex Studios"
#define AppExe "DualSensePrecision.exe"
#define DataDir "{localappdata}\DualSensePrecision"

; VIIPER, pinned to the version this app was tested with. The hash was taken from the official v0.9.0 release
; asset (no checksum file is published upstream); a download that doesn't match is refused.
#define ViiperVersion "0.9.0"
#define ViiperUrl "https://github.com/Alia5/VIIPER/releases/download/v0.9.0/viiper-windows-amd64.exe"
#define ViiperSha256 "142c144f6abee8b8a5f6a1abbc7018683444de71d2e45102cde6aaab9b2223e6"
#define HidHideVersion "1.5.230"
#define HidHideUrl "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe"
#define HidHideSha256 "f4bbbcb82e6258641b887c74bc81c4c5f66e4aa811808dfc304347687b7605f6"

[Setup]
AppId={{6B0E8F57-3C1A-4E4B-9F6E-2D7C0B9A51D3}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=Output
OutputBaseFilename=DualPrecision-{#AppVersion}-setup
SetupIconFile=..\src\DualSensePrecision.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; A running copy is asked to quit cleanly first (see CloseRunningApp in [Code]), so its controller gets un-hidden
; and the virtual pad removed, exactly like choosing Exit from its tray icon.
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startup"; Description: "Start Dual Precision with Windows (in the tray, so your settings are ready for games)"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "viiper"; Description: "Download and install VIIPER {#ViiperVersion} (about 13 MB from GitHub) - needed for the virtual Xbox controller. It installs the signed usbip-win2 driver: Windows will ask for administrator permission (click Yes), and a restart is needed."; GroupDescription: "Virtual controller:"; Check: not ViiperInstalled
Name: "hidhide"; Description: "Download and install HidHide {#HidHideVersion} (about 8 MB from GitHub) - lets the app hide your real controller from games so they only see the virtual one (you switch this on in the app). HidHide's own installer opens for you to click through; Windows asks for administrator permission for its signed driver, and a restart is needed."; GroupDescription: "Virtual controller:"; Check: not HidHideInstalled

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; DestName: "THIRD-PARTY-NOTICES.txt"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Dual Precision"; ValueData: """{app}\{#AppExe}"" --minimized"; Flags: uninsdeletevalue; Tasks: startup

[InstallDelete]
; Shortcuts from before the rename to Dual Precision (0.19), so an update doesn't leave two of each.
Type: files; Name: "{autoprograms}\DualSense Precision.lnk"
Type: files; Name: "{autodesktop}\DualSense Precision.lnk"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Messages]
FinishedLabel=Setup has finished installing [name] on your computer.%n%nPlug in your DualSense (USB-C) or Xbox controller and it's detected automatically. Turn on the virtual Xbox controller, and hiding your real controller, on the Virtual Controller page.

[Code]
var
  DownloadPage: TDownloadWizardPage;
  ViiperDownloaded: Boolean;
  ViiperRan: Boolean;
  HidHideDownloaded: Boolean;
  HidHideRan: Boolean;

// Asks a running Dual Precision to quit the same way its tray menu's Exit does (un-hides the controller,
// removes the virtual pad), then waits for it. Older versions ignore the message; the user is asked instead.
const
  AppMutexName = 'DualSensePrecision.SingleInstance';
  WM_APP_EXIT = $8002;

function CloseRunningApp(): Boolean;
var
  Wnd: HWND;
  I: Integer;
begin
  Result := True;
  if not CheckForMutexes(AppMutexName) then Exit;
  Wnd := FindWindowByClassName('DualSensePrecisionTray');
  if Wnd <> 0 then
  begin
    PostMessage(Wnd, WM_APP_EXIT, 0, 0);
    I := 0;
    while CheckForMutexes(AppMutexName) and (I < 50) do
    begin
      Sleep(200);
      I := I + 1;
    end;
  end;
  while CheckForMutexes(AppMutexName) do
    if SuppressibleMsgBox('Dual Precision is still running.' + #13#10 +
         'Right-click its tray icon and choose Exit, then click OK.', mbError, MB_OKCANCEL, IDCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
end;

function InitializeSetup(): Boolean;
begin
  Result := CloseRunningApp();
end;

function InitializeUninstall(): Boolean;
begin
  Result := CloseRunningApp();
end;

function ViiperInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{localappdata}\VIIPER\viiper.exe'));
end;

function HidHideInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\HidHide');
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('Downloading components', 'Getting the optional components you ticked from GitHub...', nil);
end;

// Download happens when the user clicks Install, so a failed download can still continue without VIIPER.
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and (WizardIsTaskSelected('viiper') or WizardIsTaskSelected('hidhide')) then
  begin
    DownloadPage.Clear;
    if WizardIsTaskSelected('viiper') then
      DownloadPage.Add('{#ViiperUrl}', 'viiper-setup.exe', '{#ViiperSha256}');
    if WizardIsTaskSelected('hidhide') then
      DownloadPage.Add('{#HidHideUrl}', 'hidhide-setup.exe', '{#HidHideSha256}');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        ViiperDownloaded := WizardIsTaskSelected('viiper');
        HidHideDownloaded := WizardIsTaskSelected('hidhide');
      except
        ViiperDownloaded := False;
        HidHideDownloaded := False;
        if not DownloadPage.AbortedByUser then
          Result := SuppressibleMsgBox('A component could not be downloaded:' + #13#10 + GetExceptionMessage + #13#10 + #13#10 +
            'Continue installing Dual Precision without the optional components? You can run this setup again later to add them.',
            mbError, MB_YESNO, IDYES) = IDYES
        else
          Result := True;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

// HidHide's installer (Advanced Installer) can't run silently: in silent mode it can't ask Windows for administrator
// rights, and its updater step can show a message box that would wait forever with nobody to click it. So it runs
// with its normal screens, the user clicks through, and we check afterwards that it really installed.
procedure InstallHidHide;
var
  ResultCode: Integer;
  Answer: Integer;
begin
  WizardForm.StatusLabel.Caption := 'Waiting for HidHide''s installer - click through its window to finish...';
  SuppressibleMsgBox('HidHide''s installer will open now.' + #13#10 + #13#10 +
    '1. Click Yes when Windows asks for permission.' + #13#10 +
    '2. Click through its screens (Next, accept, Install, Finish).' + #13#10 +
    '3. If a small "Nefarius'' Updater" message appears, click Close - HidHide still installs.' + #13#10 + #13#10 +
    'If it asks to restart, choose to restart later: this setup offers a restart at the end.',
    mbInformation, MB_OK, IDOK);
  repeat
    if not ShellExec('', ExpandConstant('{tmp}\hidhide-setup.exe'), '', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
      SuppressibleMsgBox('Could not start HidHide''s installer: ' + SysErrorMessage(ResultCode), mbError, MB_OK, IDOK);
    if HidHideInstalled then
    begin
      HidHideRan := True;
      Exit;
    end;
    Answer := SuppressibleMsgBox('HidHide doesn''t look installed yet.' + #13#10 + #13#10 +
      'If its installer window is still open, finish it first, then click Retry to check again.' + #13#10 +
      'Click Cancel to carry on without HidHide (you can run this setup again later).',
      mbError, MB_RETRYCANCEL, IDCANCEL);
    // Retry: check again first (the installer may have finished in an elevated copy of itself).
    if (Answer = IDRETRY) and HidHideInstalled then
    begin
      HidHideRan := True;
      Exit;
    end;
  until Answer <> IDRETRY;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and ViiperDownloaded then
  begin
    WizardForm.StatusLabel.Caption := 'Installing VIIPER (click Yes when Windows asks for permission for the usbip-win2 driver)...';
    // VIIPER's own unattended installer: per-user copy, autostart, Start menu entry, and usbip-win2 via its signed installer.
    // Started with "runas" so Windows shows its permission prompt: VIIPER refuses to install the driver without admin rights.
    // --no-update keeps the version this app was tested with. Exit code 3010 = installed, restart needed.
    if ShellExec('runas', ExpandConstant('{tmp}\viiper-setup.exe'), 'install --quiet --no-update', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      ViiperRan := True;
      if not ViiperInstalled then
        SuppressibleMsgBox('VIIPER didn''t finish installing (code ' + IntToStr(ResultCode) + ').' + #13#10 +
          'If you clicked No on the permission prompt, run this setup again and click Yes.' + #13#10 +
          'Or install it yourself: winget install Alia5.VIIPER', mbError, MB_OK, IDOK)
      else if (ResultCode <> 0) and (ResultCode <> 3010) then
        SuppressibleMsgBox('VIIPER''s installer finished with code ' + IntToStr(ResultCode) + '.' + #13#10 +
          'If the virtual controller doesn''t turn green after restarting, install VIIPER manually: winget install Alia5.VIIPER', mbInformation, MB_OK, IDOK);
    end
    else
      SuppressibleMsgBox('VIIPER''s installer didn''t run: ' + SysErrorMessage(ResultCode) + #13#10 +
        '(Clicking No on the Windows permission prompt causes this.) Run this setup again, or: winget install Alia5.VIIPER', mbError, MB_OK, IDOK);
  end;

  if (CurStep = ssPostInstall) and HidHideDownloaded then
    InstallHidHide;
end;

// usbip-win2 can need a restart before Windows attaches virtual devices; offer one (the user can choose later).
function NeedRestart(): Boolean;
begin
  Result := ViiperRan or HidHideRan;
end;

// Profiles, macros and settings live outside the install folder so updates never touch them.
// On uninstall, ask whether to remove them too (default: keep).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep = usUninstall then
    // "Start with Windows" entry the app may have added (per-user, HKCU).
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Dual Precision');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DualSense Precision'); // name before 0.19

  if CurUninstallStep = usPostUninstall then
  begin
    Data := ExpandConstant('{#DataDir}');
    if (not UninstallSilent) and DirExists(Data) then
      if MsgBox('Also delete your Dual Precision profiles, macros and settings?' + #13#10 + #13#10 +
                'Choose No to keep them for a future reinstall.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(Data, True, True, True);
  end;
end;
