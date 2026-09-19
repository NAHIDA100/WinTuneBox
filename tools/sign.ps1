<#
  ═══ WinTuneBox 代码签名 / 验签 ═══

  给发行版目录 dist\ 下的绿色版与安装包加 Authenticode 签名（SHA256 + RFC3161 时间戳）。
  自签名证书与真实证书走同一套脚本 —— 换证书只改配置，不改代码。

  用法:
    powershell -ExecutionPolicy Bypass -File tools\sign.ps1              # 签 dist\ 下全部产物
    powershell -ExecutionPolicy Bypass -File tools\sign.ps1 -Verify      # 只验签，不改文件
    powershell -ExecutionPolicy Bypass -File tools\sign.ps1 -NoTimestamp # 离线时不打时间戳
    powershell -ExecutionPolicy Bypass -File tools\sign.ps1 -Path a.exe,b.exe

  ── 换真实证书（SignPath / Certum / 商业证书）──

  证书来源按下面顺序解析，第一个命中的生效：

    1) 命令行显式指定
         powershell -File tools\sign.ps1 -Thumbprint <40位指纹>
         powershell -File tools\sign.ps1 -Subject "CN=你的名字"        # 支持通配
         powershell -File tools\sign.ps1 -Pfx D:\cert.pfx -PfxPasswordFile D:\pfx.pass

    2) signing\cert.json（一次配置，之后 build.ps1 / make-release.ps1 -Sign 自动沿用）
         {
           "mode": "store",                     // store = 从证书库找；pfx = 用 pfx 文件
           "thumbprint": "AB12...（40位）",       // mode=store 时用
           "pfx": "D:\\path\\cert.pfx",           // mode=pfx 时用
           "pfxPasswordFile": "D:\\path\\pfx.pass"
         }
       注意：cert.json 里不要写明文密码，用 pfxPasswordFile 指向密码文件。

    3) 自签名兜底：signing\cert.json 的 leafThumbprint → signing\WinTuneBox-codesign.pfx
       → 证书库里任何主题含 WinTuneBox 的代码签名证书

  装了 signtool.exe（Windows SDK）时自动优先用它 —— EV 证书与硬件令牌类证书
  用 signtool 兼容性更好（`/sha1 <指纹>` 指定证书）。没有 signtool 也照样能签
  （回退 Set-AuthenticodeSignature）。可用 -ForceSigntool / -NoSigntool 覆盖。

  注意:
    · 用 `-File` 调用时 PowerShell **传不了数组**（只会绑定第一个元素），
      所以脚本间调用一律用 `-ListFile <每行一个路径的文本文件>`；`-Path` 留给
      在会话里直接 `.\tools\sign.ps1 -Path a.exe,b.exe` 的交互用法。
    · 绿色版 zip 里的 exe 必须先签再打包，所以签名要发生在 make-release.ps1 压缩**之前**
      （build.ps1 -Sign / make-release.ps1 -Sign 已按这个顺序串好）。
    · release\ 下的 Setup 是 dist\ 已签产物的拷贝，签名随文件一起复制，无需重复签。
    · 未在本机安装受信任根证书时，自签名会显示「未知根证书」—— 签名本身没问题。
#>
[CmdletBinding()]
param(
    [string[]]$Path = @(),
    [string]$ListFile = '',
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [switch]$NoTimestamp,
    [switch]$Verify,

    # ── 证书来源（全部留空时按 cert.json / 自签名兜底解析）──
    [string]$Thumbprint = '',
    [string]$Subject = '',
    [string]$Pfx = '',
    [string]$PfxPassword = '',
    [string]$PfxPasswordFile = '',

    # ── 签名工具选择 ──
    [switch]$ForceSigntool,
    [switch]$NoSigntool,

    # 发行版根目录（证书与产物都在这里，见 tools\paths.ps1）
    [string]$OutRoot = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'paths.ps1') -OutRoot $OutRoot
$dir  = $OutSigning
$meta = Join-Path $dir 'cert.json'

function Info($m) { Write-Host $m -ForegroundColor Green }
function Note($m) { Write-Host "   $m" -ForegroundColor Yellow }
function Die($m) { Write-Host $m -ForegroundColor Red; exit 2 }

