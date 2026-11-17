<#
.SYNOPSIS
Builds Jason Desktop around an executable this run packaged, for one platform, as CI's test build: the package a release
builds, with every signature step and check off. Run by ci.yml's two proof jobs alone, from desktop/.

.DESCRIPTION
The step that runs this sets JASON_DESKTOP_TEST_BUILD=1, which electron-builder.config.mjs reads; this script never sets
it, so nothing that runs it elsewhere builds a test build by accident. The bundle is prepared from -Executable at
-Version, as the release prepares its own, and the package is the platform's own: on Windows the nsis installer, its block
map and latest.yml; on macOS the zip of the runner's own architecture, its block map and latest-mac.yml.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('win-x64', 'osx-arm64')] [string] $Rid,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Executable
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

if ($env:JASON_DESKTOP_TEST_BUILD -ne '1') { throw 'The step that runs this asks for a test build: JASON_DESKTOP_TEST_BUILD=1.' }

npm ci --ignore-scripts
if ($LASTEXITCODE -ne 0) { throw "npm ci exited with $LASTEXITCODE" }

$folder, $platform = switch ($Rid) {
    'win-x64' { 'bundles/x64', '--win' }
    'osx-arm64' { 'bundles/arm64', '--mac' }
}
$architecture = if ($Rid -eq 'win-x64') { '--x64' } else { '--arm64' }
node scripts/prepare.mjs --rid $Rid --output $folder --version $Version --executable $Executable
if ($LASTEXITCODE -ne 0) { throw "prepare.mjs exited with $LASTEXITCODE" }

$env:JASON_VERSION = $Version
npm run package -- $platform $architecture
if ($LASTEXITCODE -ne 0) { throw "electron-builder exited with $LASTEXITCODE" }
Get-ChildItem dist -File | ForEach-Object { Write-Host "$($_.Name) $($_.Length)" }
exit 0
