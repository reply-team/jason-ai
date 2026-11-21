#!/bin/bash
# Jason Desktop's apt route on a machine with apt, proven end to end against a deb of this run's, served the way the
# product's address and GitHub serve a release: run by CI's install.sh job on ubuntu-latest, and on any Ubuntu, by an
# account that may use sudo without a password (the install line refuses root).
#
#   test-apt-route.sh <folder>
#
# <folder> holds the deb, jason-desktop-linux-x64.deb. The leg makes two releases of it: this one (moved up to the
# install line's floor for Linux where it is older), and the next patch, the same package with its version moved on (in
# its control file and its version.json), which is what an update changes for apt. Each gets the index a release attaches, written by write-apt-index.sh and signed with a key the leg
# makes, since a pull request holds no signing key. Nothing in the product carries a switch for this: the machine is
# made to answer for the address instead. Its host name and two stand-ins for GitHub's are mapped to this machine in
# /etc/hosts, and a certificate of the leg's own, which the machine trusts for the run, is served by
# serve-release-chain.py, which answers each hop as the real chain does. The package keeps the source it really writes;
# its key, the repository's, is replaced by the leg's public key once the package is installed, and that is the one
# thing the leg changes in what the package placed, and it says so.
#
# What it proves, in order, each check failing the leg with one ::error line:
#   1. the listing of every place the package and apt write, taken before anything is installed;
#   2. the install line, install.sh, run as the published line runs, against the address: with no terminal and no
#      desktop session to ask a password in, it refuses in its own words and installs nothing; with a terminal it
#      installs the deb by sudo and apt, and the jason the package carries sets the account up, its last line naming
#      /usr/bin/jason. What the package placed: the source naming the address's /apt/ path signed by
#      /usr/share/keyrings/jason-desktop.gpg, that key the repository's, and /usr/bin/jason the jason the package carries,
#      found by name in a new login shell;
#   3. apt reading the repository through the chain, offering the release's version from it, and installing the package
#      from it, the deb taken from the release's opaque asset address;
#   4. the next release published: Ubuntu's automatic updates (unattended-upgrade, the program the daily
#      apt-daily-upgrade unit runs) leave the package where it is, apt offers the next version, and installs it when
#      asked, as a person's `sudo apt upgrade` does; `jason update apply` then refuses with
#      update_managed_by_package_manager, naming apt;
#   5. the one uninstall, `jason uninstall --purge-data --yes` in a terminal, whose package step sudo allows: the
#      package purged and everything the account had of Jason gone, and the listing taken again: the same as before, but
#      for /opt, which dpkg removes once it is empty when no installed package lists it, and the home's .net, the base
#      folder .NET's single-file host makes for every such program of the account, whose folder of this build the
#      uninstall takes.
# Everything it set up for the run (the server, the host names, the certificate, the rule that keeps the automatic
# updates off the machine's own origins for the run) is taken back on its way out.
set -euo pipefail
export LC_ALL=C DEBIAN_FRONTEND=noninteractive

[ $# -eq 1 ] || { echo "usage: test-apt-route.sh <folder holding the deb>"; exit 2; }
folder=$(realpath "$1")
here=$(dirname "$(realpath "${BASH_SOURCE[0]}")")
repository=$(realpath "$here/../..")
deb="$folder/jason-desktop-linux-x64.deb"
SUDO=sudo

fail() { echo "::error title=apt route::$1"; exit 1; }
[ "$(id -u)" -ne 0 ] || fail "Run the leg as an account that may use sudo, not as root: the install line refuses root."

work=$(mktemp -d)
hosts_line="127.0.0.1 get.jasonharness.ai github.example.test assets.example.test # test-apt-route.sh"
no_origins=/etc/apt/apt.conf.d/99zz-test-apt-route
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
  $SUDO rm -f "$no_origins"
  gpgconf --homedir "$work/gpg" --kill all > /dev/null 2>&1 || true
  $SUDO rm -rf "$work"
}
trap cleanup EXIT

# Every place the package, its scripts and apt write for it, and the account's own (its home, which holds its
# configuration, data and cache folders and its systemd user units, and Jason's data directory), as one sorted listing of
# kind and path, and dpkg's word on the package. apt's cache and lists are read for Jason's own files: the install line's
# apt also brings the package's dependencies from Ubuntu's archive where the machine lacks them, and keeps their debs,
# which are the system's and stay.
mkdir -p "$work/home"
places=(/opt /usr/bin /usr/share/applications /usr/share/icons /etc/apt/sources.list.d /etc/apt/apt.conf.d /etc/default
  /usr/share/keyrings /etc/alternatives /var/lib/dpkg/alternatives /etc/apparmor.d /var/cache/apparmor "$work/home"
  "$work/data")
