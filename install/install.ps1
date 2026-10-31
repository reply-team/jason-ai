<#
.SYNOPSIS
Installs Jason on Windows.

.DESCRIPTION
Downloads jason-win-x64.zip from the latest release, or from the release -Version names, checks it against
checksums.txt before unpacking, puts jason.exe in %LOCALAPPDATA%\Programs\jason with the licence and the
third-party notices beside it as jason-LICENSE and jason-THIRD-PARTY-NOTICES, and adds that directory to the
user's PATH unless -NoModifyPath says otherwise. Running it again over the same version downloads nothing and
says so, unless a licence file is missing beside it, which it then writes. Either way it says where Jason will keep
what it learns about your business - Jason in your home directory, unless Harness:Root in the settings names
another folder - and makes no folder for it.

    irm https://jasonharness.ai/releases/latest/download/install.ps1 | iex
    & ([scriptblock]::Create((irm https://jasonharness.ai/releases/download/v0.23.0/install.ps1))) -Version 0.23.0 -NoModifyPath

-Feed (or JASON_INSTALL_FEED) points it at a directory or a base URL holding the same files a release holds
- the archives, manifest.json and checksums.txt - which is how CI runs it against the archives it has just
built, with no network.

This is the Windows half. macOS and Linux are install.sh, which installs jason-linux-x64.tar.gz and
jason-osx-arm64.tar.gz. Written for Windows PowerShell 5.1 as well as PowerShell 7, because 5.1 is what a
fresh Windows has and what the one-liner is typed into.

.PARAMETER Version
The release to install, without a leading v. The latest, when not given.

.PARAMETER Feed
A directory or a base URL to read the release's files from, instead of the published releases. Also JASON_INSTALL_FEED.

.PARAMETER InstallDir
Where jason.exe and its licence files go. %LOCALAPPDATA%\Programs\jason, when not given.

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

    # The product's own address, in front of the releases: it redirects every path under /releases/ to the same path
    # in the public repository's releases on GitHub.
    $releasesUrl = 'https://jasonharness.ai/releases'
    # The licence is a page of the repository, at the release's tag; the downloads are a release's assets. Each has a
    # base of its own, so that the downloads can move without the licence page moving with them.
    $licenceBase = 'https://github.com/reply-team/jason-ai/blob'
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

    # A file another program holds open - a scanner reading the executable that was just run or just unpacked - can be
    # neither renamed nor removed until it lets go. Such a file is tried again for 10 seconds, every quarter of a second,
    # as jason itself does; the 10 seconds count from the first refusal, so an act slow in itself - an unpacking on a
    # loaded machine - does not spend them before the first pause. Held means a sharing or a lock violation and nothing
    # else, and any other refusal is thrown at once. A violation is read from the exception inside as well: Expand-Archive
    # reports one as the method call that failed. True once the act went through, false when the file was still held at
    # the end.
    function Held([scriptblock] $act) {
        $deadline = $null
        $violations = @(-2147024864, -2147024863)
        while ($true) {
            try {
                & $act
                return $true
            } catch {
                $inner = $_.Exception.InnerException
                if ($violations -notcontains $_.Exception.HResult -and -not ($inner -and $violations -contains $inner.HResult)) { throw }
                if ($null -eq $deadline) {
                    $deadline = [DateTime]::UtcNow.AddSeconds(10)
                } elseif ([DateTime]::UtcNow -ge $deadline) {
                    return $false
                }
                Start-Sleep -Milliseconds 250
            }
        }
    }

    # A path as a sentence says it to a person: from ~ where it is inside the home directory.
    function FromHome([string] $path) {
        $root = $HOME.TrimEnd('\')
        if ($path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { return '~' + $path.Substring($root.Length) }
        return $path
    }

    $held = 'is held open by another program; it was tried for 10 seconds.'

    # True for 0.6.0 and later, the pre-release part ignored: LICENSE-RUNTIME.md first exists at v0.6.0, and an earlier
    # release carried a notice of its own, which is not the Jason Runtime Licence.
    function Test-Licensed([string] $version) {
        $core = $null
        [version]::TryParse(($version -split '[-+]')[0], [ref] $core) -and $core -ge [version] '0.6.0'
    }

    # The archive, downloaded with checksums.txt and checked against it before Expand-Archive opens anything. An
    # archive that does not match goes with the temporary directory, and nothing is installed or written.
    function Get-CheckedArchive {
        Write-Host "Downloading Jason $latest for $rid from $base ..."
        Fetch 'checksums.txt' (Join-Path $tmp 'checksums.txt')
        $archive = Join-Path $tmp $asset
        Fetch $asset $archive

        $line = Get-Content -LiteralPath (Join-Path $tmp 'checksums.txt') | Where-Object { $_ -match "^[0-9a-f]{64}  $([regex]::Escape($asset))$" } | Select-Object -First 1
        if (-not $line) { throw "checksums.txt has no line for $asset." }
        $expected = ($line -split ' ')[0]
        $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) { throw "$asset does not match checksums.txt (sha256 $actual, expected $expected); nothing was installed." }
        return $archive
    }

    # The archive unpacked into the temporary directory. Expand-Archive refuses an archive that names a file twice or a
    # file outside the directory, but as an error the caller's preference does not reach: told to stop, it stops the
    # install before anything is installed. A file it has just unpacked can be held as the move's can - Windows
    # PowerShell's Expand-Archive closes each file and opens it again to set its time - so the unpacking is waited out
    # too, and what a refused try unpacked is cleared before the next, since Expand-Archive refuses a file that exists.
    function Expand-Checked([string] $archive) {
        $unpacked = Join-Path $tmp 'unpacked'
        $done = Held {
            if (Test-Path -LiteralPath $unpacked) { Remove-Item -LiteralPath $unpacked -Recurse -Force -ErrorAction Stop }
            Expand-Archive -LiteralPath $archive -DestinationPath $unpacked -ErrorAction Stop
        }
        if (-not $done) { throw "The downloaded archive could not be unpacked: '$(FromHome $unpacked)' or a file in it $held Nothing was installed: run this script again." }
        return $unpacked
    }

    # The two notices an archive carries beside the executable. Beside the installed executable each is jason-<name>.
    $notices = @('LICENSE', 'THIRD-PARTY-NOTICES')

    # Each notice the unpacked archive holds as a regular file of exactly its name at its top level; anything else under
    # that name - a path, a directory, another case of it - is not the release's notice. The names are read from the
    # directory, because a path asked for is answered in the case it was asked in.
    function Read-Notices([string] $unpacked) {
        $taken = @{}
        $entries = @(Get-ChildItem -LiteralPath $unpacked -Force)
        foreach ($name in $notices) {
            $item = $entries | Where-Object { $_.Name -ceq $name } | Select-Object -First 1
            if ($item -and -not $item.PSIsContainer -and -not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                $taken[$name] = $item.FullName
            }
        }
        return $taken
    }

    # Each notice beside the executable, by one rule per name: jason-<name> is written from the archive's file of that
    # name when the archive has it, and removed when it does not. Each is copied beside its final name, the old one is
    # removed, and the copy is renamed onto the name, after the executable is in place. A file another program holds is
    # waited for as the executable is: removing it is what waits, since a rename onto a held file is refused as a file
    # that exists. The reason one could not be written is returned, or nothing.
    function Write-Notices([hashtable] $taken) {
        foreach ($name in $notices) {
            $destination = Join-Path $InstallDir "jason-$name"
            $staging = $null
            if (Test-Path -LiteralPath $destination -PathType Container) { return "'$(FromHome $destination)' is a directory." }
            try {
                $written = $true
                if ($taken.ContainsKey($name)) {
                    $staging = "$destination.installing"
                    Copy-Item -LiteralPath $taken[$name] -Destination $staging -Force -ErrorAction Stop
                }
                if (Test-Path -LiteralPath $destination) { $written = Held { Remove-Item -LiteralPath $destination -Force -ErrorAction Stop } }
                if ($written -and $staging) { $written = Held { Move-Item -LiteralPath $staging -Destination $destination -ErrorAction Stop } }
                if (-not $written) { throw "'$(FromHome $destination)' $held" }
            } catch {
                if ($staging) { Remove-Item -LiteralPath $staging -Force -ErrorAction SilentlyContinue }
                return $_.Exception.Message.Trim().TrimEnd('.') + '.'
            }
        }
    }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) "jason-install-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $tmp | Out-Null
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

        $target = Join-Path $InstallDir 'jason.exe'
        $installed = $null
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            # What it prints carries build metadata (+<commit>), which a version comparison ignores.
            try { $installed = ((& $target --version) -split '\+')[0] } catch { $installed = $null }
        }

        if ($installed -eq $latest) {
            # The right version, without its notices: an installation an older jason updated, or one whose notices
            # were removed. A release before 0.6.0 never had THIRD-PARTY-NOTICES, so it has nothing to repair. The
            # archive is checked as an install's is; the notices are taken out of it, and the executable is not touched.
            $missing = @($notices | Where-Object { -not (Test-Path -LiteralPath (Join-Path $InstallDir "jason-$_") -PathType Leaf) })
            if ((Test-Licensed $latest) -and $missing.Count -gt 0) {
                $reason = Write-Notices (Read-Notices (Expand-Checked (Get-CheckedArchive)))
                if ($reason) { throw "Jason $latest is installed at $target, and its licence files could not be written beside it: $reason Run this script again." }
                Write-Host "Jason $latest is already installed at $target; its licence files were missing and are written beside it now."
            } else {
                Write-Host "Jason $latest is already installed at $target; nothing to download."
            }
        } else {
            # --- download, and check before unpacking -------------------------------------------------------

            $unpacked = Expand-Checked (Get-CheckedArchive)
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

            # The executable goes in first, and its notices after it. A running jason.exe cannot be overwritten but can
            # be renamed: the old one steps aside, the new one moves in, and the old one is removed once nothing runs it.
            New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
            $previous = Join-Path $InstallDir 'jason.previous.exe'

            # What an earlier install left is cleared first, and waited for while something holds it: moving onto it
            # would refuse a held file and a file a jason still runs from in the same words. Any other refusal - that
            # jason still running - is left to the step below, which reports it as it always has.
            $cleared = $true
            if (Test-Path -LiteralPath $previous) {
                try { $cleared = Held { Remove-Item -LiteralPath $previous -Force -ErrorAction Stop } } catch { }
            }
            if (-not $cleared) { throw "'$(FromHome $previous)' $held Nothing was installed: run this script again once it is free." }

            if (Test-Path -LiteralPath $target) {
                if (-not (Held { Move-Item -LiteralPath $target -Destination $previous -Force -ErrorAction Stop })) {
                    throw "'$(FromHome $target)' $held Nothing was installed: run this script again once it is free."
                }
            }

            # The new one moves in. Where it cannot, the old one goes back first, so that nothing was installed is
            # still true when the script says so.
            $fresh = Join-Path $unpacked 'jason.exe'
            $moved = $false
            try {
                $moved = Held { Move-Item -LiteralPath $fresh -Destination $target -ErrorAction Stop }
            } catch {
                $refusal = $_
                if (Test-Path -LiteralPath $previous) { try { [void](Held { Move-Item -LiteralPath $previous -Destination $target -ErrorAction Stop }) } catch { } }
                throw $refusal
            }
            if (-not $moved) {
                if (-not (Test-Path -LiteralPath $previous)) { throw "The downloaded jason.exe $held Nothing was installed: run this script again." }
                $back = $false
                try { $back = Held { Move-Item -LiteralPath $previous -Destination $target -ErrorAction Stop } } catch { }
                if ($back) { throw "The downloaded jason.exe $held The jason that was installed is back at '$(FromHome $target)'; nothing was installed: run this script again." }
                throw "The downloaded jason.exe $held The jason that was installed is at '$(FromHome $previous)': move it back to '$(FromHome $target)' by hand."
            }
            if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Force -ErrorAction SilentlyContinue }

            # The notices go beside it once it is in place. One that cannot be written leaves Jason installed without it,
            # and the run says so; run again, it takes the path above and writes them.
            $reason = Write-Notices (Read-Notices $unpacked)
            if ($reason) { throw "Jason $started is installed at $target, and its licence files could not be written beside it: $reason Run this script again." }

            Write-Host "Installed Jason $started to $target"
        }
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

    # --- the PATH ---------------------------------------------------------------------------------------------

    if (-not $NoModifyPath) {
        # The user's PATH lives in the registry, where a directory is its own mark: it is added when it is not there
        # and left alone when it is, so a second run changes nothing.
        #
        # Read as the registry stores it and written back with the kind it had. An ordinary account's Path is
        # REG_EXPAND_SZ and full of %USERPROFILE%, and the [Environment] pair this used to go through expanded every
        # entry on the way in and wrote the whole value back as REG_SZ. Running programs are told, which that setter
        # did for free, and so is this session.
        #
        # `jason status` prints these same statements, on one line, as its repair for a PATH that does not carry
        # Jason, and a test holds the two to each other line by line.
        $directory = $InstallDir.TrimEnd('\')
        & {
            $entry = $directory
            $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Environment')
            $stored = [string]$key.GetValue('Path', '', 'DoNotExpandEnvironmentNames')
            $kind = if ($key.GetValueNames() -contains 'Path') { $key.GetValueKind('Path') } else { 'ExpandString' }
            if (-not @($stored -split ';' | Where-Object { $(if ($kind -eq 'ExpandString') { [Environment]::ExpandEnvironmentVariables($_) } else { $_ }).TrimEnd('\') -ieq $entry })) { $key.SetValue('Path', ((@($stored -split ';' | Where-Object { $_ }) + $entry) -join ';'), $kind) }
            $key.Dispose()
            if (-not ('Jason.UserEnvironment' -as [type])) { Add-Type -Namespace Jason -Name UserEnvironment -MemberDefinition '[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);' }
            $answer = [UIntPtr]::Zero
            [void][Jason.UserEnvironment]::SendMessageTimeout([IntPtr]0xffff, 0x1a, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]$answer)
            if (-not @($env:Path -split ';' | Where-Object { $_.TrimEnd('\') -ieq $entry })) { $env:Path += ';' + $entry }
        }
        Write-Host "$directory is on your PATH; open a new terminal to run jason from anywhere."
    }

    # --- the licence, and what to run next ---------------------------------------------------------------------

    # The last two lines of a successful run, and never a question: what is installed and the terms it is installed
    # under, then the one command that says what this installation still needs. The text is in single quotes, so its
    # backticks are text and not an escape.
    if (Test-Licensed $latest) {
        Write-Host "Jason $latest is licensed under the Jason Runtime Licence: $licenceBase/v$latest/LICENSE-RUNTIME.md"
    }
    Write-Host 'Next, run `jason status`: it says what this installation still needs, and the command for each.'
}

# Removed again whatever happens: the one-liner is `irm ... | iex`, which runs this text in the session that
# typed it, and a function left behind there is this script still sitting in somebody's shell after it is done.
#
# And a failure is thrown on from here, whatever it was. An exception from a .NET call inside the function - a file
# Expand-Archive could not write, say - reached this level as an error the shell printed and went on from, and the run
# exited 0 as if it had installed. Thrown again, it ends the run with 1, as a throw or a failed command always did.
try {
    Install-Jason -Version $Version -Feed $Feed -InstallDir $InstallDir -NoModifyPath:$NoModifyPath
}
catch {
    throw
}
finally {
    Remove-Item function:Install-Jason -ErrorAction SilentlyContinue
}
