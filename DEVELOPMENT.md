# Developing Jason

For people working on Jason itself: building and running this checkout, the commands, and how a change lands. To use
Jason, install a release instead ([docs/INSTALL.md](docs/INSTALL.md)): nothing here is built, cloned or tested for that.

You need the .NET SDK named in `global.json`, Node 24 for the UI, and git.

## Run a build of this checkout

Copy the block for your system and run it from the root of the checkout. It builds the executable, gives it a data
directory of its own, deploys the role skills from this checkout, builds the UI, and starts the runtime. The first line
names the folder everything goes into; change it there and nowhere else, for example one folder per branch.

**Linux**

```sh
export JASON_DEV=~/jason-dev && export JASON_DATA_DIR="$JASON_DEV/data" && \
dotnet publish dotnet/src/Jason.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o "$JASON_DEV/bin" && \
"$JASON_DEV/bin/jason" skills install --roles-only --source . && \
(cd ui && npm ci && npm run build) && \
mkdir -p "$JASON_DATA_DIR/ui" && ln -sfn "$PWD/ui/dist" "$JASON_DATA_DIR/ui/custom" && \
"$JASON_DEV/bin/jason" runtime start
```

**macOS** (Apple silicon)

```sh
export JASON_DEV=~/jason-dev && export JASON_DATA_DIR="$JASON_DEV/data" && \
dotnet publish dotnet/src/Jason.App -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o "$JASON_DEV/bin" && \
"$JASON_DEV/bin/jason" skills install --roles-only --source . && \
(cd ui && npm ci && npm run build) && \
mkdir -p "$JASON_DATA_DIR/ui" && ln -sfn "$PWD/ui/dist" "$JASON_DATA_DIR/ui/custom" && \
"$JASON_DEV/bin/jason" runtime start
```

**Windows** (Command Prompt)

