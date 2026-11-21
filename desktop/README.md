# Jason Desktop

An Electron window onto the installation the `jason` command line uses. It is one more client of that installation, as
the browser UI is: it finds the data directory as the CLI does (`JASON_DATA_DIR`, otherwise `~/.jason`), shows the UI the
runtime serves at its loopback address, and changes the installation only through CLI verbs and the settings file a
person would edit.

This page is the builder's view. What the desktop is for a person, how it is installed, updated and taken off on each
system, and what its chat may run are in [docs/desktop.md](../docs/desktop.md), which carries the sentences of this page
that a person needs.

## What it does, and through what

| Step | How |
| --- | --- |
| Set the machine up, on macOS and Windows, at a windowed start of a version it has not yet completed for, in Demo as in Live, before Live brings the installation up; again on Put Back jason and the Skills (`src/machine-setup.ts`) | `jason install`, with `--terminal-path` naming the login shell's own PATH (the empty value where it printed none) on macOS and nothing on Windows; never `--roles`, so nothing is written under the data directory but the desktop's record `<data>/desktop/machine-setup.json`; then `jason update apply --finish`, after every run of `jason install` but one refused as `install_in_progress` or `update_in_progress`, which are later, no failure and no record; the record is written only when both completed (the finish's document `complete`, or an update's `step` at `complete`); once a start, both with no time limit, Live waiting for them at most thirty seconds and a runtime start refused meanwhile made again once they ended; a runtime that answered before them (`jason runtime status`, asked only where `<data>/run/runtime.json` is there), or the note `<data>/run/start-owed.json`, and none after them, whatever they reported, is started again with `jason runtime start`, in Demo as in Live and after Put Back, a refused start read again before it is said to have failed; the Command Line item shows the report as facts, saying that a new terminal finds `jason` only where the report's `command.placed` is true |
| Pay the note that a start is owed, at every windowed start where no set-up runs (`payOwedStart` in `src/machine-setup.ts`) | where `<data>/run/start-owed.json` stands and `jason runtime status` finds none answering, `jason runtime start` from the carried `jason`; on Linux in a Demo start only, from the deb's `jason` or the AppImage's copy; the desktop never writes or removes the note |
| Stop the runtime for the installer (`--stop-runtime`, `src/uninstall-start.ts`) | `jason runtime stop --start-owed`, always, so the next start owes a runtime; a person's own stop never passes it |
| Put back a missing carried `jason.exe` on Windows (`src/carried-restore.ts`) | at a windowed start, before the set-up: a stored image under `%LOCALAPPDATA%\jason-desktop-updater\jason-carried\<request>\` whose `jason.version` names the version the bundle's `version.json` names, copied through a file beside the target |
| Know whether a runtime is up and whether it is this build | `jason status` |
| Start one when none is up | `jason runtime start` (never `jason runtime run` as a child of its own) |
| Install the UI | `jason ui install`, only when the runtime is the version the desktop carries and the `ui_module` check of `jason status` is not ok; an install that fails stops the start only when no UI is served at all |
| Deploy the skills | `jason skills install --roles-only`, `jason skills install --root <data>/desktop/chat/.claude/skills --host claude_code` and `jason skills install --root <data>/desktop/chat/.agents/skills --host codex`, all from the skills package the desktop carries |
| Show the demo | `demo/index.html`, the page the application carries ([`demo/README.md`](demo/README.md)), in a sealed view of the window with the desktop's own Demo \| Live switch in a second view over it (`src/demo-page.ts`, `src/demo-window.ts`); no `jason` is run, no runtime started, no folder of an installation made and no host name resolved |
| Load the window | the link `jason ui open --no-open` prints |
| Place a notification plugin | `jason plugin install <id>`, its settings written, then `jason plugin reload` and `jason plugin list` |
| Wire Reply after a sign-in (`reply auth login`, or `--with-token` with the key on standard input) | `reply auth status --json` for the profile, `jason plugin install reply`, its settings written, then `jason plugin reload` |
| Say whether Reply is wired | `jason plugin list` shows `reply` valid, and `jason route list` names it the global default |
| Put the agents' roles on the AI the chat runs on, in Live, at a start once the runtime is up and as each chat turn starts (`Setup.connectRoles`, `src/roles-profile.ts`) | `jason profile list --include-disabled --limit 2`; where it lists none and its `default_execution_profile` names none, the mark in the desktop's own settings, `jason profile create claude-code --host claude_code --program <found>` (or `codex --host codex`), the id it answered added to the mark, then `jason profile list --include-disabled --limit 1` and, where its `default_execution_profile` still names none, `jason profile set-default claude-code --reason <the desktop's fixed reason>`; a look or a naming the runtime does not answer is left to the next turn, a naming it refuses (or took and did not answer in time) to the next start, which lists first; where it lists one profile alone with the mark's id, the look and the naming alone; a mark with no id or another is let go (`src/roles-moments.ts` wires the two moments) |
| Say which host the roles run on | `jason profile list --include-disabled --limit 1` for the default in force (`default_execution_profile`), then `jason profile get <that name>`: its host and whether it is enabled; said under Claude Code or Codex only where found and at its floor |
| Act on a Reply account, on the card's ask | one verb of `reply account` each (`src/reply-cli-account.ts`): `connect` (a LinkedIn account by the typed name, a mailbox by its provider), `reconnect`, `pending` (also read with the accounts), `limits` (a mailbox's daily limit), `pause` and `resume` (LinkedIn), `unblock` (a mailbox) and `delete`, each with `--json` |

When the runtime is another version, the UI it serves is loaded as it is and `setup.status()` says the versions differ
and names `jason update`. A `feed` in the desktop's settings is passed to every verb that takes `--feed`.

Claude Code's version is read with `claude --version` at each listing of the hosts and before each chat turn on it, never
remembered: a Claude Code older than 2.1.257 (`src/host-version.ts`), or one whose version cannot be read, is listed on
the Connect card and in the chat's host list with a sentence naming the version found, that floor and how to update it,
cannot be chosen there, and has every chat turn sent to it refused before the host starts, a key provider's included.
From 2.1.285 on (`noChromeFrom`) a turn that does not run in Full access also passes `--no-chrome`: Anthropic's
changelog does not say which version added the flag, and 2.1.285 is the first whose entry names it, so below it that
turn's settings alone keep the browser out.

Codex's version is read with `codex --version` before each chat turn on it, never remembered: a Codex older than 0.122.0
(`codexFloor`), the first whose `exec` takes `--ignore-user-config`, or one whose version cannot be read, has the turn
refused before Codex starts, with a sentence that says to update Codex.

Connect names the person's own MCP servers under Claude Code and Codex found here (`src/own-servers.ts`): the keys of the
top-level `mcpServers` of Claude Code's `.claude.json` (in `CLAUDE_CONFIG_DIR` where that is set, else in the home), and the
servers of Codex's `config.toml` (in `CODEX_HOME`, else `~/.codex`) less one set `enabled = false`, read by a narrow reader
of its own rather than a TOML package. Both are read from the desktop's own environment, so a desktop started from the
Dock or the Start menu without a `CLAUDE_CONFIG_DIR` that the person's shell sets names the servers of the home's file
instead; a wrong guess names other servers and harms nothing. Only the main process reads the files, whole, and only the
names leave them, each made one line, its unseen characters taken out and drawn in forty characters at most, on
`setup.status()`'s host, which a paired browser tab reads too. A file that is not there lists nothing; one that cannot be
read or parsed lists nothing either and is logged as `own_servers_unread` with the program and the kind alone. No file
can fail or hang the listing: only a regular file is opened, none over 64 MiB is read, and one nested more than 64 deep
or naming more than 1,000 servers lists nothing. Each file's last answer, its names or its kind and never its text, is
remembered in the main process by the size and modification time `stat` gave before it was opened; while both are
unchanged a listing opens nothing, a file gone lists nothing at once, and the log line is written when a program's kind
changes, not at every listing. No host command is run to list them: `claude mcp list` connects to the servers, and
`codex mcp list` can reach the network.

On Windows every read-only Codex turn names the elevated sandbox for the run (`codexWindowsSandbox`): the person's own
choice of sandbox is in the config.toml the turn leaves unread, and a command in the unelevated one can read the data
directory. A Full access turn runs in no sandbox, so it names none and is not checked. Naming it proves nothing
(OpenAI's page says a fall to the unelevated sandbox can happen where the elevated one is not set up), so the first
read-only Codex turn of each run of the desktop checks it (`codex-sandbox.ts`), in the turn's own folder. One cmd line prints a random
marker, then a marker of each file's own only if `type` opens it, the file itself going to `nul`. The first file is a
control, the system's hosts file under `SystemRoot`, which every Windows account may read and the desktop does not
own: it opening in the sandbox shows that `type` and `nul` work there at all. Then the runtime's run file, which has an
owner-only access list of its own, and the desktop's settings file, which keeps the access list the data directory
inherits from the profile, as the database and the logs do; the desktop writes that file at each start when it is
missing (`settingsAtStart`). Each path is quoted, and a path holding
`, ; = % ^ & | < > !` or a quote is not checked, with a sentence that says so. cmd reads the line from
`JASON_SANDBOX_CHECK`, so every run sees the same bytes, and runs it three times, each within thirty seconds: by its
full path outside any sandbox, where it must print every marker; inside `codex sandbox`, with the turn's own `-c`
settings and its read-only mode, where it must print the first marker and the control's alone; and outside once more, every marker
again. Anything else proves nothing, and a read marker standing as a line of its own inside refuses. `codex sandbox`
is tried without a platform word, as the help of 0.167.0-alpha.3.2 shows it (`test/fixtures`), and, when that printed
no marker, as `codex sandbox windows`, the shape the reference names for older versions. The page says "Checking
Codex's sandbox…" while it runs. The answer is kept by Codex's file, the script it is handed, its version and
`CODEX_HOME` until the desktop quits; a check that proved nothing, or found no run file or no settings file, runs again
at the next send. A turn on the OpenAI key runs under a Codex home of the desktop's own, where the setup may not reach,
so it is refused with a sentence of its own: sign in to Codex, or chat on Claude Code. The check runs with the person's
config.toml plus `-c` while the turn leaves that file unread, since `codex sandbox` has no `--ignore-user-config`, and
the desktop does not read the file to say what in it would differ. A set-up chat's naming run on Windows names no
sandbox kind and gets no check: its shell tool is off, and it is held by the read-only sandbox and those settings alone.

**In Full access a Claude Code turn on the person's own Claude Code sign-in uses the person's own Claude Code
configuration, as a terminal does: their skills, hooks, MCP servers and rules; on a provider's key set in Jason, and in
the other modes, it uses none of it.** A turn that runs in Full access passes no
`--setting-sources`, `--strict-mcp-config` or `--no-chrome`, and its settings carry no server defences
(`claudeArguments`, `hookedSettings`), so the person's own settings with their rules and hooks, their skills, the shell
commands a skill carries included, their MCP servers, their claude.ai connectors and the browser integration work in
the chat as in a terminal, with no block of reads outside the working folders. `--permission-mode` is named every time, so
the person's `defaultMode` never decides the mode; their deny and ask rules keep working, an ask a card, and their hooks
run beside the turn's, which still sees every tool the model asks for; a hook of the person's own that rewrites a
tool's input is their configuration acting. Their hooks run in a Full access turn even where their `disableAllHooks`
switches them off, since the turn's settings set it to `false` to keep Jason's check on; and an HTTP hook of theirs runs
there only where their own `allowedHttpHookUrls` names its address, since the turn's list, merged with theirs, is never
empty. A hook or a rule a Full access
turn leaves in the person's Claude Code configuration acts in every later Full access turn and in their own terminal.
The settings file of every turn the hook rides sets, as `env`, what the desktop decides of the turn's environment
(`pinnedEnvironment`): PATH, the data directory, `JASON_CHAT_TURN`, the folder and background variables, the label for
reply-cli, and empty values for what it withholds, a session's markers among them: every name `hostEnvironment` sets or
withholds by name but the scrub and a provider's variables, which a row holds to what it does. Claude Code writes a settings file's `env` over the environment
it was started with, and the turn's file outranks the person's and a project's. A PATH set under `env` in the person's
Claude Code settings does not apply in the chat; the chat runs with the PATH Jason Desktop started with, Jason's own
folder first. Nor does a value there of another of those variables. Every other turn keeps the sealed command
line whole, the line following the mode the turn runs in (`turnMode`), never the one the chat shows: a turn on a
provider's key in every mode, Full access included (`ClaudeTurn.keyed`, its settings keeping the server defences); Plan
only, Ask first and Auto-edit, and the turn of a Full access chat that runs in Ask first, below the floor of Full access, from a
browser tab, as root, started again after Claude Code refused its bypass or started the turn in another mode, or once
the hook was found filtered out. That line is `--setting-sources project`, `--strict-mcp-config`
with the turn's MCP file alone, `--no-chrome` as above, and settings that carry `disableClaudeAiConnectors: true`, deny
the browser's server by name (`deniedMcpServers: [{ "serverName": "claude-in-chrome" }]`) and turn off a skill's shell
commands (`disableSkillShellExecution: true`): `serverDefences`, in `claudeTurnSettings`, `planTurnSettings` and the
hooked settings of Ask first and Auto-edit. In every mode the turn carries its own `--mcp-config`: the prompt tool, on a
server named for that turn alone, `jason_desktop_` and a nonce (`newServerName`), so no server of the person's, the
fixed name an earlier version used included, answers a card, and a tool of any server whose name begins
`jason_desktop_` is refused to the agent (`isOwnTool`). A turn on a provider's key keeps the sealed line and runs with
the desktop's own `CLAUDE_CONFIG_DIR` (`keyedFolder`), so the person's configuration does not load there either. Every Codex turn outside
Full access, Plan only included, runs with `--ignore-user-config`: the chat does not read your Codex config.toml, so a
provider or profile set there is not used; connect a key on the Connect card. Nor are the MCP servers it names loaded,
since a Codex turn asks no one before such a tool runs. Nor are the person's ChatGPT apps (connectors), which
config.toml does not hold: every Codex turn sets `features.apps=false` (`codexConnectorsOff`), as the naming run does.
The page says the sentence under the composer while Codex is the chat's host outside Full access. Its sign-in is still
read from `CODEX_HOME`. A Full access Codex turn reads the person's config.toml, as Codex in a terminal does, its MCP
servers, providers and profiles with it; it still sets the cached search and `features.apps=false` for the run
(`codexFullAccessSettings`). One on a provider's key reads the config.toml of the desktop's own `CODEX_HOME`
(`keyedFolder`) in its place, so none of the person's, as a keyed Claude Code turn loads none of theirs.

The Reply card's acts are the Reply CLI's `account` verbs and nothing else; the card offers them only from the Reply CLI
that has them (`replyMinimumVersion`), and below it says to update the Reply CLI and offers nothing. What the CLI prints
and the codes it ends with are read in one module, `src/reply-cli-account.ts`, held to a fixture taken from the CLI's
own tests (`test/reply-cli-account-fixture.ts`); what the desktop does with each code is `src/reply-accounts.ts`. A link
the CLI makes for connecting or reconnecting an account is the person's secret: the main process checks it (https, a
host name, no user, port or anything but printable ASCII) and hands it to the system browser through the navigation
lock's one way out, or, where the page asked for a copy and the link is one that expires and connects one account,
writes it to the clipboard itself. Nothing else of the desktop holds it. The page learns that a link was opened or
copied, when it stops working and its pending entry; the CLI's output for these verbs is never logged, recorded or put
into an error. A limit's change, a pause, a resume, an unblock and a delete ask the person first in a native box of the
main process that names the account as the list does (a connect and a reconnect make a link and ask nothing), with
Cancel focused; no window to ask on is a no, a box whose window is hidden or closed is closed and read as a no, and
`delete` passes `--yes` only after the yes. One offer opens only after the code that calls for it, for the ask it
answered and behind a box of its own: a forced delete (`--force`) after `linkedin.account_in_use`. A Reply that makes no
one-mailbox link answers `email_link.not_available`, which is the plain refusal and offers nothing else. A refusal is
one sentence of the desktop's, chosen by the CLI's code; a code the desktop does not act on, known or not, is said by
one sentence that carries it, and nothing of the CLI's own words is shown or logged.

