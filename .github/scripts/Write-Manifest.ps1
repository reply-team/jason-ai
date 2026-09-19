<#
.SYNOPSIS
Assembles a release's manifest.json and checksums.txt from the fragments the packaging action wrote.

.DESCRIPTION
Each platform's packaging uploads three files: the archive, its .sha256, and a one-entry fragment of the
manifest — {"<rid>":{"asset":…,"sha256":…,"size":…}}. The release job downloads them all into one directory
and runs this, which merges the fragments into the manifest a running Jason reads, and writes checksums.txt in
sha256sum's format beside it.

Every archive is hashed again here. The manifest repeats a claim about bytes, and it repeats it only after
checking: a platform named in -Require that has no fragment, a fragment whose archive is not in the directory,
or an archive that no longer hashes to what its fragment says, is an error, and nothing is written.

.PARAMETER Version
The release's version, without a leading v. The workflow decided it from the tag; it is not judged again here.

.PARAMETER Directory
Where the archives and the fragments were downloaded to, and where manifest.json and checksums.txt are written.

.PARAMETER Require
The platforms the release must carry, as a list or comma-separated (-Require win-x64,linux-x64,osx-arm64 reads
the same either way a caller writes it). They lead the manifest in this order; a fragment for any other platform
is kept after them.

.PARAMETER ReleaseNotesUrl
The page the release notes are on, or nothing: a release with no notes is a release.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Directory,
    [Parameter(Mandatory)] [string[]] $Require,
    [string] $ReleaseNotesUrl
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# A list either way it is written. A workflow writes its invocation inline, where PowerShell parses
# `-Require win-x64,linux-x64,osx-arm64` as three arguments; a caller handing the same text over as one argv
# element means one string holding commas. Both are the same list, and this is the line that says so.
$required = @($Require | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

$found = @{}
foreach ($file in Get-ChildItem -Path $Directory -Filter '*.fragment.json' -File) {
    $fragment = Get-Content -Path $file.FullName -Raw | ConvertFrom-Json -AsHashtable
    if ($fragment.Count -ne 1) { throw "$($file.Name) names $($fragment.Count) platforms; a fragment names one." }
    $rid = @($fragment.Keys)[0]
    if ($found.ContainsKey($rid)) { throw "Two fragments name $rid." }
    $entry = $fragment[$rid]

    $archive = Join-Path $Directory $entry.asset
    if (-not (Test-Path -Path $archive -PathType Leaf)) { throw "$($file.Name) names $($entry.asset), which is not in $Directory." }

    $hash = (Get-FileHash -Path $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item -Path $archive).Length
    if ($hash -ne $entry.sha256 -or $size -ne $entry.size) {
        throw "$($entry.asset) is not the file $($file.Name) describes: it hashes to $hash ($size bytes), and the fragment says $($entry.sha256) ($($entry.size) bytes)."
    }

    $found[$rid] = [ordered]@{ asset = $entry.asset; sha256 = $hash; size = [long]$size }
}

foreach ($rid in $required) {
    if (-not $found.ContainsKey($rid)) { throw "No fragment for $rid; a release carries every platform it promises." }
}

$artifacts = [ordered]@{}
foreach ($rid in $required + @($found.Keys | Where-Object { $_ -notin $required } | Sort-Object)) {
    $artifacts[$rid] = $found[$rid]
}

$manifest = [ordered]@{
    schema            = 1
    version           = $Version
    published_at      = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
    release_notes_url = if ($ReleaseNotesUrl) { $ReleaseNotesUrl } else { $null }
    min_upgrade_from  = $null
    artifacts         = $artifacts
}

# Unix newlines whatever this runs on: checksums.txt is read by sha256sum on machines that are not this one,
# and the manifest is diffed by people.
$json = ($manifest | ConvertTo-Json -Depth 4) -replace "`r`n", "`n"
Set-Content -Path (Join-Path $Directory 'manifest.json') -Value ($json + "`n") -NoNewline

$lines = foreach ($rid in $artifacts.Keys) { "$($artifacts[$rid].sha256)  $($artifacts[$rid].asset)" }
Set-Content -Path (Join-Path $Directory 'checksums.txt') -Value (($lines -join "`n") + "`n") -NoNewline
