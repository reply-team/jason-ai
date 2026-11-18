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
                    and, with -Clean, holds the machine to the listing taken before anything was installed, a machine an
                    earlier failed leg left being taken off by the product's own uninstalls and said so. Held lists
                    every failed leg, every leg not run and every divergence, and fails the job at its end

-Signer names the signer the script under test holds the setup to: the release's on the signed packages; on CI's test
build, which a release never signs, the runner's own test certificate, which the job's copy of install.ps1 names.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Listing', 'Install', 'UninstallFromTerminal', 'InstallOverPrevious', 'InstallOverOpenWindow', 'SilentUpdateLegs', 'UninstallWaiterShown', 'UpdateApply', 'UninstallFromInstalledApps', 'FromPrevious', 'Compare', 'Held', 'ModulePath', 'Begin', 'End')] [string] $Step,
    [string] $Leg,
    [string[]] $Needs = @(),
    [switch] $Clean,
    [string] $Feed,
    [string] $Version,
    [string] $Script,
    [string] $Previous,
    [string] $Next,
    [string] $Out = (Join-Path $env:RUNNER_TEMP 'installation'),
    [string] $Name,
    [string] $Against,
    [switch] $Pinned
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

# A program run with its output kept and shown, and its exit code: Windows PowerShell's lines on stderr included.
function Invoke-Program([string] $file, [string[]] $arguments, [hashtable] $environment = @{}, [int] $seconds = 600) {
    $saved = @{}
    foreach ($name in $environment.Keys) { $saved[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name, $environment[$name]) }
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
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
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

function Get-OwnProcesses([string] $folder) {
    @(Get-CimInstance -ClassName Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($folder.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) })
}

function Get-JasonUninstallEntries {
    @(Get-ChildItem -LiteralPath $uninstallKeys -ErrorAction SilentlyContinue | ForEach-Object { Get-ItemProperty -LiteralPath $_.PSPath } | Where-Object { ($_.PSObject.Properties.Name -contains 'DisplayName') -and $_.DisplayName -like 'Jason*' })
}

# --- the listing --------------------------------------------------------------------------------------------------

# Every place an installation of Jason can touch on this account, one line each, sorted: what a person reads to see
# what an uninstall left. A folder is listed by its files, relative, with their sizes; a registry value by its data.
function Write-Listing([string] $file, [switch] $Quiet) {
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
    foreach ($name in @('jason', 'jason.cmd', 'jason-desktop', 'jason-desktop.cmd', 'jason.exe')) {
        $lines.Add("WindowsApps\$name`t$(if (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\$name")) { 'present' } else { 'absent' })")
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
}

# --- the legs' two ends ----------------------------------------------------------------------------------------------

# The leg begun and not yet ended, and every leg that ended.
$legOpen = Join-Path $Out 'leg-open.txt'
$legsEnded = Join-Path $Out 'legs-ended.txt'

# A leg that began and never ended failed: recorded by its name, its own lines being its step's.
function Close-Leg {
    if (-not (Test-Path -LiteralPath $legOpen)) { return }
    $open = (Get-Content -LiteralPath $legOpen -Raw).Trim()
    Remove-Item -LiteralPath $legOpen -Force
    if (-not $open) { return }
    $words = Join-Path $Out 'leg-failure.txt'
    $why = if (Test-Path -LiteralPath $words) { "it said: $((Get-Content -LiteralPath $words) -join ' / ')" } else { 'its own lines are in its step' }
    Write-Host "::error title=Installation::the leg '$open' failed; $why"
    Add-Content -LiteralPath (Join-Path $Out 'failed-legs.txt') -Value "the leg '$open' failed; $why"
}

# This leg not run, said by why: recorded for Held, and -Out\skip left for the job's step to end on.
function Skip-Leg([string] $why) {
    Write-Host "::error title=Installation::$Leg - $why"
    Say "$Leg - $why"
    Add-Content -LiteralPath (Join-Path $Out 'not-run.txt') -Value "$Leg - $why"
    Remove-Item -LiteralPath $legOpen -Force -ErrorAction SilentlyContinue
    New-Item -ItemType File -Path (Join-Path $Out 'skip') -Force | Out-Null
}

