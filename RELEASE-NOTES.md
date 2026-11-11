<!--
What one release says of itself. The release workflow puts everything below its title into the release's body,
after the template's opening paragraph, with {{version}} filled in as the template's is; this comment is dropped,
so it is the only comment the file holds.

The shape: the title is `# Jason <version>`, then only these sections, each at most once:
`## What is new since <version>`, `## Fixed since <version>`, `## Upgrading from <version>` and `## Known issues`.
What is below this comment is the body of a public release, read on the release page and in the mail that announces
it: a link in it is an address into the public repository or the product's own, never a relative one.

After a release this file still names it: the version pull request leaves it as it is, and the next release rewrites
it. That is safe because a tag is checked before anything is built: a tag whose file is missing, untitled or names
another version stops the release there, so notes written for one release can never ship with another. The
button's run carries the template alone in that case, and says so. A candidate's tag needs the file to name the
candidate: v1.0.0-rc.1 reads `# Jason 1.0.0-rc.1`.

While a release's notes are not yet written, the file names its version with `-unwritten`, as
`# Jason <version>-unwritten`, which no tag carries, so a placeholder can never ship; whoever writes the notes
replaces the title and the body together.
-->

# Jason 0.34.0

## What is new since 0.33.0

Jason has a desktop application, a window onto the installation the `jason` command line already uses, with a chat
that drives Claude Code or Codex on your own machine; the browser UI is rebuilt on React and gains a person panel and a
campaign made of tabs; the runtime streams its journal, and `jason journal watch` follows it; and a fresh installation
is told what a provider plugin still needs.

**The desktop**

Jason Desktop is one more client of the installation, as the browser UI is. It finds the data directory as the CLI does
(`JASON_DATA_DIR`, otherwise `~/.jason`), shows the UI the runtime serves at its loopback address, and changes the
installation only through `jason` verbs and through the settings file a person would edit. The runtime keeps no state
for it, and with no desktop installed nothing in the runtime differs.

- **Three packages, signed.** A release now publishes the desktop beside the executable: an installer for Windows x64,
  and a disk image and an update archive for each of Apple silicon and Intel Macs, with the two update feeds
  (`latest.yml`, `latest-mac.yml`) and the block maps. That is ten files more, so a release is twenty-four files.
  `checksums.txt` lists twenty of them: the three archives, the seven packages and the desktop's ten; it does not list
  `manifest.json`, itself or the two install scripts. `manifest.json` is unchanged, since it lists what `jason update`
  applies. On Windows the installer, its uninstaller and `Jason.exe` are signed, and so is the `jason.exe` the
  application carries, which is the release's own executable byte for byte. On macOS both packages are signed with a
  Developer ID, notarized and stapled. Each package carries one version, `LICENSE` and both notices.
- **One installation.** The desktop asks `jason status` whether a runtime is up and whether it is the same build, and
  starts one with `jason runtime start` only when none is, never as its own child. It deploys the role skills from the
  package it carries, and works in a chat folder of its own under `<data>/desktop/chat`. When the runtime is another
  version, its UI is loaded as it is and the window says the versions differ and names `jason update`.
- **A window that stays shut.** The page gets `window.jasonDesktop` and nothing else, answered only from the top frame
  of a page at the runtime's origin. Navigation is locked to that origin; a link that leaves it opens in the system
  browser, and only when it is `https:`. Electron's own profile, storage and logs live under `<data>/desktop/electron`.
- **Live chat.** A person talks to Claude Code or Codex, which run on their own machine, and the host works the
  installation through the same `jason` command a person types. The words go to the host on standard input, never on its
  command line. For Claude Code the desktop decides what runs without asking, from an allow list of one rule for each of
  66 verbs and a deny list of the verbs a chat never runs (everything the CLI reserves for a person, `export` and
  `import`, `suppression remove`, `route set`, `route unset`, `role set-profile`, `role add` and `project link`, since a
  folder a project links becomes a working folder of every chat on its campaigns), and asks the person on a card for
  everything else. Every host's turn runs `jason` with `JASON_CONFINED=1`: a file a request carries comes on standard
  input, `jason status --provider-cli` runs a bare name found on the `PATH` only, and a command that names the variable
  is refused. Of the Reply CLI the chat may run `reply auth status` and `reply account list` and nothing else. A card's
  "Allow for this chat" shows exactly the command it covers and is never offered for a program that runs another, for a
  command with a word the shell would rewrite, or for one that names `reply`. The data directory is refused in every
  spelling the desktop can read. Codex, in this release, reads and advises with a read-only sandbox and no network, and
  acts through nothing.
