<#
.SYNOPSIS
Fills a feed fixture under install/feeds/ over archives this run built, so an install script can be run against it.

.DESCRIPTION
A fixture is a release's manifest.json and checksums.txt in the layout of the schema it is named for, with each
value replaced by a placeholder: {{version}}, {{published_at}}, {{sha256:<rid>}} and {{size:<rid>}}. This fills
every placeholder from the archives in -Archives, the version and the time it is given, and writes the two files
beside a copy of each archive in -Out: a directory an installer's --feed / -Feed reads exactly as it reads a
release.

The fixture's checksums.txt names each platform's archive beside its digest placeholder, so the archive a
platform is filled from is the name the fixture itself gives it. A placeholder this cannot fill, or an archive the
fixture names that is not in -Archives, is an error, and nothing is written.

.PARAMETER Fixture
The fixture's directory, such as install/feeds/schema-1.

.PARAMETER Archives
Where the archives were built or downloaded to.

.PARAMETER Version
The version the filled feed advertises, without a leading v.

.PARAMETER PublishedAt
The feed's published_at, ISO-8601 UTC. Now, when it is not given.

.PARAMETER Out
Where the filled feed is written. Created if it does not exist.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Fixture,
    [Parameter(Mandatory)] [string] $Archives,
    [Parameter(Mandatory)] [string] $Version,
    [string] $PublishedAt = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture),
    [Parameter(Mandatory)] [string] $Out
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$manifest = [IO.File]::ReadAllText((Join-Path $Fixture 'manifest.json'))
$checksums = [IO.File]::ReadAllText((Join-Path $Fixture 'checksums.txt'))

$values = @{ 'version' = $Version; 'published_at' = $PublishedAt }
$copies = @()
foreach ($line in @(($checksums -split "`n") | ForEach-Object { $_.TrimEnd("`r") })) {
    if ($line -notmatch '^\{\{sha256:(?<rid>[a-z0-9-]+)\}\}  (?<asset>\S+)$') { continue }
    $archive = Join-Path $Archives $Matches.asset
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { throw "The fixture names $($Matches.asset), and $Archives has no such archive." }

    $values["sha256:$($Matches.rid)"] = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $values["size:$($Matches.rid)"] = [string](Get-Item -LiteralPath $archive).Length
    $copies += $archive
}

$fill = {
    param([string] $template)
    [regex]::Replace($template, '\{\{([a-z0-9_]+(?::[a-z0-9-]+)?)\}\}', {
        param($match)
        $name = $match.Groups[1].Value
        if (-not $values.ContainsKey($name)) { throw "The fixture names {{$name}}, and no value was found for it." }
        $values[$name]
    })
}

$filledManifest = & $fill $manifest
$filledChecksums = & $fill $checksums

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $Out 'manifest.json'), $filledManifest, $utf8)
[IO.File]::WriteAllText((Join-Path $Out 'checksums.txt'), $filledChecksums, $utf8)
foreach ($archive in $copies) { Copy-Item -LiteralPath $archive -Destination $Out -Force }

Write-Host "Filled $Fixture for $Version into $Out ($($copies.Count) archives)."
