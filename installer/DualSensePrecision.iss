; DualSense Precision installer (Inno Setup 6).
; Don't run this directly - double-click build-installer.cmd in the project folder, which publishes the app first
; and passes the version in. The app itself installs per-user with no administrator rights.
; Optionally (a ticked task, only shown when VIIPER is missing) it downloads VIIPER from its official GitHub
; release, verifies the file's SHA-256, and runs VIIPER's own unattended installer. That installer adds the signed
; usbip-win2 driver, which Windows asks administrator permission for - that prompt is the user's explicit consent.

#define AppName "DualSense Precision"
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
OutputBaseFilename=DualSensePrecision-{#AppVersion}-setup
SetupIconFile=..\src\DualSensePrecision.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Closes a running copy before installing/uninstalling (same name the app uses for its single-instance lock).
AppMutex=DualSensePrecision.SingleInstance
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "viiper"; Description: "Download and install VIIPER {#ViiperVersion} (about 13 MB from GitHub) - needed for the virtual Xbox controller. It installs the signed usbip-win2 driver: Windows will ask for administrator permission, and a restart may be needed."; GroupDescription: "Virtual controller:"; Check: not ViiperInstalled
Name: "hidhide"; Description: "Download and install HidHide {#HidHideVersion} (about 8 MB from GitHub) - lets the app hide your real controller from games so they only see the virtual one (you switch this on in the app). It installs the signed HidHide driver: Windows will ask for administrator permission, and a restart is needed."; GroupDescription: "Virtual controller:"; Check: not HidHideInstalled

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

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
            'Continue installing DualSense Precision without the optional components? You can run this setup again later to add them.',
            mbError, MB_YESNO, IDYES) = IDYES
        else
          Result := True;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and ViiperDownloaded then
  begin
    WizardForm.StatusLabel.Caption := 'Installing VIIPER (Windows will ask for permission to install the usbip-win2 driver)...';
    // VIIPER's own unattended installer: per-user copy, autostart, Start menu entry, and usbip-win2 via its signed installer.
    // --no-update keeps the version this app was tested with.
    if Exec(ExpandConstant('{tmp}\viiper-setup.exe'), 'install --quiet --no-update', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      ViiperRan := True;
      if ResultCode <> 0 then
        SuppressibleMsgBox('VIIPER''s installer finished with code ' + IntToStr(ResultCode) + '.' + #13#10 +
          'If the virtual controller doesn''t turn green later, install VIIPER manually: winget install Alia5.VIIPER', mbInformation, MB_OK, IDOK);
    end
    else
      SuppressibleMsgBox('Could not start VIIPER''s installer: ' + SysErrorMessage(ResultCode) + #13#10 +
        'You can install it later with: winget install Alia5.VIIPER', mbError, MB_OK, IDOK);
  end;

  if (CurStep = ssPostInstall) and HidHideDownloaded then
  begin
    WizardForm.StatusLabel.Caption := 'Installing HidHide (Windows will ask for permission to install the driver)...';
    // HidHide's own signed installer, unattended. It asks Windows for administrator permission itself.
    if ShellExec('', ExpandConstant('{tmp}\hidhide-setup.exe'), '/quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    begin
      HidHideRan := True;
      if (ResultCode <> 0) and (ResultCode <> 3010) then
        SuppressibleMsgBox('HidHide''s installer finished with code ' + IntToStr(ResultCode) + '.' + #13#10 +
          'If hiding doesn''t work later, install HidHide from github.com/nefarius/HidHide/releases.', mbInformation, MB_OK, IDOK);
    end
    else
      SuppressibleMsgBox('Could not start HidHide''s installer: ' + SysErrorMessage(ResultCode), mbError, MB_OK, IDOK);
  end;
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
  if CurUninstallStep = usPostUninstall then
  begin
    Data := ExpandConstant('{#DataDir}');
    if (not UninstallSilent) and DirExists(Data) then
      if MsgBox('Also delete your DualSense Precision profiles, macros and settings?' + #13#10 + #13#10 +
                'Choose No to keep them for a future reinstall.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(Data, True, True, True);
  end;
end;
