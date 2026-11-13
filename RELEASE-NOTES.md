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

# Jason 0.40.0

## What is new since 0.39.0

This release gives the desktop a top bar in your own installation and in the demo, with **Open in**, which opens a folder in an editor, a terminal or the file manager, continues a chat in a terminal and shows the desktop's chat in a browser tab; it rearranges the console around a new first screen, a sidebar with a Dashboard, a search box and a list of recent chats with a filter and a search; and a new installation now starts on the demo.

- **A new installation opens on the demo.** The first start of a new installation opens the demo, with nothing to click: the page shows at once, and the sample is made behind it under a small "Preparing the demo…" mark, which takes a little over a minute; what the sample already holds is shown while the rest arrives. Until it is ready, the Demo | Live switch, **Start the Demo Over** and **Back to Your Own Installation** wait, each saying "Available once the demo is ready." **Live** then sets up your own installation as a first start does. An installation that existed before, with a mode record or without, and one updated from an earlier version open where they did. Full access is still not offered in the demo.
- **A top bar in both modes.** A bar heads every screen, in your own installation and in the demo: a green dot and **Live**, or an amber dot, **Demo data** and "Nothing here is real or sent."; on its right, **Open in** and the **Demo | Live** switch, which is now the way into the demo and back. The start screen's "See Jason with sample data" line is gone; **Start the Demo Over** stays in the tray and the Jason menu.
- **Open in.** A split button in the top bar, in your own installation and in the demo, opens the folder in an installed editor (VS Code, Cursor, Antigravity, Windsurf, Zed), in Claude Code or Codex as a new session in a terminal, in a plain terminal, in the file manager, or copies its path. The place you used last is the default, else Claude Code. With a campaign's chat in front it is that campaign's folder, otherwise the harness root; in the demo, the demo's folders, and a terminal there works with the demo's data. Nothing is written into your folders for it, and the chat's AI cannot reach it. See [the desktop guide](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md#open-in).
- **Continue this chat in a terminal.** While a chat is in front, Open in's last group can hold **Continue this chat in Claude Code** (or **in Codex**): a new terminal in which that program continues its own conversation, the one the chat's turns ran in, at the chat's folder. Nothing is copied and no transcript is written. It is offered only when the chat's last turn ran on your own Claude Code or Codex sign-in, outside Full access. No entry is shown for a chat on a provider's key, for a chat whose last turn ran in Full access, or for a chat begun before this release, until it has had a turn. While a turn of the chat runs, it cannot be chosen. Beside it the menu says that the conversation continues under that program's own permission settings, that this window's permission cards do not follow it, and that this window does not show what happens there.
- **The desktop's chat in a browser tab.** **Open in**, then **Browser**, opens the desktop's chat in a tab of your browser, one more view onto the same chats the window shows, while the desktop runs: the list of conversations, a conversation and the box. A tab sends only to a chat in Ask first or Plan only, never answers a permission or plan card, and never changes a chat's mode or chooses Full access; a chat started from a tab starts in Ask first. The desktop's window stays the only place that approves. Only a tab the desktop opened shows the chat, paired once by a code in the link it opens; any other tab's Chat screen says how to open it from the desktop. The runtime gains no route for it.
- **`jason session open` opens a folder, with no approval or question to start from.** `jason session open [--campaign <cmp_id> | --project <prj_id or slug> | --here] [--host <word> | --shell] [--resume <id>]` opens your agent host, or with `--shell` the system's plain shell, in a new terminal at the campaign's folder, else its project's, else the harness root; with `--here`, at the folder the command runs in, as it is; with none of the three, at the project of the folder you run it in, else the harness root. The host is `--host`, else the named preference `agent.interactive_host`, else Claude Code, and no first input is sent to it. `--resume <id>` has the host continue its own conversation of that id, as `claude --resume <id>` or `codex resume <id>`; another host is refused with `host_cannot_resume`. The form with an approval or a question is unchanged. See [the browser UI's page](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/ui.md#at-a-folder-with-no-item).
- **A new first screen.** Start carries Jason's mark over "Who do you want to reach?", and its box asks "Tell Jason what you sell and who you want to reach.", adding "A website link is enough to start." where the chosen AI host can open a page. Under the box sit an access button, for the mode new chats start with on this computer, a model button holding the model, the effort and the AI host, and a send arrow. The note under it, that this interface is optional, can be closed for each mode.
- **The sidebar.** **New GTM agent** is the main button and brings Start forward; its caret also offers **New chat**, a set-up chat as before, and **Connect an account**. Below it: Search, Feed, Dashboard, Needs you, with the number of approvals and questions waiting, People, Agents, Accounts, and **More**, which holds Performance, Activity, Work items, Connect and System. Settings sits alone at the foot. Every screen reached before still is.
- **A Dashboard.** A greeting, **Review N waiting** when something waits, and six tiles: Needs you, people contacted and people who replied over 30 days, with the reply rate, and Interested, Meetings booked and Bounce rate, which have no figures in Jason yet and show a dash and "no data yet", never a zero. Below them, the decisions waiting, the running agents with their people, contacted and replied over 30 days, and the Feed's latest lines. Every number is one the page already reads.
- **Search, with Ctrl K or ⌘K.** One box over the pages, Settings, New GTM agent and New chat, the agents and chats the console holds and, once you type, people; at most twelve rows, each opening where its sidebar row would.
- **Recent.** The chats under Recent each show a status dot, the number waiting on you and an age, ten at a time with **Load more**, and can be searched by title and filtered to Needs you, Live, Paused or Draft.

## Fixed since 0.39.0

- **A screen opened in the console's Page tab fills the panel.** Performance, Work items, People, System, Accounts and Settings were drawn in a narrow column at the left of the panel; they now take its whole width.
- **The demo's first opening no longer ends on "This browser isn't paired".** The page now opens before the sample is made, so the link that pairs it no longer expires while the sample is made.
- **A desktop quit while the demo's sample is made no longer leaves half a sample.** The next demo start makes the sample whole by itself, where before it said the sample was not finished and pointed to Start the Demo Over.

## Upgrading from 0.39.0

Nothing stops, no setting is renamed and no database migration is added.

- **The executable:** `jason update apply`, or run the install line again. After it, `jason skills update` and `jason ui install` follow.
- **The packages:** reply 0.15.1, journal-forwarder 1.3.0, ntfy 1.0.3, slack 1.0.0 and telegram 1.0.0, each the same as in 0.39.0, so there is nothing to replace with `jason plugin install`.
- **The desktop is a separate install,** as before: `jason update` does not install it. Download the installer or the disk image for your machine from this release, or let an installed desktop offer the update.
- **Update the desktop and Jason together.** The top bar, Open in and the chat in a tab are drawn by the page of this release, and the terminals the desktop opens run this release's `jason session open`.
- **The runtime's page may now connect to loopback HTTP.** Its `Content-Security-Policy` for every answer under `/ui` is `default-src 'self'; connect-src 'self' http://127.0.0.1:*; frame-ancestors 'none'; base-uri 'none'`: besides its own origin, the page may open connections to any port on `127.0.0.1`, and to no other host. That is how a tab reaches the desktop's chat.
- **A bare `jason session open`,** a usage error before, now opens your agent host at a folder, as above. A script that relied on the refusal gets a terminal instead.
- **An existing installation opens as it did.** Only a new installation's first start opens on the demo; an update from 0.39.0 changes nothing of where the desktop opens.
- **From 0.38.x or earlier,** the notes of each release in between apply as well.

## Known issues

The demo is a first cut with limits: it is email only, with no LinkedIn channel; it raises no question; a person whose message waits for your approval never answers it, because a reply is attributed once, when it is stored; no AI role runs in the demo's runtime; and the sample starts today with no past, so there are no charts of earlier weeks. The replies the story wrote have all arrived in the first twelve minutes, after which a kept demo shows nothing new until **Start the demo over**.

Open in's **Browser** hands the link to your browser as an argument. If the browser was not already running, its main
process keeps the link, with the pairing code in it, in its arguments for its whole life, where another program of your
account can read it. The code is spent on its first use or expires within sixty seconds, whichever comes first, and a
spent or expired code opens nothing.

Through Reply, a person the account already holds is never found by `audience.search`, however well they match, while
`audience.preview` counts and samples them, so a search can find fewer people than its preview said. A person the
campaign holds and Reply does not, one brought from a file say, can be found by a search again and is charged for; Jason
keeps them the one member they are.

A person the campaign holds from elsewhere, found by a search under another address, with no LinkedIn profile the two
share and no identifier of that provider pinned, becomes a second contact: a name never matches anybody.

Through Reply, a stop deletes the search's list: the people Reply found since the last read are charged, stay Reply's
contacts and never land in the campaign.

A search a person resumes at the provider after it ended `stalled` is not followed by the poll again; a read anybody
makes still lands its page: `jason workitem create <campaign-id> --kind provider_op --operation audience.search_status
--input '{"search":"<the search's work item>","limit":100}'`.

The guard that keeps a route for a deferred send has two limits. A send with no moment known is not guarded, though its result says it can be cancelled: a provider answers a send it will make as soon as it can the same way as one it keeps queued for a sending schedule of its own. And a send still on its way when its route changes is not guarded: it can succeed under a plugin and account no route names any more, and it is logged only at the next start. If the route of such a send has moved, cancel it at the provider.

A thread is matched to its plugin, not to its account: a reply routed to another account of the plugin that holds the thread is not refused by Jason, and the provider refuses it as a thread it does not hold.

A threshold set while nobody is on the campaign says at once that the reserve is low, since a count of nobody is under
any threshold, and wakes the manager for nothing. Set it once a search's people have landed.

The audience reserve counts a campaign's archived members nobody started work for, while the page's count of new
people leaves them out, so the reserve can read higher than the page and the notice that the reserve is low can come
later than the page suggests.

Routes changed while the runtime was stopped, the settings file edited by hand say, are not refused at the next start,
which has no routes in force to keep, and a search whose start is under way when a route changes is not counted yet. A
deferred send stranded that way is logged once at the start, by its work item, campaign and plugin, and every later change
that would strand one is refused. A
search still followed whose account the routes then name nowhere is said in the log at warning, with its campaign; until
a route names that account again, its reads and its stop are refused and it goes on at the provider to its count.

A search the provider had already ended when its campaign was archived ends as it ended, and through Reply its list
stays at Reply with any people not yet read. A search whose start was under way when its campaign was archived has no
record yet, so nothing stops it: whether Reply took the start is read at Reply.

In Ask first, and in Plan only, the desktop's chat runs `jason audience preview`, `jason audience filter-values` and
`jason audience availability` without a card, as in 0.36.0. Each one asks the provider, and nothing in Jason limits how
often a chat, or any client holding the run token, makes them, or how many run at once: each starts a plugin process of
its own, so a typeahead asking a filter's values at every key starts one per key.

Codex in the desktop's chat reads and advises, and asks for nothing: it runs in Codex's read-only sandbox.

In a Codex chat a command can read what you can, a plugin's secret written in `settings.json` included; chat on Claude Code where that matters.

The Windows check of Codex's sandbox has been proven against a stand-in only where the elevated setup is missing. On the one real machine checked before this release, a command inside the elevated sandbox was refused Jason's run file and could still read Jason's settings file; the check now rests on the run file, and has not been run on a real machine since. Whether naming the elevated sandbox raises an administrator prompt where it is not set up has not been seen.

On a Mac and on Linux, a read-only Codex turn can read the runtime's run file, so if the loopback interface is reachable from it, a command in a Codex chat could reach Jason's runtime. Codex's read-only mode has no network by the vendor's own description; that has not been probed.

A Feed row for a send says the provider took it: a send it accepted for a later moment, or for its own queue, appears
at once and can still be cancelled until it goes.

The Feed folds a line only into the one just before it, of the same kind, campaign, operation and role, so sends that
alternate with other lines, an approval and its send say, or two campaigns sending at once, are shown one by one and
fill the Feed's 500 lines and the fifty a Feed turn is given sooner.

When the window loads, the desktop reads back the newest hundred finished work items of any kind since the Feed's
newest line and keeps the sends and the five roles' work among them, so on a busy installation, where a campaign's
reads finish at every cadence, fewer than a hundred of these may come back. Older ones are not shown, no gap is said
for them, and they stay in the journal (`jason journal list --kind workitem_succeeded`). A catch-up whose read fails,
or that finds the Feed holding no line yet, brings in no sends.

A people search with text reads the custom fields of every contact it scans, one at a time, the pickers and the console's
search box that search as you type included; on a large installation a search takes longer than a search by name alone did. No measurement has
been made.

A desktop start whose runtime does not start skips the reset of what an earlier page kept, until a start that starts the runtime.

A long-running runtime can be ended by a rare fault in the SQLite provider's connection pool ([dotnet/efcore#38854](https://github.com/dotnet/efcore/issues/38854)); nothing recorded is lost, starting it again continues the work, and the runtime's log now says what ended it.

`jason campaign audience` lists an audience's latest hundred searches at most, with no page past them, so an allowance that tops an audience up for long enough leaves its older searches out of every read of the API but their work items and their lines in the journal. And two endings of the real reply-cli have only been met by a stand-in: `outcome.unknown` at exit code 1 from `live-data start`, which the Reply package reads as a lost answer, keeping the search's list for its repeat to find the search by; and `live_data.service_failed` after Reply did save the search, which the package reads as nothing started, so it deletes the list and the attempt is repeated. Should Reply ever answer so, a person would see a search at Reply that Jason does not follow, spending credits into a list that no longer exists, while Reply refuses the repeat, `search_in_progress`, and its work item fails; stop that search at Reply.

Under a Reply route whose binding names no profile, the binding is whichever profile reply-cli is set to, so switching reply-cli to another profile makes every read and stop of a search Jason follows fail `search_not_found`, and each such search ends `cancelled` while it goes on at Reply under the first account, its people never landing. A binding that searches should name its profile.

Not yet seen on a real machine:

- The Linux packages by hand on Ubuntu 24.04: the tray on GNOME and KDE, the keyring, start at login across a sign-out, a deb update's password prompt, the AppImage replacing itself, Remove What Jason Placed, and a `jason-desktop://` link clicked.
- The chat with real hosts and keys: a wrong key on each provider, which account pays when a plan is signed in, each effort level, Plan only and its plan card on a real Claude Code, Auto-edit on a real Claude Code, and Full access kept with Always across a restart.
- Routes by channel with two real providers: the tests run a campaign through two stand-in plugins.
- On Windows, a click on one of Jason's notifications bringing the desktop to the front on Needs you, with the desktop open and with it closed.
- The Reply card's account acts, and the Accounts page that draws the same card, on a real Reply account: connecting an account and reconnecting one, pausing a LinkedIn account and resuming it, and the link pages they open in the browser; with them the shared mailbox page, LinkedIn ids for Reconnect, and the confirmation box in a packaged build.
- On macOS, Remove What Jason Placed and the LaunchAgent; on every system, how the icon looks in the menu, the dock and the taskbar.
- Open in with real editors, terminals and file managers, and Continue this chat with a real Claude Code or Codex: the tests run against stand-ins, and the words that continue a conversation were read from each program's own help.
- The desktop's chat in a tab of a real browser: the page's tests run against a stand-in for the desktop's side, and the desktop's against a test client.

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

The macOS form of `jason session open`, a script opened in Terminal, has not been watched on a Mac, with an item or at
a folder, nor has the data directory it carries, nor the login shell `--shell` reads from Terminal's own environment.

Antigravity CLI's hook route has not been run on an account. The host also reads your own global `AGENTS.md` and
`GEMINI.md` under `~/.gemini` for every role.

Two installations on one account, a build of a checkout and the installed one, take the `jason:` link from each other at
each toast, since the registration rewrites a key that is Jason's and names another executable. Uninstall leaves a key
that names another installation alone, so nothing is lost.

On Linux, kitty, foot, gnome-terminal, xfce4-terminal, mate-terminal and wezterm are started in the form each takes;
any other terminal program is given the host after `-e` as separate arguments. The terminal process is the CLI's own
child and inherits its `JASON_DATA_DIR`; a terminal that hands the command to a server of its own already running may
start the agent host in that server's environment instead, and whether any does has not been determined.

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
