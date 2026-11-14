#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes Kwanteks' NIC Tweaker (self-contained WinUI) and builds the Inno Setup installer.

.PARAMETER Sign
  Optional Authenticode thumbprint. When set, attempts to sign the setup EXE (signtool must be on PATH).

.PARAMETER SkipInstaller
  Publish only; do not run ISCC.
#>
param(
  [string]$Sign = "",
  [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root "Kwanteks' NIC Tweaker\Kwanteks' NIC Tweaker.csproj"
$publishDir = Join-Path $root 'publish\app'
$symbolsDir = Join-Path $root 'publish\symbols'
$distDir = Join-Path $root 'dist'
$iss = Join-Path $root 'installer\KwanteksNICTweaker.iss'
$isccCandidates = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
  (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
)

function Get-ProjectVersion([string]$csprojPath) {
  [xml]$xml = Get-Content -LiteralPath $csprojPath
  $ver = $xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
  if (-not $ver) { throw "No <Version> in $csprojPath" }
  return [string]$ver
}

Write-Host '=== Clean ===' -ForegroundColor Cyan
@(
  (Join-Path $root "Kwanteks' NIC Tweaker\bin"),
  (Join-Path $root "Kwanteks' NIC Tweaker\obj"),
  (Join-Path $root 'publish'),
  $distDir
) | ForEach-Object {
  if (Test-Path -LiteralPath $_) {
    Remove-Item -LiteralPath $_ -Recurse -Force
  }
}
New-Item -ItemType Directory -Force -Path $publishDir, $symbolsDir, $distDir | Out-Null

$version = Get-ProjectVersion $proj
Write-Host "Version: $version"

Write-Host '=== dotnet publish ===' -ForegroundColor Cyan
# Neutral NuGet + source paths so PDBs/binaries do not embed C:\Users\<you>\...
$nugetCache = 'C:\ProgramData\KwanteksBuild\nuget'
New-Item -ItemType Directory -Force -Path $nugetCache | Out-Null
$env:NUGET_PACKAGES = $nugetCache

# Prefer a path without apostrophe for MSBuild/XAML if mirrored copy exists.
$buildProj = $proj
$tempMirror = 'C:\temp\nictweaker-publish'
if ($proj -match "'") {
  if (Test-Path $tempMirror) { Remove-Item $tempMirror -Recurse -Force }
  New-Item -ItemType Directory -Path $tempMirror | Out-Null
  robocopy (Join-Path $root "Kwanteks' NIC Tweaker") $tempMirror /E /XD bin obj .vs /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
  $buildProj = Join-Path $tempMirror "Kwanteks' NIC Tweaker.csproj"
}

# ContinuousIntegrationBuild rewrites local source paths; NUGET_PACKAGES avoids Users\<you>\.nuget embeds.
& dotnet publish $buildProj `
  -c Release `
  -r win-x64 `
  -p:Platform=x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:PublishTrimmed=false `
  -p:PublishAot=false `
  -p:ContinuousIntegrationBuild=true `
  -p:Deterministic=true `
  -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# WinUI custom -o often omits .pri / .xbf; merge from the project bin output.
$builtOut = Join-Path (Split-Path -Parent $buildProj) 'bin\x64\Release\net8.0-windows10.0.19041.0\win-x64'
if (Test-Path -LiteralPath $builtOut) {
  Write-Host "Merging WinUI assets from $builtOut"
  & robocopy $builtOut $publishDir /E /XF *.pdb /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
  # robocopy 0-7 = success
  if ($LASTEXITCODE -ge 8) { throw "robocopy merge failed ($LASTEXITCODE)" }
}

Write-Host '=== Verify publish output ===' -ForegroundColor Cyan
$required = @(
  'KwanteksNicTweaker.exe',
  'KwanteksNicTweaker.dll',
  'KwanteksNicTweaker.deps.json',
  'KwanteksNicTweaker.runtimeconfig.json',
  'KwanteksNicTweaker.pri'
)
$missing = @()
foreach ($name in $required) {
  if (-not (Test-Path -LiteralPath (Join-Path $publishDir $name))) {
    $missing += $name
  }
}
if (-not (Get-ChildItem -LiteralPath $publishDir -Filter '*.xbf' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1)) {
  $missing += '*.xbf (XAML binary)'
}

# Runtime / WinUI presence (folder publish; casing varies)
$dllGlobs = @(
  'Microsoft.WindowsAppRuntime.dll',
  'Microsoft.ui.xaml.dll',
  'coreclr.dll',
  'hostfxr.dll'
)
foreach ($g in $dllGlobs) {
  $hit = Get-ChildItem -LiteralPath $publishDir -Filter $g -ErrorAction SilentlyContinue |
    Select-Object -First 1
  if (-not $hit) {
    $hit = Get-ChildItem -LiteralPath $publishDir -Filter ($g -replace 'ui','UI') -ErrorAction SilentlyContinue |
      Select-Object -First 1
  }
  if (-not $hit) { $missing += $g }
}

if ($missing.Count -gt 0) {
  throw ("Publish output incomplete. Missing:`n  - " + ($missing -join "`n  - "))
}

# Symbols zip (PDBs out of installer)
$pdbs = Get-ChildItem -LiteralPath $publishDir -Filter *.pdb -Recurse -ErrorAction SilentlyContinue
if ($pdbs) {
  $pdbs | ForEach-Object {
    $rel = $_.FullName.Substring($publishDir.Length).TrimStart('\')
    $dest = Join-Path $symbolsDir $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
    Move-Item -LiteralPath $_.FullName -Destination $dest -Force
  }
  $symZip = Join-Path $distDir "KwanteksNICTweaker-symbols-$version.zip"
  if (Test-Path $symZip) { Remove-Item $symZip -Force }
  Compress-Archive -Path (Join-Path $symbolsDir '*') -DestinationPath $symZip -CompressionLevel Optimal
  Write-Host "Symbols: $symZip"
}

# Fail the build if a shipped artifact still embeds the local profile path.
Write-Host '=== Privacy scan ===' -ForegroundColor Cyan
$userNeedle = $env:USERNAME
$profileNeedle = $env:USERPROFILE
$leakHits = @()
$scanFiles = @()
$scanFiles += Get-ChildItem -LiteralPath $publishDir -Recurse -Include *.exe,*.dll -File -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $symbolsDir) {
  $scanFiles += Get-ChildItem -LiteralPath $symbolsDir -Recurse -Include *.pdb -File -ErrorAction SilentlyContinue
}
foreach ($f in $scanFiles) {
  $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
  $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
  $uni = [System.Text.Encoding]::Unicode.GetString($bytes)
  $blob = $ascii + "`n" + $uni
  if ($blob.Contains($profileNeedle)) {
    $leakHits += "$($f.FullName) contains USERPROFILE"
  }
  elseif ($blob.Contains("Users\$userNeedle") -or $blob.Contains("Users/$userNeedle")) {
    $leakHits += "$($f.FullName) contains Users\$userNeedle"
  }
}
if ($leakHits.Count -gt 0) {
  throw ("Privacy leak in build output:`n  - " + ($leakHits -join "`n  - "))
}
Write-Host 'Privacy scan OK'

Write-Host "Publish OK: $publishDir ($((Get-ChildItem $publishDir -Recurse -File).Count) files)"

if ($SkipInstaller) {
  Write-Host 'SkipInstaller set — done.'
  exit 0
}

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
  Write-Host ''
  Write-Host 'Inno Setup 6 (ISCC.exe) not found.' -ForegroundColor Yellow
  Write-Host 'Download: https://jrsoftware.org/isdl.php'
  Write-Host 'Or: winget install JRSoftware.InnoSetup'
  throw 'ISCC.exe missing'
}

Write-Host '=== Inno Setup ===' -ForegroundColor Cyan
$issArgs = @(
  $iss,
  "/DMyAppVersion=$version",
  "/DMyAppSource=$publishDir",
  "/DMyOutputDir=$distDir"
)
& $iscc @issArgs
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$setup = Join-Path $distDir "KwanteksNICTweaker-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $setup)) {
  throw "Installer not produced: $setup"
}

if ($Sign) {
  Write-Host "Signing with thumbprint $Sign ..."
  & signtool sign /sha1 $Sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $setup
  if ($LASTEXITCODE -ne 0) { throw 'signtool failed' }
}

$sizeMb = [math]::Round((Get-Item -LiteralPath $setup).Length / 1MB, 1)
Write-Host ''
Write-Host "OK: $setup ($sizeMb MB)" -ForegroundColor Green