- **A chat that reads like one.** The agent's answers are drawn as a fixed subset of Markdown (paragraphs, emphasis,
  code, lists, headings, quotes and simple tables; a link is shown as its text and address, never as a link). A message
  sent while a turn runs waits under the box and goes when the turn ends well; it can be taken back or replaced. The
  agent runs nothing in the background, and its steps are described in plain words, such as "Ran a jason command".
  The desktop writes its five rules for the agent as `CLAUDE.md` and `AGENTS.md` in the chat folder before every turn,
  so an edit to either does not last.
- **Needs you, Campaigns, Activity.** The console's home is a chat with dockable panels. Needs you says who an approval
  reaches, from which account and on which channel, and narrows to approvals or to questions; Campaigns and Activity
  follow the journal as it is written.
- **Connect cards.** Slack, Telegram, ntfy and the journal forwarder are configured in one act, which edits only that
  plugin's keys in `<data>/config/settings.json`, keeps everything else in the file, and refuses a file it cannot parse.
  Reply is signed in with `reply auth login`, or with a key on standard input, then installed and wired as the default
  route only where you have set none. A card says "configured" when the runtime accepted it, never "connected".
- **Accounts and Disconnect.** The Reply card groups the accounts the Reply CLI lists by channel, Email first, then
  LinkedIn, then any other channel under its own name, and shows each account's state as Reply itself reports it:
  Connected, Disconnected or Unknown for email; Enabled, Disabled, Signed out or Daily limit reached for LinkedIn. A
  state the card does not know is shown as Reply wrote it, as plain text. The card does not manage accounts: limits,
  signatures, pauses and an account's connection stay in Reply, and a link, "Manage accounts in Reply", opens Reply's
  web app in your browser. The Slack, Telegram and ntfy cards gain Disconnect, which asks once and then edits
  `<data>/config/settings.json` once: it removes that plugin's configuration and its notification entry, keeps the
  entry's `Enabled: false` where you had set it off, removes the Http grant that was derived from a server of the
  plugin's own, and keeps every other key, the rest of the plugin's grants and the installed package. It then reloads
  plugins. A settings file the desktop cannot parse is refused and left as it is. The journal forwarder has no
  Disconnect, since removing its receiver while its subscriptions stay granted would fail every line it is handed;
  ungranting it is its own act.
- **The tray.** It counts what waits on you, approvals and questions, read through the CLI at most every 30 seconds. A
  system notification carries the counts alone, never a name or a question's text. When the runtime comes back on
  another port, the window pairs again by itself. The tray never starts a runtime.
- **Keys and logon.** The View menu has New Chat and the six screens (`CmdOrCtrl+N`, `CmdOrCtrl+1` to `6`). "Open Jason
  at login" is the desktop's own and off by default; "Start the runtime at logon" is `jason runtime autostart` and
  nothing else.
- **A `jason` command on the PATH,** placed only on your word and only in an installed build. On macOS it is a wrapper
  in `~/.local/bin`. On Windows it is `jason.cmd` and an extensionless wrapper for Git Bash in
  `%LOCALAPPDATA%\Microsoft\WindowsApps`, placed only when that folder is on your PATH. A `jason` that is not ours is
  never shadowed; the command is then named `jason-desktop`. `jason.cmd` passes its arguments through cmd.exe's parser a
  second time, so a script or an agent that passes JSON uses the executable's full path or Git Bash.
- **Updates on your word,** from this repository's stable releases only: a candidate's feed is never read. Installing
  one stops the runtime that the application's own `jason` runs, and a runtime of any other executable is never stopped.
  Before it replaces the application, the desktop copies the application as it was under
  `<data>/desktop/rollback/<version>/`, one copy at a time, named in the Jason menu. Nothing puts it back by itself;
  going back is quitting Jason and putting that folder in place of the installed application, which leaves your data as
  the newer version left it. See [how the desktop
  updates](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md).
