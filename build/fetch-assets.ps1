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

if ($Maps) {
    # Radars (1024 px, about 110 kB each) and their world-to-image transform, from MurkyYT/cs2-map-icons, which
    # follows CS2 updates. Maps with a lower level get <map>_lower.png and the altitude below which it applies.
    $target = Join-Path $root 'src/Gloria/Resources/Raw/maps'
    New-Item -ItemType Directory -Force $target | Out-Null
    $base = 'https://raw.githubusercontent.com/MurkyYT/cs2-map-icons/main/images/radars'
    $available = (Invoke-RestMethod 'https://raw.githubusercontent.com/MurkyYT/cs2-map-icons/main/data/available.json').maps
    $index = [ordered]@{}
    foreach ($map in $available.PSObject.Properties | Where-Object { $_.Name -match '^(de|cs)_' } | Sort-Object Name) {
        $name = $map.Name
        $info = $map.Value.radar_info
        if ($null -eq $info -or $null -eq $info.scale -or "$base/${name}_radar_psd.png" -notin $map.Value.radar_paths) { continue }

        Invoke-WebRequest "$base/${name}_radar_psd.png" -OutFile (Join-Path $target "$name.png")
        $entry = [ordered]@{ posX = $info.pos_x; posY = $info.pos_y; scale = $info.scale }
        $lower = $info.verticalsections.lower
        if ($null -ne $lower -and "$base/${name}_lower_radar_psd.png" -in $map.Value.radar_paths) {
            Invoke-WebRequest "$base/${name}_lower_radar_psd.png" -OutFile (Join-Path $target "${name}_lower.png")
            $entry.lowerBelow = $lower.AltitudeMax
        }

        $index[$name] = $entry
    }

    $index | ConvertTo-Json | Set-Content (Join-Path $target 'maps.json') -Encoding utf8NoBOM
    Write-Host "$($index.Count) maps in $target"
}
