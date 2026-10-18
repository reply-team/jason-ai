# Releases and updates

.NET does not update a self-contained application, so Jason owns its update behaviour. This page is the
contract for it: what a release is, what it publishes, how a running runtime learns that a newer one exists,
how a person asks the same question by hand, and what `jason update apply` does to this machine when they say
yes.

**Nothing starts itself unless you asked.** Nothing is registered with the machine until
`jason runtime autostart enable` registers the runtime to start at logon (§8). An update keeps that
registration working, and whatever you wrote yourself, for the reason §7 gives.

**Nothing in a release is signed or notarized.** Integrity is a SHA-256 you can check yourself, published beside
the files and inside the manifest. Signing and notarization are their own story, before 1.0.0.

## 1. What a release is

A release is the git tag `vX.Y.Z` and the files that tag's build publishes. A merge to `main` is not a release:
`main` is where the code is, and a tag is the statement that one commit of it is worth installing.

Two repositories carry a release. The runtime's source and its tags live in `reply-team/jason-internal`, and a tag
there builds the release. The release itself — the assets below, the notes, `latest` — is published in
`reply-team/jason-ai`, together with the open set at that version, through a pull request a person reviews and merges;
that merge is what tags `v<version>` in the public repository, so the public tag names the published tree and the
runtime's tag of the same name names the source it was built from.

