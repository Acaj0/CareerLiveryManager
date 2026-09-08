<#
.SYNOPSIS
    Publishes a self-contained win-x64 build and packages it as a versioned,
    ready-to-upload GitHub Release zip (+ .sha256 checksum), reading the version
    straight from CareerLiveryManager.App.csproj so it never has to be typed by hand.

.EXAMPLE
    ./scripts/publish-release.ps1
#>

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$csprojPath = Join-Path $repoRoot "src\CareerLiveryManager.App\CareerLiveryManager.App.csproj"
$publishDir = Join-Path $repoRoot "publish"
$distDir = Join-Path $repoRoot "dist"

if (-not (Test-Path $csprojPath)) {
    throw "Could not find $csprojPath"
}

$csprojContent = Get-Content $csprojPath -Raw
if ($csprojContent -notmatch "<Version>([^<]+)</Version>") {
    throw "Could not find <Version> in $csprojPath"
}
$version = $Matches[1]
$tag = "v$version"
$zipName = "CareerLiveryManager-$tag-win-x64.zip"

Write-Host "Publishing Career Livery Manager $tag ..." -ForegroundColor Cyan

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

dotnet publish "$repoRoot\src\CareerLiveryManager.App" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

# The .pdb isn't needed by end users and just adds size to the download.
Get-ChildItem $publishDir -Filter "*.pdb" | Remove-Item -Force

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$zipPath = Join-Path $distDir $zipName
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLower()
$hashPath = "$zipPath.sha256"
Set-Content -Path $hashPath -Value $hash -Encoding ascii -NoNewline

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Zip:      $zipPath"
Write-Host "  Checksum: $hashPath"
Write-Host ""
Write-Host "Next: create a GitHub Release tagged '$tag' and upload both files as assets."