## Its own files

- `<data>/desktop/settings.json`: the desktop's settings (`feed`, the version whose skills were last deployed, and
  `roles_profile_pending`, the name of the profile `connectRoles` is making and, once its create succeeded, its id, only
  between its mark and the runtime's naming of the default).
- `<data>/desktop/machine-setup.json`: on macOS and Windows, `ran_for`, the version `jason install` last completed for,
  `ran_from`, the carried `jason` it ran from, and `report`, the document it printed then; a start runs the set-up again
  where either differs, and a record that names no place, an earlier desktop's, counts as another place. A refusal as
  `install_vanishing_place`, an application opened from its disk image or translocated, runs no finishing step, pays no
  start that is owed and writes no record; one of the records `isNewInstallation` counts as the desktop's own. `jason
  uninstall` that keeps the data removes it, since it takes off what the set-up placed.
- `<data>/desktop/chats/<conversation id>/`: a conversation's own working folder (`folder: 'own'` on its record), where
  every turn of it runs, in every mode. It is made at the conversation's first turn. Removing the conversation moves it,
  one rename, to `chat-full-ended/<conversation id>-<moment>/`, where the sweep removes it seven days later; so does a
  start, for a folder no record names, but only when every record could be read: a record that cannot be read names no
  folder, and then none moves. A folder that cannot be moved, one a terminal holds on Windows, stays where it is, nothing
  in it removed, for a later start; on macOS and Linux a terminal open in it holds nothing, and the rename takes it along; nothing is moved out of a `chats` that is a link or a junction, and no turn starts in
  one. Before each turn the desktop copies the recorded skills of the shared folder into it, removing whatever stands at
  a name there, a link included, before it writes, and writes the instruction files there.
- `<data>/desktop/chat/`: the shared working folder, where the turns of a conversation an earlier desktop made (no
  `folder` on its record) still run; its skills, Jason's and the Reply ones, are in `.claude/skills/` for
  Claude Code and in `.agents/skills/` for Codex. Its `CLAUDE.md` and `AGENTS.md` are the application's own instruction
  text, which the desktop writes again when it starts and before each turn, so an edit made to either does not last.
- `<data>/desktop/conversations/`: the chat's conversations, one record each (see below).
- `<data>/desktop/conversations-stamped`: written once every record carries the shape this desktop writes, which no
  earlier desktop reads or writes (see below).
- `<data>/desktop/models.json`: the models Codex offered when last asked, on the person's sign-in and on the OpenAI
  key (see "Models and effort").
- `<data>/desktop/provider-keys`: AI providers' keys, encrypted by the operating system (`safeStorage`). On Linux a key
  is kept and read only where the session offers a keyring (GNOME Keyring or KWallet): where Electron falls back to its
  `basic_text` store, a fixed key, the card says so and the key is not kept.
- `<data>/desktop/claude-keyed/<provider id>/` and `<data>/desktop/codex-keyed/`: the folders a chat turn on a
  provider's key runs its host with, as `CLAUDE_CONFIG_DIR` and `CODEX_HOME`, made `0700` where the system has modes.
  No claude.ai login and no saved Codex login are in them, and a keyed turn is started without the variables that name
  another credential, a profile or a federation; the design is that a key the host does not take fails the turn instead
  of running it on the person's own plan, as built and not yet seen with a real host. A Claude Console profile kept under
  the person's own home is not moved by the folder; the key in the environment ranks above it. The key itself is never
  written there: it reaches the host in its environment alone (`CODEX_API_KEY` for Codex), but in Full access, and on
  Linux in every mode, where a Claude Code turn's commands carry it too (no scrub, `src/chat-hosts.ts`), a command that
  prints it leaves it in the transcript. No Full access turn sets `CLAUDE_CODE_SUBPROCESS_ENV_SCRUB`, nor passes on one
  the desktop inherited, since Claude Code starts in its default mode while it is set ("Permission mode forced to
  default"): in Full access a key and the credentials the turn carries reach the commands the agent runs, as in a
  terminal session. Each folder keeps the transcripts of its provider's turns, one Claude Code folder per provider since
  a conversation keeps one session per provider.
- How a turn on a key ends. On Anthropic's own key, a turn whose host says the key came from elsewhere is stopped. On a
  key run through Claude Code (Anthropic, DeepSeek, Kimi, GLM, MiniMax), a retry or an end the host reports for a
  refused key ends the turn at once with one sentence naming the provider's card in Connect: a 401 or a failed
  authentication says the key was refused, a 403 alone that the key or the request was. On the OpenAI key, run through
  Codex, Codex's own words end the turn, since OpenAI does not say how its events name a refused key. On every keyed
  turn of either host, the host's error text passes one function that replaces any part of the key, and any token
  shaped as a provider's key, by `[key]` before the page shows it or the conversation keeps it.
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
`Routes` key is ever written, and every other key is kept as it was. `Roles:DefaultExecutionProfile` is not the
desktop's to write: it asks the runtime to name the default with `jason profile set-default`, which the runtime writes
into this file itself, only where the runtime answers no default in force and only after the profile it names was made
(see the table above). Disconnect, on the `slack`, `telegram` and `ntfy` cards once configured, removes `Plugins:Config:<id>` and the plugin's entry under `Notifications:Plugins:<id>` whole, so
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

## The chat's conversations

A campaign has one conversation, found when it is opened and made at the person's first message or choice in it (the
page asks for `create` then, never when it only draws the agent's Chat), and the chat may hold any number of set-up
chats, which are about no campaign yet. Each is a record, `<data>/desktop/conversations/conv_<id>.json`: the campaign
it is about, its title, the session each agent host named for it (and, beside it in `plain_sessions`, whether the last
turn to name or answer with that session was a plain one, on the person's own Claude Code or Codex in any mode: only
such a session is offered to Open in's "Continue this chat"), and what the page drew, kept as text and never as
markup (the person's words, the agent's, the steps, each card with its answer, the errors). A record is written beside
its name and renamed over it, readable by this account alone, never through a link, when a host first names its session
and at the end of each turn. It is kept within 2 MB: each text of an entry is cut to 128 KB, keeping its end after a
mark in the desktop's words, then the oldest entries are dropped and counted, and the newest entry always stays. An
entry of a kind this desktop does not know, written by another version, is skipped and counted the same way. A file in
the folder that is not a record is counted in the log and left as it was. On a Claude Code turn the desktop's file
rules deny the agent a read or an edit of the folder; a Codex turn outside Full access is held only by its own
read-only sandbox, which the desktop does not narrow, so it may read the records, and one in Full access by nothing,
so it may write them.

Every record this desktop writes ends with `shape`, the shape of its records (`recordShape`). An earlier desktop writes
a record again with only the fields it knows: it drops `shape`, `folder` and every `mode` but Plan only. The first
start that finds records with no `shape` takes each as it is, one with no `mode` in Full access, writes it again with
its shape and then writes `conversations-stamped`, only once every one of them was written again and only where the
folder was listed and every file in it read: a start that could not write one, list the folder or read a file in it
writes no file, at no later write either, so the next start is a first start again. With no record at all in a folder
that was listed, the first record written writes that file first; a first start that found no folder writes it at the
next start, which lists the folder the first made. A start that finds the file lost is a first start again. From then on a record with no `shape` is one an
earlier desktop wrote last: it opens in Ask first, unless it is in Plan only, says why once when it is opened, and is
then written again with its mode and its shape. A record with no `folder` whose `chats/<id>/` stands as a folder is
that conversation's again, so its turns run there and its host's session is resumed there, or recapped when the host
does not find it.

- **The campaign comes from the record.** Every turn begins with a paragraph the desktop writes: the person, as
  `human:<account>` by the name the operating system gives this account (`os.userInfo().username`, which the carried
  `jason`'s `Environment.UserName` reads too), in backticks, with the single-quoted `--actor` a command gives where it
  holds a space; a name of anything but letters, digits, dots, underscores, hyphens and spaces, longer than the
  runtime's 100 units or with white space at either end is never given, and a sentence says to ask instead; then which
  campaign the conversation is about, its name quoted on one line, with every control, format and separator character
  dropped and cut at 120 characters, and its folder; or that it is about no campaign yet. The person's words follow it
  as they are. A turn names its conversation and its host, never a campaign, and a turn sent to a conversation the
  desktop did not make or keep is refused.
- **A session per host.** A turn resumes the session its host named for the conversation; a session id begins with a
  letter or a digit, and a record's that does not is read as none. When that host has none, or its session is not
  found, another answers, or the resume fails before saying anything, the turn starts a fresh session given a recap, the
  last of the person's and the agent's words, at most 16 KB, set apart as earlier messages, and the page is told why
  (`fresh`). The record gives up its session only once the fresh session has said something.
- **The hand-over.** A set-up chat's turn reads every page of `jason campaign list --all-projects --include-archived`
  before and after it; a campaign counts as new when it was absent before, is present after, was created at or after
  the turn's start, and has no conversation. Exactly one new campaign, from a turn no other turn of any conversation ran
  beside, makes the chat that campaign's conversation (`attached`); any other count above none, or a turn another
  overlapped, is offered to the person to pick from (`pick`, kept with the record), and `chat.attach` takes only a
  campaign the pick named. Nothing is handed over when a page of the list could not be read. A campaign a person makes
  by hand while a set-up turn runs cannot be told apart from one the turn made.
- **The Feed.** One conversation is of its own kind: the Feed, the person's main chat, about no campaign, marked `main`
  in its record, made and written the first time the page asks for it (`chat.feed`) and drawn first in the sidebar. The
  page hands it each line of the journal's stream whose kind it takes: `inbound_reply_observed`, `approval_requested`,
  `decision_raised`, `campaign_created`, `campaign_started`, `campaign_paused`, `workitem_failed`, `workitem_expired`,
  `provider_blocked`, `approval_approved`, `approval_rejected`, `decision_answered` and `campaign_archived`, each a kind
  the runtime writes itself (a test reads them from the runtime's source), and `workitem_succeeded` for two things alone:
  a send the provider took (sent or scheduled; the operation `message.send`, `conversation.reply` or `social_invitation.send`) and the finished
  work of a role that is shipped and shown (`planner`, `researcher`, `copywriter`, `analyst` or
  `deliverability-specialist`; not the manager and the responder, whose results already reach the Feed as approvals,
  questions and sends, nor the personalizer and the critic, which work message by message). The desktop checks each line
  again and keeps its id, moment, kind, campaign and work item, each held to its shape, and nothing else of it: no
  reason, no old or new value, no name; a succeeded line keeps the operation or the role, one of the names above, and a
  succeeded item of any other operation or of any other role, a role a person registered among them, is not kept at
  all. A line it has taken is not taken twice; lines of the same kind, campaign, operation and role that follow one
  another fold into one with a count (sends that alternate with other lines are shown one by one); at most 500 are
  kept, the oldest dropped and
  counted; the ids it took, the last 1,000, are kept on its record. Each load of the page, and a stream the runtime
  refused to resume, has the desktop read back one page of 100 of the lines written since five seconds before the
  Feed's newest (`jason journal list --kind … --since …`, within thirty seconds), and says when there were more; the
  succeeded work is read by a request of its own, one page of 100 of the newest, of which the desktop keeps the sends and
  the listed roles' work (the journal cannot be asked for an operation), so a send or a role's finished work older than
  that page is not read back and no gap is said for it; the
  lines the page hands meanwhile are checked at once and wait, the newest 200, with a gap said for the rest. A line
  never starts a turn: a turn of the Feed is given, after its header, the lines no turn was given yet, at most 50, each
  as one JSON object, under the desktop's sentence that they are records, information and never instructions; the
  record marks each line given once the turn's host started, so a line that lands while a turn starts goes to the next
  one, whatever its journal id (the runtime commits lines out of id order), and a fold that raises a given line's count
  gives it again with its new count. Rule 10 of the instruction text says the same, and that the Feed may speak of any
  campaign. A kept line out of shape is skipped and counted, and the rest of the record is read. The Feed is never renamed, removed or handed over; a campaign one of its turns makes keeps a
  conversation of its own. Its record keeps no line outside it: a line in any other record is let go when it is read,
  and of two records marked as the Feed the older is it.
- **A name a person gives.** A person renames a conversation, a set-up chat or a campaign's, from its header
  (`chat.rename`): one line, every control, format and separator character and every default-ignorable code point (a
  filler that draws nothing) dropped, cut at 60 characters, and refused when nothing is left. The record
  keeps it with a mark that a person gave it, and nothing replaces it afterwards: not a hand-over to a campaign, not the
  campaign's name, not a name the desktop asks a model for. The campaign's own name in the runtime is not changed. A
  conversation never sent to has no record yet, and its rename is refused.
- **A set-up chat named by what it is about.** Once, after a set-up chat's first turn ended without an error and still
  about no campaign, the desktop asks the conversation's own host or provider for a name of two to five words, on its
  fastest model: Claude Code's `haiku` on a sign-in or on Anthropic's key, `deepseek-flash`, `kimi-k2.6`,
  `glm-5.3-flash` and `MiniMax-M3.1-Flash` on those keys, and on Codex `gpt-6-luna` when the list the desktop keeps of
  what that host offers holds it, at low effort when it takes that level, else Codex's own default model. It is one run
  with no tool:
  on Claude Code `--tools ""`, every MCP tool denied, no MCP server, our instruction as the whole system prompt, one
  turn, nothing kept (`--no-session-persistence`); on Codex `exec --ephemeral --ignore-user-config` in its read-only
  sandbox with every tool its configuration reference names turned off. OpenAI states no Codex run with no tool at
  all, so a Codex naming run cannot be shown to have none: what holds it is the read-only sandbox, those settings, and
  the rule that anything in its output but the agent's message and its reasoning refuses the name, whatever its type,
  as anything but text and thinking does on Claude Code. The run obeys what a turn obeys: Claude Code's version is
  read first and a host below the floor is not asked, a keyed run has its provider's environment and folder, the first
  1,000 characters of the message go on standard input as one JSON string, never on the command line, and an inherited
  `CLAUDE_CODE_EFFORT_LEVEL` is withheld. It runs in an empty folder under `<data>/desktop/turns/`, removed after, one
  run at a time across the desktop, ended at 30 seconds, and ended as a stop ends a turn's host when the chat's turns
  end (the window closes or the desktop quits), its folder removed with it; one that waits then never starts. What
  comes back is untrusted: it is kept only as two to five plain words of letters and digits, each beginning with a
  letter or a digit, and anything else, a step, an error or the bound keeps the first words of the
  message, with nothing said on the page. A name the model gives is the desktop's: a person's rename still replaces
  it, and so does a later hand-over to a campaign. **Naming costs one small request of the person's plan or key per new
  set-up chat**: on a sign-in it counts against the plan, on a key it is billed to that key. A Codex naming run costs
  more than Claude Code's: it keeps Codex's built-in instructions, and when the kept list does not offer Codex's fast
  model it runs on Codex's default model at its default reasoning effort. The log says how each run ended in one word.
- **Not a boundary.** The instruction text asks the agent to keep to the conversation's campaign and to create at most
  one campaign in a set-up chat; a command that names another campaign is not refused for being in this conversation.
  What a turn may do is the permission model's to decide, as before.
- **Web pages and searches.** On Claude Code a search needs no card, and neither does a documentation page of four
  hosts: Claude Code's under `https://code.claude.com/docs/`, OpenAI's under `https://developers.openai.com/codex/`,
  `/api/docs/` and `/api/reference/` and under `https://learn.chatgpt.com/docs/`, and Reply's on `https://docs.reply.io/`
  (`documentationPages` in `src/chat-permission-model.ts`), over https, with no query string, no path segment that
  begins with an underscore, and at most 300 characters. Every other page asks, a page of Claude Code's own preapproved
  documentation set included. On Codex every turn searches OpenAI's cached index with no card and opens no page live;
  the person's own search setting is set aside for the run, also when they had turned search off.
