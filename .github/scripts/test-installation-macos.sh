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
#   requirement <zip>              install.sh's APP_REQUIREMENT against a published application: admitted, and refused for another team
#   held                           fails when any compare above found the machine after an uninstall not the one before
#   assess                         Gatekeeper's assessment of the placed application at its final path: accepted, as a
#                                  notarized Developer ID application (the release's packages alone)
#   quarantine <feed> <copy>       a copy of the feed whose desktop archives carry the quarantine attribute a browser
#                                  writes on what it downloads
#   intel <zip> <version>          the x64 application put in place by plain steps, as install.sh refuses an Intel Mac:
#                                  its signature, Gatekeeper's assessment, its jason and runtime, its window, its uninstall
set -euo pipefail

OUT="${OUT:-$RUNNER_TEMP/installation}"
mkdir -p "$OUT"
DATA="$HOME/.jason"
ROOT="$HOME/Jason"
OWN_SKILL="$HOME/.agents/skills/quick-start"
WRAPPER="$HOME/.local/bin/jason"
export JASON_Update__CheckEnabled=false

fail() { echo "::error title=Installation::$1"; exit 1; }

# A failed assertion that does not keep the legs after it from being read: said now as an error, recorded, and failed by
# the held step at the end with its own words.
fail_held() { echo "::error title=Installation::$1"; printf '%s\n' "$1" >> "$OUT/failed-legs.txt"; }

# Every process of Jason's, of its installer and of its uninstaller: what a wait that reached its bound was waiting on.
jason_processes() { ps -axo pid,ppid,etime,command | grep -iE 'jason|install\.sh|uninstall' | grep -v grep || true; }

# A command run to its end, or to its bound: at the bound what it waited for is named on stderr, with every process of
# Jason's, and the command is ended and the step fails. Its exit otherwise.
within() {
    local seconds="$1" what="$2"; shift 2
    "$@" &
    local pid=$!
    local until=$(( $(date +%s) + seconds ))
    while kill -0 "$pid" 2>/dev/null; do
        if [ "$(date +%s)" -ge "$until" ]; then
            echo "::error title=Installation::$what did not end within $seconds s" >&2
            jason_processes >&2
            kill -TERM "$pid" 2>/dev/null || true; sleep 2; kill -KILL "$pid" 2>/dev/null || true
            exit 1
        fi
        sleep 0.5
    done
    wait "$pid"
}

# A process told to quit and given 30 s to, then ended; said where it had to be.
end_process() {
    local pid="$1" what="$2"
    kill "$pid" 2>/dev/null || true
    local until=$(( $(date +%s) + 30 ))
    while kill -0 "$pid" 2>/dev/null && [ "$(date +%s)" -lt "$until" ]; do sleep 0.5; done
    if kill -0 "$pid" 2>/dev/null; then echo "$what had not quit 30 s after it was told to, and is ended"; kill -KILL "$pid" 2>/dev/null || true; fi
    wait "$pid" 2>/dev/null || true
}
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

