# Inbound sync

A campaign's manager is woken by what the runtime records ([campaign manager](campaign-manager.md)), and a reply is
something worth waking it for. Inbound sync is how the runtime comes to record one: on a cadence, the dispatcher reads
each live campaign's conversations at its provider, and for each thread that moved it writes one
`inbound_reply_observed` line to the chronicle. That line wakes the campaign's manager like a failed item or an
answered question does.

It interprets nothing. A line says that a thread moved, when, and who it belongs to — never what anybody wrote. What a
reply means, and what to do about it, is the manager's.

## 1. Where it runs

Each scan runs in eight short steps: **expire**, **enforce**, **events**, **sync**, **summon**, **resume**, **claim**,
**notify** ([work execution](work-execution.md)). Sync comes before the summon, so a reply it says wakes the manager in the same
scan, and before the claim, so a read it starts can be claimed in the same scan. Events, before it, hands a read of an
account's events to a reader that runs off the scan ([events](events.md) §5).

It runs whenever the dispatcher scans, draining included — a drain stops claims, not bookkeeping: reads are accounted
for, and a read started during a drain waits until claims resume — and does nothing when `Inbound:Enabled` is false.
A runtime with no dispatch loop reads nothing.

## 2. Which campaigns are read

Every **active** campaign, one read at a time. A draft has never gone live, and a paused or archived campaign is not
read; a paused campaign that goes live again is read from where it left off.

A read still outstanding when its campaign is paused waits, as `conversation.list` does in a paused campaign; one
already running finishes, and what it found is said once the campaign is live again. Archiving a campaign cancels its
read with the rest of its open work, and nothing it was reading is said.

A campaign is read when the operation `conversation.list` is routed for it to a plugin that reads conversations, and
the campaign is linked there — that plugin has pinned the campaign's own identifier at the provider
([routing](routing.md)). Otherwise it is skipped, by the first of four reasons, in this order:

| Code | Why |
|---|---|
| `no_route` | Nothing routes `conversation.list` for this campaign. |
| `plugin_operation_unsupported` | The plugin it is routed to is loaded and does not perform `conversation.list`. |
| `campaign_not_linked` | The campaign has no identifier pinned at that plugin, so a read would have nothing to name it by. Where the plugin reads its accounts' events, those the campaign rule places in no campaign still reach it by its people ([events](events.md) §3). |
| `plugin_reads_events` | That plugin reads its accounts' events, an event naming the campaign's provider campaign would reach it, and the source of the account the route binds is healthy. |

**When the plugin reads its accounts' events.** A plugin that declares the events contract has its accounts read on a
cadence of their own, and says a reply in a campaign from there ([events](events.md)). The inbound read leaves such a
campaign to it only where an event would reach the campaign — it is the one campaign, of those not archived, holding its
provider campaign's identifier pinned at that plugin, the rule an event is attributed by ([events](events.md) §3); two
campaigns linked to one provider campaign are both read here — and only while the source of the account its
`conversation.list` route binds is **healthy**: that source's last read ended in success and started within the shorter
of two `Dispatcher:EventsCadenceSeconds` and `Inbound:LookbackSeconds` — twenty minutes at the defaults. It never
leaves a campaign to a source before that source's first read that succeeded, and a look-back of 0 never leaves one at
all. A source that fails, or that is not read for that long, hands its campaigns back at the next due read, which starts
at the watermark — the moment of the last skip less the look-back, so never later than the start of the source's last
read that succeeded, and nothing is missed in the hand-back — and the inbound read is the fallback for as long as it
lasts. A move both reads find is said once: they say it under the same key. The step aside rests on a promise every
plugin that reads events makes, to name under `campaign` the provider campaign a moved thread belongs to. A move that
names no campaign reaches, by the person, every campaign that is not archived and holds the contact as a member
([events](events.md) §3), rather than the one campaign whose thread it is alone.

A skip is logged once for its campaign and reason in the life of the process, not once a cadence.

