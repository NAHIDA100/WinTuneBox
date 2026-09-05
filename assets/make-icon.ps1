# 生成 app.ico（全 DIB 多尺寸图标，.NET Icon 无法读取 PNG 压缩条目，必须纯 DIB）
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "app.ico"
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

# ── 绘制函数：圆角蓝底渐变 + 白色"优"字 ──
function Draw-Icon([int]$size) {
    $s = [int]$size
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)
    $pad = [Math]::Max(1, [int]($s * 0.06))
    $w = $s - (2 * $pad)
    $rect = New-Object System.Drawing.Rectangle($pad, $pad, $w, $w)
    $rad = [int]($rect.Width * 0.22)
    $d = $rad * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
        [System.Drawing.Color]::FromArgb(255, 82, 140, 255),
        [System.Drawing.Color]::FromArgb(255, 20, 60, 180), 45)
    $g.FillPath($brush, $path)
    # 高光
    $glow = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gx = $rect.X + $rect.Width * 0.12
    $gy = $rect.Y + $rect.Height * 0.08
    $gw = $rect.Width * 0.76
    $gh = $rect.Height * 0.34
    $d2 = [int]($gw * 0.5)
    $glow.AddArc($gx, $gy, $d2, $d2, 180, 90)
    $glow.AddArc($gx + $gw - $d2, $gy, $d2, $d2, 270, 90)
    $glow.AddArc($gx + $gw - $d2, $gy + $gh - $d2, $d2, $d2, 0, 90)
    $glow.AddArc($gx, $gy + $gh - $d2, $d2, $d2, 90, 90)
    $glow.CloseFigure()
    $gb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(60, 255, 255, 255))
    $g.FillPath($gb, $glow)
    # 白色 "优"
    $fs = [float]($s * 0.52)
    $font = New-Object System.Drawing.Font("Microsoft YaHei UI", $fs, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $txtRect = New-Object System.Drawing.RectangleF(0, ($pad * 0.5), $s, $s)
    $wb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.DrawString("优", $font, $wb, $txtRect, $sf)
    $g.Dispose(); $font.Dispose(); $brush.Dispose(); $gb.Dispose(); $path.Dispose(); $glow.Dispose()
    return $bmp
}

# ── 组装 ICO（ICONDIR + 条目 + BITMAPINFOHEADER + XOR(BGRA 倒序) + AND(全0)）──
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
$chunks = @()
foreach ($sz in $sizes) {
    $bmp = Draw-Icon $sz
    $w = $bmp.Width; $h = $bmp.Height
    $data = New-Object byte[] ($w * $h * 4)
    $rect2 = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $bmpData = $bmp.LockBits($rect2, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    [System.Runtime.InteropServices.Marshal]::Copy($bmpData.Scan0, $data, 0, $data.Length)
    $bmp.UnlockBits($bmpData)
    $andStride = [int]((($w + 31) -band -32) / 8)
    $andMask = New-Object byte[] ($andStride * $h)   # 全 0：alpha 通道已含透明
    $msXor = New-Object System.IO.MemoryStream
    # 自下而上写出 BGRA 行
    for ($y = $h - 1; $y -ge 0; $y--) {
        $rowStart = $y * $w * 4
        for ($x = 0; $x -lt $w; $x++) {
            $i = $rowStart + $x * 4
            $b = $data[$i]; $g = $data[$i+1]; $r = $data[$i+2]; $a = $data[$i+3]
            $msXor.WriteByte($b); $msXor.WriteByte($g); $msXor.WriteByte($r); $msXor.WriteByte($a)
        }
    }
    $xor = $msXor.ToArray()
    # BITMAPINFOHEADER (40B)：IH=2*h，含 AND 掩码
    $bi = New-Object System.IO.MemoryStream
    $biw = New-Object System.IO.BinaryWriter($bi)
    $biw.Write([uint32]40)
    $biw.Write([int32]$w)
    $biw.Write([int32]($h * 2))
    $biw.Write([uint16]1)     # planes
    $biw.Write([uint16]32)    # bpp
    $biw.Write([uint32]0); $biw.Write([uint32]0); $biw.Write([uint32]0)
    $biw.Write([uint32]0); $biw.Write([uint32]0); $biw.Write([uint32]0)
    $header = $bi.ToArray()
    $chunk = New-Object byte[] ($header.Length + $xor.Length + $andMask.Length)
    [Array]::Copy($header, 0, $chunk, 0, $header.Length)
    [Array]::Copy($xor, 0, $chunk, $header.Length, $xor.Length)
    [Array]::Copy($andMask, 0, $chunk, $header.Length + $xor.Length, $andMask.Length)
    $chunks += , @{ W = $w; H = $h; Data = $chunk }
    $bmp.Dispose()
}
foreach ($c in $chunks) {
    $cw = if ($c.W -ge 256) { 0 } else { $c.W }
    $ch = if ($c.H -ge 256) { 0 } else { $c.H }
    $bw.Write([byte]$cw); $bw.Write([byte]$ch); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$c.Data.Length); $bw.Write([uint32]$offset)
    $offset += $c.Data.Length
}
foreach ($c in $chunks) { $bw.Write($c.Data) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($out, $ms.ToArray())
Write-Host "已生成 $out ($($chunks.Count) 个尺寸, $($ms.Length) 字节)"
