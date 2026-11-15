# Downloads the game art the app bundles: weapon icons (and, with -Maps, radar images and their coordinates).
# The files are Valve's; they are committed so builds don't depend on these repositories staying up.
# Re-run after a CS2 update that adds weapons or changes a map, then review and commit the changes.
param(
    [switch]$Weapons,
    [switch]$Maps
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Weapons -and -not $Maps) { $Weapons = $true; $Maps = $true }

if ($Weapons) {
    # The sources are black silhouettes on transparent. MauiImage TintColor doesn't recolor PNGs, so they are
    # recolored here to the theme's light grey, keeping the alpha.
    Add-Type -AssemblyName System.Drawing
    $target = Join-Path $root 'src/Gloria/Resources/Images/Weapons'
    New-Item -ItemType Directory -Force $target | Out-Null
    $files = Invoke-RestMethod 'https://api.github.com/repos/Spencer-png/cs2-gun-icons/contents/cs2%20weapons'
    $icons = @($files | Where-Object { $_.name -like 'weapon_*.png' })
    foreach ($file in $icons) {
        $path = Join-Path $target $file.name
        $bytes = (Invoke-WebRequest $file.download_url).Content
        $source = [System.Drawing.Bitmap]::new([System.IO.MemoryStream]::new($bytes))
        $tinted = [System.Drawing.Bitmap]::new($source.Width, $source.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        for ($y = 0; $y -lt $source.Height; $y++) {
            for ($x = 0; $x -lt $source.Width; $x++) {
                $alpha = $source.GetPixel($x, $y).A
                $tinted.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($alpha, 0xd6, 0xd7, 0xda))
            }
        }
        $tinted.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $source.Dispose(); $tinted.Dispose()
    }
    Write-Host "$($icons.Count) weapon icons in $target"
}