listing() {
  {
    for place in "${places[@]}"; do
      if [ -e "$place" ]; then $SUDO find "$place" -printf '%y %p\n'; fi
    done
    $SUDO find /var/cache/apt/archives /var/lib/apt/lists -iname '*jason*' -printf '%y %p\n' 2> /dev/null || true
  } | sort
  echo "dpkg: $(dpkg-query -W -f='${db:Status-Abbrev}' jason-desktop 2> /dev/null || echo 'not known')"
}

# Ubuntu's automatic updates, which step 4 runs, are installed by default on Ubuntu and not on every CI runner: put here
# where they are missing, before the listing, so they are part of the machine as it was.
dpkg-query -W -f='${db:Status-Abbrev}' unattended-upgrades 2> /dev/null | grep -q '^ii' \
  || $SUDO apt-get install -y -q --no-install-recommends unattended-upgrades > "$work/unattended-upgrades.log" 2>&1 \
  || { cat "$work/unattended-upgrades.log"; fail "unattended-upgrades could not be installed for step 4."; }

# 1. Before.
dpkg-query -W jason-desktop > /dev/null 2>&1 && fail "jason-desktop is known to dpkg before the leg starts; the listing would not be the machine's before."
listing > "$work/before"

# The two releases, this one and the next patch, each with the index a release attaches, signed by the leg's key: an
# Ed25519 key that certifies with a signing subkey, as the repository's is. A release branch builds the number of its
# last tag until its last unit sets the release's own, so this run's deb can be older than the first release the install
# line takes on Linux, the floor it refuses below. The first release is then this deb moved to the floor, its
# pre-release part kept, as the next one is moved on.
built=$(dpkg-deb --field "$deb" Version)
built=${built//\~/-}
floor=$(sed -n 's/^ *if older_than "\$LATEST" \([0-9.]*\) .*/\1/p' "$repository/install/install.sh")
[ -n "$floor" ] || fail "install.sh names no floor for Linux in the line that refuses an older release."
release=$built
if dpkg --compare-versions "${built%%[-+]*}" lt "$floor"; then release="$floor${built#"${built%%[-+]*}"}"; fi
core=${release%%[-+]*}
next="${core%.*}.$((${core##*.} + 1))"
mkdir "$work/repack"
repack() {
  local to=$1 root="$work/repack/$1"
  dpkg-deb -R "$deb" "$root"
  sed -i "s/^Version: .*/Version: ${to//-/\~}/" "$root/DEBIAN/control"
  printf '{\n  "version": "%s"\n}\n' "$to" > "$root/opt/Jason/resources/bundle/version.json"
  dpkg-deb --root-owner-group -Zgzip -b "$root" "$work/repack/$to.deb" > /dev/null
}
first=$deb
if [ "$release" != "$built" ]; then repack "$release"; first="$work/repack/$release.deb"; fi
version=$(dpkg-deb --field "$first" Version)
mkdir -m 700 "$work/gpg"
export GNUPGHOME="$work/gpg"
gpg --batch --passphrase '' --quick-generate-key 'Jason apt index, this leg alone' ed25519 cert 1d 2> /dev/null
fpr=$(gpg --batch --with-colons --list-keys | awk -F: '$1 == "fpr" { print $10; exit }')
gpg --batch --passphrase '' --quick-add-key "$fpr" ed25519 sign 1d 2> /dev/null
gpg --batch --yes --output "$work/key.gpg" --export "$fpr"
secret=$(gpg --armor --export-secret-subkeys "$fpr")
publish() {
  local tag=$1 file=$2
  mkdir -p "$work/releases/v$tag"
  cp "$file" "$work/releases/v$tag/jason-desktop-linux-x64.deb"
  APT_SIGNING_KEY="$secret" bash "$here/write-apt-index.sh" "$work/releases/v$tag" > /dev/null
  printf '{\n  "schema": 1,\n  "version": "%s"\n}\n' "$tag" > "$work/releases/v$tag/manifest.json"
  (cd "$work/releases/v$tag" && sha256sum jason-desktop-linux-x64.deb > checksums.txt)
}
publish "$release" "$first"
repack "$next"
publish "$next" "$work/repack/$next.deb"
unset GNUPGHOME
echo "$release" > "$work/releases/latest"
echo "Two releases made of this run's deb, $built: $release and $next, each with its index signed by the leg's key ($fpr)."

# The chain, for the address and GitHub's hosts.
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj "/CN=test-apt-route CA" -keyout "$work/ca.key" -out "$work/ca.crt" \
  -addext basicConstraints=critical,CA:TRUE -addext keyUsage=critical,keyCertSign > /dev/null 2>&1
openssl req -newkey rsa:2048 -nodes -subj "/CN=get.jasonharness.ai" -keyout "$work/server.key" -out "$work/server.csr" > /dev/null 2>&1
printf 'subjectAltName=DNS:get.jasonharness.ai,DNS:github.example.test,DNS:assets.example.test\n' > "$work/san"
openssl x509 -req -in "$work/server.csr" -CA "$work/ca.crt" -CAkey "$work/ca.key" -CAcreateserial -days 1 -extfile "$work/san" -out "$work/server.crt" > /dev/null 2>&1
$SUDO cp "$work/ca.crt" /usr/local/share/ca-certificates/test-apt-route.crt
$SUDO update-ca-certificates > /dev/null 2>&1
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

# 2. The install line, as the published one runs it, against the address, as an account of its own: a home, a data
# directory and a harness root of the leg's, and the account's own folders under that home. First with no terminal
# (setsid leaves it none) and no desktop session: refused, nothing installed. Then in a terminal (script gives it one),
# where sudo asks for the password, which this account does not need.
person=(env HOME="$work/home" XDG_DATA_HOME="$work/home/.local/share" XDG_CONFIG_HOME="$work/home/.config"
  JASON_DATA_DIR="$work/data" JASON_Harness__Root="$work/harness" JASON_Update__CheckEnabled=false)
status=0
"${person[@]}" setsid -w sh "$repository/install/install.sh" < /dev/null > "$work/refused.out" 2>&1 || status=$?
cat "$work/refused.out"
[ "$status" -ne 0 ] || fail "With no terminal and no desktop session the install line ended 0."
grep -qF "this session has no way to ask for it: run the line in a terminal of your desktop" "$work/refused.out" \
  || fail "With no terminal and no desktop session the install line did not refuse in its own words."
dpkg-query -W jason-desktop > /dev/null 2>&1 && fail "The refused install line left jason-desktop known to dpkg."
echo "With no terminal and no desktop session the install line refused, saying why, and installed nothing."
"${person[@]}" script -qec "sh '$repository/install/install.sh'" /dev/null < /dev/null > "$work/install.out" 2>&1 \
  || { cat "$work/install.out"; fail "The install line ended non-zero in a terminal."; }
cat "$work/install.out"
tail -n 1 "$work/install.out" | tr -d '\r' | grep -q 'call it by its full path: /usr/bin/jason$' \
  || fail "The install line's last line does not name /usr/bin/jason."
source=/etc/apt/sources.list.d/jason-desktop.sources
printf 'Types: deb\nURIs: https://get.jasonharness.ai/apt/\nSuites: ./\nSigned-By: /usr/share/keyrings/jason-desktop.gpg\n' | cmp -s - "$source" \
  || { cat "$source" || true; fail "$source is not the source of Jason's repository through the address, signed by the package's key."; }
cmp -s /usr/share/keyrings/jason-desktop.gpg "$repository/install/jason-desktop-archive-keyring.gpg" \
  || fail "/usr/share/keyrings/jason-desktop.gpg is not the repository's key."
[ "$(readlink /usr/bin/jason)" = /opt/Jason/resources/bundle/jason ] || fail "/usr/bin/jason is not the jason the package carries."
said=$(env -i HOME="$work/home" PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin bash -l -c 'jason --version')
[ "${said%%+*}" = "$built" ] || fail "A new login shell's jason says '$said', and this run's build is $built."
echo "The deb placed Jason's source, its key and /usr/bin/jason; a new login shell runs jason $said."

# The start at logon, which the set-up the install line ends in registers: a user unit naming the jason the package
# carries where the runner's systemd user manager reads the leg's home, and otherwise the set-up's own sentence that the
# system would not register it, logon refused in its report, never a failed install.
"${person[@]}" /usr/bin/jason runtime autostart status > "$work/autostart.json" 2> "$work/autostart.err" || true
if grep -qF '"registered":true' "$work/autostart.json"; then
  grep -qF '"executable":"/opt/Jason/resources/bundle/jason"' "$work/autostart.json"     || fail "The start at logon names another jason than the package's: $(cat "$work/autostart.json")"
  echo "Branch: registered. The set-up registered the start at logon, the user unit naming /opt/Jason/resources/bundle/jason."
elif ! "${person[@]}" systemctl --user is-system-running > "$work/user-manager.txt" 2>&1; then
  # Only a runner with no systemd user manager may end without the unit: the set-up's sentence names systemctl and what
  # it could not reach, and nothing is registered.
  said=$(grep -oE 'The start at logon could not be registered[^"]*' "$work/install.out" || true)
  case "$said" in *systemctl*) ;; *) said="" ;; esac
  printf '%s' "$said" | grep -qiE 'bus|manager' \
    || { cat "$work/install.out" "$work/user-manager.txt"; fail "The runner has no systemd user manager, and the set-up did not say systemctl could not reach it: '$said'"; }
  grep -qF '"registered":false' "$work/autostart.json" \
    || { cat "$work/autostart.json" "$work/autostart.err"; fail "With no systemd user manager, jason runtime autostart status does not say registered false."; }
  echo "Branch: no user manager. The runner has no systemd user manager ($(head -n 1 "$work/user-manager.txt")), and the set-up said so in its report, registered nothing and ended 0."
