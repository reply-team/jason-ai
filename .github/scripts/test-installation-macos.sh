#!/bin/bash
# One step of the macOS proof that Jason installs, updates and uninstalls as one installation, run by CI's union-macos job
# on a build of this tree and by the release's proof-macos job on the signed packages.
#
#   test-installation-macos.sh <step> [arguments]
#
# Each step is one act a person or an agent performs, followed by what it must have left, asserted; a failed assertion
# ends the step non-zero. Nothing here stands in for the product: what an act leaves is read, and what remains after an
# uninstall is compared with a listing taken before anything was installed. The listings are kept under $OUT, which the
# job uploads, and they are what a person reads.
#
#   listing <name>                 the places an installation can touch, written to $OUT/<name>.txt
#   compare <after> <before>       every line equal but those of the data directory and the harness root, which are kept
#   install <feed> <version> <script>
#                                  install.sh from the feed as an agent runs it, in a shell whose PATH does not name
#                                  ~/.local/bin and whose account shell is zsh, as a person's Mac has it: the place, the
#                                  version, jason in a new login shell, the skills records, the person's own skill left
#                                  alone, the role skills; and the machine set-up an application's start runs, which
#                                  writes nothing under a data directory
#   refuse-while-open              jason uninstall while Jason Desktop runs: refused, nothing removed
#   uninstall-terminal             jason uninstall typed in a new login shell, the data directory purged
#   uninstall-menu                 the uninstall the application's Uninstall Jason item starts: the carried jason with
#                                  --wait-for the application's process, which then quits
#   from-previous <previous> <feed> <version>
#                                  the previous release's command-line Jason, installed by its own script, with a runtime
#                                  and a logon registration, updated by its own jason update apply from a served feed
set -euo pipefail

OUT="${OUT:-$RUNNER_TEMP/installation}"
mkdir -p "$OUT"
DATA="$HOME/.jason"
ROOT="$HOME/Jason"
OWN_SKILL="$HOME/.agents/skills/quick-start"
WRAPPER="$HOME/.local/bin/jason"
export JASON_Update__CheckEnabled=false

fail() { echo "::error title=Installation::$1"; exit 1; }
say() {
    echo "$1"
    if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then printf -- '- %s\n' "$1" >> "$GITHUB_STEP_SUMMARY"; fi
}

