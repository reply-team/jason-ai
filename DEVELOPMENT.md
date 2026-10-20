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
the same. This section is the part of the process that no test runs for you.

**One pull request per increment.** A branch carries one change: a feature, a fix, a document, a skill's text. Two
independent things are two pull requests, even when they were written in one sitting. When the second builds on the
first, its branch is cut from the first's and rebased onto `main` once the first has merged; it is reviewed as the
difference from the first and merges after it. The reason is the review: a reader who finds a problem in one half must
not hold up the other, and the commit on `main` should say one thing. A pull request that has grown two things is
split before it is marked ready, not after a review has found it.

**The branch is named after the story it implements**, `REPLY-<key>-<slug>`, the key first. That name is the one
place a tracker key is allowed: it is how the tracker links the pull request, and it never reaches `main`.

**Draft while you build, ready when you are done.** Open the pull request as a draft with the first push and keep
pushing to it. A draft runs the Linux part of the pipeline on each push — the version check, the linux-x64 build and
suite, an update on linux-x64, the install script on Ubuntu and the UI's checks — so a draft is cheap. Marking it
ready for review runs the whole pipeline but macOS, whose check reports as skipped, and so does every push after that,
unless the push changes nothing but documentation since the tree the last whole run tested — Markdown outside
`runtime/`, `.github/` and `plugins/` that no build compiles and no test runs as code — and then it runs the Linux part
and reports the rest as skipped. A push to `main` runs the whole pipeline but macOS, unless its tree is the one the
pull request's last whole run tested, and then only the version check runs. macOS runs on the nightly run on `main`,
when `main` has moved since the last one that passed, on a run of CI started by hand, and at a release. The ruleset
lets a pull request merge only when every job of the pipeline reports green at the head that merges, so mark it ready
once, when the change is complete and its description is written.

**The squash commit is the pull request.** `main` takes one squash commit per pull request, and that commit's message
is the pull request's title and body; the messages of the branch's own commits never reach `main`. Commit on the
branch as often as you like, and write the title as the one line `git log` will show and the body for a reader who
was not there: what changed, why, how it was verified.

**Nothing internal in what reaches `main`.** Commit messages and pull request bodies carry no tracker key, no link to
a private system, no pointer to a plan or a note outside the repository, and no tooling attribution trailer: an AI
assistant adds `Co-Authored-By` and session trailers by default, and they are stripped before the push. The reasoning
a key would have pointed at is written into the body instead. The internal part — the discussion, the alternatives,
who decided what — goes to the story's comments in the tracker. The trace runs one way, from the tracker to GitHub.
Everything `.github/publish/open-set.txt` lists is read publicly at each release, and `PublicationTests` holds its
links.

**A red run is read before it is re-run.** A job that fails on a tree a green run already covered is a candidate
defect — a timing assumption, a file still held, an order a test relied on — until its log says otherwise. It is
diagnosed from the log first and re-run after, never re-run to see whether it goes away.

**Tests and fixtures use made-up data.** Never a real person's name, a real login, a real profile link, a real mailbox
or a live provider identifier: not in a test, a fixture, a document, a pull request body or a commit message. A key or
a token of any kind reaches no file of this repository, and no log, state or setting Jason writes.

**A change to the installed product walks a real machine before the pull request leaves draft.** Install, uninstall,
update, autostart, `jason status`, the deployment of skills and the UI's installation are each right alone and wrong
together more easily than a test shows. The whole flow is run by hand, on a machine or under an account the build did
not run under, and what it finds is fixed before the pull request is marked ready. When it finds more than a couple of
blockers in code already on `main`, their fixes land as a pull request of their own first and the feature follows:
the cost of a review grows with the square of the diff.

**A skill's `verified` row is earned, never written.** A skill marked `verified` was read, as that exact text, by a
named agent host driving this runtime, and the digest of what it read is recorded. A change to its text, however
small, makes it a draft again until a host reads the new text; nobody writes the row by hand, and the reading is its
own act after the merge, not part of the pull request. `skills/runtime/README.md` says which skills stand where.

**Plans, designs and scratch files stay out of the repository.** A note written while working is kept beside the
checkout, not in it. What has lasting value is written where a reader will look for it: `docs/` for a contract, the
README of the area for how it is used.

**The pipeline and the ruleset change together.** A job renamed, added or removed in `.github/workflows/ci.yml` is a
check the ruleset on `main` still waits for, or never asked for; the ruleset is changed in the same breath, or every
merge blocks.

**Merging and releasing.** A pull request merges when its ready run is green on every job and no review asks for
changes; squash is the only merge method the repository allows, and the branch is deleted at the merge. A release is a
tag on `main`, and the release workflow does the rest: `docs/release-and-update.md` is the contract.
