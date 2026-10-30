# Events

A provider knows things Jason was not there for: somebody replied, an invitation was accepted, a message bounced. The
events family is how Jason comes to know them — by reading each account a route binds, a page at a time, from the
point the plugin's own cursor names — and how what it read reaches a campaign: as a line in that campaign's chronicle,
which wakes its manager like a failed item or an answered question does ([campaign manager](campaign-manager.md)).

Nothing here reads meaning. An event is identifiers, moments and the provider's own flags; which campaigns it concerns
is decided by the identifiers the campaigns' own operations pinned — the provider campaign first, the person where that
finds no campaign — and what it calls for is the manager's.

This page is the contract for reading an account, the events store and the lines it writes, a span read again on a
person's request, and the record of the accounts. What a plugin author implements is
[`events.read`](contracts/events/events.read.md).

## 1. The contract

`events.read` is a family of its own beside the canonical operations and the delivery contract
([contracts](contracts/README.md)): a read of an account belongs to no campaign, so no work item names it, no route
carries it and no approval applies to it. A provider plugin declares that it reads its accounts' events in its manifest,
`contracts.events: [1]`, beside `contracts.operations`; a notification plugin that declares it is refused as
`events_not_allowed` ([plugins](plugins.md)). The invocation carries the binding that says which account, and nothing
about a campaign.

A read starts from a cursor the plugin returned, or by date — `since`, with an optional `until` — and answers a page of
at most 100 events, the cursor to read on from, and whether more follow now. A child is given 60 seconds for a page.

| Kind | Identifiers | The provider's flags |
|---|---|---|
| `thread_moved` | `thread`, `contact`, `campaign` | `moved_by`, `channel`, `unread`, `has_meeting_intent` |
| `reply_detected` | `contact`, `campaign`, `thread` | `channel` |
| `invitation_accepted` | `contact`, `campaign`, `account` | — |
| `bounce` | `contact`, `campaign`, `message` | `bounce` |

`moved_by` is the provider's own word for who moved a thread: `other_side`, `account` — a message sent as the account,
Jason's own replies among them — `provider`, a change with no message such as a category or the read flag, or `null`
where the provider cannot say. Every kind is a closed object, so a subject, a preview or a name is refused with the
whole page, and so is an identifier with a space in it.

A failure carries one of `events_unreachable` (transient), `events_unauthorized`, `events_unsupported` and
`events_cursor_unknown` (permanent). A read changes nothing at a provider, so reading again is always safe.

**The cursor is the plugin's.** A cursor is 1 to 2048 printable ASCII characters with no space. The runtime stores it as
it came, beside the account it was read from, hands it back unchanged, compares it only to know that nobody moved it
meanwhile, and never parses, logs, answers or journals it.

**A read pins nothing.** An answer that carries identifiers beside its result is refused: a pin is written by an
operation's answer and by nothing else ([routing](routing.md) §11), and the store only finds them.

## 2. The store

Three tables, in the runtime's own database.

- **`event_sources`** — one row for each account a read hangs on: a plugin and the identity of a binding a route names
  for it, the `sha256:` of the binding's canonical form ([routing](routing.md) §10), or `none` for a route that names
  no binding. The row holds the cursor the plugin last returned and when it was stored. The binding's value is the
  route's, read from the active snapshot at each read; a source whose binding leaves every route keeps its row and its
  cursor. Two bindings that name one account are two sources.
- **`events`** — every event a read stored, as the contract's output schema accepted it: its kind, when it happened,
  when it was stored, which read stored it, the person it names where exactly one contact is pinned at the plugin with
  the identifier the provider gave, and its key.
- **`event_attributions`** — which campaigns an event concerns, one row each, with the rule that found it. An event may
  concern several.

**Kept whole.** The store is append-only three times over, as the journal is: no operation of the Runtime API changes
or removes an event; the runtime refuses to before the database is reached; and the triggers `events_no_update`,
`events_no_delete`, `event_attributions_no_update` and `event_attributions_no_delete` refuse a raw statement that tries.
An event that reaches no campaign by either rule (section 3) is kept, concerning nobody. Nothing re-attributes an event
later: which campaigns it concerns is decided once, when it is stored, by the pins and the memberships as they stood
then — as a campaign linked late is read by the inbound read only from then ([inbound sync](inbound-sync.md) §5), and a
person who joins a campaign after an event was stored never brings that event to it.

