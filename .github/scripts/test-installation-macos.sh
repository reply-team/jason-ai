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
#   compare <after> <before> [mode] [whole]
#                                  every line equal but those of the data directory and the harness root, which are kept
#                                  (with previous-profiles: also the empty profiles and the enabled launchd record the
#                                  previous release's own install and registration leave, each named and said; with
#                                  ran-from-image: the preferences file and the folder this build's jason unpacked, which
#                                  an application that ran from its disk image leaves, named and said). A line the
#                                  machine already had at this leg's clean begin is printed under its own heading, by the
#                                  leg that left it, and not counted; with whole, as the held step runs it, nothing is
#                                  set aside that way
#   by-hand <app> <version>        this build's application put in Applications by hand on a clean machine and opened, as
#                                  a person's start opens it: the set-up ran and completed, a new login shell finds jason,
#                                  this build's, the profile line, the skills recorded, and the set-up's report saying a
#                                  new terminal finds this jason. The uninstall is the next step's
#   ran-from-image-end             the disk image leg's own end: the two things the application that ran from the image
#                                  left, removed by name where this leg's begin did not find them, logged, never a proof
#   install <feed> <version> <script> [pinned]
#                                  install.sh from the feed as an agent runs it, in a shell whose PATH does not name
#                                  ~/.local/bin and whose account shell is zsh, as a person's Mac has it: the place, the
#                                  version, jason in a new login shell, the skills records, the person's own skill left
#                                  alone, the role skills; and the machine set-up an application's start runs, which
#                                  writes nothing under a data directory. Unpinned, as an agent runs it; with pinned, by
#                                  the pinned line, --version, after a pin naming another version was refused
#   enable-starts-now              jason runtime autostart enable with no runtime running: whether a runtime answers
#                                  within 30 s after it, said by name and asserted neither way
#   refuse-while-open              jason uninstall while Jason Desktop runs: refused, nothing removed
#   uninstall-terminal             jason uninstall typed in a new login shell, the data directory purged
#   uninstall-menu                 the uninstall the application's Uninstall Jason item starts: the carried jason with
#                                  --wait-for the application's process, which then quits
#   from-previous <previous> <feed> <version>
#                                  the previous release's command-line Jason, installed by its own script, with a runtime
#                                  and a logon registration, updated by its own jason update apply --version from a served
#                                  feed and moved into Jason Desktop: the application in place, ~/.local/bin/jason its
#                                  wrapper, the runtime running from the carried file, the logon registration naming it,
#                                  the move's record done, the command-line install retired; and whether the build says it
#                                  is a test build, as JASON_PROOF_BUILD says it must (test on CI's build, release on the
#                                  release's packages). The uninstall is the next step's
#   requirement <zip>              install.sh's APP_REQUIREMENT against a published application: admitted, and refused for another team
#   held                           the machine now compared with the machine before, whole, through no leg's begin; and
#                                  fails when any compare found the machine after an uninstall not the one before
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
# HARD: the application's removal takes this build's ~/.net/jason/<id> (other builds' are counted and left on
# macOS), so a line of this build's left after a removal counts, in the compares and in the clean check at a leg's start
# (left_over) alike. Set to 1, the two print that line under "seen, and expected until the removal takes them" and fail
# nothing: for a build whose removal does not take it alone. Test-InstallationWindows.ps1's $extractedSoft is the same switch there, and the
# two are flipped together.
EXTRACTED_SOFT=0

# A failed assertion ends its step; its words are kept beside the leg's name for the held step's list as well.
fail() { echo "::error title=Installation::$1"; printf '%s\n' "$1" >> "$OUT/leg-failure.txt" 2>/dev/null || true; exit 1; }

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
# Where an application of a release before 0.49.0 is moved out of its place, as a person moves it to the Trash: by the
# leg that installs over the previous release's Jason Desktop, and by the reset after a failed leg. The runner's temporary
# folder, which no listing reads; the Trash itself is protected on the runner.
OLD_APPS="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/over-previous-trash"
# Whether a version is one before 0.49.0; an unknown one is not.
before_049() {
    case "$1" in
        0.[0-9]*) local minor="${1#0.}"; minor="${minor%%.*}"; case "$minor" in ''|*[!0-9]*) return 1 ;; esac; [ "$minor" -lt 49 ] ;;
        *) return 1 ;;
    esac
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
# Every application is started as a person's start starts it, with this PATH (env PATH="$(plain_path)"), the one
# install.sh runs under: what the application's set-up finds on its own PATH is then the system's, never this job's. The
# legs that hold what a person's opening of the application sets up - the one on a clean machine and the two over the
# previous release's bundle - open it through LaunchServices instead (open_app), which gives it the environment a person's
# application has; a leg uses one of the two ways, never both.

# The application at the bundle $1 opened as a person opens it: through LaunchServices by the bundle's full path, never by
# name, since an older copy the runner's temporary folder holds may be known by it; what it writes to its standard output
# and error goes to $2. Prints the process id of the one process then found running that bundle's executable, held to
# run from that bundle before anything else is read; where none is found within 30 s, or more than one, or the one found
# runs from elsewhere, it says why in $OUT/open-app-failure.txt and returns 1.
open_app() {
    local bundle="$1" log="$2" pattern pids command until
    : > "$log"
    rm -f "$OUT/open-app-failure.txt"
    within 60 "open $bundle" open --stdout "$log" --stderr "$log" "$bundle" || { printf 'open %s failed\n' "$bundle" > "$OUT/open-app-failure.txt"; return 1; }
    pattern="^$(printf '%s' "$bundle" | sed 's#[][\.*^$+?(){}|]#\\&#g')/Contents/MacOS/Jason( |\$)"
    until=$(( $(date +%s) + 30 ))
    pids=""
    while [ -z "$pids" ] && [ "$(date +%s)" -lt "$until" ]; do
        sleep 0.5
        pids="$(pgrep -f -- "$pattern" || true)"
    done
    if [ -z "$pids" ]; then printf 'no process ran %s/Contents/MacOS/Jason within 30 s of open; the Jason processes: %s\n' "$bundle" "$(jason_processes | tr '\n' ';')" > "$OUT/open-app-failure.txt"; return 1; fi
    if [ "$(printf '%s\n' "$pids" | grep -c .)" != 1 ]; then printf 'more than one process runs %s/Contents/MacOS/Jason: %s\n' "$bundle" "$(printf '%s' "$pids" | tr '\n' ' ')" > "$OUT/open-app-failure.txt"; return 1; fi
    command="$(ps -o command= -p "$pids" 2>/dev/null || true)"
    case "$command" in
        "$bundle/Contents/MacOS/Jason"|"$bundle/Contents/MacOS/Jason "*) ;;
        *) printf 'the process %s found after open runs %s, not %s/Contents/MacOS/Jason\n' "$pids" "$command" "$bundle" > "$OUT/open-app-failure.txt"; return 1 ;;
    esac
    printf '%s\n' "$pids"
}

# The environment LaunchServices gives an application opened by open is launchd's, not this job's, and its SHELL is the
# account's login shell, by which jason install picks the profiles it writes. The legs that open the application hold the
# product to the shell the account really has, never to one this job sets: nothing here changes launchd's environment.
# Said before each opening: the account's login shell, and launchd's SHELL and PATH, as they are.
say_open_environment() {
    say "before open: the account's login shell is '$(account_shell)'; launchd's SHELL is '$(launchctl getenv SHELL 2>/dev/null || true)', its PATH '$(launchctl getenv PATH 2>/dev/null || true)'"
}
# The account's login shell, as the directory service records it (dscl's UserShell).
account_shell() { dscl . -read "/Users/$USER" UserShell 2>/dev/null | sed -n 's/^UserShell:[[:space:]]*//p' | head -n 1 || true; }
# The profiles the product names for the shell $1, one per line, by the product's own table: the two functions install.sh
# defines and carries verbatim from the command line's PathEntry (login_profile and interactive_profile; a test holds the
# copy to the source), run here with SHELL set to $1 - the file a login shell of it reads, then the file its every
# interactive shell reads, where it has one.
product_profiles() {
    local functions
    functions="$(sed -n '/^login_profile() {$/,/^}$/p; /^interactive_profile() {$/,/^}$/p' install/install.sh)"
    [ -n "$functions" ] || return 1
    ( eval "$functions"; SHELL="$1"; login_profile; interactive_profile )
}
# A login shell as a new terminal of the account starts one: the shell $1, in the environment the system gives.
fresh_in() { env -i HOME="$HOME" USER="$USER" LOGNAME="$USER" SHELL="$1" TERM=xterm-256color PATH=/usr/bin:/bin:/usr/sbin:/sbin "$1" -l -i -c "$2" 2>/dev/null; }
# The SHELL in the environment of the process $1, as ps reads it; empty where it names none.
app_shell_of() { ps -E -ww -o command= -p "$1" 2>/dev/null | grep -oE '(^| )SHELL=[^ ]*' | head -n 1 | sed 's/^ *SHELL=//' || true; }
# The profiles the set-up's report says it wrote the lines into (its command's profiles), from the set-up record $1, one
# per line in the file $2.
report_profiles() {
    python3 -c 'import json,sys
command = (json.load(open(sys.argv[1], encoding="utf-8")).get("report") or {}).get("command") or {}
print("\n".join(str(item) for item in (command.get("profiles") or [])))' "$1" > "$2"
}
# Every profile a shell of the account may read, by PathEntry's own list (ProfileCandidates).
PROFILE_CANDIDATES=".profile .bash_profile .bash_login .zprofile .bashrc .zshrc"
# Before this build's application is opened: the PATH a new login shell of the account's shell answers, said, and whether
# it names ~/.local/bin already; and every profile's hash, or absent. The product writes no profile where that PATH names
# the folder (MachineSetup: "is on the PATH a terminal has already, so no profile was written"), so the hold after the
# start (hold_account_terminal) takes its branch from this reading, never from what the product did.
read_terminal_before() {
    local shell answered entry
    shell="$(account_shell)"
    [ -n "$shell" ] || fail "the account's login shell could not be read (dscl . -read /Users/$USER UserShell)"
    answered="$(within 60 "a new login shell of $shell saying its PATH" fresh_in "$shell" 'printf "%s\n" "$PATH"' | tail -n 1)" || fail "a new login shell of $shell did not say its PATH"
    [ -n "$answered" ] || fail "a new login shell of $shell said no PATH"
    printf '%s\n' "$answered" > "$OUT/terminal-before-path.txt"
    : > "$OUT/terminal-before-names-local-bin.txt"
    while IFS= read -r entry; do
        if [ "${entry%/}" = "$HOME/.local/bin" ]; then echo yes > "$OUT/terminal-before-names-local-bin.txt"; fi
    done < <(printf '%s\n' "$answered" | tr ':' '\n')
    : > "$OUT/terminal-before-profiles.txt"
    for name in $PROFILE_CANDIDATES; do
        printf '%s\t%s\n' "$name" "$(if [ -f "$HOME/$name" ]; then shasum -a 256 < "$HOME/$name" | cut -d' ' -f1; else echo absent; fi)" >> "$OUT/terminal-before-profiles.txt"
    done
    say "before open: a new login shell of $shell answers the PATH '$answered', which $(if [ -s "$OUT/terminal-before-names-local-bin.txt" ]; then echo names; else echo 'does not name'; fi) ~/.local/bin; the profiles: $(tr '\t\n' ' ;' < "$OUT/terminal-before-profiles.txt")"
}
# The terminal command as the account's own new terminal finds it, after a start of this build's application: the
# account's login shell as a login shell finds ~/.local/bin/jason saying the version $1, alone by that name. Then the
# profiles, by the PATH a new login shell answered before the open (read_terminal_before):
#  - where it named ~/.local/bin already, the product writes none: the profiles the set-up's report says it wrote (the
#    file $2) are none, every profile's hash is its hash before the open, and the report's done says no profile was
#    written (the set-up record $3, where the leg has one);
#  - where it did not, every profile the product names for that shell carries its line.
# For a shell that is neither zsh nor bash the product names ~/.profile alone, and its promise is the terminal, not a
# file: there the profiles the report says it wrote are said, and the leg fails only where that login shell does not find
# jason.
hold_account_terminal() {
    local version="$1" written="${2:-}" record="${3:-}" shell found every profile listed name hashed now done_list said_done
    shell="$(account_shell)"
    [ -n "$shell" ] || fail "the account's login shell could not be read (dscl . -read /Users/$USER UserShell)"
    found="$(within 60 "a new login shell of $shell finding jason" fresh_in "$shell" 'command -v jason; jason --version')" || true
    every="$(within 60 "a new login shell of $shell listing every jason" fresh_in "$shell" 'which -a jason')" || true
    case "${shell##*/}" in
        zsh|bash)
            [ "$(printf '%s\n' "$found" | sed -n 1p)" = "$WRAPPER" ] || fail "a new login shell of $shell finds jason at '$(printf '%s\n' "$found" | sed -n 1p)', not $WRAPPER"
            [ "$(printf '%s\n' "$found" | sed -n 2p | cut -d+ -f1)" = "$version" ] || fail "jason in a new login shell of $shell does not say $version: $found"
            [ "$(printf '%s\n' "$every" | grep -c .)" = 1 ] || fail "a new login shell of $shell finds more than one jason by that name: $(printf '%s\n' "$every" | tr '\n' ' ')"
            [ -f "$OUT/terminal-before-profiles.txt" ] || fail "the PATH a new login shell answered before the open was not read (read_terminal_before)"
            if [ -s "$OUT/terminal-before-names-local-bin.txt" ]; then
                [ -z "$written" ] || [ ! -s "$written" ] || [ -z "$(grep . "$written" || true)" ] || fail "a new login shell's PATH named ~/.local/bin before the open, and the set-up's report says it wrote the profiles $(tr '\n' ' ' < "$written")"
                while IFS=$'\t' read -r name hashed; do
                    [ -n "$name" ] || continue
                    now="$(if [ -f "$HOME/$name" ]; then shasum -a 256 < "$HOME/$name" | cut -d' ' -f1; else echo absent; fi)"
                    [ "$now" = "$hashed" ] || fail "a new login shell's PATH named ~/.local/bin before the open, and ~/$name changed: $hashed before, $now after"
                done < "$OUT/terminal-before-profiles.txt"
                if [ -n "$record" ]; then
                    [ -f "$record" ] || fail "no set-up record at $record, so its report's done cannot be read"
                    done_list="$(python3 -c 'import json,sys
command = (json.load(open(sys.argv[1], encoding="utf-8")).get("report") or {}).get("command") or {}
print("\n".join(str(item) for item in (command.get("done") or [])))' "$record")" || fail "the done of the set-up record at $record could not be read"
                    printf '%s\n' "$done_list" | grep -qxF "$HOME/.local/bin is on the PATH a terminal has already, so no profile was written." || fail "a new login shell's PATH named ~/.local/bin before the open, and the set-up's report does not say no profile was written: $(printf '%s' "$done_list" | tr '\n' ' ')"
                    said_done="and the report's done says no profile was written"
                else
                    said_done="and this leg has no set-up record to read the report's done from"
                fi
                say "the account's login shell is $shell: a new login shell of it finds jason at $WRAPPER alone by that name, saying $version; its PATH named ~/.local/bin before the open, so the product wrote no profile: the report names none, every profile is as it was, $said_done"
            else
                listed="$(product_profiles "$shell")" || fail "the product's profile functions could not be read from install/install.sh"
                [ -n "$listed" ] || fail "the product names no profile for $shell"
                while IFS= read -r profile; do
                    [ -n "$profile" ] || continue
                    grep -qx '# added by jason install' "$profile" 2>/dev/null || fail "$profile, which the product names for $shell, carries no line for ~/.local/bin"
                done <<< "$listed"
                say "the account's login shell is $shell: a new login shell of it finds jason at $WRAPPER alone by that name, saying $version, and the profiles the product names for it carry the line: $(printf '%s' "$listed" | tr '\n' ' ')"
            fi
            ;;
        *)
            say "the account's login shell is $shell, neither zsh nor bash: the profiles the set-up's report says it wrote: $(if [ -n "$written" ] && [ -s "$written" ]; then while IFS= read -r profile; do [ -n "$profile" ] || continue; printf '%s (%s) ' "$profile" "$(if grep -qx '# added by jason install' "$profile" 2>/dev/null; then echo 'carries the line'; else echo 'no line'; fi)"; done < "$written"; else printf 'none read'; fi)"
            [ -n "$(printf '%s\n' "$found" | sed -n 1p)" ] || fail "a new login shell of $shell does not find jason"
            say "a new login shell of $shell finds jason at $(printf '%s\n' "$found" | sed -n 1p), saying $(printf '%s\n' "$found" | sed -n 2p); every jason by that name: $(printf '%s\n' "$every" | tr '\n' ' ')"
            ;;
    esac
}

# The desktop's machine_setup_finished lines of a start, counted; and whether the last one completed.
start_setup_finishes() {
    python3 -c 'import glob,json,os,sys
lines = []
for path in sorted(glob.glob(os.path.join(sys.argv[1], "logs", "desktop-*.jsonl"))):
    for line in open(path, encoding="utf-8", errors="replace").read().splitlines():
        try:
            event = json.loads(line)
        except ValueError:
            continue
        if isinstance(event, dict) and event.get("EventName") == "machine_setup_finished" and event.get("Why") == "start":
            lines.append(event)
last = lines[-1] if lines else {}
print("%d %s" % (len(lines), str(last.get("Completed", "none")).lower()))' "$DATA"
}

# The desktop's log of this job's data directory, copied whole to $OUT/$1 where there is one: kept before an assertion on
# a start can fail, and again on the way out.
keep_desktop_log() {
    [ -n "$(ls "$DATA"/logs/desktop-*.jsonl 2>/dev/null || true)" ] || return 0
    cat "$DATA"/logs/desktop-*.jsonl > "$OUT/$1" 2>/dev/null || true
}

# The desktop's machine_setup_ran lines of a start, counted; and the last one's exit and whether it completed.
start_setup_runs() {
    python3 -c 'import glob,json,os,sys
lines = []
for path in sorted(glob.glob(os.path.join(sys.argv[1], "logs", "desktop-*.jsonl"))):
    for line in open(path, encoding="utf-8", errors="replace").read().splitlines():
        try:
            event = json.loads(line)
        except ValueError:
            continue
        if isinstance(event, dict) and event.get("EventName") == "machine_setup_ran" and event.get("Why") == "start":
            lines.append(event)
last = lines[-1] if lines else {}
print("%d %s %s" % (len(lines), last.get("Exit", "none"), str(last.get("Completed", "none")).lower()))' "$DATA"
}

# The leg a line of $OUT/inherited.txt, or of the file $2, is attributed to, for the line $1 ("+ <line>" or "- <line>");
# nothing where none.
inherited_by() {
    local rows="${2:-$OUT/inherited.txt}"
    [ -f "$rows" ] || return 0
    LINE="$1" awk -F '\t' '{ leg = $1; sub(/^[^\t]*\t/, ""); if ($0 == ENVIRON["LINE"]) { print leg; exit } }' "$rows"
}

# What a clean leg's begin found and excused: the machine then, listed to $OUT/leg-start.txt, against the machine before,
# the data directory and the harness root aside. One row per line, "<leg><TAB><+ or -> <line>", in $OUT/inherited.txt;
# a line recorded at an earlier begin keeps the leg it was attributed to there, and a new one is attributed to $1, the leg
# that last changed the machine. The held step reads the machine at the end the same way: listed to $OUT/$2.txt, its rows
# in the file $3.
record_inherited() {
    local by="${1:-no leg, before any leg ran}" listing="$OUT/${2:-leg-start}.txt" rows="${3:-$OUT/inherited.txt}" line was
    write_listing "$listing" quiet
    : > "$rows.next"
    { comm -13 "$OUT/before.txt" "$listing" | sed 's/^/+ /'; comm -23 "$OUT/before.txt" "$listing" | sed 's/^/- /'; } \
        | { grep -vE '^[+-] (data directory|harness root)' || true; } \
        | while IFS= read -r line; do
            was="$(inherited_by "$line")"
            printf '%s\t%s\n' "${was:-$by}" "$line" >> "$rows.next"
        done
    mv "$rows.next" "$rows"
}

