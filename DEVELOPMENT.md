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
