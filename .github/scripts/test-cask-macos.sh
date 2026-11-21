#!/bin/bash
# One step of the macOS proof of Homebrew's cask, run by the release's proof-macos-cask job on the signed and notarized
# zip the macOS job uploaded. The cask is rendered from .github/cask/jason.rb.template, the one copy this repository
# keeps, into taps made inside the job: never the real tap.
#
#   test-cask-macos.sh <step> [arguments]
#
#   taps <feed> <version>          the tap jasonproof/audit, holding the cask as the real tap renders it, and the tap
#                                  jasonproof/test, holding it with its url naming the feed's zip on this machine; each
#                                  test tap is trusted by brew trust right after brew tap-new makes it, a brew with no
#                                  brew trust fails the step, and brew info must resolve the cask in both
#   audit                          brew style on the cask's file, one file inspected, and brew audit --strict on the cask
#                                  as the real tap renders it; a word of Homebrew's that a tap is not trusted or ignored
#                                  fails it
#   render <zip> <version>         jasonproof/test's cask rendered again, at <version>, for <zip>
#   install <version>              brew install --cask: the application in /Applications names Jason Desktop's identifier
#                                  at <version>, the link in Homebrew's bin leads into it and answers --version before any
#                                  open, Homebrew's record of the cask printed, the quarantine attribute recorded
#   open <version>                 the application opened as a person opens it, its first start's set-up waited for, its
#                                  record written anew, and the application quit again (test-installation-macos.sh
#                                  by-hand --placed holds that start in full, on a machine with no wrapper yet)
#   left-to-homebrew               the carried jason uninstall --dry-run says application_fate left_to_homebrew
#   start-runtime                  a runtime started from the carried jason, answering
#   upgrade <version>              brew upgrade --cask: the application at <version>, the runtime the uninstall stanza
#                                  stopped answering at <version> once a start is paid, or the note that one is owed said,
#                                  the link resolving, brew outdated --greedy empty, and a second upgrade doing nothing
#   uninstall                      brew uninstall --cask jason: the application, the link and Homebrew's record gone, and
#                                  no Jason process left
#   uninstall-left                 the listing after-cask-uninstall against before: nothing differs outside what a plain
#                                  uninstall may leave (what Jason set up, the state under ~/Library, data, harness root)
#   carried-uninstall              the carried jason uninstall: exit 0, the application, its link and its state under
#                                  ~/Library kept, and brew uninstall --zap --cask jason named
#   zap                            brew uninstall --zap --cask jason, with no --force: the application, the link,
#                                  Homebrew's record, the wrapper and both launch agents' files gone; the profile lines and
#                                  the skills recorded, not held
#   formula <tarball> <version>    jasonproof/test's formula, installing the command line of <tarball>, the older formula's
#                                  release, installed and linked
#   beside-formula                 the cask installed beside the linked formula: Homebrew's warning that it skipped the link
#   formula-named                  set-up's report and jason status name brew uninstall --formula jason
#   formula-off                    jason runtime stop and brew uninstall --formula jason; the cask's link still missing,
#                                  said, which is not a fault
#   reset                          whatever a failed step left of the cask or the formula taken off by Homebrew's own
#                                  uninstall, said
set -euo pipefail

OUT="${OUT:-$RUNNER_TEMP/installation}"
mkdir -p "$OUT"
DATA="$HOME/.jason"
export HOMEBREW_NO_AUTO_UPDATE=1 HOMEBREW_NO_ENV_HINTS=1 HOMEBREW_NO_ANALYTICS=1
TOKEN=jason
TEST_TAP=jasonproof/test
AUDIT_TAP=jasonproof/audit
IDENTIFIER=ai.jasonharness.desktop
APP=/Applications/Jason.app
CARRIED="$APP/Contents/Resources/bundle/jason"
ZIP_NAME=jason-desktop-macos-arm64.zip
step="${1:-}"; shift || true
case "$step" in
    taps|audit|render) ;;
    *) if [ "${GITHUB_ACTIONS:-}" != true ] || [ "${RUNNER_ENVIRONMENT:-}" != github-hosted ]; then echo "::error title=Cask::the step '$step' changes the machine it runs on, and runs on a GitHub-hosted runner alone"; exit 1; fi ;;
