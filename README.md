# Windows 优化工具箱（WinTuneBox）

> 一个界面简洁、功能全面、**支持 Windows 7 x86 到 Windows 11 x64** 的开源系统优化工具箱。

单文件绿色版（.NET Framework 4.0+，Win7 需要装 .NET 4，Win8/8.1/10/11 自带）。无任何推广、无联网收集、不锁定系统 —— **所有操作前自动备份，全部可撤销**。

---

## ✨ 功能总览

| 页面 | 功能 |
|---|---|
| **概览** | 系统信息（版本/CPU/内存/显卡/磁盘/开机时长）、CPU·内存·磁盘实时占用条、一键跳转 |
| **一键优化** | 12+ 项检测式开关，按系统版本自动筛选（Win7 不显示 Win11 项） |
| **垃圾清理** | 13 类清理目标：临时文件、预取、缩略图、更新缓存、浏览器缓存、崩溃转储、事件日志、回收站、注册表使用痕迹、**无效快捷方式扫描** |
| **启动项管理** | 注册表(32/64 视图) + 启动文件夹；Win8+ 与任务管理器共用 StartupApproved 禁用标记，Win7 自动用兼容方案 |
| **服务优化** | 18 条安全建议（遥测/游戏/Xbox/家庭组/搜索等），一键应用 + **改动记录可整体撤销** |
| **网络工具** | 接口/DNS 一览、公共 DNS 一键切换与还原、hosts 编辑器（含微软遥测域名一键拦截/移除）、Winsock/TCP-IP 重置 |
| **软件管理** | 已安装软件(32/64/用户级)卸载、打开安装目录、导出列表 |
| **更多工具** | SFC/DISM 修复（实时日志可中止）、磁盘检查、系统还原点、电源计划（含隐藏的卓越性能）、安全模式重启、内存整理、桌面图标、12 个常用系统工具入口 |

**一键优化项示例**（均可一键还原）：

- 隐私类：关闭遥测诊断、内容推荐/广告、搜索网页建议（Win10/11）
- Win11 精简：小组件、聊天图标、开始菜单推荐
- 性能类：视觉动画（立即生效）、后台应用、游戏录制 Game DVR、菜单延时、休眠文件
- 安全类：UAC 提权免弹窗（默认不勾选，风险已标注）

---

## 🖥 系统支持

| 系统 | 支持 | 说明 |
|---|---|---|
| Windows 7 SP1 | ✅ x86 / x64 | 需安装 .NET Framework 4.x（安装器会检测提示） |
| Windows 8 / 8.1 | ✅ | 自带 .NET 4.5+ |
| Windows 10 (10240+) | ✅ | |
| Windows 11 (22000+) | ✅ | 含 24H2/25H2 |

- 程序主体以 64 位运行，注册表 32/64 位视图分开读写，双视图启动项均能管理
- 不使用任何仅 Win10+ 的 API；CPU 占用用 `GetSystemTimes`、内存用 `GlobalMemoryStatusEx`，全部为 Win7 可用接口
- 高 DPI 下界面自动缩放（系统 DPI 感知）

---

## 🛡 安全设计

- **操作前自动创建系统还原点**（可选开关，系统保护开启时）
- 服务改动记录原值到 `%LOCALAPPDATA%\WinTuneBox\backups\service-undo.txt`，可一键还原
- 启动项删除前导出 `.reg` 或复制原文件到备份目录
- hosts 修改前自动备份；遥测拦截用带注释标记的段落，可整体移除
- 涉及 HKLM/服务/系统文件的页面全部提示先提权；工具内一键以管理员身份重启
- 清理时被占用/无权限的文件自动安全跳过，不报错中断

---

## 🚀 使用

1. 双击 `WinOptimizer.exe`（绿色版）或安装 `Setup-*.exe`。
2. 首次运行建议点击左下角权限条**以管理员身份重启**（弹 UAC 确认）。
3. 建议顺序：`一键优化` → `服务优化`（点“按建议调整”）→ `垃圾清理` → 按需用其他页。
4. 想还原：各页都有还原按钮；备份目录在左下角可一键打开。

## 🔨 从源码构建

要求：Windows + `.NET Framework 4 SDK 自带编译器`（系统自带 `csc.exe`）、PowerShell 5.1。

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

产出：

- `dist\WinOptimizer.exe` —— 绿色单文件
- `dist\Setup-Windows优化工具箱-<版本>.exe` —— Inno Setup 安装包（需 `D:\App\InnoSetup6`，可改 build.ps1 的 ISCC 路径）
- `dist\selftest.txt` —— 自动运行的自检报告（无 GUI，验证系统识别/优化项检测/清理扫描/服务建议等）

可选参数：`--skip-selftest`、`--no-installer`

## 🚀 GitHub 发布流程

```powershell
powershell -ExecutionPolicy Bypass -File make-release.ps1
```

一键产出发行文件到 `release\`（不入库）：

- `WinTuneBox-v1.0.15-绿色版.zip` —— 绿色版压缩包（exe + README + LICENSE + 免责声明）
- `WinTuneBox-v1.0.15-安装版.exe` —— 中文安装程序
- `SHA256SUMS.txt` —— 校验和

然后在 GitHub Releases 创建 Tag（如 `v1.0.15`），上传上述 3 个文件并粘贴 CHANGELOG 对应条目即可。

## 📂 目录结构

```
WinOptimizer/
├─ src/               C# 源码（csc 直接编译，无第三方依赖）
│  ├─ Controls.cs     浅色 UI 控件库（圆角卡片 AutoFit/扁平按钮/网格）
│  ├─ Os.cs           系统信息与原生 API（Win7→Win11，显卡智能识别）
│  ├─ FloatBall.cs    加速悬浮球（贴边半透明/全屏自动隐藏）
│  ├─ Cleaner.cs / StartupMgr.cs / ServicesMgr.cs
│  ├─ NetMgr.cs / SoftMgr.cs / OpData.cs / SysTools.cs
│  ├─ Pg*.cs          8 个功能页
│  └─ FrmMain.cs / Program.cs（--selftest/--uitest/--minimized）
├─ assets/            make-icon.ps1（生成多尺寸 DIB 图标）
├─ legacy/            v0.2 旧版原型（保留参考）
├─ installer.iss      Inno Setup 脚本（含免责许可页）
├─ disclaimer.txt     免责声明（安装许可页/绿色版随包）
├─ build.ps1          构建（绿色版 + 安装包 + 自检）
├─ make-release.ps1   发行打包（zip + 校验和）
├─ CHANGELOG.md       更新日志
└─ README.md / LICENSE
```

## ⚠️ 免责声明

- 本工具为**开源个人项目**，仅供学习与个人电脑优化使用；修改系统注册表/服务前请先阅读每项说明。
- 已尽力保证所有操作可还原，但**使用后果由使用者自行承担**，建议先在虚拟机或还原点保护下试用。
- 不对任何系统损坏、数据丢失负责；商业用途请自行评估风险。

## 📄 开源协议

MIT License —— 详见 [LICENSE](LICENSE)。