**Each event once.** An event's key is the `sha256:` of the plugin, the kind, the identifiers the kind names and the
moment it happened, and for a moved thread who moved it: the provider setting a category as a reply arrives, at the same
moment, is a move of its own, and the other side's reply is still said. It is the plugin's key, not the account's, so
two sources reading one account store each event once. A page read again — after a crash, or a range read again on a
person's request — stores nothing it already holds: the keys are read first, and the store's unique index refuses the
rest. The provider's own event id is kept with the event and is not part of its key.

**A page lands whole, or not at all.** The events a page adds, their attributions, the lines they say and the cursor the
page returned are written in one transaction, and the cursor moves only from the cursor the source held when the read
began. A page that does not land leaves the cursor where it was, and the next read asks for the same events again.

## 3. Which campaigns an event concerns

Two rules, asked in this order; the first that finds a campaign decides alone.

1. **`campaign_pin`.** An event reaches a campaign by the provider's campaign identifier it names, when exactly one
   campaign that is not archived holds that identifier pinned at the event's own plugin.
2. **`contact_pin`.** Otherwise — the event names no provider campaign, or no campaign that is not archived holds it, or
   more than one does — it reaches every campaign that is not archived and holds, as a local member, the one contact
   pinned with the event's contact identifier at that plugin. A local member is any membership but `excluded`, so a
   paused or finished one counts; an archived contact is a member of nothing here, and an event still names them.

An event that finds a campaign by neither concerns nobody: nothing else counts, not a route to the plugin, not a tag,
not a name. A value kept only as a divergence beside a pin is not a pin, and an identifier pinned to two contacts names
nobody, so it reaches no campaign by the person either.

**Why in that order.** A sequence's event belongs to the campaign that runs the sequence. The inbound read lists a
campaign's own threads, and the events path must agree with it about a campaign; so the person's rule is the rule for
the event no provider campaign places — a direct email's reply, a LinkedIn message outside a sequence — never a second
audience beside a campaign already found. The inbound read's step aside asks the first rule alone
([inbound sync](inbound-sync.md) §2).

## 4. What a stored event says

For each campaign it concerns, a stored event says at most one line, written in the same transaction:

| The event | The line |
|---|---|
| `thread_moved` by `other_side`, or by `null` | `inbound_reply_observed` |
| `thread_moved` by `account` or `provider` | none |
| `reply_detected`, `invitation_accepted`, `bounce` | `provider_event_observed` |

Both lines are written by `system:dispatcher`, name the campaign and no work item or attempt, and are trigger kinds a
manager is woken by under the default `Manager:Triggers`.

- **`inbound_reply_observed`** is the line the inbound read writes, with its fields — `plugin`, `thread`, `channel`,
  `last_activity_at`, `unread`, `has_meeting_intent`, `contact_id` — and two more, `event_id`, the stored event, and
  `attributed_by`, the rule that found the campaign (section 3). The inbound read's own line carries neither, since the
  thread it names is the campaign's own by the read that listed it. It is keyed as the inbound read keys it, by the
  plugin, the thread and the moment, so a move both reads found is said once.
  A move the provider cannot say who made reads as a reply, so a follow-up sent into a thread that already has a reply
  is said again, as the inbound read says it.
- **`provider_event_observed`** carries `kind`, `plugin`, `event_id`, `occurred_at`, `channel`, `contact_id`,
  `attributed_by`, and the kind's own identifiers and flags: `thread` for a detected reply, `account` for an accepted
  invitation, `message` and `bounce` for a bounce. It is keyed by the event's own key.

**Said once.** A campaign says each line once under its key, and the database refuses a second; one key may be said in
two campaigns, and the rule that found a campaign is no part of the key. **History is not said.** An event that
happened before the campaign first went live is stored and found to concern it, and says no line: `events_from` is set
when a campaign first goes live, a pause and a resume leave it, and an installation upgrading into the events store
gives every active and paused campaign the moment of the upgrade. An event found by the person is history, too, when it
happened before the person was added to the campaign: it is said only when it happened on or after both. A person
taken out and added back keeps the moment they were first added, so an event of the interval they were out, stored
after they were added back, is said.

**Never a person's words.** No line, no log line and no answer of the runtime carries a subject, a preview, a name or a
cursor: the contract admits none into an event, and the runtime writes none of its own.

## 5. Reading an account

**Which accounts.** Every account a route binds at a plugin that is loaded, usable here and declares the events
contract: a distinct plugin and binding identity named by the global routes or by any campaign's, read whether or not a
campaign is linked there yet, so an account's events are held before a campaign needs them. Each is given its source
the first time it is bound. An account no route binds any more keeps its source and its cursor and is not read; it is
read from that cursor again once a route binds it.

