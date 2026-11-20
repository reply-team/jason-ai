# Releases and updates

.NET does not update a self-contained application, so Jason owns its update behaviour. This page is the
contract for it: what a release is, what it publishes, how a running runtime learns that a newer one exists,
how a person asks the same question by hand, and what `jason update apply` does to this machine when they say
yes.

**Nothing starts itself unless you asked.** Nothing is registered with the machine until
`jason runtime autostart enable` registers the runtime to start at logon (§8). An update keeps that
registration working, and whatever you wrote yourself, for the reason §7 gives.

**The macOS executable is signed with Reply's Developer ID and notarized by Apple, and the Windows executable with a
certificate issued to Reply through Azure Artifact Signing, timestamped; the Linux executable is not signed.**
Integrity is a SHA-256 you can check yourself, published beside the files and inside the manifest. On Linux it is the
only provenance there is; on macOS and Windows the signature says who built the executable as well. The Windows
signature is made by the release's Windows job alone: it runs in the GitHub environment `signing`, which only release
tags and release branches may run in, and signs in to Azure with that job's own OIDC token, which Azure accepts from
that environment alone; there is no client secret. On macOS the one native library the executable unpacks,
`libe_sqlite3.dylib`, is signed with the same Developer ID before it is bundled, so the executable keeps the hardened
runtime's library validation and ignores `DYLD_` environment variables; its only entitlements,
`com.apple.security.cs.allow-jit` and `com.apple.security.cs.allow-unsigned-executable-memory`, are the ones the .NET
runtime's JIT compiler needs.

## 1. What a release is

A release is the git tag `vX.Y.Z` and the files that tag's build publishes. A merge to `main` is not a release: a tag
is the statement that one commit is worth installing.

Everything about a release happens in one repository, `reply-team/jason-ai`: the source, its tags and its releases.
A release starts as the release pull request into `main`, whose run builds and tests the tree on Linux, macOS and
Windows. Once that run is green, the tag `v<version>` is pushed on the pull request's head and builds the release from
it: the critical tests again under the tag's version, the three executables, the macOS and Windows ones signed
and their archived signatures verified, the plugins' and the UI's packages, the desktop's, and the apt repository's
index for the desktop's deb, signed and read back as a machine's apt reads it. The release job then writes the
manifest and the checksums and publishes the release at once, with the assets below and its notes, as the latest
release unless a newer one already is or the version is a release candidate. The pull request is merged into `main` only
after the publication is checked: the README and every page GitHub shows are read from `main`, so `main` never
describes a release that is not out, and a release run that fails leaves `main` as it was. A release no longer asks the
Homebrew tap to bump its formula (§6). A hotfix reaches a release by a shorter way, one pull request into `main` from a branch
`hotfix/<name>` and no release branch, which [DEVELOPMENT.md](../DEVELOPMENT.md), "A hotfix", describes. The same tag pushed to any other repository that holds the workflow — a fork, a copy — builds what
it can and publishes nothing: a copy without the signing secrets stops at the macOS and Windows jobs, and one that
holds them assembles the release and says in one line that it publishes nothing.

