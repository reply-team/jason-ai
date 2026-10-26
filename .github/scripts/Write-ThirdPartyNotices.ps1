<#
.SYNOPSIS
Writes THIRD-PARTY-NOTICES at the repository's root from what a release's executable is made of.

.DESCRIPTION
A release's executable is a self-contained single file. It carries the runtime pack of every shared framework its
projects reference (the .NET runtime, and ASP.NET Core's for the runtime's API), and every package of Jason.App's
closure. The file this writes holds, in order:

1. each runtime pack's LICENSE.TXT and THIRD-PARTY-NOTICES.TXT, as the pack ships them;
2. each package of the closure, with its version, licence, copyright line and source;
3. the text of each licence a package is used under, once, with the packages that use it.

Where it reads:
- the closure from runtime/src/Jason.App/obj/project.assets.json, so the solution is restored first;
- each package's licence, copyright and source from its .nuspec in the NuGet cache that restore used;
- the runtime packs at the version the SDK that global.json selects publishes with, for every platform a release ships.
  A pack not yet in the cache is restored into it by a project made for that in a temporary folder;
- the licence texts from .github/third-party-licences/, one file per SPDX identifier, each the SPDX License List's text
  (github.com/spdx/license-list-data, text/<identifier>.txt).

Texts are written as they ship, with line endings made LF and trailing spaces removed: the packs of different
platforms differ in those and in nothing else. Anything this script does not know how to write - a licence given as a
file rather than an SPDX expression, a licence with no text in .github/third-party-licences/, a package whose own
third-party notices are not one of the runtime packs' - stops it, naming the package.

Run it after a package or the SDK changes; ThirdPartyNoticesTests says when it is due. No test runs it: it reads this
machine's NuGet cache and, for a missing pack, the network.

.PARAMETER Repository
The checkout to write into. Defaults to the one this script is in.
#>
[CmdletBinding()]
param(
    [string] $Repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# The platforms a release ships: ReleaseAssets.Rids.
$rids = @('win-x64', 'linux-x64', 'osx-arm64')

function Get-NormalisedText([string] $Path) {
    $lines = ([IO.File]::ReadAllText($Path) -replace "`r`n", "`n") -split "`n" | ForEach-Object { $_.TrimEnd() }
    ($lines -join "`n").Trim("`n")
}

function Get-Field([xml] $Nuspec, [string] $Name) {
    $node = $Nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='$Name']")
    # The comma keeps PowerShell from enumerating the element into its child nodes on the way out.
    if ($null -ne $node) { , $node }
}

function Get-LicenceIds([string] $Expression) {
    $Expression -split '[\s()]+' | Where-Object { $_ -and $_ -notin @('AND', 'OR', 'WITH') }
}

# The closure, from the restore's own record.
$assetsPath = Join-Path $Repository 'runtime/src/Jason.App/obj/project.assets.json'
if (-not (Test-Path -LiteralPath $assetsPath)) {
    throw "$assetsPath does not exist: restore the solution first (dotnet restore runtime/Jason.slnx)."
}
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
$targetNames = @($assets.targets.Keys | Where-Object { $_ -notmatch '/' })
if ($targetNames.Count -ne 1 -or $targetNames[0] -notmatch '^net\d+\.\d+$') {
    throw "project.assets.json has no single netX.Y target: $($assets.targets.Keys -join ', ')."
}
$framework = $targetNames[0]
$target = $assets.targets[$framework]
$cache = @($assets.packageFolders.Keys)[0]

$keys = [string[]] @($target.Keys | Where-Object { $target[$_].type -eq 'package' })
# By name alone: ordered as name/version, Serilog.Sinks.File would come before Serilog.
[Array]::Sort[string]($keys, [Comparison[string]] { param($a, $b) [string]::CompareOrdinal(($a -split '/')[0], ($b -split '/')[0]) })

$shared = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
foreach ($each in $assets.project.frameworks.Values) {
    if ($each.ContainsKey('frameworkReferences')) { $each.frameworkReferences.Keys | ForEach-Object { [void] $shared.Add($_) } }
}
foreach ($library in $target.Values) {
    if ($library.ContainsKey('frameworkReferences')) { $library.frameworkReferences | ForEach-Object { [void] $shared.Add($_) } }
}
# The .NET runtime first: every other shared framework runs on it.
$shared = @('Microsoft.NETCore.App') + @($shared | Where-Object { $_ -ne 'Microsoft.NETCore.App' })

