; ProtoLink Communicator (Windows) Inno Setup script
; Builds artifacts\ProtoLink.Communicator.Windows-Setup.exe from artifacts\publish
;
; Quiet install from command line:
;   ProtoLink.Communicator.Windows-Setup.exe /VERYSILENT /NORESTART /SUPPRESSMSGBOXES
; Optional:
;   /DIR="C:\Path\To\Install"
; Quiet uninstall:
;   uninstall.exe /VERYSILENT /NORESTART /SUPPRESSMSGBOXES

#define MyAppName "ProtoLink Communicator"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "ProtoLink"
#define MyAppExeName "ProtoLink.Communicator.Windows.exe"
#define MyAppId "{{B8D4F0A2-5C3E-4F9B-8D2A-7E6F9B3C4D5E}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=ProtoLink.Communicator.Windows-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
SetupLogging=yes
CloseApplications=force
RestartApplications=no
AllowNoIcons=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
var
  RemoveUserData: Boolean;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  { Silent uninstall: keep user data (no interactive prompt). }
  if UninstallSilent then
    RemoveUserData := False
  else
    RemoveUserData :=
      MsgBox('Also remove ProtoLink Communicator settings and sync metadata from this user profile?',
        mbConfirmation, MB_YESNO) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  LocalAppDataPath: string;
begin
  if (CurUninstallStep = usPostUninstall) and RemoveUserData then
  begin
    LocalAppDataPath := ExpandConstant('{localappdata}\ProtoLinkCommunicator');
    if DirExists(LocalAppDataPath) then
      DelTree(LocalAppDataPath, True, True, True);
  end;
end;