# The two lines an application that ran leaves, by name: its preferences file, and the folder this build's jason unpacked
# under each id this build's jason extracts to.
ran_from_image_lines() {
    printf 'system-kept\tLibrary/Preferences/ai.jasonharness.desktop.plist\n'
    [ -f "$OUT/this-build-extracted.txt" ] || return 0
    while IFS= read -r id; do [ -z "$id" ] || printf 'extracted\t%s\n' "$id"; done < "$OUT/this-build-extracted.txt"
}

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

# The folder $1 served on 127.0.0.1:8099 by .github/scripts/serve-feed.py, its log in $2 (bound and listening, each with
# its moment, then every request), its process in $server for the caller's trap to end; waited for for 30 s to answer
# manifest.json. Where it does not, what was there is printed before anything is ended: the server's log, whether its
# process lives, what holds the port (lsof and netstat), and a sample of the server where taking one costs under 5 s.
serve_feed() {
    local folder="$1" log="$2" until
    python3 "$(dirname "$0")/serve-feed.py" "$folder" 8099 "$log" &
    server=$!
    until=$(( $(date +%s) + 30 ))
    while [ "$(date +%s)" -lt "$until" ]; do
        if curl -fsS --max-time 5 -o /dev/null http://127.0.0.1:8099/manifest.json 2> /dev/null; then
            say "the feed at $folder answers on 127.0.0.1:8099 (server process $server): $(grep -E ' (bound|listening) ' "$log" | tr '\n' ' ')"
            return 0
        fi
        sleep 0.5
    done
    echo "--- the feed server's log, $log:"
    cat "$log" 2>/dev/null || echo '(no log)'
    echo "--- the feed server's process $server: $(if kill -0 "$server" 2>/dev/null; then echo "lives: $(ps -o pid=,etime=,command= -p "$server")"; else echo 'not running'; fi)"
    echo '--- lsof -nP -iTCP:8099:'
    lsof -nP -iTCP:8099 2>&1 || true
    echo '--- netstat -anv -p tcp, the lines naming port 8099:'
    netstat -anv -p tcp 2>&1 | grep -E '(Proto|\.8099 )' || true
    if kill -0 "$server" 2>/dev/null; then
        echo "--- sample of the feed server $server, for 1 s, given 5 s:"
        ( within 5 "sample of the feed server $server" sample "$server" 1 -mayDie 2>&1 ) || echo '(no sample within 5 s)'
    fi
    fail "the feed on 127.0.0.1:8099 answering did not happen within 30 s; its server's log, process and port are printed above"
}

# --- the listing -----------------------------------------------------------------------------------------------------

folder() {
    local label="$1" path="$2"
    if [ ! -e "$path" ]; then printf '%s\tabsent\n' "$label"; return; fi
    printf '%s\tpresent\n' "$label"
    (cd "$path" && { find . \( -type f -o -type l \) -print 2>/dev/null || true; } | sort | while IFS= read -r file; do
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
            { find "$HOME/Library/$parent" -maxdepth 1 -iname '*jason*' 2>/dev/null || true; } | while IFS= read -r kept; do printf 'system-kept\t%s\n' "${kept#$HOME/}"; done
        done
        # The native libraries the single-file executable extracts, under ~/.net/jason/<id> (the host's default base on
        # macOS, one id per build): one line per folder.
        { find "$HOME/.net/jason" -mindepth 1 -maxdepth 1 -type d 2>/dev/null || true; } | while IFS= read -r extracted; do printf 'extracted\t%s\n' "${extracted##*/}"; done
        folder 'claude skills' "$HOME/.claude/skills"
        folder 'agents skills' "$HOME/.agents/skills"
        folder 'data directory' "$DATA"
        folder 'harness root' "$ROOT"
        ps -axo command= | grep -iE '(Jason\.app/|/\.local/bin/jason|/bundle/jason)' | grep -v grep | sed 's/^/process\t/' || true
    } | sort -u > "$1"
    [ "${2:-}" = quiet ] && return 0
    echo "$(wc -l < "$1" | tr -d ' ') lines written to $1"
    sed 's/^/  /' "$1"
}

# --- the legs' two ends ----------------------------------------------------------------------------------------------

# A leg that began and never ended failed: recorded by its name with the words its failure said.
close_leg() {
    [ -s "$OUT/leg-open.txt" ] || { rm -f "$OUT/leg-open.txt"; return 0; }
    local open why
    open="$(cat "$OUT/leg-open.txt")"
    rm -f "$OUT/leg-open.txt"
    if [ -s "$OUT/leg-failure.txt" ]; then why="it said: $(tr '\n' ' ' < "$OUT/leg-failure.txt")"; else why="its own lines are in its step"; fi
    echo "::error title=Installation::the leg '$open' failed; $why"
    printf '%s\n' "the leg '$open' failed; $why" >> "$OUT/failed-legs.txt"
}

# Every leg's own scratch, which a leg that fails part way can leave and no product door takes, by name: the copy of
# this build staged beside an application to swap it in (bundle-swap), the old bundles a leg moved out of their place
# (bundle-swap, over-previous-desktop), the folder the person's own quick-start is moved out to while the previous
# release is installed (way-back), the disk image, its mount and its stage (ran-from-image, whose own trap takes them
# unless it is cut off), and a feed server still serving. Every leg that makes one of them refuses to start where it is
# there already, so a clean begin takes them all, before it reads the machine: the processes running from the bundles
# quit, the image detached, the person's own quick-start put back where it is missing from its place, then the rest
# removed. On a GitHub-hosted runner alone, as the reset; each act logged in $OUT/resets.txt, never a proof.
SCRATCH_STAGED="/Applications/.Jason-swap.app $HOME/Applications/.Jason-swap.app"
SCRATCH_ASIDE="$OLD_APPS/swapped-out-by-the-leg $OLD_APPS/moved-by-the-leg"
SCRATCH_OWN_OUT="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/own-quick-start-out"
SCRATCH_MOUNT="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/jason-vanishing-place"
SCRATCH_IMAGE="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/jason-vanishing-place.dmg ${RUNNER_TEMP:-${TMPDIR:-/tmp}}/jason-vanishing-stage $SCRATCH_MOUNT"
take_scratch() {
    local leg="$1" found="" place line pid
    for place in $SCRATCH_STAGED $SCRATCH_ASIDE $SCRATCH_OWN_OUT $SCRATCH_IMAGE; do { [ -e "$place" ] || [ -L "$place" ]; } && found="$found $place"; done
    [ -n "$(pgrep -f 'serve-feed\.py' || true)" ] && found="$found feed-server"
    [ -n "$found" ] || return 0
    if [ "${GITHUB_ACTIONS:-}" != true ] || [ "${RUNNER_ENVIRONMENT:-}" != github-hosted ]; then skip_leg "not run: an earlier leg's scratch is there ($found), and it is taken on a GitHub-hosted runner alone"; exit 0; fi
    printf "before '%s': an earlier leg's scratch:%s\n" "$leg" "$found" >> "$OUT/resets.txt"
    for pid in $(pgrep -f 'serve-feed\.py' || true) $(pgrep -f '/\.Jason-swap\.app/' || true) $(pgrep -f "$OLD_APPS/" || true) $(pgrep -f "$SCRATCH_MOUNT/" || true); do
        printf '  quit the process %s: %s\n' "$pid" "$(ps -o command= -p "$pid" 2>/dev/null || echo 'ended')" >> "$OUT/resets.txt"
        kill "$pid" 2>/dev/null || true
    done
    sleep 2
    if mount | grep -qF " on $SCRATCH_MOUNT "; then
        if ( within 60 "hdiutil detach $SCRATCH_MOUNT" hdiutil detach -force "$SCRATCH_MOUNT" > /dev/null 2>&1 ); then line="  detached the disk image at $SCRATCH_MOUNT"; else line="  could not detach the disk image at $SCRATCH_MOUNT"; fi
        say "$line"
        printf '%s\n' "$line" >> "$OUT/resets.txt"
    fi
    # While the person's own quick-start is out of its place, whatever stands there is a stand-in (the previous release's
    # skills write one): removed, then the person's copy put back, as the leg's own put-back does. Where it cannot be put
    # back, nothing is removed and this leg is not run, so the person's copy is never taken with the scratch.
    if [ -d "$SCRATCH_OWN_OUT/quick-start" ]; then
        if [ -e "$OWN_SKILL" ] || [ -L "$OWN_SKILL" ]; then
            if rm -rf "$OWN_SKILL"; then line="  removed $OWN_SKILL, a stand-in while the person's own quick-start was out of its place"; else line="  could not remove $OWN_SKILL, a stand-in while the person's own quick-start was out of its place"; fi
            say "$line"
            printf '%s\n' "$line" >> "$OUT/resets.txt"
        fi
        mkdir -p "$(dirname "$OWN_SKILL")"
        if [ ! -e "$OWN_SKILL" ] && mv "$SCRATCH_OWN_OUT/quick-start" "$OWN_SKILL"; then line="  put the person's own quick-start back from $SCRATCH_OWN_OUT/quick-start to $OWN_SKILL"; else line="  could not put the person's own quick-start back from $SCRATCH_OWN_OUT/quick-start"; fi
        say "$line"
        printf '%s\n' "$line" >> "$OUT/resets.txt"
        if [ -d "$SCRATCH_OWN_OUT/quick-start" ]; then skip_leg "not run: the person's own quick-start could not be put back from $SCRATCH_OWN_OUT/quick-start to $OWN_SKILL"; exit 0; fi
    fi
    for place in $SCRATCH_STAGED $SCRATCH_ASIDE $SCRATCH_OWN_OUT $SCRATCH_IMAGE; do
        { [ -e "$place" ] || [ -L "$place" ]; } || continue
        if rm -rf "$place"; then line="  removed $place"; else line="  could not remove $place"; fi
        say "$line"
        printf '%s\n' "$line" >> "$OUT/resets.txt"
    done
    for place in $SCRATCH_STAGED $SCRATCH_ASIDE $SCRATCH_OWN_OUT $SCRATCH_IMAGE; do
        if [ -e "$place" ] || [ -L "$place" ]; then skip_leg "not run: an earlier leg's scratch, $place, could not be removed"; exit 0; fi
    done
}

# The open leg's own compare differed: the leg is recorded as failed by its name at once, and as dirty, so that its end
# fails its step and a later begin treats the machine it left as a failed leg's. Outside a leg (the held step's whole
# compare) nothing is recorded here: the held step fails the job on the divergence itself.
dirty_leg() {
    [ -s "$OUT/leg-open.txt" ] || return 0
    local open
    open="$(cat "$OUT/leg-open.txt")"
    grep -qxF -- "$open" "$OUT/legs-dirty.txt" 2>/dev/null && return 0
    printf '%s\n' "the leg '$open' failed: $1" >> "$OUT/failed-legs.txt"
    printf '%s\n' "$open" >> "$OUT/legs-dirty.txt"
}

# This leg not run, said by why: recorded for the held step, and $OUT/skip left for the job's step to end on.
skip_leg() {
    echo "::error title=Installation::$LEG - $1"
    say "$LEG - $1"
    printf '%s\n' "$LEG - $1" >> "$OUT/not-run.txt"
    rm -f "$OUT/leg-open.txt"
    : > "$OUT/skip"
}

# Every line of the machine now that is not a line of the machine before, and every line gone, as compare reads them:
# the data directory and the harness root aside, and what an earlier leg is known to leave and asserts itself - launchd's
# records, which no command without administrator rights removes, and the empty profiles the previous release's install.sh
# creates.
left_over() {
    write_listing "$OUT/now.txt" quiet
    local empty
    empty="$(printf '' | shasum -a 256 | cut -d' ' -f1)"
    { comm -13 "$OUT/before.txt" "$OUT/now.txt" | sed 's/^/+ /'; comm -23 "$OUT/before.txt" "$OUT/now.txt" | sed 's/^/- /'; } \
        | grep -vE '^[+-] (data directory|harness root|launchd record )' \
        | grep -vE "^\+ profile \.(zprofile|zshrc)	$empty\$" \
        | grep -vE '^- profile \.(zprofile|zshrc)	absent$' \
        | while IFS= read -r line; do
            # Another release's extraction folder is excused here as in the compares. This build's follows EXTRACTED_SOFT,
            # as the compares do: ignored while it is soft, since before the removal took it no product door did and a reset could
            # not; counted once it is hard, so that no leg starts on a machine a removal left dirty. The one commit that
            # flips EXTRACTED_SOFT makes both hard.
            case "$line" in
                # One gone is the removal's taking, never a reason to reset.
                "- extracted	"*) continue ;;
                "+ extracted	"*)
                    id="${line#? extracted	}"
                    if [ -s "$OUT/this-build-extracted.txt" ] && ! grep -qxF -- "$id" "$OUT/this-build-extracted.txt"; then continue; fi
                    [ "$EXTRACTED_SOFT" = 1 ] && continue ;;
            esac
            printf '%s\n' "$line"
        done || true
}

# Whether one fact of jason status, in the file $1, names the folder $2 itself, by its path, in its list of folders Jason
# did not deploy under a name Jason ships: the list before the sentence's verb, split as the list is joined (", " and
# " and "), one item the path or ending in it after a space.
names_own_skill() {
    python3 - "$1" "$2" <<'PY'
import re, sys
text, own = open(sys.argv[1], encoding='utf-8').read().replace('\\\\', '\\'), sys.argv[2]
for fact in re.finditer(r'(?P<list>[^"]+?) (?:is a (?:folder|link)|are (?:folders and links|folders|links)) Jason did not deploy, under (?:a name|names) Jason ships', text):
    for item in re.split(r', | and ', fact.group('list')):
        if item == own or item.endswith(' ' + own):
            sys.exit(0)
sys.exit(1)
PY
}

step="${1:-}"; shift || true
# ONE GUARD, AT THE ENTRY. Every step but the five that read the machine or this job's own files and write only those
# files (the listing, the compare, the held list and a leg's begin and end, which the stub trials run) changes the machine
# or runs a program on it, and runs on a GitHub-hosted runner alone. The reset inside begin keeps its own refusal.
case "$step" in
    listing|compare|held|begin|end) ;;
    *) if [ "${GITHUB_ACTIONS:-}" != true ] || [ "${RUNNER_ENVIRONMENT:-}" != github-hosted ]; then fail "the step '$step' changes the machine it runs on, and runs on a GitHub-hosted runner alone"; fi ;;
esac
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
    printf 'cdhash H"%s"\n' "$cdhash" > "$copy.requirement"
    say "install.sh refused the ad hoc test build; the copy run here differs from it in line $number alone, naming this build's own signature"
    ;;

listing)
    write_listing "$OUT/$1.txt"
    ;;

extracted)
    # The id of the folder this build's executable extracts its native libraries to, read by running it once with the
    # host's base pointed at a folder of this job's own, so that nothing is written where the listing looks.
    executable="$1"
    probe="$RUNNER_TEMP/extract-probe-$$-$(date +%s)"
    within 120 "$executable --version" env DOTNET_BUNDLE_EXTRACT_BASE_DIR="$probe" "$executable" --version > /dev/null 2>&1 || fail "$executable --version failed"
    ids="$({ find "$probe" -mindepth 2 -maxdepth 2 -type d 2>/dev/null || true; } | sed 's|.*/||')"
    [ -n "$ids" ] || fail "$executable extracted nothing under $probe: no id to tell this build's extraction folder by"
    printf '%s\n' "$ids" >> "$OUT/this-build-extracted.txt"
    rm -rf "$probe"
    say "this build's jason extracts its native libraries to the id $(printf '%s' "$ids" | tr '\n' ' ')under ~/.net/jason"
    ;;

