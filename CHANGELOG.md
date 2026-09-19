# 更新日志

## v2.0.0（2026-09-18）

**首个 WPF 主线版本**：由上一版 **WinOptimizer**（v1.0.15，WinForms）整体重构而来 —— 以 `wpf/` 为新工程重写界面与架构并更名为 **WinTuneBox**，`src/`（旧版）冻结为 legacy 存档。

### 架构
- 目录分层：`App/`（生命周期、路径、设置、日志、主题、诊断）、`Views/`、`ViewModels/`、`Services/`、`Controls/`、`Native/`、`Themes/`
- **彻底移除 HandyControl**：窗口基类改为自写 `FluentWindow`（`WindowChrome` + 自绘最小化/最大化/关闭），通知改为自写 `ToastHost`（右下角堆叠、160ms 淡入、最多 3 条），托盘改为 `Shell_NotifyIcon` 自实现，不再引用 WinForms 程序集
- **统一操作模型**：每个优化项实现同一接口（`Id / Group / Title / Note / NeedsAdmin / Risky / Recommended / Detect() / Apply() / Restore()`），检测结果驱动「已优化 / 待优化 / 部分」徽标
- 程序集更名 `WinTuneBox`（`WinTuneBox.exe`）；数据目录统一到 `%LOCALAPPDATA%\WinTuneBox`（`settings.json` + `backups/` + `snapshots/` + `logs/`），首次启动一次性迁移旧数据并清理 v1 遗留的开机自启项
- 修复单实例互斥体在获取失败时未释放、导致后续再也启动不了的问题

### 界面
- **Mica 分级降级**：Win11 22H2+ 用 Mica，Win11 21H2 纯色 + 暗色标题栏，Win10 纯色直角；不引入亚克力/模糊以保证低占用
- 左侧导航按「优化 / 管理 / 系统」分组，选中态为 Fluent 药丸高亮；窗口宽度 ≤ 900px 自动收成**纯图标栏**（图标居中不裁切，权限条与搜索入口同步收敛）
- 导航底部新增常驻的**「一键整理内存」快捷按钮**（强调色实心，位于「设置」下方、权限条上方）：整理期间按钮禁用并显示「正在整理…」，完成后弹出结果 Toast（整理进程次 / 可用内存变化）；窄窗口自动收成居中图标。为容纳它，导航项行高按 Fluent 标准由 38 → 36px，避免导航出现滚动条
- `Ctrl+K` 命令面板（搜页面与动作）、`Ctrl+Tab` / 方向键切换页面
- 主题：浅色 / 深色 / 跟随系统，7 个强调色预设，切换即时生效并持久化；自绘控件全量覆盖深浅色
- 页头**响应式**：宽度不足时操作区自动折到标题下方并换行，避免标题被挤成竖排
- 动效只用 `Opacity` / `TranslateTransform`（140–180ms 淡入 + 位移），不使用大面积 `DropShadowEffect` / 模糊
- 仪表盘：自绘圆环健康分 + CPU/内存迷你趋势线（`DrawingVisual`，无图表库），**仅在页面可见时** 1s 采样
- 图标统一 `Segoe Fluent Icons`，Win10 回退 `Segoe MDL2 Assets`

### 优化项（23 → 约 52 项）
- **性能与游戏**：暂停搜索索引后台负载、`Win32PrioritySeparation` 两档、硬件加速 GPU 计划（HAGS，需重启）、SysMain 按盘型优化、关闭 NTFS 最后访问时间戳与 8.3 短名、关闭计划碎片整理任务、电源细化（处理器最小状态 / USB 选择性暂停 / PCIe 链路电源管理 / 硬盘空闲超时）、网络低延迟（`NetworkThrottlingIndex`、`SystemResponsiveness`、TCP 自动调优/RSS、按网卡禁用 Nagle）、内存与分页（`DisablePagingExecutive`、`LargeSystemCache`、内存压缩、页面文件自定义）、关闭启动延迟与通知动画
- **清理与存储**：WinSxS 组件存储清理（`/ResetBase` 标为不可撤销 + 二次确认）、磁盘空间可视化、大文件/重复文件扫描、旧驱动包清理、应用缓存清理、日志与转储清理扩展 + 转储策略、还原点占用管理、`$WINDOWS.~BT`/`~WS` 残留、计划任务残留与孤儿卸载项扫描、字体/图标缓存重建、WMI 仓库与性能计数器重建、CompactOS 压缩切换、存储感知配置
- **隐私、安全与维护**：关闭定位与位置历史、活动历史/时间线、输入个性化与云词典、应用权限批量收紧、反馈请求与诊断数据、遥测计划任务、云内容搜索与搜索亮点、OneDrive 自动启动、网络安全加固（SMBv1 / LLMNR / NetBIOS / 远程协助）、自动播放与「最近使用文件」、关闭更新的自动重启
- **只读安全体检页**：Defender / 防火墙 / 受控文件夹 / 系统还原 / UAC / 安全启动 / 遥测 / SMBv1 状态与建议，**永不自动修改**
- 一键优化页增加分组折叠、搜索、状态筛选与「安全推荐 / 游戏 / 办公 / 极简」预设