# launchd's record of every ai.jason label enabled or disabled for this account, in the user-login domain (gui/<uid>) and
# the per-user domain (user/<uid>), one line each: domain, label, state. `load -w` and `unload -w` write it, and
# `launchctl enable` and `disable`; it outlives the job and its file.
launchd_records() {
    for domain in gui user; do
        launchctl print-disabled "$domain/$(id -u)" 2>/dev/null | awk -v domain="$domain" '
            match($0, /ai\.jason[A-Za-z0-9.-]*/) {
                label = substr($0, RSTART, RLENGTH); state = $0
                sub(/.*=>[[:space:]]*/, "", state); gsub(/[[:space:]]/, "", state)
                print domain "\t" label " " state
            }'
    done
}

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
        # launchd, read two ways that are kept apart: a job loaded in this account's domain (its label answering a print of
        # its own), and launchd's record of a label enabled or disabled, in both domains (launchd_records).
        {
        launchctl print "gui/$(id -u)/ai.jason.runtime" > /dev/null 2>&1 && printf 'launchd job loaded\tai.jason.runtime\n'
        launchctl print "gui/$(id -u)" 2>/dev/null | grep -oE 'ai\.jason[A-Za-z0-9.-]*' | sort -u | while read -r label; do
            if launchctl print "gui/$(id -u)/$label" > /dev/null 2>&1; then printf 'launchd job loaded\t%s\n' "$label"; fi
        done
        launchd_records | sed 's/^/launchd record /'
        } | sort -u || true
        folder 'updater cache' "$HOME/Library/Caches/jason-desktop-updater"
        folder 'ShipIt cache' "$HOME/Library/Caches/ai.jasonharness.desktop.ShipIt"
        # What macOS and Electron keep for an application that ran, by its bundle id or its name, in every place of
        # ~/Library they keep such things: listed, so a person sees what stays. The jobs open the window before each
        # uninstall, so these places are written as a person's are.
        for parent in Preferences "Saved Application State" HTTPStorages Caches "Application Support" Logs WebKit Cookies; do
            [ -d "$HOME/Library/$parent" ] || continue
            find "$HOME/Library/$parent" -maxdepth 1 -iname '*jason*' 2>/dev/null | while IFS= read -r kept; do printf 'system-kept\t%s\n' "${kept#$HOME/}"; done
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
stand-in)
    # A copy of install.sh whose one line naming what a signature must satisfy names this test build's own ad hoc
    # signature instead of the release's team, and differs from install.sh in nothing else: counted, or nothing is written.
    app="$1"; copy="$2"
    line="$(grep -n 'anchor apple generic' install/install.sh || true)"
    [ "$(printf '%s\n' "$line" | grep -c .)" = 1 ] || fail "install.sh does not name what a signature must satisfy in exactly one line"
    number="${line%%:*}"
    text="${line#*:}"
    printf '%s\n' "$text" | grep -Eq "^[A-Z_]+='[^'\$]*'\$" || fail "install.sh names what a signature must satisfy in a line that is not one plain assignment: $text"
    cdhash="$(within 120 'codesign naming the CDHash' codesign --display --verbose=3 "$app" 2>&1 | sed -n 's/^CDHash=//p' | head -n 1)"
    [ -n "$cdhash" ] || fail "codesign names no CDHash for $app"
    # This build's own code, by its hash: the one requirement its ad hoc signature satisfies.
    standin="${text%%=*}='cdhash H\"$cdhash\"'"
    awk -v n="$number" -v s="$standin" 'NR == n { print s; next } { print }' install/install.sh > "$copy"
    diff install/install.sh "$copy" || true
    [ "$(diff install/install.sh "$copy" | grep -c '^[<>]' || true)" = 2 ] || fail "the copy differs from install.sh in more than the one line naming the signature"
    say "install.sh refused the ad hoc test build; the copy run here differs from it in line $number alone, naming this build's own signature"
    ;;

listing)
    write_listing "$OUT/$1.txt"
    ;;

compare)
    after="$OUT/$1.txt"; before="$OUT/$2.txt"; diffed="$OUT/$2-to-$1.diff.txt"
    kept='^(data directory|harness root)'
    left="$(comm -13 "$before" "$after" | grep -vE "$kept" || true)"
    went="$(comm -23 "$before" "$after" | grep -vE "$kept" || true)"
    # After the previous release's command-line install alone, an expected leftover: the profiles 0.48's install.sh
    # created, which no record of this version's names, emptied of their lines. Said, and not counted.
    if [ "${3:-}" = previous-profiles ]; then
        empty="$(printf '' | shasum -a 256 | cut -d' ' -f1)"
        expected_left="$(printf '%s\n' "$left" | grep -E "^profile \.(zprofile|zshrc)	$empty\$" || true)"
        if [ -n "$expected_left" ]; then
            say "expected after the previous release's own install.sh: $(printf '%s' "$expected_left" | tr '\n\t' '; ') (empty profiles it created)"
            for name in $(printf '%s\n' "$expected_left" | sed -E 's/^profile (\.[a-z]+)	.*/\1/'); do
                left="$(printf '%s\n' "$left" | grep -vE "^profile \\$name	" || true)"
                went="$(printf '%s\n' "$went" | grep -vE "^profile \\$name	absent\$" || true)"
            done
        fi
    fi
    { echo "only in $2 (before):"; [ -n "$went" ] && printf '%s\n' "$went" | sed 's/^/- /'; echo "only in $1 (after):"; [ -n "$left" ] && printf '%s\n' "$left" | sed 's/^/+ /'; } > "$diffed" || true
    cat "$diffed"
    keeping="$(comm -13 "$before" "$after" | grep -cE "$kept" || true)"
    say "$1 against $2: $(printf '%s' "$left" | grep -c . || true) line(s) left, $(printf '%s' "$went" | grep -c . || true) gone, beside $keeping line(s) of the data directory and the harness root, which are kept"
    # A divergence is recorded and the job goes on to its next leg, so that one run reads every leg; the held step at the
    # end fails the job on any divergence recorded.
    if [ -n "$left" ] || [ -n "$went" ]; then
        echo "::error title=Uninstall::the machine after '$1' is not the machine before '$2': $(cat "$diffed" | tr '\n' ' ')"
        echo "$1 against $2: see $diffed" >> "$OUT/divergent.txt"
    fi
    ;;

