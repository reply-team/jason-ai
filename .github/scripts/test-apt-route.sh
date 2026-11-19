#!/bin/bash
# Jason Desktop's apt route on a machine with apt, proven end to end against this run's own deb and index, served the way
# the product's address and GitHub serve a release: run by CI's desktop job on ubuntu-latest, and on any Ubuntu where it
# may use sudo, or runs as root.
#
#   test-apt-route.sh <folder> <keyring>
#
# <folder> holds the deb, jason-desktop-linux-x64.deb, and the index write-apt-index.sh wrote beside it, signed by a key
# whose public half is <keyring>. Nothing in the product carries a switch for this: the machine is made to answer for the
# address instead. Its host name and two stand-ins for GitHub's are mapped to this machine in /etc/hosts, and a
# certificate of this run's own, which the machine trusts for the run, is served by serve-release-chain.py, which answers
# each hop as the real chain does. The package then keeps the source it really writes. Its key, the repository's, is
# replaced by this run's own public key once the package is installed, since a pull request signs with a key of its own;
# that is the one thing the leg changes in what the package placed, and it says so.
#
# What it proves, in order, each check failing the leg with one ::error line:
#   1. the listing of every place the package and apt write, taken before anything is installed;
#   2. the deb installed by apt from the file: the source naming the address's /apt/ path signed by
#      /usr/share/keyrings/jason-desktop.gpg, that key the repository's, the rule for automatic updates a configuration
#      file apt reads, and /usr/bin/jason the jason the package carries, found by name in a new login shell;
#   3. apt reading the repository through the chain, offering the deb's version from it, and installing the package from
#      it, the deb taken from the release's opaque asset address;
#   4. the package purged, and the listing taken again: the same as before, but for /opt, which dpkg removes once it is
#      empty when no installed package lists it.
# Everything it set up for the run (the server, the host names, the certificate) is taken back on its way out.
set -euo pipefail
export LC_ALL=C DEBIAN_FRONTEND=noninteractive