- A conversation whose turn runs is not removed, started fresh or attached; each is refused with a sentence.
  "Start fresh" drops the hosts' sessions and keeps the record. `chat.list` and `chat.open` say whether a
  conversation's turn runs. When the page is loaded again while a turn runs, the turn shows running with Stop, and a
  card that waited is drawn as ended, since the record keeps no request to answer it by. A conversation is kept from its first turn: one made and never sent to is not listed and
  leaves no record, and starting it fresh writes nothing.
- **A refusal is a value.** Each chat call the desktop refuses answers `{ refused }` with its sentence alone, and a
  call with nothing to answer answers null; only what is no refusal rejects. Electron would put "Error invoking remote
  method" and the channel before a sentence thrown across the seam.
- **What never carries one.** `jason doctor` carries none of it, its count and size included. The desktop's log says
  how many were read, how many files were not records, a record's error code when one could not be written, how many
  new campaigns a set-up turn found and why a turn started fresh, and nothing else. `jason export` carries no part of
  `<data>/desktop/`, so conversations do not travel to another machine, nor do the transcripts of turns on a provider's
  key. `jason uninstall --purge-data` removes both with the data directory; the transcripts the agent hosts keep of the
  chat's other turns are theirs, in their own folders, and are never touched.

## The chat's access modes

Each conversation has a mode, chosen from the menu beside the composer (`chat.setMode`, `src/chat-modes.ts`). The mode
applies to the turns a person starts by sending, from the next one on.

- **Ask first** is chosen from the menu, and a turn of a chat in Full access runs in it where Full access cannot apply
  (below). Claude Code starts with `--permission-mode default`, named every time so that no `defaultMode` of a settings
  file decides it (`permissionModes`); what the permission model does not settle is a card. From Claude Code's floor of
  Full access on, the turn carries Full access's hook as well, which answers a refusal of the three kinds or nothing
  (below), so a command an allow rule lets run with no card, one fed the settings file by a redirection say, is refused
  before it runs; its settings block no read (`hookedSettings`), and the permission model judges it by what the hook
  refuses (`ChatScope.hooked`):
  - a read anywhere, by Read, Glob, Grep, a listing or the host's own read-only commands, runs with no card; a walk of
    a folder that holds the data directory is refused by the hook with the walk's sentence (below), before the
    permission model is asked about it;
  - a change outside the chat's folders is one card, "Change a file outside the chat's folders", for Write and Edit
    alike, an append among them, a `~` of the home folder taken as the file tools take it; inside them as before;
  - every `jason` verb runs with no card but the forms of `askingCardedForms`: `uninstall`, `import`, `export`,
    `update rollback` and `plugin grant` in every form, and a verb that changes the installation in a form that names
    another source, place, ref or version, while its plain form, which takes the release's own, runs with no card:
    `update apply` with `--version` or `--feed`; `plugin install` with `--feed` or `--replace`; `skills install` and
    `skills update` with `--source`, `--feed`, `--root` or `--ref`; `ui install` with `--feed`; and `install` with
    `--retire`. The program or the command a role will be run by is such a source too: `profile create` and
    `profile update` keep a card with `--program`, `--cli-command` or `--file`. Each is a card, its words told in any
    letter case and with anything between them (`holdsEntry`), its option anywhere among them, with its value after `=`
    or `:` too, and in a compound; the help of one of them, a `--help` or `-h` before any `--`, runs;
  - a `jason` whose words do not begin with a verb of `cliCommandTree`, the CLI's own tree held to
    `CliApp.BuildRootCommand` by DesktopDenyListTests, or with a noun's help, is a card (`namesCliVerb`): a word before
    the noun that the parser takes, the root's own name or a directive, and the executable's modes that are no verb
    (`runtime run`, `plugin-host`, `toast-host`, `host-hook`, a link); Full access runs them, as it runs any program;
  - jason's own `--help`, `-h` and `--version`, each alone, and any noun's or verb's help run with no card; any other
    option before the verb is refused (`optionRefusal`);
  - a pipeline or a sequence whose every part is such a `jason` or a program of `noCardFilters` (`jq`, `grep`, `head`,
    `tail`, `wc`, `sort`, `uniq` with one file at most, `cut`, `tr`, `cat`, `ls`, `echo`), each with only the options of
    its own table (`optionTables`: none that descends, reads a file it is told of on its input or by another file,
    writes a file or starts a program; jq with `-r`, `-c`, `-e`, `-s`, `-j`, `-S`, `-M`, `-a`, `--arg` and `--argjson`
    alone) and, in a command of more than one part, as a reader of standard input that names no file (`ls` aside), read
    literally, a letter outside ASCII taken in a single-quoted body, with no directory change, no file named like one of
    its programs in the turn's folder and nothing redirected into a file, runs with no card (`hookedNoCard`); anything
    else is a card, as before.

  Below the floor no hook rides the turn, and the host is given no allow rule for a form of a verb that
  reads a request's content from a file or from standard input (`requestFileVerbs`, held to the CLI's command tree by
  `DesktopDenyListTests`): each reaches the permission tool, which refuses a redirection from the installation's data
  and runs its plain form with no card, as the host's rule would (`plainForm` in `src/chat-permission-model.ts`): one
  `jason` command of the allow list, its input nothing, a here-document or one file in the turn's folders. Anything else
  is a card, as before. Codex runs in its read-only sandbox with `JASON_CONFINED` set: it cannot ask before it
  acts, so Ask first on Codex reads and proposes, and the menu says so (`codexModeSays` in the page).
