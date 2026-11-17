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
                    machine set-up an application's start runs, which writes nothing under a data directory
  UninstallFromTerminal   jason uninstall typed in a fresh shell, the data directory purged: the waiter hands the folder to
                    the uninstaller with --uninstalled-by-jason, the folder goes whole, and no start of the application
                    writes a data directory again
  InstallOverPrevious     the previous release's Jason Desktop, installed by its own setup and left open, then this build's
                    setup run as an update runs it (--updated /S --force-run): the open window closed by the installer,
                    this version in place, its machine set-up done, the application started again
  UninstallFromInstalledApps   the uninstall Installed apps runs, QuietUninstallString: the folder, its entry, the Path
                    entry and the skills go, the data directory stays
  FromPrevious      the previous release's command-line Jason, installed by its own script, with a runtime and a logon
                    registration, updated by its own `jason update apply` from the feed served on this machine
  Compare           -Name against -Against: every line equal but those of the data directory, which an uninstall keeps
  ModulePath        a reading, never a failure: the module path Windows PowerShell rebuilds from an empty one

-Signer names the signer the script under test holds the setup to: the release's on the signed packages; on CI's test
build, which a release never signs, the runner's own test certificate, which the job's copy of install.ps1 names.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Listing', 'Install', 'UninstallFromTerminal', 'InstallOverPrevious', 'UninstallFromInstalledApps', 'FromPrevious', 'Compare', 'ModulePath')] [string] $Step,
    [string] $Feed,
    [string] $Version,
    [string] $Script,
    [string] $Previous,
    [string] $Out = (Join-Path $env:RUNNER_TEMP 'installation'),
    [string] $Name,
    [string] $Against
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

function Fail([string] $why) { throw $why }

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

# A program run with its output kept and shown, and its exit code: Windows PowerShell's lines on stderr included.
function Invoke-Program([string] $file, [string[]] $arguments, [hashtable] $environment = @{}) {
    $saved = @{}
    foreach ($name in $environment.Keys) { $saved[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name, $environment[$name]) }
    try {
        $said = (& $file @arguments 2>&1 | ForEach-Object { "$_" }) -join "`n"
        $code = $LASTEXITCODE
    } finally {
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    }
    Write-Host $said
    return [pscustomobject]@{ Exit = $code; Said = $said; Flat = ($said -replace '\s+', '') }
}

# install.ps1 as the one-liner runs it: Windows PowerShell 5.1, the file run.
function Invoke-InstallScript([string] $path) {
    Invoke-Program 'powershell.exe' @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $path) @{ JASON_INSTALL_FEED = $Feed }
}

# What a terminal opened from now on finds: a new pwsh given the environment Windows would give it, not this job's.
function Find-InNewShell([string] $command) {
    $file = Join-Path $Out "found-$([Guid]::NewGuid().ToString('N')).txt"
    $probe = "`$found = Get-Command $command -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1; if (`$found) { `$found.Source; & `$found.Source --version } else { 'not found' }"
    Start-Process pwsh -ArgumentList '-NoProfile', '-NonInteractive', '-Command', $probe -UseNewEnvironment -Wait -NoNewWindow -RedirectStandardOutput $file
    $lines = @(Get-Content -LiteralPath $file)
    Remove-Item -LiteralPath $file
    return $lines
}