compare)
    after="$OUT/$1.txt"; before="$OUT/$2.txt"; diffed="$OUT/$2-to-$1.diff.txt"
    kept='^(data directory|harness root)'
    left="$(comm -13 "$before" "$after" | grep -vE "$kept" || true)"
    went="$(comm -23 "$before" "$after" | grep -vE "$kept" || true)"
    # The held step's compare (final): the lines the previous release's own acts leave - its empty profiles, and the
    # runtime's enabled or disabled record in launchd - are expected only where the leg that left them, by the attribution
    # the held step made ($OUT/final-attributed.txt), is a leg of the previous release ($OUT/previous-legs.txt), each named
    # beside that leg; the same lines left by a leg of this build alone count.
    if [ "${3:-}" = final ]; then
        counted_left=""; counted_went=""; excused=""
        while IFS= read -r line; do
            [ -n "$line" ] || continue
            by=""
            if printf '%s\n' "$line" | grep -qE "^profile \.(zprofile|zshrc)	$(printf '' | shasum -a 256 | cut -d' ' -f1)\$|^launchd record (gui|user)	ai\.jason\.runtime (enabled|disabled)\$"; then by="$(inherited_by "+ $line" "$OUT/final-attributed.txt")"; fi
            if [ -n "$by" ] && grep -qxF -- "$by" "$OUT/previous-legs.txt" 2>/dev/null; then excused="$excused+ $line  (left by '$by', a leg of the previous release)"$'\n'; else counted_left="$counted_left$line"$'\n'; fi
        done <<< "$left"
        while IFS= read -r line; do
            [ -n "$line" ] || continue
            by=""
            if printf '%s\n' "$line" | grep -qE '^profile \.(zprofile|zshrc)	absent$'; then by="$(inherited_by "- $line" "$OUT/final-attributed.txt")"; fi
            if [ -n "$by" ] && grep -qxF -- "$by" "$OUT/previous-legs.txt" 2>/dev/null; then excused="$excused- $line  (left by '$by', a leg of the previous release)"$'\n'; else counted_went="$counted_went$line"$'\n'; fi
        done <<< "$went"
        left="$(printf '%s' "$counted_left" | sed '/^$/d')"
        went="$(printf '%s' "$counted_went" | sed '/^$/d')"
        [ -z "$excused" ] || say "expected in $1, each left by a leg of the previous release, as that release's own install, registration and removal leave them: $(printf '%s' "$excused" | tr '\t\n' ' ;')"
    fi
    # After the previous release's command-line install alone, an expected leftover: the profiles 0.48's install.sh
    # created, which no record of this version's names, emptied of their lines. Said, and not counted.
    if [ "${3:-}" = previous-profiles ] || [ "${3:-}" = previous-own ]; then
        empty="$(printf '' | shasum -a 256 | cut -d' ' -f1)"
        expected_left="$(printf '%s\n' "$left" | grep -E "^profile \.(zprofile|zshrc)	$empty\$" || true)"
        if [ -n "$expected_left" ]; then
            say "expected after the previous release's own install.sh: $(printf '%s' "$expected_left" | tr '\n\t' '; ') (empty profiles it created)"
            for name in $(printf '%s\n' "$expected_left" | sed -E 's/^profile (\.[a-z]+)	.*/\1/'); do
                left="$(printf '%s\n' "$left" | grep -vE "^profile \\$name	" || true)"
                went="$(printf '%s\n' "$went" | grep -vE "^profile \\$name	absent\$" || true)"
            done
        fi
        # And the record the previous release's own registration wrote: its autostart enable loads with -w, which records
        # the runtime's label as enabled, a record no command run without administrator rights removes, and which this
        # version's uninstall reads and leaves, since it clears a disabled record alone. Named, said, and not counted; a
        # disabled record, or any other label, still counts.
        expected_left="$(printf '%s\n' "$left" | grep -E '^launchd record (gui|user)	ai\.jason\.runtime enabled$' || true)"
        if [ -n "$expected_left" ]; then
            say "expected after the previous release's own autostart enable (load -w): $(printf '%s' "$expected_left" | tr '\n\t' '; ') (the earlier release's enabled record, which does nothing)"
            left="$(printf '%s\n' "$left" | grep -vE '^launchd record (gui|user)	ai\.jason\.runtime enabled$' || true)"
        else
            say "no enabled record of the previous release's registration is left in launchd"
        fi
        # After the previous release's own autostart disable and uninstall (the way back's end), the disabled record they
        # write (unload -w) is that release's own act, not this version's: named, said, and not counted. Any other label
        # still counts.
        if [ "${3:-}" = previous-own ]; then
            expected_left="$(printf '%s\n' "$left" | grep -E '^launchd record (gui|user)	ai\.jason\.runtime disabled$' || true)"
            if [ -n "$expected_left" ]; then
                say "expected after the previous release's own autostart disable and uninstall (unload -w): $(printf '%s' "$expected_left" | tr '\n\t' '; ') (that release's disabled record)"
                left="$(printf '%s\n' "$left" | grep -vE '^launchd record (gui|user)	ai\.jason\.runtime disabled$' || true)"
            fi
        fi
    fi
    # After this build's application ran from its disk image: the two things an application that ran leaves, which nothing
    # installed and no product door can take, named and said with their reason, and not counted; anything else left
    # counts. The leg's own end removes them afterwards (ran-from-image-end).
    if [ "${3:-}" = ran-from-image ]; then
        named="$(ran_from_image_lines)"
        expected_left="$(printf '%s\n' "$left" | grep -xF -f <(printf '%s\n' "$named") || true)"
        say "expected in $1: $(if [ -n "$expected_left" ]; then printf '%s' "$expected_left" | tr '\t\n' ' ;'; else printf 'neither of the two'; fi) (the application ran from the image: its preferences file and what its jason unpacked; the set-up placed nothing)"
        left="$(printf '%s\n' "$left" | grep -vxF -f <(printf '%s\n' "$named") || true)"
    fi
    # An extraction folder gone since the listing against is the removal's taking (this build's own; another release's by
    # that release's own removal): said, and not counted.
    taken_x="$(printf '%s\n' "$went" | grep -E '^extracted	' || true)"
    if [ -n "$taken_x" ]; then
        say "taken by the removal in $1: $(printf '%s' "$taken_x" | tr '\t\n' ' ;') (a removal takes what its build unpacked)"
        went="$(printf '%s\n' "$went" | grep -vE '^extracted	' || true)"
    fi
    # An extraction folder of another release's id is that release's own, named as expected with its reason; one of this
    # build's ids counts (softly only while EXTRACTED_SOFT says so, as a section of its own).
    ours="$(cat "$OUT/this-build-extracted.txt" 2>/dev/null || true)"
    # Hard, an extraction line is judged by this build's ids, so a compare with none recorded cannot judge one: it fails,
    # by name, rather than counting another release's folder as this build's or excusing this build's as another's.
    if [ "$EXTRACTED_SOFT" != 1 ] && [ -z "$(printf '%s' "$ours" | tr -d '[:space:]')" ]; then
        why="$1 against $2: no id of this build is recorded ($OUT/this-build-extracted.txt is missing or empty: the step reading the folder this build's jason extracts to did not run or failed), so no extraction folder can be told this build's or another release's"
        echo "::error title=Uninstall::$why"
        printf '%s\n' "$why" >> "$OUT/divergent.txt"
        dirty_leg "$why"
    fi
    expected_x=""; soft_x=""; rest=""
    while IFS= read -r line; do
        [ -n "$line" ] || continue
        case "$line" in
            "extracted	"*)
                id="${line#extracted	}"
                if [ -n "$ours" ] && ! printf '%s\n' "$ours" | grep -qxF -- "$id"; then expected_x="$expected_x$line"$'\n'
                elif [ "$EXTRACTED_SOFT" = 1 ]; then soft_x="$soft_x$line"$'\n'
                else rest="$rest$line"$'\n'; fi ;;
            *) rest="$rest$line"$'\n' ;;
        esac
    done <<< "$left"
    left="$(printf '%s' "$rest" | sed '/^$/d')"
    # The lens: a leg is charged for what it does, against the machine its own clean begin found. A line that machine
    # already had, recorded there with the leg that left it ($OUT/inherited.txt), is printed under its own heading by that
    # leg and not counted here; that leg is held to it by its own compare and stays failed by name, and the held step's
    # whole compare counts it again. One limit: a line this leg leaves that is the same as an inherited one cannot be told
    # apart from it, and is attributed to the earlier leg.
    inherited_here=""
    if [ "${4:-}" != whole ] && [ -s "$OUT/inherited.txt" ]; then
        counted_left=""; counted_went=""
        while IFS= read -r line; do
            [ -n "$line" ] || continue
            by="$(inherited_by "+ $line")"
            if [ -n "$by" ]; then inherited_here="$inherited_here+ $line  (left by '$by')"$'\n'; else counted_left="$counted_left$line"$'\n'; fi
        done <<< "$left"
        while IFS= read -r line; do
            [ -n "$line" ] || continue
            by="$(inherited_by "- $line")"
            if [ -n "$by" ]; then inherited_here="$inherited_here- $line  (left by '$by')"$'\n'; else counted_went="$counted_went$line"$'\n'; fi
        done <<< "$went"
        left="$(printf '%s' "$counted_left" | sed '/^$/d')"
        went="$(printf '%s' "$counted_went" | sed '/^$/d')"
        [ -z "$inherited_here" ] || say "inherited at this leg's begin and not counted in $1, each by the leg that left it: $(printf '%s' "$inherited_here" | tr '\t\n' ' ;')"
    fi
    [ -z "$expected_x" ] || say "expected in $1: $(printf '%s' "$expected_x" | tr '\t\n' ' ;') (another release's own extraction folder: the previous release's jason ran in this job, and taking it is that release's removal's, never this build's)"
    if [ -n "$soft_x" ]; then
        say "seen, and expected until the removal takes them (soft: EXTRACTED_SOFT is set), in $1: $(printf '%s' "$soft_x" | tr '\t\n' ' ;')"
        printf '%s: %s\n' "$1" "$(printf '%s' "$soft_x" | tr '\t\n' ' ;')" >> "$OUT/soft-seen.txt"
    fi
    { echo "only in $2 (before):"; [ -n "$went" ] && printf '%s\n' "$went" | sed 's/^/- /'; echo "only in $1 (after):"; [ -n "$left" ] && printf '%s\n' "$left" | sed 's/^/+ /'; [ -z "$inherited_here" ] || { echo "inherited at this leg's begin, not counted here (each by the leg that left it):"; printf '%s' "$inherited_here"; }; } > "$diffed" || true
    cat "$diffed"
    keeping="$(comm -13 "$before" "$after" | grep -cE "$kept" || true)"
    say "$1 against $2: $(printf '%s' "$left" | grep -c . || true) line(s) left, $(printf '%s' "$went" | grep -c . || true) gone, beside $keeping line(s) of the data directory and the harness root, which are kept"
    # A divergence is recorded and the leg goes on to its end, so that what it does after the compare still runs and one
    # run reads every leg; the leg is recorded as failed by its name at once, and its end fails its step (dirty_leg). The
    # held step at the end fails the job on any divergence recorded.
    if [ -n "$left" ] || [ -n "$went" ]; then
        echo "::error title=Uninstall::the machine after '$1' is not the machine before '$2': $(cat "$diffed" | tr '\n' ' ')"
        echo "$1 against $2: see $diffed" >> "$OUT/divergent.txt"
        dirty_leg "its compare differs, $1 against $2: see $diffed"
    fi
    ;;

begin)
    # A leg's start: the leg before recorded as failed where it began and never ended; this leg said not run where a
    # step it needs ('name=outcome') did not succeed; and, with clean, the machine held to the listing taken before
    # anything was installed. Where it is not:
    #  - the leg that last changed the machine ENDED: that leg left it, which is that leg's own failure, said and recorded
    #    by its name; the reset below then acts, that leg now being a failed one, and hides nothing;
    #  - that leg FAILED or never ended: its failure is recorded already, and the reset acts;
    #  - no leg changed the machine yet: nothing to blame and nothing to reset, so this leg is not run.
    # The reset uses the product's own uninstalls alone - the carried jason's, then the wrapper's or the previous
    # release's at ~/.local/bin - and only on a GitHub-hosted runner, never on a person's Mac. Each act is logged with what
    # it found and what it removed, here and in $OUT/resets.txt; none of it is a proof of anything. What they cannot
    # remove makes this leg not run, naming the lines left.
    LEG="$1"; shift
    clean=""; if [ "${1:-}" = clean ]; then clean=1; shift; fi
    rm -f "$OUT/skip"
    echo "=== $LEG"
    close_leg
    rm -f "$OUT/leg-failure.txt"
    printf '%s\n' "$LEG" > "$OUT/leg-open.txt"
    for need in "$@"; do
        needed="${need%=*}"; outcome="${need##*=}"
        if [ "$outcome" = success ]; then
            # A leg's step succeeds where the leg itself was not run, or failed and was recorded so, so a needed leg's
            # record is read as well as its step's outcome.
            if [ -f "$OUT/failed-legs.txt" ] && awk -v p="the leg '$needed' " 'index($0, p) == 1 { found = 1 } END { exit !found }' "$OUT/failed-legs.txt"; then outcome=failure
            elif [ -f "$OUT/not-run.txt" ] && awk -v p="$needed - " 'index($0, p) == 1 { found = 1 } END { exit !found }' "$OUT/not-run.txt"; then outcome='not run'
            else continue; fi
        fi
        if [ "$outcome" = failure ]; then skip_leg "not run: the leg it needs failed, $needed"; else skip_leg "not run: the leg it needs did not run ($outcome), $needed"; fi
        exit 0
    done
    # A leg that needs the previous release's fetch runs the previous release: recorded, for the held step's compare.
    for need in "$@"; do [ "${need%=*}" != 'Fetch the previous release' ] || { printf '%s\n' "$LEG" >> "$OUT/previous-legs.txt"; break; }; done
    if [ -n "$clean" ]; then
        [ -f "$OUT/before.txt" ] || { skip_leg 'not run: the leg it needs failed, The machine before anything is installed'; exit 0; }
        take_scratch "$LEG"
        prior="$(cat "$OUT/last-leg.txt" 2>/dev/null || true)"
        found="$(left_over)"
        if [ -n "$found" ]; then
            resets="$OUT/resets.txt"
            last="$(cat "$OUT/last-leg.txt" 2>/dev/null || true)"
            [ -n "$last" ] || { skip_leg "not run: the machine is not the machine before, and no leg before this one changed it: $(printf '%s\n' "$found" | tr '\t\n' ' ;')"; exit 0; }
            # A leg recorded once as having ended leaving the machine is a failed leg from then on: never recorded again.
            ended=""; if [ -f "$OUT/legs-ended.txt" ] && grep -qxF -- "$last" "$OUT/legs-ended.txt" && ! grep -qxF -- "$last" "$OUT/legs-dirty.txt" 2>/dev/null; then ended=1; fi
            if [ -n "$ended" ]; then
                why="the leg '$last' ended, but the machine after it is not the machine before: $(printf '%s\n' "$found" | tr '\t\n' ' ;')"
                echo "::error title=Installation::$why"
                printf '%s\n' "$why" >> "$OUT/failed-legs.txt"
                printf '%s\n' "$last" >> "$OUT/legs-dirty.txt"
                after_last="ended leaving it, recorded as its failure"
            else
                after_last="failed"
            fi
            printf "before '%s', after '%s' %s, found: %s\n" "$LEG" "$last" "$after_last" "$(printf '%s\n' "$found" | tr '\t\n' ' ;')" >> "$resets"
            say "before this leg the machine is not the machine before ($(printf '%s\n' "$found" | grep -c .) line(s)), after '$last' $after_last; found: $(printf '%s\n' "$found" | tr '\t\n' ' ;')"
            if [ "${GITHUB_ACTIONS:-}" != true ] || [ "${RUNNER_ENVIRONMENT:-}" != github-hosted ]; then skip_leg 'not run: the reset after a failed leg runs on a GitHub-hosted runner alone, and this is not one'; exit 0; fi
            for pid in $(pgrep -f '/Jason\.app/Contents/MacOS/' || true); do
                kill "$pid" 2>/dev/null || true
                printf "  quit the application's process %s\n" "$pid" >> "$resets"
            done
            sleep 3
            placed="$(app)"
            # An application of a release before 0.49.0 cannot be taken off by its own jason, which refuses as the one the
            # application carries, and is never set up or replaced from a terminal: quit above, it is moved out of its place
            # as a person moves it to the Trash, said, and never for 0.49.0 or later, which keeps its own removal. In the
            # same act its preferences go as this build's removal takes them, by the one identifier and nothing wider:
            # through the preferences system first (defaults delete, which would otherwise write back a copy it holds), then
            # the file, where it is a file of its own and not a link.
            if [ -n "$placed" ] && before_049 "$(version_of "$placed")"; then
                moved_to="$OLD_APPS/moved-by-the-reset-$(date +%s)"
                mkdir -p "$moved_to"
                if mv "$placed" "$moved_to/Jason.app"; then line="  moved the application $placed, $(version_of "$moved_to/Jason.app"), a release before 0.49.0, out of its place to $moved_to"; else line="  could not move the application $placed, a release before 0.49.0, out of its place"; fi
                echo "$line"
                printf '%s\n' "$line" >> "$resets"
                if [ ! -e "$placed" ]; then
                    preferences="$HOME/Library/Preferences/ai.jasonharness.desktop.plist"
                    status=0
                    ( within 10 'defaults delete ai.jasonharness.desktop' defaults delete ai.jasonharness.desktop > "$OUT/reset-defaults.txt" 2>&1 ) || status=$?
                    line="  defaults delete ai.jasonharness.desktop ended $status: $({ grep . "$OUT/reset-defaults.txt" || true; } | tail -n 1)"
                    echo "$line"
                    printf '%s\n' "$line" >> "$resets"
                    if [ -f "$preferences" ] && [ ! -L "$preferences" ]; then
                        if rm -f "$preferences"; then line="  removed $preferences"; else line="  could not remove $preferences"; fi
                    else
                        line="  $preferences: $(if [ -L "$preferences" ]; then echo 'a link, left'; else echo 'not there'; fi)"
                    fi
                    echo "$line"
                    printf '%s\n' "$line" >> "$resets"
                fi
                placed="$(app)"
            fi
            for jason in "${placed:+$placed/Contents/Resources/bundle/jason}" "$WRAPPER"; do
                [ -n "$jason" ] && [ -x "$jason" ] || continue
                status=0
                ( within 300 "$jason uninstall --purge-data --yes" "$jason" uninstall --purge-data --yes > "$OUT/reset-uninstall.txt" 2>&1 ) || status=$?
                line="  $jason uninstall --purge-data --yes ended $status: $({ grep . "$OUT/reset-uninstall.txt" || true; } | tail -n 3 | tr '\n' ' ')"
                echo "$line"
                printf '%s\n' "$line" >> "$resets"
            done
            until=$(( $(date +%s) + 120 ))
            while [ "$(date +%s)" -lt "$until" ] && [ -n "$(left_over)" ]; do sleep 2; done
            left="$(left_over)"
            removed="$(comm -23 <(printf '%s\n' "$found" | sort) <(printf '%s\n' "$left" | sort) | grep . || true)"
            printf '  removed: %s; left: %s\n' "$(printf '%s' "${removed:-nothing}" | tr '\t\n' ' ;')" "$(printf '%s' "${left:-nothing}" | tr '\t\n' ' ;')" >> "$resets"
            say "the reset removed: $(printf '%s' "${removed:-nothing}" | tr '\t\n' ' ;'); left: $(printf '%s' "${left:-nothing}" | tr '\t\n' ' ;')"
            if [ -n "$left" ]; then skip_leg "not run: the product's own uninstalls could not bring back the machine '$last' left: $(printf '%s\n' "$left" | tr '\t\n' ' ;')"; exit 0; fi
        fi
        # The harness root, which every uninstall keeps as its contract says, holds the folders and markers an earlier leg's
        # installation made, and a new data directory's runtime refuses them as another's: moved aside whole before a clean
        # leg, never deleted, said in one line and logged, never a proof; on a GitHub-hosted runner alone, as the reset.
        if [ -e "$ROOT" ] || [ -L "$ROOT" ]; then
            if [ "${GITHUB_ACTIONS:-}" != true ] || [ "${RUNNER_ENVIRONMENT:-}" != github-hosted ]; then skip_leg "not run: the harness root $ROOT an earlier leg left is moved aside on a GitHub-hosted runner alone, and this is not one"; exit 0; fi
            roots="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/harness-roots"
            mkdir -p "$roots"
            n=$(( $(find "$roots" -mindepth 1 -maxdepth 1 | wc -l) + 1 ))
            aside="$roots/$n-after-$(printf '%s' "${prior:-no leg}" | tr -c 'A-Za-z0-9._-' '-' | cut -c 1-80)"
            if mv "$ROOT" "$aside"; then line="before '$LEG': the harness root $ROOT, left after '${prior:-no leg}', moved aside to $aside"; else line="before '$LEG': the harness root $ROOT, left after '${prior:-no leg}', could not be moved aside to $aside"; fi
            say "$line"
            printf '%s\n' "$line" >> "$OUT/resets.txt"
            if [ -e "$ROOT" ] || [ -L "$ROOT" ]; then skip_leg "not run: the harness root $ROOT could not be moved aside"; exit 0; fi
        fi
        # What this begin found and excused, for this leg's compare to set aside by the leg that left it.
        record_inherited "$prior"
    fi
    # This leg now changes the machine, and is the one a later leg's start holds to what it leaves.
    printf '%s\n' "$LEG" > "$OUT/last-leg.txt"
    ;;

end)
    printf '%s\n' "$1" >> "$OUT/legs-ended.txt"
    rm -f "$OUT/leg-open.txt"
    # A leg whose own compare differed ran to its end and is recorded as failed already: its step fails here, so that no
    # leg reads as succeeded with a divergence in it.
    if grep -qxF -- "$1" "$OUT/legs-dirty.txt" 2>/dev/null; then
        echo "::error title=Installation::$1 - ended, but its compare differs: it is recorded as failed"
        exit 1
    fi
    say "$1 - ended"
    ;;

held)
    close_leg
    # The final compare: the machine now against the true machine before, through no leg's begin, so that a line any leg
    # left counts here whichever leg was charged for it. Each line is attributed as a begin attributes it, the last leg
    # taking what no begin saw ($OUT/final-attributed.txt); the lines the previous release's own acts leave are expected
    # only where that leg is one of the previous release, and named beside it.
    if [ -f "$OUT/before.txt" ]; then
        record_inherited "$(cat "$OUT/last-leg.txt" 2>/dev/null || true)" final "$OUT/final-attributed.txt"
        bash "$0" compare final before final whole
    fi
    [ ! -s "$OUT/soft-seen.txt" ] || say "seen, and expected until the removal takes them (soft: EXTRACTED_SOFT is set; they fail nothing): $(paste -sd '|' "$OUT/soft-seen.txt" | sed 's/|/ | /g')"
    [ ! -s "$OUT/not-run.txt" ] || cat "$OUT/not-run.txt" >> "$OUT/failed-legs.txt"
    [ ! -s "$OUT/failed-legs.txt" ] || fail "$(paste -sd '|' "$OUT/failed-legs.txt" | sed 's/|/ | /g')$( [ -s "$OUT/divergent.txt" ] && printf '| the machine after an uninstall was not the machine before it: %s' "$(tr '\n' ' ' < "$OUT/divergent.txt")")"
    [ ! -s "$OUT/divergent.txt" ] || fail "the machine after an uninstall was not the machine before it: $(tr '\n' ' ' < "$OUT/divergent.txt")"
    say "the machine after every uninstall was the machine before it, the data directory and the harness root aside"
    ;;