esac

fail() { echo "::error title=Cask::$1"; exit 1; }
say() { echo "$1"; echo "- $1" >> "${GITHUB_STEP_SUMMARY:-/dev/null}"; }
prefix() { brew --prefix; }
link() { echo "$(prefix)/bin/jason"; }
caskroom() { echo "$(prefix)/Caskroom/$TOKEN"; }
version_of() { sed -n 's/.*"version":[[:space:]]*"\([^"]*\)".*/\1/p' "$1/Contents/Resources/bundle/version.json" 2>/dev/null | head -n 1; }
identifier_of() { /usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$1/Contents/Info.plist" 2>/dev/null || true; }
sha_of() { shasum -a 256 "$1" | cut -d' ' -f1; }
# One field of a JSON object, or nothing.
field() { python3 -c 'import json,sys
try: print(json.load(open(sys.argv[1])).get(sys.argv[2], ""))
except Exception: print("")' "$1" "$2"; }

# The cask from the template, at a version, for a zip, with the url as the real tap renders it or naming a file here.
render() {
    local version="$1" sha="$2" url="${3:-}" target="$4"
    mkdir -p "$(dirname "$target")"
    sed -e "s/{{VERSION}}/$version/" -e "s/{{SHA256_MACOS_ARM64_ZIP}}/$sha/" .github/cask/jason.rb.template > "$target"
    if [ -n "$url" ]; then
        awk -v url="$url" '/^  url "https:\/\/get\.jasonharness\.ai\/releases\/download\// { print "  url \"" url "\""; next } { print }' "$target" > "$target.new"
        mv "$target.new" "$target"
        [ "$(grep -c "^  url \"$url\"\$" "$target")" = 1 ] || fail "the cask's url line was not replaced in $target"
    fi
    if grep -q '{{' "$target"; then fail "$target still holds a placeholder: $(grep '{{' "$target")"; fi
}

tap_dir() { brew --repository "$1"; }

# A brew command run once and, where Homebrew refuses it until the tap is trusted, run again after `brew trust` of the one
# cask or formula it names: what Homebrew said and whether it asked for trust are kept and said, the answer to whether a
# tap made by brew tap-new is trusted by default.
trusted() {
    local log="$1" kind="$2" ref="$3"; shift 3
    local status=0
    "$@" > "$log" 2>&1 || status=$?
    cat "$log"
    if [ "$status" != 0 ] && grep -qi 'trust' "$log"; then
        say "Homebrew refused '$*' until it is trusted: $(grep -i 'trust' "$log" | head -n 1)"
        brew trust "$kind" "$ref" > "$log.trust" 2>&1 || { cat "$log.trust"; fail "brew trust $kind $ref failed: $(tr '\n' ' ' < "$log.trust")"; }
        cat "$log.trust"
        status=0
        "$@" > "$log" 2>&1 || status=$?
        cat "$log"
    elif grep -qi 'trust' "$log"; then
        say "Homebrew said of trust, running '$*': $(grep -i 'trust' "$log" | head -n 1)"
    fi
    return "$status"
}

# Homebrew's word that a tap is not trusted, or that it ignores what a tap holds, in a command's output fails the step: a
# command that read nothing of the cask must not pass for one that read it.
ignored() {
    if grep -qiE 'not trusted|ignoring (formulae|casks)|tap trust is required' "$1"; then
        fail "$2 says a tap is not trusted or ignored: $(grep -iE 'not trusted|ignoring (formulae|casks)|tap trust is required' "$1" | head -n 2 | tr '\n' ' ')"
    fi
}

case "$step" in
taps)
    feed="$1"; version="$2"
    zip="$feed/$ZIP_NAME"
    [ -f "$zip" ] || fail "no $ZIP_NAME in $feed"
    say "$(brew --version | head -n 1)"
    for tap in "$AUDIT_TAP" "$TEST_TAP"; do
        brew tap-new --no-git "$tap" > /dev/null
        # A tap brew tap-new makes is not trusted: Homebrew ignores its casks and formulae until it is (rc.3's dry run said
        # so, and brew style then read the cask as an untrusted tap's). Each test tap is trusted whole, before style, audit
        # and install, and what Homebrew answers is printed.
        brew help trust > /dev/null 2>&1 || fail "this runner's brew has no brew trust, so the test tap $tap cannot be trusted: $(brew --version | head -n 1)"
        status=0
        brew trust "$tap" > "$OUT/cask-trust-${tap//\//-}.txt" 2>&1 || status=$?
        cat "$OUT/cask-trust-${tap//\//-}.txt"
        [ "$status" = 0 ] || fail "brew trust $tap exited $status: $(tr '\n' ' ' < "$OUT/cask-trust-${tap//\//-}.txt")"
        say "brew trust $tap: $(tr '\n' ' ' < "$OUT/cask-trust-${tap//\//-}.txt" | cut -c1-200)"
    done
    render "$version" "$(sha_of "$zip")" "" "$(tap_dir "$AUDIT_TAP")/Casks/$TOKEN.rb"
    render "$version" "$(sha_of "$zip")" "file://$zip" "$(tap_dir "$TEST_TAP")/Casks/$TOKEN.rb"
    for tap in "$AUDIT_TAP" "$TEST_TAP"; do
        status=0
        brew info --cask "$tap/$TOKEN" > "$OUT/cask-info-${tap//\//-}.txt" 2>&1 || status=$?
        cat "$OUT/cask-info-${tap//\//-}.txt"
        [ "$status" = 0 ] || fail "brew info --cask $tap/$TOKEN does not resolve the cask (exit $status): $(tr '\n' ' ' < "$OUT/cask-info-${tap//\//-}.txt" | cut -c1-300)"
        ignored "$OUT/cask-info-${tap//\//-}.txt" "brew info --cask $tap/$TOKEN"
    done
    say "the taps $AUDIT_TAP and $TEST_TAP made in this job, the cask rendered from .github/cask/jason.rb.template at $version and resolved by brew info in both; never the real tap"
    cat "$(tap_dir "$AUDIT_TAP")/Casks/$TOKEN.rb"
    ;;

audit)
    brew style "$(tap_dir "$AUDIT_TAP")/Casks/$TOKEN.rb" 2>&1 | tee "$OUT/cask-style.txt" || fail "brew style refuses the cask: $(tr '\n' ' ' < "$OUT/cask-style.txt")"
    ignored "$OUT/cask-style.txt" "brew style"
    grep -qE '(^|[^0-9])1 file inspected' "$OUT/cask-style.txt" || fail "brew style did not inspect the cask's one file: $(grep -E 'inspected' "$OUT/cask-style.txt" || echo 'it said nothing of what it inspected')"
    trusted "$OUT/cask-audit.txt" --cask "$AUDIT_TAP/$TOKEN" brew audit --strict --cask "$AUDIT_TAP/$TOKEN" || fail "brew audit --strict refuses the cask: $(tr '\n' ' ' < "$OUT/cask-audit.txt")"
    ignored "$OUT/cask-audit.txt" "brew audit --strict"
    trust="$(grep -iE 'trust' "$OUT/cask-audit.txt" "$OUT/cask-style.txt" || true)"
    say "brew style and brew audit --strict accept the cask as the real tap renders it; what they say of trust: ${trust:-nothing}"
    ;;

render)
    zip="$1"; version="$2"
    [ -f "$zip" ] || fail "no zip at $zip"
    render "$version" "$(sha_of "$zip")" "file://$zip" "$(tap_dir "$TEST_TAP")/Casks/$TOKEN.rb"
    say "$TEST_TAP's cask rendered at $version for $zip"
    ;;

install)
    version="$1"
    [ ! -e "$APP" ] || fail "an application is in place before the cask's install: $APP"
    status=0
    trusted "$OUT/cask-install.txt" --cask "$TEST_TAP/$TOKEN" brew install --cask "$TEST_TAP/$TOKEN" || status=$?
    [ "$status" = 0 ] || fail "brew install --cask exited $status: $(tail -n 5 "$OUT/cask-install.txt" | tr '\n' ' ')"
    ignored "$OUT/cask-install.txt" "brew install --cask"
    trust="$(grep -iE 'trust' "$OUT/cask-install.txt" || true)"
    say "brew install --cask $TEST_TAP/$TOKEN exited 0; what it says of trust: ${trust:-nothing}"
    [ -d "$APP" ] || fail "no application at $APP after the cask's install"
    [ "$(identifier_of "$APP")" = "$IDENTIFIER" ] || fail "$APP names $(identifier_of "$APP"), not $IDENTIFIER"
    [ "$(version_of "$APP")" = "$version" ] || fail "$APP is $(version_of "$APP"), not $version"
    [ -L "$(link)" ] || fail "no link at $(link) after the cask's install"
    resolved="$(python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$(link)")"
    [ "$resolved" = "$(python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$CARRIED")" ] || fail "$(link) leads to $resolved, not into $APP"
    answered="$("$(link)" --version 2>&1 || true)"
    case "$answered" in *"$version"*) ;; *) fail "$(link) --version answered '$answered', not $version" ;; esac
    [ ! -e "$HOME/.local/bin/jason" ] || fail "a wrapper is in place before any open: $HOME/.local/bin/jason"
    say "$APP names $IDENTIFIER at $version; $(link) leads into it and answers '$answered' before any open"
    for file in INSTALL_RECEIPT.json config.json; do
        echo "Homebrew's record, $(caskroom)/.metadata/$file:"
        cat "$(caskroom)/.metadata/$file" 2>/dev/null || echo "  (none)"
        echo
    done
    grep -q '"homebrew_version"' "$(caskroom)/.metadata/INSTALL_RECEIPT.json" || fail "the cask's receipt names no homebrew_version, which is what the product reads"
    for place in "$APP" "$CARRIED"; do
        say "the quarantine attribute on $place: $(xattr -p com.apple.quarantine "$place" 2>/dev/null || echo none)"
    done
    ;;