Versions are [SemVer](https://semver.org). `0.x` releases are ordinary releases and are never flagged as
pre-releases — the pre-release flag is reserved for release candidates (`v1.0.0-rc.1`), because a repository
whose only releases are flagged has no "latest" for anything to resolve to.

## 2. The files a release publishes

| Asset | What it is |
|---|---|
| `jason-win-x64.zip` | the Windows executable, `LICENSE` (the runtime's licence) and `THIRD-PARTY-NOTICES` |
| `jason-linux-x64.tar.gz` | the Linux executable, `LICENSE` (the runtime's licence) and `THIRD-PARTY-NOTICES` |
| `jason-osx-arm64.tar.gz` | the macOS (Apple silicon) executable, `LICENSE` (the runtime's licence) and `THIRD-PARTY-NOTICES` |
| `checksums.txt` | one `sha256sum` line per archive, and one for the plugin's package |
| `manifest.json` | what the updater reads |
| `install.sh` | the macOS and Linux install script, as this release was published with it |
| `install.ps1` | the Windows install script, as this release was published with it |
| `jason-plugin-reply.zip` | the Reply plugin's package: the files of `plugins/reply` at the tag, at the archive's root |

**The install scripts are a release's own files.** The one-liners fetch them from a release rather than from the
default branch: a script on the branch can be ahead of every release, and a pinned install runs the script the
release it installs was published with.

**The names carry no version, on purpose.** That is what makes

```text
https://jasonharness.ai/releases/latest/download/jason-linux-x64.tar.gz
```

resolve for every release there will ever be, so a README, an install script and a runtime's feed URL are
written once and never edited again. The address is Reply.io's own and keeps GitHub's paths (§4), so the store
behind it can change without any of them changing.

**An archive holds the executable, `LICENSE` and `THIRD-PARTY-NOTICES`, and nothing else.** That `LICENSE` is
the runtime's own; the repository's MIT licence covers the skills, contracts, plugins, documentation and install
scripts, not the runtime. `THIRD-PARTY-NOTICES` names the .NET runtime and the packages the executable carries,
each with its licence. A plugin is installed by copying a directory into the data directory and nothing reads the
install directory, so a plugin bundled with the executable would be a file nothing loads. The Reply plugin is
published beside the archives instead, from 0.11.0 on, as a package of its own: `jason-plugin-reply.zip`, the
files of `plugins/reply` at the tag at its root, built once per release and listed in `checksums.txt` but not in
the manifest, whose artifacts are platforms. Installing it is unpacking it into `~/.jason/plugins/reply` once it
matches its line — `docs/golden-path.md` step 3 prints the lines — and what is unpacked has the content digest of
the package at the tag (DEC-INSTALL-002). Any other plugin is installed as `docs/plugins.md` §4 says.

## 3. The manifest

`manifest.json` is the updater's feed. One GET of one file: no GitHub API, so no token, no rate limit, and
nothing to break when an API version retires.

```json
{
  "schema": 1,
  "version": "0.2.0",
  "published_at": "2026-09-19T08:00:00Z",
  "release_notes_url": "https://jasonharness.ai/releases/tag/v0.2.0",
  "min_upgrade_from": null,
  "artifacts": {
    "win-x64":   { "asset": "jason-win-x64.zip",      "sha256": "e3b0…b855", "size": 114254557 },
    "linux-x64": { "asset": "jason-linux-x64.tar.gz", "sha256": "e3b0…b855", "size": 118201344 },
    "osx-arm64": { "asset": "jason-osx-arm64.tar.gz", "sha256": "e3b0…b855", "size": 112984576 }
  }
}
```

| Field | Rule |
|---|---|
| `schema` | the shape of this document. This build reads `1` and refuses anything else rather than guessing. |
| `version` | the release's version, SemVer. A version that does not parse makes the whole feed invalid. |
| `published_at` | an ISO-8601 UTC timestamp. |
| `release_notes_url` | where a person reads what changed, or `null`. An `http`/`https` address or nothing. |
| `min_upgrade_from` | the oldest version that may upgrade to this one directly, or `null`. Read by the applier, not by the advertisement. |
| `artifacts` | one entry per platform. A release that names no artifact for your platform is still a valid release — it just has nothing for you. |
| `asset` (in `artifacts.<rid>`) | a file name, and only a file name: letters, digits, dot, dash, underscore. Never a path and never a URL. |
| `sha256` (in `artifacts.<rid>`) | 64 lowercase hexadecimal characters, with no `sha256:` prefix. |
| `size` (in `artifacts.<rid>`) | the archive's size in bytes, a positive number. |

The manifest is generated by the release workflow from the files it just built, so it is one source rather than
a second copy of what the checksums say. A download is fetched from **the feed's own directory** and the asset
name the manifest carried — never from a URL inside the document, because a feed that could name where to
download from could name anywhere, and the digest it also carries would then be checking its own bytes against
its own hash.

A manifest that breaks any rule above is refused whole (`update_feed_invalid`); nothing is half-read.

The schema changes only with a change of layout — the asset names, `checksums.txt`, the manifest's fields — and
an install script keeps reading every schema published under a tag it can be pointed at. A file published beside
the archives that the manifest does not list, as the plugin's package is, changes no schema: the manifest stays the
same document, and each install script reads `checksums.txt` by its own archive's line.

## 4. What the runtime sends, and when

A running runtime checks the feed on its own, because the person who most needs to know that an update exists is
the one who never thinks to ask.

**What it sends, in one sentence: one `GET` of the feed URL with a `User-Agent` of `jason/<version>`, and
nothing else.** No body, no query, no identifier, no machine facts, nothing about your campaigns, your plugins
or your data directory. The answer is kept in memory and shown by `system.info`; it is never written to disk.

**Where it goes.** The default feed is on `jasonharness.ai`, an address Reply.io operates, which redirects every
request under `/releases/` to the same path in the public repository's releases on GitHub. A download is composed
from the feed's own directory (§3), so it goes the same way. An installation built before that address existed
asks GitHub directly; both answer with the same file.

A check that fails — offline, a page that answers 500, something that is not a manifest — is a log line at
warning and never an error to any caller. An offline machine is a normal machine. A feed that has not answered
within **10 seconds** is one of those failures: a manifest is a few hundred bytes, so waiting longer only makes
an offline machine slower to admit it. `jason update check` waits exactly as long, for the same reason.

### The settings

| Setting | Default | Range | What it does |
|---|---|---|---|
| `Update:CheckEnabled` | `true` | — | whether this runtime asks at all |
| `Update:FeedUrl` | `https://jasonharness.ai/releases/latest/download/manifest.json` | an `https` address, or `http` on loopback | where it asks |
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
again, and one that loses it twice ends with `role_skill_unreadable` rather than running with no skill.

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

## 6. Installing

```sh
curl -fsSL https://jasonharness.ai/releases/latest/download/install.sh | sh
```

```powershell
irm https://jasonharness.ai/releases/latest/download/install.ps1 | iex
```

Each script works out your platform, downloads the archive for it, **verifies the SHA-256 before unpacking**,
installs the executable with its licence and the third-party notices beside it, as `jason-LICENSE` and
`jason-THIRD-PARTY-NOTICES`, and adds its directory to your PATH behind a marker line it will recognise next time.
Pass `--no-modify-path` to leave your PATH alone, and `--version X.Y.Z` to install a particular release.

The default install directories are `~/.local/bin/jason` on macOS and Linux, and
`%LOCALAPPDATA%\Programs\jason\jason.exe` on Windows. Both are per-user: nothing here needs administrator
rights, and nothing is installed for other people on the machine.

They resolve to the latest release, so where none has been published they answer 404 and there is nothing to
install from — build from source until one has. [`docs/INSTALL.md`](INSTALL.md) is the whole path either way,
in the order a stranger reads it.

The workflow that builds a release has a button — a `workflow_dispatch` that produces a draft, for a dry run
before any tag exists. GitHub offers that button only for a workflow that is already on the **default branch**;
once it is, a branch's own version can be run from it (`gh workflow run release.yml --ref <branch>`), which is how a
change to the pipeline is rehearsed before it merges. The dry run builds the draft in `reply-team/jason-ai` and opens
no pull request.

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
`jason runtime restart`.

Where the feed names no release, `jason update apply` answers `update_feed_unreachable` and does nothing;
there is nothing to apply until one is published. The feed resolves to the latest release, and a repository
with no releases has none.

An installation whose executable carries no `jason update apply` moves forward by re-running the install
one-liner in §6 until it is on a build that does; after that, `jason update apply` is how it moves.

### The steps, and the ledger that names them

An update is eight steps, and every one of them is written to `~/.jason/update/ledger.json` **before** it is
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
| `started` | a runtime has been started from the new executable |
| `healthy` | that runtime answers, says it is the version this update installed, and has its migrations applied |
| `complete` | the update is over, and the ledger stays behind as the record of it |

Because each line is written before its step is taken, the last step a ledger names is the last step that was
**begun** — never the last that finished. That is what makes a kill anywhere recoverable: every step is written
so that beginning it twice is the same as beginning it once, so the next `jason update apply` reads the file,
picks the sequence up at the step it names, and carries it through.

`--drain-seconds` bounds the wait at `drained`; the default is 120 and `--drain-seconds 0` means do not wait at
all. Either way the applier says what it left behind: work still running keeps its lease, and the runtime that
comes up next finishes it or its enforcer takes it back.

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

An update replaces the executable and its two notice files, and writes under `~/.jason/update/`. It does not touch the rest of the data
directory: not the database, not `~/.jason/config/settings.json`, not the plugins under `~/.jason/plugins/`, not
the skills, not the logs, not the work directories. Nor does it touch anything you registered with the machine
to start Jason — a systemd unit, a scheduled task, a login item — because the file at the install path is
replaced in place rather than moved elsewhere, so whatever points at that path still points at Jason.

So the skills stay at the tag the build before installed them from, and `jason status` is what tells you. Once the
new build is running, role skills a default install took at that tag fail `role_skills`, which is required,
and its repair — `jason skills install --roles-only`, with the `--source` they came from when that was not the
default source, `reply-team/jason-ai` — moves them to the new build's tag. An agent's own packs fail
`harness_skills`, which is optional, and `jason skills update` moves them; it is printed for you, not run for you.
A deployment made at a tag chosen with `--ref` passes both, and stays where it is until you move it.

The one thing an update does change beyond the executable is the database, and not by itself: the new build's
first start migrates it, exactly as any start does, after taking the backup that every migration takes. `healthy`
records which migrations that was and where the backup went, which is what makes the undo below possible.

### When it stops halfway

Kill the process anywhere and the ledger is still right, because it was written first. Run `jason update apply`
again and it finishes from the step on disk, or says what it cannot finish. Two states are worth knowing about
by name.

**Between `kept` and `swapped` the install path is empty.** The old executable has been moved to
`~/.jason/update/previous/`, its two notice files after it, and the new one is not in place yet. The
window is a few renames wide, but it is the one window a person cannot type their way out of, because the
`jason` on your PATH is the file that is not there. The process performing the swap is a copy of the applier under `~/.jason/update/applier/`, which is why
the swap survives the file it is swapping — and it is also the first way out:

```text
~/.jason/update/applier/jason update apply
```

The second way out needs no Jason at all: copy `~/.jason/update/previous/jason` back over the install path by
hand, and you have the version you started with, running as it was before. On Windows the file is `jason.exe`
and the install path is `%LOCALAPPDATA%\Programs\jason\jason.exe`.

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

You will usually be typing `jason update rollback` from the PATH, which means the executable being replaced is
the one running the command. That is expected and it works — the file is renamed out of the way rather than
written over — but it is also why the replaced executable may still be sitting in `~/.jason/update/replaced/`
afterwards: a running program cannot delete itself. Nothing removes it later either; each is named for the
version and the moment it was set aside, and deleting them is safe as soon as that version is no longer
running.

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
#    for a backup taken before a migration is the version you were on before the update.
~/.jason/update/previous/jason runtime start
```

### What an update refuses, and why

Every refusal exits 1 with an error envelope on stdout, and the message names the file or the version, because
a code on its own tells nobody what to do next.

| Code | What happened |
|---|---|
| `update_up_to_date` | the feed offers nothing newer than what is installed, and no `--version` asked for something else |
| `update_artifact_corrupt` | what was downloaded is not the digest the manifest published. The bytes are deleted rather than kept for a retry |
| `update_artifact_unreachable` | the download did not arrive, or the archive could not be read. Worth trying again |
| `update_artifact_unexpected` | the archive does not hold the executable a release for this platform is supposed to hold, or the release publishes nothing for this platform. Its notice files are taken where the archive holds them, and an archive without one is not refused |
| `update_in_progress` | a *different* version is already in flight. The same version is resumed rather than refused, so re-running `jason update apply` after a kill is always the right move |
| `update_not_directly_applicable` | the release's `min_upgrade_from` is above the version installed. The message names the version to pass through first |
| `update_not_updatable` | this Jason runs through `dotnet`, so there is no single executable to replace. Install a published release with the one-liner in §6 |
| `update_managed_by_package_manager` | a package manager — Homebrew or winget — installed this Jason and keeps its own record of the version, so the file is not this build's to replace. Nothing is staged, drained or stopped, and `jason update rollback` refuses the same way. The message prints the three lines to type instead: `jason runtime stop`, the manager's upgrade, `jason runtime start` |
| `update_cross_volume` | the staged file and the install path are on different volumes. An update renames rather than copies, because a half-copied executable is the one state nothing can recover from. Point `JASON_DATA_DIR` at the executable's volume, or install Jason on the data directory's |
| `update_runtime_unreachable` | the runtime would not drain or would not stop, so nothing was replaced |
| `update_not_healthy` | the new version is in place, but the runtime it starts is not the one this update installed. The executable can be put back |
| `update_file_refused` | a file could not be moved, copied or written: a directory that is not one, a permission, a lock. It covers every file these two verbs touch — an update's renames and a rollback's, the ledger both are recorded in, and the database restore. The message names the path and what to do — including which executable to run when `jason` is no longer on your PATH. On Windows an executable another program holds open — a scanner reading the one just run or just downloaded — is tried again for 10 seconds first; one still held then is named from `~`, with how long it was tried, and that refusal is retryable: the same command, once the file is free |
| `update_nothing_to_roll_back` | there is no record of an update, or the executable it replaced is no longer under `~/.jason/update/previous/` |
| `update_rollback_unsafe` | the executable was put back and the database was not: work has been recorded since the update, or whether any has cannot be told — no runtime is answering, or the update never reached a healthy runtime and so never recorded where the chronicle stood — and restoring the backup would erase it. "Restoring a database by hand" below is how to go the rest of the way, having decided you want to |
| `update_ledger_invalid` | there is a file at `~/.jason/update/ledger.json` and it is not a ledger this build can act on |

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
| macOS | a LaunchAgent labelled `ai.jason.runtime`, `RunAtLoad` and no `KeepAlive` | `~/Library/LaunchAgents/ai.jason.runtime.plist` |
| Linux | a `systemd --user` unit, enabled into your default target | `~/.config/systemd/user/jason.service` |

**It is registration, not supervision.** What starts at logon is `jason runtime run --detached --data-dir
<the directory you registered under>` — the runtime itself, in the background, on the data directory the
registration carries — and nothing watches it afterwards: a runtime that stops stays stopped until somebody
starts it. There is no restart policy, no `KeepAlive`, and no second process whose job is to keep the first one
alive.

**`enable` starts nothing now, and `disable` stops nothing.** They change what happens at the *next* logon.
Starting and stopping a runtime today is still `jason runtime start` and `jason runtime stop`.

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
manager edits, a `JASON_DATA_DIR` you export, a proxy variable — is not there. The data directory is why the
registration carries it explicitly; a plugin whose executable is only on a `PATH` your profile builds is the
other half, and it will be reported `unavailable` by a runtime that started at logon while working perfectly
from a terminal. `jason plugin list` from the same account says which.

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
> data directory, and `jason status` said the runtime is registered to start at logon. `disable` took the task
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
| this account's logon registration, on Windows including the task `\Jason` an earlier version registered for it | first, so a logon part-way through cannot start what is going |
| the runtime | and if it will not stop — or is running and does not answer — **nothing after this is removed** |
| on Windows, the notification registration | after the runtime, so its next toast cannot write it back: the AppUserModelID key the toast was shown under, and the two entries Windows made for it at the first toast. All three are this account's, under `HKEY_CURRENT_USER` |
| every recorded skill | by the paths its root's record names, digest by digest |
| the PATH entry | only where the installer wrote it and its directory holds nothing but Jason — the executable, its two notice files and what an upgrade left beside it. A PATH is a person's own document, and other programs are on it through a directory they share. On macOS and Linux the entry is the marked block in a login profile; on Windows, whose Path carries no marker, the entry in this account's Path |
| the executable, its notice files, its install directory and what it unpacked | last, because everything above runs from it. The notice files go by name, so in a directory other tools share a `LICENSE` that is not one of them stays. On Windows the running image cannot be deleted, so it is moved out of the install directory, to a directory on its own volume, and a cleanup removes the copy once the verb has exited; the native libraries the build unpacked on its first run go too |

A recorded file whose bytes have changed since is reported and kept, because the record says what Jason wrote
and a mismatch says somebody else wrote it afterwards. `--force` removes it anyway, and the containing
directory is not empty until it does.

**Where a package manager installed Jason** (§6), the last two steps are the manager's. The executable, the directory it
is in and the PATH entry that reaches it are left to the package manager, and so are the native libraries the executable
unpacked, which stay with it; the plan and the report name them as left to it, with the command that takes them off —
`brew uninstall jason`, or `winget uninstall Reply.Jason`. Everything above them goes as it always does, the runtime
stopped first, and the plan an agent reads carries the same as `left_to`.

**Never touched:** a skill there is no receipt for, including one copied into a harness by hand; any other
skill in that harness; any provider CLI; any model credential; a PATH entry the installer did not write; another
account's logon registration; the notifications already in the notification center, which hold counts only and
are the person's to dismiss.
**The data directory is kept** — §10 below says what is in it — unless `--purge-data` says otherwise, and in
the machine shape that word has to be said in advance with `--yes`, because there is nobody there to be asked
and a verb that blocked on an invisible question would hang for ever. With it, what Jason keeps in the data
directory goes and anything else stays; a data directory that holds a file system's root, the profile, the
temporary directory or the installation is refused before anything is removed.

It exits 0 when everything it set out to remove is gone, and 1 when it did not get there — it refused,
something is still there, or it stopped part-way — and every step that did happen is listed either way.

## 10. Where things live

The **data directory** is `~/.jason`, or whatever `JASON_DATA_DIR` names: the database, the configuration, the
plugins, the skills, the logs and the work directories. The **install directory** is wherever the executable
happens to be, and Jason writes nothing into it but the executable and its two notice files. They are separate on purpose — updating the program and keeping
your work are different things, and a person who installed the binary somewhere unusual should never find Jason
writing files beside it.

That is also why an update writes under the data directory, and beside the executable only the executable and its
two notice files. `~/.jason/update/`
holds the ledger, the staged download, the executable that was replaced and the copy of the applier that
replaced it; the install directory may be read-only, on another volume, or a directory somebody removes Jason
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
new version is slower than the next, and it is safe to delete when nothing is running.

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
opens the database: it renames files, asks the runtime questions and reads the backups directory.

Autostart is the CLI's too, under `Jason.Cli/Autostart/`. `AutostartArtifacts` composes each platform's document
and reads a command line back out of one, both as pure functions of what they are given; `WindowsTasks` decides,
one `schtasks` answer at a time, what happens to this account's task and to a `\Jason` 0.1.0 left, and what each
denial says; `IAutostartRegistrar` is the one place a process is started or a document written, and
`AutostartCodes` holds the two refusals §8 can raise:

| code | what it means |
| --- | --- |
| `autostart_unsupported` | this machine has no way to start something at logon that Jason knows about |
| `autostart_refused` | the tool that registers it refused, and the message carries what the tool itself said |
