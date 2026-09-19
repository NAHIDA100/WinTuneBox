; ═══ WinTuneBox / Windows 优化工具箱 安装脚本 ═══
; 需要: Inno Setup 6 + Languages\ChineseSimplified.isl（本机已就绪）
;
; 所有差异都通过 ISCC 命令行 /D 覆盖，默认值 = v1 行为（原样可复现）。
;   /DAppName=...           产品名（默认 "Windows 优化工具箱"）
;   /DAppVer=2.0.0          版本号
;   /DExeName=WinTuneBox.exe 主程序文件名
;   /DAppIdV2               定义后切换到 v2 的 AppId 与默认安装目录（可与 v1 并存、互不覆盖）
;   /DSrcDir=dist\x64       待打包 exe 所在目录
;   /DOutDir=dist           安装包输出目录
;   /DArchSuffix=-x64       追加到安装包文件名末尾
;   /DArchX64               限定只能在 x64 Windows 上安装
;   /DArchX86               限定为 32 位版本（x64 系统亦可安装）
;   /DKeepUserData          卸载时保留 %LOCALAPPDATA%\WinTuneBox（备份/快照/日志）
;   /DAppPublisher=...      发布者（写入"应用和功能"列表，默认 "WinTuneBox 开源项目"）
;   /DAppCopyright=...      版权声明（写入安装包版本资源，默认见下）

#ifndef AppName
  #define AppName "Windows 优化工具箱"
#endif
#ifndef AppVer
  #define AppVer "1.0.15"
#endif
#ifndef ExeName
  #define ExeName "WinOptimizer.exe"
#endif
#ifndef SrcDir
  #define SrcDir "dist"
#endif
#ifndef OutDir
  #define OutDir "dist"
#endif
#ifndef ArchSuffix
  #define ArchSuffix ""
#endif
#ifndef AppPublisher
  #define AppPublisher "WinTuneBox 开源项目"
#endif
#ifndef AppCopyright
  #define AppCopyright "Copyright (c) 2026 WinTuneBox contributors (bilibili @ナヒーダNAHIDA)"
#endif

[Setup]
#ifdef AppIdV2
AppId={{7C4E3B21-5A9D-4E62-8F31-2D6B9A0C4E77}
DefaultDirName={localappdata}\Programs\WinTuneBox
#else
AppId={{B4F9A1C6-7D2E-4F1A-9C3B-8E5D2A0F6B14}
DefaultDirName={localappdata}\Programs\{#AppName}
#endif
AppName={#AppName}
AppVersion={#AppVer}
AppVerName={#AppName} {#AppVer}
AppPublisher={#AppPublisher}
AppCopyright={#AppCopyright}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutDir}
OutputBaseFilename=Setup-Windows优化工具箱-{#AppVer}{#ArchSuffix}
SetupIconFile=assets\app.ico
UninstallDisplayIcon={app}\{#ExeName}
LicenseFile=disclaimer.txt
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVer}
VersionInfoDescription={#AppName} 安装程序
VersionInfoCompany={#AppPublisher}
VersionInfoCopyright={#AppCopyright}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVer}
#ifdef ArchX64
ArchitecturesAllowed=x64compatible
#endif
#ifdef ArchX86
ArchitecturesAllowed=x86compatible
#endif

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Files]
Source: "{#SrcDir}\{#ExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; 安装版：安装即同意免责声明 → 写已同意标记，主程序首启不再弹
Root: HKCU; Subkey: "Software\WinTuneBox"; ValueType: dword; ValueName: "DisclaimerShown"; ValueData: "1"; Flags: uninsdeletevalue

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "立即运行 {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
#ifndef KeepUserData
; 备份目录（服务原值/启动项备份/hosts 备份/还原记录）
Type: filesandordirs; Name: "{localappdata}\WinTuneBox"
#endif

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