```bat
set JASON_DEV=%USERPROFILE%\jason-dev
set JASON_DATA_DIR=%JASON_DEV%\data
dotnet publish dotnet/src/Jason.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o "%JASON_DEV%\bin" && ^
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
  (each detected host's skills root), which an installed release shares.
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

- Build: `dotnet build dotnet/Jason.slnx`
- Test everything: `dotnet test --solution dotnet/Jason.slnx`
- Test one project: `dotnet test --project dotnet/tests/Jason.Runtime.Tests`
- Test one method or class: append `-- --filter-method "*MethodName"` or `-- --filter-class "Namespace.ClassName"`
- Start a long pass (the whole suite, the install-script classes) from a terminal you keep open, or as a background task
  of that terminal, never through a hidden window or a detached launcher: started that way, whole passes have failed the
  install-script tests whose PowerShell 7 children hang for their full two-minute bound and print nothing, and the same
  tests pass attached. A one-test run under a hidden launcher passes, so the cause is not pinned down; the product starts
  its own children with no window and no shell, and a child so made ran under hidden and detached drivers without a hang.
- Run without publishing (with `JASON_DATA_DIR` set as above): `dotnet run --project dotnet/src/Jason.App -- --version`
  · `-- runtime run` · `-- runtime status` · `-- runtime start` (background; nothing is registered until you ask)
  · `-- runtime stop` · `-- runtime autostart enable | disable | status` · `-- campaign list` · `-- workitem list`
  · `-- role list` · `-- plugin list` · `-- plugin reload` · `-- ui open`
- Add a migration: `dotnet tool restore` once, then
  `dotnet ef migrations add <Name> --project dotnet/src/Jason.Runtime --startup-project dotnet/src/Jason.Runtime --output-dir Persistence/Migrations`
- Publish: `dotnet publish dotnet/src/Jason.App -c Release -r <rid> --self-contained -p:PublishSingleFile=true`
- UI: from `ui/`, `npm ci`, `npm run check`, `npm test`, `npm run build`, `npm run package`, `npm run e2e` (the last
  needs a Release build of `Jason.App`).
- Desktop: from `desktop/`, `npm ci`, `npm run check`, `npm test`; `desktop/README.md` says how its bundle is prepared
  and the window started.

## How a change lands

The conventions the code follows — the layout, the boundaries the build enforces, the terminology, the rules of the
API, of persistence and of the logs — are in `CLAUDE.md`, which every AI session reads and which holds for people just
the same. This section is how a change travels from a branch to a release.

**Two kinds of branch.** `main` is what has been released: it changes when a release merges, and the release's tag
follows that merge. A release branch, `release/<name>`, carries one release's scope, and several can run at once. A
change is made on a feature branch cut from its release branch, and its pull request targets that release branch.
When no release branch is open, a change starts as an issue or a discussion; `CONTRIBUTING.md` says where.

**One pull request per increment.** A branch carries one change: a feature, a fix, a document, a skill's text. Two
independent things are two pull requests, so that a problem found in one half does not hold up the other, and the
commit the change leaves says one thing.

**Squash merges.** A pull request into a release branch is squash-merged: the release branch takes one commit per pull
request, and that commit's message is the pull request's title and body, written for a reader who was not there —
what changed, why, how it was verified. The release's own pull request into `main` is squash-merged too, so `main` takes
one commit per release, and after it the one that moves `main` to the next version.

**What CI runs, by stage.** Before a push, the critical tier is run locally: the tests that carry the `Critical` trait,
run with `--filter-trait "Category=Critical"`, about two minutes on a workstation. A pull request into
a release branch runs the Linux set — the version check, the linux-x64 build with the critical tier, the install script
on Ubuntu, an update on linux-x64, the UI's checks and the desktop's — and merges on green; a change under `dotnet/`
has run the whole suite locally before that. The release's pull request into `main` runs every check, the whole suite included, on Linux,
Windows and macOS, and the release merges only when that run is green. At the tag, the release workflow runs the
critical tier again and packages and smoke-tests the executable for each platform; it does not run the whole suite
again, since the tag is the tree that run tested. `.github/workflows/ci.yml` says which run each event gets,
`CiRunsTests` holds the workflow to that rule, and `docs/release-and-update.md` is the release's contract.

**Nothing internal in what reaches this repository.** Every branch here is public. Commit messages and pull request
bodies carry no tracker key, no link to a private system, no pointer to a plan or a note outside the repository, and no
tooling attribution trailer: an AI assistant adds `Co-Authored-By` and other attribution trailers by default, and they
are stripped before the push. The reasoning a key would have pointed at is written into the body instead. A branch may
be named after the tracker story it implements; that name is the one place a key appears, and it never reaches a commit
of a release branch or of `main`. `RepositoryHygieneTests` reads every file of the tree and its name for these, and
the `hygiene.yml` workflow reads every pull request's added lines, the names it gives files, its title and its body.

**Tests and fixtures use made-up data.** Never a real person's name, a real login, a real profile link, a real mailbox
or a live provider identifier: not in a test, a fixture, a document, a pull request body or a commit message. A key or
a token of any kind reaches no file of this repository, and no log, state or setting Jason writes.

**A red run is read before it is re-run.** A job that fails on a tree a green run already covered is a candidate
defect — a timing assumption, a file still held, an order a test relied on — until its log says otherwise. It is
diagnosed from the log first and re-run after, never re-run to see whether it goes away.

**Plans, designs and scratch files stay out of the repository.** A note written while working is kept beside the
checkout, not in it. What has lasting value is written where a reader will look for it: `docs/` for a contract, the
README of the area for how it is used.

**The pipeline and the rulesets change together.** A job renamed, added or removed in `.github/workflows/ci.yml` is a
check a ruleset still waits for, or never asked for; the ruleset on `main` and the one on `release/*` are changed in
the same breath, or every merge blocks.

**The numbers in old commit subjects.** Until 0.24.0, Jason was developed in a private repository, and its whole
history came here with it. A commit made before 0.24.0 whose subject ends in "(#N)" names a pull request of that
repository. GitHub links "(#N)" in a subject to the issue or pull request of this repository that has the number, so in
those commits the link leads to something unrelated. From 0.24.0 on, the numbers are this repository's own.

**The tags before 0.24.0.** The tags `v0.1.0` to `v0.23.0` point at snapshots of the open part published here at
those versions; the releases, from `v0.7.0`, and their assets are unchanged.