- **Plan only** reads and proposes and changes nothing. Claude Code is started with `--permission-mode plan`; on macOS
  and Windows, where the turn carries `CLAUDE_CODE_SUBPROCESS_ENV_SCRUB` (`scrub` in `src/chat-hosts.ts`), it starts in
  its default mode instead, saying so on its standard error, and the rules below hold the turn just the same. Its settings
  add `"useAutoModeDuringPlan": false` so no classifier runs a command, `ExitPlanMode` is taken away by a bare deny
  rule, and the allow list is replaced by `chat-plan-list.txt`: the allow list's verbs that change nothing, written by
  the CLI's tests, where each verb of the allow list is declared a read or a change. The desktop refuses every other
  request, with no card, but two kinds of card: reading a file or a folder outside the chat's folders, and opening a web
  page. Every deny rule stays. It carries no hook: no verb of `chat-plan-list.txt` takes a request on standard input,
  which the CLI's tests hold (`DesktopDenyListTests`), so a redirection into one is neither stored nor printed back;
  every other command is refused already; and a redirection's target meets the host's own rules for the data directory
  (`dataRules`), which every mode carries. That those rules hold a redirection's target, here and below the floor for a
  command an allow rule runs with no card, is documented by the host's maker (Anthropic,
  https://code.claude.com/docs/en/permissions, read 2026-11-20), not observed by us. Codex runs as in Ask first. Plan only is kept on the conversation's record (`mode`), as
  Ask first and Auto-edit are, so it stays with the chat after a restart, an update and Start fresh. A Plan only turn that ends with no error, not
  stopped, outside the Feed and with words last marks those words its plan, `plan: true` on the kept text entry and on
  the turn's `done`; an older page or desktop ignores the field. Words too long to keep whole are no plan: the cut that
  fits them to the record takes the mark off. The mark goes from every entry at each turn's end, at
  Start fresh and when the chat leaves Plan only, so the page's card for it is never offered twice. The card's go is a
  send that names `mode: 'ask_first'`, and the desktop refuses a send whose `mode` is not the chat's mode now, before
  any host starts.
- **Full access** is Claude Code's own bypass mode, and on Codex its run with no sandbox: the AI reads, runs commands
  and changes files anywhere on this computer the account may, without asking, on Claude Code except the three kinds
  Jason refuses (below), its settings blocking no read.
  It is the mode of every new chat, the Feed's included (`feedDefaultMode`), and asks for no confirmation: a record with
  no `mode` is in Full access, and choosing it from the menu clears the field. The `full-access.json` and
  `full-access-taken-back` an earlier desktop wrote for a standing choice are removed at start, and no mode is read from
  them. It is refused where it cannot apply: on a Claude Code older than 2.1.322 or whose version cannot be read, the
  menu shows it disabled with the reason and the desktop refuses it. A turn of a chat in Full access that runs on a
  Claude Code below that floor, comes from a browser tab, is sent while Full access is not offered, or meets a Claude
  Code whose bypass mode is not available (below) runs in Ask first; the chat says why once a run for each of those
  reasons, and the record keeps its mode. On Codex a Full access turn runs as itself, held to neither Claude Code's
  floor nor its bypass (below). Each host's listing carries `runs`: for each
  mode a turn on it does not run as itself, from the window and from a tab, the mode it runs in and the sentence why,
  decided by the rule the turn itself runs by (`turnMode`), with Claude Code's version read at that listing. The page
  draws that mode, the line under the composer and the menu's check, with the sentence under the composer, and never
  draws from what it asked for; a host an earlier desktop listed hands no `runs`, and the page draws the chat's own mode.
  A choice from the menu is of the chat's own mode, the one checked included, so Ask first chosen where Auto-edit runs
  in it, on Codex, stays the chat's on Claude Code; the checked mode, being the one the running turn runs in, stops no
  turn. The setup keeps the version its last listing read (`handedClaudeVersion`); a turn whose own read
  finds Claude Code at the floor where that listing found it below or could not read it runs in Full access, asks the
  page to read the hosts again (`setup.changed`) and says so once per conversation (`fullAccessCurrent`); a listing that
  finds Claude Code across the floor from the last one, either way, asks the same (`setup.changed`), and so does a turn
  that finds it below the floor where the listing found it at it.
- **How Full access runs on Codex.** A Full access Codex turn runs `exec --json --sandbox danger-full-access
  --skip-git-repo-check` with the person's config.toml, no `--ignore-user-config` and no Windows sandbox kind
  (`codexArguments`). Codex in `exec` never asks, so it acts as the person with their files and network, and none of
  what follows holds it: no hook, no card, no deny rule, no refusal of the permission model, and not the refusal of the
  runtime's run file, the desktop's settings or the turn's own settings that every Claude Code turn keeps. It has no
  version floor of Full access. Reading the person's config.toml, it reads their `shell_environment_policy`, so it
  pins `JASON_DATA_DIR` and `JASON_CHAT_TURN` by `-c shell_environment_policy.set.<NAME>` (`codexPinnedVariables`): at
  openai/codex 9f2b87495b, read and not seen, session flags merge over config.toml table by table, so `set` adds to the
  person's, and `set` is applied after `inherit` and the excludes and before `include_only`, so a policy that keeps only
  listed names still removes them. PATH is never pinned, since `set` would replace the person's whole PATH, so such a
  policy can also take the bundle's folder off it. An administrator's `requirements.toml` that does not allow `danger-full-access` makes
  Codex, at openai/codex 9f2b87495b (read, not seen), refuse to start: `codex exec` forces `approval_policy = "never"`
  (exec/src/lib.rs), so building its configuration fails (core/src/config/mod.rs) with its words on standard error and
  no event. A Full access Codex turn that ends so, before any line on standard output and with a failing exit, ends in
  Codex's own words followed by `codexFullAccessRefused`, every time; the words are not read and nothing is
  remembered. Only under an automatic reviewer (AutoReview) does Codex, at the same commit, fall back instead to the
  sandbox the requirements allow and say so as a configuration warning, an `item.completed` of type `error` beginning
  "Configured value for `permission_profile` is disallowed by requirements" (`sandbox_mode` in an earlier Codex;
  core/src/config/mod.rs `apply_requirement_constrained_value`, exec/src/event_processor_with_jsonl_output.rs). The
  desktop reads that line (`codexLine`, `sandbox_narrowed`), lets the turn go on, keeps the chat's mode and says once in
  the conversation `codexFullAccessNarrowed`; every other warning is dropped, as before. No event names the sandbox a
  turn ran in (`thread.started` carries the thread alone), so a narrowing Codex does not warn of goes unseen.
- **How Full access runs on Claude Code.** Claude Code starts with `--permission-mode bypassPermissions`. Anthropic says a Bash rule
  "isn't a security boundary around the program", so every deny rule is passed as in an Ask first turn the hook rides,
  and the turn's settings, in a file under `<data>/desktop/turns/` beside its MCP file and removed with it
  (`hookedSettings`), add a `PreToolUse`
  HTTP hook on every tool with `"onFailure": "block"`, `"disableAllHooks": false` and the hook's address in
  `allowedHttpHookUrls`, so no settings file of the person's or a project's can switch the hook off or filter it. The
  hook posts each tool use to the turn's loopback endpoint, behind the turn's secret, and the desktop answers a refusal or nothing (`HookAnswer`), by one
  judgement (`fullAccessJudgement`), the same in every mode the hook rides:
  - **refused**, with no card, for three kinds alone, each by a sentence that says what is closed, that it is Jason's
    own and not one of the person's files, and the `jason` verb that does the act where one does, never that it cannot:
    the installation's settings files and run file, under
    every layout (`settingsRefusal`); its data directory outside the turn's folder (`dataRefusal`), and in a demo run the
    person's own installation (`personsRefusal`); and what Jason itself sets for the turn: its variables and its
    permission tool, that is `JASON_CONFINED` and `JASON_CHAT_TURN` (`confinedRefusal`), `JASON_ATTEMPT_ID`
    (`attemptRefusal`), with where the shell finds a program (`lookupRefusal`), and the tool that answers the chat's own
    permission requests, which only the person answers (`ownToolRefusal`). Each is read in a command as written, over its
    normalised forms and, where every word is literal, over the words the literal reading resolves; in a file tool's path
    by every route a path can take, on Windows a path that names this computer as another computer would (`localhost`,
    a loopback address or its own name) through a drive's share (`C$`) as that drive's path, and one in a device's or a
    volume's namespace, or through another share of this computer, by the sentence that says Jason could not place where
    it leads (`hookPlace`, `unplacedRefusal`); in the text a writer tool would put in a file; and in every string of an
    MCP tool's input. A command is also read as the shell would lead it (`readKinds`): a relative path from the folder a
    change of folder led to, a variable the reading knows (the home folder's and the profile's, the drive's, the folder
    the shell is in, and a value the text gives one of its own, `assignedValues`, in PowerShell a quoted one or a
    number, each beside the variable's own, `variableValues`), a `~` followed by the home's own account name, a POSIX
    shell's tildes where they lead (`tildesOf`: `~` to the home folder and to a value the text gives HOME, `~+` to the
    folders the shell may be in, `~-` and the folder stack's `~N` to those it has been in, and a value the text gives
    OLDPWD, for a word and for a change of folder, `cd -` among them), and a pattern by every name it can match, a name
    that begins with a dot only by a pattern that does too unless the text sets an option that says otherwise
    (`patternKind`), for a read and a listing alike. **The
    limit:** a variable the reading does not know, and a path a program builds while it runs, are not seen: such a
    command runs in Full access. A command that names another link to a settings file or the run file, as a word or as a
    redirect's source, is refused as that file, and in a demo run another link to the person's own settings file or run
    file as the person's own (`linksPersonsOwn`). With no read blocked, a walk of a folder that holds the data directory
    (the home folder, a drive's top, the data directory's parents) is the data's kind: a search of it by the Grep or
    Glob tool (`walkKind`), a command that names such a folder or takes a word the shell builds from one (the profile's
    drive and path, the system's drive and PowerShell's call for a folder of the account's are such folders), in any
    form but the few known not to descend (`plainWalkForm`: a listing or a search with only its table's options and only
    words it can read, a read of a file, a look at a path, a print, a folder change), a removal among them, and a form
    known to descend, remove or copy a tree that takes a word that begins with a variable the reading does not know
    (`treeForm`, `unfixedVariable`: a finder, a size, a tree, an archive, and a search, a listing, a copy or a removal
    told to recurse, in PowerShell by `-Recurse`; a positional parameter in each of its spellings), are refused by the
    sentence that says to name a narrower folder (`searchRefusal`, `walksRoot`); any other form given such a word runs
    unless the text names such a folder. A command in any other form that runs in such a folder, or where a change led
    that the walk did not follow, is refused by the sentence that says to run it in a narrower folder or name the folder
    it should work on (`inRootRefusal`). So is a command that reads what no word of it shows
    (`unseenReadRefusal`): a jq program that holds `env`, `$ENV`, `import`, `include` or `modulemeta`, takes its program
    from a file or is given a word the shell builds (`jqRefusal`), and `sort`, `wc`, `du` or `find` told the files it
    reads by `--files0-from` (`filesFromRefusal`). A plain listing of such a folder runs. A Glob pattern is read where
    it can lead, its fixed folders climbed once for each `..` after them, and one whose brace group can name a place is
    refused (`braceRefusal`). A judgement that fails refuses;
  - **nothing** for everything else, the deny list's verbs, a jason that starts with an option, PowerShell and a
    command Jason does not read literally among them: the host's own mode and rules decide it.
- **What Claude Code still asks about in its bypass mode is a card.** It reaches the permission tool, which refuses the
  three kinds first and makes anything else a card titled "Full access asks about this one" (`askedCard`), why
  `host_asked`; it waits thirty minutes, then is not run and the turn goes on, and "Allow for this chat" works on it as
  in Ask first. A card kept on a record from an earlier desktop may carry an older reason, which the page still says.
- **PowerShell is Full access's.** A Full access turn keeps the PowerShell tool (`fullAccessRemovedTools` takes Monitor
  alone out), and the hook reads its commands as it reads Bash's; every other mode takes both out.