**Where it runs.** The dispatcher's scan has a step for it, **events**, after the lease enforcer and before the inbound
read ([work execution](work-execution.md)). The step reads nothing itself: it hands the reader one read, the most
overdue, and only when the reader is idle, so at most one child of the plugin host reads events at a time, beside the
handler pool's and the notifier's. A read is never a work item and never takes a handler slot. A draining or stopped
dispatcher hands out no new read; a read under way goes on until the stop ends its child, and the page in flight is not
kept.

**A read.** At most 20 pages, each one child given the contract's 60 seconds, lowered by the plugin's own ceiling; the
next page is asked for from the cursor the last one returned, while the provider says more follow. Each page is held to
the contract and stored in a transaction of its own (section 2), so what earlier pages stored stays when a later one
fails. A read that stops at 20 pages with more to read is due again from when it ended: at once, but behind an account
that has been due for longer, so one account's backlog takes turns with the others.

**When.** A source this runtime has not read since it started is read at once — a start catches every account up from
its cursor — and so is one never read. After that, `Dispatcher:EventsCadenceSeconds` after the last read started,
doubled for each read in a row that failed, at most a day. A read that is due starts when the reader is free: one under
way — another account's, or a span's asked for again — ends first.

**Sooner, on a signal.** `events.signal`, and `jason events signal` at the command line, is a nudge from whatever program
receives a provider's webhooks: it names the account's source and nothing else — no actor, no reason, nothing a webhook
carried. The account's next read is then due at the next scan, never sooner than 60 seconds after its last read
started, whatever it was waiting for; that read answers every signal before it, so a burst of webhooks is one read. A
signal writes no line of its own: its moment is kept on the source, answered as `signaled_at` in the record (section 7)
and in the signal's own receipt with when the next read is due, and the account's next read says `signaled` in its
line, where it writes one: a signal had come since the last read started.

**From where.**

| The source | The read starts | |
|---|---|---|
| no cursor yet | 30 days back | a first start owes nothing before that |
| a cursor stored within 30 days | from the cursor | the cadence, and the catch-up at a start |
| a cursor stored longer ago | 30 days back | the gap: one `events_gap_observed` line |
| a cursor the plugin forgot | a day before it was stored, or 30 days back when that is later | the gap rule, when older |

**The gap.** What lies between a cursor older than 30 days and the 30 days a read reaches back is not read. It is said
once, with the first page of the read that skips it, as an `events_gap_observed` line naming no campaign — the source,
the plugin and the span — keyed by the source and where the span starts, so a read tried again says it once. The next
notification that reaches anybody counts it ([notifications](notifications.md)).

A source whose reads keep failing is not a gap: a gap is a span a read skipped, and a failing read skips nothing. It shows
in `jason events sources`, as how many reads in a row failed and how the last one did, and in the runtime's log, read with
`jason logs`, as a warning naming the source, the code and the class; while the reads fail it is never said as an
`events_gap_observed` line or counted by a notification. A failure that outlasts the 30 days leaves a gap, which the first
read that succeeds says, and which is counted like any other.

A gap is a span older than 30 days that a read reaches back to, so a source that is not being read leaves no gap,
however long it stays that way. A source never read starts 30 days back and owes nothing before that. A source whose
plugin is unrouted or unavailable is not read (see Which accounts, above): it keeps its row and its cursor, no gap is
said while it stays so, and the first read that succeeds after a route binds it again says the gap, when its cursor is
by then older than 30 days. A campaign never linked leaves none either: a gap belongs to an account and names no
campaign, and the account is read whether or not a campaign is linked there. The inbound read skips a campaign that is
not linked and has no gap to say; its start moves with every skip, so a campaign linked late is read from then
([inbound sync](inbound-sync.md) §5).

**When a read fails.**

| How it ended | The next read |
|---|---|
| `transient`, or `ambiguous` — a timeout, a crash, an answer outside the contract | at the doubled cadence, with the same input |
| `permanent` or `validation` | a day after the failed read started |
| `events_cursor_unknown` | at once, by date: the cursor is cleared and when it was stored is kept |

An answer outside the contract is `result_invalid`: a page the output schema refuses, or one it accepts that the
runtime cannot read, identifiers beside a result, or a failure whose code the contract does not declare with the class
it came with. Such a page stores nothing, and the cursor stays where it was.

