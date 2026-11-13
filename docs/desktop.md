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
| macOS on Intel | `jason-desktop-macos-x64.dmg` | The same, with an Intel `jason`. It is built and signed like the Apple silicon one, and the test suite does not run on an Intel Mac. A `jason` carried by the Intel application moves only with the desktop's own update, so `jason update apply` there answers `update_carried_by_desktop`. |
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
package it carries, once for each desktop version, deploys the Reply skills when you sign in to Reply on the Connect card, and again at a start that finds them unrecorded while you are signed in, and loads the link `jason ui open --no-open` prints. When the runtime is another version, the UI it
serves is loaded as it is and the window says the versions differ: it names the desktop's own update where that runtime
is the one the application carries, and `jason update` otherwise.

The desktop starts every program with a file and arguments, never through a shell, and only from the modules that are
named for it; a test holds that, and holds the page to a window that reaches the desktop through one object,
`window.jasonDesktop`, answered only for the top frame of the runtime's own page. Navigation is locked to that origin; a
link that leaves it opens in the system browser, and only when it is `https:`.

## What you see

The window calls a campaign with its chat an **agent** (**Agents** in the sidebar and the View menu). That is the harness's
campaign and nothing else: the commands, the API and the ids keep `campaign`, and so do the rest of these pages. The AI that
runs commands is called the AI.

The desktop wears the console: the chat is home, in a dock with panels beside it. The window opens on **Start**, which
asks "Who do you want to reach?" and says under it that this interface is optional, since everything there can also be done
from a terminal with your AI, through Jason's CLI and API. What you type there starts a set-up chat on the AI you connected, about no campaign
yet; no chat is made until you send. Enter sends and Shift+Enter makes a new line. If no AI is connected yet, the AI card
shows before anything is typed and what you send waits, while the window is open and only then, until one is connected;
**Take back** returns the words. Start opens every time the window opens, also when campaigns exist, and the sidebar's
**New chat** still makes a chat directly.

- **Needs you** lists approvals and questions, says for an approval who it reaches, from which account and on which
  channel, and narrows to All, Approvals or Questions. With nothing waiting it says you are caught up and offers
  **Open your agents**.
- **Agents** and **Activity** follow the journal as it is written. A campaign's Start, Pause and Resume are in its
  header, beside Add people, so they are there from every tab.
- The **Feed**, the sidebar's first item, is the desktop's main chat (below).
- **Connect** shows the agent hosts found on the machine, the model keys, the Reply connection and the notification
  channels (below).
- The browser's other screens, People, Performance, Work items, System, Accounts and Settings, open in a page panel of the same
  dock. [ui.md](ui.md) describes each.

**Appearance** (Settings) offers Light, Dark and the system's, in that order. A new installation meets Light; one that
existed before keeps what it had, its chosen theme or the system's if it never chose. In the desktop the choice is kept in
its settings and the starting page and the window's background before the page loads follow it.

The View menu carries the keys (New Chat, What Needs You, Performance, Agents, Work Items, People, System, Reload).
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
- `<data>/desktop/turns/`: one small configuration file per running chat turn, readable by your account alone where the
  system has modes, naming the desktop's permission endpoint and carrying that turn's secret. It is removed when the turn ends, and any a crash left
  are removed at the next start.
- `<data>/desktop/models.json`: the models Codex offered when it was last asked.
- `<data>/desktop/full-access.json`: the standing choice of Full access (below), kept for each host program that can take
  it and removed when you take it back. No chat's command or edit can change it, and a chat on Claude Code cannot read
  it, in any mode; where Codex's sandbox reads Jason's data folder a Codex command can read it, and it holds no secret. A
  program the AI writes and that you let run, in any mode, can change it, as it can anything you can, and the
  confirmation says so. Beside it, `full-access-taken-back` marks a take-back the file would not take: Jason holds the
  choice as taken back while it is there and removes the file when it next starts. Where the folder takes no new file
  when Jason starts, so that a take-back could not be marked, the choice is held as taken back until it next starts.
- `<data>/desktop/skills-records/`: the record of what the desktop placed in the chat folder's skills folders, kept outside
  the chat folder, which the chat can neither read nor write.
