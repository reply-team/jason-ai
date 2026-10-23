# Developing Jason

For people working on Jason itself. This file is never published: the runtime's source it describes stays here.

You need the .NET SDK named in `global.json`, Node 24 for the UI, and git.

## Run a build of this checkout

Copy the block for your system and run it from the root of the checkout. It builds the executable, gives it a data
directory of its own, deploys the role skills from this checkout, builds the UI, and starts the runtime. The first line
names the folder everything goes into; change it there and nowhere else, for example one folder per branch.

**Linux**

```sh
export JASON_DEV=~/jason-dev && export JASON_DATA_DIR="$JASON_DEV/data" && \
dotnet publish runtime/src/Jason.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o "$JASON_DEV/bin" && \
"$JASON_DEV/bin/jason" skills install --roles-only --source . && \
(cd ui && npm ci && npm run build) && \
mkdir -p "$JASON_DATA_DIR/ui" && ln -sfn "$PWD/ui/dist" "$JASON_DATA_DIR/ui/custom" && \
"$JASON_DEV/bin/jason" runtime start
```

**macOS** (Apple silicon)

```sh
export JASON_DEV=~/jason-dev && export JASON_DATA_DIR="$JASON_DEV/data" && \
dotnet publish runtime/src/Jason.App -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o "$JASON_DEV/bin" && \
"$JASON_DEV/bin/jason" skills install --roles-only --source . && \
(cd ui && npm ci && npm run build) && \
mkdir -p "$JASON_DATA_DIR/ui" && ln -sfn "$PWD/ui/dist" "$JASON_DATA_DIR/ui/custom" && \
"$JASON_DEV/bin/jason" runtime start
```

**Windows** (Command Prompt)

```bat
set JASON_DEV=%USERPROFILE%\jason-dev
set JASON_DATA_DIR=%JASON_DEV%\data
dotnet publish runtime/src/Jason.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o "%JASON_DEV%\bin" && ^
"%JASON_DEV%\bin\jason.exe" skills install --roles-only --source . && ^
npm --prefix ui ci && npm --prefix ui run build && ^
(rmdir "%JASON_DATA_DIR%\ui\custom" 2>nul & mkdir "%JASON_DATA_DIR%\ui" 2>nul & mklink /J "%JASON_DATA_DIR%\ui\custom" "%CD%\ui\dist") && ^
"%JASON_DEV%\bin\jason.exe" runtime start
```

The two `set` lines stay on lines of their own: cmd reads a whole line before it runs any of it, so a variable set
inside a chain is not yet set where the chain uses it. `mklink /J` makes a junction, which needs no administrator
rights; `rmdir` on a junction removes the link, never `ui\dist`.

What it leaves you with:

- **Its own data, never `~/.jason`.** A development build migrates the database it finds; a release may then refuse
  to open it. `JASON_DATA_DIR` lasts only in that terminal: in a new one, set the two variables again (the first
  line, or the two `set` lines on Windows) before running the build as `$JASON_DEV/bin/jason`
  (`%JASON_DEV%\bin\jason.exe` on Windows).
- **The role skills as this checkout has them**, uncommitted edits included. `jason skills update` reads the same
  checkout again. Without `--roles-only` the interactive and business packs go into your agent's own configuration
  (`~/.claude/skills` for Claude Code), which an installed release shares.
- **The UI served from `ui/dist`**, linked as the data directory's `ui/custom/`, which the runtime serves before
  anything else. `npm run build` writes the `manifest.json` the runtime asks for, so every later build is served by the
  next request; while you work on the UI, `npm run dev` in `ui/` rebuilds on every change.
- **The runtime on port 52766**, or the next free one if an installed runtime already has it. `jason status` lists the
  rest; its `path` check fails, because this build is not on your PATH, on purpose.

After pulling changes, stop the runtime (`jason runtime stop`, run as above) and run the block again: the executable
cannot be replaced while it runs on Windows, and the data directory moves forward with the new build. To go back to an
older build, start from a new folder.

To stop and remove it all: `jason runtime stop`, run as above, then delete the folder.

## Commands

