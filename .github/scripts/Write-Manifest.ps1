<#
.SYNOPSIS
Assembles a release's manifest.json and checksums.txt from the fragments the packaging action wrote.

.DESCRIPTION
Each platform's packaging uploads three files: the archive, its .sha256, and a one-entry fragment of the
manifest — {"<rid>":{"asset":…,"sha256":…,"size":…}}. The release job downloads them all into one directory
and runs this, which merges the fragments into the manifest a running Jason reads, and writes checksums.txt in
sha256sum's format beside it: a line per archive, then a line per package the release carries.

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

.PARAMETER Plugins
The packages the release carries beside its archives (each plugin package, the UI's and the skills'), by file name,
as a list or comma-separated. Each must be in the directory; it is hashed here and listed in checksums.txt after the
archives, and never among the manifest's artifacts, which are platforms. A plugin's package is named in the manifest
only through -PluginDigests.

.PARAMETER ReleaseNotesUrl
The page the release notes are on, or nothing: a release with no notes is a release.

.PARAMETER MinUpgradeFrom
The oldest version that updates to this one in one apply, or nothing. A bare version, X.Y.Z, the version of a
published release: a pre-release, build metadata, or anything that does not parse is refused before either file is
written.

.PARAMETER PluginDigests
The digest list of each plugin package the release carries, by path (plugins/<id>.digests), as a list or
comma-separated, or nothing. A list holds a line per version, the version, a space and the package's content digest,
and its last line is the package the release publishes. The manifest names each package under its last field,
plugins.<id>: the last line's version and digest, the asset jason-plugin-<id>.zip, which must be among -Plugins, and
every line as recorded. With no list, plugins is null. A list that is not a file or holds no version, a line that is
not a version and a digest, and a version or a digest named twice are refused before either file is written.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Directory,
    [Parameter(Mandatory)] [string[]] $Require,
    [string[]] $Plugins = @(),
    [string] $ReleaseNotesUrl,
    [string] $MinUpgradeFrom,
    [string[]] $PluginDigests = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# A bare version, X.Y.Z, the version of a published release: a pre-release, build metadata, or anything that does not
# parse is refused before either file is written. Digits without a leading zero, as the runtime reads a version.
if ($MinUpgradeFrom -and $MinUpgradeFrom -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw "-MinUpgradeFrom must be a version like 0.19.0, not '$MinUpgradeFrom'."
}

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

# The packages: hashed before anything is written, so a package the release promises and the download did not
# bring stops the release here, with the manifest and the checksums unwritten.
$carried = @($Plugins | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$packages = foreach ($name in $carried) {
    if ($name -ne (Split-Path -Leaf $name)) { throw "$name is not a file name; a package is named as the release publishes it." }
    $package = Join-Path $Directory $name
    if (-not (Test-Path -Path $package -PathType Leaf)) { throw "The release carries the package $name, and it is not in $Directory." }
    [ordered]@{ asset = $name; sha256 = (Get-FileHash -Path $package -Algorithm SHA256).Hash.ToLowerInvariant() }
}

# The plugin packages the manifest names, read before anything is written. A list's last line is the package the
# release publishes, as the package's own test holds it; every line is how a reader holding an installed copy's digest
# says which version that copy is. Nothing is built or hashed here: the list is the tag's own record.
$named = [ordered]@{}
foreach ($list in @($PluginDigests | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
    if (-not (Test-Path -Path $list -PathType Leaf)) { throw "The digest list $list is not a file." }
    $id = [IO.Path]::GetFileNameWithoutExtension($list)
    $asset = "jason-plugin-$id.zip"
    if ($asset -cnotin $carried) { throw "$list is the digest list of $asset, which is not among -Plugins; a manifest names only a package the release carries." }

    $recorded = [ordered]@{}
    $latest = $null
    foreach ($line in Get-Content -Path $list) {
        if (-not $line.Trim() -or $line.StartsWith('#')) { continue }
        if ($line -cnotmatch '^((0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)) (sha256:[0-9a-f]{64})$') {
            throw "$list holds '$line', which is not a version like 0.9.0, a space and a sha256: digest."
        }

        $listed, $digest = $Matches[1], $Matches[5]
        if ($recorded.Contains($listed)) { throw "$list names $listed twice." }
        if ($recorded.Values -contains $digest) { throw "$list names the digest of $listed under another version too." }
        $recorded[$listed] = $digest
        $latest = $listed
    }

    if (-not $latest) { throw "$list names no version." }
    $named[$id] = [ordered]@{ version = $latest; asset = $asset; digest = $recorded[$latest]; recorded = $recorded }
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
    min_upgrade_from  = if ($MinUpgradeFrom) { $MinUpgradeFrom } else { $null }
    artifacts         = $artifacts
    plugins           = if ($named.Count) { $named } else { $null }
}

# Unix newlines whatever this runs on: checksums.txt is read by sha256sum on machines that are not this one,
# and the manifest is diffed by people.
$json = ($manifest | ConvertTo-Json -Depth 5) -replace "`r`n", "`n"
Set-Content -Path (Join-Path $Directory 'manifest.json') -Value ($json + "`n") -NoNewline

$lines = @(foreach ($rid in $artifacts.Keys) { "$($artifacts[$rid].sha256)  $($artifacts[$rid].asset)" }) + @(foreach ($package in $packages) { "$($package.sha256)  $($package.asset)" })
Set-Content -Path (Join-Path $Directory 'checksums.txt') -Value (($lines -join "`n") + "`n") -NoNewline
