<#
.SYNOPSIS
Writes the feed a release's proof job installs from.

.DESCRIPTION
The proof jobs run before the release job assembles anything, on the files the platform's job and the skills' job
uploaded: downloaded into one directory, this writes the manifest and the checksums over them as the release job writes
them, naming the one platform the job proves and the files its install reads. The release's own manifest is written by the
release job alone.

.PARAMETER Rid
The platform the proof job installs: win-x64 or osx-arm64.

.PARAMETER Version
The release's version.

.PARAMETER Directory
The directory the platform's archive and fragment, the desktop's files and the skills' package were downloaded into.
#>
param(
    [Parameter(Mandatory)] [ValidateSet('win-x64', 'osx-arm64')] [string] $Rid,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Directory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$files = switch ($Rid) {
    'win-x64' { @('jason-skills.zip', 'jason-desktop-windows-x64-setup.exe', 'jason-desktop-windows-x64-setup.exe.blockmap', 'latest.yml') }
    'osx-arm64' { @('jason-skills.zip', 'jason-desktop-macos-arm64.dmg', 'jason-desktop-macos-arm64.zip', 'jason-desktop-macos-arm64.zip.blockmap', 'jason-desktop-macos-x64.dmg', 'jason-desktop-macos-x64.zip', 'jason-desktop-macos-x64.zip.blockmap', 'latest-mac.yml') }
}

& (Join-Path $PSScriptRoot 'Write-Manifest.ps1') -Version $Version -Directory $Directory -Require $Rid -Plugins $files
Get-Content -LiteralPath (Join-Path $Directory 'manifest.json') | ForEach-Object { Write-Host $_ }
