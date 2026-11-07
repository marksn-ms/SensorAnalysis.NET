# Packages one SensorAnalysis desktop product with Velopack.
# Requires the .NET 8 SDK. No secrets are required. Authenticode signing is not used.
# Example:
#   ./distribution/pack.ps1 -ProductId sensoranalysis

[CmdletBinding()]
param(
    [string]$ProductId,
    [string]$Configuration = "Release",
    [string]$Version,
    [string]$BasePackages,
    [switch]$Help
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$Root = Split-Path $PSScriptRoot -Parent
$ManifestPath = Join-Path $PSScriptRoot "products.json"

function Show-Help {
    @"
pack.ps1 builds a self-contained Windows x64 folder and packages it with Velopack.

No secrets are required. The script does not sign binaries and does not publish a GitHub release.

Usage:
  ./distribution/pack.ps1 -ProductId <id> [-Configuration Release] [-Version X.Y.Z] [-BasePackages <dir>]
  ./distribution/pack.ps1 -Help

-ProductId     sensoranalysis, sensoranalysis.space, or sensoranalysis.sphere
-Version       Overrides the manifest version for a local experiment. GitHub Actions does not use this.
-BasePackages  Directory of an older full .nupkg. Velopack uses it to build a delta.

Output:
  artifacts/dist/<id>/     Velopack output, including any base packages
  artifacts/upload/<id>/   Files safe to attach to a GitHub Release, plus SHA256SUMS.txt

Example:
  ./distribution/pack.ps1 -ProductId sensoranalysis
"@
}

if ($Help -or [string]::IsNullOrWhiteSpace($ProductId)) {
    Show-Help
    exit 0
}

$catalog = Get-Content -Raw $ManifestPath | ConvertFrom-Json
$product = $catalog.products | Where-Object { $_.id -eq $ProductId } | Select-Object -First 1
if (-not $product) {
    $known = ($catalog.products.id) -join ", "
    Write-Error "Unknown product '$ProductId'. Known products: $known"
}

$releaseVersion = if ([string]::IsNullOrWhiteSpace($Version)) { [string]$product.version } else { $Version }
if ($releaseVersion -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "Version '$releaseVersion' must be MAJOR.MINOR.PATCH."
}
if ($catalog.runtime -ne "win-x64") {
    Write-Error "The manifest runtime '$($catalog.runtime)' is not win-x64."
}

$publishDir = Join-Path $Root "artifacts/publish/$ProductId"
$distDir = Join-Path $Root "artifacts/dist/$ProductId"
$uploadDir = Join-Path $Root "artifacts/upload/$ProductId"
foreach ($dir in @($publishDir, $distDir, $uploadDir)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir | Out-Null
}

if ($BasePackages) {
    # Non-default channels are part of the Velopack file name: {packId}-{version}-{channel}-full.nupkg
    $fullPackages = Get-ChildItem -Path $BasePackages -Filter "$($product.packId)-*-$($product.channel)-full.nupkg" -File
    if (-not $fullPackages) {
        Write-Error "No $($product.packId) full packages were found in '$BasePackages'."
    }
    Copy-Item $fullPackages.FullName $distDir
}

Push-Location $Root
try {
    dotnet tool restore --tool-manifest (Join-Path $Root "dotnet-tools.json")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $project = Join-Path $Root ($product.project -replace '/', '\')
    dotnet publish $project -c $Configuration -r win-x64 --self-contained true -o $publishDir `
        -p:Version=$releaseVersion -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $mainExe = "$($product.assemblyName).exe"
    if (-not (Test-Path (Join-Path $publishDir $mainExe))) {
        Write-Error "Publish did not produce $mainExe."
    }

    dotnet tool run vpk -- pack `
        --yes `
        --packId $product.packId `
        --packVersion $releaseVersion `
        --packDir $publishDir `
        --mainExe $mainExe `
        --packTitle $product.title `
        --packAuthors "SensorAnalysis Contributors" `
        --channel $product.channel `
        --runtime $catalog.runtime `
        --outputDir $distDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

$feedName = "releases.$($product.channel).json"
$wanted = @(
    "$($product.packId)-$($product.channel)-Setup.exe",
    "$($product.packId)-$($product.channel)-Portable.zip",
    "$($product.packId)-$releaseVersion-$($product.channel)-full.nupkg",
    "$($product.packId)-$releaseVersion-$($product.channel)-delta.nupkg",
    $feedName
)

$lines = @()
foreach ($name in $wanted) {
    $source = Join-Path $distDir $name
    if (-not (Test-Path $source)) {
        if ($name -like "*-delta.nupkg") { continue }
        Write-Error "Velopack did not produce $name."
    }
    Copy-Item $source $uploadDir
    $hash = (Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines += "$hash  $name"
}

$checksumPath = Join-Path $uploadDir "SHA256SUMS.txt"
Set-Content -Path $checksumPath -Value ($lines -join "`n") -Encoding ascii -NoNewline
Add-Content -Path $checksumPath -Value "" -Encoding ascii

Write-Host "Packaged $ProductId $releaseVersion"
Write-Host "Upload files: $uploadDir"