open)
    version="$1"
    record="$DATA/desktop/machine-setup.json"
    [ "$(version_of "$APP")" = "$version" ] || fail "$APP is $(version_of "$APP"), not $version"
    before="$(stat -f %m "$record" 2>/dev/null || echo 0)"
    sleep 1
    open "$APP" || fail "open $APP failed"
    until=$(( $(date +%s) + 180 ))
    while [ "$(stat -f %m "$record" 2>/dev/null || echo 0)" -le "$before" ] && [ "$(date +%s)" -lt "$until" ]; do sleep 2; done
    [ "$(stat -f %m "$record" 2>/dev/null || echo 0)" -gt "$before" ] || fail "the application's start wrote no set-up record at $record within 180 s"
    sleep 5
    osascript -e "quit app id \"$IDENTIFIER\"" > /dev/null 2>&1 || true
    until=$(( $(date +%s) + 60 ))
    while pgrep -f "$APP/Contents/MacOS/" > /dev/null && [ "$(date +%s)" -lt "$until" ]; do sleep 1; done
    pkill -f "$APP/Contents/MacOS/" 2>/dev/null || true
    say "the application $version opened from $APP, its first start's set-up written to $record, and quit"
    ;;

left-to-homebrew)
    "$CARRIED" uninstall --dry-run > "$OUT/cask-dry-run.json" 2>"$OUT/cask-dry-run.err" || fail "the carried jason uninstall --dry-run failed: $(cat "$OUT/cask-dry-run.err")"
    fate="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["plan"]["desktop"]["application_fate"])' "$OUT/cask-dry-run.json")"
    [ "$fate" = left_to_homebrew ] || fail "the carried jason uninstall --dry-run says application_fate $fate, not left_to_homebrew"
    say "the carried jason uninstall --dry-run says the application is left to Homebrew"
    ;;

