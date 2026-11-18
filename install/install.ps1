<#
.SYNOPSIS
Installs Jason on Windows: Jason Desktop, with jason in the terminal.

.DESCRIPTION
Downloads jason-desktop-windows-x64-setup.exe from the latest release, or from the release -Version names, checks it
against checksums.txt and its Authenticode signature, ReplyApp Inc.'s, before it runs it, and runs it silently,
waiting for its exit code. The installer puts the application in %LOCALAPPDATA%\Programs\jason-desktop and sets the
machine up through the jason it carries. This script then runs that jason's `jason install --roles` once more and reads
its report: it puts the application's resources\bundle folder on this account's Path, deploys the skills for Claude Code
and Codex where each is found, retires an older command-line install of Jason, and deploys the role skills into
Jason's data directory. It opens no window. In an elevated PowerShell - an administrator's, or another account's than
the one signed in to the session - it stops before it downloads anything: what it sets up is the account's own. Where a
runtime answered before the install and none answers after it, or the report says it stopped one, a runtime is started again from the application's jason. Run again over the version
already installed, it downloads nothing and says so, and sets the machine up again, which changes nothing that is in
place. A later version already installed is kept as it is; an earlier one is installed over, as its own update does,
but not while Jason Desktop is running, in its window or in the tray: then it says to quit it and run the line again, and installs nothing. It says where Jason will keep what it learns about your business - Jason in your home directory, unless
Harness:Root in the settings names another folder - and makes no folder for it.

