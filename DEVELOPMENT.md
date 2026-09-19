# WinTuneBox · 开发者文档

构建、发行、代码签名、CI 与仓库结构的说明。**面向使用者**的说明请看 [README.md](README.md)。

- 本仓库是 **WinTuneBox**（v2.0，主线，WPF），**由上一版 WinOptimizer 重构而来**
- 仓库里同时保留 **v1.0.15 版 WinOptimizer**（`src/`，WinForms，冻结存档）以保证可复现构建与老系统支持
- 源码仓库与发行版目录**完全分离**，产物不落在源码树里（见文末「目录结构」）
- 构建无需 .NET SDK / Visual Studio，只要系统自带的 **MSBuild 4.0**

---
## 🔨 从源码构建

要求：Windows + PowerShell 5.1 + **MSBuild 4.0**（.NET Framework 4.8 自带，**无需 .NET SDK**、无需 Visual Studio）。

```powershell
# v2，默认 x64
powershell -ExecutionPolicy Bypass -File build.ps1

# 双架构 + 安装包
powershell -ExecutionPolicy Bypass -File build.ps1 -Arch both

# 冻结的 v1（csc 流程，产物 dist\WinOptimizer.exe）
powershell -ExecutionPolicy Bypass -File build.ps1 -Target v1

# 构建并覆盖到本机安装副本（点快捷方式启动的就是它）
powershell -ExecutionPolicy Bypass -File deploy.ps1
```

> ⚠️ **`build.ps1` 与 `deploy.ps1` 的区别**：快捷方式指向的是**安装副本** `%LOCALAPPDATA%\Programs\WinTuneBox\WinTuneBox.exe`。
> 只跑 `build.ps1` 只更新 `dist\` 与 `wpf\bin\`，安装副本仍是旧版 —— 表现就是「代码明明改好了，打开界面还是老样子」。
> 改了代码要立刻在本机看到效果，请跑 `deploy.ps1`（构建 → 静默覆盖安装 → 校验哈希一致 → 启动）。

| 参数 | 取值 | 默认 | 说明 |
|---|---|---|---|
| `-Target` | `v1` \| `v2` | `v2` | 构建哪个版本 |
| `-Arch` | `x64` \| `x86` \| `both` | `x64` | v2 目标架构（v1 为 AnyCPU，忽略） |
| `-SkipSelfTest` | 开关 | 关 | 跳过构建后自检 |
| `-NoInstaller` | 开关 | 关 | 不生成 Inno 安装包 |
| `-Sign` | 开关 | 关 | 用本机证书给产物加 Authenticode 签名（见「代码签名」；`make-release.ps1` / `deploy.ps1` 同名参数会透传） |

**源码与发行版完全分离**：本仓库（`D:\HENDGIH\WinTuneBox`）里**不产生任何构建产物**，
所有 exe / 安装包 / 绿色版 zip / 证书都落在**同级**的发行版目录 `D:\HENDGIH\WinTuneBox-Release\`。
路径由 `tools\paths.ps1` 统一解析（唯一真相），可用 `-OutRoot <路径>` 或 `$env:WINTUNEBOX_OUT` 覆盖。

产物：

```
发行版目录\dist\x64\WinTuneBox.exe                   v2 绿色版（x64）
发行版目录\dist\x86\WinTuneBox.exe                   v2 绿色版（x86）
发行版目录\dist\Setup-Windows优化工具箱-2.0.0-x64.exe v2 安装包（含免责许可页）
发行版目录\dist\Setup-Windows优化工具箱-2.0.0-x86.exe
发行版目录\dist\WinOptimizer.exe                     v1 绿色版（仅 -Target v1）
发行版目录\bin\ / obj\                               MSBuild 输出与中间文件
```

> x86 产物在 64 位系统上会通过 `RegistryView.Registry64` 读写系统键，并在 32 位系统上回退到默认视图。

## 🚀 GitHub 发布流程

```powershell
powershell -ExecutionPolicy Bypass -File make-release.ps1            # 默认双架构
powershell -ExecutionPolicy Bypass -File make-release.ps1 -Arch x64
```

加 `-Sign` 即可让绿色版内的 exe 与安装包**边构建边签名**（顺序是「先签主程序 → 再打安装包 → 最后签安装包自身」，
反过来做安装包会内嵌未签名的主程序）：

```powershell
powershell -ExecutionPolicy Bypass -File make-release.ps1 -Sign
```

一键产出到 `release\`（不入库）：

- `WinTuneBox-v2.0.0-x64-绿色版.zip` / `-x86-绿色版.zip` —— exe + README + LICENSE + CHANGELOG + 免责声明
- `Setup-Windows优化工具箱-2.0.0-x64.exe` / `-x86.exe`
- `SHA256SUMS.txt` —— 校验和

然后在 GitHub Releases 创建 Tag（如 `v2.0.0`），上传上述文件并粘贴 CHANGELOG 对应条目即可。

## 🔏 代码签名

仓库自带**零成本**的 Authenticode 签名链路，不依赖 `signtool.exe`（走 `Set-AuthenticodeSignature`），算法 SHA256 + RFC3161 时间戳。

### 先认清能力边界

自签名证书**不等于**受信任的商业证书：

| | 自签名（本仓库自带方案） | 商业 / 免费开源证书 |
|---|---|---|
| 成本 | 0 | SignPath.io（开源项目免费，需申请）/ Certum 开源代码签名证书（免费，需实名 + 硬件令牌）/ Azure Trusted Signing（付费） |
| 签名有效性 + 时间戳 | ✅ | ✅ |
| 装过根证书的机器 | 「已验证的发布者」 | 真实公司名 |
| **没装**根证书的机器 | 「未知发布者」，**SmartScreen 仍会拦截** | SmartScreen 信誉可逐步建立 |

绿色版与 Release 可以附上 `signing\WinTuneBox-RootCA.cer`，让愿意信任的用户手动导入。
但如果目标是**让所有用户的 SmartScreen 都安静**，只有商业证书或 SignPath / Certum 这类免费开源证书做得到 ——
那时把本仓库脚本里的证书换成真实证书即可，流程完全不用改。

### 生成证书（一次即可）

```powershell
powershell -ExecutionPolicy Bypass -File tools\make-cert.ps1 -Publisher "你的名字"
```

会生成**根 CA + 代码签名叶证书**两件套（注意：单张自签名叶证书**不能**用来签代码，会报
`A certificate's basic constraint extension has not been observed`），导出到**发行版目录**的 `signing\`：

