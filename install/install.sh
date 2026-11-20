#!/bin/sh
# Installs Jason on macOS and Linux.
#
# On macOS it installs Jason Desktop, with jason in the terminal: the application's zip for Apple silicon from the latest
# release, or from the one --version names, checked against checksums.txt, unpacked with ditto into a staging folder
# beside its place, its signature verified as a Developer ID signature of the team that signs Jason, and moved into place
# by one rename - /Applications where this account may write there, else ~/Applications. Nothing is opened, and no
# password is asked. An Intel Mac is refused before anything is downloaded: this script installs Jason Desktop on Apple
# silicon only. So is a run as root, with sudo: the application and what it sets up are the account's own.
# A Jason.app already in either folder is never renamed, replaced or deleted: one of 0.49.0 or later is kept as it is,
# once its signature verifies the same way, and an earlier one is left for its own Check for Updates, and the script ends
# non-zero saying so. Then the jason the application carries sets the machine up, `jason install --roles`:
# the wrapper ~/.local/bin/jason and the PATH lines in the profiles, the skills for Claude Code and Codex where each is found, an older
# command-line install retired, and the role skills in Jason's data directory. Where a runtime answered before and none
# answers after, or the report says it stopped one, a runtime is started again from the application's jason. It ends
# non-zero, saying which act failed, when jason was not put in the terminal; on success its last line is the full path
# of jason, for the shell it ran in and an agent's session already running, which read no profile again.
#
# On Linux it installs Jason Desktop as each system's users install comparable applications, and opens nothing. The
# package manager the system has decides. Where there is apt (Ubuntu, Debian, Mint): the release's deb, checked against
# checksums.txt and installed by apt, with the one password a system package needs - asked by sudo where this line has a
# terminal (whether /dev/tty opens), else by the desktop's own dialog through pkexec, for at most twenty minutes - and refused
# in its own words where none can be asked or given; the package adds Jason's apt repository, its key and /usr/bin/jason,
# and every later update comes with the system's. Anywhere else (Arch, Fedora and the rest): the AppImage, per account,
# with no password, placed at ~/Applications/jason-desktop-linux-x64.AppImage by one rename, its menu entry and icons
# written under ~/.local/share, and the jason it carries copied under the data directory; refused before anything is
# downloaded where the system does not let it run its sandbox (no unprivileged user namespaces) or open (no setuid
# fusermount). An AppImage already in that place stays the installation, and so does a package already installed at
# this version or a later one. Inside an agent's sandbox (NoNewPrivs), run as root, or while Jason Desktop runs and
# would be changed, it refuses before anything is changed. Then the jason the application carries sets the machine up,
# as on macOS, and the last line is the full path of jason.
#
# Either way it says where Jason will keep what it learns about your business — Jason in your home directory, unless
# Harness:Root in the settings names another folder — and makes no folder for it.
#
#   curl -fsSL https://get.jasonharness.ai/releases/latest/download/install.sh | sh
#   curl -fsSL https://get.jasonharness.ai/releases/download/v0.49.0/install.sh | sh -s -- --version 0.49.0
#
# JASON_INSTALL_FEED (or --feed) points it at a directory or a base URL holding the same files a release holds
# — the archives, the application's zips, manifest.json and checksums.txt — which is how CI runs it against what it
# has just built, with no network. Written for sh rather than bash: the one-liner pipes into sh, and on Debian that is
# dash.
set -eu

# The product's own address, in front of the releases: it redirects every path under /releases/ to the same path in
# the public repository's releases on GitHub.
RELEASES_URL="https://get.jasonharness.ai/releases"
# The licence is a page of the repository, at the release's tag; the downloads are a release's assets. Each has a base
# of its own, so that the downloads can move without the licence page moving with them.
LICENCE_BASE="https://github.com/reply-team/jason-ai/blob"
INSTALL_DIR="$HOME/.local/bin"
FEED="${JASON_INSTALL_FEED:-}"
VERSION=""
MODIFY_PATH=1
INSTALL_DIR_GIVEN=""

usage() {
    cat <<EOF
Usage: install.sh [--version X.Y.Z] [--feed DIRECTORY-OR-URL]

  --version X.Y.Z        install that release rather than the latest (on macOS, 0.49.0 or later; on Linux, 0.49.1 or later)
  --feed DIRECTORY|URL   read the release's files from there instead of the published releases (also JASON_INSTALL_FEED)

On macOS it installs Jason Desktop into /Applications, or ~/Applications where this account may not write to
/Applications, opens nothing, and puts jason in the terminal through the application's own jason install. A Jason.app
already in either folder is kept as it is, never replaced; one before 0.49.0 is updated from its own menu first.
This script installs Jason Desktop on Apple silicon Macs only: on an Intel Mac it stops before it downloads anything.
On a Mac this script is the way to install Jason; Homebrew's formula is the command line
alone, without Jason Desktop.

On Linux it installs Jason Desktop and opens nothing: with apt, the release's deb, installed by apt with your password
(sudo in a terminal, your desktop's own dialog otherwise), which adds Jason's apt repository so updates come with the
system's; without apt, the AppImage in ~/Applications, for your account alone, with no password.
EOF
}

fail() {
    printf 'install.sh: %s\n' "$1" >&2
    exit 1
}

while [ $# -gt 0 ]; do
    case "$1" in
        --version) [ $# -ge 2 ] || fail "--version needs a value"; VERSION="$2"; shift ;;
        --version=*) VERSION="${1#--version=}" ;;
        --feed) [ $# -ge 2 ] || fail "--feed needs a value"; FEED="$2"; shift ;;
        --feed=*) FEED="${1#--feed=}" ;;
        --install-dir) [ $# -ge 2 ] || fail "--install-dir needs a value"; INSTALL_DIR="$2"; INSTALL_DIR_GIVEN=1; shift ;;
        --install-dir=*) INSTALL_DIR="${1#--install-dir=}"; INSTALL_DIR_GIVEN=1 ;;
        --no-modify-path) MODIFY_PATH=0 ;;
        -h|--help) usage; exit 0 ;;
        *) usage >&2; fail "unknown option: $1" ;;
    esac
    shift