install)
    feed="$1"; version="$2"; script="$3"; pinned="${4:-}"
    said="$OUT/install.txt"
    set --
    if [ "$pinned" != pinned ]; then
        # --no-modify-path, which a release before 0.49 took, is refused on macOS with its own sentence and installs
        # nothing: every install puts jason in the terminal. Recorded, not failed, so the install below is still read.
        refusal="--no-modify-path is not taken on macOS: every install puts jason in the terminal. Nothing was installed: run this again without it."
        status=0
        within 300 'install.sh --no-modify-path' env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$feed" sh "$script" --no-modify-path > "$OUT/install-no-modify-path.txt" 2>&1 || status=$?
        cat "$OUT/install-no-modify-path.txt"
        if [ "$status" -eq 0 ]; then fail_held "install.sh --no-modify-path ended 0"
        elif ! grep -qF -- "$refusal" "$OUT/install-no-modify-path.txt"; then fail_held "install.sh --no-modify-path was refused without its own sentence"
        elif [ -n "$(app)" ] || [ -e "$HOME/.local/bin/jason" ]; then fail_held "the refused install.sh --no-modify-path left $(app) $( [ -e "$HOME/.local/bin/jason" ] && echo "$HOME/.local/bin/jason")"
        else say "install.sh --no-modify-path was refused on macOS with its own sentence, '$refusal', and installed nothing"
        fi
    fi
    if [ "$pinned" = pinned ]; then
        # The pinned line reads its pin: one naming another version than the feed's is refused, naming both, and places
        # nothing, before the pin naming this one installs.
        status=0
        within 300 'install.sh --version 9.9.9' env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$feed" sh "$script" --version 9.9.9 > "$OUT/install-wrong-pin.txt" 2>&1 || status=$?
        cat "$OUT/install-wrong-pin.txt"
        [ "$status" -ne 0 ] || fail "install.sh --version 9.9.9 ended 0 against a feed of $version"
        grep -q "version asked for 9.9.9 and .* names $version; nothing was installed" "$OUT/install-wrong-pin.txt" || fail "install.sh --version 9.9.9 was refused for another reason than the pin"
        [ -z "$(app)" ] || fail "the refused pinned install left $(app)"
        say "install.sh --version 9.9.9 against a feed of $version was refused, naming both versions, and placed nothing: the pin is read"
        set -- --version "$version"
    fi
    # The agent's install: the script read by sh, with a terminal's PATH and a zsh account; by the pinned line,
    # --version, where this leg is the pinned one.
    status=0
    within 300 'install.sh' env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$feed" sh "$script" "$@" > "$said" 2>&1 || status=$?
    cat "$said"
    [ "$status" -eq 0 ] || fail "install.sh $* exited with $status"
    if [ "$pinned" = pinned ]; then say "install.sh ran by the pinned line: --version $version"; else say "install.sh ran unpinned, with no --version, as an agent runs it"; fi
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
    for profile in .zprofile .zshrc; do grep -qx '# added by jason install' "$HOME/$profile" || fail "~/$profile carries no line for ~/.local/bin"; done
    found="$(within 60 'a new login shell finding jason' fresh 'command -v jason; jason --version')" || true
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

enable-starts-now)
    # Whether jason runtime autostart enable starts a runtime at once (the LaunchAgent carries RunAtLoad), against the
    # page's "`enable` starts nothing now": on a machine where no runtime runs, checked first, a runtime looked for for
    # 30 s after it, and what was seen said by name. A fact, asserted neither way.
    placed="$(app)"; carried="$placed/Contents/Resources/bundle/jason"
    if within 60 'jason runtime status before enable' "$carried" runtime status > /dev/null 2>&1; then fail_held "a runtime answered before enable, so whether enable starts one could not be seen"; exit 0; fi
    if ps -axo command= | grep -F "$carried runtime run" | grep -v grep > /dev/null; then fail_held "a runtime process ran before enable, so whether enable starts one could not be seen"; exit 0; fi
    within 60 'jason runtime autostart enable' "$carried" runtime autostart enable > "$OUT/enable-starts-now.json" 2>&1 || fail "jason runtime autostart enable failed: $(cat "$OUT/enable-starts-now.json")"
    cat "$OUT/enable-starts-now.json"
    seen=""
    waited=0
    while [ "$waited" -lt 60 ]; do
        if "$carried" runtime status > /dev/null 2>&1; then seen=yes; break; fi
        sleep 0.5; waited=$((waited + 1))
    done
    if [ -n "$seen" ]; then
        say "enable started the runtime at once (RunAtLoad): a runtime answered $((waited / 2)) s after jason runtime autostart enable, where none ran before"
    else
        say "enable started nothing now: no runtime answered within 30 s after jason runtime autostart enable"
    fi
    say "launchd's records after this build's autostart enable: $(launchd_records | tr '\t\n' ' ;' || true)"
    ;;

refuse-while-open)
    placed="$(app)"; carried="$placed/Contents/Resources/bundle/jason"
    record="$HOME/.agents/skills/.jason-skills.json"
    before="$(stat -f %m "$record" 2>/dev/null || echo none)"
    env PATH="$(plain_path)" "$placed/Contents/MacOS/Jason" > "$OUT/window-open.log" 2>&1 &
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
    # With account-shell, as after the legs that open the application: the uninstall typed in a new terminal of the
    # account's own login shell, and the profiles the product names for that shell held free of the line.
    by="${1:-}"
    case "$by" in ''|account-shell) ;; *) fail "uninstall-terminal takes account-shell or nothing, not '$by'" ;; esac
    placed="$(app)"; carried="$placed/Contents/Resources/bundle/jason"
    # A runtime for the uninstall to stop: the one the application's start left running, once it answers, else one started.
    sleep 5
    if ! within 60 'jason runtime status' "$carried" runtime status > /dev/null 2>&1; then
        within 120 'jason runtime start' "$carried" runtime start > "$OUT/runtime-start.txt" 2>&1 || { cat "$OUT/runtime-start.txt"; ps -axo pid,command | grep -i jason | grep -v grep || true; fail "no runtime could be started before the uninstall"; }
    fi
    within 60 'jason runtime status' "$carried" runtime status > /dev/null 2>&1 || fail "no runtime answers before the uninstall"
    status=0
    if [ "$by" = account-shell ]; then
        shell="$(account_shell)"
        [ -n "$shell" ] || fail "the account's login shell could not be read (dscl . -read /Users/$USER UserShell)"
        within 300 "jason uninstall from a new login shell of $shell" fresh_in "$shell" 'jason uninstall --purge-data --yes' > "$OUT/uninstall-terminal.json" || status=$?
    else
        within 300 'jason uninstall from a new login shell' fresh 'jason uninstall --purge-data --yes' > "$OUT/uninstall-terminal.json" || status=$?
    fi
    cat "$OUT/uninstall-terminal.json"
    [ "$status" -eq 0 ] || fail "jason uninstall from a new login shell exited with $status"
    [ ! -e "$placed" ] || fail "$placed is left"
    [ ! -e "$WRAPPER" ] || fail "the wrapper is left"
    if [ "$by" != account-shell ]; then
        if grep -qx '# added by jason install' "$HOME/.zprofile" 2>/dev/null; then fail "~/.zprofile still carries the line"; fi
    else
        case "${shell##*/}" in
            zsh|bash)
                listed="$(product_profiles "$shell")" || fail "the product's profile functions could not be read from install/install.sh"
                while IFS= read -r profile; do
                    [ -n "$profile" ] || continue
                    if grep -qx '# added by jason install' "$profile" 2>/dev/null; then fail "$profile, which the product names for $shell, still carries the line"; fi
                done <<< "$listed" ;;
            *) say "the account's login shell $shell is neither zsh nor bash: no profile is held free of the line here; the compare reads them all" ;;
        esac
    fi
    [ ! -e "$DATA" ] || fail "~/.jason is left after --purge-data"
    [ -f "$OWN_SKILL/SKILL.md" ] || fail_held "the person's own skill went with the uninstall"
    say "launchd's records after this build's jason uninstall from a new login shell: $(launchd_records | tr '\t\n' ' ;' || true)"
    say "jason uninstall from a new login shell: the runtime stopped, Jason.app, the wrapper, the profile lines, the skills and the data directory gone"
    ;;

uninstall-menu)
    placed="$(app)"; carried="$placed/Contents/Resources/bundle/jason"
    env PATH="$(plain_path)" "$placed/Contents/MacOS/Jason" > "$OUT/window-menu.log" 2>&1 &
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
    # The door writes both streams into the one file: the report is its one JSON line, kept apart in
    # uninstall-menu-report.json; every other line (a wait's sentence, on standard error) is said as such, never parsed.
    python3 -c 'import json,sys
reports, said = [], []
for line in open(sys.argv[1], encoding="utf-8", errors="replace").read().splitlines():
    try:
        value = json.loads(line)
    except ValueError:
        value = None
    (reports if isinstance(value, dict) else said).append(line)
open(sys.argv[3], "w", encoding="utf-8").write("\n".join(line for line in said if line.strip()))
if len(reports) != 1:
    sys.exit(f"{len(reports)} JSON lines, where the report is one")
open(sys.argv[2], "w", encoding="utf-8").write(reports[0] + "\n")' "$OUT/uninstall-menu.json" "$OUT/uninstall-menu-report.json" "$OUT/uninstall-menu-said.txt" > "$OUT/uninstall-menu-read.txt" 2>&1 || read_failed=1
    [ ! -s "$OUT/uninstall-menu-said.txt" ] || say "the menu item's uninstall also said, beside its report: $(tr '\n' ' ' < "$OUT/uninstall-menu-said.txt")"
    # The product's own exit first, so a failed uninstall fails on what it did, never on how its report was read.
    [ "$status" -eq 0 ] || fail "the uninstall the menu item starts exited with $status"
    [ -z "${read_failed:-}" ] || fail "the menu item's uninstall exited 0 and did not write its report as one JSON line: $(cat "$OUT/uninstall-menu-read.txt")"
    [ ! -e "$placed" ] || fail "$placed is left"
    [ -e "$DATA" ] || fail "the data directory went, which only --purge-data takes"
    say "the menu item's uninstall waited for the application to quit, then took Jason.app and everything placed off, the data directory kept"
    ;;