held)
    [ ! -s "$OUT/failed-legs.txt" ] || fail "$(tr '\n' ' ' < "$OUT/failed-legs.txt")$( [ -s "$OUT/divergent.txt" ] && printf '| the machine after an uninstall was not the machine before it: %s' "$(tr '\n' ' ' < "$OUT/divergent.txt")")"
    [ ! -s "$OUT/divergent.txt" ] || fail "the machine after an uninstall was not the machine before it: $(tr '\n' ' ' < "$OUT/divergent.txt")"
    say "the machine after every uninstall was the machine before it, the data directory and the harness root aside"
    ;;

install)
    feed="$1"; version="$2"; script="$3"
    # The agent's install: the script read by sh, with a terminal's PATH and a zsh account.
    said="$OUT/install.txt"
    status=0
    within 300 'install.sh' env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$feed" sh "$script" > "$said" 2>&1 || status=$?
    cat "$said"
    [ "$status" -eq 0 ] || fail "install.sh exited with $status"
    placed="$(app)"
    [ -n "$placed" ] || fail "no Jason.app in /Applications or ~/Applications"
    carried="$placed/Contents/Resources/bundle/jason"
    [ "$(tail -n 1 "$said" | sed 's/.*: //')" = "$WRAPPER" ] || fail "install.sh's last line does not end with the full path of jason, $WRAPPER: $(tail -n 1 "$said")"
    [ "$(version_of "$placed")" = "$version" ] || fail "$placed is $(version_of "$placed"), not $version"
    [ "$(within 60 'the carried jason --version' "$carried" --version | cut -d+ -f1)" = "$version" ] || fail "the carried jason does not say $version"
    if [ -w /Applications ]; then [ "$placed" = /Applications/Jason.app ] || fail "/Applications is writable, and the application went to $placed"; fi
    say "install.sh put Jason Desktop $version at $placed, opened nothing, and ended on the full path of jason for a shell already open"

    # The terminal command: the wrapper, the profile lines of a zsh account, and a new login shell finds it.
    [ -f "$WRAPPER" ] || fail "no wrapper at $WRAPPER"
    grep -q "$carried" "$WRAPPER" || fail "the wrapper at $WRAPPER does not run $carried"
    grep -qx '# added by jason install' "$HOME/.zprofile" || fail "~/.zprofile carries no line for ~/.local/bin"
    found="$(within 60 'a new login shell finding jason' fresh 'command -v jason; jason --version')"
    [ "$(printf '%s\n' "$found" | sed -n 1p)" = "$WRAPPER" ] || fail "a new login shell finds jason at '$(printf '%s\n' "$found" | sed -n 1p)', not $WRAPPER"
    [ "$(printf '%s\n' "$found" | sed -n 2p | cut -d+ -f1)" = "$version" ] || fail "jason in a new login shell does not say $version: $found"
    say "a new zsh login shell finds jason at $WRAPPER, and it says $version"

    # The skills of both hosts found, each recorded; the person's own skill of a name Jason ships left alone and named.
    for root in .claude/skills .agents/skills; do [ -f "$HOME/$root/.jason-skills.json" ] || fail "no skills record in ~/$root"; done
    [ -f "$DATA/skills/roles/.jason-skills.json" ] || fail "no role skills record under $DATA"
    if grep -q "person's own" "$OUT/failed-legs.txt" 2>/dev/null; then
        say "the skills of Claude Code and Codex are recorded in both folders and the role skills under ~/.jason; the person's own quick-start was lost in a leg before, as recorded, so this install's handling of it is not seen"
    elif [ -f "$OWN_SKILL/SKILL.md" ]; then
        [ "$(cat "$OWN_SKILL/SKILL.md")" = "a person's own skill" ] || fail "the person's own skill at $OWN_SKILL was changed"
        if grep -q '"quick-start/' "$HOME/.agents/skills/.jason-skills.json"; then fail "the record in ~/.agents/skills claims the person's own quick-start"; fi
        grep -q 'quick-start' "$said" || fail "the install did not name the person's own quick-start it left alone"
        say "the skills of Claude Code and Codex are recorded in both folders, the person's own quick-start is left alone and named, and the role skills are recorded under ~/.jason"
    else
        say "the skills of Claude Code and Codex are recorded in both folders and the role skills under ~/.jason; the person's own quick-start is gone already, as said above, so this install's handling of it is not seen"
    fi

    # The machine set-up an application's start runs: in a home of its own, so that what it writes is all it wrote; nothing
    # under the data directory, so a person's first start still opens on the Demo.
    person="$RUNNER_TEMP/person-start-home"
    rm -rf "$person"; mkdir -p "$person"
    within 120 'jason install as an application start runs it' env -u JASON_DATA_DIR HOME="$person" SHELL=/bin/zsh JASON_HARNESS__ROOT="$person/Jason" "$carried" install --terminal-path /usr/bin:/bin > "$OUT/person-start.json" || fail "jason install, as an application's start runs it, failed: $(cat "$OUT/person-start.json")"
    [ ! -e "$person/.jason" ] || fail "jason install with no --roles wrote under the data directory: $(find "$person/.jason" | head -20)"
    say "jason install as an application's start runs it writes no entry under the data directory"
    ;;