- **The Windows installer and uninstaller close the application and nothing else,** by its exact path. A process running
  from the carried `jason.exe` is stopped through the application's own `--stop-runtime` start, which runs
  `jason runtime stop`, after your OK where you run the installer; one still found then ends the run before a file is
  replaced. Uninstalling takes off what the desktop placed, the PATH command, Open Jason at login and the runtime's
  logon start when it starts this application's `jason`, and names what it leaves: the data directory, the harness root
  and the updater's cache.
- **Its log** is `<data>/logs/desktop-YYYYMMDD.jsonl`, in the runtime log's shape and under its limits, and carries no
  token, path, command or word you typed. `jason logs --source desktop` reads it, and `jason doctor` carries a bounded
  tail of each log.

**The browser UI**

- **React 18.** The UI moves from Preact to React 18.3.1. Its screens, stores and API client behave as they did.
- **A person panel.** People opens a person beside the list, with tabs Details, Activity, Sequences, Messages (where a
  provider knows the person's threads), Next and Research. Details edits the record in place and can queue the change
  for the provider; it also puts the person on or off Jason's own do-not-contact register, address by address or as a
  whole, asking a reason for taking one off. Sequences excludes a person from a campaign or moves them to another one.
  Research lists what the researcher role found and can ask it for more.
- **A campaign is a set of tabs:** Sequence, People, Inbox, Results and Settings. Sequence is where a campaign opens and
  ends with what happened. Inbox shows the latest replies Jason observed, each at the moment the provider says the
  thread moved. Results shows four figures as the runtime counts them. Settings holds the record and the moves its
  status allows: Pause, Resume and Start. An address that named Details opens Settings, and any other tab that is gone
  opens Sequence.
- **Needs you and Attention** show the person an approval reaches, the account it goes from and its channel, and say
  plainly when nothing waits.
- **Settings and Ukrainian.** The browser gains a Settings screen for the theme and the language, English or Ukrainian,
  kept in the browser's own storage. A Chat screen says the chat is part of the desktop and links to the releases page.

**For a person who scripts the CLI**

- **`jason journal watch [--after <id>] [--human]`** follows the journal until interrupted, one line per frame in the
  shape `journal.list` answers, over a WebSocket at `GET /v1/journal.stream`, the one `GET` under `/v1/`. Without
  `--after` it starts with the next line committed; with it, after that id. A resume may send a line once more and never
  skips one, so drop a repeat by its `id`. Exit 0 on an interrupt, 1 when the runtime refuses the request, 3 when it
  cannot be reached, refuses the token or ends the stream. The run token or a paired browser's UI token opens it; a
  browser, which can set no header on a WebSocket, offers its token as a `jason.bearer.` subprotocol, which this route
  alone accepts.
- **An argument that names a response file is a usage error (exit 2).** The CLI no longer expands a response file, and a
  word that starts with an at sign is a word like any other. A script that relied on it passes the arguments itself.
- **Four verbs take no default actor.** `jason suppression remove`, `jason route set`, `jason route unset` and
  `jason role set-profile`, typed outside a run without `--actor human:<Name>`, exit 2 before any request, in one
  sentence that names that form. Inside a run the attempt is the actor, as before, and the runtime takes these
  operations from any caller, as before.
- **`JASON_CONFINED`, set to any value that is not empty, confines one call of the CLI.** A file option of an operation
  verb (`--file`, `--value-file`, `--note-file`, `--result-file`) takes only `-`, so the content comes on standard
  input, and any other value is refused with exit 1 before the file is opened and before any request.
  `jason status --provider-cli` runs a bare name found in the fully qualified entries of the `PATH` only; a value with a
  directory, a root or, on Windows, a drive in it is neither looked up nor started, and the `provider_cli` check says
  what it did not run. A runtime the CLI starts, the role work a runtime launches and the terminal `jason session open`
  opens do not carry the variable. Unset or empty, nothing changes.
- **`jason logs` gains `--source runtime|desktop`.** The default is `runtime`, so a script that reads the runtime's log
  is unchanged.
- **The runtime's descriptor gains `installed_image`,** the executable the runtime runs as, `null` or absent when that
  is unknown. A reader of the run file that rejects a field it does not know must be taught this one.
- **`jason plugin install` says what a plugin still needs.** After a reload that answered, its output carries the
  plugin's `status`, `problems` with their messages, `capabilities` as the reload answered them (requested and granted)
  and `settings_file`; `--human` prints each problem and, for each capability granted nothing, the key to set under
  `Plugins:Grants:<id>`, the file and the reload. `plugin list --human` and `plugin reload --human` print the same lines
  under their tables.
- **`jason status` has a new optional check, `plugin_grants`,** that names each plugin asking for a capability and
  granted nothing of it. It never decides `ready`.
- **`jason runtime autostart status` reads back `path`,** the `PATH` a macOS logon start carries, `null` on Windows and
  Linux. The path stays out of `jason status` and out of a `jason doctor` bundle.
- **A missing program says the repair.** An `executable_missing` problem now ends with "install the program where the
  runtime can see it and run `jason plugin reload`".

## Fixed since 0.33.0

- **A runtime started at logon on macOS finds what a terminal finds.** launchd starts an agent with
  `/usr/bin:/bin:/usr/sbin:/sbin`, so a plugin whose program lives elsewhere was `unavailable` after a restart. The
  LaunchAgent now carries the `PATH` of the terminal that registered it.
- **A plugin granted nothing no longer looks installed and idle.** A plugin that loaded `valid` with nothing granted, so
  that every call it made was refused, is now named by `jason plugin install`, `jason plugin list`, `jason plugin
  reload` and `jason status`, with the setting that grants it.
- **Where a global npm install is refused with `EACCES`,** the golden path, the Reply package's prerequisites and the
  operating skill say to install under a prefix of the account's own. The Reply package is 0.14.8.

## Upgrading from 0.33.0

Nothing stops: no setting is renamed, and no start refuses a setting 0.33.0 took.

- **The executable:** `jason update apply`, or run the install line again. Installed with Homebrew, it is the three
  lines `jason update apply` prints instead: `jason runtime stop`, `brew upgrade jason`, `jason runtime start`. After
  it, `jason skills update` and `jason ui install` follow, and `jason plugin install <id> --replace` for each package
  you installed and did not edit.
- **The desktop is a separate install.** `jason update` does not install it. Download the installer or the disk image
  for your machine from this release, or, where an earlier desktop is installed, let it offer the update. An Intel Mac
  gets its `jason` only with the desktop's own update.
- **The packages:** reply 0.14.8; journal-forwarder 1.2.0, ntfy 1.0.3, slack 1.0.0 and telegram 1.0.0 are as in 0.33.0.
  A release is twenty-four files: the desktop's ten are new.
- **Skills:** the update redeploys them wherever a deployment was recorded. `operating-the-installation` changed its
  text (how a fresh installation connects a provider) and keeps `draft`, and no agent host has read it against 0.34.0.
- **The data directory:** this release adds no database migration: your projects, campaigns, work items and chronicle
  stay as they were. The desktop keeps what it writes under `<data>/desktop/`, and `jason uninstall --purge-data`
  removes that folder with the rest.
- **Going back:** what `jason update apply --version` to an older release does to a newer plugin package, and how to
  load one again, is in [Applying an older
  release](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#applying-an-older-release).
- **From 0.32.x or earlier,** the notes of each release in between apply as well. From 0.18.x or earlier, Jason's own
  installation updates through 0.19.0: `jason update apply --version 0.19.0`, then `jason update apply`; from a build
  older than 0.18.0, the first of the two can stop at `started`, as the known issue about that update below says.

## Known issues

A desktop build made before this release does not know the `--stop-runtime` start, so a later installer would start that
whole application and wait on it. Remove such a build with its own uninstaller first. No published release carries one.

The Mac executable built for Intel is signed and notarized like the Apple silicon one, but the test suite does not run
on an Intel Mac, so that package is covered by what was built and signed and not by a run. Its `jason` moves only with
the desktop's own update, because `jason update` publishes no build for it.

There is no desktop package for Linux: the desktop is published for Windows x64 and for Macs only.

The Windows installer ends the run, and installs nothing, when a process from the carried `jason.exe` still runs after
the stop; the next start of the application says nothing of it. Where PowerShell is not available the installer looks
for no process: a person is asked to stop the runtime, and an update goes on without a look.

The six libraries the Windows installer unpacks into its temporary folder (nsExec, nsis7z, SpiderBanner, StdUtils,
System and WinShell) carry no signature of ours. The installer's builder cannot sign them, and they are never installed.

The desktop's chat has been run in tests against stand-ins for Claude Code and Codex; no turn with a real host has been
watched through the desktop yet.

Codex in the desktop's chat reads and advises and acts through nothing: its sandbox is read-only, with no network.
Claude Code's chat asks for a card before it reads a path outside its working folders only from 2.1.257 on; an older
version skips that setting without a word.

Work the desktop's chat queues for a role runs under that role's own execution profile, not behind the chat's cards.

The desktop's tray has not been seen on a screen, and the View menu's keys have not been typed at the operating system's
level; the page receives them, but no run has shown the menu's accelerators fire. A person's run of the installed
candidate covers both.

An import on the same machine, into a second data directory, "needs the source harness root moved aside first: the
records name its folders, and project.link refuses a folder present elsewhere (harness_folder_present)". The import
names each refused link, and its `next` carries `jason project link <id> --path <folder>` for each.

`jason status` calls autostart `ok` from the registration alone: a registration whose executable is not there any more
reads as registered. `jason runtime autostart status` reads the executable it names, and says "The executable it runs
is not there any more" with its path.

On Windows, a campaign's move to another project can be refused for the instant the background import-file sweep writes
inside that folder, soon after a start; ask again and it succeeds.

`reply skills install --project` does not see an old `reply-adapter` plugin from the `reply-skills` marketplace
installed at user scope on
Claude Code, so that plugin keeps loading beside the project copy of the skills.

`reply skills install --dry-run` and `reply skills update --dry-run` describe what they would do in the past tense, as
if it had been done; nothing is changed.

The click on a notification and its Copy prompt button are Windows only. Where another program already holds `jason:` links,
Jason leaves that registration alone and the toast shows without a click or a button.

On Windows, `jason session open` starts the console host, which hands the window to Windows Terminal where that is
your default terminal; where your default terminal is the console host itself, you get a plain console window; neither
is yet measured on a machine.

The macOS form of `jason session open`, a script opened in Terminal, has not been watched on a Mac.

Antigravity CLI's hook route has not been run on an account. The host also reads your own global `AGENTS.md` and
`GEMINI.md` under `~/.gemini` for every role.

Two installations on one account, a build of a checkout and the installed one, take the `jason:` link from each other at
each toast, since the registration rewrites a key that is Jason's and names another executable. Uninstall leaves a key
that names another installation alone, so nothing is lost.

On Linux, kitty, foot, gnome-terminal, xfce4-terminal, mate-terminal and wezterm are started in the form each takes;
any other terminal program is given the host after `-e` as separate arguments.

Under Windows PowerShell 5.1 and cmd.exe the inner quotes of `--value '{"host":"codex"}'` are lost, so give the value in a file:
`jason preference set agent.interactive_host --value-file host.json --actor human:<your-name>`.

If Claude Code's npm postinstall never replaced its placeholder executable, `jason session open` reports that it started the host
and the window closes at once.

Codex CLI on Windows needs its elevated sandbox set up once by an administrator before a role can run a command; until
then every command a role runs is refused, so the role cannot report, and the attempt ends as `executor_exited` and is retried against the same refusal; Jason does not check for the
setup when the attempt starts.

Whether `jason` reaches the runtime from inside Codex's sandbox has not been checked on Linux, macOS or Windows, and on
Windows it is not yet checked whether the elevated sandbox reads only the list Jason names. Codex also reads its own global
`AGENTS.md` and offers every role the skills in `~/.agents/skills`, whatever the profile says, so those two reach every
Codex role. Holding Codex's network to loopback rests on its network proxy, an experimental feature in 0.166.0.

Whether Cursor CLI runs the allow-listed `jason` command without asking, in print mode, has not been seen on a signed-in
run. If it does not, a role cannot call the runtime back, so it cannot report, and the attempt ends as `executor_exited` and is retried against the same refusal. Cursor CLI also reads more than the
memory files Jason names, since it takes the union of every configuration layer it finds, yours included: your own allow
rules of any kind, a shell command or a write among them, stand beside Jason's. Each attempt leaves one trust marker under
`~/.cursor` that Jason does not remove.

Gemini CLI reads the whole folder of each level a role's memory files are in, and the client level's folder is the harness
root, so a role reads every memory file under it. A role on Gemini CLI is also refused when the host's system policy folder
holds a `.toml` file, since the host would then ignore the policy Jason gives it.
Gemini CLI's `--session-id` and `--admin-policy`, which Jason passes, are in the 0.66.0 source and not in its options table.
Gemini CLI also reads your own `~/.gemini/GEMINI.md`, `includeDirectories` and skills for every role.

GitHub Copilot CLI's reads outside the work directory, which a role needs for its memory files, are documented but have not
been seen to reach there.
Whether Copilot CLI 1.0.105 accepts every flag Jason composes is not said by its changelog, so its launch is composed from the
reference and not from a run.
A role on Copilot CLI runs under your own user-level hooks, and a `COPILOT_ALLOW_ALL` in your environment reaches it as the
host's own allow-all, minus what the deny list names.

A proxy flag or a proxy variable is yours to set: Jason sets none of them and reserves none of them. A flag that names a
host's own server, its credentials or its certificate check is reserved where the host has one, as the Cursor CLI section
says, and a host whose arguments are an allowlist takes no flag outside it.

On a build older than 0.18.0, the update through 0.19.0 can stop at `started`, saying that a library failed to load.
That update is still performed by the older build, and it happens when the two builds carry different .NET patches.
Type `jason update apply` again: it runs 0.19.0, which finishes the update, and one more `jason update apply` then
takes the newest release. From 0.18.x this does not happen: 0.19.0 is built on the same .NET patch as 0.18.0.

Through Reply, a contact's later replies by email can be lost: a lost reply reaches none of `conversation.list`,
`conversation.get` and the change feed, and Jason's inbound sync says nothing of it. A direct email's replies are all
detected, any number, a reply after an answer sent from Reply's inbox included. A second reply to a sequence email is
not detected: Reply takes the first reply to each email it sent. After an answer sent from Reply's inbox on a sequence
thread, a reply is detected only when its own headers reference that answer, and a reply to the original sequence email
is dropped. No auto-reply or out-of-office is stored on a direct thread; a reply whose `In-Reply-To` and `References`
are both empty is dropped; and a reply to a send older than 40 days is not matched unless a newer send to that address
exists. A LinkedIn thread keeps later messages, so they are read. That a direct email's replies are all detected was
observed on an account; the rest, the LinkedIn statement among them, is read from how Reply is built and has not been
observed.

A send or an invitation through Reply carries no sending time in its own result: `sent_at` is null, because Reply's
answer to a LinkedIn send gives a local time marked as UTC and its answer to an email gives none. `message.status`
reports when a send went, and a message's own moment is read from its thread, where it is true UTC.

The approval preview shows a send's `send_after` and not the provider object beside the arguments, so a send that also
names a sending schedule goes at the schedule's first opening at or after that moment, which may be later than the
moment the approver read.

A template held for a later moment, or queued by Reply behind the account's other sends, is filled in when it goes: a
name Reply cannot fill then fails the send after a person approved it, which `message.status` reads as `failed` with
Reply's reason.

Reply converts a LinkedIn text from HTML to text after filling it in, so markup in a LinkedIn message or InMail, a
final text or a template, does not reach the person as written; what Reply makes of a `<` that starts a tag there is
not known yet, and the Reply package does not refuse one on LinkedIn as it does on email.

Reply does not consult its blacklist when it takes a LinkedIn direct send, and does when it makes it: a send to a
person its blacklist holds is accepted, and fails when Reply makes it, which `message.status` reads as `failed` with
Reply's reason. Jason's own do-not-contact register is checked at the claim as before; record such a stop there with
`jason suppression add` as well, so the send is refused before it is made.

Through Reply, `schedule.list` does not say which campaign runs on which schedule: `campaign.get` names no schedule,
so which one a campaign sends on is read at Reply.

Through Reply, a reply on a thread, `conversation.reply`, is not held to the rule on a `<` that starts a tag yet, and
its text goes as written; whether Reply cuts a reply as it cuts a direct email has not been seen, so write an address
in a reply without its angle brackets.

The responder's skill ships marked `draft`: no agent host has read its text in the state it ships in. Its commands parse
against this build, as every skill's do, but how an agent follows its loop on a real reply is not yet seen.