[ $# -eq 2 ] || { echo "usage: test-apt-route.sh <folder holding the deb and its index> <the public keyring that signs it>"; exit 2; }
folder=$(realpath "$1")
keyring=$(realpath "$2")
here=$(dirname "$(realpath "${BASH_SOURCE[0]}")")
repository=$(realpath "$here/../..")
deb="$folder/jason-desktop-linux-x64.deb"
version=$(dpkg-deb --field "$deb" Version)
SUDO=""
[ "$(id -u)" -eq 0 ] || SUDO=sudo

fail() { echo "::error title=apt route::$1"; exit 1; }

work=$(mktemp -d)
hosts_line="127.0.0.1 get.jasonharness.ai github.example.test assets.example.test # test-apt-route.sh"
server=""
cleanup() {
  [ -z "$server" ] || $SUDO kill "$server" 2> /dev/null || true
  if grep -qF "$hosts_line" /etc/hosts; then
    grep -vF "$hosts_line" /etc/hosts > "$work/hosts" && $SUDO sh -c "cat '$work/hosts' > /etc/hosts"
  fi
  if [ -e /usr/local/share/ca-certificates/test-apt-route.crt ]; then
    $SUDO rm -f /usr/local/share/ca-certificates/test-apt-route.crt
    $SUDO update-ca-certificates --fresh > /dev/null 2>&1 || true
  fi
  $SUDO rm -rf "$work"
}
trap cleanup EXIT

# Every place the package, its scripts and apt write for it, as one sorted listing of kind and path, and dpkg's word
# on the package.
places=(/opt /usr/bin /usr/share/applications /usr/share/icons /etc/apt/sources.list.d /etc/apt/apt.conf.d /etc/default
  /usr/share/keyrings /etc/alternatives /var/lib/dpkg/alternatives /var/cache/apt/archives /var/lib/apt/lists
  /etc/apparmor.d /var/cache/apparmor)
listing() {
  for place in "${places[@]}"; do
    if [ -e "$place" ]; then $SUDO find "$place" -printf '%y %p\n'; fi
  done | sort
  echo "dpkg: $(dpkg-query -W -f='${db:Status-Abbrev}' jason-desktop 2> /dev/null || echo 'not known')"
}

# 1. Before.
dpkg-query -W jason-desktop > /dev/null 2>&1 && fail "jason-desktop is known to dpkg before the leg starts; the listing would not be the machine's before."
listing > "$work/before"

# The chain: one release, this run's, the latest.
mkdir -p "$work/releases/v$version"
cp "$deb" "$folder/Packages" "$folder/Release" "$folder/InRelease" "$folder/Release.gpg" "$work/releases/v$version/"
echo "$version" > "$work/releases/latest"
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj "/CN=test-apt-route CA" -keyout "$work/ca.key" -out "$work/ca.crt" \
  -addext basicConstraints=critical,CA:TRUE -addext keyUsage=critical,keyCertSign > /dev/null 2>&1
openssl req -newkey rsa:2048 -nodes -subj "/CN=get.jasonharness.ai" -keyout "$work/server.key" -out "$work/server.csr" > /dev/null 2>&1
printf 'subjectAltName=DNS:get.jasonharness.ai,DNS:github.example.test,DNS:assets.example.test\n' > "$work/san"
openssl x509 -req -in "$work/server.csr" -CA "$work/ca.crt" -CAkey "$work/ca.key" -CAcreateserial -days 1 -extfile "$work/san" -out "$work/server.crt" > /dev/null 2>&1
$SUDO cp "$work/ca.crt" /usr/local/share/ca-certificates/test-apt-route.crt
$SUDO update-ca-certificates > /dev/null
echo "$hosts_line" | $SUDO tee -a /etc/hosts > /dev/null
: > "$work/served.log"
chmod 666 "$work/served.log"
$SUDO python3 "$here/serve-release-chain.py" --root "$work/releases" --cert "$work/server.crt" --key "$work/server.key" --log "$work/served.log" &
server=$!
for _ in $(seq 1 50); do
  curl -fsS -o /dev/null "https://get.jasonharness.ai/apt/Release" 2> /dev/null && break
  sleep 0.2
done
curl -fsS -o /dev/null "https://get.jasonharness.ai/apt/Release" || fail "The stand-in for the address never answered https://get.jasonharness.ai/apt/Release."

# 2. The deb from the file, and what it placed for the system.
$SUDO apt-get install -y -q "$deb" > "$work/install.log" 2>&1 || { cat "$work/install.log"; fail "apt-get did not install $deb."; }
source=/etc/apt/sources.list.d/jason-desktop.sources
printf 'Types: deb\nURIs: https://get.jasonharness.ai/apt/\nSuites: ./\nSigned-By: /usr/share/keyrings/jason-desktop.gpg\n' | cmp -s - "$source" \
  || { cat "$source" || true; fail "$source is not the source of Jason's repository through the address, signed by the package's key."; }
cmp -s /usr/share/keyrings/jason-desktop.gpg "$repository/install/jason-desktop-archive-keyring.gpg" \
  || fail "/usr/share/keyrings/jason-desktop.gpg is not the repository's key."
dpkg-query -W -f='${Conffiles}\n' jason-desktop | grep -q '^ /etc/apt/apt.conf.d/50jason-desktop ' \
  || fail "/etc/apt/apt.conf.d/50jason-desktop is not a configuration file of the package."
apt-config dump | grep -qF 'Unattended-Upgrade::Origins-Pattern:: "origin=Jason,site=get.jasonharness.ai";' \
  || fail "apt does not read the rule that lets the automatic updates take Jason's."
[ "$(readlink /usr/bin/jason)" = /opt/Jason/resources/bundle/jason ] || fail "/usr/bin/jason is not the jason the package carries."
said=$(env -i HOME="$work" PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin bash -l -c 'jason --version')
[ "${said%%+*}" = "${version//\~/-}" ] || fail "A new login shell's jason says '$said', and the package is $version."
echo "The deb placed Jason's source, its key, the rule for automatic updates and /usr/bin/jason; a new login shell runs jason $said."

# The repository's key, which the package placed, stands for the key this run signed with.
$SUDO cp "$keyring" /usr/share/keyrings/jason-desktop.gpg
echo "::notice title=apt route::This run's index is signed by a key the run made, so /usr/share/keyrings/jason-desktop.gpg holds that key's public half for the rest of the leg."

# 3. apt through the chain.
update_jason() {
  $SUDO apt-get update -q -o Dir::Etc::SourceList="$source" -o Dir::Etc::SourceParts=- -o APT::Get::List-Cleanup=0 > "$work/update.log" 2>&1 \
    && ! grep -qE '^(W|E):' "$work/update.log" || { cat "$work/update.log"; fail "apt-get update did not read Jason's repository through the address without a word."; }
}
update_jason
candidate=$(apt-cache policy jason-desktop | awk '$1 == "Candidate:" { print $2 }')
[ "$candidate" = "$version" ] || fail "apt offers jason-desktop '$candidate' from the repository, and the release is $version."
apt-cache policy jason-desktop | grep -qF '//get.jasonharness.ai/apt ./' || { apt-cache policy jason-desktop; fail "apt does not name the address's repository for jason-desktop."; }
$SUDO apt-get install -y -q --reinstall jason-desktop > "$work/reinstall.log" 2>&1 || { cat "$work/reinstall.log"; fail "apt-get did not install jason-desktop from the repository."; }
grep -q "\"host\": \"assets.example.test\".*\"status\": 200, \"served\": \"v$version/jason-desktop-linux-x64.deb\"" "$work/served.log" \
  || { cat "$work/served.log"; fail "The deb did not come through the chain from the release's asset address."; }
echo "apt read Jason's repository through the address and the chain, offered $version, and installed it from there."

# 4. The purge, and the listing again.
$SUDO apt-get purge -y -q jason-desktop > "$work/purge.log" 2>&1 || { cat "$work/purge.log"; fail "apt-get did not purge jason-desktop."; }
listing > "$work/after"
if ! diff "$work/before" "$work/after" > "$work/difference"; then
  grep -vxE '[<>] d /opt|[0-9,]+[acd][0-9,]+|---' "$work/difference" > "$work/left" || true
  cat "$work/difference"
  [ ! -s "$work/left" ] || fail "The purge left the machine otherwise than before the install (above), besides /opt."
fi
echo "Purged, the places the package and apt write list as they did before the install."
