<#
.SYNOPSIS
Signs every native library a macOS single-file bundle is about to carry with the release's Developer ID, the way the
executable itself is signed, and refuses one that does not come out signed by a Developer ID Application identity.

.DESCRIPTION
The bundle unpacks its native libraries on the first run and the system loads them from there into the executable,
which is signed under the hardened runtime; library validation then loads only a library signed by the executable's
own team. The package action's first publish copies the libraries into -Directory, this script signs each one in place
(the hardened runtime, Apple's timestamp, --force to replace the ad hoc signature the linker wrote), verifies it and
prints what it now carries, and the action's second publish bundles them from there. A directory with no library in it
is refused: the bundle carries SQLite's, so finding none means the first publish copied nothing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Directory,
    [Parameter(Mandatory)] [string] $Identity,
    [Parameter(Mandatory)] [string] $Keychain
)
$ErrorActionPreference = 'Stop'

$libraries = @(Get-ChildItem -LiteralPath $Directory -Recurse -File)
if ($libraries.Count -eq 0) {
    throw "$Directory holds no native library: the first publish copied none out of the bundle, and the bundle carries SQLite's."
}

foreach ($library in $libraries) {
    $path = $library.FullName
    Write-Host "--- $path"

    & codesign --force --sign $Identity --keychain $Keychain --options runtime --timestamp $path
    if ($LASTEXITCODE -ne 0) { throw "codesign exited with $LASTEXITCODE signing $path." }

    & codesign --verify --strict --verbose=2 $path
    if ($LASTEXITCODE -ne 0) { throw "codesign exited with $LASTEXITCODE verifying $path." }

    # codesign writes what a signature says on standard error; both streams are read, as one.
    $signature = @(& codesign --display --verbose=2 $path 2>&1 | ForEach-Object { "$_" })
    $signature | ForEach-Object { Write-Host $_ }
    if (-not ($signature | Where-Object { $_.StartsWith('Authority=Developer ID Application: ', [StringComparison]::Ordinal) })) {
        throw "$path does not carry a Developer ID Application signature: what codesign said is above."
    }
}

Write-Host "$($libraries.Count) native libraries signed."