- `WinTuneBox-RootCA.cer` —— 公开的根证书，**发给别人导入**
- `WinTuneBox-codesign.pfx` + `pfx.pass` —— 私钥，**绝不能提交或外发**（`signing/` 已在 `.gitignore` 里）
- `cert.json` —— 指纹等元数据

同时装进当前用户的 `Root` + `TrustedPublisher`。加 `-MachineTrust` 才会装到机器级 ——
UAC 与 SmartScreen 跑在 SYSTEM 上下文，需要机器级信任才「认识」你的证书（文件验签用户级就够）。

> 证书默认 5 年有效。**信任绑定的是私钥**：重新生成证书后旧根证书立即作废，用户得重新导入。
> 常用参数：`-Years`、`-Password`、`-MachineTrust`、`-Force`、`-Remove`。

### 换真实证书（SignPath / Certum / 商业证书）

自签名只保证完整性与时间戳，**别人机器上仍然提示「未知发布者」**。要真正消除提示，需要一张受信任的证书。
本仓库的签名代码不用改 —— **换证书只换配置**。

| 路线 | 成本 | 门槛 | 说明 |
|---|---|---|---|
| **SignPath.io** | 免费 | 需申请 + 审核 | 开源项目的免费签名服务，签名在 SignPath 服务器上完成，**不用自己买证书**。门槛最低 |
| **Certum 开源代码签名证书** | 证书免费，**硬件要钱** | 需实名认证 | 证书本身免费，但必须配合符合要求的硬件加密设备（卡片 / 读卡器需自备或购买）。私钥不出硬件 |
| 商业证书（Sectigo / DigiCert 等） | 每年数百到数千元 | 需企业或个体资质 | 普通 OV 证书签名后 SmartScreen 仍要慢慢攒信誉；**EV 证书**才能立刻免除 SmartScreen |
| Azure Trusted Signing | 约 $10/月 | **需 3 年可验证的企业经营历史** | 个人开发者基本申请不下来 |

> 只有一个仓库、没有 GitHub 远端、也不想走 CI 的话，先把证书文件拿到手（`-Pfx` 方式）就能签，见下面第 1 节。