# Every line of the machine now that is not a line of the machine before, and every line gone, the data directory and the
# harness root aside, as Compare reads them.
function Get-LeftOver {
    $now = Join-Path $Out 'now.txt'
    Write-Listing $now -Quiet
    $before = @(Get-Content -LiteralPath (Join-Path $Out 'before.txt'))
    $after = @(Get-Content -LiteralPath $now)
    $kept = '^(data directory|harness root)'
    @(@($after | Where-Object { $before -cnotcontains $_ -and $_ -notmatch $kept } | ForEach-Object { "+ $_" }) + @($before | Where-Object { $after -cnotcontains $_ -and $_ -notmatch $kept } | ForEach-Object { "- $_" }))
}

switch ($Step) {
    'Listing' {
        if (-not $Name) { Fail '-Name names the listing.' }
        Write-Listing (Join-Path $Out "$Name.txt")
    }

    'Compare' {
        $before = @(Get-Content -LiteralPath (Join-Path $Out "$Against.txt"))
        $after = @(Get-Content -LiteralPath (Join-Path $Out "$Name.txt"))
        # The data directory is kept by every uninstall that is not told to purge it; the harness root by every one.
        $kept = '^(data directory|harness root)'
        $gone = @($before | Where-Object { $after -cnotcontains $_ -and $_ -notmatch $kept })
        $left = @($after | Where-Object { $before -cnotcontains $_ -and $_ -notmatch $kept })
        $diff = Join-Path $Out "$Against-to-$Name.diff.txt"
        Set-Content -LiteralPath $diff -Value (@("only in $Against (before):") + @($gone | ForEach-Object { "- $_" }) + @("only in $Name (after):") + @($left | ForEach-Object { "+ $_" }))
        Get-Content -LiteralPath $diff | ForEach-Object { Write-Host $_ }
        $keptLines = @($after | Where-Object { $_ -match $kept -and $before -cnotcontains $_ })
        Say "$Name against ${Against}: $($left.Count) line(s) left, $($gone.Count) gone, beside $($keptLines.Count) line(s) of the data directory and the harness root, which are kept"
        # A divergence is recorded and the job goes on to its next leg, so that one run reads every leg; the Held step at
        # the end fails the job on any divergence recorded.
        if ($gone.Count -gt 0 -or $left.Count -gt 0) {
            Write-Host "::error title=Uninstall::the machine after '$Name' is not the machine before '${Against}': $(($gone | ForEach-Object { "- $_" }) + ($left | ForEach-Object { "+ $_" }) -join '; ')"
            Add-Content -LiteralPath (Join-Path $Out 'divergent.txt') -Value "$Name against ${Against}: see $diff"
        }
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
            if ($outcome -ceq 'success') { continue }
            Skip-Leg $(if ($outcome -ceq 'failure') { "not run: the leg it needs failed, $needed" } else { "not run: the leg it needs did not run ($outcome), $needed" })
            return
        }

        # The machine this leg starts from is the machine before anything was installed. A machine an earlier failed leg
        # left is taken off by the product's own doors - jason uninstall from each jason found, then Installed apps' quiet
        # uninstall - and said so; what they cannot bring back makes this leg not run, naming what is left. That earlier
        # leg's failure is recorded already; nothing here is a proof of anything.
        if ($Clean) {
            if (-not (Test-Path -LiteralPath (Join-Path $Out 'before.txt'))) { Skip-Leg 'not run: the leg it needs failed, The machine before anything is installed'; return }
            $left = @(Get-LeftOver)
            if ($left.Count -gt 0) {
                Say "the machine is not the machine before ($($left.Count) line(s): $(($left | Select-Object -First 6) -join '; ')); an earlier leg failed, and the product's own uninstalls take it off before this leg"
                try {
                    Get-OwnProcesses $app | Where-Object { $_.Name -ceq 'Jason.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
                    foreach ($jason in @($carried, (Join-Path $commandLine 'jason.exe'))) {
                        if (Test-Path -LiteralPath $jason) {
                            $removed = Invoke-Program $jason @('uninstall', '--purge-data', '--yes') @{} 300
                            Write-Host "$jason uninstall --purge-data --yes ended $($removed.Exit)"
                        }
                    }
                    foreach ($entry in @(Get-JasonUninstallEntries)) {
                        $quietLine = if ($entry.PSObject.Properties.Name -contains 'QuietUninstallString') { [string]$entry.QuietUninstallString } else { '' }
                        if ($quietLine -match '^"(?<file>[^"]+)"\s*(?<arguments>.*)$' -and (Test-Path -LiteralPath $Matches['file'])) {
                            $quiet = Wait-Program $Matches['file'] @($Matches['arguments'])
                            Write-Host "Installed apps' quiet uninstall of $($entry.DisplayName) ended $quiet"
                        }
                    }
                } catch {
                    Skip-Leg "not run: taking off the machine an earlier failed leg left failed: $($_.Exception.Message)"
                    return
                }
                $deadline = [DateTime]::UtcNow.AddSeconds(120)
                while ([DateTime]::UtcNow -lt $deadline -and @(Get-LeftOver).Count -gt 0) { Start-Sleep -Seconds 2 }
                $left = @(Get-LeftOver)
                if ($left.Count -gt 0) { Skip-Leg "not run: the machine an earlier failed leg left could not be brought back by the product's own uninstalls: $($left -join '; ')"; return }
                Say "the product's own uninstalls brought the machine back to the machine before"
            }
        }
    }

    'End' {
        if (-not $Leg) { Fail '-Leg names the leg.' }
        Add-Content -LiteralPath $legsEnded -Value $Leg
        Remove-Item -LiteralPath $legOpen -Force -ErrorAction SilentlyContinue
        Say "$Leg - ended"
    }

    'Held' {
        Close-Leg
        $divergent = Join-Path $Out 'divergent.txt'
        $failedLegs = Join-Path $Out 'failed-legs.txt'
        $notRun = Join-Path $Out 'not-run.txt'
        $held = @()
        if (Test-Path -LiteralPath $failedLegs) { $held += @(Get-Content -LiteralPath $failedLegs) }
        if (Test-Path -LiteralPath $notRun) { $held += @(Get-Content -LiteralPath $notRun) }
        if (Test-Path -LiteralPath $divergent) { $held += "the machine after an uninstall was not the machine before it: $((Get-Content -LiteralPath $divergent) -join '; ')" }
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
        $lastLine = @($first.Said -split "`n" | Where-Object { $_.Trim() })[-1]
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
        $found = Find-InNewShell 'jason'
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
        $again = Invoke-Program 'powershell.exe' @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', "$text; if (Get-Command Install-Jason -ErrorAction SilentlyContinue) { 'Install-Jason is still defined'; exit 9 }") @{ JASON_INSTALL_FEED = $Feed }
        if ($again.Exit -ne 0) { Fail "install.ps1's text run as the one-liner runs it exited with $($again.Exit)" }
        if (-not $again.Flat.Contains(("Jason Desktop $Version is already installed" -replace '\s+', ''))) { Fail 'the second run did not say the version is already installed' }
        if ((Count-Entry $bundle) -ne 1) { Fail 'a second run added a second Path entry' }
        Say 'run again as the one-liner runs it, it downloads nothing, adds nothing to the Path and leaves no function behind'

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
                    $runs[$id] = @{ Process = $handle; Silent = $silent; Line = "$($run.CommandLine)"; Windows = [System.Collections.Generic.List[string]]::new() }
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
            Write-ProcessWindows $asked.Process.Id
            $text = @(Use-WindowsType; [JasonProof.Windows]::Describe([uint32] $asked.Process.Id) | Where-Object { $_ -match "class 'Static', text '[^']+'" } | ForEach-Object { ($_ -replace "^.*text '", '') -replace "', visible.*$", '' })
            Fail-Held "the waiter ran the uninstaller WITHOUT /S with the window closed, and it showed: $($asked.Windows -join '; '); its text: $($text -join ' / ')"
            Stop-Process -Id $asked.Process.Id -Force -ErrorAction SilentlyContinue
            Wait-Until { -not (Get-Process -Id $asked.Process.Id -ErrorAction SilentlyContinue) } 30 'the asking uninstaller ending'
            Say 'that run was ended unanswered; the application stays installed, and Installed apps takes it off in the next leg'
            $entry = @(Get-JasonUninstallEntries)
            if ($entry.Count -gt 0 -and ([string]$entry[0].QuietUninstallString) -match '^"(?<file>[^"]+)"\s*(?<arguments>.*)$') {
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
        $opened = Open-Window
        if ((Test-Runtime) -ne 'yes') {
            $started = Invoke-Program $carried @('runtime', 'start')
            if ($started.Exit -ne 0) { Fail "the previous release's carried jason could not start a runtime ($($started.Exit)): $($started.Said)" }
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
        $found = Find-InNewShell 'jason'
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
        $opened = Open-Window
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
                if ($entry.Count -gt 0 -and ([string]$entry[0].QuietUninstallString) -match '^"(?<file>[^"]+)"\s*(?<arguments>.*)$') {
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
            $leg = Test-Leg 'leg 1, --updated /S over a window that never quits' @('--updated', '/S') @(3)
            if ($leg.Ended -and $leg.Exit -eq 2 -and $leg.Seconds -lt 30) { $failures.Add('leg 1 ended 2 within 30 s: the include''s look missed the window, and the old uninstaller refused') }
            if ($leg.Ended -and $leg.Exit -eq 3 -and $leg.Seconds -lt 45) { $failures.Add("leg 1 ended 3 after $($leg.Seconds) s, before the include's bound of about 60 s") }
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
        try {
            From-Previous $false
            $held = @('LICENSE.electron.txt', 'LICENSES.chromium.html', 'resources\app.asar') | ForEach-Object { Join-Path $app $_ } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
            if (-not $held) { Fail 'leg 5: no file of the application''s folder to hold' }
            $release = Join-Path $env:RUNNER_TEMP 'release-held-file'
            Remove-Item -LiteralPath $release -Force -ErrorAction SilentlyContinue
            $holder = Start-Process pwsh -ArgumentList '-NoProfile', '-Command', "`$f = [System.IO.File]::Open('$held', 'Open', 'Read', 'Read'); while (-not (Test-Path -LiteralPath '$release')) { Start-Sleep -Milliseconds 200 }; `$f.Dispose()" -PassThru -WindowStyle Hidden
            Start-Sleep -Seconds 3
            Say "leg 5: $(Split-Path -Leaf $held) of the application's folder held open by process $($holder.Id), sharing read alone"
            $null = Test-Leg 'leg 5, --updated /S while the old uninstaller cannot remove the folder' @('--updated', '/S') @(2)
            New-Item -ItemType File -Path $release -Force | Out-Null
            $null = $holder.WaitForExit(15000)
            Assert-Untouched 'leg 5' $false
            $opened = Open-Window -RuntimeSeconds 0
            if ($opened.Count -eq 0) { $failures.Add('after leg 5 the previous release does not open') } else { Say "after leg 5 the previous release $(Read-Version $carried) opens" }
            End-Application
        } catch {
            $failures.Add("leg 5 did not finish: $($_.Exception.Message)")
        } finally {
            New-Item -ItemType File -Path (Join-Path $env:RUNNER_TEMP 'release-held-file') -Force | Out-Null
        }

        if ($failures.Count -gt 0) { Fail ($failures -join '; ') }
    }

    'UninstallWaiterShown' {
        # jason uninstall's waiter (dotnet/src/Jason.Cli/Uninstall/IInstallationRemover.cs, UninstallerHandOff): where its
        # silent uninstaller ends 3 because Jason Desktop was opened after the verb's own check, it runs the uninstaller
        # once more without /S, which shows the person its question. Here: this build installed by the stand-in script,
        # a runtime started, jason uninstall started, and the window opened once the verb has stopped that runtime, past
        # its own check. The second run is found, its window seen, and it is ended without an answer; the folder is still
        # there, and the waiter's line is in the desktop's log of the day. Installed apps then takes the application off.
        if (-not $Script -or -not $Feed) { Fail '-Script and -Feed are needed.' }
        $installed = Invoke-InstallScript $Script
        if ($installed.Exit -ne 0) { Fail "install.ps1 exited with $($installed.Exit): $($installed.Said)" }
        if ((Invoke-Program $carried @('runtime', 'start')).Exit -ne 0) { Fail 'a runtime could not be started' }
        $descriptor = Join-Path $data 'run\runtime.json'
        $verb = Start-Process -FilePath $carried -ArgumentList 'uninstall', '--yes' -PassThru -WindowStyle Hidden
        $null = $verb.Handle
        Wait-Until { -not (Test-Path -LiteralPath $descriptor) -or $verb.HasExited } 120 'jason uninstall stopping the runtime'
        if ($verb.HasExited) { Fail "jason uninstall ended (exit $($verb.ExitCode)) before the window could be opened past its check: the leg proves nothing" }
        Start-Process -FilePath (Join-Path $app 'Jason.exe') | Out-Null
        Say 'the window opened once jason uninstall had stopped the runtime, past its own check'
        if (-not $verb.WaitForExit(300000)) { Fail 'jason uninstall did not end within 300 s' }
        Say "jason uninstall ended, exit $($verb.ExitCode)"
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
        $stopReading = Join-Path $env:RUNNER_TEMP 'stop-reading-requests'
        $readsFile = Join-Path $env:RUNNER_TEMP 'request-reads.txt'
        function Start-Reader {
            Remove-Item -LiteralPath $stopReading, $readsFile -Force -ErrorAction SilentlyContinue
            $loop = "`$n = 0; while (-not (Test-Path -LiteralPath '$stopReading')) { `$files = try { [System.IO.Directory]::GetFiles('$requests', 'desktop-*.json') } catch { @() }; foreach (`$f in `$files) { try { `$s = [System.IO.File]::Open(`$f, 'Open', 'Read', 'ReadWrite'); `$b = New-Object byte[] 65536; `$null = `$s.Read(`$b, 0, `$b.Length); Start-Sleep -Milliseconds 15; `$s.Dispose(); `$n++ } catch { } }; Start-Sleep -Milliseconds 2 }; Set-Content -LiteralPath '$readsFile' -Value `$n"
            Start-Process pwsh -ArgumentList '-NoProfile', '-Command', $loop -PassThru -WindowStyle Hidden
        }
        function Stop-Reader($reader) {
            New-Item -ItemType File -Path $stopReading -Force | Out-Null
            $null = $reader.WaitForExit(15000)
            return $(if (Test-Path -LiteralPath $readsFile) { [int](Get-Content -LiteralPath $readsFile -Raw) } else { -1 })
        }

        $server = Start-Process python -ArgumentList '-m', 'http.server', '8098', '--bind', '127.0.0.1', '--directory', $Next -PassThru -NoNewWindow
        try {
            Wait-Until { try { (Invoke-WebRequest -Uri 'http://127.0.0.1:8098/manifest.json' -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200 } catch { $false } } 20 'the next build''s feed answering on 127.0.0.1:8098'
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
        $quiet = [string]$entry[0].QuietUninstallString
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
        if ((Invoke-Program $jason @('runtime', 'start') $quiet).Exit -ne 0) { Fail 'the previous release could not start a runtime' }
        if ((Invoke-Program $jason @('runtime', 'autostart', 'enable') $quiet).Exit -ne 0) { Fail 'the previous release could not register its logon start' }
        if ((Count-Entry $commandLine) -ne 1) { Fail "the previous release's install did not put $commandLine on the user Path once" }
        Say "the previous release's command-line Jason $previousVersion installed by its own script at $jason, with a runtime running and a logon registration"

        # Its own update, pinned to this version, from the feed served on this machine. The update is the 0.48 parent's;
        # the move is this version's, which the parent hands the rest to.
        $server = Start-Process python -ArgumentList '-m', 'http.server', '8099', '--bind', '127.0.0.1', '--directory', $Feed -PassThru -NoNewWindow
        try {
            Wait-Until { try { Invoke-WebRequest -Uri http://127.0.0.1:8099/manifest.json -UseBasicParsing -TimeoutSec 5 | Out-Null; $true } catch { $false } } 10 'the feed on 127.0.0.1:8099 answering'
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
        $found = Find-InNewShell 'jason'
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
        if (-not $registration -or -not $registration.registered) { Fail 'no logon registration after the move' }
        if ([string]$registration.executable -ine $carried) { Fail "the logon registration names $($registration.executable), not $carried" }
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
        $aside = @(Get-ChildItem -LiteralPath (Split-Path -Parent $commandLine) -Directory -Filter 'jason-retired-*' -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
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