**What is recorded.** The source keeps when its last read started and ended, when the last read that succeeded started,
and how many reads in a row failed, with the last one's code and class. Each read that stored something, failed, or read
by date writes one `events_read` line naming no campaign: the source, the plugin, the binding identity, how it read, the
pages, how many events it stored and found already held, how many lines it said, how it ended, the package and the
snapshots it ran under, and `signaled`, whether a signal had come since the last read started — never a cursor, a
binding or an event. A read from a cursor that found nothing writes none, so a signal answered by such a read leaves
only its moment on the source. The log carries the source's id, codes, classes and counts.

**Beside the inbound read.** While a plugin's account is read here, the inbound read leaves the campaigns routed to it —
but only a campaign an event would reach by section 3's rule, only while that account's last read that succeeded started
within the shorter of two cadences and `Inbound:LookbackSeconds`, and never before that first success
([inbound sync](inbound-sync.md) §2). A move both find is said once (section 4).

**The setting.**

| Setting | Default | Range | What it does |
|---|---|---|---|
| `Dispatcher:EventsCadenceSeconds` | `600` | 60..86400 | how long after one read of an account starts the next is due |

It applies at the next scan, without a restart. **What it costs:** one read per bound account per cadence, 144 a day at
the default, most of them one child that stores nothing and journals nothing. A plugin that reads a stretch again, as
an overlap that finds a late arrival does, hands back events the store already holds; those are counted as held, and a
read that finds only those writes one line to the runtime's log saying how many, and nothing to the journal.

## 6. Reading a span again

What a gap leaves unread, or any other span of an account, is read again only when a person asks:
`events.backfill` with the account's source, `from` and `to` — every event at or after `from` and before `to`, to the
millisecond — and `jason events backfill` at the command line. Each bound is refused by its own name: `from` must come
before `to` (`not_after_from`), `to` no later than now (`in_the_future`), and `from` at most 366 days back (`too_old`).
The same span asked for again while it waits is the one already waiting. At most 8 spans of one account wait at once,
and a ninth is refused as `event_backfill_limit` until one ends; an account no source has is `event_source_not_found`,
and one no route binds now is refused as `event_source_unbound`. The request is said once, as an
`events_backfill_requested` line naming no campaign, with who asked and their reason.

**When it is read.** In a scan where no account's own read is due, the reader is handed the waiting span asked for
longest ago of an account a route binds — keeping every account current comes first. A span waits while any account's
own read is due, a read that stopped with more among them, so an account with a backlog holds every span until it has
caught up. A span is read from its start to its end, then on from its own cursor, whose chain the plugin keeps before
`to`; up to 20 pages a read, and a span with more waits on from where its last page landed.

**What it never touches.** A span's pages move the span's own cursor and never the account's: not the cursor, not when it
was stored, and none of what says when the account is read next or whether it is healthy. It says no gap. What it finds
lands on the store's keys, so an event the account's own reading stored is stored once, and a line a campaign was told
already is not told again; an event that happened before a campaign went live is kept and says nothing (section 4).

**How it ends.** Read to its end, a span is `done`. An answer repeating cannot change ends it `failed`, with its code and
class, so that a span that could never succeed does not hold its place: a `permanent` or a `validation` failure, or an
answer outside the contract. A cursor of its own the plugin forgot sends it back to its start, at once — while that
finds something new: forgotten again with nothing new stored since the last start, it is a failure that may pass, so a
plugin that forgets every cursor costs a doubling wait rather than a read at every scan. Any other failure counts on the
span and waits as an account's does, the cadence doubled for each failure in a row, at most a day. Each read of a span
writes its `events_read` line, with `mode` `backfill`, the span's id and where it stands after the read.

**What it costs:** one child a page, a hundred events a page, read in scans when nothing else is due.

## 7. The record of the accounts

`events.sources`, and `jason events sources` at the command line, lists every account that has a source, a page at a
time in the order each was first bound: its plugin, its binding identity and the binding as the active route snapshot
holds it, whether a route binds it now, whether it holds a cursor and when that was stored — never the cursor — when its
last read started and ended and when the last one that succeeded started, how many reads in a row failed and how the last
one did, whether it is healthy by the inbound read's own question (section 5), when its own reading is next due and when
it was last signalled, the latest span its own reading skipped — it stays there once a span asked for again has read
it — and its ten newest spans asked for again with where each stands.

## 8. Not here yet

- **Reading the store.** No operation of the Runtime API lists stored events; a manager reads the lines they say.
- **Calling a span off.** A span asked for again ends by being read or by failing; nothing withdraws it.