#### 1. 本地用证书签名（三种方式，任选其一）

```powershell
# A. 证书已装进证书库（Certum 装到硬件令牌后就是这种）
powershell -File tools\sign.ps1 -Thumbprint AB12CD34...（40 位指纹）
powershell -File tools\sign.ps1 -Subject "CN=你的名字"          # 支持通配

# B. 拿到 .pfx 文件
powershell -File tools\sign.ps1 -Pfx D:\cert.pfx -PfxPasswordFile D:\pfx.pass

# C. 写进 signing\cert.json —— 一次配置，之后 build.ps1 / make-release.ps1 -Sign 自动沿用
```

`signing\cert.json` 的真实证书写法：

```json
{
  "mode": "store",
  "thumbprint": "AB12CD34...（40 位指纹）"
}
```

```json
{
  "mode": "pfx",
  "pfx": "D:\\certs\\real-cert.pfx",
  "pfxPasswordFile": "D:\\certs\\pfx.pass"
}
```

`mode` 取值：`store`（从证书库找）/ `pfx`（用 pfx 文件）/ 其它或省略 = 原来的自签名行为。
**不要在 `cert.json` 里写明文密码**，用 `pfxPasswordFile` 指向密码文件。

证书解析优先级：命令行参数（`-Thumbprint` / `-Pfx` / `-Subject`）→ `cert.json` → 自签名兜底。
证书与产物的位置由 `tools\paths.ps1` 统一解析，签名脚本同样认 `-OutRoot` / `$env:WINTUNEBOX_OUT`。

装了 `signtool.exe`（Windows SDK）时**自动优先使用它** —— EV 证书和硬件令牌类证书用 signtool 兼容性更好；
没有 signtool 也能签（回退 `Set-AuthenticodeSignature`）。用 `-ForceSigntool` / `-NoSigntool` 可强制指定。

#### 2. SignPath.io 走 CI 签名（推荐给开源项目）

流水线在 `.github/workflows/signpath.yml`。**顺序不可颠倒**：

```
binaries   构建 → SignPath 签 WinTuneBox.exe ×2
installers 用已签名的 exe 打安装包 → SignPath 签 Setup ×2      ← 必须在上一步之后
release    组装绿色版 zip + SHA256SUMS，tag 触发时发 Release
```

> 如果反过来（先打安装包再签主程序），安装包内嵌的就是未签名的 exe，用户装完在属性页看到的还是「未知发布者」。
> 这也是 `build.ps1` 新增 `-InstallerOnly` 的原因 —— 它只打安装包、不重新编译，避免覆盖掉已经签好名的主程序。

接入步骤（这些必须在 GitHub / SignPath 后台手动完成）：

1. 把仓库推送到 GitHub，装好 SignPath 的 GitHub App
2. 在 signpath.io 申请开源项目签名额度并等待审核通过
3. 在 SignPath 后台建 Organization / Project / Signing Policy，并把 `.signpath\binaries.xml` 与
   `.signpath\installers.xml` 注册为**两条 Artifact Configuration**，slug 分别叫
   `wintunebox-binaries` 与 `wintunebox-installers`（SignPath 后台有配置校验器，先校验再保存）
4. 仓库 **Secrets** 加 `SIGNPATH_API_TOKEN`
5. 仓库 **Variables** 加 `SIGNPATH_ORG_ID`、`SIGNPATH_PROJECT_SLUG`、`SIGNPATH_POLICY_SLUG`
6. 打 tag（如 `v2.0.0`）触发，或手动 `workflow_dispatch`

没配置 `SIGNPATH_API_TOKEN` 时整条流水线会**优雅跳过并输出 notice**，不会刷失败通知，也不会影响
`.github/workflows/build.yml` 的日常构建。

> ⚠️ SignPath 是外部服务，其输入参数（policy slug、artifact configuration 的 schema）可能随版本变化。
> 第一次接入后请在 SignPath 后台用它们的配置校验器确认 `.signpath\*.xml` 通过，再正式使用。

#### 3. 证书与私钥的保管

