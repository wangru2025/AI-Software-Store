#ifndef MyAppVersion
#define MyAppVersion "1.0.2"
#endif

#define MyAppName "AI 软件商店"
#define MyAppExeName "AI软件商店.exe"
#define MyAppPublisher "AI 软件商店"
#define MyAppURL "https://github.com/wangru2025/AI-Software-Store"
#define MySourceDir "..\release"

[Setup]
AppId={{B71FA3A5-B1FD-45D6-AC6E-2DA9A9837C92}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\AI软件商店
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=AI软件商店-{#MyAppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x86 x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter=AI软件商店.exe,AI软件商店.Updater.exe
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装程序
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: unchecked

[Dirs]
Name: "{commonappdata}\AI软件商店"; Permissions: users-modify
Name: "{commonappdata}\AI软件商店\Logs"; Permissions: users-modify
Name: "{commonappdata}\AI软件商店\Packages"; Permissions: users-modify

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet472Installed(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 461808);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNet472Installed() then
  begin
    MsgBox('AI 软件商店需要 .NET Framework 4.7.2 或更高版本。请先安装 .NET Framework 后再运行安装程序。', mbError, MB_OK);
    Result := False;
  end;
end;