elif ! "${person[@]}" systemctl --user show -p UnitPath --value 2> /dev/null | tr ' ' '\n' | grep -qxF "$work/home/.config/systemd/user"; then
  # The leg's account is a home of its own under the runner's account, and the runner's user manager reads its units from
  # the runner's home, not this one: the unit the set-up writes there is one the manager cannot see, so its enable is
  # refused. The set-up must say that refusal under left, report logon refused, end 0, and put the unit file back.
  "${person[@]}" systemctl --user show -p UnitPath --value > "$work/unit-path.txt" 2>&1 || true
  said=$(grep -oE 'The start at logon could not be registered[^"]*' "$work/install.out" || true)
  case "$said" in *"systemctl --user refused: Failed to enable unit: Unit jason.service does not exist. "*) ;; *)
    cat "$work/install.out" "$work/unit-path.txt"
    fail "The runner's user manager does not read the leg's home, and the set-up did not say systemctl refused the unit it cannot see: '$said'" ;;
  esac
  grep -qF '"logon":"refused"' "$work/install.out" \
    || { cat "$work/install.out"; fail "The runner's user manager does not read the leg's home, and the set-up's report does not say logon refused."; }
  grep -qF '"registered":false' "$work/autostart.json" \
    || { cat "$work/autostart.json" "$work/autostart.err"; fail "With the unit refused, jason runtime autostart status does not say registered false."; }
  [ ! -e "$work/home/.config/systemd/user/jason.service" ] \
    || fail "The refused registration left $work/home/.config/systemd/user/jason.service behind."
  echo "Branch: another home. The runner's user manager reads its units from $(cat "$work/unit-path.txt"), not the leg's home: the set-up said systemctl refused the unit, reported logon refused, left no unit file and ended 0."