# The runtime packs: the version and name pattern the SDK that global.json selects knows for each shared framework.
Push-Location $Repository
try {
    $sdk = (& dotnet --version).Trim()
    $sdkRoot = (& dotnet --list-sdks | Where-Object { $_ -like "$sdk *" } | Select-Object -First 1) -replace '^\S+ \[(.*)\]$', '$1'
}
finally {
    Pop-Location
}
[xml] $bundled = Get-Content -LiteralPath (Join-Path $sdkRoot "$sdk/Microsoft.NETCoreSdk.BundledVersions.props") -Raw
$packs = foreach ($name in $shared) {
    $known = @($bundled.SelectNodes("//*[local-name()='KnownFrameworkReference']") |
        Where-Object { $_.GetAttribute('Include') -eq $name -and $_.GetAttribute('TargetFramework') -eq $framework })
    if ($known.Count -ne 1) { throw "SDK $sdk knows $($known.Count) references to $name for $framework, not one." }
    $pattern = $known[0].GetAttribute('RuntimePackNamePatterns')
    $version = $known[0].GetAttribute('LatestRuntimeFrameworkVersion')
    foreach ($rid in $rids) {
        $id = $pattern.Replace('**RID**', $rid)
        [pscustomobject] @{ Framework = $name; Rid = $rid; Id = $id; Version = $version; Path = Join-Path $cache "$($id.ToLowerInvariant())/$version" }
    }
}

$missing = @($packs | Where-Object { -not (Test-Path -LiteralPath $_.Path) })
if ($missing.Count -gt 0) {
    Write-Host "Restoring the runtime packs not in the cache: $(($missing | ForEach-Object { "$($_.Id) $($_.Version)" }) -join ', ')."
    $probe = Join-Path ([IO.Path]::GetTempPath()) "jason-notices-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory $probe | Out-Null
    try {
        Copy-Item (Join-Path $Repository 'global.json') $probe
        $references = ($shared | Where-Object { $_ -ne 'Microsoft.NETCore.App' } | ForEach-Object { "<FrameworkReference Include=""$_"" />" }) -join ''
        $project = "<Project Sdk=""Microsoft.NET.Sdk""><PropertyGroup><TargetFramework>$framework</TargetFramework>" +
            "<RuntimeIdentifiers>$($rids -join ';')</RuntimeIdentifiers><SelfContained>true</SelfContained></PropertyGroup>" +
            "<ItemGroup>$references</ItemGroup></Project>"
        Set-Content -LiteralPath (Join-Path $probe 'probe.csproj') -Value $project
        & dotnet restore (Join-Path $probe 'probe.csproj') --packages $cache | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Restoring the runtime packs failed." }
    }
    finally {
        Remove-Item -LiteralPath $probe -Recurse -Force -ErrorAction SilentlyContinue
    }
    $still = @($packs | Where-Object { -not (Test-Path -LiteralPath $_.Path) })
    if ($still.Count -gt 0) { throw "Still not in the cache: $(($still | ForEach-Object { "$($_.Id) $($_.Version)" }) -join ', ')." }
}

$out = [Text.StringBuilder]::new()
function Add-Line([string] $Text = '') { [void] $out.Append($Text).Append("`n") }
function Add-Heading([string] $Text, [char] $Rule) { Add-Line $Text; Add-Line ([string]::new($Rule, $Text.Length)) }

Add-Heading 'Third-party notices for the Jason runtime' '='
Add-Line
Add-Line 'The Jason executable is a self-contained .NET program. Its one file carries the'
Add-Line '.NET runtime, the ASP.NET Core shared framework and the packages listed below,'
Add-Line 'each used under the licence named here. This file holds, in order:'
Add-Line
Add-Line '  1. the runtime packs the executable is built from, with the licence and the'
Add-Line '     third-party notices each of them ships;'
Add-Line '  2. the packages, each with its version, licence, copyright and source;'
Add-Line '  3. the text of each licence a package is used under, once, with the packages'
Add-Line '     that use it.'
Add-Line
Add-Line
Add-Heading '1. The runtime packs' '='