from-previous)
    previous="$1"; feed="$2"; version="$3"
    build="${JASON_PROOF_BUILD:-}"
    case "$build" in test|release) ;; *) fail "JASON_PROOF_BUILD says '$build': it must say test (CI's build) or release (the release's packages)" ;; esac
    # The previous release's command-line Jason, by its own script from its own assets, with a terminal's PATH, a runtime
    # running and its start at login registered.
    within 300 "the previous release's install.sh" env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$previous" sh "$previous/install.sh" || fail "the previous release's install.sh failed"
    old="$(within 60 "the previous release's jason --version" "$WRAPPER" --version | cut -d+ -f1)" || fail "the previous release's jason --version failed"
    within 120 "the previous release's jason runtime start" "$WRAPPER" runtime start > /dev/null || fail "the previous release could not start a runtime"
    within 60 "the previous release's jason runtime autostart enable" "$WRAPPER" runtime autostart enable > "$OUT/autostart.json" || fail "the previous release could not register its logon start: $(cat "$OUT/autostart.json")"
    [ -z "$(app)" ] || fail "an application is in place before the move: $(app)"
    say "the previous release's command-line Jason $old installed by its own script at $WRAPPER, with a runtime running and a logon registration"
    say "launchd's records after the previous release's registration: $(launchd_records | tr '\t\n' ' ;' || true)"

    # Its own update, pinned to this version, from the feed served on this machine. The update is the previous
    # release's; the move is this version's, which it hands the rest to.
    server=""
    trap '[ -z "$server" ] || kill "$server" 2>/dev/null || true' EXIT
    serve_feed "$feed" "$OUT/feed-server.log"
    status=0
    within 900 "the previous release's jason update apply --version $version" "$WRAPPER" update apply --version "$version" --feed http://127.0.0.1:8099/manifest.json --drain-seconds 0 > "$OUT/update-from-previous.json" 2> "$OUT/update-from-previous.err" || status=$?
    cat "$OUT/update-from-previous.err"
    cat "$OUT/update-from-previous.json"
    [ "$status" -eq 0 ] || fail "the previous release's jason update apply --version $version exited with $status"
    read_field() { python3 -c 'import json,sys
doc = json.load(open(sys.argv[1]))
for key in sys.argv[2].split("."):
    doc = doc.get(key) if isinstance(doc, dict) else None
print("" if doc is None else (str(doc).lower() if isinstance(doc, bool) else doc))' "$@"; }
    [ "$(read_field "$OUT/update-from-previous.json" step)" = complete ] || fail "the update did not end complete: $(cat "$OUT/update-from-previous.json")"
    [ "$(read_field "$OUT/update-from-previous.json" to)" = "$version" ] || fail "the update went to $(read_field "$OUT/update-from-previous.json" to), not $version"
    state="$(read_field "$OUT/update-from-previous.json" move.state)"
    [ -n "$state" ] || fail "the update document carries no move: the jason it handed to did not move the install"
    [ "$state" = done ] || fail "the move is $state at $(read_field "$OUT/update-from-previous.json" move.step): $(read_field "$OUT/update-from-previous.json" move.reason)"
    placed="$(app)"
    [ -n "$placed" ] || fail "no Jason.app in /Applications or ~/Applications after the move"
    [ "$(read_field "$OUT/update-from-previous.json" move.application)" = "$placed" ] || fail "the move names $(read_field "$OUT/update-from-previous.json" move.application), and the application is at $placed"
    say "the previous release's own jason update apply --version $version took the feed served on this machine and ended complete, its move done"

    # THE MOVE. The application in place, at this version.
    carried="$placed/Contents/Resources/bundle/jason"
    [ "$(version_of "$placed")" = "$version" ] || fail "$placed is $(version_of "$placed"), not $version"
    if [ -w /Applications ]; then [ "$placed" = /Applications/Jason.app ] || fail "/Applications is writable, and the move put the application at $placed"; fi
    say "Jason Desktop $version is at $placed"

    # The terminal command as the routine places it: ~/.local/bin/jason the application's wrapper, no executable of its
    # own, and a new login shell finds it.
    [ -f "$WRAPPER" ] || fail "no wrapper at $WRAPPER"
    if file "$WRAPPER" | grep -q 'Mach-O'; then fail "$WRAPPER is still an executable, not the application's wrapper"; fi
    grep -q "$carried" "$WRAPPER" || fail "the wrapper at $WRAPPER does not run $carried"
    # The wrapper names no data directory: a runtime starts every child with JASON_DATA_DIR set, the move's routine
    # among them, and the default one is not written, so the wrapper is the one the install script writes.
    ! grep -q 'JASON_DATA_DIR' "$WRAPPER" || fail "the wrapper at $WRAPPER names a data directory after a move with none named: $(cat "$WRAPPER")"
    found="$(within 60 'a new login shell finding jason' fresh 'command -v jason; jason --version')" || true
    [ "$(printf '%s\n' "$found" | sed -n 1p)" = "$WRAPPER" ] || fail "a new login shell finds jason at '$(printf '%s\n' "$found" | sed -n 1p)', not $WRAPPER"
    [ "$(printf '%s\n' "$found" | sed -n 2p | cut -d+ -f1)" = "$version" ] || fail "jason in a new login shell does not say $version: $found"
    say "~/.local/bin/jason is the application's wrapper running $carried, naming no data directory, and a new zsh login shell finds it saying $version"

    # The command-line install retired: its executable replaced by the wrapper, its notice files gone.
    for notice in jason-LICENSE jason-THIRD-PARTY-NOTICES; do [ ! -e "$HOME/.local/bin/$notice" ] || fail "the retired install's $notice is left in ~/.local/bin"; done
    say "the command-line install is retired: no executable of its own at $WRAPPER and no notice file in ~/.local/bin"

    # The runtime answering, from the carried file.
    within 60 'jason runtime status after the move' "$carried" runtime status > "$OUT/status-after-move.json" 2>/dev/null || fail "no runtime answers after the move"
    ps -axo command= | grep -F "$carried runtime run" | grep -v grep > /dev/null || fail "no runtime runs from $carried: $(ps -axo pid,command | grep -F 'runtime run' | grep -v grep || true)"
    say "the runtime answers, running from $carried"

    # The logon registration names the carried file.
    within 60 'jason runtime autostart status' "$carried" runtime autostart status > "$OUT/autostart-after-move.json" 2>/dev/null || fail "jason runtime autostart status failed after the move"
    [ "$(read_field "$OUT/autostart-after-move.json" registered)" = true ] || fail "no logon registration after the move: $(cat "$OUT/autostart-after-move.json")"
    [ "$(read_field "$OUT/autostart-after-move.json" executable)" = "$carried" ] || fail "the logon registration names $(read_field "$OUT/autostart-after-move.json" executable), not $carried"
    say "the logon registration names $carried"
    say "launchd's records after the move: $(launchd_records | tr '\t\n' ' ;' || true)"

    # The move's record, complete.
    [ -f "$DATA/update/move.json" ] || fail "no move record at $DATA/update/move.json"
    [ "$(read_field "$DATA/update/move.json" state)" = done ] || fail "the move record says $(read_field "$DATA/update/move.json" state) at $(read_field "$DATA/update/move.json" step)"
    say "the move record at ~/.jason/update/move.json says done"

    # Whether this build says it is a test build: a release never does, CI's build always.
    said="$("$carried" --version 2>&1 > /dev/null || true)"
    test_build="$(read_field "$OUT/status-after-move.json" test_build)"
    if [ "$build" = release ]; then
        [ -z "$said" ] || fail "a released jason --version wrote on standard error: $said"
        [ -z "$test_build" ] || fail "a released runtime's report carries test_build: $test_build"
        say "released: jason --version writes nothing on standard error, and the runtime's report carries no test_build"
    else
        [ "$said" = 'This jason is a test build, not a release.' ] || fail "CI's test build does not say so on standard error: '$said'"
        [ "$test_build" = true ] || fail "CI's test build's runtime report does not carry test_build: true"
        say "a test build, and it says so: jason --version on standard error, and test_build: true in the runtime's report"
    fi
    ;;

way-back)
    # Going back from 0.49 to the previous release (docs/release-and-update.md, "Going back from 0.49"), on a Mac moved
    # by the previous release's own update: one campaign made under the previous release first; then, under the moved
    # 0.49 with its runtime running, the campaigns counted and the journal read whole; jason uninstall with no
    # --purge-data; the previous release installed by its pinned line from the real release page; its skills, UI, runtime
    # and login start set up again. Asserted alone: the campaigns are as many, every journal entry is still there by id
    # with its kind and moment, no entry has appeared among the old ones, and the previous release's jason says its
    # version and its status is ready. No update and no rollback runs under it. Its own uninstall, purging the data, ends
    # the leg. After the move, the wrapper's text names no data directory.
    # With 'again', a leg of its own: after going back, the previous release's own jason update apply moves the install
    # into Jason Desktop again, as the page says it does; the moved install's own uninstall, purging the data, ends it.
    previous="$1"; feed="$2"; version="$3"; again="${4:-}"
    case "$again" in ''|again) ;; *) fail "way-back takes 'again' or nothing as its fourth word, not '$again'" ;; esac
    # It runs on the account's own home and default data directory, which a moved installation and the previous release
    # share: a GitHub-hosted runner's alone, by the guard at the entry, never a person's Mac.
    # The person's own quick-start, as the page's going back says: named by jason status before step 1, moved out of the
    # skills folder before step 3, then either put back after step 3 (put-back) or kept out while the previous release is
    # installed (keep-out). The one switch is JASON_PROOF_OWN_SKILL_GOING_BACK in ci.yml. Its SKILL.md is read, by its
    # bytes, at four moments: before step 1, after step 3, after the put-back where the words have one, and after the
    # previous release's own uninstall.
    way="${JASON_PROOF_OWN_SKILL_GOING_BACK:-}"
    case "$way" in put-back|keep-out) ;; *) fail "JASON_PROOF_OWN_SKILL_GOING_BACK is '$way', where it says put-back or keep-out" ;; esac
    outside="$RUNNER_TEMP/own-quick-start-out"
    own_out="$outside/quick-start"
    # A quick-start folder's SKILL.md by its bytes: its length and its SHA-256, or absent.
    own_bytes() { if [ -f "$1/SKILL.md" ]; then printf '%s bytes, sha256 %s' "$(wc -c < "$1/SKILL.md" | tr -d ' ')" "$(shasum -a 256 < "$1/SKILL.md" | cut -d' ' -f1)"; else printf absent; fi; }
    # Every item of a listing a jason verb pages by next_cursor, a thousand at a time, as a JSON array.
    read_all() {
        python3 - "$@" <<'PY'
import json, subprocess, sys
jason, words = sys.argv[1], sys.argv[2:]
items, cursor = [], None
for page in range(1000):
    asked = [jason] + words + ['--limit', '1000'] + (['--cursor', cursor] if cursor else [])
    ran = subprocess.run(asked, capture_output=True, text=True, timeout=120)
    if ran.returncode != 0:
        sys.exit('jason %s exited with %d: %s' % (' '.join(words), ran.returncode, ran.stderr.strip()))
    lines = [line for line in ran.stdout.splitlines() if line.lstrip().startswith('{')]
    if not lines:
        sys.exit('jason %s printed no page' % ' '.join(words))
    doc = json.loads(lines[-1])
    items += doc['items']
    cursor = doc.get('next_cursor')
    if not cursor:
        break
else:
    sys.exit('jason %s still had pages after 1000' % ' '.join(words))
json.dump(items, sys.stdout)
PY
    }
    read_field() { python3 -c 'import json,sys
doc = json.load(open(sys.argv[1]))
for key in sys.argv[2].split("."):
    doc = doc.get(key) if isinstance(doc, dict) else None
print("" if doc is None else (str(doc).lower() if isinstance(doc, bool) else doc))' "$@"; }

    within 300 "the previous release's install.sh" env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$previous" sh "$previous/install.sh" > "$OUT/way-back-previous-install.txt" 2>&1 || fail "the previous release's install.sh failed: $(tail -n 3 "$OUT/way-back-previous-install.txt")"
    old="$(within 60 "the previous release's jason --version" "$WRAPPER" --version | cut -d+ -f1)" || fail "the previous release's jason --version failed"
    within 120 "the previous release's jason runtime start" "$WRAPPER" runtime start > /dev/null || fail "the previous release could not start a runtime"
    within 60 "the previous release's jason runtime autostart enable" "$WRAPPER" runtime autostart enable > /dev/null || fail "the previous release could not register its login start"
    within 60 "the previous release's jason campaign create" "$WRAPPER" campaign create --name 'Made before going back' > "$OUT/way-back-campaign.json" || fail "the previous release could not make a campaign: $(cat "$OUT/way-back-campaign.json")"
    say "the previous release's command-line Jason $old installed by its own script, a runtime running, its login start registered and one campaign made"

    server=""
    trap '[ -z "$server" ] || kill "$server" 2>/dev/null || true' EXIT
    serve_feed "$feed" "$OUT/way-back-feed-server.log"
    status=0
    within 900 "the previous release's jason update apply --version $version" "$WRAPPER" update apply --version "$version" --feed http://127.0.0.1:8099/manifest.json --drain-seconds 0 > "$OUT/way-back-update.json" 2> "$OUT/way-back-update.err" || status=$?
    kill "$server" 2>/dev/null || true
    [ "$status" -eq 0 ] || fail "the previous release's jason update apply --version $version exited with $status: $(cat "$OUT/way-back-update.err")"
    [ "$(read_field "$OUT/way-back-update.json" move.state)" = done ] || fail "the update did not end with its move done: $(cat "$OUT/way-back-update.json")"
    placed="$(app)"
    [ -n "$placed" ] || fail "no Jason.app after the move"
    carried="$placed/Contents/Resources/bundle/jason"
    grep -q "$carried" "$WRAPPER" || fail "after the move the wrapper at $WRAPPER does not run $carried"
    # The wrapper names no data directory: a runtime starts every child with JASON_DATA_DIR set, the move's routine
    # among them, and the default one is not written, so the wrapper is the one the install script writes.
    ! grep -q 'JASON_DATA_DIR' "$WRAPPER" || fail "the wrapper at $WRAPPER names a data directory after a move with none named: $(cat "$WRAPPER")"
    say "moved into Jason Desktop $version by the previous release's own update; the wrapper runs $carried and names no data directory"

    # Step 0: under the moved 0.49, its runtime running.
    within 60 'the moved runtime answering' "$carried" runtime status > /dev/null || fail 'no runtime answers under the moved 0.49 before going back'
    read_all "$carried" campaign list --include-archived > "$OUT/way-back-campaigns-before.json" || fail "the campaigns could not be read under the moved 0.49"
    read_all "$carried" journal list > "$OUT/way-back-journal-before.json" || fail "the journal could not be read under the moved 0.49"
    counted="$(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))))' "$OUT/way-back-campaigns-before.json")"
    [ "$counted" -ge 1 ] || fail 'the campaign made under the previous release is not listed under the moved 0.49'
    [ "$(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))))' "$OUT/way-back-journal-before.json")" -ge 1 ] || fail 'the journal is empty under the moved 0.49'
    say "under the moved 0.49: $counted campaign(s), archived included; the journal holds $(python3 -c 'import json,sys; j=json.load(open(sys.argv[1])); print("%d entries, the newest %s" % (len(j), j[0]["id"] if j else "none"))' "$OUT/way-back-journal-before.json")"
    # Before step 1, as the page says: jason status names the person's own quick-start as a folder Jason did not deploy.
    within 120 'jason status under the moved 0.49' "$carried" status > "$OUT/way-back-status-before.json" 2>&1 || true
    names_own_skill "$OUT/way-back-status-before.json" "$OWN_SKILL" \
        || fail "before step 1, jason status under the moved 0.49 does not name the person's own quick-start as a folder Jason did not deploy, under a name Jason ships, left as it is: $(cat "$OUT/way-back-status-before.json")"
    say "before step 1, jason status under the moved 0.49 names the person's own quick-start, $OWN_SKILL, as a folder Jason did not deploy, under a name Jason ships, left as it is"
    own1="$(own_bytes "$OWN_SKILL")"
    [ "$own1" != absent ] || fail "the person's own quick-start is not at $OWN_SKILL before going back"
    say "moment 1, before step 1: the person's own quick-start/SKILL.md at $OWN_SKILL is $own1"

    # Step 1: the leg's own application quit, then jason uninstall with no --purge-data.
    pkill -f '/Jason\.app/Contents/MacOS/' 2>/dev/null || true
    sleep 3
    within 300 'jason uninstall with no --purge-data' "$carried" uninstall > "$OUT/way-back-uninstall.json" 2>&1 || fail "jason uninstall with no --purge-data failed: $(cat "$OUT/way-back-uninstall.json")"
    wait_until 120 "Jason.app going after jason uninstall" gone "$placed"
    [ -d "$DATA" ] || fail "jason uninstall with no --purge-data took the data directory $DATA"
    [ -d "$ROOT" ] || fail "jason uninstall with no --purge-data took the harness root $ROOT"
    say "jason uninstall with no --purge-data took the application off and kept the data directory and the harness root"

    # Step 2: the previous release by its pinned line, from the real release page, as the page says.
    line="curl -fsSL https://get.jasonharness.ai/releases/download/v$old/install.sh | sh -s -- --version $old"
    within 600 "the pinned line of $old" env PATH="$(plain_path)" SHELL=/bin/zsh sh -c "$line" > "$OUT/way-back-pinned.txt" 2>&1 || fail "the pinned line of $old failed: $(tail -n 5 "$OUT/way-back-pinned.txt")"
    [ -x "$WRAPPER" ] || fail "the pinned line of $old placed no jason at $WRAPPER"
    say "$old installed by its pinned line from the real release page: $line"

    # Before step 3: the person's own quick-start moved out of the skills folder, as it stood after steps 1 and 2.
    own="$(own_bytes "$OWN_SKILL")"
    [ "$own" = "$own1" ] || fail "steps 1 and 2 changed the person's own quick-start: it is $own, where it was $own1"
    [ ! -e "$outside" ] || fail "$outside is there before the person's own quick-start is moved to it"
    mkdir -p "$outside"
    mv "$OWN_SKILL" "$own_out" || fail "the person's own quick-start could not be moved to $own_out"
    say "before step 3: the person's own quick-start moved out of the skills folder, to $own_out"

    # Step 3: its skills, its UI, its runtime and its login start.
    for words in 'skills install' 'ui install' 'runtime start' 'runtime autostart enable'; do
        # shellcheck disable=SC2086
        within 600 "jason $words under $old" "$WRAPPER" $words > "$OUT/way-back-step3.txt" 2>&1 || fail "jason $words under $old failed: $(tail -n 3 "$OUT/way-back-step3.txt")"
    done
    say "under $old: jason skills install, ui install, runtime start and runtime autostart enable each ended 0"
    own2="$(own_bytes "$own_out")"
    say "moment 2, after step 3: the person's own SKILL.md, out at $own_out, is $own2; at $OWN_SKILL stands $(own_bytes "$OWN_SKILL")"
    [ "$own2" = "$own1" ] || fail "the person's own quick-start, out of the skills folder, changed during step 3: $own2, where it was $own1"
    if [ "$way" = put-back ]; then
        # The put-back: the person's own quick-start in its place again, over the one the previous release's skills wrote.
        rm -rf "$OWN_SKILL"
        mv "$own_out" "$OWN_SKILL" || fail "the person's own quick-start could not be put back at $OWN_SKILL"
        rmdir "$outside"
        own3="$(own_bytes "$OWN_SKILL")"
        say "moment 3, after the put-back: the person's own SKILL.md at $OWN_SKILL is $own3"
        [ "$own3" = "$own1" ] || fail "the person's own quick-start put back is $own3, where it was $own1"
    else
        say "moment 3: none; the page's words keep the person's own quick-start out while $old is installed"
    fi

    # (i) The campaigns, as many; the journal, held by id.
    read_all "$WRAPPER" campaign list --include-archived > "$OUT/way-back-campaigns-after.json" || fail "the campaigns could not be read under $old"
    read_all "$WRAPPER" journal list > "$OUT/way-back-journal-after.json" || fail "the journal could not be read under $old"
    # Written to a file and read after: the system's bash 3.2 misreads a here-document inside a command substitution.
    python3 - "$OUT/way-back-campaigns-before.json" "$OUT/way-back-campaigns-after.json" "$OUT/way-back-journal-before.json" "$OUT/way-back-journal-after.json" "$old" > "$OUT/way-back-held.txt" <<'PY' || fail "the campaigns and the journal could not be held: see this step's log"
import json, sys
c0, c1, j0, j1 = (json.load(open(path)) for path in sys.argv[1:5])
old = sys.argv[5]
faults = []
if len(c1) != len(c0):
    faults.append('under %s %d campaign(s) are listed, not the %d the moved 0.49 listed' % (old, len(c1), len(c0)))
after = {entry['id']: entry for entry in j1}
known = {entry['id'] for entry in j0}
lost = [entry['id'] for entry in j0 if entry['id'] not in after]
changed = ['%s (%s %s now %s %s)' % (e['id'], e['kind'], e['ts'], after[e['id']]['kind'], after[e['id']]['ts'])
           for e in j0 if e['id'] in after and (after[e['id']]['kind'] != e['kind'] or after[e['id']]['ts'] != e['ts'])]
if lost:
    faults.append('going back lost %d journal entr(ies): %s' % (len(lost), ', '.join(lost[:10])))
if changed:
    faults.append('going back changed the kind or the moment of %d journal entr(ies): %s' % (len(changed), '; '.join(changed[:10])))
newest = j0[0]['id'] if j0 else None
at = next((i for i, entry in enumerate(j1) if entry['id'] == newest), -1)
among = ['%s (%s)' % (e['id'], e['kind']) for e in (j1[at:] if at >= 0 else []) if e['id'] not in known]
if among:
    faults.append('going back put %d entr(ies) among the old ones, at or before %s in the journal\'s order: %s' % (len(among), newest, ', '.join(among[:10])))
if faults:
    print('FAIL ' + ' | '.join(faults))
else:
    print('under %s: %d campaign(s), as before; every one of the %d journal entries is there by id with its kind and its moment, and none has appeared at or before %s; %d newer entr(ies) are listed above it' % (old, len(c1), len(j0), newest, at))
PY
    held="$(cat "$OUT/way-back-held.txt")"
    case "$held" in FAIL\ *) fail "${held#FAIL }" ;; esac
    say "$held"

    # (ii) Its version and its status.
    said="$(within 60 "jason --version under $old" "$WRAPPER" --version 2>/dev/null | cut -d+ -f1)" || fail "jason --version under $old failed"
    [ "$said" = "$old" ] || fail "jason --version says '$said', not $old"
    within 120 "jason status under $old" "$WRAPPER" status > "$OUT/way-back-status.json" 2>/dev/null || true
    [ "$(read_field "$OUT/way-back-status.json" ready)" = true ] || fail "jason status under $old is not ready: $(cat "$OUT/way-back-status.json")"
    say "under $old: jason --version says $said, and jason status says ready: true"

    if [ "$again" = again ]; then
        # Moving again: the previous release's own update apply, pinned to this version, from the feed served here.
        serve_feed "$feed" "$OUT/way-back-again-feed-server.log"
        status=0
        within 900 "$old's jason update apply --version $version after going back" "$WRAPPER" update apply --version "$version" --feed http://127.0.0.1:8099/manifest.json --drain-seconds 0 > "$OUT/way-back-again.json" 2> "$OUT/way-back-again.err" || status=$?
        kill "$server" 2>/dev/null || true
        [ "$status" -eq 0 ] || fail "after going back, $old's jason update apply --version $version exited with $status: $(cat "$OUT/way-back-again.err")"
        [ "$(read_field "$OUT/way-back-again.json" move.state)" = done ] || fail "after going back, the update did not end with its move done: $(cat "$OUT/way-back-again.json")"
        placed="$(app)"
        [ -n "$placed" ] || fail "no Jason.app after moving again"
        carried="$placed/Contents/Resources/bundle/jason"
        [ "$(version_of "$placed")" = "$version" ] || fail "$placed is $(version_of "$placed") after moving again, not $version"
        grep -q "$carried" "$WRAPPER" || fail "after moving again, the wrapper at $WRAPPER does not run $carried"
        # The wrapper names no data directory: a runtime starts every child with JASON_DATA_DIR set, the move's routine
        # among them, and the default one is not written, so the wrapper is the one the install script writes.
        ! grep -q 'JASON_DATA_DIR' "$WRAPPER" || fail "the wrapper at $WRAPPER names a data directory after a move with none named: $(cat "$WRAPPER")"
        within 60 'the runtime answering after moving again' "$carried" runtime status > /dev/null || fail 'no runtime answers after moving again'
        read_all "$carried" campaign list --include-archived > "$OUT/way-back-campaigns-again.json" || fail "the campaigns could not be read after moving again"
        again_count="$(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))))' "$OUT/way-back-campaigns-again.json")"
        [ "$again_count" = "$counted" ] || fail "after moving again $again_count campaign(s) are listed, not the $counted listed before going back"
        say "after going back, $old's own jason update apply --version $version moved the install into Jason Desktop again: $placed at $version, the wrapper naming no data directory, $again_count campaign(s) as before"
        # The leg's own end: the moved install's own uninstall, the application quit first.
        pkill -f '/Jason\.app/Contents/MacOS/' 2>/dev/null || true
        sleep 3
        within 300 'jason uninstall --purge-data --yes after moving again' "$carried" uninstall --purge-data --yes > "$OUT/way-back-end.txt" 2>&1 || fail "jason uninstall --purge-data --yes after moving again failed: $(tail -n 3 "$OUT/way-back-end.txt")"
        wait_until 120 "Jason.app going after jason uninstall" gone "$placed"
        remover="the moved install's own uninstall"
        say "$version's own uninstall --purge-data --yes, after moving again, ended 0"
    else
        # The leg's own end: the runtime stopped, the login start taken off, and the previous release's own uninstall.
        for words in 'runtime stop' 'runtime autostart disable' 'uninstall --purge-data --yes'; do
            # shellcheck disable=SC2086
            within 300 "jason $words under $old" "$WRAPPER" $words > "$OUT/way-back-end.txt" 2>&1 || fail "jason $words under $old failed: $(tail -n 3 "$OUT/way-back-end.txt")"
        done
        remover="$old's own uninstall"
        say "$old's own runtime stop, autostart disable and uninstall --purge-data --yes each ended 0"
    fi
    if [ "$way" = put-back ]; then
        own4="$(own_bytes "$OWN_SKILL")"
        say "moment 4, after $remover: the person's own SKILL.md at $OWN_SKILL is $own4"
        [ "$own4" = "$own1" ] || fail "$remover took or changed the person's own quick-start: it is $own4, where it was $own1"
    else
        own4="$(own_bytes "$own_out")"
        place4="$(own_bytes "$OWN_SKILL")"
        say "moment 4, after $remover: the person's own SKILL.md, out at $own_out, is $own4; at $OWN_SKILL stands $place4"
        [ "$own4" = "$own1" ] || fail "the person's own quick-start, out of the skills folder, changed while $old was installed: $own4, where it was $own1"
        [ ! -e "$OWN_SKILL" ] || fail "after $remover a quick-start stands at $OWN_SKILL ($place4), where the person's own goes back"
        mkdir -p "$(dirname "$OWN_SKILL")"
        mv "$own_out" "$OWN_SKILL" || fail "the person's own quick-start could not be put back at $OWN_SKILL"
        rmdir "$outside"
        back="$(own_bytes "$OWN_SKILL")"
        [ "$back" = "$own1" ] || fail "the person's own quick-start put back is $back, where it was $own1"
        say "the person's own quick-start put back at $OWN_SKILL, $back"
    fi
    ;;

