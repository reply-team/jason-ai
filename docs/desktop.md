# Jason Desktop

Jason Desktop is a window onto the installation the `jason` command line already uses. It is one more client of that
installation, as the [browser UI](ui.md) is: it finds the data directory as the CLI does (`JASON_DATA_DIR`, otherwise
`~/.jason`), starts the runtime only through `jason runtime start`, and changes the installation only through CLI verbs
and the settings file a person would edit. There is no second data directory, no second harness root and nothing in the
runtime that exists for the desktop.

The full experience is still a session with your agent, by text or by voice, with the skills loaded; the desktop is for
the moments you want to see a screen, and what a screen shows is never all there is. It adds three things the browser
cannot do: a chat that starts an agent on your computer and asks you before the agent acts, a tray that counts what
waits for you, and the one-step setup of Reply, your notification channels and your AI provider's key. Everything else
it shows is the browser's UI, served by the runtime ([ui.md](ui.md)).

This page is for the person using it. [desktop/README.md](../desktop/README.md) is the builder's view of the same
program: its modules, its guards and how a package is made.

## Getting it

Each release publishes Jason Desktop beside the runtime's downloads ([release-and-update.md](release-and-update.md),
§2). Every package carries that release's `jason` executable, with `LICENSE` and `THIRD-PARTY-NOTICES`.

| System | File | Notes |
| --- | --- | --- |
| Windows on x64 | `jason-desktop-windows-x64-setup.exe` | Run it. It installs for your account, with no administrator rights. The installer, its uninstaller, `Jason.exe` and the `jason.exe` it carries are signed. |
| macOS on Apple silicon | `jason-desktop-macos-arm64.dmg` | Open it and drag Jason into Applications. Signed, notarized and stapled. |
| macOS on Intel | `jason-desktop-macos-x64.dmg` | The same, with an Intel `jason`. It is built and signed like the Apple silicon one, and the test suite does not run on an Intel Mac. A `jason` carried by the Intel application moves only with the desktop's own update, so `jason update apply` there answers `update_not_updatable`. |
| Linux on x64, Debian and Ubuntu | `jason-desktop-linux-x64.deb` | `sudo apt install ./jason-desktop-linux-x64.deb`. It installs to `/opt/Jason` and puts Jason in the applications menu. This is the package to take on Ubuntu 24.04 and later. Not signed. |
| Linux on x64, any | `jason-desktop-linux-x64.AppImage` | Make it executable and run it from where you keep it. Not signed. |

