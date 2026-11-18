<#
.SYNOPSIS
One step of the Windows proof that Jason installs, updates and uninstalls as one installation, run by CI's union-windows
job on a build of this tree and by the release's proof-windows job on the signed packages.

.DESCRIPTION
Each -Step is one act a person or an agent performs, followed by what it must have left, asserted; a failed assertion
throws, and the step fails. Nothing here stands in for the product: what an act leaves is read, and what remains after an
uninstall is compared with a listing taken before anything was installed. The listings are kept under -Out, which the job
uploads, and they are what a person reads.

  Listing           the places an installation can touch, written to -Out\<-Name>.txt
  Install           install.ps1 (-Script) from the feed, as an agent runs it: Windows PowerShell 5.1, the file, then its
                    text through Invoke-Expression as the one-liner runs it; the place, jason in a fresh shell, the Path
                    entry and its kind, the skills records, the person's own skill left alone, the role skills; and the
                    machine set-up an application's start runs, which writes nothing under a data directory. Unpinned,
                    as an agent runs it; with -Pinned, by the pinned line, -Version, after a pin naming another version
                    was refused and placed nothing
  UninstallFromTerminal   jason uninstall typed in a fresh shell, the data directory purged: the waiter hands the folder to
                    the uninstaller with --uninstalled-by-jason, the folder goes whole, and no start of the application
                    writes a data directory again
  InstallOverPrevious     the previous release's Jason Desktop, installed by its own setup and left open, then this build's
                    setup run as an update runs it (--updated /S --force-run): the open window closed by the installer,
                    this version in place, its machine set-up done, the application started again
  UninstallFromInstalledApps   the uninstall Installed apps runs, QuietUninstallString: the folder, its entry, the Path
                    entry and the skills go, the data directory stays
  FromPrevious      the previous release's command-line Jason, installed by its own script, with a runtime and a logon
                    registration, updated by its own `jason update apply --version` from the feed served on this
                    machine, and moved into Jason Desktop: the application in place, jason in a new shell its carried
                    file, the runtime running from it, the logon registration naming it, the move's record done, the
                    command-line install retired; and whether the build says it is a test build, as JASON_PROOF_BUILD
                    says it must (test on CI's build, release on the release's packages). The uninstall is the next step's
  Compare           -Name against -Against: every line equal but those of the data directory, which an uninstall keeps
  ModulePath        a reading, never a failure: the module path Windows PowerShell rebuilds from an empty one
  Begin, End        a leg's two ends, so that no leg's failure keeps the legs after it from running. Begin records the
                    leg before it as failed when that one began and never ended; says "not run" for this leg when a step
                    it needs (-Needs 'name=outcome') did not succeed, and leaves -Out\skip for the job's step to read;
                    and, with -Clean, holds the machine to the listing taken before anything was installed: a machine
                    the leg that last changed it left is that leg's failure, recorded by its name, and on a
                    GitHub-hosted runner alone the product's own uninstalls take it off, each act logged in
                    -Out\resets.txt. -Reading marks a leg that changes nothing. Held lists every failed leg, every leg
                    not run and every divergence, and fails the job at its end
  PathShape         the account's Path made ExpandString with an entry spelled through a variable, on a GitHub-hosted
                    runner alone; every install after it holds both by name
  WayBack           going back from 0.49 to the previous release on a moved machine: campaigns as many, the journal held
                    by id, the previous release's version and its ready status. With -Again, the previous release's own
                    update then moves the install into Jason Desktop again, and the moved install's own uninstall ends it
  InstallOverCommandLine   the previous release's command-line Jason, a runtime running and its logon start registered, then
                    this build's unpinned line run over it: the old runtime stopped before the install is retired, a runtime
                    again from the carried jason, the Path, a new shell, and the campaigns the same by id
  OverBothPrevious  the previous release's Jason Desktop and its command-line Jason side by side, the command-line folder on
                    the Path and the Desktop placing nothing there, as that release leaves them, a runtime
                    started by the command-line jason and left running, then this build's setup run silently as an update
                    without --force-run: at its exit one installation, the command-line one retired and its runtime ended,
                    the note that a start is owed standing; then the application started by the leg: a runtime answering
                    from the carried jason, the note gone, the logon registration naming the carried file, the Path naming
                    the bundle folder once, a new shell finding the carried jason, the campaigns kept

-Signer names the signer the script under test holds the setup to: the release's on the signed packages; on CI's test
build, which a release never signs, the runner's own test certificate, which the job's copy of install.ps1 names.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Listing', 'Install', 'ServedFeed', 'UninstallFromTerminal', 'InstallOverPrevious', 'InstallOverOpenWindow', 'SilentUpdateLegs', 'UninstallWaiterShown', 'UpdateApply', 'UninstallFromInstalledApps', 'FromPrevious', 'Compare', 'Held', 'ModulePath', 'Begin', 'End', 'PathShape', 'WayBack', 'Extracted', 'OneTemporary', 'InstallOverCommandLine', 'OverBothPrevious')] [string] $Step,
    [string] $Executable,
    [string] $Leg,
    [string[]] $Needs = @(),
    [switch] $Clean,
    [switch] $Reading,
    [string] $Feed,
    [string] $Version,
    [string] $Script,
    [string] $Previous,
    [string] $Next,
    [string] $Out = (Join-Path $env:RUNNER_TEMP 'installation'),
    [string] $Name,
    [string] $Against,
    [switch] $Pinned,
    [switch] $Again
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false
New-Item -ItemType Directory -Force -Path $Out | Out-Null

$app = Join-Path $env:LOCALAPPDATA 'Programs\jason-desktop'
$carried = Join-Path $app 'resources\bundle\jason.exe'
$bundle = Join-Path $app 'resources\bundle'
$commandLine = Join-Path $env:LOCALAPPDATA 'Programs\jason'
$data = Join-Path $env:USERPROFILE '.jason'
$harnessRoot = Join-Path $env:USERPROFILE 'Jason'
$uninstallKeys = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
# A skill folder of a name Jason ships, written before the install as a person's own: never touched, never recorded.
$ownSkill = Join-Path $env:USERPROFILE '.agents\skills\quick-start'
# The entry the PathShape step adds to the account's Path, spelled through a variable, which every install must keep so.
$keptEntry = '%USERPROFILE%\jason-ci-kept'
# The temporary directory, as the product reads it (Path.GetTempPath): where the single-file host extracts and a retired
# jason is renamed aside.
$temporary = [IO.Path]::GetTempPath().TrimEnd('\')
# The ids of the folders this build's executables extract to, written by the Extracted step; any other id under
# %TEMP%\.net\jason is another release's (the previous release's jason runs in several legs).
$thisBuild = Join-Path $Out 'this-build-extracted.txt'
# HARD: the carried removal takes this build's extraction folder (%TEMP%\.net\jason\<id>), on Windows every
# other build's as well, and sweeps the retired folders, so a line of either left after a removal counts, in the compares
# and in the clean check at a leg's start (Get-LeftOver) alike. Set to $true, the two print those lines under "seen, and
# expected until the removal takes them" and fail nothing: for a build whose removal takes neither alone. test-installation-macos.sh's
# EXTRACTED_SOFT is the same switch there, and the two are flipped together.
$extractedSoft = $false
# Every leg's own scratch, which a leg that fails part way can leave and no product door takes, as test-installation-macos.sh
# takes its own (take_scratch): the folder the person's own quick-start is moved out to while the previous release is
# installed (WayBack), and the helper processes this script starts and leaves running while a leg works - the feed server,
# the process holding a file of the application's folder (SilentUpdateLegs, leg 5) and the second reader of the update's
# request file (UpdateApply). A step ended by its timeout never runs a leg's finally, so these outlive it: a feed server
# keeps its port and the next one cannot bind it; a holder keeps the folder and the reset's uninstall cannot take it. Each
# helper is recorded when it is started, by its process id and its command line (Register-Scratch); a clean begin takes
# them all before it reads the machine (Take-Scratch). The signals the holder and the reader wait for are named here once,
# so a clean begin gives the same ones.
$scratchTemp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $temporary }
$scratchOwnOut = Join-Path $scratchTemp 'own-quick-start-out'
$scratchPlaces = @($scratchOwnOut)
$scratchHeldFile = Join-Path $scratchTemp 'release-held-file'
$scratchStopReading = Join-Path $scratchTemp 'stop-reading-requests'
$scratchSignals = @($scratchHeldFile, $scratchStopReading)
$scratchRecords = Join-Path $Out 'scratch-pids.txt'
# What the previous release's own uninstall leaves by that release's own design, by the listing's name for it and the place
# it is: the skills record it wrote into ~\.agents\skills, which it keeps where the person's own quick-start, put back by
# going back, stands in a place the record names. Only a leg of the previous release that has read the file as that
# release's own records its listing line, exactly ("<leg><TAB><line>", -Out\previous-own.txt): that leg's compare excuses
# that line alone, the held step's whole compare excuses it where it is attributed to that leg, and a clean begin after
# that leg takes it (Take-PreviousOwn). Nothing else is ever recorded there.
$previousOwn = [ordered]@{ 'agents skills\.jason-skills.json' = (Join-Path $env:USERPROFILE '.agents\skills\.jason-skills.json') }
$previousOwnRows = Join-Path $Out 'previous-own.txt'

# The leg a line of -Out\inherited.txt, or of the file $rows, is attributed to, for the line ("+ <line>" or "- <line>");
# nothing where none.
function Get-InheritedBy([string] $line, [string] $rows = (Join-Path $Out 'inherited.txt')) {
    if (-not (Test-Path -LiteralPath $rows)) { return '' }
    foreach ($row in @(Get-Content -LiteralPath $rows)) {
        $at = $row.IndexOf("`t")
        if ($at -gt 0 -and $row.Substring($at + 1) -ceq $line) { return $row.Substring(0, $at) }
    }
    return ''
}

# A failed assertion ends its step; its words are kept beside the leg's name for Held's list as well.
function Fail([string] $why) {
    Add-Content -LiteralPath (Join-Path $Out 'leg-failure.txt') -Value $why -ErrorAction SilentlyContinue
    throw $why
}

# A failed assertion that does not keep the legs after it from being read: said now as an error, recorded, and failed by
# the Held step at the end with its own words.
function Fail-Held([string] $why) {
    Write-Host "::error title=Installation::$why"
    Add-Content -LiteralPath (Join-Path $Out 'failed-legs.txt') -Value $why
}

function Say([string] $line) {
    Write-Host $line
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "- $line" }
}

# The account's Path as it is stored: its entries, unexpanded, and the value's kind.
function Get-UserPath {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment')
    try {
        $raw = [string]$key.GetValue('Path', '', 'DoNotExpandEnvironmentNames')
        $kind = if ($null -ne $key.GetValue('Path')) { [string]$key.GetValueKind('Path') } else { 'absent' }
        return [pscustomobject]@{ Raw = $raw; Kind = $kind; Entries = @($raw -split ';' | Where-Object { $_ }) }
    } finally { $key.Dispose() }
}

function Count-Entry([string] $folder) {
    @((Get-UserPath).Entries | Where-Object { [Environment]::ExpandEnvironmentVariables($_).TrimEnd('\') -ieq $folder.TrimEnd('\') }).Count
}

# A program run to its own end, and not its children's, and its exit code: Start-Process -Wait also waits for every
# process the program started, and an installer run with --force-run starts the application, which never ends by itself.
# A program that has not ended by the bound is described before the step fails: every process it started, and every
# process of the application's folder and of jason, with its command line, so that the run says what it waited on.
function Wait-Program([string] $file, [string[]] $arguments, [int] $seconds = 300) {
    $process = Start-Process -FilePath $file -ArgumentList $arguments -PassThru
    # The handle, taken now, keeps the exit code readable once the process has ended.
    $null = $process.Handle
    return Wait-Started $process "$file $($arguments -join ' ')" $seconds
}

# Every process the started one started, and every process of the application's folder and of jason, with its command
# line shortened.
function Write-ProcessTree([System.Diagnostics.Process] $process) {
    $all = @(Get-CimInstance -ClassName Win32_Process)
    $ids = @($process.Id)
    Write-Host "  the setup, process $($process.Id), ended: $($process.HasExited); the processes it started:"
    for ($at = 0; $at -lt $ids.Count; $at++) {
        foreach ($child in @($all | Where-Object { $_.ParentProcessId -eq $ids[$at] })) {
            $ids += $child.ProcessId
            Write-Host "    $($child.ProcessId), started by $($child.ParentProcessId): $("$($child.CommandLine)".Substring(0, [Math]::Min(200, "$($child.CommandLine)".Length)))"
        }
    }
    Write-Host '  every process of the application folder and of jason:'
    foreach ($running in @($all | Where-Object { ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($app, [StringComparison]::OrdinalIgnoreCase)) -or $_.Name -like 'jason*' })) {
        Write-Host "    $($running.ProcessId), started by $($running.ParentProcessId): $("$($running.CommandLine)".Substring(0, [Math]::Min(200, "$($running.CommandLine)".Length)))"
    }
}

function Write-JasonProcesses {
    Write-Host '  every process of the application folder, of jason, of the uninstaller and of Windows PowerShell:'
    foreach ($running in @(Get-CimInstance -ClassName Win32_Process | Where-Object { ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($app, [StringComparison]::OrdinalIgnoreCase)) -or $_.Name -like 'jason*' -or $_.Name -like 'Uninstall*' -or $_.Name -like 'Un_*' -or $_.Name -ceq 'powershell.exe' })) {
        Write-Host "    $($running.ProcessId), started by $($running.ParentProcessId): $("$($running.CommandLine)".Substring(0, [Math]::Min(300, "$($running.CommandLine)".Length)))"
        foreach ($window in (Get-VisibleWindows @([int] $running.ProcessId))) { Write-Host "      visible window: $window" }
    }
}

# Every top-level window a process owns, visible or not, with its class and title and the text of each of its child
# controls: what a setup that waits with no child shows, a message box naming itself. Read on a hosted runner only.
function Use-WindowsType {
    if (-not ('JasonProof.Windows' -as [type])) {
        Add-Type -Namespace JasonProof -Name Windows -MemberDefinition @'
public delegate bool EnumProc(System.IntPtr hwnd, System.IntPtr lparam);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, System.IntPtr lparam);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumChildWindows(System.IntPtr parent, EnumProc callback, System.IntPtr lparam);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr hwnd, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr hwnd);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetClassName(System.IntPtr hwnd, System.Text.StringBuilder text, int count);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern int GetWindowText(System.IntPtr hwnd, System.Text.StringBuilder text, int count);
public static System.Collections.Generic.List<string> Describe(uint owner) {
    var lines = new System.Collections.Generic.List<string>();
    EnumWindows((hwnd, _) => {
        uint pid; GetWindowThreadProcessId(hwnd, out pid);
        if (pid != owner) return true;
        lines.Add("window " + hwnd + ": class '" + ClassOf(hwnd) + "', title '" + TextOf(hwnd) + "', visible " + IsWindowVisible(hwnd));
        EnumChildWindows(hwnd, (child, __) => { lines.Add("  control " + child + ": class '" + ClassOf(child) + "', text '" + TextOf(child) + "', visible " + IsWindowVisible(child)); return true; }, System.IntPtr.Zero);
        return true;
    }, System.IntPtr.Zero);
    return lines;
}
public static System.Collections.Generic.List<string> Visible(uint owner) {
    var lines = new System.Collections.Generic.List<string>();
    EnumWindows((hwnd, _) => {
        uint pid; GetWindowThreadProcessId(hwnd, out pid);
        if (pid == owner && IsWindowVisible(hwnd)) lines.Add("class '" + ClassOf(hwnd) + "', title '" + TextOf(hwnd) + "'");
        return true;
    }, System.IntPtr.Zero);
    return lines;
}
static string ClassOf(System.IntPtr hwnd) { var b = new System.Text.StringBuilder(256); GetClassName(hwnd, b, b.Capacity); return b.ToString(); }
static string TextOf(System.IntPtr hwnd) { var b = new System.Text.StringBuilder(1024); GetWindowText(hwnd, b, b.Capacity); return b.ToString(); }
'@
    }
}

function Write-ProcessWindows([int] $id) {
    Use-WindowsType
    $lines = [JasonProof.Windows]::Describe([uint32] $id)
    Write-Host "  the windows of process $($id): $($lines.Count) line(s)"
    foreach ($line in $lines) { Write-Host "    $line" }
}

# The visible top-level windows of these processes, one line each: class and title.
function Get-VisibleWindows([int[]] $ids) {
    Use-WindowsType
    $lines = foreach ($id in $ids) { [JasonProof.Windows]::Visible([uint32] $id) }
    @($lines | Where-Object { $_ })
}

# A setup started and watched until it ends or the bound: twice a second, every visible top-level window of it and of
# every process it started is noted, and the step given is run with the seconds gone by. What it ended with, when, and
# every window seen.
function Watch-Setup([string] $file, [string[]] $arguments, [int] $seconds, [scriptblock] $whileRunning = $null) {
    $process = Start-Process -FilePath $file -ArgumentList $arguments -PassThru
    $null = $process.Handle
    $started = Get-Date
    $seen = [System.Collections.Generic.List[string]]::new()
    while (-not $process.HasExited -and ((Get-Date) - $started).TotalSeconds -lt $seconds) {
        $all = @(Get-CimInstance -ClassName Win32_Process)
        $ids = @($process.Id)
        for ($at = 0; $at -lt $ids.Count; $at++) { $ids += @($all | Where-Object { $_.ParentProcessId -eq $ids[$at] } | ForEach-Object { [int] $_.ProcessId }) }
        foreach ($window in (Get-VisibleWindows $ids)) { if (-not $seen.Contains($window)) { $seen.Add($window); Write-Host "  a window of the setup at $([int]((Get-Date) - $started).TotalSeconds) s: $window" } }
        if ($whileRunning) { & $whileRunning ((Get-Date) - $started).TotalSeconds }
        Start-Sleep -Milliseconds 500
    }
    $ended = $process.WaitForExit(0)
    $result = @{ Ended = $ended; Exit = $(if ($ended) { $process.ExitCode } else { $null }); Seconds = [int]((Get-Date) - $started).TotalSeconds; Windows = @($seen) }
    if (-not $ended) {
        Write-ProcessTree $process
        Write-ProcessWindows $process.Id
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    return $result
}

function Wait-Started([System.Diagnostics.Process] $process, [string] $what, [int] $seconds = 300) {
    $file = $what; $arguments = @()
    if (-not $process.WaitForExit($seconds * 1000)) {
        $all = @(Get-CimInstance -ClassName Win32_Process)
        $ids = @($process.Id)
        Write-Host "$file $($arguments -join ' ') (process $($process.Id)) had not ended after $seconds seconds; the processes it started:"
        for ($at = 0; $at -lt $ids.Count; $at++) {
            foreach ($child in @($all | Where-Object { $_.ParentProcessId -eq $ids[$at] })) {
                $ids += $child.ProcessId
                Write-Host "  $($child.ProcessId), started by $($child.ParentProcessId): $($child.CommandLine)"
            }
        }
        Write-Host 'every process of the application folder and of jason:'
        foreach ($running in @($all | Where-Object { ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($app, [StringComparison]::OrdinalIgnoreCase)) -or $_.Name -like 'jason*' })) {
            Write-Host "  $($running.ProcessId), started by $($running.ParentProcessId): $($running.CommandLine)"
        }
        Fail "$file $($arguments -join ' ') had not ended after $seconds seconds"
    }
    return $process.ExitCode
}

# The previous release's runtime started by its own jason. 0.48.5's start gives up after 30 s while the runtime it started
# still runs, saying it may still be starting and that jason runtime status answers once it has (RuntimeStartCommand at
# v0.48.5: a first start migrates the database after a backup). Where it says so, jason runtime status is asked until it
# answers, up to 180 s from the start; the seconds it took are said either way. False where it never answers, or where the
# start failed otherwise.
function Start-PreviousRuntime([string] $jason, [hashtable] $environment = @{}) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $start = Invoke-Program $jason @('runtime', 'start') $environment
    if ($start.Exit -eq 0) { Say "the previous release's runtime answered its start $([int]$clock.Elapsed.TotalSeconds) s after it was asked"; return $true }
    if (-not ("$($start.Said)".Contains('"runtime_start_failed"') -and "$($start.Said)".Contains('is still running'))) { return $false }
    while ($clock.Elapsed.TotalSeconds -lt 180) {
        Start-Sleep -Seconds 5
        if ((Invoke-Program $jason @('runtime', 'status') $environment 60).Exit -eq 0) {
            Say "the previous release's runtime answered jason runtime status $([int]$clock.Elapsed.TotalSeconds) s after its start was asked, past the start's own 30 s"
            return $true
        }
    }
    Say "the previous release's runtime never answered within 180 s of its start"
    return $false
}

# The data directory's logs, copied into -Out before anything purges them, under the name of the leg they tell about; once a
# leg. Nothing where there are none.
function Save-JasonLogs([string] $leg) {
    $logs = Join-Path $data 'logs'
    if (-not $leg -or -not (Test-Path -LiteralPath $logs -PathType Container)) { return }
    $slug = ($leg -replace '[^A-Za-z0-9]+', '-').Trim('-')
    $kept = Join-Path $Out "logs-$($slug.Substring(0, [Math]::Min(80, $slug.Length)))"
    if (Test-Path -LiteralPath $kept) { return }
    try {
        Copy-Item -LiteralPath $logs -Destination $kept -Recurse -Force -ErrorAction Stop
        Write-Host "the data directory's logs, as '$leg' left them, are kept at $kept"
    } catch {
        Write-Host "the data directory's logs could not be kept for '$leg': $($_.Exception.Message)"
    }
}

# A program run with its output kept and shown, and its exit code: Windows PowerShell's lines on stderr included.
function Invoke-Program([string] $file, [string[]] $arguments, [hashtable] $environment = @{}, [int] $seconds = 600) {
    $saved = @{}
    foreach ($variable in $environment.Keys) { $saved[$variable] = [Environment]::GetEnvironmentVariable($variable); [Environment]::SetEnvironmentVariable($variable, $environment[$variable]) }
    try {
        $job = Start-ThreadJob -ArgumentList $file, $arguments -ScriptBlock {
            param($program, $words)
            $lines = (& $program @words 2>&1 | ForEach-Object { "$_" }) -join "`n"
            [pscustomobject]@{ Said = $lines; Code = $LASTEXITCODE }
        }
        if (-not (Wait-Job -Job $job -Timeout $seconds)) {
            $what = "$(Split-Path -Leaf $file) $($arguments -join ' ')"
            Write-Host "$what had not ended after $seconds s"
            Write-JasonProcesses
            $started = @(Get-CimInstance -ClassName Win32_Process | Where-Object { $_.ParentProcessId -eq $PID -and $_.Name -ne 'conhost.exe' })
            $started | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
            Fail "$what did not end within $seconds s"
        }
        $result = Receive-Job -Job $job
        Remove-Job -Job $job -Force
        $said = $result.Said
        $code = $result.Code
    } finally {
        foreach ($variable in $saved.Keys) { [Environment]::SetEnvironmentVariable($variable, $saved[$variable]) }
    }
    Write-Host $said
    return [pscustomobject]@{ Exit = $code; Said = $said; Flat = ($said -replace '\s+', '') }
}

