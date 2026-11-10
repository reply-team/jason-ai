<#
.SYNOPSIS
Refuses a desktop update feed, latest.yml or latest-mac.yml, that names a version other than -Version, names no file,
lists files other than exactly -Expect, or names a file whose bytes beside it have a digest or a size other than the feed
says.

.DESCRIPTION
electron-updater downloads the file a feed names and installs it only when its SHA-512 is the one the feed gives, and it
reads the size for its download. So every file the feed lists under files:, and the one its path: names, is read again
from the folder the feed is in, as it will be published, and its SHA-512 (base64) and its size in bytes are held to what
the feed says. A file the feed names must be beside it: a name with a folder in it is refused. Each file is printed with
its digest and its size. The files it lists are exactly -Expect, in any order, given as names or as one comma-separated
argument: a Mac feed lists both architectures' zips, and a feed that names one alone is refused.

The blockmap beside each file is not read. electron-updater uses it only to download the part of a file that changed,
and falls back to the whole file when the part it built does not match; what it installs is decided by the SHA-512 this
script holds.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string[]] $Expect
)
$ErrorActionPreference = 'Stop'
$Expect = @($Expect | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

# A refusal is one plain line on standard error and exit 1: what a log, a person and a test all read the same way.
function Refuse([string] $Why) {
    [Console]::Error.WriteLine($Why)
    exit 1
}

if (-not (Test-Path -LiteralPath $Feed -PathType Leaf)) { Refuse "There is no feed at $Feed." }
$path = (Resolve-Path -LiteralPath $Feed).Path
$name = Split-Path -Leaf $path
$folder = Split-Path -Parent $path
$lines = @(Get-Content -LiteralPath $path)

if (@($lines | Where-Object { $_ -ceq "version: $Version" }).Count -ne 1) { Refuse "$name does not name $Version." }

# The feed as electron-builder writes it: files: a list of url, sha512, size and other keys, then path: and sha512: for
# the file an older updater reads.
$entries = [System.Collections.Generic.List[hashtable]]::new()
$current = $null
$listing = $false
$named = $null
$namedDigest = $null
foreach ($line in $lines) {
    if ($line -cmatch '^files:\s*$') { $listing = $true; continue }
    if ($listing -and $line -cmatch '^  - url: (\S+)\s*$') {
        $current = @{ Url = $Matches[1]; Sha512 = $null; Size = $null }
        $entries.Add($current)
        continue
    }
    if ($listing -and $null -ne $current -and $line -cmatch '^    sha512: (\S+)\s*$') { $current.Sha512 = $Matches[1]; continue }
    if ($listing -and $null -ne $current -and $line -cmatch '^    size: (\S+)\s*$') { $current.Size = $Matches[1]; continue }
    if ($line -cmatch '^\S') { $listing = $false; $current = $null }
    if ($line -cmatch '^path: (\S+)\s*$') { $named = $Matches[1] }
    if ($line -cmatch '^sha512: (\S+)\s*$') { $namedDigest = $Matches[1] }
}

if ($entries.Count -eq 0) { Refuse "$name names no file." }

foreach ($entry in $entries) {
    $url = $entry.Url
    if (-not $entry.Sha512 -or -not $entry.Size) { Refuse "$name names $url without its sha512 and its size." }
    if ($url -match '[\\/]' -or $url -eq '..' -or $url -eq '.') { Refuse "$name names $url, a file outside the folder it is in." }
    $file = Join-Path $folder $url
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { Refuse "$name names $url, which is not beside it." }

    $stream = [System.IO.File]::OpenRead($file)
    try { $digest = [Convert]::ToBase64String([System.Security.Cryptography.SHA512]::HashData($stream)) } finally { $stream.Dispose() }
    $size = (Get-Item -LiteralPath $file).Length
    if ($digest -cne $entry.Sha512) { Refuse "$name says $url has the SHA-512 $($entry.Sha512), and the file beside it has $digest." }
    if ("$size" -cne $entry.Size) { Refuse "$name says $url is $($entry.Size) bytes, and the file beside it is $size." }
    $entry.Digest = $digest
    Write-Host "${url}: sha512 $digest, $size bytes, as $name says"
}

$lists = @($entries | ForEach-Object { $_.Url } | Sort-Object -CaseSensitive)
$expected = @($Expect | Sort-Object -CaseSensitive)
if (($lists -join ', ') -cne ($expected -join ', ')) { Refuse "$name lists $($lists -join ', '), and it must list exactly $($expected -join ', ')." }

if (-not $named -or -not $namedDigest) { Refuse "$name has no path: and sha512: for the file an older updater reads." }
$listed = @($entries | Where-Object { $_.Url -ceq $named })
if ($listed.Count -ne 1) { Refuse "$name says its path is $named, which it does not list under files:." }
if ($namedDigest -cne $listed[0].Digest) { Refuse "$name says its path $named has the SHA-512 $namedDigest, and the file beside it has $($listed[0].Digest)." }