Versions are [SemVer](https://semver.org). `0.x` releases are ordinary releases and are never flagged as
pre-releases — the pre-release flag is reserved for release candidates (`v1.0.0-rc.1`), because a repository
whose only releases are flagged has no "latest" for anything to resolve to.

## 2. The files a release publishes

| Asset | What it is |
|---|---|
| `jason-win-x64.zip` | the Windows executable, `LICENSE` (the MIT licence) and `THIRD-PARTY-NOTICES` |
| `jason-linux-x64.tar.gz` | the Linux executable, `LICENSE` (the MIT licence) and `THIRD-PARTY-NOTICES` |
| `jason-osx-arm64.tar.gz` | the macOS (Apple silicon) executable, `LICENSE` (the MIT licence) and `THIRD-PARTY-NOTICES` |
| `checksums.txt` | one `sha256sum` line per archive, and one for each package |
| `manifest.json` | what the updater reads |
| `install.sh` | the macOS and Linux install script, as this release was published with it |
| `install.ps1` | the Windows install script, as this release was published with it |
| `jason-plugin-reply.zip` | the Reply plugin's package: the files of `plugins/reply` at the tag, at the archive's root |
| `jason-plugin-journal-forwarder.zip` | the journal forwarder's package: the files of `plugins/journal-forwarder` at the tag, at the archive's root |
| `jason-plugin-ntfy.zip` | the ntfy plugin's package: the files of `plugins/ntfy` at the tag, at the archive's root |
| `jason-plugin-slack.zip` | the Slack plugin's package: the files of `plugins/slack` at the tag, at the archive's root |
| `jason-plugin-telegram.zip` | the Telegram plugin's package: the files of `plugins/telegram` at the tag, at the archive's root |
| `jason-ui.zip` | the browser UI's package: `ui/` built at the tag, with the release's version in its `manifest.json`; `jason ui install` places it |
| `jason-skills.zip` | the skills' package: `skills/` at the tag, under `skills/`, and a `source.json` naming the repository, the tag and its commit; `jason skills install` deploys from it when it is given no `--source` |
| `jason-desktop-windows-x64-setup.exe` | the desktop's Windows installer, signed, carrying this release's signed `jason.exe` |
| `jason-desktop-windows-x64-setup.exe.blockmap` | the installer's block map, from which the Windows desktop's updater downloads only what changed |
| `latest.yml` | the Windows desktop's update feed: the version, the installer's name and its SHA-512 |
| `jason-desktop-macos-arm64.dmg` | the desktop for Apple silicon, signed, notarized and stapled, carrying the osx-arm64 executable |
| `jason-desktop-macos-arm64.zip` | the same application, as the Mac desktop's updater downloads it |
| `jason-desktop-macos-arm64.zip.blockmap` | that zip's block map |
| `jason-desktop-macos-x64.dmg` | the desktop for Intel Macs, signed, notarized and stapled, carrying an osx-x64 executable built for it alone |
| `jason-desktop-macos-x64.zip` | the same application, as the Mac desktop's updater downloads it |
| `jason-desktop-macos-x64.zip.blockmap` | that zip's block map |
| `latest-mac.yml` | the Mac desktop's update feed: the version, each zip's name and its SHA-512 |
| `jason-desktop-linux-x64.deb` | the desktop for Linux on x64 as a deb, not signed, carrying the linux-x64 executable; it installs to `/opt/Jason` |
| `jason-desktop-linux-x64.AppImage` | the same application as one AppImage file, not signed, on the AppImage runtime that needs no libfuse2, its block map inside it; it needs a system that lets it run its sandbox, and on Ubuntu 24.04 and later is built to name the deb instead |
| `latest-linux.yml` | the Linux desktop's update feed: the version, the deb's and the AppImage's names and their SHA-512 |
| `Packages` | the apt repository's index of the deb: its control fields, its file name, its size, MD5 and SHA-256 |
| `Release` | the apt repository's release file: the origin `Jason`, the architecture, the date, and the size, MD5 and SHA-256 of `Packages` |
| `InRelease` | `Release`, clearsigned with the apt repository's key |
| `Release.gpg` | `Release`'s detached signature by the same key |

**The install scripts are a release's own files.** The one-liners fetch them from a release rather than from the
default branch: a script on the branch can be ahead of every release, and a pinned install runs the script the
release it installs was published with.

**The names carry no version, on purpose.** That is what makes

```text
https://get.jasonharness.ai/releases/latest/download/jason-linux-x64.tar.gz
```

resolve for every release there will ever be, so a README, an install script and a runtime's feed URL are
written once and never edited again. The address is Reply.io's own and keeps GitHub's paths (§4), so the store
behind it can change without any of them changing.

**An archive holds the executable, `LICENSE` and `THIRD-PARTY-NOTICES`, and nothing else.** That `LICENSE` is
the repository's, the MIT licence, which covers the whole repository, the runtime included. `THIRD-PARTY-NOTICES` names the .NET runtime and the packages the executable carries,
each with its licence. A plugin is installed into the data directory and nothing reads the install directory, so
a plugin bundled with the executable would be a file nothing loads. The Reply plugin is
published beside the archives instead, from 0.11.0 on, as a package of its own: `jason-plugin-reply.zip`, the
files of `plugins/reply` at the tag at its root, built once per release, listed in `checksums.txt`, and named in the
manifest's `plugins` (§3) rather than its artifacts, which are platforms. Installing it is `jason plugin install reply`, which
unpacks it into `~/.jason/plugins/reply` once it matches its line — `docs/golden-path.md` step 3 runs it — and
what is unpacked has the content digest of the package at the tag (DEC-INSTALL-002). The journal forwarder is
published the same way from 0.30.0 on, as `jason-plugin-journal-forwarder.zip`, the files of `plugins/journal-forwarder`
at the tag, and `jason plugin install journal-forwarder` places it in `~/.jason/plugins/journal-forwarder`; a receiver
on another host takes a grant and a configuration and no edit of the package, as
[its README](../plugins/journal-forwarder/README.md) says, and a copy a person does edit is theirs, which an update
leaves in place. ntfy, Slack and Telegram are published the same way from 0.32.0 on, as `jason-plugin-ntfy.zip`,
`jason-plugin-slack.zip` and `jason-plugin-telegram.zip`, each placed by `jason plugin install <id>`. Any other plugin
is installed as `docs/plugins.md` §4 says.

**The browser UI is published the same way, as `jason-ui.zip`:** `ui/` built once per release at the tag, with the
release's version in its `manifest.json`, and listed in `checksums.txt` but not in the manifest. It is built at the
tag rather than taken from CI, whose zip carries a development version and expires. `jason ui install` places it:
it downloads the package from the release this build was published as, holds it to its line in `checksums.txt`, to
this build's version and to the runtime's API, and swaps it in as `ui/default/` whole or not at all. The install
guide runs it after the role skills, `jason update apply` runs it at its end, and `jason status` names it where the
default UI is missing or another release's (DEC-UI-003).

**The desktop's files are published beside them, from 0.34.0 on, and its Linux packages from 0.35.0,** and listed in
`checksums.txt` but not in the manifest: an installed desktop updates itself through electron-updater, which reads
`latest.yml`, `latest-mac.yml` or `latest-linux.yml` of the latest release and checks the download against the SHA-512
the feed gives, so the runtime's updater never reads them. Each package carries the executable its platform's release
job built, and signed where that job signs, at the release's version, with `LICENSE` and `THIRD-PARTY-NOTICES`. The
Windows job builds the installer while its sign-in holds: the installer, its
uninstaller and `Jason.exe` are signed there, and verified with the `jason.exe` they carry. The macOS job builds an
osx-x64 executable for the Intel package alone, signed, notarized and assessed as the osx-arm64 one is, and never
published as an archive, so on an Intel Mac `jason update apply` hands the update to the application as it does on Apple
silicon (§7, "Handing the update to Jason Desktop"), which reads no archive; the test suite does not run on an Intel Mac.
The Linux job, which signs nothing, builds the deb and the AppImage around the linux-x64 executable it archives, reads
each back, and starts each once under a virtual display, sandboxed. The desktop never runs without Chromium's sandbox,
so the AppImage needs a system that lets it run its sandbox. It is built so that on Ubuntu 24.04 and later, as shipped,
the AppImage says so and names the deb, whose AppArmor profile lets the program make the user namespaces its sandbox
uses; on Ubuntu 24.04 and 26.04 as shipped the deb has been seen to start sandboxed and the AppImage to refuse. Neither is
signed.
Checked beside the deb,
`sha256sum --check --ignore-missing checksums.txt` proves it is the file the release published, not who built it;
checked beside the AppImage, it proves the same of the AppImage and no more. The deb's own updater is off: its updates
come with the system's, from Jason's apt repository, which is the index each release publishes beside the deb (below,
and §7). An AppImage updates itself, replacing the file where it is. Either is taken off by the one uninstall, `jason
uninstall` or Uninstall Jason… in the Jason menu (§9), which takes the updater's cache, `~/.cache/jason-desktop-updater`,
and the demo's two folders with it, keeps the data directory unless told otherwise, and keeps the harness root. A candidate writes the
same three feed names a release does, and its release is marked a pre-release, which the latest release never is, so no
installed desktop reads a candidate's feed: the desktop updates to stable releases only.

Installing a desktop update restarts the runtime. The desktop stops the runtime its own `jason` runs as, with `jason
runtime stop`, before the installer replaces that file, and the new version starts it again when it opens; it knows the
runtime is its own when `jason runtime status` answers for the instance the run file names and the descriptor's
`installed_image` is the application's own `jason` (`docs/runtime-api.md`). A runtime that does not stop leaves the
update uninstalled, and so does a run file whose runtime did not answer while the process it names still runs, or names
none, and a run file that is there and cannot be read; one started from another executable is never stopped. Where
PowerShell is available, the Windows installer and uninstaller look for any process running from the carried `jason.exe`
by its exact path, whichever data directory started it, and never close one: they stop it through the application's own
`--stop-runtime` start, which runs `jason runtime stop` for the data directory the installer's environment names, or the
default one, and look again. A person running them is asked first; an update and a silent run are not. A process still
running ends the run before a file is replaced, and a person is told to stop a runtime that did not stop with the
carried `jason.exe` by its full path, with `JASON_DATA_DIR` set when it was started with another data directory. Where
PowerShell is not available nothing is looked for: a person is asked to stop the runtime, and an update goes on without
a look. Once the runtime has stopped and before the update installs, the application as it was is copied under
`<data>/desktop/rollback/<version>/`, kept until the next update replaces it and named in the Jason menu; nothing puts
it back by itself, so going back is quitting Jason and putting that folder back in the place of the installed
application by hand, which leaves the data directory as the newer version left it. On Linux what an AppImage update
keeps there is the AppImage file itself. The deb is updated by the system, which keeps no previous version: going back
from it is installing the previous release's `jason-desktop-linux-x64.deb` again with `sudo apt install
--allow-downgrades` and holding it there with `sudo apt-mark hold jason-desktop`, or the next update installs the newer
one again (§7, "Going back").

**Taking the desktop off.** On macOS, Windows and Linux, Jason is one installation: the Jason Desktop application, which
carries `jason`, the command line your terminal and your agents use. It is removed once, by the one uninstall, the
carried `jason uninstall` (§9), from a terminal on every system, from Installed apps on Windows and from Uninstall
Jason… in the Jason menu on macOS and Linux: what Jason placed, then the application; the data directory stays unless the person
says otherwise, and the harness root always. On Linux the deb's package goes first, purged by apt with an administrator's
password asked once, and nothing of the account's goes when it is refused; an AppImage's file goes last. The system's own
removal (`sudo apt purge jason-desktop`, App Center, deleting the AppImage) takes the application and leaves the
account's own files, as it does for any application, and what they would start is skipped once its file is gone.
[desktop.md](desktop.md) lists what to take off by hand for a Jason that will not start.

**The apt repository's index is published beside the deb, from 0.49.1 on:** `Packages`, `Release`, `InRelease` and
`Release.gpg`, the four files apt reads from a flat repository, so that a release's downloads are an apt repository
holding the one deb. `Packages` is the deb's own control fields with its file name, `./jason-desktop-linux-x64.deb`, its
size, MD5 and SHA-256; `Release` names `Jason` as its origin and label, the suite `stable`, the deb's architecture and
the date it was written, with the size, MD5 and SHA-256 of `Packages`; `InRelease` is `Release` clearsigned and `Release.gpg` its
detached signature, both by the apt repository's key, whose public half is
[`install/jason-desktop-archive-keyring.gpg`](../install/jason-desktop-archive-keyring.gpg): an Ed25519 key that
certifies, kept offline, with an Ed25519 subkey that signs. The release's `apt-index` job writes them from the deb the
Linux job built, signing with that subkey, which the `signing` environment holds as the secret `APT_SIGNING_KEY`, and
then reads them back as a machine's apt reads them, against that public key: gpgv's verdict on both signatures,
`InRelease` signing the very text of `Release`, the deb's size and hashes against `Packages`, the hashes of `Packages`
against `Release`, `apt-get update` reading a copy of the files with no warning, through `InRelease` and again through
`Release` and `Release.gpg`. Before it signs, in the same step, every key in that file and every subkey that signs must be
unrevoked and more than 90 days from its expiry, read from the key itself, or the index is not signed, and the failure names
the date: an expired key makes apt refuse the index, and every machine that reads the repository then takes no update
until a deb is installed by hand, so the key is extended or replaced, and the new public key shipped in a deb, before
then. That check runs in this job alone, the tag's run and the dispatch dry run's, so it stops the assets that are
signed and never a pull request. Any of these failing stops the release before anything is published. The job may write
nothing; the release job needs it and attaches the four files, which `checksums.txt` lists after the desktop's. A pull
request has no signing environment, so CI's desktop job writes and reads back the same files from its own deb with a
key the run makes, and says so. `.github/scripts/write-apt-index.sh`, `check-apt-index.sh` and `check-apt-key.sh` say
each step.

## 3. The manifest

`manifest.json` is the updater's feed. One GET of one file: no GitHub API, so no token, no rate limit, and
nothing to break when an API version retires.

```json
{
  "schema": 1,
  "version": "0.2.0",
  "published_at": "2026-09-19T08:00:00Z",
  "release_notes_url": "https://get.jasonharness.ai/releases/tag/v0.2.0",
  "min_upgrade_from": null,
  "artifacts": {
    "win-x64":   { "asset": "jason-win-x64.zip",      "sha256": "e3b0…b855", "size": 114254557 },
    "linux-x64": { "asset": "jason-linux-x64.tar.gz", "sha256": "e3b0…b855", "size": 118201344 },
    "osx-arm64": { "asset": "jason-osx-arm64.tar.gz", "sha256": "e3b0…b855", "size": 112984576 }
  },
  "plugins": {
    "reply": {
      "version": "0.9.0",
      "asset": "jason-plugin-reply.zip",
      "digest": "sha256:5e0e…0a7d",
      "recorded": { "0.8.0": "sha256:b5c2…50f6", "0.9.0": "sha256:5e0e…0a7d" }
    },
    "journal-forwarder": {
      "version": "1.0.1",
      "asset": "jason-plugin-journal-forwarder.zip",
      "digest": "sha256:430e…8afa",
      "recorded": { "1.0.0": "sha256:9239…545b", "1.0.1": "sha256:430e…8afa" }
    }
  }
}
```

| Field | Rule |
|---|---|
| `schema` | the shape of this document. This build reads `1` and refuses anything else rather than guessing. |
| `version` | the release's version, SemVer. A version that does not parse makes the whole feed invalid. |
| `published_at` | an ISO-8601 UTC timestamp. |
| `release_notes_url` | where a person reads what changed, or `null`. An `http`/`https` address or nothing. |
| `min_upgrade_from` | the oldest version that may update to this one in one apply, or `null`. Every release from 0.20.0 names `0.19.0`, until an applier change raises it, the first build whose apply loads what it needs and hands over to the new executable (§7), so from it any release lands in one apply whatever .NET patch either side carries; an older build goes through 0.19.0 first. Read by the applier and by `jason update check`, not by the advertisement. |
| `artifacts` | one entry per platform. A release that names no artifact for your platform is still a valid release — it just has nothing for you. |
| `asset` (in `artifacts.<rid>`) | a file name, and only a file name: letters, digits, dot, dash, underscore. Never a path and never a URL. |
| `sha256` (in `artifacts.<rid>`) | 64 lowercase hexadecimal characters, with no `sha256:` prefix. |
| `size` (in `artifacts.<rid>`) | the archive's size in bytes, a positive number. |
| `plugins` | the plugin packages the release publishes beside its archives, by plugin id, or `null`: every release from 0.20.0 names the Reply plugin's, every release from 0.30.0 the journal forwarder's beside it, every release from 0.32.0 ntfy's, Slack's and Telegram's beside those, and a release with no list names none. The last field, after `artifacts`. A reader before 0.20.0 skips it, as it skips every root field it does not read. Read by `jason update check` (§5) and by the applier's `plugins` step (§7), not by the advertisement. A malformed `plugins` is refused with the whole feed by every reader from 0.20.0 on, the applier included, so its shape changes only with the schema. |
| `version` (in `plugins.<id>`) | the version of the package the release publishes, SemVer. |
| `asset` (in `plugins.<id>`) | the package's file name beside the archives, under the rule an archive's `asset` follows; its line in `checksums.txt` is the one the package is checked against. |
| `digest` (in `plugins.<id>`) | the package's content digest, `sha256:` and 64 hexadecimal characters: what the runtime computes over the unpacked files, and what `jason plugin list` shows for an installed copy. |
| `recorded` (in `plugins.<id>`) | every version the package's digest list at the tag records — `plugins/<id>.digests` — each with its digest, and the package's own among them. No version is recorded twice and no digest under two versions, so an installed copy's digest says which version it is. A digest the record does not hold is a package the list does not know: built or edited on that machine, or, for Reply's package, a version before its list's first line, 0.3.2. The check never names it; whether it is behind is `jason status`'s to say, by its version. |

The manifest is generated by the release workflow from the files it just built, so it is one source rather than
a second copy of what the checksums say. A download is fetched from **the feed's own directory** and the asset
name the manifest carried — never from a URL inside the document, because a feed that could name where to
download from could name anywhere, and the digest it also carries would then be checking its own bytes against
its own hash.

A manifest that breaks any rule above is refused whole (`update_feed_invalid`); nothing is half-read.

The schema changes only with a change of layout — the asset names, `checksums.txt`, the manifest's fields — and
an install script keeps reading every schema published under a tag it can be pointed at. A file published beside
the archives that the manifest does not list, as the UI's package is, changes no schema: the manifest stays the same
document, and each install script reads `checksums.txt` by its own archive's line. Nor does an added root field every
reader since 0.1.0 skips, as `plugins` is: the runtime and `install.ps1` read the root's fields by name, and
`install.sh` takes the first `"version":` line, which `plugins`, written after the root's `version`, never comes
before.

## 4. What the runtime sends, and when

A running runtime checks the feed on its own, because the person who most needs to know that an update exists is
the one who never thinks to ask.

**What it sends, in one sentence: one `GET` of the feed URL with a `User-Agent` of `jason/<version>`, and
nothing else.** No body, no query, no identifier, no machine facts, nothing about your campaigns, your plugins
or your data directory. The answer is kept in memory and shown by `system.info`; it is never written to disk.

**Where it goes.** The default feed is on `get.jasonharness.ai`, an address Reply.io operates, which redirects every
request under `/releases/` to the same path in the public repository's releases on GitHub. A download is composed
from the feed's own directory (§3), so it goes the same way, with the same `User-Agent`. An installation built
before 0.7.0 asks GitHub directly; both answer with the same file. An installation from 0.7.0 to 0.23.x asks the
earlier address, `jasonharness.ai`, which keeps redirecting the same paths; if it ever stops,
`jason update apply --feed https://get.jasonharness.ai/releases/latest/download/manifest.json`, or a reinstall with
the current one-liner, moves it, and the executable that arrives asks `get.jasonharness.ai` from then on.
`Update:FeedUrl` set to that address moves only that runtime's own check: `jason update`, `jason ui install` and
`jason plugin install` read no settings and ask the address they were built with unless `--feed` names another.

A check that fails — offline, a page that answers 500, something that is not a manifest — is a log line at
warning and never an error to any caller. An offline machine is a normal machine. A feed that has not answered
within **10 seconds** is one of those failures: a manifest is a few hundred bytes, so waiting longer only makes
an offline machine slower to admit it. `jason update check` waits exactly as long, for the same reason.

### The settings

| Setting | Default | Range | What it does |
|---|---|---|---|
| `Update:CheckEnabled` | `true` | — | whether this runtime asks at all |
| `Update:FeedUrl` | `https://get.jasonharness.ai/releases/latest/download/manifest.json` | an `https` address, or `http` on loopback | where it asks |
| `Update:InitialDelayMinutes` | `5` | 1..1440 | how long after starting it first asks |
| `Update:IntervalHours` | `24` | 1..168 | how often it asks after that |

Settings live in `~/.jason/config/settings.json` or in `JASON_`-prefixed environment variables, like every other
setting; they are read at each tick, so an edit applies without a restart. A live edit never stops the runtime: a
refused edit keeps the last good settings and is logged by path and type, and a start with a malformed or
mistyped file still fails, saying where — on stderr under `jason runtime run`, and after "It said:" in what
`jason runtime start` answers: the line and byte of a malformed file, the setting's path and
expected type for a mistyped one. `Update:CheckEnabled=false` turns the outbound call off entirely.

**The feed must be `https`.** Plain `http` is allowed only on loopback, which exists so that a test can put a
stub where the network would be; a manifest fetched over plain `http` on a network is one anybody in the way can
rewrite, and the digest inside it would then be the attacker's digest for the attacker's bytes.

### What it says

`system.info` — and therefore `jason runtime status` — carries an `update` section once a check has succeeded:

```json
{ "available": true, "version": "0.2.0", "checked_at": "2026-09-19T08:00:00Z", "release_notes_url": "…" }
```

It is `null` before the first successful check, and `available` is `false` when the release is not newer than
what is running. A development build carries the number of the release it is on the way to with `-dev` after it,
which is *older* than that release and newer than every release before it: it advertises that release once it is
published, and none that came before.

### And what it says about the plugin packages its release publishes

`system.info` carries a `released_plugins` section as well, read with no network: the runtime carries the plugin
packages its own release publishes beside its archives — today the Reply plugin's, the journal forwarder's, ntfy's,
Slack's and Telegram's — and says, for each, how the package of that id installed stands against it:

```json
[{ "id": "journal-forwarder", "state": "not_installed", "released": { "version": "1.0.1", "digest": "sha256:…" }, "installed": null },
 { "id": "reply", "state": "behind", "released": { "version": "0.7.0", "digest": "sha256:…" }, "installed": { "version": "0.6.1", "digest": "sha256:…" } }]
```

`current` is the same content digest; `behind` is another digest with a lower version, an earlier release's
package; `differs` is another digest with the same version or a higher one, so a later release's package or one
built or edited on this machine; `not_installed` has `installed: null`. The yardstick is the release this runtime
is, never the newest one — whether a newer release exists is `update`'s to say — and a development build compares
with its own tree. The section is `null` on a runtime older than it. `jason status` reads it as the optional
`plugin_package` check. The recorded exchanges under `docs/api/fixtures/system.info/` number their digests per
occurrence, so a `current` recording shows two different words where the runtime saw one digest.

### And what it says about the skills it will teach from

`system.info` carries a `skills` section beside it, describing the one directory the runtime reads role skills
from — `<data>/skills/roles` — as it is at the moment it is asked:

```json
{
  "role_skills_directory": "/home/you/.jason/skills/roles",
  "max_skill_bytes": 1048576,
  "roles": [{ "role": "researcher", "bytes": 4096, "problem": null, "problem_code": null, "problem_exception": null }],
  "problem": null,
  "problem_code": null,
  "problem_exception": null
}
```

The cap is reported because it is a live setting rather than a constant, and something that deploys into that
directory has to validate against the number this runtime will actually enforce: a deployment measured against
the wrong cap is refused at every launch of that role. A `problem` on a role is why a launch would not be
taught it — the refusals the launcher makes, and the case it does not refuse but nobody meant, a directory
with no `SKILL.md`, which is copied and teaches a host nothing. `bytes` is `null` when the directory could not
be measured at all, which is not the same as a skill of no bytes.

Every `problem` comes with a `problem_code` from a closed set, and, where the operating system refused, a
`problem_exception` naming the refusal by one of `UnauthorizedAccessException`, `IOException`,
`ArgumentException` or `other`. The sentence says what is wrong and what to do; the code says which problem it
is, and is what a diagnostic bundle carries, because the bundle withholds the sentence — it can carry an
operating system's message.

| code | what is wrong | a launch of the role |
|---|---|---|
| `skills_directory_unreadable` | the role skills directory is there and cannot be listed | not asked: a launch reads its own role's directory |
| `role_directory_unreadable` | the operating system refused part of the role's directory | fails: `executor_launch_failed` |
| `role_directory_too_deep` | the role's directory nests deeper than a skill is read | fails: `executor_launch_failed` |
| `role_directory_changed` | the role's directory moved while it was being read, as a deployment does | refused after reading it again: `role_skill_unreadable` |
| `skill_file_missing` | the directory holds no `SKILL.md`: it is copied, and the host learns nothing from it | no; it teaches nothing |
| `skill_file_is_link` | `SKILL.md` is a link, which a launch does not copy | refused: `role_skill_invalid` |
| `skill_file_unreadable` | `SKILL.md` could not be read, or the operating system would not say what it is | refused: `role_skill_invalid` |
| `skill_name_missing` | `SKILL.md` gives itself no name: no front matter, no name in it, or an empty one | refused: `role_skill_invalid` |
| `skill_name_mismatch` | the skill names itself something other than the role | refused: `role_skill_invalid` |
| `skill_too_large` | the skill's tree is over `Roles:MaxSkillBytes` | refused: `role_skill_invalid` |

**Nothing here is cached.** The role skills directory is read at every launch, so a deployment is live the
moment it lands and there is nothing to reload. That is also why this section reports what is on disk now
rather than what was there when the runtime started.

Because it is read at every launch, something deploying into it can be reading it at the same moment. A
deployment therefore assembles each skill beside its destination and moves it into place, so a launch sees the
tree that was there or the tree that arrived and never a blend; a launch that loses that race reads the tree
again, and one that loses it twice ends with `role_skill_unreadable` rather than running with no skill. A skill
holding a file of the person's is the exception: it is written file by file where it stands, with no reader waited
for, so a launch may read some of its files new and some old.

## 5. Asking by hand

```sh
jason update check
jason update check --human
jason update check --feed http://127.0.0.1:8080/manifest.json
```

`jason update check` asks the feed itself rather than the runtime, so it answers on a machine whose runtime will
not start — which is one of the times you most want to know what the newest version is. It exits 0 with the
answer, or 1 with an error envelope: `update_feed_unreachable` when nobody answered, `update_feed_invalid` when
what answered was not a manifest, `update_feed_insecure` when the feed is not an address this build will fetch.

The answer also names `upgrade_command`, the one command that updates this installation: `jason update apply` on
Jason's own, and the package manager's upgrade — `brew upgrade jason`, `winget upgrade Reply.Jason` — where one
installed it (§6). Under a manager `--human` prints the three lines to type, the runtime stopped before the manager's
upgrade and started after it, because winget cannot replace the executable a running runtime holds and Homebrew
replaces the image a running runtime starts its plugin hosts from (§7); the field stays one command.

On Jason's own installation, when the newest release is newer and this version is below its `min_upgrade_from`,
`install_first` names that version and `upgrade_command` is `jason update apply --version` with it; otherwise
`install_first` is null, and always under a manager. `--human` prints the refusal's line in place of the install clause.

When the newest release is newer and names its plugin packages (§3), `plugin_updates` lists each package installed
here that the release publishes a later version of, as `{"id": "reply", "installed": "0.8.0", "latest": "0.9.0"}`, in
id order and under a manager too, since no manager places plugins; `--human` prints a line for each after the lines
about Jason. The installed version is the one the release's `recorded` holds for the content digest of the files under
`~/.jason/plugins/<id>/`, so the runtime is still not asked. A package whose digest the record does not hold is a
package the list does not know: built or edited on that machine, or, for Reply's package, a version before its list's
first line, 0.3.2. The check never names it, since whether it is behind is `jason status`'s to say, by its version; and
a package that cannot be read is passed over in silence, since what is wrong there is `jason status`'s to say as well.
Beside `install_first`'s null, `plugin_updates` is null where no package is named or the release is not newer, and `[]`
where the release is newer and nothing installed here that the record holds is behind. The list and the `plugins` step
of `jason update apply` (§7) read one plan, so it names exactly the packages the apply replaces; a manager's upgrade
runs no apply, so after one `jason plugin install <id> --replace` places each.

## 6. Installing

```sh
curl -fsSL https://get.jasonharness.ai/releases/latest/download/install.sh | sh
```

```powershell
irm https://get.jasonharness.ai/releases/latest/download/install.ps1 | iex
```

Each script works out your platform. `--version X.Y.Z` installs a particular release.

On macOS, Windows and Linux, Jason is one installation: the Jason Desktop application, which carries `jason`, the command line
your terminal and your agents use. It is installed once, updated once and removed once. On macOS and Windows the script downloads the
application's package, **verifies its SHA-256** and its signature, puts it in its place — on a Mac `/Applications`
where this account may write there, else `~/Applications`; on Windows the installer, run silently, into
`%LOCALAPPDATA%\Programs\jason-desktop` — and opens nothing. Then the `jason` the application carries runs
`jason install --roles` (§9): `jason` in the terminal, the skills for Claude Code and Codex where each is found, an older command-line
install retired, and the role skills. [`docs/INSTALL.md`](INSTALL.md) §2 says what each script does, step by step.

On Linux the script installs Jason Desktop as the system's users install comparable applications, **verifies the
SHA-256** against `checksums.txt` before anything of it runs, and opens nothing; the system decides the form. Where there
is apt (Ubuntu, Debian, Mint), the release's deb is installed by apt, with the one administrator's password a system
package needs, asked by `sudo` where the line has a terminal and otherwise by the desktop's own password dialog through
`pkexec`, apt waiting up to ten minutes for the package system and the install given twenty; a password not given, the
dialog left unanswered for those twenty minutes, no session to ask in, the dialog failing, the package system held by
another install are each refused in their own words, with nothing installed; apt failing part-way says so, with the
commands that finish the package or take it off. The deb's
own scripts add Jason's apt repository, `https://get.jasonharness.ai/apt/`, as
`/etc/apt/sources.list.d/jason-desktop.sources`, signed by the key the package carries as
`/usr/share/keyrings/jason-desktop.gpg`, and `/usr/bin/jason`, a link to the `jason` the package carries, for every
account; the package adds no rule for Ubuntu's automatic updates, so Jason's updates are offered and never installed on
their own. Where there is no apt (Arch, Fedora and the rest), the AppImage is placed for this account
alone, with no password, at `~/Applications/jason-desktop-linux-x64.AppImage` by one rename, its menu entry and icons
written under `~/.local/share`, and the `jason` it carries copied under `<data>/desktop/bundle/<version>/` as the
application's own start copies it; refused before anything is downloaded where the system refuses unprivileged user
namespaces, which Chromium's sandbox needs, or has no setuid `fusermount`, which opens the file. An AppImage already in
that place stays the installation, apt or not, and so does a package already at the version or a later one. The script
refuses before anything is read when run as root, when given `--install-dir` or `--no-modify-path`, and inside an
agent's sandbox (`NoNewPrivs`), where neither `sudo`, `pkexec` nor `fusermount` could act; and before anything is
changed while Jason Desktop runs and the install would change it. Then the `jason` the application carries sets the
machine up as on macOS. It installs no release before 0.49.1, whose own script installs the command line alone on
Linux. The command-line archive, `jason-linux-x64.tar.gz`, is still published, to carry the update of a command-line
install made before (§7).

The terminal's `jason` is `~/.local/bin/jason` on macOS, the wrapper that runs the application's own, and so for an
AppImage on Linux, running the copy; for the deb it is the package's own `/usr/bin/jason`; and on Windows the
application's `resources\bundle\jason.exe`, its folder on this account's Path. On macOS and Windows nothing here needs
administrator rights or a password, and on Linux the deb does, once each time apt installs or takes it off. On Windows everything is installed
for this account alone, and so is an AppImage; on a Mac the application goes to `/Applications` where this account may
write there, else `~/Applications`, and the deb installs Jason Desktop for every account of the computer, at
`/opt/Jason`, with its `/usr/bin/jason`; the skills and the data are this account's own everywhere, and the terminal
command everywhere but the deb.

They resolve to the latest release, so where none has been published they answer 404 and there is nothing to
install from — build from source until one has. [`docs/INSTALL.md`](INSTALL.md) is the whole path either way,
in the order a stranger reads it.

The workflow that builds a release has a button — a `workflow_dispatch` that produces a draft, the button's draft,
before any tag exists. GitHub offers that button only for a workflow that is already on the **default branch**;
once it is, a branch's own version can be run from it (`gh workflow run release.yml --ref <branch>`). Its jobs that
sign, publish or hold a token run in the environment `signing`, which admits only release tags and release branches, so
a change to the pipeline is rehearsed from a release branch before it merges: a run from any other branch, a dry run
included, is refused at the macOS and Windows jobs, and nothing is assembled. The button's draft is made in
`reply-team/jason-ai` alone, names the commit the run built, is never the latest release, and asks nothing of the tap.
The draft is for reading and is deleted afterwards: a release is published only by its tag, never by publishing the
button's draft. With `dry_run` it makes no draft either: every job runs, and the assembled release — archives, packages, manifest,
checksums and notes — comes back as one artifact of the run.

A release's notes are the template `.github/release-notes-template.md` and what that release says of itself,
`RELEASE-NOTES.md` at the root: its first line is `# Jason <version>`, and the rest goes into the body after the
template's opening paragraph, with `{{version}}` filled in. The version job reads it before anything is built: on a
tag, a file that is missing, untitled or names another version stops the release there, so the file the version pull
request leaves naming the last release can never ship with the next one; the button's run carries the template alone
instead, with a warning. A candidate's tag needs the file to name the candidate, and notes not yet written name their
version with `-unwritten`, which no tag carries.

### Through a package manager

Where a package manager installed Jason — Homebrew on macOS and Linux, winget on Windows — the installation is the
manager's. The manager keeps its own record of what it installed: Homebrew a receipt in the keg, winget an entry in
this account's list of installed programs. That record, and not the path, is how Jason tells such an installation from
its own, so a directory that merely looks like the manager's is still Jason's own.

The manager then updates the installation, with the runtime stopped first and started after (§7), and takes it off,
with its own commands; Jason's verbs act only on what is Jason's:

| | Homebrew | winget |
|---|---|---|
| updates it | `brew upgrade jason` | `winget upgrade Reply.Jason` |
| takes it off | `brew uninstall jason` | `winget uninstall Reply.Jason` |

`jason update check` names the command (§5) and `jason update apply` refuses (§7). `jason runtime autostart enable`
registers the manager's own entry, which stays put across its upgrades, rather than the keg a link resolves to (§8).
`jason uninstall` takes off the logon registration, the skills and, on the word, the data directory, and leaves the
rest to the manager (§9).

Homebrew installs from the tap `reply-team/tap`, kept in the repository `reply-team/homebrew-tap`. Its formula installs
the command line alone, without Jason Desktop, and from 0.49.0 Jason on a Mac is Jason Desktop, so the formula stays at
the last 0.48 release it carries: a release no longer asks the tap to bump it, and `brew upgrade jason` finds
nothing newer.

The one line at the top of this section is the way to 0.49 and later on both systems: on a Mac it installs Jason
Desktop, and on Linux, from 0.49.1, Jason Desktop as well. An installation Homebrew made, on a Mac or on Linux, reaches it in three lines:

```sh
jason uninstall
brew uninstall jason
curl -fsSL https://get.jasonharness.ai/releases/latest/download/install.sh | sh
```

`jason uninstall` stops the runtime and takes off the logon registration and the skills it deployed, keeps the data
directory, and leaves the rest to Homebrew (§9); `brew uninstall jason` takes the rest; the one line installs Jason
Desktop, which opens the same data directory as it is.

### Carried by the desktop

The desktop application carries a `jason` of its own inside its package, and beside it `version.json`, a JSON object
whose `version` names the version the package was built at. That record, and not the path, is how a `jason` knows it
is carried: a folder that merely looks like an application's is still Jason's own, and a copy of the carried folder
made with its record is carried wherever it is.

The file is the application's: on macOS it is part of a signed application, on Windows the installer owns it, on Linux
the deb's package owns it, and an AppImage's copy under `<data>/desktop/bundle/<version>/` is made by the application from
the file it carries. The desktop's own updater replaces the application and the `jason` inside it together, and the
system's way of removing the application removes both. So, run as a carried executable, `jason update apply` replaces no
file itself: it hands the update to the application's own updater, with no window, and the new `jason` the application
then carries finishes it (§7, "Handing the update to Jason Desktop"); Check for Updates… in the desktop's menu updates the
same way from a window. `jason update rollback` puts back what the command line kept and names the one step by hand that
puts the application back (§7, "Going back"). On Linux the deb's updates are the system's: there both refuse with
`update_managed_by_package_manager`, naming apt (§7). Where there is no Jason Desktop to hand the update to, the
application not where its `jason` says, both refuse with `update_carried_by_desktop`, before anything is fetched or
stopped, and on Linux `jason update apply` refuses the same way from a terminal with no display, which the application it
starts needs (an ssh session, a service). `jason uninstall` is the one
uninstall of the application and of everything placed for it on macOS, Windows and Linux, and refuses with
`uninstall_carried_by_desktop` only where no system's places are known, before anything is read or removed (§9). A resumed apply and a
rollback act on the file the update record names, not on the one they run as, so another `jason` sharing the data
directory, one the install line put on the PATH say, refuses both where that record names a carried file other than
itself. `jason update check` and `jason status` answer as they do anywhere. When the runtime the desktop finds is
another version than the one it carries, its system screen names Check for Updates where that runtime runs from the
carried `jason`, and `jason update` where it is another installation's.

A desktop whose `jason` was built before 0.49.0 carries the record too. One built before the refusal that came before this
hand-over, the candidate 0.34.0-rc.1, takes every step up to `swapped` in its own process before it hands the rest to the
new build (§7): the carried file is replaced before any code of a later build runs. The new build finishes an update handed
to it that way, rather than leave the runtime stopped and the update half done.

## 7. Updating

```sh
jason update apply
jason update apply --version 0.2.0
jason update apply --drain-seconds 0
jason update apply --feed http://127.0.0.1:8080/manifest.json
```

`jason update apply` takes this installation from the version it is running to the version the feed names: it
downloads the release for your platform, proves its digest, drains and stops the runtime, puts the new
executable where the old one was, starts it again and checks that what came up is what was installed. It is the
same command whether a runtime is running or not.

**It refuses an installation a package manager owns** (§6), with `update_managed_by_package_manager`, before anything
is staged, drained or stopped, and so does `jason update rollback`. The manager keeps its own record of the version,
and a file replaced behind its back would leave that record wrong. Such an installation is updated through its
manager, in three lines rather than one:

```sh
jason runtime stop
brew upgrade jason
jason runtime start
```

— or `winget upgrade Reply.Jason` in the middle on Windows. The runtime is stopped first because the manager knows
nothing about it. On Windows, winget cannot remove the executable a running runtime holds: its upgrade fails and leaves
the old version in place. With the runtime stopped, the same upgrade replaces it and keeps the uninstall entry, its
install location and the package directory as they were. Homebrew replaces the image behind a running runtime and
deletes the old keg, and a running runtime starts every plugin host from its own image, so provider work fails at launch
until the runtime is restarted. A runtime left running across such an upgrade, or across a file copied over by hand, is
still the old build; `jason status` says so in its required `runtime_version` check, whose repair is
`jason runtime restart`. A runtime newer than the `jason` asking is the other way round, and its repair is
`jason update apply`: a restart from the older `jason` would start the older build on the newer data.

**On Linux the deb's updates come with the system's.** The package is one of the system's, read from Jason's apt
repository like any other: Software Updater offers a new version and installs it when the person says so, as `sudo apt
update && sudo apt upgrade` does in a terminal, and Ubuntu's automatic updates leave it alone, since the package adds no
rule for them. So the `jason` the deb carries refuses `jason update apply` and `jason update rollback` with
`update_managed_by_package_manager` before anything is read or staged, printing apt's commands, with the runtime stopped
first and started after; for a rollback, the previous release's deb installed again and held with `sudo apt-mark hold
jason-desktop`, since apt keeps no previous version, and `sudo apt-mark unhold jason-desktop` takes updates again. `jason
update apply --finish` still runs, as the application's start runs it once apt has installed a new version; `jason update
check` names `sudo apt update && sudo apt upgrade`. Where the system installed a new version under a running window,
which looks at the package's `version.json` once a minute, the update item reads **Restart Jason and Its Runtime to
Finish the Update to <version>** and one notice says so; the item, or a click on the notice, stops the runtime the old
version runs and starts Jason again, whose start finishes the update. Otherwise the item reads **Updates Come Through the
Package**. `jason status`'s optional `update` check says when updates cannot arrive — Jason's apt source gone or turned
off, as a release upgrade of Ubuntu turns third-party sources off, to be turned on again in Software & Updates → Other
Software — and when the package is a version this installation was not set up for yet, which Jason Desktop's start
finishes: it runs `jason install` and the finishing step, and only once both completed records the version it set up
for. So the check names that start and no command, since the finishing step alone records nothing; in a window already
open, the restart item where the package changed under it, and otherwise **Put Back jason and the Skills…** under
Command Line, which runs the same set-up again. `sudo apt-mark hold jason-desktop` stops every update of it.

**A `jason` the desktop application carries hands the update to the application** (§6): the Desktop's own updater
installs it with no window, and the new carried `jason` finishes it, as "Handing the update to Jason Desktop" below says;
on Linux that is an AppImage, which updates itself. Where there is no Jason Desktop to hand it to, the application not
where its `jason` says, it refuses with `update_carried_by_desktop` before anything is staged, drained or stopped, and so
does `jason update rollback`; on Linux `jason update apply` refuses the same way from a terminal with no display. An
update handed to it past `swapped` whose install path is the carried file itself is still finished there. Any
other `jason` refuses with the same code where the update in flight, or for a rollback the update recorded, names a carried
file other than itself.

Where the feed names no release, `jason update apply` answers `update_feed_unreachable` and does nothing;
there is nothing to apply until one is published. The feed resolves to the latest release, and a repository
with no releases has none.

An installation whose executable carries no `jason update apply` moves forward by re-running the install
one-liner in §6 until it is on a build that does; after that, `jason update apply` is how it moves.

### The steps, and the ledger that names them

An update is nine steps, and every one of them is written to `~/.jason/update/ledger.json` **before** it is
taken. That file is the update's **ledger**, and it is never called a journal: the journal is the runtime's own
append-only chronicle inside the database, and this is a single file that one process writes, the next one
reads, and the update after this one overwrites.

| Step | What has been begun |
|---|---|
| `staged` | the archive for this platform is downloaded, its SHA-256 checked against the manifest, and the executable and its two notice files are unpacked under `~/.jason/update/staged/<version>/` |
| `drained` | the runtime has been told to claim nothing new, and what it was already running has finished or the wait has run out |
| `stopped` | the runtime has been asked to stop, and its process is gone |
| `kept` | the installed executable has been moved to `~/.jason/update/previous/`, its notice files after it, and the install path is empty |
| `swapped` | the staged executable has been moved onto the install path, its notice files after it |
| `plugins` | each plugin package installed as an earlier version the new release records has been replaced by the release's own, and the one it replaced moved to `~/.jason/update/previous/plugins/<id>/` |
| `started` | a runtime has been started from the new executable |
| `healthy` | that runtime answers, says it is the version this update installed, and has its migrations applied |
| `complete` | the update is over, and the ledger stays behind as the record of it |

After `complete` the apply redeploys the skills this installation recorded, and that is no step of the ledger: by
then the update has happened, so nothing about the skills is resumed, rolled back or allowed to fail it. "What an
update does not touch", below, says what it does.

Because each line is written before its step is taken, the last step a ledger names is the last step that was
**begun** — never the last that finished. That is what makes a kill anywhere recoverable: every step is written
so that beginning it twice is the same as beginning it once, so the next `jason update apply` reads the file,
picks the sequence up at the step it names, and carries it through.

`--drain-seconds` bounds the wait at `drained`; the default is 120 and `--drain-seconds 0` means do not wait at
all. Either way the applier says what it left behind: work still running keeps its lease, and the runtime that
comes up next finishes it or its enforcer takes it back.

**The new executable finishes the update.** `jason update apply` runs from the install path, which is the file it
replaces. The executable is a single file that reads the .NET libraries inside it as it first needs them, and from
`kept` on the file at its own path is no longer the one it is running, so a library first needed after that would be
read out of another build. So the apply loads every library it can reach before it renames anything, and once `swapped`
has put the new executable in place it runs `jason update apply` from that executable: the new build reads the ledger at
`swapped` and takes `plugins`, `started`, `healthy` and `complete`, the skills and the UI, from a file that is its own.
What it says on standard error is passed on. When it completes, its answer carries every step's line, the ones taken
before it first; when it refuses, its refusal and its exit code are the apply's. A new executable that cannot be run, or
that ends any other way — a crash, a kill, an answer cut short — is `update_not_healthy`, naming the step the update
stands at: typing `jason update apply` again runs the new build, which finishes it, and `jason update rollback` puts the
old one back. An apply that resumes past `swapped`, typed into the new executable, has nothing to hand over and carries
on itself. A rollback loads the same before it moves the executable aside. Because the hand-over comes at `swapped`, the
`plugins` step is always the new build's, whichever build began the update, so it raises no floor: `min_upgrade_from`
stays 0.19.0.

Every release from 0.20.0 names 0.19.0 as its `min_upgrade_from`, so a build older than 0.19.0 updates to 0.19.0 first;
that update is performed by the older build, which does neither, so when the two builds carry different .NET patches
it can stop at `started` with a failure to load a library. Typing `jason update apply` again runs the new build, which
finishes it; one more `jason update apply` then takes the newest release.

**An update leaves a runtime running.** It stops the old one itself and starts the new one from the install
path, so a machine that had a runtime up before the update has one up after it. So does a machine that did
not: on an installation with no runtime answering, `drained` and `stopped` are satisfied without asking anybody
anything — the ledger says so and says why — and `started` starts the new build anyway. That is the ordinary
way to update an installation nobody has started yet, and it is the only way the new build's *first* start is
the one that migrates the database.

### Where an update stands

```sh
jason update status
jason update status --human
```

`jason update status` reads the ledger and asks no runtime anything, because the moment you most want this
answer is the moment there may be no runtime to ask. It exits 0 either way: exit 3 is this CLI saying a step
needed the runtime and could not have it, and reading a file is not such a step.

It answers `in_flight` with the step reached, or `in_flight: false` with no step at all when this installation
has never run an update. A ledger left at `complete` is the third answer: `in_flight` is false, the step is
`complete`, and the record carries the version pair, the migrations the new build's first start applied and the
backup it wrote. That record is what a rollback reads an hour later, so you can see what one would act on
before you ask for it. A file that is there and is not a ledger is refused with `update_ledger_invalid` rather
than half-read.

### What an update does not touch

An update replaces the executable and its two notice files, replaces each plugin package installed as an earlier
release's, writes under `~/.jason/update/`, redeploys the skills this installation recorded, and places the new
release's UI as `ui/default/`. It does not touch the rest of the data directory: not the database, not
`~/.jason/config/settings.json` and the grants and plugin configuration in it, not any other package under
`~/.jason/plugins/`, not the logs,
not the work directories. Nor does it touch anything you registered with the machine to start Jason — a systemd unit,
a scheduled task, a login item — because the file at the install path is replaced in place rather than moved
elsewhere, so whatever points at that path still points at Jason.

Because `settings.json` is left as you wrote it, a key a release renames stays there under its old name.
`Responder:MaxLaunchesPerDay` is such a key: 0.31.0 renamed it to `Launches:MaxPerDay:responder`, and
[`work-execution.md`](work-execution.md) has the map. A file that still names it stops the new runtime's first start, so
the update stands at `started` with `update_not_healthy` and the runtime's sentence after "It said:". Carry the number
over by hand, take the old key out, and `jason update apply` starts the runtime again.

Its `plugins` step, between `swapped` and `started`, replaces each plugin package installed as an earlier version the
new release records — the packages `jason update check` named (§5) — with the release's own. The archive is fetched from
the release's own directory and held to its line in `checksums.txt`, and the tree it unpacks to to the digest the
manifest names, before anything moves; the package it replaces is moved to `~/.jason/update/previous/plugins/<id>/`. It
comes before `started` because no runtime is running there, so no package moves under a runtime and the new runtime's
first load reads the new package. The packages it replaces are written to the ledger before the first of them moves, so
a kill anywhere in the step is finished by the next `jason update apply`. A package the record does not know — built or
edited on that machine, or, for Reply's package, a version before its list's first line, 0.3.2 — is left byte for
byte, and so is one that is current, later than the release's, or cannot be read; a plugin that is not installed is not
placed. The apply prints one `plugins:` line for each package the release publishes: `plugins: reply 0.8.0 -> 0.9.0`,
`plugins: reply 0.9.0 is current`, or why it was left as it is, except for packages that are not installed, which are
said as in `jason status`: one on a line of its own, `plugins: slack is not installed, so none was placed; …`, and two
or more on one line naming each, `plugins: slack and telegram are not installed, so none was placed; …`, which ends
with the command that installs one. A step resumed after a kill prints lines only for the packages its ledger names. A
release the step cannot read again — the network gone after the swap — gives one line,
`plugins: not checked — …; run: jason status`, and every package stays as it was. A package that cannot be fetched,
checked or moved — a download that breaks off, a digest that does not match, a file held open past the wait — is left
as it was, and the update still stands:
its line says why and ends `run: jason plugin install <id> --replace`, and until that has run `jason status` fails its
optional `plugin_package` check, naming the installed version and the version this release published. A release that
publishes no plugin package says so and names nothing to run. A newer package may request a capability you have not
granted; the apply grants nothing, and `jason plugin list` shows what each package requests beside what is granted. A
package manager's upgrade runs no `jason update apply`, so after it `jason plugin install <id> --replace` places the
package, as step 3 of [the wiring check](golden-path.md) does.

Its last step, after `complete`, is the skills: exactly what `jason skills update` does, at the new release's tag.
The runtime's own role skills, and each agent harness this machine detects, are redeployed wherever a deployment
there was recorded, from the source its record names; a deployment made with `--root` into a directory detection
does not find is not, and `jason skills update --root <dir>` moves it. A record naming this repository's git address,
which every default deployment before 0.33.0 names, is read as the default, so it moves to the new release's
`jason-skills.zip`; an apply given `--feed` reads that package from the feed it names as well. A deployment made at a tag
chosen with `--ref` stays at its ref, and nothing is written into an agent's harness that has no record. The apply
prints one `skills:` line for each pack group it carried — one pack from one source at one ref, in the roots that
hold it — with what it wrote. A file you edited is kept and the rest of its group moves; the group's line names
the file. If a group cannot be redeployed — no network, a release that published no skills package, no `git` for a
group taken from a git repository with `--source`, a skill held open by a role that was launching, an interrupt while
they are fetched — the update still stands: the new executable
is in place and its runtime is running, that group's `skills:` line says why and ends
`run: jason skills update`, and whatever could not move is still recorded at the tag before, so `jason status`
fails it and prints the same repair it always has. An update applied by a build before 0.12.0 has no such step, so
it needs one `jason skills update` after it; from 0.12.0 on, `jason update apply` does it. A package manager's
upgrade does not run `jason update apply`, so after `brew upgrade jason` or `winget upgrade Reply.Jason` it is
`jason skills update` that moves the skills.

After the skills comes the UI, as `jason ui install` places it: the new release's `jason-ui.zip`, read from the
feed's own directory beside its `checksums.txt`, held to its line there, to the new version and to the API the new
runtime says it speaks, and swapped in as `ui/default/`; `ui/custom/` is never touched. One `ui:` line says
`placed <version>`. A release that publishes no UI — one from before the UI was published, which `--version` can
name — says so and names nothing to run. Anything else — a digest that does not match, no network — says why and ends
`run: jason ui install`. The update still stands either way. An update applied by a build older than this step, and
a package manager's upgrade, place no UI, so `jason ui install` follows them, and `jason status` names it until it
has.

The one thing an update does change beyond the executable is the database, and not by itself: the new build's
first start migrates it, exactly as any start does, after taking the backup that every migration takes. `healthy`
records which migrations that was and where the backup went, which is what makes the undo below possible.

### When it stops halfway

Kill the process anywhere and the ledger is still right, because it was written first. Run `jason update apply`
again and it finishes from the step on disk, or says what it cannot finish; one killed during `healthy` starts the
runtime again before it asks it, since the kill may have left none. Before `kept` nothing has been replaced: `jason
status` is ready beside a runtime that answers, its optional `update` check names the update and the step it stopped
at with `jason update apply` as the repair, and `jason runtime start` starts the installed build and says on stderr
that the update waits. From the swap on, from `kept`, `jason status` fails its `runtime` check the same way, and
`jason runtime start` and `restart` start and stop nothing and say the same: a runtime started any other way would run
outside the update, with its last steps never taken. A runtime registered to start at sign-in (§8) runs `jason runtime
run`, which reads the ledger as well: a sign-in in that window is refused and starts nothing: with `update_in_progress`,
or, while the update's own runtime is up, as a second runtime. `jason status` names the apply. The apply's own start
of the runtime, and the rollback's, carry a mark in the runtime's environment, `JASON_UPDATE_LAUNCH`, never on its
command line, and `jason runtime run` starts under that mark alone; the runtime drops it before it starts anything of
its own. A ledger that cannot be read is refused by all four, with `update_ledger_invalid` and the file's path;
removing it is the way on when no update is wanted. Two states are worth knowing about by name.

**Between `kept` and `swapped` the install path is empty.** The old executable has been moved to
`~/.jason/update/previous/`, its two notice files after it, and the new one is not in place yet. The
window is a few renames wide, but it is the one window a person cannot type their way out of, because the
`jason` on your PATH is the file that is not there. Before the window opens the applier leaves a copy of itself under
`~/.jason/update/applier/`, an ordinary Jason that reads the ledger and finishes the update it finds, and that copy is
the first way out:

```text
~/.jason/update/applier/jason update apply
```

The second way out needs no Jason at all: copy `~/.jason/update/previous/jason` back over the install path by
hand, and you have the version you started with, running as it was before. On Windows the file is `jason.exe`
and the install path is `%LOCALAPPDATA%\Programs\jason\jason.exe`. The ledger still says the update is in flight from
`kept`, so remove `~/.jason/update/ledger.json` as well: a build from 0.21.0 on reads it, and while it is there refuses
`jason runtime start` and the next sign-in with `update_in_progress`.

**Between `started` and `healthy` a new runtime is up and has not been checked.** The next run asks it what
version it is and whether its migrations are applied, and takes it no further until it answers. A runtime that
answers as the wrong version, or with no migrations applied, is `update_not_healthy`.

### Going back

```sh
jason update rollback
```

It puts the executable from `~/.jason/update/previous/` back and starts the runtime on it.
Where the update's new build migrated the database, it also restores the backup that migration took and deletes
the WAL sidecars beside it — a database file from before a migration, opened with the write-ahead log of after
it, is corruption.

A rollback leaves the skills where the update's last step put them, at the newer release's tag. The runtime you
went back to then fails `role_skills`, because its role skills belong to a release it is not, and `ready` is false
until `jason skills update` — or `jason skills install --roles-only`, for the role skills alone — brings them to the
tag of the version you are on. It leaves the UI where the update put it too, and `jason status` names
`jason ui install` for the default UI from the newer release, which places the one of the version you are on.
A rollback does put back each plugin package the update replaced, from `~/.jason/update/previous/plugins/<id>/`, where
the package installed is still the one the update placed, or where nothing is installed beside the kept copy — the
ledger names that package, so an empty place there is the update's own move left half made — and leaves one that
differs, with the earlier package kept there: the runtime you went back to cannot run without packages it loads, as it
can without the skills and the UI, and a package changed since the update is somebody's later install, which a rollback
does not undo.

**The database is restored only while it is certain that nothing has happened since.** That is decided from the
chronicle: the line it stood at when the new version was declared healthy, against the line it stands at now. If
it has moved on, work has been recorded that the older schema has no place for, and silently discarding it would
be the worse of the two answers. If **no runtime is answering** — you stopped it, it crashed, the machine was
rebooted — then the question cannot be answered at all, and that counts the same way: a rollback does not
assume the answer it would prefer. Nor does it where the update **never reached a healthy runtime**: the line is
recorded at `healthy`, so an update refused because the version that came up was the wrong one, or one still
migrating as you type this, left nothing to compare against. Never healthy is not never served — that runtime is
still up, because nothing here stops it, and it has been recording through the new schema ever since. All three
answers are the same answer: the executable goes back, the database is left exactly as it
stands, and the message names the backup file. The section below is how you then put that database back
yourself, having decided that you want to.

**A Jason does not start on a database a newer one migrated.** From 0.49.0 on, a runtime whose database holds a
migration its build does not know refuses to start, with `data_newer` and a sentence that names its version and the
data directory and says to update, before it takes the lock, writes a log or a backup, or runs a migration: no file in
the data directory is added or removed, and the database and its write-ahead log keep every byte; SQLite may
rebuild `jason.db-shm`, its index of a log a runtime left behind. `jason runtime start` and `restart` answer that refusal by its code, and a start at
logon refuses the same way and starts nothing. So a version from 0.49.0 on that a rollback put back, with the database
left as it stands, starts no runtime until that database is put back by hand; the refusal says so instead of that it
may refuse. A version before 0.49.0 has no such check, and EF Core passes over the history rows it has no migration
for, so it runs its own model over the newer schema.

**A rollback of an update a carried `jason` handed to Jason Desktop** puts back what the command line kept: each plugin
package the update replaced, by the rule above, and the database, only while the chronicle stands where it stood at
`healthy`. It replaces no file of the application's and starts no runtime, because the `jason` that would start one is
still the newer one, and a newer runtime would migrate a restored database again. It stops the runtime and ends with one
line that names the step by hand: quit Jason Desktop, put the copy the Desktop kept (`kept_application` in the ledger,
under `~/.jason/desktop/rollback/<version>/`) back in the place of the application's folder, then `jason runtime start`;
for an AppImage the copy is the AppImage file, put back in the place of the file. A record that names another `jason`'s
file is refused with `update_carried_by_desktop`, naming that `jason`'s own rollback. The `jason` the deb carries
refuses a rollback with `update_managed_by_package_manager` and names the previous release's package, installed again
with `sudo apt install --allow-downgrades` and held with `sudo apt-mark hold jason-desktop` (above).

You will usually be typing `jason update rollback` from the PATH, which means the executable being replaced is
the one running the command. That is expected and it works — the file is renamed out of the way rather than
written over — but it is also why the replaced executable may still be sitting in `~/.jason/update/replaced/`
afterwards: a running program cannot delete itself. Nothing removes it later either; each is named for the
version and the moment it was set aside, and deleting them is safe as soon as that version is no longer
running.

### Applying an older release

```sh
jason update apply --version 0.30.0
```

`--version` names any release the feed publishes, an older one included, and `jason update apply` takes the installation
there by the same nine steps. What it does not do is move a plugin package back. The `plugins` step replaces a package
that is behind the release's, and leaves one that is not any package the release published byte for byte. A newer package
is such a one: the release's record holds the digests of what it published, and a later version is not among them. So the
apply prints a line such as `plugins: journal-forwarder differs from every package a release published, so it was left as it is`
and the newer package stays in `~/.jason/plugins/<id>/`. A release before 0.32.0 names no `ntfy` at all, so the apply says
nothing about it and the package simply stays. The older runtime then loads a package written for a newer one.

Two packages make that matter. A runtime older than 0.31.0 does not know the manifest key
`capabilities.http.any_granted_host`, which `ntfy` 1.0.3 and the journal forwarder 1.1.0 carry, and it refuses a manifest
with a key it does not know. A package problem rejects the whole load, and at a start there is no earlier set to keep
([plugins](plugins.md), "Two kinds of problem"), so the runtime starts with **no plugin loaded at all**, Reply's included.
What you see is a runtime that answers, work that fails because no plugin is there to perform it, and `jason plugin list`
naming the package and the manifest key it refused. The same holds for a copy of the forwarder 1.2.0 on a runtime older
than 0.32.0, which refuses `capabilities.files` in the same way, and for the forwarder 1.3.0 on a runtime older than 0.36.0,
which refuses the reserve and search kinds it declares as kinds it does not write.

To go back cleanly, put the package the older release published in its place, which depends on whether that release
published it. `jason plugin install` places the package of the release the running executable is, so it only works for a
package that release publishes.

Where the older release publishes the package, which for the journal forwarder is 0.30.0 and later, run this just after
the apply, for each such package you had installed:

```sh
jason plugin install journal-forwarder --replace
```

Where it does not, which for `ntfy` is anything older than 0.32.0, `jason plugin install` refuses, saying the release
publishes no such package. Move `~/.jason/plugins/ntfy/` out of the plugins directory, anywhere you like, and run:

```sh
jason plugin reload
```

The same holds for the forwarder on a release older than 0.30.0. Either way, `jason plugin list` then shows the plugins
loaded. Settings you wrote for the newer package, a grant of a host by name, say, stay in `settings.json` and are read
again if you update forward later.

None of this touches the database, and an apply of an older release migrates nothing: the older executable opens the
database as the newer one left it. One from 0.49.0 on refuses to start on a database the newer one migrated
(`data_newer`, "Going back" above), so the apply's own start of it fails; an earlier one may fail on it, as `jason
update rollback` itself warns. A rollback after such an apply puts the newer executable back and leaves the database
as it is. "Going back" above says when a rollback restores a backup and why the write-ahead log beside a restored file
is deleted; read it before you rely on an older executable with the data directory you have.

### Restoring a database by hand

Every start that migrates writes a backup under `~/.jason/state/backups/` first, named
`jason-<stamp>-before-<migration>.db`. `jason runtime status` names the one this start wrote, and so does the
ledger at `complete`. Putting one back is four steps, and it is a manual procedure on purpose — nothing here
decides for you that yesterday's database is the one you want:

```sh
# 1. Stop the runtime, and make sure it is really gone.
jason runtime stop

# 2. Copy the backup over the database.
cp ~/.jason/state/backups/jason-20260919T120000Z-before-20260920000000_Next.db ~/.jason/state/jason.db

# 3. Delete the sidecars. A write-ahead log from after the migration against a main file from
#    before it is corruption, and SQLite will happily replay it.
rm -f ~/.jason/state/jason.db-wal ~/.jason/state/jason.db-shm

# 4. Start the binary that matches that database — the one the backup's name was written by, which
#    for a backup taken before a migration is the version you were on before the update; a command-line
#    install's update kept it here:
~/.jason/update/previous/jason runtime start
```

Step 4 is the command line's. **Where Jason Desktop carries `jason`**, on macOS and Windows and for an AppImage on
Linux, the version before the update is the copy of the application Jason Desktop kept under
`~/.jason/desktop/rollback/<version>/` (`kept_application` in the ledger): quit Jason Desktop, put that copy back in the
place of the application's folder, or of the AppImage file, then run `jason runtime start`. For the deb it is the
previous release's package, installed again and held as "Going back" above says. `~/.jason/update/previous/jason` is not that version there: after a move into Jason
Desktop it is the command-line executable the move's update replaced, a release before 0.49.

There is one case the runtime puts a backup back itself: a start whose migration fails. Nothing has written to the
database since that start took its backup, so it puts that copy back, does not start, and says so — the migration
that failed, the backup, and that the version you ran before still runs on the database. A migration SQLite cannot
make in one transaction would otherwise leave a database part migrated, which no later start can migrate. What no
code survives — a power cut, a process ended while it migrated — still leaves one part migrated, and the next start
then fails on it. So a start that migrates first writes down, in `~/.jason/state/migration.json`, which backup it took
and the line the chronicle stood at, and deletes that record once it is done. The next start finds the record, and when
its own migration fails too it puts back the stopped start's backup — the installation as it stood before the upgrade
— keeps both backups, says so, and the start after it migrates from there. It does that only while the chronicle stands
at the line written down: if anything has been recorded since, putting the backup back would lose it, so the database
is left as this start found it and the message names the stopped start's backup for the steps above, for you to decide.
A migration that itself writes lines to the chronicle moves that line too, so a start stopped part way through one is
never put back this way: by design, the backup is named instead. The record names the migration the stopped start began
at, and while anything is left to migrate it stays even when that migration is in the history, since the start may have
been stopped inside a later one; only a start with nothing left to migrate deletes it. A second start stopped the same
way carries the first one's backup and line forward rather than its own. Older backups
are let go of only once a migration has succeeded, and never the one a record still names. This put-back restores the
database alone: it leaves an update's backup and its ledger exactly as they were, and a `jason update rollback` after it
still finds its ledger whole and still puts the previous executable back. The database it would restore is already the
one the start put back, the copy from before the upgrade; and since an update whose new version was stopped while it
migrated never reached a healthy runtime, its ledger holds no chronicle line, so the rollback answers from the executable
alone and leaves the database as it stands. Such a start also leaves the migration lock taken, which would hold every
later start waiting; the runtime migrates only while it holds its instance lock, so the next start clears that lock, says so
in the log, and migrates. A start in the same second as one before it names its backup `-2`, `-3` and
on, rather than write over one. A start that made the database itself, on a new installation, has nothing to take a backup of: if its migration fails it deletes the half-made file, says that the file held nothing and no data is lost, and the next start begins from nothing.

### What an update refuses, and why

Every refusal exits 1 with an error envelope on stdout, and the message names the file or the version, because
a code on its own tells nobody what to do next.

| Code | What happened |
|---|---|
| `update_up_to_date` | the feed offers nothing newer than what is installed, and no `--version` asked for something else |
| `update_artifact_corrupt` | what was downloaded is not the digest the manifest published. The bytes are deleted rather than kept for a retry |
| `update_artifact_unreachable` | the download did not arrive, or the archive could not be read. Worth trying again |
| `update_artifact_unexpected` | the archive does not hold the executable a release for this platform is supposed to hold, or the release publishes nothing for this platform, or the manifest read for `--version` names another version. That last is said before the manifest's `min_upgrade_from` is read, since a manifest of another release says nothing about this one: no archive is downloaded and no update begins. The same code comes at `staged` when the manifest read again there names another version than the update in flight, which has already begun. Its notice files are taken where the archive holds them, and an archive without one is not refused |
| `update_in_progress` | an earlier carried `jason update apply` handed Jason Desktop's installer this update — the record at `kept`, or its recorded request given the go or answered `installing` — less than the install's ten-minute wait ago (a request whose files are written in the future, by a clock set back since, counts as older than the wait), and the application is not the new version yet: Jason Desktop is not asked again and nothing is changed, and once the application is the new version `jason update apply` finishes the update; a run that carries such an update on after that wait asks again, and never puts back a record it did not begin. Or a *different* version is already in flight, or, run as the `jason` Jason Desktop carries, another `jason update apply` or `jason update rollback` of this data directory holds the update's lock, `<data>/update/update.lock`, which each part of an update takes while it acts and keeps while the next part runs as its child (below), and which `jason install` takes for its machine setup and refuses with this code while an update holds it; where `jason install` holds it, an update beside it and a second `jason install` refuse with `install_in_progress` instead. The same version is resumed rather than refused, so re-running `jason update apply` after a kill is always the right move. `jason runtime start` and `restart` refuse with it too from the swap on, from `kept`, starting and stopping nothing: the apply starts the runtime itself as one of its steps, and a runtime started around it would run outside the update. So does `jason runtime run`, which a sign-in registration runs, on stderr with exit 1 and nothing started, unless the start is the apply's or the rollback's own; while the update's own runtime is up, a sign-in is refused instead as a second runtime, and starts nothing either. Before `kept` nothing has been replaced, and `jason runtime start` and a sign-in start the installed build |
| `update_not_directly_applicable` | the release's `min_upgrade_from` is above the version installed; no archive is downloaded and no update begins. From 0.20.0 the message names both commands, `jason update apply --version <floor>` and then `jason update apply` or `jason update apply --version <to>` when the apply named one; a build before 0.20.0 names the version to install first, which the same two commands reach |
| `update_not_updatable` | this Jason runs through `dotnet`, so there is no single executable to replace. Install a published release with the one-liner in §6 |
| `update_managed_by_package_manager` | a package manager — Homebrew or winget — installed this Jason and keeps its own record of the version, or on Linux this is the `jason` Jason Desktop's deb carries, a package the system updates, so the file is not this build's to replace. Nothing is staged, drained or stopped, and `jason update rollback` refuses the same way. The message prints the three lines to type instead: `jason runtime stop`, the manager's upgrade (for the deb `sudo apt update && sudo apt upgrade`), `jason runtime start`; for the deb's rollback, the previous release's package installed again with `sudo apt install --allow-downgrades` and held with `sudo apt-mark hold jason-desktop` between the two |
| `update_carried_by_desktop` | this `jason` is the one the desktop application carries, known by the `version.json` beside it (§6), and there is no Jason Desktop to hand the update to — the application is not where this `jason` says — or the update recorded names such a file other than this one, so the file is the application's and not this build's to replace. Nothing is staged, drained or stopped, and `jason update rollback` refuses the same way. The message names the desktop's Check for Updates, which updates the application and its `jason` together. On Linux `jason update apply` refuses with it too, before anything is read, from a terminal with no display (an ssh session, a service), since the Jason Desktop it starts needs one; the message names Jason Desktop's update item, or a terminal of the desktop |
| `update_refused_by_desktop` | a carried `jason` handed the update to Jason Desktop and the Desktop refused it: its reason and its sentence are the message, with the version its feed offered where it read one. Nothing was installed, a `jason` is at the carried path (on Windows put back there first), the record that stood before is put back, and the runtime is as it was found. Retryable where the reason is one that passes: `busy`, `window_open`, `check_failed`, `download_failed`, `runtime_not_stopped`, `timed_out`, `go_timed_out`, `go_there` |
| `update_desktop_unanswered` | a carried `jason` handed the update to Jason Desktop and had no answer: the application could not be started, its start ended with no request file naming an end, a running window that took the request named none within the wait, a file of the request's id was there before the request was made, or a go of the request's id was there before the verb made one. A `jason` is at the carried path (on Windows put back there first), the record that stood is put back and the runtime is as it was found; retryable |
| `update_desktop_not_installed` | Jason Desktop handed the update to its installer, and ten minutes later the application's `version.json` still did not name the version asked for; on macOS the message carries the end of ShipIt's own log. Where it still names the version the update began from, nothing was installed, and the message names the likely cause, a Jason Desktop that is running, one of another data directory included, whose application its installer does not replace while it runs: quit it, then run `jason update apply` again. On Windows a `jason` is put back at the carried path first where the installer left none there. Where the application still names the version the update began from, the record that stood is put back; otherwise the update stays in flight and the runtime is started from the `jason` at the carried path, or, where there is none, from the verb's own file. Retryable |
| `update_cross_volume` | the staged file and the install path are on different volumes. An update renames rather than copies, because a half-copied executable is the one state nothing can recover from. Point `JASON_DATA_DIR` at the executable's volume, or install Jason on the data directory's |
| `update_runtime_unreachable` | the runtime would not drain or would not stop, so nothing was replaced |
| `update_not_healthy` | the new version is in place, but the runtime it starts is not the one this update installed, or the new executable could not be run to finish the update. The executable can be put back |
| `update_file_refused` | a file could not be moved, copied or written: a directory that is not one, a permission, a lock. It covers every file these two verbs touch — an update's renames and a rollback's, the ledger both are recorded in, and the database restore. The message names the path and what to do — including which executable to run when `jason` is no longer on your PATH. On Windows an executable another program holds open — a scanner reading the one just run or just downloaded — is tried again for 10 seconds first; one still held then is named from `~`, with how long it was tried, and that refusal is retryable: the same command, once the file is free |
| `update_nothing_to_roll_back` | there is no record of an update, or the executable it replaced is no longer under `~/.jason/update/previous/`, or, run as the `jason` Jason Desktop carries, the update stands before `kept`, or at `kept`, and the application is still the version it began from: nothing was put aside yet, or nothing is to be put back. Where the application names the new version already, the refusal is `update_in_progress`, and `jason update apply` finishes the update first |
| `update_cancelled` | a carried `jason update apply` was cancelled — Ctrl+C, a SIGTERM, its console closed — while it waited for Jason Desktop or before the go. The record that stood is put back, on Windows a `jason` is put back at the carried path, and a runtime it stopped is launched again and not waited for, which the message says, with `jason runtime status` as the way to see it answer. Cancelled once the go was given or the installer has the update, nothing is put back or started: the application is the installer's, the runtime stays stopped until the update is finished, and the message names `jason update apply` and `jason status`; this answer is not retryable, since an apply typed while the installer still has the update is refused with `update_in_progress` (below) |
| `update_elevated` | a carried `jason update apply`, `--finish` included, was run elevated — on Windows with a token elevated above the account's ordinary one (an administrator's elevated terminal under User Account Control), or as another account than the one signed in to the session; on macOS as root — and is refused before anything is read, staged or asked: Jason Desktop, its installer and the runtime it starts again would run elevated too, or install into the profile of the account that elevated. The message names an ordinary terminal, or Check for Updates in Jason Desktop's menu An administrator whose Windows has no split token, User Account Control off as on a hosted build machine, runs with the account's ordinary token and is not refused. |
| `data_dir_unreadable` | `jason update apply --finish` could not list the data directory, or its `logs/` or `update/`, so whether an installation is there cannot be told: it is refused before the update's lock, nothing is made and no release read. Worth trying again once the directory can be read |
| `update_rollback_unsafe` | the executable was put back and the database was not: work has been recorded since the update, or whether any has cannot be told — no runtime is answering, or the update never reached a healthy runtime and so never recorded where the chronicle stood — and restoring the backup would erase it. A version from 0.49.0 on that was put back does not start on the database the newer one migrated (`data_newer`), and the message says so. "Restoring a database by hand" below is how to go the rest of the way, having decided you want to |
| `update_ledger_invalid` | there is a file at `~/.jason/update/ledger.json` and it is not a ledger this build can act on. The message names the file. `jason runtime start`, `restart` and `run` refuse with it too and start nothing, as they do when the file cannot be opened at all, and `jason status` fails its `runtime` check with the same words; removing the file is the way on when no update is wanted |

### Handing the update to Jason Desktop

Where `jason` is the one Jason Desktop carries, the application is updated by the Desktop's own updater, and a terminal
hands it the work rather than replace a file that belongs to the application. This section is the contract between the
two halves: the starts of the application the carried `jason update apply` makes, the file it reads the answer from, and
the exits. Both halves are described as they behave.

**Which file is started, and how.** On Windows `Jason.exe`, two folders above `resources\bundle\jason.exe`; on macOS
`Jason.app/Contents/MacOS/Jason`, three folders above `Contents/Resources/bundle/jason`. It is started as every child of
`jason` is, with the verb's own environment: the same `JASON_DATA_DIR`, so the start acts on the same data directory and
meets the same Desktop. Nothing goes to it on standard input and nothing comes back on standard output; the request file
is the answer. The verb starts the application first, before it drains or stops anything, and the application waits,
once the update is downloaded, for the verb's go: only then is the runtime drained and stopped and, on Windows, the
carried `jason.exe` moved out of the folder, because the Windows installer refuses while any process runs from it.

**On Linux, an AppImage.** The `jason` a terminal runs there is the copy the application made of what it carries, under
`<data>/desktop/bundle/<version>/`, and the file started is the AppImage itself: the one each start of the application
records in `<data>/desktop/appimage.json`, `{"file":"<full path>"}`, or the install line's place,
`~/Applications/jason-desktop-linux-x64.AppImage`. It takes the request as the macOS application does, reading
`latest-linux.yml`; its updater replaces the file where it is, and what it keeps under `<data>/desktop/rollback/<from>/`
is the AppImage file. At `swapped` the copy of what the new file carries is read out of it, by the file's own runtime,
under the new version, whole or not at all, and that copy's `jason update apply --finish` finishes the update; its `jason
install` moves a logon registration that names another version's copy to itself. Electron needs a display even with no
window, so from a terminal with no display (an ssh session, a service) the verb refuses with `update_carried_by_desktop`
before anything is read. The deb takes no request: its `jason` refuses the update with
`update_managed_by_package_manager` (above), and its application answers a request `unsupported_platform`.

| Argument | What it says |
|---|---|
| `--update-apply` | update the application to one version, with no window |
| `--to <version>` | that version, as the feeds name it: `0.49.1`, or with a pre-release part, `0.49.1-ci.7` |
| `--request <id>` | the request's id, made by the verb afresh for each request: letters, digits, `-` and `_`, at most 64, never a path |
| `--await-go` | optional: once the update is downloaded, write `downloaded` and wait for the go (below) before anything is stopped, kept or installed. Only a request that names it waits; given twice, or with `--update-settle`, it is a usage error |
| `--feed <address>` | optional, and taken only by a start of its own: read the Desktop's update file (`latest.yml`, `latest-mac.yml`) from this address instead of the releases. Only `http` on the loopback address is taken (`localhost`, `127.0.0.0/8`, `[::1]`), with no credentials, query or fragment: it is for a test's own server. What the address binds is below |
| `--update-settle` | alone: record the update as kept, once the new version's runtime answered (below) |

**What the start does.** It shows no window, and on macOS no Dock icon: the activation policy is set to `accessory`
before Electron is ready. It reads the file that records whether the Desktop opens on Live or on the Demo and is not
ruled by it, and writes neither record. It takes the single-instance lock of its data directory. Where a running Jason
Desktop of that data directory holds the lock, Live or Demo, the request goes to that one in the lock's additional data
and the start exits 3; otherwise it takes the request itself. Whichever process takes it reads the feed; refuses unless
the version the feed's file names is exactly `--to`; downloads it; where the request names `--await-go`, writes
`downloaded` and waits for the go, and past its wait installs and stops nothing; stops the runtime the application
carries when that is the one running, through `jason runtime stop`, as a click on the menu's install item does; keeps
the application aside under `<data>/desktop/rollback/<from>/`; and hands the update to the installer with no start of
the application after it: on Windows the installer runs as `--updated /S`, and on macOS the application quits once
Squirrel has the update, and ShipIt installs it after the application has ended. On Windows an installer that refuses —
the application still running a minute after it was told to quit, a Jason process still found at the carried path, or
the old version's uninstaller refusing to let go of its files — replaces no file, ends with a code other than 0 and, run
silently, shows no window, so it leaves the old version installed and working. On the terminal's update nobody reads
that exit: `jason update apply` sees a setup that ended 3, Jason Desktop still running, only as an application whose
`version.json` did not name the new version within its ten-minute wait, after which, the old version still named, it
puts a `jason` back at the carried path and the record that stood, starts the runtime again where one was running
and answers `update_desktop_not_installed` (below). The Desktop's start never starts a runtime, and touches
no plugin, skill or UI: those are the new carried `jason`'s to finish. A Demo window holds no updater: it resolves no host
name, so it could read no feed, and it refuses the request as `demo_open` without checking anything itself. The
terminal's `jason update apply` has read the feed before it hands anything over, so where the feed offers nothing newer
the answer is the ordinary one, `update_up_to_date`, and where it offers a newer version the Demo refuses it and nothing
is stopped, kept or downloaded; leaving the Demo, or quitting Jason Desktop, and running the verb again takes it. On Windows a start of its own sets its `ComSpec` to the system's `cmd.exe` and puts the system's
folders first on its `PATH`, the PowerShell folder among them, before it reads the feed: the updater checks the
installer's publisher by running PowerShell through that shell, and lets the check pass where PowerShell cannot be run
at all, so the shell and the PowerShell are found where the system keeps them rather than where the caller's `PATH`
says. The working directory stays the caller's, and the command interpreter looks there before `PATH` unless
`NoDefaultCurrentDirectoryInExePath` is set, so the start sets it. The system's folder is the one `SystemRoot` names,
and that value is the caller's too: what is closed is the working directory and the order of `PATH`, not a `SystemRoot`
the caller pointed at a folder of its own. Where `SystemRoot` is not a drive's absolute path nothing is changed.

**What a feed binds, and what it does not.** The address binds where the feed's file is read, and nothing more. The files
that file names may be absolute addresses on any host, and a redirect is followed to any host, so the package may come
from anywhere the file or a redirect says. The version compared with `--to`, and refused where it is older than the one
running, is the file's own word: nothing reads the version of the package itself. The one check on the package is the
publisher's signature: on Windows the updater refuses an installer not signed by the publisher the application carries in
its own `app-update.yml`, never one a feed names; on macOS Squirrel refuses a bundle whose code signature does not satisfy
the running application's. A package signed by the publisher of any version therefore passes, a pre-release included: a
feed is for a test's own server, and only a start of its own takes one.

**A window that takes a request** goes as far as the download and stops there: the update waits under its menu's install
item, nothing is stopped or kept, and the request is refused with `window_open`. One switch in the Desktop,
`windowOpenUpdate`, decides whether an open window may instead close to install, silently and without starting again; it
is set to stop. Where the switch lets a window close to install and the request names `--await-go`, the window waits for
the go as a start of its own does, its install item at downloaded the while, and past the wait refuses with
`go_timed_out` and leaves the update under that item; a start of its own has no menu, and its sentence names Check for
Updates in Jason Desktop's menu instead. A window takes no request that names a feed: it refuses it with
`window_feed` before it checks anything, so no feed is set and nothing is downloaded, and its install item only ever
offers what the releases offer.

**The request file** is `<data>/update/desktop-<id>.json`. The process that takes the request makes it, with its first
step, only where no file of that name is there, and only that process writes it after; a request whose file is already
there is not taken, and nothing is written over that file; a request whose file cannot be made (`<data>/update` is a
file, say) is refused as `failed` and taken no further, with no file to answer it. A `busy` or `usage` refusal is written the same way, so it is
never written over the file of a request under way with the same id. Each step is written before it is taken, by a file
renamed over it, so a reader meets no file, an empty one, a whole one or the one before; on Windows a read that meets the
rename is tried again. On Windows a rename over the file is refused while the verb's read holds it: the Desktop tries it
again for half a second, and then writes the step in place, which a read may meet half written and takes for no answer,
the next poll reading it whole; the rename stays the normal way, and on macOS it is the only one. Two steps are ones the
verb acts on, `downloaded`, after which it gives the go, and `installing`, before which it lets the installer have the
application, and neither is passed over unwritten: where one is written neither way, the Desktop goes no further, waits
for no go and starts no installer, and ends the request `failed` where that can still be written, else with the file at
the step before and the verb given no answer; and where the runtime was stopped for it, which the verb then never learns,
the Desktop starts it again itself at once, as it leaves that to the verb wherever the file says `runtime_stopped`. A lost `checking` or `downloading` only says less. The verb makes a fresh id for each request and reads a file that was there before it started the
application as no answer. Every member is always there:

| Member | What it holds |
|---|---|
| `request` | the id |
| `state` | `checking`, `downloading` or `downloaded` while the request is taken; `installing`, `current` or `refused` at its end |
| `from` | the application's version that took the request |
| `to` | the version asked for; null only when the arguments could not be read |
| `reason` | at `refused`, one code of the table below; null otherwise |
| `message` | at `refused`, a sentence for the person; null otherwise |
| `offered` | the version the feed offered, where it was read |
| `window` | whether a running window took the request |
| `runtime_stopped` | whether the runtime the application carries was stopped for it; nothing in the Desktop starts it again |
| `kept` | the copy of the application kept aside, where one was |
| `updated_at` | when this step was written, ISO-8601 UTC |

| State | Meaning |
|---|---|
| `checking` | the feed is being read |
| `downloading` | the feed offered `to`, and it is being downloaded |
| `downloaded` | the request names `--await-go`, the update is downloaded, and the go is waited for; nothing has been stopped or kept |
| `installing` | the runtime was stopped where it ran from the application, the copy kept, and the installer handed the update; the Desktop's part is over, and the carried `version.json` naming `to` says the install finished |
| `current` | the application already is `to`; nothing was done |
| `refused` | nothing was installed, for `reason` |

| Reason | What happened |
|---|---|
| `usage` | the arguments could not be read; written only where the id could be |
| `not_installed` | a development build, which no installer updates |
| `unsupported_platform` | not Windows, macOS or an AppImage on Linux: the deb's updates are the system's |
| `window_feed` | a window took a request that names a feed; nothing was checked, set or downloaded |
| `demo_open` | a window that shows the Demo took the request; it resolves no host name, so nothing was checked, stopped or downloaded |
| `updater_inactive` | the updater cannot replace the application where it is installed |
| `busy` | the Desktop was already checking for, downloading or installing an update, or taking another request |
| `version_rejected` | `to` failed its first start on this machine before |
| `check_failed` | the feed could not be read |
| `version_mismatch` | the feed offers another version than `to`, or none newer; `offered` names it |
| `download_failed` | the download did not arrive |
| `window_open` | a window took the request and may not close: the update waits under its install item |
| `go_there` | the request names `--await-go` and its go was there when the request was taken, or the go found is dated before the request's own `downloaded` write: a go the verb did not make is no go, and nothing was stopped, kept or installed |
| `go_timed_out` | the request names `--await-go` and no go came within the wait: nothing was stopped, kept or installed; a window keeps the download under its menu's install item |
| `runtime_not_stopped` | the runtime the application carries did not stop, or did not answer while its process runs |
| `keep_failed` | the copy could not be kept; `runtime_stopped` says whether the runtime was stopped first |
| `install_failed` | the installer stopped before it finished, at once or, on macOS, when Squirrel said so later |
| `timed_out` | a start of its own did not end within 30 minutes, the install included, or 45 where it waited for a go |
| `failed` | an error the Desktop did not expect, or a `downloaded` or `installing` it could not write, so it went no further; `runtime_stopped` says whether the runtime was stopped first |

| Exit of `--update-apply` | Meaning |
|---|---|
| 0 | the request file names the end: `installing`, `current` or `refused`. While the installer has the update, the start ends as the updater quits it, and on Windows the installer may end it first, so any exit after `installing` is read the same way |
| 1 | the start ended in a way it did not expect, or its bound passed (`failed`, `timed_out`, where the file could be written), or the request's file could not be made, so nothing was taken or written |
| 2 | the arguments could not be read (`usage`, where the id could be and no file of that id was there), or the request's file was already there, so nothing was taken or written |
| 3 | a running Jason Desktop of this data directory took the request; that one writes the file |

**The go** is `<data>/update/desktop-<id>.go`, an empty file beside the request's. The verb makes it by an exclusive
create, as the last thing it does before the install, after the drain, the stop, the move and the record below; a go
that is there already is no go, on both sides: the verb gives none and ends with `update_desktop_unanswered`, and a
request whose go is there when it is taken is refused with `go_there`, as is one whose go is dated, by the file system,
before the request's own file said `downloaded`: the verb makes its go only after it read that, so an older one is a go
planted while the update downloaded. The two waits are held apart by a margin: the
verb gives the go within 10 minutes of reading `downloaded`, the drain at most 8 of them, and past that bound ends
having given none; the Desktop waits 15 minutes for it (`goWaitMs` in `desktop/src/update-request.ts`), five more than
the verb's bound, and past that installs and stops nothing and refuses with `go_timed_out`, so a go never arrives after
the Desktop stopped waiting for one. The verb waits for the application's start up to 50 minutes: the start's own 45 and
five more. The verb takes the go away once the hand-over has ended.

**What the verb does with it.** The update is one ledger (above), its steps read for this hand-over. On Windows as on
macOS the caller waits on the process it started, `jason update apply`, and gets one JSON document and the exit code:

| Step | What is begun |
|---|---|
| `staged` | the plan: the feed's `manifest.json` read and held to this installation — the version, `--version`, `min_upgrade_from` — and no archive asked for, since the application is the Desktop's to download; the record that stood is kept beside the ledger, for a refusal to put back |
| `staged`, then `asked:` | the request: the application started with `--update-apply --to <version> --request <id> --await-go`, and `--feed` with the feed's folder where the feed is `http` on the loopback address, which is all the Desktop takes. Nothing is drained or stopped until it answers `downloaded`; an end before that leaves the runtime untouched |
| `drained` | at `downloaded`, `system.drain`, as any update's drain, within the go's bound |
| `stopped` | on Windows the verb stops the runtime itself, as any update's `stopped` does, and a runtime an earlier version's Demo left running on its own folder beside the data directory or the harness root, which runs the carried `jason` too; then it steps out of the folder (below) and gives the go, under a `stopped: Jason Desktop told to go` line. On macOS the go follows the drain, and the Desktop's start stops the runtime the application carries, through the carried `jason`, as a click on the menu's install item does. A Demo runtime is the Desktop's to start again, at its next Demo start |
| `kept` | the answer `installing`: the copy of the application the Desktop kept, recorded as `kept_application`. On Windows that copy was made while the carried file was out of the folder, so the verb puts a copy of its own file at `resources\bundle\jason.exe` in it |
| `swapped` | the application's `version.json` names the version: the new carried `jason` is run as `update apply --finish --feed <feed>`, a child the verb waits for, and its answer passed on, its lines after the verb's own |
| `plugins` to `complete` | the new carried `jason`, as any update's; then the skills this installation recorded redeployed at the new tag, the role skills among them; `jason install`'s machine setup with no `--roles`, under `install:` lines — the terminal command and the harness skills, which never fail the update and end `run: jason install` where they did not get there; the UI; and the application started with `--update-settle`, under a `settle:` line |

**Out of the application's folder.** The Windows installer refuses while a process runs from the carried path — the
installer's look finds one by that exact path (`desktop/installer/uninstall.nsh`) — or holds a file in the folder, and
on macOS the application is replaced under a running process. So the verb steps out of the folder before the application
is told to go. On Windows, before the drain, the verb looks for every process that runs from the carried path and
refuses, naming each by its id, where one is neither this data directory's runtime nor a Demo runtime, both of which it
stops: with neither up it looks before the Desktop is asked, and otherwise once both are stopped, waiting up to fifteen
seconds for a stopped runtime's plugin hosts to end before it refuses. A carried `jason update apply` run elevated is
refused before any of this with `update_elevated`. On Windows the carried process moves its own file, as its last act before the go, to
`%LOCALAPPDATA%\jason-desktop-updater\jason-carried\<request>\jason.exe`, on the application's volume and outside its
folder; a move that leaves the file where it was and copies it, as across two volumes
behind one drive letter, is taken back and refused as `update_file_refused`. Beside the moved file it writes
`jason.version`, the version the file came from alone, as the application's `version.json` names it, with nothing after
it, so that a later start of the application can tell whether the folder is still that version. It goes on running from
there: Windows lets a running image be renamed, as `jason uninstall` does for its own removal (§9). Windows still
reports the path the process was started from, so beside the carried file it writes `jason-update.pid`, its process id
and its start in Windows file time, and the installer's look passes over the one process where both match a process that
is running and whose command line carries ` update apply`; every other process at that path is looked for as before, and
a file left behind names a process that has ended. Only the installer the updater starts with `--updated`, and the
uninstaller it runs, read the file; a person's install or uninstall reads none, whatever it was started with. The verb
takes the file away once the install has ended. Folders earlier requests left in the store are swept, quietly and only
where nothing runs from them, at the next move, by `jason update apply --finish` with no update in flight, and by
`jason install` where no update is in flight. A line `moved:` says where the file went. On macOS a copy of the carried `jason`
goes under `~/.jason/update/applier/` after `staged` and carries the steps from `drained` to `swapped`; the carried
process waits for it and passes on its answer and its exit.

**Every way it ends leaves a working installation.** Every way out after the request is in one place: where the
installer put no new `jason.exe` at the carried path, the verb puts one back on Windows before it answers — a copy of
its own file, under a `put back:` line, never into an application whose `Jason.exe` is gone, as an uninstall leaves it —
puts back the record that stood where nothing was installed, and starts again the runtime it stopped:

| How it ends | What is at the carried path when the verb answers |
|---|---|
| before `downloaded`: the Desktop refused, a failed download or `go_there` among the reasons, or its start ended with no answer (`update_refused_by_desktop`, `update_desktop_unanswered`) | the carried file, never moved; nothing was drained or stopped |
| the drain, the stop, the move or the record did not end within the go's bound, or was refused (`update_runtime_unreachable`, `update_file_refused`), a move that copied among them; no go was given | the carried file where it was, or a copy of the verb's own file; a drained runtime claims work again |
| a go was there before the verb made one (`update_desktop_unanswered`); no go was given | a copy of the verb's own file |
| after the go, the Desktop refused or never answered (`update_refused_by_desktop`, `update_desktop_unanswered`) | a copy of the verb's own file |
| the installer was handed the update and the application never named the version (`update_desktop_not_installed`) | the installer's file where it placed one, never written over; otherwise a copy of the verb's own file |
| a cancel — Ctrl+C, a SIGTERM, a console closed — before the installer has the update (`update_cancelled`) | a copy of the verb's own file, or the carried file where it was never moved; the runtime launched and not waited for |
| a cancel once the installer has the update (`update_cancelled`) | what the installer leaves: the folder is its own, nothing is copied in and no runtime started, and the answer names the finish |
| the install landed | the new `jason.exe`, which finishes the update |

A cancel is held: the verb lets ten seconds pass for its way out before the process ends, and a closed console four, of
the five Windows gives it. A child the verb waits on — the copy on macOS, the new `jason`'s finish on either system — is
given six seconds of that after a cancel to end by itself — two after a console's close, so that the verb's own way out
still runs within the close's four — since a terminal's Ctrl+C or hang-up reaches the copy too, and
the finish, which has no console, may end the step it is in; a child that answered within them is passed on as it
answered. Only then is it ended, and the carried process runs the same way out by the same rule: the application the
installer has is left to it, so with the go given, or the ledger at `kept`, and the application not yet the new version,
nothing is put back and no runtime is started, the old build under the installer least of all; otherwise the record that
stood is put back and the runtime launched from the application's `jason`, and not waited for. The copy records its
request's id beside the ledger, `<data>/update/desktop-request.id`, so the carried process can read whether the Desktop
was given the go or has the update, and takes away then the request files and goes of every earlier request, which nothing
reads once a newer one is recorded; after the go the verb, and the copy, read the request's file once more on the way out
too, so a cancel in the moment between the Desktop writing `installing` and its being read ends no installer's start.

**While the installer has the update, the runtime stays down.** This is the one way out where the rule above yields: a
runtime started then would run the old build, from a folder the installer is replacing. The runtime stays stopped until
the update is finished, and three doors finish it once the application is the new version, each starting the runtime:
`jason update apply` typed again into the carried `jason`; `jason status`, which names an update that landed and was not
finished with that one command; and the first start of the new Jason Desktop, which finishes an update a record still
holds where the application is the version it names.

**What a kill leaves.** A kill of the verb's own process — a task manager, a power cut — runs none of the above:

| The verb killed | What is left | The way on |
|---|---|---|
| before `downloaded` | nothing drained, stopped or moved; the record at `staged`. The Desktop's start downloads, waits for a go that never comes, and installs nothing; it holds the single-instance lock of the data directory the while, up to 45 minutes, and a person's own start of Jason Desktop then ends without a window | `jason update apply` again |
| during the drain, which can take up to eight minutes of the go's ten | the runtime drained, claiming no work, until something resumes it; the record at `drained`. No go comes, so the Desktop installs nothing | `jason runtime resume` has the runtime claim work again, or `jason runtime restart` starts it afresh; then `jason update apply` again |
| between the stop and the go | the runtime stopped, the record at `stopped`; on Windows, once the move is made, the application's folder without a `jason.exe`. No go comes, so the Desktop installs nothing | on Windows, Check for Updates in Jason Desktop's menu or its installer run again puts a `jason.exe` back, and the moved file is whole in the store, `jason.version` beside it, to be copied back to `resources\bundle\jason.exe` by hand; then `jason update apply` again. The steps from the stop to the go take seconds, and this is the one gap |
| after the go | the installer has the update; the record at `stopped` or `kept`, and no runtime started | `jason status` names the update the installer landed and the finish as its repair; `jason update apply`, typed into the new carried `jason`, finishes it and starts the runtime |
| after the go, and the installer then fails | the application still the old version, the record at `kept` (or `stopped`), no runtime, and `jason runtime start` refused from `kept`; on Windows no `jason.exe` at the carried path, its image in the store | Check for Updates in Jason Desktop's menu offers the version again and installs it, and the update is then finished as above; or, on Windows, the moved file copied back from the store by hand, `jason.version` beside it naming the version it came from, and `jason update apply` again |

The request id is made afresh for each request, and a file of that id there before the request is no answer. The verb
reads the file while the application's start runs, and waits for that start up to 50 minutes, then reads the file,
whatever the exit. After exit 3 it reads the file again until it names `downloaded` or an end, and after the go an end,
since a window takes the request at its own pace: a window that failed before it served the request never answers, so
each wait is bounded — two minutes for the file to appear, 30 for what it waits for — and the refusal says so. One
`jason update` of a data directory acts at a time: each part — the carried process's plan and hand-over, the copy's, the
finish, a carried rollback — takes `<data>/update/update.lock`, and a second one beside it refuses with
`update_in_progress`. The lock is the file held open for that process alone, not the file's being there: the operating
system lets it go when its holder ends, by a kill as much as by an exit, and a lock file a dead process left refuses nobody.
Each holder names itself beside it in `update.lock.holder` — `update` or `install`, its process id and a nonce — and a
refusal says by that line what holds the lock. The lock is never let go between two parts: a part that runs the next as a
child it waits for keeps it and hands the nonce to the child alone, in `JASON_UPDATE_LOCK` — never on the command line,
and taken out of the child's own environment once read, so nothing it starts, the runtime above all, carries it; a child
whose nonce is the one written there goes on under the lock it was handed, and nothing else does. `jason install`'s
machine setup takes the same lock where the data directory has an update folder, and refuses with `update_in_progress`
while an update holds it: the installer runs `jason install` once its files are in place, which is when the verb's finish
begins to set the machine up too, and the two never write the Path entry and the skills at once; the update's finish sets
the machine up itself. While `jason install` holds it, an update, its finish and a rollback refuse with
`install_in_progress` instead, retryable, naming the setup's process: the setup takes seconds. Two edges are known and
left open: a child still running when the process that handed it the lock is killed goes on under a lock no one holds,
the one moment a second update can begin beside it; and the lock is the data directory's, so an installer started from
Jason Desktop's menu, which runs `jason install` on the default data directory, does not take the lock of a data
directory named only in a terminal. At `refused`, and with no answer, it ends with `update_refused_by_desktop`
or `update_desktop_unanswered`, puts back the record that stood, and leaves the runtime as it found it before the drain:
a runtime that answers drained claims work again, and one that was running and is gone — the Desktop or the verb stopped
it — is started again from the carried file. At `current` it goes on to `swapped`. At `installing` it waits up to ten
minutes for the carried `version.json` to name `to`; where it does not, the refusal is `update_desktop_not_installed`,
and on macOS it carries the end of ShipIt's own log,
`~/Library/Caches/ai.jasonharness.desktop.ShipIt/ShipIt_stderr.log`. Whoever asked for a runtime's stop starts one
again: a hand-over to the copy or to the new `jason` that ends with no answer starts the runtime again where one was
running and none answers, from the carried file where its `version.json` can be read and otherwise from the verb's own
file, and says where the update stands. An update stopped part-way is carried on by typing `jason update apply` again,
into the carried `jason`, or on macOS into the copy; one whose application is already the version asked for, updated
from the window say, is finished by the new carried `jason` with no request. `--finish` while an update stands before
`swapped`, its application not yet the version asked for, is refused with `update_in_progress`; with none in flight it
is the finishing step below. A carried `jason update rollback` of an update that stands before `kept` puts back nothing,
since nothing was put aside: it refuses with `update_nothing_to_roll_back`, or with `update_in_progress` where the
application names the new version already. So does one at `kept` whose application is still the version the update began
from, the installer never having put the new one in place: there is nothing to put back by hand.

**The finishing step.** `jason update apply --finish`, run as the `jason` Jason Desktop carries with no update in flight
— the ledger absent or at `complete` — is the one step both doors use to bring what belongs to the installed version up
to it: the window's update, whose new version's start runs it, and the command line's, whose hand-over runs it past
`swapped`. It needs no Jason Desktop to answer, and a `jason` the application does not carry is refused it as a usage
error.

| Part | What it brings up to the installed version |
|---|---|
| `plugins` | the plugin packages the installed version's release publishes, by the plan an update's `plugins` step makes against that release's `manifest.json`: a package behind is replaced, and every other is left as that step leaves it — not installed, current, later, unreadable, or edited on this machine (it "differs from every package a release published") |
| `skills` | every skills deployment this installation records, redeployed at the installed version's tag, as an update's skills step redeploys them, the role skills among them; `already_there` where every record named that ref already, from a source read at a ref rather than a directory |
| `ui` | the release's UI for the installed version, where the build in place is not already that version for this build's API |

It starts and stops no runtime: where a package changed and a runtime answers, it asks the reload `jason plugin install`
asks, and where none answers the package loads at the runtime's next start. Run again it changes nothing and says each
part is there already. Where no data directory exists — a machine that only ever opened the Demo — it does nothing,
makes nothing under one, asks nothing of the network, and says so; and so it does where the data directory holds nothing
of an installation but Jason Desktop's own folder and its log, as on the Desktop's first start. That is the one
definition of an installation being there, and `installation_found` says it: the Desktop's own folder, its
`logs/desktop-*.jsonl`, an `update/` holding nothing but the update's lock file and the note of its holder, which this
step makes for itself and a holder that was killed leaves behind, and which never make a machine installed, and the system's own litter (`.DS_Store`, `desktop.ini`, `Thumbs.db`) at the
top and in `logs/` are no installation; anything else is one, a record under `update/` among it. A data directory it
cannot list is refused with `data_dir_unreadable` before anything is made or read, never taken for either answer.
Wherever an installation is there it runs under the update's lock, making `<data>/update` for it where that is not
there yet. Where the release cannot be read, it says what it
could not do and changes nothing of that part. It exits 0 with one document whenever it could run, and the document says
what became of each part:

```json
{
  "version": "0.49.0",
  "data_dir": "/Users/you/.jason",
  "data_dir_found": true,
  "installation_found": true,
  "complete": true,
  "parts": [
    { "part": "plugins", "state": "done", "reason": null, "lines": ["reply 0.17.0 -> 0.18.0", "reload: the runtime loaded the packages"] },
    { "part": "skills", "state": "already_there", "reason": null, "lines": ["nothing on this machine was deployed by jason skills, so nothing was redeployed"] },
    { "part": "ui", "state": "done", "reason": null, "lines": ["placed 0.49.0"] }
  ]
}
```

| Member | Meaning |
|---|---|
| `version` | the installed version, the running `jason`'s own |
| `data_dir` | the data directory the parts belong to |
| `data_dir_found` | whether it exists; `false` means nothing was done or made, and every part is `not_done` with the reason saying so |
| `installation_found` | whether an installation is there; `false` where no data directory exists or it holds nothing but Jason Desktop's own, as above: nothing was done, read or made, every part is `not_done` with the reason saying so, and nothing is left to finish; `true` in every other answer |
| `complete` | whether every part is `done` or `already_there` |
| `parts` | `plugins`, `skills`, `ui`, always in that order |
| `parts[].state` | `done` — it changed something and is the installed version's now; `already_there` — nothing needed changing, or nothing of it is installed; `not_done` — it could not be brought up, and nothing of it was left half changed |
| `parts[].reason` | at `not_done`, one sentence why; `null` otherwise |
| `parts[].lines` | what the part did and left, one line each, as an update's step lines say them; a line that left something to do ends with `run:` and the command that does it |

With `--human` it prints one line a part, its state and any reason, and the part's lines under it.

Jason Desktop's start on macOS and Windows runs it right after `jason install`, at the first start of a version, and
records that version as done only where both succeeded ([desktop.md](desktop.md), "The machine set-up at a start"). Where
a carried update is in flight past `swapped`, or the application is already the version it names, the same call finishes
that update and starts the runtime, the third door above, and prints the update's own document; the Desktop takes it as
done where its `step` is `complete`. To that start `install_in_progress` and `update_in_progress` mean later: nothing is
recorded, it is no failure, and the next start runs both again.


**`--update-settle`** records the update that installed the version it runs as kept, as a window's first start would:
the copy stays at `kept` until the next update, and the version is not refused. It settles only that update and only
once. It takes the single-instance lock of its data directory, so it never writes the record while a window settles its
own start; where another Jason Desktop holds the lock it exits 0 having written nothing, and the record is settled by a
Live window's start, as it is after an update from the menu. A Demo window and a start that takes a request do not settle
it: it stays pending until the next Live window starts. It shows nothing and brings no window forward, and exits 0, or 1
when the record could not be written. An update whose runtime did not come up is not settled: the next Live window says
where the copy is. Putting the previous application back is one step by hand, as it is after an update from the menu.
The verb waits for the settling start at most two minutes, ends it past that and says so in its `settle:` line: the next
Live window's start records the update then.

### Moving a command-line install into Jason Desktop

From 0.49 on, Jason on macOS and Windows is Jason Desktop and the `jason` it carries, and on Linux from 0.49.1. A
command-line install of an earlier version reaches it through its own update: `jason update apply`, typed into the 0.48 install, puts this version
at the install's path and hands the rest of the update to it, and that `jason`, once the update is complete, moves the
install into Jason Desktop. Its 0.48 parent reads what it prints as it reads every handed-over answer and never changes,
so the move is entirely the new version's.

**It never fails the update.** The update is complete before the move begins, and whatever the move meets, the answer
is the update's success: one document whose `step` is `complete`, its own lines in `steps` with the move's after them,
each beginning `move:`, and, apart, where the move stands:

```json
{
  "from": "0.48.2",
  "to": "0.49.0",
  "step": "complete",
  "steps": ["...", "complete: 0.49.0", "move: installed Jason Desktop 0.49.0 to /Users/you/Applications/Jason.app", "move: done: /Users/you/.local/bin/jason is in Jason Desktop at /Users/you/Applications/Jason.app"],
  "move": { "state": "done", "step": "started", "application": "/Users/you/Applications/Jason.app", "reason": null }
}
```

| `move.state` | Meaning |
|---|---|
| `done` | the install is retired into the application, and a runtime it stopped runs again from the application's `jason` |
| `pending` | the move waits for something outside it, and nothing was changed that a later run cannot carry on; `reason` says what and how |
| `failed` | a check refused or a step did not succeed; nothing of what it refused ran, and the command-line install answers as it did |
| `not_applicable` | a system other than macOS, Windows and Linux: the command-line install is the installation there, and nothing was moved or written |

`move.step` is the last step done, `application` where the application is or goes, and `reason` one sentence, `null`
at `done`. The same is kept in `<data>/update/move.json`, with the step being done while one is, written before it
begins; `jason status` reads it as its `move` check, which names a move that has not ended and the command that carries
it on: `jason update apply` from the command-line install, which at the version it already is carries the move on rather
than refuse with `update_up_to_date`, and answers with `from` and `to` both that version. A move that is `done` is not
carried on again, and a command-line install the application's routine retired is reported as moved, whatever the
record says. Where the move waits on what no command typed here changes — an Intel Mac, an application older than 0.49 or
one of no readable version, on Linux an AppImage older than this version or one whose version cannot be read, a system
that refuses unprivileged user namespaces or has no setuid `fusermount` — the check says what stands and what would move
it, with no repair. Each step, and a running
download every fifteen seconds, says so in one line beginning `move:` on standard error, which the 0.48 parent passes
through as it comes; the document is untouched by them.

**The steps**, each recorded before it is taken, after the folders an earlier routine renamed a still-running executable
into are removed where their process has ended:

| Step | What it does |
|---|---|
| `looked` | on Windows, first, this jason's own Authenticode signature, verified by the routine that verifies the installer: a jason with no signature, or whose signature does not verify, moves nothing and fetches nothing, and the move is `failed`, saying to install Jason Desktop from the release page, whose first start takes the command-line install in; `jason status` names no repair for it. A test build takes the signer `JASON_TEST_BUILD_SIGNER` names before its own. Then, on Windows, while the installer this move downloaded still runs, left by an earlier run past its bound or after a kill, nothing at the place is used, reused or installed over, and the move is `pending` until that installer has ended. Otherwise an application is looked for: on Windows in `%LOCALAPPDATA%\Programs\jason-desktop`; on macOS at `~/Applications/Jason.app`, then `/Applications/Jason.app`. One of 0.49.0 or later is reused as it is once it verifies — its signature on macOS, its jason's signer on Windows — and the next three steps are taken as done. An application is never replaced, renamed or closed from a terminal: one whose window is open, of any version, leaves the move `pending` until it is closed; one older than 0.49 leaves it `pending`, saying to update Jason Desktop from its own menu, whose first start of 0.49 or later takes the install in; one of no readable version, `pending`, saying to run its installer from the release page again — except on Windows the one a setup this move began there left, which is set up again where nothing runs from it. On Linux the form follows the install line's rule: an AppImage already at `~/Applications/jason-desktop-linux-x64.AppImage` stays the installation, apt or not; otherwise the deb where there is apt, and the AppImage where there is not. Jason Desktop running leaves the move `pending` until it is quit. The package `jason-desktop` installed at this version or a later one, or an AppImage there of this version or later, its version read out of it by its own runtime and the copy of its `jason` made, is reused; an older AppImage, or one whose version cannot be read, leaves the move `pending`. An older package is installed over by apt, as the system upgrades any package. Where the AppImage is to be placed and the system refuses unprivileged user namespaces or has no setuid `fusermount`, the move is `pending` before anything is downloaded, saying why |
| `fetched` | the release of this version read again, and refused where it names another; `checksums.txt` and the application's asset downloaded under `<data>/update/move/`: `jason-desktop-windows-x64-setup.exe`, `jason-desktop-macos-arm64.zip`, `jason-desktop-linux-x64.deb` or `jason-desktop-linux-x64.AppImage`. A download is given 30 minutes whole and never two without a byte; one that does not arrive is `pending` |
| `verified` | the SHA-256 against its line in `checksums.txt`, then the publisher: on Windows the installer's Authenticode signature, valid and naming the publisher that signed this jason, the signer's simple name in full and in the same case, read from this jason's own signature at the first step; on macOS `codesign --verify --deep --strict` with the designated requirement `install.sh` holds it to, on the application unpacked from the zip, before anything of it runs; on Linux the SHA-256 alone, since neither Linux package is signed. A refusal is `failed`, and the download is removed |
| `placed` | Windows: the place looked at again, then the setup run with the raw command line `/S /D=<place>`, the place last and unquoted as NSIS takes it, spaces included, with no window, and waited for at most ten minutes; one that has not ended then is left running and never killed, and the move is `pending`. Its exit 1, a Jason process still running from the place or a look for one that failed and could not tell, is `failed`; so is its exit 2, which it ends with before Jason Desktop is installed, either because another installer of Jason Desktop holds the installer's mutex (the template's `.onInit` aborts) or because the installed version's uninstaller refused and was left as it put itself back (the include's uninstall-check hooks); the sentence names both and says to run `jason update apply` once no other installer runs and Jason Desktop is quit; its exit 3, Jason Desktop running in its window or the tray, or a look that could not tell, on which a silent installer kills nothing and changes nothing, is `pending`, saying to quit Jason Desktop if it runs and run `jason update apply` again. macOS: the zip unpacked with `ditto -x -k` into a folder beside the place, so that putting it there is one rename on one volume, and renamed into the place only while it is empty; the shared folder is chosen where this account may write in it, as the updater needs, else the account's own. Linux, the deb: installed by apt with the one administrator's password, asked by `sudo` where there is a terminal and otherwise by the desktop's own dialog through `pkexec`, bounded to twenty minutes, ten more than apt waits for the package system; a password not given, no session to ask in, the dialog failing and the package system held by another install are each `pending` with their reason and nothing changed, and so is an install still going at the bound, which apt may yet finish and the next `jason update apply` carries on from; apt failing otherwise is `failed`. Linux, an AppImage: put at `~/Applications/jason-desktop-linux-x64.AppImage` by one rename, its menu entry (`TryExec=` naming the file) and icons written under `~/.local/share`, and the copy of its `jason` made under `<data>/desktop/bundle/<version>/` |
| `set_up` | the application's own `jason install --retire <the command-line install>` — on Linux run by the `jason` the deb carries or by the AppImage's copy — handed the update's lock and read by its report (§9): it places the application's `jason` in the terminal before it retires anything, stops the command-line install's runtime, leaving the note that a start is owed, and retires that install |
| `started` | where the routine stopped a runtime, or the note stands and nothing answers, one started from the application's `jason` and waited for for a minute |

**One at a time, and one stop.** The move takes `<data>/update/update.lock` and holds it to its end; a move that finds it
held is `pending` and writes nothing. It gives the setup an environment with no nonce of the lock's and with `JASON_MOVE`,
the marker its record keeps from one run to the next while it stands not done. The installer's own last step, `jason
install` with no flags, finds that marker naming a record standing not done and does nothing — nothing placed, retired or
stopped — says so in its document and exits 0; a `JASON_MOVE` naming no such record is ignored. So the routine that runs
is the move's, once the installer has returned, and a setup the move left running, past its bound or after a kill, changes
nothing of the command-line install or its runtime: the next `jason update apply` finds the application, reuses it and runs
the routine under its own hold. The only runtime stopped is the routine's, after the download and both checks, and the
move, having asked for it, starts one again. Nothing of the move, the sweep of retired executables included, fails the
update.

**Every prefix leaves a working installation.** Until `set_up` the command-line install is untouched and answers as
`jason`, its runtime with it; from `set_up` on, the application's `jason` answers in a new terminal. A move ended at any
point, the runtime's start it owed with it, leaves the note that a start is owed, which the application's own start and
`jason status` read (§9). `jason update apply` from the command-line install carries a move on from where it stopped.

**What the move never does.** It never renames, replaces or deletes an application that is there, and never closes one;
the one exception is Linux's deb, a package of the system's, which apt upgrades from an older version as it upgrades
every package. It is `pending`, never begun, in a process `update_elevated` would refuse (elevated above the account's ordinary
rights, another account than the session's, root) and for an install a package manager made, and writes no record
there; on an Intel Mac it is `pending` at `looked`, with nothing changed.

**After a move,** the executable the update replaced stays in `<data>/update/previous/` until the next update, and
`jason update rollback` refuses, pointing to the section below, once the move is done or the command-line install is
retired, whatever the record says.

**A test build.** A CLI built with `-p:JasonTestBuild=true` takes the Windows signer `JASON_TEST_BUILD_SIGNER` names before
its own and the macOS requirement `JASON_TEST_BUILD_REQUIREMENT` names, so a CI run can move into an application it signed itself;
no released build is one, which a guard holds. A test build says so: `jason --version` writes `This jason is a test build, not a
release.` on standard error, its version alone staying on standard output; `jason runtime status --human` and `jason status`
say `(a test build, not a release)` after the version; and `system.info` carries `test_build: true`, a member a released
build's report never has.

### Going back from 0.49

`jason update rollback` does not roll Jason 0.49 back from a move of a command-line install into Jason Desktop: once the
move is done, it refuses with `update_carried_by_desktop` and points here. Going back is taking Jason off and installing
0.48.7, with the data directory kept. 0.49 adds no migration to the database and keeps the layout 0.48.7 runs, so 0.48.7
opens the same data as it is, and there is no backup to put back.

Before step 1, run `jason status`. Where it names a folder in an agent's skills folder as one "Jason did not deploy,
under a name Jason ships, left as it is", move that folder out of the skills folder before step 3 and put it back after
step 3. 0.49 leaves such a folder as it is; 0.48.7's `jason skills install` deploys its own skill over a folder of the
same name and records it as its own, and 0.48.7's uninstall removes what it recorded. A folder put back after step 3
differs from what 0.48.7 recorded, and 0.48.7 keeps a file it finds changed since it wrote it.

1. Quit Jason Desktop, then take Jason off in a terminal, keeping the data directory:
   ```sh
   jason uninstall
   ```
   It removes Jason Desktop, the terminal command, the logon registration and the skills it deployed (§9), and keeps
   `~/.jason` and the harness root. Where it refuses, its message says why (Jason Desktop still open, on Windows
   another `jason` still running, or on Linux no administrator's password given to take the deb's package off), and
   nothing has been removed.
2. Install 0.48.7 by its own pinned line, then open a new terminal.
   Windows (PowerShell):
   ```powershell
   & ([scriptblock]::Create((irm https://get.jasonharness.ai/releases/download/v0.48.7/install.ps1))) -Version 0.48.7
   ```
   macOS and Linux (on Linux 0.48.7's script installs the command line, as it did then):
   ```sh
   curl -fsSL https://get.jasonharness.ai/releases/download/v0.48.7/install.sh | sh -s -- --version 0.48.7
   ```
   A later 0.48 release, if the releases page shows one, is installed the same way under its own number.
3. Put back what the uninstall took and the install does not:
   ```sh
   jason skills install
   jason ui install
   jason runtime start
   ```
   and `jason runtime autostart enable` where the runtime started at logon before.

Nothing in the data directory is lost: campaigns, the journal, settings, plugins and their grants are data 0.48.7 reads
as it is. What goes is Jason Desktop: under 0.48.7 it was a separate download, and whoever wants it takes it from
[0.48.7's release page](https://get.jasonharness.ai/releases/tag/v0.48.7) after step 2; on Linux apt keeps no
previous version of the package, so the deb of 0.48.7 is taken from there too. The same three steps
also go back from a 0.49 installed fresh rather than moved. Under 0.48.7, `jason update apply` brings 0.49 back and moves
the install into Jason Desktop again, and `jason update rollback` acts on 0.49's update record, putting back the version
that update replaced over 0.48.7. Run neither until you want 0.49 again.

## 8. Starting at logon

Nothing is registered until you ask. `jason runtime autostart enable` registers **the runtime itself** to start
when this account logs on; `disable` takes that registration away, and `status` says what is registered:

```sh
jason runtime autostart enable
jason runtime autostart status
jason runtime autostart disable
```

One registration per operating system and per account, in the account's own scope, and **none of the three needs
elevation**. On Windows it is a task the Task Scheduler accepts from an ordinary prompt; on macOS and Linux it is
a file in the account's own home:

| platform | what is registered | where the document lives |
| --- | --- | --- |
| Windows | a logon task called `Jason-<SID>`, one per account and named for its SID, with an interactive logon for that account (`InteractiveToken`), registered from a task document with `schtasks /Create /XML` | `<data>/autostart/jason-task.xml`; the task itself is the Task Scheduler's |
| macOS | a LaunchAgent labelled `ai.jason.runtime`, `RunAtLoad` and no `KeepAlive`, and the `PATH` of the process that registered it under `EnvironmentVariables` | `~/Library/LaunchAgents/ai.jason.runtime.plist` |
| Linux | a `systemd --user` unit, enabled into your default target, which starts only while the `jason` it runs is there (`ConditionPathExists=`), so once a removal Jason did not make took that file, a package purged or an AppImage deleted, it is skipped at logon rather than failed | `~/.config/systemd/user/jason.service` |

**It is registration, not supervision.** What starts at logon is `jason runtime run --detached --data-dir
<the directory you registered under>` — the runtime itself, in the background, on the data directory the
registration carries — and nothing watches it afterwards: a runtime that stops stays stopped until somebody
starts it. There is no restart policy, no `KeepAlive`, and no second process whose job is to keep the first one
alive.

**`enable` starts nothing now, and `disable` stops nothing.** They change what happens at the *next* logon.
Starting and stopping a runtime today is still `jason runtime start` and `jason runtime stop`. On macOS `disable`, and
`jason uninstall` with it, take the job out of launchd before its file goes — `launchctl unload -w` on the file where it is
there, then `launchctl remove ai.jason.runtime` by its label, which holds for a job whose file is gone already, each
waited for ten seconds at most and a job not loaded no error — so no job is left loaded with its plist gone. `-w` records
the label as disabled, which keeps a file left behind from loading at the next login. `enable` first reads launchd's record
of the label in this account's two domains, `launchctl print-disabled gui/<uid>` and `user/<uid>`: where one records it as
disabled, or one cannot be read, it loads with `-w`, which clears that record; where none does, it loads without `-w` and
writes none. What loads the job at each login is its file in `~/Library/LaunchAgents`, with `RunAtLoad` and no `Disabled`
key, not a record. `jason uninstall` leaves no such mark: it unloads without `-w`, so no login job of Jason's is left, and
none is left disabled. It reads the same two records, and where one records the label as disabled, as an earlier removal
with `-w` left it, it clears that there with `launchctl enable`, so that a later install's job of the same label loads;
where none does, it writes none. A cleared record stays behind as "enabled" (§9).

**The registration carries the data directory it was made under**, as `--data-dir` on the registered command
line, and `autostart status` prints it. This is the whole reason it is a verb rather than a documented
`schtasks` line: an operator working under a `JASON_DATA_DIR` who registered without it would get a background
runtime on `~/.jason` at every logon, and two runtimes disagreeing about which data is real. A second `enable`
under a different data directory **replaces** the registration rather than adding one — same task name, same
plist path, same unit name — and `status` shows which directory the one that is there names.

`status` also says whether the executable the registration names is still on disk. That is how a registration
rots: an update that moved the binary, or a directory somebody deleted, leaves something that fails at every
logon without saying anything to anybody.

That is also why, where a package manager installed Jason (§6), what is registered is the manager's own entry, which
its upgrades keep in place: Homebrew's link in its prefix, winget's link where it made one, and otherwise the
executable in winget's package directory. Never the keg a link resolves to, which the next `brew upgrade` deletes.

### Windows: an ordinary prompt, no window, and what it costs

**`enable`, `disable` and `status` all run from an ordinary prompt**, on an administrator's account or a
standard account. The task is registered for the account that asks, by its SID, and named for it,
`Jason-<SID>`: the Task Scheduler's root folder is one namespace for the whole machine, so every account on it
has its own task there, and another account's ordinary prompt can neither read nor change it. It runs with an
**interactive logon** (`InteractiveToken`) at `LeastPrivilege`: when that account signs in, the Task Scheduler
starts the runtime in the account's own session, with the rights the account already has. Microsoft's
[reference for the logon type](https://learn.microsoft.com/en-us/windows/win32/taskschd/principal-logontype) says of it: "User must already be logged on. The task will be run only in
an existing interactive session."
Jason does not check your integrity level before asking: the Task Scheduler's answer is the only thing that
really knows, and Jason repeats the tool's own line when it refuses. Every refusal names the task it is about and its
case: the task named for this account, `Jason-<SID>`, out of this prompt's reach; the task `\Jason` an earlier version left,
below; or a machine that will not register a logon task for this account, where `jason runtime start` still starts
the runtime by hand.

**No window, on Windows 11 24H2 and later.** `jason.exe` is a console program, started in a session with a
desktop. What keeps a console window off that desktop is the executable itself: its application manifest
declares `consoleAllocationPolicy` `detached`, which Windows 11 24H2 (build 26100) and later, and
Windows Server 2025, read. Microsoft's [Console Allocation Policy](https://learn.microsoft.com/en-us/windows/console/console-allocation-policy)
says that such a manifest "informs the operating system to skip allocating a console". A program that declares it
still inherits a console it is started in: typed in a terminal, `jason` uses the terminal's and behaves as it
always has; started at logon it has none.
Every program the runtime starts in turn is started with no console window of its own.

**Before 24H2, a window opens at logon and closes by itself.** Windows 10, and Windows 11 before 24H2, ignore
the setting and hand the runtime a console. `--detached` frees it as soon as the runtime starts, and the console
window closes with it. That window is conhost, or Windows Terminal where that is the default terminal. It was
measured on Windows 11 24H2 with a test program built the way Jason is built but without the setting, which
freed its console first thing: a Windows Terminal window, visible for about a tenth of a second. Jason frees its
console once it has started, so its window lasts as long as that takes. Jason itself has not been started at logon
on an older Windows. What has been run on one is the packaged `jason.exe`, on every pull request, on Windows
Server 2022 (build 20348), which ignores the setting: it starts there, and a runtime started with
`jason runtime start` logs `"Console":"freed_hidden"`, as it does on 24H2. The check fails without that line.

The runtime's log says what it found, at every start from a logon task or from `jason runtime start`. The log
under `<data>/logs/` is JSON Lines, one object per line, with the message template and its value as separate
properties, so the line to look for is the one whose `"@mt"` is `"Console at start: {Console}"`:

```json
{"@t":"2026-09-28T20:05:31.0856506Z","@mt":"Console at start: {Console}","Console":"freed_hidden"}
```

Its `"Console"` is one of four values:

- `"Console":"none"`: it had no console.
- `"Console":"freed"`: it had a console with a visible window and let go of it. That is the window above.
- `"Console":"freed_hidden"`: it had a console with no visible window, which nobody saw, and let go of it.
  `jason runtime start` hands the runtime such a console, so this is what a runtime started that way says.
- `"Console":"not_freed"`: it had a console and Windows would not let it go, so any window that console has
  stays open while the runtime runs.

**What it costs: the runtime ends when you sign out, without a chance to say so.** Registered this way, the runtime
is a program in your session like any other, and so are the programs it started. Microsoft's
[documentation of the console's control signals](https://learn.microsoft.com/en-us/windows/console/handlerroutine)
says of such programs: "Interactive applications are terminated at logoff". Its log ends at whatever it was doing
last, with no line saying that it stopped. Work in flight is left the way a crash leaves it, and the
next runtime takes it up as [`work-execution.md`](work-execution.md) says under "Stopping and restarting"; your
next sign-in starts that runtime. What the runtime starts — an agent host, a vendor's command-line tool — runs on
your desktop too, so anything it opens, a browser window for a sign-in included, opens there. That has always
been true of a runtime started with `jason runtime start`.

**0.1.0 registered this task differently**: one task called `\Jason` for the whole machine, with an **S4U**
logon, which only an elevated prompt may register. 0.1.0's `enable` and `disable` had to be run from one, and a
standard account could not use them. Microsoft's [reference for the logon type](https://learn.microsoft.com/en-us/windows/win32/taskschd/principal-logontype) says of S4U that "no
password is stored by the system and there is no access to either the network or encrypted files". `enable`, `disable` and
`jason uninstall` ask after `\Jason` once, and take it away only where it is this account's 0.1.0 task, the one that runs
the runtime: another account's, or a task of that name that runs anything else, is never touched.
**If one of them answers `ERROR: Access is denied.` about the task named `Jason`**, an earlier version may have
registered it from an elevated prompt, and only an elevated prompt can take such a task away. On an administrator's
own account, run `jason runtime autostart disable` from an elevated prompt once, which removes it and this version's
task alike, then `jason runtime autostart enable` from an ordinary one if you want autostart back; if it was
`jason uninstall` that stopped there, run the same elevated `disable`, then `jason uninstall` again. But on a
standard account an elevated prompt runs as somebody else, the administrator whose password is typed, and a
`disable` there would take that administrator's tasks away and leave yours: ask an administrator to delete the task
by the name the message gives, and never run `jason` under another account. The same holds for this version's own
task, `Jason-<SID>`, where one was registered from an elevated prompt. `status`
reads this account's task from an ordinary prompt; it does not ask after `\Jason`, so after an upgrade from
0.1.0 run `enable` or `disable` once. If that answers `ERROR: Access is denied.`, do what the paragraph above says
first. And stop the runtime 0.1.0's task started, once: `jason runtime stop`, or restart the machine. Then start one:
`jason runtime start`, or sign in again with autostart enabled. That runtime runs with no desktop and outlives a
sign-out, and `enable` and `disable` stop nothing. While it holds the data directory, the task this version registers starts a runtime at every sign-in that
finds the directory taken and exits without a word.

A toast comes only from a runtime in the person's own session: the one this task starts at sign-in, or one started
with `jason runtime start` in that session. It has no desktop otherwise, so a runtime that 0.1.0's logon task started
in session 0 shows none — `jason status` says so in its `notifications` check ([notifications.md](notifications.md)
§4).

### The environment an autostarted runtime has

It is the account's logon environment, not your shell's. Anything your shell's profile sets — a `PATH` a version
manager edits, a `JASON_DATA_DIR` you export, a proxy variable — is not there, save that from 0.34.0 a macOS
registration, and from 0.35.0 a Linux one, carries the `PATH` of the terminal that registered it, as the paragraphs
below say. The data
directory is why the registration carries it explicitly; a plugin whose executable is only on a `PATH` your profile
builds is the other half, and it will be reported `unavailable` by a runtime that started at logon while working
perfectly from a terminal. `jason plugin list` from the same account says which.

On Linux the registration is a `systemd --user` unit, and the environment it starts the runtime with is the systemd
user manager's, which reads no shell profile: not `~/.profile`, and not `~/.bashrc`, where a version manager or a
package manager's own setup most often puts its line. So from 0.35.0 `enable` records the `PATH` of the process that
registers in the unit, as an `Environment=` line, and the runtime starts at logon with it; `jason runtime autostart
status` says it under `path`, and a unit an earlier version registered carries none, so `--human` names the user
manager's. A `PATH` with a double quote, a backslash or a control character in an entry has no spelling the unit
carries, so `enable` refuses it and names the entry; `disable` and `jason uninstall` take the unit away whatever the
`PATH` holds. A program installed after the registration, in a directory that `PATH` did not hold, is missing to a
runtime started at logon until you register again from a terminal whose `PATH` has it, with `jason runtime autostart
enable`, then `jason runtime restart`. Or start the runtime from that terminal instead.

On macOS the registration is a LaunchAgent, and launchd starts an agent on a search path of its own,
`/usr/bin:/bin:/usr/sbin:/sbin`, which holds no program a package manager installed. So `enable` records the `PATH`
of the process that registers, under the agent's `EnvironmentVariables`, and the runtime starts at logon with it;
`jason runtime autostart status` says it under `path`, and an agent an earlier version registered carries none, so
`--human` names launchd's. A program installed after the registration, in a directory that `PATH` did not hold, is
missing to a runtime started at logon until you register again from a terminal whose `PATH` has it, with
`jason runtime autostart enable`, then `jason runtime restart`.

### What tests prove here, and what they cannot

Every document above is composed and asserted by tests, on whatever machine the tests run on, including the two
platforms that machine cannot register anything for. What no test here does is register something for real: a
test that did would leave a logon task behind on whoever ran it, and CI runners are whoever ran it three times
over.

So registering for real can only be checked **by hand**, on a real machine, and only for the platforms this
project has one of: Windows and Linux. **macOS is composed and asserted only** — nobody working on this has a
Mac, and this page would rather say so than imply a check nobody ran.

> **Checked by hand so far:** Linux, on 2026-09-20, on one machine, and Windows, on 2026-09-29, on one machine.
>
> **Windows, on 2026-09-29, on a standard account**, on Windows 11 24H2 (build 26100), installed from this version's
> own archive by `install.ps1`. `enable` from an ordinary prompt registered the task `Jason-<SID>` with an
> interactive logon (`InteractiveToken`), and `status` read it back from the Task Scheduler's own copy. The account
> **signed out and back in**, and a runtime was running because of it: in the account's own session, at medium
> integrity, with no console (its log carried `"@mt":"Console at start: {Console}"` with `"Console":"none"`) and no
> window from it, or from the program it started to check a plugin's executable, which had no console window
> either; nor when a plugin reload started that program again later. It **ended when the account signed out**,
> without writing a line about stopping, and the next sign-in started a new one on the same data: no migration left
> to apply, a campaign made before the sign-out still there, and again no console and no window. `status` named the
> data directory, and `jason status` said the runtime had a logon registration. `disable` took the task
> away, a second `disable` still succeeded, and neither stopped the runtime; stopped and started again with
> `jason runtime start`, the runtime logged `"Console":"freed_hidden"` and had no console afterwards.
> `jason uninstall` put the logon registration first in its plan, took it away and left nothing behind: no task,
> process, data or install directory, unpacked library or Path entry. "No window" is about Jason's processes: other
> software opened windows of its own during the same logons, among them the shell, Start, Search and a VPN client.
>
> **On an administrator's account, from an ordinary prompt**, `enable`, `status` and `disable` did the same for that
> account's own task, while the standard account's task stood registered beside it: the administrator's prompt could
> not read the standard account's task, before or after (access denied), and the standard account then read its task
> back exactly as it had registered it.
>
> **Linux**, unprivileged, under a `systemd --user` manager. The unit registered and enabled; the machine was
> restarted; a runtime was running before anything asked for it. `disable` removed the unit and the link in the
> default target.
>
> **Not checked:** macOS, which is composed and asserted only; and Windows before 24H2, whose logon window was
> measured with a test program built like Jason but without the setting, not with Jason itself, and not on an
> older Windows.

## 9. Taking an installation off the machine

```sh
jason uninstall --human --dry-run
jason uninstall
jason uninstall --purge-data --yes
```

The other end of §6. **Skills go only by receipt**: every root a deployment wrote into carries a record listing
each path written there, and this verb removes those skills and nothing else. A verb that deleted by pattern
would eventually delete somebody's own file; a verb that deletes by receipt cannot. The rest of the installation
goes by rule, one step at a time:

The order is the order that makes a half-finished run safe:

| Step | Why here |
|---|---|
| this account's logon registration, on Windows including the task `\Jason` an earlier version registered for it | first, so a logon part-way through cannot start what is going. On macOS just after the runtime instead: taking the job out of launchd ends the process launchd started at login — the runtime, with no drain — and the job (`RunAtLoad`, no `KeepAlive`) starts no runtime that stopped, so nothing starts one between the two; a runtime that will not stop leaves the registration in place |
| the runtime | and if it will not stop — or is running and does not answer — **nothing after this is removed**. On macOS and Linux, a runtime that is still starting, which runs this installation's `jason` and has not yet published its descriptor, is waited for before the runtime is looked for and before anything is removed, up to 60 seconds, said once on stderr, and then stopped as any runtime is; a dry run never waits. Where another `jason` of this installation still runs at that bound, the verb refuses with `jason_running`, naming each process, and removes nothing; where one still runs ten seconds after the stop, it refuses the same way and removes nothing further; and one more look comes just before the executable, or the application, is deleted, where a `jason` started in the removal's own seconds, or Jason Desktop opened again, keeps it and is named among the problems; after the stop a runtime that answers on this data directory is one started again, and counts, and the problem names the `runtime stop` of the `jason` still standing that stops it. Such a `jason` may be a call to a provider that a stopped runtime left running, which ends by itself within that operation's own time limit, a runtime still starting or migrating its database, or a set-up or an update still finishing: the refusal says to wait until it ends and uninstall again. On a computer more than one account uses, it may be Jason running under another account from this same application, which that account quits. Windows neither waits nor looks again: there a `jason` running from the application's folder is refused at once, below |
| on Windows, the notification registration | after the runtime, so its next toast cannot write it back: the AppUserModelID key the toast was shown under, the two entries Windows made for it at the first toast, and the `jason:` link where its command names this executable — one whose command is other than this installation's is kept, since it may be another installation's or a spelling of this one, and another program's never touched. All are this account's, under `HKEY_CURRENT_USER` |
| every recorded skill | by the paths its root's record names, digest by digest |
| the PATH entry | only where the installer wrote it and its directory holds nothing but Jason — the executable, its two notice files and what an upgrade left beside it. A PATH is a person's own document, and other programs are on it through a directory they share. On macOS and Linux the entry is the marked block in a login profile, and in `~/.bashrc` or `~/.zshrc` beside it; on Windows, whose Path carries no marker, the entry in this account's Path |
| the executable, its notice files, its install directory and what it unpacked | last, because everything above runs from it. The notice files go by name, so in a directory other tools share a `LICENSE` that is not one of them stays. On Windows the running image cannot be deleted, so it is moved out of the install directory, to a directory on its own volume, and a cleanup removes the copy once the verb has exited; the native libraries the build unpacked on its first run go too, and on Windows those every other build unpacked beside them — earlier versions of this installation, or another installation of this account, which nothing there tells apart; a build unpacks again on its next start, and one a running copy holds stays and is named. On macOS and Linux a running copy holds nothing, so the others stay, counted in one line that names the directory they are in, for removing by hand once nothing runs from them; where the directory builds unpack into was moved (§10), they are not looked for |
| on Windows, the `jason-retired-<id>` folders | after the executable, by the sweep `jason install`, `jason update apply --finish` and a move run (below, the rest of the retired install), in the places those rename into — the temporary folder, and beside the folder of the default command-line install and of the one this removal takes, an older one found where `jason install` looks for one where the application carries this `jason`: a folder of that name holding nothing but the `jason.exe` one of them renamed there while a process was running it goes once that process has ended, and is said; one that process still runs is named among the problems, with the folder to remove once it has ended, since nothing of Jason sweeps it after this. An executable this removal renames aside is said there once, by its folder |

A recorded file whose bytes have changed since is reported and kept, because the record says what Jason wrote
and a mismatch says somebody else wrote it afterwards. `--force` removes it anyway, and the containing
directory is not empty until it does.

**Where a package manager installed Jason** (§6), the last two steps are the manager's. The executable, the directory it
is in and the PATH entry that reaches it are left to the package manager, and so are the native libraries the executable
unpacked, which stay with it; the plan and the report name them as left to it, with the command that takes them off —
`brew uninstall jason`, or `winget uninstall Reply.Jason`. Everything above them goes as it always does, the runtime
stopped first, and the plan an agent reads carries the same as `left_to`.

**Where the desktop application carries this `jason`** (§6), on macOS, Windows and Linux, this verb is the one
uninstall: of the application, of what `jason install` placed for it, and of an older command-line install. It is
reached from a terminal on every system, from Installed apps on Windows, whose uninstaller runs it, and from **Uninstall
Jason…** in the Jason menu on macOS and Linux; the data directory is kept unless `--purge-data` says otherwise, and the
harness root is never touched. While Jason Desktop is open it refuses with `desktop_running` before anything is removed, a dry run aside;
`--wait-for <pid>` first waits up to two minutes for that process to end, and only then reads what it will take off,
which is how the menu item starts it before it quits. On Windows, run from a terminal, it also refuses with
`jason_running` at once, with no wait, naming each, while another process runs the carried `jason` in any session of
this account — a runtime still starting, a runtime of another data directory, a `jason journal watch`, a call of an agent
host opened by hand, a task that runs whether the person is signed in or not — because the application's uninstaller, which looks in every session by the image's path,
removes nothing while one does; the runtime this verb stops, an earlier demo's, and every process either started, at any
depth (plugin hosts, agents' calls for work items, which that stop ends), are not counted. `--keep-application` together with `--purge-data` is
refused with `uninstall_refused`: the first is the uninstaller's call from inside the application, and a purge never
runs from there. Beside the steps above:

| Step | What it takes |
|---|---|
| just after the runtime, and on macOS after its logon registration | Open Jason at Login, where it starts this application — after the stop, so a runtime that will not stop leaves it in place, as the refusal says the installation is; nothing starts the window between the two, since an open window refuses this verb before anything and a sign-in of the account it runs in, already signed in, starts nothing again: the LaunchAgent `~/Library/LaunchAgents/ai.jasonharness.desktop.plist` on macOS, unloaded from the login session before its file goes (`launchctl unload`, without `-w`, then `launchctl remove ai.jasonharness.desktop`, by its label, each waited for ten seconds at most, a job not loaded no error); with `--keep-application` the file goes and the job is not unloaded, since the application kept may be that job's own window, and the report says in one line that the job stays loaded until the next login; the values of this account's `Run` key on Windows with the state Electron keeps beside each under `StartupApproved`; the XDG autostart entry `ai.jasonharness.desktop.desktop` under `XDG_CONFIG_HOME` (`~/.config` by default) on Linux |
| Linux, the deb, just after the runtime and before anything of this account's is removed | the package `jason-desktop`, purged with `apt-get`, which asks for an administrator's password once, through `sudo` where there is a terminal and otherwise through the desktop's own dialog (`pkexec`); its scripts take back what it placed for every account, `/usr/bin/jason`, Jason's apt source and its lists among them. Where it does not go — no password given, no session to ask in, the package system held, apt failing — nothing of this account's is removed, the runtime this stopped is started again, and the verb exits 1, naming `sudo apt purge jason-desktop` as the way by hand. A purge still going after twenty minutes, ten more than apt waits for the package system, may yet take the package off, so the runtime is not started again from it: where the package stays, Jason Desktop's next start or `jason runtime start` starts it. The runtime is stopped on the installation's behalf, so a package that stays starts it again by the note that a start is owed, and the demo's runtime, which runs the `jason` apt deletes, is stopped before the purge |
| with the runtime | the runtime an earlier version's demo left running on its own data directory, stopped through `jason runtime stop` on it; one that does not stop ends the verb as the person's own does |
| with the `jason:` link | on Windows the `jason-desktop` link registration, where its command starts this application |
| in place of the PATH entry and the executable | the terminal command, by the rules `jason install` placed it by: on macOS, and on Linux for an AppImage, `~/.local/bin/jason` and `jason-desktop` where each is the application's wrapper running this `jason`, or one that is gone, or for an AppImage another version's copy beside it (from the deb `/usr/bin/jason` is the package's, gone with it), then `install.sh`'s lines for `~/.local/bin` where that folder then holds nothing but Jason — a profile file itself removed only where `jason install` recorded that it created that file and nothing but whitespace is left once the lines are out, so a file the person had, one they have written into since, and one with no record (an install before 0.49) stay — then the folder once empty; on Windows the application's `jason` and `jason-desktop` command files in WindowsApps, and the entry in this account's Path naming the application's `resources\bundle` where that folder holds nothing but what the application carries there (the executable, `LICENSE`, `THIRD-PARTY-NOTICES`, `skills/`, `version.json`, and the `jason-update.pid` a carried update writes beside them and an update ended part-way leaves, §10). Then the native libraries this `jason`'s build unpacked, by the executable's step above and its routine — its own build's directory, on Windows every other build's beside it, on macOS the others counted and left — since the `jason` itself goes with the application and the runtime that loads them has stopped. An older command-line install, found where `jason install` looks for one, goes by the rule that retires one: its executable, its notice files, its Path entry or profile block, its folder. Then the folders an earlier version's demo made, each only where it carries the demo's mark, and the updater's cache — on macOS also Squirrel's ShipIt state, `~/Library/Caches/ai.jasonharness.desktop.ShipIt`; on Linux under `XDG_CACHE_HOME`, `~/.cache` by default |
| Linux, an AppImage, just before the file, only where it goes | its menu entry, `applications/jason-desktop.desktop` under `XDG_DATA_HOME` (`~/.local/share` by default) where it starts that file, its icons `icons/hicolor/<size>/apps/jason-desktop-app.png` there, the copies of its `jason` under `<data>/desktop/bundle/`, the copy an update kept under `<data>/desktop/rollback/`, `<data>/desktop/appimage.json`, and `ai.jasonharness.desktop` there, `jason install`'s record of the profiles it created. `--keep-application` keeps all of it |
| macOS, just before the application, only where it goes | the application's own state under `~/Library`, each only where it is found as itself, never through a link, and each named in the plan before anything is removed. The application moves Electron's profile and logs into the data directory before it is ready (`app.setPath('userData')`, `setAppLogsPath`), so what is here is what macOS and the frameworks keep by the bundle identifier: `Preferences/ai.jasonharness.desktop.plist`, removed through the preferences system first (`defaults delete ai.jasonharness.desktop`, "Removes all default information for domain", waited for ten seconds at most), which would otherwise write back a copy it holds, then as a file; `Saved Application State/ai.jasonharness.desktop.savedState`, the windows macOS restores at the next open; `Caches/ai.jasonharness.desktop`, Squirrel.Mac's update state (`NSCachesDirectory` and the bundle identifier); `HTTPStorages/ai.jasonharness.desktop` and `HTTPStorages/ai.jasonharness.desktop.binarycookies`, the system's HTTP storage and cookies for the bundle; `WebKit/ai.jasonharness.desktop`; and `Application Support/ai.jasonharness.desktop`, the folder Apple's convention gives the bundle, where `jason install` records the profiles it created, `created-profiles.json`. Removing them forgets the application's preferences, its window layout, its caches and cookies; none is the person's work. Only what is provably the application's goes by name: `Application Support/Jason` and `Logs/Jason`, Electron's default profile and log folders (`app.getPath('userData')`, "the appData directory appended with your app's name"; `~/Library/Logs/YourAppName`), are named after the application and not its identifier, and the application has written its own into the data directory since its first release, so nothing in them proves them its own rather than another application's named Jason: each found is named under `left` and left, `--purge-data` or not. Where the home directory is `/`, whose `Library` is the system's, or is not the account's own as the account database names it (`getpwuid`), nothing under `~/Library` is looked at or removed, and `left` says so. `--keep-application` keeps all of it |
| last, after the data directory | the application. macOS: the `Jason.app` the `jason` is inside, deleted by this verb, whose own image stays mapped until it exits, and only where its `Contents/Info.plist` names the bundle identifier `ai.jasonharness.desktop`; a bundle that does not is left and named, with `application_fate` `not_found`. A delete macOS refuses — it may refuse a terminal the right to delete an application — is reported with what is left and the one thing to do, move that bundle, named by its path wherever it is, to the Trash, and the verb exits 1. An application in `/Applications` is one for every account of the Mac, and taking it off takes it from all of them. Windows: the application's uninstaller refuses while any process runs from its folder, this verb's included, so the verb starts one waiter, which once the verb has ended copies `Uninstall Jason.exe` out of the folder and runs the copy silently on it in place (`/S --uninstalled-by-jason _?=<folder>`), waiting for it. A silent uninstaller never closes a running Jason Desktop: finding one, or unable to look, it removes nothing and ends 3, and the waiter then runs the copy once more without `/S`, so the person who asked is asked on the screen; any other code but 0 at the end leaves one line in the desktop's log of that day, which `jason logs --source desktop` shows, where the log folder is still there. The report says the folder goes after the verb ends and names the waiter's process. Once that process has ended, and only if Jason is then still listed in Installed apps in Settings, uninstall Jason there; do not start that uninstall before then, since two at once can leave part of the folder behind. The uninstaller in Installed apps runs this verb through the application's own start, so a second one started there runs the application's `Jason.exe` from the folder while the first deletes the folder, and that file stays. `--uninstalled-by-jason` tells the uninstaller the application's steps are done, so it does not start the application, which would write into a data directory just purged. `--keep-application` leaves the folder: the uninstaller in Installed apps runs the verb so, through the application's own start, and removes the folder itself next. Linux: the AppImage file, deleted; from the deb nothing here, the package having gone first |

The plan carries these under `desktop`: `application`, `application_fate` (`deleted`, `after_exit`, `kept`, `purged` for
the deb's package, or `not_found` where the `jason` is not inside a folder shaped as the application's), `open_at_login`, `desktop_link`,
`command_files`, `command_folder`, `command_line_install`, `demo`, `caches`, `application_state` (macOS: each place under
`~/Library` the row above removes, found there, and empty where the application stays or the home is `/` or not the
account's own) and `left`. A dry run and the `--human` plan name each of them. Wherever no system's places are known,
the carried `jason` refuses with `uninstall_carried_by_desktop` before anything is read or removed, a dry run included,
and the message says to remove the application the way that system removes one; the data directory is kept either way.

**The system's own removal on Linux** — `sudo apt purge jason-desktop`, App Center, deleting the AppImage — takes the
application and leaves the account's own files, as it does for any application: the skills, the terminal command an
AppImage's `jason install` wrote, the logon registration and Open Jason at Login's entry. What they would start is made
harmless: the runtime's `systemd --user` unit starts only while its `jason` is there (`ConditionPathExists=`, §8), and the
autostart entry and an AppImage's menu entry name the file they start as `TryExec=`, so a session skips them once it is
gone. `jason uninstall` is the whole removal.

**Never touched:** a skill there is no receipt for, including one copied into a harness by hand; any other
skill in that harness; any provider CLI; any model credential; a PATH entry the installer did not write; another
account's logon registration; the notifications already in the notification center, which hold counts only and
are the person's to dismiss; and the harness root with every folder and file in it, the person's memory files and
every project's and campaign's folder ([projects](projects.md) §8), `--purge-data` included.
**The data directory is kept** — §10 below says what is in it — unless `--purge-data` says otherwise, and in
the machine shape that word has to be said in advance with `--yes`, because there is nobody there to be asked
and a verb that blocked on an invisible question would hang for ever. With it, what Jason keeps in the data
directory goes and anything else stays; a data directory that holds a file system's root, the profile, the
temporary directory or the installation is refused before anything is removed, and so is a folder of the harness
root or one inside it, and one holding such a folder directly in it or anywhere in a folder the purge would delete,
each known by its `.jason/marker.json`, or holding a folder there that cannot be looked into.

It exits 0 when everything it set out to remove is gone, and 1 when it did not get there — it refused,
something is still there, or it stopped part-way — and every step that did happen is listed either way.

**On macOS one launchd record can stay**: the runtime's label, `ai.jason.runtime`, recorded as enabled in this account's
launchd records (`launchctl print-disabled`). It stays where a disabled record was cleared, by `enable` or by this verb
(§8), or where a release before 0.49 registered the job, whose `enable` loaded it with `-w`. No command run without
administrator rights removes it, and it does nothing: with the job's file gone, nothing loads under that label.

### The other half: setting the machine up, `jason install`

```sh
jason install
jason install --roles
jason install --retire <executable>
jason install --terminal-path <PATH>
```

Run by the `jason` the desktop application carries (a `version.json` beside it), it sets this machine up for the
application: the terminal command, the skills for Claude Code and Codex where each is found, and an older command-line install retired. Any
other `jason` is refused with `install_not_carried`, a system other than macOS, Windows and Linux with
`install_unsupported`, and a `jason` run elevated with `install_elevated` — on Windows as an administrator above the
account's ordinary rights or as another account than the one signed in to the session, on macOS and Linux as root, by the rule
`update_elevated` follows (§7) — each before anything is read or written. On macOS, the `jason` of an application that runs from a place
that goes away, its disk image mounted or a copy macOS runs from a temporary place of its own (App Translocation) because
it was opened where it was downloaded, is refused with `install_vanishing_place` before anything is stopped, written or
retired, and before the update's lock is read or taken: nothing is placed, retired or stopped, and the sentence says to
move Jason to Applications, or to `~/Applications`, and open it there. A volume is such a place only where it is
read-only, as `access(2)` answers, which writes nothing; a disk of a person's own is not refused by its path. The routine
an update runs in its own process refuses the same way at its entry. Windows is not refused this way: its installer puts
the application in its place. Where the data directory has an update folder it holds the
update's lock, `<data>/update/update.lock`, while it runs, naming itself beside it as `install` (§7), and while another
`jason` holds it — an update, its finish or a rollback — it refuses with `update_in_progress` and sets nothing up, since an
update's own finish sets the machine up itself; while another `jason install` holds it, it refuses with
`install_in_progress`, retryable, a sentence of its own naming that process. A setup the lock's holder runs as its waited
child, handed the holder's nonce in `JASON_UPDATE_LOCK` as the update's own parts are (§7), goes on under that hold
instead, and one handed another nonce is refused. The lock is the open file, so one a killed
holder left refuses nobody. Where there is no update folder, or no data directory at all, it takes no lock and makes
none: no update can be in flight there, and a person's first start still writes nothing under it. `install.sh` and
`install.ps1` print a refusal's sentence and end non-zero; the installer goes on whatever `jason install` answers. The two
edges of §7 hold here too: a setup under a lock whose holder was killed, and an installer started from the menu, which
takes the default data directory's lock and not that of a data directory named only in a terminal. It is idempotent — a second run changes nothing and says so —
it starts no process that outlives it, and it prints one JSON document (`--human`: lines) always: a step that fails
part-way is named under `problems`, and the steps after it still run. Its steps, in order:

| Step | What it does |
|---|---|
| a command-line install's runtime | where an older command-line install is found — the executable `--retire` names, else the one the last `jason update apply` recorded, else the installer's default place (`~/.local/bin/jason`, `%LOCALAPPDATA%\Programs\jason\jason.exe`), each only where it is Jason's: a `jason-LICENSE` or `jason-THIRD-PARTY-NOTICES` beside it, the record, or on Windows that folder, and `--retire` only a regular file named `jason` (`jason.exe` on Windows) with one of those signs, anything else named and left — a runtime running from that executable is drained and stopped. **No runtime is ever started**: the report says `stopped`, and `jason runtime start` starts one from the carried file; a caller that goes on to use Jason runs it. Just before it drains and stops the runtime, it writes the note that a start is owed (below), and takes it back only where the runtime goes on running having taken no stop. Where nothing is to be retired, no runtime is asked anything. A runtime that does not stop leaves that install as it is, and so does one that does not name the executable it runs from; it is resumed unless it took the stop and has not ended, or has ended. Where the data directory's `layout.json` refuses this `jason` (`layout_newer`, `layout_unreadable`), no runtime is stopped, no registration moved and nothing of that install retired, with `--roles` or without |
| the terminal command | Linux, the deb (`package-type` saying `deb` in the package's resources, beside the carried file's folder): the package's own `/usr/bin/jason`, a link to the carried file for every account, read back as running it and never written, with no profile line; once it does, the application's own wrapper at `~/.local/bin/jason` or `~/.local/bin/jason-desktop`, which an earlier version's Command Line item wrote and Ubuntu's profile puts first on the PATH, taken back, each only where it is the application's shape. Linux, an AppImage's copy under `<data>/desktop/bundle/<version>/`: as on macOS, below, the wrapper running the copy, and the profiles the routine creates recorded under `~/.local/share/ai.jasonharness.desktop`. macOS: `~/.local/bin/jason`, the application's own two-line `sh` wrapper running the carried file (naming `JASON_DATA_DIR` where that is set), written over an earlier wrapper, the retired executable or a link to an application's `jason`, and over no other file; then, only where the PATH a terminal has does not name `~/.local/bin` already — `install.sh`'s own test, `case ":$PATH:" in *":$INSTALL_DIR:"*)` — `install.sh`'s marker and line, `export PATH="<home>/.local/bin:$PATH"`, appended to the login profile and to `~/.zshrc` or `~/.bashrc`, each only where the line is not already a line of that file — the rule `install.sh` appends by, held to it word for word. The PATH a terminal has is the one `--terminal-path` names, and without it the routine's own, which is a terminal's when a script runs it; `--terminal-path ""`, the empty value, says the login shell gave no answer, and the folder counts as not there. A home whose folder holds a line break, `"`, `$`, a backtick or `\` takes no line, as `install.sh` refuses it, and a profile that is a link is appended through only to a regular file; either is named with the line to add by hand. A home whose folder holds `:`, which separates PATH's entries, takes no line either, since no line could name it. And the application's 0.48 `~/.local/bin/jason-desktop` wrapper is taken back, only where it is the application's shape. Windows: one entry in this account's Path naming the carried file's folder, `resources\bundle`, only where it is not there, the value's kind kept and running programs told, as `install.ps1` wrote its own before 0.49, and a Path of a kind that is not a string refused and left; and the `jason`/`jason-desktop` command files the application placed in `%LOCALAPPDATA%\Microsoft\WindowsApps` taken back, each only where it is one of the application's shapes. The command files go, and the next step runs, only once the terminal command reads back as placed — the wrapper and its lines, or the Path entry — since otherwise the old install stays: where it sat at `~/.local/bin/jason` the wrapper has already taken its executable's place, and elsewhere it is what still answers `jason`. A shell already open finds neither, and runs the path the report gives as `open_shell` |
| the rest of the retired install | its logon registration, where it names that executable and this data directory, or no data directory (and then it names none again), registered again naming the carried file — on macOS the file is moved and launchd loads it at the next logon, so nothing starts now, and until that login the old job stays loaded and idle; its notice files, and on Windows `jason.previous.exe`, removed. Windows: the executable removed, or, while a running process holds it, renamed into a `jason-retired-<id>` folder on its own volume and left there, with nothing started to wait for that process, until a later `jason install`, `jason update apply --finish`, move or `jason uninstall` finds that process ended and removes the folder; its Path entry taken off where its folder then holds nothing but what `install.ps1` wrote there before 0.49; the folder once empty. macOS, and an AppImage's copy on Linux: the wrapper has taken its place at the default path; one elsewhere is removed, with its profile block by §9's rule. Linux, the deb: the executable is removed, with its profile block where its folder then holds nothing but Jason |
| the harness skills | both packs, from the skills beside the carried file, read where they stand, into the folder of each of these two hosts found by its home folder, and of no other: Claude Code (`~/.claude` → `~/.claude/skills`) and Codex (`~/.codex` → `~/.agents/skills`, which other hosts read too), recorded as `jason skills install` records them. A skill folder there under a name Jason ships that no record of Jason's names is the person's own: left as it is, nothing of it recorded, and named under `left_alone` and in `left`; one whose files equal the carried skills byte for byte is what an earlier deployment wrote before its record, and is adopted and recorded. It is the rule of every deployment, the roles home, an update's skills step and the finishing step included, and no flag reaches past it |
| the role skills | with `--roles` only, into the data directory, as `jason skills install --roles-only` deploys them |

Without `--roles` it writes nothing under the data directory, so the application's first start still opens on the Demo;
only an install being retired, which has data already, may have its runtime stopped, the note that a start is owed
written and its logon registration made again.

**The note that a start is owed.** The routine that stops a runtime is the one that records it: just before `jason
install` drains and stops this data directory's runtime, it writes `run/start-owed.json` in the data directory, one JSON
object with the moment in it,

```json
{"stopped_at":"2026-11-17T15:05:35Z"}
```

(ISO-8601 UTC, to the second), written to a temporary file in the same folder and renamed into place. It is taken back
only where the runtime goes on running having taken no stop: it refused the shutdown, or the drain or the wait before it
failed or was cancelled, and the runtime is resumed. Where the runtime took the shutdown and has not ended, has ended, or
was sent the shutdown and its answer never came, the note stands, the routine reports `would_not_stop` or `stopped`, and
only the last of these is resumed, since whether it is going down is not known; beside a runtime that answers the note is
inert, and its own start or a `jason runtime stop` takes it back. Nothing is written where no runtime is to be stopped, and so none
where no data directory exists, since a runtime has run wherever one does. A runtime's own start removes it, once its
endpoint descriptor is published; so does any stop through `jason runtime stop` without `--start-owed` (below), whoever
runs it, and `jason uninstall` where it keeps the data, since with the installation taken off none is owed. So it stands from the stop until a runtime
runs on that data directory again — a routine killed or timed out during the stop's wait (up to thirty seconds) and a
caller that quit or relaunched before it started one included, and the Demo case: the
application's mode record says demo while a runtime a terminal started runs on that data directory, an install or the
window's own update runs `jason install`, which stops it, and the application comes back in the Demo, which starts none. Its presence is what
decides; the moment is for a person. `jason status` names it in its `runtime` check while no runtime answers, with the
one command that pays it, `jason runtime start`; Jason Desktop, at every start of its own, Demo or Live, starts a runtime
from the `jason` it carries when it finds the note standing and none answering. A note whose content cannot be read
stands all the same. Its callers: the Windows installer at every install and update, silent or not, with no flags; the agent's install
script on macOS and Linux once the application is in place, with `--roles` and no `--terminal-path`; the application's start where
this last ran for an older version, with `--terminal-path` naming the PATH its login shell printed — never the
application's own, which it adds places to — or the empty value where the shell gave none; and the move of a command-line
install, with `--retire` naming the executable it moves from. On Windows `--terminal-path` is read by nothing; on Linux
the application's start runs it from the `jason` the deb carries, or from an AppImage's copy, never from the AppImage's
mount, which ends with the run.

**A stop on an installer's behalf: `jason runtime stop --start-owed`.** A stop made on behalf of an install or an update
leaves the note; only a stop a person or an agent asks for takes it back. `--start-owed` is that stop. It writes the
note, as above, just before it asks the runtime to shut down, and prints the stop's own answer unchanged. Where it does
not see the runtime end, it reads what became of the runtime as `jason install`'s stop does, by the same rule: the note is
taken back only where the runtime refused the shutdown and goes on running, and stands where the runtime ended, where it
took the shutdown and has not ended, and where the shutdown was sent and no answer came, a cancel inside the send among
them, since whether that runtime is going down is not known. It takes back only the note it wrote: where an
older note already stands it writes none and leaves that one as it is, whatever the stop comes to. Where no runtime
answers to the data directory, none running or no data directory at all, it stops nothing, writes nothing and makes no
folder. A note it cannot write is said on standard error, and the stop's exit code is the stop's. Jason Desktop's own
stop for its installer, `--stop-runtime`, which `desktop/installer/uninstall.nsh` runs before an install, an update and
an uninstall alike, always passes it, since that start is only ever made on an installer's behalf. So an uninstall run
through the installer leaves no note: its stop writes one, and `jason uninstall` after it takes it back where it keeps
the data, or takes it with the data under `--purge-data`.

The document carries `version` and `executable` (the carried file); `command` — `runs` (what a new terminal finds `jason`
through: the wrapper on macOS and for an AppImage on Linux, `/usr/bin/jason` for the deb, the folder on the Path on Windows), `placed` (whether a
new terminal finds this `jason`, as read back: a `command` that is null or not `placed` is what makes the install
scripts end non-zero), `open_shell`, `changed`, `profiles`, `taken_back`, and `finds`: the file a terminal opened from
now on runs for `jason`, read once everything is placed and retired, as that terminal reads it — on Windows the machine's
Path, then the account's, each expanded as its registry kind says, the name tried with each `PATHEXT` extension in
order; on macOS and Linux the PATH a terminal has, with `~/.local/bin` in front where the profile lines put it there. Where it is
neither the carried file nor the wrapper running it, a problem names it and what removes it — a command-line install's
own `jason update apply`, which moves it into Jason Desktop; a package manager's command; or else removing it or taking
its folder off the PATH — and it is left as it is. Null where none is found or the command was not placed. Beside it,
`finds_this` says whether `finds` is this jason or the wrapper running it, decided once by `jason install`: the install
scripts read that member, compare no path themselves, and end non-zero where it is false; a report without it, from an
older kept application, is read as before it existed. Where the command-line install of the first step is held, its runtime not stopped
or the layout refusing, the terminal command is left as it is on every system, the Path unwritten on Windows, and
`placed` is false;
`skills`, one entry per root — `root`, `hosts`, `written`, `unchanged`, `left_alone`; `roles` (with `--roles`, else null);
`retired` — `executable`, `aside`, `removed`, `reregistered` — or null; `runtime` — `not_running`, `running` (another
executable's, left as it is), `stopped`, `would_not_stop`; and `done`, `left` and `problems`, a sentence each, with
`completed`. It exits 0 when everything it set out to do is done, 1 when it refused or a step did not get there, each named
under `problems`, and 2 for a usage error.

## 10. Where things live

The **data directory** is `~/.jason`, or whatever `JASON_DATA_DIR` names: the database, the configuration, the
plugins, the skills, the logs and the work directories. The **install directory** is wherever the executable
happens to be, and Jason writes nothing into it but the executable and its two notice files; a `jason` Jason Desktop
carries on Windows writes one more beside itself while it hands an update to the Desktop, `jason-update.pid`, and takes it
away again, and its own file waits in the updater's store under `%LOCALAPPDATA%\jason-desktop-updater\jason-carried\` (§7). They are separate on purpose — updating the program and keeping
your work are different things, and a person who installed the binary somewhere unusual should never find Jason
writing files beside it.

That is also why an update writes under the data directory, and beside the executable only the executable and its
two notice files. `~/.jason/update/`
holds the ledger, the staged download, the executable that was replaced and the copy of the applier kept
for a resume by hand; the install directory may be read-only, on another volume, or a directory somebody removes Jason
by deleting.

`~/.jason/autostart/` holds one file, and only on Windows: the task document §8's `enable` hands to `schtasks`.
The registration itself is the Task Scheduler's after that, so deleting the document unregisters nothing —
`jason runtime autostart disable` does. `autostart status` prints that path while the file is there, which is
also how you can tell that the registration you are looking at was made from this data directory. macOS and Linux keep their document where the system reads it, under
`~/Library/LaunchAgents/` and `~/.config/systemd/user/`, which are not this directory and are not Jason's to
tidy up.

The Unix execute bit is not carried by a zip and is somebody else's idea of who may run this inside a tar, so it
is restored by whoever unpacks the archive: `UpdateStager` for an update, and `install.sh` for an install. On
Windows there is no such bit and neither does anything.

There is a third place, which nothing here controls. The executable is a single file with the .NET runtime and
its native libraries inside it, and the first run of each version unpacks those natives to a cache — 
`%TEMP%\.net\jason\<hash>\` on Windows, `~/.net/jason/<hash>/` in the home directory on macOS and Linux, or
under `DOTNET_BUNDLE_EXTRACT_BASE_DIR` where that is set. It is per user and per build, it is why the first start of a
new version is slower than the next, and it is safe to delete when nothing is running. `jason uninstall` removes its
own build's directory there; on Windows every other build's too, and elsewhere it counts them and leaves them, since
a copy running from one holds nothing there. On Windows it also looks under other temporary folders — the folder its
own temporary folder is numbered inside, where it is one, and that folder's numbered folders, or its own numbered
folders where it is none, as Remote Desktop Session Host numbers a temporary folder per session — and names what a build
of Jason unpacked there, with that folder, and leaves it: this removal takes only what stands under its own temporary
folder, and what it names is safe to remove once nothing runs from it. Under `DOTNET_BUNDLE_EXTRACT_BASE_DIR` it removes
its own alone.

## 11. The code behind this page

`SemanticVersion`, `ReleaseAssets`, `UpdateManifest`, `UpdateFeed` and `UpdateLedger` live in `Jason.Contracts`,
which is shared by the runtime and the CLI. `UpdateFeed` is the one type in that assembly that performs I/O,
which is a deliberate exception: two callers ask this question — the runtime unattended, and `jason update check`
when a person asks — and what a Jason installation sends to a web page must be one rule rather than two copies
that can drift. It opens nothing by itself; its caller hands it the HTTP client.

The applier is the CLI's, under `Jason.Cli/Update/`: `UpdateStager` downloads and unpacks, `UpdateApplier` is
the step machine and the only code that renames anything, `UpdatePaths` composes every path under
`~/.jason/update/`, and `UpdateCodes` holds the refusals above — the table in §7 is read out of it by a test, so
a code renamed in the code turns this page red rather than quietly disagreeing with it. None of the applier
opens the database: it renames files, asks the runtime questions and reads the backups directory. `PluginUpdates`,
beside them, answers the check's `plugin_updates` from the manifest and the plugins directory alone (§5), and the
`plugins` step reads the same plan, placing each package through the installer `jason plugin install` uses.

Autostart is the CLI's too, under `Jason.Cli/Autostart/`. `AutostartArtifacts` composes each platform's document
and reads a command line back out of one, both as pure functions of what they are given; `WindowsTasks` decides,
one `schtasks` answer at a time, what happens to this account's task and to a `\Jason` 0.1.0 left, and what each
denial says; `IAutostartRegistrar` is the one place a process is started or a document written, and
`AutostartCodes` holds the two refusals §8 can raise:

| code | what it means |
| --- | --- |
| `autostart_unsupported` | this machine has no way to start something at logon that Jason knows about |
| `autostart_refused` | the tool that registers it refused, and the message carries what the tool itself said |