start-runtime)
    "$CARRIED" runtime start > "$OUT/cask-runtime-start.txt" 2>&1 || true
    "$CARRIED" runtime status > "$OUT/cask-runtime-before.json" 2>/dev/null || fail "no runtime answers: $(cat "$OUT/cask-runtime-start.txt")"
    say "a runtime started from $CARRIED answers: process $(field "$OUT/cask-runtime-before.json" pid), version $(field "$OUT/cask-runtime-before.json" runtime_version)"
    ;;

upgrade)
    version="$1"
    rm -f "$OUT/cask-upgrade.txt"
    status=0
    brew upgrade --cask "$TEST_TAP/$TOKEN" > "$OUT/cask-upgrade.txt" 2>&1 || status=$?
    cat "$OUT/cask-upgrade.txt"
    [ "$status" = 0 ] || fail "brew upgrade --cask exited $status"
    [ "$(version_of "$APP")" = "$version" ] || fail "$APP is $(version_of "$APP") after the upgrade, not $version"
    [ -L "$(link)" ] && [ -e "$(link)" ] || fail "$(link) does not resolve after the upgrade"
    owed="$DATA/run/start-owed.json"
    old_pid="$(field "$OUT/cask-runtime-before.json" pid)"
    [ -n "$old_pid" ] || fail "no runtime was recorded before the upgrade (start-runtime)"
    if [ -f "$owed" ]; then stopped="the note that a start is owed was written ($owed)"
    elif ! kill -0 "$old_pid" 2>/dev/null; then stopped="the old runtime's process $old_pid is gone"
    else fail "the old runtime's process $old_pid still runs after the upgrade, and no note says a start is owed"; fi
    kill -0 "$old_pid" 2>/dev/null && fail "the old runtime's process $old_pid still runs after the upgrade"
    sleep 10
    if "$CARRIED" runtime status > "$OUT/cask-runtime-after.json" 2>/dev/null; then
        answered="$(field "$OUT/cask-runtime-after.json" runtime_version)"
        case "$answered" in "$version"|"$version+"*) ;; *) fail "the runtime answering after the upgrade is $answered, not $version" ;; esac
        say "the uninstall stanza stopped the old runtime ($stopped), and a runtime answers at $answered after the upgrade"
    elif [ -f "$owed" ]; then
        say "the uninstall stanza stopped the old runtime ($stopped); Homebrew did not reopen the application, so nothing paid the owed start yet"
    else
        fail "after the upgrade no runtime answers and no note says a start is owed"
    fi
    outdated="$(brew outdated --cask --greedy "$TEST_TAP/$TOKEN" 2>&1 || true)"
    [ -z "$outdated" ] || fail "brew outdated --cask --greedy still names the cask: $outdated"
    again=0
    brew upgrade --cask "$TEST_TAP/$TOKEN" > "$OUT/cask-upgrade-again.txt" 2>&1 || again=$?
    cat "$OUT/cask-upgrade-again.txt"
    [ "$again" = 0 ] || fail "a second brew upgrade --cask exited $again"
    [ "$(version_of "$APP")" = "$version" ] || fail "a second upgrade changed the application to $(version_of "$APP")"
    say "brew upgrade --cask took the application to $version; brew outdated names nothing; a second upgrade did nothing"
    ;;