# install.ps1 as the one-liner runs it: Windows PowerShell 5.1, the file run.
function Invoke-InstallScript([string] $path, [string[]] $words = @()) {
    Invoke-Program 'powershell.exe' (@('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $path) + $words) @{ JASON_INSTALL_FEED = $Feed }
}

# The one JSON document a jason verb prints on standard output, among the lines Invoke-Program merged with its stderr: the
# last line that reads as a JSON object; null where none does.
function Read-Document([string] $said) {
    $lines = @(@($said -split "`n") | Where-Object { $_.TrimStart().StartsWith('{') })
    if ($lines.Count -eq 0) { return $null }
    try { return ($lines[-1] | ConvertFrom-Json) } catch { return $null }
}

# Every item of a listing a jason verb pages by next_cursor, a thousand at a time, read whole: each item as the JSON the
# verb printed (System.Text.Json, so that a moment is the text written and never a date the reader converted).
function Read-All([string] $jason, [string[]] $words) {
    $items = [System.Collections.Generic.List[object]]::new()
    $cursor = $null
    for ($page = 1; $page -le 1000; $page++) {
        $asked = @($words) + @('--limit', '1000') + $(if ($cursor) { @('--cursor', $cursor) } else { @() })
        $ran = Invoke-Program $jason $asked @{ JASON_Update__CheckEnabled = 'false' } 120
        if ($ran.Exit -ne 0) { Fail "jason $($words -join ' ') exited with $($ran.Exit): $($ran.Said)" }
        $lines = @(@($ran.Said -split "`n") | Where-Object { $_.TrimStart().StartsWith('{') })
        if ($lines.Count -eq 0) { Fail "jason $($words -join ' ') printed no page" }
        $root = ([System.Text.Json.JsonDocument]::Parse($lines[-1])).RootElement
        foreach ($item in $root.GetProperty('items').EnumerateArray()) { $items.Add($item.Clone()) }
        $nextCursor = $root.GetProperty('next_cursor')
        $cursor = if ($nextCursor.ValueKind -eq [System.Text.Json.JsonValueKind]::String) { $nextCursor.GetString() } else { $null }
        if (-not $cursor) { return $items.ToArray() }
    }
    Fail "jason $($words -join ' ') still had pages after 1000"
}

# A journal entry by what going back holds it to: its id, its kind and its moment as written.
function Read-Entry($item) {
    [pscustomobject]@{ Id = $item.GetProperty('id').GetString(); Kind = $item.GetProperty('kind').GetString(); Ts = $item.GetProperty('ts').GetRawText() }
}

# What a terminal opened from now on finds: a new pwsh given the environment Windows would give it, not this job's.
function Find-InNewShell([string] $command) {
    $file = Join-Path $Out "found-$([Guid]::NewGuid().ToString('N')).txt"
    $probe = "`$found = Get-Command $command -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1; if (`$found) { `$found.Source; & `$found.Source --version } else { 'not found' }"
    $shell = Start-Process pwsh -ArgumentList '-NoProfile', '-NonInteractive', '-Command', $probe -UseNewEnvironment -PassThru -NoNewWindow -RedirectStandardOutput $file
    $null = $shell.Handle
    if (-not $shell.WaitForExit(120000)) { Write-JasonProcesses; Stop-Process -Id $shell.Id -Force -ErrorAction SilentlyContinue; Fail "a new shell looking for $command did not end within 120 s" }
    $lines = @(Get-Content -LiteralPath $file)
    Remove-Item -LiteralPath $file
    return $lines
}

function Read-Version([string] $jason) {
    $record = Join-Path (Split-Path -Parent $jason) 'version.json'
    if (-not (Test-Path -LiteralPath $record)) { return $null }
    return [string](Get-Content -LiteralPath $record -Raw | ConvertFrom-Json).version
}

# The application's window started as a person starts it, its processes once it shows, given up to a minute for the
# runtime its start brings up to answer; the window's process ids.
function Open-Window([int] $RuntimeSeconds = 60) {
    Start-Process -FilePath (Join-Path $app 'Jason.exe') | Out-Null
    Wait-Until { @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -gt 0 } 60 'the window starting'
    Wait-Until { @(Get-VisibleWindows @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' } | ForEach-Object { [int] $_.ProcessId })).Count -gt 0 } 60 'the window showing'
    $until = (Get-Date).AddSeconds($RuntimeSeconds)
    while ((Get-Date) -lt $until -and (Invoke-Program $carried @('runtime', 'status')).Exit -ne 0) { Start-Sleep -Seconds 2 }
    Start-Sleep -Seconds 3
    return @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' } | ForEach-Object { $_.ProcessId })
}

function Test-Runtime { if ((Invoke-Program $carried @('runtime', 'status')).Exit -eq 0) { 'yes' } else { 'no' } }

function Wait-Until([scriptblock] $condition, [int] $seconds, [string] $what) {
    $until = [DateTime]::UtcNow.AddSeconds($seconds)
    while ([DateTime]::UtcNow -lt $until) {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 500
    }
    Fail "$what did not happen within $seconds s"
}

# What is known of a feed server that did not answer, printed before anything is killed: its log, whether its process
# lives, and what holds the port, by Windows' own table of TCP connections and by netstat.
function Write-FeedServerState([System.Diagnostics.Process] $server, [int] $port, [string] $log) {
    Write-Host "the feed server's log, ${log}:"
    foreach ($file in @($log, "$log.err")) {
        if (Test-Path -LiteralPath $file) { Get-Content -LiteralPath $file | ForEach-Object { Write-Host "  $_" } } else { Write-Host "  ($file is not there)" }
    }
    $server.Refresh()
    Write-Host "the feed server, process $($server.Id): $(if ($server.HasExited) { "ended, with $($server.ExitCode)" } else { 'still running' })"
    $held = @(Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue)
    if ($held.Count -eq 0) { Write-Host "no TCP connection or listener on port $port" }
    foreach ($connection in $held) {
        $owner = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $($connection.OwningProcess)" -ErrorAction SilentlyContinue
        Write-Host "  $($connection.LocalAddress):$($connection.LocalPort) $($connection.State), held by $($connection.OwningProcess) $(if ($owner) { "$($owner.Name): $($owner.CommandLine)" } else { '(no such process)' })"
    }
    @(netstat -ano | Select-String -Pattern ":$port\s") | ForEach-Object { Write-Host "  netstat: $($_.Line.Trim())" }
}

# A folder served on 127.0.0.1 at the port by the proof's one feed server (serve-feed.py, which looks no name up between
# bind and listen), and waited for up to 30 s until its manifest answers 200; the process is returned for the caller to
# stop. One that never answers is described first, then fails the step.
function Start-FeedServer([string] $folder, [int] $port, [string] $log) {
    # Each argument quoted, as Start-Process joins them with spaces alone. The server writes every line to the log itself,
    # and to stderr only a usage line.
    $logged = "$(Get-Content -LiteralPath $log -Raw -ErrorAction SilentlyContinue)".Length
    $arguments = @((Join-Path $PSScriptRoot 'serve-feed.py'), $folder, "$port", $log) | ForEach-Object { '"' + $_ + '"' }
    $server = Start-Process python -ArgumentList $arguments -PassThru -NoNewWindow -RedirectStandardError "$log.err"
    Register-Scratch "the feed server for 127.0.0.1:$port" $server
    $null = $server.Handle
    $until = [DateTime]::UtcNow.AddSeconds(30)
    # An answer counts only once this server says it listens, which on Windows it can only do on a port no other socket
    # holds: before that, or from a server that has ended, the answer came from whatever else holds the port.
    # The log is appended to, so only what this server wrote after the bytes already there counts.
    $listening = "listening on 127.0.0.1:$port"
    $answered = $false
    while (-not $answered -and [DateTime]::UtcNow -lt $until) {
        $server.Refresh()
        if ($server.HasExited) { break }
        $written = "$(Get-Content -LiteralPath $log -Raw -ErrorAction SilentlyContinue)"
        if (-not $server.HasExited -and $written.Length -gt $logged -and $written.Substring($logged).Contains($listening)) {
            try { $answered = (Invoke-WebRequest -Uri "http://127.0.0.1:$port/manifest.json" -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200 } catch { }
        }
        if (-not $answered) { Start-Sleep -Milliseconds 500 }
    }
    $server.Refresh()
    if ($answered -and -not $server.HasExited) { return $server }
    Write-FeedServerState $server $port $log
    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    Fail $(if ($server.HasExited) { "the feed server for 127.0.0.1:$port ended, with $($server.ExitCode), before it answered: its log above says why" } else { "the feed on 127.0.0.1:$port answering did not happen within 30 s" })
}

function Get-OwnProcesses([string] $folder) {
    @(Get-CimInstance -ClassName Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($folder.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) })
}

# For the reset's record: every process whose image is under the application's folder, and every uninstaller process
# (one named Uninstall Jason*.exe, or one whose image is under the account's temporary folder, where NSIS relaunches its
# own copy), each with its command line.
function Write-ResetProcesses([string] $resets, [string] $when) {
    $root = (Join-Path $env:LOCALAPPDATA 'Temp').TrimEnd('\') + '\'
    $all = @(Get-CimInstance -ClassName Win32_Process)
    $own = @($all | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($app.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) })
    $uninstallers = @($all | Where-Object { $_.Name -like 'Uninstall Jason*.exe' -or ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) })
    Add-Content -LiteralPath $resets -Value "  $when, the processes of the application's folder: $(if ($own) { ($own | ForEach-Object { "$($_.ProcessId) $($_.CommandLine)" }) -join ' | ' } else { 'none' }); the uninstaller processes: $(if ($uninstallers) { ($uninstallers | ForEach-Object { "$($_.ProcessId) $($_.CommandLine)" }) -join ' | ' } else { 'none' })"
}

# An Installed apps entry's quiet uninstall line; empty where the entry has none.
function Get-QuietUninstall($entry) {
    if ($entry -and $entry.PSObject.Properties.Name -contains 'QuietUninstallString') { return [string]$entry.QuietUninstallString }
    return ''
}

function Get-JasonUninstallEntries {
    @(Get-ChildItem -LiteralPath $uninstallKeys -ErrorAction SilentlyContinue | ForEach-Object { Get-ItemProperty -LiteralPath $_.PSPath } | Where-Object { ($_.PSObject.Properties.Name -contains 'DisplayName') -and $_.DisplayName -like 'Jason*' })
}

# --- the listing --------------------------------------------------------------------------------------------------

# Every place an installation of Jason can touch on this account, one line each, sorted: what a person reads to see
# what an uninstall left. A folder is listed by its files, relative, with their sizes; a registry value by its data.
# The temporary folder a new shell of this account has: where a terminal the legs open, and every program it starts,
# unpacks its native libraries and renames a held executable aside.
function Get-NewShellTemporary {
    $file = Join-Path $Out "temporary-$([Guid]::NewGuid().ToString('N')).txt"
    $shell = Start-Process pwsh -ArgumentList '-NoProfile', '-NonInteractive', '-Command', '[IO.Path]::GetTempPath()' -UseNewEnvironment -PassThru -NoNewWindow -RedirectStandardOutput $file
    $null = $shell.Handle
    if (-not $shell.WaitForExit(60000)) { Stop-Process -Id $shell.Id -Force -ErrorAction SilentlyContinue; Fail 'a new shell asked for its temporary folder did not end within 60 s' }
    $said = ((Get-Content -LiteralPath $file -Raw) + '').Trim().TrimEnd('\')
    Remove-Item -LiteralPath $file
    if (-not $said) { Fail 'a new shell asked for its temporary folder said nothing' }
    return $said
}

# Every folder the host may unpack this account's native libraries under: the account's temporary root and each numbered
# per-session folder in it, and this step's own where it is neither. Each with the name its listing lines give it.
function Get-ExtractBases {
    $root = Join-Path $env:LOCALAPPDATA 'Temp'
    $bases = [ordered]@{ 'Temp' = $root }
    foreach ($session in @(Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d+$' })) { $bases["Temp\$($session.Name)"] = $session.FullName }
    $known = @($bases.Values | ForEach-Object { (Get-Item -LiteralPath $_ -Force -ErrorAction SilentlyContinue).FullName })
    $own = (Get-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue).FullName
    if ($own -and $known -inotcontains $own) { $bases[$temporary] = $temporary }
    return $bases
}

function Write-Listing([string] $file, [switch] $Quiet) {
    # One temporary folder for the job: a step reading another than a new shell's would look where no leg's programs
    # unpacked, and miss what they left. The proof's own fault where it happens; never the product's. Compared on a
    # GitHub-hosted runner alone, by one new shell; elsewhere (a stub trial) no shell is started, since a new shell reads
    # the account's own environment.
    if ($env:GITHUB_ACTIONS -ceq 'true' -and $env:RUNNER_ENVIRONMENT -ceq 'github-hosted') {
        $fresh = Get-NewShellTemporary
        if ($fresh -ine $temporary) { Fail "this step's temporary folder is $temporary, and a new shell of this account has ${fresh}: the proof's own fault, since every step of the job is to be given the new shell's (OneTemporary)" }
    }
    else { Say "the new shell's temporary folder was not compared (not a hosted runner)" }
    $lines = [System.Collections.Generic.List[string]]::new()
    function Folder([string] $label, [string] $path) {
        if (-not (Test-Path -LiteralPath $path)) { $lines.Add("$label`tabsent"); return }
        $lines.Add("$label`tpresent")
        foreach ($item in @(Get-ChildItem -LiteralPath $path -Recurse -Force -File -ErrorAction SilentlyContinue)) {
            $lines.Add("$label\$($item.FullName.Substring($path.Length).TrimStart('\'))`t$($item.Length)")
        }
    }
    Folder 'application' $app
    Folder 'command-line install' $commandLine
    Folder 'updater cache' (Join-Path $env:LOCALAPPDATA 'jason-desktop-updater')
    # What a jason run leaves outside every folder above, one line per folder: the native libraries the single-file
    # executable extracts, under %TEMP%\.net\jason\<id> (the host's default base, one id per build), and the folders a
    # retired command-line jason is renamed into while a process holds it - jason-retired-<id> in the temporary directory
    # on the executable's volume, .jason-retired-<id> beside its folder otherwise (CommandLineInstalls.AsideFolder).
    $bases = Get-ExtractBases
    foreach ($base in $bases.Keys) {
        foreach ($extracted in @(Get-ChildItem -LiteralPath (Join-Path $bases[$base] '.net\jason') -Directory -Force -ErrorAction SilentlyContinue)) { $lines.Add("extracted`t$base`t$($extracted.Name)") }
    }
    # The retired folders a process makes under its own temporary folder, so under every base as the extracted ones, named.
    foreach ($base in $bases.Keys) {
        foreach ($retired in @(Get-ChildItem -LiteralPath $bases[$base] -Directory -Force -Filter 'jason-retired-*' -ErrorAction SilentlyContinue)) { $lines.Add("retired`t$base`t$($retired.Name)") }
    }
    foreach ($retired in @(Get-ChildItem -LiteralPath (Split-Path -Parent $commandLine) -Directory -Force -Filter '.jason-retired-*' -ErrorAction SilentlyContinue)) { $lines.Add("retired`tPrograms\$($retired.Name)") }
    # What Windows and Electron keep for an application that ran, by its name, in the roaming and local application data:
    # listed, so a person sees what stays. The jobs open the window before each uninstall, so these are written as a
    # person's are.
    foreach ($parent in @($env:APPDATA, $env:LOCALAPPDATA)) {
        foreach ($kept in @(Get-ChildItem -LiteralPath $parent -Force -Filter '*jason*' -ErrorAction SilentlyContinue)) { $lines.Add("system-kept`t$($kept.FullName.Substring($env:USERPROFILE.Length).TrimStart('\'))") }
    }
    Folder 'claude skills' (Join-Path $env:USERPROFILE '.claude\skills')
    Folder 'agents skills' (Join-Path $env:USERPROFILE '.agents\skills')
    Folder 'data directory' $data
    Folder 'harness root' $harnessRoot
    foreach ($command in @('jason', 'jason.cmd', 'jason-desktop', 'jason-desktop.cmd', 'jason.exe')) {
        $lines.Add("WindowsApps\$command`t$(if (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\$command")) { 'present' } else { 'absent' })")
    }
    $path = Get-UserPath
    $lines.Add("user Path kind`t$($path.Kind)")
    for ($at = 0; $at -lt $path.Entries.Count; $at++) { $lines.Add("user Path entry $at`t$($path.Entries[$at])") }
    foreach ($entry in Get-JasonUninstallEntries) { $lines.Add("Installed apps`t$($entry.DisplayName)`t$($entry.PSChildName)") }
    foreach ($key in @('jason', 'jason-desktop')) {
        $lines.Add("link scheme $key`t$(if (Test-Path -LiteralPath "HKCU:\Software\Classes\$key") { 'registered' } else { 'absent' })")
    }
    $run = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
    if ($run) { foreach ($value in @($run.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' -and "$($_.Value)" -match '(?i)jason' })) { $lines.Add("Run value $($value.Name)`t$($value.Value)") } }
    foreach ($id in @(Get-ChildItem -LiteralPath 'HKCU:\Software\Classes\AppUserModelId' -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '(?i)jason' })) { $lines.Add("notification registration`t$($id.PSChildName)") }
    foreach ($task in @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -like 'Jason*' })) { $lines.Add("logon task`t$($task.TaskPath)$($task.TaskName)`t$($task.Actions.Execute)") }
    foreach ($place in @([Environment]::GetFolderPath('StartMenu'), [Environment]::GetFolderPath('Desktop'))) {
        foreach ($link in @(Get-ChildItem -LiteralPath $place -Recurse -Filter 'Jason*.lnk' -ErrorAction SilentlyContinue)) { $lines.Add("shortcut`t$($link.FullName)") }
    }
    foreach ($process in @(Get-CimInstance -ClassName Win32_Process | Where-Object { $_.ExecutablePath -match '(?i)\\jason[^\\]*\\|\\jason(\.exe)?$|\\Jason\.exe$' })) { $lines.Add("process`t$($process.ExecutablePath)") }
    $sorted = @($lines | Sort-Object -Unique)
    Set-Content -LiteralPath $file -Value $sorted
    if ($Quiet) { return }
    Write-Host "$($sorted.Count) lines written to $file"
    $sorted | ForEach-Object { Write-Host "  $_" }
    # When each extraction folder was made and last written, said beside the listing and never part of it, against the
    # times of the removal's own lines in the log.
    foreach ($base in $bases.Keys) {
        foreach ($folder in @(Get-ChildItem -LiteralPath (Join-Path $bases[$base] '.net\jason') -Directory -Force -ErrorAction SilentlyContinue)) {
            Write-Host "  (where) extracted $($folder.Name) at $($folder.FullName), made $($folder.CreationTimeUtc.ToString('o')), written $($folder.LastWriteTimeUtc.ToString('o'))"
        }
    }
    Write-Host "  (where) this step's temporary folder is $temporary"
}

# --- the legs' two ends ----------------------------------------------------------------------------------------------

# The leg begun and not yet ended, and every leg that ended.
$legOpen = Join-Path $Out 'leg-open.txt'
$legsEnded = Join-Path $Out 'legs-ended.txt'
# The leg that last changed the machine, which a later leg's start holds to what it left.
$lastLegFile = Join-Path $Out 'last-leg.txt'

# A leg that began and never ended failed: recorded by its name, its own lines being its step's.
function Close-Leg {
    if (-not (Test-Path -LiteralPath $legOpen)) { return }
    $open = "$(Get-Content -LiteralPath $legOpen -Raw)".Trim()
    Remove-Item -LiteralPath $legOpen -Force
    if (-not $open) { return }
    $words = Join-Path $Out 'leg-failure.txt'
    $why = if (Test-Path -LiteralPath $words) { "it said: $((Get-Content -LiteralPath $words) -join ' / ')" } else { 'its own lines are in its step' }
    Write-Host "::error title=Installation::the leg '$open' failed; $why"
    Add-Content -LiteralPath (Join-Path $Out 'failed-legs.txt') -Value "the leg '$open' failed; $why"
    Save-JasonLogs $open
}

# The open leg's own compare differed: the leg is recorded as failed by its name at once, and as dirty, so that its End
# fails its step and a later Begin treats the machine it left as a failed leg's. Outside a leg (Held's whole compare)
# nothing is recorded here: Held fails the job on the divergence itself.
function Set-LegDirty([string] $why) {
    if (-not (Test-Path -LiteralPath $legOpen)) { return }
    $open = "$(Get-Content -LiteralPath $legOpen -Raw)".Trim()
    if (-not $open) { return }
    $dirty = Join-Path $Out 'legs-dirty.txt'
    if ((Test-Path -LiteralPath $dirty) -and (@(Get-Content -LiteralPath $dirty) -ccontains $open)) { return }
    Add-Content -LiteralPath (Join-Path $Out 'failed-legs.txt') -Value "the leg '$open' failed: $why"
    Add-Content -LiteralPath $dirty -Value $open
}

# This leg not run, said by why: recorded for Held, and -Out\skip left for the job's step to end on.
function Skip-Leg([string] $why) {
    Write-Host "::error title=Installation::$Leg - $why"
    Say "$Leg - $why"
    Add-Content -LiteralPath (Join-Path $Out 'not-run.txt') -Value "$Leg - $why"
    Remove-Item -LiteralPath $legOpen -Force -ErrorAction SilentlyContinue
    New-Item -ItemType File -Path (Join-Path $Out 'skip') -Force | Out-Null
}

# A helper process this script started and leaves running while a leg works, recorded for a clean begin to take if the leg
# leaves it: one JSON line in -Out\scratch-pids.txt with its id, its command line as Windows reports it now, and what it is;
# its id said when it is started.
function Register-Scratch([string] $what, [System.Diagnostics.Process] $process) {
    $started = "$((Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $($process.Id)" -ErrorAction SilentlyContinue).CommandLine)"
    Add-Content -LiteralPath $scratchRecords -Value (ConvertTo-Json -Compress -InputObject ([ordered]@{ pid = $process.Id; command_line = $started; what = $what }))
    Say "started $what as process $($process.Id), recorded for a clean begin to stop if a leg leaves it running"
}

# The recorded helpers still running: a process under a recorded id whose command line is still the one recorded. Windows
# gives a freed id to a later process, so one under the same id with another command line is never a helper; one recorded
# with no command line is never matched.
function Get-RunningScratch {
    @(if (Test-Path -LiteralPath $scratchRecords) {
        foreach ($row in @(Get-Content -LiteralPath $scratchRecords | Where-Object { $_ })) {
            $record = $row | ConvertFrom-Json
            if (-not $record.command_line) { continue }
            $now = @(Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $([int]$record.pid)" -ErrorAction SilentlyContinue)
            if ($now.Count -gt 0 -and "$($now[0].CommandLine)" -ceq $record.command_line) { $record }
        }
    })
}

# An earlier leg's scratch taken before a clean leg reads the machine, as test-installation-macos.sh's take_scratch takes
# its own: the holder and the reader signalled first, so they let go by themselves; a recorded helper still running then
# stopped by its id alone, and only while its command line is still the one recorded; the person's own quick-start put
# back where it is out of its place, a stand-in in its place removed first; then the places removed. On a GitHub-hosted
# runner alone, as the reset; each act said and logged in -Out\resets.txt, never a proof. $false where the leg is not run.
function Take-Scratch([string] $leg) {
    $places = @($scratchPlaces | Where-Object { Test-Path -LiteralPath $_ })
    $running = @(Get-RunningScratch)
    if ($places.Count -eq 0 -and $running.Count -eq 0) { return $true }
    $found = @($places) + @($running | ForEach-Object { "process $($_.pid) ($($_.what))" })
    if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted') { Skip-Leg "not run: an earlier leg's scratch is there ($($found -join '; ')), and it is taken on a GitHub-hosted runner alone"; return $false }
    $resets = Join-Path $Out 'resets.txt'
    Add-Content -LiteralPath $resets -Value "before '$leg': an earlier leg's scratch: $($found -join '; ')"
    function Write-Taken([string] $line) { Say $line; Add-Content -LiteralPath $resets -Value "  $line" }
    if ($running.Count -gt 0) {
        foreach ($signal in $scratchSignals) { New-Item -ItemType File -Path $signal -Force | Out-Null }
        $until = [DateTime]::UtcNow.AddSeconds(15)
        while ([DateTime]::UtcNow -lt $until -and @(Get-RunningScratch).Count -gt 0) { Start-Sleep -Milliseconds 500 }
        $stopped = @(Get-RunningScratch)
        foreach ($record in $running) {
            if (@($stopped | Where-Object { $_.pid -eq $record.pid }).Count -eq 0) { Write-Taken "the process $($record.pid) ($($record.what)) ended when signalled" }
        }
        foreach ($record in $stopped) {
            Stop-Process -Id ([int]$record.pid) -Force -ErrorAction SilentlyContinue
            Write-Taken "stopped the process $($record.pid) ($($record.what)) by its id, recorded when it was started and still running the command line recorded: $($record.command_line)"
        }
        $until = [DateTime]::UtcNow.AddSeconds(15)
        while ([DateTime]::UtcNow -lt $until -and @(Get-RunningScratch).Count -gt 0) { Start-Sleep -Milliseconds 500 }
        $left = @(Get-RunningScratch)
        if ($left.Count -gt 0) { Skip-Leg "not run: an earlier leg's helper could not be stopped: $(($left | ForEach-Object { "process $($_.pid) ($($_.what))" }) -join '; ')"; return $false }
    }
    # While the person's own quick-start is out of its place, whatever stands there is a stand-in (the previous release's
    # skills write one): removed, then the person's copy put back, as the leg's own put-back does. Where it cannot be put
    # back, nothing is removed and the leg is not run, so the person's copy is never taken with the scratch.
    $outSkill = Join-Path $scratchOwnOut 'quick-start'
    if (Test-Path -LiteralPath $outSkill -PathType Container) {
        if (Test-Path -LiteralPath $ownSkill) {
            try { Remove-Item -LiteralPath $ownSkill -Recurse -Force; Write-Taken "removed $ownSkill, a stand-in while the person's own quick-start was out of its place" }
            catch { Write-Taken "could not remove $ownSkill, a stand-in while the person's own quick-start was out of its place: $($_.Exception.Message)" }
        }
        if (-not (Test-Path -LiteralPath $ownSkill)) {
            try {
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ownSkill) | Out-Null
                Move-Item -LiteralPath $outSkill -Destination $ownSkill
                Write-Taken "put the person's own quick-start back from $outSkill to $ownSkill"
            } catch { Write-Taken "could not put the person's own quick-start back from ${outSkill}: $($_.Exception.Message)" }
        }
        if (Test-Path -LiteralPath $outSkill) { Skip-Leg "not run: the person's own quick-start could not be put back from $outSkill to $ownSkill"; return $false }
    }
    foreach ($place in $scratchPlaces) {
        if (-not (Test-Path -LiteralPath $place)) { continue }
        try { Remove-Item -LiteralPath $place -Recurse -Force; Write-Taken "removed $place" }
        catch { Write-Taken "could not remove ${place}: $($_.Exception.Message)" }
    }
    foreach ($place in $scratchPlaces) {
        if (Test-Path -LiteralPath $place) { Skip-Leg "not run: an earlier leg's scratch, $place, could not be removed"; return $false }
    }
    return $true
}

# Whether the listing line $line (without its "+ ") is one the leg $leg recorded as the previous release's own: exactly that
# line, recorded by that leg, which is a leg of the previous release (-Out\previous-legs.txt).
function Test-PreviousOwn([string] $leg, [string] $line) {
    if (-not $leg -or -not (Test-Path -LiteralPath $previousOwnRows)) { return $false }
    $previousLegs = @(if (Test-Path -LiteralPath (Join-Path $Out 'previous-legs.txt')) { Get-Content -LiteralPath (Join-Path $Out 'previous-legs.txt') })
    ($previousLegs -ccontains $leg) -and (@(Get-Content -LiteralPath $previousOwnRows) -ccontains "$leg`t$line")
}

# The previous release's own lines the leg that last changed the machine, $by, recorded (see $previousOwn), taken before a
# clean leg reads the machine: each file removed only while its listing line is still exactly the one recorded. On a
# GitHub-hosted runner alone, as the reset; each act said and logged in -Out\resets.txt, never a proof. $false where the
# leg is not run.
function Take-PreviousOwn([string] $leg, [string] $by) {
    foreach ($ownName in $previousOwn.Keys) {
        $file = $previousOwn[$ownName]
        if (-not $by -or -not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
        $line = "$ownName`t$((Get-Item -LiteralPath $file -Force).Length)"
        if (-not (Test-PreviousOwn $by $line)) { continue }
        if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted') { Skip-Leg "not run: $file, the previous release's own left after '$by', is taken on a GitHub-hosted runner alone, and this is not one"; return $false }
        try { Remove-Item -LiteralPath $file -Force } catch { Add-Content -LiteralPath (Join-Path $Out 'resets.txt') -Value "  could not remove ${file}: $($_.Exception.Message)" }
        $state = if (Test-Path -LiteralPath $file) { 'still there' } else { 'gone' }
        Add-Content -LiteralPath (Join-Path $Out 'resets.txt') -Value "before '$leg': removed $file, the previous release's own that '$by' recorded as its uninstall leaves it ($($line -replace "`t", ' ')): it is $state"
        Say "$file, the previous release's own that '$by' recorded as its uninstall leaves it: removed, and it is $state"
    }
    return $true
}

# The updater's folder under the account's local application data (jason-desktop-updater), which a release before 0.49
# keeps at its own uninstall by design and where its setup stores itself (electron-builder's installer.nsh): taken where
# the leg that last changed the machine, $by, is one of the previous release (-Out\previous-legs.txt), whichever uninstall
# of that release left it, the leg's own or the reset's; after a leg of this build alone it is never taken, and counts
# against that leg. Removed directly, with nothing of this build involved; what it held and whether it went logged in
# -Out\resets.txt, never a proof. $false where the leg is not run.
function Take-UpdaterCache([string] $leg, [string] $by) {
    $folder = Join-Path $env:LOCALAPPDATA 'jason-desktop-updater'
    if (-not $by -or -not (Test-Path -LiteralPath $folder)) { return $true }
    $previousLegs = @(if (Test-Path -LiteralPath (Join-Path $Out 'previous-legs.txt')) { Get-Content -LiteralPath (Join-Path $Out 'previous-legs.txt') })
    if ($previousLegs -cnotcontains $by) { return $true }
    if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted') { Skip-Leg "not run: the updater's folder $folder, left after '$by', a leg of the previous release, is taken on a GitHub-hosted runner alone, and this is not one"; return $false }
    $resets = Join-Path $Out 'resets.txt'
    $held = @(Get-ChildItem -LiteralPath $folder -Recurse -Force -File -ErrorAction SilentlyContinue | ForEach-Object { "$($_.FullName.Substring($folder.Length).TrimStart('\')) $($_.Length)" })
    Add-Content -LiteralPath $resets -Value "before '$leg': found $folder after '$by', a leg of the previous release, whose own uninstall keeps it by design, holding: $(if ($held) { $held -join '; ' } else { 'no file' })"
    try { Remove-Item -LiteralPath $folder -Recurse -Force } catch { Add-Content -LiteralPath $resets -Value "  could not remove ${folder}: $($_.Exception.Message)" }
    $state = if (Test-Path -LiteralPath $folder) { 'still there' } else { 'gone' }
    Add-Content -LiteralPath $resets -Value "  removed ${folder}: it is $state"
    Say "the updater's folder $folder, left after '$by', a leg of the previous release, whose own uninstall keeps it: removed, and it is $state"
    return $true
}

# Every line of the machine now that is not a line of the machine before, and every line gone, the data directory and the
# harness root aside, as Compare reads them.
function Read-OwnSkill([string] $folder) {
    # A quick-start folder's SKILL.md by its bytes: its length and its SHA-256, or absent.
    $file = Join-Path $folder 'SKILL.md'
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { return 'absent' }
    $bytes = [IO.File]::ReadAllBytes($file)
    "$($bytes.Length) bytes, sha256 $([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant())"
}

function Test-NamesOwnSkill([string] $said, [string] $own) {
    # Whether one fact of jason status names $own itself, by its path, in its list of folders Jason did not deploy under a
    # name Jason ships: the list before the sentence's verb, split as the list is joined (", " and " and "), one item the
    # path or ending in it after a space. Status prints JSON, so a backslash arrives doubled.
    $text = $said.Replace('\\', '\')
    foreach ($fact in [regex]::Matches($text, '(?<list>[^"]+?) (?:is a (?:folder|link)|are (?:folders and links|folders|links)) Jason did not deploy, under (?:a name|names) Jason ships')) {
        foreach ($item in ($fact.Groups['list'].Value -split ', | and ')) {
            if ($item -ieq $own -or $item.EndsWith(" $own", [StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
    }
    $false
}

function Get-LeftOver {
    $now = Join-Path $Out 'now.txt'
    Write-Listing $now -Quiet
    $before = @(Get-Content -LiteralPath (Join-Path $Out 'before.txt'))
    $after = @(Get-Content -LiteralPath $now)
    # Another release's extraction folder is excused here as in the compares. This build's and the retired folders follow
    # $extractedSoft, as the compares do: ignored while it is soft, since before the removal took them no product door did and a reset
    # could not, so every later leg would go unrun; counted once it is hard, so that no leg starts on a machine a removal
    # left dirty. The one commit that flips $extractedSoft makes both hard.
    $kept = '^(data directory|harness root)'
    $ours = @(if (Test-Path -LiteralPath $thisBuild) { Get-Content -LiteralPath $thisBuild | Where-Object { $_ } })
    $counts = {
        param([string] $line)
        if ($line -match $kept) { return $false }
        if ($line -match '^extracted\t(?:[^\t]*\t)?(?<id>[^\t]+)$' -and $ours.Count -gt 0 -and $ours -cnotcontains $Matches['id']) { return $false }
        if ($line -match '^(extracted|retired)\t' -and $extractedSoft) { return $false }
        return $true
    }
    # One gone is the removal's taking, which on Windows takes every build's extraction folder and sweeps the retired ones.
    @(@($after | Where-Object { $before -cnotcontains $_ -and (& $counts $_) } | ForEach-Object { "+ $_" }) + @($before | Where-Object { $after -cnotcontains $_ -and $_ -notmatch '^(extracted|retired)\t' -and (& $counts $_) } | ForEach-Object { "- $_" }))
}

# What a clean leg's begin found and excused: the machine then, listed to -Out\<listing>.txt, against the machine before, the
# data directory and the harness root aside. One row per line, "<leg><TAB><+ or -> <line>", in the file $rows; a line
# recorded at an earlier begin keeps the leg it was attributed to there (-Out\inherited.txt), and a new one is attributed to
# $by, the leg that last changed the machine. The held step reads the machine at the end the same way, into a file of its own.
function Write-Inherited([string] $by, [string] $listing, [string] $rows) {
    if (-not $by) { $by = 'no leg, before any leg ran' }
    $now = Join-Path $Out "$listing.txt"
    Write-Listing $now -Quiet
    $was = @(Get-Content -LiteralPath (Join-Path $Out 'before.txt'))
    $is = @(Get-Content -LiteralPath $now)
    $kept = '^(data directory|harness root)'
    $changes = @(@($is | Where-Object { $was -cnotcontains $_ -and $_ -notmatch $kept } | ForEach-Object { "+ $_" }) + @($was | Where-Object { $is -cnotcontains $_ -and $_ -notmatch $kept } | ForEach-Object { "- $_" }))
    $written = @(foreach ($change in $changes) {
        $earlier = Get-InheritedBy $change
        "$(if ($earlier) { $earlier } else { $by })`t$change"
    })
    Set-Content -LiteralPath $rows -Value $written
}

# --- the readers of the legs over a previous release ----------------------------------------------------------------

# Every jason runtime running on this machine: the processes of jason.exe whose command line runs `runtime run`.
function Get-RuntimeProcesses {
    @(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'jason.exe'" | Where-Object { "$($_.CommandLine)" -match '\bruntime\s+run\b' })
}

# Whether the process a reading kept (its ProcessId and CreationDate) is still the one running under that id: Windows gives a
# freed id to a later process, so one under the same id that started at another moment is another process.
function Test-SameProcess($kept) {
    $now = @(Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $([int]$kept.ProcessId)")
    $now.Count -gt 0 -and $now[0].CreationDate -eq $kept.CreationDate
}

# The report jason install printed, among the lines install.ps1 showed: the last line that reads as a JSON object with a
# command and a runtime. A runtime started again afterwards prints a document of its own, which this passes over.
function Read-InstallReport([string] $said) {
    $lines = @(@($said -split "`n") | Where-Object { $_.TrimStart().StartsWith('{') })
    for ($at = $lines.Count - 1; $at -ge 0; $at--) {
        try { $document = $lines[$at] | ConvertFrom-Json } catch { continue }
        $names = @($document.PSObject.Properties.Name)
        if ($names -contains 'command' -and $names -contains 'runtime') { return $document }
    }
    return $null
}

# The command-line install's three files, each there: what its retirement later takes is first held to have been placed.
function Assert-CommandLineFiles([string] $by) {
    foreach ($file in 'jason.exe', 'jason-LICENSE', 'jason-THIRD-PARTY-NOTICES') {
        if (-not (Test-Path -LiteralPath (Join-Path $commandLine $file) -PathType Leaf)) { Fail "$by placed no $file at $commandLine" }
    }
}

# The jason command files in this account's WindowsApps, by name. A folder that is not there holds none; one that is there
# and cannot be read fails, rather than reading as empty: read through .NET, since Get-ChildItem lists nothing, and throws
# nothing even with -ErrorAction Stop, for a folder it may not list.
function Get-WindowsAppsCommandFiles {
    $folder = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps'
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) { return @() }
    try { return @([IO.Directory]::GetFileSystemEntries($folder, 'jason*') | ForEach-Object { [IO.Path]::GetFileName($_) }) }
    catch { Fail "the WindowsApps folder $folder could not be read: $($_.Exception.Message)" }
}

# A listing compared with another one, by name, under -Out: what the later one left and what went, each line said, and a
# divergence recorded for the held step. A leg's compare sets aside what its own clean begin found (the lens); the held
# step's whole compare (-Final) sets aside nothing but the previous release's own lines left by a leg that ran it.
function Compare-Listings([string] $listed, [string] $base, [switch] $Final) {
    $before = @(Get-Content -LiteralPath (Join-Path $Out "$base.txt"))
    $after = @(Get-Content -LiteralPath (Join-Path $Out "$listed.txt"))
    # The data directory is kept by every uninstall that is not told to purge it; the harness root by every one.
    $kept = '^(data directory|harness root)'
    # An extraction folder or a retired folder gone since the listing against is the removal's taking: on Windows it takes
    # this build's extraction folder and every other build's, and sweeps the retired folders. Said, and not counted.
    $goneAll = @($before | Where-Object { $after -cnotcontains $_ -and $_ -notmatch $kept })
    $taken = @($goneAll | Where-Object { $_ -match '^(extracted|retired)\t' })
    $gone = @($goneAll | Where-Object { $_ -notmatch '^(extracted|retired)\t' })
    if ($taken.Count -gt 0) { Say "taken by the removal in ${listed}: $(($taken | ForEach-Object { $_ -replace "`t", ' ' }) -join '; ') (a removal takes what every build unpacked here and sweeps the retired folders)" }
    $candidates = @($after | Where-Object { $before -cnotcontains $_ -and $_ -notmatch $kept })
    # An extraction folder of another release's id is that release's own, named as expected with its reason; one of
    # this build's ids, and every retired folder, count (softly only while $extractedSoft says so, as a section of their own).
    $ours = @(if (Test-Path -LiteralPath $thisBuild) { Get-Content -LiteralPath $thisBuild | Where-Object { $_ } })
    # Hard, an extraction line is judged by this build's ids, so a compare with none recorded cannot judge one: it fails,
    # by name, rather than counting another release's folder as this build's or excusing this build's as another's.
    if (-not $extractedSoft -and $ours.Count -eq 0) {
        $why = "${listed} against ${base}: no id of this build is recorded ($thisBuild is missing or empty: the step reading the folder this build's jason extracts to did not run or failed), so no extraction folder can be told this build's or another release's"
        Write-Host "::error title=Uninstall::$why"
        Add-Content -LiteralPath (Join-Path $Out 'divergent.txt') -Value $why
        Set-LegDirty $why
    }
    $left = [System.Collections.Generic.List[string]]::new()
    $expectedLines = [System.Collections.Generic.List[string]]::new()
    $softLines = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $candidates) {
        if ($line -match '^extracted\t(?:[^\t]*\t)?(?<id>[^\t]+)$' -and $ours.Count -gt 0 -and $ours -cnotcontains $Matches['id']) { $expectedLines.Add($line) }
        elseif ($line -match '^(extracted|retired)\t' -and $extractedSoft) { $softLines.Add($line) }
        else { $left.Add($line) }
    }
    if ($expectedLines.Count -gt 0 -and -not $Final) { Say "expected in ${listed}: $(($expectedLines | ForEach-Object { $_ -replace "`t", ' ' }) -join '; ') (another release's own extraction folder: the previous release's jason ran in this job after this build's last removal; that release's own removal leaves it, and on Windows this build's removal takes it when it next runs)" }
    if ($softLines.Count -gt 0) {
        Say "seen, and expected until the removal takes them (soft: `$extractedSoft is set), in ${listed}: $(($softLines | ForEach-Object { $_ -replace "`t", ' ' }) -join '; ')"
        Add-Content -LiteralPath (Join-Path $Out 'soft-seen.txt') -Value "${listed}: $(($softLines | ForEach-Object { $_ -replace "`t", ' ' }) -join '; ')"
    }
    $gone = [System.Collections.Generic.List[string]]::new([string[]] @($gone))
    # The lens: a leg is charged for what it does, against the machine its own clean begin found. A line that machine
    # already had, recorded there with the leg that left it (-Out\inherited.txt), is printed under its own heading by that
    # leg and not counted here; that leg is held to it by its own compare and stays failed by name, and the held step's
    # whole compare counts it again. One limit: a line this leg leaves that is the same as an inherited one cannot be told
    # apart from it, and is attributed to the earlier leg.
    $inheritedHere = [System.Collections.Generic.List[string]]::new()
    $excused = [System.Collections.Generic.List[string]]::new()
    if ($Final) {
        # The held step's whole compare: the lines the previous release's own acts leave - its jason's extraction folder,
        # which that release's removal never takes - are expected only where the leg they are attributed to
        # (-Out\final-attributed.txt) is one that ran the previous release (-Out\previous-legs.txt), named beside it; the same
        # lines left by a leg of this build alone count.
        $attributed = Join-Path $Out 'final-attributed.txt'
        $previousLegs = @(if (Test-Path -LiteralPath (Join-Path $Out 'previous-legs.txt')) { Get-Content -LiteralPath (Join-Path $Out 'previous-legs.txt') })
        foreach ($line in $expectedLines) {
            $by = Get-InheritedBy "+ $line" $attributed
            if ($by -and $previousLegs -ccontains $by) { $excused.Add("+ $line  (left by '$by', a leg of the previous release)") } else { $left.Add($line) }
        }
        if ($excused.Count -gt 0) { Say "expected in ${listed}, each left by a leg of the previous release, as that release's own removal leaves it: $(($excused | ForEach-Object { $_ -replace "`t", ' ' }) -join '; ')" }
    } elseif (Test-Path -LiteralPath (Join-Path $Out 'inherited.txt')) {
        foreach ($line in @($left)) {
            $by = Get-InheritedBy "+ $line"
            if ($by) { $inheritedHere.Add("+ $line  (left by '$by')"); [void] $left.Remove($line) }
        }
        foreach ($line in @($gone)) {
            $by = Get-InheritedBy "- $line"
            if ($by) { $inheritedHere.Add("- $line  (left by '$by')"); [void] $gone.Remove($line) }
        }
        if ($inheritedHere.Count -gt 0) { Say "inherited at this leg's begin and not counted in ${listed}, each by the leg that left it: $(($inheritedHere | ForEach-Object { $_ -replace "`t", ' ' }) -join '; ')" }
    }
    # The previous release's own lines (see $previousOwn): excused exactly where the leg that left them recorded them, the
    # open leg in its own compare, the leg a line is attributed to in the held step's whole one.
    $own = [System.Collections.Generic.List[string]]::new()
    $ownBy = if ($Final) { '' } elseif (Test-Path -LiteralPath $legOpen) { "$(Get-Content -LiteralPath $legOpen -Raw)".Trim() } else { '' }
    foreach ($line in @($left)) {
        $by = if ($Final) { Get-InheritedBy "+ $line" (Join-Path $Out 'final-attributed.txt') } else { $ownBy }
        if (Test-PreviousOwn $by $line) { $own.Add("+ $line  (left by '$by', the previous release's own, as its uninstall leaves it)"); [void] $left.Remove($line) }
    }
    if ($own.Count -gt 0) { Say "expected in ${listed}, the previous release's own as its uninstall leaves it, each recorded by the leg that left it: $(($own | ForEach-Object { $_ -replace "`t", ' ' }) -join '; ')" }
    foreach ($line in $own) { $excused.Add($line) }
    $diff = Join-Path $Out "$base-to-$listed.diff.txt"
    Set-Content -LiteralPath $diff -Value (@("only in $base (before):") + @($gone | ForEach-Object { "- $_" }) + @("only in $listed (after):") + @($left | ForEach-Object { "+ $_" }) +
        @(if ($inheritedHere.Count -gt 0) { "inherited at this leg's begin, not counted here (each by the leg that left it):"; $inheritedHere }) +
        @(if ($excused.Count -gt 0) { "expected, each left by a leg of the previous release:"; $excused }))
    Get-Content -LiteralPath $diff | ForEach-Object { Write-Host $_ }
    $keptLines = @($after | Where-Object { $_ -match $kept -and $before -cnotcontains $_ })
    Say "$listed against ${base}: $($left.Count) line(s) left, $($gone.Count) gone, beside $($keptLines.Count) line(s) of the data directory and the harness root, which are kept"
    # A divergence is recorded and the leg goes on to its End, so that what it does after the compare still runs and one
    # run reads every leg; the leg is recorded as failed by its name at once, and its End fails its step (Set-LegDirty).
    # The Held step at the end fails the job on any divergence recorded.
    if ($gone.Count -gt 0 -or $left.Count -gt 0) {
        Write-Host "::error title=Uninstall::the machine after '$listed' is not the machine before '${base}': $((@($gone | ForEach-Object { "- $_" }) + @($left | ForEach-Object { "+ $_" })) -join '; ')"
        Add-Content -LiteralPath (Join-Path $Out 'divergent.txt') -Value "$listed against ${base}: see $diff"
        Set-LegDirty "its compare differs, $listed against ${base}: see $diff"
    }
}

# ONE GUARD, AT THE ENTRY. Every step but the five that read the machine or this job's own files and write only those
# files (the listing, the compare, the held list and a leg's begin and end, which the stub trials run) changes the machine
# or runs a program on it, and runs on a GitHub-hosted runner alone. Of the five, a listing on a GitHub-hosted runner
# starts one new shell, to read its temporary folder, and elsewhere starts none. The reset inside Begin keeps its own
# refusal.
$readingSteps = @('Listing', 'Compare', 'Held', 'Begin', 'End')
if ($Step -cnotin $readingSteps -and ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted')) {
    Fail "the step $Step changes the machine it runs on, and runs on a GitHub-hosted runner alone"
}

switch ($Step) {
    'Extracted' {
        # The id of the folder this build's executable extracts its native libraries to, read by running it once with the
        # host's base pointed at a folder of this job's own, so that nothing is written where the listing looks.
        if (-not $Executable) { Fail '-Executable names the executable.' }
        $probe = Join-Path $env:RUNNER_TEMP "extract-probe-$([guid]::NewGuid().ToString('N'))"
        $ran = Invoke-Program $Executable @('--version') @{ DOTNET_BUNDLE_EXTRACT_BASE_DIR = $probe } 120
        if ($ran.Exit -ne 0) { Fail "$Executable --version exited with $($ran.Exit)" }
        $ids = @(Get-ChildItem -LiteralPath $probe -Directory -ErrorAction SilentlyContinue | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Directory } | ForEach-Object { $_.Name })
        if ($ids.Count -eq 0) { Fail "$Executable extracted nothing under ${probe}: no id to tell this build's extraction folder by" }
        Add-Content -LiteralPath $thisBuild -Value $ids
        Remove-Item -LiteralPath $probe -Recurse -Force -ErrorAction SilentlyContinue
        Say "this build's $(Split-Path -Leaf $Executable) extracts its native libraries to the id $($ids -join ', ') under %TEMP%\.net\jason"
    }

    'OneTemporary' {
        # Every later step of the job gets the temporary folder a new shell of this account has, through GITHUB_ENV, so
        # that the listings look where the legs' programs unpack and rename aside.
        if (-not $env:GITHUB_ENV) { Fail 'OneTemporary writes the job''s environment, GITHUB_ENV, and there is none' }
        $fresh = Get-NewShellTemporary
        Add-Content -LiteralPath $env:GITHUB_ENV -Value "TEMP=$fresh"
        Add-Content -LiteralPath $env:GITHUB_ENV -Value "TMP=$fresh"
        Say "the job's steps get the temporary folder a new shell of this account has, $fresh, where this step had $temporary"
    }

    'PathShape' {
        # The account's Path as an ordinary account has it: REG_EXPAND_SZ, with an entry spelled through a variable.
        # Through [Environment]'s pair an installer expands every such entry and writes the whole value back as REG_SZ,
        # which a fresh account's install once did. A runner's own value is whatever its image left, so the job gives it
        # the shape to be kept rather than hoping for one; everything already in it stays. A GitHub-hosted runner's
        # account alone, by the guard at the entry: no person's Path is ever written here.
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Environment')
        try {
            $entries = @(([string]$key.GetValue('Path', '', 'DoNotExpandEnvironmentNames')) -split ';' | Where-Object { $_ -and $_ -cne $keptEntry })
            $key.SetValue('Path', (@($entries) + $keptEntry) -join ';', [Microsoft.Win32.RegistryValueKind]::ExpandString)
        } finally { $key.Dispose() }
        $shaped = Get-UserPath
        if ($shaped.Kind -cne 'ExpandString' -or $shaped.Entries -cnotcontains $keptEntry) { Fail "the account's Path is $($shaped.Kind) without '$keptEntry' after it was given the shape" }
        Say "the account's Path is ExpandString, ending in the entry '$keptEntry', spelled through a variable and unexpanded"
    }

    'Listing' {
        if (-not $Name) { Fail '-Name names the listing.' }
        Write-Listing (Join-Path $Out "$Name.txt")
    }

    'Compare' {
        Compare-Listings $Name $Against
    }

    'Begin' {
        if (-not $Leg) { Fail '-Leg names the leg.' }
        $skip = Join-Path $Out 'skip'
        Remove-Item -LiteralPath $skip -Force -ErrorAction SilentlyContinue
        Write-Host "=== $Leg"
        Close-Leg
        Remove-Item -LiteralPath (Join-Path $Out 'leg-failure.txt') -Force -ErrorAction SilentlyContinue
        Set-Content -LiteralPath $legOpen -Value $Leg

        # A step this leg needs that did not succeed: said, recorded, and the leg's step told to end here.
        foreach ($need in $Needs) {
            $at = $need.LastIndexOf('=')
            if ($at -lt 1) { Fail "-Needs takes 'name=outcome', not '$need'" }
            $needed = $need.Substring(0, $at)
            $outcome = $need.Substring($at + 1)
            if ($outcome -ceq 'success') {
                # A leg's step succeeds where the leg itself was not run, or failed and was recorded so, so a needed leg's
                # record is read as well as its step's outcome.
                $failedFile = Join-Path $Out 'failed-legs.txt'
                $notRunFile = Join-Path $Out 'not-run.txt'
                if ((Test-Path -LiteralPath $failedFile) -and @(Get-Content -LiteralPath $failedFile | Where-Object { $_.StartsWith("the leg '$needed' ", [StringComparison]::Ordinal) }).Count -gt 0) { $outcome = 'failure' }
                elseif ((Test-Path -LiteralPath $notRunFile) -and @(Get-Content -LiteralPath $notRunFile | Where-Object { $_.StartsWith("$needed - ", [StringComparison]::Ordinal) }).Count -gt 0) { $outcome = 'not run' }
                else { continue }
            }
            Skip-Leg $(if ($outcome -ceq 'failure') { "not run: the leg it needs failed, $needed" } else { "not run: the leg it needs did not run ($outcome), $needed" })
            return
        }
        # A leg that needs the previous release's fetch runs the previous release: recorded, for the held step's compare.
        if (@($Needs | Where-Object { $_.Substring(0, [Math]::Max(0, $_.LastIndexOf('='))) -ceq 'Fetch the previous release' }).Count -gt 0) {
            Add-Content -LiteralPath (Join-Path $Out 'previous-legs.txt') -Value $Leg
        }

        # The machine this leg starts from is the machine before anything was installed. Where it is not:
        #  - the leg that last changed the machine ENDED: that leg left it, which is that leg's own failure, said and
        #    recorded by its name; the reset below then acts, that leg now being a failed one, and hides nothing;
        #  - that leg FAILED or never ended: its failure is recorded already, and the reset acts;
        #  - no leg changed the machine yet: nothing to blame and nothing to reset, so this leg is not run.
        # The reset uses the product's own doors alone - jason uninstall --purge-data from each jason found, then Installed
        # apps' quiet uninstall - and only on a GitHub-hosted runner, never on a person's machine. Each act is logged with
        # what it found and what it removed, here and in -Out\resets.txt; none of it is a proof of anything. What those
        # doors cannot remove makes this leg not run, naming the lines left. Three acts are the proof's own, never a product
        # door, logged the same way: before the machine is read, an earlier leg's scratch is taken (Take-Scratch) and, after a
        # leg of the previous release, the updater's folder that release keeps (Take-UpdaterCache); after the reset, the
        # harness root every uninstall keeps is moved aside whole.
        if ($Clean) {
            if (-not (Test-Path -LiteralPath (Join-Path $Out 'before.txt'))) { Skip-Leg 'not run: the leg it needs failed, The machine before anything is installed'; return }
            if (-not (Take-Scratch $Leg)) { return }
            $prior = if (Test-Path -LiteralPath $lastLegFile) { "$(Get-Content -LiteralPath $lastLegFile -Raw)".Trim() } else { '' }
            if (-not (Take-UpdaterCache $Leg $prior)) { return }
            if (-not (Take-PreviousOwn $Leg $prior)) { return }
            $found = @(Get-LeftOver)
            if ($found.Count -gt 0) {
                $resets = Join-Path $Out 'resets.txt'
                $last = if (Test-Path -LiteralPath $lastLegFile) { "$(Get-Content -LiteralPath $lastLegFile -Raw)".Trim() } else { '' }
                if (-not $last) { Skip-Leg "not run: the machine is not the machine before, and no leg before this one changed it: $($found -join '; ')"; return }
                # A leg recorded once as having ended leaving the machine is a failed leg from then on: never recorded again.
                $dirty = Join-Path $Out 'legs-dirty.txt'
                $ended = (Test-Path -LiteralPath $legsEnded) -and (@(Get-Content -LiteralPath $legsEnded) -ccontains $last) -and -not ((Test-Path -LiteralPath $dirty) -and (@(Get-Content -LiteralPath $dirty) -ccontains $last))
                if ($ended) {
                    $why = "the leg '$last' ended, but the machine after it is not the machine before: $($found -join '; ')"
                    Write-Host "::error title=Installation::$why"
                    Add-Content -LiteralPath (Join-Path $Out 'failed-legs.txt') -Value $why
                    Add-Content -LiteralPath $dirty -Value $last
                }
                Add-Content -LiteralPath $resets -Value "before '$Leg', after '$last' $(if ($ended) { 'ended leaving it' } else { 'failed' }), found: $($found -join '; ')"
                Say "before this leg the machine is not the machine before ($($found.Count) line(s)), after '$last' $(if ($ended) { 'ended leaving it, recorded as its failure' } else { 'failed' }); found: $($found -join '; ')"
                if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted') { Skip-Leg 'not run: the reset after a failed leg runs on a GitHub-hosted runner alone, and this is not one'; return }
                # One door at a time, each waited to its end: two uninstallers on one folder each skip what the other holds.
                Save-JasonLogs $last
                try {
                    Write-ResetProcesses $resets 'before the reset'
                    foreach ($process in @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' })) {
                        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
                        Add-Content -LiteralPath $resets -Value "  quit the application's process $($process.ProcessId)"
                    }
                    foreach ($jason in @($carried, (Join-Path $commandLine 'jason.exe'))) {
                        if (Test-Path -LiteralPath $jason) {
                            $removed = Invoke-Program $jason @('uninstall', '--purge-data', '--yes') @{} 300
                            $said = "  $jason uninstall --purge-data --yes ended $($removed.Exit): $(($removed.Said -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 3) -join ' / ')"
                            Write-Host $said
                            Add-Content -LiteralPath $resets -Value $said
                            if ($jason -ne $carried) { continue }
                            # Refused outright, as a release before 0.49 refuses for the jason its Jason Desktop carries
                            # (uninstall_carried_by_desktop): nothing was handed to a waiter, so Installed apps is next, at once.
                            $refusal = Read-Document $removed.Said
                            if ($refusal -and $refusal.PSObject.Properties['error']) {
                                Add-Content -LiteralPath $resets -Value "  the carried jason refused the uninstall outright ($($refusal.error.code)): Installed apps is next, with no wait"
                                continue
                            }
                            # The application's removal hands the folder to its own uninstaller, run by a waiter once the verb
                            # has ended: that waiter, by the process its report names, is waited for to its end; where none
                            # is named, the folder and its Installed apps entry, both gone. Bounded at 300 s.
                            $waiters = @([regex]::Matches([string]$removed.Said, 'process (\d+) waits') | ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object -Unique)
                            $deadline = [DateTime]::UtcNow.AddSeconds(300)
                            if ($waiters.Count -gt 0) {
                                foreach ($waiter in $waiters) {
                                    $process = Get-Process -Id $waiter -ErrorAction SilentlyContinue
                                    $left = [int][Math]::Max(0, ($deadline - [DateTime]::UtcNow).TotalMilliseconds)
                                    $ended = (-not $process) -or $process.WaitForExit($left)
                                    Add-Content -LiteralPath $resets -Value "  the waiter $waiter the removal named $(if ($ended) { 'has ended' } else { 'had not ended within 300 s' })"
                                }
                            } else {
                                while ([DateTime]::UtcNow -lt $deadline -and ((Test-Path -LiteralPath $app) -or @(Get-JasonUninstallEntries).Count -gt 0)) { Start-Sleep -Seconds 2 }
                                Add-Content -LiteralPath $resets -Value "  the removal named no waiter; the application's folder $(if (Test-Path -LiteralPath $app) { 'still stands' } else { 'is gone' }) and its Installed apps entries number $(@(Get-JasonUninstallEntries).Count), after waiting for both to go"
                            }
                        }
                    }
                    # Installed apps' door, only where an entry still stands: as the product's waiter runs it, a copy of the
                    # uninstaller in a folder of its own, given the application's folder by _?=, so that NSIS runs that copy
                    # in place and to its end instead of relaunching one and returning at once.
                    Write-ResetProcesses $resets 'before Installed apps'
                    foreach ($entry in @(Get-JasonUninstallEntries)) {
                        $quietLine = Get-QuietUninstall $entry
                        if ($quietLine -match '^"(?<file>[^"]+)"\s*(?<arguments>.*)$' -and (Test-Path -LiteralPath $Matches['file'])) {
                            $uninstaller = $Matches['file']; $arguments = $Matches['arguments']
                            $copyFolder = Join-Path $temporary "jason-reset-uninstaller-$([Guid]::NewGuid().ToString('N'))"
                            New-Item -ItemType Directory -Path $copyFolder | Out-Null
                            $copy = Join-Path $copyFolder (Split-Path -Leaf $uninstaller)
                            Copy-Item -LiteralPath $uninstaller -Destination $copy
                            $quiet = Wait-Program $copy (@(@($arguments -split '\s+') | Where-Object { $_ }) + @("_?=$(Split-Path -Parent $uninstaller)"))
                            $said = "  Installed apps' quiet uninstall of $($entry.DisplayName), a copy run in place with _?=$(Split-Path -Parent $uninstaller), ended $quiet"
                            Write-Host $said
                            Add-Content -LiteralPath $resets -Value $said
                            Remove-Item -LiteralPath $copyFolder -Recurse -Force -ErrorAction SilentlyContinue
                        }
                    }
                    # The updater's folder the uninstalls above left, where the leg that left the machine is one of the
                    # previous release, as before this leg's reading (Take-UpdaterCache).
                    $null = Take-UpdaterCache $Leg $last
                } catch {
                    Add-Content -LiteralPath $resets -Value "  the reset failed: $($_.Exception.Message)"
                    Skip-Leg "not run: taking off the machine '$last' left failed: $($_.Exception.Message)"
                    return
                }
                $deadline = [DateTime]::UtcNow.AddSeconds(120)
                while ([DateTime]::UtcNow -lt $deadline -and @(Get-LeftOver).Count -gt 0) { Start-Sleep -Seconds 2 }
                $left = @(Get-LeftOver)
                if ($left.Count -gt 0) { Write-ResetProcesses $resets 'when the poll gave up' }
                $removedLines = @($found | Where-Object { $left -cnotcontains $_ })
                Add-Content -LiteralPath $resets -Value "  removed: $(if ($removedLines) { $removedLines -join '; ' } else { 'nothing' }); left: $(if ($left) { $left -join '; ' } else { 'nothing' })"
                Say "the reset removed: $(if ($removedLines) { $removedLines -join '; ' } else { 'nothing' }); left: $(if ($left) { $left -join '; ' } else { 'nothing' })"
                if ($left.Count -gt 0) { Skip-Leg "not run: the product's own uninstalls could not bring back the machine '$last' left: $($left -join '; ')"; return }
            }
            # The harness root, which every uninstall keeps as its contract says, holds the folders and markers an earlier
            # leg's installation made, and a new data directory's runtime refuses them as another's (harness_folder_taken):
            # moved aside whole before a clean leg, never deleted, said in one line and logged, never a proof; on a
            # GitHub-hosted runner alone, as the reset. test-installation-macos.sh moves its own aside the same way.
            if (Test-Path -LiteralPath $harnessRoot) {
                $priorName = if ($prior) { $prior } else { 'no leg' }
                if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted') { Skip-Leg "not run: the harness root $harnessRoot an earlier leg left is moved aside on a GitHub-hosted runner alone, and this is not one"; return }
                $roots = Join-Path $scratchTemp 'harness-roots'
                New-Item -ItemType Directory -Force -Path $roots | Out-Null
                $asideName = "$(@(Get-ChildItem -LiteralPath $roots -Force).Count + 1)-after-$($priorName -replace '[^A-Za-z0-9._-]', '-')"
                if ($asideName.Length -gt 80) { $asideName = $asideName.Substring(0, 80) }
                $aside = Join-Path $roots $asideName
                try { Move-Item -LiteralPath $harnessRoot -Destination $aside; $movedLine = "before '$Leg': the harness root $harnessRoot, left after '$priorName', moved aside to $aside" }
                catch { $movedLine = "before '$Leg': the harness root $harnessRoot, left after '$priorName', could not be moved aside to ${aside}: $($_.Exception.Message)" }
                Say $movedLine
                Add-Content -LiteralPath (Join-Path $Out 'resets.txt') -Value $movedLine
                if (Test-Path -LiteralPath $harnessRoot) { Skip-Leg "not run: the harness root $harnessRoot could not be moved aside"; return }
            }
            # What this begin found and excused, for this leg's compare to set aside by the leg that left it.
            Write-Inherited $prior 'leg-start' (Join-Path $Out 'inherited.txt')
        }

        # This leg now changes the machine, and is the one a later leg's start holds to what it leaves; a reading changes
        # nothing.
        if (-not $Reading) { Set-Content -LiteralPath $lastLegFile -Value $Leg }
    }

    'End' {
        if (-not $Leg) { Fail '-Leg names the leg.' }
        Add-Content -LiteralPath $legsEnded -Value $Leg
        Remove-Item -LiteralPath $legOpen -Force -ErrorAction SilentlyContinue
        # A leg whose own compare differed ran to its End and is recorded as failed already: its step fails here, so that
        # no leg reads as succeeded with a divergence in it.
        $dirty = Join-Path $Out 'legs-dirty.txt'
        if ((Test-Path -LiteralPath $dirty) -and (@(Get-Content -LiteralPath $dirty) -ccontains $Leg)) {
            Write-Host "::error title=Installation::$Leg - ended, but its compare differs: it is recorded as failed"
            exit 1
        }
        Say "$Leg - ended"
    }

    'Held' {
        Close-Leg
        # The final compare: the machine now against the true machine before, through no leg's begin, so that a line any leg
        # left counts here whichever leg was charged for it. Each line is attributed as a begin attributes it, the last leg
        # taking what no begin saw (-Out\final-attributed.txt); the previous release's own lines are expected only where
        # that leg is one of the previous release, and named beside it. Its divergence, recorded, fails the job below.
        if (Test-Path -LiteralPath (Join-Path $Out 'before.txt')) {
            $lastNow = if (Test-Path -LiteralPath $lastLegFile) { "$(Get-Content -LiteralPath $lastLegFile -Raw)".Trim() } else { '' }
            Write-Inherited $lastNow 'final' (Join-Path $Out 'final-attributed.txt')
            Compare-Listings 'final' 'before' -Final
        }
        $divergent = Join-Path $Out 'divergent.txt'
        $failedLegs = Join-Path $Out 'failed-legs.txt'
        $notRun = Join-Path $Out 'not-run.txt'
        $held = @()
        if (Test-Path -LiteralPath $failedLegs) { $held += @(Get-Content -LiteralPath $failedLegs) }
        if (Test-Path -LiteralPath $notRun) { $held += @(Get-Content -LiteralPath $notRun) }
        if (Test-Path -LiteralPath $divergent) { $held += "the machine after an uninstall was not the machine before it: $((Get-Content -LiteralPath $divergent) -join '; ')" }
        $soft = Join-Path $Out 'soft-seen.txt'
        if (Test-Path -LiteralPath $soft) { Say "seen, and expected until the removal takes them (soft: `$extractedSoft is set; they fail nothing): $((Get-Content -LiteralPath $soft) -join ' | ')" }
        if ($held.Count -gt 0) { Fail ($held -join ' | ') }
        Say 'the machine after every uninstall was the machine before it, the data directory and the harness root aside'
    }

    'Install' {
        if (-not $Feed -or -not $Version -or -not $Script) { Fail '-Feed, -Version and -Script are needed.' }
        $before = Get-UserPath

        # The pinned line reads its pin: one naming another version than the feed's is refused, naming both, and places
        # nothing, before the pin naming this one installs.
        # -NoModifyPath, which a release before 0.49 took, is refused with its own sentence and installs nothing: every
        # install puts jason in the terminal. Recorded, not thrown, so the install below is still read.
        if (-not $Pinned) {
            $refusal = "-NoModifyPath is not taken: every install puts jason in the terminal, on this account's Path. Nothing was installed: run this again without it."
            $noPath = Invoke-InstallScript $Script @('-NoModifyPath')
            $placed = @($app, $commandLine) | Where-Object { Test-Path -LiteralPath $_ }
            if ($noPath.Exit -eq 0) { Fail-Held "install.ps1 -NoModifyPath ended 0" }
            elseif (-not $noPath.Flat.Contains(($refusal -replace '\s+', ''))) { Fail-Held "install.ps1 -NoModifyPath was refused without its own sentence: $($noPath.Said)" }
            elseif ($placed) { Fail-Held "the refused install.ps1 -NoModifyPath left $($placed -join ', ')" }
            elseif ((Count-Entry $bundle) -ne 0) { Fail-Held 'the refused install.ps1 -NoModifyPath put the bundle folder on the Path' }
            else { Say "install.ps1 -NoModifyPath was refused with its own sentence, '$refusal', and installed nothing" }
        }

        $pin = @()
        if ($Pinned) {
            $wrong = Invoke-InstallScript $Script @('-Version', '9.9.9')
            if ($wrong.Exit -eq 0) { Fail "install.ps1 -Version 9.9.9 ended 0 against a feed of $Version" }
            if (-not $wrong.Flat.Contains(('-Version asked for 9.9.9 and' -replace '\s+', '')) -or -not $wrong.Flat.Contains("names$Version;nothingwasinstalled")) { Fail "install.ps1 -Version 9.9.9 was refused for another reason than the pin: $($wrong.Said)" }
            if (Test-Path -LiteralPath $app) { Fail "the refused pinned install left $app" }
            Say "install.ps1 -Version 9.9.9 against a feed of $Version was refused, naming both versions, and placed nothing: the pin is read"
            $pin = @('-Version', $Version)
        }

        # The agent's install: the script run as a file in Windows PowerShell, as the one-liner's shell runs it; by the
        # pinned line, -Version, where this leg is the pinned one.
        $first = Invoke-InstallScript $Script $pin
        if ($first.Exit -ne 0) { Fail "install.ps1 $($pin -join ' ') exited with $($first.Exit)" }
        Say "install.ps1 ran $(if ($Pinned) { "by the pinned line: -Version $Version" } else { 'unpinned, with no -Version, as an agent runs it' })"
        $printed = @($first.Said -split "`n" | Where-Object { $_.Trim() })
        if ($printed.Count -eq 0) { Fail "install.ps1 $($pin -join ' ') printed nothing" }
        $lastLine = $printed[-1]
        if (-not $lastLine.TrimEnd().EndsWith($carried, [StringComparison]::OrdinalIgnoreCase)) { Fail "install.ps1's last line does not end with the full path of jason, $carried, for a shell already open: $lastLine" }
        Say "install.ps1 installed Jason Desktop and ended on the full path of jason for a shell already open"

        # The place, and the version.
        if (-not (Test-Path -LiteralPath (Join-Path $app 'Jason.exe') -PathType Leaf)) { Fail "no Jason.exe at $app" }
        if ((Read-Version $carried) -ne $Version) { Fail "$carried is $(Read-Version $carried), not $Version" }
        $said = (& $carried --version)
        if ((($said -split '\+')[0]) -ne $Version) { Fail "the carried jason says '$said', not $Version" }
        Say "the application is at $app and its jason says $Version"

        # The terminal command: one entry naming the bundle folder, the Path's kind kept, and a new shell finds it.
        $after = Get-UserPath
        if ((Count-Entry $bundle) -ne 1) { Fail "the user Path holds $bundle $(Count-Entry $bundle) time(s), not once: $($after.Raw)" }
        if ($after.Kind -ne $before.Kind -and $before.Kind -ne 'absent') { Fail "the install turned the user Path from $($before.Kind) into $($after.Kind)" }
        foreach ($entry in $before.Entries) { if ($after.Entries -cnotcontains $entry) { Fail "the install rewrote the user Path entry '$entry'" } }
        # The shape the PathShape step gave the account's Path, held by name: ExpandString into ExpandString, and the
        # entry spelled through a variable still unexpanded.
        if ($before.Kind -ceq 'ExpandString' -and $before.Entries -ccontains $keptEntry) {
            if ($after.Kind -cne 'ExpandString') { Fail "the install turned the user Path from ExpandString into $($after.Kind)" }
            if ($after.Entries -cnotcontains $keptEntry) { Fail "the install expanded or dropped the user Path entry '$keptEntry'" }
            Say "the user Path is ExpandString before and after the install, and its entry '$keptEntry' is still unexpanded"
        } else {
            # Both proof jobs give the Path that shape first, by the PathShape step, and a guard holds them to it; a run
            # without it still holds the Path's kind and entries as they were.
            Say "the user Path was given no ExpandString shape before this install (kind $($before.Kind)): its kind and entries are held as they were"
        }
        $found = @(Find-InNewShell 'jason')
        if ($found.Count -lt 2) { Fail "a new shell looking for jason said: $(if ($found) { $found -join ' / ' } else { 'nothing' })" }
        if ($found[0] -ine $carried) { Fail "a new shell finds jason at '$($found[0])', not $carried" }
        if ((($found[1] -split '\+')[0]) -ne $Version) { Fail "jason in a new shell says '$($found[1])', not $Version" }
        Say "a new shell finds jason at $carried, and it says $Version; the user Path is still $($after.Kind) and every entry it held is kept"
        if (@(Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps') -Filter 'jason*' -ErrorAction SilentlyContinue).Count -ne 0) { Fail 'a jason command file was placed in WindowsApps' }

        # The skills of both hosts found, each recorded, and the person's own skill of a name Jason ships left alone.
        foreach ($root in @('.claude\skills', '.agents\skills')) {
            $record = Join-Path $env:USERPROFILE "$root\.jason-skills.json"
            if (-not (Test-Path -LiteralPath $record)) { Fail "no skills record at $record" }
        }
        if ((Get-Content -LiteralPath (Join-Path $ownSkill 'SKILL.md') -Raw) -cne "a person's own skill`n") { Fail "the person's own skill at $ownSkill was changed" }
        if ((Get-Content -LiteralPath (Join-Path $env:USERPROFILE '.agents\skills\.jason-skills.json') -Raw).Contains('"quick-start/')) { Fail "the record at .agents\skills claims the person's own quick-start" }
        if (-not ($first.Flat.Contains('quick-start'))) { Fail "the install did not name the person's own quick-start it left alone" }
        Say "the skills of Claude Code and Codex are recorded in both folders, and the person's own quick-start is left alone and named"

        # The role skills, deployed into the data directory by the agent's install.
        if (-not (Test-Path -LiteralPath (Join-Path $data 'skills\roles\.jason-skills.json'))) { Fail "no role skills record under $data" }
        Say "the role skills are recorded under $data"

        # Run again as the one-liner runs it: its text in the shell that typed it, which keeps nothing of it, and over the
        # version already installed, which downloads nothing.
        # The pinned one-liner runs the text as a script block given -Version, as the page's pinned line does.
        $text = if ($Pinned) { "& ([scriptblock]::Create((Get-Content -LiteralPath '$Script' -Raw))) -Version $Version" } else { "Invoke-Expression (Get-Content -LiteralPath '$Script' -Raw)" }
        # The application and the jason it carries, read before the run again: a run over the version already here leaves
        # both as they are.
        $executablesBefore = @((Join-Path $app 'Jason.exe'), $carried) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
        $runAgain = Invoke-Program 'powershell.exe' @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', "$text; if (Get-Command Install-Jason -ErrorAction SilentlyContinue) { 'Install-Jason is still defined'; exit 9 }") @{ JASON_INSTALL_FEED = $Feed }
        if ($runAgain.Exit -ne 0) { Fail "install.ps1's text run as the one-liner runs it exited with $($runAgain.Exit)" }
        if (-not $runAgain.Flat.Contains(("Jason Desktop $Version is already installed" -replace '\s+', ''))) { Fail 'the second run did not say the version is already installed' }
        if ((Count-Entry $bundle) -ne 1) { Fail 'a second run added a second Path entry' }
        $executablesAfter = @((Join-Path $app 'Jason.exe'), $carried) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
        if (($executablesAfter -join ' ') -cne ($executablesBefore -join ' ')) { Fail "the run again over the version already installed changed $(Join-Path $app 'Jason.exe') or $carried" }
        $licenceLine = "Jason $Version is licensed under the MIT licence: https://github.com/reply-team/jason-ai/blob/v$Version/LICENSE"
        if (-not $runAgain.Flat.Contains(($licenceLine -replace '\s+', ''))) { Fail "the run again did not say the licence: $licenceLine" }
        Say 'run again as the one-liner runs it, it downloads nothing, adds nothing to the Path and leaves no function behind'
        Say "the run again left Jason.exe and the carried jason as they were, and said: $licenceLine"

        # The machine set-up an application's start runs, and the installer's own, with no flags: nothing written under the
        # data directory, so a person's first start still opens on the Demo.
        $fresh = Join-Path $env:RUNNER_TEMP 'person-start-data'
        if (Test-Path -LiteralPath $fresh) { Remove-Item -LiteralPath $fresh -Recurse -Force }
        $start = Invoke-Program $carried @('install') @{ JASON_DATA_DIR = $fresh; JASON_Harness__Root = (Join-Path $env:RUNNER_TEMP 'person-start-root') }
        if ($start.Exit -ne 0) { Fail "jason install, as an application's start runs it, exited with $($start.Exit)" }
        $written = @(if (Test-Path -LiteralPath $fresh) { Get-ChildItem -LiteralPath $fresh -Recurse -Force })
        if ($written.Count -ne 0) { Fail "jason install with no flags wrote under the data directory: $($written.FullName -join ', ')" }
        Say 'jason install with no flags, as an application start and the installer run it, writes no entry under the data directory'
    }

    'ServedFeed' {
        if (-not $Feed -or -not $Version -or -not $Script) { Fail '-Feed, -Version and -Script are needed.' }
        # The feed this job assembled, served over http on this machine, and install.ps1 run by Windows PowerShell 5.1
        # against its address: the path every real install line takes and a feed in a directory never reaches - TLS 1.2
        # and no progress bar under 5.1, and every file fetched by Invoke-WebRequest. The feed's address alone names it:
        # JASON_INSTALL_FEED is taken out of the run.
        $served = 'http://127.0.0.1:8099'
        # The shell that runs it, said on a line of its own and held to Windows PowerShell 5.1: powershell.exe as this
        # account finds it, asked for its own path, edition and version, so that a pwsh found first by that name shows here.
        $asked = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes('((Get-Process -Id $PID).Path, $PSVersionTable.PSEdition, $PSVersionTable.PSVersion.ToString()) -join "|"'))
        $shell = Invoke-Program 'powershell.exe' @('-NoProfile', '-NonInteractive', '-EncodedCommand', $asked)
        $shellPath, $shellEdition, $shellVersion = "$(@($shell.Said -split "`n" | Where-Object { $_.Contains('|') })[-1])".Trim() -split '\|', 3
        Say "install.ps1 runs in $shellPath, PowerShell $shellVersion, edition $shellEdition"
        if ($shell.Exit -ne 0 -or $shellEdition -cne 'Desktop' -or -not "$shellVersion".StartsWith('5.1.')) { Fail "powershell.exe is not Windows PowerShell 5.1: it said '$($shell.Said)'" }
        $serverLog = Join-Path $Out 'served-feed-server.log'
        $server = Start-FeedServer $Feed 8099 $serverLog
        try {
            $install = Invoke-Program 'powershell.exe' @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $Script, '-Feed', $served) @{ JASON_INSTALL_FEED = $null }
            if ($install.Exit -ne 0) { Fail "install.ps1 -Feed $served exited with $($install.Exit)" }
            if (-not $install.Flat.Contains(("Downloading Jason Desktop $Version for Windows from $served ..." -replace '\s+', ''))) { Fail "install.ps1 -Feed $served did not say it downloads from $served" }
        } finally {
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
            $null = $server.WaitForExit(10000)
        }
        Say "install.ps1 -Feed $served ran in Windows PowerShell 5.1 and said it downloads Jason Desktop $Version from $served"

        # Each file came over the web, from this feed and nothing else: the server's own record of the three requests.
        $requests = "$(Get-Content -LiteralPath $serverLog -Raw -ErrorAction SilentlyContinue)"
        foreach ($requested in @('manifest.json', 'checksums.txt', 'jason-desktop-windows-x64-setup.exe')) {
            if ($requests -notmatch ('"GET /' + [regex]::Escape($requested) + ' HTTP/1\.[01]" 200')) { Fail "the served feed's log records no answered request for $requested" }
        }
        Say 'the served feed answered install.ps1 its manifest, its checksums and the setup, each with 200'

        # Installed, at this version.
        if (-not (Test-Path -LiteralPath (Join-Path $app 'Jason.exe') -PathType Leaf)) { Fail "no Jason.exe at $app" }
        if ((Read-Version $carried) -ne $Version) { Fail "$carried is $(Read-Version $carried), not $Version" }

        # The role skills: deployed by the install's jason install --roles from the skills beside the jason it carries,
        # never from a feed, so their record names a folder of the application.
        $rolesRecord = Join-Path $data 'skills\roles\.jason-skills.json'
        if (-not (Test-Path -LiteralPath $rolesRecord)) { Fail "no role skills record at $rolesRecord" }
        $recorded = Get-Content -LiteralPath $rolesRecord -Raw
        $sources = @([regex]::Matches($recorded, '"source"\s*:\s*("(?:[^"\\]|\\.)*")') | ForEach-Object { $_.Groups[1].Value | ConvertFrom-Json })
        if ($sources.Count -eq 0) { Fail "the role skills' record names no source: $recorded" }
        $outside = @($sources | Where-Object { -not ([string]$_).StartsWith($app.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) })
        if ($outside.Count -ne 0) { Fail "the role skills' record names a source outside $app`: $($outside -join '; ')" }
        Say "the role skills' record names the skills beside the application's jason: $(@($sources | Sort-Object -Unique) -join '; ')"

        # And a runtime over them says role_skills is ok.
        if ((Invoke-Program $carried @('runtime', 'status')).Exit -ne 0) {
            $runtime = Invoke-Program $carried @('runtime', 'start') @{ JASON_Update__CheckEnabled = 'false' }
            if ($runtime.Exit -ne 0) { Fail "a runtime could not be started over the installed role skills ($($runtime.Exit)): $($runtime.Said)" }
        }
        $status = Invoke-Program $carried @('status')
        if (-not $status.Flat.Contains('"name":"role_skills","required":true,"state":"ok"')) { Fail "jason status does not say role_skills is ok: $($status.Said)" }
        Say 'jason status says role_skills is ok'

        $licenceLine = "Jason $Version is licensed under the MIT licence: https://github.com/reply-team/jason-ai/blob/v$Version/LICENSE"
        if (-not $install.Flat.Contains(($licenceLine -replace '\s+', ''))) { Fail "install.ps1 -Feed $served did not say the licence: $licenceLine" }
        Say "install.ps1 -Feed $served said: $licenceLine"
    }

    'UninstallFromTerminal' {
        # Typed in a new terminal, where jason is the application's own on the Path; the data directory purged, so that a
        # start of the application after the verb, which the uninstaller makes unless --uninstalled-by-jason says the
        # terminal did it all, would show as a data directory written again.
        # The window opened once and closed, as a person leaves it, so the listing after sees what an application that ran
        # leaves; its start starts the runtime the uninstall then stops.
        Start-Process -FilePath (Join-Path $app 'Jason.exe') | Out-Null
        Wait-Until { @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -gt 0 } 60 'the window starting'
        Start-Sleep -Seconds 10
        # The application's first start sets the machine up (jason install): the skills record and the person's own skill
        # read right after it.
        $record = Join-Path $env:USERPROFILE '.agents\skills\.jason-skills.json'
        $claims = @(Select-String -LiteralPath $record -Pattern '"quick-start/[^"]*"' -AllMatches -ErrorAction SilentlyContinue | ForEach-Object { $_.Matches } | ForEach-Object { $_.Value } | Sort-Object -Unique)
        $ownText = if (Test-Path -LiteralPath (Join-Path $ownSkill 'SKILL.md')) { ((Get-Content -LiteralPath (Join-Path $ownSkill 'SKILL.md') -Raw).Trim() -split "`n")[0].Trim() } else { '(absent)' }
        Say "after the window's first start, the record at .agents\skills names quick-start in: $(if ($claims) { $claims -join '; ' } else { 'no line' }); the person's own SKILL.md reads: $ownText"
        if ($claims -or $ownText -ne "a person's own skill") { Fail-Held "the window's first start took the person's own quick-start: the record names it in $(if ($claims) { $claims -join '; ' } else { 'no line' }), and the file reads '$ownText'" }
        Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Wait-Until { @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -eq 0 } 30 'the window closing'
        # The runtime the window's start asked for may still be starting: it is given half a minute to answer.
        $until = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $until -and (Invoke-Program $carried @('runtime', 'status')).Exit -ne 0) { Start-Sleep -Seconds 2 }
        if ((Invoke-Program $carried @('runtime', 'status')).Exit -ne 0) {
            $runtime = Invoke-Program $carried @('runtime', 'start') @{ JASON_Update__CheckEnabled = 'false' }
            if ($runtime.Exit -ne 0) { Fail "a runtime could not be started before the uninstall ($($runtime.Exit))" }
        }
        $file = Join-Path $Out 'uninstall-terminal.json'
        $probe = "jason uninstall --purge-data --yes | Set-Content -LiteralPath '$file'; exit `$LASTEXITCODE"
        # The shell's own process is waited for, not what it started: the waiter jason uninstall leaves outlives it by design.
        $verb = Start-Process pwsh -ArgumentList '-NoProfile', '-NonInteractive', '-Command', $probe -UseNewEnvironment -PassThru -NoNewWindow
        $null = $verb.Handle
        if (-not $verb.WaitForExit(300000)) { Write-JasonProcesses; Fail 'jason uninstall in a new shell did not end within 300 s' }
        Get-Content -LiteralPath $file | ForEach-Object { Write-Host $_ }
        if ($verb.ExitCode -ne 0) { Fail "jason uninstall exited with $($verb.ExitCode)" }
        if (-not ((Get-Content -LiteralPath $file -Raw).Contains('Handed the application at'))) { Fail 'jason uninstall did not hand the application to its uninstaller' }

        # The waiter hands the folder to the uninstaller once the verb has ended, and the folder goes whole. Watched for
        # 150 s, twice a second: every run of the uninstaller, silent or not, with its exit and its visible windows, and the
        # waiter's Windows PowerShell. The window was closed before the verb, so a run without /S, which asks the person,
        # has no cause here: one fails the leg by name, quoting its window.
        $runs = @{}
        $asked = $null
        $started = Get-Date
        while (((Get-Date) - $started).TotalSeconds -lt 150 -and (Test-Path -LiteralPath $app)) {
            foreach ($run in @(Get-CimInstance -ClassName Win32_Process | Where-Object { $_.Name -ceq 'Uninstall Jason.exe' })) {
                $id = [int] $run.ProcessId
                if (-not $runs.ContainsKey($id)) {
                    $handle = Get-Process -Id $id -ErrorAction SilentlyContinue
                    if ($handle) { $null = $handle.Handle }
                    $silent = "$($run.CommandLine)" -match '(^|\s)/S(\s|$)'
                    $runs[$id] = @{ Id = $id; Process = $handle; Silent = $silent; Line = "$($run.CommandLine)"; Windows = [System.Collections.Generic.List[string]]::new() }
                    Write-Host "  at $([int]((Get-Date) - $started).TotalSeconds) s an uninstaller run, process $id, $(if ($silent) { 'with /S' } else { 'WITHOUT /S' }): $($run.CommandLine)"
                }
                foreach ($window in (Get-VisibleWindows @($id))) { if (-not $runs[$id].Windows.Contains($window)) { $runs[$id].Windows.Add($window); Write-Host "    its visible window: $window" } }
                if (-not $runs[$id].Silent -and $runs[$id].Windows.Count -gt 0 -and -not $asked) { $asked = $runs[$id] }
            }
            if ($asked) { break }
            Start-Sleep -Milliseconds 500
        }
        foreach ($id in $runs.Keys) {
            $run = $runs[$id]
            $exit = if ($run.Process -and $run.Process.HasExited) { $run.Process.ExitCode } else { 'still running' }
            Say "an uninstaller run after jason uninstall, $(if ($run.Silent) { 'with /S' } else { 'without /S' }): exit $exit, visible windows: $(if ($run.Windows.Count) { $run.Windows -join '; ' } else { 'none' })"
        }
        if ($asked) {
            Write-JasonProcesses
            Write-ProcessWindows $asked.Id
            $text = @(Use-WindowsType; [JasonProof.Windows]::Describe([uint32] $asked.Id) | Where-Object { $_ -match "class 'Static', text '[^']+'" } | ForEach-Object { ($_ -replace "^.*text '", '') -replace "', visible.*$", '' })
            Fail-Held "the waiter ran the uninstaller WITHOUT /S with the window closed, and it showed: $($asked.Windows -join '; '); its text: $($text -join ' / ')"
            Stop-Process -Id $asked.Id -Force -ErrorAction SilentlyContinue
            Wait-Until { -not (Get-Process -Id $asked.Id -ErrorAction SilentlyContinue) } 30 'the asking uninstaller ending'
            Say 'that run was ended unanswered; the application stays installed, and Installed apps takes it off in the next leg'
            $entry = @(Get-JasonUninstallEntries)
            if ($entry.Count -gt 0 -and (Get-QuietUninstall $entry[0]) -match '^"(?<file>[^"]+)"\s*(?<arguments>.*)$') {
                $quiet = Wait-Program $Matches['file'] @($Matches['arguments'])
                Say "the quiet uninstall Installed apps runs ended $quiet"
                if ($quiet -ne 0) { Fail-Held "the quiet uninstall Installed apps runs ended $quiet with nothing of Jason running" }
                Wait-Until { -not (Test-Path -LiteralPath $app) -or $quiet -ne 0 } 120 "the application's folder going after the quiet uninstall"
            }
            return
        }
        if (Test-Path -LiteralPath $app) {
            Write-JasonProcesses
            Fail "the application's folder had not gone 150 s after jason uninstall ended"
        }
        Wait-Until { @(Get-JasonUninstallEntries).Count -eq 0 } 30 "the Installed apps entry going"
        Start-Sleep -Seconds 5
        if (Test-Path -LiteralPath $data) { Fail "$data was written again after the purge: the uninstaller started the application although jason uninstall had done it all" }
        if ((Count-Entry $bundle) -ne 0) { Fail 'the Path entry naming the bundle folder is left' }
        if (@(Get-OwnProcesses $app).Count -ne 0) { Fail 'a process still runs from the application folder' }
        if (-not (Test-Path -LiteralPath (Join-Path $ownSkill 'SKILL.md'))) { Fail-Held "the person's own skill went with the uninstall" }
        Say 'jason uninstall from a new shell: the runtime stopped, the folder gone whole after the verb ended, no Installed apps entry, no Path entry, no data directory written again, the person''s own skill kept'
    }

    'InstallOverPrevious' {
        if (-not $Previous -or -not $Feed -or -not $Version) { Fail '-Previous, -Feed and -Version are needed.' }
        $old = Join-Path $Previous 'jason-desktop-windows-x64-setup.exe'
        $setup = Get-AuthenticodeSignature -LiteralPath $old
        if ($setup.Status -ne 'Valid' -or $setup.SignerCertificate.GetNameInfo('SimpleName', $false) -cne 'ReplyApp Inc.') { Fail "the previous release's setup is not signed by ReplyApp Inc.: $($setup.Status)" }
        $installed = Wait-Program $old @('/S')
        if ($installed -ne 0) { Fail "the previous release's setup exited with $installed" }
        $previousVersion = Read-Version $carried
        Say "the previous release's Jason Desktop $previousVersion installed by its own setup /S"

        # Open, as a person leaves it. A fresh 0.48.2 opens on the Demo, which starts no runtime; a person who works in it
        # has one running from the carried jason, which the updater then stops, so one is started from it here.
        $opened = @(Open-Window)
        if ((Test-Runtime) -ne 'yes') {
            if (-not (Start-PreviousRuntime $carried)) { Fail "the previous release's carried jason could not start a runtime" }
        }
        Say "the previous Jason Desktop $previousVersion open, and a runtime running from its carried jason: $(Test-Runtime)"

        # What 0.48.2's own updater does on the person's word (desktop/src/updates.ts install(), at v0.48.2): it stops the
        # runtime the application carries through jason runtime stop, keeps the application aside, and electron-updater
        # 6.8.11's quitAndInstall() starts the installer with --updated --force-run - not silent, so autoRunAppAfterInstall
        # decides the force-run - and quits the application at once. The copy kept aside is the Desktop's own, and is not
        # made here; the quit is the window's processes ended as the installer starts.
        $stopped = Invoke-Program $carried @('runtime', 'stop')
        if ($stopped.Exit -ne 0) { Fail "the previous release's jason runtime stop exited with $($stopped.Exit): $($stopped.Said)" }
        $installer = Start-Process -FilePath (Join-Path $Feed 'jason-desktop-windows-x64-setup.exe') -ArgumentList '--updated', '--force-run' -PassThru
        $null = $installer.Handle
        Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        $new = Wait-Started $installer "this build's setup --updated --force-run, as 0.48.2's updater starts it"
        if ($new -ne 0) { Fail "this build's setup run as 0.48.2's updater runs it exited with $new" }
        if ((Read-Version $carried) -ne $Version) { Fail "after the update the application is $(Read-Version $carried), not $Version" }
        $stillOpen = @($opened | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
        if ($stillOpen.Count -ne 0) { Fail "the previous window's processes $($stillOpen -join ', ') outlived the update's installer" }
        Say "this build's setup, started as 0.48.2's updater starts it (runtime stopped, --updated --force-run, the window quitting), put $Version in place"

        # Its own machine set-up ran: the Path names the bundle folder and a new shell finds this version.
        if ((Count-Entry $bundle) -ne 1) { Fail "after the update the user Path holds $bundle $(Count-Entry $bundle) time(s)" }
        $found = @(Find-InNewShell 'jason')
        if ($found.Count -lt 2) { Fail "a new shell looking for jason said: $(if ($found) { $found -join ' / ' } else { 'nothing' })" }
        if ($found[0] -ine $carried -or (($found[1] -split '\+')[0]) -ne $Version) { Fail "after the update a new shell finds '$($found -join ' ')'" }
        Wait-Until { @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -gt 0 } 60 'the application starting again after the update (--force-run)'
        Say "the installer's machine set-up put jason in the terminal, and --force-run started $Version again"

        # A person closes the window before an uninstall; the uninstall of an open application is the next step's to refuse.
        Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Wait-Until { @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -eq 0 } 30 'the window closing'
    }

    'InstallOverOpenWindow' {
        # A shape 0.48.2's updater never makes: this build's setup run by hand, silently, as an update, over an open window,
        # the Demo a fresh 0.48.2 opens on. The previous release is installed again for it, and taken off after by Installed
        # apps.
        if (-not $Previous -or -not $Feed -or -not $Version) { Fail '-Previous, -Feed and -Version are needed.' }
        $installed = Wait-Program (Join-Path $Previous 'jason-desktop-windows-x64-setup.exe') @('/S')
        if ($installed -ne 0) { Fail "the previous release's setup exited with $installed" }
        $opened = @(Open-Window)
        Say "the previous Jason Desktop $(Read-Version $carried) open again; a runtime answering: $(Test-Runtime)"
        # Whether the setup is hung or waits for the old application to end: a minute with the window open, the processes
        # then, the window ended as a quit ends it, and how long after that the setup ends, with the processes then.
        $installer = Start-Process -FilePath (Join-Path $Feed 'jason-desktop-windows-x64-setup.exe') -ArgumentList '--updated', '/S', '--force-run' -PassThru
        $null = $installer.Handle
        if ($installer.WaitForExit(60000)) {
            Say "the setup ended within 60 s with the window open, exit $($installer.ExitCode)"
        } else {
            Write-Host 'at 60 s, with the window open:'
            Write-ProcessTree $installer
            $main = @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' -and $_.CommandLine -notmatch '--type=' })
            $closed = $false
            foreach ($window in $main) { $running = Get-Process -Id $window.ProcessId -ErrorAction SilentlyContinue; if ($running -and $running.CloseMainWindow()) { $closed = $true } }
            $until = (Get-Date).AddSeconds(15)
            while ((Get-Date) -lt $until -and @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -gt 0) { Start-Sleep -Milliseconds 250 }
            $how = 'its main window closed'
            if (@(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -gt 0) {
                $how = if ($closed) { 'its main window closed, and then its processes ended, the close not having ended them in 15 s' } else { 'its processes ended, no main window answering a close' }
                $main | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
                Wait-Until { @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -eq 0 } 30 'the window''s processes ending'
            }
            $ended = Get-Date
            Say "the 0.48.2 window ended at $($ended.ToUniversalTime().ToString('HH:mm:ss.fff'))Z: $how"
            if ($installer.WaitForExit(300000)) {
                Say "the setup ended $([int]((Get-Date) - $ended).TotalSeconds) s after the window ended, exit $($installer.ExitCode)"
            } else {
                Write-Host '300 s after the window ended:'
                Write-ProcessTree $installer
                Fail 'the setup did not end within 300 s after the window ended: it is hung, not waiting for the application'
            }
            Write-Host 'after the setup ended:'
            Write-ProcessTree $installer
        }
        $new = $installer.ExitCode
        if ($new -ne 0) { Fail "this build's setup run by hand over the open window exited with $new" }
        if ((Read-Version $carried) -ne $Version) { Fail "after the setup the application is $(Read-Version $carried), not $Version" }
        Say "this build's setup with --updated /S --force-run, run by hand over an open window, put $Version in place"
        Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Wait-Until { @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -eq 0 } 30 'the window closing'
    }

    'SilentUpdateLegs' {
        # The silent setups of this build over an installed previous release, each leg from that release installed afresh
        # (docs/release-and-update.md, section 7; desktop/installer/uninstall.nsh). A silent setup never shows a window:
        # every visible top-level window of the setup and of every process it started is looked for twice a second while
        # it runs, and any one is a failure.
        #  1. --updated /S, the arguments the window-less update hands the installer, over a window that never quits: 3
        #     after the include's bound of about 60 s (here at most 150 s), the old version untouched and still running;
        #     a 2 within about 30 s would say the include's look missed the window. Then, the window ended, the same setup
        #     again: 0, so the first left no installer's mutex held.
        #  2. plain /S over a window: 3, the old version untouched.
        #  3. plain /S over the application left in the tray, its window closed: 3, the old version untouched.
        #  4. --updated /S, the old application quitting 20 s late: 0.
        #  5. --updated /S, the application not running, while another process holds a file of its folder, so the old
        #     version's uninstaller refuses: 2, the old version still in place and opening.
        # Then this build is put back over it by --updated /S, the application not running: 0, so the uninstall that
        # closes the leg is this build's.
        if (-not $Previous -or -not $Feed -or -not $Version) { Fail '-Previous, -Feed and -Version are needed.' }
        $old = Join-Path $Previous 'jason-desktop-windows-x64-setup.exe'
        $new = Join-Path $Feed 'jason-desktop-windows-x64-setup.exe'
        $failures = [System.Collections.Generic.List[string]]::new()
        function End-Application {
            Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
            Wait-Until { @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -eq 0 } 30 'the application''s processes ending'
        }
        function From-Previous([bool] $open = $true) {
            if (Test-Path -LiteralPath $carried) {
                End-Application
                $entry = @(Get-JasonUninstallEntries)
                if ($entry.Count -gt 0 -and (Get-QuietUninstall $entry[0]) -match '^"(?<file>[^"]+)"\s*(?<arguments>.*)$') {
                    $null = Wait-Program $Matches['file'] @($Matches['arguments'])
                    Wait-Until { -not (Test-Path -LiteralPath $app) } 120 "the application's folder going before the leg"
                }
            }
            $installed = Wait-Program $old @('/S')
            if ($installed -ne 0) { Fail "the previous release's setup exited with $installed" }
            $script:previousVersion = Read-Version $carried
            if ($open) {
                $null = Open-Window -RuntimeSeconds 0
                Say "the previous release $previousVersion installed, its window shown"
            } else {
                Say "the previous release $previousVersion installed, not running"
            }
        }
        function Test-Leg([string] $name, [string[]] $arguments, [int[]] $expected, [scriptblock] $whileRunning = $null) {
            Write-Host "--- $name"
            $watched = Watch-Setup $new $arguments 150 $whileRunning
            $said = "$name - " + $(if ($watched.Ended) { "the setup ended after $($watched.Seconds) s, exit $($watched.Exit)" } else { "the setup had not ended after $($watched.Seconds) s" }) + ", the application $(Read-Version $carried)"
            Say $said
            if ($watched.Windows.Count -gt 0) { $failures.Add("$name showed a window: $($watched.Windows -join '; ')") }
            if (-not $watched.Ended) { $failures.Add("$name did not end within 150 s") }
            elseif ($expected -notcontains $watched.Exit) { $failures.Add("$name exited $($watched.Exit), not $($expected -join ' or ')") }
            return $watched
        }
        function Assert-Untouched([string] $name, [bool] $running) {
            if ((Read-Version $carried) -ne $previousVersion) { $failures.Add("after $name the application is $(Read-Version $carried), not the previous $previousVersion") }
            $alive = @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -gt 0
            if ($running -and -not $alive) { $failures.Add("after $name the previous release no longer runs") }
        }

        # 1. Over a window that never quits, then the same setup again with the window ended.
        # A sub-leg that cannot finish is recorded, and the next one still runs from a fresh install of its own.
        try {
            From-Previous
            $firstLeg = Test-Leg 'leg 1, --updated /S over a window that never quits' @('--updated', '/S') @(3)
            if ($firstLeg.Ended -and $firstLeg.Exit -eq 2 -and $firstLeg.Seconds -lt 30) { $failures.Add('leg 1 ended 2 within 30 s: the include''s look missed the window, and the old uninstaller refused') }
            if ($firstLeg.Ended -and $firstLeg.Exit -eq 3 -and $firstLeg.Seconds -lt 45) { $failures.Add("leg 1 ended 3 after $($firstLeg.Seconds) s, before the include's bound of about 60 s") }
            Assert-Untouched 'leg 1' $true
            End-Application
            $null = Test-Leg 'leg 1, the same setup again once the window ended' @('--updated', '/S') @(0)
            if ((Read-Version $carried) -ne $Version) { $failures.Add("after leg 1's second setup the application is $(Read-Version $carried), not $Version") }
        } catch {
            $failures.Add("leg 1 did not finish: $($_.Exception.Message)")
        }

        # 2. Plain /S over a window.
        # A sub-leg that cannot finish is recorded, and the next one still runs from a fresh install of its own.
        try {
            From-Previous
            $null = Test-Leg 'leg 2, plain /S over a window' @('/S') @(3)
            Assert-Untouched 'leg 2' $true
        } catch {
            $failures.Add("leg 2 did not finish: $($_.Exception.Message)")
        }

        # 3. Plain /S over the application in the tray: its window closed as a person closes it, the application left
        # running.
        # A sub-leg that cannot finish is recorded, and the next one still runs from a fresh install of its own.
        try {
            From-Previous
            $main = @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' -and $_.CommandLine -notmatch '--type=' })
            foreach ($window in $main) { $running = Get-Process -Id $window.ProcessId -ErrorAction SilentlyContinue; if ($running) { $null = $running.CloseMainWindow() } }
            Start-Sleep -Seconds 5
            $shown = @($main | ForEach-Object { Get-VisibleWindows @([int] $_.ProcessId) }) | Where-Object { $_ }
            if (@(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -eq 0) {
                $failures.Add('leg 3: closing the window ended the application, so there was no application in the tray to set up over')
            } else {
                Say "leg 3: the window closed, the application left running in the tray; its visible windows: $(if ($shown) { $shown -join '; ' } else { 'none' })"
                $null = Test-Leg 'leg 3, plain /S over the application in the tray' @('/S') @(3)
                Assert-Untouched 'leg 3' $true
            }
        } catch {
            $failures.Add("leg 3 did not finish: $($_.Exception.Message)")
        }

        # 4. The old application quitting 20 s after the setup started.
        # A sub-leg that cannot finish is recorded, and the next one still runs from a fresh install of its own.
        try {
            From-Previous
            $late = { param($elapsed) if ($elapsed -ge 20 -and -not $script:quit) { $script:quit = $true; End-Application } }
            $script:quit = $false
            $null = Test-Leg 'leg 4, --updated /S, the old application quitting 20 s late' @('--updated', '/S') @(0) $late
            if (-not $script:quit) { $failures.Add('leg 4: the setup ended before the application was made to quit') }
            if ((Read-Version $carried) -ne $Version) { $failures.Add("after leg 4 the application is $(Read-Version $carried), not $Version") }
        } catch {
            $failures.Add("leg 4 did not finish: $($_.Exception.Message)")
        }

        # 5. The old uninstaller refusing: a file of the application's folder held by another process, no delete shared.
        # A sub-leg that cannot finish is recorded, and the next one still runs from a fresh install of its own.
        $holder = $null
        try {
            From-Previous $false
            $held = @('LICENSE.electron.txt', 'LICENSES.chromium.html', 'resources\app.asar') | ForEach-Object { Join-Path $app $_ } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
            if (-not $held) { Fail 'leg 5: no file of the application''s folder to hold' }
            $release = $scratchHeldFile
            Remove-Item -LiteralPath $release -Force -ErrorAction SilentlyContinue
            $holder = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', "`$f = [System.IO.File]::Open('$held', 'Open', 'Read', 'Read'); while (-not (Test-Path -LiteralPath '$release')) { Start-Sleep -Milliseconds 200 }; `$f.Dispose()" -PassThru -WindowStyle Hidden
            Register-Scratch 'the process holding a file of the application''s folder' $holder
            Start-Sleep -Seconds 3
            Say "leg 5: $(Split-Path -Leaf $held) of the application's folder held open by process $($holder.Id), sharing read alone"
            $null = Test-Leg 'leg 5, --updated /S while the old uninstaller cannot remove the folder' @('--updated', '/S') @(2)
            New-Item -ItemType File -Path $release -Force | Out-Null
            $null = $holder.WaitForExit(15000)
            Assert-Untouched 'leg 5' $false
            $opened = @(Open-Window -RuntimeSeconds 0)
            if ($opened.Count -eq 0) { $failures.Add('after leg 5 the previous release does not open') } else { Say "after leg 5 the previous release $(Read-Version $carried) opens" }
            End-Application
        } catch {
            $failures.Add("leg 5 did not finish: $($_.Exception.Message)")
        } finally {
            # Released, and waited for as the sub-leg's own release waits, so the put-back below never meets the file held.
            New-Item -ItemType File -Path $scratchHeldFile -Force | Out-Null
            if ($holder) { $null = $holder.WaitForExit(15000) }
        }

        # This build put back over the previous release leg 5 leaves, the application not running, as leg 4 puts it: the
        # uninstall that closes the leg is then this build's, so the leg's compare holds this build's removal, as every other
        # leg's does, and excuses nothing the previous release's own uninstall keeps.
        try {
            End-Application
            $null = Test-Leg 'this build put back, --updated /S over the previous release not running' @('--updated', '/S') @(0)
            if ((Read-Version $carried) -ne $Version) { $failures.Add("after this build was put back the application is $(Read-Version $carried), not $Version") }
        } catch {
            $failures.Add("putting this build back did not finish: $($_.Exception.Message)")
        }

        if ($failures.Count -gt 0) { Fail ($failures -join '; ') }
    }

    'UninstallWaiterShown' {
        # jason uninstall's waiter (dotnet/src/Jason.Cli/Uninstall/IInstallationRemover.cs, UninstallerHandOff): where its
        # silent uninstaller ends 3 because Jason Desktop was opened after the verb's own check, it runs the uninstaller
        # once more without /S, which shows the person its question. Here: this build installed by the stand-in script,
        # a runtime started, jason uninstall started with a skill file it removes held open, and the window opened once the
        # verb has stopped that runtime, past its own check, while the hold keeps the verb running. The second run is found, its window seen, and it is ended without an answer; the folder is still
        # there, and the waiter's line is in the desktop's log of the day. Installed apps then takes the application off.
        if (-not $Script -or -not $Feed) { Fail '-Script and -Feed are needed.' }
        $installed = Invoke-InstallScript $Script
        if ($installed.Exit -ne 0) { Fail "install.ps1 exited with $($installed.Exit): $($installed.Said)" }
        if ((Invoke-Program $carried @('runtime', 'start')).Exit -ne 0) { Fail 'a runtime could not be started' }
        $descriptor = Join-Path $data 'run\runtime.json'
        # A hold the leg controls, the silent leg's own: a file of this build's skills, which the verb removes after it stops
        # the runtime, held open sharing read alone, so the verb tries it again for its wait (InstallationRemovers.HeldFiles)
        # and is still running when the window opens; let go once the window's process runs. Without it the verb could end
        # before the window opened, and the leg would prove nothing.
        $skillsRecord = Join-Path $env:USERPROFILE '.claude\skills\.jason-skills.json'
        $skillsRoot = Split-Path -Parent $skillsRecord
        $named = @(try { @((Get-Content -LiteralPath $skillsRecord -Raw | ConvertFrom-Json).packs) | ForEach-Object { @($_.files) } | ForEach-Object { [string]$_.path } } catch { })
        $held = $named | Where-Object { $_ } | ForEach-Object { Join-Path $skillsRoot ($_ -replace '/', '\') } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        if (-not $held) { Fail "no file named by this build's skills record at $skillsRecord to hold the verb by" }
        $release = $scratchHeldFile
        Remove-Item -LiteralPath $release -Force -ErrorAction SilentlyContinue
        $holder = $null
        try {
            $holder = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', "`$f = [System.IO.File]::Open('$held', 'Open', 'Read', 'Read'); while (-not (Test-Path -LiteralPath '$release')) { Start-Sleep -Milliseconds 200 }; `$f.Dispose()" -PassThru -WindowStyle Hidden
            Register-Scratch 'the process holding a skill file jason uninstall removes' $holder
            Wait-Until { try { ([System.IO.File]::Open($held, 'Open', 'ReadWrite', 'ReadWrite')).Dispose(); $false } catch { $true } } 30 "$held held, refusing a writer"
            Say "$held held open by process $($holder.Id), sharing read alone"
            $verb = Start-Process -FilePath $carried -ArgumentList 'uninstall', '--yes' -PassThru -WindowStyle Hidden
            $null = $verb.Handle
            Wait-Until { -not (Test-Path -LiteralPath $descriptor) -or $verb.HasExited } 120 'jason uninstall stopping the runtime'
            if ($verb.HasExited) { Fail "jason uninstall ended (exit $($verb.ExitCode)) before the window could be opened past its check, with $held held: the leg proves nothing" }
            Start-Process -FilePath (Join-Path $app 'Jason.exe') | Out-Null
            Wait-Until { $verb.HasExited -or @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -gt 0 } 30 'the window''s process starting'
            $landed = -not $verb.HasExited
            New-Item -ItemType File -Path $release -Force | Out-Null
            $null = $holder.WaitForExit(15000)
            if (-not $landed) { Fail "jason uninstall ended (exit $($verb.ExitCode)) before the window's process ran, with $held held: the leg proves nothing" }
            Say "the window opened once jason uninstall had stopped the runtime, past its own check, while the verb was still running, held at $held; then let go"
        } finally {
            New-Item -ItemType File -Path $scratchHeldFile -Force | Out-Null
            if ($holder) { $null = $holder.WaitForExit(15000) }
        }
        if (-not $verb.WaitForExit(300000)) { Fail 'jason uninstall did not end within 300 s' }
        Say "jason uninstall ended, exit $($verb.ExitCode)"
        if (Test-Path -LiteralPath $held) { Fail "jason uninstall ended and $held, which it was held at, is still there: it was let go after the verb's wait ran out" }
        $shown = $null
        $until = (Get-Date).AddSeconds(150)
        while ((Get-Date) -lt $until -and -not $shown) {
            $copies = @(Get-CimInstance -ClassName Win32_Process | Where-Object { $_.Name -ceq 'Uninstall Jason.exe' -and $_.CommandLine -notmatch '(^|\s)/S(\s|$)' })
            foreach ($copy in $copies) { $windows = @(Get-VisibleWindows @([int] $copy.ProcessId)); if ($windows.Count -gt 0) { $shown = @{ Process = $copy; Windows = $windows } } }
            if (-not $shown) { Start-Sleep -Milliseconds 500 }
        }
        if (-not $shown) {
            Write-Host 'every uninstaller process now:'
            Get-CimInstance -ClassName Win32_Process | Where-Object { $_.Name -like 'Uninstall*' -or $_.Name -like 'Un_*' -or $_.Name -ceq 'powershell.exe' } | ForEach-Object { Write-Host "  $($_.ProcessId), started by $($_.ParentProcessId): $($_.CommandLine)" }
            Fail 'no uninstaller run without /S showed a window within 150 s of jason uninstall ending'
        }
        Say "the waiter ran the uninstaller once more without /S (process $($shown.Process.ProcessId)), and it showed: $($shown.Windows -join '; ')"
        if (-not (Test-Path -LiteralPath (Join-Path $app 'Jason.exe'))) { Fail 'the application''s folder went while the window ran' }
        Stop-Process -Id $shown.Process.ProcessId -Force
        $logs = Join-Path $data 'logs'
        Wait-Until { @(Get-ChildItem -LiteralPath $logs -Filter 'desktop-*.jsonl' -ErrorAction SilentlyContinue | Select-String -SimpleMatch 'uninstaller_exited').Count -gt 0 } 30 'the waiter''s line in the desktop''s log of the day'
        Say 'that run ended without an answer, the folder is still there, and the waiter left its uninstaller_exited line in the desktop''s log of the day'
        Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Wait-Until { @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -eq 0 } 30 'the window closing'
    }

    'UpdateApply' {
        # The carried jason update apply on a real install of this test build, to a next test build - its own jason, its
        # setup and its feed - served on the loopback (docs/release-and-update.md, "Handing the update to Jason Desktop").
        # First with a window open, which refuses a request naming a feed (window_feed). Then with no window: the verb
        # hands the update to Jason Desktop, which installs it with --updated /S, and the new carried jason finishes it;
        # this runner's administrator has no split token, so the verb is not refused with update_elevated. While each
        # runs, a second reader opens the request's file as fast as it can, so the Desktop's writes of it meet a reader
        # holding it as the verb's own poll does; no step may be lost to that (desktop/src/update-request.ts,
        # writeRequestFile), and the desktop's log may not say a step went unwritten. The test build's updater checks no
        # publisher, so this says nothing of the signature check.
        if (-not $Feed -or -not $Version -or -not $Script -or -not $Next) { Fail '-Feed, -Version, -Script and -Next are needed.' }
        $nextVersion = "$Version.1"
        $failure = $null
        $installed = Invoke-InstallScript $Script
        if ($installed.Exit -ne 0) { Fail "install.ps1 exited with $($installed.Exit): $($installed.Said)" }
        if ((Read-Version $carried) -ne $Version) { Fail "the application installed is $(Read-Version $carried), not $Version" }
        $runtime = Invoke-Program $carried @('runtime', 'start')
        if ($runtime.Exit -ne 0) { Fail "a runtime could not be started before the update ($($runtime.Exit))" }

        $requests = Join-Path $data 'update'
        $stopReading = $scratchStopReading
        $readsFile = Join-Path $env:RUNNER_TEMP 'request-reads.txt'
        function Start-Reader {
            Remove-Item -LiteralPath $stopReading, $readsFile -Force -ErrorAction SilentlyContinue
            $loop = "`$n = 0; while (-not (Test-Path -LiteralPath '$stopReading')) { `$files = try { [System.IO.Directory]::GetFiles('$requests', 'desktop-*.json') } catch { @() }; foreach (`$f in `$files) { try { `$s = [System.IO.File]::Open(`$f, 'Open', 'Read', 'ReadWrite'); `$b = New-Object byte[] 65536; `$null = `$s.Read(`$b, 0, `$b.Length); Start-Sleep -Milliseconds 15; `$s.Dispose(); `$n++ } catch { } }; Start-Sleep -Milliseconds 2 }; Set-Content -LiteralPath '$readsFile' -Value `$n"
            $reader = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', $loop -PassThru -WindowStyle Hidden
            Register-Scratch 'the second reader of the update''s request file' $reader
            $reader
        }
        function Stop-Reader($reader) {
            New-Item -ItemType File -Path $stopReading -Force | Out-Null
            $null = $reader.WaitForExit(15000)
            return $(if (Test-Path -LiteralPath $readsFile) { [int](Get-Content -LiteralPath $readsFile -Raw) } else { -1 })
        }

        $server = Start-FeedServer $Next 8098 (Join-Path $Out 'feed-server-next-version.log')
        try {
            $feedAddress = 'http://127.0.0.1:8098/manifest.json'

            # A window open: the Desktop refuses a request that names a feed, and the verb says so.
            $null = Open-Window
            $reader = Start-Reader
            $refused = Invoke-Program $carried @('update', 'apply', '--feed', $feedAddress, '--drain-seconds', '0')
            $reads = Stop-Reader $reader
            $refused.Said | ForEach-Object { Write-Host $_ }
            if ($refused.Exit -ne 1 -or -not $refused.Flat.Contains('update_refused_by_desktop') -or -not $refused.Flat.Contains('window_feed')) { Fail "with a window open the update was not refused as update_refused_by_desktop, window_feed (exit $($refused.Exit))" }
            if ((Invoke-Program $carried @('runtime', 'status')).Exit -ne 0) { Fail 'the refused update left no runtime answering' }
            Say "with a window open the carried jason update apply was refused by the window, update_refused_by_desktop for window_feed, and the runtime answers; a second reader opened the request's file $reads time(s) meanwhile"
            Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
            Wait-Until { @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -eq 0 } 30 'the window closing'
            if ((Invoke-Program $carried @('runtime', 'status')).Exit -ne 0) { $null = Invoke-Program $carried @('runtime', 'start') }

            # No window open: the update goes through.
            $reader = Start-Reader
            $applied = Invoke-Program $carried @('update', 'apply', '--feed', $feedAddress, '--drain-seconds', '0')
            $reads = Stop-Reader $reader
            $applied.Said | ForEach-Object { Write-Host $_ }
            Say "with no window, a second reader opened the request's file $reads time(s) while the update ran"
            if ($applied.Flat.Contains('update_elevated')) { Fail 'the carried jason update apply was refused with update_elevated on a runner whose administrator has no split token' }
            Say 'the carried jason update apply was not refused with update_elevated'
            if ($applied.Exit -eq 0) {
                Wait-Until { (Read-Version $carried) -eq $nextVersion } 180 "the application's version.json naming $nextVersion"
                $said = (Invoke-Program $carried @('--version')).Said
                if ((("$said".Trim()) -split '\+')[0] -ne $nextVersion) { $failure = "after the update the carried jason says '$said', not $nextVersion" }
                elseif ((Invoke-Program $carried @('runtime', 'status')).Exit -ne 0) { $failure = 'no runtime answers after the update, which starts one again' }
                elseif (@(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -ne 0) { $failure = 'a window was started by an update asked for from a terminal' }
                else { Say "the carried jason update apply handed $nextVersion to Jason Desktop with no window; the application and its jason say $nextVersion and the runtime answers again" }
            } else {
                # What a refusal left: the application as it was, its jason at its path, the runtime answering again.
                $kept = if ((Read-Version $carried) -eq $Version -and (((("$((Invoke-Program $carried @('--version')).Said)".Trim()) -split '\+')[0]) -eq $Version) -and (Invoke-Program $carried @('runtime', 'status')).Exit -eq 0) { "the application, its jason and the runtime are as they were" } else { "the application is $(Read-Version $carried) and the runtime answers: $(Test-Runtime)" }
                $failure = if ($applied.Flat.Contains('keep_failed')) {
                    "the carried jason update apply was refused by Jason Desktop with keep_failed (exit $($applied.Exit)): the copy of the application kept aside to go back to could not be made; $kept"
                } else {
                    "the carried jason update apply exited with $($applied.Exit); $kept"
                }
            }
        } finally {
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        }

        $unwritten = @(Get-ChildItem -LiteralPath (Join-Path $data 'logs') -Filter 'desktop-*.jsonl' -ErrorAction SilentlyContinue | Select-String -SimpleMatch 'could not be written at the step')
        if ($unwritten.Count -gt 0) { $unwritten | ForEach-Object { Write-Host $_.Line }; $failure = (@($failure, 'the desktop''s log says a step of the request''s file went unwritten') | Where-Object { $_ }) -join '; ' }
        else { Say 'the desktop''s log says no step of the request''s file went unwritten' }

        $removed = Invoke-Program $carried @('uninstall', '--purge-data', '--yes')
        if ($removed.Exit -ne 0) { Fail "jason uninstall after the update exited with $($removed.Exit): $($removed.Said)" }
        Wait-Until { -not (Test-Path -LiteralPath $app) } 120 "the application's folder going after jason uninstall ended"
        if ($failure) { Fail $failure }
    }

    'UninstallFromInstalledApps' {
        $entry = @(Get-JasonUninstallEntries)
        if ($entry.Count -ne 1) { Fail "Installed apps lists $($entry.Count) Jason entries, not one" }
        $quiet = Get-QuietUninstall $entry[0]
        if ($quiet -notmatch '^"(?<file>[^"]+)"\s*(?<arguments>.*)$') { Fail "QuietUninstallString is not a quoted program and its arguments: $quiet" }
        $removal = Wait-Program $Matches['file'] @($Matches['arguments'])
        if ($removal -ne 0) { Fail "the uninstall Installed apps runs exited with $removal" }
        Wait-Until { -not (Test-Path -LiteralPath $app) } 60 "the application's folder going"
        Wait-Until { @(Get-JasonUninstallEntries).Count -eq 0 } 30 'the Installed apps entry going after the folder'
        if ((Count-Entry $bundle) -ne 0) { Fail 'the Path entry naming the bundle folder is left: the uninstaller did not run jason uninstall --keep-application' }
        foreach ($root in @('.claude\skills', '.agents\skills')) { if (Test-Path -LiteralPath (Join-Path $env:USERPROFILE "$root\.jason-skills.json")) { Fail "the skills record in $root is left" } }
        if (-not (Test-Path -LiteralPath $data)) { Fail "the data directory $data went, which only --purge-data takes" }
        Say "Installed apps' quiet uninstall: the folder, its entry, the Path entry and the skills gone, the data directory kept"
    }

    'FromPrevious' {
        if (-not $Previous -or -not $Feed -or -not $Version) { Fail '-Previous, -Feed and -Version are needed.' }
        $build = $env:JASON_PROOF_BUILD
        if ($build -cnotin @('test', 'release')) { Fail "JASON_PROOF_BUILD says '$build': it must say test (CI's build) or release (the release's packages)" }

        # The previous release's command-line Jason, installed by its own script from its own assets, as a person has it,
        # with a runtime running and its start at logon registered.
        $old = Invoke-Program 'powershell.exe' @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Previous 'install.ps1')) @{ JASON_INSTALL_FEED = $Previous }
        if ($old.Exit -ne 0) { Fail "the previous release's install.ps1 exited with $($old.Exit)" }
        $jason = Join-Path $commandLine 'jason.exe'
        $previousVersion = ((& $jason --version) -split '\+')[0]
        $quiet = @{ JASON_Update__CheckEnabled = 'false' }
        if (-not (Start-PreviousRuntime $jason $quiet)) { Fail 'the previous release could not start a runtime' }
        if ((Invoke-Program $jason @('runtime', 'autostart', 'enable') $quiet).Exit -ne 0) { Fail 'the previous release could not register its logon start' }
        if ((Count-Entry $commandLine) -ne 1) { Fail "the previous release's install did not put $commandLine on the user Path once" }
        Say "the previous release's command-line Jason $previousVersion installed by its own script at $jason, with a runtime running and a logon registration"

        # Its own update, pinned to this version, from the feed served on this machine. The update is the 0.48 parent's;
        # the move is this version's, which the parent hands the rest to.
        $server = Start-FeedServer $Feed 8099 (Join-Path $Out 'feed-server-from-previous.log')
        try {
            $update = Invoke-Program $jason @('update', 'apply', '--version', $Version, '--feed', 'http://127.0.0.1:8099/manifest.json', '--drain-seconds', '0') $quiet 900
            Set-Content -LiteralPath (Join-Path $Out 'update-from-previous.json') -Value $update.Said
            if ($update.Exit -ne 0) { Fail "the previous release's jason update apply --version $Version exited with $($update.Exit)" }
        } finally {
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        }
        $document = Read-Document $update.Said
        if (-not $document) { Fail 'the update printed no document' }
        if ($document.step -cne 'complete' -or $document.to -cne $Version) { Fail "the update ended at '$($document.step)' to '$($document.to)', not complete to $Version" }
        $move = $document.PSObject.Properties['move']
        if (-not $move -or -not $move.Value) { Fail 'the update document carries no move: the jason it handed to did not move the install' }
        if ($move.Value.state -cne 'done') { Fail "the move is $($move.Value.state) at $($move.Value.step): $($move.Value.reason)" }
        if ([string]$move.Value.application -ine $app) { Fail "the move put the application at $($move.Value.application), not $app" }
        Say "the previous release's own jason update apply --version $Version took the feed served on this machine and ended complete, its move done at $($move.Value.step)"

        # THE MOVE. The application in place, at this version.
        if (-not (Test-Path -LiteralPath (Join-Path $app 'Jason.exe') -PathType Leaf)) { Fail "no Jason.exe at $app" }
        if ((Read-Version $carried) -ne $Version) { Fail "$carried is $(Read-Version $carried), not $Version" }
        Say "Jason Desktop $Version is at $app"

        # The terminal command as the routine places it: the user Path names the bundle folder once and the command-line
        # install's folder no more, and a new shell finds the carried jason.
        if ((Count-Entry $bundle) -ne 1) { Fail "the user Path holds $bundle $(Count-Entry $bundle) time(s), not once: $((Get-UserPath).Raw)" }
        if ((Count-Entry $commandLine) -ne 0) { Fail "the user Path still names the retired $commandLine" }
        $found = @(Find-InNewShell 'jason')
        if ($found.Count -lt 2) { Fail "a new shell looking for jason said: $(if ($found) { $found -join ' / ' } else { 'nothing' })" }
        if ($found[0] -ine $carried) { Fail "a new shell finds jason at '$($found[0])', not $carried" }
        if ((($found[1] -split '\+')[0]) -ne $Version) { Fail "jason in a new shell says '$($found[1])', not $Version" }
        Say "a new shell finds jason at $carried and it says $Version; the user Path names $bundle once and $commandLine no more"

        # The runtime answering, from the carried file, and none from the retired one.
        $status = Invoke-Program $carried @('runtime', 'status') $quiet
        if ($status.Exit -ne 0) { Fail 'no runtime answers after the move' }
        $runtimes = @(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'jason.exe'" | Where-Object { "$($_.CommandLine)" -match '\bruntime\s+run\b' })
        $fromCarried = @($runtimes | Where-Object { [string]$_.ExecutablePath -ieq $carried })
        $fromOld = @($runtimes | Where-Object { [string]$_.ExecutablePath -like "$commandLine\*" })
        if ($fromCarried.Count -lt 1) { Fail "no runtime runs from $carried; the runtimes running: $(($runtimes | ForEach-Object { $_.ExecutablePath }) -join ', ')" }
        if ($fromOld.Count -ne 0) { Fail "a runtime still runs from the retired install: $(($fromOld | ForEach-Object { $_.ExecutablePath }) -join ', ')" }
        Say "the runtime answers, running from $carried, and none from $commandLine"

        # The logon registration names the carried file.
        $registration = Read-Document (Invoke-Program $carried @('runtime', 'autostart', 'status') $quiet).Said
        if (-not $registration -or -not $registration.PSObject.Properties['registered'] -or -not $registration.registered) { Fail "no logon registration after the move: $(if ($registration) { $registration | ConvertTo-Json -Compress -Depth 6 } else { 'no body' })" }
        if (-not $registration.PSObject.Properties['executable'] -or [string]$registration.executable -ine $carried) { Fail "the logon registration names $(if ($registration.PSObject.Properties['executable']) { $registration.executable } else { 'no executable' }), not $carried" }
        Say "the logon registration names $carried"

        # The move's record, complete.
        $recordFile = Join-Path $data 'update\move.json'
        if (-not (Test-Path -LiteralPath $recordFile)) { Fail "no move record at $recordFile" }
        $record = Get-Content -LiteralPath $recordFile -Raw | ConvertFrom-Json
        if ($record.state -cne 'done') { Fail "the move record at $recordFile says $($record.state) at $($record.step)" }
        Say "the move record at $recordFile says done"

        # The command-line install retired: its executable and notice files gone from its folder.
        foreach ($file in 'jason.exe', 'jason-LICENSE', 'jason-THIRD-PARTY-NOTICES') {
            if (Test-Path -LiteralPath (Join-Path $commandLine $file)) { Fail "the retired install still holds $file at $commandLine" }
        }
        $left = @(if (Test-Path -LiteralPath $commandLine) { Get-ChildItem -LiteralPath $commandLine -Force | ForEach-Object { $_.Name } })
        # Where the product renames a retired jason a running process holds (CommandLineInstalls.AsideFolder): jason-retired-<id> in
        # the temporary directory on the executable's volume, .jason-retired-<id> beside its folder otherwise.
        # Under every temporary base, each named, as the listing reads them.
        $bases = Get-ExtractBases
        $aside = @(@(foreach ($base in $bases.Keys) { Get-ChildItem -LiteralPath $bases[$base] -Directory -Force -Filter 'jason-retired-*' -ErrorAction SilentlyContinue | ForEach-Object { "$base\$($_.Name)" } }) + @(Get-ChildItem -LiteralPath (Split-Path -Parent $commandLine) -Directory -Force -Filter '.jason-retired-*' -ErrorAction SilentlyContinue | ForEach-Object { "Programs\$($_.Name)" }))
        Say "the command-line install is retired: no jason.exe or notice file at $commandLine; that folder $(if (Test-Path -LiteralPath $commandLine) { "is kept, holding: $(if ($left) { $left -join ', ' } else { 'nothing' })" } else { 'is gone' }); executables renamed aside: $(if ($aside) { $aside -join ', ' } else { 'none' })"

        # Whether this build says it is a test build: a release never does, CI's build always.
        $stderr = Join-Path $Out 'version-stderr.txt'
        $null = & $carried --version 2> $stderr
        $said = ((Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue) + '').Trim()
        $info = Read-Document $status.Said
        $testBuild = $info -and $info.PSObject.Properties['test_build'] -and $info.test_build -eq $true
        if ($build -ceq 'release') {
            if ($said) { Fail "a released jason --version wrote on standard error: $said" }
            if (-not $info) { Fail 'jason runtime status printed no document to read test_build from' }
            if ($info.PSObject.Properties['test_build']) { Fail "a released runtime's report carries test_build: $($info.test_build)" }
            Say 'released: jason --version writes nothing on standard error, and the runtime''s report carries no test_build'
        } else {
            if ($said -cne 'This jason is a test build, not a release.') { Fail "CI's test build does not say so on standard error: '$said'" }
            if (-not $testBuild) { Fail "CI's test build's runtime report does not carry test_build: true" }
            Say 'a test build, and it says so: jason --version on standard error, and test_build: true in the runtime''s report'
        }
        exit 0
    }

    'WayBack' {
        # Going back from 0.49 to the previous release (docs/release-and-update.md, "Going back from 0.49"), on a machine
        # moved by the previous release's own update: one campaign made under the previous release first; then, under the
        # moved 0.49 with its runtime running, the campaigns counted and the journal read whole; jason uninstall with no
        # --purge-data; the previous release installed by its pinned line from the real release page; its skills, UI,
        # runtime and logon start set up again. Asserted alone: the campaigns are as many, every journal entry is still
        # there by id with its kind and moment, no entry has appeared among the old ones, and the previous release's
        # jason says its version and its status is ready. No update and no rollback runs under it. Its own uninstall,
        # purging the data, ends the leg.
        # With -Again, a leg of its own: after going back, the previous release's own jason update apply moves the install
        # into Jason Desktop again, as the page says it does: the move done, the application at this version, the Path
        # naming the bundle folder once and the command-line folder no more, a new shell finding the carried jason, the
        # runtime answering and the campaigns as many; the moved install's own uninstall, purging the data, ends it, and the
        # person's own quick-start is read at moment 4 after it.
        if (-not $Previous -or -not $Feed -or -not $Version) { Fail '-Previous, -Feed and -Version are needed.' }
        # It runs on the account's own home and default data directory, which a moved installation and the previous
        # release share: a GitHub-hosted runner's alone, by the guard at the entry, never a person's machine.
        $quiet = @{ JASON_Update__CheckEnabled = 'false' }
        $jason = Join-Path $commandLine 'jason.exe'
        # The person's own quick-start, as the page's going back will say: moved out of the skills folder before step 3, then
        # either put back after step 3 (put-back) or kept out while the previous release is installed (keep-out). The one
        # switch is JASON_PROOF_OWN_SKILL_GOING_BACK in ci.yml. Its SKILL.md is read, by its bytes, at four moments: before
        # step 1, after step 3, after the put-back where the words have one, and after the previous release's own uninstall.
        $way = "$env:JASON_PROOF_OWN_SKILL_GOING_BACK"
        if ($way -cnotin @('put-back', 'keep-out')) { Fail "JASON_PROOF_OWN_SKILL_GOING_BACK is '$way', where it says put-back or keep-out" }
        $outside = $scratchOwnOut
        $ownOut = Join-Path $outside 'quick-start'

        $old = Invoke-Program 'powershell.exe' @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Previous 'install.ps1')) @{ JASON_INSTALL_FEED = $Previous }
        if ($old.Exit -ne 0) { Fail "the previous release's install.ps1 exited with $($old.Exit)" }
        $previousVersion = (("$(& $jason --version)".Trim()) -split '\+')[0]
        if (-not (Start-PreviousRuntime $jason $quiet)) { Fail 'the previous release could not start a runtime' }
        if ((Invoke-Program $jason @('runtime', 'autostart', 'enable') $quiet).Exit -ne 0) { Fail 'the previous release could not register its logon start' }
        $made = Invoke-Program $jason @('campaign', 'create', '--name', 'Made before going back') $quiet
        if ($made.Exit -ne 0) { Fail "the previous release could not make a campaign: $($made.Said)" }
        Say "the previous release's command-line Jason $previousVersion installed by its own script, a runtime running, its logon start registered and one campaign made"

        $server = Start-FeedServer $Feed 8099 (Join-Path $Out "feed-server-way-back$(if ($Again) { '-again' }).log")
        try {
            $update = Invoke-Program $jason @('update', 'apply', '--version', $Version, '--feed', 'http://127.0.0.1:8099/manifest.json', '--drain-seconds', '0') $quiet 900
        } finally {
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        }
        if ($update.Exit -ne 0) { Fail "the previous release's jason update apply --version $Version exited with $($update.Exit)" }
        $document = Read-Document $update.Said
        $move = if ($document) { $document.PSObject.Properties['move'] } else { $null }
        if (-not $move -or -not $move.Value -or $move.Value.state -cne 'done') { Fail "the update did not end with its move done: $($update.Said)" }
        Say "moved into Jason Desktop $Version by the previous release's own update"

        # Step 0: under the moved 0.49, its runtime running.
        if ((Invoke-Program $carried @('runtime', 'status') $quiet).Exit -ne 0) { Fail 'no runtime answers under the moved 0.49 before going back' }
        $campaignsBefore = @(Read-All $carried @('campaign', 'list', '--include-archived'))
        $journalBefore = @(Read-All $carried @('journal', 'list') | ForEach-Object { Read-Entry $_ })
        if ($campaignsBefore.Count -lt 1) { Fail 'the campaign made under the previous release is not listed under the moved 0.49' }
        if ($journalBefore.Count -lt 1) { Fail 'the journal is empty under the moved 0.49' }
        $newest = $journalBefore[0].Id
        Say "under the moved 0.49: $($campaignsBefore.Count) campaign(s), archived included; the journal holds $($journalBefore.Count) entries, the newest $newest"
        # Before step 1, as the page says: jason status names the person's own quick-start as a folder Jason did not deploy.
        $named = Invoke-Program $carried @('status') $quiet
        if (-not (Test-NamesOwnSkill $named.Said $ownSkill)) { Fail "before step 1, jason status under the moved 0.49 does not name the person's own quick-start as a folder Jason did not deploy, under a name Jason ships, left as it is: $($named.Said)" }
        Say "before step 1, jason status under the moved 0.49 names the person's own quick-start, $ownSkill, as a folder Jason did not deploy, under a name Jason ships, left as it is"
        $own1 = Read-OwnSkill $ownSkill
        if ($own1 -ceq 'absent') { Fail "the person's own quick-start is not at $ownSkill before going back" }
        Say "moment 1, before step 1: the person's own quick-start\SKILL.md at $ownSkill is $own1"

        # Step 1: the leg's own window quit, then jason uninstall with no --purge-data.
        foreach ($process in @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' })) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue }
        $removed = Invoke-Program $carried @('uninstall') $quiet 300
        if ($removed.Exit -ne 0) { Fail "jason uninstall with no --purge-data exited with $($removed.Exit): $($removed.Said)" }
        Wait-Until { -not (Test-Path -LiteralPath $app) } 120 "the application's folder going after jason uninstall ended"
        if (-not (Test-Path -LiteralPath $data)) { Fail "jason uninstall with no --purge-data took the data directory $data" }
        if (-not (Test-Path -LiteralPath $harnessRoot)) { Fail "jason uninstall with no --purge-data took the harness root $harnessRoot" }
        Say "jason uninstall with no --purge-data took the application off and kept the data directory and the harness root"

        # Step 2: the previous release by its pinned line, from the real release page, as the page says.
        $line = "& ([scriptblock]::Create((irm https://get.jasonharness.ai/releases/download/v$previousVersion/install.ps1))) -Version $previousVersion"
        $back = Invoke-Program 'powershell.exe' @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', $line) @{} 600
        if ($back.Exit -ne 0) { Fail "the pinned line of $previousVersion exited with $($back.Exit): $($back.Said)" }
        if (-not (Test-Path -LiteralPath $jason)) { Fail "the pinned line of $previousVersion placed no jason at $jason" }
        Say "$previousVersion installed by its pinned line from the real release page: $line"

        # Before step 3: the person's own quick-start moved out of the skills folder, as it stood after steps 1 and 2.
        $own = Read-OwnSkill $ownSkill
        if ($own -cne $own1) { Fail "steps 1 and 2 changed the person's own quick-start: it is $own, where it was $own1" }
        if (Test-Path -LiteralPath $outside) { Fail "$outside is there before the person's own quick-start is moved to it" }
        New-Item -ItemType Directory -Force -Path $outside | Out-Null
        Move-Item -LiteralPath $ownSkill -Destination $ownOut
        Say "before step 3: the person's own quick-start moved out of the skills folder, to $ownOut"

        # Step 3: its skills, its UI, its runtime and its logon start.
        foreach ($words in @(@('skills', 'install'), @('ui', 'install'), @('runtime', 'start'), @('runtime', 'autostart', 'enable'))) {
            if ($words[0] -ceq 'runtime' -and $words[1] -ceq 'start') {
                if (-not (Start-PreviousRuntime $jason $quiet)) { Fail "jason runtime start under $previousVersion did not bring up a runtime that answers" }
                continue
            }
            $ran = Invoke-Program $jason $words $quiet 600
            if ($ran.Exit -ne 0) { Fail "jason $($words -join ' ') under $previousVersion exited with $($ran.Exit): $($ran.Said)" }
        }
        Say "under ${previousVersion}: jason skills install, ui install, runtime start and runtime autostart enable each ended 0"
        $own2 = Read-OwnSkill $ownOut
        Say "moment 2, after step 3: the person's own SKILL.md, out at $ownOut, is $own2; at $ownSkill stands $(Read-OwnSkill $ownSkill)"
        if ($own2 -cne $own1) { Fail "the person's own quick-start, out of the skills folder, changed during step 3: $own2, where it was $own1" }
        if ($way -ceq 'put-back') {
            # The put-back: the person's own quick-start in its place again, over the one the previous release's skills wrote.
            if (Test-Path -LiteralPath $ownSkill) { Remove-Item -LiteralPath $ownSkill -Recurse -Force }
            Move-Item -LiteralPath $ownOut -Destination $ownSkill
            Remove-Item -LiteralPath $outside -Force
            $own3 = Read-OwnSkill $ownSkill
            Say "moment 3, after the put-back: the person's own SKILL.md at $ownSkill is $own3"
            if ($own3 -cne $own1) { Fail "the person's own quick-start put back is $own3, where it was $own1" }
        } else {
            Say "moment 3: none; the page's words keep the person's own quick-start out while $previousVersion is installed"
        }

        # (i) The campaigns, as many; the journal, held by id.
        $campaignsAfter = @(Read-All $jason @('campaign', 'list', '--include-archived'))
        if ($campaignsAfter.Count -ne $campaignsBefore.Count) { Fail "under $previousVersion $($campaignsAfter.Count) campaign(s) are listed, not the $($campaignsBefore.Count) the moved 0.49 listed" }
        $journalAfter = @(Read-All $jason @('journal', 'list') | ForEach-Object { Read-Entry $_ })
        $after = @{}
        foreach ($entry in $journalAfter) { $after[$entry.Id] = $entry }
        $known = @{}
        foreach ($entry in $journalBefore) { $known[$entry.Id] = $true }
        $lost = @($journalBefore | Where-Object { -not $after.ContainsKey($_.Id) } | ForEach-Object { $_.Id })
        $changed = @($journalBefore | Where-Object { $after.ContainsKey($_.Id) -and ($after[$_.Id].Kind -cne $_.Kind -or $after[$_.Id].Ts -cne $_.Ts) } | ForEach-Object { "$($_.Id) ($($_.Kind) $($_.Ts) now $($after[$_.Id].Kind) $($after[$_.Id].Ts))" })
        if ($lost.Count -gt 0) { Fail "going back lost $($lost.Count) journal entr(ies): $(($lost | Select-Object -First 10) -join ', ')" }
        if ($changed.Count -gt 0) { Fail "going back changed the kind or the moment of $($changed.Count) journal entr(ies): $(($changed | Select-Object -First 10) -join '; ')" }
        $at = -1
        for ($i = 0; $i -lt $journalAfter.Count; $i++) { if ($journalAfter[$i].Id -ceq $newest) { $at = $i; break } }
        $among = @(if ($at -ge 0) { for ($i = $at; $i -lt $journalAfter.Count; $i++) { if (-not $known.ContainsKey($journalAfter[$i].Id)) { "$($journalAfter[$i].Id) ($($journalAfter[$i].Kind))" } } })
        if ($among.Count -gt 0) { Fail "going back put $($among.Count) entr(ies) among the old ones, at or before $newest in the journal's order: $(($among | Select-Object -First 10) -join ', ')" }
        Say "under ${previousVersion}: $($campaignsAfter.Count) campaign(s), as before; every one of the $($journalBefore.Count) journal entries is there by id with its kind and its moment, and none has appeared at or before $newest; $($at) newer entr(ies) are listed above it"

        # (ii) Its version and its status.
        $said = (("$(& $jason --version)".Trim()) -split '\+')[0]
        if ($said -cne $previousVersion) { Fail "jason --version says '$said', not $previousVersion" }
        $status = Read-Document (Invoke-Program $jason @('status') $quiet).Said
        if (-not $status -or -not $status.PSObject.Properties['ready'] -or $status.ready -ne $true) { Fail "jason status under $previousVersion is not ready: $(if ($status) { $status | ConvertTo-Json -Compress -Depth 6 } else { 'no body' })" }
        Say "under ${previousVersion}: jason --version says $said, and jason status says ready: true"

        if ($Again) {
            # Moving again: the previous release's own update apply, pinned to this version, from the feed served here, as the
            # page says it moves the install into Jason Desktop again.
            $server = Start-FeedServer $Feed 8099 (Join-Path $Out 'feed-server-way-back-moved-again.log')
            try {
                $moved = Invoke-Program $jason @('update', 'apply', '--version', $Version, '--feed', 'http://127.0.0.1:8099/manifest.json', '--drain-seconds', '0') $quiet 900
            } finally {
                Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
            }
            Set-Content -LiteralPath (Join-Path $Out 'way-back-again.json') -Value $moved.Said
            if ($moved.Exit -ne 0) { Fail "after going back, $previousVersion's jason update apply --version $Version exited with $($moved.Exit)" }
            $document = Read-Document $moved.Said
            $move = if ($document) { $document.PSObject.Properties['move'] } else { $null }
            if (-not $document) { Fail "after going back, the update printed no document: $($moved.Said)" }
            if ($document.step -cne 'complete' -or $document.to -cne $Version) { Fail "after going back, the update ended at '$($document.step)' to '$($document.to)', not complete to $Version" }
            if (-not $move -or -not $move.Value -or $move.Value.state -cne 'done') { Fail "after going back, the update did not end with its move done: $($moved.Said)" }
            if ([string]$move.Value.application -ine $app) { Fail "after going back, the move put the application at $($move.Value.application), not $app" }
            if ((Read-Version $carried) -ne $Version) { Fail "after moving again the application is $(Read-Version $carried), not $Version" }
            if (Test-Path -LiteralPath $jason) { Fail "after moving again the command-line install's jason is still at $jason" }
            if ((Count-Entry $bundle) -ne 1) { Fail "after moving again the user Path holds $bundle $(Count-Entry $bundle) time(s), not once: $((Get-UserPath).Raw)" }
            if ((Count-Entry $commandLine) -ne 0) { Fail "after moving again the user Path still names the retired $commandLine" }
            $found = @(Find-InNewShell 'jason')
            if ($found.Count -lt 2 -or $found[0] -ine $carried -or (($found[1] -split '\+')[0]) -ne $Version) { Fail "after moving again a new shell finds '$($found -join ' ')', not $carried saying $Version" }
            if ((Invoke-Program $carried @('runtime', 'status') $quiet).Exit -ne 0) { Fail 'no runtime answers after moving again' }
            $campaignsAgain = @(Read-All $carried @('campaign', 'list', '--include-archived'))
            if ($campaignsAgain.Count -ne $campaignsBefore.Count) { Fail "after moving again $($campaignsAgain.Count) campaign(s) are listed, not the $($campaignsBefore.Count) listed before going back" }
            Say "after going back, $previousVersion's own jason update apply --version $Version moved the install into Jason Desktop again: $app at $Version, the Path naming $bundle once and $commandLine no more, a new shell finding $carried, the runtime answering, $($campaignsAgain.Count) campaign(s) as before"

            # The leg's own end: the moved install's own uninstall, the application quit first.
            foreach ($process in @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' })) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue }
            $ran = Invoke-Program $carried @('uninstall', '--purge-data', '--yes') $quiet 300
            if ($ran.Exit -ne 0) { Fail "jason uninstall --purge-data --yes after moving again exited with $($ran.Exit): $($ran.Said)" }
            Wait-Until { -not (Test-Path -LiteralPath $app) } 120 "the application's folder going after jason uninstall ended"
            $remover = "the moved install's own uninstall"
            Say "$Version's own uninstall --purge-data --yes, after moving again, ended 0 and the application's folder went"
        } else {
            # The leg's own end: the runtime stopped, the logon start taken off, and the previous release's own uninstall.
            foreach ($words in @(@('runtime', 'stop'), @('runtime', 'autostart', 'disable'), @('uninstall', '--purge-data', '--yes'))) {
                $ran = Invoke-Program $jason $words $quiet 300
                if ($ran.Exit -ne 0) { Fail "jason $($words -join ' ') under $previousVersion exited with $($ran.Exit): $($ran.Said)" }
            }
            $remover = "$previousVersion's own uninstall"
            Say "$previousVersion's own runtime stop, autostart disable and uninstall --purge-data --yes each ended 0"

            # What going back leaves: the previous release's own skills record in ~\.agents\skills, which its uninstall keeps
            # where the person's own quick-start stands in a place the record names. Read as that release's own - every pack
            # at its version - and only then recorded by its listing line, exactly, for this leg's compare to excuse and the
            # next clean begin to take (see $previousOwn); a record that is not its own fails the leg.
            $leftRecord = $previousOwn['agents skills\.jason-skills.json']
            if (Test-Path -LiteralPath $leftRecord -PathType Leaf) {
                $packs = @(try { @((Get-Content -LiteralPath $leftRecord -Raw | ConvertFrom-Json).packs) } catch { })
                $refs = @($packs | ForEach-Object { if ($_ -and $_.PSObject.Properties['ref']) { [string]$_.ref } else { '' } })
                if ($refs.Count -eq 0 -or @($refs | Where-Object { $_ -cne "v$previousVersion" -and $_ -cne $previousVersion }).Count -gt 0) { Fail "after $remover, $leftRecord is left, and it is not $previousVersion's own skills record: its packs' refs are '$($refs -join "', '")'" }
                $named = @($packs | ForEach-Object { if ($_.PSObject.Properties['files']) { @($_.files) } } | ForEach-Object { if ($_ -and $_.PSObject.Properties['path']) { [string]$_.path } })
                $openLeg = if (Test-Path -LiteralPath $legOpen) { "$(Get-Content -LiteralPath $legOpen -Raw)".Trim() } else { '' }
                if (-not $openLeg) { Fail "no leg is open to record $leftRecord as the previous release's own" }
                Add-Content -LiteralPath $previousOwnRows -Value "$openLeg`tagents skills\.jason-skills.json`t$((Get-Item -LiteralPath $leftRecord -Force).Length)"
                Say "going back leaves $leftRecord, $previousVersion's own skills record ($($packs.Count) pack(s) at $previousVersion, naming $($named.Count) file(s)$(if ($named -ccontains 'quick-start/SKILL.md') { ', quick-start/SKILL.md among them' })): $remover keeps it where the person's own quick-start stands in a place it names; this leg's compare excuses exactly that line as the previous release's own, and the next clean begin takes it"
            }
        }
        if ($way -ceq 'put-back') {
            $own4 = Read-OwnSkill $ownSkill
            Say "moment 4, after ${remover}: the person's own SKILL.md at $ownSkill is $own4"
            if ($own4 -cne $own1) { Fail "$remover took or changed the person's own quick-start: it is $own4, where it was $own1" }
        } else {
            $own4 = Read-OwnSkill $ownOut
            $place4 = Read-OwnSkill $ownSkill
            Say "moment 4, after ${remover}: the person's own SKILL.md, out at $ownOut, is $own4; at $ownSkill stands $place4"
            if ($own4 -cne $own1) { Fail "the person's own quick-start, out of the skills folder, changed while $previousVersion was installed: $own4, where it was $own1" }
            if (Test-Path -LiteralPath $ownSkill) { Fail "after $remover a quick-start stands at $ownSkill ($place4), where the person's own goes back" }
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ownSkill) | Out-Null
            Move-Item -LiteralPath $ownOut -Destination $ownSkill
            Remove-Item -LiteralPath $outside -Force
            $back = Read-OwnSkill $ownSkill
            if ($back -cne $own1) { Fail "the person's own quick-start put back is $back, where it was $own1" }
            Say "the person's own quick-start put back at $ownSkill, $back"
        }
        exit 0
    }

    'InstallOverCommandLine' {
        # The previous release's command-line Jason, installed by its own script, a runtime running and its logon start
        # registered, one campaign made; then this build's unpinned line (the stand-in copy of install.ps1) run over it as an
        # agent runs it, while that runtime runs. The setup's own last step, its jason install with no flags (customInstall,
        # desktop/installer/uninstall.nsh), holds the command-line install until the old runtime's stop has answered, then
        # retires it; nsExec discards what it prints, so its report is kept nowhere. install.ps1 then runs the line's own jason
        # install --roles, which finds nothing left to stop or retire, and starts a runtime again from the carried jason.
        # Asserted: of the line's own report, what is true of it - no runtime running and nothing retired - and install.ps1's
        # sentence that it starts a runtime again; the old runtime (by its process and its start) and the old executable both
        # gone, with no order claimed: that order is held by MachineSetup's own rows; the command-line install's
        # files gone and no runtime running from it; the runtime answering from the carried jason and the logon registration
        # naming it; the Path naming the bundle folder once and the command-line folder no more; a new shell finding this
        # version; and the campaigns the same, by id. The uninstall from a new terminal is the next step.
        if (-not $Previous -or -not $Feed -or -not $Version -or -not $Script) { Fail '-Previous, -Feed, -Version and -Script are needed.' }
        # It writes the account's own data directory, Path, logon registration and Installed apps: a GitHub-hosted runner's
        # alone, by the guard at the entry, never a person's machine.
        $quiet = @{ JASON_Update__CheckEnabled = 'false' }
        $jason = Join-Path $commandLine 'jason.exe'
        $old = Invoke-Program 'powershell.exe' @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Previous 'install.ps1')) @{ JASON_INSTALL_FEED = $Previous }
        if ($old.Exit -ne 0) { Fail "the previous release's install.ps1 exited with $($old.Exit)" }
        if (-not (Test-Path -LiteralPath $jason -PathType Leaf)) { Fail "the previous release's install.ps1 placed no jason at $jason" }
        Assert-CommandLineFiles 'the previous release''s install.ps1'
        $previousVersion = (("$(& $jason --version)".Trim()) -split '\+')[0]
        if ($previousVersion -ceq $Version) { Fail "the previous release's jason says $previousVersion, the version this build is" }
        if (-not (Start-PreviousRuntime $jason $quiet)) { Fail 'the previous release could not start a runtime' }
        if ((Invoke-Program $jason @('runtime', 'autostart', 'enable') $quiet).Exit -ne 0) { Fail 'the previous release could not register its logon start' }
        $made = Invoke-Program $jason @('campaign', 'create', '--name', 'Made before the line ran over it') $quiet
        if ($made.Exit -ne 0) { Fail "the previous release could not make a campaign: $($made.Said)" }
        if ((Count-Entry $commandLine) -ne 1) { Fail "the previous release's install did not put $commandLine on the user Path once" }
        $idsBefore = @(Read-All $jason @('campaign', 'list', '--include-archived') | ForEach-Object { $_.GetProperty('id').GetString() } | Sort-Object)
        if ($idsBefore.Count -lt 1) { Fail 'the campaign made under the previous release is not listed' }
        $oldRuntimes = @(Get-RuntimeProcesses | Where-Object { [string]$_.ExecutablePath -like "$commandLine\*" })
        if ($oldRuntimes.Count -lt 1) { Fail "no runtime runs from $commandLine before the line runs over it" }
        Say "the previous release's command-line Jason $previousVersion installed by its own script at $jason, a runtime running from it, its logon start registered, $($idsBefore.Count) campaign(s)"

        # This build's unpinned line over it, as an agent runs it, while that runtime runs.
        $over = Invoke-InstallScript $Script
        if ($over.Exit -ne 0) { Fail "install.ps1 over the command-line install exited with $($over.Exit)" }
        $report = Read-InstallReport $over.Said
        if (-not $report) { Fail 'install.ps1 showed no report of jason install that could be read' }
        Say "install.ps1 ran unpinned over the command-line install while its runtime ran, and ended 0"

        # The line's own report, run after the setup's own jason install had already stopped the old runtime and retired the
        # install: no runtime running and nothing retired, and the script's sentence that it starts a runtime again.
        if ([string]$report.runtime -cne 'not_running') { Fail "the line's own jason install --roles reports the runtime '$($report.runtime)', where the setup's own jason install had already stopped it: not_running" }
        $retired = $report.PSObject.Properties['retired']
        if ($retired -and $retired.Value) { Fail "the line's own jason install --roles reports a retirement, where the setup's own jason install had already retired the command-line install: $($retired.Value | ConvertTo-Json -Compress -Depth 6)" }
        $startedAgain = "A runtime answered before the install and none answers now, so one is started again from $carried."
        if (-not $over.Said.Contains($startedAgain)) { Fail "install.ps1 did not say it starts a runtime again: '$startedAgain'" }
        Say "the line's own jason install --roles reports no runtime running and nothing retired, and install.ps1 said: $startedAgain"

        # The old runtime and the old executable both gone, with no order claimed.
        $oldAlive = @($oldRuntimes | Where-Object { Test-SameProcess $_ })
        if ($oldAlive.Count -ne 0) { Fail "the previous release's runtime still runs: process $(($oldAlive | ForEach-Object { $_.ProcessId }) -join ', ')" }
        if (Test-Path -LiteralPath $jason) { Fail "the command-line install's jason is still at $jason" }
        foreach ($file in 'jason.exe', 'jason-LICENSE', 'jason-THIRD-PARTY-NOTICES') {
            if (Test-Path -LiteralPath (Join-Path $commandLine $file)) { Fail "the retired install still holds $file at $commandLine" }
        }
        Say "the previous release's runtime (process $(($oldRuntimes | ForEach-Object { $_.ProcessId }) -join ', ')) and $jason are both gone, with its notice files; that the install is retired only once the stop has answered is held by MachineSetup's own rows, not by this leg: the setup's own jason install made the stop, and its report is kept nowhere"

        # The runtime again, from the carried jason, and none from the retired one; the logon registration naming it.
        if ((Invoke-Program $carried @('runtime', 'status') $quiet).Exit -ne 0) { Fail 'no runtime answers after the line ran over the command-line install' }
        $runtimes = @(Get-RuntimeProcesses)
        if (@($runtimes | Where-Object { [string]$_.ExecutablePath -ieq $carried }).Count -lt 1) { Fail "no runtime runs from $carried; the runtimes running: $(($runtimes | ForEach-Object { $_.ExecutablePath }) -join ', ')" }
        if (@($runtimes | Where-Object { [string]$_.ExecutablePath -like "$commandLine\*" }).Count -ne 0) { Fail "a runtime still runs from the retired install at $commandLine" }
        $registration = Read-Document (Invoke-Program $carried @('runtime', 'autostart', 'status') $quiet).Said
        if (-not $registration -or -not $registration.PSObject.Properties['registered'] -or -not $registration.registered) { Fail "no logon registration after the line ran over the command-line install: $(if ($registration) { $registration | ConvertTo-Json -Compress -Depth 6 } else { 'no body' })" }
        if (-not $registration.PSObject.Properties['executable'] -or [string]$registration.executable -ine $carried) { Fail "the logon registration names $(if ($registration.PSObject.Properties['executable']) { $registration.executable } else { 'no executable' }), not $carried" }
        Say "a runtime answers from $carried and none from $commandLine; the logon registration names $carried"

        # The terminal command.
        if ((Count-Entry $bundle) -ne 1) { Fail "the user Path holds $bundle $(Count-Entry $bundle) time(s), not once: $((Get-UserPath).Raw)" }
        if ((Count-Entry $commandLine) -ne 0) { Fail "the user Path still names the retired $commandLine" }
        $found = @(Find-InNewShell 'jason')
        if ($found.Count -lt 2) { Fail "a new shell looking for jason said: $(if ($found) { $found -join ' / ' } else { 'nothing' })" }
        if ($found[0] -ine $carried -or (($found[1] -split '\+')[0]) -ne $Version) { Fail "a new shell finds '$($found -join ' ')', not $carried saying $Version" }
        Say "the user Path names $bundle once and $commandLine no more; a new shell finds $carried, saying $Version"

        # The data kept: the same campaigns, by id.
        $idsAfter = @(Read-All $carried @('campaign', 'list', '--include-archived') | ForEach-Object { $_.GetProperty('id').GetString() } | Sort-Object)
        if (($idsAfter -join ',') -cne ($idsBefore -join ',')) { Fail "the campaigns are not the same after the line ran over the command-line install: before $($idsBefore -join ', '); after $($idsAfter -join ', ')" }
        Say "the data kept: the same $($idsAfter.Count) campaign(s), by id"
    }

    'OverBothPrevious' {
        # The previous release's Jason Desktop, installed by its own setup /S, and its command-line Jason, installed by its own
        # install.ps1, side by side: the command-line folder on the Path, the Desktop placing nothing there; then this build's
        # setup run silently as an update (/S --updated), whose own last step, jason install with no flags, retires the
        # command-line one, and nothing else: without --force-run a silent one-click setup starts no application
        # (electron-builder's installSection.nsh, ONE_CLICK: doStartApp only where not Silent or isForceRun), so what stands at
        # the setup's exit is the setup's own doing.
        # The runtime is started by the command-line jason and left running: the previous release's updater stops only a
        # runtime it judges its own, one started from the jason it carries, so a real update hands the setup a machine where
        # the command-line install's runtime still runs from its folder, and nothing here stops it first.
        # Asserted at the setup's exit: one installation, at this version; the command-line install's files gone; none of its
        # runtimes alive (each known by its process id and start time) and none running from its folder; the note that a start
        # is owed standing (MachineSetup writes it before the stop's drain), since nothing has started a runtime yet; no move
        # record written (a move writes one, and this is no move) and jason status's move check reading absent; the Path
        # naming the bundle folder once and the command-line folder no more, and no command file in WindowsApps; a new shell
        # finding the carried jason. Then the leg starts the application, as --force-run would, and holds: a runtime answering
        # from the carried jason and none from the command-line folder, the note gone, the logon registration naming the
        # carried file, and the campaigns the same, by id; jason status is printed whole. The uninstall Installed apps runs is
        # the next step.
        # Not proved here: the start --force-run makes. The leg starts the application itself; the install over the previous
        # release's Jason Desktop is the leg that runs the setup with --force-run.
        if (-not $Previous -or -not $Feed -or -not $Version) { Fail '-Previous, -Feed and -Version are needed.' }
        # It writes the account's own data directory, Path and Installed apps: a GitHub-hosted runner's alone, by the guard
        # at the entry, never a person's machine.
        $quiet = @{ JASON_Update__CheckEnabled = 'false' }
        $jason = Join-Path $commandLine 'jason.exe'
        $previousSetup = Join-Path $Previous 'jason-desktop-windows-x64-setup.exe'
        $signature = Get-AuthenticodeSignature -LiteralPath $previousSetup
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo('SimpleName', $false) -cne 'ReplyApp Inc.') { Fail "the previous release's setup is not signed by ReplyApp Inc.: $($signature.Status)" }
        $placed = Wait-Program $previousSetup @('/S')
        if ($placed -ne 0) { Fail "the previous release's setup exited with $placed" }
        $desktopVersion = Read-Version $carried
        if (-not $desktopVersion -or $desktopVersion -ceq $Version) { Fail "the previous release's Jason Desktop is '$desktopVersion', where it must be a version before $Version" }
        $cli = Invoke-Program 'powershell.exe' @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Previous 'install.ps1')) @{ JASON_INSTALL_FEED = $Previous }
        if ($cli.Exit -ne 0) { Fail "the previous release's install.ps1 beside its Jason Desktop exited with $($cli.Exit)" }
        if (-not (Test-Path -LiteralPath $jason -PathType Leaf)) { Fail "the previous release's install.ps1 placed no jason at $jason" }
        Assert-CommandLineFiles 'the previous release''s install.ps1 beside its Jason Desktop'
        $cliVersion = (("$(& $jason --version)".Trim()) -split '\+')[0]
        if (-not $cliVersion -or $cliVersion -ceq $Version) { Fail "the previous release's command-line jason says '$cliVersion', where it must be a version before $Version" }

        # Side by side as that release leaves them: the command-line folder's own entry on the Path, and nothing of the
        # Desktop's there. Its setup runs no step of its own at install (v0.48.5's desktop/installer holds only uninstall.nsh,
        # with no customInstall), and its application places its command, jason.cmd and a Git Bash wrapper in WindowsApps,
        # only on the person's word and never as a Path entry (v0.48.5's desktop/src/path-command.ts). Held, so that what
        # this build's setup puts there after is its own doing.
        if ((Count-Entry $commandLine) -ne 1) { Fail "the previous release's install did not put $commandLine on the user Path once" }
        $commandFiles = @(Get-WindowsAppsCommandFiles)
        if ((Count-Entry $bundle) -ne 0) { Fail "the previous release's Jason Desktop is named on the user Path ($bundle), which its setup never writes" }
        if ($commandFiles.Count -ne 0) { Fail "the previous release's Jason Desktop has command files in WindowsApps ($($commandFiles -join ', ')), which its application places only on the person's word" }
        $first = @(Find-InNewShell 'jason')
        Say "side by side, as the previous release leaves them: Jason Desktop $desktopVersion at $app, named nowhere on the Path and with no command file in WindowsApps, and the command-line Jason $cliVersion at $jason, its folder on the Path once; a new shell finds $(if ($first) { $first -join ' saying ' } else { 'nothing' })"

        # The runtime, started by the command-line jason as a person using it starts it, its logon start registered, and one
        # campaign made.
        if (-not (Start-PreviousRuntime $jason $quiet)) { Fail 'the previous release''s command-line jason could not start a runtime' }
        if ((Invoke-Program $jason @('runtime', 'autostart', 'enable') $quiet).Exit -ne 0) { Fail 'the previous release''s command-line jason could not register its logon start' }
        $made = Invoke-Program $jason @('campaign', 'create', '--name', 'Made beside both previous installs') $quiet
        if ($made.Exit -ne 0) { Fail "the previous release could not make a campaign: $($made.Said)" }
        $idsBefore = @(Read-All $jason @('campaign', 'list', '--include-archived') | ForEach-Object { $_.GetProperty('id').GetString() } | Sort-Object)
        if ($idsBefore.Count -lt 1) { Fail 'the campaign made under the previous release is not listed' }
        $runtimesBefore = @(Get-RuntimeProcesses)
        # Judged after by process as well as by path, since a retired image renamed aside would escape a filter on the folder:
        # each by its id and its start time, since Windows gives a freed id to a later process.
        $oldRuntimes = @($runtimesBefore | Where-Object { [string]$_.ExecutablePath -like "$commandLine\*" } | ForEach-Object { [pscustomobject]@{ ProcessId = [int]$_.ProcessId; CreationDate = $_.CreationDate } })
        if ($oldRuntimes.Count -lt 1) { Fail "no runtime runs from $commandLine before the setup; the runtimes running: $(if ($runtimesBefore) { ($runtimesBefore | ForEach-Object { $_.ExecutablePath }) -join ', ' } else { 'none' })" }
        # A fact, not an assertion: what runtime status answers before the setup, and the runtimes running, by their files.
        $before = Invoke-Program $jason @('runtime', 'status') $quiet
        Say "before the setup, runtime status ended $($before.Exit) and said: $("$($before.Said)".Trim()); runtimes running from: $(($runtimesBefore | ForEach-Object { $_.ExecutablePath }) -join ', '); $($idsBefore.Count) campaign(s)"

        # This build's setup run silently as an update, /S --updated and no --force-run, with the command-line install's
        # runtime still running: the previous release's updater stops none it does not judge its own, and nothing here stops
        # it either. Without --force-run the setup starts no application, so all that stands at its exit is its own doing.
        $installer = Start-Process -FilePath (Join-Path $Feed 'jason-desktop-windows-x64-setup.exe') -ArgumentList '/S', '--updated' -PassThru
        $null = $installer.Handle
        $new = Wait-Started $installer "this build's setup /S --updated, as an update starts it, without --force-run"
        if ($new -ne 0) { Fail "this build's setup run as an update runs it exited with $new" }
        if ((Read-Version $carried) -ne $Version) { Fail "after the update the application is $(Read-Version $carried), not $Version" }
        $openWindows = @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' })
        if ($openWindows.Count -ne 0) { Fail "the setup run without --force-run started the application: process $(($openWindows | ForEach-Object { $_.ProcessId }) -join ', ')" }
        Say "this build's setup, run silently as an update without --force-run (/S --updated), put $Version in place and started no application"

        # One installation: one Installed apps entry, the application at this version, the command-line install retired by the
        # setup's own run.
        $entries = @(Get-JasonUninstallEntries)
        if ($entries.Count -ne 1) { Fail "Installed apps lists $($entries.Count) Jason entries, not one" }
        foreach ($file in 'jason.exe', 'jason-LICENSE', 'jason-THIRD-PARTY-NOTICES') {
            if (Test-Path -LiteralPath (Join-Path $commandLine $file)) { Fail "the command-line install still holds $file at ${commandLine} at the setup's exit: the setup's own jason install did not retire it" }
        }
        # Its runtime gone: none of the processes it ran as is alive, each known by its id and its start time, since Windows
        # gives a freed id to a later process; and none runs from its folder.
        $oldAlive = @($oldRuntimes | Where-Object { Test-SameProcess $_ })
        if ($oldAlive.Count -ne 0) { Fail "the command-line install's runtime is still alive at the setup's exit, process $(($oldAlive | ForEach-Object { $_.ProcessId }) -join ', '), wherever its image now stands" }
        $runtimesAfter = @(Get-RuntimeProcesses)
        if (@($runtimesAfter | Where-Object { [string]$_.ExecutablePath -like "$commandLine\*" }).Count -ne 0) { Fail "a runtime still runs from the retired install at $commandLine at the setup's exit; the runtimes running: $(($runtimesAfter | ForEach-Object { $_.ExecutablePath }) -join ', ')" }
        # The stop owed a start, and nothing has started one: the note that says so stands (JasonPaths.StartOwedFile).
        $startOwed = Join-Path $data 'run\start-owed.json'
        if (-not (Test-Path -LiteralPath $startOwed -PathType Leaf)) { Fail "no note that a start is owed stands at $startOwed at the setup's exit, where its run stopped the command-line install's runtime and nothing has started one" }
        Say "at the setup's exit: the command-line install's files gone from $commandLine, its runtime ended and none running from it; the note that a start is owed stands at $startOwed"

        # The move's record is written by a move, which jason update apply makes; the setup's own jason install retires with
        # none, so none stands, and jason status's move check, which status always lists, reads absent.
        $recordFile = Join-Path $data 'update\move.json'
        if (Test-Path -LiteralPath $recordFile) { Fail "a move record stands at $recordFile after the setup's own jason install, which writes none: $(Get-Content -LiteralPath $recordFile -Raw)" }
        $statusRun = Invoke-Program $carried @('status') $quiet
        # jason status ends 0 or 1, ready or not, and never 3: any other end is not an answer to read.
        if ($statusRun.Exit -ne 0 -and $statusRun.Exit -ne 1) { Fail "jason status after the update exited with $($statusRun.Exit): $($statusRun.Said)" }
        $status = Read-Document $statusRun.Said
        if (-not $status -or -not $status.PSObject.Properties['checks']) { Fail "jason status after the update printed no document with its checks: $($statusRun.Said)" }
        $checks = @($status.checks)
        $moveCheck = @($checks | Where-Object { $_.name -ceq 'move' })
        if ($moveCheck.Count -ne 1) { Fail "jason status after the update lists $($moveCheck.Count) move check(s), not one: $($statusRun.Said)" }
        $moveSaid = "$($moveCheck[0].state): $($moveCheck[0].fact)"
        if ($moveCheck[0].state -cne 'absent') { Fail "no move record was written, and jason status's move check does not read absent: $moveSaid" }
        Say "one installation: one Installed apps entry, $app at $Version; no move record was written, as the setup's own jason install writes none; jason status's move check reads $moveSaid"

        # The terminal command.
        if ((Count-Entry $bundle) -ne 1) { Fail "the user Path holds $bundle $(Count-Entry $bundle) time(s), not once: $((Get-UserPath).Raw)" }
        if ((Count-Entry $commandLine) -ne 0) { Fail "the user Path still names the retired $commandLine" }
        $leftFiles = @(Get-WindowsAppsCommandFiles)
        if ($leftFiles.Count -ne 0) { Fail "jason command files are left in WindowsApps: $($leftFiles -join ', ')" }
        $found = @(Find-InNewShell 'jason')
        if ($found.Count -lt 2) { Fail "a new shell looking for jason said: $(if ($found) { $found -join ' / ' } else { 'nothing' })" }
        if ($found[0] -ine $carried -or (($found[1] -split '\+')[0]) -ne $Version) { Fail "a new shell finds '$($found -join ' ')', not $carried saying $Version" }
        Say "the user Path names $bundle once and $commandLine no more, no command file in WindowsApps, and a new shell finds $carried, saying $Version"

        # The application, started here as --force-run would start it, pays the start the setup's run owed: a runtime answering
        # from the carried jason and none from the command-line folder, and the note gone.
        $null = Start-Process -FilePath (Join-Path $app 'Jason.exe') -PassThru
        Wait-Until { (Invoke-Program $carried @('runtime', 'status') $quiet).Exit -eq 0 } 120 "a runtime answering through $carried, the start the setup owed paid by the application's start"
        $runtimesStarted = @(Get-RuntimeProcesses)
        if (@($runtimesStarted | Where-Object { [string]$_.ExecutablePath -ieq $carried }).Count -lt 1) { Fail "a runtime answers, but none runs from $carried; the runtimes running: $(if ($runtimesStarted) { ($runtimesStarted | ForEach-Object { $_.ExecutablePath }) -join ', ' } else { 'none' })" }
        $oldAlive = @($oldRuntimes | Where-Object { Test-SameProcess $_ })
        if ($oldAlive.Count -ne 0) { Fail "the command-line install's runtime is alive after the application's start, process $(($oldAlive | ForEach-Object { $_.ProcessId }) -join ', ')" }
        if (@($runtimesStarted | Where-Object { [string]$_.ExecutablePath -like "$commandLine\*" }).Count -ne 0) { Fail "a runtime runs from the retired install at $commandLine after the application's start" }
        Wait-Until { -not (Test-Path -LiteralPath $startOwed) } 60 "the note that a start is owed, $startOwed, going once the application's start had started a runtime"
        Say "the application started by the leg: a runtime answers from $carried and none runs from $commandLine; the note that a start is owed at $startOwed is gone"

        # The logon registration, which the command-line jason registered, names the carried file.
        $registration = Read-Document (Invoke-Program $carried @('runtime', 'autostart', 'status') $quiet).Said
        if (-not $registration -or -not $registration.PSObject.Properties['registered'] -or -not $registration.registered) { Fail "no logon registration after the update over both previous installs: $(if ($registration) { $registration | ConvertTo-Json -Compress -Depth 6 } else { 'no body' })" }
        if (-not $registration.PSObject.Properties['executable'] -or [string]$registration.executable -ine $carried) { Fail "the logon registration names $(if ($registration.PSObject.Properties['executable']) { $registration.executable } else { 'no executable' }), not $carried" }
        Say "the logon registration names $carried"

        # A fact, not an assertion: jason status whole, once the application's start has run.
        $statusAfter = Invoke-Program $carried @('status') $quiet
        Say "jason status after the update and the application's start ended $($statusAfter.Exit) and said: $("$($statusAfter.Said)".Trim())"

        # The data kept: the same campaigns, by id.
        $idsAfter = @(Read-All $carried @('campaign', 'list', '--include-archived') | ForEach-Object { $_.GetProperty('id').GetString() } | Sort-Object)
        if (($idsAfter -join ',') -cne ($idsBefore -join ',')) { Fail "the campaigns are not the same after the update over both previous installs: before $($idsBefore -join ', '); after $($idsAfter -join ', ')" }
        Say "the data kept: the same $($idsAfter.Count) campaign(s), by id"

        # The leg started the application; a person closes it before an uninstall.
        Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Wait-Until { @(Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' }).Count -eq 0 } 30 'the window closing'
    }

    'ModulePath' {
        # A reading: what Windows PowerShell 5.1 rebuilds from an emptied module path, as electron-updater's publisher check
        # starts it ('set "PSModulePath=" & powershell.exe'); whether the account's own modules come first.
        $said = (& cmd.exe /d /c 'set "PSModulePath=" & powershell.exe -NoProfile -NonInteractive -Command "$env:PSModulePath"') -join ''
        $entries = @($said -split ';' | Where-Object { $_ })
        $own = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'WindowsPowerShell\Modules'
        Say "Windows PowerShell rebuilds an emptied module path as: $($entries -join ' ; ')"
        Say "the account's own modules ($own) come $(if ($entries.Count -gt 0 -and $entries[0].TrimEnd('\') -ieq $own) { 'first' } elseif ($entries -icontains $own) { 'among them, not first' } else { 'nowhere' })"
        exit 0
    }
}

# Every assertion above throws; reached here, the step succeeded, whatever the last program it ran exited with.
exit 0
