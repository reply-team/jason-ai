# Jason Desktop

An Electron window onto the installation the `jason` command line uses. It is one more client of that installation, as
the browser UI is: it finds the data directory as the CLI does (`JASON_DATA_DIR`, otherwise `~/.jason`), shows the UI the
runtime serves at its loopback address, and changes the installation only through CLI verbs and the settings file a
person would edit.

## What it does, and through what

| Step | How |
| --- | --- |
| Know whether a runtime is up and whether it is this build | `jason status` |
| Start one when none is up | `jason runtime start` (never `jason runtime run` as a child of its own) |
| Install the UI | `jason ui install`, only when the runtime is the version the desktop carries and the `ui_module` check of `jason status` is not ok; an install that fails stops the start only when no UI is served at all |
| Deploy the skills | `jason skills install --roles-only`, `jason skills install --root <data>/desktop/chat/.claude/skills --host claude_code` and `jason skills install --root <data>/desktop/chat/.agents/skills --host codex`, all from the skills package the desktop carries |
| Load the window | the link `jason ui open --no-open` prints |
| Place a notification plugin | `jason plugin install <id>`, its settings written, then `jason plugin reload` and `jason plugin list` |
| Wire Reply after a sign-in (`reply auth login`, or `--with-token` with the key on standard input) | `reply auth status --json` for the profile, `jason plugin install reply`, its settings written, then `jason plugin reload` |
| Say whether Reply is wired | `jason plugin list` shows `reply` valid, and `jason route list` names it the global default |

When the runtime is another version, the UI it serves is loaded as it is and `setup.status()` says the versions differ
and names `jason update`. A `feed` in the desktop's settings is passed to every verb that takes `--feed`.

Claude Code's version is read with `claude --version` at each listing of the hosts and before each chat turn on it, never
remembered: a Claude Code older than 2.1.257 (`src/host-version.ts`), or one whose version cannot be read, is listed on
the Connect card and in the chat's host list with a sentence naming the version found, that floor and how to update it,
cannot be chosen there, and has every chat turn sent to it refused before the host starts, a key provider's included.

## Its own files

- `<data>/desktop/settings.json`: the desktop's settings (`feed`, and the version whose skills were last deployed).
- `<data>/desktop/chat/`: the chat's working folder; its skills, Jason's and the Reply ones, are in `.claude/skills/` for
  Claude Code and in `.agents/skills/` for Codex. Its `CLAUDE.md` and `AGENTS.md` are the application's own instruction
  text, which the desktop writes again when it starts and before each turn, so an edit made to either does not last.
- `<data>/desktop/provider-keys`: AI providers' keys, encrypted by the operating system (`safeStorage`). On Linux a key
  is kept and read only where the session offers a keyring (GNOME Keyring or KWallet): where Electron falls back to its
  `basic_text` store, a fixed key, the card says so and the key is not kept.
- `<data>/desktop/bundle/<version>/`: on Linux, an AppImage's copy of the `jason` it carries (see "On Linux").
- `<data>/desktop/electron/`: Electron's own files (its profile, the page's storage, and in `logs/` what Electron itself
  logs), set before anything else, so a second data directory is a second desktop and nothing of it lands outside
  `<data>`.
- `<data>/logs/desktop-YYYYMMDD[_NNN].jsonl`: its log, in the runtime log's shape and under the same limits
  (`Logging:RetainedFileCountLimit`, `Logging:FileSizeLimitBytes`). `jason logs --source desktop` reads it.

