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

# Jason 0.38.0

## What is new since 0.37.0

This release lets one campaign send each channel through the plugin that suits it, and gives the desktop's chat and its page the controls people asked for: Full access answered once, Auto-edit, a plan card, an Accounts page, and an approval card that shows the message as written.

- **A campaign can send email through one plugin and LinkedIn messages through another.** A route may now name a channel under an operation, for `message.send`, `conversation.reply` and the `sender.list` filter: `jason route set --campaign <id> --operation message.send --channel linkedin ...`, or `Channels` under the operation's entry in `Routes:Operations` in `settings.json`. A channel route is looked for before the operation's own route, in this order: the campaign's channel route, its operation route, its default; then the global channel route, the global operation route, the global default. A campaign's own route or default therefore still hides a global channel route, and a call that names no channel never takes a channel route. What follows the send:
  - **A cancel and a status read of a send go where the send ran,** so a campaign with two plugins can cancel all its sends, not half.
  - **Work on a thread goes where the campaign saw it.** A read or a filing of a conversation goes to the plugin where the campaign saw the thread, and a reply routed to another plugin than the one that holds the thread is refused before anything runs, with `thread_route_mismatch`.
  - **A route change may not strand a deferred send.** `route.set`, `route.unset` and a reload that would take away the last route naming the plugin and account of a send the provider still holds for a later, known moment are refused with `route_orphans_send`, saying how many sends, their ids and the latest moment among them; cancel them with `message.cancel`, or change the route after that moment.
  - **A channel route needs a plugin that reads events,** since a reply to what a channel route sends reaches the campaign only through the events of the account it went from. A plugin that declares no `contracts.events` takes no channel route (`route_channel_plugin_reads_no_events`), and a package update that would drop `contracts.events` from a plugin a channel route names is refused whole.
  - **Every surface shows them.** `jason route set`, `route unset`, `route resolve` and `operation describe` take `--channel`; `route list` and the human views read a channel route as `<operation> on <channel>`; `jason status` and the System screen say how many of the routes go by channel, and `jason doctor` lists each channel route with its channel and gives no count. The routing guide, [Routing](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/routing.md), and the skills say how.
- **Full access can be answered once for good.** The confirmation offers **Give full access this time**, **Always on this computer** and **Cancel**; a checkbox, counted only with Always, also starts new chats in Full access. The desktop keeps the choice in a file of its own in the data directory, which a chat's commands are refused a write to, and it is taken back from the mode menu or the System screen. A chat already in Full access stays in it, and a chat reopened after a restart is in Ask first. The Feed is still refused Full access, and a held command's card still offers no Always. The choice exists for Claude Code only. It carries a consent number: a later release that widens what Full access reaches asks again.
- **Auto-edit** is a fourth access mode in the desktop's chat, between Plan only and Full access. It is Ask first, except that `mkdir`, `touch`, `mv`, `cp`, `rm` and `rmdir` run without a card when every path they name, and every path a copy or move writes, is inside the chat's own folder. Jason reads each command word by word and never hands this decision to the host's own accept-edits mode. A recursive or forced removal, a copy of a folder, a link that leads nowhere, a file in the chat folder named like one of the six programs, and anything outside the chat folder stay Ask first's card, and whatever Ask first refuses stays refused. Auto-edit is on Claude Code only, is not offered for the Feed, asks no confirmation, is never a default, and is gone when you close Jason. Its three limits are in [the desktop guide](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md).
- **A plan card in Plan only.** The answer a Plan only turn ends with is shown as a plan card with **Approve and run** and **Keep planning**. Approve and run switches the chat to Ask first and asks the AI to go ahead with that plan, never in Auto-edit or Full access, and sends nothing if the mode changed in between; Keep planning stays in Plan only. The card is live on the host that made the plan; on Codex it says the approved plan still only reads and that Claude Code carries it out. The Feed has no plan card.
- **An Accounts page.** One screen for what you have connected: in the desktop, the Reply card with its confirmed account acts (connect, reconnect, limits, pause and resume, unblock, delete), and for the notification channels whether each is on and what happened to its last notification, read only. In a browser the page says the provider's acts belong to the desktop.
- **The approval card shows the message as written.** For a parked email it shows From, To, Subject and the body as text; for a LinkedIn message, its text; for a template, the text and the example the runtime made for the person. A sentence under it says what it leaves out: the provider's signature and tracking.
- **A parked message can be edited on its card,** a first message or a reply: **Edit message**, then **Save**. Saving approves nothing; Jason asks again, about the new text, when it next takes the work, and the old approval can no longer be given. A card whose text changed elsewhere offers no Approve.
- **The page says "agent" for a campaign with its chat,** and the program that runs commands is "the AI". Only the page's words change; the CLI, the API, the skills and the documents keep "campaign".
- **The desktop's first screen says the interface is optional,** and an Alpha label sits beside the wordmark, in the browser and in the desktop alike.
- **The model menu starts with Default (recommended),** which passes no model and lets the host choose. Claude Code's entries read Opus (latest), Sonnet (latest), Haiku (latest) and Fable (latest), and a chat that chose a model before keeps it.
- **What fences a command of a Codex chat, system by system.**
  - On Windows a Codex chat needs Codex's elevated sandbox. The desktop checks, with no model, at the first Codex message of a run, that a command inside it is refused Jason's run file; Windows may ask for an administrator then, and answering No means chatting on Claude Code instead. Where the check fails, the chat says so and names `codex sandbox setup --elevated`, or Claude Code.
  - On macOS and Linux, Codex's read-only mode has no network by the vendor's own description, while the run file is readable there.
  - On every system a Codex command can read the rest of the data directory, a plugin's secret kept in `settings.json` among it, and the line under the Codex composer says so, system by system.
  - The desktop's window no longer keeps its pairing with the runtime on disk, on any system. At its first start this version ends every UI pairing and removes what earlier versions stored, so a browser you paired with `jason ui open` must be paired again once.

