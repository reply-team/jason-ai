<#
.SYNOPSIS
Writes the skills' package as the zip a release publishes beside its archives.

.DESCRIPTION
One entry per file under the tree's skills/ directory, named by its path relative to the tree with forward slashes,
so the zip holds skills/runtime/... and skills/business/... as the tree does; no entry for a directory; the bytes as
they are on disk, with no line-ending conversion. Files whose names start with a dot are kept: each pack's
.claude-plugin/plugin.json is one, and an agent host reads a pack by it. Beside skills/ at the zip's root, source.json
says where the files came from: the repository, the ref and the commit, which is what `jason skills install` records
for a deployment from this package. Nothing else is in the zip.

.PARAMETER Tree
The checkout whose skills/ is archived, the release's own.

.PARAMETER Archive
The zip to write, a relative path being from where PowerShell stands. An archive already there is replaced.

.PARAMETER Repository
The repository the tree is a checkout of, as an https address.

.PARAMETER Ref
The tag the release publishes, v<version>.

.PARAMETER Commit
The commit the tree is, forty hexadecimal digits.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Tree,
    [Parameter(Mandatory)] [string] $Archive,
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [string] $Ref,
    [Parameter(Mandatory)] [string] $Commit
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Repository -cnotmatch '^https://[^\s]+$') { throw "-Repository must be an https address; '$Repository' is not one." }
if ($Ref -cnotmatch '^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$') { throw "-Ref must be v and a version; '$Ref' is not one." }
if ($Commit -cnotmatch '^[0-9a-f]{40}$') { throw "-Commit must be forty lowercase hexadecimal digits; '$Commit' is not." }

$root = (Resolve-Path -LiteralPath $Tree).Path
$skills = Join-Path $root 'skills'
if (-not (Test-Path -LiteralPath $skills -PathType Container)) { throw "$Tree holds no skills directory." }

# Absolute before .NET sees it: a relative path is the caller's, measured from where PowerShell stands, and .NET would
# measure it from the process's own directory, which a Set-Location does not move.
$Archive = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Archive)

[string[]] $files = @(
    Get-ChildItem -LiteralPath $skills -Recurse -File -Force |
        ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') }
)
[Array]::Sort($files, [StringComparer]::Ordinal)

# Written by hand rather than by ConvertTo-Json, whose layout differs between PowerShell versions: the same tree gives
# the same bytes wherever the release runs.
$source = "{`n  `"repository`": `"$Repository`",`n  `"ref`": `"$Ref`",`n  `"commit`": `"$Commit`"`n}`n"

$directory = Split-Path -Parent $Archive
if ($directory) { New-Item -ItemType Directory -Force $directory | Out-Null }
if (Test-Path -LiteralPath $Archive) { Remove-Item -LiteralPath $Archive }

$zip = [IO.Compression.ZipFile]::Open($Archive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $root $file), $file, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }

    $entry = $zip.CreateEntry('source.json', [IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($source)
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally {
        $stream.Dispose()
    }
}
finally {
    $zip.Dispose()
}

Write-Host "$Archive holds $($files.Count) files of skills/ at $Ref and source.json."