### 文档
- `README.md` 改为**面向普通用户的使用指南**（不是开发者文档，也不是发布说明）：重排为
  「免责声明 → 它能帮你做什么（按场景）→ 下载与安装 → 第一次打开 → 我该点哪个（对号入座表）→
  界面速览（含托盘与悬浮球）→ 它安全吗 → 常见问题 → 卸载 / 我的数据在哪 → 技术细节 → 开源协议」
  - **全文换成口语**：术语改成大白话（「检测」→「先读一遍当前状态」之类），不再出现 `HKLM` / 服务名 /
    `Detect()/Apply()/Restore()` 这类实现细节，技术内容压缩成末尾的「技术细节」表格
  - **首次上手路径**写进「第一次打开」：看权限条 → 建快照 → 再去一键优化，把「先备份」变成默认动作
  - 「我该点哪个」用**「我想…… / 去哪一页 / 说明」**表格替代按页罗列，普通用户按处境查找即可
  - 明确写出**关窗口不等于退出**（托盘右键 → 退出），以及悬浮球的开关位置 —— 这是最容易踩的坑
  - **免责声明前置**并展开为 4 条（原文照搬 `disclaimer.txt` 的要点），安装/使用前必须先读
  - **技术栈降级为附录**：C# 5 / WPF / .NET Framework 4.8、零第三方 UI 库、零外部 DLL、零联网行为、
    Mica 分级降级、`Shell_NotifyIcon` 自写托盘、自绘图表 —— 全部挪到末尾的「技术细节」表格，
    并注明普通用户可跳过，不再挡在正文前面
  - **实测资源占用**换成真机数据（Win11 25H2：空闲工作集 **8.7 MB**、首帧 **844 ms**、私有提交约 103 MB）
  - 新增**版本沿革**表，写明 **WinTuneBox 由上一版 WinOptimizer（v1.0.15 / WinForms）整体重构**而来，
    重构后更名 WinTuneBox，且自 v2.0.0 起**单独建仓维护**，不再与旧版共用仓库
  - 新增**「界面速览」**（12 页导航分组 + 快捷键 + 托盘/悬浮球专节）与**「我该点哪个」**对照表
  - 新增**「常见问题」**，含**杀毒软件误报**的成因与处理（行为特征命中，非签名问题）与「未知发布者」的解释
- 新增 `DEVELOPMENT.md`：把开发向内容（从源码构建 / GitHub 发布 / 代码签名 / 自检与验收 / 目录结构）从 README 整体迁出，
  README 末尾只留速览与链接。绿色版 zip 随包文档同步加入 `DEVELOPMENT.md`

