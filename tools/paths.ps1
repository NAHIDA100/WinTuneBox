<#
  ═══ 统一路径解析：源码仓库 / 发行版目录 **完全分离** ═══

  源码仓库里**不产生任何构建产物**。所有 exe、安装包、绿色版 zip、证书私钥
  都落在「发行版根目录」下，默认与源码目录同级：

      源码   D:\HENDGIH\WinTuneBox
      发行版 D:\HENDGIH\WinTuneBox-Release
             ├─ dist\     编译产物（x64\ / x86\ / Setup-*.exe / v1 exe / 自检报告）
             ├─ release\  发行包（绿色版 zip + 安装包 + SHA256SUMS + 根证书）
             ├─ signing\  证书与私钥（**绝不能提交**）
             ├─ bin\      MSBuild 编译输出
             └─ obj\      MSBuild 中间文件

  解析顺序（第一个有值的生效）：
    1) 调用方传入的 -OutRoot
    2) 环境变量 $env:WINTUNEBOX_OUT
    3) 源码目录的**同级**目录 <源码目录名>-Release
    4) 兜底 <源码目录>\_out

  用法（根目录下的脚本）：
      . (Join-Path $root 'tools\paths.ps1') -OutRoot $OutRoot
  用法（tools\ 下的脚本）：
      . (Join-Path $PSScriptRoot 'paths.ps1') -OutRoot $OutRoot

  可用变量：$SrcRoot $OutRoot $OutDist $OutRelease $OutSigning $OutBin $OutObj
  目录不存在时会自动创建。

  CI 里建议显式指定，避免产物落到 workspace 外面：
      $env:WINTUNEBOX_OUT = "${{ github.workspace }}/out"
#>
[CmdletBinding()]
param([string]$OutRoot = '')

$ErrorActionPreference = 'Stop'

# 本文件位于 <源码>\tools\ 下
$SrcRoot = Split-Path -Parent $PSScriptRoot

if (-not $OutRoot) { $OutRoot = $env:WINTUNEBOX_OUT }
if (-not $OutRoot) {
    $SrcRoot = (Resolve-Path -LiteralPath $SrcRoot).Path
    $OutRoot = Join-Path (Split-Path -Parent $SrcRoot) ((Split-Path -Leaf $SrcRoot) + '-Release')
}
if (-not $OutRoot) { $OutRoot = Join-Path $SrcRoot '_out' }

$OutRoot    = [IO.Path]::GetFullPath($OutRoot)
$OutDist    = Join-Path $OutRoot 'dist'
$OutRelease = Join-Path $OutRoot 'release'
$OutSigning = Join-Path $OutRoot 'signing'
$OutBin     = Join-Path $OutRoot 'bin'
$OutObj     = Join-Path $OutRoot 'obj'

foreach ($d in @($OutRoot, $OutDist, $OutRelease, $OutSigning, $OutBin, $OutObj)) {
    if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}