refuse-while-open)
    placed="$(app)"; carried="$placed/Contents/Resources/bundle/jason"
    record="$HOME/.agents/skills/.jason-skills.json"
    before="$(stat -f %m "$record" 2>/dev/null || echo none)"
    "$placed/Contents/MacOS/Jason" > "$OUT/window-open.log" 2>&1 &
    window=$!
    # The application's first start sets the machine up (jason install --terminal-path): the skills record and the
    # person's own skill read once the record was written again, or at 60 s.
    waited=0
    while [ "$waited" -lt 120 ] && [ "$(stat -f %m "$record" 2>/dev/null || echo none)" = "$before" ]; do sleep 0.5; waited=$((waited + 1)); done
    if [ "$(stat -f %m "$record" 2>/dev/null || echo none)" = "$before" ]; then moment='not written again within 60 s of the start'; else moment="written again $((waited / 2)) s after the start"; fi
    sleep 2
    claims="$(grep -o '"quick-start/[^"]*"' "$record" 2>/dev/null | sort -u | tr '\n' ' ' || true)"
    own="$(cat "$OWN_SKILL/SKILL.md" 2>/dev/null || echo '(absent)')"
    say "after the application's first start the record in ~/.agents/skills was $moment; it names quick-start in: ${claims:-no line}; the person's own SKILL.md reads: $own"
    if [ -n "$claims" ] || [ "$own" != "a person's own skill" ]; then fail_held "the application's first start took the person's own quick-start: the record names it in ${claims:-no line}, and the file reads '$own'"; fi
    say "launchd's records after the application's first start, before any uninstall: $(launchd_records | tr '\t\n' ' ;' || true)"
    status=0
    within 120 'jason uninstall while the application runs' "$carried" uninstall > "$OUT/refused.json" 2>&1 || status=$?
    cat "$OUT/refused.json"
    end_process "$window" 'the application'
    [ "$status" -eq 1 ] || fail "jason uninstall exited with $status while Jason Desktop ran, not 1"
    grep -q 'desktop_running' "$OUT/refused.json" || fail "the refusal is not desktop_running"
    [ -e "$placed" ] && [ -f "$WRAPPER" ] || fail "the refused uninstall removed something"
    say "jason uninstall while Jason Desktop runs (a real process) is refused with desktop_running, and nothing is removed"
    ;;

uninstall-terminal)
    placed="$(app)"; carried="$placed/Contents/Resources/bundle/jason"
    # A runtime for the uninstall to stop: the one the application's start left running, once it answers, else one started.
    sleep 5
    if ! within 60 'jason runtime status' "$carried" runtime status > /dev/null 2>&1; then
        within 120 'jason runtime start' "$carried" runtime start > "$OUT/runtime-start.txt" 2>&1 || { cat "$OUT/runtime-start.txt"; ps -axo pid,command | grep -i jason | grep -v grep || true; fail "no runtime could be started before the uninstall"; }
    fi
    within 60 'jason runtime status' "$carried" runtime status > /dev/null 2>&1 || fail "no runtime answers before the uninstall"
    status=0
    within 300 'jason uninstall from a new login shell' fresh 'jason uninstall --purge-data --yes' > "$OUT/uninstall-terminal.json" || status=$?
    cat "$OUT/uninstall-terminal.json"
    [ "$status" -eq 0 ] || fail "jason uninstall from a new login shell exited with $status"
    [ ! -e "$placed" ] || fail "$placed is left"
    [ ! -e "$WRAPPER" ] || fail "the wrapper is left"
    if grep -qx '# added by jason install' "$HOME/.zprofile" 2>/dev/null; then fail "~/.zprofile still carries the line"; fi
    [ ! -e "$DATA" ] || fail "~/.jason is left after --purge-data"
    [ -f "$OWN_SKILL/SKILL.md" ] || fail_held "the person's own skill went with the uninstall"
    say "launchd's records after this build's jason uninstall from a new login shell: $(launchd_records | tr '\t\n' ' ;' || true)"
    say "jason uninstall from a new login shell: the runtime stopped, Jason.app, the wrapper, the profile lines, the skills and the data directory gone"
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
    end_process "$window" 'the application'
    waited=0
    while kill -0 "$verb" 2>/dev/null && [ "$waited" -lt 600 ]; do sleep 0.5; waited=$((waited + 1)); done
    if kill -0 "$verb" 2>/dev/null; then jason_processes; fail "the uninstall the menu item starts did not end within 300 s of the application quitting"; fi
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
    within 300 "the previous release's install.sh" env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$previous" sh "$previous/install.sh" || fail "the previous release's install.sh failed"
    old="$(within 60 "the previous release's jason --version" "$WRAPPER" --version | cut -d+ -f1)"
    within 120 "the previous release's jason runtime start" "$WRAPPER" runtime start > /dev/null || fail "the previous release could not start a runtime"
    within 60 "the previous release's jason runtime autostart enable" "$WRAPPER" runtime autostart enable > "$OUT/autostart.json" || fail "the previous release could not register its logon start: $(cat "$OUT/autostart.json")"
    say "the previous release's command-line Jason $old installed by its own script, with a runtime and a logon registration"
    say "launchd's records after the previous release's registration: $(launchd_records | tr '\t\n' ' ;' || true)"

    python3 -m http.server 8099 --bind 127.0.0.1 --directory "$feed" > "$OUT/feed-server.log" 2>&1 &
    server=$!
    trap 'kill "$server" 2>/dev/null || true' EXIT
    wait_until 10 'the feed on 127.0.0.1:8099 answering' curl -fsS --max-time 5 -o /dev/null http://127.0.0.1:8099/manifest.json
    status=0
    within 600 "the previous release's jason update apply" "$WRAPPER" update apply --feed http://127.0.0.1:8099/manifest.json --drain-seconds 0 > "$OUT/update-from-previous.json" 2>&1 || status=$?
    cat "$OUT/update-from-previous.json"
    [ "$status" -eq 0 ] || fail "the previous release's jason update apply exited with $status"
    say "the previous release's own jason update apply took the feed served on this machine"

    # THE MOVE (the unit that moves a command-line install into the application) is asserted here once it lands: the
    # application in place, ~/.local/bin/jason its wrapper, the runtime running from the carried file, the logon
    # registration naming it, the move's record complete. Until then the update is the command-line update, read back so.
    [ "$(within 60 'jason --version after the update' "$WRAPPER" --version | cut -d+ -f1)" = "$version" ] || fail "after the update ~/.local/bin/jason does not say $version"
    within 60 'jason runtime status after the update' "$WRAPPER" runtime status > /dev/null || fail "no runtime answers after the update"
    say "the command-line install now says $version and its runtime answers (the move into the application is asserted once it lands)"

    within 300 "jason uninstall of the updated command-line install" "$WRAPPER" uninstall > "$OUT/uninstall-previous.json" || fail "jason uninstall of the updated command-line install failed: $(cat "$OUT/uninstall-previous.json")"
    say "launchd's records after $version's jason uninstall: $(launchd_records | tr '\t\n' ' ;' || true)"
    ;;

