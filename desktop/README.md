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
| Deploy the skills | `jason skills install --roles-only` and `jason skills install --root <data>/desktop/chat --host claude_code`, both from the skills package the desktop carries |
| Load the window | the link `jason ui open --no-open` prints |
| Place a notification plugin | `jason plugin install <id>`, its settings written, then `jason plugin reload` and `jason plugin list` |
| Wire Reply after a sign-in (`reply auth login`, or `--with-token` with the key on standard input) | `reply auth status --json` for the profile, `jason plugin install reply`, its settings written, then `jason plugin reload` |
| Say whether Reply is wired | `jason plugin list` shows `reply` valid, and `jason route list` names it the global default |

When the runtime is another version, the UI it serves is loaded as it is and `setup.status()` says the versions differ
and names `jason update`. A `feed` in the desktop's settings is passed to every verb that takes `--feed`.

## Its own files

- `<data>/desktop/settings.json`: the desktop's settings (`feed`, and the version whose skills were last deployed).
- `<data>/desktop/chat/`: the chat's working folder, where the skills for it are deployed.
- `<data>/desktop/provider-keys`: AI providers' keys, encrypted by the operating system (`safeStorage`).
- `<data>/desktop/electron/`: Electron's own files (its profile, the page's storage, and in `logs/` what Electron itself
  logs), set before anything else, so a second data directory is a second desktop and nothing of it lands outside
  `<data>`.
- `<data>/logs/desktop-YYYYMMDD[_NNN].jsonl`: its log, in the runtime log's shape and under the same limits
  (`Logging:RetainedFileCountLimit`, `Logging:FileSizeLimitBytes`). `jason logs --source desktop` reads it.

A Connect card also writes, into `<data>/config/settings.json`, the keys of the plugin it configures and no other:
`Plugins:Config:<id>`, `Plugins:Grants:<id>:Http` and `Notifications:Plugins:<id>:Binding`, each section made only when
a value is written into it. The `Http` grant is written only where there is none, or where the server or receiver it
derives from changed, so a grant you wrote yourself (a server of your own for ntfy, say) outlives a new token or topic.
The Reply card writes `Plugins:Grants:reply:Exec` (`["reply"]`) and, only where the file names no `Routes:Default` at
all, `Routes:Default` with the profile signed in (`{"Plugin": "reply", "Binding": {"profile": "default"}}` unless
`reply auth status` names another), as the golden path writes them by hand; a default you set is kept. No other
`Routes` key is ever written, and every other key is kept as it was.

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
(on macOS only, the login shell's PATH, read before anything is started) and `scripts/prepare.mjs` (`dotnet publish`).
On Windows, a program npm installed is a batch file; the desktop reads which script it hands to Node and starts that
with Node. `test/process-guard.test.ts` holds this, and also refuses a `require` of `child_process`, an IPC handler
registered anywhere but `src/ipc.ts` (on `ipcMain` or on a `webContents.ipc`), `shell.openPath`, and `shell.openExternal`
outside the navigation lock in `src/main.ts`.

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

To run it against an installation other than your own, set `JASON_DATA_DIR` and `JASON_Harness__Root` in the environment
it is started from.