A plugin that is routed but not in the loaded set, unavailable on this machine, speaking another contract version or
refusing its binding is **not** a reason to skip. The read is made, and it fails at its claim, where the reason is
recorded on the work item ([routing](routing.md)) — and a failed read wakes the manager like any other failure.

## 3. A read

A read is one work item, made by the dispatcher through the routine every work item is made by, so it is routed,
claimed, retried and recorded like any other:

- kind `provider_op`, operation `conversation.list`, made by `system:dispatcher`, root lineage, about no one;
- the default priority and limits, and the reason "inbound sync";
- its input, under `context.input`: `source` `inbound` (only threads the other side wrote into), `since` (where the
  read starts, to the second), `limit` 100, and `offset` (0 for the first page).

It takes its place in the queue like any other work: one item per campaign per scan, the most important first, never
while the campaign has something scheduled or processing. A campaign with a backlog of work at the same priority reads
its conversations when the read's turn comes.

The read's own work item keeps the page it read as its result, as every read does — subjects and previews included. The
lines written from it carry none of that (section 4).

## 4. What a completed read says

For each thread on the page that moved at or after the read's `since`, one line:

| Field | Value |
|---|---|
| `kind` | `inbound_reply_observed` |
| `actor` | `system:dispatcher` |
| `campaign_id` | the campaign |
| `work_item_id`, `attempt_id` | the read, and the attempt of it that succeeded |
| `key` | `sha256:` over the plugin, the thread and the moment it moved |
| `new` | `plugin`, `thread`, `channel`, `last_activity_at`, `unread`, `has_meeting_intent`, `contact_id` |

`unread` and `has_meeting_intent` are the provider's own flags, said as it said them. `contact_id` names the person the
thread belongs to: the one contact pinned at that plugin with the identifier the provider gave, and `null` when no
contact is or more than one is. A value kept only as a divergence beside a pin is not a pin.

**Said once.** A campaign says each move of a thread once, however often a read lists it, and the database refuses a
second line under a key the campaign already holds. A thread that moves again is a new moment, and a new line: the
other side may have written twice. The plugin in the key, and the plugin whose pins name the person, is the one the
read ran at, as its attempt recorded it — a route changed since changes where the next read goes, not what this one
found.

**Never a person's words.** The subject, the preview, the name the provider shows, its own campaign identifier and its
vendor block are not read out of the page at all, and appear in no line and no log.

**What a line does not know.** A line says that a thread the other side wrote into moved, not that the other side moved
it last: a follow-up sent into a thread that already has a reply moves it too, and says it again. The manager reads the
thread to know.

## 5. Where a read starts

A campaign's **watermark** is where its next read starts, on the runtime's own clock.

- A campaign's first going live sets it, and a pause and a resume leave it where it is. What a person's conversations
  held before the campaign went live is history, not a reply to it; a reply that came in during a pause is not.
- An installation upgrading into inbound sync gives every active and paused campaign a watermark of the upgrade's
  moment, so nothing from before the upgrade is read.
- After a complete read it becomes the moment that read started, less `Inbound:LookbackSeconds`, unless it is already
  later. It never moves back, so never before the campaign went live.

**The clocks.** The watermark is the runtime's clock, and the provider compares it with its own stamps. The look-back
absorbs the difference between the two clocks, and the time a provider takes to bring a reply into its inbox after
the moment it stamps on the thread: the next read starts the look-back (four hours by default) behind where the last
one started, not at the newest moment it saw, and lists such a reply then. That is the bound: a reply the provider
takes into its inbox more than `Inbound:LookbackSeconds` after the moment it stamps on the thread is not read by the
step. A provider slower than that needs a longer look-back: raise `Inbound:LookbackSeconds`. The watermark follows
the clock rather than the moments read, so a campaign nobody writes to still moves on. With a look-back of 0 the next
read starts where the last one started, and a reply the provider lists that late is not said: a bound the setting
states.

