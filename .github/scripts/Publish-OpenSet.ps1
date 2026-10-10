<#
.SYNOPSIS
Makes a checkout of the public repository exactly what a release publishes from this one.

.DESCRIPTION
What a release publishes is a projection of this repository at the release's tag: the open set, as
.github/publish/open-set.txt lists it; LICENSE as its MIT block alone, since the scope paragraph above that block is
about this repository; a VERSION file naming the release; and the public repository's own files, written here under
.github/publish/public/ and placed at that repository's root. The target becomes exactly that projection: everything
in it but .git is removed first, so the public tree is a function of the tag and the version, and nothing in it is
ever kept from a hand edit.

The list is read again here and refused if it names something that is never published, although a test holds the
list to its rules: the script does not trust the test.

.PARAMETER Source
A checkout of this repository, at the tag being released.

.PARAMETER Target
A checkout of the public repository: a directory holding .git. Required unless -ListOnly.

.PARAMETER Version
The release's version, without a leading v. Written to VERSION. Required unless -ListOnly.

.PARAMETER ListOnly
Print the entries of the list, one per line, and stop: how a test holds this reader to its own.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Source,
    [string] $Target,
    [string] $Version,
    [switch] $ListOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$utf8 = [Text.UTF8Encoding]::new($false)
$list = Join-Path $Source '.github/publish/open-set.txt'
$text = [IO.File]::ReadAllText($list)
[string[]] $entries = $text.Split("`n")
if ($text.EndsWith("`n")) { $entries = $entries[0..($entries.Count - 2)] }
for ($index = 0; $index -lt $entries.Count; $index++) {
    $entry = $entries[$index]
    $at = "open-set.txt, line $($index + 1)"
    $segments = $entry.TrimEnd('/').Split('/')
    if ($entry -eq '') { throw "$at is blank." }
    if ($entry -ne $entry.Trim()) { throw "$at has whitespace around '$($entry.Trim())'." }
    if ($entry.StartsWith('#')) { throw "$at is a comment; the list holds paths only." }
    if ($entry -match '[*?\[\]{}]') { throw "$at, '$entry', is a pattern; the list names paths, not globs." }
    if ($entry.StartsWith('/') -or $entry.Contains('\') -or @($segments | Where-Object { $_ -in '', '.', '..' }).Count -gt 0) {
        throw "$at, '$entry', is not a plain path relative to the repository root."
    }

    if ($segments[0] -in 'runtime', '.github') { throw "$at, '$entry', is never published." }
    if (-not (Test-Path -LiteralPath (Join-Path $Source $entry.TrimEnd('/')))) { throw "$at, '$entry', does not exist." }
}

if ($ListOnly) {
    $entries
    return
}

if ([string]::IsNullOrWhiteSpace($Target) -or [string]::IsNullOrWhiteSpace($Version)) {
    throw '-Target and -Version are required unless -ListOnly.'
}

if (-not (Test-Path -LiteralPath (Join-Path $Target '.git'))) {
    throw "'$Target' is not a checkout: it holds no .git."
}

# Every file under a directory, as paths relative to it with forward slashes, hidden ones included.
function Get-Files([string] $root) {
    [IO.Directory]::EnumerateFiles($root, '*', [IO.SearchOption]::AllDirectories) |
        ForEach-Object { [IO.Path]::GetRelativePath($root, $_).Replace('\', '/') } |
        Sort-Object { $_ } -CaseSensitive
}

# One file copied byte for byte, its directory made first. File.Copy keeps a file's mode on Unix, which is what keeps
# install.sh executable in the public tree.
function Copy-One([string] $from, [string] $to) {
    $directory = [IO.Path]::GetDirectoryName($to)
    if ($directory) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
    [IO.File]::Copy($from, $to, $true)
}

$stage = Join-Path ([IO.Path]::GetTempPath()) ('jason-publish-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
try {
    $published = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

    foreach ($entry in $entries) {
        $from = Join-Path $Source $entry.TrimEnd('/')
        if ($entry -eq 'LICENSE') {
            # The MIT block alone: from the one line that is exactly "MIT License" to the end, byte for byte.
            $license = [IO.File]::ReadAllText($from, $utf8)
            $starts = [regex]::Matches($license, '(?m)^MIT License\r?$')
            if ($starts.Count -ne 1) { throw "LICENSE holds $($starts.Count) lines reading 'MIT License'; the block to publish starts at exactly one." }
            [IO.File]::WriteAllText((Join-Path $stage 'LICENSE'), $license.Substring($starts[0].Index), $utf8)
            [void] $published.Add('LICENSE')
        } elseif ($entry.EndsWith('/')) {
            foreach ($relative in Get-Files $from) {
                $path = $entry + $relative
                Copy-One (Join-Path $from $relative) (Join-Path $stage $path)
                [void] $published.Add($path)
            }
        } else {
            Copy-One $from (Join-Path $stage $entry)
            [void] $published.Add($entry)
        }
    }

    [IO.File]::WriteAllText((Join-Path $stage 'VERSION'), "$Version`n", $utf8)
    [void] $published.Add('VERSION')

    $own = Join-Path $Source '.github/publish/public'
    foreach ($relative in Get-Files $own) {
        if (-not $published.Add($relative)) { throw "The public repository's own file '$relative' is also a path the open set publishes." }
        Copy-One (Join-Path $own $relative) (Join-Path $stage $relative)
    }

    # The target becomes the projection: everything but .git goes, then the projection comes in.
    foreach ($item in [IO.Directory]::EnumerateFileSystemEntries($Target)) {
        if ([IO.Path]::GetFileName($item) -eq '.git') { continue }
        if ([IO.Directory]::Exists($item)) { [IO.Directory]::Delete($item, $true) } else { [IO.File]::Delete($item) }
    }

    foreach ($relative in Get-Files $stage) {
        Copy-One (Join-Path $stage $relative) (Join-Path $Target $relative)
    }

    Write-Host "The public tree is Jason $Version's projection: $($published.Count) files."
} finally {
    [IO.Directory]::Delete($stage, $true)
}