- `<data>/desktop/rollback/<version>/`: the application as it was before the last update (below).
- `<data>/desktop/bundle/<version>/`: on Linux, an AppImage's copy of the `jason` it carries. An AppImage runs from a mount
  that lasts only while it runs, so each start copies its `jason` here; that copy is the `jason` it runs, starts the
  runtime from, puts on the PATH and gives the chat. A copy is used only when every file matches the package's, and one
  that differs, or a link where the folder should be, stops the start and is named, to be removed by hand.
- `<data>/desktop/electron/`: Electron's own profile and its logs, so a second data directory is a second desktop and
  nothing of it lands outside `<data>`. The window's page keeps its storage in memory only, so its pairing with the
  runtime, a key with the run token's rights, never reaches this folder, whatever build of the page the runtime serves.
  An earlier version kept it here: at the first start after the update the desktop revokes every UI token before the
  window loads, then removes what that page stored, so a browser you paired with `jason ui open` must be paired again
  once. If the revoke fails, nothing is removed and the next start tries again. A start whose runtime does not start
  does none of this, and neither does a reload of the window later in that run: what the earlier page kept stays here,
  its tokens still valid, until a start that starts the runtime.
- `<data>/desktop/window-pairing.json`: whether that clearing is done, and the id of the UI token the window's page
  holds now, never the token. The page reports the id once it has paired, and the desktop revokes the token the window
  held before, so the paired list keeps one row for the window.
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

- **Agent hosts.** The cards detect the agent hosts (Claude Code, Codex, Gemini CLI, Antigravity CLI, Cursor CLI and GitHub Copilot CLI), `reply` and `node`. A missing program is explained with its install
  command and a link, and the command runs on a click when `npm` is present, never without one; Node itself is never
  installed. The desktop never signs in to a host: sign in once by running the host in a terminal, and the chat uses that
  sign-in.
