#!/bin/bash
# Writes the index of the flat apt repository a release is, beside the release's deb, run by the release's apt-index job
# with the repository's signing subkey and by CI's desktop job with a key of its run's own.
#
#   write-apt-index.sh <folder>
#
# The folder holds jason-desktop-linux-x64.deb. Beside it this writes the four files apt reads from a flat repository:
#
#   Packages     the deb's own control fields, then its file name, size, MD5 and SHA-256
#   Release      Jason as the origin and the label, the suite stable, the deb's architecture, the date, and Packages'
#                MD5 and SHA-256: the shape under which apt and Software Updater were seen to take the package
#   InRelease    Release, clearsigned
#   Release.gpg  Release's detached signature, armored
#
# It signs with APT_SIGNING_KEY: the repository key's signing subkey alone, armored, with no passphrase, as the release's
# signing environment holds it. The key that certifies that subkey is kept offline, so a secret that carries that key's
# own secret is refused, and so is one with no signing subkey or more than one, before anything is written. The secret is
# imported into a gpg home of this run's own, which is removed, and its agent stopped, however the script ends; nothing
# here prints it. Every digest is pinned to SHA-512, which apt accepts from an Ed25519 key, rather than left to gpg's
# defaults. A failure is one ::error line and exit 1.
set -euo pipefail
export LC_ALL=C

fail() { echo "::error title=apt index::$1"; exit 1; }

[ $# -eq 1 ] || fail "usage: write-apt-index.sh <folder holding jason-desktop-linux-x64.deb>"
folder=$1
name=jason-desktop-linux-x64.deb
deb="$folder/$name"
[ -f "$deb" ] || fail "$deb is not there: the index describes the release's deb, and is written beside it."
[ -n "${APT_SIGNING_KEY:-}" ] || fail "APT_SIGNING_KEY is empty: it holds the apt repository's signing subkey, in the signing environment, which only release tags and release branches may run in."

home=$(mktemp -d)
trap 'gpgconf --homedir "$home" --kill all > /dev/null 2>&1 || true; rm -rf "$home"' EXIT
chmod 700 "$home"
export GNUPGHOME="$home"

# gpg's own words are not repeated: on an armor it cannot read, they quote the secret's text.
printf '%s\n' "$APT_SIGNING_KEY" | gpg --batch --import > /dev/null 2>&1 \
  || fail "gpg did not import APT_SIGNING_KEY cleanly: it holds the signing subkey alone, armored as gpg --export-secret-subkeys --armor writes it."

# A secret key's line says in its fifteenth field whether its secret is here: '#' for a stub, which is all an export of
# the subkeys alone carries of the key that certifies them.
listing=$(gpg --batch --with-colons --list-secret-keys)
if awk -F: '$1 == "sec" && $15 != "#" { found = 1 } END { exit !found }' <<< "$listing"; then
  fail "APT_SIGNING_KEY holds the primary key's secret as well: store the signing subkey alone (gpg --export-secret-subkeys), and keep the primary offline."
fi
signers=$(awk -F: '
  $1 == "ssb" { want = ($15 != "#" && $12 ~ /s/ && $2 !~ /[erd]/) }
  $1 == "fpr" && want { print $10; want = 0 }
' <<< "$listing")
[ -n "$signers" ] || fail "APT_SIGNING_KEY holds no subkey that can sign."
[ "$(wc -l <<< "$signers")" -eq 1 ] || fail "APT_SIGNING_KEY holds $(wc -l <<< "$signers") subkeys that can sign; it holds the one the release signs with."
signer=$signers

control=$(dpkg-deb --field "$deb")
architecture=$(dpkg-deb --field "$deb" Architecture)
{
  printf '%s\n' "$control"
  echo "Filename: ./$name"
  echo "Size: $(stat -c %s "$deb")"
  echo "MD5sum: $(md5sum < "$deb" | cut -d' ' -f1)"
  echo "SHA256: $(sha256sum < "$deb" | cut -d' ' -f1)"
} > "$folder/Packages"

size=$(stat -c %s "$folder/Packages")
{
  echo "Origin: Jason"
  echo "Label: Jason"
  echo "Suite: stable"
  echo "Architectures: $architecture"
  echo "Date: $(date -u '+%a, %d %b %Y %H:%M:%S UTC')"
  echo "MD5Sum:"
  echo " $(md5sum < "$folder/Packages" | cut -d' ' -f1) $size Packages"
  echo "SHA256:"
  echo " $(sha256sum < "$folder/Packages" | cut -d' ' -f1) $size Packages"
} > "$folder/Release"

rm -f "$folder/InRelease" "$folder/Release.gpg"
gpg --batch --yes --local-user "$signer!" --digest-algo SHA512 --clearsign --output "$folder/InRelease" "$folder/Release"
gpg --batch --yes --local-user "$signer!" --digest-algo SHA512 --armor --detach-sign --output "$folder/Release.gpg" "$folder/Release"
echo "Wrote Packages and Release for $name, and signed Release with the subkey $signer as InRelease and Release.gpg."