### 仓库结构：源码与发行版完全分离
- 源码仓库（`D:\HENDGIH\WinTuneBox`）里**不再产生任何构建产物**；所有 exe / 安装包 / 绿色版 zip / 证书
  统一落在**同级**的发行版目录 `D:\HENDGIH\WinTuneBox-Release\`（`dist\` / `release\` / `signing\` / `bin\` / `obj\`）
- 新增 `tools\paths.ps1` 作为**路径解析的唯一真相**（此前 `dist` / `release` / `signing` 在 5 个脚本里各写死一份）：
  解析顺序 `-OutRoot` → `$env:WINTUNEBOX_OUT` → 同级 `<源码目录名>-Release` → 兜底 `_out`，并自动建目录。
  `build.ps1` / `make-release.ps1` / `deploy.ps1` / `tools\sign.ps1` / `tools\make-cert.ps1` 全部改为 dot-source 它
- MSBuild 的 `OutputPath` 与 `BaseIntermediateOutputPath` 也重定向到发行版目录（命令行 `/p:` 覆盖 csproj 属性），
  `wpf\bin\` 与 `wpf\obj\` 不再出现在源码树里
- `installer.iss` 的 `SrcDir` / `OutDir` 改传**绝对路径**（`dist\` 已不在源码树，相对路径会失效）
- **顺带修掉一个隐患**：`deploy.ps1` 里把版本号写死成 `2.0.0-x64.exe`，改版本号就会找不到安装包；
  现在和 `make-release.ps1` 一样从 `build.ps1` 的 `$v2ver` 解析
- **安全收益**：`signing\`（PFX 私钥 + 口令）从源码树搬到发行版目录 —— 此前虽然在 `.gitignore` 里，
  但整仓打 zip 备份或 `git add -f` 都可能把私钥带出去
- `.gitignore` 保留 `dist/ release/ signing/ bin/ obj/ wpf/bin/ wpf/obj/ _Backup/` 作为**安全网**，
  防止有人在仓库里跑旧脚本或手工编译后误提交
- 两个 CI workflow 显式设置 `WINTUNEBOX_OUT: ${{ github.workspace }}`，把产物钉回 checkout 内，避免写到工作区外面

### 修复
- **修复托盘右键「退出」无效**：`App.xaml` 是 `ShutdownMode="OnExplicitShutdown"`，关窗口**永远不会**结束进程，只有显式调用 `Application.Current.Shutdown()` 才行。而托盘的「退出」只调了 `Close()`，于是主窗口关掉了、进程与托盘图标都留在后台 —— 表现就是「点退出没反应」，再点也没用（窗口已关闭）。现改为直接走 `CleanupAndShutdown()`（内部 `Application.Current.Shutdown()`），并给 `CleanupAndShutdown()` 加了防重入标记
  - 已用 `tools\tray-exit-test.ps1` 做**改前/改后对照**实测：旧写法点击「退出」后进程存活（FAIL），修复后 0.5s 内退出（PASS）
- **修复开机自启「没有静默启动」以及启动后**界面一片空白、**字体完全不渲染**（同一个根因）**：`StartHidden()` 把 `Hide()` 放在了 `Loaded` 事件里，而 `Loaded` 是在 `Show()` **内部**（`CreateSourceWindow`）同步触发的，此时 `Show()` 还没收尾 —— `Hide()` 先把 `WS_VISIBLE` 清掉，紧接着被收尾的 `ShowWindow(SW_SHOW)` 重新盖上，于是形成「WPF 认为窗口已隐藏（`IsVisible=false`）、Win32 侧窗口仍然可见（`WS_VISIBLE`）」的分裂状态。WPF 既然认为窗口不可见，就不再走渲染流程，结果是托盘里留下一个**只有 Mica 底色、导航与文字完全不渲染的空窗口**
  - 这就是开机自启时看到的「没静默启动 + UI 渲染失败、字也渲染不出来」：窗口看得见，是因为 Win32 侧还亮着；内容全空，是因为 WPF 侧已经收工
  - 现改为 `Hide()` 在 `Show()` **返回之后**调用；并新增 `Native\NativeWindow.cs`（`IsWindowVisible` / `ShowWindow(SW_HIDE)`）做一次**原生兜底** —— 万一两个状态再次不一致，直接对 HWND 补一次隐藏并在日志里告警
  - 顺带修掉两个连带问题：① 隐藏启动时窗口尚未布局，`ActualWidth` 为 0，`ApplyCompact(ActualWidth < 900)` 会把导航误判成窄窗口模式，现改为显示时按真实宽度决定（`ShowMain()` 里补一次）；② 登录瞬间不再抢焦点（`ShowActivated = false`，显示时恢复）
  - 实测（Win11 25H2 / 26200，`--minimized` 启动）：修复前主窗口 `WS_VISIBLE=True` 且是整块空白深色矩形；修复后 0.5–5.7 s 逐帧采样始终 `WS_VISIBLE=False`，模拟托盘左键后窗口正常显示、文字/图标/自绘图表全部渲染完整；静默启动空闲工作集 **3.4 MB**
### 低占用与修复
- 空闲工作集 **约 11 MB**（目标 < 80 MB），私有提交约 58 MB，托管堆 4–5 MB；首帧 **< 1.2 s**（目标 < 1.5 s）
- 页面首次访问才创建、离开重型页释放数据源、列表统一 `VirtualizingStackPanel` + 容器回收、无常驻轮询计时器
- **修复 WPF 列表文字全部空白**：`StartupEntry` / `SoftInfo` / `AdapterInfo` / `DnsPreset` / `SvcRow` / `SvcSug` / `SvcView` / `SnapshotInfo` 等模型原先用 public **字段**，而 WPF 绑定无法绑定字段 → 改为属性，启动项/软件/网络/服务/快照页恢复正常显示
- 修复主题切换瞬间的 `ResourceReferenceKeyNotFoundException`、只读属性的 `Run.Text` 双向绑定报错、标题栏按钮字形渲染成方框等问题
- **修复浅色模式「上黑下白」标题条带**：启用 Mica 时 `DwmSetWindowAttribute(SYSTEMBACKDROP_TYPE)` 虽返回成功，但 `WindowChrome.GlassFrameThickness = 0` 未扩展 DWM 玻璃帧，背板实际不绘制，未覆盖区域被填充为不透明黑；调色板中 `TitleBarBrush`（透明）、`NavBackgroundBrush`（`#CCF7F8FB`）、`RegionBrush`（`#E8FFFFFF`）等半透明笔刷与 `Window.Background = Transparent` 全部叠在黑色之上，合成出标题栏纯黑、导航与卡片灰化的错色。现在启用 Mica 时把玻璃帧扩展到整窗（`GlassFrameThickness = -1`，等价 `DwmExtendFrameIntoClientArea(-1)`），并让 WPF 按逐像素 Alpha 合成，Mica 才真正可见；关闭 Mica 或系统不支持时还原为 `0` 并回退到不透明 `WindowBackgroundBrush`，不会再出现黑色底衬
- 上述路径经「进程外整屏抓取 + 像素采样」实测验证：浅色 `#F3F3F3` / 深色 `#1A2229`（Mica 生效）、关闭后 `#F3F4F8`（纯色回退），浅色/深色/最大化/运行时切换开关四种场景均无黑边