done

# --- this machine -------------------------------------------------------------------------------------------

# Git Bash answers MINGW64_NT-… here and MSYS answers MSYS_NT-…. Neither is a Linux, and a Linux binary is the
# one thing this script must never put on Windows; the Windows release is install.ps1's.
case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) fail "this is a Windows shell; run install.ps1 in PowerShell instead, which installs Jason Desktop for Windows" ;;
esac

# Both install Jason Desktop: a Mac's architecture is read further down, and Linux is x64 alone.
case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) RID="linux-x64"; ASSET="" ;;
    Darwin-*) RID="macos"; ASSET="" ;;
    *) fail "no release is built for $(uname -s) on $(uname -m); the releases are linux-x64, osx-arm64 and win-x64, and Jason Desktop for macOS" ;;
esac

# Refused before anything is read or written: neither can be done the way they ask any more.
if [ "$RID" = macos ]; then
    [ -z "$INSTALL_DIR_GIVEN" ] || fail "--install-dir is not taken on macOS: Jason Desktop goes to /Applications or ~/Applications, and the jason it carries is the one the terminal runs. Nothing was installed: run this again without it."
    [ "$MODIFY_PATH" -eq 1 ] || fail "--no-modify-path is not taken on macOS: every install puts jason in the terminal. Nothing was installed: run this again without it."
else
    [ -z "$INSTALL_DIR_GIVEN" ] || fail "--install-dir is not taken on Linux: Jason Desktop is installed as the system installs applications, and the jason it carries is the one the terminal runs. Nothing was installed: run this again without it."
    [ "$MODIFY_PATH" -eq 1 ] || fail "--no-modify-path is not taken on Linux: every install puts jason in the terminal. Nothing was installed: run this again without it."
fi
# Root - the line piped into `sudo sh` - would place the application owned by root, which its own updater then cannot
# replace without a password, and through a home sudo keeps, a data directory, a wrapper and skills the account's own
# runtime cannot write. So it stops before anything is downloaded; on Linux it asks for the password itself where the
# package needs one.
[ "$(id -u)" != 0 ] || fail "this script runs as root, and Jason Desktop and everything it sets up are your account's own, so nothing was downloaded or installed: run the install line again as yourself, without sudo."

# --- the feed -----------------------------------------------------------------------------------------------

if [ -n "$FEED" ]; then
    BASE="$FEED"
elif [ -n "$VERSION" ]; then
    BASE="$RELEASES_URL/download/v$VERSION"
else
    BASE="$RELEASES_URL/latest/download"
fi

# One file from the feed into the temporary directory: over the web with curl or wget, or a copy from a directory.
fetch() {
    case "$BASE" in
        http://*|https://*)
            if command -v curl >/dev/null 2>&1; then
                curl -fsSL "$BASE/$1" -o "$2" || fail "could not download $BASE/$1"
            elif command -v wget >/dev/null 2>&1; then
                wget -q "$BASE/$1" -O "$2" || fail "could not download $BASE/$1"
            else
                fail "neither curl nor wget is on this machine"
            fi
            ;;
        *)
            [ -f "$BASE/$1" ] || fail "$BASE/$1 does not exist"
            cp "$BASE/$1" "$2"
            ;;
    esac
}

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# The archive, downloaded with checksums.txt and checked against it before anything opens it. An archive that does
# not match goes with the temporary directory, and nothing is installed or written.
download_archive() {
    echo "Downloading Jason $LATEST for $RID from $BASE ..."
    fetch checksums.txt "$TMP/checksums.txt"
    fetch "$ASSET" "$TMP/$ASSET"

    # The digest from checksums.txt, computed here with whichever tool this machine has.
    EXPECTED="$(grep "  $ASSET\$" "$TMP/checksums.txt" | cut -d' ' -f1)"
    [ -n "$EXPECTED" ] || fail "checksums.txt has no line for $ASSET"
    if command -v sha256sum >/dev/null 2>&1; then
        ACTUAL="$(sha256sum "$TMP/$ASSET" | cut -d' ' -f1)"
    elif command -v shasum >/dev/null 2>&1; then
        ACTUAL="$(shasum -a 256 "$TMP/$ASSET" | cut -d' ' -f1)"
    else
        fail "neither sha256sum nor shasum is on this machine, so the download cannot be checked"
    fi
    [ "$ACTUAL" = "$EXPECTED" ] || fail "$ASSET does not match checksums.txt (sha256 $ACTUAL, expected $EXPECTED); nothing was installed"
}

# True when the version in $2 is 0.$1.0 or later, its pre-release part ignored. Field by field, since sort -V is not on
# macOS; a field that is not a number is no such version.
from_minor() {
    CORE="${2%%[-+]*}"
    MAJOR="$(echo "$CORE" | cut -d. -f1)"
    MINOR="$(echo "$CORE" | cut -d. -f2)"
    for FIELD in "$MAJOR" "$MINOR"; do
        case "$FIELD" in
            ''|*[!0-9]*) return 1 ;;
        esac
    done
    [ "$MAJOR" -gt 0 ] || [ "$MINOR" -ge "$1" ]
}

# --- macOS: Jason Desktop -----------------------------------------------------------------------------------------

