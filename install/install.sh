#!/bin/sh
# Installs Jason on macOS and Linux: the archive for this machine from the latest release, or from the one
# --version names, checked against checksums.txt before it is unpacked; the executable put in ~/.local/bin, with
# the licence and the third-party notices beside it as jason-LICENSE and jason-THIRD-PARTY-NOTICES; and that
# directory added to the PATH behind a marker line unless --no-modify-path says otherwise. Running it again over
# the same version downloads nothing and says so, unless a licence file is missing beside it, which it then writes.
#
#   curl -fsSL https://jasonharness.ai/releases/latest/download/install.sh | sh
#   curl -fsSL https://jasonharness.ai/releases/download/v0.9.0/install.sh | sh -s -- --version 0.9.0 --no-modify-path
#
# JASON_INSTALL_FEED (or --feed) points it at a directory or a base URL holding the same files a release holds
# — the archives, manifest.json and checksums.txt — which is how CI runs it against the archives it has just
# built, with no network. Written for sh rather than bash: the one-liner pipes into sh, and on Debian that is
# dash.
set -eu

# The product's own address, in front of the releases: it redirects every path under /releases/ to the same path in
# the public repository's releases on GitHub.
RELEASES_URL="https://jasonharness.ai/releases"
# The licence is a page of the repository, at the release's tag; the downloads are a release's assets. Each has a base
# of its own, so that the downloads can move without the licence page moving with them.
LICENCE_BASE="https://github.com/reply-team/jason-ai/blob"
MARKER="# added by jason install"
INSTALL_DIR="$HOME/.local/bin"
FEED="${JASON_INSTALL_FEED:-}"
VERSION=""
MODIFY_PATH=1

usage() {
    cat <<EOF
Usage: install.sh [--version X.Y.Z] [--feed DIRECTORY-OR-URL] [--install-dir DIRECTORY] [--no-modify-path]

  --version X.Y.Z        install that release rather than the latest
  --feed DIRECTORY|URL   read the release's files from there instead of the published releases (also JASON_INSTALL_FEED)
  --install-dir DIR      put the executable there instead of ~/.local/bin
  --no-modify-path       do not add the install directory to the PATH in your profile
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
        --install-dir) [ $# -ge 2 ] || fail "--install-dir needs a value"; INSTALL_DIR="$2"; shift ;;
        --install-dir=*) INSTALL_DIR="${1#--install-dir=}" ;;
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
    MINGW*|MSYS*|CYGWIN*) fail "this is a Windows shell; run install.ps1 in PowerShell instead, which installs jason-win-x64.zip" ;;
esac

case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) RID="linux-x64"; ASSET="jason-linux-x64.tar.gz" ;;
    Darwin-arm64) RID="osx-arm64"; ASSET="jason-osx-arm64.tar.gz" ;;
    *) fail "no release is built for $(uname -s) on $(uname -m); the releases are linux-x64, osx-arm64 and win-x64" ;;
esac

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

# True when a version is 0.6.0 or later, its pre-release part ignored: LICENSE-RUNTIME.md first exists at v0.6.0, and an
# earlier release carried a notice of its own, which is not the Jason Runtime Licence. Field by field, since sort -V is
# not on macOS; a field that is not a number is no such version.
licensed() {
    CORE="${1%%[-+]*}"
    MAJOR="$(echo "$CORE" | cut -d. -f1)"
    MINOR="$(echo "$CORE" | cut -d. -f2)"
    for FIELD in "$MAJOR" "$MINOR"; do
        case "$FIELD" in
            ''|*[!0-9]*) return 1 ;;
        esac
    done
    [ "$MAJOR" -gt 0 ] || [ "$MINOR" -ge 6 ]
}

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
LINE="export PATH=\"$INSTALL_DIR:\$PATH\""
# What is searched for is the whole line this script wrote, not the marker above it: the marker is an English
# sentence a person's own profile could carry, and keying the decision on it also meant that a run with a
# different --install-dir found "the mark", wrote nothing, and left the directory it had just installed into off
# the PATH. The line names that directory, so the line is the thing to match — whole (-x) and fixed (-F).
add_to_profile() {
    if [ -f "$1" ] && grep -qxF "$LINE" "$1"; then
        return 0
    fi
    printf '\n%s\n%s\n' "$MARKER" "$LINE" >> "$1"
    echo "Added $INSTALL_DIR to the PATH in $1; open a new terminal, or run: $LINE"
}

if [ "$MODIFY_PATH" -eq 1 ]; then
    case ":$PATH:" in
        *":$INSTALL_DIR:"*) ;;
        *) add_to_profile "$(login_profile)" ;;
    esac
fi

# --- the licence ---------------------------------------------------------------------------------------------

# The last line of a successful run, and never a question: what is installed, and the terms it is installed under.
if licensed "$LATEST"; then
    echo "Jason $LATEST is licensed under the Jason Runtime Licence: $LICENCE_BASE/v$LATEST/LICENSE-RUNTIME.md"
fi