- 私钥 / PFX / 密码文件放在**发行版目录**的 `signing\`（源码树里根本没有这个目录），**绝不入库、绝不外发**
- 真实证书的 `.pfx` 建议另存到仓库之外，`cert.json` 只写路径
- 硬件令牌类证书的私钥不可导出，只能在本机（插着令牌时）签名 —— CI 上会失败，此时请用 SignPath

### 签名 / 验签

```powershell
powershell -ExecutionPolicy Bypass -File tools\sign.ps1              # 签 dist\ 下全部产物
powershell -ExecutionPolicy Bypass -File tools\sign.ps1 -Verify      # 只验签，不改文件
powershell -ExecutionPolicy Bypass -File tools\sign.ps1 -NoTimestamp # 离线时不打时间戳
powershell -ExecutionPolicy Bypass -File tools\sign.ps1 -Path a.exe,b.exe
```

时间戳服务器按 `timestamp.digicert.com` → `timestamp.sectigo.com` 顺序尝试，全失败才降级为无时间戳签名（会警告）。
`-Verify` 输出「文件 / 签名者 / 时间戳 / 状态」表格，看到 `Valid` 即通过；
显示「未知根证书」说明该机器没装根证书，**签名本身没问题**。

### 该签哪些文件

签名必须发生在**压缩绿色版 zip 之前**，以及**安装包编译之后**（打进去的 exe 与安装包自身是两个独立文件）。
`build.ps1 -Sign` / `make-release.ps1 -Sign` / `deploy.ps1 -Sign` 已按
「先签主程序 → 再打安装包 → 最后签安装包自身」串好顺序，一次性覆盖：

```
dist\x64\WinTuneBox.exe / dist\x86\WinTuneBox.exe      主程序（绿色版来源）
dist\Setup-Windows优化工具箱-2.0.0-{x64,x86}.exe        安装包自身
release\WinTuneBox-v2.0.0-*-绿色版.zip 内的 exe          随 zip 一起带签名
%LOCALAPPDATA%\Programs\WinTuneBox\WinTuneBox.exe      安装副本（内嵌主程序已签，无需重签）
```

> 顺序反了（先打安装包再签主程序）会导致安装包内嵌未签名的主程序，装完属性页又变成「未知发布者」。

`build.ps1 -InstallerOnly` 就是为这个顺序服务的：它**只打安装包、不重新编译**，
可以安全地在「主程序已经签好名」之后单独跑，用于 CI 与手工重打包。

安装包版本资源里的 `AppCopyright` / `AppPublisher` 也可以覆盖：

```powershell
powershell -File build.ps1 -AppCopyright "Copyright (c) 2026 你的名字" ...
```

## 🧪 自检与验收

产物支持无界面自检，可用于 CI 与回归：

```powershell
WinTuneBox.exe --selftest              # 全部只读检测（CI 用这个）
WinTuneBox.exe --selftest-write        # 额外做注册表/服务写-读-还原往返验证（需管理员）
WinTuneBox.exe --shot <dir>            # 12 页 × 浅/深 × 1180/640 截图回归
WinTuneBox.exe --memprobe --startup-bench   # 工作集/首帧报告
```

报告落在 `%LOCALAPPDATA%\WinTuneBox\logs\`（`selftest-latest.txt`、`selftest-mem.txt`、`startup-bench.txt`）。
`--shot` 会等待页面后台加载完成再截图，避免拍到「加载中」空帧。

窗口外观（DWM 背板 / Mica / 玻璃帧 / 标题栏）改动请额外跑进程外抓取 —— `--shot` 用离屏渲染并自铺底色，看不到这类合成层缺陷：

```powershell
powershell -ExecutionPolicy Bypass -File tools\livecap.ps1 -Out D:\shots\live.png
powershell -ExecutionPolicy Bypass -File tools\livecap.ps1 -Max 1        # 最大化
powershell -ExecutionPolicy Bypass -File tools\livecap.ps1 -ToggleMica 1 # 运行时开关 Mica
```

采样值出现 `#000000` 即为合成失败（v2.0.0 修过的「浅色标题栏纯黑」就是这一类）。

托盘与退出行为也有回归脚本（会真鼠标点击托盘菜单，不要用 `--shot` 那类离屏手段代替）：

```powershell
powershell -ExecutionPolicy Bypass -File tools\tray-exit-test.ps1
```

它按 PID 找到托盘消息窗口（`WinTuneBoxTraySink`）→ 投递 `WM_TRAY`+`WM_RBUTTONUP` 弹出右键菜单 →
用 UI Automation 定位「退出」项并真鼠标点击 → 断言进程退出。**前提是应用已在运行。**