# Where the application is, the account's own folder first, as install.sh looks.
app() {
    if [ -e "$HOME/Applications/Jason.app" ]; then echo "$HOME/Applications/Jason.app"
    elif [ -e /Applications/Jason.app ]; then echo /Applications/Jason.app
    else echo ""; fi
}
version_of() { sed -n 's/.*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$1/Contents/Resources/bundle/version.json" 2>/dev/null | head -n 1; }

# A login shell as a new terminal starts one: the environment the system gives, not this job's, whose PATH names
# ~/.local/bin already.
fresh() { env -i HOME="$HOME" USER="$USER" LOGNAME="$USER" SHELL=/bin/zsh TERM=xterm-256color PATH=/usr/bin:/bin:/usr/sbin:/sbin /bin/zsh -l -i -c "$1" 2>/dev/null; }

# This job's PATH without ~/.local/bin, which the runner's image puts there and a person's terminal does not have.
plain_path() { printf '%s' "$PATH" | tr ':' '\n' | grep -vx "$HOME/.local/bin" | paste -sd: -; }

wait_until() {
    local seconds="$1" what="$2"; shift 2
    local until=$(( $(date +%s) + seconds ))
    while [ "$(date +%s)" -lt "$until" ]; do
        if "$@"; then return 0; fi
        sleep 0.5
    done
    fail "$what did not happen within $seconds s"
}
gone() { [ ! -e "$1" ]; }

# --- the listing -----------------------------------------------------------------------------------------------------

folder() {
    local label="$1" path="$2"
    if [ ! -e "$path" ]; then printf '%s\tabsent\n' "$label"; return; fi
    printf '%s\tpresent\n' "$label"
    (cd "$path" && find . \( -type f -o -type l \) -print 2>/dev/null | sort | while IFS= read -r file; do
        printf '%s/%s\t%s\n' "$label" "${file#./}" "$(stat -f %z "$file" 2>/dev/null || echo '?')"
    done)
}

write_listing() {
    {
        # The application's bundle is listed by presence and version, not file by file: it is one thing that is there or not.
        for place in /Applications "$HOME/Applications"; do
            if [ -e "$place/Jason.app" ]; then printf 'application %s\t%s\n' "$place" "$(version_of "$place/Jason.app")"; else printf 'application %s\tabsent\n' "$place"; fi
        done
        folder 'local bin' "$HOME/.local/bin"
        for profile in .zshenv .zprofile .zshrc .zlogin .profile .bash_profile .bash_login .bashrc; do
            if [ -e "$HOME/$profile" ]; then printf 'profile %s\t%s\n' "$profile" "$(shasum -a 256 "$HOME/$profile" | cut -d' ' -f1)"; else printf 'profile %s\tabsent\n' "$profile"; fi
        done
        for agent in "$HOME"/Library/LaunchAgents/ai.jason*; do [ -e "$agent" ] && printf 'launch agent file\t%s\n' "${agent#$HOME/}"; done
        launchctl print "gui/$(id -u)" 2>/dev/null | grep -oE 'ai\.jason[A-Za-z0-9.-]*' | sort -u | sed 's/^/launchd job\t/' || true
        folder 'updater cache' "$HOME/Library/Caches/jason-desktop-updater"
        folder 'ShipIt cache' "$HOME/Library/Caches/ai.jasonharness.desktop.ShipIt"
        # What macOS itself keeps for an application that ran, by its bundle id: listed, so a person sees what stays.
        for kept in "Library/Preferences/ai.jasonharness.desktop.plist" "Library/Saved Application State/ai.jasonharness.desktop.savedState" "Library/HTTPStorages/ai.jasonharness.desktop" "Library/Caches/ai.jasonharness.desktop" "Library/Application Support/Jason" "Library/Application Support/jason-desktop" "Library/Logs/Jason"; do
            [ -e "$HOME/$kept" ] && printf 'system-kept\t%s\n' "$kept"
        done
        folder 'claude skills' "$HOME/.claude/skills"
        folder 'agents skills' "$HOME/.agents/skills"
        folder 'data directory' "$DATA"
        folder 'harness root' "$ROOT"
        ps -axo command= | grep -iE '(Jason\.app/|/\.local/bin/jason|/bundle/jason)' | grep -v grep | sed 's/^/process\t/' || true
    } | sort -u > "$1"
    echo "$(wc -l < "$1" | tr -d ' ') lines written to $1"
    sed 's/^/  /' "$1"
}

step="${1:-}"; shift || true
case "$step" in
listing)
    write_listing "$OUT/$1.txt"
    ;;

compare)
    after="$OUT/$1.txt"; before="$OUT/$2.txt"; diffed="$OUT/$2-to-$1.diff.txt"
    kept='^(data directory|harness root)'
    left="$(comm -13 "$before" "$after" | grep -vE "$kept" || true)"
    went="$(comm -23 "$before" "$after" | grep -vE "$kept" || true)"
    { echo "only in $2 (before):"; [ -n "$went" ] && printf '%s\n' "$went" | sed 's/^/- /'; echo "only in $1 (after):"; [ -n "$left" ] && printf '%s\n' "$left" | sed 's/^/+ /'; } > "$diffed" || true
    cat "$diffed"
    keeping="$(comm -13 "$before" "$after" | grep -cE "$kept" || true)"
    say "$1 against $2: $(printf '%s' "$left" | grep -c . || true) line(s) left, $(printf '%s' "$went" | grep -c . || true) gone, beside $keeping line(s) of the data directory and the harness root, which are kept"
    [ -z "$left" ] && [ -z "$went" ] || fail "the machine after '$1' is not the machine before '$2': see $diffed"
    ;;

