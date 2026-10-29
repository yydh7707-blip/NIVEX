#define AppName "NIVEX"
#define AppVersion "0.3.1"
#define SourceExe "..\artifacts\publish-fd\NIVEX.Desktop.exe"
#define RuntimeUrl "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{6B1C3E52-7A4F-4C0B-9D1E-2F5A8C7D4E10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=NIVEX
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\NIVEX.exe
OutputDir=..\artifacts\installer
OutputBaseFilename=NIVEX-Setup
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "NIVEX.exe"; Flags: ignoreversion

[Icons]
Name: "{group}\NIVEX"; Filename: "{app}\NIVEX.exe"
Name: "{autodesktop}\NIVEX"; Filename: "{app}\NIVEX.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\NIVEX.exe"; Description: "{cm:LaunchProgram,NIVEX}"; Flags: nowait postinstall skipifsilent

[Code]
var
  RuntimePage: TDownloadWizardPage;

function RuntimeInstalled: Boolean;
var
  Found: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), Found) then
  begin
    Result := True;
    FindClose(Found);
  end;
end;

procedure InitializeWizard;
begin
  RuntimePage := CreateDownloadPage(SetupMessage(msgWizardPreparing), '.NET 8 Desktop Runtime', nil);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Installer: String;
  ResultCode: Integer;
begin
  Result := '';
  if RuntimeInstalled then Exit;
  Installer := ExpandConstant('{tmp}\dotnet-desktop-runtime.exe');
  RuntimePage.Clear;
  RuntimePage.Add('{#RuntimeUrl}', 'dotnet-desktop-runtime.exe', '');
  RuntimePage.Show;
  try
    try
      RuntimePage.Download;
    except
      Result := '.NET 8 Desktop Runtime download failed. Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and run setup again.';
      Exit;
    end;
  finally
    RuntimePage.Hide;
  end;
  if not ShellExec('runas', Installer, '/install /quiet /norestart', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode)
     or ((ResultCode <> 0) and (ResultCode <> 3010)) then
    Result := '.NET 8 Desktop Runtime installation failed (code ' + IntToStr(ResultCode) + ').';
end;