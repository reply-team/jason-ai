#!/bin/bash
# Reads a release's apt index back as a machine's apt reads it, before anything is published, run by the release's
# apt-index job against the repository's public key and by CI's desktop job against its run's own key.
#
#   check-apt-index.sh <folder> <keyring>
#
# The folder holds jason-desktop-linux-x64.deb and the four files write-apt-index.sh wrote beside it; the keyring is the
# binary public key the index must be signed with. Each check is read on its own, never from the writer's word:
#
#   Packages     one entry, for the deb: its package, version and architecture as dpkg-deb reads them from the deb,
#                its file name, and its size, MD5 and SHA-256 computed again from the deb
#   Release      the origin Jason, which the deb's rule for automatic updates names, and Packages' size, MD5 and
#                SHA-256 computed again
#   the key      check-apt-key.sh, first: every key in the keyring and every subkey that signs unrevoked and more
#                than 90 days from its expiry, read from the key itself
#   signatures   InRelease and Release.gpg, each one good signature by a key in the keyring, read from gpgv's status
#                lines and never from its exit code alone, which is 0 for a key that has expired or been revoked; and
#                InRelease signs the text Release holds, byte for byte
#   apt          apt-get update, as the account this runs as, with a configuration, lists and cache of this run's own
#                and nothing of the machine's, reads a copy of the files by a file: source signed-by the keyring,
#                through InRelease, then again through Release and Release.gpg alone, and says no warning, error or
#                notice either time; and then offers the deb's version
#
# A failure is one ::error line per problem found, and exit 1.
set -euo pipefail
export LC_ALL=C

problems=0
problem() { echo "::error title=apt index::$1"; problems=$((problems + 1)); }
fail() { problem "$1"; exit 1; }
settled() { [ "$problems" -eq 0 ] || exit 1; }