over-previous-desktop)
    # The previous release's Jason Desktop placed as a person places its download, opened and quit as a person leaves it,
    # then this build's install.sh run over it as an agent runs it. A Jason.app before 0.49.0 is never replaced from a
    # terminal (docs/INSTALL.md, "an earlier one is left as it is, and the script ends non-zero saying to update it from
    # its own menu"): asserted, the refusal by its words and nothing changed, held before against after. Then the person
    # takes the old application out instead of updating it from its menu: quit, and moved out of its place as to the Trash
    # (into the runner's temporary folder, which no listing reads; the Trash itself is protected on the runner); and the
    # line, run again, installs: this build in that place, one application only, the terminal command the application's
    # wrapper naming no data directory, and a new login shell finding it, on a machine the previous release's Desktop
    # set up at its first start. The uninstall and the compare after it are the leg's next steps.
    previous="$1"; feed="$2"; version="$3"; script="$4"
    zip="$previous/jason-desktop-macos-arm64.zip"
    [ -f "$zip" ] || fail "no $zip: the step fetching the previous release did not take it"
    [ -z "$(app)" ] || fail "an application is in place before the previous release's is placed: $(app)"
    place=/Applications
    [ -w "$place" ] || { place="$HOME/Applications"; mkdir -p "$place"; }
    # Unpacked where applications are kept, by ditto, which keeps the links, the signature and its ticket.
    within 300 "unpacking $zip" ditto -x -k "$zip" "$place" || fail "$zip could not be unpacked into $place"
    placed="$place/Jason.app"
    within 300 "codesign verifying the previous release's application" codesign --verify --deep --strict "$placed" || fail "the signature of the previous release's $placed does not verify"
    old="$(within 60 "the previous release's carried jason --version" "$placed/Contents/Resources/bundle/jason" --version | cut -d+ -f1)" || fail "the previous release's carried jason --version failed"
    { [ -n "$old" ] && [ "$old" != "$version" ]; } || fail "the previous release's application says '$old', where it must be a version before $version"
    say "the previous release's Jason Desktop $old placed at $placed from its own download, its signature verified"

    # Opened, as a person opens it, then quit, as a person closes it before running a line in a terminal.
    env PATH="$(plain_path)" "$placed/Contents/MacOS/Jason" > "$OUT/over-previous-window.log" 2>&1 &
    window=$!
    sleep 15
    kill -0 "$window" 2>/dev/null || fail "the previous release's Jason Desktop did not stay running: $(tail -n 20 "$OUT/over-previous-window.log")"
    if [ -e "$WRAPPER" ]; then opened_wrapper="$WRAPPER"; else opened_wrapper=none; fi
    say "the previous release's Jason Desktop $old open; the terminal command after its first start: $opened_wrapper"
    end_process "$window" "the previous release's Jason Desktop"
    pkill -f '/Jason\.app/Contents/MacOS/' 2>/dev/null || true
    sleep 3
    say "the previous release's Jason Desktop quit, as a person closes it before installing"

    # This build's line over it, as an agent runs it: refused, and nothing changed. The previous release's first start may
    # have placed a terminal command and profile lines of its own, so nothing is held absent: each is held the same, by
    # its bytes and its modification time, before the line and after it, an absent one staying absent; and the
    # application's version and signature the same.
    held_files() {
        for file in "$WRAPPER" "$HOME/.zshenv" "$HOME/.zprofile" "$HOME/.zshrc" "$HOME/.zlogin" "$HOME/.profile" "$HOME/.bash_profile" "$HOME/.bash_login" "$HOME/.bashrc"; do
            if [ -e "$file" ]; then printf '%s\t%s\t%s\n' "$file" "$(shasum -a 256 < "$file" | cut -d' ' -f1)" "$(stat -f %m "$file")"; else printf '%s\tabsent\n' "$file"; fi
        done
    }
    held_files > "$OUT/over-previous-files-before.txt"
    write_listing "$OUT/over-previous-refusal-before.txt" quiet
    status=0
    within 300 'install.sh over the previous Jason Desktop' env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$feed" sh "$script" > "$OUT/over-previous-refused.txt" 2>&1 || status=$?
    cat "$OUT/over-previous-refused.txt"
    [ "$status" -ne 0 ] || fail "install.sh over the previous Jason Desktop $old ended 0, where a Jason.app before 0.49.0 is left as it is and the script ends non-zero"
    # The sentence as install.sh writes it on stderr, whole, as its own line.
    refusal="install.sh: Jason Desktop $old at $placed is older than 0.49.0 and cannot be set up from a terminal; nothing was changed. Update Jason Desktop from its own menu (Check for Updates), then run the install line again."
    grep -qxF -- "$refusal" "$OUT/over-previous-refused.txt" || fail "install.sh over the previous Jason Desktop exited $status without the line '$refusal': $(tail -n 3 "$OUT/over-previous-refused.txt")"
    held_files > "$OUT/over-previous-files-after.txt"
    cmp -s "$OUT/over-previous-files-before.txt" "$OUT/over-previous-files-after.txt" || fail "install.sh said nothing was changed, and the terminal command or a profile changed: $(diff "$OUT/over-previous-files-before.txt" "$OUT/over-previous-files-after.txt" | grep '^[<>]' | tr '\t\n' ' ;')"
    [ "$(version_of "$placed")" = "$old" ] || fail "after the refusal $placed is $(version_of "$placed"), not $old"
    write_listing "$OUT/over-previous-refusal-after.txt" quiet
    # The data directory and the harness root aside, as every reading of a leg's leftovers sets them aside: a runtime the
    # previous release's window started may still be writing there.
    for when in before after; do grep -vE '^(data directory|harness root)' "$OUT/over-previous-refusal-$when.txt" > "$OUT/over-previous-refusal-$when-held.txt" || true; done
    cmp -s "$OUT/over-previous-refusal-before-held.txt" "$OUT/over-previous-refusal-after-held.txt" || fail "install.sh said nothing was changed, and the machine differs: $(diff "$OUT/over-previous-refusal-before-held.txt" "$OUT/over-previous-refusal-after-held.txt" | grep '^[<>]' | tr '\t\n' ' ;')"
    within 300 "codesign verifying the previous release's application after the refusal" codesign --verify --deep --strict "$placed" || fail "after the refusal the signature of $placed does not verify"
    say "this build's install.sh over Jason Desktop $old exited $status with its refusal, naming Check for Updates; the terminal command and the profiles the same by bytes and time, $placed still $old with its signature verifying, and the machine outside the data directory and the harness root listed the same before and after"

    # The person takes the old application out instead of updating it from its menu: quit, and moved out of its place as
    # to the Trash, into the runner's temporary folder, out of every place a listing reads. Then the line again.
    aside="$OLD_APPS/moved-by-the-leg"
    [ ! -e "$aside" ] || fail "$aside is there before the leg makes it"
    mkdir -p "$aside"
    mv "$placed" "$aside/Jason.app" || fail "$placed could not be moved out of $place"
    [ -z "$(app)" ] || fail "after the previous release's application was moved out, an application stands at $(app)"
    say "the previous release's Jason Desktop $old moved out of $place, as to the Trash"
    status=0
    within 300 'install.sh once the previous Jason Desktop is moved out' env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$feed" sh "$script" > "$OUT/over-previous-install.txt" 2>&1 || status=$?
    cat "$OUT/over-previous-install.txt"
    [ "$status" -eq 0 ] || fail "install.sh once the previous Jason Desktop was moved out exited with $status: $(tail -n 3 "$OUT/over-previous-install.txt")"
    [ "$(app)" = "$placed" ] || fail "install.sh once the previous Jason Desktop was moved out of $placed put the application at '$(app)'"
    for other in /Applications/Jason.app "$HOME/Applications/Jason.app"; do
        [ "$other" = "$placed" ] || [ ! -e "$other" ] || fail "a second application stands at $other beside $placed"
    done
    [ "$(version_of "$placed")" = "$version" ] || fail "$placed is $(version_of "$placed") after install.sh, not $version"
    carried="$placed/Contents/Resources/bundle/jason"
    [ "$(within 60 'the carried jason --version' "$carried" --version | cut -d+ -f1)" = "$version" ] || fail "the carried jason does not say $version"
    say "install.sh run again put Jason Desktop $version in the previous release's place, $placed, and no second application stands"

    # The terminal command: the application's wrapper, naming no data directory, and a new login shell finds it.
    [ -f "$WRAPPER" ] || fail "no wrapper at $WRAPPER"
    if file "$WRAPPER" | grep -q 'Mach-O'; then fail "$WRAPPER is an executable, not the application's wrapper"; fi
    grep -q "$carried" "$WRAPPER" || fail "the wrapper at $WRAPPER does not run $carried"
    ! grep -q 'JASON_DATA_DIR' "$WRAPPER" || fail "the wrapper at $WRAPPER names a data directory, with none named: $(cat "$WRAPPER")"
    found="$(within 60 'a new login shell finding jason' fresh 'command -v jason; jason --version')" || true
    [ "$(printf '%s\n' "$found" | sed -n 1p)" = "$WRAPPER" ] || fail "a new login shell finds jason at '$(printf '%s\n' "$found" | sed -n 1p)', not $WRAPPER"
    [ "$(printf '%s\n' "$found" | sed -n 2p | cut -d+ -f1)" = "$version" ] || fail "jason in a new login shell does not say $version: $found"
    say "~/.local/bin/jason is the application's wrapper running $carried, naming no data directory, and a new zsh login shell finds it saying $version"
    ;;