uninstall)
    brew uninstall --cask "$TOKEN" 2>&1 | tee "$OUT/cask-uninstall.txt" || fail "brew uninstall --cask exited non-zero"
    [ ! -e "$APP" ] || fail "$APP is still there after brew uninstall --cask"
    [ ! -L "$(link)" ] && [ ! -e "$(link)" ] || fail "$(link) is still there after brew uninstall --cask"
    [ ! -e "$(caskroom)" ] || fail "Homebrew's record $(caskroom) is still there after brew uninstall --cask"
    sleep 5
    running="$(ps -axo command= | grep -iE '(Jason\.app/|/bundle/jason)' | grep -v grep || true)"
    [ -z "$running" ] || fail "a Jason process is left after brew uninstall --cask: $running"
    say "brew uninstall --cask took the application, its link and Homebrew's record"
    ;;

uninstall-left)
    # The listing after a plain uninstall against the machine before. What it may leave, as the caveats say: what Jason
    # set up (the wrapper, the profile lines, the starts at logon and launchd's record of them, the skills), the
    # application's state under ~/Library (and the native libraries the carried jason extracted), the data directory and
    # the harness root. Any other line, left or gone, fails.
    after="$OUT/after-cask-uninstall.txt"; before="$OUT/before.txt"
    [ -f "$after" ] && [ -f "$before" ] || fail "no listing to compare: $after and $before"
    declared='^(local bin|profile |launch agent file|launchd |claude skills|agents skills|system-kept|updater cache|ShipIt cache|extracted|data directory|harness root)'
    outside="$( { comm -13 "$before" "$after" | sed 's/^/+ /'; comm -23 "$before" "$after" | sed 's/^/- /'; } | grep -vE "^[+-] $declared" || true)"
    [ -z "$outside" ] || fail "brew uninstall --cask left the machine different from before outside what it may leave: $(printf '%s' "$outside" | tr '\t\n' ' ;')"
    say "against the machine before, a plain uninstall differs only in what it may leave: $(comm -13 "$before" "$after" | cut -f1 | sed 's|/.*||' | sort -u | tr '\n' ';')"
    ;;

