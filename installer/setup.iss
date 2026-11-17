; WoW Stick Bridge - installer script (Inno Setup 6)
#define AppName "WoW Stick Bridge"
#define AppVersion "1.7"
#define AppPublisher "WoW Stick Bridge"
#define AppExe "WoWStickBridge.exe"
#define DriverExe "ViGEmBus_1.22.0_x64_x86_arm64.exe"

[Setup]
AppId={{9B6C4B7E-2F3A-4E71-9C55-D2B1A8F04A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppComments=Virtual gamepad stick from keyboard keys for WoW Forever
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=WoWStickBridge-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
CloseApplications=no
SetupLogging=yes
AllowNoIcons=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
DesktopIconDesc=Create a desktop shortcut
ru.DesktopIconDesc=Создать ярлык на рабочем столе
AutoStartDesc=Start WoW Stick Bridge with Windows (hidden, no console window)
ru.AutoStartDesc=Запускать WoW Stick Bridge при старте Windows (скрыто, без окна консоли)
RunAppDesc=Launch WoW Stick Bridge now
ru.RunAppDesc=Запустить WoW Stick Bridge сейчас
DriverStatus=Installing ViGEmBus driver (needed for the virtual gamepad)...
ru.DriverStatus=Установка драйвера ViGEmBus (нужен для виртуального геймпада)...
DriverFailed=ViGEmBus driver was not installed automatically. Install it manually from https://github.com/nefarius/ViGEmBus/releases (ViGEmBus 1.22.0) and run WoW Stick Bridge again.
ru.DriverFailed=Драйвер ViGEmBus не удалось установить автоматически. Установи вручную: https://github.com/nefarius/ViGEmBus/releases (ViGEmBus 1.22.0), затем запусти WoW Stick Bridge.
DriverOk=ViGEmBus driver is installed.
ru.DriverOk=Драйвер ViGEmBus установлен.
NoNetFx=Microsoft .NET Framework 4.8 was not found. WoW Stick Bridge needs it (Windows 10/11 usually has it). Continue anyway?
ru.NoNetFx=Не найден Microsoft .NET Framework 4.8. WoW Stick Bridge требует его (в Windows 10/11 он обычно уже есть). Продолжить?

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconDesc}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "{cm:AutoStartDesc}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "vendor\{#DriverExe}"; DestDir: "{tmp}"; Flags: dontcopy

[Icons]
Name: "{group}\WoW Stick Bridge"; Filename: "{app}\run.cmd"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"
Name: "{group}\WoW Stick Bridge (hidden)"; Filename: "{app}\run-hidden.vbs"; WorkingDir: "{app}"
Name: "{autodesktop}\WoW Stick Bridge"; Filename: "{app}\run.cmd"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{commonstartup}\WoW Stick Bridge"; Filename: "{app}\run-hidden.vbs"; WorkingDir: "{app}"; Tasks: autostart

[Run]
Filename: "{app}\run.cmd"; Description: "{cm:RunAppDesc}"; Flags: postinstall nowait skipifsilent runascurrentuser

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /IM WoWStickBridge.exe /F >nul 2>&1"; Flags: runhidden; RunOnceId: "StopApp"

[UninstallDelete]
Type: files; Name: "{app}\bridge.log"

[Code]
function IsViGEmBusInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus')
    or FileExists(ExpandConstant('{sys}\drivers\ViGEmBus.sys'));
end;

procedure KillRunningApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/c taskkill /IM WoWStickBridge.exe /F >nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure InstallViGEmBusIfNeeded();
var
  ResultCode: Integer;
  DriverPath: String;
begin
  if IsViGEmBusInstalled() then
  begin
    Log('ViGEmBus driver already installed.');
    exit;
  end;
  Log('Installing ViGEmBus driver...');
  ExtractTemporaryFile('{#DriverExe}');
  DriverPath := ExpandConstant('{tmp}\{#DriverExe}');
  if not Exec(DriverPath, '/exenoui /qn /norestart', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode) then
  begin
    Log('Could not start ViGEmBus installer: ' + IntToStr(ResultCode));
  end;
  Sleep(1500);
  if IsViGEmBusInstalled() then
    Log('ViGEmBus driver installed OK.')
  else
  begin
    Log('ViGEmBus driver installation failed, code=' + IntToStr(ResultCode));
    MsgBox(ExpandConstant('{cm:DriverFailed}'), mbError, MB_OK);
  end;
end;

function HasDotNet48(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
    'Release', Release) and (Release >= 528040);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not HasDotNet48() then
  begin
    if MsgBox(ExpandConstant('{cm:NoNetFx}'), mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    KillRunningApp();
  if CurStep = ssPostInstall then
    InstallViGEmBusIfNeeded();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    KillRunningApp();
end;