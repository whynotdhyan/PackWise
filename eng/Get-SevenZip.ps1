<#
.SYNOPSIS
    Fetches the pinned, official 7-Zip engine (7z.dll) used by PackWise.

.DESCRIPTION
    Downloads the official 7-Zip MSI named in third_party/7zip/engine.json,
    verifies its SHA-256 digest, performs an administrative (extract-only)
    install into a scratch folder and copies 7z.dll and License.txt into
    third_party/7zip. Nothing is installed on the machine.

.PARAMETER Force
    Re-download even if 7z.dll already matches the pinned version.
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$engineDir = Join-Path $root 'third_party\7zip'
$manifest = Get-Content (Join-Path $engineDir 'engine.json') -Raw | ConvertFrom-Json
$targetDir = Join-Path $engineDir 'x64'
$targetDll = Join-Path $targetDir '7z.dll'

if (-not $Force -and (Test-Path $targetDll)) {
    $current = (Get-Item $targetDll).VersionInfo.ProductVersion
    if ($current -eq $manifest.version) {
        Write-Host "7z.dll $current is already present."
        return
    }
}

$work = Join-Path $engineDir '.download'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null

try {
    $msi = Join-Path $work 'engine.msi'
    Write-Host "Downloading 7-Zip $($manifest.version)..."
    Invoke-WebRequest -Uri $manifest.source -OutFile $msi -UseBasicParsing

    $actual = (Get-FileHash $msi -Algorithm SHA256).Hash
    if ($actual -ne $manifest.sha256) {
        throw "SHA-256 mismatch for $($manifest.source). Expected $($manifest.sha256), got $actual."
    }

    $extractDir = Join-Path $work 'extract'
    $process = Start-Process msiexec.exe -ArgumentList @('/a', "`"$msi`"", '/qn', "TARGETDIR=`"$extractDir`"") -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "msiexec administrative extraction failed with exit code $($process.ExitCode)."
    }

    $dll = Get-ChildItem $extractDir -Recurse -Filter '7z.dll' | Select-Object -First 1
    $license = Get-ChildItem $extractDir -Recurse -Filter 'License.txt' | Select-Object -First 1
    if (-not $dll -or -not $license) {
        throw 'The downloaded package does not contain 7z.dll and License.txt.'
    }

    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Copy-Item $dll.FullName $targetDll -Force
    Copy-Item $license.FullName (Join-Path $engineDir 'License.txt') -Force
    Write-Host "7z.dll $((Get-Item $targetDll).VersionInfo.ProductVersion) is ready."
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
