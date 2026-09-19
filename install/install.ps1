<#
.SYNOPSIS
Installs Jason on Windows.

.DESCRIPTION
Downloads jason-win-x64.zip from the latest release, or from the release -Version names, checks it against
checksums.txt before unpacking, puts jason.exe in %LOCALAPPDATA%\Programs\jason, and adds that directory to
the user's PATH unless -NoModifyPath says otherwise. Running it again over the same version downloads nothing
and says so.

    irm https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.ps1 | iex
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.ps1))) -Version 0.2.0 -NoModifyPath

-Feed (or JASON_INSTALL_FEED) points it at a directory or a base URL holding the same files a release holds
- the archives, manifest.json and checksums.txt - which is how CI runs it against the archives it has just
built, with no network.

This is the Windows half. macOS and Linux are install.sh, which installs jason-linux-x64.tar.gz and
jason-osx-arm64.tar.gz. Written for Windows PowerShell 5.1 as well as PowerShell 7, because 5.1 is what a
fresh Windows has and what the one-liner is typed into.

.PARAMETER Version
The release to install, without a leading v. The latest, when not given.

.PARAMETER Feed
A directory or a base URL to read the release's files from, instead of GitHub. Also JASON_INSTALL_FEED.

.PARAMETER InstallDir
Where jason.exe goes. %LOCALAPPDATA%\Programs\jason, when not given.

.PARAMETER NoModifyPath
Leave the user's PATH alone.
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

    $repositoryUrl = 'https://github.com/reply-team/jason-ai'
    $rid = 'win-x64'
    $asset = 'jason-win-x64.zip'

    if (-not $Feed) { $Feed = $env:JASON_INSTALL_FEED }
    if (-not $InstallDir) { $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\jason' }

    # --- this machine -----------------------------------------------------------------------------------------

    # PowerShell 7 runs on macOS and Linux too, and there this is the wrong script.
    if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) {
        throw "This is the Windows installer. On macOS and Linux run install.sh, which installs jason-linux-x64.tar.gz or jason-osx-arm64.tar.gz."
    }
    # PROCESSOR_ARCHITECTURE says what this shell is, not what the machine is: a 32-bit PowerShell on 64-bit
    # Windows - which is what a scheduled task, an installer's "run this afterwards" or an old shortcut still
    # gives people - reads x86 there, and Windows puts the machine's own architecture in PROCESSOR_ARCHITEW6432
    # for exactly that shell. Reading only the first refuses to install on an ordinary x64 machine.
    $architecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    if ($architecture -ne 'AMD64') {
        throw "No release is built for Windows on $architecture; the releases are win-x64, linux-x64 and osx-arm64."
    }

    # --- the feed ---------------------------------------------------------------------------------------------

    $base = if ($Feed) { $Feed } elseif ($Version) { "$repositoryUrl/releases/download/v$Version" } else { "$repositoryUrl/releases/latest/download" }
    $fromWeb = $base -match '^https?://'

    if ($fromWeb -and $PSVersionTable.PSVersion.Major -lt 6) {
        # Windows PowerShell's defaults are older than GitHub accepts, and its progress bar makes a download slow.
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

    $tmp = Join-Path ([IO.Path]::GetTempPath()) "jason-install-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        # --- the version, and whether it is already here ------------------------------------------------------

        Fetch 'manifest.json' (Join-Path $tmp 'manifest.json')
        $latest = (Get-Content -LiteralPath (Join-Path $tmp 'manifest.json') -Raw | ConvertFrom-Json).version
        if (-not $latest) { throw "$base/manifest.json does not name a version." }

        $target = Join-Path $InstallDir 'jason.exe'
        $installed = $null
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            # What it prints carries build metadata (+<commit>), which a version comparison ignores.
            try { $installed = ((& $target --version) -split '\+')[0] } catch { $installed = $null }
        }

        if ($installed -eq $latest) {
            Write-Host "Jason $latest is already installed at $target; nothing to download."
        } else {
            # --- download, and check before unpacking -------------------------------------------------------

            Write-Host "Downloading Jason $latest for $rid from $base ..."
            Fetch 'checksums.txt' (Join-Path $tmp 'checksums.txt')
            $archive = Join-Path $tmp $asset
            Fetch $asset $archive

            # The digest from checksums.txt, compared before Expand-Archive opens anything. An archive that does
            # not match goes with the temporary directory, and nothing is installed.
            $line = Get-Content -LiteralPath (Join-Path $tmp 'checksums.txt') | Where-Object { $_ -match "^[0-9a-f]{64}  $([regex]::Escape($asset))$" } | Select-Object -First 1
            if (-not $line) { throw "checksums.txt has no line for $asset." }
            $expected = ($line -split ' ')[0]
            $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -ne $expected) { throw "$asset does not match checksums.txt (sha256 $actual, expected $expected); nothing was installed." }

            $unpacked = Join-Path $tmp 'unpacked'
            Expand-Archive -LiteralPath $archive -DestinationPath $unpacked
            # Expand-Archive hands the download's mark of the web on to what it unpacks, and an executable carrying
            # it is stopped by SmartScreen the first time it runs.
            Unblock-File -LiteralPath (Join-Path $unpacked 'jason.exe')

            # --- start it once, then install ----------------------------------------------------------------

            # The checked, unpacked executable is asked for its version before it goes anywhere near the install
            # directory: a program that does not start here is not installed over one that did. What it prints
            # must be the manifest's version.
            $started = & (Join-Path $unpacked 'jason.exe') --version
            if ($LASTEXITCODE -ne 0 -or -not $started) { throw "The downloaded jason.exe does not start on this machine; nothing was installed." }
            $started = ($started -split '\+')[0]
            if ($started -ne $latest) { throw "The downloaded jason.exe says it is $started and the manifest says $latest; nothing was installed." }

            # Only the executable is installed; LICENSE stays in the archive, and the install directory holds
            # nothing but the program. A running jason.exe cannot be overwritten but can be renamed: the old one
            # steps aside, the new one moves in, and the old one is removed once nothing runs it.
            New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
            $previous = Join-Path $InstallDir 'jason.previous.exe'
            if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Force -ErrorAction SilentlyContinue }
            if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $previous -Force }
            Move-Item -LiteralPath (Join-Path $unpacked 'jason.exe') -Destination $target
            if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Force -ErrorAction SilentlyContinue }

            Write-Host "Installed Jason $started to $target"
        }
    } finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }

    # --- the PATH ---------------------------------------------------------------------------------------------

    if ($NoModifyPath) { return }

    # The user's PATH lives in the registry, where a directory is its own mark: it is added when it is not there
    # and left alone when it is, so a second run changes nothing. The running session learns it too.
    $directory = $InstallDir.TrimEnd('\')
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $entries = @(if ($userPath) { $userPath -split ';' | Where-Object { $_ } })
    if (@($entries | Where-Object { $_.TrimEnd('\') -ieq $directory }).Count -gt 0) { return }

    [Environment]::SetEnvironmentVariable('Path', (($entries + $directory) -join ';'), 'User')
    if (@($env:Path -split ';' | Where-Object { $_.TrimEnd('\') -ieq $directory }).Count -eq 0) { $env:Path = "$env:Path;$directory" }
    Write-Host "Added $directory to your PATH; open a new terminal to run jason from anywhere."
}

Install-Jason -Version $Version -Feed $Feed -InstallDir $InstallDir -NoModifyPath:$NoModifyPath
