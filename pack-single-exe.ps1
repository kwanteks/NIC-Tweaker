# Builds a working single EXE wrapper (WinUI cannot use PublishSingleFile reliably).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$appProj = Join-Path $root "Kwanteks' NIC Tweaker\Kwanteks' NIC Tweaker.csproj"
$hostDir = Join-Path $root 'KwanteksNicTweaker.Host'
$pub = Join-Path $root "Kwanteks' NIC Tweaker\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish-portable"
$zip = Join-Path $hostDir 'payload.zip'
$out = Join-Path $root 'dist'

if (Test-Path 'C:\temp\nictweaker\Kwanteks'' NIC Tweaker.csproj') {
  $appProj = "C:\temp\nictweaker\Kwanteks' NIC Tweaker.csproj"
  $pub = 'C:\temp\nictweaker\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish-portable'
}

Write-Host 'Publishing WinUI app (multi-file)...'
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish $appProj -c Release -r win-x64 -p:Platform=x64 -p:DebugType=None -p:DebugSymbols=false -o $pub
if ($LASTEXITCODE -ne 0) { throw 'app publish failed' }

Write-Host 'Zipping payload...'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pub '*') -DestinationPath $zip -CompressionLevel Optimal

Write-Host 'Publishing single-file host...'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null
dotnet publish (Join-Path $hostDir 'KwanteksNicTweaker.Host.csproj') -c Release -r win-x64 -o $out
if ($LASTEXITCODE -ne 0) { throw 'host publish failed' }

$exe = Join-Path $out 'KwanteksNicTweaker.exe'
Write-Host "OK: $exe ($([math]::Round((Get-Item $exe).Length/1MB,1)) MB)"