over-running-cli)
    # The one line over a running command-line install of the previous release: its own install.sh from its own assets,
    # a runtime running, its login start registered and one campaign made; then this build's install.sh, as an agent runs
    # it, with that runtime still running. Asserted: the application at this version; ~/.local/bin/jason the
    # application's wrapper, naming no data directory, and a new login shell finding it; jason install's report saying the
    # old runtime stopped and naming the retired executable; the command-line install retired, no runtime running from it;
    # a runtime running from the carried jason, which install.sh starts again; the login registration naming the carried
    # jason; and the campaigns the same, by id. The uninstall and the compare after it are the leg's next steps.
    previous="$1"; feed="$2"; version="$3"; script="$4"
    read_field() { python3 -c 'import json,sys
doc = json.load(open(sys.argv[1]))
for key in sys.argv[2].split("."):
    doc = doc.get(key) if isinstance(doc, dict) else None
print("" if doc is None else (str(doc).lower() if isinstance(doc, bool) else doc))' "$@"; }
    # The ids of the campaigns a jason lists, archived included, sorted, one a line, into a file: one page of a thousand;
    # more than one page is refused.
    campaign_ids() {
        within 120 "jason campaign list with $1" "$1" campaign list --include-archived --limit 1000 > "$OUT/over-running-cli-campaigns.json" || fail "jason campaign list with $1 failed"
        python3 -c 'import json,sys
doc = json.loads([l for l in open(sys.argv[1]).read().splitlines() if l.lstrip().startswith("{")][-1])
if doc.get("next_cursor"): sys.exit("more than one page of campaigns")
print("\n".join(sorted(item["id"] for item in doc["items"])))' "$OUT/over-running-cli-campaigns.json" > "$2" || fail "the campaigns $1 lists could not be read"
    }
    # A member of jason install's report, the last document install.sh printed that has a command and a runtime.
    report_field() { python3 -c 'import json,sys
doc = None
for line in open(sys.argv[1]).read().splitlines():
    line = line.strip()
    if not line.startswith("{"):
        continue
    try:
        found = json.loads(line)
    except ValueError:
        continue
    if isinstance(found, dict) and "command" in found and "runtime" in found:
        doc = found
if doc is None:
    sys.exit("no report of jason install")
for key in sys.argv[2].split("."):
    doc = doc.get(key) if isinstance(doc, dict) else None
print("" if doc is None else doc)' "$@"; }
    # The processes whose command line runs a runtime from the file named.
    runtimes_of() { ps -axo pid=,command= | grep -F "$1 runtime run" | grep -v grep || true; }
    within 300 "the previous release's install.sh" env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$previous" sh "$previous/install.sh" > "$OUT/over-running-cli-previous.txt" 2>&1 || fail "the previous release's install.sh failed: $(tail -n 3 "$OUT/over-running-cli-previous.txt")"
    old="$(within 60 "the previous release's jason --version" "$WRAPPER" --version | cut -d+ -f1)" || fail "the previous release's jason --version failed"
    # The previous release, held: an empty or this build's own version would prove this build over itself.
    { [ -n "$old" ] && [ "$old" != "$version" ]; } || fail "the previous release's jason says '$old', where it must be a version before $version"
    within 120 "the previous release's jason runtime start" "$WRAPPER" runtime start > /dev/null || fail "the previous release could not start a runtime"
    within 60 "the previous release's jason runtime autostart enable" "$WRAPPER" runtime autostart enable > /dev/null || fail "the previous release could not register its login start"
    within 60 "the previous release's jason campaign create" "$WRAPPER" campaign create --name 'Made before the one line' > "$OUT/over-running-cli-campaign.json" || fail "the previous release could not make a campaign: $(cat "$OUT/over-running-cli-campaign.json")"
    made="$(read_field "$OUT/over-running-cli-campaign.json" id)" || fail "the campaign made under the previous release has no id: $(cat "$OUT/over-running-cli-campaign.json")"
    [ -n "$made" ] || fail "the campaign made under the previous release has no id: $(cat "$OUT/over-running-cli-campaign.json")"
    campaign_ids "$WRAPPER" "$OUT/over-running-cli-ids-before.txt"
    grep -qx "$made" "$OUT/over-running-cli-ids-before.txt" || fail "the campaign $made made under the previous release is not listed"
    within 60 "the previous release's runtime answering" "$WRAPPER" runtime status > /dev/null || fail "no runtime answers under the previous release before the one line"
    # What the retirement later takes, held first to be there: the notice files, and a runtime from $WRAPPER found by the
    # same match that later finds none, so that a match which never finds anything cannot pass.
    for notice in jason-LICENSE jason-THIRD-PARTY-NOTICES; do [ -f "$HOME/.local/bin/$notice" ] || fail "the previous release's install.sh placed no $notice in ~/.local/bin"; done
    [ -n "$(runtimes_of "$WRAPPER")" ] || fail "no process runs '$WRAPPER runtime run' before the one line, so finding none after it would prove nothing; the jason processes: $(ps -axo pid=,command= | grep -F jason | grep -v grep || true)"
    say "the previous release's command-line Jason $old installed by its own script at $WRAPPER with its notice files, a runtime running from it (pid $(runtimes_of "$WRAPPER" | awk '{print $1}' | tr '\n' ' ')), its login start registered, $(wc -l < "$OUT/over-running-cli-ids-before.txt" | tr -d ' ') campaign(s)"

    # This build's line, as an agent runs it, with that runtime still running.
    status=0
    within 300 'install.sh over the running command-line install' env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$feed" sh "$script" > "$OUT/over-running-cli-install.txt" 2>&1 || status=$?
    cat "$OUT/over-running-cli-install.txt"
    [ "$status" -eq 0 ] || fail "this build's install.sh over the running $old exited with $status"
    # jason install's report: the old runtime stopped, and the executable retired the one $old placed.
    said_runtime="$(report_field "$OUT/over-running-cli-install.txt" runtime)" || fail "install.sh printed no report of jason install"
    [ "$said_runtime" = stopped ] || fail "jason install's report says the old runtime is '$said_runtime', not stopped"
    said_retired="$(report_field "$OUT/over-running-cli-install.txt" retired.executable)" || fail "install.sh printed no report of jason install"
    [ "$said_retired" = "$WRAPPER" ] || fail "jason install's report retired '$said_retired', not $WRAPPER"
    say "this build's install.sh ran over the running command-line $old and ended 0; jason install's report says the old runtime stopped and retired $said_retired"

    # The application in place, at this version.
    placed="$(app)"
    [ -n "$placed" ] || fail "no Jason.app in /Applications or ~/Applications after the one line"
    carried="$placed/Contents/Resources/bundle/jason"
    [ "$(version_of "$placed")" = "$version" ] || fail "$placed is $(version_of "$placed"), not $version"

    # The terminal command: the application's wrapper, naming no data directory, and a new login shell finds it.
    [ -f "$WRAPPER" ] || fail "no wrapper at $WRAPPER"
    if file "$WRAPPER" | grep -q 'Mach-O'; then fail "$WRAPPER is still an executable, not the application's wrapper"; fi
    grep -q "$carried" "$WRAPPER" || fail "the wrapper at $WRAPPER does not run $carried"
    ! grep -q 'JASON_DATA_DIR' "$WRAPPER" || fail "the wrapper at $WRAPPER names a data directory, with none named: $(cat "$WRAPPER")"
    found="$(within 60 'a new login shell finding jason' fresh 'command -v jason; jason --version')" || true
    [ "$(printf '%s\n' "$found" | sed -n 1p)" = "$WRAPPER" ] || fail "a new login shell finds jason at '$(printf '%s\n' "$found" | sed -n 1p)', not $WRAPPER"
    [ "$(printf '%s\n' "$found" | sed -n 2p | cut -d+ -f1)" = "$version" ] || fail "jason in a new login shell does not say $version: $found"
    say "Jason Desktop $version is at $placed; ~/.local/bin/jason is its wrapper running $carried, naming no data directory, and a new zsh login shell finds it saying $version"

    # The command-line install retired: its notice files gone, no runtime running from its executable.
    for notice in jason-LICENSE jason-THIRD-PARTY-NOTICES; do [ ! -e "$HOME/.local/bin/$notice" ] || fail "the retired install's $notice is left in ~/.local/bin"; done
    [ -z "$(runtimes_of "$WRAPPER")" ] || fail "a runtime still runs from the retired $WRAPPER: $(runtimes_of "$WRAPPER")"
    say "the command-line install is retired: no notice file in ~/.local/bin, and no runtime runs from $WRAPPER"

    # The runtime install.sh starts again after jason install stopped the old one: answering, and running from the carried
    # jason. None is started here.
    within 60 'a runtime answering after the one line' "$carried" runtime status > "$OUT/over-running-cli-status.txt" 2>&1 || fail "no runtime answers after the one line, where install.sh starts one again after jason install stopped the old: $(cat "$OUT/over-running-cli-status.txt")"
    [ -n "$(runtimes_of "$carried")" ] || fail "a runtime answers after the one line, but none runs from $carried; the jason processes: $(ps -axo pid=,command= | grep -F jason | grep -v grep || true)"
    say "a runtime answers after the one line, running from $carried (pid $(runtimes_of "$carried" | awk '{print $1}' | tr '\n' ' '))"

    # The data kept, by id, and the login registration naming the carried file.
    campaign_ids "$carried" "$OUT/over-running-cli-ids-after.txt"
    cmp -s "$OUT/over-running-cli-ids-before.txt" "$OUT/over-running-cli-ids-after.txt" || fail "after the one line the campaigns listed are not the ones listed before it: $(diff "$OUT/over-running-cli-ids-before.txt" "$OUT/over-running-cli-ids-after.txt" | tr '\n' ' ')"
    after_count="$(wc -l < "$OUT/over-running-cli-ids-after.txt" | tr -d ' ')"
    within 60 'jason runtime autostart status' "$carried" runtime autostart status > "$OUT/over-running-cli-autostart.json" 2>/dev/null || fail "jason runtime autostart status failed after the one line"
    [ "$(read_field "$OUT/over-running-cli-autostart.json" registered)" = true ] || fail "no login registration after the one line: $(cat "$OUT/over-running-cli-autostart.json")"
    [ "$(read_field "$OUT/over-running-cli-autostart.json" executable)" = "$carried" ] || fail "the login registration names $(read_field "$OUT/over-running-cli-autostart.json" executable), not $carried"
    say "after the one line: the same $after_count campaign(s), by id, and the login registration names $carried"
    ;;

bundle-swap)
    # The previous release's Jason Desktop, its bundle replaced by this build's as its updater replaces it, sets the
    # machine up at its first start. Its real download placed where applications are kept, its signature verified, started
    # once and let do what its first start does, then quit; the machine as it then stands listed, a fact. Then the swap an
    # update makes, made by the leg: a runtime of the old application's own jason stopped by it, as its updater does
    # first; this build's application copied beside it by ditto, keeping the signature the test build has, and renamed
    # into the same place, the old bundle moved out. Then this build's application started, and
    # held by name once its first start's set-up has run: the wrapper at ~/.local/bin/jason running the carried jason and
    # naming no data directory; a new login shell's jason saying this build; one jason found by name and no runtime but
    # the carried one's; the skills recorded for the hosts found, the person's own quick-start left alone and named by
    # jason status; where the previous release had its start at login, the registration naming the carried jason; and
    # jason status printed whole. With running-cli, the same over a command-line install of the previous release at
    # ~/.local/bin/jason whose runtime runs before the swap, and also held: a runtime answering from the carried jason
    # (looked for 120 s, since it starts only once the set-up's finishing step returns), the note that a start is owed
    # gone, the old executable now the wrapper, its notice files gone, and the campaigns the same by id. In both, the
    # set-up's routine ended 0 and completed. Without it, the set-up record the first start writes once the set-up
    # completes; with it, no record is held, since the finishing step reads the public feed, where a test build has no
    # release, and writes none. Whether the previous release's quit left its own runtime running is said either way. Not proved here: the old
    # application's own updater, its download, its signature check and its swap; the swap is the leg's. The uninstall and
    # the compare after it are the leg's next steps.
    previous="$1"; built="$2"; version="$3"; mode="${4:-}"
    case "$mode" in ''|running-cli) ;; *) fail "bundle-swap takes running-cli or nothing after the version, not '$mode'" ;; esac
    zip="$previous/jason-desktop-macos-arm64.zip"
    [ -f "$zip" ] || fail "no $zip: the step fetching the previous release did not take it"
    [ -d "$built" ] || fail "no application at $built"
    [ -z "$(app)" ] || fail "an application is in place before the leg: $(app)"
    [ ! -e "$WRAPPER" ] || fail "a terminal command is in place before the leg: $WRAPPER"
    read_field() { python3 -c 'import json,sys
doc = json.loads([l for l in open(sys.argv[1]).read().splitlines() if l.lstrip().startswith("{")][-1])
for key in sys.argv[2].split("."):
    doc = doc.get(key) if isinstance(doc, dict) else None
print("" if doc is None else (str(doc).lower() if isinstance(doc, bool) else doc))' "$@"; }
    campaign_ids() {
        within 120 "jason campaign list with $1" "$1" campaign list --include-archived --limit 1000 > "$OUT/bundle-swap-campaigns.json" || fail "jason campaign list with $1 failed"
        python3 -c 'import json,sys
doc = json.loads([l for l in open(sys.argv[1]).read().splitlines() if l.lstrip().startswith("{")][-1])
if doc.get("next_cursor"): sys.exit("more than one page of campaigns")
print("\n".join(sorted(item["id"] for item in doc["items"])))' "$OUT/bundle-swap-campaigns.json" > "$2" || fail "the campaigns $1 lists could not be read"
    }
    runtimes_of() { ps -axo pid=,command= | grep -F "$1 runtime run" | grep -v grep || true; }
    state_of() { if [ -e "$1" ]; then printf '%s %s' "$(shasum -a 256 < "$1" | cut -d' ' -f1)" "$(stat -f %m "$1")"; else printf absent; fi; }
    record="$DATA/desktop/machine-setup.json"
    owed="$DATA/run/start-owed.json"

    # With running-cli: the previous release's command-line Jason by its own script, a runtime running from it, its login
    # start registered and one campaign made; the notice files and the runtime held first to be there.
    if [ "$mode" = running-cli ]; then
        within 300 "the previous release's install.sh" env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$previous" sh "$previous/install.sh" > "$OUT/bundle-swap-cli.txt" 2>&1 || fail "the previous release's install.sh failed: $(tail -n 3 "$OUT/bundle-swap-cli.txt")"
        cli_old="$(within 60 "the previous release's jason --version" "$WRAPPER" --version | cut -d+ -f1)" || fail "the previous release's jason --version failed"
        { [ -n "$cli_old" ] && [ "$cli_old" != "$version" ]; } || fail "the previous release's jason says '$cli_old', where it must be a version before $version"
        within 120 "the previous release's jason runtime start" "$WRAPPER" runtime start > /dev/null || fail "the previous release could not start a runtime"
        within 60 "the previous release's jason runtime autostart enable" "$WRAPPER" runtime autostart enable > /dev/null || fail "the previous release could not register its login start"
        within 60 "the previous release's jason campaign create" "$WRAPPER" campaign create --name 'Made before the swap' > "$OUT/bundle-swap-campaign.json" || fail "the previous release could not make a campaign: $(cat "$OUT/bundle-swap-campaign.json")"
        made="$(read_field "$OUT/bundle-swap-campaign.json" id)" || fail "the campaign made under the previous release has no id"
        [ -n "$made" ] || fail "the campaign made under the previous release has no id: $(cat "$OUT/bundle-swap-campaign.json")"
        campaign_ids "$WRAPPER" "$OUT/bundle-swap-ids-before.txt"
        grep -qx "$made" "$OUT/bundle-swap-ids-before.txt" || fail "the campaign $made made under the previous release is not listed"
        for notice in jason-LICENSE jason-THIRD-PARTY-NOTICES; do [ -f "$HOME/.local/bin/$notice" ] || fail "the previous release's install.sh placed no $notice in ~/.local/bin"; done
        [ -n "$(runtimes_of "$WRAPPER")" ] || fail "no process runs '$WRAPPER runtime run' after the previous release's runtime start; the jason processes: $(ps -axo pid=,command= | grep -F jason | grep -v grep || true)"
        say "the previous release's command-line Jason $cli_old installed by its own script at $WRAPPER, a runtime running from it, its login start registered, $(wc -l < "$OUT/bundle-swap-ids-before.txt" | tr -d ' ') campaign(s)"
    fi

    # The previous release's Jason Desktop, from its own download, started once and quit.
    place=/Applications
    [ -w "$place" ] || { place="$HOME/Applications"; mkdir -p "$place"; }
    within 300 "unpacking $zip" ditto -x -k "$zip" "$place" || fail "$zip could not be unpacked into $place"
    placed="$place/Jason.app"
    within 300 "codesign verifying the previous release's application" codesign --verify --deep --strict "$placed" || fail "the signature of the previous release's $placed does not verify"
    old="$(version_of "$placed")"
    { [ -n "$old" ] && [ "$old" != "$version" ]; } || fail "the previous release's application says '$old', where it must be a version before $version"
    # The application is opened in this leg, by LaunchServices, with the environment it gives; the desktop's log kept on the
    # way out once this build's start has begun.
    trap '[ -z "${desktop_log:-}" ] || keep_desktop_log "$desktop_log"' EXIT
    say_open_environment
    window="$(open_app "$placed" "$OUT/bundle-swap-previous-window.log")" || fail "the previous release's Jason Desktop could not be opened from $placed: $(cat "$OUT/open-app-failure.txt" 2>/dev/null)"
    say "the previous release's Jason Desktop opened by open from $placed, its process $window running from that bundle, with SHELL '$(app_shell_of "$window")'"
    sleep 30
    kill -0 "$window" 2>/dev/null || fail "the previous release's Jason Desktop did not stay running: $(tail -n 20 "$OUT/bundle-swap-previous-window.log")"
    end_process "$window" "the previous release's Jason Desktop"
    pkill -f '/Jason\.app/Contents/MacOS/' 2>/dev/null || true
    sleep 3
    # Whether the previous release's quit left a runtime of its own running: a fact, either way.
    if [ -n "$(runtimes_of "$placed/Contents/Resources/bundle/jason")" ]; then
        say "the previous release's quit left its own runtime running (pid $(runtimes_of "$placed/Contents/Resources/bundle/jason" | awk '{print $1}' | tr '\n' ' '))"
    else
        say "the previous release's quit left no runtime of its own running"
    fi
    write_listing "$OUT/bundle-swap-before-swap.txt" quiet
    say "the previous release's Jason Desktop $old placed at $placed from its own download, its signature verified, started once for 30 s and quit; the machine then, $(grep -c . "$OUT/bundle-swap-before-swap.txt") line(s), is in bundle-swap-before-swap.txt"
    cat "$OUT/bundle-swap-before-swap.txt"
    # Whether the previous release had its start at login, by its own jason: the command-line one's with running-cli,
    # else the application's.
    if [ "$mode" = running-cli ]; then asked="$WRAPPER"; else asked="$placed/Contents/Resources/bundle/jason"; fi
    within 60 "the previous release's jason runtime autostart status" "$asked" runtime autostart status > "$OUT/bundle-swap-autostart-before.json" 2>/dev/null || fail "the previous release's jason runtime autostart status failed"
    had_login="$(read_field "$OUT/bundle-swap-autostart-before.json" registered)"
    say "before the swap the previous release's start at login is registered: ${had_login:-unknown}; launchd's records: $(launchd_records | tr '\t\n' ' ;' || true)"
    if [ "$mode" = running-cli ]; then
        [ -n "$(runtimes_of "$WRAPPER")" ] || fail "the previous release's command-line runtime no longer runs before the swap; the jason processes: $(ps -axo pid=,command= | grep -F jason | grep -v grep || true)"
        say "the previous release's command-line runtime still runs from $WRAPPER before the swap"
    fi

    # The swap an update makes, made here: a runtime the old application's own jason runs stopped by that jason first, as
    # its updater stops it (a runtime of another executable, the command-line one, it leaves running); this build's
    # application copied beside the old one by ditto, which keeps the signature the test build has, then renamed into its
    # place, the old bundle moved out.
    old_carried="$placed/Contents/Resources/bundle/jason"
    if [ -n "$(runtimes_of "$old_carried")" ]; then
        within 120 "the previous application's jason runtime stop" "$old_carried" runtime stop > "$OUT/bundle-swap-old-stop.txt" 2>&1 || fail "the previous application's jason could not stop its runtime: $(cat "$OUT/bundle-swap-old-stop.txt")"
        say "a runtime ran from the previous application's own jason; stopped by it, as its updater stops it before the swap"
    else
        say "no runtime ran from the previous application's own jason before the swap"
    fi
    staged="$place/.Jason-swap.app"
    swapped="$OLD_APPS/swapped-out-by-the-leg"
    [ ! -e "$staged" ] && [ ! -e "$swapped" ] || fail "$staged or $swapped is there before the leg makes it"
    within 300 'ditto copying this build beside the previous application' ditto "$built" "$staged" || fail "this build's application could not be copied to $staged"
    mkdir -p "$swapped"
    mv "$placed" "$swapped/Jason.app" || fail "the previous release's application could not be moved out of $placed"
    mv "$staged" "$placed" || fail "this build's application could not be renamed into $placed"
    within 300 "codesign verifying this build's application after the swap" codesign --verify --deep --strict "$placed" || fail "this build's application at $placed does not verify after the swap"
    [ "$(version_of "$placed")" = "$version" ] || fail "after the swap $placed is $(version_of "$placed"), not $version"
    carried="$placed/Contents/Resources/bundle/jason"
    say "the bundle at $placed swapped for this build's $version, its signature verifying, the previous release's moved out to $swapped"

    # This build's application started: its first start's set-up read in its log, looked for for 180 s.
    ran_before="$(start_setup_runs)" || fail "the desktop's log could not be read before the start"
    finished_before="$(start_setup_finishes)" || fail "the desktop's log could not be read before the start"
    record_before="$(state_of "$record")"
    # The desktop's log kept from the start on, whatever the leg comes to.
    desktop_log="bundle-swap${mode:+-$mode}-desktop-log.jsonl"
    say_open_environment
    read_terminal_before
    window="$(open_app "$placed" "$OUT/bundle-swap-window.log")" || fail "this build's application could not be opened from $placed after the swap: $(cat "$OUT/open-app-failure.txt" 2>/dev/null)"
    say "this build's application opened by open from $placed, its process $window running from that bundle, with SHELL '$(app_shell_of "$window")'"
    until=$(( $(date +%s) + 180 ))
    ran_after="$ran_before"
    while [ "${ran_after%% *}" -le "${ran_before%% *}" ] && [ "$(date +%s)" -lt "$until" ]; do
        kill -0 "$window" 2>/dev/null || fail "this build's application did not stay running after the swap: $(tail -n 20 "$OUT/bundle-swap-window.log")"
        sleep 2
        ran_after="$(start_setup_runs)" || fail "the desktop's log could not be read after the start"
    done
    keep_desktop_log "$desktop_log"
    [ "${ran_after%% *}" -gt "${ran_before%% *}" ] || fail "this build's application wrote no machine_setup_ran line at its start within 180 s of it"
    set -- $ran_after
    ran_exit="$2"; ran_completed="$3"
    say "this build's first start after the swap ran the set-up: jason install exited $ran_exit, completed $ran_completed"
    [ "$ran_exit" = 0 ] || fail "the set-up at this build's first start after the swap exited $ran_exit"
    # Completed is the routine's own (jason install, which runs offline), never the finishing step's: held in both halves.
    [ "$ran_completed" = true ] || fail "the set-up at this build's first start after the swap did not complete"
    # The finishing step after the routine, waited for before any jason here runs, since a jason that writes under the data
    # directory first would make the finishing step find an installation: its line looked for for 120 s. Without
    # running-cli it completes, and the record is written; with it, it reads the public feed, where a test build has no
    # release, and its outcome is said.
    until=$(( $(date +%s) + 120 ))
    finished_after="$finished_before"
    while [ "${finished_after%% *}" -le "${finished_before%% *}" ] && [ "$(date +%s)" -lt "$until" ]; do
        sleep 2
        finished_after="$(start_setup_finishes)" || fail "the desktop's log could not be read after the start"
    done
    keep_desktop_log "$desktop_log"
    [ "${finished_after%% *}" -gt "${finished_before%% *}" ] || fail "this build's application wrote no machine_setup_finished line at its start within 120 s of the set-up"
    say "the finishing step after the set-up ran: completed ${finished_after#* }"
    [ "$mode" = running-cli ] || [ "${finished_after#* }" = true ] || fail "the finishing step after the set-up at this build's first start after the swap did not complete"
    # The runtime: looked for for 120 s from the carried jason, since it starts only once the finishing step returns.
    until=$(( $(date +%s) + 120 ))
    answered=""
    while [ "$(date +%s)" -lt "$until" ]; do
        if within 20 'jason runtime status from the carried jason' "$carried" runtime status > "$OUT/bundle-swap-runtime.txt" 2>&1 && [ -n "$(runtimes_of "$carried")" ]; then answered=1; break; fi
        sleep 2
    done
    if [ "$mode" = running-cli ]; then
        [ -n "$answered" ] || fail "no runtime answered from $carried within 120 s of the set-up: $(cat "$OUT/bundle-swap-runtime.txt"); the jason processes: $(ps -axo pid=,command= | grep -F jason | grep -v grep || true)"
        say "a runtime answers from $carried (pid $(runtimes_of "$carried" | awk '{print $1}' | tr '\n' ' '))"
    else
        say "a runtime from $carried after the first start: $(if [ -n "$answered" ]; then echo "answering (pid $(runtimes_of "$carried" | awk '{print $1}' | tr '\n' ' '))"; else echo 'none within 120 s'; fi)"
    fi

    # The terminal command: the application's wrapper, naming no data directory; a new login shell finds it, saying this
    # build; one jason by that name; no runtime but the carried jason's.
    [ -f "$WRAPPER" ] || fail "no wrapper at $WRAPPER after this build's first start"
    if file "$WRAPPER" | grep -q 'Mach-O'; then fail "$WRAPPER is an executable, not the application's wrapper"; fi
    grep -q "$carried" "$WRAPPER" || fail "the wrapper at $WRAPPER does not run $carried"
    ! grep -q 'JASON_DATA_DIR' "$WRAPPER" || fail "the wrapper at $WRAPPER names a data directory, with none named: $(cat "$WRAPPER")"
    : > "$OUT/bundle-swap-report-profiles.txt"
    if [ -f "$record" ]; then report_profiles "$record" "$OUT/bundle-swap-report-profiles.txt" || true; fi
    # With running-cli the finishing step writes no record (it reads the public feed), so there is no done to read.
    if [ "$mode" = running-cli ]; then hold_account_terminal "$version" "$OUT/bundle-swap-report-profiles.txt"; else hold_account_terminal "$version" "$OUT/bundle-swap-report-profiles.txt" "$record"; fi
    others="$(ps -axo pid=,command= | grep -E '/jason runtime run' | grep -v grep | grep -vF "$carried runtime run" || true)"
    [ -z "$others" ] || fail "a runtime runs from another jason than $carried: $others"
    say "~/.local/bin/jason is the application's wrapper running $carried, naming no data directory, and no runtime runs from another jason"

    # The skills of the hosts found, recorded; the person's own quick-start left alone and named by jason status.
    for root in .claude/skills .agents/skills; do [ -f "$HOME/$root/.jason-skills.json" ] || fail "no skills record in ~/$root after this build's first start"; done
    within 120 'jason status' "$carried" status > "$OUT/bundle-swap-status.json" 2>/dev/null || true
    if grep -q "person's own" "$OUT/failed-legs.txt" 2>/dev/null; then
        say "the skills of Claude Code and Codex are recorded in both folders; the person's own quick-start was lost in a leg before, as recorded, so this start's handling of it is not seen"
    elif [ -f "$OWN_SKILL/SKILL.md" ]; then
        [ "$(cat "$OWN_SKILL/SKILL.md")" = "a person's own skill" ] || fail "the person's own skill at $OWN_SKILL was changed by this build's first start"
        if grep -q '"quick-start/' "$HOME/.agents/skills/.jason-skills.json"; then fail "the record in ~/.agents/skills claims the person's own quick-start"; fi
        names_own_skill "$OUT/bundle-swap-status.json" "$OWN_SKILL" || fail "jason status does not name the person's own quick-start, $OWN_SKILL, as a folder Jason did not deploy"
        say "the skills of Claude Code and Codex are recorded in both folders, and the person's own quick-start is left alone, named by jason status"
    else
        say "the skills of Claude Code and Codex are recorded in both folders; the person's own quick-start is gone already, so this start's handling of it is not seen"
    fi

    # The start at login: where the previous release had one, it now names the carried jason.
    within 60 'jason runtime autostart status' "$carried" runtime autostart status > "$OUT/bundle-swap-autostart.json" 2>/dev/null || fail "jason runtime autostart status failed after this build's first start"
    if [ "$had_login" = true ]; then
        [ "$(read_field "$OUT/bundle-swap-autostart.json" registered)" = true ] || fail "the previous release had its start at login, and after the swap none is registered: $(cat "$OUT/bundle-swap-autostart.json")"
        [ "$(read_field "$OUT/bundle-swap-autostart.json" executable)" = "$carried" ] || fail "the start at login names $(read_field "$OUT/bundle-swap-autostart.json" executable), not $carried"
        say "the previous release had its start at login, and it now names $carried"
    else
        say "the previous release had no start at login; after the swap it is registered: $(read_field "$OUT/bundle-swap-autostart.json" registered)"
    fi

    if [ "$mode" = running-cli ]; then
        # The command-line install retired: the note that a start is owed gone, the old executable now the wrapper (held
        # above), its notice files gone, no runtime from it, and the campaigns the same by id.
        [ ! -e "$owed" ] || fail "the note that a start is owed stands at $owed after this build's first start: $(cat "$owed")"
        for notice in jason-LICENSE jason-THIRD-PARTY-NOTICES; do [ ! -e "$HOME/.local/bin/$notice" ] || fail "the retired install's $notice is left in ~/.local/bin"; done
        [ -z "$(runtimes_of "$WRAPPER")" ] || fail "a runtime still runs from the retired $WRAPPER: $(runtimes_of "$WRAPPER")"
        campaign_ids "$carried" "$OUT/bundle-swap-ids-after.txt"
        cmp -s "$OUT/bundle-swap-ids-before.txt" "$OUT/bundle-swap-ids-after.txt" || fail "after the swap the campaigns listed are not the ones listed before it: $(diff "$OUT/bundle-swap-ids-before.txt" "$OUT/bundle-swap-ids-after.txt" | tr '\n' ' ')"
        say "the command-line install is retired: no note of an owed start, no notice file in ~/.local/bin, no runtime from $WRAPPER; the same $(wc -l < "$OUT/bundle-swap-ids-after.txt" | tr -d ' ') campaign(s), by id"
        say "the set-up record at $record: $(state_of "$record") (not held: the finishing step reads the public feed, where a test build has no release)"
    else
        record_after="$(state_of "$record")"
        [ "$record_after" != absent ] && [ "$record_after" != "$record_before" ] || fail "this build's first start wrote no set-up record at $record (before: $record_before; after: $record_after)"
        say "this build's first start wrote the set-up record at $record"
    fi

    say "jason status after this build's first start, whole:"
    cat "$OUT/bundle-swap-status.json"
    end_process "$window" "this build's application"
    pkill -f "$placed/Contents/MacOS/" 2>/dev/null || true
    sleep 3
    ;;

vanishing-place)
    # An application opened from its disk image sets nothing up: this build's application put in a read-only disk image,
    # as a download is, and the image mounted; jason install from the application on it, as its first start runs it,
    # refused with install_vanishing_place; the application opened from the image as a person opens a download, its own
    # start running the setup and refused, and quit; the image ejected. Asserted: the refusal by its code and its words,
    # nothing placed, the start's setup run and refused and no setup record written, the image gone, and no jason in a new
    # terminal after the eject. The listing and the compare after it are the leg's next steps. On any failure between the
    # staging and the detach, the staged copy and the image are removed, the application from the image ended and the image
    # detached, so that no later leg finds it holding the data directory.
    built="$1"
    [ -d "$built" ] || fail "no application at $built"
    [ -z "$(app)" ] || fail "an application is in place before the leg: $(app)"
    [ ! -e "$WRAPPER" ] || fail "a terminal command is in place before the leg: $WRAPPER"
    data="${JASON_DATA_DIR:-$HOME/.jason}"
    record="$data/desktop/machine-setup.json"
    image="$RUNNER_TEMP/jason-vanishing-place.dmg"
    mount="$RUNNER_TEMP/jason-vanishing-place"
    stage="$RUNNER_TEMP/jason-vanishing-stage"
    [ ! -e "$image" ] && [ ! -e "$mount" ] && [ ! -e "$stage" ] || fail "$image, $mount or $stage is there before the leg makes it"
    # From here to the detach, a failure leaves nothing behind: the staged copy and the image removed, the application
    # from the image ended, the image detached.
    trap 'pkill -f "$mount/Jason.app/Contents/MacOS/" 2>/dev/null || true; hdiutil detach -force "$mount" > /dev/null 2>&1 || true; rm -f "$image"; rm -rf "$stage"' EXIT
    # The volume's root holds Jason.app, as a download's image does: the application staged in a folder of its own.
    mkdir -p "$stage"
    ditto "$built" "$stage/Jason.app" || fail "this build's application could not be staged at $stage"
    within 300 'hdiutil create' hdiutil create -srcfolder "$stage" -volname 'Jason Test' -format UDZO "$image" > "$OUT/vanishing-create.txt" 2>&1 || fail "hdiutil create failed: $(tail -n 3 "$OUT/vanishing-create.txt")"
    rm -rf "$stage"
    within 120 'hdiutil attach' hdiutil attach -nobrowse -readonly -mountpoint "$mount" "$image" > "$OUT/vanishing-attach.txt" 2>&1 || fail "hdiutil attach failed: $(tail -n 3 "$OUT/vanishing-attach.txt")"
    mounted="$mount/Jason.app"
    [ -d "$mounted" ] || fail "no Jason.app at the root of the mounted image at $mount"
    say "this build's application put in a read-only disk image (UDZO), at its root, and mounted at $mount"

    # jason install from the application on the image, as its first start runs it.
    status=0
    within 120 'jason install from the disk image' env SHELL=/bin/zsh "$mounted/Contents/Resources/bundle/jason" install > "$OUT/vanishing-install.json" 2> "$OUT/vanishing-install.err" || status=$?
    cat "$OUT/vanishing-install.json" "$OUT/vanishing-install.err"
    [ "$status" -ne 0 ] || fail "jason install from the disk image ended 0"
    python3 -c 'import json,sys
