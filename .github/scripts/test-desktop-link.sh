#!/bin/bash
# `jason ui open` against Jason Desktop on Linux, proven on a deb of this run's: run by CI's install.sh job on
# ubuntu-latest after the apt route, and on any Ubuntu by an account that may use sudo without a password.
#
#   test-desktop-link.sh <folder>
#
# <folder> holds the deb, jason-desktop-linux-x64.deb. The leg installs it with apt, starts a display of its own (Xvfb:
# with no desktop session, xdg-open runs in its generic mode, as it does outside GNOME and KDE) and gives Jason a data
# directory and a harness root of its own. Then, each check failing the leg with one ::error line:
#   1. with nothing of Jason's running, `jason ui open --approval <id>` prints the desktop's address for that approval and
#      hands it on; Jason Desktop starts and records that a link opened the approval's page;
#   2. with Jason Desktop running, `jason ui open --question <id>` does the same for the question's page, through the
#      window that runs;
#   3. `jason ui open --no-open` prints the address of what waits and hands nothing on;
#   4. with the deb's menu entry out of sight (XDG_DATA_HOME and XDG_DATA_DIRS naming an empty folder), `jason ui open`
#      refuses with desktop_not_installed and hands nothing on;
# and no browser runs at any point. The desktop records a link by its page alone, never the address; the id it opened
# is the address `jason ui open` printed. Jason Desktop, the runtime it started, the display and the package are taken
# off on the way out.
set -uo pipefail