previous-removal)
    # The previous release's own removal of its logon registration, said in launchd's records: its command-line Jason
    # installed by its own script, its logon registration, and its own jason uninstall. Facts for the release before this
    # one, whose code no longer changes: the records before, after the registration and after the removal are said, and
    # the listing is kept, not compared.
    previous="$1"
    say "launchd's records before the previous release's own removal leg: $(launchd_records | tr '\t\n' ' ;' || true)"
    within 300 "the previous release's install.sh" env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$previous" sh "$previous/install.sh" > "$OUT/install-previous-again.txt" || fail "the previous release's install.sh failed"
    old="$(within 60 "the previous release's jason --version" "$WRAPPER" --version | cut -d+ -f1)"
    within 120 "the previous release's jason runtime start" "$WRAPPER" runtime start > /dev/null || fail "the previous release could not start a runtime"
    within 60 "the previous release's jason runtime autostart enable" "$WRAPPER" runtime autostart enable > "$OUT/autostart-again.json" || fail "the previous release could not register its logon start"
    say "launchd's records after $old's registration: $(launchd_records | tr '\t\n' ' ;' || true)"
    within 300 "the previous release's own jason uninstall" "$WRAPPER" uninstall > "$OUT/uninstall-previous-own.json" || fail "the previous release's own jason uninstall failed: $(cat "$OUT/uninstall-previous-own.json")"
    loaded=no; launchctl print "gui/$(id -u)/ai.jason.runtime" > /dev/null 2>&1 && loaded=yes
    say "launchd's records after $old's own jason uninstall: $(launchd_records | tr '\t\n' ' ;' || true); the job loaded: $loaded"
    ;;