# The team whose Developer ID signs every application a release publishes, as codesign names it: the signature, and not
# the checksum, says who made it, and a notarized signature of any other team would verify as well.
APP_TEAM="BD8M7U5YBV"
# The requirement an application of that team's satisfies, which codesign --verify -R tests: a chain to Apple's own root,
# an intermediate that is Apple's Developer ID certification authority, a leaf that is a Developer ID Application
# certificate, and that leaf's team. Without it codesign checks a bundle only against the requirement its own signature
# carries, and the team that signature names is a claim any chain can make. Kept on one line.
APP_REQUIREMENT='anchor apple generic and certificate 1[field.1.2.840.113635.100.6.2.6] and certificate leaf[field.1.2.840.113635.100.6.1.13] and certificate leaf[subject.OU] = "BD8M7U5YBV"'
# The folder every account's applications share, used where this account may write there; else the account's own.
SYSTEM_APPLICATIONS="/Applications"

# The version a placed application carries, from the version.json beside its jason; nothing where there is none.
app_version() {
    sed -n 's/.*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$1/Contents/Resources/bundle/version.json" 2>/dev/null | head -n 1
}

# True when the version in $1 is earlier than the one in $2, their pre-release parts ignored, field by field; false
# where either is not three numbers.
older_than() {
    A="${1%%[-+]*}"
    B="${2%%[-+]*}"
    for I in 1 2 3; do
        X="$(echo "$A" | cut -d. -f$I)"
        Y="$(echo "$B" | cut -d. -f$I)"
        case "$X" in ''|*[!0-9]*) return 1 ;; esac
        case "$Y" in ''|*[!0-9]*) return 1 ;; esac
        [ "$X" -lt "$Y" ] && return 0
        [ "$X" -gt "$Y" ] && return 1
    done
    return 1
}

# Whether a runtime answers on this installation's data directory, asked of the jason that may have started it.
answers() {
    [ -n "$1" ] && [ -f "$1" ] && "$1" runtime status >/dev/null 2>&1
}

# A runtime started again from the application's jason where it is in place, else from the command-line install's:
# whichever is whole at this step, on success and on failure alike. What it says is shown.
start_again() {
    FROM=""
    if [ -f "$CARRIED" ]; then FROM="$CARRIED"; elif [ -n "$COMMAND_LINE" ] && [ -f "$COMMAND_LINE" ]; then FROM="$COMMAND_LINE"; fi
    [ -n "$FROM" ] || return 0
    echo "A runtime answered before the install and none answers now, so one is started again from $FROM."
    "$FROM" runtime start || echo "The runtime did not start; the lines above say why. Run $FROM runtime start once that is dealt with."
}

# True when the application at $1 is signed as Jason's: its integrity, and the requirement above. What codesign says
# goes to stderr.
signed_as_jason() {
    codesign --verify --deep --strict -R="$APP_REQUIREMENT" "$1"
}