### 构建与发行
- `build.ps1` 参数化：`-Target v1|v2`、`-Arch x64|x86|both`、`-SkipSelfTest`、`-NoInstaller`；v2 走 MSBuild 4.0 + .NET Framework 4.8（**无 .NET SDK 依赖**），v1 保留原 csc 流程
- `installer.iss` 全面 `/D` 参数化（`AppVer` / `ExeName` / `ArchSuffix` / `SrcDir` / `AppIdV2` …），每个架构独立安装包，保留免责声明许可页
- `deploy.ps1`：本机构建 → 静默覆盖安装副本 → 校验 `dist` 与安装目录哈希一致 → 启动。避免「只跑 `build.ps1` 导致快捷方式仍启动旧版」的排查陷阱
- `tools/livecap.ps1`：进程外窗口抓取（置顶 + 整屏抓取 + 关键位置像素采样）。`--shot` 走离屏渲染并自铺底色，看不到 DWM 背板 / Mica / 玻璃帧 / 标题栏这类合成层缺陷，窗口外观改动必须用它复验
- `make-release.ps1` 产出 `WinTuneBox-v2.0.0-{x64,x86}-绿色版.zip`、`Setup-Windows优化工具箱-2.0.0-{x64,x86}.exe`、`SHA256SUMS.txt`
- 新增 `.github/workflows/build.yml`：在 `windows-latest` 上构建 x64 与 x86、跑只读自检并上传产物

### 代码签名
- 新增 `tools/make-cert.ps1`：一键生成**自签名根 CA + 代码签名叶证书**（默认 5 年），导出根证书 / PFX / 密码 / 指纹元数据到 `signing\`，并装入当前用户 `Root` + `TrustedPublisher`
  - 踩坑记录：`New-SelfSignedCertificate -Type CodeSigningCert` 的证书 `CA=false`，**不能**用于签名（报 `A certificate's basic constraint extension has not been observed`）；改成 `CA=true` 同样不行（CA 不能当叶证书）。**唯一可用配方是两根证书的链**：根 CA（`CA=true&pathlength=1`）签发叶证书（`-Signer $root`，EKU `1.3.6.1.5.5.7.3.3`）
  - 文件验签只需 **CurrentUser** 级信任即可返回 `Valid`（无需管理员）；只有 UAC / SmartScreen 这类跑在 SYSTEM 上下文的场景才需要机器级信任（`-MachineTrust`）