A Connect card also writes, into `<data>/config/settings.json`, the keys of the plugin it configures and no other:
`Plugins:Config:<id>`, `Plugins:Grants:<id>:Http` and `Notifications:Plugins:<id>:Binding`, each section made only when
a value is written into it. The `Http` grant is written only where there is none, or where the server or receiver it
derives from changed, so a grant you wrote yourself (a server of your own for ntfy, say) outlives a new token or topic.
The Reply card writes `Plugins:Grants:reply:Exec` (`["reply"]`, only where the grant does not already hold
`reply`, so one you widened is kept) and, only where the file names no `Routes:Default` at
all, `Routes:Default` with the profile signed in (`{"Plugin": "reply", "Binding": {"profile": "default"}}` unless
`reply auth status` names another), as the golden path writes them by hand; a default you set is kept. No other
`Routes` key is ever written, and every other key is kept as it was. Disconnect, on the `slack`, `telegram` and `ntfy`
cards once configured, removes `Plugins:Config:<id>` and the plugin's entry under `Notifications:Plugins:<id>` whole, so
the plugin is no longer a channel that is on; an entry whose `Enabled` is `false` keeps it and loses only its `Binding`.
The plugin's grant under `Plugins:Grants:<id>` stays, except the Http grant derived from a server of the plugin's own,
which goes with the server: an `Http` that is exactly the one host Configure derived from it, so a later Configure
derives it again; any other, one you widened included, stays. A section the removal leaves empty is dropped; one empty
before is kept. The package stays; `jason plugin reload` runs after, and `jason plugin list` only after a reload that
exits 0.

A file the desktop cannot parse is refused and left as it is: the runtime reads comments and trailing commas, and the
desktop, which writes plain JSON, will not rewrite them away. A target is checked against the pattern the plugin's
binding declares before anything is written, and an empty one is refused. A card with no values runs only
`jason plugin reload` and `jason plugin list`, and a reload that cannot read the settings file is what
`setup.status().settingsRefused` says.

| Plugin | Token | Target | Grant |
| --- | --- | --- | --- |
| `slack` | `Plugins:Config:slack:token` | `Notifications:Plugins:slack:Binding:channel` | `slack.com` |
| `telegram` | `Plugins:Config:telegram:token` | `Notifications:Plugins:telegram:Binding:chat` | `api.telegram.org` |
| `ntfy` | none: `NTFY_TOKEN` is read from the runtime's environment only | `Notifications:Plugins:ntfy:Binding:topic`, and `server` beside it for a server of your own | `ntfy.sh`, or that server's host |
| `journal-forwarder` | `Plugins:Config:journal-forwarder:token` | `Plugins:Config:journal-forwarder:url` | the receiver's host |

The desktop never writes an `Env` grant (ntfy's `NTFY_TOKEN`) or a `Subscriptions` grant (the journal kinds the
forwarder is admitted to); each card's note says where those are set.

## The window

- The page is the runtime's own, at `http://127.0.0.1:<port>`; navigation is locked to that origin. A link or
  `window.open` that leaves it goes to the system browser only when it is `https:`.
- `contextIsolation` on, `nodeIntegration` off, `sandbox` on; no webview attached, and of the permissions a page asks
  for only `clipboard-sanitized-write` is granted, to the runtime's origin alone, so a Copy button works.
- The preload exposes `window.jasonDesktop` and nothing else. Every call is answered only when it comes from the top frame
  of a page at the runtime's origin (`src/ipc.ts`); a subframe, another origin and a `file:` page are refused.

## Processes