- Build: `dotnet build runtime/Jason.slnx`
- Test everything: `dotnet test --solution runtime/Jason.slnx`
- Test one project: `dotnet test --project runtime/tests/Jason.Runtime.Tests`
- Test one method or class: append `-- --filter-method "*MethodName"` or `-- --filter-class "Namespace.ClassName"`
- Run without publishing (with `JASON_DATA_DIR` set as above): `dotnet run --project runtime/src/Jason.App -- --version`
  · `-- runtime run` · `-- runtime status` · `-- runtime start` (background; nothing is registered until you ask)
  · `-- runtime stop` · `-- runtime autostart enable | disable | status` · `-- campaign list` · `-- workitem list`
  · `-- role list` · `-- plugin list` · `-- plugin reload` · `-- ui open`
- Add a migration: `dotnet tool restore` once, then
  `dotnet ef migrations add <Name> --project runtime/src/Jason.Runtime --startup-project runtime/src/Jason.Runtime --output-dir Persistence/Migrations`
- Publish: `dotnet publish runtime/src/Jason.App -c Release -r <rid> --self-contained -p:PublishSingleFile=true`
- UI: from `ui/`, `npm ci`, `npm run check`, `npm test`, `npm run build`, `npm run package`, `npm run e2e` (the last
  needs a Release build of `Jason.App`).

## How a change lands

The conventions the code follows — the layout, the boundaries the build enforces, the terminology, the rules of the
API, of persistence and of the logs — are in `CLAUDE.md`, which every AI session reads and which holds for people just
the same. This section is the part of the process that no test runs for you. It describes the flow from the release
after 0.14.0 on; where the pipeline still runs by the earlier rule, the paragraph says so.

**Two kinds of branch.** `main` is what has been released: it changes when a release branch merges, and the tag
follows that merge (`docs/release-and-update.md` is the contract). A release branch, `release/<name>`, carries one
release's scope: a business increment heavy enough to be tried as a whole and to have release notes of its own, cut by
urgency or by kind, never a polish. Several release branches run at once. Each is led by one session and owned by one
person, who scopes it with the architecture session; the branches share nothing until a release. There is no cadence:
the goal reached closes the release. A feature branch is cut from its release branch and its pull request targets that
release branch. When a release merges, every other release branch takes `main` then, and conflicts are resolved there
and nowhere else. A release branch takes `main` by a fast-forward, or by a rebase pushed straight to the branch by its
owner, never through a pull request into the branch, because the release's rebase into `main` must find `main`'s own
commits in the branch. The ruleset on the release branches lets a repository admin push to them for this act alone, and
the push's run is the Linux set and gates nothing.

**One pull request per increment.** A branch carries one change: a feature, a fix, a document, a skill's text. Two
independent things are two pull requests, even when they were written in one sitting. When the second builds on the
first, its branch is cut from the first's and rebased onto the release branch once the first has merged; it is
reviewed as the difference from the first and merges after it. The reason is the review: a reader who finds a problem
in one half must not hold up the other, and the commit on the release branch should say one thing. A pull request that
has grown two things is split before it is marked ready, not after a review has found it.

**The branch is named after the story it implements**, `REPLY-<key>-<slug>`, the key first. That name is the one
place a tracker key is allowed: it is how the tracker links the pull request, and it never reaches a release branch or
`main`. A pull request with no story of its own — the version after a release, a change to the pipeline, a
document — is named after the standing story kept for such changes.