function Read-Version([string] $jason) {
    $record = Join-Path (Split-Path -Parent $jason) 'version.json'
    if (-not (Test-Path -LiteralPath $record)) { return $null }
    return [string](Get-Content -LiteralPath $record -Raw | ConvertFrom-Json).version
}

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
function Write-Listing([string] $file) {
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
    Write-Host "$($sorted.Count) lines written to $file"
    $sorted | ForEach-Object { Write-Host "  $_" }
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
        if ($gone.Count -gt 0 -or $left.Count -gt 0) { Fail "the machine after '$Name' is not the machine before '$Against': see $diff" }
    }

    'Install' {
        if (-not $Feed -or -not $Version -or -not $Script) { Fail '-Feed, -Version and -Script are needed.' }
        $before = Get-UserPath

        # The agent's install: the script run as a file in Windows PowerShell, as the one-liner's shell runs it.
        $first = Invoke-InstallScript $Script
        if ($first.Exit -ne 0) { Fail "install.ps1 exited with $($first.Exit)" }
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
        $again = Invoke-Program 'powershell.exe' @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', "Invoke-Expression (Get-Content -LiteralPath '$Script' -Raw); if (Get-Command Install-Jason -ErrorAction SilentlyContinue) { 'Install-Jason is still defined'; exit 9 }") @{ JASON_INSTALL_FEED = $Feed }
        if ($again.Exit -ne 0) { Fail "install.ps1's text run through Invoke-Expression exited with $($again.Exit)" }
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
        $runtime = Invoke-Program $carried @('runtime', 'start') @{ JASON_Update__CheckEnabled = 'false' }
        if ($runtime.Exit -ne 0) { Fail "a runtime could not be started before the uninstall ($($runtime.Exit))" }
        $file = Join-Path $Out 'uninstall-terminal.json'
        $probe = "jason uninstall --purge-data --yes | Set-Content -LiteralPath '$file'; exit `$LASTEXITCODE"
        $verb = Start-Process pwsh -ArgumentList '-NoProfile', '-NonInteractive', '-Command', $probe -UseNewEnvironment -Wait -PassThru -NoNewWindow
        Get-Content -LiteralPath $file | ForEach-Object { Write-Host $_ }
        if ($verb.ExitCode -ne 0) { Fail "jason uninstall exited with $($verb.ExitCode)" }
        if (-not ((Get-Content -LiteralPath $file -Raw).Contains('Handed the application at'))) { Fail 'jason uninstall did not hand the application to its uninstaller' }

        # The waiter hands the folder to the uninstaller once the verb has ended, and the folder goes whole.
        Wait-Until { -not (Test-Path -LiteralPath $app) } 120 "the application's folder going after jason uninstall ended"
        Wait-Until { @(Get-JasonUninstallEntries).Count -eq 0 } 30 "the Installed apps entry going"
        Start-Sleep -Seconds 5
        if (Test-Path -LiteralPath $data) { Fail "$data was written again after the purge: the uninstaller started the application although jason uninstall had done it all" }
        if ((Count-Entry $bundle) -ne 0) { Fail 'the Path entry naming the bundle folder is left' }
        if (@(Get-OwnProcesses $app).Count -ne 0) { Fail 'a process still runs from the application folder' }
        if (-not (Test-Path -LiteralPath (Join-Path $ownSkill 'SKILL.md'))) { Fail "the person's own skill went with the uninstall" }
        Say 'jason uninstall from a new shell: the runtime stopped, the folder gone whole after the verb ended, no Installed apps entry, no Path entry, no data directory written again, the person''s own skill kept'
    }

    'InstallOverPrevious' {
        if (-not $Previous -or -not $Feed -or -not $Version) { Fail '-Previous, -Feed and -Version are needed.' }
        $old = Join-Path $Previous 'jason-desktop-windows-x64-setup.exe'
        $setup = Get-AuthenticodeSignature -LiteralPath $old
        if ($setup.Status -ne 'Valid' -or $setup.SignerCertificate.GetNameInfo('SimpleName', $false) -cne 'ReplyApp Inc.') { Fail "the previous release's setup is not signed by ReplyApp Inc.: $($setup.Status)" }
        $installed = Start-Process -FilePath $old -ArgumentList '/S' -Wait -PassThru
        if ($installed.ExitCode -ne 0) { Fail "the previous release's setup exited with $($installed.ExitCode)" }
        $previousVersion = Read-Version $carried
        Say "the previous release's Jason Desktop $previousVersion installed by its own setup /S"

        # Open, as a person leaves it; the update's installer closes it.
        $window = Start-Process -FilePath (Join-Path $app 'Jason.exe') -PassThru
        Wait-Until { @(Get-OwnProcesses $app).Count -gt 0 } 60 'the previous Jason Desktop starting'
        Start-Sleep -Seconds 5
        $opened = @(Get-OwnProcesses $app | ForEach-Object { $_.ProcessId })

        # This build's setup, as its updater runs it.
        $new = Start-Process -FilePath (Join-Path $Feed 'jason-desktop-windows-x64-setup.exe') -ArgumentList '--updated', '/S', '--force-run' -Wait -PassThru
        if ($new.ExitCode -ne 0) { Fail "this build's setup run as an update exited with $($new.ExitCode)" }
        if ((Read-Version $carried) -ne $Version) { Fail "after the update the application is $(Read-Version $carried), not $Version" }
        $stillOpen = @($opened | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
        if ($stillOpen.Count -ne 0) { Fail "the previous window's processes $($stillOpen -join ', ') outlived the update's installer" }
        Say "this build's setup with --updated /S --force-run closed the open $previousVersion window and put $Version in place"

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

    'UninstallFromInstalledApps' {
        $entry = @(Get-JasonUninstallEntries)
        if ($entry.Count -ne 1) { Fail "Installed apps lists $($entry.Count) Jason entries, not one" }
        $quiet = [string]$entry[0].QuietUninstallString
        if ($quiet -notmatch '^"(?<file>[^"]+)"\s*(?<arguments>.*)$') { Fail "QuietUninstallString is not a quoted program and its arguments: $quiet" }
        $removal = Start-Process -FilePath $Matches['file'] -ArgumentList $Matches['arguments'] -Wait -PassThru
        if ($removal.ExitCode -ne 0) { Fail "the uninstall Installed apps runs exited with $($removal.ExitCode)" }
        Wait-Until { -not (Test-Path -LiteralPath $app) } 60 "the application's folder going"
        if (@(Get-JasonUninstallEntries).Count -ne 0) { Fail 'the Installed apps entry is left' }
        if ((Count-Entry $bundle) -ne 0) { Fail 'the Path entry naming the bundle folder is left: the uninstaller did not run jason uninstall --keep-application' }
        foreach ($root in @('.claude\skills', '.agents\skills')) { if (Test-Path -LiteralPath (Join-Path $env:USERPROFILE "$root\.jason-skills.json")) { Fail "the skills record in $root is left" } }
        if (-not (Test-Path -LiteralPath $data)) { Fail "the data directory $data went, which only --purge-data takes" }
        Say "Installed apps' quiet uninstall: the folder, its entry, the Path entry and the skills gone, the data directory kept"
    }

    'FromPrevious' {
        if (-not $Previous -or -not $Feed) { Fail '-Previous and -Feed are needed.' }
        # The previous release's command-line Jason, installed by its own script from its own assets, as a person has it.
        $old = Invoke-Program 'powershell.exe' @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Previous 'install.ps1')) @{ JASON_INSTALL_FEED = $Previous }
        if ($old.Exit -ne 0) { Fail "the previous release's install.ps1 exited with $($old.Exit)" }
        $jason = Join-Path $commandLine 'jason.exe'
        $previousVersion = ((& $jason --version) -split '\+')[0]
        $quiet = @{ JASON_Update__CheckEnabled = 'false' }
        if ((Invoke-Program $jason @('runtime', 'start') $quiet).Exit -ne 0) { Fail 'the previous release could not start a runtime' }
        if ((Invoke-Program $jason @('autostart', 'enable') $quiet).Exit -ne 0) { Fail 'the previous release could not register its logon start' }
        Say "the previous release's command-line Jason $previousVersion installed by its own script, with a runtime and a logon registration"

        # Its own update, from the feed served on this machine.
        $server = Start-Process python -ArgumentList '-m', 'http.server', '8099', '--bind', '127.0.0.1', '--directory', $Feed -PassThru -NoNewWindow
        try {
            Wait-Until { try { Invoke-WebRequest -Uri http://127.0.0.1:8099/manifest.json -UseBasicParsing | Out-Null; $true } catch { $false } } 10 'the feed on 127.0.0.1:8099 answering'
            $update = Invoke-Program $jason @('update', 'apply', '--feed', 'http://127.0.0.1:8099/manifest.json', '--drain-seconds', '0') $quiet
            Set-Content -LiteralPath (Join-Path $Out 'update-from-previous.json') -Value $update.Said
            if ($update.Exit -ne 0) { Fail "the previous release's jason update apply exited with $($update.Exit)" }
        } finally {
            Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        }
        Say "the previous release's own jason update apply took the feed served on this machine"

        # THE MOVE (the unit that moves a command-line install into the application) is asserted here once it lands: the
        # application at $app, jason in a new shell its carried file, the command-line install retired, the runtime
        # running from the carried file, the logon task naming it, and the move's record complete. Until then the update
        # is the command-line update, read back as such.
        $now = ((& $jason --version) -split '\+')[0]
        if ($now -ne $Version) { Fail "after the update the command-line jason says $now, not $Version" }
        $status = Invoke-Program $jason @('runtime', 'status') $quiet
        if ($status.Exit -ne 0) { Fail 'no runtime answers after the update' }
        Say "the command-line install now says $Version and its runtime answers (the move into the application is asserted once it lands)"

        # Taken off by its own uninstall, which is the product's; what remains is compared after this step.
        $removal = Invoke-Program $jason @('uninstall') $quiet
        if ($removal.Exit -ne 0) { Fail "jason uninstall of the updated command-line install exited with $($removal.Exit)" }
        Wait-Until { -not (Test-Path -LiteralPath $jason) } 60 'the command-line executable going'
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
