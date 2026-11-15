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

# Jason 0.43.0

## What is new since 0.42.0

This release is about what comes back after a message goes. A bounce is now read as a bounce, not as a reply: it is
counted in a campaign's activity and on the Dashboard, a notification says how many emails bounced, and where the
provider says the address itself does not take mail, the address goes on the do-not-contact register so nothing more is
sent to it; through Reply, that stop waits on releases of Reply and reply-cli not yet out, as the known issues say. A
reply to a message a campaign sent directly is now that campaign's own, answered by its responder and holding its next
automatic send like any other reply. A provider's limit on one channel of an account holds that channel alone, and
Reply's two limits on a direct email hold the mailbox instead of being repeated blind. And the console is one area
chosen from the sidebar, with no tab strips, and Connect names the MCP servers you already have for Claude Code and
Codex.

- **Bounces in a campaign's activity and on the Dashboard.** `jason campaign activity` (`campaign.activity`) gains the
  count `bounced`: the campaign's people a provider's bounce event names in the period, dated by the moment the provider
  gives, a person who bounced twice counted once, and hard and soft alike, since an event does not always say which. It
  is a count like the others: `jason campaign list-contacts --activity bounced` lists the people it counts,
  `jason campaign activity-by-period` carries it, `--count` takes it, a person's why names it, and
  `campaign activity --human` gains a BOUNCED column. The Dashboard's **Bounce rate** tile, which always showed a dash,
  now shows the window's bounced people over its contacted ones, with "N bounced" under it. Where nobody was contacted,
  or more people bounced than were contacted, yesterday's sends bouncing today say, it shows the dash with the count
  under it, never a rate above 100%. The campaigns table, the panel's figures, the People tab's Activity chips and the
  Performance tiles list it beside the other counts. See
  [a campaign's people, counted](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/work-execution.md#a-campaigns-people-counted).
- **A notification says how many emails bounced.** A seventh count joins the six a notification carried: "1 email
  bounced." or "N emails bounced.", each bounce counted once however many campaigns it reached, since the last
  notification that reached anybody, with no address, name or campaign name. A bounce is a reason to notify by itself,
  under the rule that already holds a burst to one notification in 30 minutes; there is no new setting. Said alone, it
  ends "Ask your agent what bounced.", and the `operating-the-installation` skill tells your agent where to look: the
  journal's bounce lines, each naming its campaign, and the `bounced` count and the people it counts. Every pointer still
  fits the delivery contract's 400 characters. See
  [notifications](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/notifications.md).
- **A hard bounce stops the address.** When a provider's events say that one of a campaign's contacts bounced, the
  runtime reads, with `message.status`, the campaign's own emails to that person the bounce could be of: sent directly
  through the plugin the bounce was read at, finished no earlier than 45 days before it, the newest three. A read that
  finds the email bounced writes `message_bounce_observed` about that send, and where the bounce is hard, or the
  provider names no kind for it, it puts the contact's email on the do-not-contact register as `system:bounce`, with
  the line named in the reason, so the claim refuses every later send to that address. A soft bounce, a full mailbox
  say, stops nothing and is the manager's to weigh; a send said soft and later found hard is said once more, and then
  stops. A person lifts a stop with `jason suppression remove`, and the same send never puts it back. The reads sit one
  below `Responder:Priority`, so a person's reply is read before a mail server's notice, and both before the sends. See
  [inbound sync](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/inbound-sync.md#1-where-it-runs) and
  [`message.status`](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/contracts/operations/message.status.md).
  Through Reply, this waits for releases of Reply and of reply-cli that are not out yet: until then each read answers
  that it cannot say, and nothing is stopped (Known issues).
- **A bounce is not an opt-out.** The activity's `opted_out` leaves out the register entries the runtime writes for a
  bounce, in the count, its moments, the people it lists and the counts over time, so a list with bad addresses does not
  read as one whose people asked to be left alone. An opt-out, and a person's own `jason suppression add`, count as
  before.
- **The bounce line, and two lines of 0.42.0, for subscribers.** The journal forwarder 1.4.0 declares
  `message_bounce_observed`, and `global_routes_updated` and `plugin_grants_updated`, which 0.42.0 wrote and which can
  now be subscribed to, so a person can grant all three. The bounce line is no default manager trigger, since the
  provider's bounce event has already woken the manager; add it to `Manager:Triggers` to be woken by it too.
- **A reply to a campaign's own direct send is the campaign's own.** A reply Jason finds through the person, rather
  than through the campaign's own provider campaign, used to be left to the manager, so a campaign that wrote to a
  person directly never had its responder raised for their answer, could not see them become warm, and did not hold its
  automatic sends to them while the answer waited. Such a reply is now the campaign's own where the thread already was,
  or, for a reply that names no provider campaign, on a thread no campaign it was found in holds yet, where that
  campaign alone had written to the person through that plugin before the reply: a message, an invitation or an answer
  that went. It raises the campaign's responder, a later move on the thread is recorded on the responder already open,
  an unjudged one makes the contact wait, and a `person` verdict on it makes them warm. `inbound_reply_observed` says so
  with `campaign_own: true`. See [inbound sync](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/inbound-sync.md)
  and [events](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/events.md).
- **The console: a sidebar and one area.** The console, in the desktop and in a browser tab, is a sidebar and one area
  beside it that shows one thing at a time: Start, one chat, Dashboard, Needs you, Agents, Activity, Connect, or one of
  People, Performance, Work items, System, Accounts and Settings. You choose it from the sidebar, its Recent list or
  Search, and the sidebar marks what is shown. The tab strips are gone: nothing is closed any more, only chosen. A chat
  you leave and choose again keeps the words you typed and did not send, what arrived meanwhile and the message that
  waits. Needs you keeps its count in the sidebar. The window opens on Start, and a chat taken away shows Start in its
  place. See [the desktop](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md) and
  [the browser UI](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/ui.md).
- **Connect names the MCP servers you already have.** Under Claude Code and under Codex, Connect says in one line which
  MCP servers you have set up for that program, the first five and then "and N more", and that they work when you open
  Jason in it from Open in, where it runs with your own settings: Claude Code's user servers, and the servers of Codex's
  `config.toml` but one set `enabled = false`. Only the names are read; no command, argument, address, header or
  variable is kept, and a name is never logged, given to the chat or put in a diagnostic bundle. With no
  server, or a file that cannot be read, Connect says nothing. Open in's Claude Code and Codex entries now read "A new
  session in a terminal, with your own settings and tools". See
  [the desktop](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md).

## Fixed since 0.42.0

- **A bounce notice is no longer taken for a reply.** When an email bounces, the notice lands on its thread at Reply as
  an inbound message. Reply's change feed flags such a thread, and the Reply package used to read it like any thread the
  other side moved: it was said as `inbound_reply_observed` and raised a responder to answer a mail server. Through the
  Reply package 0.16.0, a thread flagged as holding a bounce and none of a reply, an automatic reply or an absence,
  whose last message is inbound, is one `bounce` event, said as `provider_event_observed`: it wakes the campaign's
  manager and feeds the count, the notification and the stop above. A thread flagged with a bounce and a reply stays a
  reply. A bounce on a thread Reply names no contact for is left out, and the package says so in one line of its
  diagnostics, by the thread's and the sequence's identifiers.
- **A provider's block on one channel of an account holds that channel alone.** A sender's block used to hold the
  sender on every channel: a daily limit on a mailbox's email stopped the same account's LinkedIn work, and the first
  send of any channel that got past cleared it. A sender's block may now name its channel (`channel` in
  [the block's document](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/contracts/block/provider_block.md)):
  it holds that channel's work, and the sender's block that names none holds every channel with no block of its own.
  Only a send that gets past, which clears its own channel's block and the one that names none, or a person clears a
  sender's block. The Reply package 0.16.0 names the channel, `email` or `linkedin`, in every sender's block it reports,
  since Reply numbers its mailboxes apart from its LinkedIn accounts and mailbox 12 and LinkedIn account 12 are two
  accounts. A block that names another channel than the one the send was asked to go on is refused, and the attempt
  ends `permanent`.
- **Reply's limits on a direct email hold the mailbox instead of being repeated blind.** The mailbox's daily limit
  (`contact.dailyLimitReached`) and the account's active contacts for its billing period
  (`contact.activeContactsLimitReached`) reached the package as reply-cli's `rate_limited` and were taken for a busy
  Reply, so the send was repeated against a limit that would refuse it again. Through the Reply package 0.16.0 both end
  the attempt `provider_blocked`, on the mailbox's block for the `email` channel, which leaves its LinkedIn work alone.
  The daily block clears at the next midnight UTC, when Reply counts a new day, or at the wait reply-cli names; the
  active-contacts block only at such a wait, and it is otherwise tried at the runtime's cadence. A refusal that names a
  record of the send names a dead one, which is no longer kept as the work item's `send`. `message.status` passes both
  codes through as the send's `failure_code`. Against a Reply that never answers these codes, nothing changes.

## Upgrading from 0.42.0

Nothing stops and no setting is renamed. Two database migrations run on the first start, after the backup the runtime
already takes: a provider block's channel, with the rule of one open block per sender made again over it, and each
campaign's place in the bounce reads, with an index for a send's bounce lines. Every block already open names no
channel, and every campaign starts at the journal's last line, so a bounce said before the update asks for no read.

- **The executable:** `jason update apply`, or run the install line again. After it, `jason skills update` and
  `jason ui install` follow: the first brings the skills' words on bounces and on a block's channel, the second the
  console's one area and the Bounce rate tile.
- **The packages:** reply 0.16.0 (0.15.1 in 0.42.0) and journal-forwarder 1.4.0 (1.3.0); ntfy 1.0.3, slack 1.0.0 and
  telegram 1.0.0 as in 0.42.0. `jason update apply` replaces an installed package you did not edit; for one you edited,
  `jason plugin install <id> --replace` puts this release's in its place.
- **The Reply package 0.16.0 needs Jason 0.43.0,** and its manifest cannot say so: an earlier runtime loads it, refuses
  a block that names a channel, and the send that met the block ends `permanent` instead of waiting on it. Update the
  executable and the package together, as `jason update apply` does.
- **A block open at the update** names no channel, so it still holds its sender on every channel until a send gets past
  it or a person clears it with `jason block clear`.
- **The opt-out count** leaves out only the runtime's own bounce entries, which no release before this one wrote, so
  every entry already on the register counts as before.
- **For subscribers.** `message_bounce_observed` is a new kind. `inbound_reply_observed` may carry `campaign_own`, and
  `notification_sent` carries `counts.bounces`, each marked in its document as arriving in 0.43.0. A sender's block a
  plugin reports may carry `channel`.
- **A narrowed `Manager:Triggers`** keeps your choice; `message_bounce_observed` is in no default, so add it if a send's
  bounce should wake the manager.
- **The desktop is a separate install,** as before: `jason update` does not install it. Download the installer or the
  disk image for your machine from this release, or let an installed desktop offer the update. This release's desktop
  carries the console's one area and Connect's list of MCP servers.
- **Going back:** an apply of 0.42.0 leaves the newer packages where they are. 0.42.0 refuses the forwarder 1.4.0 over
  the kinds it declares, and a package problem rejects the whole load, so the runtime starts with no plugin loaded at
  all until the forwarder is put back with `jason plugin install journal-forwarder --replace`, run just after the
  apply. It loads Reply 0.16.0 and refuses its blocks, as above, so put that back the same way,
  `jason plugin install reply --replace`. The older executable opens the database as this one migrated it, which it may
  refuse to do. See
  [Applying an older release](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#applying-an-older-release).
- **From 0.41.x or earlier,** the notes of each release in between apply as well.

## Known issues

Through Reply, the stop after a bounce works only once a release of Reply reports a direct send's bounce on the send's
own record and a release of reply-cli prints it; neither is out yet. Until then, a bounce is recorded, counted in the
campaign's activity and on the Dashboard, and notified, and nothing is stopped: each read a bounce asks for answers that
it cannot say whether the email bounced.

A campaign's bounce reads sit just below its responder and above its sends, and a campaign's work is claimed one item a
scan, so while many bounces' reads wait, a storm of them after a bad list say, that campaign's sends and its manager's
check-in wait behind them. Each bounce asks for up to three reads.

A bounce stops an address only where it is of an email the campaign itself sent directly through Jason, through the
plugin the bounce was read at, no earlier than 45 days before the bounce, and only the newest three such sends are
read, so for a bounce read long after it happened, a span read again say, they may be sends made after it, and the one
that bounced is not read. A bounce of a sequence's email at the provider, of a mail written by hand or of an older send
is counted and notified, wakes the campaign's manager, and stops nothing.

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

The demo has limits: it is email only, with no LinkedIn channel; it raises no question; a person whose message waits for your approval never answers it, because a reply is attributed once, when it is stored; and no AI role runs in the demo's runtime, so no reply there is judged, and Interested, Meetings booked and **Replies by category** have nothing to count. The sample's story began when the release was built, with no past before it, so the Dashboard's 30 days hold it for thirty days after. Where the sample is placed, the replies the story wrote are already in when the demo opens; where it is made at the start instead, its story starts that day and its replies arrive within minutes. After that, a kept demo shows nothing new of the story until **Start the Demo Over**. In a placed demo the Dashboard's daily trends read ±0 at first, since nothing of the story falls in the last 24 hours.

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
- The demo placed from the release's archive on a Mac and on Linux: the placing has been timed on Windows alone.
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
