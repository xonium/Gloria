# Turns _assets/icon/gloria_icon.png (git-ignored) into the app icon: crops the black margin around the artwork
# into src/Gloria/Resources/AppIcon/appicon.png (MauiIcon, which becomes the exe and window icon) and writes
# src/Gloria/Resources/AppIcon/gloria.ico for the exe (ApplicationIcon) and Setup.exe. Re-run after changing the artwork, then review and commit both files.
param(
    [string]$Source = (Join-Path (Split-Path -Parent $PSScriptRoot) '_assets/icon/gloria_icon.png'),

    # Black kept around the artwork, as a fraction of its larger side.
    [double]$Margin = 0.08
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Drawing

function Resize([System.Drawing.Image]$image, [System.Drawing.Rectangle]$from, [int]$size) {
    $result = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($result)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Black)
    $g.DrawImage($image, (New-Object System.Drawing.Rectangle 0, 0, $size, $size), $from, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $result
}

$image = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Source))
try {
    $w = $image.Width; $h = $image.Height
    $data = $image.LockBits((New-Object System.Drawing.Rectangle 0, 0, $w, $h),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $image.UnlockBits($data)

    # Count lit pixels per row and column. The background is noisy near-black, so "lit" means a bright channel,
    # and a row or column needs a few of them so a stray speck doesn't widen the box.
    $rows = New-Object int[] $h; $cols = New-Object int[] $w
    for ($y = 0; $y -lt $h; $y++) {
        $i = $y * $data.Stride
        for ($x = 0; $x -lt $w; $x++) {
            if ($bytes[$i] -gt 60 -or $bytes[$i + 1] -gt 60 -or $bytes[$i + 2] -gt 60) { $rows[$y]++; $cols[$x]++ }
            $i += 4
        }
    }
    $lit = { param($counts) $idx = @(0..($counts.Length - 1) | Where-Object { $counts[$_] -ge 3 }); $idx[0], $idx[-1] }
    $top, $bottom = & $lit $rows
    $left, $right = & $lit $cols

    $side = [Math]::Max($right - $left, $bottom - $top) + 1
    $side = [Math]::Min([int][Math]::Round($side * (1 + 2 * $Margin)), [Math]::Min($w, $h))
    $x0 = [Math]::Min([Math]::Max([int](($left + $right + 1 - $side) / 2), 0), $w - $side)
    $y0 = [Math]::Min([Math]::Max([int](($top + $bottom + 1 - $side) / 2), 0), $h - $side)
    $crop = New-Object System.Drawing.Rectangle $x0, $y0, $side, $side
    Write-Host "Artwork x $left-$right, y $top-$bottom; cropping $side px at ($x0, $y0)"

    $png = Join-Path $root 'src/Gloria/Resources/AppIcon/appicon.png'
    $icon = Resize $image $crop 512
    $icon.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $icon.Dispose()
    Write-Host "Wrote $png"

    # The sidebar logo, shown at about 30 px.
    $logo = Join-Path $root 'src/Gloria/Resources/Images/logo.png'
    $icon = Resize $image $crop 96
    $icon.Save($logo, [System.Drawing.Imaging.ImageFormat]::Png)
    $icon.Dispose()
    Write-Host "Wrote $logo"

    # An .ico with PNG-encoded entries (Vista and later read these).
    $sizes = 16, 24, 32, 48, 64, 128, 256
    $entries = foreach ($size in $sizes) {
        $bitmap = Resize $image $crop $size
        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        , $stream.ToArray()
    }
    $ico = Join-Path $root 'src/Gloria/Resources/AppIcon/gloria.ico'
    $out = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($ico))
    try {
        $out.Write([uint16]0); $out.Write([uint16]1); $out.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($k = 0; $k -lt $sizes.Count; $k++) {
            $dim = if ($sizes[$k] -ge 256) { 0 } else { $sizes[$k] }
            $out.Write([byte]$dim); $out.Write([byte]$dim); $out.Write([byte]0); $out.Write([byte]0)
            $out.Write([uint16]1); $out.Write([uint16]32)
            $out.Write([uint32]$entries[$k].Length); $out.Write([uint32]$offset)
            $offset += $entries[$k].Length
        }
        foreach ($entry in $entries) { $out.Write($entry) }
    }
    finally { $out.Dispose() }
    Write-Host "Wrote $ico"
}
finally { $image.Dispose() }