- **When the bypass is not available.** The turn runs in Ask first and each conversation is told why once in each hold
  of the cause, by the cause that holds whatever a start found within it, in one shape, "Full access is not available
  here: <why>" (`bypassSentence`), "Full access is not
  available here right now: <why>" where the cause holds a minute, and "Full access is not available here until Jason
  Desktop starts again: <why>" where a tool ran that the hook never saw. The record keeps Full access, and the box says
  so with what the menu draws: "This turn runs in Ask first, and the menu shows the mode each next turn runs in; the chat
  stays set to Full access." (`askFirstThisTurn`); a turn stopped for a tool the hook never saw ends in the same words
  (`unguardedSentence`). The causes: the desktop runs as root (`getuid`), under which Claude Code refuses the mode;
  Claude Code starts the turn in another mode, which its init line names first, so that start is stopped there, what of
  the person's own configuration runs as a session starts having started with it, and the turn starts again at once in
  Ask first's own shape: its settings, its sealed line, its environment, no PowerShell, every page asked about and the
  host's rules over the chat's own names. Every start of a turn meets this, the fresh session after a resume Claude Code could not find included (`unlessNarrowed` in `Chat`), and each start's environment is built from
  the mode it starts in as it starts, as its command line is. A resumed turn starts again in a fresh session given the
  recap (`startAgain`), never in the session it resumed: the stopped start may already have written the message there,
  which a resume would then hold twice; the record takes the fresh session's id. The word the init line names is read
  from a closed table of Claude Code's modes
  (`claudeModeWords`: `default`, `acceptEdits`, `plan`, `auto`, `dontAsk`; any other word is `another`), and once the
  start has ended its standard error is matched against Claude Code's own words "Permission mode forced to default"
  (`forcedDefault`), a closed match, nothing else of what it wrote being read: matched, the cause is `forced_default`,
  "Claude Code said it forced its permission mode to default."; not, the sentence names the mode, "Claude Code did not
  start in its bypass mode, and named its default mode instead.", and no setting is named. Claude Code refuses the mode
  before it starts, in its restricted mode or under a policy, exiting with `bypassPermissions` in its error, so the turn
  starts again at once in the same shape; or a tool ran that the hook never saw (below). A cause a turn found holds a
  minute (`bypassHeldMs`), in memory: the Claude Code turns of a Full access chat in that minute run in Ask first and
  ask for no bypass, so the host is not started twice on every turn while the cause stays; the menu draws Ask first and
  is drawn again when the minute ends; and the first Full access turn after it asks for the bypass again, so a cause
  that went away needs no restart of the desktop. Running as root, and a tool the hook never saw, hold until the
  desktop starts again. The desktop's log says each cause that comes to hold, by its cause and the mode named, running
  as root once as the desktop starts (`chat_bypass_unavailable`), so `jason logs --source desktop` tells the cases
  apart. What a cause leads to, for the turn that found it and the turns after it, and for how long, is decided in one
  place (`bypassLeadsTo` in `src/chat-modes.ts`).
- **What the reader does not see.** A program the agent writes to a file and runs is not read, and neither is the
  person's own shell configuration (aliases, shell options), nor what an MCP server or connector of the person's own,
  which a Full access turn loads, does with the input of one of its tools: that input is read as its strings are
  (below). Nor are the shell commands a skill of the person's own carries in its `!` lines: Claude Code runs them itself,
  outside the hook, in a Full access turn, whose settings leave them on.
- **The AI's folder.** A Full access turn runs in the conversation's working folder, as its other turns do. The folder
  an earlier desktop made for a Full access grant, under `<data>/desktop/chat-full/`, is moved to `chat-full-ended/` at
  start; the conversation says where it is, with an Open folder button, when it is next opened, and the desktop removes
  it after seven days. Added campaign or project folders whose `.claude` holds skills,
  agents or commands are left out of a turn, and the chat says so once.
- **A denied verb in any letter case is refused, in Plan only.** Outside Plan only no verb of the deny list is refused
  (`modeFence`): the acts of a person's word (`personsWordForms`: five verbs, and `preference set` and `unset` of two
  keys, `sender.identity` and `tone_of_voice`, each a host rule of its own) are allowed as the allow list's verbs are,
  `reply record` (`cardedVerbs`) leaves the allow list, and every other one is the mode's: where the hook rides an Ask
  first or Auto-edit turn it runs with no card but the forms of `askingCardedForms`, below the floor it is a card, and
  Full access runs it. In Plan only `jason Campaign Set-Send-Mode` is refused as
  `jason campaign set-send-mode` is (`holdsEntry` folds the words). The CLI takes a mis-cased verb for a usage error and
  sends nothing, and the fence does not lean on that. A value spelled like a denied verb is refused too, in any case, as
  its lower-case spelling already was.
- **A re-spelled refusal is still a refusal, in every mode.** Before judging, the deny-list commands and the operations by
  their dots (in Plan only, the one mode that refuses them), Jason's own variables, the attempt's variable and the data directory's names are looked for a second time over a normalised text:
  quotes and a backslash inside a word removed, a bracket, a comma or a parenthesis read as a space. `appro\ve`,
  `approv''e`, `approval.appr''ove` and `['jason','approval','approve']` are refused in Plan only as their plain spelling
  is, and a re-spelled variable or data directory in every mode, the content of a file being written in Full access
  included. The same look reads through an empty `$''`
  or `$""` inside a word, a `$'\xNN'` string, a line continuation and a brace group (`{approve,}`; a word of more than
  64 expansions is refused, never judged on the first ones), and reads `\n`, `\t` and `\r` in a string as the space they print.
  What the plain spelling would not refuse stays a card in Ask first. An
  `env` that clears the environment (`-i`, `-`, `-u`, `--unset`) or `exec -c`, in a command part that runs a jason,
  clears Jason's own variables without naming them, and Jason alone sets those: a jason started without the chat's
  environment may act on another installation than the one this window shows, so it is refused in every mode; an `env`
  elsewhere in a command list is not.
- **The chat's own names, by every route.** The chat's instructions, its `.claude`, `.agents` and `.codex` folders and its
  `.mcp.json` directly in the chat folder are refused to a shell command as to the file tools, and the host is denied
  edits of them (`chatRules`), in every mode but Full access. In Full access the turn's folder is its own, a
  conversation's own folder and the shared chat folder alike, and is put right again before the next turn: the
  instructions are written afresh and the agent folders hold only what the desktop placed (below). Two conversations an
  earlier desktop made share that folder, so while both run a turn, one may read a skill the other's turn rewrote; the
  next turn of each starts from the desktop's files.
- **The files and commands that add an MCP server are a card each time in Ask first and Auto-edit.** A write of
  `.mcp.json` anywhere else, or of
  `.claude.json` anywhere (Claude Code keeps the user's and the local scopes' servers there), is a guarded name's card,
  and so is a command that runs `claude` or `codex` with the word `mcp` after it (`claude mcp add`, `codex mcp add`,
  through any folder or `npx` package name, and as the shell would read a re-spelled word). None is covered by "Allow
  for this chat", and Full access runs each.
- **A tool of the person's own MCP server runs in Full access, the one mode such servers reach (above).** The fence
  stands in the other modes anyway. A tool named `mcp__<server>__<tool>` of any server but the chat's own (`isOwnTool`)
  is judged before the
  command check, so an argument named `command` or `file_path` is never read as a shell command or a file tool's path:
  the card is "Use one of your own tools", its detail the server and the tool, then the input exactly as the host sent
  it (`JSON.stringify`), never cut. Before the card, an input whose detail would be longer than the card shows whole
  (4,000 characters) is refused, and so is one any of whose strings, at any depth and the keys of its objects among
  them, is refused when read as a command's text: in Plan only a deny-list verb or operation, plainly or re-spelled; `JASON_CONFINED`, `JASON_CHAT_TURN`, `JASON_ATTEMPT_ID`,
  the data directory's variable or the data directory in any spelling, the settings file and the run file included.
  "Allow for this chat" is never offered for it, Plan only refuses it, and Full access runs it, refusing one whose input
  names the three kinds.
- **The chat folder's top is the desktop's, in every mode.** What the desktop placed in it is recorded outside it,
  under `<data>/desktop/skills-records/`, which the chat neither reads nor writes: the record `jason skills install`
  writes of each deployment, put beside the skills only while the CLI runs and moved out after, and the Reply skills'
  files with their digests, recorded for the folders the Connect card copies in that call and no others. At the start
  before anything is deployed, and before every turn (`src/chat-agent-folder.ts`), the top is put right: `.claude/` and
  `.agents/` hold their `skills/` and in it only the files those records name with the bytes recorded; everything else
  there is removed, a settings file and a record beside the skills included; `.codex/`, the memory files the desktop
  does not write (`CLAUDE.local.md`, `AGENTS.override.md`) and a `.mcp.json` are removed. The person's own `.claude`
  anywhere else, in their home folder or a folder of theirs, is never touched: that is where their own configuration
  enters a turn that runs in Full access. Nothing below the top is looked at in this
  version: an agent folder or a memory file in a subfolder is not removed, and a host may read instructions from a
  folder it works in (in Ask first every write there is a card). A link is removed as a link, never walked, and a deploy
  never goes through one: `.claude`, `.agents` and their `skills` are each made a real folder before anything is
  written into them, so nothing outside the chat folder is deleted or written. A chat skills folder whose deployment
  the desktop keeps no record of, an installation's first start of this version included, is emptied and deployed
  afresh at the start, and the Reply skills' record goes with it, so they are copied in again by the step after. A start
  whose pass over the top cannot finish deploys nothing into the chat folder, the Reply skills included, and marks
  nothing as deployed: the next start tries again, and a turn refuses on the same folder anyway. While no record of the Reply skills is kept, a start whose `reply auth status` says
  signed in deploys them by the sign-in's own routine and records them, in the same step as Jason's skills; a
  `reply-*` folder that stood there is removed and replaced by the Reply CLI's copy, never kept, and an install that
  fails is logged by its exit code, the start goes on without them, and the next start tries again. A turn does not
  start on a folder that could not be put right.
- **When the hook did not run.** In Full access the first tool that runs without the hook having seen its use stops the
  turn, that tool having already run, and the bypass is taken as not available (above): the record keeps Full access,
  and later turns run in Ask first until the desktop starts again. One a turn already stopped, by Stop or by a change of
  mode, still says on its way out is taken the same way, and that turn ends with no word of a mode. In Ask first and
  Auto-edit the turn runs on, the cards and the host's own rules deciding as before, and the chat says once per
  conversation that Jason's refusals could not stand in front of them (`hookFilteredSentence`). **The limit:** the
  refusals read what a command's text and a tool's input show; they are not a sandbox, and a program the agent writes
  and runs acts with the person's account.
