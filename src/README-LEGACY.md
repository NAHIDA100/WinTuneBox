# ⚠️ 本目录已冻结（legacy 存档）

这里是 **v1.x（WinForms 版）** 的源码，产物为 `WinOptimizer.exe`。

- **不再在此目录添加功能或修复**；日常使用与后续开发都在 `../wpf/`（WinTuneBox v2）。
- 保留它的唯一目的是：**老系统（Windows 7 SP1 / 8 / 8.1）支持**与**可复现构建**。
- 构建方式（需 .NET Framework 自带的 csc.exe，无第三方依赖）：

```powershell
powershell -ExecutionPolicy Bypass -File ..\build.ps1 -Target v1
```

- 产物：`dist\WinOptimizer.exe`、`dist\Setup-Windows优化工具箱-1.0.15.exe`
- 支持的系统：Windows 7 SP1（需 .NET 4.x）、8 / 8.1 / 10 / 11

> v2 与 v1 的差异、以及为什么重写，见仓库根目录的 `README.md` 与 `CHANGELOG.md` 的 v2.0.0 段落。