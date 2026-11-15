<#
.SYNOPSIS
    Publishes Gloria self-contained and packs it into a Velopack release (Setup.exe + update packages).
.EXAMPLE
    ./build/pack.ps1 -Version 0.1.0
#>
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [string]$Runtime = 'win-x64',

    # vpk pack reads earlier releases here to build delta updates.
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\releases')
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$publishDir = Join-Path $root "artifacts\publish\$Runtime"

Push-Location $root
try {
    dotnet tool restore
    if ($LASTEXITCODE) { throw 'dotnet tool restore failed' }

    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

    # Always pack publish output, never bin/.
    dotnet publish src/Gloria/Gloria.csproj -c Release -r $Runtime --self-contained -p:ApplicationDisplayVersion=$Version -o $publishDir
    if ($LASTEXITCODE) { throw 'dotnet publish failed' }

    dotnet vpk pack --packId Gloria --packVersion $Version --packDir $publishDir --mainExe Gloria.exe --packTitle Gloria --outputDir $OutputDir
    if ($LASTEXITCODE) { throw 'vpk pack failed' }
}
finally {
    Pop-Location
}