install)
    feed="$1"; version="$2"; script="$3"
    # The agent's install: the script read by sh, with a terminal's PATH and a zsh account.
    said="$OUT/install.txt"
    status=0
    env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$feed" sh "$script" > "$said" 2>&1 || status=$?
    cat "$said"
    [ "$status" -eq 0 ] || fail "install.sh exited with $status"
    placed="$(app)"
    [ -n "$placed" ] || fail "no Jason.app in /Applications or ~/Applications"
    carried="$placed/Contents/Resources/bundle/jason"
    [ "$(tail -n 1 "$said" | sed 's/.*: //')" = "$WRAPPER" ] || fail "install.sh's last line does not end with the full path of jason, $WRAPPER: $(tail -n 1 "$said")"
    [ "$(version_of "$placed")" = "$version" ] || fail "$placed is $(version_of "$placed"), not $version"
    [ "$("$carried" --version | cut -d+ -f1)" = "$version" ] || fail "the carried jason does not say $version"
    if [ -w /Applications ]; then [ "$placed" = /Applications/Jason.app ] || fail "/Applications is writable, and the application went to $placed"; fi
    say "install.sh put Jason Desktop $version at $placed, opened nothing, and ended on the full path of jason for a shell already open"

    # The terminal command: the wrapper, the profile lines of a zsh account, and a new login shell finds it.
    [ -f "$WRAPPER" ] || fail "no wrapper at $WRAPPER"
    grep -q "$carried" "$WRAPPER" || fail "the wrapper at $WRAPPER does not run $carried"
    grep -qx '# added by jason install' "$HOME/.zprofile" || fail "~/.zprofile carries no line for ~/.local/bin"
    found="$(fresh 'command -v jason; jason --version')"
    [ "$(printf '%s\n' "$found" | sed -n 1p)" = "$WRAPPER" ] || fail "a new login shell finds jason at '$(printf '%s\n' "$found" | sed -n 1p)', not $WRAPPER"
    [ "$(printf '%s\n' "$found" | sed -n 2p | cut -d+ -f1)" = "$version" ] || fail "jason in a new login shell does not say $version: $found"
    say "a new zsh login shell finds jason at $WRAPPER, and it says $version"

    # The skills of both hosts found, each recorded; the person's own skill of a name Jason ships left alone and named.
    for root in .claude/skills .agents/skills; do [ -f "$HOME/$root/.jason-skills.json" ] || fail "no skills record in ~/$root"; done
    [ "$(cat "$OWN_SKILL/SKILL.md")" = "a person's own skill" ] || fail "the person's own skill at $OWN_SKILL was changed"
    if grep -q '"quick-start/' "$HOME/.agents/skills/.jason-skills.json"; then fail "the record in ~/.agents/skills claims the person's own quick-start"; fi
    grep -q 'quick-start' "$said" || fail "the install did not name the person's own quick-start it left alone"
    [ -f "$DATA/skills/roles/.jason-skills.json" ] || fail "no role skills record under $DATA"
    say "the skills of Claude Code and Codex are recorded in both folders, the person's own quick-start is left alone and named, and the role skills are recorded under ~/.jason"

    # The machine set-up an application's start runs: in a home of its own, so that what it writes is all it wrote; nothing
    # under the data directory, so a person's first start still opens on the Demo.
    person="$RUNNER_TEMP/person-start-home"
    rm -rf "$person"; mkdir -p "$person"
    env -u JASON_DATA_DIR HOME="$person" SHELL=/bin/zsh JASON_HARNESS__ROOT="$person/Jason" "$carried" install --terminal-path /usr/bin:/bin > "$OUT/person-start.json" || fail "jason install, as an application's start runs it, failed: $(cat "$OUT/person-start.json")"
    [ ! -e "$person/.jason" ] || fail "jason install with no --roles wrote under the data directory: $(find "$person/.jason" | head -20)"
    say "jason install as an application's start runs it writes no entry under the data directory"
    ;;

refuse-while-open)
    placed="$(app)"; carried="$placed/Contents/Resources/bundle/jason"
    "$placed/Contents/MacOS/Jason" > "$OUT/window-open.log" 2>&1 &
    window=$!
    sleep 8
    status=0
    "$carried" uninstall > "$OUT/refused.json" 2>&1 || status=$?
    cat "$OUT/refused.json"
    kill "$window" 2>/dev/null || true
    wait "$window" 2>/dev/null || true
    [ "$status" -eq 1 ] || fail "jason uninstall exited with $status while Jason Desktop ran, not 1"
    grep -q 'desktop_running' "$OUT/refused.json" || fail "the refusal is not desktop_running"
    [ -e "$placed" ] && [ -f "$WRAPPER" ] || fail "the refused uninstall removed something"
    say "jason uninstall while Jason Desktop runs (a real process) is refused with desktop_running, and nothing is removed"
    ;;

