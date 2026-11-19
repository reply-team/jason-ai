#!/bin/bash
# Holds the apt repository's public key to its expiry, run by check-apt-index.sh on the key an index is checked against,
# and by CI's desktop job on the repository's own key, install/jason-desktop-archive-keyring.gpg, on every pull request.
#
#   check-apt-key.sh <keyring>
#
# The keyring is a binary public key file. Every key in it, and every subkey of it that signs, must be unrevoked and more
# than 90 days from its expiry, read from the key itself, never from a date written beside it: apt refuses an index
# signed by an expired or a revoked key, and every machine with the repository then takes no update until a deb is
# installed by hand. So the key is extended or replaced, and the new public key shipped in a release's deb, at least 90
# days before; a subkey that no longer signs is taken out of the file rather than left to expire in it. The file must
# hold a subkey that signs. A failure is one ::error line per key, and exit 1.
set -euo pipefail
export LC_ALL=C

problems=0
problem() { echo "::error title=apt signing key::$1"; problems=$((problems + 1)); }

[ $# -eq 1 ] || { problem "usage: check-apt-key.sh <keyring>"; exit 1; }
keyring=$1
[ -f "$keyring" ] || { problem "$keyring is not there."; exit 1; }

home=$(mktemp -d)
trap 'gpgconf --homedir "$home" --kill all > /dev/null 2>&1 || true; rm -rf "$home"' EXIT
chmod 700 "$home"
export GNUPGHOME="$home"

listing=$(gpg --batch --with-colons --show-keys "$keyring" 2> /dev/null) || { problem "gpg does not read $keyring as a public key."; exit 1; }

# One line per key or subkey: its kind, its validity (r for revoked), its expiry or never, what it can do, and its
# fingerprint, which gpg writes on the line after the key's own.
keys=$(awk -F: '
  $1 == "pub" || $1 == "sub" { kind = $1; validity = ($2 == "" ? "-" : $2); at = ($7 == "" ? "never" : $7); can = ($12 == "" ? "-" : $12); next }
  $1 == "fpr" && kind != "" { print kind, validity, at, can, $10; kind = "" }
' <<< "$listing")
grep -q '^pub ' <<< "$keys" || { problem "$keyring holds no public key."; exit 1; }

now=$(date +%s)
limit=$((now + 90 * 86400))
signing=0
said=()
while read -r kind validity at can fingerprint; do
  if [ "$kind" = sub ]; then
    [[ $can == *s* ]] || continue
    what="The apt repository's signing subkey $fingerprint"
  else
    what="The apt repository's key $fingerprint"
  fi
  if [ "$validity" = r ]; then
    problem "$what is revoked: apt refuses an index it signs. Sign with a key that is not revoked, and ship its public key in a release's deb first."
    continue
  fi
  [ "$kind" = pub ] || signing=$((signing + 1))
  if [ "$at" = never ]; then
    said+=("$what never expires")
    continue
  fi
  when=$(date -u -d "@$at" +%F)
  if [ "$at" -le "$limit" ]; then
    verb="expires on"
    [ "$at" -gt "$now" ] || verb="expired on"
    problem "$what $verb $when, within 90 days of this run: it must be extended or replaced, and the new public key shipped in a release's deb, before then. apt refuses an index signed by an expired key, and every machine with the repository then takes no update until a deb is installed by hand."
  else
    said+=("$what expires on $when")
  fi
done <<< "$keys"
[ "$signing" -gt 0 ] || problem "$keyring holds no subkey that signs and is not revoked."
[ "$problems" -eq 0 ] || exit 1

printf '%s.\n' "${said[@]}"
echo "Each is unrevoked and more than 90 days from this run."