[ $# -eq 2 ] || fail "usage: check-apt-index.sh <folder holding the deb and its index> <the public keyring that signs it>"
[ -f "$2" ] || fail "$2 is not there: the index is checked against the public key it must be signed with."
folder=$(realpath "$1")
keyring=$(realpath "$2")
name=jason-desktop-linux-x64.deb
deb="$folder/$name"
for file in "$name" Packages Release InRelease Release.gpg; do
  [ -f "$folder/$file" ] || problem "$folder/$file is not there: a release carries Packages, Release, InRelease and Release.gpg beside its deb."
done
settled
bash "$(dirname "${BASH_SOURCE[0]}")/check-apt-key.sh" "$keyring" || exit 1

home=$(mktemp -d)
trap 'gpgconf --homedir "$home" --kill all > /dev/null 2>&1 || true; rm -rf "$home"' EXIT
chmod 700 "$home"
export GNUPGHOME="$home"

# Packages: one entry, each of these fields once, each equal to what the deb itself says.
grep -q '^$' "$folder/Packages" && problem "Packages holds more than one entry; the repository carries the one deb."
said() { awk -v field="$1" 'index($0, field ": ") == 1 { print substr($0, length(field) + 3); n++ } END { exit n != 1 }' "$2"; }
version=$(dpkg-deb --field "$deb" Version)
for field in Package Version Architecture; do
  expected=$(dpkg-deb --field "$deb" "$field")
  listed=$(said "$field" "$folder/Packages") || { problem "Packages does not say $field once."; continue; }
  [ "$listed" = "$expected" ] || problem "Packages says $field $listed, and the deb's is $expected."
done
for pair in "Filename ./$name" "Size $(stat -c %s "$deb")" "MD5sum $(md5sum < "$deb" | cut -d' ' -f1)" "SHA256 $(sha256sum < "$deb" | cut -d' ' -f1)"; do
  field=${pair%% *}
  expected=${pair#* }
  listed=$(said "$field" "$folder/Packages") || { problem "Packages does not say $field once."; continue; }
  [ "$listed" = "$expected" ] || problem "Packages says $field $listed, and the deb's is $expected."
done

# Release: Jason's origin, and Packages' size and each hash, each once in its own section.
origin=$(said Origin "$folder/Release") || origin=
[ "$origin" = Jason ] || problem "Release says the origin '$origin', and the deb's rule for automatic updates takes the origin Jason."
size=$(stat -c %s "$folder/Packages")
for pair in "MD5Sum $(md5sum < "$folder/Packages" | cut -d' ' -f1)" "SHA256 $(sha256sum < "$folder/Packages" | cut -d' ' -f1)"; do
  section=${pair%% *}
  expected=${pair#* }
  listed=$(awk -v section="$section:" '
    /^[^ ]/ { inside = ($0 == section) ; next }
    inside && $3 == "Packages" { print $1 " " $2; n++ }
    END { exit n != 1 }
  ' "$folder/Release") || { problem "Release does not list Packages once under $section."; continue; }
  [ "$listed" = "$expected $size" ] || problem "Release says Packages has $section ${listed% *} (${listed#* } bytes), and it has $expected ($size bytes)."
done
settled

# The signatures, read from gpgv's status lines. A signature by a key that has expired is "good" to gpgv, with an
# EXPKEYSIG, and exits 0, so the exit code alone is never the verdict.
verify() {
  gpgv --status-fd 1 --keyring "$keyring" "$@" 2> /dev/null || true
}
inline=$(verify --output "$home/signed" "$folder/InRelease")
detached=$(verify "$folder/Release.gpg" "$folder/Release")
words() { sed -n 's/^\[GNUPG:\] //p' <<< "$1" | grep -v -E '^(NEWSIG|KEY_CONSIDERED|SIG_ID|TRUST_|PLAINTEXT)' | tr '\n' ';' ; }
validsig=$(grep '^\[GNUPG:\] VALIDSIG ' <<< "$inline" || true)
[ "$(grep -c '^\[GNUPG:\] NEWSIG' <<< "$inline")" -eq 1 ] && [ "$(wc -l <<< "$validsig")" -eq 1 ] && [ -n "$validsig" ] \
  || fail "InRelease is not signed by a key in $keyring, once: gpgv said $(words "$inline")"
signer=$(awk '{ print $3 }' <<< "$validsig")

grep -q '^\[GNUPG:\] GOODSIG ' <<< "$inline" || fail "InRelease's signature is not good: gpgv said $(words "$inline")"
cmp -s "$home/signed" "$folder/Release" || fail "InRelease does not sign the text of Release: the two say different things."
grep -q '^\[GNUPG:\] GOODSIG ' <<< "$detached" && grep -q "^\[GNUPG:\] VALIDSIG $signer " <<< "$detached" \
  || fail "Release.gpg is not a good signature of Release by the key that signed InRelease: gpgv said $(words "$detached")"

# apt itself, from a copy of the four files and nothing of this machine's: APT_CONFIG is read before anything else, and
# it points every other place apt reads or writes at this run's own folder.
read_by_apt() {
  local through=$1 root
  shift
  root=$(mktemp -d "$home/apt.XXXXXX")
  mkdir -p "$root/repository" "$root/etc/apt.conf.d" "$root/etc/sources.list.d" "$root/etc/preferences.d" "$root/etc/trusted.gpg.d" "$root/lists/partial" "$root/cache/archives/partial"
  for file in "$@"; do cp "$folder/$file" "$root/repository/"; done
  : > "$root/status"
  echo "deb [signed-by=$keyring] file:$root/repository/ ./" > "$root/etc/sources.list"
  cat > "$root/apt.conf" <<CONF
Dir::Etc "$root/etc/";
Dir::Etc::main "/dev/null";
Dir::Etc::parts "$root/etc/apt.conf.d/";
Dir::Etc::sourcelist "$root/etc/sources.list";
Dir::Etc::sourceparts "$root/etc/sources.list.d/";
Dir::Etc::preferences "$root/etc/preferences";
Dir::Etc::preferencesparts "$root/etc/preferences.d/";
Dir::Etc::trusted "$root/etc/trusted.gpg";
Dir::Etc::trustedparts "$root/etc/trusted.gpg.d/";
Dir::State "$root/";
Dir::State::lists "$root/lists/";
Dir::State::status "$root/status";
Dir::Cache "$root/cache/";
CONF
  local out code=0
  out=$(APT_CONFIG="$root/apt.conf" apt-get update 2>&1) || code=$?
  if [ "$code" -ne 0 ] || grep -q -E '^(W|E|N):' <<< "$out"; then
    fail "apt-get update did not read the index without a word through $through (exit $code): $(tr '\n' ' ' <<< "$out")"
  fi
  local offered
  offered=$(APT_CONFIG="$root/apt.conf" apt-cache policy jason-desktop | awk '$1 == "Candidate:" { print $2 }')
  [ "$offered" = "$version" ] || fail "apt reads the index through $through and offers jason-desktop '$offered', and the deb is $version."
}
read_by_apt InRelease Packages InRelease
read_by_apt "Release and Release.gpg" Packages Release Release.gpg
echo "apt-get update read the index through InRelease, and through Release and Release.gpg, without a word, and offers jason-desktop $version, signed by the subkey $signer."