carried-uninstall)
    state_places=("$HOME/Library/Preferences/$IDENTIFIER.plist" "$HOME/Library/Saved Application State/$IDENTIFIER.savedState" "$HOME/Library/Caches/$IDENTIFIER" "$HOME/Library/HTTPStorages/$IDENTIFIER" "$HOME/Library/HTTPStorages/$IDENTIFIER.binarycookies" "$HOME/Library/WebKit/$IDENTIFIER" "$HOME/Library/Application Support/$IDENTIFIER")
    there=()
    for place in "${state_places[@]}"; do [ -e "$place" ] && there+=("$place"); done
    printf '%s\n' "${there[@]:-}" > "$OUT/cask-state-before-carried-uninstall.txt"
    [ "${#there[@]}" -gt 0 ] || fail "none of the application's state under ~/Library was there before the carried jason uninstall, though the leg opened the application"
    status=0
    "$CARRIED" uninstall > "$OUT/cask-carried-uninstall.json" 2>"$OUT/cask-carried-uninstall.err" || status=$?
    cat "$OUT/cask-carried-uninstall.json"
    [ "$status" = 0 ] || fail "the carried jason uninstall exited $status: $(cat "$OUT/cask-carried-uninstall.err")"
    [ -d "$APP" ] || fail "the carried jason uninstall deleted the application Homebrew placed"
    [ -L "$(link)" ] || fail "the carried jason uninstall took the cask's link"
    for place in "${there[@]:-}"; do
        [ -z "$place" ] || [ -e "$place" ] || fail "the carried jason uninstall took $place, the application's state Homebrew's zap takes"
    done
    say "the application's state under ~/Library kept: ${#there[@]} place(s) there before, each still there"
    grep -qF 'brew uninstall --zap --cask jason' "$OUT/cask-carried-uninstall.json" || fail "the carried jason uninstall does not name brew uninstall --zap --cask jason"
    [ ! -e "$HOME/.local/bin/jason" ] || fail "the carried jason uninstall left the wrapper"
    say "the carried jason uninstall exited 0, left the application, its link and its state to Homebrew, and named brew uninstall --zap --cask jason"
    ;;

zap)
    status=0
    brew uninstall --zap --cask "$TOKEN" > "$OUT/cask-zap.txt" 2>&1 || status=$?
    cat "$OUT/cask-zap.txt"
    [ "$status" = 0 ] || fail "brew uninstall --zap --cask exited $status"
    [ ! -e "$APP" ] || fail "$APP is still there after the zap"
    [ ! -L "$(link)" ] && [ ! -e "$(link)" ] || fail "$(link) is still there after the zap"
    [ ! -e "$(caskroom)" ] || fail "Homebrew's record $(caskroom) is still there after the zap"
    [ ! -e "$HOME/.local/bin/jason" ] || fail "the wrapper $HOME/.local/bin/jason is still there after the zap"
    agents="$(ls "$HOME"/Library/LaunchAgents/ai.jason.runtime.plist "$HOME"/Library/LaunchAgents/$IDENTIFIER.plist 2>/dev/null || true)"
    [ -z "$agents" ] || fail "a start at logon is still there after the zap: $agents"
    profiles="$(grep -l '\.local/bin' "$HOME"/.zprofile "$HOME"/.zshrc "$HOME"/.profile "$HOME"/.bashrc 2>/dev/null | tr '\n' ' ' || true)"
    skills="$( { find "$HOME/.claude/skills" -mindepth 1 -maxdepth 1 2>/dev/null || true; find "$HOME/.agents/skills" -mindepth 1 -maxdepth 1 2>/dev/null || true; } | wc -l | tr -d ' ')"
    say "recorded, not asserted: after the zap the profiles naming ~/.local/bin are '${profiles:-none}', and $skills skill folder(s) are under ~/.claude/skills and ~/.agents/skills"
    say "brew uninstall --zap --cask jason, with no --force, took the application, its link, Homebrew's record, the wrapper and both launch agents' files"
    ;;

