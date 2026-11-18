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
#                                  (with previous-profiles: also the empty profiles and the enabled launchd record the
#                                  previous release's own install and registration leave, each named and said)
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
    [ -z "$expected_x" ] || say "expected in $1: $(printf '%s' "$expected_x" | tr '\t\n' ' ;') (another release's own extraction folder: the previous release's jason ran in this job, and taking it is that release's removal's, never this build's)"
    if [ -n "$soft_x" ]; then
        say "seen, and expected until the removal takes them (soft: EXTRACTED_SOFT is set), in $1: $(printf '%s' "$soft_x" | tr '\t\n' ' ;')"
        printf '%s: %s\n' "$1" "$(printf '%s' "$soft_x" | tr '\t\n' ' ;')" >> "$OUT/soft-seen.txt"
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
        [ "$outcome" = success ] && continue
        if [ "$outcome" = failure ]; then skip_leg "not run: the leg it needs failed, $needed"; else skip_leg "not run: the leg it needs did not run ($outcome), $needed"; fi
        exit 0
    done
    if [ -n "$clean" ]; then
        [ -f "$OUT/before.txt" ] || { skip_leg 'not run: the leg it needs failed, The machine before anything is installed'; exit 0; }
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
    fi
    # This leg now changes the machine, and is the one a later leg's start holds to what it leaves.
    printf '%s\n' "$LEG" > "$OUT/last-leg.txt"
    ;;

end)
    printf '%s\n' "$1" >> "$OUT/legs-ended.txt"
    rm -f "$OUT/leg-open.txt"
    say "$1 - ended"
    ;;

held)
    close_leg
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
    grep -qx '# added by jason install' "$HOME/.zprofile" || fail "~/.zprofile carries no line for ~/.local/bin"
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
    python3 -m http.server 8099 --bind 127.0.0.1 --directory "$feed" > "$OUT/feed-server.log" 2>&1 &
    server=$!
    trap 'kill "$server" 2>/dev/null || true' EXIT
    wait_until 10 'the feed on 127.0.0.1:8099 answering' curl -fsS --max-time 5 -o /dev/null http://127.0.0.1:8099/manifest.json
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
    # share: a GitHub-hosted runner's alone, never a person's Mac.
    if [ "${GITHUB_ACTIONS:-}" != true ] || [ "${RUNNER_ENVIRONMENT:-}" != github-hosted ]; then fail "way-back runs on the account's own home and data directory, and on a GitHub-hosted runner alone."; fi
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

    python3 -m http.server 8099 --bind 127.0.0.1 --directory "$feed" > "$OUT/way-back-feed-server.log" 2>&1 &
    server=$!
    trap 'kill "$server" 2>/dev/null || true' EXIT
    wait_until 10 'the feed on 127.0.0.1:8099 answering' curl -fsS --max-time 5 -o /dev/null http://127.0.0.1:8099/manifest.json
    status=0
    within 900 "the previous release's jason update apply --version $version" "$WRAPPER" update apply --version "$version" --feed http://127.0.0.1:8099/manifest.json --drain-seconds 0 > "$OUT/way-back-update.json" 2> "$OUT/way-back-update.err" || status=$?
    kill "$server" 2>/dev/null || true
    [ "$status" -eq 0 ] || fail "the previous release's jason update apply --version $version exited with $status: $(cat "$OUT/way-back-update.err")"
    [ "$(read_field "$OUT/way-back-update.json" move.state)" = done ] || fail "the update did not end with its move done: $(cat "$OUT/way-back-update.json")"
    placed="$(app)"
    [ -n "$placed" ] || fail "no Jason.app after the move"
    carried="$placed/Contents/Resources/bundle/jason"
    # The wrapper names no data directory: a runtime starts every child with JASON_DATA_DIR set, the move's routine
    # among them, and the default one is not written, so the wrapper is the one the install script writes.
    ! grep -q 'JASON_DATA_DIR' "$WRAPPER" || fail "the wrapper at $WRAPPER names a data directory after a move with none named: $(cat "$WRAPPER")"
    say "moved into Jason Desktop $version by the previous release's own update; the wrapper names no data directory"

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
    grep -qF 'quick-start' "$OUT/way-back-status-before.json" && grep -qE 'Jason did not deploy, under (a name Jason ships, left as it is|names Jason ships, left as they are)' "$OUT/way-back-status-before.json" \
        || fail "before step 1, jason status under the moved 0.49 does not name the person's own quick-start as a folder Jason did not deploy, under a name Jason ships, left as it is: $(cat "$OUT/way-back-status-before.json")"
    say "before step 1, jason status under the moved 0.49 names the person's own quick-start as a folder Jason did not deploy, under a name Jason ships, left as it is"
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
    held="$(python3 - "$OUT/way-back-campaigns-before.json" "$OUT/way-back-campaigns-after.json" "$OUT/way-back-journal-before.json" "$OUT/way-back-journal-after.json" "$old" <<'PY'
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
)" || fail "the campaigns and the journal could not be held: see this step's log"
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
        python3 -m http.server 8099 --bind 127.0.0.1 --directory "$feed" > "$OUT/way-back-again-feed-server.log" 2>&1 &
        server=$!
        wait_until 10 'the feed on 127.0.0.1:8099 answering' curl -fsS --max-time 5 -o /dev/null http://127.0.0.1:8099/manifest.json
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