**Pages.** When the provider holds more, the next page is asked for at once, from where the page ended, with the same
`since`. Every page of one read lists the threads that moved since the same moment, and a thread that moves while the
read is paging is listed again by the next read. A read ends at the furthest offset `conversation.list` takes, a
million threads, and moves the watermark as any complete read does: a thread further down the provider's order than
that, which moved before the read began, is not said.

**Skips.** A skip moves the watermark as a complete read that found nothing would: to the moment of the skip less the
look-back, unless it is already later. So a campaign that gains its route or link later starts from then, less the
look-back, and never before its go-live.

## 6. When a read fails

A read that is failed, cancelled or expired, or that succeeded with something that is not a page, counts a failure,
and so does a read that ended but could not be accounted for — a write that would not land; that one leaves no line
in the chronicle, only a warning in the log, and wakes nobody. A read still running is left to finish. The next read
waits `Inbound:SyncSeconds` doubled for each failure in a row, at most a day, measured from when the failed read
started; a read that completes clears the count, and so does a skip, which is not a read that failed. A read that fails starts again from its first page
with the same since, and the key keeps the re-listed threads from being said twice. Starting again rather than resuming
is what keeps a reply: pages are ordered by last activity, so a thread that moved while the read waited is listed first.

A failed read ends through the ordinary routine, so its `workitem_failed` line wakes the manager like any failure.
Making the next read is the dispatcher's, not the manager's: a failed `conversation.list` made by `system:dispatcher` is
not work to do again by hand.

A read whose result is not a page is logged by the kind of fault and where a reader stopped, never by what it held.

## 7. Settings

| Setting | Default | Range | What it does |
|---|---|---|---|
| `Inbound:Enabled` | `true` | | whether the scan reads conversations at all |
| `Inbound:SyncSeconds` | `900` | 60..86400 | how long after one read of a campaign starts the next is due |
| `Inbound:LookbackSeconds` | `14400` | 0..86400 | how far behind the last read's start the next one starts |

They apply at the next scan, without a restart; an edit the validator refuses leaves the last settings that validated
in force. A campaign skipped while `Inbound:Enabled` is false, or while the runtime is stopped, catches up when it is
read again: its next read starts from its watermark.

## 8. What it costs

One read per linked active campaign per cadence. At the default 900 seconds that is about 96 read work items a day per
linked active campaign, and about 384 lifecycle lines in the chronicle (each read is made, scheduled, processing and
succeeded), besides the lines that say a reply. A read lists every thread that moved within the look-back, 100 to a
page, so a busy campaign may take two or three read work items a cadence rather than one: as many pages as the threads
that moved in the last four hours fill. A thread listed again says nothing: its key holds a move already said. A
manager reading everything in the chronicle since its last review reads those too, about 80 in five hours; reading by
kind (`jason journal list --kind inbound_reply_observed`) passes over them, and one person's replies are
`jason journal list --contact cnt_… --kind inbound_reply_observed`.

Each read also keeps the page it read as its result, up to 100 threads with their subjects and previews, as any read
does. And a reply, or a read that failed, can wake the campaign's manager: what a check-in costs is in
[campaign manager](campaign-manager.md).

## 9. Upgrading

`inbound_reply_observed` joins the default `Manager:Triggers`. An installation that narrowed its triggers before the
kind existed keeps its own list, and is not woken by replies until it adds the kind.

## 10. Not here yet

- **Reading a reply.** A line names a thread; reading what it says is a `conversation.get` work item, or the
  responder's work.
- **Meaning.** Nothing classifies a reply, and nothing but the manager decides what it calls for.
- **Telling a person.** A reply is not notified; the manager's check-in is what it wakes.
- **A cadence per campaign,** and a read of any plugin but the one the campaign's route names.
- **Telling a person that a source of events keeps failing.** Nothing notifies it; while it fails, its campaigns are
  read here.