> 退出相关的坑：`App.xaml` 是 `ShutdownMode="OnExplicitShutdown"`，**关闭窗口不会结束进程**。
> 任何新增的「退出」入口都必须调用 `CleanupAndShutdown()`，只调 `Close()` 会留下后台进程与托盘图标。

## 📂 目录结构

分成两个**完全独立**的文件夹：源码仓库只放能入库的东西，构建产物与私钥一律在发行版目录。

```
D:\HENDGIH\WinTuneBox\              源码仓库（本仓库，可安全 push）
│
├─ wpf/                    ★ v2 主线（WPF / MSBuild 工程）
│  ├─ App/                 生命周期、路径、设置、日志、主题、诊断
│  ├─ Views/               12 个页面（C# 构建 UI + 少量 XAML 壳）
│  ├─ ViewModels/          INotifyPropertyChanged 状态与命令
│  ├─ Services/            优化/清理/服务/网络/存储/安全/维护 等业务服务
│  ├─ Controls/            自绘控件：FluentWindow / Charts / Toast
│  ├─ Native/              P/Invoke：Dwm、ShellNotifyIcon
│  ├─ Themes/              Tokens / Palette.Light / Palette.Dark / Controls / Templates
│  ├─ Properties/          AssemblyInfo（版本号 2.0.0.0）
│  ├─ app.manifest         兼容性声明 + asInvoker
│  └─ WinTuneBox.csproj    输出 WinTuneBox.exe
├─ src/                    v1 存档（WinForms，已冻结，仅保证可复现构建）
├─ legacy/                 v0.2 原型（保留参考）
├─ assets/                 app.ico 与 make-icon.ps1
├─ installer.iss           Inno Setup 脚本（/D 参数化：AppVer / ExeName / Arch）
├─ disclaimer.txt          免责声明（安装许可页 / 绿色版随包）
├─ build.ps1               构建（-Target v1|v2、-Arch x64|x86|both、-SkipSelfTest、-NoInstaller、-Sign、-InstallerOnly）
├─ make-release.ps1        发行打包（绿色版 zip + 安装包 + SHA256SUMS）
├─ deploy.ps1              本机构建 + 覆盖安装副本（-Arch x64|x86、-SkipBuild、-NoLaunch）
├─ tools/paths.ps1          ★ 路径解析唯一真相：源码/发行版分离就靠它
├─ tools/make-cert.ps1      生成自签名根 CA + 代码签名叶证书（零成本方案）
├─ tools/sign.ps1           Authenticode 签名 / 验签（SHA256 + RFC3161；支持自签名与真实证书）
├─ tools/livecap.ps1        进程外窗口抓取，验证 DWM 背板 / Mica / 标题栏（--shot 覆盖不到）
├─ tools/tray-exit-test.ps1 托盘「退出」端到端回归（真鼠标点击 + UI Automation）
├─ .github/workflows/      CI：build.yml 日常构建；signpath.yml 用真实证书签发行版
├─ .signpath/              SignPath.io 制品配置（binaries.xml / installers.xml）
├─ CHANGELOG.md            更新日志
├─ _PROGRESS_v2.md         本地续作笔记（已 gitignore）
└─ README.md / LICENSE

D:\HENDGIH\WinTuneBox-Release\      发行版目录（**不进 git**）
├─ dist\                          编译产物：x64\ / x86\ / Setup-*.exe / v1 exe / 自检报告
├─ release\                       发行包：绿色版 zip + 安装包 + SHA256SUMS + 根证书
├─ signing\                       证书与私钥（cer / pfx / 口令）—— 从源码树搬出来，避免误打包泄漏
├─ bin\  obj\                     MSBuild 编译输出与中间文件
└─ _Backup\_<时间戳>\             迁移前的整仓备份
```

---

## ⚠️ 免责声明

- 本工具为**开源个人项目**，仅供学习与个人电脑优化使用；修改系统注册表/服务前请先阅读每项说明。
- 已尽力保证所有操作可还原，但**使用后果由使用者自行承担**，建议先在虚拟机或系统还原点保护下试用。
- 不对任何系统损坏、数据丢失负责；商业用途请自行评估风险。

## 📄 开源协议

MIT License —— 详见 [LICENSE](LICENSE)。作者：bilibili @ナヒーダNAHIDA