uninstall-terminal)
    placed="$(app)"
    "$placed/Contents/Resources/bundle/jason" runtime start > /dev/null || fail "no runtime could be started before the uninstall"
    status=0
    fresh 'jason uninstall --purge-data --yes' > "$OUT/uninstall-terminal.json" || status=$?
    cat "$OUT/uninstall-terminal.json"
    [ "$status" -eq 0 ] || fail "jason uninstall from a new login shell exited with $status"
    [ ! -e "$placed" ] || fail "$placed is left"
    [ ! -e "$WRAPPER" ] || fail "the wrapper is left"
    if grep -qx '# added by jason install' "$HOME/.zprofile" 2>/dev/null; then fail "~/.zprofile still carries the line"; fi
    [ ! -e "$DATA" ] || fail "~/.jason is left after --purge-data"
    [ -f "$OWN_SKILL/SKILL.md" ] || fail "the person's own skill went with the uninstall"
    say "jason uninstall from a new login shell: the runtime stopped, Jason.app, the wrapper, the profile lines, the skills and the data directory gone, the person's own skill kept"
    ;;

uninstall-menu)
    placed="$(app)"; carried="$placed/Contents/Resources/bundle/jason"
    "$placed/Contents/MacOS/Jason" > "$OUT/window-menu.log" 2>&1 &
    window=$!
    sleep 8
    # What the item starts once the person said Uninstall: the carried jason on its own, waiting for this process, which
    # then quits.
    nohup "$carried" uninstall --wait-for "$window" > "$OUT/uninstall-menu.json" 2>&1 &
    verb=$!
    sleep 2
    kill "$window"
    wait "$window" 2>/dev/null || true
    status=0
    wait "$verb" || status=$?
    cat "$OUT/uninstall-menu.json"
    [ "$status" -eq 0 ] || fail "the uninstall the menu item starts exited with $status"
    [ ! -e "$placed" ] || fail "$placed is left"
    [ -e "$DATA" ] || fail "the data directory went, which only --purge-data takes"
    say "the menu item's uninstall waited for the application to quit, then took Jason.app and everything placed off, the data directory kept"
    ;;

from-previous)
    previous="$1"; feed="$2"; version="$3"
    # The previous release's command-line Jason, by its own script from its own assets, with a terminal's PATH.
    env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$previous" sh "$previous/install.sh" || fail "the previous release's install.sh failed"
    old="$("$WRAPPER" --version | cut -d+ -f1)"
    "$WRAPPER" runtime start > /dev/null || fail "the previous release could not start a runtime"
    "$WRAPPER" autostart enable > "$OUT/autostart.json" || fail "the previous release could not register its logon start: $(cat "$OUT/autostart.json")"
    say "the previous release's command-line Jason $old installed by its own script, with a runtime and a logon registration"

    python3 -m http.server 8099 --bind 127.0.0.1 --directory "$feed" > "$OUT/feed-server.log" 2>&1 &
    server=$!
    trap 'kill "$server" 2>/dev/null || true' EXIT
    wait_until 10 'the feed on 127.0.0.1:8099 answering' curl -fsS -o /dev/null http://127.0.0.1:8099/manifest.json
    status=0
    "$WRAPPER" update apply --feed http://127.0.0.1:8099/manifest.json --drain-seconds 0 > "$OUT/update-from-previous.json" 2>&1 || status=$?
    cat "$OUT/update-from-previous.json"
    [ "$status" -eq 0 ] || fail "the previous release's jason update apply exited with $status"
    say "the previous release's own jason update apply took the feed served on this machine"

    # THE MOVE (the unit that moves a command-line install into the application) is asserted here once it lands: the
    # application in place, ~/.local/bin/jason its wrapper, the runtime running from the carried file, the logon
    # registration naming it, the move's record complete. Until then the update is the command-line update, read back so.
    [ "$("$WRAPPER" --version | cut -d+ -f1)" = "$version" ] || fail "after the update ~/.local/bin/jason does not say $version"
    "$WRAPPER" runtime status > /dev/null || fail "no runtime answers after the update"
    say "the command-line install now says $version and its runtime answers (the move into the application is asserted once it lands)"

    "$WRAPPER" uninstall > "$OUT/uninstall-previous.json" || fail "jason uninstall of the updated command-line install failed: $(cat "$OUT/uninstall-previous.json")"
    ;;

*)
    echo "usage: test-installation-macos.sh listing|compare|install|refuse-while-open|uninstall-terminal|uninstall-menu|from-previous ..." >&2
    exit 2
    ;;
esac
