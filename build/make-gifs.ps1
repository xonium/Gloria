# Records the README's GIFs from the running app: an encounter replay, the Overview and the Compare page. Each scene
# starts Gloria with GLORIA_RECORD (see src/Gloria/Services/DevRecorder.cs), which saves frames into
# artifacts/gifs/<scene>/ and quits; ffmpeg then turns them into docs/media/<scene>.gif. Uses your real database and
# current profile, so the names in it show. Needs ffmpeg on PATH (winget install Gyan.FFmpeg). Review and commit the GIFs.
param(
    # The encounter to replay: a flick into a spray that kills, on Dust2.
    [int]$EncounterId = 9889,

    [string[]]$Scenes = @('replay', 'overview', 'compare'),

    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs/media')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    throw 'ffmpeg was not found on PATH. Install it with: winget install Gyan.FFmpeg'
}

dotnet build (Join-Path $root 'src/Gloria') -v quiet -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$exe = Join-Path $root 'src/Gloria/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Gloria.exe'

# Width of each GIF; the README shows Overview and Compare at half this, sharp on high-DPI screens.
$widths = @{ replay = 880; overview = 860; compare = 860 }
$routes = @{ replay = "encounter?id=$EncounterId"; overview = '//overview'; compare = '//compare' }

New-Item -ItemType Directory -Force $Out | Out-Null
foreach ($name in 'GLORIA_IMPORT', 'GLORIA_CAPTURE', 'GLORIA_CAPTURE_SHELL', 'GLORIA_CAPTURE_SCROLL') {
    Remove-Item "Env:$name" -ErrorAction SilentlyContinue
}

foreach ($scene in $Scenes) {
    $frames = Join-Path $root "artifacts/gifs/$scene"
    $env:GLORIA_RECORD = $frames
    $env:GLORIA_RECORD_SCENE = $scene
    $env:GLORIA_START_ROUTE = $routes[$scene]
    Write-Host "Recording $scene..."
    $app = Start-Process $exe -PassThru
    if (-not $app.WaitForExit(180000)) {
        Stop-Process -Id $app.Id
        throw "$scene took more than 3 minutes; frames so far are in $frames."
    }
    if (Test-Path (Join-Path $frames 'error.txt')) {
        throw "$scene failed: $(Get-Content (Join-Path $frames 'error.txt') -Raw)"
    }

    $gif = Join-Path $Out "$scene.gif"
    $filter = "scale=$($widths[$scene]):-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle"
    ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i (Join-Path $frames 'frames.txt') -vf $filter -fps_mode vfr -loop 0 $gif
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed on $scene." }
    $count = (Get-ChildItem $frames -Filter *.png).Count
    Write-Host ("  {0}: {1} frames, {2:N1} MB" -f $gif, $count, ((Get-Item $gif).Length / 1MB))
}

foreach ($name in 'GLORIA_RECORD', 'GLORIA_RECORD_SCENE', 'GLORIA_START_ROUTE') {
    Remove-Item "Env:$name" -ErrorAction SilentlyContinue
}
