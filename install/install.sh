#!/bin/sh
# Installs Jason on macOS and Linux.
#
# On macOS it installs Jason Desktop, with jason in the terminal: the application's zip for Apple silicon from the latest
# release, or from the one --version names, checked against checksums.txt, unpacked with ditto into a staging folder
# beside its place, its signature verified as a Developer ID signature of the team that signs Jason, and moved into place
# by one rename - /Applications where this account may write there, else ~/Applications. Nothing is opened, and no
# password is asked. An Intel Mac is refused before anything is downloaded: this script installs Jason Desktop on Apple
# silicon only.
# A Jason.app already in either folder is never renamed, replaced or deleted: one of 0.49.0 or later is kept as it is,
# once its signature verifies the same way, and an earlier one is left for its own Check for Updates, and the script ends
# non-zero saying so. Then the jason the application carries sets the machine up, `jason install --roles`:
# the wrapper ~/.local/bin/jason and the PATH lines in the profiles, the skills for Claude Code and Codex where each is found, an older
# command-line install retired, and the role skills in Jason's data directory. Where a runtime answered before and none
# answers after, or the report says it stopped one, a runtime is started again from the application's jason. It ends
# non-zero, saying which act failed, when jason was not put in the terminal; on success its last line is the full path
# of jason, for the shell it ran in and an agent's session already running, which read no profile again.
#
# On Linux: the archive for this machine, checked against checksums.txt before it is unpacked; the executable put in
# ~/.local/bin, with the licence and the third-party notices beside it as jason-LICENSE and jason-THIRD-PARTY-NOTICES;
# and that directory added to the PATH behind a marker line unless --no-modify-path says otherwise. Running it again over
# the same version downloads nothing and says so, unless a licence file is missing beside it, which it then writes.
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
MARKER="# added by jason install"
INSTALL_DIR="$HOME/.local/bin"
FEED="${JASON_INSTALL_FEED:-}"
VERSION=""
MODIFY_PATH=1
INSTALL_DIR_GIVEN=""

usage() {
    cat <<EOF
Usage: install.sh [--version X.Y.Z] [--feed DIRECTORY-OR-URL] [--install-dir DIRECTORY] [--no-modify-path]

  --version X.Y.Z        install that release rather than the latest (on macOS, 0.49.0 or later)
  --feed DIRECTORY|URL   read the release's files from there instead of the published releases (also JASON_INSTALL_FEED)
  --install-dir DIR      Linux: put the executable there instead of ~/.local/bin; not taken on macOS
  --no-modify-path       Linux: do not add the install directory to the PATH in your profile; not taken on macOS

On macOS it installs Jason Desktop into /Applications, or ~/Applications where this account may not write to
/Applications, opens nothing, and puts jason in the terminal through the application's own jason install. A Jason.app
already in either folder is kept as it is, never replaced; one before 0.49.0 is updated from its own menu first.
This script installs Jason Desktop on Apple silicon Macs only: on an Intel Mac it stops before it downloads anything.
On a Mac this script is the way to install Jason; Homebrew's formula is the command line
alone, without Jason Desktop.
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

# A Mac installs Jason Desktop, whose architecture is read further down; Linux the archive.
case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) RID="linux-x64"; ASSET="jason-linux-x64.tar.gz" ;;
    Darwin-*) RID="macos"; ASSET="" ;;
    *) fail "no release is built for $(uname -s) on $(uname -m); the releases are linux-x64, osx-arm64 and win-x64, and Jason Desktop for macOS" ;;
esac

# Refused on macOS before anything is read or written: neither can be done the way they ask any more.
if [ "$RID" = macos ]; then
    [ -z "$INSTALL_DIR_GIVEN" ] || fail "--install-dir is not taken on macOS: Jason Desktop goes to /Applications or ~/Applications, and the jason it carries is the one the terminal runs. Nothing was installed: run this again without it."
    [ "$MODIFY_PATH" -eq 1 ] || fail "--no-modify-path is not taken on macOS: every install puts jason in the terminal. Nothing was installed: run this again without it."
fi

# --- the install directory ----------------------------------------------------------------------------------