# ══════════════ 1. 找证书 ══════════════

function Read-Meta {
    if (-not (Test-Path $meta)) { return $null }
    try { return (Get-Content $meta -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { return $null }
}

function Normalize-Thumbprint([string]$tp) {
    $t = ($tp -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
    if ($t.Length -ne 40) { Die "指纹格式不对（需要 40 位十六进制）：$tp" }
    return $t
}

function Get-CertByThumbprint([string]$tp) {
    $t = Normalize-Thumbprint $tp
    foreach ($store in @('Cert:\CurrentUser\My', 'Cert:\LocalMachine\My')) {
        $c = Get-ChildItem $store -ErrorAction SilentlyContinue |
             Where-Object { $_.Thumbprint -eq $t -and $_.HasPrivateKey } |
             Select-Object -First 1
        if ($c) { return $c }
    }
    return $null
}

function Test-IsCaCert($c) {
    # 必须按类型判断：Oid.FriendlyName 在中文系统上返回「基本约束」，按英文字符串比较会永远失败
    foreach ($e in $c.Extensions) {
        if ($e -is [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]) {
            return [bool]$e.CertificateAuthority
        }
    }
    return $false
}

function Get-CertBySubject([string]$sub) {
    foreach ($store in @('Cert:\CurrentUser\My', 'Cert:\LocalMachine\My')) {
        $c = Get-ChildItem $store -CodeSigningCert -ErrorAction SilentlyContinue |
             Where-Object {
                 if (-not $_.HasPrivateKey) { return $false }
                 if ($_.Subject -notlike $sub) { return $false }
                 # 根 CA 也带 digitalSignature 用途，会被 -CodeSigningCert 命中，必须排除
                 if (Test-IsCaCert $_) { return $false }
                 return $true
             } |
             Sort-Object NotAfter -Descending | Select-Object -First 1
        if ($c) { return $c }
    }
    return $null
}

function Read-PasswordFile([string]$file) {
    if (-not $file) { return '' }
    if (-not (Test-Path $file)) { Die "找不到密码文件：$file" }
    return ([IO.File]::ReadAllText($file)).Trim()
}

function Import-PfxCert([string]$path, [string]$pw) {
    if (-not $path)  { return $null }
    if (-not (Test-Path $path)) { Die "找不到 PFX 文件：$path" }
    $sec = ConvertTo-SecureString -String $pw -AsPlainText -Force
    $c = Import-PfxCertificate -FilePath $path -CertStoreLocation 'Cert:\CurrentUser\My' -Password $sec -ErrorAction SilentlyContinue |
         Where-Object { $_.HasPrivateKey } | Select-Object -First 1
    if (-not $c) { Die "PFX 导入失败（密码错误或文件不含私钥）：$path" }
    return $c
}

# signtool 探测：PATH → Windows Kits\10\bin\<最新版本>\x64
function Find-Signtool {
    if ($NoSigntool) { return $null }
    if ($ForceSigntool) {
        $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    } else {
        $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    $bases = @()
    if (${env:ProgramFiles(x86)}) { $bases += (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') }
    if ($env:ProgramFiles)        { $bases += (Join-Path $env:ProgramFiles 'Windows Kits\10\bin') }
    foreach ($base in $bases) {
        if (-not (Test-Path $base)) { continue }
        $hit = Get-ChildItem $base -Directory -ErrorAction SilentlyContinue |
               Sort-Object Name -Descending |
               ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
               Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($hit) { return $hit }
    }
    if ($ForceSigntool) { Die '指定了 -ForceSigntool，但找不到 signtool.exe（需装 Windows SDK）' }
    return $null
}

$m     = Read-Meta
$mode  = if ($m -and $m.PSObject.Properties['mode']) { [string]$m.mode } else { 'self-signed' }
$cert  = $null
$from  = ''

if ($Thumbprint) {
    $cert = Get-CertByThumbprint $Thumbprint
    if (-not $cert) { Die "证书库里找不到指纹为 $Thumbprint 的代码签名证书（含私钥）" }
    $from = '命令行 -Thumbprint'
}
elseif ($Pfx) {
    $pw = if ($PfxPassword) { $PfxPassword } else { Read-PasswordFile $PfxPasswordFile }
    $cert = Import-PfxCert $Pfx $pw
    $from = '命令行 -Pfx'
}
elseif ($Subject) {
    $cert = Get-CertBySubject $Subject
    if (-not $cert) { Die "证书库里找不到主题匹配 $Subject 的代码签名证书（含私钥）" }
    $from = '命令行 -Subject'
}
elseif ($m -and $mode -eq 'store' -and $m.thumbprint) {
    $cert = Get-CertByThumbprint ([string]$m.thumbprint)
    if (-not $cert) { Die "cert.json 指定的证书不在本机证书库里（指纹 $($m.thumbprint)）" }
    $from = 'signing\cert.json（mode=store）'
}
elseif ($m -and $mode -eq 'pfx' -and $m.pfx) {
    $cert = Import-PfxCert ([string]$m.pfx) (Read-PasswordFile ([string]$m.pfxPasswordFile))
    $from = 'signing\cert.json（mode=pfx）'
}

# 自签名兜底
if (-not $cert -and $m -and $m.leafThumbprint) {
    $cert = Get-CertByThumbprint ([string]$m.leafThumbprint)
    if ($cert) { $from = 'signing\cert.json（自签名叶证书）' }
}
if (-not $cert) {
    $pfxFile = Join-Path $dir 'WinTuneBox-codesign.pfx'
    if (Test-Path $pfxFile) {
        $cert = Import-PfxCert $pfxFile (Read-PasswordFile (Join-Path $dir 'pfx.pass'))
        $from = 'signing\WinTuneBox-codesign.pfx（自签名）'
    }
}
if (-not $cert) {
    $cert = Get-CertBySubject '*WinTuneBox*'
    if ($cert) { $from = '证书库中主题含 WinTuneBox 的证书（自签名）' }
}
if (-not $cert) {
    Write-Host '找不到签名证书。' -ForegroundColor Red
    Write-Host '  自签名：  powershell -File tools\make-cert.ps1' -ForegroundColor Yellow
    Write-Host '  真实证书：用 -Thumbprint / -Pfx 指定，或写进 signing\cert.json' -ForegroundColor Yellow
    exit 2
}

# 链尾证书是不是 make-cert.ps1 自产的那张根（用于决定输出措辞）
$chainRoot = $null
try {
    $ch = New-Object System.Security.Cryptography.X509Certificates.X509Chain
    $ch.ChainPolicy.RevocationMode = 'NoCheck'
    $null = $ch.Build($cert)
    if ($ch.ChainElements.Count -gt 0) { $chainRoot = $ch.ChainElements[$ch.ChainElements.Count - 1].Certificate }
} catch { }
$selfSigned = ($m -and $m.rootThumbprint -and $chainRoot -and ($chainRoot.Thumbprint -eq $m.rootThumbprint))

Write-Host ("证书: {0}" -f $cert.Subject) -ForegroundColor Cyan
Write-Host ("      指纹 {0}  有效期至 {1}  来源 {2}" -f $cert.Thumbprint, $cert.NotAfter.ToString('yyyy-MM-dd'), $from) -ForegroundColor Cyan
if ($selfSigned) {
    Write-Host '      类型 自签名（仅保证完整性与时间戳，别人机器仍会提示未知发布者）' -ForegroundColor Yellow
} else {
    Write-Host '      类型 真实证书' -ForegroundColor Green
}

$signtool = Find-Signtool
if ($signtool) { Write-Host "      signtool: $signtool" -ForegroundColor Cyan }

# ══════════════ 2. 目标文件 ══════════════

if ($ListFile) {
    if (-not (Test-Path $ListFile)) { Die "找不到清单文件 $ListFile" }
    # 去 BOM 并忽略空行（清单若被别的工具写成带 BOM，首条路径会多出 U+FEFF）
    $Path = @(Get-Content -LiteralPath $ListFile -Encoding UTF8 |
              ForEach-Object { $_.Trim().TrimStart([char]0xFEFF).Trim() } |
              Where-Object { $_ })
}
if ($Path.Count -eq 0) {
    $Path = @()
    foreach ($a in @('x64', 'x86')) {
        $p = Join-Path $OutDist "$a\WinTuneBox.exe"
        if (Test-Path $p) { $Path += $p }
    }
    foreach ($pat in @('Setup-Windows优化工具箱-*.exe', 'WinOptimizer.exe')) {
        $Path += @(Get-ChildItem $OutDist -Filter $pat -File -ErrorAction SilentlyContinue |
                   ForEach-Object { $_.FullName })
    }
}
$Path = @($Path | Where-Object { $_ } | Select-Object -Unique)
if ($Path.Count -eq 0) { Die '没有找到可签名的文件（先跑 build.ps1）' }

$tsServers = @($TimestampServer, 'http://timestamp.sectigo.com', 'http://timestamp.digicert.com') |
             Where-Object { $_ } | Select-Object -Unique

# ══════════════ 3. 签名 / 验签 ══════════════

$rows = @()
$failed = 0
foreach ($f in $Path) {
    if (-not (Test-Path $f)) { Note "跳过（不存在）: $f"; continue }
    $rel = $f.Replace($OutRoot + '\', '')

    if (-not $Verify) {
        if ($signtool) {
            $desc = ''
            try { $desc = (Get-Item -LiteralPath $f).VersionInfo.FileDescription } catch { }
            if (-not $desc) { $desc = 'WinTuneBox' }

            $done = $false
            $tryTs = if ($NoTimestamp) { @($null) } else { $tsServers }
            foreach ($ts in $tryTs) {
                $a = @('sign', '/fd', 'sha256', '/sha1', $cert.Thumbprint.ToLowerInvariant(), '/d', $desc)
                if ($ts) { $a += @('/tr', $ts, '/td', 'sha256') }
                $a += $f
                $null = & $signtool @a 2>&1
                if ($LASTEXITCODE -eq 0) { $done = $true; break }
            }
            if (-not $done) {
                Note "signtool 签名失败，回退 Set-AuthenticodeSignature: $rel"
                $signtool = $null            # 回退后本批不再尝试 signtool
            }
        }
        if (-not $signtool) {
            $sig = $null
            if (-not $NoTimestamp) {
                foreach ($ts in $tsServers) {
                    try {
                        $sig = Set-AuthenticodeSignature -FilePath $f -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $ts
                        break
                    } catch { $sig = $null }
                }
                if ($sig -eq $null) { Note '时间戳服务器不可达，改为不打时间戳签名' }
            }
            if ($sig -eq $null) {
                $sig = Set-AuthenticodeSignature -FilePath $f -Certificate $cert -HashAlgorithm SHA256
            }
        }
    }

    $v = Get-AuthenticodeSignature $f
    $signedByUs = ($v.SignerCertificate -ne $null) -and ($v.SignerCertificate.Thumbprint -eq $cert.Thumbprint)
    $ts = ($v.TimeStamperCertificate -ne $null)
    if (-not $signedByUs) { $failed++ }
    $state = if ($v.Status -eq 'Valid') { '受信任' } elseif ($signedByUs) { '已签名（本机不信任该根）' } else { '未签名' }
    $rows += [pscustomobject]@{
        文件 = $rel
        签名者 = if ($v.SignerCertificate) { $v.SignerCertificate.Thumbprint.Substring(0, 8) + '…' } else { '-' }
        时间戳 = if ($ts) { '有' } else { '无' }
        状态 = $state
    }
}

Write-Host ''
Write-Host ('── {0} ──' -f $(if ($Verify) { '验签结果' } else { '签名结果' })) -ForegroundColor Cyan
$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

if ($failed -gt 0) {
    if ($Verify) { Write-Host "有 $failed 个文件的签名者不是当前指定的证书" -ForegroundColor Red }
    else         { Write-Host "有 $failed 个文件未成功签名" -ForegroundColor Red }
    exit 1
}
if ($Verify) { Info "全部 $($rows.Count) 个文件签名有效" } else { Info "已签名 $($rows.Count) 个文件" }
if ($selfSigned -and -not ($rows | Where-Object { $_.状态 -eq '受信任' })) {
    Note '本机尚未信任这张自签名证书，因此状态不是「受信任」。用管理员运行 make-cert.ps1 -MachineTrust 即可。'
}
