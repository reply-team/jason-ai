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

# Jason 0.45.0

## What is new since 0.44.0

This release is about what happens when you first start the desktop: an AI you connect now runs Jason's agents, the Demo
opens at once as a sample page, and the console has a new look.

- **A new installation's agents run.** When an AI is connected in the desktop, Jason's roles (drafting, personalising,
  answering) now run on it. In Live, where the installation has no execution profile at all and its settings name no
  default one, the desktop makes one profile for the AI its chat runs on, `claude-code` or `codex`, and names it the
  default, and Connect says so in one line beside that AI: "Your agents' roles run on Claude Code, by the execution
  profile claude-code." Of Jason's settings it writes only one profile and one key, `Roles:DefaultExecutionProfile`, in
  `~/.jason/config/settings.json`, which the runtime reads without a restart (while it works the desktop also keeps a
  mark of its own, the profile's name and id, in its own settings, and removes it when done). An installation that already has a
  profile, or already names a default, is not touched, and neither is one set up by hand. An AI reached by a model key
  never gets a profile, since a profile holds no credential. If you install from the command line, the install guide
  gives the two steps the desktop does for you: `jason profile create claude-code --host claude_code --program claude`,
  then the line `"Roles": {"DefaultExecutionProfile": "claude-code"}` in the settings file. `jason status` now has an
  optional check, `roles_launchable`, that says when no role can run and which of the two steps is missing. It never
  decides `ready`, and it reads only the default profile: not one a work item, a campaign or a role names for itself.
  See [the desktop](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md) and
  [the install guide](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/INSTALL.md).
- **The Demo is a sample page.** The desktop's Demo opens at once on a new installation and shows an example of what
  can be built on Jason. It runs no Jason behind it, sends nothing and reaches nothing: the page is shown sealed, so it
  cannot reach the network, your installation or your disk, and what you do in it is not kept. Some of what it shows is
  not in the product. The switch to Live is the one at the top of the window. Choosing Demo no longer starts a second
  installation, and no longer makes folders on your machine. See
  [the demo](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md#the-demo).
- **The console has a new look.** Every screen of the console now shares one palette, so People, Accounts, Settings,
  an agent's tabs, Work items, System and Performance no longer look like another product beside it (in the light
  theme; the dark theme and the classic browser page draw as before). The sidebar is wider, with its rows, counts and
  icons redrawn, and the top strip sits to its right, with Demo | Live as one switch and a quieter Open in. The start
  screen has the question centred over a box that grows with what you type, with the access mode and the model chosen
  from menus, each saying what its choices do, in place of the browser's own selects, and one line under the box that
  says what the mode lets the AI do. In the chat, the agent's words carry a small "Jason" label; an approval's Approve is
  green and Reject red, a long message shows four lines with "Show the whole message" under them, and a question's
  options are pills followed by "Something else…", which opens a box for an answer of your own. Nothing about deciding
  has moved: an approval is still approved only once it has been read whole.
- **On macOS the window has no title bar.** Its three buttons sit at the top left over the sidebar, and the window is
  moved by the top strip and the top of the sidebar. So that it can be moved on every page it shows, the desktop adds a
  small drag area of its own at the top left, over an older or custom console too. Windows and Linux keep their native
  frame.

## Fixed since 0.44.0

- **A new installation could not start any AI role.** Before this release, a new installation had no execution profile
  and named no default one until a person made them, so the work of every AI role failed when it was claimed, with
  `role_not_launchable`: nothing was drafted and no approval appeared after pressing Start, while `jason status` said
  ready. The desktop now makes the profile, the status check now says when none can run, and the install guide gives the
  two steps.

## Upgrading from 0.44.0

Nothing stops, no setting is renamed and no database migration runs.

- **The executable:** `jason update apply`, or run the install line again. After it, `jason ui install` brings the
  console's new look, and `jason status` shows the new optional check `roles_launchable`.
- **The first Live start of the new desktop** with an AI connected makes the profile and writes the default described
  above, where the installation has none of either. Where a profile exists, or a default is named, nothing changes, and
  the desktop never replaces either. Without the desktop, `jason status` tells you the command and the line.
- **The Demo** no longer uses the two folders the earlier Demo made, `~/.jason-demo` and `~/Jason-demo`. They stay on the
  machine and nothing reads them. Remove What Jason Placed (the Jason menu and the tray, on macOS and Linux) and the
  Windows uninstall take them off today; otherwise delete them by hand once nothing runs from them. A later release
  removes them itself.
- **The packages** are those of 0.44.0: reply 0.16.0, journal-forwarder 1.4.0, ntfy 1.0.3, slack 1.0.0 and
  telegram 1.0.0, and the skills are unchanged.
- **The desktop is a separate install,** as before: `jason update` does not install it. Download the installer or the
  disk image for your machine from this release, or let an installed desktop offer the update. This release's desktop
  carries all of the changes above.
- **From 0.43.x or earlier,** the notes of each release in between apply as well.

## Known issues

The macOS window's new frame was not looked at on a Mac before this release: it is held by tests and by a reading of
the type definitions of the Electron the desktop carries, and the Demo's macOS window, with its own band at the top, was not seen either. If the window
cannot be moved there, open the console in a browser tab instead, with Open in's Browser or `jason ui open`, and tell
us; Windows and Linux are not affected.

On macOS, a console older than the desktop, or a custom one under `<data>/ui/custom`, draws its strip across the whole
top of the window, so its first words sit under the window's buttons. The window still moves and no control is lost;
`jason ui install` brings the current console.

In the Demo, the page's own controls that would take you out of it, a link or a form, say, do nothing: the page is
sealed. Use the switch at the top for Live.

In the chat, the four-line clamp of a long message does not hold Approve: Approve is live with four lines shown, as it
is when the whole message is open, once the approval has been read whole.

The status check `roles_launchable` reads the default execution profile alone. It does not look at a profile a work
item, a campaign's policy or a role's policy names, nor at an entry command, so it can say `ok` for a role that is


A Needs you row can still be approved without opening the message: its subject and body show only in Details. Approve
is live once the approval has been read whole, whether or not Details was opened.

The agent's type in the header reads one page of the agent's unfinished provider operations, oldest first, and no more,
so that a busy agent's header costs one read. An agent with more unfinished provider operations than a page, whose
campaign's creation is not among the first of them, reads **Adaptive** while a provider campaign is still being made,
until that creation is reached. If that read fails, no type is shown.

The Work items page still offers Approve on an item that waits for approval without that read: it shows no doubt and
does not check a message against its work item. Approve on Needs you and on the agent's chat, where the approval is read
whole first.

Through Reply, the stop after a bounce works only once a release of Reply reports a direct send's bounce on the send's
own record and a release of reply-cli prints it; neither is out yet. Until then, a bounce is recorded, counted in the
campaign's activity and on the Dashboard, and notified, and nothing is stopped: each read a bounce asks for answers that
it cannot say whether the email bounced.

A campaign's bounce reads sit just below its responder and, while `Responder:Priority` stays above the sends' own
priority, above its sends; a campaign's work is claimed one item a scan, so while many bounces' reads wait, a storm of
them after a bad list say, that campaign's sends and its manager's check-in wait behind them. Each bounce asks for up to
three reads.

A bounce stops an address only where it is of an email the campaign itself sent directly through Jason, through the
plugin the bounce was read at, no earlier than 45 days before the bounce, and only the newest three such sends are
read, so for a bounce read long after it happened, a span read again say, they may be sends made after it, and the one
that bounced is not read. A bounce of a sequence's email at the provider, of a mail written by hand or of an older send
is counted and notified, wakes the campaign's manager, and stops nothing. So is a bounce of a send whose campaign's
route has since moved to another plugin or account: no route takes a read of the send where it ran.

Through Reply, a bounce on a thread that Reply's change feed also flags as holding a reply, an automatic reply or an
absence, flags that stay once set, is still read as a move of the thread: it raises the responder against the mail
server's notice, and it is not counted, notified or stopped as a bounce. A bounce on a thread whose newest message is
already the account's own when the feed is read is not seen at all.

The address a hard bounce puts on the register is the contact's email when the read ends, since a send's record does not
keep the address it went to: where the contact's email was changed between the send and the read, the new address is
the one stopped.

A person whose email a bounce already stopped, and who then asks to be left alone, gets no register entry of their own
for that address, so the opt-out is not counted for it; the opt-out lock still holds them in every campaign.

No one command lists what bounced: `jason journal list --kind provider_event_observed` lists every provider event, of
every kind, and `jason campaign list-contacts --activity bounced` the people a campaign's bounces named in a period.

The desktop's chat runs without your own MCP servers: Connect lists them, and they work in a session opened from Open
in. Connect lists Claude Code's user servers and the servers of Codex's `config.toml` only; the servers of the folder
Open in opens, of a plugin, and claude.ai connectors or ChatGPT apps are not listed, and work in such a session all the
same.

A reply Jason finds through the person, not through the campaign's own provider campaign, is a campaign's own only where
the thread already was, or where that campaign alone wrote to the person through that plugin before it; otherwise it
neither makes the person warm there nor holds a message to them there. So once two campaigns have written to a person,
a reply on a thread neither holds as its own raises no responder and holds nothing in either, a person already warm in
one of them included; both managers are woken by it, and only an opt-out verdict on it stops the sends. Whether a send
came before the reply is told by two clocks, the runtime's and the provider's, so a send that finished within their
difference of the reply may be counted on the wrong side of it.

Where two campaigns both already hold a thread as their own, a reply on it raises a responder in each, so the person can
get two answers.

Through Reply, a thread whose newest message is the responder's own answer is read as a move of the thread, so in a
campaign whose threads are its own, direct sends' threads now among them, every answer the responder sends costs one
more read of the thread and one more responder run, which find nothing to answer.

While Reply's active-contacts limit holds a mailbox, every email from it waits, answers to people already in
conversation included, though Reply may take those. A block's trial is always its oldest waiting send, so while Reply
keeps refusing that one, the others wait behind it. Raise the limit at Reply, or wait for the billing period to renew.

Reply gives its daily-limit code also to an email held for later while the mailbox already holds as many held emails as
its daily limit, and Jason cannot tell the two apart: every email from that mailbox, one that would go now included,
then waits until midnight UTC, or longer while the mailbox holds that many.

Where reply-cli passes one of Reply's two limits on a direct email on as an answer it lost, rather than as a refusal, no
block is opened: the active-contacts limit then ends the send `provider_call_failed`, permanent, and the daily limit at
a 429 is repeated as a lost answer is.

Nothing follows a send Reply holds for later to its end: one Reply fails when it goes is known only when somebody reads
it with `message.status`, which gives Reply's `failure_code`.

A send moved to another sending account while it waited on the first account's block clears that block when it goes
from the second, though nothing went from the first; the first account's other sends are then tried again, and meet the
provider's refusal anew where it still stands.

The opt-out lock holds by the contact's record: another contact record of the same person, with a value the opted-out
record never had, is not covered, since Jason does not merge contacts. The register still holds every value the
opted-out record had.

Where a provider does not say which person a reply is from, Jason cannot hold an automatic message to that person until
the reply is judged: until a verdict names them, a first message to them may still go.

Under `automatic` for people who have replied, a message that is not an answer, such as a follow-up planned before the
person wrote, can still go to them on its own after their reply was marked for you. Only an answer in that conversation
is held.

`route set --global` and `plugin grant` refuse a settings file that is a link to another file
(`settings_file_not_editable`), since a write would replace the link with a file of its own. Edit such a file by hand
and run `jason plugin reload`, as before.

The runtime writes the settings file only if it still holds what was read, and makes its edit again from a fresh read
when another program changed it in between. A program that writes the file in the instant between that check and the
rename can still lose its change. Editing `settings.json` by hand while a `route set --global` or `plugin grant` runs is
the case to avoid.

`jason session open --setup` adds the plugins folder only for Claude Code and Codex. Another host opens at the role
skills folder alone, and reaches the plugins folder by the path `jason status` prints.


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
- The console in a tab of a real browser that the desktop opened: the page's tests run against a stand-in for the desktop's side, and the desktop's against a test client.
- Connect's list of MCP servers against a real Claude Code's and a real Codex's settings: the tests plant their own
  files in a temporary home.
- A bounce on a real Reply account: the package's reading of Reply's change feed is tested against a stand-in for
  reply-cli.

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

The quick-start skill ships marked `draft` as well: its words, and the install guide's hand-over to it, are held by tests,
and no agent host has yet been watched following it from a ready installation to a first campaign.
