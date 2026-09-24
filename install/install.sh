#!/bin/sh
# Installs Jason on macOS and Linux: the archive for this machine from the latest release, or from the one
# --version names, checked against checksums.txt before it is unpacked; the executable put in ~/.local/bin;
# and that directory added to the PATH behind a marker line unless --no-modify-path says otherwise. Running
# it again over the same version downloads nothing and says so.
#
#   curl -fsSL https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.sh | sh
#   curl -fsSL https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.sh | sh -s -- --version 0.2.0 --no-modify-path
#
# JASON_INSTALL_FEED (or --feed) points it at a directory or a base URL holding the same files a release holds
# — the archives, manifest.json and checksums.txt — which is how CI runs it against the archives it has just
# built, with no network. Written for sh rather than bash: the one-liner pipes into sh, and on Debian that is
# dash.
set -eu

REPOSITORY_URL="https://github.com/reply-team/jason-ai"
MARKER="# added by jason install"
INSTALL_DIR="$HOME/.local/bin"
FEED="${JASON_INSTALL_FEED:-}"
VERSION=""
MODIFY_PATH=1

usage() {
    cat <<EOF
Usage: install.sh [--version X.Y.Z] [--feed DIRECTORY-OR-URL] [--install-dir DIRECTORY] [--no-modify-path]

  --version X.Y.Z        install that release rather than the latest
  --feed DIRECTORY|URL   read the release's files from there instead of GitHub (also JASON_INSTALL_FEED)
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
    BASE="$REPOSITORY_URL/releases/download/v$VERSION"
else
    BASE="$REPOSITORY_URL/releases/latest/download"
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
    echo "Jason $LATEST is already installed at $TARGET; nothing to download."
else
    # --- download, and check before unpacking ---------------------------------------------------------------

    echo "Downloading Jason $LATEST for $RID from $BASE ..."
    fetch checksums.txt "$TMP/checksums.txt"
    fetch "$ASSET" "$TMP/$ASSET"

    # The digest from checksums.txt, computed here with whichever tool this machine has, compared before tar
    # opens anything. An archive that does not match goes with the temporary directory, and nothing is installed.
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

    # Only the executable is taken out of the archive; LICENSE stays in it, and the install directory holds
    # nothing but the program.
    mkdir -p "$TMP/unpacked"
    tar -xzf "$TMP/$ASSET" -C "$TMP/unpacked" jason

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
    echo "Installed Jason $STARTED to $TARGET"
fi

# --- the PATH ------------------------------------------------------------------------------------------------

[ "$MODIFY_PATH" -eq 1 ] || exit 0

case ":$PATH:" in
    *":$INSTALL_DIR:"*) exit 0 ;;
esac

# The line goes behind a marker, and the profile is searched before the line is written, so a second run adds
# nothing. Into the one file a login shell of this account reads: zsh reads ~/.zprofile; bash reads the first of
# ~/.bash_profile, ~/.bash_login and ~/.profile that exists, and never ~/.profile when one of the others does;
# everything else reads ~/.profile. The same rule is `jason status`'s and its repair's, which spell it in C# and
# hold this function to it word for word.
login_profile() {
    case "${SHELL##*/}" in
        zsh) echo "$HOME/.zprofile" ;;
        bash)
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

add_to_profile "$(login_profile)"