**Tests by stage.** A commit runs the critical tier locally before it is pushed — the tests that carry the `Critical`
trait, about two minutes on a workstation — and never the whole suite, which takes seventeen minutes there and hours
over a pull request's life. A pull request that touches `runtime/` runs the whole suite locally once before it is
marked ready, in Release, with the log written to a file; one that changes Markdown only skips that. On CI, a draft's
push runs the Linux set: the version check, the linux-x64 build and the critical tier, an update on linux-x64, the
install script on Ubuntu and the UI's checks; it keeps no record of the tree it tested. Marking a pull request into a
release branch ready runs the Linux set with the critical tier on linux-x64; the whole suite runs once per release, on
the release's pull request, and locally before ready for a change under `runtime/`.
The pull request from a release branch into `main` runs every check on all three operating systems, macOS included,
and that run is the release's gate; a pull request into `main` from any other branch — a document, a change to the
pipeline — runs the Linux set and reports the rest as skipped by rule. The version pull request into `main` runs the
critical tier alone, on linux-x64 and under the `linux-x64` name a ruleset requires, since for it the tier is the ready
run. A push to `main` or to a release branch whose tree a passing run recorded runs the version check alone, and so does
a push to `main` whose change against its parent is documentation alone. Nothing runs
nightly. The critical tier is the `Critical`
trait, run with `--filter-trait "Category=Critical"`, and one table pins it: how many critical cases each family has in
each test assembly (`CriticalTier.cs`). Each test assembly discovers itself and holds its column to the table, so a row
that leaves the tier, which a run of the tier cannot notice, turns that guard red; every family keeps a critical case;
the tier stays at or under 15 % of the suite's methods and of its discovered cases, with no floor; and every mark gives
its reason, the attribute's own or a `// Critical:` comment above a marked row or above the member that marks rows.
The guard also holds each assembly to at least the test methods the table measured, and to nine tenths of its cases,
since the ceiling is measured on them; so a commit that marks or unmarks a test, adds or removes a row of a critical
theory, or removes test methods rewrites the table in the same commit, from the numbers the guard prints. A draft's
leg that tests the tier alone is named `linux-x64 (critical tier)`, so only a ready run reports the `linux-x64` that a
ruleset requires. `CiRunsTests` holds the rule the workflow carries.

**Build on a branch, open the pull request as a draft when the work is done.** A branch is pushed as the work goes, and
no pull request is opened while it is built: the critical tier runs locally for each commit before it is pushed, and a
pushed branch without a pull request runs nothing on CI, where an open draft would run the Linux set on every push.
When the work is done the pull request opens as a draft, and opening it runs the Linux set once, whose test leg is the
critical tier on linux-x64. Then come the two readings below and the fixes they asked for, as commits on the draft.
Mark it ready once, after that, and the ready run is the Linux set with the critical tier on linux-x64; the whole suite
runs once per release, on the release's pull request into `main`, and locally before ready for a change under
`runtime/`. On the release's pull request, a push after ready that changes nothing but documentation since the tree
the last full run tested — Markdown outside `runtime/`, `.github/` and `plugins/` that no build compiles and no test
runs as code — runs the Linux set and reports the rest as skipped; any other push after ready runs the full run again. The ruleset on a release branch lets a pull request
merge only when the Linux set reports green at the head that merges; the ruleset on `main` waits for every check.

**Two readers at ready.** Before a pull request into a release branch is marked ready it is read twice: by the session
that leads the release, which knows the scope and what the release's other pull requests do, and by a reviewer with no
part in the change, started fresh for this reading, whose model and effort follow the size of the change — a document
reads cheaply, a migration, a contract or a change to a guard reads at the highest effort. Each records in the pull
request what it found and that it approves; what was found is fixed before ready. The pull request merges into its
release branch on both approvals and a green run, and the session that leads the release merges it. Nothing is
reviewed on commits; a complex commit may be read by a peer when the lead asks.

**The release review.** The pull request from a release branch into `main` is read by the architecture session alone,
its sub-agents reading the diff by blocks: the release against its scope and the direction, the seams, the hygiene of
what the release publishes, the skills' statuses. It does not repeat the two readings each pull request had. The owner
then walks the release on a real machine with the session that led it; what the walk finds lands as pull requests into
the release branch under the rules above, and the three-platform run follows the patches. What that run finds is fixed
in the release branch too: `main` takes only a green head, and the owner merges it.

**Seams are read early.** Some paths are shared by every release branch and by every reader of the product: the
operation documents and the catalog under `docs/contracts/`, a migration under
`runtime/src/Jason.Runtime/Persistence/Migrations/`, the open set in `.github/publish/`, a skill's craft block or
readings row under `skills/runtime/`, the release workflow and the public files, the version in
`runtime/Directory.Build.props`. A pull request that touches one says so to the architecture session in one line when
it opens, and the session reads that part then rather than twenty pull requests later; a release's scope names the
seams it will touch at the start. One release branch owns the schema at a time, and a migration is regenerated on the
release branch after it takes `main`, never merged by hand.