# Refused before anything is written. The entries of PATH are separated by ':', with no escape, so a directory whose name
# holds one is two entries to every reader, neither of them the directory: it can be on no PATH at all, and is refused
# under --no-modify-path too. On macOS the application's own jason install writes the terminal command, and says what it
# could not write. The other characters are this script's own limit: the line it writes into a profile reads
# the directory inside double quotes, where '"', '$', '`' and '\' are not read as themselves, and a line break ends that
# line. Under --no-modify-path no line is written, and a person may put the directory on PATH their own way.
if [ "$RID" != macos ]; then
case "$INSTALL_DIR" in
    *:*) fail "the install directory $INSTALL_DIR holds ':', which separates the entries of PATH, so no PATH entry can name it; nothing was installed: choose a folder whose name does not hold it with --install-dir" ;;
esac
if [ "$MODIFY_PATH" -eq 1 ]; then
    LINE_BREAK='
'
    UNWRITTEN=""
    case "$INSTALL_DIR" in
        *"$LINE_BREAK"*) UNWRITTEN="it holds a line break, which would end the line it writes in your profile" ;;
        *'"'*) UNWRITTEN="it holds '\"', which the line it writes in your profile reads inside double quotes as something else" ;;
        *'$'*) UNWRITTEN="it holds '\$', which the line it writes in your profile reads inside double quotes as something else" ;;
        *'`'*) UNWRITTEN="it holds '\`', which the line it writes in your profile reads inside double quotes as something else" ;;
        *'\'*) UNWRITTEN="it holds '\\', which the line it writes in your profile reads inside double quotes as something else" ;;
    esac
    [ -z "$UNWRITTEN" ] || fail "this installer cannot write $INSTALL_DIR into your PATH: $UNWRITTEN. Nothing was installed: choose another --install-dir, or install with --no-modify-path and add the directory to your PATH yourself"
fi
fi

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

# The two notices an archive carries beside the executable. Beside the installed executable each is jason-<name>.
NOTICES="LICENSE THIRD-PARTY-NOTICES"

# Each notice the archive holds as a regular file whose entry is exactly its name at the archive's top level, into
# $TMP/notices; anything else under that name — a path, a directory, a link — is not the release's notice. A name the
# archive lists more than once is not taken at all: GNU tar extracts the last of them and bsdtar the first, so the only
# rule that is the same on Linux and macOS is that neither is this release's file.
take_notices() {
    mkdir -p "$TMP/notices"
    tar -tzf "$TMP/$ASSET" > "$TMP/listing"
    for N in $NOTICES; do
        [ "$(grep -cxE "(\./)?$N" "$TMP/listing" || true)" -eq 1 ] || continue
        [ "$(grep -cxF "$N" "$TMP/listing" || true)" -eq 1 ] || continue
        mkdir -p "$TMP/notice-$N"
        tar -xzf "$TMP/$ASSET" -C "$TMP/notice-$N" "$N" 2>/dev/null || true
        if [ -f "$TMP/notice-$N/$N" ] && [ ! -L "$TMP/notice-$N/$N" ]; then
            mv "$TMP/notice-$N/$N" "$TMP/notices/$N"
        fi
    done
}

# Each notice beside the executable, by one rule per name: jason-<name> is written from the archive's file of that name
# when the archive has it, and removed when it does not. Each is copied beside its final name and renamed onto it, after
# the executable is in place. A failure leaves its reason in REASON.
write_notices() {
    for N in $NOTICES; do
        DESTINATION="$INSTALL_DIR/jason-$N"
        if [ -L "$DESTINATION" ]; then
            REASON="$(rm -f "$DESTINATION" 2>&1)" || return 1
        elif [ -d "$DESTINATION" ]; then
            REASON="$DESTINATION is a directory"
            return 1
        fi
        if [ -f "$TMP/notices/$N" ]; then
            REASON="$(cp "$TMP/notices/$N" "$DESTINATION.installing.$$" 2>&1 && mv -f "$DESTINATION.installing.$$" "$DESTINATION" 2>&1)" || {
                rm -f "$DESTINATION.installing.$$"
                return 1
            }
        else
            REASON="$(rm -f "$DESTINATION" 2>&1)" || return 1
        fi
    done
}