install_desktop() {
    # Stopped by a signal - an agent that times the script out, say - it still goes through its exit, which removes what
    # it downloaded and the staging folder beside the application's place.
    trap 'exit 129' HUP
    trap 'exit 130' INT
    trap 'exit 143' TERM
    # The architecture of the machine, not of this shell: under Rosetta uname answers x86_64 on Apple silicon.
    if [ "$(sysctl -n hw.optional.arm64 2>/dev/null || true)" = 1 ]; then ARCH="arm64"; else ARCH="x64"; fi
    # This script installs Jason Desktop on Apple silicon only, and this line is where an Intel Mac is refused.
    [ "$ARCH" = arm64 ] || fail "This script installs Jason Desktop on Apple silicon Macs only, and this is an Intel Mac; nothing was installed."
    ASSET="jason-desktop-macos-$ARCH.zip"
    ANSWERED=""
    CARRIED=""
    COMMAND_LINE=""

    fetch manifest.json "$TMP/manifest.json"
    # The manifest is machine-written, one field per line; this is the value on its "version" line.
    LATEST="$(sed -n 's/^[[:space:]]*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$TMP/manifest.json" | head -n 1)"
    [ -n "$LATEST" ] || fail "$BASE/manifest.json does not name a version"
    if [ -n "$VERSION" ] && [ "$LATEST" != "$VERSION" ]; then
        fail "--version asked for $VERSION and $BASE/manifest.json names $LATEST; nothing was installed"
    fi
    # A release before 0.49.0 carries a jason that cannot set the machine up; its own script installs it.
    from_minor 49 "$LATEST" || fail "Jason $LATEST is installed by its own release's script, not this one, which installs Jason Desktop 0.49.0 and later; nothing was installed"

    # The place: an application already in either folder, this account's own first; else, for a new one, the shared
    # folder where this account may write to it, as the application's updater needs or every update would ask for a
    # password; else the account's own folder.
    if [ -e "$HOME/Applications/Jason.app" ]; then
        PLACE="$HOME/Applications"
    elif [ -e "$SYSTEM_APPLICATIONS/Jason.app" ]; then
        PLACE="$SYSTEM_APPLICATIONS"
    elif [ -d "$SYSTEM_APPLICATIONS" ] && [ -w "$SYSTEM_APPLICATIONS" ]; then
        PLACE="$SYSTEM_APPLICATIONS"
    else
        PLACE="$HOME/Applications"
    fi
    DEST="$PLACE/Jason.app"
    CARRIED="$DEST/Contents/Resources/bundle/jason"
    # A command-line install of an earlier release, in its installer's place, and only where its notices say it is one: a
    # runtime may run from it, and the setup below retires it.
    if [ -f "$HOME/.local/bin/jason" ] && { [ -f "$HOME/.local/bin/jason-LICENSE" ] || [ -f "$HOME/.local/bin/jason-THIRD-PARTY-NOTICES" ]; }; then
        COMMAND_LINE="$HOME/.local/bin/jason"
    fi

    INSTALLED="$LATEST"
    BEHIND=""
    KEPT=""
    if [ -e "$DEST" ]; then
        # An application already there is never renamed, replaced or deleted from here: its own updater moves it, which no
        # terminal's rename of a signed application stands in for. One of 0.49.0 or later carries the routine and the
        # update, so it is kept and its routine run, once its signature verifies as a new one's does: its jason is run
        # only then. An earlier one cannot be set up from a terminal at all.
        PLACED_VERSION="$(app_version "$DEST")"
        if [ -n "$PLACED_VERSION" ] && from_minor 49 "$PLACED_VERSION" && [ -f "$CARRIED" ]; then
            signed_as_jason "$DEST" || fail "the Jason.app at $DEST does not verify as signed by $APP_TEAM, the team that signs Jason (said above), so its jason was not run; nothing was changed. Move $DEST to the Trash, then run the install line again: it downloads Jason Desktop and puts it there."
            KEPT=1
            if [ "$PLACED_VERSION" = "$LATEST" ]; then
                echo "Jason Desktop $LATEST is already installed at $DEST; nothing to download."
            else
                INSTALLED="$PLACED_VERSION"
                echo "Jason Desktop $PLACED_VERSION is already installed at $DEST; it is kept as it is, and nothing is downloaded."
                if older_than "$PLACED_VERSION" "$LATEST"; then BEHIND=1; fi
            fi
        else
            fail "Jason Desktop ${PLACED_VERSION:-of an unknown version} at $DEST is older than 0.49.0 and cannot be set up from a terminal; nothing was changed. Update Jason Desktop from its own menu (Check for Updates), then run the install line again."
        fi
    fi
    # Asked before anything is placed, and of no application's jason but one kept above.
    if answers "$CARRIED" || answers "$COMMAND_LINE"; then ANSWERED=1; fi

    if [ -z "$KEPT" ]; then
        # The download says "Downloading Jason <version> for <this machine> from <the feed>".
        RID="macOS on $ARCH"
        download_archive

        # Unpacked beside its place, so that putting it there is one rename on one volume: no moment at which a
        # half-copied application is where the system finds it. ditto keeps the links, the signature and its ticket.
        mkdir -p "$PLACE" || fail "$PLACE could not be made; nothing was installed"
        STAGE="$(mktemp -d "$PLACE/.jason-install.XXXXXX")" || fail "no folder could be made in $PLACE to unpack into; nothing was installed"
        trap 'rm -rf "$TMP" "$STAGE"' EXIT
        ditto -x -k "$TMP/$ASSET" "$STAGE" || fail "$ASSET could not be unpacked into $STAGE; nothing was installed"
        [ -d "$STAGE/Jason.app" ] || fail "$ASSET holds no Jason.app; nothing was installed"
        if ! signed_as_jason "$STAGE/Jason.app"; then
            # Which team the signature names, read only to say it in words: the requirement above is the check.
            TEAM="$(codesign --display --verbose=2 "$STAGE/Jason.app" 2>&1 | sed -n 's/^TeamIdentifier=//p')"
            [ "$TEAM" = "$APP_TEAM" ] || fail "the Jason.app in $ASSET is signed by the team '$TEAM', not by $APP_TEAM, the team that signs Jason; nothing was installed"
            fail "the signature of the Jason.app in $ASSET does not verify as a Developer ID signature of $APP_TEAM, the team that signs Jason (said above); nothing was installed"
        fi
        [ "$(app_version "$STAGE/Jason.app")" = "$LATEST" ] || fail "the Jason.app in $ASSET is not Jason $LATEST; nothing was installed"
        [ ! -e "$DEST" ] || fail "$DEST appeared while this ran; nothing was installed"
        mv "$STAGE/Jason.app" "$DEST" || fail "$DEST could not be put in place; nothing was installed"
        # The check and the rename are two steps: a Jason.app that appeared between them would have taken this one inside
        # it. It is moved back out, and that one is left as it was.
        if [ ! -e "$STAGE/Jason.app" ] && [ -e "$DEST/Jason.app" ]; then
            mv "$DEST/Jason.app" "$STAGE/Jason.app" 2>/dev/null || true
            fail "$DEST appeared while this ran and is left as it was; nothing was installed"
        fi
        [ -f "$CARRIED" ] && [ "$(app_version "$DEST")" = "$LATEST" ] || fail "$DEST is not Jason $LATEST once it was put in place, so jason was not put in the terminal"
        rm -rf "$STAGE"
        echo "Installed Jason Desktop $LATEST to $DEST"
    fi

    set_up_and_end
}