- **Auto-edit** is Ask first, except that the six file commands run with no card on paths in the chat folder
  (`autoEditJudgement`, `src/chat-permission-model.ts`, over `src/chat-auto-edit.ts`). Its command line is Ask first's,
  byte for byte: `--permission-mode default`, no `acceptEdits` and no `defaultMode`, and the same hook from the floor of
  Full access on; the permission tool decides what the hook leaves, and the broker uses `autoEditJudgement` in place of
  `judge` for a turn started in it. That judgement returns
  `judge`'s result unchanged unless `judge` says ask, the tool is `Bash`, and the literal reading (`readLiteral`, with
  `~` held) is a run of the six commands joined by `&&`, `;`, `||` or new lines, each named bare, with nothing piped in
  or redirected and no folder of its own; then it allows. Every option is read wherever it stands before `--`, since
  GNU's tools take one after an operand; an option outside the list, an abbreviated long option, a lone `-` or an empty
  word leaves the card:

  | Command | Options it runs with | Operands at least |
  | --- | --- | --- |
  | `mkdir` | `-p`, `-v`, `--parents`, `--verbose` | 1 |
  | `touch` | `-c`, `--no-create` | 1 |
  | `mv` | `-n`, `-v`, `--no-clobber`, `--verbose` | 2 |
  | `cp` | `-n`, `-v`, `-p`, `--no-clobber`, `--verbose` | 2 |
  | `rm` | `-d`, `-v`, `--dir`, `--verbose` | 1 |
  | `rmdir` | `-v`, `--verbose` | 1 |

  Each operand must hold no `..` component, pass the spelling check Ask first applies (`judgeSpelling`), name no device
  on Windows (`con`, `nul`, `com1`, `lpt1` and the like, with any extension), and lie strictly inside
  the folder the turn runs in, the conversation's own or `<data>/desktop/chat`, both as written and once links are followed (`located`), the folder itself excluded; the names
  an agent loads (`guardedPlace`, `namesLocalJason`) and every refusal over the resolved words stay as in Ask first.
  A link or a junction that leads nowhere, at the operand or at any folder above it in the chat folder, is a card: the
  scope's `entry` (`entryOf`, `src/chat-real-path.ts`) finds something there that the real path reader cannot follow,
  and a `touch` or a `cp` through it would write where it leads. A scope without `entry` runs nothing unasked. For a
  `mv` or a `cp`, the path it writes when its last operand is a folder, that operand with each source's name after it,
  is judged as one more operand for every copy and move, whatever the last operand is at the judgement, since an
  earlier part of the line may make it a folder; and a `mv` whose destination or written path ends in the first part of
  a guarded name of two parts (`.github`) is a card, since the folder moved there may hold the rest. A file named like
  one of the six (with a program extension on Windows, folded where names fold) or like jason, directly in the chat
  folder, is a card when an operand names it or when the command runs that program and such a file is already there:
  where the search path holds the current folder the shell would run it in place of the system's.
  The chain rule: once a `mv` or a `cp` names or writes a path, no later operand of the same command line may lie
  inside it, since what the earlier part made there is not what the judgement saw. Auto-edit is refused on Codex (`codexAutoEdit`) and on a
  host that does not drive the chat; each host's listing carries `autoEdit`, the
  reason or null, and the menu shows the item disabled with it. It has no version floor, no confirmation and no switch;
  it is kept on the conversation's record (`mode`), so a restart keeps it and Start fresh leaves it; a turn sent to
  Codex runs in Ask first, read-only, the chat says so once a run, and the record keeps Auto-edit. The page draws the mode a turn on the host runs in, from its
  `runs`, as it does for Full access. **The limits:** the conversations an earlier desktop made share the chat folder, so such a chat in
  Auto-edit can remove a file another of them left there; a hard link already in the folder is not told by its path, so a
  command that writes through it reaches the file it shares; and the judgement is made before the command runs, so a
  link put into the folder by something else between the two is the same limit Ask first's Write has.
- A change to a mode narrower than the running turn's stops that turn, narrow to wide being Plan only, Ask first,
  Auto-edit, Full access, and the page gives the waiting message back as Stop does, judging by the mode the turn runs
  in as the desktop hands it (`choiceStops`), never by the mode the chat keeps: no turn follows by itself. So
  leaving Full access, or leaving Auto-edit for a narrower mode (Ask first or Plan only), stops the turn that runs in
  it, and a change to Plan only stops any running turn, so the menu never says a mode while the running turn could
  still do more than it lets; a change to a wider mode, Auto-edit to Full access included, lets the running turn finish
  in the mode it started in.
- **No turn starts by itself.** Every turn is a person's send:
  - a message the person sends while a turn runs waits in the page, every one sent meanwhile added under the one before,
    and they go together as the next turn once that one ends well, in the mode of the moment it goes;
  - a resumed host session is the same send started again with a recap, in the same mode, and stops with it;
  - a set-up chat's naming run is no turn: it has no tool at all, no hook and no permission mode, in every mode;
  - a line the Feed takes never starts a turn: it reaches the agent with the person's next message, in the mode of that
    turn. The Feed takes the modes as any chat does.
- The log says each change of mode by its two modes (`chat_mode_changed`), the mode of each turn started and of each
  request settled, each refusal of the hook (`chat_request_decided`, refused, by the model, in the turn's mode), a
  Full access turn stopped because a tool ran unchecked (`chat_turn_stopped`, `unchecked_tool`), and, once in a turn of
  Ask first or Auto-edit that ran on after one, its mode (`chat_hook_filtered`); never a conversation's id or title, a
  command, a path or a person's words.

## Models and effort

The chat's box picks the host, a model the host offers, and where that model takes one an effort; each reaches the
turn as the host's own flag, and none is passed when none is chosen, so the host keeps its own default. The conversation's
record keeps what its last turn was sent with, and the box shows it again when the conversation is opened.

| Host | The models offered | Effort |
| --- | --- | --- |
| Claude Code, and Anthropic's key | the vendor's aliases `opus`, `sonnet`, `haiku`, `fable`, which "point to the recommended version for your provider and update over time"; beside them a model's full name the person types, passed as it is, with no effort | `--effort`: the five levels on each alias; on `haiku` only from Claude Code 2.1.293, the version from which, on the Anthropic API, the alias names a model that takes them (the rule is the Anthropic API's: on another provider the box may offer a level Claude Code does not apply) |
| Codex, on the person's sign-in and on the OpenAI key | what `codex app-server` answers to `model/list`, kept per host id (see below) | `-c model_reasoning_effort=<level>`: the levels each model lists among the five |
| DeepSeek, MiniMax, Kimi, GLM | the desktop's table of each provider's models | `--effort` where the vendor's guide names levels: DeepSeek `low`, `high`, `max`; MiniMax the five; Kimi and GLM none |

The effort is one of five words, `low`, `medium`, `high`, `xhigh`, `max`, and the desktop passes no other: never
`ultracode` or `ultra`, which run more agents rather than set a level. A send is refused before any host starts when
its effort is another word; when its model is not one the host offers by the table or the list kept, or, on Claude
Code and Anthropic's key alone, a typed name of the model shape, so a model chosen on one host never reaches another;
and when its effort is not one the model named takes (the host's own default model, a typed name and a model no list
names take none). On Claude Code the effort is decided after the version is read, as the floor is. A turn that names an
effort is started without an inherited `CLAUDE_CODE_EFFORT_LEVEL`, which Claude Code ranks above `--effort`.
DeepSeek's retired `deepseek-v4-flash` is sent, and read from a record, as `deepseek-flash`, the name its page gives
now. A model a conversation kept that its host no longer lists is shown as no longer listed, and its turn goes on the
host's own model with no effort; while "Another model…" is chosen with no name typed, nothing is sent.

Claude Code documents no command that lists its models, so its list is the desktop's own table of the aliases, and
nothing is asked of Claude Code in the background. Codex's list is asked of the host: `codex app-server` is started
with no message, as a turn of that host id is started (found on the PATH, no shell, the chat folder, the turn's
environment, and on the OpenAI key the key and the desktop's own Codex folder), and sent `initialize`, `initialized`
and `model/list` page by page, each after the answer before it; no thread or turn is started, so nothing reaches a
model. The lists are kept in `<data>/desktop/models.json`, one per host id since OpenAI says an API key's models follow
its organization and project, shown at once from what is kept, and asked again in the background when the hosts are
listed, one ask at a time, only for a Codex on the PATH and an OpenAI key stored.

- **Only a list read counts for the day.** A host whose list was read is asked again once a day. An ask that is
  refused, answers what cannot be read, ends early or passes its bound keeps the last list read as it was, is logged by
  its kind alone, and is kept in memory only: its host is asked again at the next start, or an hour later while the
  desktop runs. Where no list was ever read, the box keeps Codex's model control with its own default alone and one line
  that says the models could not be read, or are being read.
- **The bounds.** An ask ends at 30 seconds, after 20 pages, past 200 models, or on a line longer than 1 MB, each read
  as unreadable but the first. A kept list of more than 200 models, or a file larger than two such lists could take, is
  not read, nor is a list the desktop cannot read whole; an entry is shown only when its name is one the desktop passes
  to a host.
- **Quitting.** When the desktop quits, or its last window closes, an ask that runs is ended as a turn's host is, and
  the asks that wait are dropped.
- On a Claude Code turn the desktop's file rules deny the agent the file.

## The window

- The page is the runtime's own, at `http://127.0.0.1:<port>`; navigation is locked to that origin. A link or
  `window.open` that leaves it goes to the system browser only when it is `https:`.
- `contextIsolation` on, `nodeIntegration` off, `sandbox` on; no webview attached, and of the permissions a page asks
  for only `clipboard-sanitized-write` is granted, to the runtime's origin alone, so a Copy button works.
- The preload exposes `window.jasonDesktop` and nothing else. Every call but one is answered only when it comes from the
  top frame of a page at the runtime's origin (`src/ipc.ts`); a subframe, another origin and a `file:` page are refused.
  The one is `chat.pathOf`, a dropped file's path, which the preload answers itself, with no channel to the main process
  and so with no check of the origin. That is safe because Electron reads a path only from a `File`, and a page holds a
  `File` only when the person dropped it there or picked it: a page learns the path of no file it was not given. The
  preload answers it only for a page served over `http:`, as the runtime's is, and with an empty path for any other.
- On macOS the window has no title bar (`src/window-frame.ts`): its three buttons sit in a band of 26 pixels over the top
  of the console's sidebar, which the console leaves empty in the desktop's own window (its `desktop-mac` class), and the
  window is dragged by that band and by the console's top strip, every control on them excepted. So that the window
  stays movable whatever it shows (a console older than this desktop, a custom one, a page that failed to load), the
  desktop also adds a band of its own to every document the window loads: an empty element at the end of the document,
  drawn over those 284 by 26 pixels as a drag region by a stylesheet of the user's origin whose every declaration is
  important, which no stylesheet of a page outranks. The page shown while Jason starts has a drag band across its top.
- On Windows and Linux the window has no title bar either: the caption buttons are Electron's title bar overlay
  (`titleBarStyle: 'hidden'` with `titleBarOverlay`), 38 pixels high, the console's top strip's height, standing over
  the strip's right end. Their ground has no colour at all, so the strip shows through them in either mode and theme;
  their symbols are the console's ink in the theme in force, set again at each `nativeTheme` `updated` while the window
  is open (`followTheme`), the person's choice and the system's own change alike. The console leaves the buttons' width
  free at the strip's right (its `desktop-overlay` class), read from the overlay's own geometry, the
  `env(titlebar-area-*)` variables Chromium gives the page, never a number of its own, so Open in and the Demo | Live
  switch stand left of them at any scale of the display. The desktop's own band is there too, 40 pixels wide just left
  of the buttons and as high as they are, placed by the same variables, so a page that marks no drag region still moves
  the window; a double click on a drag region does what one on the desktop's own title bars does (on Windows, where a
  drag region answers the system's hit test as the caption and the buttons as minimize, maximize and close, it
  maximizes; on GNOME and Plasma too, as seen). The window shows no menu bar on
  Windows or Linux: Electron makes none for a window without its title bar, and on Windows it was hidden before too. The
  menu stays the application's, so its keys act in the window; the tray carries the Jason menu's other acts, and About
  is not reachable from the window or the tray.
- On Linux the buttons are drawn in the desktop's own style (round on Ubuntu's GNOME, Breeze's on Plasma), and as many
  as the desktop's button layout names for its windows (`org.gnome.desktop.wm.preferences` `button-layout`, which GTK
  reads, and follows as it changes): where it names the close button alone (`appmenu:close`, GNOME's own default,
  which Ubuntu changes), the overlay shows that alone, like the desktop's other windows. A layout that puts the buttons
  at the left stands them over the sidebar's brand, and the strip keeps its right end free all the same: the window is
  closed and moved as ever, but the console leaves no room there. The same window is made under Wayland and under X11.
  On Hyprland, which draws no title bars, a window with the system's frame has no buttons at all; the overlay gives it
  its close button under Wayland, and all three under XWayland, where its layout does not reach.