## Fixed since 0.37.0

- **Editing a parked item's input no longer leaves its question about the old text.** In 0.37.0 an edit of the arguments of work awaiting approval left the approval pending about the old arguments, and approving it went on to fail at the claim. Now the edit withdraws that approval in the same save and the work goes back to the queue, where the claim asks again about what it says. An approval or a rejection of the withdrawn question answers `approval_not_pending`, and an edit that races an approval writes nothing and answers `concurrent_update`. A standing approval or the campaign's allowance never lets an edited item go.

## Upgrading from 0.37.0

Nothing stops, with one exception below, and no setting is renamed.

- **Two database migrations,** `ChannelRoutes` (a table of its own for a campaign's channel routes) and `JournalByThread` (an index over the journal's observed replies), apply automatically at the first start, after the backup every migration takes. `jason update rollback` puts that backup back, so what the runtime recorded after the update is not carried back to 0.37.0. An executable older than this release can change or remove an operation's own route while channel routes stay; the newer one reads that state as it stands.
- **A `Routes` settings file can now name channels,** under an operation's entry as `Channels:<channel>: { Plugin, Binding }`. A file without `Channels` reads and activates as before. The one exception: a `Channels` written where no operation stands, under `Routes:Default` or at the top of `Routes`, which 0.37.0 read as nothing, is now refused with `route_channel_not_routable`: a reload is rejected whole and the previous routes stay, and a start with such a file starts with no plugins and no routes and reports it.
- **`--channel` on the route verbs and `operation describe` refuses with `runtime_too_old`** (exit 1, nothing sent) until the running runtime is this version. Restart the runtime if it was updated without a restart, or update Jason.
- **The executable:** `jason update apply`, or run the install line again. After it, `jason skills update` and `jason ui install` follow.
- **The packages:** reply 0.15.1, journal-forwarder 1.3.0, ntfy 1.0.3, slack 1.0.0 and telegram 1.0.0, each the same as in 0.37.0, so there is nothing to replace with `jason plugin install`.
- **The desktop is a separate install,** as before: `jason update` does not install it. Download the installer or the disk image for your machine from this release, or let an installed desktop offer the update. Its minimums are those of 0.37.0: reply-cli 0.21.0 for the Reply card, Claude Code 2.1.257 to run a chat turn and 2.1.322 for Full access, Codex 0.122.0.
- **Codex on Windows needs its elevated sandbox for the chat.** Have an administrator run `codex sandbox setup --elevated` once, or chat on Claude Code; until then the chat declines a Codex message and says so. A Codex chat signed in with an OpenAI key is declined on Windows too.
- **A browser paired with `jason ui open` pairs again once,** after the desktop's first start of this version.
- **Out of the box nothing changes in the chat:** Ask first stays the default, and Full access asks each time until you choose Always.
- **Going back:** what `jason update apply --version` to an older release does to a newer plugin package, and how to load one again, is in [Applying an older
  release](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#applying-an-older-release).
- **From 0.36.x or earlier,** the notes of each release in between apply as well.

## Known issues

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
