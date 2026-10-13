<#
.SYNOPSIS
    Builds the PackWise release: installer, installer zip, portable zip and checksums.

.DESCRIPTION
    1. Runs the tests (unless -SkipTests).
    2. Publishes a self-contained PackWise for Windows x64.
    3. Packs it as the installer payload and builds PackWise-Setup.exe (.NET Framework 4.8,
       no prerequisites) with the payload embedded.
    4. Writes to artifacts/release:
         PackWise-<version>-Setup.exe       the installer
         PackWise-<version>-Setup.zip       the installer plus a short read-me
         PackWise-<version>-Portable.zip    run-anywhere copy, no installation
         SHA256SUMS.txt

.PARAMETER Version
    Version number to stamp into the build, for example 1.0.0.

.PARAMETER SkipTests
    Skip the test run (CI runs tests in a separate step).
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'artifacts\release'
$work = Join-Path $root 'artifacts\work'
$app = Join-Path $work 'PackWise'
$payload = Join-Path $work 'payload.zip'

foreach ($folder in $output, $work) {
    if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
    New-Item -ItemType Directory -Path $folder | Out-Null
}

Push-Location $root
try {
    if (-not $SkipTests) {
        dotnet test --project tests/PackWise.Tests -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }

    Write-Host "Publishing PackWise $Version..."
    dotnet publish src/PackWise/PackWise.csproj -c Release -r win-x64 --self-contained `
        -p:Version=$Version -p:PublishReadyToRun=true -p:DebugType=none -o $app
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item README.md, LICENSE, THIRD-PARTY-NOTICES.md $app

    Compress-Archive -Path (Join-Path $app '*') -DestinationPath $payload -CompressionLevel Optimal

    Write-Host 'Building the installer...'
    dotnet build installer/PackWise.Setup/PackWise.Setup.csproj -c Release -p:Version=$Version -p:PayloadPath=$payload -o (Join-Path $work 'setup')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

    $setup = Join-Path $output "PackWise-$Version-Setup.exe"
    Copy-Item (Join-Path $work 'setup\PackWise-Setup.exe') $setup

    # Installer zip: Setup.exe plus a short read-me for people who download the zip.
    $setupFolder = Join-Path $work "PackWise-$Version-Setup"
    New-Item -ItemType Directory -Path $setupFolder | Out-Null
    Copy-Item $setup $setupFolder
    @"
PackWise $Version - free, offline archive manager for Windows
by Northstar Labs

INSTALL
  Double-click PackWise-$Version-Setup.exe and choose Install.
  PackWise installs just for you. No administrator rights, account or internet connection needed.

FIRST STEPS
  A short tour opens the first time you start PackWise (Help > Getting started brings it back).
  Help > User guide explains every feature.

RIGHT-CLICK MENU
  In File Explorer, right-click files and choose PackWise. On Windows 11 it is under
  "Show more options" (Shift+F10).

UNINSTALL
  Windows Settings > Apps > Installed apps > PackWise > Uninstall.

If Windows SmartScreen shows "Windows protected your PC", choose "More info" > "Run anyway".
This appears for new apps that haven't built up a download reputation yet.

Licensed under the MIT License. Uses the 7-Zip engine by Igor Pavlov (GNU LGPL).
"@ | Set-Content (Join-Path $setupFolder 'Read me.txt') -Encoding utf8
    Compress-Archive -Path (Join-Path $setupFolder '*') -DestinationPath (Join-Path $output "PackWise-$Version-Setup.zip") -CompressionLevel Optimal

    # Portable zip: the app folder as-is; settings still live in %LOCALAPPDATA%.
    $portable = Join-Path $work "PackWise-$Version-Portable"
    Copy-Item $app $portable -Recurse
    Compress-Archive -Path $portable -DestinationPath (Join-Path $output "PackWise-$Version-Portable.zip") -CompressionLevel Optimal

    $sums = Get-ChildItem $output -File | Sort-Object Name | ForEach-Object {
        "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) *$($_.Name)"
    }
    $sums | Set-Content (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii

    Write-Host ''
    Write-Host "Release ready in $output"
    Get-ChildItem $output | ForEach-Object { Write-Host ("  {0,-34} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
}
finally {
    Pop-Location
}