- 新增 `tools/sign.ps1`：不依赖 `signtool.exe`（走 `Set-AuthenticodeSignature`）对产物做 Authenticode 签名（SHA256 + RFC3161 时间戳），支持 `-Path` / `-ListFile` / `-NoTimestamp` / `-Verify`；时间戳服务器按 DigiCert → Sectigo 顺序回退，全失败才降级为无时间戳并告警
- `build.ps1` / `make-release.ps1` / `deploy.ps1` 新增 `-Sign` 透传，并固定签名顺序为「先签主程序 → 跑自检 → 打安装包（内嵌已签名主程序）→ 最后签安装包自身」；顺序反了会导致装完又变回「未知发布者」
  - 脚本间调用改用 `-ListFile` 传路径清单：`powershell.exe -File` 传数组只会绑定第一个元素；清单以**无 BOM** UTF-8 写出（带 BOM 时首条路径会多出 `U+FEFF` 前缀而找不到文件），`sign.ps1` 侧也做了 BOM 兜底剥离
- 已在 x64 全链路验证：`dist\x64|x86\WinTuneBox.exe`、两个 v2 安装包、v1 安装包与 `WinOptimizer.exe` 共 6 个文件，外加两个绿色版 zip 内嵌 exe，`Get-AuthenticodeSignature` 全部 `Valid`，签名者 `CN=WinTuneBox (NAHIDA100)`，时间戳 `DigiCert SHA256 RSA4096 Timestamp Responder 2026`
- 能力边界（已在 README 说明）：自签名可保证**完整性与时间戳**，但不能免除他人机器上的 SmartScreen 警告；要做到无警告需 SignPath.io（开源免费）/ Certum 开源证书（免费，实名 + 硬件令牌）/ 商业证书
- `.gitignore` 新增 `signing/`，私钥与 PFX 绝不入库

### 真实证书接入（SignPath / Certum / 商业证书）
- `tools\sign.ps1` 重构为**证书来源可插拔**，自签名与真实证书走同一套脚本，换证书只改配置、不改代码：
  - 新增 `-Thumbprint` / `-Subject`（支持通配）/ `-Pfx` / `-PfxPasswordFile`，以及 `-ForceSigntool` / `-NoSigntool`
  - `signing\cert.json` 支持 `mode`（`store` = 从证书库找 / `pfx` = 用 pfx 文件）+ `thumbprint` / `pfx` / `pfxPasswordFile`，配置一次后 `build.ps1 -Sign`、`make-release.ps1 -Sign` 自动沿用
  - 解析优先级：命令行参数 → `cert.json` → 自签名兜底（`leafThumbprint` → `WinTuneBox-codesign.pfx` → 证书库里主题含 WinTuneBox 的证书）
  - 装了 `signtool.exe`（Windows SDK）时**自动优先使用**（`/fd sha256 /sha1 <指纹> /tr <TSA> /td sha256`），EV 与硬件令牌类证书兼容性更好；没有 signtool 时回退 `Set-AuthenticodeSignature`，signtool 失败也会自动回退
  - 输出新增「类型 真实证书 / 自签名」与证书来源，避免再拿自签名当真实证书用
  - **修复**：`-Subject` 通配会误选中根 CA —— 根证书带 `digitalSignature` 用途，会被 `-CodeSigningCert` 命中，且 `NotAfter` 更晚（2036 > 2031）导致排序后胜出。现按扩展**类型**判断 `X509BasicConstraintsExtension.CertificateAuthority` 排除 CA
  - **修复**：中文系统上 `Oid.FriendlyName` 返回「基本约束」而非 `Basic Constraints`，按英文字符串比较会永远失败 —— 改为按扩展类型判断，与系统区域设置无关
  - `-Verify` 模式下签名者不匹配时报「签名者不是当前指定的证书」，不再误报「未成功签名」
- 新增 `.github/workflows/signpath.yml`：用 **SignPath.io**（开源项目免费）做受信任签名，三个 job 顺序固定为
  `binaries`（构建 + 签主程序）→ `installers`（用已签名 exe 打安装包 + 签安装包）→ `release`（组装绿色版 + SHA256SUMS + 发 Release）
  - 未配置 `SIGNPATH_API_TOKEN` 时整条流水线**优雅跳过并输出 notice**，不刷失败通知
  - 新增 `.signpath\binaries.xml` 与 `.signpath\installers.xml` 两条 SignPath 制品配置
- `build.ps1` 新增 `-InstallerOnly`（跳过编译，只把现有 `dist\<arch>\WinTuneBox.exe` 打成安装包）——
  真实证书签名必须「先签主程序 → 再打安装包 → 最后签安装包自身」，这一步用来在签名后单独重打包而不覆盖已签名的 exe