else
  cat "$work/autostart.json" "$work/autostart.err" "$work/install.out"; fail "The runner has a systemd user manager, and the set-up registered no start at logon."
fi

# The repository's key, which the package placed, stands for the leg's from here on.
trust_the_leg() { $SUDO cp "$work/key.gpg" /usr/share/keyrings/jason-desktop.gpg; }
trust_the_leg
echo "::notice title=apt route::The leg's indexes are signed by a key it made, so /usr/share/keyrings/jason-desktop.gpg holds that key's public half for the rest of the leg."

# 3. apt through the chain.
update_jason() {
  $SUDO apt-get update -q -o Dir::Etc::SourceList="$source" -o Dir::Etc::SourceParts=- -o APT::Get::List-Cleanup=0 > "$work/update.log" 2>&1 \
    && ! grep -qE '^(W|E):' "$work/update.log" || { cat "$work/update.log"; fail "apt-get update did not read Jason's repository through the address without a word."; }
}
installed() { dpkg-query -W -f='${Version}' jason-desktop; }
update_jason
candidate=$(apt-cache policy jason-desktop | awk '$1 == "Candidate:" { print $2 }')
[ "$candidate" = "$version" ] || fail "apt offers jason-desktop '$candidate' from the repository, and the release is $version."
apt-cache policy jason-desktop | grep -qF '//get.jasonharness.ai/apt ./' || { apt-cache policy jason-desktop; fail "apt does not name the address's repository for jason-desktop."; }
# Only what apt asks from here on counts: the install line fetched the same deb through the same hops above.
asked=$(wc -l < "$work/served.log")
$SUDO apt-get install -y -q --reinstall jason-desktop > "$work/reinstall.log" 2>&1 || { cat "$work/reinstall.log"; fail "apt-get did not install jason-desktop from the repository."; }
grep -qxF 'Open Jason Desktop to start Jason.' "$work/reinstall.log"   || { cat "$work/reinstall.log"; fail "The deb installed by apt did not end with its one line, Open Jason Desktop to start Jason."; }
echo "The deb installed by apt ends with its line: Open Jason Desktop to start Jason."
tail -n "+$((asked + 1))" "$work/served.log" \
  | grep -q "\"host\": \"assets.example.test\".*\"status\": 200, \"agent\": \"Debian APT[^\"]*\", \"served\": \"v$release/jason-desktop-linux-x64.deb\"" \
  || { cat "$work/served.log"; fail "apt's own download of the deb did not come through the chain from the release's asset address."; }
