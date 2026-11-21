#!/bin/bash
# One step of the macOS proof of Homebrew's cask, run by the release's proof-macos-cask job on the signed and notarized
# zip the macOS job uploaded. The cask is rendered from .github/cask/jason.rb.template, the one copy this repository
# keeps, into taps made inside the job: never the real tap.
#
#   test-cask-macos.sh <step> [arguments]
#
#   taps <feed> <version>          the tap jasonproof/audit, holding the cask as the real tap renders it, and the tap
#                                  jasonproof/test, holding it with its url naming the feed's zip on this machine
#   audit                          brew style and brew audit --strict on the cask as the real tap renders it; what
#                                  Homebrew says about trusting the tap is printed
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
#                                  the link resolving, brew outdated empty, and a second upgrade doing nothing
#   uninstall                      brew uninstall --cask jason: the application, the link and Homebrew's record gone, and
#                                  no Jason process left; what Jason set up stays, said
#   carried-uninstall              the carried jason uninstall: exit 0, the application, its link and its state under
#                                  ~/Library kept, and brew uninstall --zap --cask jason named
#   zap                            brew uninstall --zap --cask jason, with no --force: the application, the link and
#                                  Homebrew's record gone
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

case "$step" in
taps)
    feed="$1"; version="$2"
    zip="$feed/$ZIP_NAME"
    [ -f "$zip" ] || fail "no $ZIP_NAME in $feed"
    for tap in "$AUDIT_TAP" "$TEST_TAP"; do
        brew tap-new --no-git "$tap" > /dev/null
    done
    render "$version" "$(sha_of "$zip")" "" "$(tap_dir "$AUDIT_TAP")/Casks/$TOKEN.rb"
    render "$version" "$(sha_of "$zip")" "file://$zip" "$(tap_dir "$TEST_TAP")/Casks/$TOKEN.rb"
    say "the taps $AUDIT_TAP and $TEST_TAP made in this job, the cask rendered from .github/cask/jason.rb.template at $version; never the real tap"
    cat "$(tap_dir "$AUDIT_TAP")/Casks/$TOKEN.rb"
    ;;

audit)
    brew style "$AUDIT_TAP" 2>&1 | tee "$OUT/cask-style.txt" || fail "brew style refuses the cask: $(tr '\n' ' ' < "$OUT/cask-style.txt")"
    trusted "$OUT/cask-audit.txt" --cask "$AUDIT_TAP/$TOKEN" brew audit --strict --cask "$AUDIT_TAP/$TOKEN" || fail "brew audit --strict refuses the cask: $(tr '\n' ' ' < "$OUT/cask-audit.txt")"
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
    "$CARRIED" runtime status > /dev/null 2>&1 || fail "no runtime answers: $(cat "$OUT/cask-runtime-start.txt")"
    say "a runtime started from $CARRIED answers"
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
    sleep 10
    if "$CARRIED" runtime status > "$OUT/cask-runtime-after.json" 2>/dev/null; then
        say "a runtime answers after the upgrade: $(tr -d '\n' < "$OUT/cask-runtime-after.json" | cut -c1-200)"
    elif [ -f "$owed" ]; then
        say "the uninstall stanza stopped the runtime and left the note that a start is owed ($owed); Homebrew did not reopen the application, so nothing paid it yet"
    else
        fail "after the upgrade no runtime answers and no note says a start is owed"
    fi
    outdated="$(brew outdated --cask "$TEST_TAP/$TOKEN" 2>&1 || true)"
    [ -z "$outdated" ] || fail "brew outdated --cask still names the cask: $outdated"
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
    left=""
    [ -e "$HOME/.local/bin/jason" ] && left="$left the wrapper,"
    ls "$HOME"/Library/LaunchAgents/ai.jasonharness.desktop* > /dev/null 2>&1 && left="$left the start at logon,"
    say "brew uninstall --cask took the application, its link and Homebrew's record, and left what Jason set up:${left:- nothing} as the caveats say"
    ;;

carried-uninstall)
    status=0
    "$CARRIED" uninstall > "$OUT/cask-carried-uninstall.json" 2>"$OUT/cask-carried-uninstall.err" || status=$?
    cat "$OUT/cask-carried-uninstall.json"
    [ "$status" = 0 ] || fail "the carried jason uninstall exited $status: $(cat "$OUT/cask-carried-uninstall.err")"
    [ -d "$APP" ] || fail "the carried jason uninstall deleted the application Homebrew placed"
    [ -L "$(link)" ] || fail "the carried jason uninstall took the cask's link"
    [ -e "$HOME/Library/Preferences/$IDENTIFIER.plist" ] || [ -e "$HOME/Library/Saved Application State/$IDENTIFIER.savedState" ] || [ -e "$HOME/Library/Caches/$IDENTIFIER" ] || say "no state of the application under ~/Library was there to keep"
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
    say "brew uninstall --zap --cask jason, with no --force, took the application, its link and Homebrew's record"
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
