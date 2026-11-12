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

# Jason 0.36.0-unwritten

## What is new since 0.35.0

The desktop's chat is fenced more tightly: it loads none of your own MCP servers or connectors, it runs Codex on Windows only where Codex's sandbox keeps Jason's run token out of reach, and the CLI declines, inside a chat turn, the commands that give your word or change the installation.

- **The chat loads none of your own MCP servers or connectors,** on Claude Code and on Codex. Every MCP tool call the chat does make goes through its permission model. What you give up: the chat no longer reads your Codex `config.toml`, so a provider or a profile set there is not used; connect a key on the Connect card instead.
- **What fences a command of a Codex chat, system by system.** On Windows, a Codex chat needs Codex's elevated sandbox, which keeps Jason's run token out of its reach; that was seen on a real machine, by the desktop's own check, with Codex 0.167.0-alpha.4.1. The desktop checks it itself, with no model, at the first Codex message of a run; Windows may ask for an administrator then, and answering No means chatting on Claude Code instead. Where the check fails, the chat says so and names `codex sandbox setup --elevated`, or Claude Code. On macOS and Linux, Codex's read-only mode has no network by the vendor's own description, while the run file is readable there; we have not probed that yet. On every system a Codex command can read the rest of the data directory, and the chat says so where you choose Codex.
- **The CLI refuses a person's word and changes to the installation inside a chat turn.** Every desktop chat turn carries `JASON_CONFINED`, and under it the CLI refuses, before any request, the commands that give your word or change this installation, with one sentence and exit 1. This is a higher bar, not a boundary: a program that holds the run token can still act as you.
- **A plugin's secret stays out of an AI role's agent.** A secret given through the runtime's environment, by either of the two ways the plugin guide offers, no longer reaches the agent a role starts.

## Fixed since 0.35.0

- **Appending to a file several plugins write at once** no longer fails one of them while the others keep writing: the wait for the file counts as stuck only while nobody makes progress, and a holder that writes nothing still fails the append after ten seconds.

## Upgrading from 0.35.0

Nothing stops: no setting is renamed, and no start refuses a setting 0.35.0 took. This release adds no database migration.

- **The executable:** `jason update apply`, or run the install line again. After it, `jason skills update` and `jason ui install` follow, and `jason plugin install <id> --replace` for each package you installed and did not edit.
- **The desktop is a separate install,** as before: `jason update` does not install it. Download the installer or the disk image for your machine from this release, or let an installed desktop offer the update.
- **Codex on Windows needs its elevated sandbox for the chat.** Have an administrator run `codex sandbox setup --elevated` once, or chat on Claude Code; until then the chat declines a Codex message and says so.
- **The chat no longer uses the providers in Codex's `config.toml`.** Connect a key on the Connect card.
- **Claude Code older than 2.1.257 runs no chat turn;** update Claude Code first.
- **Codex older than 0.122.0 runs no chat turn;** the chat says to update Codex.
- **Going back:** what `jason update apply --version` to an older release does to a newer plugin package, and how to load one again, is in [Applying an older
  release](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#applying-an-older-release).
- **From 0.34.x or earlier,** the notes of each release in between apply as well.

## Known issues

Codex in the desktop's chat reads and advises and acts through nothing: its sandbox is read-only.

In a Codex chat a command can read what you can, a plugin's secret written in settings.json included; chat on Claude Code where that matters.

The Windows check of Codex's sandbox has been proven against a stand-in only where the elevated setup is missing.

On a Mac and on Linux, a read-only Codex turn can read the runtime's run file, so if the loopback interface is reachable from it, a command in a Codex chat could reach Jason's runtime. That has not been probed.

A long-running runtime can be ended by a rare fault in the SQLite provider's connection pool ([dotnet/efcore#38854](https://github.com/dotnet/efcore/issues/38854)); nothing recorded is lost, starting it again continues the work, and the runtime's log now says what ended it.

Not yet seen on a real machine:

- The Linux packages by hand on Ubuntu 24.04: the tray on GNOME and KDE, the keyring, start at login across a sign-out, a deb update's password prompt, the AppImage replacing itself, Remove What Jason Placed, and a `jason-desktop://` link clicked.
- The chat with real hosts and keys: a wrong key on each provider, which account pays when a plan is signed in, each effort level, and Plan only on a real Claude Code.
- Reply with a real account: the shared mailbox page, LinkedIn ids for Reconnect, and the confirmation box in a packaged build.
- On macOS, Remove What Jason Placed and the LaunchAgent; on every system, how the icon looks in the menu, the dock and the taskbar.

The AppImage refuses to start on a system that refuses its sandbox, with a sentence naming the deb; that refusal has not been seen either.

The Mac executable built for Intel is signed and notarized like the Apple silicon one, but the test suite does not run
on an Intel Mac, so that package is covered by what was built and signed and not by a run. Its `jason` moves only with
the desktop's own update, because `jason update` publishes no build for it.

The Windows installer ends the run, and installs nothing, when a process from the carried `jason.exe` still runs after
the stop; the next start of the application says nothing of it. Where PowerShell is not available the installer looks
for no process: a person is asked to stop the runtime, and an update goes on without a look.

The six libraries the Windows installer unpacks into its temporary folder (nsExec, nsis7z, SpiderBanner, StdUtils,
System and WinShell) carry no signature of ours. The installer's builder cannot sign them, and they are never installed.

The desktop's chat has been run in tests against stand-ins for Claude Code and Codex; no turn with a real host has been
watched through the desktop yet.

Work the desktop's chat queues for a role runs under that role's own execution profile, not behind the chat's cards.

The candidate 0.34.0-rc.1's desktop carries a `jason` without this refusal; `jason update apply` typed into it replaces
the file inside the application before any code of the new build runs. The new build finishes that update and refuses
the next one; the desktop's Check for Updates then brings the application to the same version. Such an update that
stopped at `kept`, before the new file was in place, leaves its record at `~/.jason/update/ledger.json`, in the data
directory, and every start of the runtime refuses while it is there; the desktop's `jason` once updated refuses to
finish or roll it back, so removing that file is what lets the runtime start.

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