- In Demo the window loads no runtime's page and has no preload: it holds two views (`src/demo-window.ts`), the demo page
  and, over its right end, the desktop's switch (`shell/demo-switch.html`, a page with no script whose Live is a link the
  view refuses and reads as the choice). Each view has Chromium's sandbox, `contextIsolation`, no Node, no preload and an
  in-memory partition of its own, and `src/demo-page.ts` seals it: its session answers its own `jason-demo:` or
  `jason-switch:` address alone through a protocol handler that sends the content policy as a header (inline script
  and style, `data:` fonts and images, nothing else, and a `sandbox` of scripts alone, the page's own frame, so its
  origin is opaque, `null`, and it sets no cookie); `http`, `https` and `file` answer 403 there, every request but
  the page's own address or a `data:` one is cancelled, and a proxy that leads nowhere stands behind them; WebRTC keeps
  to the proxy. Pop-ups, navigation, redirects, frames' navigation, webviews, downloads, devtools, a login, a
  certificate error and an unload prompt are refused or cancelled, a main frame that ends anywhere but its own address
  (`about:blank`, which no handler is asked about) is loaded with it again, and every permission request and check,
  device chooser and screen capture is denied. Every document of the page's view gets a sheet of the user's origin that
  makes each element no-drag (`noDragSheet`), so no region the page marks is the window's drag region, which in a window
  without a frame would take the clicks of our switch over it. Each kind refused is logged once a run (`demo_refused`), by
  its name alone; the person's click on Live is a choice and is not. The page's note names the repository as text,
  `github.com/reply-team/jason-ai`, and the page's view draws it as a link of the desktop's own: a sheet of the user's
  origin (`demoLinkSheet`: the colour of the page's own links, a pointer, an underline on hover and focus) and a script
  run in a world of its own beside the page's (`demoLinkScript`), which marks the element whose whole text is the address,
  keeps marking it as the page draws its note again, makes it focusable with the link role, and on a person's click or
  Enter there, never on load, asks for the address as a navigation of the view. The view's guard refuses that navigation
  as it refuses every other, and its reader (`demoLinkOpener`), asked of the main frame's navigations alone (a frame's
  within the page and a redirect ask nothing), for `https://github.com/reply-team/jason-ai` (`demoRepository`) compared
  whole, calls `openDemoRepository` in `src/main.ts`, which is handed no address and gives `shell.openExternal` that
  constant. It does so only within a second (`demoLinkGestureMs`) of a person's left click let go or Enter pressed in the
  page's view, which the main process sees as input reaching the view (`watchDemoGestures`, by `input-event` and
  `before-input-event`), so a page that asks for the address on load or on its own opens nothing; and at most once in two
  seconds (`demoLinkSpanMs`), so a person clicking again and again opens one tab. Said plainly: any real click or Enter
  in the page, on the link or anywhere else, gives the page one second in which that one address may open, at most one
  tab per act. Both moments are read from a monotonic clock (`performance.now()`), and a moment before the gesture is
  refused. Any other address opens nothing. The view's pop-ups stay denied, the policy is
  unchanged and the page's file is not edited; in a browser tab the address stays text. The open is logged as
  `demo_link_opened`, or `demo_link_open_failed`, without the address. A Demo start appends, before Electron is ready, the
  Chromium switch `host-resolver-rules` with `MAP * ~NOTFOUND` (`demoBeforeReady`), so the run resolves no host name at
  all: a name the page names ahead (`<link rel="dns-prefetch">`) is looked up below every guard above, and this is what
  stops it. Nothing of the desktop in Demo needs a name: no runtime, no update check, no deep-link registration, only the
  application's own files. A Live start is a start of its own, relaunched with its own arguments, and never has the switch.
  `test/demo-page.test.ts` holds each seal on stand-ins for the contents and the session, and holds the switch to the
  Demo start, before ready.
