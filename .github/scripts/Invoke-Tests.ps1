<#
.SYNOPSIS
Runs the built tree's tests, runs each test that failed again, alone, at most twice, and names on the run's summary every
test that passed only after a repeat.

.DESCRIPTION
One `dotnet test` per target, in turn, with --no-build: the whole solution when no project is named, or each named test
project. Every target is told --retry-failed-tests 2, the test platform's own repeat (the Microsoft.Testing.Extensions.Retry
package every test project references): a test that failed is run again in a new process, by itself and nothing else, until
it passes or has failed three times. A test that passes on a repeat leaves its target green; one that fails all three
times fails it, and this script exits 1 when any target failed.

Every attempt writes a TRX report under the target's results directory, the repeats under Retries/<run>/<attempt>/. Read
in attempt order, a test that failed once and passed last is said in the job's summary under "passed after a repeat", and
as a warning in the run's annotations, so a repeat never hides a flaky test; one that failed every attempt is said under
"failed every attempt".

.PARAMETER Projects
Test projects to run, by name (Jason.App.Tests), each as dotnet/tests/<name>/<name>.csproj. None runs dotnet/Jason.slnx.

.PARAMETER Namespaces
Namespaces whose tests alone are run, each with every namespace inside it.

.PARAMETER NotNamespaces
Namespaces whose tests are not run, each with every namespace inside it.

.PARAMETER Critical
Runs the critical tier alone: the tests that carry the Critical trait.

.PARAMETER Version
The version the build was stamped with, handed to dotnet test as the build had it.

.PARAMETER Results
The directory the reports are written under, one directory per target. It is emptied first.

.PARAMETER Title
What the summary calls this run of the tests.
#>
[CmdletBinding()]
param(
    [string[]] $Projects = @(),
    [string[]] $Namespaces = @(),
    [string[]] $NotNamespaces = @(),
    [switch] $Critical,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Results,
    [Parameter(Mandatory)] [string] $Title
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# How many times a failed test is run again: at most twice, so three attempts in all.
$repeats = 2

$Projects = @($Projects | Where-Object { $_ })
$Namespaces = @($Namespaces | Where-Object { $_ })
$NotNamespaces = @($NotNamespaces | Where-Object { $_ })
foreach ($name in $Projects) {
    if ($name -cnotmatch '^Jason\.[A-Za-z]+\.Tests$') { throw "'$name' is not the name of a test project." }
}

# A namespace filter names one namespace exactly; the one inside it is named with .* after it.
$filters = @(
    $Namespaces | ForEach-Object { '--filter-namespace', $_, '--filter-namespace', "$_.*" }
    $NotNamespaces | ForEach-Object { '--filter-not-namespace', $_, '--filter-not-namespace', "$_.*" }
    if ($Critical) { '--filter-trait', 'Category=Critical' }
)

$targets = [Collections.Generic.List[string[]]]::new()
if ($Projects.Count -eq 0) { $targets.Add(@('--solution', 'dotnet/Jason.slnx')) }
foreach ($name in $Projects) { $targets.Add(@('--project', "dotnet/tests/$name/$name.csproj")) }

$Results = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Results)
if (Test-Path -LiteralPath $Results) { Remove-Item -LiteralPath $Results -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Results | Out-Null

$failed = @()
$index = 0
foreach ($target in $targets) {
    $index++
    $directory = Join-Path $Results "$index"
    $arguments = @('test') + $target + @(
        '--configuration', 'Release', '--no-build', "-p:Version=$Version", '--results-directory', $directory,
        '--') + $filters + @('--retry-failed-tests', "$repeats", '--report-xunit-trx')
    Write-Host "dotnet $($arguments -join ' ')"
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { $failed += "$($target[1]) exited $LASTEXITCODE" }
}

# Every attempt's report, by the run of the test platform it belongs to, in attempt order: each test's outcomes in turn.
$histories = [ordered] @{}
$retries = @(Get-ChildItem -LiteralPath $Results -Recurse -Directory -Filter Retries -ErrorAction SilentlyContinue)
foreach ($run in @($retries | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Directory })) {
    $attempts = @(Get-ChildItem -LiteralPath $run.FullName -Directory | Where-Object { $_.Name -cmatch '^[0-9]+$' } | Sort-Object { [int] $_.Name })
    foreach ($attempt in $attempts) {
        foreach ($report in @(Get-ChildItem -LiteralPath $attempt.FullName -Filter *.trx -File)) {
            [xml] $trx = Get-Content -Raw -LiteralPath $report.FullName
            foreach ($result in @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))) {
                $key = "$($run.FullName)|$($result.testName)"
                if (-not $histories.Contains($key)) { $histories[$key] = [Collections.Generic.List[string]]::new() }
                $histories[$key].Add("$($result.outcome)")
            }
        }
    }
}

$repeated = @()
$neverPassed = @()
foreach ($key in $histories.Keys) {
    $outcomes = $histories[$key]
    $name = $key.Substring($key.IndexOf('|') + 1)
    if ($outcomes -cnotcontains 'Failed') { continue }
    if ($outcomes[$outcomes.Count - 1] -ceq 'Passed') { $repeated += "$name (passed on attempt $($outcomes.Count) of $($repeats + 1))" }
    else { $neverPassed += "$name (failed $(@($outcomes | Where-Object { $_ -ceq 'Failed' }).Count) attempts)" }
}

$repeated = @($repeated | Sort-Object -Unique)
$neverPassed = @($neverPassed | Sort-Object -Unique)
foreach ($test in $repeated) { Write-Host "::warning title=Passed after a repeat ($Title)::$test" }

$summary = @("### $Title", '')
$summary += if ($repeated.Count -eq 0) { 'Passed after a repeat: none.' } else { @("Passed after a repeat: $($repeated.Count)", '') + @($repeated | ForEach-Object { "- ``$_``" }) }
if ($neverPassed.Count -gt 0) { $summary += @('', "Failed every attempt: $($neverPassed.Count)", '') + @($neverPassed | ForEach-Object { "- ``$_``" }) }
if ($failed.Count -gt 0) { $summary += @('', "Failed: $($failed -join '; ')") }
$summary | ForEach-Object { Write-Host $_ }
if ($env:GITHUB_STEP_SUMMARY) { $summary + @('') | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY }

if ($failed.Count -gt 0) { exit 1 }
exit 0