requirement)
    # The requirement install.sh holds an application to, read from the script itself, against an application a release
    # published: the release's own signature must satisfy it, and the same requirement naming another team must refuse
    # it, so that what codesign tests is the line and not a string a row compares.
    zip="$1"
    requirement="$(sed -n "s/^APP_REQUIREMENT='\(.*\)'\$/\1/p" install/install.sh)"
    [ -n "$requirement" ] && [ "$(printf '%s\n' "$requirement" | grep -c .)" = 1 ] || fail "install.sh names no single APP_REQUIREMENT line"
    unpacked="$RUNNER_TEMP/published-application"
    rm -rf "$unpacked"; mkdir -p "$unpacked"
    within 300 "unpacking $zip" ditto -x -k "$zip" "$unpacked" || fail "$zip could not be unpacked"
    [ -d "$unpacked/Jason.app" ] || fail "$zip holds no Jason.app"
    echo "requirement: $requirement"
    within 300 'codesign verifying the published application' codesign --verify --deep --strict --verbose=2 -R="$requirement" "$unpacked/Jason.app" || fail "the published application does not satisfy install.sh's requirement"
    other="${requirement//BD8M7U5YBV/AAAAAAAAAA}"
    [ "$other" != "$requirement" ] || fail "install.sh's requirement names no team"
    if within 300 'codesign verifying with another team' codesign --verify --deep --strict -R="$other" "$unpacked/Jason.app" 2> /dev/null; then fail "the published application satisfies the requirement with another team: -R is not what decides"; fi
    say "the published application $(version_of "$unpacked/Jason.app") satisfies install.sh's requirement, and the same requirement naming another team refuses it"
    ;;

