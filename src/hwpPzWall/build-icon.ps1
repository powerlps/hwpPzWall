# app.ico 생성기 — 트레이 아이콘과 같은 그림을 여러 크기로 만들어 ICO 로 묶는다.
# 아이콘 모양을 바꾸고 싶으면 IconFactory.cs 와 이 파일을 함께 수정하면 된다.

Add-Type -AssemblyName System.Drawing

$outPath = Join-Path $PSScriptRoot 'app.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # 트레이 '켜짐' 아이콘과 같은 붉은 계열 (IconFactory.cs 와 맞춰 둘 것)
    $accent = [System.Drawing.Color]::FromArgb(0xE5, 0x39, 0x35)
    $frame  = [System.Drawing.Color]::FromArgb(0x9A, 0x1B, 0x1B)

    $pad = $size * 0.09
    $screenX = $pad
    $screenY = $pad * 1.6
    $screenW = $size - $pad * 2
    $screenH = $size - $pad * 3.4

    $body = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 0xFF, 0xE8, 0xE6))
    $g.FillRectangle($body, $screenX, $screenY, $screenW, $screenH)

    $penWidth = [Math]::Max(1.4, $size * 0.075)
    $pen = New-Object System.Drawing.Pen ($frame, $penWidth)
    $g.DrawRectangle($pen, $screenX, $screenY, $screenW, $screenH)

    $bandBrush = New-Object System.Drawing.SolidBrush ($accent)
    $g.FillRectangle($bandBrush, $screenX, $screenY, ($screenW * 0.24), $screenH)

    $standBrush = New-Object System.Drawing.SolidBrush ($frame)
    $sw = $size * 0.34
    $g.FillRectangle($standBrush, (($size - $sw) / 2), ($screenY + $screenH + $size * 0.04), $sw, ($size * 0.10))

    $body.Dispose(); $pen.Dispose(); $bandBrush.Dispose(); $standBrush.Dispose(); $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$images = New-Object System.Collections.ArrayList
foreach ($s in $sizes) {
    [void]$images.Add([pscustomobject]@{ Size = $s; Data = (New-IconPng $s) })
}

$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR
$bw.Write([UInt16]0)              # reserved
$bw.Write([UInt16]1)              # type = icon
$bw.Write([UInt16]$images.Count)

$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $size = $img.Size
    $data = $img.Data
    $dim = if ($size -ge 256) { 0 } else { $size }

    $bw.Write([Byte]$dim)          # width
    $bw.Write([Byte]$dim)          # height
    $bw.Write([Byte]0)             # color count
    $bw.Write([Byte]0)             # reserved
    $bw.Write([UInt16]1)           # planes
    $bw.Write([UInt16]32)          # bit count
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}

foreach ($img in $images) { $bw.Write($img.Data, 0, $img.Data.Length) }

$bw.Flush(); $bw.Close(); $fs.Close()

Write-Output "생성 완료: $outPath ($((Get-Item $outPath).Length) bytes)"