# The machine set up by the jason the placed application carries, $CARRIED, and the last lines: the same on macOS and
# Linux. $DEST is where the application is, for the sentences; $INSTALLED, $LATEST and $BEHIND what was placed or kept.
set_up_and_end() {
    # The machine set up: the application's own jason, with the role skills, and its one report read. What it says on
    # stderr goes straight through; its report is shown as it came.
    REPORT="$("$CARRIED" install --roles)" || true
    printf '%s\n' "$REPORT"
    case "$REPORT" in
        *'"runtime":"stopped"'*) start_again ;;
        *) if [ -n "$ANSWERED" ] && ! answers "$CARRIED"; then start_again; fi ;;
    esac
    ANSWERED=""
    case "$REPORT" in
        '{"error":'*) fail "jason install refused, as its report above says, so jason was not put in the terminal. Jason Desktop is installed at $DEST" ;;
        *'"placed":true'*) ;;
        '{'*) fail "jason install did not put jason in the terminal; its problems above say why. Jason Desktop is installed at $DEST" ;;
        *) fail "jason install printed no report, so whether jason is in the terminal is not known. Jason Desktop is installed at $DEST" ;;
    esac
    case "$REPORT" in
        *'"completed":true'*) ;;
        *) echo 'Jason is installed and in the terminal; `jason install` named above what else it did not get done, and `jason status` names it too.' ;;
    esac
    OPEN_SHELL="$(printf '%s' "$REPORT" | sed -n 's/.*"open_shell":"\([^"\\]*\)".*/\1/p' | head -n 1)"
    [ -n "$OPEN_SHELL" ] || OPEN_SHELL="$HOME/.local/bin/jason"
    # Whether a terminal opened from now on runs this jason for the name, as jason install decided it once everything was
    # placed (finds_this), and the file it finds (finds): another file earlier on the PATH is named in the report's problems
    # with what removes it. No path is compared here. A report without the member is one from before it was read, and
    # says nothing either way.
    case "$REPORT" in
        *'"finds_this":false'*)
            FINDS="$(printf '%s' "$REPORT" | sed -n 's/.*"finds":"\([^"\\]*\)".*/\1/p' | head -n 1)"
            fail "jason install put jason in the terminal, but a terminal opened from now on finds ${FINDS:-no jason} first by that name; its problems above say what to do. Jason Desktop is installed at $DEST" ;;
    esac

    # Where the files will be, the licence, what to run next, and the path to run it by: the last lines of a successful
    # run, and never a question. A shell that was already open reads no profile again: this one, and an agent's session
    # that ran this script.
    ROOT_HOME="$HOME"
    while [ "${ROOT_HOME%/}" != "$ROOT_HOME" ]; do
        ROOT_HOME="${ROOT_HOME%/}"
    done
    printf 'Jason keeps what it learns about your business in plain files you can read and edit, one folder per project and campaign, in `%s` by default; to keep them somewhere else, set `Harness:Root` in the settings before your first campaign.\n' "$ROOT_HOME/Jason"
    printf '%s\n' 'Campaigns you create without naming a project go to the project `Default`; rename it, or make one project per product.'
    echo "Jason $INSTALLED is licensed under the MIT licence: $LICENCE_BASE/v$INSTALLED/LICENSE"
    echo 'Next, run `jason status`: it says what this installation still needs, and the command for each.'
    # An application kept at an older version than the release asked for is named on the same last line, with the
    # command that updates it: the line still ends with the full path.
    if [ -n "$BEHIND" ]; then
        echo "Jason Desktop $INSTALLED is kept, older than $LATEST: \`$OPEN_SHELL update apply\` updates it. A terminal opened from now on finds jason by name; this one, and an agent's session already running, call it by its full path: $OPEN_SHELL"
    else
        echo "A terminal opened from now on finds jason by name; this one, and an agent's session already running, call it by its full path: $OPEN_SHELL"
    fi
}

if [ "$RID" = macos ]; then
    install_desktop
    exit 0
fi

# --- Linux: Jason Desktop -----------------------------------------------------------------------------------------

# The deb and the AppImage a release publishes, and where an AppImage goes: ~/Applications, where AppImage tools look.
DEB_ASSET="jason-desktop-linux-x64.deb"
# Where the deb puts the application, and so the jason it carries.
DEB_PLACE="/opt/Jason"
APPIMAGE_ASSET="jason-desktop-linux-x64.AppImage"
APPIMAGE_PLACE="$HOME/Applications"
# How long apt waits for the package lock another installation holds.
LOCK_SECONDS=600

# How long an install through the system's password dialog is given: apt's wait for the lock and ten minutes more, for
# the password and apt's own work, so that apt answers first.
ASK_SECONDS=1200

# Whether Jason Desktop runs under this account: a process whose program, the first word of its command line, is
# jason-desktop-app, the deb's or an AppImage's mount's. Where pgrep is not there, nothing is known to run.
desktop_running() {
    command -v pgrep >/dev/null 2>&1 && pgrep -u "$(id -u)" -f '^[^ ]*/jason-desktop-app( |$)' >/dev/null 2>&1
}

# Nothing that would change the application is done while it runs: a person's application is never closed from here.
refuse_while_running() {
    if desktop_running; then
        fail "Jason Desktop is running, in its window or in the tray, so nothing was installed: quit it - Quit in its tray menu - and run this line again."
    fi
}

# The apt options every install of the package takes: the package lock waited for, a configuration file the person
# changed kept, and the person's proxy, which sudo and pkexec both drop.
apt_options() {
    APT_OPTIONS="-y -o DPkg::Lock::Timeout=$LOCK_SECONDS -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold"
    [ -z "${http_proxy:-}" ] || APT_OPTIONS="$APT_OPTIONS -o Acquire::http::Proxy=$http_proxy"
    [ -z "${https_proxy:-}" ] || APT_OPTIONS="$APT_OPTIONS -o Acquire::https::Proxy=$https_proxy"
}

# Whether this account is one the system's dialog asks its own password of: an administrator, by Ubuntu's rule.
administrator() {
    id -nG 2>/dev/null | tr ' ' '\n' | grep -qxE 'sudo|admin|wheel'
}

# The package installed by apt from the checked file, with the one password an install of a system package needs, asked
# the way the moment allows: by sudo where this line has a terminal (a person typed it; `curl | sh` gives sh a pipe, so the
# test is whether /dev/tty opens), else by the desktop's own dialog through pkexec (an agent runs it in the person's
# session). Which refusal it is is read from their own words, never from an exit code alone.
apt_install() {
    FILE="$1"
    apt_options
    APT_GET="$(command -v apt-get)"
    : > "$TMP/apt.err"
    STATUS=0
    ROUTE=none
    if (: < /dev/tty) 2>/dev/null && command -v sudo >/dev/null 2>&1; then
        ROUTE=sudo
        echo "Installing Jason Desktop's package with apt; sudo asks for your password."
        # shellcheck disable=SC2086
        sudo "$APT_GET" install $APT_OPTIONS "$FILE" 2> "$TMP/apt.err" || STATUS=$?
    elif command -v pkexec >/dev/null 2>&1; then
        ROUTE=pkexec
        echo "Installing Jason Desktop's package with apt; your desktop asks for your password in its own window."
        if command -v timeout >/dev/null 2>&1; then
            # shellcheck disable=SC2086
            timeout "$ASK_SECONDS" pkexec "$APT_GET" install $APT_OPTIONS "$FILE" 2> "$TMP/apt.err" || STATUS=$?
        else
            # shellcheck disable=SC2086
            pkexec "$APT_GET" install $APT_OPTIONS "$FILE" 2> "$TMP/apt.err" || STATUS=$?
        fi
    else
        STATUS=127
    fi
    [ "$STATUS" -eq 0 ] && return 0
    cat "$TMP/apt.err" >&2
    # The bound on the dialog ends the wait, not apt, which runs as root once the password is given: a late answer, or apt
    # waiting on the package lock, may still install the package. Where it did, it is installed.
    case "$(dpkg-query -W -f='${db:Status-Abbrev}|${Version}' jason-desktop 2>/dev/null || true)" in
        ii*"|$WANTED")
            echo "apt went on after the wait ended and installed the package."
            return 0
            ;;
    esac
    NOT_ADMIN=""
    if ! administrator; then
        if [ "$ROUTE" = sudo ]; then
            NOT_ADMIN=" This account is not an administrator: an administrator of this computer can install the package for you."
        else
            NOT_ADMIN=" This account is not an administrator: an administrator of this computer can type their password in that dialog, or install the package for you."
        fi
    fi
    # apt's own last words first. pkexec becomes apt, as root, once the password is given, and timeout cannot end that, so
    # it waits for apt: a run past the bound has ended by now, and said why. Then the bound alone: pkexec was ended while
    # its dialog waited for an answer, and nothing ran.
    if grep -qE '^E: (Could not get lock|Unable to acquire the dpkg frontend lock)' "$TMP/apt.err"; then
        fail "Another installation (Ubuntu's own updates, likely) held the package system for ten minutes, so nothing was installed: run the line again in a few minutes."
    elif [ "$STATUS" -eq 124 ] && ! grep -q '^E:' "$TMP/apt.err"; then
        fail "The password dialog was not answered within $((ASK_SECONDS / 60)) minutes, so nothing was installed. Run the line again to be asked again.$NOT_ADMIN"
    elif grep -qE 'Request dismissed|Not authorized|incorrect password attempt|incorrect authentication attempt|Authentication failed|a password is required|is not in the sudoers file|may not run sudo|afraid I can.t do that' "$TMP/apt.err"; then
        fail "The install needs an administrator's password once, and none was given, so nothing was installed. Run the line again to be asked again.$NOT_ADMIN"
    elif grep -qE 'No authentication agent found|Error getting authority' "$TMP/apt.err" || ! command -v pkexec >/dev/null 2>&1; then
        fail "Jason Desktop is installed from a system package here, which needs your password once, and this session has no way to ask for it: run the line in a terminal of your desktop, or download $DEB_ASSET from $BASE and run: sudo apt install ./$DEB_ASSET"
    elif grep -qE 'Message recipient disconnected|No session for cookie' "$TMP/apt.err"; then
        fail "The system's password dialog failed, so nothing was installed. Sign out and back in (or restart), then run the line again."
    fi
    fail "Jason Desktop was not installed whole: apt said why above. sudo dpkg --configure -a finishes it, or sudo apt purge jason-desktop takes it off."
}

# The deb: installed by apt, then set up by the jason the package carries, /opt/Jason/resources/bundle/jason. The package
# adds Jason's apt repository and its key, and /usr/bin/jason; every later update comes with the system's.
install_deb() {
    DEST="$DEB_PLACE"
    CARRIED="$DEST/resources/bundle/jason"
    INSTALLED="$LATEST"
    WANTED="$(printf '%s' "$LATEST" | tr '-' '~')"
    PLACED_STATE="$(dpkg-query -W -f='${db:Status-Abbrev}|${Version}' jason-desktop 2>/dev/null || true)"
    PLACED_VERSION=""
    case "$PLACED_STATE" in
        ii*'|'*) PLACED_VERSION="${PLACED_STATE#*|}" ;;
    esac
    if answers "$CARRIED" || answers "$COMMAND_LINE"; then ANSWERED=1; fi
    if [ "$PLACED_VERSION" = "$WANTED" ]; then
        echo "Jason Desktop $LATEST is already installed; nothing to download."
    elif [ -n "$PLACED_VERSION" ] && dpkg --compare-versions "$PLACED_VERSION" gt "$WANTED"; then
        INSTALLED="$(printf '%s' "$PLACED_VERSION" | tr '~' '-')"
        echo "Jason Desktop $INSTALLED is already installed; it is kept as it is, and nothing is downloaded."
    else
        refuse_while_running
        ASSET="$DEB_ASSET"
        RID="Linux (the deb)"
        download_archive
        apt_install "$TMP/$ASSET"
        echo "Installed Jason Desktop $LATEST from its package"
    fi
    [ -f "$CARRIED" ] || fail "the package jason-desktop is installed, and $CARRIED is not there, so jason was not put in the terminal: sudo apt install --reinstall jason-desktop puts it back."
    set_up_and_end
}

