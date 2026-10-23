<#
.SYNOPSIS
Writes the body of the pull request a release opens in the public repository, from what the release staged there.

.DESCRIPTION
Reads the staged difference of a checkout of the public repository — a moved file read as a removal and an addition,
so every list is plain — and writes the body: which contract documents changed, which pages explain them, how many
files each area gained, changed and lost, and every file. When $env:GITHUB_OUTPUT is set it also writes
label=contracts there when a contract document changed, and label= when none did, so the pull request is labelled
for the people who read contract changes.

Every path the difference holds belongs to one area, or the body is refused: a path a release publishes and the body
cannot place is a change to what is published, and the areas say so first.

.PARAMETER Target
A checkout of the public repository with the release's projection staged.

.PARAMETER Version
The release's version, without a leading v.

.PARAMETER Body
Where the body is written. Its directory is made when it does not exist.

.PARAMETER AreasOf
Comma-separated paths, as the open set lists them: each is placed in its area, and one that belongs to none is
refused. How a test holds the areas to every path a release publishes.
#>
[CmdletBinding(DefaultParameterSetName = 'Body')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Body')] [string] $Target,
    [Parameter(Mandatory, ParameterSetName = 'Body')] [string] $Version,
    [Parameter(Mandatory, ParameterSetName = 'Body')] [string] $Body,
    [Parameter(Mandatory, ParameterSetName = 'Areas')] [string] $AreasOf
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# The areas, in the order the table lists them; a prefix ending with '/' is a directory, any other a file.
$areas = @(
    [pscustomobject]@{ Name = 'Skills (`skills/`, `.claude-plugin/`)'; Paths = @('skills/', '.claude-plugin/') }
    [pscustomobject]@{ Name = 'Plugins (`plugins/`)'; Paths = @('plugins/') }
    [pscustomobject]@{ Name = 'Contracts (`docs/contracts/`)'; Paths = @('docs/contracts/') }
    [pscustomobject]@{ Name = 'Documentation (`README.md`, `docs/`)'; Paths = @('README.md', 'docs/') }
    [pscustomobject]@{ Name = 'Install path (`install/`)'; Paths = @('install/') }
    [pscustomobject]@{ Name = 'The UI (`ui/`)'; Paths = @('ui/') }
    # A row of its own, so that a change to the terms, to the notices or to the note on the marks reads as something
    # other than 0 0 0. The own-files row changes in every release, with VERSION, and would hide it.
    [pscustomobject]@{
        Name  = 'Licences (`LICENSE`, `LICENSE-RUNTIME.md`, `THIRD-PARTY-NOTICES`, `TRADEMARKS.md`)'
        Paths = @('LICENSE', 'LICENSE-RUNTIME.md', 'THIRD-PARTY-NOTICES', 'TRADEMARKS.md')
    }
    [pscustomobject]@{
        Name  = 'This repository''s own files (`VERSION`, `CODEOWNERS`, `.editorconfig`, `.gitattributes`, `.gitignore`, `CONTRIBUTING.md`, `SECURITY.md`, `.github/`)'
        Paths = @('VERSION', 'CODEOWNERS', '.editorconfig', '.gitattributes', '.gitignore', 'CONTRIBUTING.md', 'SECURITY.md', '.github/')
    }
)

function Get-Area([string] $path) {
    foreach ($area in $areas) {
        foreach ($prefix in $area.Paths) {
            if (($prefix.EndsWith('/') -and $path.StartsWith($prefix, [StringComparison]::Ordinal)) -or $path -ceq $prefix) { return $area }
        }
    }

    throw "'$path' belongs to no area of the pull request's body: add it to the areas in Write-PublishBody.ps1."
}

if ($PSCmdlet.ParameterSetName -eq 'Areas') {
    foreach ($path in $AreasOf.Split(',', [StringSplitOptions]::RemoveEmptyEntries)) {
        Write-Output "$path`t$((Get-Area $path).Name)"
    }
    return
}

$raw = git -C $Target diff --cached --name-status --no-renames -z
if ($LASTEXITCODE -ne 0) { throw "git diff exited with $LASTEXITCODE" }

# NUL-separated pairs, the status and then the path, in git's own order, which is the paths' byte order.
$fields = (@($raw) -join "`n").Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)
$word = @{ A = 'added'; M = 'changed'; T = 'changed'; D = 'removed' }
$changes = @(for ($index = 0; $index + 1 -lt $fields.Count; $index += 2) {
    $status = $fields[$index]
    if (-not $word.ContainsKey($status)) { throw "git reported '$status' for '$($fields[$index + 1])', which a projection never stages." }
    [pscustomobject]@{ Status = $status; Path = $fields[$index + 1]; Area = (Get-Area $fields[$index + 1]).Name }
})

