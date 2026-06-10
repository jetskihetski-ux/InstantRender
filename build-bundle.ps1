<#
.SYNOPSIS
  Build Instant Render and assemble a ready-to-copy AutoCAD plugin bundle.

.DESCRIPTION
  Run this on a PC that has AutoCAD 2025/2026 + the .NET 8 SDK. It produces
  .\dist\InstantRender.bundle\ which you copy to another machine's
  %APPDATA%\Autodesk\ApplicationPlugins\ to install (see -Install switch).

.EXAMPLE
  # Build the bundle (auto-detects AutoCAD path):
  .\build-bundle.ps1

.EXAMPLE
  # Build with an explicit AutoCAD path, then install on THIS machine:
  .\build-bundle.ps1 -AcadDir "C:\Program Files\Autodesk\AutoCAD 2026\" -Install
#>
param(
    [string]$AcadDir = "",
    [switch]$Install
)

$ErrorActionPreference = "Stop"
$root    = $PSScriptRoot
$proj    = Join-Path $root "src\InstantRender.Plugin\InstantRender.Plugin.csproj"
$distDir = Join-Path $root "dist"
$bundle  = Join-Path $distDir "InstantRender.bundle"
$contents= Join-Path $bundle "Contents"

# 1. Build (Release). Pass AcadDir through if supplied.
Write-Host "==> Building (Release)..." -ForegroundColor Cyan
if ($AcadDir -ne "") {
    dotnet build $proj -c Release -p:AcadDir="$AcadDir"
} else {
    dotnet build $proj -c Release
}
if ($LASTEXITCODE -ne 0) { throw "Build failed. Set -AcadDir to your AutoCAD program folder." }

$outDir = Join-Path $root "src\InstantRender.Plugin\bin\Release\net8.0-windows"
$dll    = Join-Path $outDir "InstantRender.dll"
if (-not (Test-Path $dll)) { throw "Build output not found: $dll" }

# 2. Assemble the bundle folder.
Write-Host "==> Assembling bundle..." -ForegroundColor Cyan
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $contents "scripts") | Out-Null

Copy-Item $dll -Destination $contents -Force
Copy-Item (Join-Path $root "scripts\render_scene.py") `
          -Destination (Join-Path $contents "scripts") -Force
Copy-Item (Join-Path $root "packaging\PackageContents.xml") `
          -Destination $bundle -Force
Copy-Item (Join-Path $outDir "instantrender.config.sample.json") `
          -Destination (Join-Path $contents "instantrender.config.sample.json") -Force

Write-Host "==> Bundle ready: $bundle" -ForegroundColor Green
Write-Host "    Copy the InstantRender.bundle folder to the other PC at:"
Write-Host "    %APPDATA%\Autodesk\ApplicationPlugins\" -ForegroundColor Yellow

# 3. Optional: install on THIS machine.
if ($Install) {
    $target = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\InstantRender.bundle"
    Write-Host "==> Installing to $target" -ForegroundColor Cyan
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Copy-Item $bundle -Destination $target -Recurse -Force
    Write-Host "==> Installed. Restart AutoCAD; the Instant Render tab appears." -ForegroundColor Green
}