# The version an AppImage carries, read from the version.json of its bundle; nothing where it is not one of Jason's.
appimage_version() {
    READ="$(mktemp -d "$TMP/read.XXXXXX")"
    (cd "$READ" && "$1" --appimage-extract 'resources/bundle/version.json' >/dev/null 2>&1) || true
    sed -n 's/.*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$READ/squashfs-root/resources/bundle/version.json" 2>/dev/null | head -n 1
}

# The AppImage: one file per account, which updates itself, with no root at any step. Refused before anything is
# downloaded where it could not run its sandbox (user namespaces refused, as on linux-hardened) or open at all (no
# setuid fusermount, which mounts it). Placed in ~/Applications by one rename, its menu entry and icons written under the
# account's data folder, and the jason it carries copied under the data directory as each start of it copies it, so the
# copy's own `jason install` runs before the application has ever started.
install_appimage() {
    if command -v unshare >/dev/null 2>&1 && ! unshare -Ur true >/dev/null 2>&1; then
        fail "This system does not let an application make the user namespaces Chromium's sandbox needs, and Jason never runs without its sandbox, so nothing was installed. On Arch this is the linux-hardened kernel or a sysctl that turns them off."
    fi
    FUSE=""
    for TOOL in fusermount3 fusermount; do
        FOUND="$(command -v "$TOOL" 2>/dev/null || true)"
        if [ -n "$FOUND" ] && [ -u "$FOUND" ]; then FUSE=1; fi
    done
    [ -n "$FUSE" ] || fail "Jason Desktop is an AppImage here, which needs FUSE 3 to open: install the fuse3 package (sudo pacman -S fuse3 on Arch), then run the line again. Nothing was installed."

    DEST="$APPIMAGE_PLACE/$APPIMAGE_ASSET"
    DATA="${JASON_DATA_DIR:-$HOME/.jason}"
    INSTALLED="$LATEST"
    KEPT=""
    if [ -e "$DEST" ]; then
        PLACED_VERSION="$(appimage_version "$DEST")"
        [ -n "$PLACED_VERSION" ] || fail "A file of that name that is not Jason Desktop is in $APPIMAGE_PLACE ($DEST); it is left as it is, and nothing was installed."
        KEPT=1
        INSTALLED="$PLACED_VERSION"
        if [ "$PLACED_VERSION" = "$LATEST" ]; then
            echo "Jason Desktop $LATEST is already installed at $DEST; nothing to download."
        else
            echo "Jason Desktop $PLACED_VERSION is already installed at $DEST; it is kept as it is, and its own update moves it."
            if older_than "$PLACED_VERSION" "$LATEST"; then BEHIND=1; fi
        fi
    fi
    CARRIED="$DATA/desktop/bundle/$INSTALLED/jason"
    if answers "$CARRIED" || answers "$COMMAND_LINE"; then ANSWERED=1; fi

    if [ -z "$KEPT" ]; then
        refuse_while_running
        ASSET="$APPIMAGE_ASSET"
        RID="Linux (the AppImage)"
        download_archive
        mkdir -p "$APPIMAGE_PLACE" || fail "$APPIMAGE_PLACE could not be made; nothing was installed"
        STAGE="$(mktemp "$APPIMAGE_PLACE/.jason-install.XXXXXX")" || fail "no file could be made in $APPIMAGE_PLACE; nothing was installed"
        trap 'rm -rf "$TMP" "$STAGE"' EXIT
        cp "$TMP/$ASSET" "$STAGE" && chmod 755 "$STAGE" || fail "$ASSET could not be put in $APPIMAGE_PLACE; nothing was installed"
        [ "$(appimage_version "$STAGE")" = "$LATEST" ] || fail "the AppImage in the release is not Jason Desktop $LATEST; nothing was installed"
        [ ! -e "$DEST" ] || fail "$DEST appeared while this ran; nothing was installed"
        mv "$STAGE" "$DEST" || fail "$DEST could not be put in place; nothing was installed"
        echo "Installed Jason Desktop $LATEST to $DEST"
    fi

    # What the application carries, read out of it once: the bundle the copy is made of, its menu entry and its icons.
    OPENED="$(mktemp -d "$TMP/opened.XXXXXX")"
    (cd "$OPENED" && "$DEST" --appimage-extract >/dev/null 2>&1) || fail "$DEST could not be read, so jason was not put in the terminal. Jason Desktop is installed at $DEST"
    ROOT="$OPENED/squashfs-root"

    # The copy under the version, made whole beside its place and renamed into it, as the application's own start makes it;
    # one already there is the application's, which its start checks.
    BUNDLES="$DATA/desktop/bundle"
    if [ ! -d "$BUNDLES/$INSTALLED" ]; then
        mkdir -p "$BUNDLES" || fail "$BUNDLES could not be made, so jason was not put in the terminal. Jason Desktop is installed at $DEST"
        COPY="$BUNDLES/.$INSTALLED.tmp-$$"
        cp -R "$ROOT/resources/bundle" "$COPY" && mv "$COPY" "$BUNDLES/$INSTALLED" || fail "the jason Jason Desktop carries could not be copied to $BUNDLES/$INSTALLED, so jason was not put in the terminal. Jason Desktop is installed at $DEST"
    fi

    # The menu entry and the icons, in the account's own data folder, where desktops and launchers read them. The entry
    # starts the file where it is with -- before the address, is hidden once that file is gone (TryExec), and takes the
    # desktop's links (MimeType), in the form the jason it carries writes too (AppImageMenuEntry). The path is bare in Exec
    # wherever nothing in it needs quoting: xdg-open outside GNOME and KDE takes Exec's first word, quotes and all, as the
    # program. Else it is spelled as the Desktop Entry Specification reads it: quoted in Exec, where a double quote, a
    # backtick, a dollar sign and a backslash take a backslash and a per-cent is doubled; then, in both lines, every
    # backslash doubled as a string's own escape. awk reads the path from its environment, since -v would take its
    # backslashes as escapes, and byte by byte (LC_ALL=C), as the jason it carries judges the path, whatever the locale.
    SHARE="${XDG_DATA_HOME:-$HOME/.local/share}"
    mkdir -p "$SHARE/applications"
    LC_ALL=C PLACED="$DEST" awk '
        function spelled(path, quoted,    out, i, c) {
            out = ""
            for (i = 1; i <= length(path); i++) {
                c = substr(path, i, 1)
                if (quoted && (c == "\"" || c == "`" || c == "$")) out = out "\\\\" c
                else if (c == "\\") out = out (quoted ? "\\\\\\\\" : "\\\\")
                else if (quoted && c == "%") out = out "%%"
                else out = out c
            }
            return out
        }
        function bare(path,    i, c) {
            if (path == "") return 0
            for (i = 1; i <= length(path); i++) {
                c = substr(path, i, 1)
                if (c ~ /[[:space:][:cntrl:]]/ || index("\"'\''\\><~|&;$*?#()`%", c) > 0) return 0
            }
            return 1
        }
        /^\[/ {
            entry = ($0 == "[Desktop Entry]")
            print
            if (entry) {
                print "TryExec=" spelled(ENVIRON["PLACED"], 0)
                print "MimeType=x-scheme-handler/jason-desktop;"
            }
            next
        }
        entry && /^(TryExec|MimeType)=/ { next }
        entry && /^Exec=/ {
            if (bare(ENVIRON["PLACED"])) print "Exec=" ENVIRON["PLACED"] " -- %U"
            else print "Exec=\"" spelled(ENVIRON["PLACED"], 1) "\" -- %U"
            next
        }
        { print }
    ' "$ROOT/jason-desktop-app.desktop" > "$SHARE/applications/jason-desktop.desktop" || fail "the menu entry could not be written to $SHARE/applications. Jason Desktop is installed at $DEST"
    # The folder's cache, without which GIO finds no program for the desktop's link scheme there. A system without the tool
    # still starts the entry from its menu, and xdg-open outside GNOME finds it by its own scan.
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database "$SHARE/applications" >/dev/null 2>&1 || true
    fi
    for ICON in "$ROOT"/usr/share/icons/hicolor/*/apps/jason-desktop-app.png; do
        [ -f "$ICON" ] || continue
        SIZE="$(basename "$(dirname "$(dirname "$ICON")")")"
        mkdir -p "$SHARE/icons/hicolor/$SIZE/apps" && cp "$ICON" "$SHARE/icons/hicolor/$SIZE/apps/jason-desktop-app.png"
    done
    set_up_and_end
}

install_linux() {
    trap 'exit 129' HUP
    trap 'exit 130' INT
    trap 'exit 143' TERM
    # An agent's own sandbox (bubblewrap or landlock style) sets NoNewPrivs: sudo, pkexec and a setuid fusermount cannot
    # raise privileges inside it, so nothing it could start would install.
    if grep -qE '^NoNewPrivs:[[:space:]]*1' /proc/self/status 2>/dev/null; then
        fail "This command runs inside a sandbox that cannot install software: run the line in your own terminal, or let your agent run it outside its sandbox. Nothing was installed."
    fi
    ANSWERED=""
    CARRIED=""
    BEHIND=""
    COMMAND_LINE=""
    if [ -f "$HOME/.local/bin/jason" ] && { [ -f "$HOME/.local/bin/jason-LICENSE" ] || [ -f "$HOME/.local/bin/jason-THIRD-PARTY-NOTICES" ]; }; then
        COMMAND_LINE="$HOME/.local/bin/jason"
    fi
    fetch manifest.json "$TMP/manifest.json"
    LATEST="$(sed -n 's/^[[:space:]]*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$TMP/manifest.json" | head -n 1)"
    [ -n "$LATEST" ] || fail "$BASE/manifest.json does not name a version"
    if [ -n "$VERSION" ] && [ "$LATEST" != "$VERSION" ]; then
        fail "--version asked for $VERSION and $BASE/manifest.json names $LATEST; nothing was installed"
    fi
    # A release before 0.49.1 installs the command line alone on Linux, by its own release's script.
    if older_than "$LATEST" 0.49.1 || ! from_minor 49 "$LATEST"; then
        fail "Jason $LATEST is installed on Linux by its own release's script, not this one, which installs Jason Desktop 0.49.1 and later; nothing was installed"
    fi
    # The package manager the system has decides: apt's package where there is apt, else the AppImage. An AppImage already
    # in its place stays the installation, apt or not, and no deb is installed beside it.
    if [ ! -e "$APPIMAGE_PLACE/$APPIMAGE_ASSET" ] && command -v apt-get >/dev/null 2>&1 && command -v dpkg >/dev/null 2>&1; then
        install_deb
    else
        install_appimage
    fi
}

# --- the profiles ----------------------------------------------------------------------------------------------

# The rule the PATH lines follow, which the application's `jason install` writes by on macOS and for an AppImage on Linux,
# and which `jason status`, its repair and `jason uninstall` hold to these two functions word for word. Into the one file
# a login shell of this account reads: zsh reads ~/.zprofile; bash reads the first of
# ~/.bash_profile, ~/.bash_login and ~/.profile that exists, and never ~/.profile when one of the others does;
# everything else reads ~/.profile, as does an account with no SHELL at all.
login_profile() {
    case "${SHELL:-}" in
        zsh|*/zsh) echo "$HOME/.zprofile" ;;
        bash|*/bash)
            for candidate in .bash_profile .bash_login; do
                if [ -f "$HOME/$candidate" ]; then
                    echo "$HOME/$candidate"
                    return 0
                fi
            done
            echo "$HOME/.profile" ;;
        *) echo "$HOME/.profile" ;;
    esac
}
# A terminal a desktop opens is not a login shell: GNOME's reads ~/.bashrc and never the login profile, which the
# desktop reads once, at sign-in. So for bash and zsh the line goes into the file every interactive shell of theirs
# reads as well, ~/.bashrc or ~/.zshrc, and a new terminal finds jason without a sign-out. A shell with no such file
# of its own is told to sign out and back in, since only that reads the login profile again. `jason status`'s repair
# and `jason uninstall` hold the same rule, in C#, and this function to it word for word.
interactive_profile() {
    case "${SHELL:-}" in
        zsh|*/zsh) echo "$HOME/.zshrc" ;;
        bash|*/bash) echo "$HOME/.bashrc" ;;
        *) ;;
    esac
}

install_linux
exit 0
