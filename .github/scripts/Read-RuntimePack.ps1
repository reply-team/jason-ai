# Which .NET runtime a published Jason executable carries, read out of the file rather than asked of the program.
#
# A self-contained single-file publish bundles its dependency manifest, uncompressed, and the manifest names the runtime
# pack the executable was built on: runtimepack.Microsoft.NETCore.App.Runtime.<rid>/<version>. The update job reads it on
# both sides of its update, where the executable installed must not be run before the update: a run would open the
# database the job prepared for the new version to migrate.
#
# Prints the version, or refuses when the file names no runtime pack or more than one.
param(
    [Parameter(Mandatory)]
    [string] $Executable
)

$ErrorActionPreference = 'Stop'

$text = [System.Text.Encoding]::Latin1.GetString([System.IO.File]::ReadAllBytes($Executable))
$found = @([regex]::Matches($text, 'runtimepack\.Microsoft\.NETCore\.App\.Runtime\.[A-Za-z0-9-]+/(\d+\.\d+\.\d+)') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique)

if ($found.Count -ne 1) {
    throw "'$Executable' names $($found.Count) .NET runtime packs, not one: $($found -join ', ')"
}

$found[0]