[ $# -eq 1 ] || { echo "usage: test-desktop-link.sh <folder holding jason-desktop-linux-x64.deb>" >&2; exit 2; }
deb=$(readlink -f "$1/jason-desktop-linux-x64.deb")
[ -f "$deb" ] || { echo "no $1/jason-desktop-linux-x64.deb" >&2; exit 2; }

fail() { echo "::error title=desktop link::$1"; exit 1; }
SUDO=sudo
[ "$(id -u)" -eq 0 ] && SUDO=

work=$(mktemp -d)
mkdir -p "$work/empty"
export JASON_DATA_DIR=$work/data JASON_Harness__Root=$work/harness DISPLAY=:97
unset WAYLAND_DISPLAY XDG_CURRENT_DESKTOP XDG_SESSION_DESKTOP XDG_SESSION_TYPE DESKTOP_SESSION GNOME_DESKTOP_SESSION_ID KDE_FULL_SESSION BROWSER
xvfb=

cleanup() {
  pkill -f '^/opt/Jason/jason-desktop-app' 2> /dev/null
  [ -x /usr/bin/jason ] && /usr/bin/jason runtime stop > /dev/null 2>&1
  [ -n "$xvfb" ] && kill "$xvfb" 2> /dev/null
  $SUDO apt-get purge -y -qq jason-desktop > /dev/null 2>&1
  rm -rf "$work"
}
trap cleanup EXIT

# Every browser a runner or a desktop carries, and every one xdg-open's generic mode falls back to, by process name.
# Jason Desktop's own processes are named jason-desktop-a and chrome_crashpad, which none of these is.
browsers='firefox|firefox-bin|firefox-esr|chrome|google-chrome|chromium|chromium-browse|msedge|microsoft-edge|epiphany|konqueror|x-www-browser|www-browser|links|links2|elinks|lynx|w3m'
no_browser() { local seen; seen=$(pgrep -a -x "$browsers") && fail "a browser runs after $1: $seen"; return 0; }

# The links Jason Desktop recorded as opened, for one page or for every page.
opened() { cat "$JASON_DATA_DIR"/logs/desktop-*.jsonl 2> /dev/null | grep -F '"@mt":"A link opened the page {Page}"' | grep -c -F "\"Page\":\"${1:-}"; }
wait_opened() {
  for _ in $(seq 1 90); do
    [ "$(opened "$1")" -gt "$2" ] && return 0
    sleep 1
  done
  tail -n 30 "$JASON_DATA_DIR"/logs/desktop-*.jsonl 2> /dev/null
  fail "$3"
}

$SUDO apt-get install -y -qq "$deb" xvfb > "$work/apt.log" 2>&1 || { tail -n 30 "$work/apt.log"; fail "the deb and Xvfb could not be installed."; }
[ -x /usr/bin/jason ] && [ -x /opt/Jason/jason-desktop-app ] || fail "the deb placed no /usr/bin/jason or /opt/Jason/jason-desktop-app."

Xvfb :97 -screen 0 1280x800x24 -nolisten tcp > "$work/xvfb.log" 2>&1 &
xvfb=$!
for _ in $(seq 1 30); do [ -S /tmp/.X11-unix/X97 ] && break; sleep 0.5; done
[ -S /tmp/.X11-unix/X97 ] || { cat "$work/xvfb.log"; fail "Xvfb did not start."; }
no_browser "the display started"

approval=apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6
question=dec_01JBZ3X7Q9K2M4N6P8R0S2T4V6

# 1. Nothing of Jason's runs: the address goes to xdg-open, which starts the deb's entry with `--` and the address. Run so,
# xdg-open waits for the program it started, so `jason ui open` may say its wait ended, and that is no failure.
out=$(/usr/bin/jason ui open --approval "$approval" 2> "$work/open1.err")
code=$?
[ "$code" -eq 0 ] || { cat "$work/open1.err"; fail "jason ui open --approval ended $code: $out"; }
[ "$out" = "{\"address\":\"jason-desktop://attention/approvals/$approval\"}" ] || fail "jason ui open --approval printed: $out"
wait_opened approval 0 "Jason Desktop recorded no link to the approval's page within 90 seconds of jason ui open --approval."
pgrep -f '^/opt/Jason/jason-desktop-app' > /dev/null || fail "no Jason Desktop runs after jason ui open --approval."
no_browser "jason ui open --approval"
echo "1. jason ui open --approval: Jason Desktop started and opened the approval's page. It said: $(cat "$work/open1.err")"

# 2. Jason Desktop runs: the second start hands the address to the window that runs, and ends.
out=$(/usr/bin/jason ui open --question "$question" 2> "$work/open2.err")
code=$?
[ "$code" -eq 0 ] || { cat "$work/open2.err"; fail "jason ui open --question ended $code: $out"; }
[ "$out" = "{\"address\":\"jason-desktop://attention/questions/$question\"}" ] || fail "jason ui open --question printed: $out"
wait_opened question 0 "The running Jason Desktop recorded no link to the question's page within 90 seconds of jason ui open --question."
no_browser "jason ui open --question"
echo "2. jason ui open --question: the running Jason Desktop opened the question's page. It said: $(cat "$work/open2.err")"

# 3. --no-open hands nothing on.
before=$(opened)
out=$(/usr/bin/jason ui open --no-open 2> "$work/open3.err")
code=$?
[ "$code" -eq 0 ] && [ "$out" = '{"address":"jason-desktop://attention"}' ] || fail "jason ui open --no-open ended $code, printing: $out"
sleep 5
[ "$(opened)" -eq "$before" ] || fail "jason ui open --no-open handed its address on."
no_browser "jason ui open --no-open"
echo "3. jason ui open --no-open printed $out and handed nothing on."

# 4. No menu entry declares the scheme: refused in words, nothing handed on.
out=$(XDG_DATA_HOME=$work/empty XDG_DATA_DIRS=$work/empty /usr/bin/jason ui open 2> "$work/open4.err")
code=$?
[ "$code" -eq 1 ] || fail "jason ui open with no Jason Desktop in sight ended $code, not 1: $out"
case "$out" in *'"code":"desktop_not_installed"'*) ;; *) fail "jason ui open with no Jason Desktop in sight printed: $out" ;; esac
sleep 5
[ "$(opened)" -eq "$before" ] || fail "jason ui open with no Jason Desktop in sight handed its address on."
no_browser "jason ui open with no Jason Desktop in sight"
echo "4. jason ui open with no Jason Desktop in sight refused: $(cat "$work/open4.err")"