formula)
    tarball="$1"; version="$2"
    [ -f "$tarball" ] || fail "no archive at $tarball"
    formula="$(tap_dir "$TEST_TAP")/Formula/$TOKEN.rb"
    mkdir -p "$(dirname "$formula")"
    cat > "$formula" <<EOF
class Jason < Formula
  desc "Jason's command line, as the older formula installed it"
  homepage "https://get.jasonharness.ai/"
  url "file://$tarball"
  version "$version"
  sha256 "$(sha_of "$tarball")"

  def install
    bin.install "jason"
  end
end
EOF
    trusted "$OUT/formula-install.txt" --formula "$TEST_TAP/$TOKEN" brew install --formula "$TEST_TAP/$TOKEN" || fail "the formula did not install"
    [ -L "$(link)" ] || fail "the formula is not linked at $(link)"
    case "$(python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$(link)")" in "$(prefix)/Cellar/$TOKEN/$version/"*) ;; *) fail "$(link) does not lead into the formula's keg" ;; esac
    say "the older formula $version is installed and linked at $(link)"
    ;;

beside-formula)
    status=0
    trusted "$OUT/cask-beside-formula.txt" --cask "$TEST_TAP/$TOKEN" brew install --cask "$TEST_TAP/$TOKEN" || status=$?
    [ "$status" = 0 ] || fail "brew install --cask beside the formula exited $status"
    grep -qiE 'already a binary|skipping|not linking|conflict' "$OUT/cask-beside-formula.txt" || fail "Homebrew said nothing of the link it could not make beside the formula"
    case "$(python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$(link)")" in "$(prefix)/Cellar/"*) ;; *) fail "$(link) no longer leads to the formula's keg" ;; esac
    say "beside the linked formula the cask installed, and Homebrew said it did not link the cask's jason: $(grep -iE 'already a binary|skipping|not linking|conflict' "$OUT/cask-beside-formula.txt" | head -n 1)"
    ;;

formula-named)
    record="$DATA/desktop/machine-setup.json"
    [ -f "$record" ] || fail "no set-up record at $record"
    grep -qF 'brew uninstall --formula jason' "$record" || fail "set-up's report does not name brew uninstall --formula jason: $(cat "$record")"
    "$CARRIED" status > "$OUT/cask-status.json" 2>/dev/null || true
    python3 -c 'import json,sys
checks = {c["name"]: c for c in json.load(open(sys.argv[1]))["checks"]}
detail = (checks["path"].get("details") or {}).get("homebrew_formula") or ""
sys.exit(0 if "brew uninstall --formula jason" in detail else 1)' "$OUT/cask-status.json" || fail "jason status's path check carries no homebrew_formula detail naming brew uninstall --formula jason: $(cat "$OUT/cask-status.json")"
    say "set-up's report and jason status's path check name brew uninstall --formula jason"
    ;;

formula-off)
    "$CARRIED" runtime stop > /dev/null 2>&1 || true
    brew uninstall --formula "$TOKEN" 2>&1 | tee "$OUT/formula-uninstall.txt" || fail "brew uninstall --formula jason failed"
    if [ -L "$(link)" ]; then say "after brew uninstall --formula jason the cask's link is at $(link)"
    else say "after brew uninstall --formula jason the cask's link is still missing, as Homebrew skipped it at the install; not a fault: the wrapper Jason's set-up placed finds this jason"; fi
    ;;

reset)
    for kind in --cask --formula; do
        if brew list "$kind" "$TOKEN" > /dev/null 2>&1; then
            if [ "$kind" = --cask ]; then brew uninstall --zap --force --cask "$TOKEN" || true; else brew uninstall --force --formula "$TOKEN" || true; fi
            say "reset: Homebrew's $kind $TOKEN, left by a step before, taken off by Homebrew's own uninstall"
        fi
    done
    ;;

*)
    fail "no step '$step'"
    ;;
esac
