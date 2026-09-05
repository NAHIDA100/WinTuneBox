; ═══ Windows 优化工具箱 安装脚本 ═══
; 需要: Inno Setup 6 + Languages\ChineseSimplified.isl（本机已就绪）
#define AppName "Windows 优化工具箱"
#define AppVer "1.0.1"
#define ExeName "WinOptimizer.exe"

[Setup]
AppId={{B4F9A1C6-7D2E-4F1A-9C3B-8E5D2A0F6B14}
AppName={#AppName}
AppVersion={#AppVer}
AppVerName={#AppName} {#AppVer}
AppPublisher=WinTuneBox 开源项目
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=Setup-Windows优化工具箱-{#AppVer}
SetupIconFile=assets\app.ico
UninstallDisplayIcon={app}\{#ExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVer}
VersionInfoDescription={#AppName} 安装程序

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Files]
Source: "dist\{#ExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "立即运行 {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 安装目录残留文件（含卸载后生成物）
Type: filesandordirs; Name: "{app}"
; 备份目录（服务原值/启动项备份/hosts 备份/还原记录）
Type: filesandordirs; Name: "{localappdata}\WinTuneBox"

[Code]
// ── .NET Framework 4 Full 检测（Win7 需要安装，Win8+ 自带）──
function IsDotNet4Full(): Boolean;
var
  K: String;
  Release: Cardinal;
begin
  Result := True;
  K := 'Software\Microsoft\NET Framework Setup\NDP\v4\Full';
  if RegQueryDWordValue(HKLM, K, 'Release', Release) then
    Result := Release >= 378389   // 4.5+ 都兼容 4.0
  else if not RegKeyExists(HKLM, K) then
    Result := False;
end;

// ── 是否正在运行（同一会话 Local 命名互斥量）──
function OpenMutexW(dwDesiredAccess: DWORD; bInheritHandle: Boolean; lpName: String): THandle;
  external 'OpenMutexW@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function IsAppRunning(): Boolean;
var
  h: THandle;
begin
  h := OpenMutexW($001F0001, False, 'Local\WinTuneBox_SingleInstance');
  Result := h <> 0;
  if h <> 0 then CloseHandle(h);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if IsAppRunning() then
  begin
    MsgBox('检测到 Windows 优化工具箱正在运行。' + #13#10 +
           '请先关闭程序（托盘/窗口），再重新运行安装程序。',
           mbError, MB_OK);
    Result := False;
    Exit;
  end;
  if not IsDotNet4Full() then
  begin
    if MsgBox('本机未检测到 .NET Framework 4.x（Win7 需手动安装，Win8/10/11 已内置）。' + #13#10 +
              '请先安装 .NET Framework 4.8，可从微软官网下载。' + #13#10#13#10 +
              '仍然继续安装吗？',
              mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