assess)
    placed="$(app)"
    [ -n "$placed" ] || fail "no Jason.app in /Applications or ~/Applications"
    status=0
    said="$(within 300 "Gatekeeper's assessment" spctl --assess --type execute --verbose=4 "$placed" 2>&1)" || status=$?
    echo "$said"
    [ "$status" -eq 0 ] || fail "Gatekeeper refuses $placed at its final path: $said"
    printf '%s\n' "$said" | grep -q 'source=Notarized Developer ID' || fail "Gatekeeper accepts $placed, but not as a notarized Developer ID application: $said"
    quarantined="$(xattr -p com.apple.quarantine "$placed" 2>/dev/null || echo none)"
    say "Gatekeeper accepts $placed at its final path as a notarized Developer ID application; its quarantine attribute: $quarantined"
    ;;

quarantine)
    feed="$1"; copy="$2"
    rm -rf "$copy"
    cp -R "$feed" "$copy"
    # The attribute as a browser writes it: downloaded (0x0001), not yet approved by a person (no 0x0040), the moment, the
    # agent and an id of the download.
    value="$(printf '0081;%08x;Safari;%s' "$(date +%s)" "$(uuidgen)")"
    for archive in "$copy"/jason-desktop-macos-*.zip; do
        xattr -w com.apple.quarantine "$value" "$archive"
        [ "$(xattr -p com.apple.quarantine "$archive")" = "$value" ] || fail "$archive does not carry the quarantine attribute"
    done
    say "the copy of the feed at $copy carries its desktop archives as a browser leaves what it downloads: com.apple.quarantine $value"
    ;;

intel)
    zip="$1"; version="$2"
    [ "$(uname -m)" = x86_64 ] || fail "this leg is the Intel Mac's, and this machine is $(uname -m)"
    place=/Applications
    [ -w "$place" ] || { place="$HOME/Applications"; mkdir -p "$place"; }
    # The plain steps a person takes with the archive, since install.sh refuses an Intel Mac: unpacked where applications
    # are kept, by ditto, which keeps the links, the signature and its ticket.
    within 300 "unpacking $zip" ditto -x -k "$zip" "$place" || fail "$zip could not be unpacked into $place"
    placed="$place/Jason.app"
    carried="$placed/Contents/Resources/bundle/jason"
    within 300 'codesign verifying the x64 application' codesign --verify --deep --strict --verbose=2 "$placed" || fail "the signature of $placed does not verify"
    file "$carried" | grep -q 'x86_64' || fail "the jason $placed carries is not an x86_64 program: $(file "$carried")"
    bash "$0" assess
    [ "$(within 60 'the carried jason --version' "$carried" --version | cut -d+ -f1)" = "$version" ] || fail "the carried jason does not say $version"
    within 120 'jason runtime start on the Intel Mac' "$carried" runtime start > "$OUT/intel-runtime-start.txt" 2>&1 || fail "the carried jason could not start a runtime: $(cat "$OUT/intel-runtime-start.txt")"
    within 60 'jason runtime status on the Intel Mac' "$carried" runtime status > "$OUT/intel-runtime-status.json" || fail "no runtime answers on the Intel Mac"
    grep -q '"applied_migrations":\["' "$OUT/intel-runtime-status.json" || fail "the runtime reported no applied migrations: $(cat "$OUT/intel-runtime-status.json")"
    say "the x64 application's jason says $version on an Intel Mac, and its runtime started, migrated and answered"

    "$placed/Contents/MacOS/Jason" > "$OUT/intel-window.log" 2>&1 &
    window=$!
    sleep 10
    kill -0 "$window" 2>/dev/null || fail "Jason Desktop did not stay running on the Intel Mac: $(tail -n 20 "$OUT/intel-window.log")"
    end_process "$window" 'Jason Desktop on the Intel Mac'
    say "Jason Desktop ran on the Intel Mac until it was told to quit"

    within 300 'jason uninstall on the Intel Mac' "$carried" uninstall --purge-data --yes > "$OUT/intel-uninstall.json" 2>&1 || fail "jason uninstall on the Intel Mac failed: $(cat "$OUT/intel-uninstall.json")"
    wait_until 30 "$placed going" test ! -e "$placed" || fail "$placed is still there after the uninstall"
    say "jason uninstall took the x64 application and everything it placed off the Intel Mac"
    ;;

*)
    echo "usage: test-installation-macos.sh listing|compare|held|install|refuse-while-open|uninstall-terminal|uninstall-menu|from-previous|assess|quarantine|intel ..." >&2
    exit 2
    ;;
esac
