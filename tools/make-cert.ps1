<#
  ═══ 生成 WinTuneBox 代码签名证书（免费自签名方案）═══

  免费 ≠ 全局受信任，先看清楚前提：
    · 本脚本生成的是**自签名**证书链（根 CA + 代码签名叶证书），零成本、可离线、可无限重签。
    · 它能让 exe 拥有真正的 Authenticode 签名（SHA256 + RFC3161 时间戳）：谁都能验证
      「文件没被篡改」「确实是这个发布者签的（前提是他信任这张证书）」。
    · 但它**不能**让全世界的 SmartScreen / 「未知发布者」自动消失 —— 其他机器要信任，
      必须把导出的根证书导入那台机器的「受信任的根证书颁发机构」。想免警告地全网分发，
      只有三条路：SignPath.io（开源项目免费，需申请）、Certum 开源代码签名证书（免费，需
      实名审核 + 硬件令牌）、Azure Trusted Signing（付费）。详见 README。

  用法:
    # 生成（装入当前用户证书存储，无需管理员；不需要提示"始终信任"即可本机自用）
    powershell -ExecutionPolicy Bypass -File tools\make-cert.ps1

    # 同时写入计算机级受信任根（需要**管理员**权限；本机才会真正"受信任"）
    powershell -ExecutionPolicy Bypass -File tools\make-cert.ps1 -MachineTrust

    # 换发布者名称 / 有效期
    powershell -ExecutionPolicy Bypass -File tools\make-cert.ps1 -Publisher "CN=Your Name" -Years 5

    # 重新生成（覆盖旧的） / 卸载全部证书
    powershell -ExecutionPolicy Bypass -File tools\make-cert.ps1 -Force
    powershell -ExecutionPolicy Bypass -File tools\make-cert.ps1 -Remove

  产物（`signing\`，已被 .gitignore 忽略，**切勿提交私钥**）:
    WinTuneBox-RootCA.cer      根证书公钥（分发给别人导入用）
    WinTuneBox-codesign.cer    叶证书公钥
    WinTuneBox-codesign.pfx    含私钥，签名机之间迁移用
    pfx.pass                   PFX 口令（与 pfx 放一起，仅本地自用够；对外请自行保管）
    cert.json                  指纹与有效期，供 sign.ps1 定位证书
#>
[CmdletBinding()]
param(
    [string]$Publisher = 'WinTuneBox (NAHIDA100)',
    [int]$Years = 5,
    [string]$Password = '',
    [switch]$MachineTrust,
    [switch]$Force,
    [switch]$Remove,
    # 发行版根目录（证书落在 <OutRoot>\signing\，见 tools\paths.ps1）
    [string]$OutRoot = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'paths.ps1') -OutRoot $OutRoot
$dir  = $OutSigning
$meta = Join-Path $dir 'cert.json'

function Info($m) { Write-Host $m -ForegroundColor Green }
function Note($m) { Write-Host "   $m" -ForegroundColor Yellow }

function Get-Stores([string]$scope) {
    return @('My', 'Root', 'TrustedPublisher', 'Disallowed') | ForEach-Object { "$scope\$_" }
}

function Remove-BySubject([string]$subjectLike, [string]$scope) {
    $n = 0
    foreach ($sn in @('My', 'Root', 'TrustedPublisher', 'Disallowed')) {
        try {
            $st = New-Object System.Security.Cryptography.X509Certificates.X509Store($sn, $scope)
            $st.Open('ReadWrite')
            foreach ($c in @($st.Certificates | Where-Object { $_.Subject -like $subjectLike })) {
                $st.Remove($c); $n++
            }
            $st.Close()
        } catch { }
    }
    return $n
}

# ── 卸载 ──
if ($Remove) {
    Write-Host '==> 移除 WinTuneBox 代码签名证书 ...' -ForegroundColor Cyan
    $n1 = Remove-BySubject '*WinTuneBox*' 'CurrentUser'
    $n2 = Remove-BySubject '*WinTuneBox*' 'LocalMachine'   # 非管理员时静默失败
    Info "已移除：当前用户 $n1 项，本机 $n2 项"
    if (Test-Path $meta) { [IO.File]::Delete($meta) }
    Note 'signing\ 下的 cer/pfx 未删除（如需彻底清除请手动删除该目录）'
    return
}

if ((Test-Path $meta) -and -not $Force) {
    $old = Get-Content $meta -Raw -Encoding UTF8 | ConvertFrom-Json
    $exists = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
              Where-Object { $_.Thumbprint -eq $old.leafThumbprint }
    if ($exists) {
        Info "已存在可用证书：$($old.leafThumbprint)（发布者 $($old.publisher)）"
        Note '要重新生成请加 -Force'
        return
    }
    Note '证书已不在存储中，将重新生成'
}

if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }

# 旧的同名证书先清掉，避免 Root/TrustedPublisher 里堆积一堆失效项
[void](Remove-BySubject '*WinTuneBox*' 'CurrentUser')

$pwd = if ($Password) { $Password } else {
    -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ })
}

Write-Host '==> 1/4 生成根 CA ...' -ForegroundColor Cyan
$rootCert = New-SelfSignedCertificate -Type Custom `
    -Subject ("CN=$Publisher Root CA") `
    -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
    -KeyUsage CertSign, CRLSign, DigitalSignature -KeyUsageProperty Sign `
    -TextExtension @('2.5.29.19={text}CA=true&pathlength=1', '2.5.29.37={text}1.3.6.1.5.5.7.3.3') `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddYears($Years + 5)
Info "    根证书 $($rootCert.Thumbprint)"

Write-Host '==> 2/4 生成代码签名叶证书 ...' -ForegroundColor Cyan
$leaf = New-SelfSignedCertificate -Type Custom `
    -Subject ("CN=$Publisher") -Signer $rootCert `
    -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
    -KeyUsage DigitalSignature -KeyUsageProperty Sign `
    -TextExtension @('2.5.29.19={text}CA=false', '2.5.29.37={text}1.3.6.1.5.5.7.3.3') `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddYears($Years)
Info "    叶证书 $($leaf.Thumbprint)"

Write-Host '==> 3/4 导出到 signing\ ...' -ForegroundColor Cyan
Export-Certificate -Cert $rootCert -FilePath (Join-Path $dir 'WinTuneBox-RootCA.cer') -Type CERT | Out-Null
Export-Certificate -Cert $leaf     -FilePath (Join-Path $dir 'WinTuneBox-codesign.cer') -Type CERT | Out-Null
$sec = ConvertTo-SecureString -String $pwd -AsPlainText -Force
Export-PfxCertificate -Cert $leaf -FilePath (Join-Path $dir 'WinTuneBox-codesign.pfx') -Password $sec | Out-Null
[IO.File]::WriteAllText((Join-Path $dir 'pfx.pass'), $pwd, (New-Object System.Text.UTF8Encoding($false)))

$info = [ordered]@{
    publisher     = $Publisher
    rootThumbprint = $rootCert.Thumbprint
    leafThumbprint = $leaf.Thumbprint
    notAfter       = $leaf.NotAfter.ToString('yyyy-MM-dd')
    created        = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
}
[IO.File]::WriteAllText($meta, ($info | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
Info '    已导出 cer / pfx / pfx.pass / cert.json'

Write-Host '==> 4/4 安装信任 ...' -ForegroundColor Cyan
$rootPub = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList @(, $rootCert.RawData)
$leafPub = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList @(, $leaf.RawData)

function Add-To([string]$storeName, [string]$scope, $cert) {
    $st = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, $scope)
    $st.Open('ReadWrite'); $st.Add($cert); $st.Close()
}
try {
    Add-To 'Root' 'CurrentUser' $rootPub
    Add-To 'TrustedPublisher' 'CurrentUser' $leafPub
    Info '    已装入 当前用户\受信任的根证书颁发机构 + 受信任的发布者'
} catch { Note "当前用户存储写入失败：$($_.Exception.Message)" }

$machineOk = $false
if ($MachineTrust) {
    try {
        Add-To 'Root' 'LocalMachine' $rootPub
        Add-To 'TrustedPublisher' 'LocalMachine' $leafPub
        $machineOk = $true
        Info '    已装入 计算机级受信任的根证书颁发机构 + 受信任的发布者'
    } catch {
        Note "计算机级写入失败（需要以管理员身份运行）：$($_.Exception.Message.Split([char]10)[0])"
    }
}

Write-Host ''
Info "完成：$Publisher  有效期至 $($leaf.NotAfter.ToString('yyyy-MM-dd'))"
if (-not $machineOk) {
    Note '提示：当前只装到「当前用户」存储 —— 文件验签（Get-AuthenticodeSignature）已经会显示「受信任」。'
    Note '      UAC 弹窗与 SmartScreen 跑在 SYSTEM 上下文，读的是**计算机级**存储；想让它们也认出发布者，'
    Note '      请用管理员权限再跑一次： powershell -ExecutionPolicy Bypass -File tools\make-cert.ps1 -MachineTrust'
}
Note '分发给他人时，把 signing\WinTuneBox-RootCA.cer 一并附上，让对方导入「受信任的根证书颁发机构」。'