- **AI provider keys.** Anthropic and OpenAI keys, and the Anthropic-compatible providers DeepSeek, Kimi, GLM and
  MiniMax, which run through Claude Code with a token and a base address (GLM's default models are `glm-5.3` and
  `glm-5.3-flash`, MiniMax's `MiniMax-M3.1-Flash`). A key is kept by the operating system's store, reaches the host's
  process environment and nothing else (`CODEX_API_KEY` for Codex), and is never written in the clear, to a file of Jason's or anywhere else: the file `provider-keys` holds it encrypted by the operating system. A turn on
  a key runs its host with a configuration folder of the desktop's own, holding no claude.ai login and no saved Codex
  login, and without the variables that name another credential, a profile or a federation: the design is that a key the
  host does not take fails the turn instead of running it on your own plan, as built and not yet seen with a real host.
  A refused key ends the turn at once with a sentence naming the provider's card in Connect (OpenAI's key, run through
  Codex, ends it with Codex's own words), and on a turn on a key the exact key is replaced by `[key]` in every line of text and every
  step the host says, and a host's error text also loses the key's first six and last four characters and any token
  shaped as a provider's key, before the page shows it or the conversation keeps it.
- **The model list.** A chat's model menu starts with "Default (recommended)", which passes no model, so the host chooses
  and a new chat starts there. Claude Code's entries follow with a name to read each ("Opus (latest)", "Sonnet (latest)"),
  while the alias is what the host is given and the version it stands for is the provider's to move. Codex's list is
  asked of Codex at most once a day; Claude Code documents no way to list its models without a turn, so its list is the
  desktop's own. A chat that chose a model before keeps it.
- **Reply.** Signed in with `reply auth login`, or with a key on standard input that only the Reply CLI keeps, then the
  plugin installed and, only where you have set no default route, wired as the default. A card says "configured" when the
  runtime accepted it, never "connected". What the desktop tells Reply about itself is one label in the User-Agent of
  `reply`'s requests and nothing else: `jason-harness/<the bundle's version>`.
  The card lists the Reply account's mailboxes and its LinkedIn accounts, each channel read on its own, so an account
  without LinkedIn still shows its email. A channel that account's plan or role does not have says so ("This Reply
  account has no LinkedIn."), and one that could not be read says that and offers **Check again**; the other channel's
  accounts stay in every case. The list is read again when the window takes the focus back, at most once in 30 seconds.
  Each act is a verb of the Reply CLI, and the card offers them only from the Reply CLI that has them; below it, it says
  to update the Reply CLI and offers nothing but **Update**, where npm is there to update it with. **Add account** offers
  Gmail, Outlook and LinkedIn (a channel the account does not have is not offered). A mailbox row offers **Limits** (its
  daily limit), **Unblock** (sending again after its provider stopped it) and **Delete**; a LinkedIn row **Pause** when
  it is enabled, **Resume** when it is disabled, neither at its daily limit, and **Delete**; and a row that is not in a
  good state **Reconnect**. A connect or a reconnect makes a link to Reply's own page, which needs no Reply sign-in:
  the desktop opens it here, in this computer's browser, or, where you tick **Copy a new link to hand on**, puts it on
  the clipboard for you to send to whoever owns the mailbox or the LinkedIn account. They sign in to Google, Microsoft
  or LinkedIn there with their own credentials, so a password never passes through Jason. Only a link that expires and
  connects one account is copied. The desktop keeps the link nowhere, and the card says only that it was opened or
  copied and until when it connects an account. A limit's change, a pause, a resume, an unblock and a delete ask you
  first in a box of the desktop's own that names the account; with no window to ask in, or once the window is hidden or
  closed, it does not act. Where Reply does not yet make a link for one mailbox, the card offers the account's shared
  mailbox page instead, only on your click and after a box that warns that the address does not expire and lets whoever
  holds it manage all of the user's mailboxes; it is always opened here, never copied. Where sequences use a LinkedIn
  account, its delete is refused, and **Delete anyway** asks again in a box of its own. A signed-out LinkedIn account
  offers **Reconnect**, and no pause or resume until it is reconnected. An answer that leaves the account's state unknown
  reads the list again and shows what is; nothing is repeated by itself. The card lists the links made and not used yet
  under their channel, with Waiting or Expired and their expiry; such a link is not shown again, so adding the account
  again makes a new one. Finishing a connection happens on Reply's side, so the card learns of it by **Check now**,
  **Check again** or a focus. Other mailboxes (SMTP/IMAP), signatures and a LinkedIn account's limits are set in Reply,
  which the card links to.
  The same card appears inside the chat of a draft campaign while Reply is not connected. It is an offer, so it is hidden
  only where the runtime says the campaign's enrolment is routed to a plugin other than Reply (the window asks
  `route.resolve` for `campaign.enroll`, which applies the campaign's own route, its default, the global override and the
  global default). Where the route is Reply, where no route exists yet (signing in is what writes the first one) and
  where the route cannot be read, the card shows; nothing shows while it is being read, and a campaign that is already
  running shows none. **Not now** hides it for that chat until the window is closed.
  The Accounts page of the browser's screens shows the same card, drawn by the same code and acting through the same acts,
  beside the notification channels as the runtime reports them; see [the browser UI](ui.md#accounts).
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
  From 2.1.285 on a turn also passes `--no-chrome`, the first version whose changelog names the flag; below it the
  turn's settings alone keep the browser out.
- **Codex's floor is 0.122.0**, read before each turn on it: a Codex turn leaves your `config.toml` unread
  (`--ignore-user-config`, which Codex has from that version on). Below it, or with a version that cannot be read, the
  turn is refused before Codex starts, with a sentence that says to update Codex. **The chat does not read your Codex
  config.toml, so a provider or profile set there is not used; connect a key on the Connect card.** Nor are the MCP
  servers it names loaded, since Codex asks no one before such a tool runs, nor your ChatGPT apps (connectors), which
  every Codex turn turns off; its sign-in is still used.
- **On Windows, Codex needs its elevated sandbox.** Every Windows Codex turn names it (`windows.sandbox="elevated"`),
  since a command in Codex's other Windows sandbox can read the runtime's run file, and with it the token that lets a
  program act as you through Jason's API. Naming it does not prove it is set up, so at the first send to Codex in each
  run of the desktop the chat checks first, saying so: **Checking Codex's sandbox… Windows may ask once for an
  administrator; answering No means chatting on Claude Code instead.** That line is Windows' alone. A command tries to open two
  files of the data directory and says only whether it could, never what is in them. One is the runtime's run file,
  which has an access list of its own; the other is the desktop's settings file, which keeps the access list the rest
  of the data directory, the database and the logs among them, inherits. It also opens Windows' own hosts file, which
  every account may read, to show that it can open a file at all. The same command runs first outside any sandbox,
  where it must open all three, then in Codex's sandbox, where it must open the hosts file and not the run file, then
  outside again, where it must open all three once more. The verdict rests on the run file alone: the elevated sandbox
  opens the settings file, as a Codex command can read the rest of the data directory on macOS and Linux too, and
  whether it did is a fact the check reports and the desktop's log records (`chat_sandbox_checked`, a word for the
  answer and one for the settings file, never a path), not a reason to refuse. What the verdict stands for, credential
  by credential: the run token is checked; the window's own pairing is absent, kept in memory only; the AI providers'
  keys are sealed by the operating system for your account, which Codex's sandbox on Windows does not run as, since it
  runs as an account of its own; a plugin's secret kept in the installation's settings,
  the live Claude turns' endpoint secrets, which open no API, and Jason's data, conversations and transcripts are
  readable, and the line under the composer says so; a browser's own pairing and the agent hosts' own sign-ins are
  outside Jason's folder and not checked. The chat runs on Codex only on exactly
  that; otherwise the turn is refused with one sentence: **Codex on Windows needs its elevated sandbox, set up once by an administrator with
  `codex sandbox setup --elevated` (https://learn.chatgpt.com/docs/windows/windows-sandbox), or chat on Claude Code.**
  A turn on the OpenAI key is refused instead with **Codex on a key cannot be checked on Windows yet: sign in to Codex,
  or chat on Claude Code.** The answer is kept for that Codex until the desktop quits. With no runtime running there is
  nothing to open, and the turn is refused until there is; the desktop writes its settings file at each start when it
  is missing, and if it is missing all the same, the turn says to restart Jason. A data
  directory whose path holds one of `, ; = % ^ & | < > !` or a quote is not checked, and Codex does not run there. The
  check reads your Codex config.toml, which the turn does not, since Codex's sandbox command cannot leave it unread. It
  is not run at the Connect card, since naming the elevated sandbox may ask for an administrator.
- **Codex only reads and advises.** A Codex turn runs `exec --json --sandbox read-only` and asks for
  nothing, so no card and no rule of the desktop applies to it. It reads the chat folder, the folders added to it and its
  skills in `.agents/skills`, and answers. It searches the web through its cached index. Its sandbox may keep `jason` from the runtime's
  loopback address, and, being read-only, it can read any file you can read, with no card: wider than the chat folder.
  While Codex is the chat's host, on your sign-in or on the OpenAI key, the line under the composer says what a command
  can read, system by system as it was seen. On Windows, where the desktop's check has seen the elevated sandbox keep
  the run file from a command: **A Codex command can read your files, Jason's data and settings among them, a plugin's
  secret kept in settings.json too, but not the key Jason's own commands use, its run token. Codex runs it
  read-only.** On macOS and Linux, where nobody has looked, and on a desktop that does not say its system: **A Codex
  command can read your files, Jason's data and settings among them, a plugin's secret kept in settings.json too, and
  the key Jason's own commands use, its run token. Codex runs it read-only.** Neither says anything of the network or
  of acting for you, which nobody has seen. Keep a plugin's secret out of `settings.json`
  ([plugins.md](plugins.md#hostconfig)). A browser you paired keeps its token in its own profile, which a command of a
  Codex chat may read; revoke a browser you no longer use ([ui.md](ui.md#paired-browsers)).

### Conversations

Each campaign has one conversation, found or made the first time it is opened, and any number of set-up chats are about
no campaign yet. The window opens on Start, and a set-up chat is made when you send from it. A set-up chat whose turn made exactly one campaign becomes that
campaign's conversation; one that made more, or ran beside another turn, offers what it made to pick from. A
conversation is kept from its first turn, one never sent to leaves nothing, and **Start afresh** begins the agent's next
turn without its memory of the conversation and keeps what is shown. **Remove** deletes the conversation's record from
this computer after one question; the host keeps its own transcript.

A conversation is a record under `<data>/desktop/conversations/`, readable by your account alone where the system has
modes: the campaign, a title,
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
chat is in. A mode applies to the turns you start by sending, from the next one on. **Ask first** is the mode of every new
chat and the chat as the next section describes it. **Plan only** lets the agent read and plan and change nothing: it
passes only the allowed verbs that read, Claude Code starts in its plan mode with the exit from it removed, and every
other request is refused. The cards left in Plan only are a read outside the chat's folders and a web page, and every
refusal of the next section stays. A Codex turn runs in its read-only sandbox in every mode it can be in.

When a Plan only turn ends with an answer, the answer is shown as a plan card, as text, with **Approve and run** and
**Keep planning**. Approve and run does what choosing Ask first from the menu does, then sends "Go ahead with the plan in
your last answer." as your message; it never enters Auto-edit or Full access, and if the chat's mode changed in between,
from the menu or by a Full access box answered meanwhile, nothing is sent and the chat says so. The card is offered only
while the plan is the chat's last word and the chat is still in Plan only, so a plan approved, or followed by another
turn, is not offered again, after a reload or a restart either. It is offered only on the agent host that made the
plan: on another one, which never saw it, the card says to ask for the plan again. Keep planning leaves the chat in Plan
only and puts the cursor in the message box. On Codex the card says that the approved plan still only reads and that Claude Code carries
it out. The Feed has no plan card.

**Auto-edit** is Ask first, except that six file commands run without a card when every path they name is inside the
chat's own folder: `mkdir`, `touch`, `mv`, `cp`, `rm` and `rmdir`, read word by word by Jason, never by the host's own
accept-edits mode. Such a command runs only when every part of it is one of the six with only the options Jason lists
for it, with no pipe, redirection, pattern, variable, `~` or `..`, and every path strictly inside the chat folder once
links are followed and outside the names an agent loads. The path a copy or a move writes into a folder counts as a
path it names. A recursive or forced removal (`rm -r`, `rm -f`), a copy of a folder, a link that leads nowhere, a move
to `.github`, a file in the chat folder named like one of the six (which the shell might run in place of the system's),
and anything in a campaign's or a project's folder, in the data directory or elsewhere is Ask first's card, and
whatever Ask first refuses stays refused. Auto-edit runs on Claude Code only and is not offered for the Feed; it has no
confirmation and no Always, is never a default, and is gone when you close Jason. Leaving it for Ask first or Plan only
while a turn runs stops that turn; a change to Full access lets the turn finish in the mode it started in.

Three limits come with it. The chat folder is shared by the chats that are not in Full access, so a chat in Auto-edit
can remove a file another chat left there. A hard link already in the folder is not told by its path, so a command that
writes through it, a `cp` onto it say, reaches the file it shares wherever that file is. And the judgement is made
before the command runs, so a link put into the folder by something else between the two is the same limit Ask first's
Write has.

Plan only is kept with the chat: it holds after a restart of the desktop and after an update, and **Start afresh**
leaves it. Changing to Plan only stops the turn that is running, so the menu never says Plan only while a turn could
still ask to change something; changing back to Ask first lets a running turn finish in the mode it started in. The menu
also lists Full access. Full access is chosen only after a confirmation, with Cancel focused and Escape answering
Cancel, unless you gave the standing choice (below). In Full access the AI runs commands and changes files on this computer without
asking, except a command Jason cannot read plainly or one that changes what the AI loads: that is a card titled "Full
access asks about this one", which waits thirty minutes and then is not run, and the turn goes on. A held card offers no
"Always", and an earlier "Always" grant from Ask first never lets a held command through: each hold is yours to answer
every time. A deletion on a critical path and a tool Jason never saw are cards too. Whatever Ask first refuses is
refused in Full access, with no card, and every `reply` command is among them but its exact no-card forms. PowerShell is
not a tool of the chat. Jason does not read a program the AI writes to a file and runs, nor your own shell
configuration (aliases, shell options), so such a program can still act as you. None of your own MCP servers or
connectors reaches a turn. The AI works in a folder of its own; when Full access ends, the chat says where it is,
with an Open folder button, and Jason removes it after seven days. The host keeps a session by the folder it began in,
so the first turn after a change into or out of Full access starts a fresh session, with a recap of the last words.
Added campaign or project folders whose `.claude` holds skills, subagents or commands are left out of a turn, said once.
Out of the box Full access is never a default; given this time, it is gone when you close Jason. It is not offered for
the Feed, whose turns are handed lines that others and providers wrote without you asking for them.

The confirmation is answered in one of two ways. **Give full access this time** holds for that chat until you change
its mode or close Jason. **Always on this computer** is the standing choice: every Claude Code chat on this computer
then enters Full access from its menu without the confirmation, after a restart too. With **With Always, also start new
chats in Full access** ticked, a new chat whose host can take Full access starts in it, its line under the composer
shown from the first moment; a new chat on a host that cannot, Codex or a Claude Code too old for it, starts in Ask
first with no notice, and the menu shows the mode it has. With This time that box is ignored, and its label says so. A
chat reopened after a restart is in Ask first as before: the standing choice skips the confirmation, it never enters
Full access by itself. You take it back with **Full access: ask each time again** in the mode menu, or **Ask each time
again** on the System screen's Full access card, which only the desktop shows; a chat that is in Full access stays in
it, its line still shown, and the next entry asks again. The page only reads the standing choice and takes it back;
only the confirmation gives it. A tool that ran without Jason having seen it takes the standing choice back as well as
ending the turn. A release that widens what Full access reaches asks again; a change of wording alone does not. The
Feed is refused whatever stands, and a held card still offers no "Always".

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
  agent to the chat's own instruction files, to the `.claude`, `.agents` and `.codex` folders of the chat folder and to
  its `.mcp.json`.
- **A card asks you.** Every other command, `jason doctor` and the `jason` commands no allowed verb begins included; a read outside the chat
  folder and the added folders; a write outside them, or in a campaign's or a project's folder to a file the host loads
  (`.claude`, `.git`, `.jason`, an agent's settings or instruction file, a file named `jason`); a write of a file Claude
  Code reads its MCP servers from (`.mcp.json`, `.claude.json`) wherever it is, and a `claude mcp` or `codex mcp` command,
  each time, never "Allow for this chat"; a web page, once per site per chat, the documentation pages below aside. A
  path that begins with `~` or is spelled in a way the system would read differently is a card too.
- **A tool of an MCP server of yours is a card each time, if one ever reaches a turn.** None does in this version (see
  What is not here); the fence stands anyway. Its card says "Use one of your own tools", the server and the tool, and the
  input exactly as the host would send it, never cut: an input too long to show whole is refused, and so is one any of
  whose texts would be refused as a command (a verb of the deny list, `JASON_CONFINED`, the data directory). Full access
  does not run it. "Allow for this chat" is never offered for it.
- **The documentation opens with no card, and a page is information.** A fetch from these pages needs no card: the
  `/docs/` pages of `code.claude.com` and of `learn.chatgpt.com`, the `/codex/`, `/api/docs/` and `/api/reference/`
  pages of `developers.openai.com`, and all of `docs.reply.io`. A page counts only when its address is `https`, at most 300
  characters, with no query, no port, no dot segment, no escape and no path segment that begins with an underscore; any other page of those hosts, or any other site, gets the card
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
- **The person's own words and the installation's changes are refused.** The same `JASON_CONFINED=1` makes `jason` refuse, locally
  before any request, exit 1 and one sentence, every command of the deny list's command half: approving, rejecting,
  answering and the rest of a person's own word, and the commands that change the installation. Inside the desktop's
  chat a decision is given in the page, or in a terminal, where the variable is not set. No attempt id in the
  environment lifts the refusal: a role's own work never carries the variable, because the runtime drops it from what it
  launches. A higher bar, not a boundary: the runtime cannot tell a person from a program that holds the token, and an
  attempt id in the environment is a claim, not a proof. The deny list's command lines are written from the CLI's one
  table of these commands.
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
`campaign/<campaign id>`, `approval/<approval id>`, `question/<decision id>` and `workitem/<work item id>`,
`person/<contact id>`, and `attention`, `attention/approvals/<approval id>` and `attention/questions/<decision id>`.
An approval or a question opens Needs you on that item, and deciding stays your click; `attention` opens Needs you. An
address with a query, a fragment, a percent escape, a dot segment, a port or a second id opens nothing. The scheme is
the desktop's own on every system; `jason://`, which the runtime's Windows notifications use, is the runtime's and the
desktop never registers it.

On Windows a click on one of Jason's notifications opens the desktop where it is installed for your account. The
command line handling the `jason://attention` link reads the desktop's registration under your account, never writes
it, and when the program it names is there hands the matching `jason-desktop://attention` address to Windows, which
starts the desktop; the address is made from the link's parsed parts, never from its text. With no desktop registered,
its program gone, or the hand-on failing, the browser UI opens as before, and the failure is said by its code
([notifications.md](notifications.md)). On macOS and Linux a notification's click opens nothing.

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

## What is not here

Managing Reply accounts beyond connecting and reconnecting one, Codex acting in the chat (it reads and advises), your own tools in the
chat, a marketplace and plugins in frames, and a Linux build for arm64 are not here. Nothing of your own tools reaches a
turn: a Claude Code turn loads none of your MCP servers, claude.ai connectors or browser, and a Codex turn does not read
your `config.toml`, so the MCP servers it names are not loaded either. Everything in this
page that a real machine shows and only a real machine shows is marked as built so.
