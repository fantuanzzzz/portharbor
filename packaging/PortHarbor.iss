; The test build has its own AppId and Start Menu group. Never smoke-test the shipping identity.
#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifdef TestId
  #define ProductId "PH.Check." + TestId
  #define GroupName "PortHarbor Packaging Check " + TestId
#else
  #define ProductId "PortHarbor"
  #define GroupName "PortHarbor"
#endif

[Setup]
AppId={#ProductId}
AppName=PortHarbor
AppVersion={#AppVersion}
AppPublisher=PortHarbor
DefaultDirName={localappdata}\Programs\PortHarbor
DefaultGroupName={#GroupName}
DisableProgramGroupPage=yes
DisableDirPage=no
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#BuildOutput}
OutputBaseFilename={#OutputName}
WizardStyle=modern
SetupIconFile=..\assets\portharbor.ico
Compression=lzma2/fast
SolidCompression=yes
SetupLogging=yes
UninstallDisplayIcon={app}\PortHarbor.exe
CloseApplications=yes
RestartApplications=no
UninstallLogMode=append
InfoBeforeFile={#Payload}\INSTALL-NOTES.txt

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimp.SeparateFolder=请选择独立的程序目录。此目录包含 PortHarbor 用户数据或便携副本。
english.SeparateFolder=Choose a program folder separate from PortHarbor user data and portable copies.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#Payload}\PortHarbor.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\PortHarbor.installed"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\INSTALL-NOTES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\PortHarbor"; Filename: "{app}\PortHarbor.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\PortHarbor"; Filename: "{app}\PortHarbor.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\PortHarbor.exe"; Description: "{cm:LaunchProgram,PortHarbor}"; Flags: nowait postinstall skipifsilent

[Code]
function IsSameOrChild(Path, Parent: String): Boolean;
begin
  Path := AddBackslash(ExpandFileName(Path));
  Parent := AddBackslash(ExpandFileName(Parent));
  Result := CompareText(Copy(Path, 1, Length(Parent)), Parent) = 0;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Target: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then begin
    Target := WizardDirValue;
    if IsSameOrChild(Target, ExpandConstant('{localappdata}\PortHarbor')) or
       FileExists(AddBackslash(Target) + 'PortHarbor.portable') or
       FileExists(AddBackslash(Target) + 'PortHarbor.ini') then begin
      MsgBox(CustomMessage('SeparateFolder'), mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  { This check also applies to /VERYSILENT installs, where wpSelectDir is skipped. }
  if IsSameOrChild(WizardDirValue, ExpandConstant('{localappdata}\PortHarbor')) or
     FileExists(AddBackslash(WizardDirValue) + 'PortHarbor.portable') or
     FileExists(AddBackslash(WizardDirValue) + 'PortHarbor.ini') then
    Result := CustomMessage('SeparateFolder');
end;