It ends non-zero, saying which act failed, when jason was not put in the terminal. On success its last line is the
full path of jason, for the terminal it ran in and an agent's session already running, which do not see the new Path.

    irm https://get.jasonharness.ai/releases/latest/download/install.ps1 | iex
    & ([scriptblock]::Create((irm https://get.jasonharness.ai/releases/download/v0.49.0/install.ps1))) -Version 0.49.0

-Feed (or JASON_INSTALL_FEED) points it at a directory or a base URL holding the same files a release holds
- the installer, manifest.json and checksums.txt - which is how CI runs it against what it has just built.

This is the Windows half. macOS and Linux are install.sh. Written for Windows PowerShell 5.1 as well as PowerShell 7,
because 5.1 is what a fresh Windows has and what the one-liner is typed into.

.PARAMETER Version
The release to install, without a leading v. The latest, when not given. Jason Desktop is installed this way from 0.49.0.

.PARAMETER Feed
A directory or a base URL to read the release's files from, instead of the published releases. Also JASON_INSTALL_FEED.

.PARAMETER InstallDir
Not taken: Jason Desktop goes to %LOCALAPPDATA%\Programs\jason-desktop. Given, nothing is installed.

.PARAMETER NoModifyPath
Not taken: every install puts jason in the terminal. Given, nothing is installed.
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $Feed,
    [string] $InstallDir,
    [switch] $NoModifyPath
)

# The documented one-liner pipes this text into iex, which runs it in the session the person is sitting in: every
# top-level assignment would stay behind when the install finished - the error preference and the strict mode
# above all, which change how every later command in that session behaves. So the installer is a function, and
# the call at the bottom is the only thing that session ever sees. This file stays ASCII for the same kind of
# reason: Windows PowerShell 5.1 reads a file without a byte-order mark as ANSI, and the one-liner is typed there.
function Install-Jason {
    [CmdletBinding()]
    param(
        [string] $Version,
        [string] $Feed,
        [string] $InstallDir,
        [switch] $NoModifyPath
    )

    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    # A program's exit code is read here and said in words; PowerShell 7.3 and later would otherwise stop on it first.
    $PSNativeCommandUseErrorActionPreference = $false

    # The product's own address, in front of the releases: it redirects every path under /releases/ to the same path
    # in the public repository's releases on GitHub.
    $releasesUrl = 'https://get.jasonharness.ai/releases'
    # The licence is a page of the repository, at the release's tag; the downloads are a release's assets. Each has a
    # base of its own, so that the downloads can move without the licence page moving with them.
    $licenceBase = 'https://github.com/reply-team/jason-ai/blob'
    $asset = 'jason-desktop-windows-x64-setup.exe'
    # Where the installer puts the application: per user, in this account's own programs folder. It is handed to the
    # installer rather than left to it, so the jason this script runs afterwards is the one it installed.
    $app = Join-Path $env:LOCALAPPDATA 'Programs\jason-desktop'
    $carried = Join-Path $app 'resources\bundle\jason.exe'
    # A command-line install of an earlier release, in the place its installer used: a runtime may run from it.
    $commandLine = Join-Path $env:LOCALAPPDATA 'Programs\jason\jason.exe'

    if (-not $Feed) { $Feed = $env:JASON_INSTALL_FEED }

    # --- this machine -----------------------------------------------------------------------------------------

    # PowerShell 7 runs on macOS and Linux too, and there this is the wrong script.
    if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) {
        throw "This is the Windows installer. On macOS and Linux run install.sh."
    }
    # PROCESSOR_ARCHITECTURE says what this shell is, not what the machine is: a 32-bit PowerShell on 64-bit
    # Windows - which is what a scheduled task, an installer's "run this afterwards" or an old shortcut still
    # gives people - reads x86 there, and Windows puts the machine's own architecture in PROCESSOR_ARCHITEW6432
    # for exactly that shell. Reading only the first refuses to install on an ordinary x64 machine.
    $architecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    if ($architecture -ne 'AMD64') {
        throw "No release is built for Windows on $architecture; Jason Desktop for Windows is built for x64."
    }

    # Refused before anything is read or written: neither can be done the way they ask any more.
    if ($InstallDir) {
        throw "-InstallDir is not taken: Jason Desktop goes to $app, and the jason it carries is the one the terminal runs. Nothing was installed: run this again without it."
    }
    if ($NoModifyPath) {
        throw "-NoModifyPath is not taken: every install puts jason in the terminal, on this account's Path. Nothing was installed: run this again without it."
    }

    # Whether this PowerShell runs above what its account ordinarily has, by the rule `jason install` and an update refuse
    # by: the full half of a split token, which an administrator's elevated terminal holds with User Account Control on, or
    # an account other than the one signed in to this session. An administrator whose Windows has no split token - User
    # Account Control off, as on a hosted build machine - runs with the account's one token and is not refused, and nor is
    # a session with no account to compare with, a service's. Where the token's kind cannot be read, being in the
    # Administrators role decides. Its members are compiled once per session and run nothing else.
    function Test-Elevated {
        if (-not ('JasonInstall.Elevation' -as [type])) {
            Add-Type -Namespace JasonInstall -Name Elevation -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
[DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
[DllImport("advapi32.dll")] public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
[DllImport("advapi32.dll")] public static extern bool GetTokenInformation(IntPtr token, int informationClass, out int information, int length, out int returned);
[DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "WTSQuerySessionInformationW")] public static extern bool WTSQuerySessionInformation(IntPtr server, int session, int what, out IntPtr buffer, out int bytes);
[DllImport("wtsapi32.dll")] public static extern void WTSFreeMemory(IntPtr memory);

// TOKEN_ELEVATION_TYPE: 1 no split token, 2 the full half of one, 3 the limited half; 0 where it cannot be read.
public static int TokenKind() {
    IntPtr token;
    if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out token)) { return 0; }
    try {
        int kind, returned;
        return GetTokenInformation(token, 18, out kind, 4, out returned) && kind >= 1 && kind <= 3 ? kind : 0;
    } finally {
        CloseHandle(token);
    }
}

// The user (5) or domain (7) name of the account signed in to this session; null where there is none to read.
public static string Session(int what) {
    IntPtr buffer;
    int bytes;
    try {
        if (!WTSQuerySessionInformation(IntPtr.Zero, -1, what, out buffer, out bytes)) { return null; }
    } catch (DllNotFoundException) {
        return null;
    } catch (EntryPointNotFoundException) {
        return null;
    }
    try { return Marshal.PtrToStringUni(buffer); } finally { WTSFreeMemory(buffer); }
}
'@
        }

        $kind = [JasonInstall.Elevation]::TokenKind()
        if ($kind -eq 0) {
            $principal = New-Object Security.Principal.WindowsPrincipal ([Security.Principal.WindowsIdentity]::GetCurrent())
            return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        }
        if ($kind -eq 2) { return $true }

        $sessionUser = [JasonInstall.Elevation]::Session(5)
        $sessionDomain = [JasonInstall.Elevation]::Session(7)
        $user = [Environment]::UserName
        $domain = [Environment]::UserDomainName
        if (-not $sessionUser -or -not $user) { return $false }
        if ($sessionUser -ne $user) { return $true }
        return [bool] ($sessionDomain -and $domain -and $sessionDomain -ne $domain)
    }

    if (Test-Elevated) {
        throw "This PowerShell runs as an administrator, elevated above this account's ordinary rights, or as another account than the one signed in to this session, and Jason Desktop, jason in the terminal and the skills would be set up the same way or in another account's profile, so nothing was downloaded or installed: run the install line again from an ordinary PowerShell of the account signed in here, not run as an administrator."
    }

    # --- the feed ---------------------------------------------------------------------------------------------

    $base = if ($Feed) { $Feed } elseif ($Version) { "$releasesUrl/download/v$Version" } else { "$releasesUrl/latest/download" }
    $fromWeb = $base -match '^https?://'

    if ($fromWeb -and $PSVersionTable.PSVersion.Major -lt 6) {
        # Windows PowerShell's defaults are older than the release's address and GitHub accept, and its progress bar
        # makes a download slow.
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $ProgressPreference = 'SilentlyContinue'
    }

    # One file from the feed into the temporary directory: over the web, or a copy from a directory.
    function Fetch([string] $name, [string] $to) {
        if ($fromWeb) {
            Invoke-WebRequest -Uri "$base/$name" -OutFile $to -UseBasicParsing
        } else {
            $source = Join-Path $base $name
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "$source does not exist." }
            Copy-Item -LiteralPath $source -Destination $to
        }
    }

    # A version's numbers, the pre-release part ignored, to compare field by field; nothing where they are not numbers.
    function Get-Core([string] $version) {
        $core = $null
        if ([version]::TryParse(($version -split '[-+]')[0], [ref] $core)) { return $core }
        return $null
    }

    # True when $version is $floor or later, the pre-release part ignored, compared field by field.
    function Test-From([string] $version, [string] $floor) {
        $core = Get-Core $version
        $null -ne $core -and $core -ge [version] $floor
    }

    # Whether Jason Desktop is running: a program running from its own executable, found by its exact path, as its
    # installer looks for it - the process, not a window, so a Desktop left in the tray, which has no window, is found as
    # surely as one whose window is open. A query that fails here reads as not running; the installer's own look fails
    # closed instead. A silent installer never closes it: where it finds it, or cannot look, it ends with 3, said in the
    # same sentence as this look's, which names both.
    function Test-DesktopRunning {
        $application = Join-Path $app 'Jason.exe'
        if (-not (Test-Path -LiteralPath $application -PathType Leaf)) { return $false }
        $running = @(Get-CimInstance -ClassName Win32_Process | Where-Object { $_.ExecutablePath -and [string]::Equals($_.ExecutablePath, $application, [StringComparison]::OrdinalIgnoreCase) })
        return $running.Count -gt 0
    }
    $desktopRunning = 'Jason Desktop is running, in its window or in the tray, or Jason could not tell whether it is, so nothing was installed: quit it - Quit in its tray menu - and run this line again.'

    # The installer is run only when it carries a valid Authenticode signature whose signer is the release's publisher,
    # by the simple name of the certificate the file's own signature names, in full and in the same case: a valid
    # signature alone says only that some trusted publisher signed it. The checksum says the file is the one the
    # release lists; this says who made it.
    function Assert-Signed([string] $file) {
        $signer = 'ReplyApp Inc.'
        $signature = Get-AuthenticodeSignature -LiteralPath $file
        if ($signature.Status -ne 'Valid') { throw "$asset does not carry a valid signature ($($signature.Status)), so it was not run; nothing was installed." }
        $name = $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
        if ($name -cne $signer) { throw "$asset is signed by '$name', not by $signer, so it was not run; nothing was installed." }
    }

    # Whether a runtime answers on this installation's data directory, asked of the jason that may have started it:
    # the application's, where it is already installed, else a command-line install's in its installer's place. Asked
    # before the install and after it: an update stops the application's runtime, and the setup retires a command-line
    # install's, and the caller that asked for the install is the one that starts a runtime again.
    function Test-Answers([string] $jason) {
        if (-not $jason -or -not (Test-Path -LiteralPath $jason -PathType Leaf)) { return $false }
        # Windows PowerShell turns a redirected line on stderr into an error, which this function's preference would stop on.
        $ErrorActionPreference = 'Continue'
        try {
            & $jason runtime status *> $null
            return $LASTEXITCODE -eq 0
        } catch {
            return $false
        }
    }

    # The version an application's jason says it is, from the version.json beside it; nothing where there is none.
    function Get-Carried([string] $jason) {
        $record = Join-Path (Split-Path -Parent $jason) 'version.json'
        if (-not (Test-Path -LiteralPath $jason -PathType Leaf) -or -not (Test-Path -LiteralPath $record -PathType Leaf)) { return $null }
        try { return [string](Get-Content -LiteralPath $record -Raw | ConvertFrom-Json).version } catch { return $null }
    }

    # A jason run with what it writes on stdout returned and what it writes on stderr passed on to stderr as it wrote it.
    # Windows PowerShell 5.1 makes an error record of every line a program writes on stderr once this script's own
    # streams are redirected, as they are when an agent runs it, and the preference above would stop on the first one:
    # a good install would end as a failure for a line of diagnostics. So the preference here is Continue, the two
    # streams are taken apart, and the exit code is left in $LASTEXITCODE for the caller.
    function Invoke-Jason([string] $jason, [string[]] $arguments) {
        if (-not (Test-Path -LiteralPath $jason -PathType Leaf)) { throw "$jason is not there." }
        $ErrorActionPreference = 'Continue'
        & $jason @arguments 2>&1 | ForEach-Object {
            if ($_ -is [Management.Automation.ErrorRecord]) { [Console]::Error.WriteLine($_.ToString()) } else { $_ }
        }
    }

    # A runtime started again from the application's jason, where it is in place, else from the command-line install's:
    # whichever is whole at this step, on success and on failure alike. What it says is shown.
    function Start-Again {
        $from = if (Test-Path -LiteralPath $carried -PathType Leaf) { $carried } elseif (Test-Path -LiteralPath $commandLine -PathType Leaf) { $commandLine } else { $null }
        if (-not $from) { return }
        Write-Host "A runtime answered before the install and none answers now, so one is started again from $from."
        try {
            Invoke-Jason $from @('runtime', 'start') | ForEach-Object { Write-Host $_ }
            if ($LASTEXITCODE -ne 0) { Write-Host "The runtime did not start; the lines above say why. Run ``$from runtime start`` once that is dealt with." }
        } catch {
            Write-Host "The runtime could not be started: $($_.Exception.Message)"
        }
    }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) "jason-install-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $tmp | Out-Null
    $answered = $false
    try {
        # --- the version, and whether it is already here ------------------------------------------------------

        Fetch 'manifest.json' (Join-Path $tmp 'manifest.json')
        $latest = (Get-Content -LiteralPath (Join-Path $tmp 'manifest.json') -Raw | ConvertFrom-Json).version
        if (-not $latest) { throw "$base/manifest.json does not name a version." }

        # -Version names a release, and the manifest that came back has to be that release's. A feed that
        # answered with another version - a stale mirror, a directory holding the wrong release, a download URL
        # that resolved to something else - would otherwise be installed anyway, under the version asked for.
        if ($Version -and $latest -ne $Version) {
            throw "-Version asked for $Version and $base/manifest.json names $latest; nothing was installed."
        }
        # A release before 0.49.0 carries a jason that cannot set the machine up; its own script installs it.
        if (-not (Test-From $latest '0.49.0')) {
            throw "Jason $latest is installed by its own release's script, not this one, which installs Jason Desktop 0.49.0 and later; nothing was installed."
        }

        $answered = (Test-Answers $carried) -or (Test-Answers $commandLine)

        # An application already here of the release asked for is set up again; one of a later release is kept as it
        # is, as an installer run over it would take it back. An earlier one is installed over, as its own update does,
        # but never while it is running, in its window or in the tray: a person's application is never closed from here.
        $placed = Get-Carried $carried
        $installed = $latest
        if ($placed -eq $latest) {
            Write-Host "Jason Desktop $latest is already installed at $app; nothing to download."
        } elseif ($placed -and (Get-Core $placed) -and (Get-Core $latest) -and (Get-Core $placed) -gt (Get-Core $latest)) {
            $installed = $placed
            Write-Host "Jason Desktop $placed is already installed at $app, later than $latest; it is kept as it is, and nothing is downloaded."
        } else {
            if (Test-DesktopRunning) {
                throw $desktopRunning
            }
            # --- download, and check before running -------------------------------------------------------------

            Write-Host "Downloading Jason Desktop $latest for Windows from $base ..."
            Fetch 'checksums.txt' (Join-Path $tmp 'checksums.txt')
            $setup = Join-Path $tmp $asset
            Fetch $asset $setup
            $line = Get-Content -LiteralPath (Join-Path $tmp 'checksums.txt') | Where-Object { $_ -match "^[0-9a-f]{64}  $([regex]::Escape($asset))$" } | Select-Object -First 1
            if (-not $line) { throw "checksums.txt has no line for $asset." }
            $expected = ($line -split ' ')[0]
            $actual = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -ne $expected) { throw "$asset does not match checksums.txt (sha256 $actual, expected $expected); nothing was installed." }
            Assert-Signed $setup
            # PowerShell's own download marks nothing, but a feed in a directory may hold a setup a browser downloaded,
            # which carries the mark of the web, and the copy keeps it: SmartScreen would hold a silent install on it
            # with a dialog nobody sees. The signer is checked above, so the mark says nothing more.
            Unblock-File -LiteralPath $setup

            # --- install, silently, and wait for it ---------------------------------------------------------

            # Looked for again at the last moment, for a Desktop started while the setup was downloaded and checked; one
            # started after this look is found by the installer itself, which then ends with 3.
            if (Test-DesktopRunning) {
                throw $desktopRunning
            }
            # A GUI program typed bare does not hold PowerShell, and gives it no exit code: Start-Process -Wait waits for
            # it and everything it starts, and the installer starts nothing that outlives it. /D= is the last argument,
            # unquoted, as the installer reads it to the end of the line.
            Write-Host "Installing Jason Desktop $latest to $app ..."
            $installer = Start-Process -FilePath $setup -ArgumentList "/S /D=$app" -Wait -PassThru
            switch ($installer.ExitCode) {
                0 { }
                1 { throw "The installer stopped before replacing a file: a Jason process runs from $app that did not stop - a runtime started with another data directory, say. Stop it with ``$carried runtime stop``, with JASON_DATA_DIR set as it was started, and run this again." }
                2 { throw "The installer stopped before it installed: another install of Jason ran at the same time, or the version already there could not be removed. Run this again." }
                3 { throw $desktopRunning }
                default { throw "The installer exited with $($installer.ExitCode), and Jason Desktop was not installed." }
            }
            if ((Get-Carried $carried) -ne $latest) { throw "The installer exited 0, and $carried is not Jason $latest." }
            Write-Host "Installed Jason Desktop $latest to $app"
        }

        # --- the machine set up ---------------------------------------------------------------------------------

        # The installer has already run the setup, with no flags; run here, it also deploys the role skills, and its
        # report is read. It writes one JSON document on stdout, shown as it came; what it says on stderr goes
        # straight through, never through PowerShell's error stream, where Windows PowerShell would stop on it.
        $document = $null
        try {
            $document = @(Invoke-Jason $carried @('install', '--roles')) -join "`n"
        } catch {
            throw "``jason install`` could not be started ($($_.Exception.Message)), so jason was not put in the terminal."
        }
        Write-Host $document
        $report = $null
        try { $report = $document | ConvertFrom-Json } catch { }

        $stopped = $report -and ($report.PSObject.Properties.Name -contains 'runtime') -and $report.runtime -eq 'stopped'
        if ($stopped -or ($answered -and -not (Test-Answers $carried))) { Start-Again }
        $answered = $false

        if (-not $report) { throw "``jason install`` printed no report that could be read, so whether jason is in the terminal is not known." }
        $members = @($report.PSObject.Properties.Name)
        if ($members -contains 'error') { throw "``jason install`` refused ($($report.error.code)): $($report.error.message)" }
        $command = if ($members -contains 'command') { $report.command } else { $null }
        if (-not $command -or -not (@($command.PSObject.Properties.Name) -contains 'placed') -or -not $command.placed) {
            throw "``jason install`` did not put jason in the terminal; its problems above say why. Jason Desktop is installed at $app."
        }
        # Whether a terminal opened from now on runs this jason for the name, as jason install decided it once everything
        # was placed (finds_this), and the file it finds (finds): another file earlier on the Path is named in the report's
        # problems with what removes it. No path is compared here. A report without the member is one from before it was
        # read, and says nothing either way.
        if (@($command.PSObject.Properties.Name) -contains 'finds_this') {
            if (-not $command.finds_this) {
                $first = if ([string]$command.finds) { [string]$command.finds } else { 'no jason' }
                throw "``jason install`` put jason on this account's Path, but a terminal opened from now on finds $first first by that name; its problems above say what to do. Jason Desktop is installed at $app."
            }
        }
        if (-not (($members -contains 'completed') -and $report.completed)) {
            Write-Host 'Jason is installed and in the terminal; `jason install` named above what else it did not get done, and `jason status` names it too.'
        }
        $openShell = [string]$command.open_shell
    } catch {
        # A failure after a runtime was stopped still leaves one started from whichever jason is whole.
        if ($answered -and -not ((Test-Answers $carried) -or (Test-Answers $commandLine))) { Start-Again }
        throw
    } finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }

    # --- where the files will be ------------------------------------------------------------------------------

    # What Jason learns about the person's business is kept in plain files under the harness root, which the runtime
    # makes at the first project or campaign it creates; this script makes no folder there. The folder is said as the
    # default, made as the runtime makes it: Harness:Root in the settings can name another, and only the runtime reads
    # its settings, so the root in force is for `jason status` to say rather than for a second reader of them here.
    # Single-quoted, so the backticks are text.
    $harnessRoot = [IO.Path]::Combine([Environment]::GetFolderPath('UserProfile'), 'Jason')
    Write-Host ('Jason keeps what it learns about your business in plain files you can read and edit, one folder per project and campaign, in `' + $harnessRoot + '` by default; to keep them somewhere else, set `Harness:Root` in the settings before your first campaign.')
    Write-Host 'Campaigns you create without naming a project go to the project `Default`; rename it, or make one project per product.'

    # --- the licence, what to run next, and the path to run it by ---------------------------------------------

    # The last three lines of a successful run, and never a question: the terms it is installed under, the one command
    # that says what this installation still needs, and the full path to run it by in a shell that was already open,
    # which reads no Path again: this one, and an agent's session that ran this script. The text is in single quotes
    # where it carries backticks, so they are text and not an escape.
    Write-Host "Jason $installed is licensed under the MIT licence: $licenceBase/v$installed/LICENSE"
    Write-Host 'Next, run `jason status`: it says what this installation still needs, and the command for each.'
    Write-Host "A terminal opened from now on finds jason by name; this one, and an agent's session already running, call it by its full path: $openShell"
}

# Removed again whatever happens: the one-liner is `irm ... | iex`, which runs this text in the session that
# typed it, and a function left behind there is this script still sitting in somebody's shell after it is done.
#
# And a failure is thrown on from here, whatever it was. An exception from a .NET call inside the function reached this
# level as an error the shell printed and went on from, and the run exited 0 as if it had installed. Thrown again, it
# ends the run with 1, as a throw or a failed command always did.
try {
    Install-Jason -Version $Version -Feed $Feed -InstallDir $InstallDir -NoModifyPath:$NoModifyPath
}
catch {
    throw
}
finally {
    Remove-Item function:Install-Jason -ErrorAction SilentlyContinue
}