Every process is started with a file and arguments, never through a shell, and only by the named modules:
`src/cli.ts` (the bundled `jason`), `src/programs.ts` (the agent hosts, `reply`, `node` and `npm`), `src/shell-path.ts`
(on macOS and Linux only, the login shell's PATH, read before anything is started) and `scripts/prepare.mjs` (`dotnet publish`).
On Windows, a program npm installed is a batch file; the desktop reads which script it hands to Node and starts that
with Node. `test/process-guard.test.ts` holds this, and also refuses a `require` of `child_process`, an IPC handler
registered anywhere but `src/ipc.ts` (on `ipcMain` or on a `webContents.ipc`), `shell.openPath`, and `shell.openExternal`
outside the navigation lock in `src/main.ts`.

What the desktop tells Reply about itself is one label in the User-Agent of reply-cli's requests and nothing else:
`REPLY_CLIENT=jason-harness/<the bundle's version, less any build metadata after a plus sign>`, set in the environment of
the `reply` the desktop starts and of every agent host's turn in place of any `REPLY_CLIENT` the desktop inherited, and
neither set nor passed on there by a build that carries no bundle version (`src/reply-client.ts`).

## Updating and taking it off

Before an update installs, the desktop stops the runtime its own `jason` runs as, with `jason runtime stop`, and goes no
further when that runtime does not stop, or when the run file names a runtime that did not answer while the process it
names still runs, or when the run file is there and cannot be read (`src/own-runtime.ts`); it asks only the data
directory it was started with.

On Windows, where PowerShell is available, the installer and the uninstaller also look for any process running from the
carried `jason.exe`, whichever data directory started it, and never close one (`installer/uninstall.nsh`): they stop it
through the application's own `--stop-runtime` start, which runs `jason runtime stop` for the data directory their
environment names, or the default one, and exits, and look again. A person running them is asked first; an update and a
silent run are not. A process still running, a runtime that did not stop or one started with another data directory say,
ends the install or the uninstall before a file is replaced, and a person is told to stop it with
`resources\bundle\jason.exe runtime stop` in the application's folder, with `JASON_DATA_DIR` set when it was started
with another data directory. Where PowerShell is not available nothing is looked for: a person is asked to stop the
runtime, and an update goes on without a look. A person's uninstall starts the application with `--uninstall`, which
stops the runtime it carries, takes off the PATH command's files that are its own, Open Jason at Login and the runtime's
start at logon when it starts this application's `jason`, and names what it leaves: the data directory, the harness root
and the updater's cache.

On macOS there is no installer, so nothing looks when Jason.app is moved to the Trash, and the desktop's own look before
an update is the whole guard. Before removing Jason.app, take off by hand what it placed:

1. In the Jason menu, turn off Open Jason at Login and Start the Runtime at Logon.
2. Run `jason runtime stop`.
3. In the same menu, under Command Line, choose Remove jason from the PATH (or Remove jason-desktop from the PATH, by the
   name it was put there with), then quit Jason.

What stays: the data directory (`~/.jason`, or the one `JASON_DATA_DIR` names), the harness root (`~/Jason`, or the one
`Harness:Root` names) and the updater's cache, `~/Library/Caches/jason-desktop-updater`.

## On Linux

- The window keeps its menu bar, which carries the tray's items, since a session may show no tray icon. Closing the
  window quits the application; the runtime keeps running. A start at sign-in opens the window minimized.
- Open Jason at Login writes `~/.config/autostart/ai.jasonharness.desktop.desktop` (under `$XDG_CONFIG_HOME` when that
  is set), whose `Exec` line starts the AppImage file or the installed executable with `--opened-at-login`; it reads as
  on only while that line is there and the entry is neither hidden nor marked `X-GNOME-Autostart-enabled=false`. A path
  the entry cannot spell, one with a `%` or a control character, leaves the item off. An update that installs the
  AppImage under a new file name moves an entry that is on to it.
- The Command Line submenu places a `jason` wrapper in `~/.local/bin`, as on macOS.
- An AppImage runs from a mount that lasts only while it runs, so each start copies its `jason` to
  `<data>/desktop/bundle/<version>/`, and that copy is the `jason` it runs, starts the runtime from, puts on the PATH and
  gives the chat. A copy already there is used only when every file matches the package's; one that differs, or a link
  at the bundles folder or the version's folder, stops the start and is named, to be removed by hand. Without a copy,
  Start the Runtime at Logon and the Command Line's acts are not offered. A start at logon or a PATH command the person
  set up from another version's copy is set up again from this one at each start, the same act as turning it on; one
  never set up, or one that names a `jason` elsewhere, is left alone. The copies of other versions go after that, but
  the one an update kept to go back to, one something set up still names, and one the running runtime was started
  from. No folder of the mount reaches what the desktop starts: the PATH and the libraries it hands on are the
  session's.
- Jason never runs without Chromium's sandbox. An AppImage's launcher, which electron-builder writes, adds
  `--no-sandbox` wherever the system refuses unprivileged user namespaces; a start that carries it, on any platform,
  is refused before anything else is decided: a dialog says so, one log line says it was refused, and the application
  quits (`src/sandbox.ts`). So the AppImage needs a system that lets it run its sandbox: on Ubuntu 24.04 and later, as
  shipped, it says so and names the deb, whose install brings an AppArmor profile that lets the program make the user
  namespaces its sandbox uses.
- Updates come to the AppImage and to the deb. An AppImage update keeps the AppImage file itself under
  `<data>/desktop/rollback/<version>/`; a deb update keeps no copy, and if the new version does not start, the page names
  the previous release's deb and `sudo apt install ./jason-desktop-linux-x64.deb`. Anywhere else the update item says
  updates come through the package. When the installer stops before it finishes (a refused password, say), Jason stays
  open and says so. The updater's cache is `~/.cache/jason-desktop-updater`, or under `$XDG_CACHE_HOME` when that is set.

## Building and testing

From `desktop/`:

```sh
npm ci
npm run check         # the type-check of the sources and the tests
npm test              # the Node tests
npm run prepare:bundle -- --pre rc.1   # publishes this checkout's jason into bundle/ as <VersionPrefix>-rc.1
npm run dev           # builds and starts the window
```

A desktop build always carries a candidate's version string (`0.34.0-rc.1`), never `dev`, so `jason ui install` and
`jason plugin install` resolve that candidate's own release directory. `test/candidate-version.test.ts` proves it against
a local feed when `JASON_UI_ZIP` names a UI build packaged with `JASON_VERSION` set to the candidate.

A package is built from `electron-builder.config.mjs` at the version `JASON_VERSION` names, a release's or a candidate's,
around a bundle prepared for each architecture at that same version. On Windows, unsigned:

```sh
node scripts/prepare.mjs --rid win-x64 --output bundles/x64 --version 0.34.0-rc.1
JASON_VERSION=0.34.0-rc.1 npm run package -- --win --x64   # dist/: the nsis installer, latest.yml, win-unpacked/
```

On Linux, where nothing is signed, the same two lines make the deb and the AppImage, and the feed that names both:

```sh
node scripts/prepare.mjs --rid linux-x64 --output bundles/x64 --version 0.35.0-rc.1
JASON_VERSION=0.35.0-rc.1 npm run package -- --linux --x64   # dist/: the deb, the AppImage, latest-linux.yml
```

The program is `jason-desktop-app`, so the deb's `/usr/bin` link never takes `jason-desktop`, the name the PATH command
falls back to. The AppImage is packed on electron-builder's static AppImage runtime, which needs no libfuse2. The two
forms are packed at once from one folder, and the deb writes `resources/package-type` into it, which makes
electron-updater update a package as a deb, so the configuration starts the deb only once the AppImage is built.

The release passes `--executable` with the `jason` it has already built and signed, so the package carries that very
file, and signs the package itself: a Mac package is never built unsigned. Inside `app.asar` are `lib/`, `shell/`,
`resources/`, the chat's two lists and its instruction text; beside it, in the package's `resources/`, are `bundle/`
(the executable with `LICENSE` and `THIRD-PARTY-NOTICES`, the skills and `version.json`) and `notices/` (the
repository's `LICENSE`, this folder's `THIRD-PARTY-NOTICES` and Chromium's `LICENSES.chromium.html`). The package
carries no UI: `jason ui install` places the UI of the runtime's own release.

electron-builder fetches Electron's distribution for each platform and architecture it packs. The configuration hands it
the sha256 of each file from the `checksums.json` the `electron` package carries, which `package-lock.json` pins, so a zip
that does not match, downloaded or cached, stops the pack; its own tools (nsis, the resource and icon tools, 7-Zip,
dmgbuild) it checks against the sha256 its own source names.

`scripts/check-package.mjs` reads a built package back against this configuration: the feed and, on Windows, the
publisher its `app-update.yml` names, the fuses its binary carries, and on Linux the updater the form makes: a deb must
carry `resources/package-type` saying `deb`, an AppImage none. The release runs it on every package it builds:

```sh
node scripts/check-package.mjs --app dist/win-unpacked --platform win32
node scripts/check-package.mjs --app <unpacked deb>/opt/Jason --platform linux --form deb
node scripts/check-package.mjs --app squashfs-root --platform linux --form appimage   # after the AppImage's --appimage-extract
```

To run it against an installation other than your own, set `JASON_DATA_DIR` and `JASON_Harness__Root` in the environment
it is started from.