$documents = @($changes | Where-Object { $_.Path -cmatch '^docs/contracts/(?:operations|notifications)/.+\.json$' })
$pages = @($changes | Where-Object { $_.Path -cmatch '^docs/contracts/.+\.md$' })

$text = [Text.StringBuilder]::new()
function Add-Line([string] $line = '') { [void] $text.Append($line).Append("`n") }

Add-Line "Jason ${Version}: this pull request brings this repository to the version the runtime released as ``v$Version`` — its skills, plugins, operation contracts, install scripts and documentation. It was opened by the release process. Merging it tags ``v$Version`` here and publishes the draft release that holds the runtime's executables."
Add-Line
Add-Line "The runtime was built and tested at this version."
Add-Line
Add-Line '## Contract documents changed'
Add-Line
if ($documents.Count -eq 0) {
    Add-Line 'None.'
} else {
    foreach ($document in $documents) { Add-Line "- ``$($document.Path)`` — $($word[$document.Status])" }
}
Add-Line
$explained = if ($pages.Count -eq 0) { 'none' } else { ($pages | ForEach-Object { "``$($_.Path)``" }) -join ', ' }
Add-Line "Pages that explain them, prose only: $explained."
Add-Line
Add-Line '## Files changed, by area'
Add-Line
Add-Line '| Area | Added | Changed | Removed |'
Add-Line '|---|---|---|---|'
foreach ($area in $areas) {
    $in = @($changes | Where-Object { $_.Area -ceq $area.Name })
    $added = @($in | Where-Object { $_.Status -eq 'A' }).Count
    $changed = @($in | Where-Object { $_.Status -in 'M', 'T' }).Count
    $removed = @($in | Where-Object { $_.Status -eq 'D' }).Count
    Add-Line "| $($area.Name) | $added | $changed | $removed |"
}
Add-Line
Add-Line '<details><summary>Every file</summary>'
Add-Line
Add-Line '```'
foreach ($change in $changes) { Add-Line "$($change.Status)`t$($change.Path)" }
Add-Line '```'
Add-Line
Add-Line '</details>'
Add-Line
Add-Line '## The draft release'
Add-Line
Add-Line "``v$Version`` is a draft until this merges. It holds ``jason-win-x64.zip``, ``jason-linux-x64.tar.gz``, ``jason-osx-arm64.tar.gz``, ``manifest.json``, ``checksums.txt``, ``install.sh``, ``install.ps1``, ``jason-plugin-reply.zip`` and ``jason-ui.zip``."

$directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Body))
[IO.Directory]::CreateDirectory($directory) | Out-Null
[IO.File]::WriteAllText($Body, $text.ToString(), [Text.UTF8Encoding]::new($false))

$label = if ($documents.Count -gt 0) { 'contracts' } else { '' }
if ($env:GITHUB_OUTPUT) { [IO.File]::AppendAllText($env:GITHUB_OUTPUT, "label=$label`n") }
Write-Host "The body lists $($changes.Count) files and $($documents.Count) contract documents; label: $(if ($label) { $label } else { 'none' })."