# True when a notice is not beside the executable as a file.
missing_notice() {
    for N in $NOTICES; do
        [ -f "$INSTALL_DIR/jason-$N" ] || return 0
    done
    return 1
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

# True when a version is 0.6.0 or later: its archive carries the licence and the notices, and its tag a licence file.
# An earlier release carried a notice of its own, which is neither.
licensed() {
    from_minor 6 "$1"
}

# True when a version is 0.24.0 or later, published under the MIT licence. From 0.6.0 to 0.23.x a release was
# published under the Jason Runtime Licence, LICENSE-RUNTIME.md, which those tags keep.
mit() {
    from_minor 24 "$1"
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
            signed_as_jason "$DEST" || fail "the Jason.app at $DEST does not verify as signed by $APP_TEAM, the team that signs Jason (said above), so its jason was not run; nothing was changed"
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

# --- the version, and whether it is already here ------------------------------------------------------------

fetch manifest.json "$TMP/manifest.json"
# The manifest is machine-written, one field per line; this is the value on its "version" line.
LATEST="$(sed -n 's/^[[:space:]]*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$TMP/manifest.json" | head -n 1)"
[ -n "$LATEST" ] || fail "$BASE/manifest.json does not name a version"

# --version names a release, and the manifest that came back has to be that release's. A feed that answered with
# another version — a stale mirror, a directory holding the wrong release, a download URL that resolved to
# something else — would otherwise be installed anyway, under the version that was asked for.
if [ -n "$VERSION" ] && [ "$LATEST" != "$VERSION" ]; then
    fail "--version asked for $VERSION and $BASE/manifest.json names $LATEST; nothing was installed"
fi

TARGET="$INSTALL_DIR/jason"
INSTALLED=""
if [ -x "$TARGET" ]; then
    # What it prints carries build metadata (+<commit>), which a version comparison ignores.
    INSTALLED="$("$TARGET" --version 2>/dev/null | head -n 1 | cut -d+ -f1 || true)"
fi

if [ "$INSTALLED" = "$LATEST" ]; then
    # The right version, without its notices: an installation an older jason updated, or one whose notices were
    # removed. A release before 0.6.0 never had THIRD-PARTY-NOTICES, so it has nothing to repair. The archive is
    # checked as an install's is; the notices are taken out of it, and the executable is not touched.
    if licensed "$LATEST" && missing_notice; then
        download_archive
        take_notices
        write_notices || fail "Jason $LATEST is installed at $TARGET, and its licence files could not be written beside it: $REASON. Run this script again."
        echo "Jason $LATEST is already installed at $TARGET; its licence files were missing and are written beside it now."
    else
        echo "Jason $LATEST is already installed at $TARGET; nothing to download."
    fi
else
    # --- download, and check before unpacking ---------------------------------------------------------------

    download_archive

    # The executable is taken out of the archive first and alone; the notices are read out of it after.
    mkdir -p "$TMP/unpacked"
    tar -xzf "$TMP/$ASSET" -C "$TMP/unpacked" jason
    take_notices

    # --- start it once, then install ------------------------------------------------------------------------

    # The checked, unpacked executable is asked for its version before it goes anywhere near the install
    # directory: a program that does not start here — a Linux without the ICU library, say — is not installed
    # over one that did, and its own words on stderr say why. What it prints must be the manifest's version.
    chmod 755 "$TMP/unpacked/jason"
    STARTED="$("$TMP/unpacked/jason" --version | head -n 1 | cut -d+ -f1)"
    [ -n "$STARTED" ] || fail "the downloaded jason does not start on this machine (see above); nothing was installed"
    [ "$STARTED" = "$LATEST" ] || fail "the downloaded jason says it is $STARTED and the manifest says $LATEST; nothing was installed"

    mkdir -p "$INSTALL_DIR"
    # Copied beside its destination first, then one rename onto it: the temporary directory may be on another
    # file system, and there must be no moment at which a half-written jason is on the PATH.
    cp "$TMP/unpacked/jason" "$INSTALL_DIR/jason.installing.$$"
    mv -f "$INSTALL_DIR/jason.installing.$$" "$TARGET"

    # The notices go beside it once it is in place. One that cannot be written leaves Jason installed without it, and
    # the run says so; run again, it takes the path above and writes them.
    write_notices || fail "Jason $STARTED is installed at $TARGET, and its licence files could not be written beside it: $REASON. Run this script again."
    echo "Installed Jason $STARTED to $TARGET"
fi

# --- where the files will be ---------------------------------------------------------------------------------

# What Jason learns about the person's business is kept in plain files under the harness root, which the runtime makes
# at the first project or campaign it creates; this script makes no folder there. The folder is said as the default,
# made as the runtime makes it, Jason in the home: Harness:Root in the settings can name another, and only the runtime
# reads its settings, so the root in force is for `jason status` to say rather than for a second reader of them here.
# Each text is in single quotes, so its backticks are text and not a command to substitute. The home loses every
# separator at its end, as the runtime reads it: `/home/marta//` is `/home/marta`, and a home of `/` gives `/Jason`.
ROOT_HOME="$HOME"
while [ "${ROOT_HOME%/}" != "$ROOT_HOME" ]; do
    ROOT_HOME="${ROOT_HOME%/}"
done
printf 'Jason keeps what it learns about your business in plain files you can read and edit, one folder per project and campaign, in `%s` by default; to keep them somewhere else, set `Harness:Root` in the settings before your first campaign.\n' "$ROOT_HOME/Jason"
printf '%s\n' 'Campaigns you create without naming a project go to the project `Default`; rename it, or make one project per product.'

# --- the PATH ------------------------------------------------------------------------------------------------

# The line goes behind a marker, and the profile is searched before the line is written, so a second run adds
# nothing. Into the one file a login shell of this account reads: zsh reads ~/.zprofile; bash reads the first of
# ~/.bash_profile, ~/.bash_login and ~/.profile that exists, and never ~/.profile when one of the others does;
# everything else reads ~/.profile, as does an account with no SHELL at all. The same rule is `jason status`'s and
# its repair's, which spell it in C# and hold this function to it word for word.
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
LINE="export PATH=\"$INSTALL_DIR:\$PATH\""
# What is searched for is the whole line this script wrote, not the marker above it: the marker is an English
# sentence a person's own profile could carry, and keying the decision on it also meant that a run with a
# different --install-dir found "the mark", wrote nothing, and left the directory it had just installed into off
# the PATH. The line names that directory, so the line is the thing to match — whole (-x) and fixed (-F).
ADDED=""
add_to_profile() {
    if [ -f "$1" ] && grep -qxF "$LINE" "$1"; then
        return 0
    fi
    printf '\n%s\n%s\n' "$MARKER" "$LINE" >> "$1"
    ADDED="${ADDED:+$ADDED and }$1"
}

if [ "$MODIFY_PATH" -eq 1 ]; then
    case ":$PATH:" in
        *":$INSTALL_DIR:"*) ;;
        *)
            add_to_profile "$(login_profile)"
            INTERACTIVE="$(interactive_profile)"
            if [ -n "$INTERACTIVE" ]; then
                add_to_profile "$INTERACTIVE"
            fi
            if [ -n "$ADDED" ] && [ -n "$INTERACTIVE" ]; then
                echo "Added $INSTALL_DIR to the PATH in $ADDED; open a new terminal, or run: $LINE"
            elif [ -n "$ADDED" ]; then
                echo "Added $INSTALL_DIR to the PATH in $ADDED; sign out and back in for a new terminal to find it, or run: $LINE"
            fi ;;
    esac