# Each pack's two texts, written once for the platforms whose packs hold the same ones.
$packNotices = @{}
foreach ($name in $shared) {
    $groups = [ordered] @{}
    foreach ($pack in @($packs | Where-Object Framework -eq $name)) {
        $files = @(Get-ChildItem -LiteralPath $pack.Path -File)
        $licence = @($files | Where-Object Name -match '^LICENSE\.txt$')
        $notices = @($files | Where-Object Name -match '^THIRD-PARTY-NOTICES\.txt$')
        if ($licence.Count -ne 1 -or $notices.Count -ne 1) { throw "$($pack.Id) $($pack.Version) has no LICENSE.TXT or no THIRD-PARTY-NOTICES.TXT." }
        $texts = @((Get-NormalisedText $licence[0].FullName), (Get-NormalisedText $notices[0].FullName))
        $key = $texts -join "`n`0`n"
        if (-not $groups.Contains($key)) { $groups[$key] = [pscustomobject] @{ Texts = $texts; Packs = [Collections.Generic.List[object]]::new() } }
        $groups[$key].Packs.Add($pack)
    }
    foreach ($group in $groups.Values) {
        $first = $group.Packs[0]
        Add-Line
        # To major.minor: the patch is the SDK's on the machine that runs this, and the executable is published with
        # whatever runtime pack the publishing machine's SDK carries, so a patch here would claim one nothing holds to.
        $majorMinor = ($first.Version -split '\.')[0..1] -join '.'
        Add-Heading "Runtime pack: $($first.Id) $majorMinor" '-'
        $others = @($group.Packs | Select-Object -Skip 1 | ForEach-Object Rid)
        if ($others.Count -gt 0) {
            Add-Line "The packs for $($others -join ' and ') hold the same two texts."
            Add-Line
        }
        Add-Line $group.Texts[0]
        Add-Line
        Add-Line $group.Texts[1]
        Add-Line
        $packNotices[$group.Texts[1]] = "$($first.Id) $($majorMinor)"
    }
}

Add-Line
Add-Heading '2. The packages' '='

$users = [Collections.Generic.SortedDictionary[string, Collections.Generic.List[string]]]::new([StringComparer]::Ordinal)
foreach ($key in $keys) {
    $name, $version = $key -split '/'
    $directory = Join-Path $cache $assets.libraries[$key].path
    [xml] $nuspec = Get-Content -LiteralPath (@(Get-ChildItem -LiteralPath $directory -Filter '*.nuspec')[0].FullName) -Raw

    $licence = Get-Field $nuspec 'license'
    if ($null -eq $licence -or $licence.GetAttribute('type') -ne 'expression') {
        throw "$name $version gives its licence as a file or a URL, not an SPDX expression: write its entry by teaching this script."
    }
    $expression = $licence.InnerText.Trim()
    $copyright = Get-Field $nuspec 'copyright'
    $copyright = if ($null -ne $copyright -and $copyright.InnerText.Trim()) { $copyright.InnerText.Trim() }
        else { "not stated by the package; its authors: $((Get-Field $nuspec 'authors').InnerText.Trim() -replace ',\s*', ', ')" }
    $origin = Get-Field $nuspec 'repository'
    $source = if ($null -ne $origin -and $origin.GetAttribute('url')) { $origin.GetAttribute('url') } else { (Get-Field $nuspec 'projectUrl').InnerText.Trim() }

    Add-Line
    Add-Line "$name $version"
    Add-Line "  Licence: $expression"
    Add-Line "  Copyright: $copyright"
    Add-Line "  Source: $source"

    $own = @(Get-ChildItem -LiteralPath $directory -File | Where-Object Name -match '^THIRD-PARTY-NOTICES(\.txt)?$')
    if ($own.Count -gt 0) {
        $text = Get-NormalisedText $own[0].FullName
        if (-not $packNotices.ContainsKey($text)) {
            throw "$name $version ships third-party notices that are none of the runtime packs': write them by teaching this script."
        }
        Add-Line "  Third-party notices: the same as those of $($packNotices[$text]), in section 1."
    }
    if ($name -eq 'SQLitePCLRaw.lib.e_sqlite3') {
        Add-Line '  Embeds: SQLite — public domain (no licence text; see https://sqlite.org/copyright.html)'
    }

    foreach ($id in Get-LicenceIds $expression) {
        if (-not $users.ContainsKey($id)) { $users[$id] = [Collections.Generic.List[string]]::new() }
        $users[$id].Add("$name $version")
    }
}

Add-Line
Add-Line
Add-Heading '3. The licence texts' '='

foreach ($id in $users.Keys) {
    $path = Join-Path $Repository ".github/third-party-licences/$id.txt"
    if (-not (Test-Path -LiteralPath $path)) {
        throw "$id has no text: add the SPDX License List's text/$id.txt as .github/third-party-licences/$id.txt."
    }
    Add-Line
    Add-Heading "Licence text: $id" '-'
    Add-Line 'Used by:'
    foreach ($user in $users[$id]) { Add-Line "  $user" }
    Add-Line
    Add-Line (Get-NormalisedText $path)
}

$written = Join-Path $Repository 'THIRD-PARTY-NOTICES'
[IO.File]::WriteAllText($written, $out.ToString().TrimEnd("`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "Wrote $written for $($keys.Count) packages and $($shared.Count) runtime packs on $($rids.Count) platforms, with SDK $sdk."