- The switch's view lies over the page's top strip on the page's own numbers (`demoLayout`): right of its 284-pixel
  sidebar to the window's right edge, 38 high, the window's drag region but for the switch. The view and its page are
  transparent, so the page's own dot and sentence show through, and our page (`shell/demo-switch.html`) draws the switch
  alone at the strip's right end, which the page leaves free for one, on the strip's colour. It is laid out again at
  each resize and added after the page's view, so it is drawn over it and takes the clicks there. On macOS the Demo
  window takes the window's own frame (`frameOf`, above: no title bar, the buttons in a band of `titleBand`, 26 pixels),
  and both views start at the window's top edge, as they do elsewhere. The traffic lights stand over the left end of the
  page's sidebar, so the desktop puts a sheet of its own, of the user's origin, into the page's view (`demoMacSheet`):
  the page's sidebar (`.jc-console .side`, the page's own selector, which `test/demo-page.test.ts` holds the page to)
  takes those 26 pixels as padding at its top, so its contents start below the buttons and its ground runs on under
  them; the page itself is not edited. The page has two rules of its own for a Mac window, keyed on a class
  (`html.desktop-mac`) the view never sets: that padding, which the sheet gives, and a drag region over the sidebar's
  top 284 by 26 pixels (`#title-drag`), which it leaves out, since no drag region of the page is the window's and our
  band drags those same pixels; `test/demo-page.test.ts` holds the page to exactly these two. The window is dragged by
  our band (`shell/demo-band.html`, no script): the window's own contents, under the two views, whose body is a drag
  region over the window's top 26 pixels, from the window's left edge to a span of `demoGeometry.macCut` (177) pixels at
  its right end where our switch stands, so over the sidebar and over the Demo strip up to the switch. The span holds
  the switch by construction: the switch's page fixes the control's greatest extent in its own stylesheet, its box at
  most 160 pixels wide, its segments hiding what of their words would not fit, 12 from the right edge, with a shadow of
  5 (`demoSwitchMost`), and the span is those together, the one number the layout uses and the band's sheet
  (`demoBandSheet`), which the desktop puts into the band once it is loaded and before either view is; until it lands
  the band's own stylesheet cuts out the whole band. Electron asks each of them whether a point is in its drag region,
  the window's own contents first, whichever view is drawn over the point; so the band's body alone drags, as high as
  `demoGeometry.macBand`, the document as a whole carries no drag region, which would take every click meant for the
  page and the switch, and the band never drags where the switch stands. Inside the span the switch's page decides what
  drags and what takes a click, as on Windows and Linux: all of it drags but the switch itself. Under the page, the band
  is the page's own ground, the sidebar's colour over the left 284 pixels and the page's white beyond, never the Demo
  strip's yellow. On Windows and Linux the views
  start at the top of the content, and the caption buttons stand over the right end of our switch's view, which
  Electron tells the free title bar area in the view's own coordinates; the switch's page pads its right by the buttons' width from those variables, so the switch stands left of
  them and the rest of the view moves the window. The page does not know the buttons are there: it picks its sentence
  by the window's width alone. So there the desktop puts a sheet of its own, of the user's origin, into each view
  (`demoStripSheets`): the page's strip is inset by the buttons' width, read from the same variables, and our switch's,
  and the page shows its own short sentence wherever the strip so inset is too narrow for its long one; where even the
  short sentence would reach the switch, at the narrowest widths, the switch's two segments take less padding, with the
  same words and height. No word of the page lies under the switch at any width the window allows (`demoStripAt`, held
  by `test/demo-page.test.ts` at every width, with the buttons' width as Windows, GNOME and Plasma draw them). The
  page's sentence is drawn in the font the page carries, so its numbers hold on both systems; our switch's are measured
  in Segoe UI, so on Windows its words are drawn in that font by name, and on Linux, which lacks it, in the system's
  own: Ubuntu's and Plasma's fonts were seen to fit in the room kept beside them, and a much wider one may put the
  sentence's last letters under the switch's edge. On macOS neither is put, only the sidebar's sheet above.

## Links

A `jason-desktop://` link opens a page of the application and does nothing else: no command, no turn, no text put into
a chat, no card, approval or question answered, no chat mode, no setting (`src/deep-link.ts`). The scheme is the
desktop's own on every platform. `jason:` is the runtime's: on Windows its toast host registers it for the command line,
which handles `jason://attention...` ([`docs/notifications.md`](../docs/notifications.md), section 7); the desktop never
registers, declares or removes it. Where the desktop's registration names a program that is there, the command line
hands the same list or item on as `jason-desktop://attention...`, made from the parsed link, and opens the browser UI
otherwise. The addresses are a closed table:

| Address | Opens |
| --- | --- |
| `jason-desktop://feed` | the Feed |
| `jason-desktop://needs` | Needs you |
| `jason-desktop://campaigns` | Agents |
| `jason-desktop://campaign/cmp_…` | that campaign's page |
| `jason-desktop://approval/apr_…` | Needs you, with that approval open; deciding it stays a person's click |
| `jason-desktop://question/dec_…` | Needs you, with that question open |
| `jason-desktop://workitem/wi_…` | Work items, at that item |
| `jason-desktop://person/cnt_…` | People, at that person |
| `jason-desktop://attention` | Needs you, as a toast's click on the list hands it on |
| `jason-desktop://attention/approvals/apr_…` | Needs you, with that approval open |
| `jason-desktop://attention/questions/dec_…` | Needs you, with that question open |

An id is its prefix and 26 uppercase Crockford characters; one trailing `/` is taken, and anything else (a query, a
fragment, a percent escape, another page word, a second id, more than 72 characters) opens nothing. The page receives
the page the desktop parsed, never the address. The log has three lines for links, none of which holds the address:
`deep_link_opened` with the page, `deep_link_refused` with the refusal's code, and `deep_link_registration_failed`
when Windows did not take the registration.

- **Windows.** The installed application registers the scheme at each start under
  `HKEY_CURRENT_USER\Software\Classes\jason-desktop`, with the command as Electron writes it,
  `"<...>\Jason.exe" -- "%1"`: the executable quoted, the `--` bare, the address quoted. What keeps an address from
  acting as a switch is that `--`, which ends Chromium's switches; the packaged build's fuse that refuses Node's
  inspector options; and the desktop's own pick, which takes a link only when the arguments are the address alone or
  `--` and the address. A start whose arguments carry anything beside them (a switch, a flag of the desktop's own, a
  second address, or the pieces a quote in a crafted address splits it into) opens nothing and quits; a start with
  `--no-sandbox` is refused before that. Electron's own check of a link's arguments stops at the first `--`, which
  comes first here, so it plays no part. The uninstall takes the registration off when it names this application.
- **macOS.** The bundle declares the scheme in `Info.plist`, and the system hands a link to the application by its
  `open-url` event, running or not; a link in a start's arguments is refused there.
- **Linux.** The deb's menu entry declares `MimeType=x-scheme-handler/jason-desktop;` and starts the program with `--`
  before the address (`Exec=/opt/Jason/jason-desktop-app -- %U`). The AppImage declares no scheme and takes no link,
  and neither does a development build.

A link that comes before the page is loaded waits for it, the newest replacing an older one; the window comes forward,
also when the link is refused.

## Processes

Every process is started with a file and arguments, never through a shell, and only by the named modules:
`src/cli.ts` (the bundled `jason`), `src/programs.ts` (the agent hosts, `reply`, `node` and `npm`, and a set-up chat's
naming run, whose command line `src/chat-naming.ts` gives), `src/chat-process.ts` (a chat turn's host, and Codex's
model listing, `codex app-server`, which only `src/model-lists.ts` starts), `src/open-in-start.ts` (an editor or the file
manager that Open in names, started on its own with the folder), `src/shell-path.ts` (on macOS and Linux
only, the login shell's PATH, read before anything is started) and `scripts/prepare.mjs` (`dotnet publish`).
On Windows, a program npm installed is a batch file; the desktop reads which script it hands to Node and starts that
with Node. `test/process-guard.test.ts` holds this, and also refuses a `require` of `child_process`, an IPC handler
registered anywhere but `src/ipc.ts` (on `ipcMain` or on a `webContents.ipc`), `shell.openPath`, and `shell.openExternal`
outside the navigation lock in `src/main.ts`, but for the Demo's opener there, `openDemoRepository`, which hands it the
repository's address alone, once, and is called once, by the Demo window; `src/main.ts` binds no name of its own to that
address.

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
runtime, and an update goes on without a look. Once the files are in place, at every install and every update, silent
or not, the installer sets the machine up through the carried `jason install` with no flags, run with no window and
waited for (`customInstall`): `jason` on this account's Path, the skills for Claude Code and Codex where each is found, and an older
command-line install retired, with nothing written under the data directory and no runtime started; what it did not get
done never fails the install. On macOS, Windows and Linux, Jason is one installation: the Jason Desktop application, which
carries `jason`, the command line your terminal and your agents use. It is removed once, by the one uninstall, the
carried `jason uninstall` (`docs/release-and-update.md` §9), which takes off what Jason and Jason Desktop placed and
leaves the data directory unless told otherwise and the harness root always. A person's uninstall from Installed apps
starts the application with `--uninstall` (`src/uninstall-start.ts`), which runs that uninstall as
`jason uninstall --keep-application`, shows what was taken off and what is left unless the uninstall is silent, and
exits; the uninstaller then removes the application's folder, unless the exit says to go no further.

On macOS there is no installer, so the desktop's own look before an update is the whole guard. The Jason menu's
Uninstall Jason… (and the tray's; `src/uninstall-door.ts`) asks first,
with a box for the data directory, Cancel the default, and on the person's word starts the carried `jason uninstall`
on its own with `--wait-for` this process's id, and quits; the verb deletes Jason.app last and writes what it did to a
file in the temporary folder, which the dialog names. `jason uninstall` from a terminal is the same uninstall, with
Jason quit. When Jason will not start, run `Contents/Resources/bundle/jason uninstall` inside Jason.app
(`/Applications/Jason.app/Contents/Resources/bundle/jason uninstall` where it was put in Applications).

## On Linux

- The window shows no menu bar, as on Windows (above): the menu's keys act, and the tray carries the Jason menu's acts
  but About. The update item and Back to Your Own Installation have keys of their own (`itemKeys` in
  `src/keybindings.ts`: Ctrl+Shift+D and Ctrl+Shift+L, Cmd on a Mac), so a session that shows no tray icon (GNOME
  without an extension for one) still reaches them. The update key acts on the item's current step (check, download,
  restart to install), which shows only in the item's label: on such a session, nowhere. At the install it asks first,
  naming the version (`pressItem`, `installQuestion`), on every platform, since the install stops the runtime, which
  ends a running turn and the work in progress, and restarts the desktop; a click on the item, whose label says so,
  installs at once. The tray's item takes every press as a click: a Linux tray reports a click with no mouse button,
  which Electron's `triggeredByAccelerator` counts as a key's. The page shown when the Demo could not be shown names the
  back key; the versions-differ sentence and the System screen's notice of an update name the update key where the item
  can act (`updateKeyWords`, which the page reads through the seam's `updates.key`), never under "Updates Come to an
  Installed Build" or "Through the Package", where the key does nothing. `jason update`'s refusal of a runtime the
  desktop carries still names the menu and the tray alone. Such a session has no way from the window to the items
  without a key: Open Jason at Login, Start the Runtime at Logon, Command Line and Uninstall Jason…. Closing the
  window quits the application; the runtime keeps running. A start at sign-in opens the window minimized.
- Open Jason at Login writes `~/.config/autostart/ai.jasonharness.desktop.desktop` (under `$XDG_CONFIG_HOME` when that
  is set), whose `Exec` line starts the AppImage file or the installed executable with `--opened-at-login`; it reads as
  on only while that line is there and the entry is neither hidden nor marked `X-GNOME-Autostart-enabled=false`. A path
  the entry cannot spell, one with a `%` or a control character, leaves the item off. An update that installs the
  AppImage under a new file name moves an entry that is on to it.
- The Command Line item is the machine set-up's report, as on macOS and Windows (`commandIsSetup` in
  `src/machine-setup.ts`; [desktop.md](../docs/desktop.md#the-command-line-and-the-login-items)): a start of a new version
  runs the carried `jason install` with the login shell's PATH as `--terminal-path`, from the `jason` the deb carries or
  from an AppImage's copy under `<data>/desktop/bundle/<version>/`, never from the mount, and runs none where there is no
  copy. From the deb the terminal's `jason` is the package's own `/usr/bin/jason`, which the package's scripts write;
  from an AppImage it is `jason install`'s wrapper in `~/.local/bin`, with the installer's PATH lines where a new
  terminal's PATH lacks that folder. The login shell is asked from the PATH the start was handed less what an earlier
  start added to its own (`handedPath` in `src/shell-path.ts`): a desktop an update or the mode switch starts inherits that
  start's PATH, its usual places at the end and its AppImage mount in front, and a profile that keeps the PATH it is
  given, as Arch's does, would otherwise print them back as a terminal's. `src/path-command.ts` remains only for a system
  the set-up does not cover.
- An AppImage runs from a mount that lasts only while it runs, so each start copies its `jason` to
  `<data>/desktop/bundle/<version>/`, and that copy is the `jason` it runs, starts the runtime from, puts on the PATH and
  gives the chat. A copy already there is used only when every file matches the package's; one that differs, or a link
  at the bundles folder or the version's folder, stops the start and is named, to be removed by hand. Without a copy,
  Start the Runtime at Logon and the Command Line's acts are not offered. A start at logon the person set up from
  another version's copy is set up again from this one at each start, the same act as turning it on, and the copy's own
  `jason install` writes the wrapper anew; one
  never set up, or one that names a `jason` elsewhere, is left alone. The copies of other versions go after that, but
  the one an update kept to go back to, one something set up still names, and one the running runtime was started
  from. No folder of the mount reaches what the desktop starts: the PATH and the libraries it hands on are the
  session's.
- Jason never runs without Chromium's sandbox. An AppImage's launcher, which electron-builder writes, adds
  `--no-sandbox` wherever the system refuses unprivileged user namespaces; a start that carries it, on any platform,
  is refused before anything else is decided: a dialog says so, one log line says it was refused, and the application
  quits (`src/sandbox.ts`). So the AppImage needs a system that lets it run its sandbox. It is built so that on Ubuntu
  24.04 and later, as shipped, it says so and names the deb, whose install brings an AppArmor profile that lets the
  program make the user namespaces its sandbox uses; neither package has yet been seen on such a machine.
- Updates come to the AppImage through electron-updater, which replaces the file where it is and keeps the AppImage
  file itself under `<data>/desktop/rollback/<version>/`. Each start of an AppImage records its file in
  `<data>/desktop/appimage.json` (`recordAppImage` in `src/linux-package.ts`), and so does an update that installs it
  under a new name, so that the command line's update hands over to that file; a terminal's `--update-apply` request is
  taken by an AppImage alone (`requestSupport` in `src/update-request.ts`). The deb's electron-updater is turned off
  (`updaterOf`): the system updates the package from Jason's apt repository, so the update item says "Updates Come
  Through the Package", and where the package's `version.json`, watched once a minute, names another version than the one
  running, "Restart Jason and Its Runtime to Finish the Update to <version>", with one notice whose click does the same
  (`menuItemOf` and `restartForPackage` in `src/updates.ts`): the old build's runtime stopped, as an update's install stops
  it, then a relaunch, whose machine set-up finishes that update. A start that finds the runtime this application's own
  `jason` started still running an older build, the system having updated the package while no window was open, offers
  the same item and notice (`packageMovedTo`); neither ever restarts by itself, since a restart ends a running turn. On
  the deb no sentence names Check for Updates: the versions-differ sentence names that item or `jason runtime restart`,
  or the system's update where the runtime is newer. If a version the deb's updater recorded does
  not start, the page names the previous release's deb and `sudo apt install ./jason-desktop-linux-x64.deb`, after the
  source's removal where that release is before 0.49.1 (`keptNotice`). An AppImage
  replacing itself has been seen in virtual machines (Arch, GNOME and Hyprland), from its update item and from `jason
  update apply`. The updater's cache is
  `~/.cache/jason-desktop-updater`, or under `$XDG_CACHE_HOME` when that is set.
- Uninstall Jason… in the Jason menu (and the tray's; `src/uninstall-door.ts`) is the one uninstall, as on macOS: it asks
  first, with a box for the data directory, Cancel the default, and on the person's word starts the carried `jason
  uninstall` on its own with `--wait-for` this process's id, and quits. For the deb the verb takes the package
  `jason-desktop` off first, once the runtime has stopped, purged by apt through the system's own password dialog, and
  removes nothing of the account's when that is refused; for an AppImage it removes its menu entry, its icons, the copies
  of its `jason` and the AppImage file, last. Remove What Jason Placed… (`src/remove-placed.ts`) is offered on no system.
  The system's own removal leaves the account's files, and what they start is made harmless: the autostart entry and
  the AppImage's menu entry name the file they start as `TryExec=` (`autostartEntry` in `src/login-item.ts`), and the
  runtime's systemd user unit carries `ConditionPathExists=`. When Jason will not start, take off by hand what it placed.
  Its `jason` is `/opt/Jason/resources/bundle/jason` for the deb, and each `<data>/desktop/bundle/<version>/jason` for an
  AppImage:

  1. Read `installed_image` in `<data>/run/runtime.json`. Only when it names that `jason` is the runtime this
     application's: stop it with `<that jason> runtime stop`. A runtime of any other `jason` stays.
  2. Run `<that jason> runtime autostart status`. When the `executable` it names is that `jason`, run
     `<that jason> runtime autostart disable`, which takes off the runtime's systemd user unit.
  3. Delete `~/.config/autostart/ai.jasonharness.desktop.desktop` (under `$XDG_CONFIG_HOME` when that is set), which is
     Open Jason at Login, when its `Exec` line starts the AppImage file or the deb's executable.
  4. Delete the `jason` (or `jason-desktop`) wrapper in `~/.local/bin` whose `exec` line runs that `jason`. Once that
     folder holds no `jason` or `jason-desktop` and nothing else but Jason's `jason-LICENSE` and
     `jason-THIRD-PARTY-NOTICES`, take the line `# added by jason install` and the
     `export PATH="…/.local/bin:$PATH"` line under it out of `~/.profile`, `~/.bash_profile`, `~/.bash_login`,
     `~/.bashrc`, `~/.zprofile` and `~/.zshrc`, wherever they are.
  5. For an AppImage, once nothing runs from them, delete the folders named as versions under `<data>/desktop/bundle/`.
  6. Remove the application: `sudo apt purge jason-desktop` for the deb; for an AppImage the file, its menu entry
     `~/.local/share/applications/jason-desktop.desktop` and its icons under `~/.local/share/icons/hicolor/`.

## Building and testing

From `desktop/`:

```sh
npm ci
npm run check         # the type-check of the sources and the tests
node node_modules/electron/install.js   # Electron's own binary, for the rows that run under it
npm test              # the Node tests
npm run prepare:bundle -- --pre rc.1   # publishes this checkout's jason into bundle/ as <VersionPrefix>-rc.1
npm run dev           # builds and starts the window
```

The electron package fetches its binary only when asked, held to the sha256 its `checksums.json` names, and no test
downloads it. `test/rollback-electron.test.ts` runs the copy an update keeps aside under that binary as Node, where the
file system reads `resources/app.asar` as an archive as the application's main process does; without the binary it skips
and says so, and with `JASON_DESKTOP_ELECTRON_REQUIRED=1`, as CI sets it, it fails instead.

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

The program is `jason-desktop-app`, so the deb's `/usr/bin` link to it never takes `jason-desktop`, the name an earlier
version's PATH command fell back to; the deb's own scripts (`installer/deb/`) add `/usr/bin/jason` and Jason's apt
source, signed by the key the package carries, and take them back. The AppImage is packed on electron-builder's static AppImage runtime, which needs no libfuse2. The two
forms are packed at once from one folder, and the deb writes `resources/package-type` into it, which makes
electron-updater update a package as a deb, so the configuration starts the deb only once the AppImage is built.

The release passes `--executable` with the `jason` it has already built and signed, so the package carries that very
file, and signs the package itself: a Mac package is never built unsigned. Inside `app.asar` are `lib/`, `shell/`,
`resources/` (the application icon as a PNG, and the tray's images; the Mac's icon set is read at build time from `installer/icon.icns`, and the Linux packages install the PNGs of `installer/linux-icons/`, the same icon set's 32, 64, 128, 256, 512 and 1024 px images, under hicolor), `demo/index.html` (the demo page, without its folder's README and licence text), the chat's two lists and its instruction text; beside it, in the package's `resources/`, are `bundle/`
(the executable with `LICENSE` and `THIRD-PARTY-NOTICES`, the skills and `version.json`) and `notices/` (the
repository's `LICENSE`, this folder's `THIRD-PARTY-NOTICES` and Chromium's `LICENSES.chromium.html`). The package
carries no UI: `jason ui install` places the UI of the runtime's own release.

electron-builder fetches Electron's distribution for each platform and architecture it packs. The configuration hands it
the sha256 of each file from the `checksums.json` the `electron` package carries, which `package-lock.json` pins, so a zip
that does not match, downloaded or cached, stops the pack; its own tools (nsis, the resource and icon tools, 7-Zip,
dmgbuild) it checks against the sha256 its own source names.

`scripts/check-package.mjs` reads a built package back against this configuration: the feed and, on Windows, the
publisher its `app-update.yml` names, the fuses its binary carries, and on Linux what each form is: a deb must carry
`resources/package-type` saying `deb` and an AppArmor profile that lets `/opt/Jason/jason-desktop-app` make user
namespaces; an AppImage must carry no `package-type`, hold `AppRun`, and have a menu entry at its root whose `Exec`
line is there and carries no `--no-sandbox`. The release runs it on every package it builds:

```sh
node scripts/check-package.mjs --app dist/win-unpacked --platform win32
node scripts/check-package.mjs --app <unpacked deb>/opt/Jason --platform linux --form deb
node scripts/check-package.mjs --app squashfs-root --platform linux --form appimage   # after the AppImage's --appimage-extract
```

To run it against an installation other than your own, set `JASON_DATA_DIR` and `JASON_Harness__Root` in the environment
it is started from.