- `build.ps1` 新增 `-AppPublisher` / `-AppCopyright`，可覆盖安装包的发布者与版权声明；`installer.iss` 把 `AppPublisher` 改为 `/D` 可覆盖，并补齐 `AppCopyright` / `VersionInfoCopyright` / `VersionInfoCompany` / `VersionInfoProductName` / `VersionInfoProductVersion`
- `build.ps1` 的 ISCC 路径改为**自动探测**（`$env:ISCC` → 本机 `D:\App\InnoSetup6` → `Program Files` → PATH），以便在 GitHub `windows-latest` 上运行
- `AssemblyInfo.cs` 的 `AssemblyCopyright` 由 `MIT License`（这是许可证名，不是版权声明）改为真实版权声明，主程序属性页的「版权」字段现在有意义

## v1.0.15（2026-09-05）
- 🐛 **修复严重问题**：免责声明检查参数颠倒（`CheckDisclaimer` 误把"静默启动"当"允许弹窗"），导致 **v1.0.14 绿色版完全无法启动**；一并移除了启动阶段调试痕迹

## v1.0.14（2026-09-05）
- 🛡 新增免责声明双通道：安装器增加许可页（须同意才能继续）+ 安装时写入已同意标记；绿色版首次启动弹免责声明（同意才继续；静默启动不打扰）
- ⚠️ 本版本含致命缺陷（见 v1.0.15），请勿使用 1.0.14

## v1.0.13（2026-09-05）
- 🚀 新增开机自启动开关（静默运行到托盘，`--minimized` 参数，不闪窗）
- 🗔 托盘启用时，点击窗口 × = 最小化到托盘（托盘右键→退出 才是真退出）
- 🎮 加速悬浮球：检测到前台全屏应用（游戏/全屏视频）时自动隐藏，退出全屏自动恢复

## v1.0.12（2026-09-05）
- 🐛 修复电源计划切换解析失败：`powercfg /list` 与 `/duplicatescheme` 输出的 GUID 不带花括号，改为裸 8-4-4-4-12 正则解析（此前每次点击都会多复制一个同名计划）
- ✨ 已有同名计划激活失败时自动继续尝试其它同名项

## v1.0.11（2026-09-05）
- 🖥 显卡识别修复：枚举全部显示适配器，过滤虚拟/远程类（MuMu、GameViewer、Duet、spacedesk 等），按 NVIDIA → AMD → Intel 优先级选主卡

## v1.0.10（2026-09-05）
- 🐛 **根治布局倒置**：WinForms `Dock=Top` 按集合 index 降序停靠，Root 与卡片内子层首次布局时各反转一次，解决"网络工具只剩 hosts 卡、标题沉底"等问题
- ✨ hosts 多行编辑框关闭 AutoSize（长内容曾把卡片高度撑爆）；子控件高度上限防御 wrap 缓存异常

## v1.0.9（2026-09-05）
- 🐛 卡片高度改为独立 Recalc + 每页构建两轮收敛布局（修复"卡片塌缩只剩 hosts"）

## v1.0.8（2026-09-05）
- 🐛 全屏/分辨率切换后布局错乱不恢复：卡片异常宽度保护 + 窗口 Resize/显示变化强制重排 + 切页复位滚动

## v1.0.7（2026-09-05）
- 🎈 新增加速悬浮球：低占用、闲置自动贴边隐藏并半透明、拖动/单击优化/双击主界面/右键菜单

## v1.0.6（2026-09-05）
- ⚡ Win11 24H2/25H2 已移除隐藏"卓越性能"方案时的自动替代（复制高性能并命名"卓越性能-增强"）

## v1.0.5（2026-09-05）
- 🐛 四项修复：侧栏标题裁切、软件卸载后列表自动刷新、电源计划复用与提权引导、DISM 中止后重跑异常

## v1.0.4（2026-09-05）
- 💾 内存优化引擎增强（三轮整理+等待脏页写回，报告真实释放量）；托盘图标 Explorer 重启自动恢复

## v1.0.3（2026-09-05）
- 📌 内存优化图标可一键固定到托盘可见区（EnableAutoTray，Win10/7）+ Win11 指引

## v1.0.2（2026-09-05）
- 📊 概览页新增内存优化卡（PCL 风格报告）；通知区域托盘常驻：单击=内存优化+气泡、双击=打开主界面

## v1.0.1（2026-09-05）
- 🐛 修复提权重启被单实例互斥锁拦截（先释放锁再启动新实例，取消 UAC 自动恢复）

## v1.0.0（2026-09-05）
- 🎉 首个正式版本：8 大功能页（概览/一键优化/垃圾清理/启动项/服务优化/网络工具/软件管理/更多工具），支持 Win7 x86 → Win11 x64