trust_the_leg
echo "apt read Jason's repository through the address and the chain, offered $version, and installed it from there."

# 4. The next release, offered and not taken on its own: Ubuntu's automatic updates, run as the daily run runs them
# (unattended-upgrade), with the machine's own origins kept out for this run so the leg upgrades nothing else of the
# machine's, leave the package where it is; apt offers the next version, and installs it when asked, as a person's
# `sudo apt upgrade` does, for this one package.
echo "$next" > "$work/releases/latest"
update_jason
automatic=$(apt-config dump | grep -E '^Unattended-Upgrade::' || true)
if grep -qiE 'jason|get\.jasonharness\.ai' <<< "$automatic"; then
  echo "$automatic"; fail "apt's configuration names Jason for the automatic updates."
fi
printf '#clear Unattended-Upgrade::Allowed-Origins;\nUnattended-Upgrade::Allowed-Origins {};\n' | $SUDO tee "$no_origins" > /dev/null
$SUDO unattended-upgrade -v > "$work/automatic.log" 2>&1 || { cat "$work/automatic.log"; fail "unattended-upgrade failed."; }
[ "$(installed)" = "$version" ] || { cat "$work/automatic.log"; fail "Ubuntu's automatic updates installed jason-desktop $(installed) on their own; Jason's updates are offered, and installed when the person says so."; }
offered=$(apt list --upgradable 2> /dev/null || true)
grep -q "^jason-desktop/.* $next " <<< "$offered" || { echo "$offered"; fail "apt does not offer jason-desktop $next as an upgrade."; }
$SUDO apt-get install -y -q --only-upgrade jason-desktop > "$work/upgrade.log" 2>&1 || { cat "$work/upgrade.log"; fail "apt-get did not upgrade jason-desktop."; }
[ "$(installed)" = "$next" ] || { cat "$work/upgrade.log"; fail "apt left jason-desktop at $(installed), and the repository offers $next."; }
grep -q "\"version\": \"$next\"" /opt/Jason/resources/bundle/version.json || fail "The package's version.json does not name $next after the update."
echo "Ubuntu's automatic updates left the package at $version; apt offered $next and installed it when asked."
status=0
"${person[@]}" /usr/bin/jason update apply > "$work/apply.json" 2>&1 || status=$?
cat "$work/apply.json"
[ "$status" -eq 1 ] && grep -qF '"code":"update_managed_by_package_manager"' "$work/apply.json" && grep -qF 'sudo apt update' "$work/apply.json" \
  || fail "jason update apply on the deb did not refuse with update_managed_by_package_manager, naming apt."
echo "jason update apply on the deb refuses, naming apt."

# 5. The one uninstall, in a terminal, where sudo takes the package off, and the listing again.
$SUDO rm -f "$no_origins"
"${person[@]}" script -qec "/usr/bin/jason uninstall --purge-data --yes" /dev/null < /dev/null > "$work/uninstall.out" 2>&1 \
  || { cat "$work/uninstall.out"; fail "jason uninstall --purge-data --yes ended non-zero."; }
cat "$work/uninstall.out"
dpkg-query -W jason-desktop > /dev/null 2>&1 && [ "$(dpkg-query -W -f='${db:Status-Abbrev}' jason-desktop)" != "un " ] \
  && fail "jason uninstall left jason-desktop known to dpkg as $(dpkg-query -W -f='${db:Status-Abbrev}' jason-desktop)."
listing > "$work/after"
if ! diff "$work/before" "$work/after" > "$work/difference"; then
  grep -vxE "[<>] d /opt|> d $work/home/.net|[0-9,]+[acd][0-9,]+|---" "$work/difference" > "$work/left" || true
  cat "$work/difference"
  [ ! -s "$work/left" ] || fail "The uninstall left the machine otherwise than before the install (above), besides /opt and the home's .net."
fi
echo "Uninstalled, the places the package, apt and the account write list as they did before the install."