Each file also has a short address on the download address, which sends you to the latest release's file:
`https://get.jasonharness.ai/desktop/windows`, `/desktop/mac` (Apple silicon), `/desktop/mac-intel`,
`/desktop/linux-deb` and `/desktop/linux-appimage`. The address counts, by day, how often each is used and from which
link a person came, and keeps nothing of the person ([responsible-use.md](responsible-use.md), "Update check and
downloads"). The release page lists the same files, and the address's `/releases/*` paths are unchanged.

A release's `checksums.txt` lists each of them. On Linux nothing is signed, so `sha256sum --check --ignore-missing
checksums.txt` beside the file proves it is the file the release published and not who built it. The `jason` the Linux
packages carry needs the ICU library (`libicu`), which desktop distributions have; the deb recommends it.

Jason never runs without Chromium's sandbox. An AppImage's launcher adds `--no-sandbox` wherever the system refuses
unprivileged user namespaces, and Jason refuses a start that carries it, with a dialog and no window, on every system.
So the AppImage needs a system that lets it run its sandbox. It is built so that on Ubuntu 24.04 and later, as shipped,
it says so and names the deb, whose install brings an AppArmor profile that lets the program make the user namespaces
its sandbox uses. Built so, not yet seen on a real machine: neither package has been seen there; each was started once
under a virtual display, sandboxed, when it was built.

## What it does at a start

The desktop asks `jason status` whether a runtime is up and whether it is this build, and starts one with
`jason runtime start` only when none is; a runtime that is already up is used as it is, and `jason runtime run` is never
started as its child. It places the release's UI with `jason ui install` when the runtime is the version the desktop
carries and no UI is served, deploys the role skills (`jason skills install --roles-only`) and the chat's skills from the
package it carries, once for each desktop version, and loads the link `jason ui open --no-open` prints. When the runtime is another version, the UI it
serves is loaded as it is and the window says the versions differ: it names the desktop's own update where that runtime
is the one the application carries, and `jason update` otherwise.

The desktop starts every program with a file and arguments, never through a shell, and only from the modules that are
named for it; a test holds that, and holds the page to a window that reaches the desktop through one object,
`window.jasonDesktop`, answered only for the top frame of the runtime's own page. Navigation is locked to that origin; a
link that leaves it opens in the system browser, and only when it is `https:`.

## What you see

The desktop wears the console: the chat is home, in a dock with panels beside it. The window opens on **Start**, which
asks "Who do you want to reach?". What you type there starts a set-up chat on the AI you connected, about no campaign
yet; no chat is made until you send. Enter sends and Shift+Enter makes a new line. If no AI is connected yet, the AI card
shows before anything is typed and what you send waits, while the window is open and only then, until one is connected;
**Take back** returns the words. Start opens every time the window opens, also when campaigns exist, and the sidebar's
**New chat** still makes a chat directly.

- **Needs you** lists approvals and questions, says for an approval who it reaches, from which account and on which
  channel, and narrows to All, Approvals or Questions. With nothing waiting it says you are caught up and offers
  **Open your campaigns**.
- **Campaigns** and **Activity** follow the journal as it is written. A campaign's Start, Pause and Resume are in its
  header, beside Add people, so they are there from every tab.
- The **Feed**, the sidebar's first item, is the desktop's main chat (below).
- **Connect** shows the agent hosts found on the machine, the model keys, the Reply connection and the notification
  channels (below).
- The browser's other screens, People, Performance, Work items, System and Settings, open in a page panel of the same
  dock. [ui.md](ui.md) describes each.

**Appearance** (Settings) offers Light, Dark and the system's, in that order. A new installation meets Light; one that
existed before keeps what it had, its chosen theme or the system's if it never chose. In the desktop the choice is kept in
its settings and the starting page and the window's background before the page loads follow it.

The View menu carries the keys (New Chat, What Needs You, Performance, Campaigns, Work Items, People, System, Reload).
The Jason menu carries Open Jason at Login, Start the Runtime at Logon, Command Line, the update item and, on macOS and
Linux, Remove What Jason Placed. The tray's menu opens the window, a new chat and what needs you, and carries the same
acts for the installation. The tray counts what waits for you, approvals and questions, read through
`jason approval list` and `jason decision list` no more often than every 30 seconds while a runtime answers; a system
notification carries the counts alone, never a name or the words of a question. Both login items are off until you turn
them on.

## Its own files

- `<data>/desktop/settings.json`: the desktop's settings (a `feed`, the theme, and the version whose skills were last deployed).
- `<data>/desktop/chat/`: the chat's working folder. Its skills, Jason's and Reply's, are in `.claude/skills/` for Claude
  Code and in `.agents/skills/` for Codex. Its `CLAUDE.md` and `AGENTS.md` are the application's own instructions to the
  agent, written again when it starts and before each turn, so an edit to either does not last.
- `<data>/desktop/conversations/`: the chat's conversations, one record each.
- `<data>/desktop/provider-keys`: AI providers' keys, encrypted by the operating system. On Linux a key is kept and read
  only where the session offers a keyring (GNOME Keyring or KWallet); where Electron would fall back to its fixed-key
  store, the card says so and the key is not kept.
- `<data>/desktop/claude-keyed/<provider id>/` and `<data>/desktop/codex-keyed/`: the folders a turn on a provider's key
  runs its host with. They keep the host's transcripts of those turns, never the key, and are readable by your account
  alone where the system has modes.
- `<data>/desktop/turns/`: one small configuration file per running chat turn, readable by your account alone, naming the
  desktop's permission endpoint and carrying that turn's secret. It is removed when the turn ends, and any a crash left
  are removed at the next start.
- `<data>/desktop/rollback/<version>/`: the application as it was before the last update (below).
- `<data>/desktop/bundle/<version>/`: on Linux, an AppImage's copy of the `jason` it carries. An AppImage runs from a mount
  that lasts only while it runs, so each start copies its `jason` here; that copy is the `jason` it runs, starts the
  runtime from, puts on the PATH and gives the chat. A copy is used only when every file matches the package's, and one
  that differs, or a link where the folder should be, stops the start and is named, to be removed by hand.
- `<data>/desktop/electron/`: Electron's own profile, the page's storage and its logs, so a second data directory is a
  second desktop and nothing of it lands outside `<data>`.
- `<data>/logs/desktop-YYYYMMDD[_NNN].jsonl`: its log, in the runtime log's shape and under its limits, and never any
  text a person gave Jason. `jason logs --source desktop` reads it, and `jason doctor` carries a bounded slice.

`jason export` carries no part of `<data>/desktop/`, the keyed folders included, `jason doctor` carries none of a
conversation or of a keyed folder, and `jason uninstall --purge-data` removes `<data>/desktop/` with the rest of the data
directory.

A Connect card writes, into `<data>/config/settings.json`, the keys of the plugin it configures and no other, as a person
would write them (`Plugins:Config:<id>`, `Plugins:Grants:<id>:Http`, `Notifications:Plugins:<id>:Binding`), and then runs
`jason plugin reload`. A file it cannot parse is refused and left as it is. [desktop/README.md](../desktop/README.md)
lists each key and each card's grant.

## Connect

- **Agent hosts.** The cards detect `claude`, `codex`, `reply` and `node`. A missing program is explained with its install
  command and a link, and the command runs on a click when `npm` is present, never without one; Node itself is never
  installed. The desktop never signs in to a host: sign in once by running the host in a terminal, and the chat uses that
  sign-in.
- **AI provider keys.** Anthropic and OpenAI keys, and the Anthropic-compatible providers DeepSeek, Kimi, GLM and
  MiniMax, which run through Claude Code with a token and a base address (GLM's default models are `glm-5.3` and
  `glm-5.3-flash`, MiniMax's `MiniMax-M3.1-Flash`). A key is kept by the operating system's store, reaches the host's
  process environment and nothing else (`CODEX_API_KEY` for Codex), and is never written to a file of Jason's. A turn on
  a key runs its host with a configuration folder of the desktop's own, holding no claude.ai login and no saved Codex
  login, and without the variables that name another credential, a profile or a federation: the design is that a key the
  host does not take fails the turn instead of running it on your own plan, as built and not yet seen with a real host.
  A refused key ends the turn at once with a sentence naming the provider's card in Connect (OpenAI's key, run through
  Codex, ends it with Codex's own words), and on a turn on a key the exact key is replaced by `[key]` in every line of text and every
  step the host says, and a host's error text also loses the key's first six and last four characters and any token
  shaped as a provider's key, before the page shows it or the conversation keeps it.
- **Reply.** Signed in with `reply auth login`, or with a key on standard input that only the Reply CLI keeps, then the
  plugin installed and, only where you have set no default route, wired as the default. A card says "configured" when the
  runtime accepted it, never "connected". What the desktop tells Reply about itself is one label in the User-Agent of
  `reply`'s requests and nothing else: `jason-harness/<the bundle's version>`.
  The card lists the Reply account's mailboxes and its LinkedIn accounts, each channel read on its own, so an account
  without LinkedIn still shows its email. A channel that account's plan or role does not have says so ("This Reply
  account has no LinkedIn."), and one that could not be read says that and offers **Check again**; the other channel's
  accounts stay in every case. The list is read again when the window takes the focus back, at most once in 30 seconds.
  **Add account** offers Gmail, Outlook, another mailbox and LinkedIn (a channel the account does not have is not
  offered), and a row that is not in a good state offers **Reconnect**. Each opens a dialog with Reply's own link for the
  act, its host in plain text, **Copy link**, **Open in the browser** and **Check now**. Reply's page needs no Reply
  sign-in, so you can send the link to whoever owns the mailbox or the LinkedIn account, who signs in to Google,
  Microsoft or LinkedIn there with their own credentials; a mailbox by SMTP or IMAP is added on Reply's page, so its
  password never passes through Jason. For a mailbox the dialog warns that anyone who has the link can add and manage
  the Reply account's mailboxes and that it does not expire. A LinkedIn link is valid for a limited time, and the
  card lists pending connections with Waiting or Expired and their expiry. The desktop asks Reply for the link, checks it
  before showing it, shows it only on your click, and keeps it nowhere after the dialog closes. Finishing happens on
  Reply's side, so the card learns of it by **Check now**, **Check again** or a focus. Limits, signatures, pauses and
  removal stay in Reply.
- **Notification channels** (Slack, Telegram, ntfy, the journal forwarder). One act configures a plugin, and Disconnect, on the Slack, Telegram
  and ntfy cards, removes what it wrote.

## The chat

A person talks to an agent host that runs on their own machine, and the host works the installation through the same
`jason` command a person types. The person's words go to the host on standard input, never on its command line.

### Hosts

Claude Code and Codex drive the chat. Gemini CLI, Antigravity CLI, Cursor CLI and GitHub Copilot CLI are listed as "runs
campaign work, not the chat yet" and are never substituted for the one you chose; the key providers above run through
Claude Code.

- **Claude Code's floor is 2.1.257.** The desktop reads `claude --version` at each listing of the hosts and before each
  turn, never remembers it, and treats a version it cannot read as below the floor. Below it, the host is listed with a
  sentence that names the version found, the floor and how to update, it cannot be chosen, and a turn sent to it is
  refused before the host starts, a key provider's turn on it included. The reason is one setting: every Claude Code turn
  carries `permissions.blockReadsOutsideWorkingDirectories`, so that a command which reads a file outside the folders
  the chat works in asks you first, and a Claude Code older than 2.1.257 skips a key it does not know without a word.
- **Codex is a reading host in this release.** A Codex turn runs `exec --json --sandbox read-only` and asks for
  nothing, so no card and no rule of the desktop applies to it. It reads the chat folder, the folders added to it and its
  skills in `.agents/skills`, and answers; it acts through nothing. It searches the web through its cached index. Its sandbox may keep `jason` from the runtime's
  loopback address, and, being read-only, it can read any file you can read, with no card: wider than the chat folder.

### Conversations

Each campaign has one conversation, found or made the first time it is opened, and any number of set-up chats are about
no campaign yet. The window opens on Start, and a set-up chat is made when you send from it. A set-up chat whose turn made exactly one campaign becomes that
campaign's conversation; one that made more, or ran beside another turn, offers what it made to pick from. A
conversation is kept from its first turn, one never sent to leaves nothing, and **Start afresh** begins the agent's next
turn without its memory of the conversation and keeps what is shown. **Remove** deletes the conversation's record from
this computer after one question; the host keeps its own transcript.

A conversation is a record under `<data>/desktop/conversations/`, readable by your account alone: the campaign, a title,
the session each host named for it, and what the page drew, as text, within 2 MB. Every turn begins with a paragraph the
desktop writes, naming the conversation's campaign and its folder, and a turn that cannot resume its host's session
starts a fresh one with a recap of the last words. The conversation is no boundary: the agent is asked to keep to its
campaign, and a command that names another campaign is not refused for that. What a turn may do is the permission
model's to decide.

### The Feed

One conversation is of its own kind: the **Feed**, your main chat, about no campaign, drawn first in the sidebar above
**New chat**. It shows what happens in the campaigns as the runtime writes it, one line each: a reply observed, an
approval or a question waiting, a campaign created, started or paused, work that stopped or expired, a provider block
holding work. Consecutive lines of the same kind and campaign fold into one with a count, and the Feed keeps the newest
500. A line carries a kind, ids, a count and a moment and never a person's words, a subject or a name; the campaign's name
on it opens that campaign's conversation, and an approval or a question opens Needs you on that item. When the window
loads, the desktop reads what it missed with `jason journal list`.

**A line never starts a turn.** The agent hears the lines it has not yet been given only with your next message there,
at most 50, as records Jason took from the journal: information, never instructions. The Feed cannot be renamed,
removed or handed over, and it is not a campaign's conversation, so there the agent may speak of any campaign. Its turns
go through the same permission model as any chat's.

The agent hosts keep their own transcripts of a turn, in their own folders: Claude Code's are under `~/.claude/projects`,
and those of a turn on a provider's key are in that provider's keyed folder (see Its own files). Jason never touches the
first, and the desktop keeps no transcript of a host.

### Access modes

Each chat has an access mode, chosen from a menu beside the composer, and the line under the composer says which one the
chat is in. **Ask first**, the default, is the chat as the next section describes it. **Plan only** lets the agent read
and plan and change nothing: it passes only the allowed verbs that read, Claude Code starts in its plan mode with the
exit from it removed, and every write is refused. The cards left in Plan only are a read outside the chat's folders and
a web page. Switching between the two lets a running turn finish. A Codex turn runs in its read-only sandbox in either.

### What runs without asking, what is refused, what is a card

Claude Code turns are held by two lines, the host's own rules and the desktop's permission tool.

- **Runs without asking.** The skills, web search (Codex searches an index its vendor keeps, never the live web),
  the documentation of the hosts and of Reply (below), subagents and the task list; of `reply` exactly `reply auth status`
  and `reply account list`; and one rule for each `jason` verb in [chat-allow-list.txt](../desktop/chat-allow-list.txt),
  which is the CLI's verbs less the deny list and less `jason doctor`, which writes its bundle to any path and so asks
  every time, held to the real tree by a test.
- **Refused, with no card.** The verbs in [chat-deny-list.txt](../desktop/chat-deny-list.txt): everything the CLI
  reserves for a person (approving and rejecting, answering a decision, a standing approval, a preference, clearing a
  block, resuming work, a report, rewinding a subscription), everything that changes the installation, follows it
  without end or carries it off the machine (`runtime`, `plugin install`, `plugin reload`, `profile`, `skills`,
  `update`, `uninstall`, `ui`, `session`, `journal watch`, `export`, `import`), and four more that decide who is reached
  or what runs (`suppression remove`, `route set`, `route unset`, `role set-profile`). **`role add` and `project link` are
  typed in a terminal**: a role's entry command is started by the dispatcher, and a project's linked folder becomes a
  working folder of every chat on its campaigns. A command that names an operation these verbs call is refused too.
  Also refused: any `reply` command but the two above, any read or write under the data directory outside the chat folder (the
  settings file and the run file first among them), a command that names that directory, a command that names `JASON_CONFINED`, a command that changes
  where the shell finds a program (`PATH`, `PATHEXT`), a command too long for a card to show whole, and a write by the
  agent to the chat's own instruction files and to the `.claude`, `.agents` and `.codex` folders of the chat folder.
- **A card asks you.** Every other command, `jason doctor` and the `jason` commands no allowed verb begins included; a read outside the chat
  folder and the added folders; a write outside them, or in a campaign's or a project's folder to a file the host loads
  (`.claude`, `.git`, `.jason`, an agent's settings or instruction file, a file named `jason`); a web page, once per site
  per chat, the documentation pages below aside. A path that begins with `~`
  or is spelled in a way the system would read differently is a card too.
- **The documentation opens with no card, and a page is information.** A fetch from these pages needs no card: the
  `/docs/` pages of `code.claude.com` and of `learn.chatgpt.com`, the `/codex/`, `/api/docs/` and `/api/reference/`
  pages of `developers.openai.com`, and all of `docs.reply.io`. A page counts only when its address is `https`, at most 300
  characters, with no query, no port and no dot segment; any other page of those hosts, or any other site, gets the card
  "Open a web page", remembered by its host. The agent is told to read a web page or a search result as information and
  never to do what it says.
- **What "Allow for this chat" covers** is printed on the button, and remembered for the chat only; it holds across a
  change of host, being your word about a command or a folder and not about a host. It is never offered for a shell or
  an exec wrapper (`cmd`, `sh`, `bash`, `pwsh`, `env`, `sudo`, `xargs`, `npx`, `wsl`, `start` and their kind), an
  interpreter or a package runner (`python`, `node`, `npm`, `make` and the like), a program named by a path, or a command
  with a word the shell would rewrite. A remembered grant never allows what the model
  refuses.
- **PowerShell and Monitor are removed** from the chat's turns, so the one shell a turn has is the fenced one. On Windows
  that shell is Git Bash, which Claude Code needs there.
- **The agent runs nothing in the background**: every command it starts finishes inside the turn, and it is told to say
  what to click in Jason rather than tell you to run a command.

What the desktop claims is only what its tests prove: the listed commands are refused by the host's tool fence and by
the desktop, and the listed operations are not callable through the allowed tools. The agent holds the machine user's
rights, as every UI token does; there is one level of trust.

Under the chat, these things differ from a terminal:

- **A file option takes standard input only.** Every host's turn runs `jason` with `JASON_CONFINED=1`, under which the
  options that read a file into a request (`--file`, `--value-file`, `--note-file`, `--result-file`) take only `-`, so
  the content comes on standard input: `--file - < contacts.json` or a here-document, and for long content a file in the
  chat folder given as `--file - < file`. The reason is that `jason` opens such a file itself, which the host's file rules
  never see.
- **The provider check names a program, never a path.** `jason status --provider-cli <program>` runs a bare name found on
  the search path, and a name with a directory in it is neither looked up nor started; run a path from a terminal.
- **A credential in an environment variable does not reach the commands the agent runs.** Every Claude Code turn sets
  `CLAUDE_CODE_SUBPROCESS_ENV_SCRUB=1`, which strips what looks like a credential, by name or by value, from the commands
  the host runs. `reply` signed in through its own store is unaffected. On Linux the host runs those commands in a process
  namespace of their own, so a check that looks at a host process by its number may answer differently from inside the
  chat than in a terminal; what `jason status` sees of the runtime from there has not been checked on a real machine.
- **A folder the chat works in is trusted** the way a folder you open your agent in is trusted: the agent definitions and
  skills it carries run with what they declare, and the cards stand in front of what the agent itself asks for.
- **Limits that are said.** Claude Code's documentation leaves one case open, a read-only command that names no path after
  a `cd` the host traced outside the folders, and the desktop adds nothing for it. A network path is told by its spelling,
  so a mapped drive reads as a local one.
- **The CLI expands no response file, anywhere.** An argument that starts with an at sign is a word like any other, so
  `jason @args.txt` is a usage error and exits 2; a script that relied on it passes the arguments itself
  ([handbook.md](handbook.md), [the release notes](../RELEASE-NOTES.md)).

## Links

A `jason-desktop://` link opens a page of the application and does nothing else: it never approves, sends, drafts or
starts a conversation. The addresses are a closed table: `jason-desktop://feed`, `needs`, `campaigns`,
`campaign/<campaign id>`, `approval/<approval id>`, `question/<decision id>` and `workitem/<work item id>`, and
`person/<contact id>`. An approval or a question opens Needs you on that item, and deciding stays your click. An address
with a query, a fragment, a percent escape, a dot segment, a port or a second id opens nothing. The scheme is the
desktop's own on every system; `jason://`, which the runtime's Windows notifications use, is the runtime's and the
desktop never registers it.

On Windows and macOS the application registers the scheme, on Windows at each start for your account alone. On Linux the
deb's menu entry registers it and the AppImage opens no link. Built so, and not yet seen on a real machine: a link from a
browser on Linux. A start that carries anything beside the address opens nothing, and the Windows uninstall takes the
registration off. [desktop/README.md](../desktop/README.md) has the table and the rules for the builder.

## The command line, and the login items

- **Command Line** places a `jason` that your terminal finds and that runs the executable the application carries. On
  macOS and Linux it is a wrapper in `~/.local/bin`. On Windows it is a `jason.cmd` shim and, beside it, an extensionless
  `jason` for Git Bash (which finds a `.cmd` only by its full name) in `%LOCALAPPDATA%\Microsoft\WindowsApps`, placed only
  when that folder is on your PATH, since no PATH entry is ever written. It is placed and removed on your word, read back
  on every look, never rewritten silently, and never put in the place of a `jason` that is not ours: it is then named
  `jason-desktop`. The menu always shows the executable's full path.
- The Windows shim hands its arguments to cmd.exe's parser a second time, with `%*`, so a quoted argument beside `&`, `|`,
  `<`, `>` or `^` (JSON passed from PowerShell 7.3 or later, which hands a `.cmd` its arguments in its legacy way) can be
  split, or run as a command of its own. The shim is for a person typing; a script or an agent passes such arguments to the
  executable's full path, or runs `jason` from Git Bash, where the wrapper beside the shim passes them whole.
- **Open Jason at Login** is a Windows login item, a macOS LaunchAgent (`~/Library/LaunchAgents/ai.jasonharness.desktop.plist`)
  or a Linux autostart entry (`~/.config/autostart/ai.jasonharness.desktop.desktop`), and opens the window only.
  **Start the Runtime at Logon** is `jason runtime autostart` for the `jason` the application carries.

## On Linux

- The window keeps its menu bar, which carries the tray's items, since a session may show no tray icon. Closing the window
  quits the application; the runtime keeps running. A start at sign-in opens the window minimized.
- Open Jason at Login reads as on only while its autostart entry's `Exec` line is there and the entry is neither hidden
  nor marked `X-GNOME-Autostart-enabled=false`.
- An AppImage update that installs under a new file name moves an entry that is on to it, and re-points a logon
  registration or a PATH wrapper that was set up from another version's copy under `bundle/`. Another version's copy goes
  only after that, and never the one an update kept to go back to, one something still names or one the running runtime
  was started from.
- Built so, and not yet seen on a real machine: the tray on GNOME and KDE, the keyring backends, a start at login across
  a real sign-out, the AppImage's mount, and a deb update's password prompt.

## Updating

The desktop's update item reads the latest release's `latest.yml`, `latest-mac.yml` or `latest-linux.yml` through
electron-updater, straight from GitHub, on stable releases only: a release with a pre-release part is never offered, and
a candidate is installed by hand. It checks on a back-off, offers what it found, downloads only on your word and installs
only on your word again. The download is kept in the updater's cache under your profile, outside `<data>`:
`%LOCALAPPDATA%\jason-desktop-updater` on Windows, `~/Library/Caches/jason-desktop-updater` on macOS and
`~/.cache/jason-desktop-updater` on Linux (under `$XDG_CACHE_HOME` when that is set). A build that is not installed says "Updates Come to an Installed Build", and where
electron-updater cannot replace what runs the item says "Updates Come Through the Package". On Windows the installer is held to the signer
the package names.

Updates come to the Windows installer, the macOS application, the AppImage and the deb; anywhere else the item says
updates come through the package. Built so, and not yet seen on a real machine: a deb update installs the new deb with
`dpkg`, through the first of `gksudo`, `kdesudo`, `pkexec` and `beesu` the system has and otherwise `sudo`, and asks for an
administrator's password every time, so an account that cannot give one cannot install it and Jason stays open and says
so; an AppImage update replaces the file where it is, so its folder must be writable.

**The runtime restarts.** When the runtime runs from the application's own `jason`, the update item says so, "Restart
Jason and Its Runtime to Install": the desktop stops that runtime with `jason runtime stop` before the installer replaces
the file, and the new version starts it again when it opens. Work that is running keeps its lease, as at any stop. A
runtime that does not stop, or a run file that cannot be read, leaves the update uninstalled and says why; a runtime
started from another `jason` is never stopped.

**Going back is by hand.** Before the installer runs, the application as it was is copied under
`<data>/desktop/rollback/<version>/`, and the Jason menu shows an unclickable line, "The previous version `<v>` is kept at
`<folder>`; put it back by hand", until the next update replaces the copy. Nothing puts it back by itself, because the
runtime has no guard against a data directory a newer version migrated: if the new version does not start, the
starting page says what went wrong and where the kept version is, and going back is quitting Jason and putting that
folder back in the place of the installed application. That puts back the previous application and the Jason it carried;
the data directory stays as the newer version left it. On Linux an AppImage update keeps the AppImage file itself; a deb
update keeps nothing, and the page names the previous release's deb and `sudo apt install ./jason-desktop-linux-x64.deb`.
On Windows the system's list of installed programs keeps showing the newer version's number after a folder is put back.

**Not covered.** An update of the desktop from one build to the next through the updater is checked by nothing: a
candidate is installed by hand and the stable channel offers it nothing. The first run of the macOS login agent and of the
hardened runtime is at a candidate, and the Linux behaviour above that only a real machine shows is built so and not yet
seen. The x64 Mac executable is built and signed and not covered by the suite.

## Taking it off

Take off what Jason placed first, then the application. What stays in every case is your data: the data directory
(`~/.jason`, or the one `JASON_DATA_DIR` names), the harness root (`~/Jason`, or the one `Harness:Root` names) and the
updater's cache named above.

- **Windows.** Remove Jason from Installed apps in Settings. The uninstaller stops the runtime the application carries and
  removes nothing when it does not stop, then takes off the PATH command's files that are its own, Open Jason at Login,
  the `jason-desktop://` link registration when it names this application, and the runtime's logon start when that start
  names this application's `jason`, and names what it leaves.
- **macOS and Linux.** Choose **Remove What Jason Placed…** in the Jason menu, or the tray's. Once you confirm, it takes the
  Windows uninstall's steps: it stops the runtime first, going no further when it does not stop, turns off Open Jason at
  Login and the runtime's start at logon when that start names this application's `jason`, removes the PATH command's
  files that are its own and, on an AppImage, removes the copies under `<data>/desktop/bundle/` once nothing runs from
  them. It says what came of each step, what to do by hand for one that failed, and offers to quit Jason. Then remove
  the application: move Jason.app to the Trash, or `sudo apt remove jason-desktop` for the deb, or delete the AppImage file.
- **On macOS and Linux, when Jason will not start**, take off by hand what it placed. Its `jason` is `Contents/Resources/bundle/jason` inside
  Jason.app on macOS, `/opt/Jason/resources/bundle/jason` for the deb and each `<data>/desktop/bundle/<version>/jason`
  for an AppImage.
  1. Read `installed_image` in `<data>/run/runtime.json`. Only when it names that `jason` is the runtime this
     application's: stop it with `<that jason> runtime stop`. A runtime of any other `jason` is another installation's
     and stays.
  2. Run `<that jason> runtime autostart status`. When the `executable` it names is that `jason`, run
     `<that jason> runtime autostart disable`.
  3. Delete Open Jason at Login: `~/Library/LaunchAgents/ai.jasonharness.desktop.plist` on macOS, or on Linux
     `~/.config/autostart/ai.jasonharness.desktop.desktop` (under `$XDG_CONFIG_HOME` when that is set) when its `Exec`
     line starts the AppImage file or the deb's executable.
  4. Delete the `jason` (or `jason-desktop`) wrapper in `~/.local/bin` whose `exec` line runs that `jason`.
  5. For an AppImage, once nothing runs from them, delete the folders named as versions under `<data>/desktop/bundle/`.

## What this release leaves out

Managing Reply accounts beyond connecting and reconnecting one, Codex acting in the chat (it reads and advises), your own connectors and MCP
servers in the chat, a marketplace and plugins in frames, and a Linux build for arm64 are not here. Everything in this
page that a real machine shows and only a real machine shows is marked as built so.