**The version.** A release branch sets its version when its release pull request opens — the number after the last
tag — and the branch that merges second sets its own again then. The pull request into `main` is marked ready with the
bare version, so the suite that gates the release runs under the version the tag will carry. After a release, the pull
request that moves `main` to the next version carries the version prefix and every pin the version guards hold to it,
and nothing else, and is squash-merged. The release's own flow opens it right after the tag, as the release's last
act on `main`, so that `main` holds the next version between releases and every release branch inherits it when it
takes `main`. It has one reading and no second reader, since it changes no behaviour.

**The squash commit is the pull request.** A release branch takes one squash commit per pull request, and that
commit's message is the pull request's title and body; the messages of the branch's own commits never reach it. Commit
on the branch as often as you like, and write the title as the one line `git log` will show and the body for a reader
who was not there: what changed, why, how it was verified. The release's pull request into `main` merges by rebase,
so `main` keeps one commit per pull request of the release, in order, and its tree is the one the three-platform run
tested.

**Nothing internal in what reaches a release branch or `main`.** Commit messages and pull request bodies carry no
tracker key, no link to a private system, no pointer to a plan or a note outside the repository, and no tooling
attribution trailer: an AI assistant adds `Co-Authored-By` and session trailers by default, and they are stripped
before the push. The reasoning a key would have pointed at is written into the body instead. The internal part — the
discussion, the alternatives, who decided what — goes to the story's comments in the tracker. The trace runs one way,
from the tracker to GitHub. Everything `.github/publish/open-set.txt` lists is read publicly at each release, and
`PublicationTests` holds its links.

**A red run is read before it is re-run.** A job that fails on a tree a green run already covered is a candidate
defect — a timing assumption, a file still held, an order a test relied on — until its log says otherwise. It is
diagnosed from the log first and re-run after, never re-run to see whether it goes away. The release's record counts
the full runs each pull request needed until it was green; the target is one, and a pull request that needed more is
an incident with a cause named, not a number.

**Tests and fixtures use made-up data.** Never a real person's name, a real login, a real profile link, a real mailbox
or a live provider identifier: not in a test, a fixture, a document, a pull request body or a commit message. A key or
a token of any kind reaches no file of this repository, and no log, state or setting Jason writes.

**A release walks a real machine once.** Install, uninstall, update, autostart, `jason status`, the deployment of
skills and the UI's installation are each right alone and wrong together more easily than a test shows. The owner runs
the release's scenario by hand, on a machine or under an account the build did not run under, on the release branch
after the architecture session's review, with the session that led the release; what it finds is fixed as pull
requests into the release branch before the three-platform run. A single pull request is walked only when a fact has
to be found to build it at all, on the lead's request.

**A skill's `verified` row is earned, never written.** A skill marked `verified` was read, as that exact text, by a
named agent host driving this runtime, and the digest of what it read is recorded. A change to its text, however
small, makes it a draft again until a host reads the new text; nobody writes the row by hand, and the reading is its
own act after the merge, not part of the pull request. `skills/runtime/README.md` says which skills stand where.

**Plans, designs and scratch files stay out of the repository.** A note written while working is kept beside the
checkout, not in it. What has lasting value is written where a reader will look for it: `docs/` for a contract, the
README of the area for how it is used.

**The pipeline and the rulesets change together.** A job renamed, added or removed in `.github/workflows/ci.yml` is a
check a ruleset still waits for, or never asked for; the ruleset on `main` and the one on `release/*` are changed in
the same breath, or every merge blocks.

**Merging and releasing.** A pull request into a release branch merges when its Linux run is green, both readers have
approved and no review asks for changes; squash is the only merge method there, and the branch is deleted at the
merge. The release's pull request into `main` merges when the three-platform run is green at its head and the
architecture session's review is done, by rebase, by the owner; the tag follows, and the release workflow does the
rest: `docs/release-and-update.md` is the contract.