fi

# --- the role skills ----------------------------------------------------------------------------------------

# The installed jason deploys the role skills, the half of the skills a runtime cannot work without, from the
# release it is: by default from the public repository's releases, from a feed this script was pointed at when that
# feed is an address. A feed in a directory is not one the skills verb reads, so nothing is sent anywhere this script
# did not read itself, and the one line says what to run. What the verb prints is shown; a deployment that fails
# leaves Jason installed, says so in one line after the verb's own, and `jason status` names it as well. JASON_DATA_DIR
# reaches the verb as it reached this script.
deploy_role_skills() {
    if "$TARGET" skills install --roles-only "$@"; then
        :
    else
        echo 'The role skills were not deployed; the lines above say why. Run `jason skills install --roles-only` once that is dealt with; `jason status` names it as well.'
    fi
}
case "$FEED" in
    '') deploy_role_skills ;;
    http://*|https://*) deploy_role_skills --feed "${FEED%/}/manifest.json" ;;
    *) echo 'The role skills were not deployed: the feed is a directory, which `jason skills install` does not read. Run `jason skills install --roles-only` now that Jason is installed, offline with `--source <checkout>`.' ;;
esac

# --- the licence, and what to run next -----------------------------------------------------------------------

# The last two lines of a successful run, and never a question: what is installed and the terms it is installed
# under, then the one command that says what this installation still needs. Each text is in single quotes where it
# carries backticks, so they are text and not a command to substitute.
if mit "$LATEST"; then
    echo "Jason $LATEST is licensed under the MIT licence: $LICENCE_BASE/v$LATEST/LICENSE"
elif licensed "$LATEST"; then
    echo "Jason $LATEST was published under the Jason Runtime Licence: $LICENCE_BASE/v$LATEST/LICENSE-RUNTIME.md"
fi
echo 'Next, run `jason status`: it says what this installation still needs, and the command for each.'
