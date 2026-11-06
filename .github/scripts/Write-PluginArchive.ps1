<#
.SYNOPSIS
Writes a plugin's package as the zip a release publishes beside its archives.

.DESCRIPTION
One entry per file of the package, named by its path relative to the package with forward slashes, so the files sit
at the zip's root with no folder around them; no entry for a directory; the bytes as they are on disk, with no
line-ending conversion. A file whose path has a segment starting with a dot is left out: the package's content digest
does not count it, so the zip carries the package's identity and nothing outside it. Unpacked into an empty
directory, the zip therefore has the content digest the package has where it was archived, which is what a test
holds this script to.

.PARAMETER Package
The package's directory, plugins/<id> (plugins/reply, say), as the release's checkout holds it.

.PARAMETER Archive
The zip to write, a relative path being from where PowerShell stands. An archive already there is replaced.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Package,
    [Parameter(Mandatory)] [string] $Archive
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path -LiteralPath $Package).Path

# Absolute before .NET sees it: a relative path is the caller's, measured from where PowerShell stands, and .NET would
# measure it from the process's own directory, which a Set-Location does not move.
$Archive = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Archive)

[string[]] $files = @(
    Get-ChildItem -LiteralPath $root -Recurse -File -Force |
        ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') } |
        Where-Object { -not ($_.Split('/') | Where-Object { $_.StartsWith('.') }) }
)
[Array]::Sort($files, [StringComparer]::Ordinal)

$directory = Split-Path -Parent $Archive
if ($directory) { New-Item -ItemType Directory -Force $directory | Out-Null }
if (Test-Path -LiteralPath $Archive) { Remove-Item -LiteralPath $Archive }

$zip = [IO.Compression.ZipFile]::Open($Archive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $root $file), $file, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $zip.Dispose()
}

Write-Host "$Archive holds $($files.Count) files of $Package."