code, message = "", ""
for path in sys.argv[1:]:
    for line in open(path, encoding="utf-8").read().splitlines():
        line = line.strip()
        if line.startswith("{"):
            try:
                doc = json.loads(line)
            except ValueError:
                continue
            error = doc.get("error") if isinstance(doc, dict) else None
            if isinstance(error, dict) and error.get("code"):
                code, message = error["code"], str(error.get("message") or "")
print(code)
print(message)' "$OUT/vanishing-install.json" "$OUT/vanishing-install.err" > "$OUT/vanishing-refusal.txt" || fail "the refusal could not be read"
    code="$(sed -n 1p "$OUT/vanishing-refusal.txt")"
    [ "$code" = install_vanishing_place ] || fail "jason install from the disk image exited $status with the code '$code', not install_vanishing_place"
    # The branch of the refusal a mounted image takes, by its words, rather than a translocated copy's.
    grep -qF 'a read-only volume' "$OUT/vanishing-refusal.txt" || fail "the refusal does not say the place is a read-only volume: $(sed -n 2p "$OUT/vanishing-refusal.txt")"
    [ -z "$(app)" ] || fail "the refused jason install placed an application at $(app)"
    [ ! -e "$WRAPPER" ] || fail "the refused jason install placed a terminal command at $WRAPPER"
    say "jason install from the application on the disk image exited $status, refused with install_vanishing_place as on a read-only volume, and placed nothing"

    # Opened from the image, as a person opens a download: its start runs the setup, which is refused; then quit. The
    # setup's run is read in the desktop's log, by its line counted before the open and after; the setup record, which a
    # refused setup does not write, is held to be as it was.
    ran_lines() {
        python3 -c 'import glob,json,os,sys
count = 0
for path in sorted(glob.glob(os.path.join(sys.argv[1], "logs", "desktop-*.jsonl"))):
    for line in open(path, encoding="utf-8", errors="replace").read().splitlines():
        try:
            event = json.loads(line)
        except ValueError:
            continue
        if isinstance(event, dict) and event.get("EventName") == "machine_setup_ran" and event.get("Why") == "start" and event.get("Exit") not in (0, None) and event.get("Completed") is False:
            count += 1
print(count)' "$data"
    }
    ran_before="$(ran_lines)" || fail "the desktop's log could not be read before the open"
    if [ -e "$record" ]; then record_before="$(shasum -a 256 < "$record") $(stat -f %m "$record")"; else record_before=absent; fi
    env PATH="$(plain_path)" "$mounted/Contents/MacOS/Jason" > "$OUT/vanishing-window.log" 2>&1 &
    window=$!
    until=$(( $(date +%s) + 120 ))
    ran_after="$ran_before"
    while [ "$ran_after" -le "$ran_before" ] && [ "$(date +%s)" -lt "$until" ]; do
        kill -0 "$window" 2>/dev/null || fail "Jason Desktop opened from the disk image did not stay running: $(tail -n 20 "$OUT/vanishing-window.log")"
        sleep 2
        ran_after="$(ran_lines)" || fail "the desktop's log could not be read after the open"
    done
    [ "$ran_after" -gt "$ran_before" ] || fail "Jason Desktop opened from the disk image wrote no refused setup run (machine_setup_ran at start, exit not 0, not completed) to its log within 120 s"
    kill -0 "$window" 2>/dev/null || fail "Jason Desktop opened from the disk image did not stay running: $(tail -n 20 "$OUT/vanishing-window.log")"
    end_process "$window" 'Jason Desktop opened from the disk image'
    pkill -f "$mount/Jason.app/Contents/MacOS/" 2>/dev/null || true
    sleep 3
    if [ -e "$record" ]; then record_after="$(shasum -a 256 < "$record") $(stat -f %m "$record")"; else record_after=absent; fi
    [ "$record_after" = "$record_before" ] || fail "opening the application from its disk image wrote the setup record at $record: $(cat "$record" 2>/dev/null || true)"
    [ -z "$(app)" ] || fail "opening the application from its disk image placed an application at $(app)"
    [ ! -e "$WRAPPER" ] || fail "opening the application from its disk image placed a terminal command at $WRAPPER: $(cat "$WRAPPER")"
    say "Jason Desktop opened from the disk image, its start ran the setup and was refused ($((ran_after - ran_before)) line(s) in its log), and it quit having set nothing up: no application placed, no terminal command, the setup record $(if [ "$record_before" = absent ]; then echo 'not written'; else echo 'as it was'; fi)"

    # Ejected, as a person ejects a download's image, and gone.
    within 120 'hdiutil detach' hdiutil detach "$mount" > "$OUT/vanishing-detach.txt" 2>&1 || fail "the disk image could not be ejected: $(tail -n 3 "$OUT/vanishing-detach.txt")"
    [ ! -e "$mounted" ] || fail "after the eject the application is still at $mounted"
    if [ -d "$mount" ]; then [ -z "$(ls -A "$mount")" ] || fail "after the eject $mount still holds: $(ls -A "$mount" | tr '\n' ' ')"; fi
    rm -f "$image"
    trap - EXIT
    found="$(within 60 'a new login shell looking for jason' fresh 'command -v jason || echo none')" || true
    [ "$found" = none ] || fail "after the disk image was ejected a new login shell finds jason at '$found'"
    say "the disk image ejected and its mount gone, and a new zsh login shell finds no jason"
    ;;

previous-removal)
    # The previous release's own removal of its logon registration, said in launchd's records: its command-line Jason
    # installed by its own script, its logon registration, and its own jason uninstall. Facts for the release before this
    # one, whose code no longer changes: the records before, after the registration and after the removal are said, and
    # the listing is kept, not compared.
    previous="$1"
    say "launchd's records before the previous release's own removal leg: $(launchd_records | tr '\t\n' ' ;' || true)"
    within 300 "the previous release's install.sh" env PATH="$(plain_path)" SHELL=/bin/zsh JASON_INSTALL_FEED="$previous" sh "$previous/install.sh" > "$OUT/install-previous-again.txt" || fail "the previous release's install.sh failed"
    old="$(within 60 "the previous release's jason --version" "$WRAPPER" --version | cut -d+ -f1)" || fail "the previous release's jason --version failed"
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

    env PATH="$(plain_path)" "$placed/Contents/MacOS/Jason" > "$OUT/intel-window.log" 2>&1 &
    window=$!
    sleep 10
    kill -0 "$window" 2>/dev/null || fail "Jason Desktop did not stay running on the Intel Mac: $(tail -n 20 "$OUT/intel-window.log")"
    end_process "$window" 'Jason Desktop on the Intel Mac'
    say "Jason Desktop ran on the Intel Mac until it was told to quit"

    within 300 'jason uninstall on the Intel Mac' "$carried" uninstall --purge-data --yes > "$OUT/intel-uninstall.json" 2>&1 || fail "jason uninstall on the Intel Mac failed: $(cat "$OUT/intel-uninstall.json")"
    wait_until 30 "$placed going" test ! -e "$placed" || fail "$placed is still there after the uninstall"
    say "jason uninstall took the x64 application and everything it placed off the Intel Mac"
    ;;

by-hand)
    # This build's application put in Applications by hand on a clean machine, as a person drags it there, and opened as a
    # person opens it, with no script before it: its first start sets the machine up. Held: the set-up's routine ran at the
    # start, exited 0 and completed; a new login shell finds jason at ~/.local/bin/jason, the application's wrapper, and it
    # says this build, the one jason by that name; the profile line is there; the skills of the hosts found are recorded,
    # the person's own quick-start left alone and named by jason status; and the set-up's report, in the record the start
    # writes, says a new terminal finds this jason. The uninstall from a new terminal and the compare are the leg's next
    # steps.
    built="$1"; version="$2"
    [ -d "$built" ] || fail "no application at $built"
    [ -z "$(app)" ] || fail "an application is in place before the leg: $(app)"
    [ ! -e "$WRAPPER" ] || fail "a terminal command is in place before the leg: $WRAPPER"
    place=/Applications
    [ -w "$place" ] || { place="$HOME/Applications"; mkdir -p "$place"; }
    placed="$place/Jason.app"
    within 300 "copying this build's application into $place" ditto "$built" "$placed" || fail "this build's application could not be copied to $placed"
    within 300 "codesign verifying this build's application" codesign --verify --deep --strict "$placed" || fail "this build's application at $placed does not verify"
    [ "$(version_of "$placed")" = "$version" ] || fail "$placed is $(version_of "$placed"), not $version"
    carried="$placed/Contents/Resources/bundle/jason"
    record="$DATA/desktop/machine-setup.json"
    say "this build's application $version put at $placed by hand, its signature verifying, with no script run before it"

    # Opened as a person opens it: its first start's set-up read in its log, looked for for 180 s; the desktop's log kept
    # from here on.
    ran_before="$(start_setup_runs)" || fail "the desktop's log could not be read before the start"
    finished_before="$(start_setup_finishes)" || fail "the desktop's log could not be read before the start"
    desktop_log=by-hand-desktop-log.jsonl
    trap 'keep_desktop_log "$desktop_log"' EXIT
    say_open_environment
    read_terminal_before
    window="$(open_app "$placed" "$OUT/by-hand-window.log")" || fail "this build's application could not be opened from $placed: $(cat "$OUT/open-app-failure.txt" 2>/dev/null)"
    say "this build's application opened by open from $placed, its process $window running from that bundle, with SHELL '$(app_shell_of "$window")'"
    until=$(( $(date +%s) + 180 ))
    ran_after="$ran_before"
    while [ "${ran_after%% *}" -le "${ran_before%% *}" ] && [ "$(date +%s)" -lt "$until" ]; do
        kill -0 "$window" 2>/dev/null || fail "this build's application did not stay running: $(tail -n 20 "$OUT/by-hand-window.log")"
        sleep 2
        ran_after="$(start_setup_runs)" || fail "the desktop's log could not be read after the start"
    done
    keep_desktop_log "$desktop_log"
    [ "${ran_after%% *}" -gt "${ran_before%% *}" ] || fail "this build's application wrote no machine_setup_ran line at its first start within 180 s of it"
    set -- $ran_after
    ran_exit="$2"; ran_completed="$3"
    say "this build's first start ran the set-up: jason install exited $ran_exit, completed $ran_completed"
    [ "$ran_exit" = 0 ] || fail "the set-up at this build's first start exited $ran_exit"
    [ "$ran_completed" = true ] || fail "the set-up at this build's first start did not complete"
    # The finishing step after the routine, waited for before any jason here runs, since a jason that writes under the data
    # directory first would make it find an installation and leave the record unwritten: on a clean machine it completes.
    until=$(( $(date +%s) + 120 ))
    finished_after="$finished_before"
    while [ "${finished_after%% *}" -le "${finished_before%% *}" ] && [ "$(date +%s)" -lt "$until" ]; do
        kill -0 "$window" 2>/dev/null || fail "this build's application did not stay running: $(tail -n 20 "$OUT/by-hand-window.log")"
        sleep 2
        finished_after="$(start_setup_finishes)" || fail "the desktop's log could not be read after the start"
    done
    keep_desktop_log "$desktop_log"
    [ "${finished_after%% *}" -gt "${finished_before%% *}" ] || fail "this build's application wrote no machine_setup_finished line at its first start within 120 s of the set-up"
    say "the finishing step after the set-up ran: completed ${finished_after#* }"
    [ "${finished_after#* }" = true ] || fail "the finishing step after the set-up at this build's first start did not complete"

    # The set-up's report, as the record the first start writes keeps it: a new terminal finds this jason.
    [ -f "$record" ] || fail "this build's first start wrote no set-up record at $record, so its report cannot be read"
    python3 -c 'import json,sys
command = (json.load(open(sys.argv[1], encoding="utf-8")).get("report") or {}).get("command") or {}
print(command.get("finds") or "")
print(str(command.get("finds_this")).lower())' "$record" > "$OUT/by-hand-report.txt" || fail "the set-up record at $record could not be read: $(cat "$record")"
    finds="$(sed -n 1p "$OUT/by-hand-report.txt")"
    [ "$(sed -n 2p "$OUT/by-hand-report.txt")" = true ] || fail "the set-up's report does not say a new terminal finds this jason: finds '$finds', finds_this $(sed -n 2p "$OUT/by-hand-report.txt")"
    { [ "$finds" = "$WRAPPER" ] || [ "$finds" = "$carried" ]; } || fail "the set-up's report says a new terminal finds '$finds', neither $WRAPPER nor $carried"
    say "the set-up's report, in the record at $record, says a new terminal finds $finds, this jason"

    # The terminal command: the application's wrapper; the account's own new terminal finds it (hold_account_terminal).
    [ -f "$WRAPPER" ] || fail "no wrapper at $WRAPPER after this build's first start"
    if file "$WRAPPER" | grep -q 'Mach-O'; then fail "$WRAPPER is an executable, not the application's wrapper"; fi
    grep -q "$carried" "$WRAPPER" || fail "the wrapper at $WRAPPER does not run $carried"
    report_profiles "$record" "$OUT/by-hand-report-profiles.txt" || fail "the profiles in the set-up record at $record could not be read"
    hold_account_terminal "$version" "$OUT/by-hand-report-profiles.txt" "$record"
    say "~/.local/bin/jason is the application's wrapper running $carried"

    # The skills of the hosts found, recorded; the person's own quick-start left alone and named by jason status.
    for root in .claude/skills .agents/skills; do [ -f "$HOME/$root/.jason-skills.json" ] || fail "no skills record in ~/$root after this build's first start"; done
    within 120 'jason status' "$carried" status > "$OUT/by-hand-status.json" 2>/dev/null || true
    if grep -q "person's own" "$OUT/failed-legs.txt" 2>/dev/null; then
        say "the skills of Claude Code and Codex are recorded in both folders; the person's own quick-start was lost in a leg before, as recorded, so this start's handling of it is not seen"
    elif [ -f "$OWN_SKILL/SKILL.md" ]; then
        [ "$(cat "$OWN_SKILL/SKILL.md")" = "a person's own skill" ] || fail "the person's own skill at $OWN_SKILL was changed by this build's first start"
        if grep -q '"quick-start/' "$HOME/.agents/skills/.jason-skills.json"; then fail "the record in ~/.agents/skills claims the person's own quick-start"; fi
        names_own_skill "$OUT/by-hand-status.json" "$OWN_SKILL" || fail "jason status does not name the person's own quick-start, $OWN_SKILL, as a folder Jason did not deploy"
        say "the skills of Claude Code and Codex are recorded in both folders, and the person's own quick-start is left alone, named by jason status"
    else
        say "the skills of Claude Code and Codex are recorded in both folders; the person's own quick-start is gone already, so this start's handling of it is not seen"
    fi

    say "jason status after this build's first start, whole:"
    cat "$OUT/by-hand-status.json"
    end_process "$window" "this build's application"
    pkill -f "$placed/Contents/MacOS/" 2>/dev/null || true
    sleep 3
    ;;

ran-from-image-end)
    # The disk image leg's own end, on every way out of the leg once it ran: the two things the application that ran from
    # the image leaves, which no product door can take since nothing was installed, removed by name - its preferences
    # through the preferences system first (which would otherwise write back a copy it holds), then the file where it is a
    # file of its own and not a link; and each folder this build's jason unpacked, by this build's id alone. Each only where
    # this leg's begin did not find it, so nothing an earlier leg left is taken here. Logged in $OUT/leg-ends.txt, never a
    # proof; the next leg's begin holds the machine it hands on to the machine before. Once per leg.
    done_mark="$OUT/ran-from-image-ended"
    [ ! -e "$done_mark" ] || exit 0
    : > "$done_mark"
    started="$OUT/leg-start.txt"
    [ -f "$started" ] || fail "no listing of this leg's begin at $started, so what this leg left cannot be told from what it found"
    log="$OUT/leg-ends.txt"
    pkill -f '/Jason\.app/Contents/MacOS/' 2>/dev/null || true
    preferences="$HOME/Library/Preferences/ai.jasonharness.desktop.plist"
    if grep -qxF "$(printf 'system-kept\tLibrary/Preferences/ai.jasonharness.desktop.plist')" "$started"; then
        line="  $preferences: there at this leg's begin, left"
    elif [ -L "$preferences" ]; then
        line="  $preferences: a link, left"
    elif [ -f "$preferences" ]; then
        status=0
        ( within 10 'defaults delete ai.jasonharness.desktop' defaults delete ai.jasonharness.desktop > "$OUT/ran-from-image-defaults.txt" 2>&1 ) || status=$?
        if [ -f "$preferences" ] && [ ! -L "$preferences" ]; then rm -f "$preferences" || true; fi
        if [ -e "$preferences" ]; then line="  $preferences: defaults delete ended $status, and the file could not be removed"; else line="  $preferences: removed (defaults delete ended $status)"; fi
    else
        line="  $preferences: not there"
    fi
    printf '%s\n' "the leg 'Open this build from its disk image, then eject it', at its end:" "$line" >> "$log"
    echo "$line"
    if [ -f "$OUT/this-build-extracted.txt" ]; then
        while IFS= read -r id; do
            [ -n "$id" ] || continue
            folder="$HOME/.net/jason/$id"
            case "$id" in *[!A-Za-z0-9]*) line="  $folder: not an id of the shape a build's is, left" ;; *)
                if grep -qxF "$(printf 'extracted\t%s' "$id")" "$started"; then line="  $folder: there at this leg's begin, left"
                elif [ -L "$folder" ]; then line="  $folder: a link, left"
                elif [ -d "$folder" ]; then
                    if rm -rf "$folder"; then line="  $folder: removed"; else line="  $folder: could not be removed"; fi
                else line="  $folder: not there"; fi ;;
            esac
            printf '%s\n' "$line" >> "$log"
            echo "$line"
        done < "$OUT/this-build-extracted.txt"
    fi
    say "the disk image leg's own end removed by name what the application that ran from the image left, where this leg's begin did not find it (in leg-ends.txt; not a proof)"
    ;;

*)
    echo "usage: test-installation-macos.sh listing|compare|held|install|refuse-while-open|uninstall-terminal|uninstall-menu|from-previous|over-previous-desktop|over-running-cli|bundle-swap|by-hand|vanishing-place|ran-from-image-end|assess|quarantine|intel ..." >&2
    exit 2
    ;;
esac
